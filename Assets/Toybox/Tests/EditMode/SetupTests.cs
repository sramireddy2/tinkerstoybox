using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Toybox.EditorTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Toybox.Tests
{
    // Setup steps of the test assembly. ProjectSetup.Run only looks in the editor assembly, so these never
    // run as part of the real setup.
    static class ProbeSetup
    {
        public static readonly List<string> Ran = new List<string>();
        public static bool Break;

        [SetupStep(300)]
        static void Late() => Ran.Add("Late");

        [SetupStep(100)]
        public static void Early() => Ran.Add("Early");

        [SetupStep(200)]
        static void Middle()
        {
            Ran.Add("Middle");
            if (Break) throw new InvalidOperationException("middle step broke");
        }

        // Same order as Middle: ties go by name.
        [SetupStep(200)]
        static void Another() => Ran.Add("Another");

        public static void NotAStep() => Ran.Add("NotAStep");
    }

    /// <summary>
    /// The setup-step seam: ProjectSetup finds [SetupStep] methods by reflection and runs them in order;
    /// SetupUtil gives the steps create-or-load helpers and a field setter that fails loudly.
    /// </summary>
    public class SetupTests
    {
        [SetUp]
        public void Reset()
        {
            ProbeSetup.Ran.Clear();
            ProbeSetup.Break = false;
        }

        [Test]
        public void Discovery_FindsMarkedStaticMethods_InOrder()
        {
            List<SetupStep> steps = ProjectSetup.Discover(typeof(SetupTests).Assembly);
            var names = new List<string>();
            foreach (SetupStep step in steps) names.Add(step.Order + " " + step.Name);
            CollectionAssert.AreEqual(new[] { "100 ProbeSetup.Early", "200 ProbeSetup.Another", "200 ProbeSetup.Middle", "300 ProbeSetup.Late" }, names,
                "private and public alike, by order, ties by name; unmarked methods are not steps");
        }

        [Test]
        public void TheProjectsOwnSteps_AreFoundAndTheCoreComesFirst()
        {
            List<SetupStep> steps = ProjectSetup.Discover();
            var names = new List<string>();
            foreach (SetupStep step in steps) names.Add(step.Name);
            string[] core =
            {
                "CoreSetup.Folders", "CoreSetup.ConfigurePlayer", "CoreSetup.ConfigureLayers", "CoreSetup.ConfigurePhysics",
                "CoreSetup.ConfigureRenderPipeline", "CoreSetup.ConfigureShadows", "CoreSetup.CreateMaterials", "CoreSetup.CreateMainScene",
            };
            // Whatever the areas add comes after the core, which keeps its order.
            CollectionAssert.AreEqual(core, names.GetRange(0, core.Length));
            for (int i = 0; i < steps.Count; i++)
            {
                if (i > 0) Assert.LessOrEqual(steps[i - 1].Order, steps[i].Order, "sorted");
                bool isCore = steps[i].Method.DeclaringType == typeof(CoreSetup);
                Assert.AreEqual(isCore, steps[i].Order < 100, steps[i].Name + ": orders below 100 are the core's, areas start at 100");
                Assert.IsTrue(steps[i].Method.IsStatic);
                Assert.AreEqual(0, steps[i].Method.GetParameters().Length);
                Assert.AreEqual(typeof(ProjectSetup).Assembly, steps[i].Method.DeclaringType.Assembly);
            }

            CollectionAssert.AreEqual(new[] { "CoreSetup.ConfigurePlayer", "CoreSetup.ConfigurePhysics" }, Names(ProjectSetup.Filter(steps, "configureplayer, Physics")),
                "-toyboxSteps picks steps by a part of their name");
            Assert.AreEqual(0, ProjectSetup.Filter(steps, "NoSuchStep").Count);
        }

        static List<string> Names(List<SetupStep> steps)
        {
            var names = new List<string>();
            foreach (SetupStep step in steps) names.Add(step.Name);
            return names;
        }

        [Test]
        public void RunSteps_RunsThemInOrder_AndLogsEachOne()
        {
            List<SetupStep> steps = ProjectSetup.Discover(typeof(SetupTests).Assembly);
            LogAssert.Expect(LogType.Log, "[Toybox] setup: ProbeSetup.Early");
            LogAssert.Expect(LogType.Log, "[Toybox] setup: ProbeSetup.Another");
            LogAssert.Expect(LogType.Log, "[Toybox] setup: ProbeSetup.Middle");
            LogAssert.Expect(LogType.Log, "[Toybox] setup: ProbeSetup.Late");
            Assert.AreEqual(0, ProjectSetup.RunSteps(steps));
            CollectionAssert.AreEqual(new[] { "Early", "Another", "Middle", "Late" }, ProbeSetup.Ran);

            // Twice is as good as once: the orchestrator adds no state of its own.
            ProbeSetup.Ran.Clear();
            Assert.AreEqual(0, ProjectSetup.RunSteps(steps));
            CollectionAssert.AreEqual(new[] { "Early", "Another", "Middle", "Late" }, ProbeSetup.Ran);
        }

        [Test]
        public void RunSteps_AStepThatThrowsIsReported_AndTheRestStillRun()
        {
            List<SetupStep> steps = ProjectSetup.Discover(typeof(SetupTests).Assembly);
            ProbeSetup.Break = true;
            LogAssert.Expect(LogType.Error, new Regex(@"^\[Toybox\] setup FAILED: ProbeSetup\.Middle: InvalidOperationException: middle step broke"));
            LogAssert.Expect(LogType.Exception, new Regex("middle step broke"));
            Assert.AreEqual(1, ProjectSetup.RunSteps(steps));
            CollectionAssert.AreEqual(new[] { "Early", "Another", "Middle", "Late" }, ProbeSetup.Ran);
        }

        // ------------------------------------------------------------------------------------------
        // SetupUtil
        // ------------------------------------------------------------------------------------------

        [Test]
        public void SetField_WritesSerializedFieldsByName_AndSaysWhetherAnythingChanged()
        {
            var holder = new GameObject("SetupTests Light") { hideFlags = HideFlags.DontSave };
            try
            {
                Light light = holder.AddComponent<Light>();
                light.intensity = 1f;
                Assert.IsTrue(SetupUtil.SetField(light, "m_Intensity", 2.5f));
                Assert.AreEqual(2.5f, light.intensity);
                Assert.IsFalse(SetupUtil.SetField(light, "m_Intensity", 2.5f), "idempotent: the second time nothing changes");
                Assert.IsTrue(SetupUtil.SetField(light, "m_Color", Color.red));
                Assert.AreEqual(Color.red, light.color);
                Assert.IsTrue(SetupUtil.SetField(light, "m_Enabled", false));
                Assert.IsFalse(light.enabled);
                Assert.IsTrue(SetupUtil.SetField(light, "m_Type", (int)LightType.Directional), "an enum takes its number");
                Assert.AreEqual(LightType.Directional, light.type);
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void SetField_AFieldThatDoesNotExistIsAClearError()
        {
            var holder = new GameObject("SetupTests Light") { hideFlags = HideFlags.DontSave };
            try
            {
                Light light = holder.AddComponent<Light>();
                var missing = Assert.Throws<InvalidOperationException>(() => SetupUtil.SetField(light, "m_Intensty", 2f));
                StringAssert.Contains("Light", missing.Message, "it names the object");
                StringAssert.Contains("'m_Intensty'", missing.Message, "the field that was asked for");
                StringAssert.Contains("m_Intensity", missing.Message, "and the fields that do exist with a similar name");

                var wrongKind = Assert.Throws<InvalidOperationException>(() => SetupUtil.SetField(light, "m_Intensity", true));
                StringAssert.Contains("m_Intensity", wrongKind.Message);
                StringAssert.Contains("Float", wrongKind.Message);
                Assert.Throws<ArgumentNullException>(() => SetupUtil.SetField(null, "m_Intensity", 1f));

                var serialized = new SerializedObject(light);
                Assert.IsNotNull(SetupUtil.Field(serialized, "m_Range"));
                var nothingLike = Assert.Throws<InvalidOperationException>(() => SetupUtil.Field(serialized, "m_Qzxw"));
                StringAssert.Contains("top-level fields", nothingLike.Message);
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        [Test]
        public void AssetHelpers_LoadWhatExists_AndCreateOnlyWhatIsMissing()
        {
            // Folders that exist are left alone.
            Assert.DoesNotThrow(() => SetupUtil.EnsureFolder("Assets/Toybox/Resources/Materials"));
            Assert.DoesNotThrow(() => SetupUtil.EnsureFolder("Assets\\Toybox\\Settings\\"));
            Assert.Throws<ArgumentException>(() => SetupUtil.EnsureFolder("NotAssets"));

            // An asset that exists is loaded, not made again.
            int made = 0;
            Material existing = SetupUtil.LoadOrCreate(CoreSetup.PlainLitPath, () => { made++; return new Material(Shader.Find("Universal Render Pipeline/Lit")); }, out bool created);
            Assert.IsNotNull(existing);
            Assert.IsFalse(created);
            Assert.AreEqual(0, made);
            Assert.AreSame(existing, SetupUtil.LoadOrCreateMaterial(CoreSetup.PlainLitPath, "Universal Render Pipeline/Lit"));
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<Material>(CoreSetup.ToyLitPath), SetupUtil.LoadOrCreateMaterial(CoreSetup.ToyLitPath, "Universal Render Pipeline/Lit", repoint: false),
                "repoint: false never changes the shader of a material that exists");

            var noShader = Assert.Throws<InvalidOperationException>(() => SetupUtil.LoadOrCreateMaterial(CoreSetup.PlainLitPath, "Toybox/NoSuchShader"));
            StringAssert.Contains("Toybox/NoSuchShader", noShader.Message);

            Assert.IsNotNull(SetupUtil.ProjectSettingsAsset("ProjectSettings/TagManager.asset"));
            Assert.Throws<InvalidOperationException>(() => SetupUtil.ProjectSettingsAsset("ProjectSettings/NoSuch.asset"));
        }

        [Test]
        public void RendererFeatures_AreAddedOnce_AndKeptInTheOrderAsked()
        {
            // A renderer that only exists in memory: the same list handling, without touching the project's asset.
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            try
            {
                FullScreenPassRendererFeature sticker = SetupUtil.EnsureRendererFeature<FullScreenPassRendererFeature>(renderer, "Sticker");
                Assert.IsNotNull(sticker);
                Assert.AreEqual("Sticker", sticker.name);
                Assert.AreSame(sticker, SetupUtil.EnsureRendererFeature<FullScreenPassRendererFeature>(renderer, "Sticker"), "asking again adds nothing");
                Assert.AreEqual(1, renderer.rendererFeatures.Count);

                // The lens blur arrives second (its owner ran later) but has to run first.
                FullScreenPassRendererFeature blur = SetupUtil.EnsureRendererFeature<FullScreenPassRendererFeature>(renderer, "MacroBand");
                Assert.AreNotSame(sticker, blur, "the same type under another name is another feature");
                Assert.AreEqual(2, renderer.rendererFeatures.Count);
                Assert.AreSame(sticker, renderer.rendererFeatures[0]);

                Assert.IsTrue(SetupUtil.OrderRendererFeatures(renderer, "MacroBand", "Sticker"));
                Assert.AreSame(blur, renderer.rendererFeatures[0]);
                Assert.AreSame(sticker, renderer.rendererFeatures[1]);
                Assert.IsFalse(SetupUtil.OrderRendererFeatures(renderer, "MacroBand", "Sticker"), "already in order: nothing moves");
                Assert.IsFalse(SetupUtil.OrderRendererFeatures(renderer, "NoSuchFeature", "MacroBand", "Sticker"), "names that are not there are skipped");

                // A feature nobody names keeps its place.
                FullScreenPassRendererFeature other = SetupUtil.EnsureRendererFeature<FullScreenPassRendererFeature>(renderer, "Other");
                Assert.IsTrue(SetupUtil.OrderRendererFeatures(renderer, "Sticker", "MacroBand"));
                Assert.AreSame(sticker, renderer.rendererFeatures[0]);
                Assert.AreSame(blur, renderer.rendererFeatures[1]);
                Assert.AreSame(other, renderer.rendererFeatures[2]);

                Assert.Throws<ArgumentException>(() => SetupUtil.EnsureRendererFeature(renderer, typeof(Light), "Nope"));
                Assert.Throws<ArgumentNullException>(() => SetupUtil.EnsureRendererFeature<FullScreenPassRendererFeature>(null, "Nope"));
            }
            finally
            {
                foreach (ScriptableRendererFeature feature in renderer.rendererFeatures)
                    if (feature != null) Object.DestroyImmediate(feature);
                Object.DestroyImmediate(renderer);
            }
        }

        [Test]
        public void TheCoreSetup_HasLeftTheProjectAsTheGameNeedsIt()
        {
            Assert.IsTrue(File.Exists(ProjectSetup.ScenePath), "the bootstrap scene");
            Assert.AreEqual(1, EditorBuildSettings.scenes.Length);
            Assert.AreEqual(ProjectSetup.ScenePath, EditorBuildSettings.scenes[0].path);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Material>(CoreSetup.ToyLitPath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Material>(CoreSetup.PlainLitPath));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Object>(CoreSetup.RendererPath));
            Assert.AreEqual("Prop", LayerMask.LayerToName(8));
            Assert.AreEqual("Held", LayerMask.LayerToName(10));
            Assert.IsNotNull(UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline, "a render pipeline is assigned");
        }
    }
}
