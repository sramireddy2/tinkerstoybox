using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Toybox.EditorTools
{
    /// <summary>
    /// Idempotent project configuration. Everything that would normally be clicked together in the
    /// editor (render pipeline assets, player settings, layers, the bootstrap scene) is created here so
    /// the project can be rebuilt from source control alone:
    ///   Unity -batchmode -quit -executeMethod Toybox.EditorTools.ProjectSetup.Run
    /// </summary>
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Toybox/Scenes/Main.unity";
        const string SettingsDir = "Assets/Toybox/Settings";
        const string UrpAssetPath = SettingsDir + "/ToyboxURP.asset";
        const string RendererPath = SettingsDir + "/ToyboxRenderer.asset";
        const string ToyLitPath = "Assets/Toybox/Resources/Materials/ToyLit.mat";

        // Physics layers. Kept in sync with Toybox.Layers at runtime.
        static readonly (int index, string name)[] LayerNames =
        {
            (8, "Prop"), (9, "Player"), (10, "Held"), (11, "Trigger"),
        };

        [MenuItem("Toybox/Run Project Setup")]
        public static void Run()
        {
            Directory.CreateDirectory(SettingsDir);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            AssetDatabase.Refresh();

            ConfigurePlayer();
            ConfigureLayers();
            ConfigureRenderPipeline();
            CreateMaterials();
            CreateMainScene();

            AssetDatabase.SaveAssets();
            Debug.Log("[Toybox] Project setup complete.");
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Tinker's Toybox";
            PlayerSettings.productName = "Tinker's Toybox";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SplashScreen.show = false;

            // GitHub Pages cannot set Content-Encoding headers, so ship gzip with the JS fallback decoder.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.nameFilesAsHashes = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.template = "PROJECT:Toybox";
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);

            // 1 = Input System package only.
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (settings.Length > 0)
            {
                var so = new SerializedObject(settings[0]);
                var handler = so.FindProperty("activeInputHandler");
                if (handler != null && handler.intValue != 1)
                {
                    handler.intValue = 1;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }

        static void ConfigureLayers()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            var layers = so.FindProperty("layers");
            if (layers == null) return;
            foreach (var (index, name) in LayerNames)
            {
                var element = layers.GetArrayElementAtIndex(index);
                if (element.stringValue != name) element.stringValue = name;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureRenderPipeline()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                rendererData.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                    UniversalRenderPipelineAsset.packagePath + "/Runtime/Data/PostProcessData.asset");
                AssetDatabase.CreateAsset(rendererData, RendererPath);
                ResourceReloader.ReloadAllNullIn(rendererData, UniversalRenderPipelineAsset.packagePath);
                EditorUtility.SetDirty(rendererData);
            }

            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (urp == null)
            {
                urp = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(urp, UrpAssetPath);
            }

            urp.supportsHDR = true;
            urp.msaaSampleCount = 4;
            urp.shadowDistance = 80f;
            urp.supportsCameraDepthTexture = true;
            urp.supportsCameraOpaqueTexture = false;
            EditorUtility.SetDirty(urp);

            GraphicsSettings.defaultRenderPipeline = urp;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        // A shader only ships in a player build if an asset references it, so anything the game looks up
        // at runtime needs a material under Resources. Shader.Find alone returns null in the build.
        static void CreateMaterials()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ToyLitPath));
            AssetDatabase.Refresh();
            if (AssetDatabase.LoadAssetAtPath<Material>(ToyLitPath) == null)
                AssetDatabase.CreateAsset(new Material(Shader.Find("Universal Render Pipeline/Lit")), ToyLitPath);
        }

        static void CreateMainScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Bootstrap");
            go.AddComponent<Bootstrap>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
