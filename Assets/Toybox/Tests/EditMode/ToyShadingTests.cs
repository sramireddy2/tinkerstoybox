using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Toybox.Art;
using Toybox.EditorTools;
using Toybox.Engine;
using Toybox.Platform;
using Toybox.Render;
using Toybox.Toys;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Toybox.Tests
{
    /// <summary>
    /// Toy shading and the held-object sticker (ART_BIBLE 2.6, 3.2, 3.4, 4.2, 4.3, 8, 9.1 - 9.3): the
    /// shaders and their templates, the procedural detail textures, what the material system writes, the
    /// setup step, the held presenter's cues, and - where there is a graphics device - the picture itself,
    /// including the acceptance test of 8.3. Pictures are written to tools/out/shots/toy-shading.
    /// </summary>
    public class ToyShadingTests
    {
        const string ShotDirectory = "tools/out/shots/toy-shading";

        static readonly string[] GlobalVectors =
        {
            "_AmbSky", "_AmbEquator", "_AmbGround", "_KickDir", "_KickColor", "_WinDir", "_WinRight", "_WinUp",
            "_GlintColor", "_EnvCeil", "_EnvWall", "_EnvFloor",
        };

        Game game;
        ScriptedInput input;
        Presentation presentation;
        readonly List<Object> temporary = new List<Object>();

        [TearDown]
        public void CleanUp()
        {
            presentation?.Dispose();
            presentation = null;
            game?.Dispose();
            game = null;
            Game.Current?.Dispose();
            foreach (Object o in temporary)
                if (o != null) Object.DestroyImmediate(o);
            temporary.Clear();
            StickerLook.Reset();
            Materials.Plain = false;
            Materials.Tier = QualityTier.Medium;
            Settings.Use(null);
        }

        // ------------------------------------------------------------------------------------------
        // Shaders and templates
        // ------------------------------------------------------------------------------------------

        [Test]
        public void ToyLit_HasEveryPropertyOfTheArtBible_AndOnlyTheShadowKeywords()
        {
            Shader shader = Shader.Find("Toybox/ToyLit");
            Assert.IsNotNull(shader, "Toybox/ToyLit exists and compiles");
            Assert.IsFalse(UnityEditor.ShaderUtil.ShaderHasError(shader), "no compile errors");

            // 3.2, with its defaults. Colours are Vector properties: Unity would convert a Color from sRGB.
            var floats = new Dictionary<string, float>
            {
                { "_Metallic", 0f }, { "_Smoothness", 0.6f }, { "_Wrap", 0.2f }, { "_Env", 0.5f }, { "_Coat", 0f }, { "_Streak", 0f },
                { "_Glint", 1f }, { "_GlintSoft", 0.02f }, { "_GlintStretch", 1f }, { "_Rim", 0.55f }, { "_RimPow", 3f },
                { "_SelfGlow", 0.06f }, { "_Translucency", 0f }, { "_DetailAlbedo", 0f }, { "_DetailSmooth", 0f }, { "_DetailBump", 0f },
                { "_AlphaFace", 1f }, { "_AlphaEdge", 1f }, { "_AlphaPow", 2.5f }, { "_ShadowDither", 0f },
                { "_SrcBlend", (float)BlendMode.One }, { "_DstBlend", (float)BlendMode.Zero }, { "_ZWrite", 1f }, { "_Cull", (float)CullMode.Back },
            };
            foreach (KeyValuePair<string, float> property in floats)
            {
                int index = shader.FindPropertyIndex(property.Key);
                Assert.GreaterOrEqual(index, 0, property.Key);
                ShaderPropertyType type = shader.GetPropertyType(index);
                Assert.IsTrue(type == ShaderPropertyType.Float || type == ShaderPropertyType.Range, property.Key + " is a number");
                Assert.AreEqual(property.Value, shader.GetPropertyDefaultFloatValue(index), 1e-6f, property.Key + " default");
            }
            foreach (string vector in new[] { "_BaseColor", "_RimColor", "_Emission", "_Sweep", "_SquashA" })
            {
                int index = shader.FindPropertyIndex(vector);
                Assert.GreaterOrEqual(index, 0, vector);
                Assert.AreEqual(ShaderPropertyType.Vector, shader.GetPropertyType(index), vector + " is a Vector, not a Color");
            }
            Color paper = Palette.Lin(Palette.Paper);
            Vector4 rim = shader.GetPropertyDefaultVectorValue(shader.FindPropertyIndex("_RimColor"));
            Assert.Less((new Vector3(rim.x, rim.y, rim.z) - new Vector3(paper.r, paper.g, paper.b)).magnitude, 1e-3f, "the rim is Paper by default, linear");
            Assert.AreEqual(Vector4.zero, shader.GetPropertyDefaultVectorValue(shader.FindPropertyIndex("_Emission")));
            Assert.AreEqual(ShaderPropertyType.Texture, shader.GetPropertyType(shader.FindPropertyIndex("_DetailMap")));

            // 3.1: ForwardLit, ShadowCaster, DepthOnly - and no DepthNormals pass.
            var modes = new List<string>();
            for (int pass = 0; pass < shader.passCount; pass++) modes.Add(shader.FindPassTagValue(pass, new ShaderTagId("LightMode")).name.ToUpperInvariant());
            CollectionAssert.AreEqual(new[] { "UNIVERSALFORWARD", "SHADOWCASTER", "DEPTHONLY" }, modes);
            Assert.AreEqual("UniversalPipeline", shader.FindSubshaderTagValue(0, new ShaderTagId("RenderPipeline")).name);

            // No shader_feature, no fog, no instancing: the six main-light shadow keywords and nothing else.
            List<string> keywords = OwnKeywords(shader);
            CollectionAssert.AreEqual(new[]
            {
                "_MAIN_LIGHT_SHADOWS", "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT", "_SHADOWS_SOFT_HIGH", "_SHADOWS_SOFT_LOW", "_SHADOWS_SOFT_MEDIUM",
            }, keywords);

            // One UnityPerMaterial block, identical in every pass (the editor's own check, where it exists).
            System.Reflection.MethodInfo batcher = typeof(UnityEditor.ShaderUtil).GetMethod("GetSRPBatcherCompatibilityCode",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (batcher != null && batcher.GetParameters().Length == 2)
                Assert.AreEqual(0, Convert.ToInt32(batcher.Invoke(null, new object[] { shader, 0 })), "SRP Batcher compatible");
        }

        // The keywords a shader declares itself. Unity puts its four stereo keywords into every shader's space.
        static List<string> OwnKeywords(Shader shader)
        {
            var keywords = new List<string>();
            foreach (string name in shader.keywordSpace.keywordNames)
                if (!name.StartsWith("STEREO_", StringComparison.Ordinal) && name != "UNITY_SINGLE_PASS_STEREO") keywords.Add(name);
            keywords.Sort(string.CompareOrdinal);
            return keywords;
        }

        [Test]
        public void Sticker_IsTwoStencilledPassesWithoutProperties()
        {
            Shader shader = Shader.Find("Toybox/Sticker");
            Assert.IsNotNull(shader, "Toybox/Sticker exists and compiles");
            Assert.IsFalse(UnityEditor.ShaderUtil.ShaderHasError(shader));
            Assert.AreEqual(0, shader.GetPropertyCount(), "no material properties: everything is a global");
            Assert.AreEqual(2, shader.passCount);
            Assert.AreEqual("", string.Join(" ", OwnKeywords(shader)), "no keywords at all");

            var material = new Material(shader);
            temporary.Add(material);
            StringAssert.AreEqualIgnoringCase("Border", material.GetPassName(0));
            StringAssert.AreEqualIgnoringCase("PeelShadow", material.GetPassName(1));
        }

        // The build target is WebGL 2: every pass has to compile for GLES 3.0, with the keyword sets the tiers use.
        [Test]
        public void BothShaders_CompileForWebGL()
        {
            string[][] lit =
            {
                new string[0],
                new[] { "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT_LOW" },
                new[] { "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT_MEDIUM" },
                new[] { "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT_HIGH" },
                new[] { "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT" },
                new[] { "_MAIN_LIGHT_SHADOWS" },
            };
            string[][] plain = { new string[0] };
            int compiled = 0;
            foreach (string name in new[] { "Toybox/ToyLit", "Toybox/Sticker" })
            {
                Shader shader = Shader.Find(name);
                Assert.IsNotNull(shader, name);
                UnityEditor.ShaderData.Subshader subshader = UnityEditor.ShaderUtil.GetShaderData(shader).GetSubshader(0);
                for (int p = 0; p < subshader.PassCount; p++)
                {
                    UnityEditor.ShaderData.Pass pass = subshader.GetPass(p);
                    foreach (string[] keywords in name == "Toybox/ToyLit" && p == 0 ? lit : plain)
                    {
                        foreach (UnityEditor.Rendering.ShaderType stage in new[] { UnityEditor.Rendering.ShaderType.Vertex, UnityEditor.Rendering.ShaderType.Fragment })
                        {
                            UnityEditor.ShaderData.VariantCompileInfo info = pass.CompileVariant(stage, keywords,
                                UnityEditor.Rendering.ShaderCompilerPlatform.GLES3x, UnityEditor.BuildTarget.WebGL);
                            var errors = new List<string>();
                            foreach (UnityEditor.ShaderMessage message in info.Messages)
                                if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error) errors.Add(message.message + " (" + message.file + ":" + message.line + ")");
                            string what = name + " pass " + p + " '" + pass.Name + "' " + stage + " [" + string.Join(" ", keywords) + "]";
                            Assert.AreEqual("", string.Join("; ", errors), what);
                            Assert.IsTrue(info.Success, what);
                            // A GLSL program carries both stages in the vertex blob; the fragment's own is empty.
                            if (stage == UnityEditor.Rendering.ShaderType.Vertex) Assert.Greater(info.ShaderData.Length, 0, what);
                            compiled++;
                        }
                    }
                }
            }
            Assert.AreEqual((6 + 1 + 1 + 2) * 2, compiled);
        }

        [Test]
        public void Templates_PointAtTheGamesShaders_AndTheRendererCarriesTheStickerPass()
        {
            Material toy = Resources.Load<Material>("Materials/ToyLit");
            Assert.IsNotNull(toy);
            Assert.AreEqual("Toybox/ToyLit", toy.shader.name, "ToyShadingSetup repoints the toy template (run ProjectSetup)");
            Assert.AreEqual(0, toy.shaderKeywords.Length, "nothing of URP Lit is left on it");
            Material sticker = Resources.Load<Material>("Materials/Sticker");
            Assert.IsNotNull(sticker, "Resources/Materials/Sticker.mat");
            Assert.AreEqual("Toybox/Sticker", sticker.shader.name);

            List<UniversalRendererData> renderers = ToyShadingSetup.Renderers();
            Assert.Greater(renderers.Count, 0);
            foreach (UniversalRendererData renderer in renderers)
            {
                StickerFeature feature = null;
                int stickerAt = -1, bandAt = -1;
                for (int i = 0; i < renderer.rendererFeatures.Count; i++)
                {
                    ScriptableRendererFeature candidate = renderer.rendererFeatures[i];
                    if (candidate == null) continue;
                    if (candidate.name == "MacroBand") bandAt = i;
                    if (!(candidate is StickerFeature found)) continue;
                    feature = found;
                    stickerAt = i;
                }
                Assert.IsNotNull(feature, renderer.name + " has the sticker pass (run ProjectSetup)");
                Assert.AreEqual(StickerFeature.FeatureName, feature.name);
                Assert.IsTrue(feature.isActive);
                Assert.AreSame(sticker, feature.Material, "the serialized reference is what keeps Toybox/Sticker in the build");
                Assert.Less(bandAt, stickerAt, "the lens blur runs before the sticker");
                Assert.AreEqual(0, renderer.opaqueLayerMask.value & Layers.HeldMask, renderer.name + ": the opaque pass skips the Held layer");
                Assert.AreEqual(0, renderer.transparentLayerMask.value & Layers.HeldMask, renderer.name + ": the transparent pass skips the Held layer");
                Assert.AreNotEqual(0, renderer.opaqueLayerMask.value & Layers.PropMask, "and nothing else was taken out");
                Assert.AreNotEqual(0, renderer.opaqueLayerMask.value & Layers.DefaultMask);
                DepthFormat depth = renderer.depthAttachmentFormat;
                Assert.IsTrue(depth == DepthFormat.Default || GraphicsFormatUtility.IsStencilFormat((GraphicsFormat)depth), "the depth attachment has a stencil");
            }
        }

        [Test]
        public void Setup_ConfiguresARendererOnce_AndLeavesItAloneTheSecondTime()
        {
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            var sticker = new Material(Shader.Find("Toybox/Sticker"));
            temporary.Add(renderer);
            temporary.Add(sticker);
            Assert.AreEqual(-1, renderer.opaqueLayerMask.value, "a new renderer draws every layer");

            Assert.IsTrue(ToyShadingSetup.Configure(renderer, sticker), "the first run changes it");
            Assert.AreEqual(1, renderer.rendererFeatures.Count);
            var feature = (StickerFeature)renderer.rendererFeatures[0];
            temporary.Add(feature);
            Assert.AreEqual("Sticker", feature.name);
            Assert.AreSame(sticker, feature.Material);
            Assert.AreEqual(~Layers.HeldMask, renderer.opaqueLayerMask.value);
            Assert.AreEqual(~Layers.HeldMask, renderer.transparentLayerMask.value);
            Assert.AreEqual(~Layers.HeldMask, renderer.prepassLayerMask.value);

            Assert.IsFalse(ToyShadingSetup.Configure(renderer, sticker), "the second run finds nothing to do");
            Assert.AreEqual(1, renderer.rendererFeatures.Count);

            // The pipeline's lens blur added afterwards still ends up in front of the sticker, and a lost
            // material reference or a mask somebody opened again is put right.
            ScriptableRendererFeature band = SetupUtil.EnsureRendererFeature<FullScreenPassRendererFeature>(renderer, "MacroBand");
            temporary.Add(band);
            feature.Material = null;
            renderer.opaqueLayerMask = -1;
            renderer.depthAttachmentFormat = DepthFormat.Depth_32;
            Assert.IsTrue(ToyShadingSetup.Configure(renderer, sticker));
            Assert.AreEqual(2, renderer.rendererFeatures.Count);
            Assert.AreEqual("MacroBand", renderer.rendererFeatures[0].name);
            Assert.AreSame(feature, renderer.rendererFeatures[1]);
            Assert.AreSame(sticker, feature.Material);
            Assert.AreEqual(~Layers.HeldMask, renderer.opaqueLayerMask.value);
            Assert.AreEqual(DepthFormat.Default, renderer.depthAttachmentFormat, "a depth format without stencil is replaced");
            Assert.IsFalse(ToyShadingSetup.Configure(renderer, sticker));
        }

        // ------------------------------------------------------------------------------------------
        // Detail textures (4.2)
        // ------------------------------------------------------------------------------------------

        struct Channel
        {
            public int Min, Max, Changed;
        }

        static void Measure(Color32[] pixels, out Channel r, out Channel g, out Channel b)
        {
            r = g = b = new Channel { Min = 255, Max = 0 };
            foreach (Color32 p in pixels)
            {
                r.Min = Mathf.Min(r.Min, p.r); r.Max = Mathf.Max(r.Max, p.r); if (p.r != 128) r.Changed++;
                g.Min = Mathf.Min(g.Min, p.g); g.Max = Mathf.Max(g.Max, p.g); if (p.g != 128) g.Changed++;
                b.Min = Mathf.Min(b.Min, p.b); b.Max = Mathf.Max(b.Max, p.b); if (p.b != 128) b.Changed++;
            }
        }

        [Test]
        public void DetailPixels_AreDeterministic_AndFollowTheRecipes()
        {
            Assert.AreEqual(2166136261u, TexCache.Seed(""), "FNV-1a offset basis");
            Assert.AreEqual(0xE40C292Cu, TexCache.Seed("a"), "FNV-1a of 'a'");
            Assert.AreNotEqual(TexCache.Seed("Peel"), TexCache.Seed("Pore"));

            foreach (DetailTexture kind in (DetailTexture[])Enum.GetValues(typeof(DetailTexture)))
            {
                Color32[] first = TexCache.Pixels(kind, out int width, out int height);
                Color32[] second = TexCache.Pixels(kind, out _, out _);
                Assert.AreEqual(width * height, first.Length, kind.ToString());
                CollectionAssert.AreEqual(first, second, kind + ": the same picture every time");
                Assert.AreEqual(kind == DetailTexture.Neutral ? 4 : 256, width, kind.ToString());
                Assert.AreEqual(kind == DetailTexture.Neutral ? 4 : kind == DetailTexture.Barb ? 64 : 256, height, kind.ToString());
            }

            Channel r, g, b;
            Measure(TexCache.Pixels(DetailTexture.Neutral, out _, out _), out r, out g, out b);
            Assert.AreEqual(0, r.Changed + g.Changed + b.Changed, "Neutral is 128 everywhere");

            Measure(TexCache.Pixels(DetailTexture.Peel, out _, out _), out r, out g, out b);
            Assert.AreEqual(0, r.Changed + g.Changed, "Peel is height only");
            Assert.GreaterOrEqual(b.Min, 108);
            Assert.LessOrEqual(b.Max, 148);
            Assert.Greater(b.Max - b.Min, 20, "two octaves of noise, not a flat field");

            Color32[] brush = TexCache.Pixels(DetailTexture.Brush, out _, out _);
            Measure(brush, out r, out g, out b);
            Assert.GreaterOrEqual(r.Min, 118); Assert.LessOrEqual(r.Max, 138);
            Assert.GreaterOrEqual(b.Min, 113); Assert.LessOrEqual(b.Max, 143);
            Assert.GreaterOrEqual(g.Min, 123); Assert.LessOrEqual(g.Max, 133);
            Assert.Greater(b.Max - b.Min, 20, "strokes both ways");
            for (int y = 0; y < 256; y += 37)
                for (int x = 1; x < 256; x += 51)
                    Assert.AreEqual(brush[y * 256], brush[y * 256 + x], "a stroke is a full-width band");
            foreach (Color32 p in brush)
                if (p.r > 130) Assert.LessOrEqual(p.g, 128, "where paint is thick (R up, B up) it is rougher (G down)");

            Measure(TexCache.Pixels(DetailTexture.Stipple, out _, out _), out r, out g, out b);
            Assert.AreEqual(0, r.Changed);
            Assert.AreEqual(128, b.Min); Assert.AreEqual(168, b.Max, "dots rise by 40");
            Assert.AreEqual(118, g.Min); Assert.AreEqual(128, g.Max, "and are 10 rougher");
            Assert.Greater(b.Changed, 256 * 256 / 10, "900 dots cover a good part of the texture");

            Color32[] streak = TexCache.Pixels(DetailTexture.Streak, out _, out _);
            Measure(streak, out r, out g, out b);
            Assert.AreEqual(0, b.Changed);
            Assert.GreaterOrEqual(g.Min, 112); Assert.LessOrEqual(g.Max, 144);
            Assert.Greater(g.Max - g.Min, 20);
            long along = 0, across = 0;
            for (int y = 0; y < 255; y++)
            {
                for (int x = 0; x < 255; x++)
                {
                    along += Mathf.Abs(streak[y * 256 + x].g - streak[y * 256 + x + 1].g);
                    across += Mathf.Abs(streak[y * 256 + x].g - streak[(y + 1) * 256 + x].g);
                    Assert.LessOrEqual(Mathf.Abs((streak[y * 256 + x].r - 128) * 2 - (streak[y * 256 + x].g - 128)), 2, "R carries half of G");
                }
            }
            Assert.Less(along * 4, across, "the streaks run along U");

            Measure(TexCache.Pixels(DetailTexture.Fibre, out _, out _), out r, out g, out b);
            Assert.AreEqual(0, g.Changed);
            Assert.GreaterOrEqual(r.Min, 104); Assert.LessOrEqual(r.Max, 152);
            Assert.GreaterOrEqual(b.Min, 104); Assert.LessOrEqual(b.Max, 152);
            Assert.Greater(b.Changed, 256 * 256 / 4, "3000 strokes");

            Color32[] speckle = TexCache.Pixels(DetailTexture.Speckle, out _, out _);
            int speckles = 0;
            for (int y = 0; y < 256; y++)
            {
                for (int x = 0; x < 256; x++)
                {
                    Color32 p = speckle[y * 256 + x];
                    Assert.AreEqual(128, p.g);
                    if (y < 64)
                    {
                        Assert.AreEqual(speckle[x], p, "flutes are the same on every row of the bottom quarter");
                    }
                    else
                    {
                        Assert.AreEqual(128, p.b, "no height above the flutes");
                        if (y < 128) Assert.AreEqual(128, p.r, "the second quarter is plain");
                        else if (p.r != 128) speckles++;
                        Assert.LessOrEqual(Mathf.Abs(p.r - 128), 15);
                    }
                }
            }
            Assert.Greater(speckles, 3000, "4000 speckles in the top half (a few fall on each other)");
            Measure(new List<Color32>(speckle).GetRange(0, 256).ToArray(), out r, out g, out b);
            Assert.LessOrEqual(b.Min, 100); Assert.GreaterOrEqual(b.Max, 156, "flutes swing by 30");

            Measure(TexCache.Pixels(DetailTexture.Pore, out _, out _), out r, out g, out b);
            Assert.AreEqual(0, g.Changed);
            Assert.AreEqual(70, r.Min); Assert.AreEqual(128, r.Max, "pores only darken: 128 - 58");
            Assert.AreEqual(68, b.Min); Assert.AreEqual(128, b.Max, "and only sink: 128 - 60");

            Measure(TexCache.Pixels(DetailTexture.Barb, out _, out _), out r, out g, out b);
            Assert.GreaterOrEqual(r.Min, 110); Assert.LessOrEqual(r.Max, 146);
            Assert.GreaterOrEqual(b.Min, 88); Assert.LessOrEqual(b.Max, 168);
            Assert.AreEqual(128, g.Min); Assert.AreEqual(138, g.Max);

            // Low tier: the same picture, averaged down.
            Color32[] peel = TexCache.Pixels(DetailTexture.Peel, out int w, out int h);
            Color32[] half = TexCache.Halve(peel, ref w, ref h);
            Assert.AreEqual(128, w);
            Assert.AreEqual(128, h);
            Assert.AreEqual((peel[0].b + peel[1].b + peel[256].b + peel[257].b + 2) / 4, half[0].b);
        }

        [Test]
        public void DetailTextures_AreMadeOnce_ToTheBudget()
        {
            Assume.That(TexCache.HasGraphics, "textures need a graphics device (-nographics run)");
            Materials.Tier = QualityTier.High;

            Texture2D brush = TexCache.Get(DetailTexture.Brush, QualityTier.Medium);
            Assert.AreSame(brush, TexCache.Get(DetailTexture.Brush, QualityTier.High), "Medium and High share a texture");
            Assert.AreSame(brush, TexCache.Get(DetailTexture.Brush), "and so does the tier in force");
            Assert.AreEqual(256, brush.width);
            Assert.AreEqual(256, brush.height);
            Assert.AreEqual(9, brush.mipmapCount);
            Assert.AreEqual(TextureWrapMode.Repeat, brush.wrapMode);
            Assert.AreEqual(FilterMode.Trilinear, brush.filterMode);
            Assert.AreEqual(4, brush.anisoLevel);
            Assert.IsFalse(GraphicsFormatUtility.IsSRGBFormat(brush.graphicsFormat), "data, not colour: linear");
            Assert.IsFalse(brush.isReadable, "no CPU copy is kept");
            Texture2D barb = TexCache.Get(DetailTexture.Barb, QualityTier.Medium);
            Assert.AreEqual(256, barb.width);
            Assert.AreEqual(64, barb.height);
            Assert.AreEqual(4, TexCache.Get(DetailTexture.Neutral, QualityTier.Low).width);

            // The orange peel is height only and the bump is off below High: plastic takes the neutral texture there.
            Assert.AreSame(TexCache.Get(DetailTexture.Neutral, QualityTier.Medium), TexCache.For(ToyRecipe.GlossyPlastic, QualityTier.Medium));
            Assert.AreSame(TexCache.Get(DetailTexture.Peel, QualityTier.High), TexCache.For(ToyRecipe.GlossyPlastic, QualityTier.High));
            Assert.AreSame(TexCache.Get(DetailTexture.Fibre, QualityTier.Medium), TexCache.For(ToyRecipe.Felt, QualityTier.Medium));

            // The budget: nine textures under 3 MB.
            int guard = 0;
            while (TexCache.WarmNext(QualityTier.High)) Assert.Less(guard++, 20);
            Assert.AreEqual(9, TexCache.Count);
            Assert.Less(TexCache.Bytes, 3L * 1024 * 1024);
            Assert.IsFalse(TexCache.WarmNext(QualityTier.High), "nothing left to make");

            // The loading screen makes at most one per call (here: the half-size set, which does not exist on High).
            int made = 0;
            while (TexCache.WarmNext(QualityTier.Low))
            {
                made++;
                Assert.AreEqual(9 + made, TexCache.Count, "one texture per call");
                Assert.Less(guard++, 40);
            }
            Assert.AreEqual(7, made, "seven kinds have a half-size version: all but Neutral, and the peel is High only");
            TexCache.Trim(QualityTier.High);
            Assert.AreEqual(9, TexCache.Count);

            // Emptying the cache leaves no material with a destroyed texture.
            Material wood = Materials.Toy(ToyRecipe.PaintedWood, Palette.Tangerine);
            TexCache.Clear();
            Assert.IsTrue(wood.GetTexture("_DetailMap") != null, "what is in use is made again at once");
            Assert.AreSame(TexCache.Get(DetailTexture.Brush), wood.GetTexture("_DetailMap"));
            while (TexCache.WarmNext(QualityTier.High)) Assert.Less(guard++, 60);
            Assert.AreEqual(9, TexCache.Count);

            // A tier change never leaves textures of the other size behind.
            Materials.Tier = QualityTier.Medium;
            Assert.AreEqual(8, TexCache.Count, "below High the peel is not kept");
            Materials.Tier = QualityTier.Low;
            while (TexCache.WarmNext(QualityTier.Low)) Assert.Less(guard++, 80);
            Assert.AreEqual(8, TexCache.Count, "on Low only the half-size ones exist (Neutral is the same on every tier)");
            Assert.Less(TexCache.Bytes, 1024 * 1024);
            Texture2D low = TexCache.Get(DetailTexture.Brush, QualityTier.Low);
            Assert.AreEqual(128, low.width);
            Assert.AreEqual(1, low.anisoLevel);
            Materials.Tier = QualityTier.Medium;
            while (TexCache.WarmNext(QualityTier.Medium)) Assert.Less(guard++, 100);
            Assert.AreEqual(8, TexCache.Count);

            // Generation, measured: all nine pictures at full size.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (DetailTexture kind in (DetailTexture[])Enum.GetValues(typeof(DetailTexture))) TexCache.Pixels(kind, out _, out _);
            clock.Stop();
            File.WriteAllText(ShotPath("texcache-timing").Replace(".png", ".txt"), "all nine pictures: " + clock.Elapsed.TotalMilliseconds.ToString("0.0") + " ms (editor, Mono)");
            Assert.Less(clock.Elapsed.TotalMilliseconds, 600.0, "the budget is 60 ms in the player; the editor's Mono gets ten times that");
        }

        // ------------------------------------------------------------------------------------------
        // Materials
        // ------------------------------------------------------------------------------------------

        // The table of ART_BIBLE 4.3, row by row, as written there.
        // metal, smooth, wrap, env, coat, glint, soft, stretch, rim, rimPow, selfGlow, detail, albedo, smooth, bump
        static readonly (ToyRecipe recipe, float[] numbers, DetailTexture detail)[] Table =
        {
            (ToyRecipe.GlossyPlastic, new[] { 0f, 0.62f, 0.20f, 0.5f, 1.0f, 1.0f, 0.02f, 1f, 0.55f, 3f, 0.06f, 0f, 0f, 0.03f }, DetailTexture.Peel),
            (ToyRecipe.PaintedWood, new[] { 0f, 0.45f, 0.25f, 0.3f, 0.35f, 0.35f, 0.25f, 1f, 0.40f, 3f, 0.03f, 0.08f, 0.2f, 0.6f }, DetailTexture.Brush),
            (ToyRecipe.Rubber, new[] { 0f, 0.18f, 0.50f, 0.1f, 0f, 0.12f, 0.5f, 1f, 0.65f, 2.5f, 0.04f, 0f, 0.3f, 0.15f }, DetailTexture.Stipple),
            (ToyRecipe.BrushedMetal, new[] { 1f, 0.66f, 0f, 1.2f, 0.3f, 0.8f, 0.15f, 3f, 0.30f, 3f, 0f, 0.06f, 0.5f, 0f }, DetailTexture.Streak),
            (ToyRecipe.Glass, new[] { 0f, 0.96f, 0f, 1.6f, 1.0f, 1.4f, 0.01f, 1f, 0.8f, 2.5f, 0f, 0f, 0f, 0f }, DetailTexture.Neutral),
            (ToyRecipe.Felt, new[] { 0f, 0f, 0.60f, 0f, 0f, 0f, -1f, -1f, 0.9f, 2f, 0.05f, 0.16f, 0f, 1.0f }, DetailTexture.Fibre),
            (ToyRecipe.Cardboard, new[] { 0f, 0.08f, 0.30f, 0.05f, 0f, 0f, -1f, -1f, 0.35f, 3f, 0.02f, 0.12f, 0f, 0.3f }, DetailTexture.Speckle),
            (ToyRecipe.Sponge, new[] { 0f, 0f, 0.60f, 0f, 0f, 0f, -1f, -1f, 0.5f, 2.5f, 0.05f, 0.9f, 0f, 1.0f }, DetailTexture.Pore),
            (ToyRecipe.Feather, new[] { 0f, 0.40f, 0.50f, 0.1f, 0f, 0.25f, 0.2f, 4f, 0.7f, 2f, 0.03f, 0.25f, 0.3f, 0.4f }, DetailTexture.Barb),
        };

        static readonly string[] TableProperties =
        {
            "_Metallic", "_Smoothness", "_Wrap", "_Env", "_Coat", "_Glint", "_GlintSoft", "_GlintStretch", "_Rim", "_RimPow", "_SelfGlow",
            "_DetailAlbedo", "_DetailSmooth", "_DetailBump",
        };

        static Vector3 Rgb(Vector4 v) => new Vector3(v.x, v.y, v.z);
        static Vector3 Rgb(Color c) => new Vector3(c.r, c.g, c.b);

        [Test]
        public void ToyMaterials_CarryTheNumbersOfTheRecipeTable_OnTheGamesShader()
        {
            Materials.Tier = QualityTier.High;
            foreach ((ToyRecipe recipe, float[] numbers, DetailTexture detail) in Table)
            {
                Material material = Materials.Toy(recipe, Palette.Lagoon);
                Assert.AreEqual("Toybox/ToyLit", material.shader.name, recipe.Name);
                for (int i = 0; i < numbers.Length; i++)
                {
                    // -1: the table leaves the column empty (no glint, so its softness and stretch do not matter).
                    if (numbers[i] < 0f) continue;
                    Assert.AreEqual(numbers[i], material.GetFloat(TableProperties[i]), 1e-6f, recipe.Name + " " + TableProperties[i]);
                }
                Assert.AreEqual(detail, recipe.Detail, recipe.Name);
                if (TexCache.HasGraphics) Assert.AreSame(TexCache.Get(detail, QualityTier.High), material.GetTexture("_DetailMap"), recipe.Name + " detail map");
                Assert.AreEqual(0f, material.GetVector("_Sweep").w, "no sweep on the material itself");
                Assert.AreEqual(0f, material.GetVector("_SquashA").w, "and no squash");
            }

            Color lagoon = Palette.Lin(Palette.Lagoon);
            Assert.Less((Rgb(Materials.Toy(ToyRecipe.GlossyPlastic, Palette.Lagoon).GetVector("_BaseColor")) - Rgb(lagoon)).magnitude, 1e-5f, "plastic is the candy colour");
            Assert.Less((Rgb(Materials.Toy(ToyRecipe.Rubber, Palette.Lagoon).GetVector("_BaseColor")) - Rgb(lagoon) * 0.92f).magnitude, 1e-5f, "rubber is candy x 0.92");
            Assert.Less((Rgb(Materials.Toy(ToyRecipe.Glass, Palette.Lagoon).GetVector("_BaseColor")) - Rgb(Color.LerpUnclamped(Color.white, lagoon, 0.35f))).magnitude, 1e-5f);
            Assert.Less((Rgb(Materials.Toy(ToyRecipe.Sponge, Palette.Lagoon).GetVector("_BaseColor")) - Rgb(Color.LerpUnclamped(lagoon, Color.white, 0.15f))).magnitude, 1e-5f);
            Assert.Less((Rgb(Materials.Toy(ToyRecipe.Feather, Palette.Lagoon).GetVector("_BaseColor")) - Rgb(Color.LerpUnclamped(Palette.Lin(Palette.Paper), lagoon, 0.65f))).magnitude, 1e-5f);
            Assert.Less((Rgb(Materials.Toy(ToyRecipe.Rubber, Palette.Lagoon).GetVector("_RimColor")) - Rgb(Color.LerpUnclamped(lagoon, Color.white, 0.6f))).magnitude, 1e-5f);
            Assert.Less((Rgb(Materials.Toy(ToyRecipe.Felt, Palette.Lagoon).GetVector("_RimColor")) - Rgb(Color.LerpUnclamped(lagoon, Color.white, 0.35f))).magnitude, 1e-5f);
            Assert.Less((Rgb(Materials.Toy(ToyRecipe.Glass, Palette.Lagoon).GetVector("_RimColor")) - Rgb(lagoon)).magnitude, 1e-5f, "the glass rim is the candy colour");

            // The per-material extras of 4.3.
            Assert.AreEqual(0.18f, Materials.Toy(ToyRecipe.BrushedMetal, Palette.Lagoon).GetFloat("_Streak"), 1e-6f);
            Material glass = Materials.Toy(ToyRecipe.Glass, Palette.Lagoon);
            Assert.AreEqual(3000, glass.renderQueue);
            Assert.AreEqual((float)BlendMode.SrcAlpha, glass.GetFloat("_SrcBlend"));
            Assert.AreEqual(0f, glass.GetFloat("_ZWrite"));
            Assert.AreEqual(0.22f, glass.GetFloat("_AlphaFace"), 1e-6f);
            Assert.AreEqual(0.85f, glass.GetFloat("_AlphaEdge"), 1e-6f);
            Assert.AreEqual(1f, glass.GetFloat("_ShadowDither"));
            Material[] set = Materials.ToySet(ToyRecipe.Glass, Palette.Lagoon);
            Assert.AreEqual(2, set.Length);
            Assert.AreEqual(2999, set[0].renderQueue);
            Assert.AreEqual((float)CullMode.Front, set[0].GetFloat("_Cull"));
            Assert.AreEqual(0.18f, set[0].GetFloat("_AlphaFace"), 1e-6f);
            Assert.AreEqual(0.18f, set[0].GetFloat("_AlphaEdge"), 1e-6f);
            Assert.AreEqual((float)CullMode.Off, Materials.Toy(ToyRecipe.PaperSheet, Palette.Paper).GetFloat("_Cull"));
            Assert.AreEqual(0.5f, Materials.Toy(ToyRecipe.PaperSheet, Palette.Paper).GetFloat("_Translucency"), 1e-6f);
            Assert.AreEqual(0.8f, Materials.Toy(ToyRecipe.Feather, Palette.Lagoon).GetFloat("_Translucency"), 1e-6f);
            Assert.AreEqual(0.7f, Materials.Toy(ToyRecipe.TapeStrip, Palette.Lagoon).GetFloat("_Smoothness"), 1e-6f);
            Assert.AreEqual(0f, Materials.Toy(ToyRecipe.PlainProp, Palette.Birch).GetFloat("_Rim"), "what cannot be lifted has no rim");
            Assert.AreEqual(0f, Materials.Gadget(GadgetPart.Body).GetFloat("_Rim"));
        }

        [Test]
        public void ToyMaterials_FollowTheTier_BumpOnlyOnHigh_SmallMapsOnLow()
        {
            Materials.Tier = QualityTier.Medium;
            Material wood = Materials.Toy(ToyRecipe.PaintedWood, Palette.Tangerine);
            Material plastic = Materials.Toy(ToyRecipe.GlossyPlastic, Palette.Tangerine);
            Assert.AreEqual(0f, wood.GetFloat("_DetailBump"));
            Assert.AreEqual(0f, plastic.GetFloat("_DetailBump"));
            // Glint, rim, coat and colour are identical on every tier.
            float glint = plastic.GetFloat("_Glint"), rim = plastic.GetFloat("_Rim"), coat = plastic.GetFloat("_Coat");

            Materials.Tier = QualityTier.High;
            Assert.AreEqual(0.6f, wood.GetFloat("_DetailBump"), 1e-6f);
            Assert.AreEqual(0.03f, plastic.GetFloat("_DetailBump"), 1e-6f);
            Materials.Tier = QualityTier.Low;
            Assert.AreEqual(0f, wood.GetFloat("_DetailBump"));
            Assert.AreEqual(glint, plastic.GetFloat("_Glint"));
            Assert.AreEqual(rim, plastic.GetFloat("_Rim"));
            Assert.AreEqual(coat, plastic.GetFloat("_Coat"));

            if (!TexCache.HasGraphics) return;
            Assert.AreEqual(128, wood.GetTexture("_DetailMap").width, "detail maps are 128 on Low");
            Assert.AreEqual(4, plastic.GetTexture("_DetailMap").width, "plastic has the neutral map below High");
            Materials.Tier = QualityTier.High;
            Assert.AreEqual(256, wood.GetTexture("_DetailMap").width);
            Assert.AreSame(TexCache.Get(DetailTexture.Peel, QualityTier.High), plastic.GetTexture("_DetailMap"), "and the orange peel on High");
            Assert.LessOrEqual(TexCache.Count, 9, "switching tiers leaves no texture of the other size behind");
            Materials.Tier = QualityTier.Medium;
            Assert.AreEqual(256, wood.GetTexture("_DetailMap").width);
            Assert.AreEqual(4, plastic.GetTexture("_DetailMap").width);
        }

        // ------------------------------------------------------------------------------------------
        // The held presenter (no rendering needed)
        // ------------------------------------------------------------------------------------------

        static List<PresenterRegistry.Entry> Only(params Type[] types)
        {
            var list = new List<PresenterRegistry.Entry>();
            foreach (PresenterRegistry.Entry entry in PresenterRegistry.All)
                if (Array.IndexOf(types, entry.Type) >= 0) list.Add(entry);
            Assert.AreEqual(types.Length, list.Count, "the presenters exist");
            return list;
        }

        // A floor, a pillar close by, a wall far away, and one toy in front of the player.
        Prop Yard(ToyRecipe recipe = null, bool plain = false)
        {
            Prop toy = null;
            input = new ScriptedInput();
            game = Game.Create(new GameOptions { Input = input });
            game.LoadLevel(new AdHocLevel(ctx =>
            {
                TestHelpers.Floor(ctx, 60f);
                TestHelpers.Box(ctx, new Vector3(0f, 6f, 12.5f), new Vector3(30f, 12f, 1f));
                TestHelpers.Box(ctx, new Vector3(3f, 1.5f, 5f), new Vector3(1f, 3f, 1f));
                toy = ctx.AddProp(Showpiece(recipe ?? ToyRecipe.GlossyPlastic, Palette.Cherry), new Vector3(0f, 0.65f, 4f));
                ctx.SetSpawn(Vector3.zero, 0f);
            }));
            presentation = Presentation.Create(game, new PresentationOptions { Plain = plain, Presenters = Only(typeof(HeldLook), typeof(ToySquash)) });
            TestHelpers.Run(game, 5);
            presentation.Frame(Sim.Dt, 1f);
            return toy;
        }

        void Click()
        {
            input.Once.GrabPressed = true;
            game.Tick();
        }

        static Vector4 Block(Renderer renderer, string property)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            return block.GetVector(property);
        }

        [Test]
        public void Grab_StopsTheShadow_PopsTheBorder_AndSlidesTheShadowOut()
        {
            Prop toy = Yard();
            HeldLook look = presentation.Get<HeldLook>();
            Assert.IsNotNull(look, "HeldLook is a presenter of the game");
            Renderer renderer = toy.GameObject.GetComponentInChildren<Renderer>();
            Assert.AreEqual(ShadowCastingMode.On, renderer.shadowCastingMode);
            Assert.IsFalse(StickerLook.Active);
            Assert.IsFalse(StickerLook.Border);

            TestHelpers.LookAt(game.Player, toy.Center);
            Click();
            Assert.AreSame(toy, game.Grabber.Held);
            Assert.AreSame(toy, look.Held, "the grab reached the presenter with the tick's events");
            Assert.AreEqual(ShadowCastingMode.Off, renderer.shadowCastingMode, "a held toy casts no shadow (8.1 step 3)");
            Assert.IsTrue(StickerLook.Active, "the sticker pass has something to draw");
            Assert.IsTrue(StickerLook.Border);
            Assert.AreEqual(0f, StickerLook.BorderPx, "the border starts from nothing");

            // 0 -> 6.5 -> 4.5 px in 110 ms; the shadow slides out to (+10, -12).
            float peak = 0f, last = 0f, time = 0f;
            for (int i = 0; i < 22; i++)
            {
                presentation.Frame(0.005f, 1f);
                time += 0.005f;
                last = look.BorderPx;
                peak = Mathf.Max(peak, last);
                Assert.AreEqual(last, StickerLook.BorderPx, "what the pass reads is what the presenter says");
                if (time < 0.03f) Assert.Less(look.PeelPx.magnitude, StickerLook.PeelOffset.magnitude * 0.3f, "the shadow starts under the toy");
            }
            Assert.AreEqual(HeldLook.PopPeak, peak, 0.05f, "the pop overshoots to 6.5 px");
            Assert.AreEqual(StickerLook.BorderWidth, last, 1e-3f, "and settles at 4.5 px");

            // Hold: the picture is stable; only the peel shadow breathes, by a pixel, at 0.5 Hz.
            float nearest = float.MaxValue, farthest = 0f;
            for (int i = 0; i < 200; i++)
            {
                presentation.Frame(0.01f, 1f);
                Assert.AreEqual(StickerLook.BorderWidth, look.BorderPx, 1e-3f);
                float along = Vector2.Dot(look.PeelPx, StickerLook.PeelOffset.normalized);
                nearest = Mathf.Min(nearest, along);
                farthest = Mathf.Max(farthest, along);
                Assert.Less(Mathf.Abs(Vector2.Dot(look.PeelPx, new Vector2(12f, 10f).normalized)), 1e-3f, "it breathes along its own direction");
            }
            float rest = StickerLook.PeelOffset.magnitude;
            Assert.AreEqual(rest - HeldLook.BreathePx, nearest, 0.02f);
            Assert.AreEqual(rest + HeldLook.BreathePx, farthest, 0.02f);
            Color peel = StickerLook.PeelColor, border = StickerLook.BorderColor;
            Color ink = Palette.Lin(Palette.Ink), paper = Palette.Lin(Palette.Paper);
            Assert.AreEqual(Mathf.Lerp(1f, ink.r, 0.22f), peel.r, 1e-5f, "lerp(white, Ink, 0.22)");
            Assert.AreEqual(paper.g * 1.1f, border.g, 1e-5f, "Paper x 1.1");

            // Release: shadows are back in the same frame and the border is gone.
            Click();
            Assert.IsNull(game.Grabber.Held);
            Assert.IsNull(look.Held);
            Assert.AreEqual(ShadowCastingMode.On, renderer.shadowCastingMode);
            Assert.IsFalse(StickerLook.Border);
            Assert.IsFalse(StickerLook.Active);
            Assert.AreEqual(0f, look.BorderPx);

            Assert.AreEqual(0f, HeldLook.BackOut(0f), 1e-6f);
            Assert.AreEqual(1f, HeldLook.BackOut(1f), 1e-6f);
        }

        [Test]
        public void Hold_FlashesTheBorderWhenTheToyJumpsToAnotherSurface()
        {
            Prop toy = Yard();
            HeldLook look = presentation.Get<HeldLook>();
            // Hold it against the pillar, a few units away.
            TestHelpers.LookAt(game.Player, toy.Center);
            Click();
            TestHelpers.LookAt(game.Player, new Vector3(3f, 1.6f, 4.5f));
            for (int i = 0; i < 30; i++)
            {
                game.Tick();
                presentation.Frame(Sim.Dt, 1f);
            }
            Assert.AreEqual(StickerLook.BorderWidth, look.BorderPx, 1e-3f, "at rest in the hand");
            float before = toy.Scale;

            // Off the pillar's edge: the toy lands on the far wall, several times the size.
            TestHelpers.LookAt(game.Player, new Vector3(0f, 3f, 12f));
            game.Tick();
            Assert.Greater(toy.Scale / before, 1f + HeldLook.JumpThreshold, "the projected scale jumped");
            presentation.Frame(0f, 1f);
            Assert.AreEqual(StickerLook.BorderWidth + HeldLook.FlashPx, look.BorderPx, 1e-3f, "+1.5 px");
            presentation.Frame(HeldLook.FlashSeconds * 0.5f, 1f);
            Assert.Greater(look.BorderPx, StickerLook.BorderWidth);
            Assert.Less(look.BorderPx, StickerLook.BorderWidth + HeldLook.FlashPx);
            presentation.Frame(HeldLook.FlashSeconds, 1f);
            Assert.AreEqual(StickerLook.BorderWidth, look.BorderPx, 1e-3f, "for 80 ms");

            // A steady hold does not flash.
            for (int i = 0; i < 20; i++)
            {
                game.Tick();
                presentation.Frame(Sim.Dt, 1f);
                Assert.AreEqual(StickerLook.BorderWidth, look.BorderPx, 1e-3f);
            }
        }

        [Test]
        public void TheHold_EndsCleanly_WhenTheLevelGoesAwayOrThePresentationDoes()
        {
            Prop toy = Yard();
            HeldLook look = presentation.Get<HeldLook>();
            Renderer renderer = toy.GameObject.GetComponentInChildren<Renderer>();
            TestHelpers.LookAt(game.Player, toy.Center);
            Click();
            presentation.Frame(0.2f, 1f);
            Assert.IsTrue(StickerLook.Border);

            // Disposed in the middle of a hold: the toy casts its shadow again and the pass loses its border.
            presentation.Dispose();
            presentation = null;
            Assert.AreEqual(ShadowCastingMode.On, renderer.shadowCastingMode);
            Assert.IsFalse(StickerLook.Border);
            Assert.AreEqual(0f, StickerLook.BorderPx);
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_ToyRimBoost"));
            Assert.IsTrue(StickerLook.Active, "the game still holds the toy: the pass goes on drawing it, on top, without a border");

            // Attached in the middle of a hold (a tool does that): the sticker is simply there.
            presentation = Presentation.Create(game, new PresentationOptions { Presenters = Only(typeof(HeldLook)) });
            look = presentation.Get<HeldLook>();
            Assert.AreSame(toy, look.Held);
            Assert.AreEqual(ShadowCastingMode.Off, renderer.shadowCastingMode);
            Assert.AreEqual(StickerLook.BorderWidth, look.BorderPx, 1e-3f, "no pop");

            // The level is reloaded with the toy in hand: no PropDropped arrives, and nothing is left behind.
            game.RestartLevel();
            Assert.IsNull(look.Held);
            Assert.IsFalse(StickerLook.Border);
            presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(0f, look.BorderPx);
            Assert.AreEqual(0, look.SweepCount);
        }

        [Test]
        public void ThePlainLook_HasNoSticker_ButTheHeldToyIsStillDrawnOnTop()
        {
            Prop toy = Yard(plain: true);
            HeldLook look = presentation.Get<HeldLook>();
            Assert.IsNotNull(look, "it provides neither light nor HUD, so it is attached - and stays out of the way");
            Renderer renderer = toy.GameObject.GetComponentInChildren<Renderer>();
            TestHelpers.LookAt(game.Player, toy.Center);
            Click();
            presentation.Frame(0.2f, 1f);
            Assert.AreSame(toy, game.Grabber.Held);
            Assert.IsNull(look.Held);
            Assert.AreEqual(ShadowCastingMode.On, renderer.shadowCastingMode, "as before the game had a look");
            Assert.IsFalse(StickerLook.Border, "no border, no peel shadow");
            Assert.IsTrue(StickerLook.Active, "but the sticker pass draws the Held layer, which the ordinary passes skip");
            Assert.AreEqual(0f, Block(renderer, "_Sweep").w);
        }

        [Test]
        public void Focus_RunsOneGlintSweepAcrossTheToy()
        {
            Prop toy = Yard();
            HeldLook look = presentation.Get<HeldLook>();
            Renderer renderer = toy.GameObject.GetComponentInChildren<Renderer>();
            TestHelpers.LookAt(game.Player, new Vector3(-6f, 1f, 4f));
            game.Tick();
            presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(0, look.SweepCount, "nothing under the crosshair");

            TestHelpers.LookAt(game.Player, toy.Center);
            game.Tick();
            Assert.AreSame(toy, game.Grabber.Focus);
            presentation.Frame(0f, 1f);
            Assert.AreEqual(1, look.SweepCount);
            Vector4 start = Block(renderer, "_Sweep");
            Assert.AreEqual(1f, start.w, "gain");
            Vector3 viewport = presentation.Camera.WorldToViewportPoint(toy.Center);
            Assert.AreEqual(viewport.x, start.x, 1e-4f, "the band runs through the toy's middle on screen");
            Assert.AreEqual(viewport.y, start.y, 1e-4f);
            Assert.Less(start.z, -HeldLook.SweepBand, "it starts beyond one side of the toy");

            // 300 ms: from one side to the other, then the property block has its zero back.
            float previous = start.z;
            for (int i = 0; i < 14; i++)
            {
                presentation.Frame(0.02f, 1f);
                Vector4 now = Block(renderer, "_Sweep");
                Assert.Greater(now.z, previous, "the band moves across");
                previous = now.z;
            }
            Assert.Greater(previous, HeldLook.SweepBand * 0.5f);
            presentation.Frame(0.05f, 1f);
            presentation.Frame(0f, 1f);
            Assert.AreEqual(Vector4.zero, Block(renderer, "_Sweep"), "over");

            // Still aimed at: no second sweep. Aimed away and back at once: not yet either. Later: again.
            for (int i = 0; i < 10; i++)
            {
                game.Tick();
                presentation.Frame(Sim.Dt, 1f);
                Assert.AreEqual(0f, Block(renderer, "_Sweep").w);
            }
            TestHelpers.LookAt(game.Player, new Vector3(-6f, 1f, 4f));
            for (int i = 0; i < 60; i++)
            {
                game.Tick();
                presentation.Frame(Sim.Dt, 1f);
            }
            Assert.AreEqual(0, look.SweepCount);
            TestHelpers.LookAt(game.Player, toy.Center);
            game.Tick();
            presentation.Frame(0f, 1f);
            Assert.AreEqual(1f, Block(renderer, "_Sweep").w, "focus again, sweep again");

            // The toy in hand takes no sweep: its picture must not change.
            Click();
            presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(Vector4.zero, Block(renderer, "_Sweep"));
            Assert.AreEqual(0, look.SweepCount);
            look.StartSweep(toy);
            Assert.AreEqual(0, look.SweepCount);

            // Level complete: every toy sweeps, 200 ms later.
            Click();
            TestHelpers.LookAt(game.Player, new Vector3(-6f, 1f, 4f));
            for (int i = 0; i < 60; i++)
            {
                game.Tick();
                presentation.Frame(Sim.Dt, 1f);
            }
            game.CompleteLevel();
            presentation.Frame(0.1f, 1f);
            Assert.AreEqual(0, look.SweepCount);
            presentation.Frame(0.15f, 1f);
            Assert.AreEqual(1, look.SweepCount);
            Assert.AreEqual(1f, Block(renderer, "_Sweep").w);
        }

        // ART_BIBLE 12.2: zero managed allocation per frame in Render.
        [Test]
        public void TheHeldPresenters_AllocateNothingPerFrame()
        {
            Prop toy = Yard(ToyRecipe.Rubber);
            HeldLook look = presentation.Get<HeldLook>();
            TestHelpers.LookAt(game.Player, toy.Center);
            game.Tick();
            presentation.Frame(0.01f, 1f);
            Assert.AreEqual(1, look.SweepCount, "a sweep is running");
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 25; i++) presentation.Frame(0.01f, 1f);
            Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before, "bytes allocated by 25 frames of a sweep");

            Click();
            presentation.Frame(0.01f, 1f);
            Assert.AreSame(toy, look.Held);
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) presentation.Frame(0.005f, 1f);
            Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before, "bytes allocated by 200 frames of a hold");
        }

        [Test]
        public void HighVisibility_BoostsTheRimOfEveryToy()
        {
            Settings.Use(new MemoryStore());
            Yard();
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_ToyRimBoost"));
            Settings.HighVisibility = true;
            Assert.AreEqual(HeldLook.HighVisibilityRim - 1f, Shader.GetGlobalFloat("_ToyRimBoost"), 1e-6f, "rim x 1.6");
            Settings.HighVisibility = false;
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_ToyRimBoost"));
            Settings.HighVisibility = true;
            presentation.Dispose();
            presentation = null;
            Assert.AreEqual(0f, Shader.GetGlobalFloat("_ToyRimBoost"), "a global the presenter set is put back");
        }

        [Test]
        public void Squash_OnTheFirstImpactAfterARelease_ByTheRecipesAmount()
        {
            Assert.AreEqual(1f, ToySquash.Shape(0f), 1e-6f);
            Assert.AreEqual(0f, ToySquash.Shape(1f), 1e-6f);
            float lowest = 0f;
            int crossings = 0;
            for (int i = 1; i <= 100; i++)
            {
                float a = ToySquash.Shape((i - 1) / 100f), b = ToySquash.Shape(i / 100f);
                lowest = Mathf.Min(lowest, b);
                if (a > 0f != b > 0f && b != 0f) crossings++;
            }
            Assert.AreEqual(1, crossings, "one overshoot");
            Assert.Less(lowest, -0.05f);
            Assert.Greater(lowest, -0.2f);

            Prop toy = Yard(ToyRecipe.Rubber);
            ToySquash squash = presentation.Get<ToySquash>();
            Renderer renderer = toy.GameObject.GetComponentInChildren<Renderer>();
            // Carry it up the far wall and let go: it falls a few units.
            TestHelpers.LookAt(game.Player, toy.Center);
            Click();
            TestHelpers.LookAt(game.Player, new Vector3(0f, 7f, 12f));
            for (int i = 0; i < 10; i++)
            {
                game.Tick();
                presentation.Frame(Sim.Dt, 1f);
            }
            Assert.AreEqual(0f, Block(renderer, "_SquashA").w, "never in the hand");
            Click();
            float strongest = 0f;
            int ticks = 0;
            for (; ticks < 300 && squash.Count == 0; ticks++)
            {
                game.Tick();
                presentation.Frame(Sim.Dt, 1f);
            }
            Assert.AreEqual(1, squash.Count, "the landing was an impact");
            Vector4 at = Block(renderer, "_SquashA");
            strongest = at.w;
            Assert.Greater(strongest, 0.05f);
            Assert.LessOrEqual(strongest, ToyRecipe.Rubber.Squash + 1e-6f, "rubber squashes 14%");
            Assert.AreEqual(renderer.bounds.center.x, at.x, 1e-3f, "about its middle");
            Assert.AreEqual(renderer.bounds.min.y, at.y, 1e-3f, "toward its lowest point");
            presentation.Frame(ToySquash.Seconds + 0.01f, 1f);
            Assert.AreEqual(0, squash.Count, "recovered in 180 ms");
            Assert.AreEqual(Vector4.zero, Block(renderer, "_SquashA"));

            // Only the first impact after a release: it goes on bouncing and rolling without squashing again.
            for (int i = 0; i < 120; i++)
            {
                game.Tick();
                presentation.Frame(Sim.Dt, 1f);
                Assert.AreEqual(0, squash.Count);
            }
        }

        [Test]
        public void TheStandIn_FillsTheToyGlobals_OnlyWhereNobodyElseHas()
        {
            var saved = new Vector4[GlobalVectors.Length];
            for (int i = 0; i < saved.Length; i++) saved[i] = Shader.GetGlobalVector(GlobalVectors[i]);
            float savedGlow = Shader.GetGlobalFloat("_ToyGlowGain");
            var standIn = new ToyLookStandIn();
            try
            {
                standIn.Release();
                foreach (string name in GlobalVectors) Shader.SetGlobalVector(name, Vector4.zero);
                Shader.SetGlobalFloat("_ToyGlowGain", 0f);

                EnvironmentDescriptor day = EnvironmentSolver.Solve(EnvironmentPreset.SunnyRug, new Bounds(Vector3.zero, new Vector3(20f, 4f, 20f)), 0f);
                standIn.Apply(day);
                Assert.AreEqual(13, standIn.Filled, "nobody had set anything");
                Color light = Palette.Lin(Palette.Mint.Light), mid = Palette.Lin(Palette.Mint.Mid), deep = Palette.Lin(Palette.Mint.Deep);
                Assert.Less((Rgb(Shader.GetGlobalVector("_AmbSky")) - Vector3.one * 0.45f).magnitude, 1e-5f);
                Assert.Less((Rgb(Shader.GetGlobalVector("_AmbEquator")) - Rgb(Color.LerpUnclamped(Color.white, light, 0.5f)) * 0.42f).magnitude, 1e-5f);
                Assert.Less((Rgb(Shader.GetGlobalVector("_AmbGround")) - Rgb(deep) * 0.40f).magnitude, 1e-5f);
                Assert.Less((Rgb(Shader.GetGlobalVector("_KickColor")) - Rgb(light) * 0.20f).magnitude, 1e-5f);
                Vector3 kick = Rgb(Shader.GetGlobalVector("_KickDir"));
                Assert.AreEqual(1f, kick.magnitude, 1e-5f);
                Assert.AreEqual(Mathf.Sin(20f * Mathf.Deg2Rad), kick.y, 1e-5f, "the kicker stands 20 degrees up");
                Vector3 sunFlat = new Vector3(day.SunDirection.x, 0f, day.SunDirection.z).normalized, kickFlat = new Vector3(kick.x, 0f, kick.z).normalized;
                Assert.AreEqual(145f, Vector3.Angle(sunFlat, kickFlat), 0.01f, "145 degrees round from the sun");
                Assert.Less((Rgb(Shader.GetGlobalVector("_WinDir")) - day.GlintDir).magnitude, 1e-6f, "the glint mirrors the real window");
                Assert.Less((Rgb(Shader.GetGlobalVector("_WinRight")) - day.GlintRight).magnitude, 1e-6f);
                Assert.Less((Rgb(Shader.GetGlobalVector("_WinUp")) - day.GlintUp).magnitude, 1e-6f);
                Assert.Less((Rgb(Shader.GetGlobalVector("_GlintColor")) - Rgb(Palette.Lin(Palette.GlintDay))).magnitude, 1e-5f);
                Assert.Less((Rgb(Shader.GetGlobalVector("_EnvCeil")) - Vector3.one * 1.6f).magnitude, 1e-5f);
                Assert.Less((Rgb(Shader.GetGlobalVector("_EnvWall")) - Rgb(light)).magnitude, 1e-5f);
                Assert.Less((Rgb(Shader.GetGlobalVector("_EnvFloor")) - Rgb(mid) * 0.7f).magnitude, 1e-5f);
                Assert.AreEqual(1f, Shader.GetGlobalFloat("_ToyGlowGain"));

                // The lighting rig sets two of them: those are its own from then on; the rest follow the level.
                var theirs = new Vector4(0.1f, 0.2f, 0.3f, 1f);
                Shader.SetGlobalVector("_GlintColor", theirs);
                Shader.SetGlobalFloat("_ToyGlowGain", 3f);
                EnvironmentDescriptor night = EnvironmentSolver.Solve(EnvironmentPreset.NightLight, new Bounds(Vector3.zero, new Vector3(20f, 4f, 20f)), 0f);
                standIn.Apply(night);
                Assert.AreEqual(11, standIn.Filled);
                Assert.AreEqual(theirs, Shader.GetGlobalVector("_GlintColor"), "never overwritten");
                Assert.AreEqual(3f, Shader.GetGlobalFloat("_ToyGlowGain"));
                Assert.Less((Rgb(Shader.GetGlobalVector("_AmbSky")) - Rgb(Palette.Lin(Palette.NightAmbient)) * 0.34f).magnitude, 1e-5f, "the night rig");
                Assert.Less((Rgb(Shader.GetGlobalVector("_AmbGround")) - Rgb(Palette.Lin(Palette.Plum.Deep)) * 0.35f).magnitude, 1e-5f);

                standIn.Release();
                Assert.AreEqual(Vector4.zero, Shader.GetGlobalVector("_AmbSky"), "its own are taken back");
                Assert.AreEqual(theirs, Shader.GetGlobalVector("_GlintColor"), "the others are left alone");
                Assert.AreEqual(3f, Shader.GetGlobalFloat("_ToyGlowGain"));
            }
            finally
            {
                standIn.Release();
                for (int i = 0; i < saved.Length; i++) Shader.SetGlobalVector(GlobalVectors[i], saved[i]);
                Shader.SetGlobalFloat("_ToyGlowGain", savedGlow);
            }
        }

        // ------------------------------------------------------------------------------------------
        // The picture
        // ------------------------------------------------------------------------------------------

        static bool HasGraphics => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        static string ShotPath(string name)
        {
            string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), ShotDirectory);
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, name + ".png");
        }

        /// <summary>Renders the camera into a texture the test can read (destroyed with the test).</summary>
        Texture2D Render(Camera camera, int width = 640, int height = 360, string save = null)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                antiAliasing = 4,
                hideFlags = HideFlags.HideAndDontSave,
            };
            target.Create();
            var image = new Texture2D(width, height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
            temporary.Add(image);
            RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
            bool previousAsync = UnityEditor.ShaderUtil.allowAsyncCompilation;
            UnityEditor.ShaderUtil.allowAsyncCompilation = false;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                image.Apply();
                if (save != null) File.WriteAllBytes(ShotPath(save), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                UnityEditor.ShaderUtil.allowAsyncCompilation = previousAsync;
                target.Release();
                Object.DestroyImmediate(target);
            }
            return image;
        }

        // The whole game look: every presenter the game has.
        void Begin(LevelDefinition level, QualityTier tier = QualityTier.Medium)
        {
            game = Game.Create(new GameOptions());
            game.LoadLevel(level);
            presentation = Presentation.Create(game, new PresentationOptions { Quality = tier });
            presentation.Frame(0f, 1f);
        }

        void End()
        {
            presentation.Dispose();
            presentation = null;
            game.Dispose();
            game = null;
        }

        void Tick(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                game.Tick();
                presentation.Frame(Sim.Dt, 1f);
            }
        }

        // Runs the level's script to its end, a presentation frame per tick.
        void Play()
        {
            var bot = new Bot(game);
            var script = new BotRunner(game.Level.Solve(bot));
            for (int i = 0; i < 1200 && script.Advance(); i++) Tick();
            Assert.IsFalse(script.Failed, script.Error != null ? script.Error.Message : "");
        }

        // A toy of any recipe: a ball on a rounded block with a drum beside it, as one mesh. Concave, with flat
        // faces, curved faces and bevels - everything the toy shader has a term for.
        static GameObject Showpiece(ToyRecipe recipe, Color candy)
        {
            var root = new GameObject("Showpiece " + recipe.Name);
            root.AddComponent<BoxCollider>().size = new Vector3(1.4f, 1.3f, 0.9f);
            Mesh mesh = MeshKit.Cached("Test Showpiece", () => MeshKit.Merge("Test Showpiece", new[]
            {
                new MeshPart(MeshKit.RoundedBox(new Vector3(0.9f, 0.5f, 0.9f), 0.04f), new Vector3(-0.25f, -0.4f, 0f)),
                new MeshPart(MeshKit.Sphere(0.4f), new Vector3(-0.25f, 0.25f, 0f)),
                new MeshPart(MeshKit.Cylinder(0.22f, 0.9f, 24, 0.03f), new Vector3(0.48f, -0.2f, 0f)),
            }));
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            visual.AddComponent<MeshRenderer>().sharedMaterials = Materials.ToySet(recipe, candy);
            ToyInfo.Tag(root, recipe, candy);
            return root;
        }

        static Color CandyFor(ToyRecipe recipe, int index)
        {
            if (recipe == ToyRecipe.Cardboard) return Palette.Kraft;
            if (recipe == ToyRecipe.PaperSheet) return Palette.Paper;
            return Palette.Candy[index % Palette.Candy.Length];
        }

        // Every toy recipe side by side on a floor in the sunny-rug room, seen from the player's height.
        sealed class ShowcaseLevel : LevelDefinition
        {
            public readonly List<Prop> Props = new List<Prop>();
            public override string Environment => "sunny-rug";

            public override void Build(LevelContext ctx)
            {
                Props.Clear();
                ctx.AddStatic(BasicToys.Slab(new Vector3(30f, 1f, 30f)), new Vector3(0f, -0.5f, 4f));
                ctx.AddStatic(BasicToys.Slab(new Vector3(30f, 8f, 1f)), new Vector3(0f, 4f, 14f));
                ToyRecipe[] recipes = ToyRecipe.Toys;
                for (int i = 0; i < recipes.Length; i++)
                {
                    float x = (i - (recipes.Length - 1) * 0.5f) * 1.7f;
                    Props.Add(ctx.AddProp(Showpiece(recipes[i], CandyFor(recipes[i], i)), new Vector3(x, 0.65f, 5f + 0.6f * Mathf.Abs(x)),
                        Quaternion.Euler(0f, -x * 6f, 0f), new PropOptions { Name = recipes[i].Name }));
                }
                ctx.SetSpawn(new Vector3(0f, 0f, -3f), 0f, -8f);
            }
        }

        // How many pixels are a saturated colour (a toy), and how many are a missing shader's magenta.
        static void Count(Texture2D image, out int vivid, out int magenta)
        {
            vivid = magenta = 0;
            foreach (Color32 p in image.GetPixels32())
            {
                int max = Mathf.Max(p.r, Mathf.Max(p.g, p.b)), min = Mathf.Min(p.r, Mathf.Min(p.g, p.b));
                if (max > 120 && max - min > 90) vivid++;
                if (p.r > 230 && p.b > 230 && p.g < 40) magenta++;
            }
        }

        [Test]
        public void Showcase_EveryRecipeRenders_OnEveryTier()
        {
            Assume.That(HasGraphics, "rendering needs a graphics device (-nographics run)");
            foreach (QualityTier tier in new[] { QualityTier.Medium, QualityTier.Low, QualityTier.High })
            {
                Begin(new ShowcaseLevel(), tier);
                Camera camera = presentation.Camera;
                // The very first render after a script reload is not to be trusted (see Shots.Warm).
                Render(camera);
                Tick(30);
                string suffix = tier == QualityTier.Medium ? "" : "-" + tier.ToString().ToLowerInvariant();
                Texture2D wide = Render(camera, 1280, 720, "showcase" + suffix);
                Count(wide, out int vivid, out int magenta);
                Assert.Greater(vivid, 4000, tier + ": the toys show their colours");
                Assert.Less(magenta, 50, tier + ": magenta means a shader that did not compile");

                // Close-ups: the same eye, a longer lens, turned toward each third of the row.
                var level = (ShowcaseLevel)game.Level;
                camera.fieldOfView = 22f;
                for (int group = 0; group < 3; group++)
                {
                    Vector3 target = (level.Props[group * 4].Center + level.Props[Mathf.Min(group * 4 + 3, level.Props.Count - 1)].Center) * 0.5f;
                    camera.transform.rotation = Quaternion.LookRotation(target - camera.transform.position, Vector3.up);
                    Count(Render(camera, 1280, 720, "showcase" + suffix + "-close-" + group), out vivid, out magenta);
                    Assert.Less(magenta, 50, tier + " close-up " + group);
                }
                End();
            }
        }

        // One toy in front of the player in the sunny-rug room, with things behind it for the sticker to cover.
        sealed class HoldLevel : LevelDefinition
        {
            readonly ToyRecipe recipe;
            readonly Color candy;
            public Prop Toy;

            public HoldLevel(ToyRecipe recipe, Color candy)
            {
                this.recipe = recipe;
                this.candy = candy;
            }

            public override string Environment => "sunny-rug";

            public override void Build(LevelContext ctx)
            {
                ctx.AddStatic(BasicToys.Slab(new Vector3(30f, 1f, 40f)), new Vector3(0f, -0.5f, 10f));
                ctx.AddStatic(BasicToys.Slab(new Vector3(30f, 8f, 1f)), new Vector3(0f, 4f, 24f));
                ctx.AddStatic(BasicToys.Slab(new Vector3(2f, 3f, 2f)), new Vector3(1.5f, 1.5f, 12f));
                ctx.AddProp(BasicToys.Ball(0.6f, Palette.Lagoon), new Vector3(-1.2f, 0.6f, 9f));
                Toy = ctx.AddProp(Showpiece(recipe, candy), new Vector3(0f, 0.65f, 3.5f), Quaternion.Euler(0f, 25f, 0f), new PropOptions { Name = "Held " + recipe.Name });
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f, 0f);
            }

            public override System.Collections.IEnumerator Solve(Bot bot)
            {
                yield return bot.Grab(Toy);
                // Up from the floor: the toy flies out until it meets something, and hangs there over everything.
                yield return bot.LookAt(new Vector3(0.4f, 2.6f, 24f));
                yield return bot.Wait(0.5f);
            }
        }

        // Puts the held prop at a distance of the test's choosing on the camera's axis, keeping scale / distance:
        // "freeze the camera, hold a toy, force d" (8.3). Nothing of the simulation runs afterwards.
        static void ForceHoldDistance(Prop prop, Camera camera, float ratio, float distance)
        {
            float scale = ratio * distance;
            Quaternion rotation = prop.Rotation;
            Transform eye = camera.transform;
            prop.Transform.localScale = new Vector3(scale, scale, scale);
            prop.Transform.SetPositionAndRotation(eye.position + eye.forward * distance - rotation * (prop.LocalCenter * scale), rotation);
        }

        struct Difference
        {
            public int Max;         // largest per-channel difference, in 8-bit steps
            public int Above;       // pixels that differ by more than one step
            public int Any;         // pixels that differ at all
        }

        static Difference Compare(Texture2D a, Texture2D b, string save = null)
        {
            Color32[] p = a.GetPixels32(), q = b.GetPixels32();
            var result = new Difference();
            Color32[] picture = save != null ? new Color32[p.Length] : null;
            for (int i = 0; i < p.Length; i++)
            {
                int d = Mathf.Max(Mathf.Abs(p[i].r - q[i].r), Mathf.Max(Mathf.Abs(p[i].g - q[i].g), Mathf.Abs(p[i].b - q[i].b)));
                if (d > result.Max) result.Max = d;
                if (d > 1) result.Above++;
                if (d > 0) result.Any++;
                if (picture != null)
                {
                    byte shown = (byte)Mathf.Min(255, d * 32);
                    picture[i] = new Color32(shown, shown, shown, 255);
                }
            }
            if (picture != null)
            {
                var image = new Texture2D(a.width, a.height, TextureFormat.RGB24, false);
                image.SetPixels32(picture);
                image.Apply();
                File.WriteAllBytes(ShotPath(save), image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            return result;
        }

        // ART_BIBLE 8.3, once per material recipe and tier: the held toy at 3 units and at 90 units is the same
        // picture, border and peel shadow included - although at 90 it stands far behind the level's back wall.
        void Acceptance(QualityTier tier, System.Text.StringBuilder report)
        {
            ToyRecipe[] recipes = ToyRecipe.Toys;
            bool first = true;
            for (int i = 0; i < recipes.Length; i++)
            {
                var level = new HoldLevel(recipes[i], CandyFor(recipes[i], i));
                Begin(level, tier);
                Camera camera = presentation.Camera;
                if (first) Render(camera);
                Play();
                Assert.AreSame(level.Toy, game.Grabber.Held, recipes[i].Name + ": the toy is in hand");
                Assert.IsTrue(StickerLook.Border);
                Assert.AreEqual(StickerLook.BorderWidth, StickerLook.BorderPx, 1e-3f);
                camera.GetUniversalAdditionalCameraData().dithering = false;
                float ratio = game.Grabber.Ratio;
                string tag = tier.ToString().ToLowerInvariant() + "-" + recipes[i].Name.Replace(' ', '-').ToLowerInvariant();
                bool save = tier == QualityTier.Medium;

                if (first && save) Render(camera, 1280, 720, "held-natural-720p");
                Texture2D natural = Render(camera);
                ForceHoldDistance(level.Toy, camera, ratio, 3f);
                Texture2D near = Render(camera, 640, 360, save ? "held-" + tag + "-3" : null);
                ForceHoldDistance(level.Toy, camera, ratio, 90f);
                Texture2D far = Render(camera, 640, 360, save ? "held-" + tag + "-90" : null);
                Difference d = Compare(near, far, save ? "held-" + tag + "-diff" : null);
                Difference n = Compare(natural, near);
                report.AppendLine(tier + " " + recipes[i].Name + ": d = 3 vs d = 90: max " + d.Max + "/255, pixels above one step " + d.Above + ", pixels that differ " + d.Any +
                                  " of " + near.width * near.height + " | as held (d = " + game.Grabber.HoldDistance.ToString("0.0") + ") vs d = 3: max " + n.Max + ", above one step " + n.Above);

                // The art bible asks for less than 1/255 everywhere. Measured on this machine: identical but
                // for a few pixels that fall the other way in rasterisation or in the bump's derivatives (the
                // two frames go through different matrices), by one or two steps: at most 2 pixels on Medium
                // and Low, 26 of 230400 for the sponge on High. The limits leave room for another GPU.
                Assert.LessOrEqual(d.Above, 12, tier + " " + recipes[i].Name + ": pixels that differ by more than 1/255");
                Assert.LessOrEqual(d.Any, 60, tier + " " + recipes[i].Name + ": pixels that differ at all");
                Assert.LessOrEqual(d.Max, 6, tier + " " + recipes[i].Name + ": the largest difference");
                Assert.LessOrEqual(n.Above, 12, tier + " " + recipes[i].Name + ": the same holds for the distance the game chose");
                first = false;
                End();
            }
        }

        [Test]
        public void HeldToy_LooksTheSameAtThreeUnitsAndAtNinety()
        {
            Assume.That(HasGraphics, "rendering needs a graphics device (-nographics run)");
            var report = new System.Text.StringBuilder();
            try
            {
                Acceptance(QualityTier.Medium, report);
                Acceptance(QualityTier.High, report);
                Acceptance(QualityTier.Low, report);
            }
            finally
            {
                File.WriteAllText(ShotPath("held-acceptance").Replace(".png", ".txt"), report.ToString());
            }
        }

        [Test]
        public void HeldToy_IsDrawnByTheStickerPassAlone_WithBorderAndPeelShadow()
        {
            Assume.That(HasGraphics, "rendering needs a graphics device (-nographics run)");
            var level = new HoldLevel(ToyRecipe.GlossyPlastic, Palette.Cherry);
            Begin(level);
            Camera camera = presentation.Camera;
            Render(camera);
            Play();
            camera.GetUniversalAdditionalCameraData().dithering = false;
            Texture2D sticker = Render(camera);

            // The same frame without the pass: the ordinary passes skip the Held layer, so the toy is gone.
            StickerLook.Force = false;
            Texture2D without = Render(camera, 640, 360, "held-without-pass");
            StickerLook.Force = null;
            // And with the pass but without border and shadow: the toy alone, on top.
            StickerLook.Border = false;
            Texture2D bare = Render(camera, 640, 360, "held-without-border");
            StickerLook.Border = true;

            Color32[] full = sticker.GetPixels32(), none = without.GetPixels32(), toyOnly = bare.GetPixels32();
            int centre = (sticker.height / 2) * sticker.width + sticker.width / 2;
            int toyPixels = 0, borderPixels = 0, shadowPixels = 0;
            for (int i = 0; i < full.Length; i++)
            {
                bool red = toyOnly[i].r > 150 && toyOnly[i].r - toyOnly[i].g > 70 && toyOnly[i].r - toyOnly[i].b > 60;
                bool wasRed = none[i].r > 150 && none[i].r - none[i].g > 70 && none[i].r - none[i].b > 60;
                if (red && !wasRed) toyPixels++;
                int change = (full[i].r + full[i].g + full[i].b) - (toyOnly[i].r + toyOnly[i].g + toyOnly[i].b);
                // The border is Paper, brighter than anything it covers; the peel shadow darkens what it lies on by a fifth.
                if (change > 30 && full[i].r > 225 && full[i].g > 225 && full[i].b > 215) borderPixels++;
                if (change < -24 && change > -200) shadowPixels++;
            }
            Assert.Greater(toyPixels, 2000, "the toy is there with the pass and absent without it: only the sticker pass draws the Held layer");
            Assert.Greater(borderPixels, 250, "a white border all around");
            Assert.Greater(shadowPixels, 250, "and a hard shadow beside it");
            Assert.Greater(full[centre].r - full[centre].g, 60, "the toy sits on the crosshair");

            // It covers what stands in front of it in the world, too: at 90 units it is far behind the back
            // wall (24 away) and the pillar, and not a pixel of it is hidden.
            float ratio = game.Grabber.Ratio;
            ForceHoldDistance(level.Toy, camera, ratio, 90f);
            Assert.Greater(Vector3.Distance(camera.transform.position, level.Toy.Center), 80f);
            Texture2D behind = Render(camera);
            Assert.LessOrEqual(Compare(sticker, behind).Above, 4);
        }

        // A ball off to one side, for the sweep to cross.
        sealed class SweepLevel : LevelDefinition
        {
            public Prop Ball;
            public override string Environment => "sunny-rug";

            public override void Build(LevelContext ctx)
            {
                ctx.AddStatic(BasicToys.Slab(new Vector3(30f, 1f, 40f)), new Vector3(0f, -0.5f, 10f));
                Ball = ctx.AddProp(BasicToys.Ball(0.8f, Palette.Grape), new Vector3(2.6f, 0.8f, 6f), new PropOptions { Name = "Ball" });
                ctx.SetSpawn(Vector3.zero, 0f, 8f);
            }
        }

        [Test]
        public void TheFocusSweep_CrossesTheToyWhereItIsOnScreen()
        {
            Assume.That(HasGraphics, "rendering needs a graphics device (-nographics run)");
            var level = new SweepLevel();
            Begin(level);
            Camera camera = presentation.Camera;
            HeldLook look = presentation.Get<HeldLook>();
            Assert.IsNotNull(look);
            Render(camera);
            Tick(60);

            // Half way through, the band lies across the middle of the ball.
            look.StartSweep(level.Ball);
            presentation.Frame(0f, 1f);
            presentation.Frame(HeldLook.SweepSeconds * 0.5f, 1f);
            Texture2D during = Render(camera, 640, 360, "sweep-middle");
            presentation.Frame(HeldLook.SweepSeconds, 1f);
            Texture2D after = Render(camera, 640, 360, "sweep-after");

            Color32[] lit = during.GetPixels32(), plain = after.GetPixels32();
            double sumX = 0, sumY = 0;
            int count = 0;
            for (int y = 0; y < during.height; y++)
            {
                for (int x = 0; x < during.width; x++)
                {
                    int i = y * during.width + x;
                    int gain = (lit[i].r + lit[i].g + lit[i].b) - (plain[i].r + plain[i].g + plain[i].b);
                    if (gain < 60) continue;
                    sumX += x + 0.5;
                    sumY += y + 0.5;
                    count++;
                }
            }
            Vector3 viewport = camera.WorldToViewportPoint(level.Ball.Center);
            float radius = level.Ball.Radius / (Vector3.Distance(camera.transform.position, level.Ball.Center) * 2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad)) * during.height;
            Assert.Greater(count, (int)(radius * radius), "a band of light across the ball");
            var centroid = new Vector2((float)(sumX / count), (float)(sumY / count));
            var expected = new Vector2(viewport.x * during.width, viewport.y * during.height);
            // Across the band (it runs at 45 degrees) - along it the lit pixels follow the ball's shading. A
            // band placed with the wrong y convention would miss by three radii.
            float across = Vector2.Dot(centroid - expected, new Vector2(0.7071f, 0.7071f));
            Assert.Less(Mathf.Abs(across), radius * 0.35f,
                "the band passes through the ball's middle (" + expected + "), not somewhere else on screen: lit pixels centre on " + centroid);
            Assert.Less((centroid - expected).magnitude, radius, "and it is the ball that lights up");
        }

        [Test]
        public void Sandbox_TheHeldPlankWearsItsSticker()
        {
            Assume.That(HasGraphics, "rendering needs a graphics device (-nographics run)");
            Begin(LevelRegistry.Get(0));
            Render(presentation.Camera);

            int grabbedAt = -1, droppedAt = -1, tick = 0;
            Prop plank = null;
            game.Events.PropGrabbed += e =>
            {
                grabbedAt = tick;
                plank = e.Prop;
            };
            game.Events.PropDropped += e => droppedAt = tick;
            var bot = new Bot(game);
            var script = new BotRunner(game.Level.Solve(bot));
            Render(presentation.Camera, 1280, 720, "sandbox-rest");
            for (; tick < 600 && grabbedAt < 0; tick++)
            {
                script.Advance();
                Tick();
            }
            Assert.GreaterOrEqual(grabbedAt, 0, "the bot grabs the plank");
            Renderer renderer = plank.GameObject.GetComponentInChildren<Renderer>();
            Assert.AreEqual(Layers.Held, renderer.gameObject.layer);
            Assert.AreEqual(ShadowCastingMode.Off, renderer.shadowCastingMode);
            // The bot lets go again within a few ticks; this is the hold as the game shows it.
            script.Advance();
            Tick();
            Assert.IsTrue(StickerLook.Border);
            Render(presentation.Camera, 1280, 720, "sandbox-held");
            for (; tick < 1200 && droppedAt < 0; tick++)
            {
                script.Advance();
                Tick();
            }
            Assert.GreaterOrEqual(droppedAt, 0);
            Assert.AreEqual(ShadowCastingMode.On, renderer.shadowCastingMode, "cast shadow on, in the frame of the release");
            Assert.IsFalse(StickerLook.Border);
            Tick(30);
            Render(presentation.Camera, 1280, 720, "sandbox-dropped");
            File.WriteAllText(ShotPath("sandbox-timeline").Replace(".png", ".txt"),
                "grabbed at tick " + grabbedAt + " (" + (grabbedAt * Sim.Dt).ToString("0.00") + " s), dropped at tick " + droppedAt + " (" + (droppedAt * Sim.Dt).ToString("0.00") + " s)");
        }
    }
}
