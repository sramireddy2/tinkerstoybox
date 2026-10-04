using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Toybox.Art;
using Toybox.EditorTools;
using Toybox.Engine;
using Toybox.Platform;
using Toybox.Render;
using Toybox.Toys;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using Object = UnityEngine.Object;

namespace Toybox.Tests
{
    /// <summary>A level built from a lambda that stands in one of the six rooms.</summary>
    public sealed class RoomLevel : LevelDefinition
    {
        readonly string environment;
        readonly Action<LevelContext> build;
        readonly float groundY;

        public RoomLevel(string environment, Action<LevelContext> build = null, float groundY = 0f)
        {
            this.environment = environment;
            this.build = build ?? Yard;
            this.groundY = groundY;
        }

        public override string Environment => environment;
        public override float GroundY => groundY;
        public override void Build(LevelContext ctx) => build(ctx);

        /// <summary>A low deck 20 by 40 on the play plane, the player on its near end.</summary>
        public static void Yard(LevelContext ctx)
        {
            ctx.AddStatic(BasicToys.Slab(new Vector3(20f, 0.4f, 40f)), new Vector3(0f, 0.2f, 0f));
            ctx.SetSpawn(new Vector3(0f, 0.4f, -15f), 0f);
        }
    }

    /// <summary>What the room's fixtures share: a Game in a room, presented by chosen presenters, all taken down afterwards.</summary>
    public abstract class RoomFixture
    {
        protected Game Game;
        protected Presentation Presentation;
        protected ScriptedInput Input;

        [SetUp]
        public void CleanSlate()
        {
            Game.Current?.Dispose();
            Materials.Plain = false;
            Settings.Use(new MemoryStore());
        }

        [TearDown]
        public void TakeDown()
        {
            Presentation?.Dispose();
            Presentation = null;
            Game?.Dispose();
            Game = null;
            Game.Current?.Dispose();
            Materials.Plain = false;
            Materials.Tier = QualityTier.Medium;
            Settings.Use(null);
        }

        protected Game Load(string environment, Action<LevelContext> build = null, float groundY = 0f)
        {
            Input = new ScriptedInput();
            Game = Game.Create(new GameOptions { Input = Input });
            Game.LoadLevel(new RoomLevel(environment, build, groundY));
            return Game;
        }

        protected Presentation Present(QualityTier tier, params Type[] presenters)
        {
            Presentation = Presentation.Create(Game, new PresentationOptions { Quality = tier, Presenters = Only(presenters) });
            return Presentation;
        }

        /// <summary>The room's three presenters and nothing else, in their order.</summary>
        public static readonly Type[] RoomPresenters = { typeof(LightingRig), typeof(RoomVisuals), typeof(PoolSystem) };

        public static List<PresenterRegistry.Entry> Only(params Type[] types)
        {
            var entries = new List<PresenterRegistry.Entry>();
            foreach (Type type in types) entries.Add(new PresenterRegistry.Entry(type, type.GetCustomAttribute<PresenterAttribute>()));
            entries.Sort((a, b) => a.Order.CompareTo(b.Order));
            return entries;
        }

        protected static void RequireGraphics() =>
            Assume.That(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "needs a graphics device (-nographics run)");

        protected static EnvironmentDescriptor Solve(EnvironmentPreset preset, float groundY = 0f) =>
            EnvironmentSolver.Solve(preset, new Bounds(new Vector3(0f, 0.2f, 0f), new Vector3(20f, 0.4f, 40f)), groundY);

        protected static void AssertVector(Vector4 expected, Vector4 actual, float tolerance, string what)
        {
            Assert.AreEqual(expected.x, actual.x, tolerance, what + ".x");
            Assert.AreEqual(expected.y, actual.y, tolerance, what + ".y");
            Assert.AreEqual(expected.z, actual.z, tolerance, what + ".z");
        }

        protected static Vector4 Lin(Color srgb, float gain = 1f)
        {
            Color linear = Palette.Lin(srgb);
            return new Vector4(linear.r * gain, linear.g * gain, linear.b * gain, 1f);
        }
    }

    // ----------------------------------------------------------------------------------------------------
    // Shaders, templates, the setup step
    // ----------------------------------------------------------------------------------------------------

    public class RoomShaderTests : RoomFixture
    {
        [Test]
        public void RoomLitAndFlatExistCompileAndHaveExactlyTheirPasses()
        {
            Shader room = Shader.Find(RoomSetup.RoomLitShader);
            Assert.IsNotNull(room, "Toybox/RoomLit was not found");
            Assert.IsFalse(ShaderUtil.ShaderHasError(room), "Toybox/RoomLit has compile errors");
            Assert.IsTrue(room.isSupported);
            Assert.AreEqual(new[] { "UNIVERSALFORWARD", "SHADOWCASTER", "DEPTHONLY" }, LightModes(room), "no DepthNormals pass, nothing else");

            Shader flat = Shader.Find(RoomSetup.FlatShader);
            Assert.IsNotNull(flat, "Toybox/Flat was not found");
            Assert.IsFalse(ShaderUtil.ShaderHasError(flat), "Toybox/Flat has compile errors");
            Assert.IsTrue(flat.isSupported);
            Assert.AreEqual(new[] { "SRPDEFAULTUNLIT" }, LightModes(flat));
        }

        static string[] LightModes(Shader shader)
        {
            var modes = new string[shader.passCount];
            // Unity reports some tag values in capitals, whatever the source says.
            for (int i = 0; i < modes.Length; i++) modes[i] = shader.FindPassTagValue(i, new ShaderTagId("LightMode")).name.ToUpperInvariant();
            return modes;
        }

        [Test]
        public void BothShadersCompileForTheWebBuild()
        {
            // The build is WebGL 2: every pass must survive the translation to GLSL ES 3, with and without
            // the shadow keywords the tiers use. (The editor only ever compiles them for its own device.)
            var keywordSets = new[]
            {
                new string[0],
                new[] { "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT" },
                new[] { "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT_LOW" },
                new[] { "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT_HIGH" },
            };
            foreach (string name in new[] { RoomSetup.RoomLitShader, RoomSetup.FlatShader })
            {
                ShaderData.Subshader subshader = ShaderUtil.GetShaderData(Shader.Find(name)).GetSubshader(0);
                for (int pass = 0; pass < subshader.PassCount; pass++)
                    foreach (string[] keywords in keywordSets)
                        foreach (ShaderType stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
                        {
                            ShaderData.VariantCompileInfo info = subshader.GetPass(pass).CompileVariant(stage, keywords, ShaderCompilerPlatform.GLES3x, BuildTarget.WebGL);
                            var messages = new List<string>();
                            foreach (ShaderMessage message in info.Messages)
                                if (message.severity == ShaderCompilerMessageSeverity.Error) messages.Add(message.message + " (" + message.file + ":" + message.line + ")");
                            Assert.IsTrue(info.Success && messages.Count == 0,
                                name + " pass " + pass + " " + stage + " [" + string.Join(" ", keywords) + "] does not compile for WebGL: " + string.Join("; ", messages));
                            // GLSL comes out as one program: the vertex stage's result holds both stages.
                            if (stage == ShaderType.Vertex) Assert.Greater(info.ShaderData.Length, 0, name + " pass " + pass + ": nothing was compiled");
                        }
            }
        }

        [Test]
        public void ColoursAreVectorPropertiesSoUnityDoesNotConvertThem()
        {
            Shader room = Shader.Find(RoomSetup.RoomLitShader), flat = Shader.Find(RoomSetup.FlatShader);
            foreach (string name in new[] { "_ColorTop", "_ColorSide", "_ColorDado", "_PatternA" })
                Assert.AreEqual(ShaderPropertyType.Vector, room.GetPropertyType(room.FindPropertyIndex(name)), name);
            foreach (string name in new[] { "_DadoY", "_Pattern", "_Corner", "_Cull" })
                Assert.GreaterOrEqual(room.FindPropertyIndex(name), 0, "RoomLit has no property " + name);
            Assert.AreEqual(ShaderPropertyType.Vector, flat.GetPropertyType(flat.FindPropertyIndex("_Color")), "_Color");
            foreach (string name in new[] { "_Shape", "_Soft", "_SrcBlend", "_DstBlend", "_ZWrite", "_ZTest", "_Cull" })
                Assert.GreaterOrEqual(flat.FindPropertyIndex(name), 0, "Flat has no property " + name);
        }

        [Test]
        public void TheOnlyKeywordsAreUrpsMainLightShadowOnes()
        {
            // No shader_feature: a material made in code can never ask for a variant the build stripped.
            var allowed = new HashSet<string> { "_MAIN_LIGHT_SHADOWS", "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT", "_SHADOWS_SOFT_LOW", "_SHADOWS_SOFT_MEDIUM", "_SHADOWS_SOFT_HIGH" };
            Assert.IsEmpty(Unexpected(RoomSetup.RoomLitShader, allowed), "RoomLit declares keywords of its own");
            Assert.IsEmpty(Unexpected(RoomSetup.FlatShader, new HashSet<string>()), "Flat has no keywords at all");
        }

        // The shader's keywords other than the allowed ones and the stereo ones Unity adds to every shader.
        static List<string> Unexpected(string shaderName, HashSet<string> allowed)
        {
            var unexpected = new List<string>();
            foreach (string keyword in Shader.Find(shaderName).keywordSpace.keywordNames)
                if (!allowed.Contains(keyword) && !keyword.StartsWith("STEREO_") && !keyword.StartsWith("UNITY_SINGLE_PASS_STEREO")) unexpected.Add(keyword);
            return unexpected;
        }

        [Test]
        public void TheTemplatesAreUnderResourcesAndCarryTheShaders()
        {
            // A shader only ships if a material under Resources points at it.
            Material room = Resources.Load<Material>("Materials/RoomLit"), flat = Resources.Load<Material>("Materials/Flat");
            Assert.IsNotNull(room, "Resources/Materials/RoomLit.mat is missing: run ProjectSetup");
            Assert.IsNotNull(flat, "Resources/Materials/Flat.mat is missing: run ProjectSetup");
            Assert.AreEqual(RoomSetup.RoomLitShader, room.shader.name);
            Assert.AreEqual(RoomSetup.FlatShader, flat.shader.name);
            Assert.AreEqual(RoomSetup.RoomLitPath, AssetDatabase.GetAssetPath(room));
            Assert.AreEqual(RoomSetup.FlatPath, AssetDatabase.GetAssetPath(flat));
            Assert.IsEmpty(room.shaderKeywords);
            Assert.IsEmpty(flat.shaderKeywords);
            Assert.IsFalse(room.enableInstancing);
            Assert.IsFalse(flat.enableInstancing);
        }

        [Test]
        public void TheSetupStepIsFoundInTheRoomsRangeAndIsIdempotent()
        {
            SetupStep step = ProjectSetup.Discover().Find(s => s.Name == "RoomSetup.CreateMaterials");
            Assert.IsNotNull(step, "RoomSetup.CreateMaterials is not a setup step");
            Assert.That(step.Order, Is.InRange(200, 299));

            string before = AssetDatabase.AssetPathToGUID(RoomSetup.RoomLitPath);
            RoomSetup.CreateMaterials();
            RoomSetup.CreateMaterials();
            Assert.AreEqual(before, AssetDatabase.AssetPathToGUID(RoomSetup.RoomLitPath), "the template was created again");
            Assert.IsFalse(EditorUtility.IsDirty(AssetDatabase.LoadAssetAtPath<Material>(RoomSetup.RoomLitPath)), "a second run changed the template");
        }

        [Test]
        public void RoomAndFlatMaterialsComeOutOfTheTemplatesWithTheRecipesNumbers()
        {
            Material floor = Materials.Room(RoomSurface.ShellFloor, Palette.Mint, PatternSpec.Dots);
            Assert.AreEqual(RoomSetup.RoomLitShader, floor.shader.name);
            AssertVector(Lin(Palette.Mint.Mid), floor.GetVector("_ColorTop"), 1e-4f, "_ColorTop");
            Assert.AreEqual(1f, floor.GetFloat("_Pattern"));
            Assert.AreEqual(PatternSpec.Dots.A, floor.GetVector("_PatternA"));
            Assert.AreEqual(1f, floor.GetFloat("_Corner"));

            Material wall = Materials.Room(RoomSurface.ShellWall, Palette.Butter, PatternSpec.None, 5f);
            AssertVector(Lin(Palette.Butter.Light), wall.GetVector("_ColorSide"), 1e-4f, "_ColorSide");
            AssertVector(Lin(Palette.Butter.Mid), wall.GetVector("_ColorDado"), 1e-4f, "_ColorDado");
            Assert.AreEqual(35f, wall.GetFloat("_DadoY"), "the dado line is 30 above the play plane");

            Material shadow = Materials.Flat(new FlatRecipe { Name = "Test Shadow", Color = new Color(0.2f, 0.6f, 0.45f, 1f), Blend = FlatBlend.Multiply });
            Assert.AreEqual(RoomSetup.FlatShader, shadow.shader.name);
            Assert.AreEqual((float)BlendMode.DstColor, shadow.GetFloat("_SrcBlend"));
            Assert.AreEqual((float)BlendMode.Zero, shadow.GetFloat("_DstBlend"));
            Assert.AreEqual(0f, shadow.GetFloat("_ZWrite"));
            Assert.AreEqual(3000, shadow.renderQueue);

            // A level's own slabs take the room's dip through the same shader.
            Load("cardboard-box");
            Renderer slab = Game.LevelRoot.GetComponentInChildren<Renderer>();
            Assert.AreEqual(RoomSetup.RoomLitShader, slab.sharedMaterial.shader.name);
            AssertVector(Lin(Palette.Peach.Light), slab.sharedMaterial.GetVector("_ColorTop"), 1e-4f, "a level static's top in the Peach room");
            AssertVector(Lin(Palette.Peach.Deep), slab.sharedMaterial.GetVector("_ColorSide"), 1e-4f, "a level static's side in the Peach room");
        }
    }

