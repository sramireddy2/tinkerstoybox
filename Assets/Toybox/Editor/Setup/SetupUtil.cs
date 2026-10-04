using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Toybox.EditorTools
{
    /// <summary>
    /// Helpers shared by the setup steps (Editor/Setup/*Setup.cs). They are written so that a step built
    /// from them is idempotent: folders and assets are created only if missing, and values are only
    /// written when they differ.
    /// </summary>
    public static class SetupUtil
    {
        public const string Root = "Assets/Toybox";
        public const string SettingsDir = Root + "/Settings";
        public const string ResourcesDir = Root + "/Resources";
        public const string MaterialsDir = ResourcesDir + "/Materials";

        /// <summary>Makes sure an asset folder exists ("Assets/Toybox/Resources/Volumes"), creating its parents as needed.</summary>
        public static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folder)) return;
            int slash = folder.LastIndexOf('/');
            if (slash < 0) throw new ArgumentException("Not a folder under Assets: '" + folder + "'.", nameof(folder));
            string parent = folder.Substring(0, slash);
            EnsureFolder(parent);
            // The folder may exist on disk without having been imported yet.
            if (Directory.Exists(folder))
            {
                AssetDatabase.Refresh();
                if (AssetDatabase.IsValidFolder(folder)) return;
            }
            AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1));
        }

        /// <summary>
        /// The asset at the path, created by <paramref name="create"/> (and saved there) if it does not exist.
        /// <paramref name="created"/> tells which it was, for setting defaults only once.
        /// </summary>
        public static T LoadOrCreate<T>(string path, Func<T> create, out bool created) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            created = false;
            if (asset != null) return asset;
            EnsureFolder(Path.GetDirectoryName(path));
            asset = create();
            if (asset == null) throw new InvalidOperationException("Could not create the asset for '" + path + "'.");
            AssetDatabase.CreateAsset(asset, path);
            created = true;
            return asset;
        }

        public static T LoadOrCreate<T>(string path, Func<T> create) where T : Object => LoadOrCreate(path, create, out _);

        /// <summary>
        /// The material at the path with the named shader: created if missing, and - unless
        /// <paramref name="repoint"/> is false - switched to the shader if it has another one. Throws with
        /// the shader's name if the shader does not exist (not imported yet, or failed to compile).
        /// </summary>
        public static Material LoadOrCreateMaterial(string path, string shaderName, bool repoint = true)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
                throw new InvalidOperationException("The shader '" + shaderName + "' was not found: it has not been imported or it does not compile.");
            Material material = LoadOrCreate(path, () => new Material(shader), out bool created);
            if (!created && repoint && material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }
            return material;
        }

        /// <summary>
        /// The serialized field of an object by its path ("m_SoftShadowsSupported"), or an exception that
        /// names the object, the field and the fields that do exist with a similar name - for all the
        /// settings that have no public setter.
        /// </summary>
        public static SerializedProperty Field(SerializedObject serialized, string field)
        {
            SerializedProperty property = serialized.FindProperty(field);
            if (property != null) return property;

            Object target = serialized.targetObject;
            string needle = field.TrimStart('m', '_').ToLowerInvariant();
            if (needle.Length > 6) needle = needle.Substring(0, 6);
            var similar = new List<string>();
            var all = new List<string>();
            SerializedProperty iterator = serialized.GetIterator();
            bool children = true;
            while (iterator.Next(children))
            {
                children = false;
                all.Add(iterator.propertyPath);
                if (needle.Length > 0 && iterator.propertyPath.ToLowerInvariant().Contains(needle)) similar.Add(iterator.propertyPath);
            }
            List<string> shown = similar.Count > 0 ? similar : all;
            if (shown.Count > 40) shown = shown.GetRange(0, 40);
            throw new InvalidOperationException(
                (target != null ? target.GetType().Name + " '" + target.name + "'" : "The object") + " has no serialized field '" + field + "'. " +
                (similar.Count > 0 ? "Fields with a similar name: " : "Its top-level fields: ") + string.Join(", ", shown) + ".");
        }

        /// <summary>Sets a serialized field by name. Returns true if the value changed. Throws if there is no such field.</summary>
        public static bool SetField(Object target, string field, bool value) => Change(target, field, p =>
        {
            Require(p, SerializedPropertyType.Boolean);
            if (p.boolValue == value) return false;
            p.boolValue = value;
            return true;
        });

        public static bool SetField(Object target, string field, int value) => Change(target, field, p =>
        {
            if (p.propertyType == SerializedPropertyType.Enum)
            {
                if (p.intValue == value) return false;
                p.intValue = value;
                return true;
            }
            Require(p, SerializedPropertyType.Integer, SerializedPropertyType.LayerMask, SerializedPropertyType.ArraySize);
            if (p.intValue == value) return false;
            p.intValue = value;
            return true;
        });

        public static bool SetField(Object target, string field, float value) => Change(target, field, p =>
        {
            Require(p, SerializedPropertyType.Float);
            if (p.floatValue == value) return false;
            p.floatValue = value;
            return true;
        });

        public static bool SetField(Object target, string field, string value) => Change(target, field, p =>
        {
            Require(p, SerializedPropertyType.String);
            if (p.stringValue == value) return false;
            p.stringValue = value;
            return true;
        });

        public static bool SetField(Object target, string field, Object value) => Change(target, field, p =>
        {
            Require(p, SerializedPropertyType.ObjectReference);
            if (p.objectReferenceValue == value) return false;
            p.objectReferenceValue = value;
            return true;
        });

        public static bool SetField(Object target, string field, Color value) => Change(target, field, p =>
        {
            Require(p, SerializedPropertyType.Color);
            if (p.colorValue == value) return false;
            p.colorValue = value;
            return true;
        });

        public static bool SetField(Object target, string field, Vector3 value) => Change(target, field, p =>
        {
            Require(p, SerializedPropertyType.Vector3);
            if (p.vector3Value == value) return false;
            p.vector3Value = value;
            return true;
        });

        public static bool SetField(Object target, string field, Vector2 value) => Change(target, field, p =>
        {
            Require(p, SerializedPropertyType.Vector2);
            if (p.vector2Value == value) return false;
            p.vector2Value = value;
            return true;
        });

        /// <summary>
        /// The renderer feature of this type and name on a renderer asset, added at the end of its list if it
        /// is not there yet. Two areas add features to the one shared renderer (the lens blur, the sticker
        /// pass); both go through here, and both finish with <see cref="OrderRendererFeatures"/>.
        /// </summary>
        public static T EnsureRendererFeature<T>(ScriptableRendererData renderer, string name) where T : ScriptableRendererFeature =>
            (T)EnsureRendererFeature(renderer, typeof(T), name);

        public static ScriptableRendererFeature EnsureRendererFeature(ScriptableRendererData renderer, Type type, string name)
        {
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            if (type == null || !typeof(ScriptableRendererFeature).IsAssignableFrom(type) || type.IsAbstract)
                throw new ArgumentException(type + " is not a concrete ScriptableRendererFeature.", nameof(type));
            foreach (ScriptableRendererFeature existing in renderer.rendererFeatures)
                if (existing != null && existing.GetType() == type && existing.name == name) return existing;

            var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
            feature.name = name;
            // As the renderer's own inspector does it: a sub-asset, the list, and the list of local ids beside it.
            long localId = 0;
            if (EditorUtility.IsPersistent(renderer))
            {
                AssetDatabase.AddObjectToAsset(feature, renderer);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out localId);
            }
            var serialized = new SerializedObject(renderer);
            SerializedProperty features = Field(serialized, "m_RendererFeatures");
            SerializedProperty map = Field(serialized, "m_RendererFeatureMap");
            int index = features.arraySize;
            features.arraySize = index + 1;
            features.GetArrayElementAtIndex(index).objectReferenceValue = feature;
            map.arraySize = index + 1;
            map.GetArrayElementAtIndex(index).longValue = localId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            renderer.SetDirty();
            EditorUtility.SetDirty(renderer);
            return feature;
        }

        /// <summary>
        /// Puts the named features of a renderer into this order relative to each other (the passes of an
        /// injection point run in list order). Features that are not named keep their places; names that are
        /// not on the renderer are skipped. Returns true if anything moved.
        /// </summary>
        public static bool OrderRendererFeatures(ScriptableRendererData renderer, params string[] names)
        {
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            var serialized = new SerializedObject(renderer);
            SerializedProperty features = Field(serialized, "m_RendererFeatures");
            SerializedProperty map = Field(serialized, "m_RendererFeatureMap");
            bool hasMap = map.arraySize == features.arraySize;

            // The slots the named features occupy now, and the features in the order they should have.
            var slots = new List<int>();
            var wanted = new List<(Object feature, long id)>();
            for (int i = 0; i < features.arraySize; i++)
            {
                Object feature = features.GetArrayElementAtIndex(i).objectReferenceValue;
                if (feature != null && Array.IndexOf(names, feature.name) >= 0) slots.Add(i);
            }
            foreach (string name in names)
                foreach (int slot in slots)
                {
                    Object feature = features.GetArrayElementAtIndex(slot).objectReferenceValue;
                    if (feature.name != name) continue;
                    wanted.Add((feature, hasMap ? map.GetArrayElementAtIndex(slot).longValue : 0));
                }

            bool moved = false;
            for (int k = 0; k < slots.Count && k < wanted.Count; k++)
            {
                SerializedProperty element = features.GetArrayElementAtIndex(slots[k]);
                if (element.objectReferenceValue == wanted[k].feature) continue;
                element.objectReferenceValue = wanted[k].feature;
                if (hasMap) map.GetArrayElementAtIndex(slots[k]).longValue = wanted[k].id;
                moved = true;
            }
            if (!moved) return false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            renderer.SetDirty();
            EditorUtility.SetDirty(renderer);
            return true;
        }

        /// <summary>The first asset of a ProjectSettings file ("ProjectSettings/TagManager.asset"); throws if it cannot be loaded.</summary>
        public static Object ProjectSettingsAsset(string path)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            if (assets.Length == 0 || assets[0] == null) throw new InvalidOperationException("Could not load '" + path + "'.");
            return assets[0];
        }

        static bool Change(Object target, string field, Func<SerializedProperty, bool> write)
        {
            if (target == null) throw new ArgumentNullException(nameof(target), "SetField('" + field + "') was given no object.");
            var serialized = new SerializedObject(target);
            SerializedProperty property = Field(serialized, field);
            if (!write(property)) return false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
            return true;
        }

        static void Require(SerializedProperty property, params SerializedPropertyType[] types)
        {
            foreach (SerializedPropertyType type in types)
                if (property.propertyType == type) return;
            Object target = property.serializedObject.targetObject;
            throw new InvalidOperationException(
                "The field '" + property.propertyPath + "' of " + (target != null ? target.GetType().Name : "the object") +
                " is a " + property.propertyType + ", not a " + types[0] + ".");
        }
    }
}
