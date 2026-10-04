using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Toybox.EditorTools
{
    /// <summary>
    /// The setup steps the engine itself needs (orders 0-99): player settings, layers, physics, a render
    /// pipeline to start from, the template materials and the bootstrap scene. Areas add their own steps
    /// in Editor/Setup/&lt;Area&gt;Setup.cs with orders from 100 and never edit this file.
    ///
    /// The public steps can also be run on their own:
    ///   tools\unity.ps1 exec -Method Toybox.EditorTools.CoreSetup.ConfigurePlayer
    /// </summary>
    public static class CoreSetup
    {
        public const string UrpAssetPath = SetupUtil.SettingsDir + "/ToyboxURP.asset";
        public const string RendererPath = SetupUtil.SettingsDir + "/ToyboxRenderer.asset";
        public const string ToyLitPath = SetupUtil.MaterialsDir + "/ToyLit.mat";
        public const string PlainLitPath = SetupUtil.MaterialsDir + "/PlainLit.mat";
        const string UrpLit = "Universal Render Pipeline/Lit";

        // Physics layers. Kept in sync with Toybox.Engine.Layers at runtime.
        static readonly (int index, string name)[] LayerNames =
        {
            (8, "Prop"), (9, "Player"), (10, "Held"), (11, "Trigger"),
        };

        [SetupStep(0)]
        static void Folders()
        {
            SetupUtil.EnsureFolder(SetupUtil.SettingsDir);
            SetupUtil.EnsureFolder(SetupUtil.MaterialsDir);
            SetupUtil.EnsureFolder(SetupUtil.Root + "/Scenes");
        }

        [SetupStep(10)]
        public static void ConfigurePlayer()
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
            // Levels and their solve scripts are code. The runner promises that one that fails is reported
            // and survived (a level that cannot be built, a bot script that goes wrong, a faulty event
            // listener), and it keeps that promise with catch blocks. "Explicitly thrown only" would turn
            // the most common bug of all, a null reference, into a hard stop of the page instead.
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.FullWithoutStacktrace;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);

            // 1 = Input System package only.
            SetupUtil.SetField(SetupUtil.ProjectSettingsAsset("ProjectSettings/ProjectSettings.asset"), "activeInputHandler", 1);
            AssetDatabase.SaveAssets();
        }

        [SetupStep(20)]
        static void ConfigureLayers()
        {
            Object tagManager = SetupUtil.ProjectSettingsAsset("ProjectSettings/TagManager.asset");
            var serialized = new SerializedObject(tagManager);
            SerializedProperty layers = SetupUtil.Field(serialized, "layers");
            foreach (var (index, name) in LayerNames)
            {
                SerializedProperty element = layers.GetArrayElementAtIndex(index);
                if (element.stringValue != name) element.stringValue = name;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Physics settings that have no runtime API and are read when a physics scene is created.
        /// (Gravity, solver iterations and the layer matrix are set at runtime by Toybox.Engine.Game.)
        /// </summary>
        [SetupStep(30)]
        public static void ConfigurePhysics()
        {
            // Temporal Gauss-Seidel. The mechanic produces mass ratios of thousands to one (mass goes with
            // scale cubed); with the default solver a heavy body sinks straight through a light one.
            SetupUtil.SetField(SetupUtil.ProjectSettingsAsset("ProjectSettings/DynamicsManager.asset"), "m_SolverType", 1);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// A pipeline to start from: one URP asset with one renderer, assigned to every quality level. The
        /// pipeline area replaces the assignment with its tier assets in a later step.
        /// </summary>
        [SetupStep(50)]
        static void ConfigureRenderPipeline()
        {
            UniversalRendererData rendererData = SetupUtil.LoadOrCreate(RendererPath, () =>
            {
                var data = ScriptableObject.CreateInstance<UniversalRendererData>();
                data.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                    UniversalRenderPipelineAsset.packagePath + "/Runtime/Data/PostProcessData.asset");
                return data;
            }, out bool createdRenderer);
            if (createdRenderer)
            {
                ResourceReloader.ReloadAllNullIn(rendererData, UniversalRenderPipelineAsset.packagePath);
                EditorUtility.SetDirty(rendererData);
            }

            UniversalRenderPipelineAsset urp = SetupUtil.LoadOrCreate(UrpAssetPath, () => UniversalRenderPipelineAsset.Create(rendererData), out bool createdAsset);
            if (createdAsset)
            {
                urp.supportsHDR = true;
                urp.msaaSampleCount = 4;
                urp.supportsCameraDepthTexture = true;
                urp.supportsCameraOpaqueTexture = false;
                EditorUtility.SetDirty(urp);
            }

            // Only where nothing is assigned yet: a later area's assets must survive a re-run of this step.
            if (GraphicsSettings.defaultRenderPipeline == null) GraphicsSettings.defaultRenderPipeline = urp;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                if (QualitySettings.renderPipeline == null) QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        /// <summary>
        /// Shadow settings of the starting pipeline asset. A first-person view needs cascades (one shadow
        /// map spread over 80 units is far too coarse at the player's feet), and a light can only cast soft
        /// shadows if the asset allows them.
        /// </summary>
        [SetupStep(51)]
        public static void ConfigureShadows()
        {
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (urp == null) return;

            urp.shadowDistance = 80f;
            urp.shadowCascadeCount = 4;
            urp.mainLightShadowmapResolution = 2048;
            // Soft shadows have no public setter.
            SetupUtil.SetField(urp, "m_SoftShadowsSupported", true);
            EditorUtility.SetDirty(urp);
            AssetDatabase.SaveAssets();
        }

        // A shader only ships in a player build if an asset references it, so anything the game looks up
        // at runtime needs a material under Resources. Shader.Find alone returns null in the build.
        [SetupStep(60)]
        static void CreateMaterials()
        {
            // The template of every toy material. The toy-shading area points it at Toybox/ToyLit; until
            // then (and never against its will) it is URP Lit.
            SetupUtil.LoadOrCreateMaterial(ToyLitPath, UrpLit, repoint: false);
            // The plain look (?plain=1) stays URP Lit for good.
            SetupUtil.LoadOrCreateMaterial(PlainLitPath, UrpLit);
        }

        // The one scene: a Bootstrap object and nothing else. Written only if it is missing, so that running
        // the setup again does not touch the file.
        [SetupStep(90)]
        static void CreateMainScene()
        {
            if (!System.IO.File.Exists(ProjectSetup.ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var go = new GameObject("Bootstrap");
                go.AddComponent<Bootstrap>();
                EditorSceneManager.SaveScene(scene, ProjectSetup.ScenePath);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (scenes.Length != 1 || scenes[0].path != ProjectSetup.ScenePath || !scenes[0].enabled)
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ProjectSetup.ScenePath, true) };
        }
    }
}