    // ----------------------------------------------------------------------------------------------------
    // The lighting rig
    // ----------------------------------------------------------------------------------------------------

    public class LightingRigTests : RoomFixture
    {
        [Test]
        public void TheDayRigIsTheArtBiblesNumbersForEveryDayPreset()
        {
            foreach (EnvironmentPreset preset in EnvironmentPreset.All)
            {
                if (preset.Night) continue;
                EnvironmentDescriptor env = Solve(preset, 3f);
                LightingValues v = LightingValues.For(env);
                Dip dip = preset.Dip;
                string name = preset.Key;

                Assert.AreEqual(preset.SunColor, v.SunColor, name);
                Assert.AreEqual(preset.SunIntensity, v.SunIntensity, name);
                Assert.Less(Quaternion.Angle(env.SunRotation, v.SunRotation), 0.01f, name);

                AssertVector(new Vector4(0.45f, 0.45f, 0.45f), v.AmbSky, 1e-5f, name + " _AmbSky");
                Vector4 equator = (new Vector4(1f, 1f, 1f, 1f) + Lin(dip.Light)) * 0.5f * 0.42f;
                AssertVector(equator, v.AmbEquator, 1e-5f, name + " _AmbEquator");
                AssertVector(Lin(dip.Deep, 0.40f), v.AmbGround, 1e-5f, name + " _AmbGround");
                AssertVector(Lin(dip.Light, 0.20f), v.KickColor, 1e-5f, name + " _KickColor");

                AssertVector(Lin(Palette.GlintDay), v.GlintColor, 1e-5f, name + " _GlintColor");
                AssertVector(new Vector4(1.6f, 1.6f, 1.6f), v.EnvCeil, 1e-5f, name + " _EnvCeil");
                AssertVector(Lin(dip.Light), v.EnvWall, 1e-5f, name + " _EnvWall");
                AssertVector(Lin(dip.Mid, 0.7f), v.EnvFloor, 1e-5f, name + " _EnvFloor");
                Assert.AreEqual(1f, v.ToyGlowGain, name);

                AssertVector(Lin(dip.Haze), v.HazeColor, 1e-5f, name + " _HazeColor");
                Assert.AreEqual(new Vector4(preset.HazeDensity, 3f + 60f, 3f + 150f, 0.5f), v.HazeParams, name + " _HazeParams");
                AssertVector(Lin(preset.PatchColor, preset.PatchGain), v.PatchColor, 1e-5f, name + " _PatchColor");
                Assert.AreEqual(dip.Haze, v.Background, name + ": the camera clears to the haze colour");
            }
        }

        [Test]
        public void TheNightRigHasItsOwnAmbientItsMoonGlintAndGlowingToys()
        {
            EnvironmentDescriptor env = Solve(EnvironmentPreset.NightLight);
            LightingValues v = LightingValues.For(env);
            Dip plum = Palette.Plum;
            Vector4 sky = Lin(Palette.NightAmbient);

            Assert.AreEqual(Palette.Moon, v.SunColor);
            Assert.AreEqual(0.50f, v.SunIntensity);
            AssertVector(sky * 0.34f, v.AmbSky, 1e-5f, "_AmbSky");
            AssertVector((sky + Lin(plum.Light)) * 0.5f * 0.30f, v.AmbEquator, 1e-5f, "_AmbEquator");
            AssertVector(Lin(plum.Deep, 0.35f), v.AmbGround, 1e-5f, "_AmbGround");
            AssertVector(Lin(Palette.Moon), v.GlintColor, 1e-5f, "_GlintColor");
            Assert.AreEqual(6f, v.ToyGlowGain);
            AssertVector(Lin(Palette.Moon, 0.35f), v.PatchColor, 1e-5f, "_PatchColor");
        }

        [Test]
        public void TheKickerComesFromBehindAndAcrossTheSunLow()
        {
            foreach (EnvironmentPreset preset in EnvironmentPreset.All)
            {
                EnvironmentDescriptor env = Solve(preset);
                Vector3 kick = LightingValues.For(env).KickDir;
                Assert.AreEqual(1f, kick.magnitude, 1e-4f, preset.Key);
                Assert.AreEqual(20f, Mathf.Asin(kick.y) * Mathf.Rad2Deg, 0.01f, preset.Key + ": elevation");
                float azimuth = Mathf.Atan2(kick.x, kick.z) * Mathf.Rad2Deg;
                Assert.AreEqual(0f, Mathf.DeltaAngle(azimuth, env.SunAzimuth + 145f), 0.01f, preset.Key + ": azimuth");
            }
        }

        [Test]
        public void TheWindowTheGlintAndTheShellAreTheDescriptors()
        {
            EnvironmentDescriptor env = Solve(EnvironmentPreset.BlockHall);
            LightingValues v = LightingValues.For(env);
            AssertVector(env.WindowCenter, v.WinO, 1e-5f, "_WinO");
            AssertVector(env.WindowU, v.WinU, 1e-6f, "_WinU");
            AssertVector(env.WindowV, v.WinV, 1e-6f, "_WinV");
            AssertVector(env.WindowNormal, v.WinN, 1e-6f, "_WinN");
            Assert.AreEqual(0.04f, v.WinMullion);
            AssertVector(env.SunDirection, v.WinDir, 1e-6f, "_WinDir");
            AssertVector(env.GlintRight, v.WinRight, 1e-6f, "_WinRight");
            AssertVector(env.GlintUp, v.WinUp, 1e-6f, "_WinUp");
            AssertVector(env.ShellMin, v.ShellMin, 1e-5f, "_ShellMin");
            AssertVector(env.ShellMax, v.ShellMax, 1e-5f, "_ShellMax");

            // The ray the patch is solved for: from the window's centre along the light onto the level's focus.
            Vector3 landed = env.WindowCenter - env.SunDirection * ((env.WindowCenter.y - env.GroundY) / env.SunDirection.y);
            Assert.Less(Vector3.Distance(landed, env.Focus), 0.01f, "the patch does not land on the level");
        }

        [Test]
        public void ALevelWithoutARoomKeepsItsSunAndHasNoPatch()
        {
            EnvironmentDescriptor env = Solve(EnvironmentPreset.None);
            LightingValues v = LightingValues.For(env);
            Assert.AreEqual(Vector4.zero, v.PatchColor);
            Assert.AreEqual(0.62f, v.SunIntensity);
            Assert.Less(v.ShellMin.x, -1e4f, "the corner gradient must find no shell");
            Assert.Greater(v.ShellMax.y, 1e4f);
        }

        [Test]
        public void CalibrationASunlitFloorGetsAboutNinetyFivePercentAndItsShadowAboutHalfOfThat()
        {
            // ART_BIBLE 5.1: 0.62 x sin 45 + 0.45 + kicker = about 0.95; lit to shadow between 1.8 and 2.0.
            foreach (EnvironmentPreset preset in EnvironmentPreset.All)
            {
                if (preset.Night) continue;
                EnvironmentDescriptor env = Solve(preset);
                LightingValues v = LightingValues.For(env);
                float sun = v.SunIntensity * Mathf.Sin(env.SunElevation * Mathf.Deg2Rad);
                float fill = v.AmbSky.x + v.KickColor.x * Mathf.Max(0f, v.KickDir.y);
                Assert.That(sun + fill, Is.InRange(0.90f, 1.0f), preset.Key + ": light on a sunlit floor");
                Assert.That((sun + fill) / fill, Is.InRange(1.8f, 2.0f), preset.Key + ": lit to shadow");
            }
        }

        [Test]
        public void ThePresenterSetsTheSunTheGlobalsTheAmbientAndTheCameraAndPutsThemBack()
        {
            AmbientMode modeBefore = RenderSettings.ambientMode;
            Color skyBefore = RenderSettings.ambientSkyColor;
            bool fogBefore = RenderSettings.fog;

            Load("block-hall");
            Present(QualityTier.Medium, typeof(LightingRig));
            var rig = Presentation.Get<LightingRig>();
            Assert.IsNotNull(rig);
            LightingValues v = LightingValues.For(Game.Environment);

            Light[] lights = Game.Root.GetComponentsInChildren<Light>();
            Assert.AreEqual(1, lights.Length, "one real light, the sun");
            Assert.AreSame(rig.Sun, lights[0]);
            Assert.AreEqual(LightType.Directional, rig.Sun.type);
            Assert.AreEqual(LightShadows.Soft, rig.Sun.shadows);
            Assert.AreEqual(1f, rig.Sun.shadowStrength);
            Assert.AreEqual(0.2f, rig.Sun.shadowNearPlane, 1e-5f);
            Assert.AreEqual(0.66f, rig.Sun.intensity, 1e-5f);
            Assert.IsTrue(Palette.Same(Palette.Hex("#FFF1DC"), rig.Sun.color));
            Assert.Less(Vector3.Angle(-rig.Sun.transform.forward, Game.Environment.SunDirection), 0.01f, "the light shines from the sun");
            Assert.IsTrue(rig.Sun.transform.IsChildOf(Presentation.Context.Root));

            AssertVector(v.AmbSky, Shader.GetGlobalVector("_AmbSky"), 1e-5f, "_AmbSky");
            AssertVector(v.AmbEquator, Shader.GetGlobalVector("_AmbEquator"), 1e-5f, "_AmbEquator");
            AssertVector(v.AmbGround, Shader.GetGlobalVector("_AmbGround"), 1e-5f, "_AmbGround");
            AssertVector(v.KickDir, Shader.GetGlobalVector("_KickDir"), 1e-5f, "_KickDir");
            AssertVector(v.KickColor, Shader.GetGlobalVector("_KickColor"), 1e-5f, "_KickColor");
            AssertVector(v.WinDir, Shader.GetGlobalVector("_WinDir"), 1e-5f, "_WinDir");
            AssertVector(v.GlintColor, Shader.GetGlobalVector("_GlintColor"), 1e-5f, "_GlintColor");
            AssertVector(v.EnvCeil, Shader.GetGlobalVector("_EnvCeil"), 1e-5f, "_EnvCeil");
            AssertVector(v.EnvWall, Shader.GetGlobalVector("_EnvWall"), 1e-5f, "_EnvWall");
            AssertVector(v.EnvFloor, Shader.GetGlobalVector("_EnvFloor"), 1e-5f, "_EnvFloor");
            Assert.AreEqual(1f, Shader.GetGlobalFloat("_ToyGlowGain"));
            AssertVector(v.HazeColor, Shader.GetGlobalVector("_HazeColor"), 1e-5f, "_HazeColor");
            Assert.AreEqual(v.HazeParams, Shader.GetGlobalVector("_HazeParams"));
            AssertVector(v.WinO, Shader.GetGlobalVector("_WinO"), 1e-4f, "_WinO");
            AssertVector(v.WinU, Shader.GetGlobalVector("_WinU"), 1e-6f, "_WinU");
            AssertVector(v.WinV, Shader.GetGlobalVector("_WinV"), 1e-6f, "_WinV");
            AssertVector(v.WinN, Shader.GetGlobalVector("_WinN"), 1e-6f, "_WinN");
            Assert.AreEqual(0.04f, Shader.GetGlobalFloat("_WinMullion"), 1e-6f);
            AssertVector(v.PatchColor, Shader.GetGlobalVector("_PatchColor"), 1e-5f, "_PatchColor");
            AssertVector(v.ShellMin, Shader.GetGlobalVector("_ShellMin"), 1e-4f, "_ShellMin");
            AssertVector(v.ShellMax, Shader.GetGlobalVector("_ShellMax"), 1e-4f, "_ShellMax");

            Assert.AreEqual(AmbientMode.Trilight, RenderSettings.ambientMode);
            Assert.IsFalse(RenderSettings.fog, "haze is computed in RoomLit only");
            Assert.IsNull(RenderSettings.skybox, "the sky is a card outside the window");
            Assert.AreEqual(CameraClearFlags.SolidColor, Presentation.Camera.clearFlags);
            Assert.IsTrue(Palette.Same(Palette.Butter.Haze, Presentation.Camera.backgroundColor), "the camera clears to the haze colour");

            Presentation.Dispose();
            Presentation = null;
            Assert.AreEqual(modeBefore, RenderSettings.ambientMode);
            Assert.AreEqual(skyBefore, RenderSettings.ambientSkyColor);
            Assert.AreEqual(fogBefore, RenderSettings.fog);
            Assert.AreEqual(0, Game.Root.GetComponentsInChildren<Light>().Length, "the sun stayed behind");
            Assert.AreEqual(Vector4.zero, Shader.GetGlobalVector("_PatchColor"), "the patch stayed behind");
        }

        [Test]
        public void EveryLevelLoadSetsTheRigAgain()
        {
            Load("sunny-rug");
            Present(QualityTier.Medium, typeof(LightingRig));
            var rig = Presentation.Get<LightingRig>();
            AssertVector(Lin(Palette.Mint.Haze), Shader.GetGlobalVector("_HazeColor"), 1e-5f, "_HazeColor in the Mint room");

            Game.LoadLevel(new RoomLevel("night-light", null, 2f));
            AssertVector(Lin(Palette.Plum.Haze), Shader.GetGlobalVector("_HazeColor"), 1e-5f, "_HazeColor in the Plum room");
            Assert.AreEqual(6f, Shader.GetGlobalFloat("_ToyGlowGain"));
            Assert.AreEqual(0.50f, rig.Sun.intensity, 1e-5f);
            Assert.IsTrue(Palette.Same(Palette.Moon, rig.Sun.color));
            Assert.AreEqual(2f + 60f, Shader.GetGlobalVector("_HazeParams").y, 1e-4f, "height haze starts 60 above the play plane");
            Assert.IsTrue(Palette.Same(Palette.Plum.Haze, Presentation.Camera.backgroundColor));

            Game.LoadLevel(new AdHocLevel(ctx => TestHelpers.Floor(ctx)));
            Assert.AreEqual(Vector4.zero, Shader.GetGlobalVector("_PatchColor"), "a level without a room has no window patch");
        }

        [Test]
        public void TheRigAllocatesNothingPerFrame()
        {
            Load("sunny-rug");
            Present(QualityTier.Medium, typeof(LightingRig));
            var rig = Presentation.Get<LightingRig>();
            rig.Frame(Sim.Dt, 1f);
            Assert.That(() => rig.Frame(Sim.Dt, 1f), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }
    }

    // ----------------------------------------------------------------------------------------------------
    // Geometry: the bag, the shell, the furniture
    // ----------------------------------------------------------------------------------------------------

    public class RoomKitTests : RoomFixture
    {
        [Test]
        public void ABagTurnsQuadsBoxesAndMeshesToFaceTheWayTheyShould()
        {
            var bag = new MeshBag();
            Assert.IsTrue(bag.Empty);
            Assert.IsNull(bag.Build("nothing"));

            // Corners given the wrong way round still face along the normal.
            bag.Quad(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, 0f), Vector3.down);
            AssertFacing(bag, 0, Vector3.down);
            int afterQuad = bag.IndexCount;
            bag.Box(new Vector3(5f, 5f, 5f), new Vector3(2f, 4f, 6f), Quaternion.Euler(0f, 30f, 0f), Color.white);
            Assert.AreEqual(2 + 12, bag.TriangleCount);
            AssertFacing(bag, afterQuad, null);

            Mesh sphere = MeshKit.Sphere(1f, 8, 6);
            int afterBox = bag.IndexCount, vertices = bag.VertexCount;
            bag.Add(sphere, Matrix4x4.TRS(new Vector3(-10f, 0f, 0f), Quaternion.identity, new Vector3(2f, 1f, -3f)), new Color(0.5f, 0.5f, 0.5f, 1f));
            MeshKit.Release(sphere);
            for (int v = vertices; v < bag.VertexCount; v++)
            {
                Vector3 p = bag.Position(v) - new Vector3(-10f, 0f, 0f);
                Assert.AreEqual(1f, new Vector3(p.x / 2f, p.y, p.z / 3f).magnitude, 1e-3f, "the mesh was not placed by its matrix");
                Assert.AreEqual(0.5f, bag.Color(v).r, 1e-5f, "the tint");
                Assert.Greater(Vector3.Dot(bag.Normal(v), p), -1e-3f, "a mirrored sphere's normals still point outward");
            }
            AssertFacing(bag, afterBox, null);

            Mesh mesh = bag.Build("Test Bag");
            try
            {
                Assert.AreEqual(bag.VertexCount, mesh.vertexCount);
                Assert.AreEqual(bag.TriangleCount, MeshUtil.TriangleCount(mesh));
                Assert.IsFalse(mesh.isReadable, "built meshes are uploaded and let go of");
            }
            finally
            {
                MeshKit.Release(mesh);
            }
        }

        // Every triangle from an index on faces along its vertices' normals (or along the normal given).
        static void AssertFacing(MeshBag bag, int fromIndex, Vector3? normal)
        {
            for (int i = fromIndex; i + 2 < bag.IndexCount; i += 3)
            {
                Vector3 a = bag.Position(bag.Index(i)), b = bag.Position(bag.Index(i + 1)), c = bag.Position(bag.Index(i + 2));
                Vector3 face = Vector3.Cross(b - a, c - a);
                Vector3 expected = normal ?? bag.Normal(bag.Index(i)) + bag.Normal(bag.Index(i + 1)) + bag.Normal(bag.Index(i + 2));
                Assert.Greater(Vector3.Dot(face, expected), 0f, "triangle " + i / 3 + " faces the wrong way");
            }
        }

        [Test]
        public void ContactShadingDarkensTheFootToThreeQuartersOverExactlySixUnits()
        {
            var bag = new MeshBag();
            bag.Box(new Vector3(100f, 0f, 0f), new Vector3(4f, 4f, 4f), Quaternion.identity, Color.white);   // an earlier piece: untouched
            int from = bag.IndexCount, firstVertex = bag.VertexCount;
            bag.Box(new Vector3(0f, 20f, 0f), new Vector3(10f, 40f, 10f), Quaternion.identity, Color.white);
            int before = bag.TriangleCount;
            bag.ShadeBase(from, 0f, 6f, 0.75f);

            Assert.Greater(bag.TriangleCount, before, "the tall sides were not cut at the top of the fade");
            bool cut = false;
            for (int v = 0; v < bag.VertexCount; v++)
            {
                Vector3 p = bag.Position(v);
                float shade = bag.Color(v).r;
                if (v < firstVertex)
                {
                    Assert.AreEqual(1f, shade, 1e-6f, "an earlier piece was shaded");
                    continue;
                }
                float expected = Mathf.Lerp(0.75f, 1f, Mathf.Clamp01(p.y / 6f));
                Assert.AreEqual(expected, shade, 1e-4f, "shade at height " + p.y);
                if (Mathf.Abs(p.y - 6f) < 1e-3f) cut = true;
            }
            Assert.IsTrue(cut, "no vertices at the top of the fade");
            AssertFacing(bag, from, null);
        }

        [Test]
        public void TheConvexHullRunsCounterClockwiseWithoutInnerOrCollinearPoints()
        {
            var points = new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(4f, 0f), new Vector2(4f, 3f), new Vector2(0f, 3f),
                new Vector2(2f, 1f), new Vector2(2f, 0f), new Vector2(4f, 0f), new Vector2(1f, 2f),
            };
            List<Vector2> hull = RoomKit.ConvexHull(points);
            Assert.AreEqual(4, hull.Count);
            float area = 0f;
            for (int i = 0; i < hull.Count; i++)
            {
                Vector2 a = hull[i], b = hull[(i + 1) % hull.Count];
                area += a.x * b.y - b.x * a.y;
            }
            Assert.AreEqual(12f, area * 0.5f, 1e-4f, "counter-clockwise, the whole rectangle");
            Assert.IsEmpty(RoomKit.ConvexHull(new List<Vector2> { Vector2.zero, Vector2.one }));
        }

        [Test]
        public void TheWindowIsCutOutOfItsWallAndNothingElseIs()
        {
            foreach (EnvironmentPreset preset in EnvironmentPreset.All)
            {
                EnvironmentDescriptor env = Solve(preset);
                var walls = new MeshBag();
                RoomKit.Walls(env, walls, walls);
                float x = env.WindowSide < 0 ? env.ShellMin.x : env.ShellMax.x;
                Vector2 half = env.WindowHalfSize;
                Vector3 w = env.WindowCenter;

                Assert.AreEqual(0, CoveringX(walls, x, w.z, w.y), preset.Key + ": the opening is walled up at its centre");
                Assert.AreEqual(0, CoveringX(walls, x, w.z + half.x - 0.5f, w.y + half.y - 0.5f), preset.Key + ": ... in its corner");
                Assert.AreEqual(1, CoveringX(walls, x, w.z + half.x + 0.5f, w.y), preset.Key + ": the wall beside the opening");
                Assert.AreEqual(1, CoveringX(walls, x, w.z, w.y - half.y - 0.5f), preset.Key + ": the wall under the opening");
                Assert.AreEqual(1, CoveringX(walls, x, w.z, w.y + half.y + 0.5f), preset.Key + ": the wall over the opening");
                float other = env.WindowSide < 0 ? env.ShellMax.x : env.ShellMin.x;
                Assert.AreEqual(1, CoveringX(walls, other, w.z, w.y), preset.Key + ": the wall across the room is whole");

                // All four walls face into the room.
                Vector3 middle = (env.ShellMin + env.ShellMax) * 0.5f;
                for (int i = 0; i + 2 < walls.IndexCount; i += 3)
                {
                    Vector3 a = walls.Position(walls.Index(i)), b = walls.Position(walls.Index(i + 1)), c = walls.Position(walls.Index(i + 2));
                    Vector3 centre = (a + b + c) / 3f;
                    Vector3 inward = middle - centre;
                    inward.y = 0f;
                    Assert.Greater(Vector3.Dot(Vector3.Cross(b - a, c - a), inward), 0f, preset.Key + ": a wall faces out of the room");
                    Assert.Greater(Vector3.Dot(walls.Normal(walls.Index(i)), inward), 0f, preset.Key + ": a wall's normal points out of the room");
                }
            }
        }

        // How many triangles in the plane x = wallX cover the point (z, y).
        static int CoveringX(MeshBag bag, float wallX, float z, float y)
        {
            int covering = 0;
            var p = new Vector2(z, y);
            for (int i = 0; i + 2 < bag.IndexCount; i += 3)
            {
                Vector3 a = bag.Position(bag.Index(i)), b = bag.Position(bag.Index(i + 1)), c = bag.Position(bag.Index(i + 2));
                if (Mathf.Abs(a.x - wallX) > 1e-3f || Mathf.Abs(b.x - wallX) > 1e-3f || Mathf.Abs(c.x - wallX) > 1e-3f) continue;
                if (Inside(p, new Vector2(a.z, a.y), new Vector2(b.z, b.y), new Vector2(c.z, c.y))) covering++;
            }
            return covering;
        }

        static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d0 = Cross(b - a, p - a), d1 = Cross(c - b, p - b), d2 = Cross(a - c, p - c);
            return (d0 >= 0f && d1 >= 0f && d2 >= 0f) || (d0 <= 0f && d1 <= 0f && d2 <= 0f);
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        [Test]
        public void TheShellIsExactlyTheDescriptorsBox()
        {
            EnvironmentDescriptor env = Solve(EnvironmentPreset.PegboardWorkbench, -9f);
            var floor = new MeshBag();
            var ceiling = new MeshBag();
            RoomKit.Floor(env, floor);
            RoomKit.Ceiling(env, ceiling);
            for (int v = 0; v < floor.VertexCount; v++)
            {
                Assert.AreEqual(env.FloorY, floor.Position(v).y, 1e-4f, "the real floor is the preset's drop below the play plane");
                Assert.AreEqual(Vector3.up, floor.Normal(v));
            }
            Assert.AreEqual(-9f - 60f, env.FloorY, 1e-4f);
            for (int v = 0; v < ceiling.VertexCount; v++)
            {
                Assert.AreEqual(-9f + 170f, ceiling.Position(v).y, 1e-4f, "the ceiling is 170 above the play plane");
                Assert.AreEqual(Vector3.down, ceiling.Normal(v));
            }

            // The island's visual is its collider box.
            var island = new MeshBag();
            RoomKit.Island(env, island);
            Bounds bounds = BoundsOf(island, 0);
            Assert.Less(Vector3.Distance(env.IslandBounds.min, bounds.min), 1e-3f);
            Assert.Less(Vector3.Distance(env.IslandBounds.max, bounds.max), 1e-3f);
            Assert.AreEqual(-9f, bounds.max.y, 1e-4f, "the bench top is the play plane");

            var none = new MeshBag();
            RoomKit.Island(Solve(EnvironmentPreset.BlockHall), none);
            Assert.IsTrue(none.Empty, "block-hall has no island");
        }

        static Bounds BoundsOf(MeshBag bag, int fromVertex)
        {
            var bounds = new Bounds(bag.Position(fromVertex), Vector3.zero);
            for (int v = fromVertex; v < bag.VertexCount; v++) bounds.Encapsulate(bag.Position(v));
            return bounds;
        }

        [Test]
        public void SkirtingFrameAndPlatesAreTheSizesOfTheScaleAnchors()
        {
            EnvironmentDescriptor env = Solve(EnvironmentPreset.SunnyRug);
            var trim = new MeshBag();
            RoomKit.Trim(env, trim);
            Bounds all = BoundsOf(trim, 0);

            // Skirting: 3.5 high and 0.5 deep along all four walls, standing on the real floor.
            Assert.AreEqual(env.FloorY, all.min.y, 1e-4f);
            bool top = false, farWall = false, backWall = false;
            for (int v = 0; v < trim.VertexCount; v++)
            {
                Vector3 p = trim.Position(v);
                if (Mathf.Abs(p.y - (env.FloorY + 3.5f)) < 1e-3f) top = true;
                if (Mathf.Abs(p.x - (env.ShellMax.x - 0.5f)) < 1e-3f && p.y < env.FloorY + 3.2f) farWall = true;
                if (Mathf.Abs(p.z - (env.ShellMax.z - 0.5f)) < 1e-3f && p.y < env.FloorY + 3.2f) backWall = true;
                // Nothing of the trim stands more than a sill's depth inside the room, or deeper in the wall than the wall is thick.
                Assert.GreaterOrEqual(p.x, env.ShellMin.x - RoomKit.WallThickness - 1e-3f);
                Assert.LessOrEqual(p.x, env.ShellMax.x + 1e-3f);
            }
            Assert.IsTrue(top, "no skirting 3.5 high");
            Assert.IsTrue(farWall, "no skirting 0.5 deep on the wall across the room");
            Assert.IsTrue(backWall, "no skirting 0.5 deep on the back wall");

            // The frame's face is flush with the pane the simulation put in the opening.
            Assert.IsTrue(env.TryGetBox(EnvironmentBoxKind.WindowPane, out EnvironmentBox pane));
            float paneFace = pane.Center.x + pane.Size.x * 0.5f;
            bool flush = false;
            for (int v = 0; v < trim.VertexCount; v++)
            {
                Vector3 p = trim.Position(v);
                // The bars' corners inside the opening (its own border belongs to the reveal, the casing and the sill).
                bool inOpening = Mathf.Abs(p.z - env.WindowCenter.z) < 35.01f && Mathf.Abs(p.y - env.WindowCenter.y) < 52f;
                if (!inOpening) continue;
                Assert.LessOrEqual(p.x, paneFace + 1e-3f, "a bar of the frame stands in front of the pane");
                if (Mathf.Abs(p.x - paneFace) < 1e-3f) flush = true;
            }
            Assert.IsTrue(flush, "the frame's bars are not flush with the pane");
        }

        [Test]
        public void TheSkyCardStandsSixtyOutsideTheWindowAndCarriesItsColours()
        {
            foreach (EnvironmentPreset preset in new[] { EnvironmentPreset.SunnyRug, EnvironmentPreset.BlockHall, EnvironmentPreset.NightLight })
            {
                EnvironmentDescriptor env = Solve(preset);
                var sky = new MeshBag();
                RoomKit.Sky(env, sky);
                float wall = env.WindowSide < 0 ? env.ShellMin.x : env.ShellMax.x;
                float brightest = 0f;
                for (int v = 0; v < sky.VertexCount; v++)
                {
                    float outside = (sky.Position(v).x - wall) * env.WindowSide;
                    Assert.That(outside, Is.InRange(40f, 60.01f), preset.Key + ": the sky is not outside the window wall");
                    brightest = Mathf.Max(brightest, sky.Color(v).maxColorComponent);
                }
                // The card itself: first four vertices, bottom tone.
                Color bottom = RoomKit.Hdr(preset.Night ? Palette.NightSkyBottom : Palette.SkyBottom, preset.Night ? 1f : RoomKit.SkyGain);
                Assert.AreEqual(bottom.r, sky.Color(0).r, 1e-4f, preset.Key);
                Assert.AreEqual(bottom.b, sky.Color(0).b, 1e-4f, preset.Key);
                Assert.AreEqual(wall + env.WindowSide * 60f, sky.Position(0).x, 1e-3f, preset.Key + ": 60 outside");
                Assert.AreEqual(Palette.Lin(Palette.Paper).r * (preset.Night ? 2.5f : RoomKit.CloudGain), brightest, 1e-3f,
                    preset.Key + (preset.Night ? ": the moon is Paper x 2.5" : ": the clouds are Paper at the clouds' gain"));
                Assert.Greater(brightest, 1.1f, preset.Key + ": the brightest of the sky must pass the bloom threshold");
            }
        }

        [Test]
        public void EveryPieceOfFurnitureFillsItsColliderBoxesAndStaysInThem()
        {
            int pieces = 0;
            var seen = new HashSet<FurnitureKind>();
            foreach (EnvironmentPreset preset in EnvironmentPreset.All)
            {
                EnvironmentDescriptor env = Solve(preset);
                foreach (EnvironmentPiece piece in env.Pieces)
                {
                    pieces++;
                    seen.Add(piece.Kind);
                    var bags = new RoomBags();
                    FurnitureKit.BuildPiece(env, piece, bags);
                    string name = preset.Key + " " + piece.Kind + " #" + piece.Index;
                    int vertices = bags.Furniture.VertexCount + bags.Trim.VertexCount + bags.Cardboard.VertexCount + bags.Glow.VertexCount;
                    Assert.Greater(vertices, 0, name + " has no visual");

                    // A door's frame and doorway are on the wall beside an open door; a lantern's cord runs up to the ceiling.
                    float tolerance = piece.Kind == FurnitureKind.Door ? 13f : 0.75f;
                    bool any = false;
                    Bounds visual = default;
                    foreach (MeshBag bag in new[] { bags.Furniture, bags.Trim, bags.Cardboard, bags.Glow })
                    {
                        for (int v = 0; v < bag.VertexCount; v++)
                        {
                            Vector3 p = bag.Position(v);
                            float distance = DistanceToBoxes(env, piece, p, piece.Kind == FurnitureKind.PaperLantern);
                            Assert.LessOrEqual(distance, tolerance, name + ": a vertex at " + p + " is outside its collider boxes");
                            if (!any) visual = new Bounds(p, Vector3.zero);
                            else visual.Encapsulate(p);
                            any = true;
                        }
                    }

                    // ... and it fills them: along its two largest dimensions at least 85% of the boxes' extent.
                    Bounds boxes = env.Boxes[piece.FirstBox].Bounds;
                    for (int b = 1; b < piece.BoxCount; b++) boxes.Encapsulate(env.Boxes[piece.FirstBox + b].Bounds);
                    var axes = new List<int> { 0, 1, 2 };
                    axes.Sort((a, b) => boxes.size[b].CompareTo(boxes.size[a]));
                    for (int k = 0; k < 2; k++)
                        Assert.GreaterOrEqual(visual.size[axes[k]], boxes.size[axes[k]] * 0.85f, name + ": the visual is smaller than its colliders along axis " + axes[k]);
                }
            }
            Assert.Greater(pieces, 20);
            foreach (FurnitureKind kind in (FurnitureKind[])Enum.GetValues(typeof(FurnitureKind)))
                Assert.IsTrue(seen.Contains(kind), "no preset placed a " + kind + " around the test level, so its visual was not checked");
        }

        static float DistanceToBoxes(EnvironmentDescriptor env, EnvironmentPiece piece, Vector3 point, bool ignoreAbove)
        {
            float nearest = float.MaxValue;
            for (int b = 0; b < piece.BoxCount; b++)
            {
                EnvironmentBox box = env.Boxes[piece.FirstBox + b];
                Vector3 local = Quaternion.Inverse(box.Rotation) * (point - box.Center);
                Vector3 half = box.Size * 0.5f;
                var outside = new Vector3(Mathf.Max(Mathf.Abs(local.x) - half.x, 0f), Mathf.Max(Mathf.Abs(local.y) - half.y, 0f), Mathf.Max(Mathf.Abs(local.z) - half.z, 0f));
                if (ignoreAbove && local.y > 0f) outside.y = 0f;
                nearest = Mathf.Min(nearest, outside.magnitude);
            }
            return nearest;
        }

        [Test]
        public void FurnitureIsDeterministicWithinBudgetAndShadedAtItsFoot()
        {
            foreach (EnvironmentPreset preset in EnvironmentPreset.All)
            {
                EnvironmentDescriptor env = Solve(preset);
                var first = new RoomBags();
                var second = new RoomBags();
                FurnitureKit.Build(env, first);
                FurnitureKit.Build(env, second);
                int triangles = first.Furniture.TriangleCount + first.Trim.TriangleCount + first.Cardboard.TriangleCount + first.Glow.TriangleCount;
                Assert.Less(triangles, 12000, preset.Key + ": furniture triangles (ART_BIBLE 6.3)");
                Assert.AreEqual(first.Furniture.VertexCount, second.Furniture.VertexCount, preset.Key);
                for (int v = 0; v < first.Furniture.VertexCount; v += 7)
                    Assert.AreEqual(first.Furniture.Position(v), second.Furniture.Position(v), preset.Key + ": the same room twice");

                // Nothing is darker than the contact shade, and something is exactly that dark.
                float darkest = 1f;
                foreach (MeshBag bag in new[] { first.Furniture, first.Cardboard })
                    for (int v = 0; v < bag.VertexCount; v++)
                    {
                        float shade = bag.Color(v).r;
                        if (shade < 0.5f) continue;   // the dark of an open doorway is authored, not shading
                        darkest = Mathf.Min(darkest, shade);
                    }
                Assert.That(darkest, Is.InRange(0.6f, 0.76f), preset.Key + ": the foot of the furniture");
            }

            Assert.AreEqual(2.2f, Bags(EnvironmentPreset.NightLight).GlowGain, 1e-5f, "the night-light glows at 2.2");
            Assert.AreEqual(2f, Bags(EnvironmentPreset.CardboardBox).GlowGain, 1e-5f, "fairy lights glow at 2");
            Assert.IsFalse(Bags(EnvironmentPreset.CardboardBox).Glow.Empty, "24 fairy bulbs");
            Assert.IsFalse(Bags(EnvironmentPreset.CardboardBox).Cardboard.Empty, "box stacks are cardboard");
            Assert.IsTrue(Bags(EnvironmentPreset.SunnyRug).Cardboard.Empty);
        }

        static RoomBags Bags(EnvironmentPreset preset)
        {
            var bags = new RoomBags();
            FurnitureKit.Build(Solve(preset), bags);
            return bags;
        }

        [Test]
        public void StandingFurnitureGetsAHullShadowOnTheSurfaceItStandsOn()
        {
            EnvironmentDescriptor env = Solve(EnvironmentPreset.SunnyRug);
            var shadow = new MeshBag();
            RoomKit.HullShadows(env, shadow);
            Assert.IsFalse(shadow.Empty);

            var bases = new List<float>();
            foreach (EnvironmentPiece piece in env.Pieces)
                if (RoomKit.CastsHull(piece.Kind)) bases.Add(piece.Position.y + RoomKit.HullLift);
            Assert.IsNotEmpty(bases);
            Assert.IsTrue(RoomKit.CastsHull(FurnitureKind.Bed));
            Assert.IsTrue(RoomKit.CastsHull(FurnitureKind.Table), "a table's top casts for its legs");
            Assert.IsFalse(RoomKit.CastsHull(FurnitureKind.TableLegs));
            Assert.IsFalse(RoomKit.CastsHull(FurnitureKind.Curtain), "what hangs on a wall has no shadow on the floor");

            float darkest = 0f, clearest = 1f;
            for (int v = 0; v < shadow.VertexCount; v++)
            {
                Vector3 p = shadow.Position(v);
                bool onASurface = false;
                foreach (float y in bases) onASurface |= Mathf.Abs(p.y - y) < 1e-3f;
                Assert.IsTrue(onASurface, "a hull shadow vertex floats at " + p.y);
                Assert.AreEqual(Vector3.up, shadow.Normal(v));
                darkest = Mathf.Max(darkest, shadow.Color(v).a);
                clearest = Mathf.Min(clearest, shadow.Color(v).a);
            }
            Assert.AreEqual(0.35f, darkest, 1e-5f, "alpha 0.35 inside the hull");
            Assert.AreEqual(0f, clearest, 1e-5f, "feathered to nothing");

            // The bed's shadow reaches away from the sun: further from the window than the bed itself.
            EnvironmentPiece bed = null;
            foreach (EnvironmentPiece piece in env.Pieces)
                if (piece.Kind == FurnitureKind.Bed) bed = piece;
            Assert.IsNotNull(bed);
            Bounds box = env.Boxes[bed.FirstBox].Bounds;
            float reach = float.MinValue;
            for (int v = 0; v < shadow.VertexCount; v++) reach = Mathf.Max(reach, shadow.Position(v).x);
            Assert.Greater(reach, box.max.x + 5f, "the sun is at the -X window, so shadows fall toward +X");

            // A chair's seat floats on its legs: its shadow still lies on the floor under it.
            EnvironmentDescriptor hall = Solve(EnvironmentPreset.BlockHall);
            var hallShadow = new MeshBag();
            RoomKit.HullShadows(hall, hallShadow);
            Assert.IsFalse(hallShadow.Empty);
            for (int v = 0; v < hallShadow.VertexCount; v++)
                Assert.AreEqual(hall.FloorY + RoomKit.HullLift, hallShadow.Position(v).y, 1e-3f, "block-hall's furniture stands on the floor");
        }
    }

    // ----------------------------------------------------------------------------------------------------
    // The room presenter
    // ----------------------------------------------------------------------------------------------------

    public class RoomVisualsTests : RoomFixture
    {
        [Test]
        public void TheRoomPresentersAreLookProvidersInTheirRanges()
        {
            var orders = new Dictionary<Type, int>();
            foreach (PresenterRegistry.Entry entry in PresenterRegistry.All)
            {
                if (Array.IndexOf(RoomPresenters, entry.Type) < 0) continue;
                orders[entry.Type] = entry.Order;
                Assert.IsTrue(entry.ProvidesLook, entry.Type.Name + " must provide the look, so the plain look steps aside");
                Assert.IsFalse(entry.Fallback);
                Assert.IsFalse(entry.ProvidesHud);
            }
            Assert.AreEqual(3, orders.Count, "LightingRig, RoomVisuals and PoolSystem must all be discovered");
            Assert.That(orders[typeof(LightingRig)], Is.InRange(100, 199));
            Assert.That(orders[typeof(RoomVisuals)], Is.InRange(100, 199));
            Assert.That(orders[typeof(PoolSystem)], Is.InRange(200, 299));
            Assert.Less(orders[typeof(LightingRig)], orders[typeof(RoomVisuals)], "the rig is set before the room is built");

            // With the plain look asked for they are left out; without it the plain look is.
            List<PresenterRegistry.Entry> plain = PresenterRegistry.Select(PresenterRegistry.All, true);
            List<PresenterRegistry.Entry> game = PresenterRegistry.Select(PresenterRegistry.All, false);
            foreach (Type type in RoomPresenters)
            {
                Assert.IsFalse(plain.Exists(e => e.Type == type), type.Name + " runs in the plain look");
                Assert.IsTrue(game.Exists(e => e.Type == type), type.Name + " does not run in the game's look");
            }
            Assert.IsFalse(game.Exists(e => e.Type == typeof(PlainLook)), "the plain look still runs beside the room");
        }

        [Test]
        public void EveryPresetBuildsARoomWithinTheDrawAndTriangleBudgets()
        {
            RequireGraphics();
            foreach (EnvironmentPreset preset in EnvironmentPreset.All)
            {
                Load(preset.Key);
                Present(QualityTier.High, RoomPresenters);
                var room = Presentation.Get<RoomVisuals>();
                Assert.IsNotNull(room, preset.Key);
                Assert.IsNotNull(room.Root, preset.Key + ": no room was built");
                string name = preset.Key;

                var names = new List<string>();
                int shell = 0, furniture = 0;
                foreach (Renderer renderer in room.Renderers)
                {
                    names.Add(renderer.name);
                    Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                    Assert.IsNotNull(mesh, name + " " + renderer.name);
                    int triangles = MeshUtil.TriangleCount(mesh);
                    // The Paper trim of shell and furniture is one mesh; it counts with the furniture.
                    if (renderer.name.StartsWith("Shell") || renderer.name == "Island") shell += triangles;
                    if (renderer.name == "Furniture" || renderer.name == "Trim" || renderer.name == "Cardboard" || renderer.name == "Glow") furniture += triangles;
                    Assert.AreEqual(ShadowCastingMode.Off, renderer.shadowCastingMode, name + " " + renderer.name + ": the room only receives");
                    Assert.AreEqual(Layers.Default, renderer.gameObject.layer);
                    string shader = renderer.sharedMaterial.shader.name;
                    Assert.IsTrue(shader.StartsWith("Toybox/"), name + " " + renderer.name + " renders with " + shader);
                }
                // Shell 5, furniture 2 (+ cardboard, + glow), window and sky 3, hull shadows 1 (ART_BIBLE 12.1).
                Assert.LessOrEqual(room.Renderers.Count, 12, name + ": draws: " + string.Join(", ", names));
                CollectionAssert.Contains(names, "Shell Floor", name);
                CollectionAssert.Contains(names, "Shell Walls", name);
                CollectionAssert.Contains(names, "Trim", name);
                CollectionAssert.Contains(names, "Furniture", name);
                CollectionAssert.Contains(names, "Sky", name);
                CollectionAssert.Contains(names, "Light Shaft", name);
                Assert.AreEqual(preset.Island != IslandKind.None, names.Contains("Island"), name + ": island");
                Assert.Less(shell, 2000, name + ": shell triangles");
                Assert.Less(furniture, 12000, name + ": furniture triangles");
                Assert.Less(room.Triangles, 16000, name + ": all triangles");
                Debug.Log("[Toybox] room " + name + ": " + room.Renderers.Count + " draws (" + string.Join(", ", names) + "), " + room.Triangles +
                          " triangles (shell " + shell + ", furniture " + furniture + "), built in " + room.BuildMilliseconds.ToString("0.0") + " ms");

                // Presentation never adds colliders: they would join the simulation.
                Assert.IsEmpty(Presentation.Context.Root.GetComponentsInChildren<Collider>(true), name + ": a collider under the presentation root");
                Assert.IsTrue(room.Root.IsChildOf(Presentation.Context.Root));

                Presentation.Dispose();
                Presentation = null;
                Game.Dispose();
                Game = null;
            }
        }

        [Test]
        public void TheShellMaterialsCarryThePresetsPatternsAndTheDipsTones()
        {
            RequireGraphics();
            Load("pegboard-workbench", null, -9f);
            Present(QualityTier.Medium, RoomPresenters);
            var room = Presentation.Get<RoomVisuals>();
            Dip pool = Palette.Pool;

            Material floor = Find(room, "Shell Floor").sharedMaterial;
            Assert.AreEqual((float)RoomPattern.Tiles, floor.GetFloat("_Pattern"), "the floor far below the bench is tiled");
            AssertVector(Lin(pool.Mid), floor.GetVector("_ColorTop"), 1e-4f, "floor top");
            Assert.AreEqual(1f, floor.GetFloat("_Corner"));

            Material walls = Find(room, "Shell Walls").sharedMaterial;
            Assert.AreEqual((float)RoomPattern.None, walls.GetFloat("_Pattern"), "dado only");
            AssertVector(Lin(pool.Light), walls.GetVector("_ColorSide"), 1e-4f, "wall");
            AssertVector(Lin(pool.Mid), walls.GetVector("_ColorDado"), 1e-4f, "dado");
            Assert.AreEqual(-9f + 30f, walls.GetFloat("_DadoY"), 1e-4f, "the dado line is 30 above the play plane");

            Material back = Find(room, "Shell Back Wall").sharedMaterial;
            Assert.AreEqual((float)RoomPattern.Pegboard, back.GetFloat("_Pattern"), "pegboard on the back wall");
            Assert.AreEqual(-0.25f, back.GetVector("_PatternA").w, 1e-5f, "the holes are dark");

            Material bench = Find(room, "Island").sharedMaterial;
            Assert.AreEqual((float)RoomPattern.Pegboard, bench.GetFloat("_Pattern"), "pegboard on the bench top");
            AssertVector(Lin(pool.Mid), bench.GetVector("_ColorTop"), 1e-4f, "bench top");
            AssertVector(Lin(pool.Deep), bench.GetVector("_ColorSide"), 1e-4f, "bench edge");

            Material trim = Find(room, "Trim").sharedMaterial;
            AssertVector(Lin(Palette.Paper), trim.GetVector("_ColorTop"), 1e-4f, "trim is Paper");
            Material furniture = Find(room, "Furniture").sharedMaterial;
            AssertVector(Lin(pool.Mid), furniture.GetVector("_ColorTop"), 1e-4f, "furniture top");
            AssertVector(Lin(pool.Deep), furniture.GetVector("_ColorSide"), 1e-4f, "furniture side");
            Material cardboard = Find(room, "Cardboard").sharedMaterial;
            Assert.AreEqual((float)RoomPattern.Corrugated, cardboard.GetFloat("_Pattern"));

            Material hull = Find(room, "Hull Shadows").sharedMaterial;
            Assert.AreEqual((float)BlendMode.DstColor, hull.GetFloat("_SrcBlend"), "hull shadows multiply");
            AssertVector(Lin(pool.Deep), hull.GetVector("_Color"), 1e-4f, "toward the deep tone");
            Material shaft = Find(room, "Light Shaft").sharedMaterial;
            Assert.AreEqual((float)BlendMode.One, shaft.GetFloat("_DstBlend"), "the shaft is additive");
            Assert.AreEqual(0.06f, shaft.GetVector("_Color").w, 1e-5f, "alpha 0.06");
        }

        static Renderer Find(RoomVisuals room, string name)
        {
            foreach (Renderer renderer in room.Renderers)
                if (renderer.name == name) return renderer;
            Assert.Fail("the room has no '" + name + "' renderer");
            return null;
        }

        [Test]
        public void TheRoomFollowsTheLevelAndIsGoneWithThePresenter()
        {
            RequireGraphics();
            Load("sunny-rug");
            Present(QualityTier.Medium, RoomPresenters);
            var room = Presentation.Get<RoomVisuals>();
            Transform first = room.Root;
            Assert.IsNotNull(first);
            Mesh firstMesh = room.Renderers[0].GetComponent<MeshFilter>().sharedMesh;
            AssertVector(Lin(Palette.Mint.Mid), Find(room, "Shell Floor").sharedMaterial.GetVector("_ColorTop"), 1e-4f, "Mint");

            Game.LoadLevel(new RoomLevel("cardboard-box"));
            Assert.IsTrue(first == null, "the old room's objects are still there");
            Assert.IsTrue(firstMesh == null, "the old room's meshes leaked");
            Assert.IsNotNull(room.Root);
            AssertVector(Lin(Palette.Peach.Mid), Find(room, "Shell Floor").sharedMaterial.GetVector("_ColorTop"), 1e-4f, "Peach");
            Assert.AreEqual(Game.Scene, room.Root.gameObject.scene, "the room is not in the simulation's scene, where the camera renders");

            // A level without a room has none.
            Game.LoadLevel(new AdHocLevel(ctx => TestHelpers.Floor(ctx)));
            Assert.IsNull(room.Root);
            Assert.AreEqual(0, room.Renderers.Count);
            Assert.AreEqual(0, room.Triangles);

            Game.LoadLevel(new RoomLevel("high-shelf"));
            Transform last = room.Root;
            Assert.IsNotNull(last);
            Presentation.Dispose();
            Presentation = null;
            Assert.IsTrue(last == null, "the room outlived its presenter");
        }

        [Test]
        public void ShaftAndDustAreForMediumAndHighAndFollowTheTier()
        {
            RequireGraphics();
            Load("sunny-rug");
            Present(QualityTier.Low, RoomPresenters);
            var room = Presentation.Get<RoomVisuals>();
            Assert.IsNull(room.Motes, "Low has no dust");
            foreach (Renderer renderer in room.Renderers) Assert.AreNotEqual("Light Shaft", renderer.name, "Low has no shaft");
            int low = room.Renderers.Count;

            Presentation.Context.Quality = QualityTier.Medium;
            Assert.AreEqual(low + 1, room.Renderers.Count, "Medium adds the shaft");
            Assert.IsNotNull(room.Motes);
            Assert.AreEqual(120, room.Motes.main.maxParticles);
            Assert.Greater(room.Motes.particleCount, 60, "the beam is not full of dust from the first frame");
            Assert.AreEqual("Toybox/Flat", room.Motes.GetComponent<ParticleSystemRenderer>().sharedMaterial.shader.name);

            var particles = new ParticleSystem.Particle[room.Motes.particleCount];
            int alive = room.Motes.GetParticles(particles);
            for (int i = 0; i < alive; i++)
                Assert.That(particles[i].startSize, Is.InRange(0.04f - 1e-4f, 0.12f + 1e-4f), "a mote is 0.04 to 0.12 units");
            Presentation.Frame(1f, 1f);
            Assert.Greater(room.Motes.particleCount, 60, "the dust died out");

            Presentation.Context.Quality = QualityTier.High;
            Assert.AreEqual(300, room.Motes.main.maxParticles);
            Assert.AreEqual(low + 1, room.Renderers.Count);

            Presentation.Context.Quality = QualityTier.Low;
            Assert.IsNull(room.Motes);
            Assert.AreEqual(low, room.Renderers.Count, "back on Low the shaft is gone");
        }

        [Test]
        public void ThePlayersShadowIsAFigureThatOnlyCastsAndFollowsThePlayer()
        {
            RequireGraphics();
            Mesh figure = RoomVisuals.FigureMesh();
            try
            {
                Assert.LessOrEqual(MeshUtil.TriangleCount(figure), 300);
                Assert.AreEqual(0f, figure.bounds.min.y, 0.01f, "feet on the origin");
                Assert.AreEqual(Player.BaseHeight, figure.bounds.max.y, 0.01f, "as tall as the player");
            }
            finally
            {
                MeshKit.Release(figure);
            }

            Load("sunny-rug");
            Present(QualityTier.Medium, RoomPresenters);
            var room = Presentation.Get<RoomVisuals>();
            Assert.IsNotNull(room.PlayerShadow);
            Assert.AreEqual(ShadowCastingMode.ShadowsOnly, room.PlayerShadow.shadowCastingMode);
            Assert.IsEmpty(room.PlayerShadow.GetComponentsInChildren<Collider>());

            Game.Player.Teleport(new Vector3(3f, 0.4f, -6f), 70f);
            Presentation.Frame(Sim.Dt, 1f);
            Transform shadow = room.PlayerShadow.transform;
            Assert.Less(Vector3.Distance(Game.Player.Position, shadow.position), 0.01f, "the figure stands where the player stands");
            Assert.AreEqual(70f, shadow.eulerAngles.y, 0.01f, "and turns with the view");

            // It survives a level without a room: the shadow is the player's, not the room's.
            Game.LoadLevel(new AdHocLevel(ctx => TestHelpers.Floor(ctx)));
            Presentation.Frame(Sim.Dt, 1f);
            Assert.IsNotNull(room.PlayerShadow);
            Assert.Less(Vector3.Distance(Game.Player.Position, room.PlayerShadow.transform.position), 0.01f);
        }

        [Test]
        public void ARoomFrameAllocatesNothing()
        {
            RequireGraphics();
            Load("sunny-rug");
            Present(QualityTier.Medium, RoomPresenters);
            var room = Presentation.Get<RoomVisuals>();
            for (int i = 0; i < 3; i++) Presentation.Frame(Sim.Dt, 1f);
            Assert.That(() => room.Frame(Sim.Dt, 1f), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void ThePreviewLevelPutsARegisteredLevelIntoAnotherRoomAndStillSolvesIt()
        {
            var preview = new RoomPreviewLevel(LevelRegistry.Get(0), "block-hall");
            Assert.AreEqual("block-hall", preview.Environment);
            Assert.AreEqual(LevelRegistry.Get(0).GroundY, preview.GroundY);
            Assert.AreEqual(LevelRegistry.Get(0).KillY, preview.KillY);

            Game = Game.Create(new GameOptions());
            Game.LoadLevel(preview);
            Assert.AreSame(EnvironmentPreset.BlockHall, Game.Environment.Preset);
            Assert.AreSame(Palette.Butter, Materials.Dip, "the level's statics are dipped in the room's colour");
            TestHelpers.PlayLevel(Game);
        }
    }

    // ----------------------------------------------------------------------------------------------------
    // Colour pools, the splash ring, first-impact dust
    // ----------------------------------------------------------------------------------------------------

    public class PoolSystemTests : RoomFixture
    {
        PoolSystem pools;

        void Start(QualityTier tier, Action<LevelContext> build, string environment = "sunny-rug")
        {
            Load(environment, build);
            Present(tier, typeof(PoolSystem));
            pools = Presentation.Get<PoolSystem>();
            Assert.IsNotNull(pools);
        }

        static void Ground(LevelContext ctx)
        {
            ctx.AddStatic(BasicToys.Slab(new Vector3(60f, 1f, 60f)), new Vector3(0f, -0.5f, 0f));
            ctx.SetSpawn(Vector3.zero, 0f);
        }

        void Frames(float seconds)
        {
            for (int i = TestHelpers.Ticks(seconds); i > 0; i--)
            {
                Game.Tick();
                Presentation.Frame(Sim.Dt, 1f);
            }
        }

        void Click()
        {
            Input.Once.GrabPressed = true;
            Game.Tick();
        }

        [Test]
        public void ARestingToySitsInAPoolOfItsOwnColourAtItsTrueSize()
        {
            Prop ball = null;
            Start(QualityTier.Medium, ctx =>
            {
                Ground(ctx);
                ball = ctx.AddProp(BasicToys.Ball(0.5f, Palette.Cherry), new Vector3(0f, 1f, 6f), new PropOptions { Scale = 2f });
            });
            Presentation.Frame(0f, 1f);

            Assert.AreEqual(1, pools.Count, "a toy that is there when the level starts has its pool at once");
            Vector4 position = pools.Position(0);
            Assert.Less(Vector3.Distance(ball.Center, position), 1e-4f, "the pool is at the toy's centre");
            Assert.AreEqual(0.8f * ball.Radius, position.w, 1e-4f, "0.8 of the true radius");
            Assert.Greater(position.w, 0.79f, "a ball of radius 0.5 at scale 2 is at least 1 in radius");
            Vector4 tint = pools.Tint(0);
            Color cherry = ToyRecipe.GlossyPlastic.PoolColor(Palette.Cherry);
            AssertVector(new Vector4(cherry.r, cherry.g, cherry.b), tint, 1e-4f, "the toy's linear base colour");
            AssertVector(Lin(Palette.Cherry), tint, 0.02f, "... which for glossy plastic is its candy colour");
            Assert.AreEqual(1f, tint.w, 1e-4f, "strength");

            Assert.AreEqual(1f, Shader.GetGlobalFloat("_PoolCount"));
            Assert.AreEqual(position, Shader.GetGlobalVectorArray("_PoolPos")[0]);
            Assert.AreEqual(tint, Shader.GetGlobalVectorArray("_PoolTint")[0]);
            Assert.AreEqual(16, Shader.GetGlobalVectorArray("_PoolPos").Length, "the shader's arrays are 16 long");
            Assert.AreEqual(0.5f, Shader.GetGlobalFloat("_PoolGain"), 1e-5f, "sunny-rug's pool gain");
            Assert.AreEqual(0.5f, pools.Gain, 1e-5f);
        }

        [Test]
        public void ATinyToyStillHasAPoolOfTheMinimumRadius()
        {
            Start(QualityTier.Medium, ctx =>
            {
                Ground(ctx);
                ctx.AddProp(BasicToys.Ball(0.5f), new Vector3(0f, 0.04f, 4f), new PropOptions { Scale = 0.08f });
            });
            Presentation.Frame(0f, 1f);
            Assert.AreEqual(1, pools.Count);
            Assert.AreEqual(0.08f, pools.Position(0).w, 1e-5f, "0.8 x 0.04 would be invisible");
        }

        [Test]
        public void OnlyToysThatCanBeLiftedHavePools()
        {
            Prop fixedBlock = null, plain = null, toy = null;
            Start(QualityTier.High, ctx =>
            {
                Ground(ctx);
                fixedBlock = ctx.AddProp(BasicToys.Block(1f, Palette.Lime), new Vector3(-4f, 0.5f, 6f), new PropOptions { Grabbable = false });
                GameObject crate = BasicToys.Block(1f, Palette.Birch);
                ToyInfo.Tag(crate, ToyRecipe.PlainProp, Palette.Birch);
                plain = ctx.AddProp(crate, new Vector3(0f, 0.5f, 6f));
                toy = ctx.AddProp(BasicToys.Block(1f, Palette.Grape), new Vector3(4f, 0.5f, 6f));
            });
            Presentation.Frame(0f, 1f);
            Assert.AreEqual(1, pools.Count, "a barricade block and a plain prop have no pool");
            Assert.Less(Vector3.Distance(toy.Center, pools.Position(0)), 1e-3f);

            // When a toy stops being liftable its pool fades out; when it is liftable again it fades back in.
            toy.Grabbable = false;
            Presentation.Frame(0.1f, 1f);
            Assert.AreEqual(1, pools.Count);
            Assert.AreEqual(0.5f, pools.Tint(0).w, 1e-3f, "half gone after 100 of 200 ms");
            Presentation.Frame(0.11f, 1f);
            Assert.AreEqual(0, pools.Count);
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_PoolCount"));
            toy.Grabbable = true;
            Presentation.Frame(0.05f, 1f);
            Assert.AreEqual(0.25f, pools.Tint(0).w, 1e-3f, "fading in over 200 ms");
        }

        [Test]
        public void TheTierLimitsThePoolsToTheOnesThatMatterMostFromHere()
        {
            Assert.AreEqual(6, PoolSystem.PoolsFor(QualityTier.Low));
            Assert.AreEqual(10, PoolSystem.PoolsFor(QualityTier.Medium));
            Assert.AreEqual(16, PoolSystem.PoolsFor(QualityTier.High));

            var balls = new List<Prop>();
            Start(QualityTier.Low, ctx =>
            {
                Ground(ctx);
                // Twelve equal balls in a row leading away from the player.
                for (int i = 0; i < 12; i++) balls.Add(ctx.AddProp(BasicToys.Ball(0.3f), new Vector3(0f, 0.3f, 3f + i * 2f)));
            });
            Presentation.Frame(0f, 1f);
            Assert.AreEqual(6, pools.Count, "Low draws six pools");
            for (int i = 0; i < 6; i++)
            {
                bool found = false;
                for (int k = 0; k < pools.Count; k++) found |= Vector3.Distance(balls[i].Center, pools.Position(k)) < 1e-3f;
                Assert.IsTrue(found, "ball " + i + " is among the six nearest and has no pool");
            }

            Presentation.Context.Quality = QualityTier.Medium;
            Presentation.Frame(0.3f, 1f);
            Assert.AreEqual(10, pools.Count, "Medium draws ten");
            Presentation.Context.Quality = QualityTier.High;
            Presentation.Frame(0.3f, 1f);
            Assert.AreEqual(12, pools.Count, "High draws all twelve");

            // The player walks to the far end: the set changes, and the pools left behind fade rather than pop.
            Presentation.Context.Quality = QualityTier.Low;
            Presentation.Frame(0.3f, 1f);
            Assert.AreEqual(6, pools.Count);
            Game.Player.Teleport(new Vector3(0f, 0f, 30f), 180f);
            Presentation.Frame(0.1f, 1f);
            Assert.AreEqual(12, pools.Count, "six fading in, six fading out");
            Presentation.Frame(0.11f, 1f);
            Assert.AreEqual(6, pools.Count);
            bool nearest = false;
            for (int k = 0; k < pools.Count; k++) nearest |= Vector3.Distance(balls[11].Center, pools.Position(k)) < 1e-3f;
            Assert.IsTrue(nearest, "the ball beside the player has no pool");
        }

        [Test]
        public void LongToysUseTheProxySpheresTheirCatalogSupplies()
        {
            Prop plank = null;
            Start(QualityTier.Medium, ctx =>
            {
                Ground(ctx);
                GameObject toy = BasicToys.Block(new Vector3(6f, 0.4f, 1f), Palette.Tangerine);
                ToyInfo.Tag(toy, ToyRecipe.GlossyPlastic, Palette.Tangerine, new Vector4(-2f, 0f, 0f, 0.5f), new Vector4(0f, 0f, 0f, 0.5f), new Vector4(2f, 0f, 0f, 0.5f));
                plank = ctx.AddProp(toy, new Vector3(0f, 0.4f, 6f), Quaternion.Euler(0f, 90f, 0f), new PropOptions { Scale = 2f });
            });
            Presentation.Frame(0f, 1f);
            Assert.AreEqual(3, pools.Count);
            var expected = new[] { plank.Transform.TransformPoint(new Vector3(-2f, 0f, 0f)), plank.Center, plank.Transform.TransformPoint(new Vector3(2f, 0f, 0f)) };
            foreach (Vector3 centre in expected)
            {
                bool found = false;
                for (int k = 0; k < pools.Count; k++)
                {
                    if (Vector3.Distance(centre, pools.Position(k)) > 1e-3f) continue;
                    found = true;
                    Assert.AreEqual(1f, pools.Position(k).w, 1e-4f, "the proxy's radius times the toy's scale");
                }
                Assert.IsTrue(found, "no pool at the proxy sphere " + centre);
            }
            Assert.AreEqual(8f, Vector3.Distance(expected[0], expected[2]), 1e-3f, "the proxies are in the toy's own space, scaled and turned with it");
        }

        [Test]
        public void GlassPoolsBrighterAndHighVisibilityRaisesTheGain()
        {
            Start(QualityTier.Medium, ctx =>
            {
                Ground(ctx);
                GameObject marble = BasicToys.Ball(0.4f, Palette.Lagoon);
                ToyInfo.Tag(marble, ToyRecipe.Glass, Palette.Lagoon);
                ctx.AddProp(marble, new Vector3(0f, 0.4f, 5f));
            }, "night-light");
            Presentation.Frame(0f, 1f);
            Color expected = ToyRecipe.Glass.PoolColor(Palette.Lagoon);
            AssertVector(new Vector4(expected.r, expected.g, expected.b), pools.Tint(0), 1e-4f, "glass pools at 1.6 of its base colour: a caustic");
            Assert.AreEqual(0.9f, pools.Gain, 1e-5f, "at night the pools are the light on the floor");

            Settings.HighVisibility = true;
            Presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(0.9f * 1.5f, pools.Gain, 1e-5f, "high-visibility toys: pool gain x 1.5");
            Assert.AreEqual(0.9f * 1.5f, Shader.GetGlobalFloat("_PoolGain"), 1e-5f);
        }

        [Test]
        public void FloodCurveRisesToOneAndAQuarterAndSettlesAtOne()
        {
            Assert.AreEqual(0f, PoolSystem.Flood(0f));
            Assert.That(PoolSystem.Flood(0.07f), Is.InRange(0.3f, 1.0f));
            Assert.AreEqual(1.25f, PoolSystem.Flood(0.14f), 1e-4f);
            Assert.That(PoolSystem.Flood(0.26f), Is.InRange(1.0f, 1.25f));
            Assert.AreEqual(1f, PoolSystem.Flood(0.38f), 1e-4f);
            Assert.AreEqual(1f, PoolSystem.Flood(5f));
            for (int i = 0; i < 13; i++) Assert.LessOrEqual(PoolSystem.Flood(i * 0.01f), PoolSystem.Flood((i + 1) * 0.01f) + 1e-5f, "rising to the peak");
            for (int i = 14; i < 40; i++) Assert.GreaterOrEqual(PoolSystem.Flood(i * 0.01f) + 1e-5f, PoolSystem.Flood((i + 1) * 0.01f), "settling from the peak");
        }

        [Test]
        public void AGrabLeavesThePoolBehindToDrainAndAReleaseFloodsItAtTheNewSize()
        {
            Prop ball = null;
            Start(QualityTier.Medium, ctx =>
            {
                Ground(ctx);
                ctx.AddStatic(BasicToys.Slab(new Vector3(60f, 20f, 1f)), new Vector3(0f, 10f, 30.5f));
                ball = ctx.AddProp(BasicToys.Ball(0.4f, Palette.Lagoon), new Vector3(0f, 0.4f, 4f));
            });
            Frames(0.3f);
            Assert.AreEqual(1, pools.Count);
            Vector4 resting = pools.Position(0);
            Assert.Less(Vector3.Distance(ball.Center, resting), 1e-3f);

            // Grab: looking a little above the ball's far side carries it out to the wall, much bigger.
            TestHelpers.LookAt(Game.Player, ball.Center);
            Click();
            Assert.IsTrue(ball.Held);
            Presentation.Frame(0.03f, 1f);
            Assert.AreEqual(1, pools.Count, "the pool is still there, draining");
            Assert.AreEqual(resting, pools.Position(0), "the pool stays where the toy was grabbed; it does not follow the held toy");
            Assert.AreEqual(0.75f, pools.Tint(0).w, 1e-3f, "30 of 120 ms drained");
            Presentation.Frame(0.1f, 1f);
            Assert.AreEqual(0, pools.Count, "drained after 120 ms; a held toy has no pool");
            Assert.IsFalse(pools.SplashActive);

            Game.Player.Yaw = 0f;
            Game.Player.Pitch = 12f;
            for (int i = 0; i < 20; i++)
            {
                Game.Tick();
                Presentation.Frame(Sim.Dt, 1f);
                Assert.AreEqual(0, pools.Count, "a pool under the held toy would give its distance away");
            }
            float grabbedScale = ball.Scale;
            Assert.Greater(grabbedScale, 3f, "the test's hold did not enlarge the ball");

            // Release, and look away: a toy under the crosshair has its pool brightened by a fifth (focus).
            Click();
            Assert.IsFalse(ball.Held);
            Game.Player.Yaw = 150f;
            float radius = ball.Radius;
            Presentation.Frame(0.01f, 1f);
            Assert.AreEqual(1, pools.Count, "the pool floods on the frame of the release");
            Assert.AreEqual(0.8f * radius, pools.Position(0).w, 1e-3f, "at the toy's true radius: a toy several times bigger floods a pool as much wider");
            Assert.Less(pools.Tint(0).w, 0.2f, "the flood starts from nothing");
            Assert.IsTrue(pools.SplashActive);
            Assert.That(pools.SplashRingRadius, Is.InRange(radius, radius * 1.1f), "the ring starts at the toy's bounding radius");
            Assert.That(pools.SplashRingStrength, Is.InRange(0.55f, 0.6f));
            Vector4 splash = Shader.GetGlobalVector("_Splash"), splashTint = Shader.GetGlobalVector("_SplashTint");
            Assert.AreEqual(radius, splash.w, 1e-3f, "_Splash.w is the prop's radius");
            Assert.AreEqual(pools.SplashRingRadius, splashTint.w, 1e-4f, "_SplashTint.a is the ring's radius now");
            Assert.AreEqual(Palette.Lin(Palette.Lagoon).b * pools.SplashRingStrength, splashTint.z, 1e-3f, "the ring is in the toy's colour");

            Presentation.Frame(0.13f, 1f);
            Assert.AreEqual(1.25f, pools.Tint(0).w, 0.02f, "the flood peaks at 1.25 after 140 ms");
            Assert.That(pools.SplashRingRadius, Is.InRange(radius * 1.7f, radius * 1.9f), "the ring is on its way to three radii");
            Presentation.Frame(0.25f, 1f);
            Assert.AreEqual(1f, pools.Tint(0).w, 1e-3f, "and settles at 1 by 380 ms");
            Assert.IsFalse(pools.SplashActive, "the ring is over after 350 ms");
            Assert.AreEqual(Vector4.zero, Shader.GetGlobalVector("_SplashTint"));
        }

        [Test]
        public void TheFirstImpactAfterAReleaseRaisesEightDustDiscsAndASecondRing()
        {
            Prop ball = null;
            int impacts = 0;
            Start(QualityTier.Medium, ctx =>
            {
                Ground(ctx);
                ctx.AddStatic(BasicToys.Slab(new Vector3(60f, 20f, 1f)), new Vector3(0f, 10f, 12.5f));
                ball = ctx.AddProp(BasicToys.Ball(0.4f, Palette.Bubblegum), new Vector3(0f, 0.4f, 4f), new PropOptions { Bounciness = 0f });
            });
            Game.Events.PropImpact += e => { if (e.Prop == ball) impacts++; };
            Frames(0.2f);
            Assert.AreEqual(0, pools.DustRaised, "a toy that just lies there raises no dust");

            // Carry the ball up the wall and let go: it drops a few units.
            TestHelpers.LookAt(Game.Player, ball.Center);
            Click();
            Game.Player.Yaw = 0f;
            Game.Player.Pitch = 25f;
            Frames(0.2f);
            Assert.Greater(ball.Center.y, 3f);
            Click();
            Presentation.Frame(Sim.Dt, 1f);

            int ticks = 0;
            while (impacts == 0 && ticks++ < 240)
            {
                Game.Tick();
                Presentation.Frame(Sim.Dt, 1f);
            }
            Assert.Greater(impacts, 0, "the dropped ball never hit anything");
            Assert.AreEqual(8, pools.DustRaised, "a ring of eight discs");
            Assert.IsTrue(pools.SplashActive, "a second ring");
            Assert.LessOrEqual(pools.SplashRingStrength, 0.3f + 1e-4f, "weaker than the first: 0.3");

            // Later impacts of the same drop raise nothing more.
            Frames(2f);
            Assert.AreEqual(8, pools.DustRaised);
        }

        [Test]
        public void PoolsAreForgottenWithTheLevel()
        {
            Start(QualityTier.Medium, ctx =>
            {
                Ground(ctx);
                ctx.AddProp(BasicToys.Ball(0.4f), new Vector3(0f, 0.4f, 4f));
            });
            Presentation.Frame(0f, 1f);
            Assert.AreEqual(1, pools.Count);
            pools.Splash(Vector3.zero, 1f, Color.white, 0.6f);

            Game.LoadLevel(new RoomLevel("sunny-rug", Ground));
            Presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(0, pools.Count);
            Assert.IsFalse(pools.SplashActive);
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_PoolCount"));

            Presentation.Dispose();
            Presentation = null;
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_PoolCount"), "pools stayed behind in the shader");
        }

        [Test]
        public void APoolFrameAllocatesNothing()
        {
            Start(QualityTier.Medium, ctx =>
            {
                Ground(ctx);
                for (int i = 0; i < 14; i++) ctx.AddProp(BasicToys.Ball(0.3f), new Vector3(-6f + i, 0.3f, 5f));
            });
            for (int i = 0; i < 5; i++) Presentation.Frame(Sim.Dt, 1f);
            Assert.That(() => pools.Frame(Sim.Dt, 1f), UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
        }
    }

    // ----------------------------------------------------------------------------------------------------
    // What the shaders actually draw
    // ----------------------------------------------------------------------------------------------------

    public class RoomPictureTests : RoomFixture
    {
        const int Width = 480, Height = 270;
        const float Tolerance = 5f / 255f;

        // Renders the level with the room's presenters only (no post-processing: the picture is the
        // shaders' linear output, encoded to sRGB) and returns it; the view is the level's spawn.
        static Texture2D Shoot(LevelDefinition level, QualityTier tier = QualityTier.Low)
        {
            string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "ToyboxRoomTests");
            List<string> files = Shots.Run(new ShotRequest
            {
                Level = 0, Definition = level, Width = Width, Height = Height, OutputDirectory = directory, Quality = tier, Presenters = Only(RoomPresenters),
            });
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.IsTrue(image.LoadImage(File.ReadAllBytes(files[0])));
            Assert.AreEqual(Width, image.width);
            return image;
        }

        // The pixel a world point lands on, for a player whose feet are at `feet`.
        static Color At(Texture2D image, Vector3 world, Vector3 feet, float yaw, float pitch)
        {
            Vector3 eye = feet + Vector3.up * Player.BaseEyeHeight;
            Vector3 view = Quaternion.Inverse(Quaternion.Euler(-pitch, yaw, 0f)) * (world - eye);
            Assert.Greater(view.z, 0.1f, "the point " + world + " is behind the camera");
            float tan = Mathf.Tan(Settings.FieldOfView * 0.5f * Mathf.Deg2Rad);
            float x = view.x / (view.z * tan * Width / Height), y = view.y / (view.z * tan);
            Assert.That(Mathf.Abs(x), Is.LessThan(0.98f), "the point " + world + " is out of the picture");
            Assert.That(Mathf.Abs(y), Is.LessThan(0.98f), "the point " + world + " is out of the picture");
            return image.GetPixel(Mathf.Clamp(Mathf.FloorToInt((x * 0.5f + 0.5f) * Width), 0, Width - 1), Mathf.Clamp(Mathf.FloorToInt((y * 0.5f + 0.5f) * Height), 0, Height - 1));
        }

        static Vector3 Rgb(Vector4 v) => new Vector3(v.x, v.y, v.z);
        static Vector3 Rgb(Color c) => new Vector3(c.r, c.g, c.b);

        // Toybox/RoomLit's lighting for a surface without pattern, pool or corner gradient (linear).
        static Vector3 Lit(LightingValues v, Color albedoSrgb, Vector3 normal, Vector3 sunDirection, float shadow, float patch, Vector3 point, Vector3 eye)
        {
            Vector3 albedo = Rgb(Palette.Lin(albedoSrgb));
            Vector3 sun = Rgb(Palette.Lin(v.SunColor)) * v.SunIntensity;
            float lit = Mathf.Clamp01(Vector3.Dot(normal, sunDirection)) * shadow;
            Vector3 tri = normal.y >= 0f ? Vector3.Lerp(Rgb(v.AmbEquator), Rgb(v.AmbSky), normal.y) : Vector3.Lerp(Rgb(v.AmbEquator), Rgb(v.AmbGround), -normal.y);
            Vector3 light = sun * lit + Rgb(v.KickColor) * Mathf.Clamp01(Vector3.Dot(normal, Rgb(v.KickDir))) + tri;
            Vector3 c = Vector3.Scale(albedo, light) + Vector3.Scale(albedo, Rgb(v.PatchColor)) * (patch * lit);
            return Hazed(v, c, point, eye);
        }

        // Toybox/RoomLit's window patch: how much of the four panes' light reaches a point.
        static float Patch(LightingValues v, Vector3 point, Vector3 sunDirection)
        {
            float facing = Vector3.Dot(sunDirection, Rgb(v.WinN));
            if (facing > -1e-4f) return 0f;
            float t = Vector3.Dot(Rgb(v.WinO) - point, Rgb(v.WinN)) / facing;
            Vector3 q = point + sunDirection * t - Rgb(v.WinO);
            var p = new Vector2(Vector3.Dot(q, Rgb(v.WinU)), Vector3.Dot(q, Rgb(v.WinV)));
            float soft = 0.012f + 0.0006f * t;
            float qx = Mathf.Abs(Mathf.Abs(p.x) - 0.5f) - (0.5f - v.WinMullion), qy = Mathf.Abs(Mathf.Abs(p.y) - 0.5f) - (0.5f - v.WinMullion);
            float edge = Mathf.Max(qx, qy);
            return t < 0f ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(soft, -soft, edge));
        }

        static Vector3 Hazed(LightingValues v, Vector3 c, Vector3 point, Vector3 eye)
        {
            float distance = Vector3.Distance(point, eye);
            float fd = 1f - Mathf.Exp(-Mathf.Pow(distance * v.HazeParams.x, 2f));
            float fh = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(v.HazeParams.y, v.HazeParams.z, point.y)) * v.HazeParams.w * Mathf.Clamp01(distance / 120f);
            return Vector3.Lerp(c, Rgb(v.HazeColor), 1f - (1f - fd) * (1f - fh));
        }

        static void AssertColour(Vector3 expectedLinear, Color actual, string what)
        {
            Color expected = Palette.Srgb(new Color(expectedLinear.x, expectedLinear.y, expectedLinear.z));
            string message = what + ": expected " + Palette.ToHex(expected) + ", drawn " + Palette.ToHex(actual);
            Assert.AreEqual(expected.r, actual.r, Tolerance, message);
            Assert.AreEqual(expected.g, actual.g, Tolerance, message);
            Assert.AreEqual(expected.b, actual.b, Tolerance, message);
        }

        // The nearest crossing of the rug's dot grid: as far from every dot as a point can be.
        static Vector3 BetweenDots(Vector3 p) => new Vector3(Mathf.Round(p.x / 8f) * 8f, p.y, Mathf.Round(p.z / 8f) * 8f);

        [Test]
        public void TheRugIsDrawnAsTheFormulaSaysInSunInShadowAndInThePatch()
        {
            RequireGraphics();
            // A pillar on the rug, seen from straight above.
            var feet = new Vector3(0f, 58f, 0f);
            var level = new RoomLevel("sunny-rug", ctx =>
            {
                ctx.AddStatic(BasicToys.Slab(new Vector3(4f, 12f, 4f)), new Vector3(0f, 6f, 0f));
                ctx.SetSpawn(feet, 0f, -89f);
            });
            EnvironmentDescriptor env = EnvironmentSolver.Solve(EnvironmentPreset.SunnyRug, new Bounds(new Vector3(0f, 6f, 0f), new Vector3(4f, 12f, 4f)), 0f);
            LightingValues v = LightingValues.For(env);
            Vector3 s = env.SunDirection, eye = feet + Vector3.up * Player.BaseEyeHeight;
            Color mid = Palette.Mint.Mid;

            Texture2D image = Shoot(level);
            try
            {
                // In the sun, outside the patch: beyond the far end of the panes.
                Vector3 sunny = BetweenDots(new Vector3(-56f, 0f, -24f));
                Assert.AreEqual(0f, Patch(v, sunny, s), "the test's sunny point is in the patch");
                Vector3 expectedSunny = Lit(v, mid, Vector3.up, s, 1f, 0f, sunny, eye);
                AssertColour(expectedSunny, At(image, sunny, feet, 0f, -89f), "the rug in the sun");

                // In the middle of a pane of the patch.
                Vector3 onWindow = env.WindowCenter + env.WindowTangent * 17.5f + Vector3.up * 26.25f;
                Vector3 pane = BetweenDots(onWindow - s * ((onWindow.y - env.GroundY) / s.y));
                Assert.AreEqual(1f, Patch(v, pane, s), "the test's pane point is not in a pane");
                Vector3 expectedPane = Lit(v, mid, Vector3.up, s, 1f, 1f, pane, eye);
                AssertColour(expectedPane, At(image, pane, feet, 0f, -89f), "the rug in a pane of the window patch");
                Assert.Greater(expectedPane.x, expectedSunny.x * 1.1f, "the patch is light");

                // Under the mullion the patch dims again - not to nothing: after this throw the bar's shadow is
                // all penumbra. The level's focus is on the cross; this is on the bar beside the pillar.
                Vector3 horizontal = new Vector3(s.x, 0f, s.z).normalized;
                Vector3 cross = env.Focus + env.WindowTangent * 9f;
                float underBar = Patch(v, cross, s);
                Assert.That(underBar, Is.InRange(0.01f, 0.5f), "the mullion's shadow");
                AssertColour(Lit(v, mid, Vector3.up, s, 1f, underBar, cross, eye), At(image, cross, feet, 0f, -89f), "the rug under the mullion");

                // In the pillar's shadow, 7 units from it, away from the sun.
                Vector3 shaded = -horizontal * 7f;
                Vector3 expectedShade = Lit(v, mid, Vector3.up, s, 0f, 1f, shaded, eye);
                Color drawnShade = At(image, shaded, feet, 0f, -89f);
                AssertColour(expectedShade, drawnShade, "the rug in the pillar's shadow");

                // Calibration (ART_BIBLE 5.1). 1: with the preset's exposure the sunlit mid floor shows its authored hex within 4%.
                Color drawnSunny = Palette.Lin(At(image, sunny, feet, 0f, -89f));
                float exposure = Mathf.Pow(2f, EnvironmentPreset.SunnyRug.Exposure);
                Color shown = Palette.Srgb(new Color(drawnSunny.r * exposure, drawnSunny.g * exposure, drawnSunny.b * exposure));
                Assert.AreEqual(mid.r, shown.r, 0.04f, "sunlit mid floor, red, at +0.2 EV");
                Assert.AreEqual(mid.g, shown.g, 0.04f, "sunlit mid floor, green, at +0.2 EV");
                Assert.AreEqual(mid.b, shown.b, 0.04f, "sunlit mid floor, blue, at +0.2 EV");
                // 2: lit to shadow, by luminance.
                Color linearShade = Palette.Lin(drawnShade);
                float ratio = Luminance(drawnSunny) / Luminance(linearShade);
                Assert.That(ratio, Is.InRange(1.7f, 2.0f), "lit-to-shadow luminance: airy, never dramatic");
                // 3: the shadow has the floor's hue, not a grey or a black.
                Color.RGBToHSV(mid, out float floorHue, out _, out _);
                Color.RGBToHSV(drawnShade, out float shadeHue, out float shadeSaturation, out _);
                Assert.Less(Mathf.Abs(Mathf.DeltaAngle(floorHue * 360f, shadeHue * 360f)), 5f, "a shadow is a deeper tint of the room's hue");
                Assert.Greater(shadeSaturation, 0.15f, "a shadow is not grey");
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }

        static float Luminance(Color linear) => 0.2126f * linear.r + 0.7152f * linear.g + 0.0722f * linear.b;

        [Test]
        public void HazeDissolvesTheCeilingAndTheFarWall()
        {
            RequireGraphics();
            var feet = new Vector3(0f, 0.4f, 0f);
            var level = new RoomLevel("sunny-rug", ctx =>
            {
                ctx.AddStatic(BasicToys.Slab(new Vector3(8f, 0.4f, 8f)), new Vector3(0f, 0.2f, 0f));
                ctx.SetSpawn(feet, 180f, 80f);
            });
            EnvironmentDescriptor env = EnvironmentSolver.Solve(EnvironmentPreset.SunnyRug, new Bounds(new Vector3(0f, 0.2f, 0f), new Vector3(8f, 0.4f, 8f)), 0f);
            LightingValues v = LightingValues.For(env);
            Vector3 eye = feet + Vector3.up * Player.BaseEyeHeight;

            Texture2D image = Shoot(level);
            try
            {
                // Straight overhead, far from every wall: light tone, lit from below by the ground ambient, half dissolved.
                var above = new Vector3(0f, env.ShellMax.y, -20f);
                Vector3 plain = Lit(v, Palette.Mint.Light, Vector3.down, env.SunDirection, 1f, 0f, above, above);
                Vector3 expected = Hazed(v, plain, above, eye);
                Color drawn = At(image, above, feet, 180f, 80f);
                AssertColour(expected, drawn, "the ceiling");
                float haze = 1f - Vector3.Distance(expected, Rgb(v.HazeColor)) / Vector3.Distance(plain, Rgb(v.HazeColor));
                Assert.That(haze, Is.InRange(0.5f, 0.75f), "height haze takes half of the ceiling, distance haze some more");
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }

        [Test]
        public void FlatDrawsItsColourUnlitAndBlendsByItsPreset()
        {
            RequireGraphics();
            var feet = new Vector3(0f, 0.4f, 0f);
            // Four cards 6 ahead on a grey backdrop: opaque, alpha, additive, multiply.
            var grey = new Color(0.5f, 0.5f, 0.5f, 1f);
            var ink = new Color(0.2f, 0.6f, 0.4f, 0.5f);
            var level = new RoomLevel("none", ctx =>
            {
                ctx.AddStatic(BasicToys.Slab(new Vector3(8f, 0.4f, 8f)), new Vector3(0f, 0.2f, 0f));
                ctx.SetSpawn(feet, 0f, 0f);
                Card(ctx, new Vector3(0f, 1.95f, 8f), new Vector2(14f, 8f), Materials.Flat(new FlatRecipe { Name = "Test Backdrop", Color = grey }));
                Card(ctx, new Vector3(-3f, 1.95f, 6f), new Vector2(1.5f, 1.5f), Materials.Flat(new FlatRecipe { Name = "Test Opaque", Color = new Color(0.9f, 0.3f, 0.1f, 1f) }));
                Card(ctx, new Vector3(-1f, 1.95f, 6f), new Vector2(1.5f, 1.5f), Materials.Flat(new FlatRecipe { Name = "Test Alpha", Color = ink, Blend = FlatBlend.Alpha }));
                Card(ctx, new Vector3(1f, 1.95f, 6f), new Vector2(1.5f, 1.5f), Materials.Flat(new FlatRecipe { Name = "Test Additive", Color = ink, Blend = FlatBlend.Additive }));
                Card(ctx, new Vector3(3f, 1.95f, 6f), new Vector2(1.5f, 1.5f), Materials.Flat(new FlatRecipe { Name = "Test Multiply", Color = ink, Blend = FlatBlend.Multiply }));
                // A disc: its corners are cut away, its middle is there.
                Card(ctx, new Vector3(0f, 3.6f, 6f), new Vector2(1.5f, 1.5f), Materials.Flat(new FlatRecipe { Name = "Test Disc", Color = new Color(0.1f, 0.1f, 0.9f, 1f), Shape = FlatShape.SoftDisc, Blend = FlatBlend.Alpha }));
            });

            Texture2D image = Shoot(level);
            try
            {
                Vector3 g = Rgb(grey), c = Rgb(ink);
                AssertColour(g, At(image, new Vector3(-5.6f, 1.95f, 8f), feet, 0f, 0f), "the backdrop: unlit, exactly its colour");
                AssertColour(new Vector3(0.9f, 0.3f, 0.1f), At(image, new Vector3(-3f, 1.95f, 6f), feet, 0f, 0f), "opaque");
                AssertColour(Vector3.Lerp(g, c, 0.5f), At(image, new Vector3(-1f, 1.95f, 6f), feet, 0f, 0f), "alpha: half the colour over the backdrop");
                AssertColour(g + c * 0.5f, At(image, new Vector3(1f, 1.95f, 6f), feet, 0f, 0f), "additive: colour x alpha added");
                AssertColour(Vector3.Scale(g, Vector3.Lerp(Vector3.one, c, 0.5f)), At(image, new Vector3(3f, 1.95f, 6f), feet, 0f, 0f), "multiply: toward the colour by alpha");
                AssertColour(new Vector3(0.1f, 0.1f, 0.9f), At(image, new Vector3(0f, 3.6f, 6f), feet, 0f, 0f), "the middle of a disc");
                AssertColour(g, At(image, new Vector3(0.68f, 4.28f, 6f), feet, 0f, 0f), "the corner of a disc's quad is cut away");
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }

        // A quad facing the player (-Z), with unit UVs; no collider.
        static void Card(LevelContext ctx, Vector3 centre, Vector2 size, Material material)
        {
            var bag = new MeshBag();
            Vector3 x = Vector3.right * size.x * 0.5f, y = Vector3.up * size.y * 0.5f;
            bag.Quad(-x - y, x - y, x + y, -x + y, Vector3.back);
            var card = new GameObject("Card");
            card.AddComponent<MeshFilter>().sharedMesh = bag.Build("Card");
            card.AddComponent<MeshRenderer>().sharedMaterial = material;
            ctx.AddStatic(card, centre);
        }

        [Test]
        public void AToysPoolDarkensItsContactAndBouncesItsColour()
        {
            RequireGraphics();
            var feet = new Vector3(0f, 18f, 0f);
            Vector3 ball = new Vector3(0f, 1f, 0f);
            var level = new RoomLevel("none", ctx =>
            {
                ctx.AddStatic(BasicToys.Slab(new Vector3(30f, 1f, 30f)), new Vector3(0f, -0.5f, 0f));
                ctx.SetSpawn(feet, 0f, -89f);
                ctx.AddProp(BasicToys.Ball(1f, Palette.Cherry), ball, new PropOptions { Body = PropBody.Fixed, Grabbable = true });
            });
            EnvironmentDescriptor env = EnvironmentSolver.Solve(EnvironmentPreset.None, new Bounds(new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 30f)), 0f);
            LightingValues v = LightingValues.For(env);
            Vector3 s = env.SunDirection, eye = feet + Vector3.up * Player.BaseEyeHeight;
            Color light = Palette.Mint.Light;

            Texture2D image = Shoot(level);
            try
            {
                // Toward the sun from the ball, so the ball's own shadow is out of the way.
                Vector3 toSun = new Vector3(s.x, 0f, s.z).normalized;
                Vector3 far = toSun * 12f, near = toSun * 1.35f;
                AssertColour(Lit(v, light, Vector3.up, s, 1f, 0f, far, eye), At(image, far, feet, 0f, -89f), "the floor far from any toy");

                // The pool of ART_BIBLE 3.3 as the shader draws it, by hand: radius 0.8, strength 1, gain 0.5;
                // the halo with the square root of the facing term, capped, and partly in the toy's own hue.
                float r = 0.8f;
                Vector3 dv = ball - near;
                float l2 = dv.sqrMagnitude, r2 = r * r;
                float reach = Mathf.Clamp01((36f - l2 / r2) / 20f);
                float nl = Mathf.Max(Vector3.Dot(Vector3.up, dv) / Mathf.Sqrt(l2), 0f) * reach;
                float core = nl * r2 / Mathf.Max(l2, r2);
                float halo = Mathf.Sqrt(nl) * 6.25f * r2 / (l2 + 6.25f * r2);
                Vector3 albedo = Rgb(Palette.Lin(light)) * (1f - 0.65f * core);
                Vector3 glow = Rgb(Palette.Lin(Palette.Cherry)) * (halo * (1f - core));
                glow /= Mathf.Max(1f, Mathf.Max(glow.x, Mathf.Max(glow.y, glow.z)) / PoolSystem.HaloCap);
                Vector3 poolAlbedo = Vector3.Lerp(albedo, Vector3.one, PoolSystem.OwnHue);
                Vector3 sun = Rgb(Palette.Lin(v.SunColor)) * v.SunIntensity * s.y;
                Vector3 expected = Vector3.Scale(albedo, sun + Rgb(v.KickColor) * Mathf.Max(0f, v.KickDir.y) + Rgb(v.AmbSky)) + Vector3.Scale(poolAlbedo, glow) * 0.5f;
                Color drawn = At(image, near, feet, 0f, -89f);
                AssertColour(Hazed(v, expected, near, eye), drawn, "the floor beside the ball");
                Color plain = Palette.Lin(At(image, far, feet, 0f, -89f)), pooled = Palette.Lin(drawn);
                Assert.Greater(pooled.r / pooled.g, plain.r / plain.g * 1.08f, "the pool is not cherry");
                Assert.Less(Luminance(pooled), Luminance(plain), "the contact is not darker than the open floor");
                Assert.Greater(core, 0.1f, "the test point is not in the contact core");
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }
    }
}
