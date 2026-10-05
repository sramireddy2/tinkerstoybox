using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using Toybox.Art;
using Toybox.EditorTools;
using Toybox.Engine;
using Toybox.Platform;
using Toybox.Toys;
using Toybox.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.TestTools.Constraints;
using UnityEngine.UI;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;
using Progress = Toybox.Platform.Progress;

namespace Toybox.Tests
{
    /// <summary>What the UI tests share: the game's own two UI presenters by name, and a small level.</summary>
    static class UiTestKit
    {
        public static PresenterRegistry.Entry Entry<T>() where T : IPresenter =>
            new PresenterRegistry.Entry(typeof(T), typeof(T).GetCustomAttribute<PresenterAttribute>());

        /// <summary>The HUD and the menus, and nothing else: no lighting, no audio, no stand-ins.</summary>
        public static List<PresenterRegistry.Entry> Own()
        {
            var own = new List<PresenterRegistry.Entry> { Entry<HudPresenter>(), Entry<MenuPresenter>() };
            // In the order the registry would give them.
            own.Sort((a, b) => a.Order.CompareTo(b.Order));
            return own;
        }

        public static LevelList Levels(int count)
        {
            var ids = new int[count];
            for (int i = 0; i < count; i++) ids[i] = i + 1;
            return new LevelList(ids, id => new UiLevel(id));
        }

        public static float Angle(RectTransform rect)
        {
            float z = rect.localEulerAngles.z;
            return z > 180f ? z - 360f : z;
        }

        /// <summary>
        /// What some rects take up of their canvas, measured on the rects themselves: in canvas pixels from
        /// the canvas's middle, x to the right and y up (the coordinates of <see cref="HudFrame"/>).
        /// </summary>
        public static Rect OnCanvas(UiRoot root, params RectTransform[] rects)
        {
            var corners = new Vector3[4];
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (RectTransform rect in rects)
            {
                rect.GetWorldCorners(corners);
                foreach (Vector3 corner in corners)
                {
                    Vector2 point = root.Rect.InverseTransformPoint(corner);
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>What a sticker takes up of its canvas as it is drawn: its face, its border and its peel shadow.</summary>
        public static Rect Footprint(UiRoot root, Sticker sticker) =>
            OnCanvas(root, sticker.Face.rectTransform, sticker.Border.rectTransform, sticker.Shadow.rectTransform);

        public static void AreEqual(Rect expected, Rect actual, float tolerance, string message)
        {
            Assert.AreEqual(expected.xMin, actual.xMin, tolerance, message + " (left)");
            Assert.AreEqual(expected.xMax, actual.xMax, tolerance, message + " (right)");
            Assert.AreEqual(expected.yMin, actual.yMin, tolerance, message + " (bottom)");
            Assert.AreEqual(expected.yMax, actual.yMax, tolerance, message + " (top)");
        }
    }

    /// <summary>A walled room, a block to grab four units ahead and an exit off to the side. Its number tells the levels apart.</summary>
    sealed class UiLevel : LevelDefinition
    {
        public readonly int Number;
        public Prop Block, Plate;

        public UiLevel(int number) => Number = number;

        public override string Title => "Test Level " + Number;
        public override string Blurb => "Carry the block to the door.";
        public override string[] Hints => new[] { "The block is light.", "The door is to the right." };
        // The art bible's table, so the catalogue's cards have their dips.
        public override string Environment => EnvironmentPreset.KeyForLevel(Number, true);

        public override void Build(LevelContext ctx)
        {
            TestHelpers.Room(ctx, 20f, 12f);
            Block = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 4f), new PropOptions { Name = "Block" });
            Plate = ctx.AddProp(BasicToys.Block(new Vector3(1f, 0.2f, 1f)), new Vector3(-3f, 0.1f, 4f), new PropOptions { Name = "Plate", AllowPitch = false });
            ctx.AddExit(new Vector3(6f, 1f, 6f), new Vector3(2f, 2f, 2f));
            ctx.SetSpawn(Vector3.zero, 0f);
        }

        public override IEnumerator Solve(Bot bot)
        {
            yield return bot.WalkTo(new Vector3(6f, 0f, 6f), 0.5f);
            yield return bot.Until(() => bot.Game.LevelCompleted, 3f);
        }
    }

    // ----------------------------------------------------------------------------------------------
    // The building blocks: the atlas, the fonts, the sticker and its motion (ART_BIBLE 10.1 - 10.4)
    // ----------------------------------------------------------------------------------------------

    public class UiKitTests
    {
        GameObject holder;
        RectTransform parent;
        MemoryStore store;

        [SetUp]
        public void MakeAParent()
        {
            store = new MemoryStore();
            Settings.Use(store);
            holder = new GameObject("UI Test Parent", typeof(RectTransform)) { hideFlags = HideFlags.DontSave };
            parent = (RectTransform)holder.transform;
            parent.sizeDelta = new Vector2(1920f, 1080f);
        }

        [TearDown]
        public void CleanUp()
        {
            if (holder != null) Object.DestroyImmediate(holder);
            UiFonts.Folder = UiFonts.DefaultFolder;
            Settings.Use(null);
        }

        [Test]
        public void TheAtlas_IsOne256Texture_HoldingEveryShapeAndGlyph()
        {
            Texture2D texture = UiAtlas.Texture;
            Assert.AreEqual(256, texture.width);
            Assert.AreEqual(256, texture.height);
            Assert.AreEqual(TextureFormat.RGBA32, texture.format);

            var rects = new List<RectInt>();
            foreach (UiShape shape in Enum.GetValues(typeof(UiShape)))
            {
                Sprite sprite = UiAtlas.Sprite(shape);
                Assert.IsNotNull(sprite, shape.ToString());
                Assert.AreSame(texture, sprite.texture, shape + " is on the one atlas, so every image batches");
                // The pill is the circle, nine-sliced.
                if (shape != UiShape.Pill) rects.Add(UiAtlas.Rect(shape));
            }
            foreach (UiGlyph glyph in Enum.GetValues(typeof(UiGlyph)))
            {
                Assert.AreSame(texture, UiAtlas.Sprite(glyph).texture, glyph.ToString());
                rects.Add(UiAtlas.Rect(glyph));
            }
            for (int i = 0; i < rects.Count; i++)
            {
                Assert.IsTrue(rects[i].xMin >= 0 && rects[i].yMin >= 0 && rects[i].xMax <= 256 && rects[i].yMax <= 256, "inside the atlas: " + rects[i]);
                for (int j = i + 1; j < rects.Count; j++)
                    Assert.IsFalse(rects[i].Overlaps(rects[j]), rects[i] + " overlaps " + rects[j]);
            }

            // Rounded rectangle: radius 18, nine-slice border 24. Pill: sliced down the middle.
            Assert.AreEqual(Vector4.one * 24f, UiAtlas.Sprite(UiShape.Panel).border);
            Assert.AreEqual(Vector4.one * UiAtlas.PillBorder, UiAtlas.Sprite(UiShape.Pill).border);
            Assert.AreEqual(Vector4.zero, UiAtlas.Sprite(UiShape.Circle).border);

            int builds = UiAtlas.Builds;
            UiAtlas.Sprite(UiShape.Figure);
            UiAtlas.Sprite(UiGlyph.Key);
            Assert.AreEqual(builds, UiAtlas.Builds, "drawn once");
        }

        [Test]
        public void TheAtlas_IsAntiAliasedFromADistanceFunction()
        {
            Color32[] pixels = UiAtlas.Texture.GetPixels32();
            Color32 At(RectInt rect, int x, int y) => pixels[(rect.y + y) * UiAtlas.Size + rect.x + x];

            RectInt circle = UiAtlas.Rect(UiShape.Circle);
            Assert.AreEqual(255, At(circle, 32, 32).a, "solid in the middle");
            Assert.AreEqual(0, At(circle, 1, 1).a, "empty in the corner");
            // Along the middle row the edge is a ramp, not a step: some pixel is neither empty nor solid.
            int partial = 0;
            for (int x = 0; x < 8; x++)
            {
                byte a = At(circle, x, 32).a;
                if (a > 0 && a < 255) partial++;
            }
            Assert.GreaterOrEqual(partial, 1);
            // Shapes are white everywhere, so filtering at an edge never darkens it.
            Assert.AreEqual(255, At(circle, 1, 1).r);

            RectInt panel = UiAtlas.Rect(UiShape.Panel);
            Assert.AreEqual(255, At(panel, 28, 28).a);
            Assert.AreEqual(0, At(panel, 1, 1).a, "the corner is rounded away");
            Assert.AreEqual(255, At(panel, 28, 2).a, "the straight edge is not");

            // The four-pane mark: panes, and a gap between them.
            RectInt mark = UiAtlas.Rect(UiShape.FourPane);
            Assert.AreEqual(255, At(mark, 12, 12).a);
            Assert.AreEqual(255, At(mark, 36, 36).a);
            Assert.AreEqual(0, At(mark, 24, 12).a);
            Assert.AreEqual(0, At(mark, 12, 24).a);

            // The reticle pane keeps its colours: Paper inside, an Ink edge.
            RectInt pane = UiAtlas.Rect(UiShape.Pane);
            Color32 inside = At(pane, 10, 10), edge = At(pane, 2, 10);
            Assert.Greater(inside.r, 240);
            Assert.Less(edge.r, 90, "the edge is Ink");
            Assert.Greater(edge.a, 200);
        }

        [Test]
        public void HeroGlyphs_FollowTheCampaign()
        {
            Assert.AreEqual(UiGlyph.Wedge, UiAtlas.GlyphFor("cheese-wedge", 1));
            Assert.AreEqual(UiGlyph.Key, UiAtlas.GlyphFor("the-keyhole", 99));
            Assert.AreEqual(UiGlyph.Gear, UiAtlas.GlyphFor("rube-goldberg", 15));
            Assert.AreEqual(UiGlyph.Apple, UiAtlas.GlyphFor("something-else", 3), "by its place in the campaign");
            Assert.AreEqual(UiGlyph.Block, UiAtlas.GlyphFor("sandbox", 0));
            Assert.AreEqual(UiGlyph.Block, UiAtlas.GlyphFor(null, 40));
        }

        [Test]
        public void TheFonts_AreStatic_HoldEveryCharacter_AndFitOneAtlasEach()
        {
            Assert.IsTrue(UiFonts.Available);
            Assert.IsTrue(UiFonts.HasDisplay, "run Toybox.EditorTools.ProjectSetup.Run: Resources/Fonts/" + UiFonts.DisplayName + " is missing");
            Assert.IsTrue(UiFonts.HasBody, "run Toybox.EditorTools.ProjectSetup.Run: Resources/Fonts/" + UiFonts.BodyName + " is missing");
            Assert.AreEqual(95 + 12, UiFonts.Characters.Length, "ASCII 32-126 and the twelve marks");

            foreach (UiFont which in new[] { UiFont.Display, UiFont.Body })
            {
                TMP_FontAsset font = UiFonts.Font(which);
                Assert.AreEqual(AtlasPopulationMode.Static, font.atlasPopulationMode, which + ": nothing is rasterised at runtime");
                Assert.AreEqual(1, font.atlasTextures.Length, which + ": one atlas");
                Assert.AreEqual(1024, font.atlasTexture.width);
                Assert.AreEqual(1024, font.atlasTexture.height);
                Assert.IsFalse(font.atlasTexture.isReadable, "the atlas lives on the GPU only");
                foreach (char c in UiFonts.Characters)
                    Assert.IsTrue(font.HasCharacter(c), which + " lacks '" + c + "' (U+" + ((int)c).ToString("X4") + ")");
                // No ligature may point at a glyph that is not in the atlas.
                Assert.AreEqual(0, font.fontFeatureTable.ligatureRecords.Count);
                Assert.Greater(font.fontFeatureTable.glyphPairAdjustmentRecords.Count, 0, "kerning is kept");
                Assert.Less(font.fontFeatureTable.glyphPairAdjustmentRecords.Count, 107 * 107);
            }
            Assert.AreEqual("Unbounded", UiFonts.Display.faceInfo.familyName);
            Assert.AreEqual(72, UiFonts.Display.faceInfo.pointSize, "display: sampling size 72");
            Assert.AreEqual(48, UiFonts.Body.faceInfo.pointSize, "body: sampling size 48");
            // What one face lacks is taken from the other.
            CollectionAssert.Contains(UiFonts.Body.fallbackFontAssetTable, UiFonts.Display);
        }

        [Test]
        public void TheMaterialPresets_AreAssets_AndTheStickerStyleCarriesItsKeywords()
        {
            Material sticker = UiFonts.Preset(UiFont.Display, sticker: true);
            Material plain = UiFonts.Preset(UiFont.Display);
            Material body = UiFonts.Preset(UiFont.Body);
            Assert.IsNotNull(sticker);
            Assert.IsNotNull(plain);
            Assert.IsNotNull(body);
            Assert.AreNotSame(sticker, plain);
            foreach (Material preset in new[] { sticker, plain, body })
                Assert.IsTrue(EditorUtility.IsPersistent(preset), preset.name + " is an asset: shader_feature keywords are stripped from a build otherwise");

            // Outline in Paper, thickness 0.2; underlay in Ink at 18%, offset (0.5, -0.7), no softness.
            Assert.IsTrue(sticker.IsKeywordEnabled(ShaderUtilities.Keyword_Outline));
            Assert.IsTrue(sticker.IsKeywordEnabled(ShaderUtilities.Keyword_Underlay));
            Assert.AreEqual(0.2f, sticker.GetFloat(ShaderUtilities.ID_OutlineWidth), 1e-4f);
            Assert.IsTrue(Palette.Same(Palette.Paper, sticker.GetColor(ShaderUtilities.ID_OutlineColor)));
            Color underlay = sticker.GetColor(ShaderUtilities.ID_UnderlayColor);
            Assert.AreEqual(0.18f, underlay.a, 1e-3f);
            Assert.AreEqual(Palette.Ink.r, underlay.r, 1e-3f);
            Assert.AreEqual(0.5f, sticker.GetFloat(ShaderUtilities.ID_UnderlayOffsetX), 1e-4f);
            Assert.AreEqual(-0.7f, sticker.GetFloat(ShaderUtilities.ID_UnderlayOffsetY), 1e-4f);
            Assert.AreEqual(0f, sticker.GetFloat(ShaderUtilities.ID_UnderlaySoftness), 1e-4f);
            Assert.IsFalse(plain.IsKeywordEnabled(ShaderUtilities.Keyword_Outline));
            Assert.IsFalse(plain.IsKeywordEnabled(ShaderUtilities.Keyword_Underlay));
            Assert.IsFalse(body.IsKeywordEnabled(ShaderUtilities.Keyword_Outline));

            // A preset draws from its own font's atlas.
            Assert.AreSame(UiFonts.Display.atlasTexture, sticker.GetTexture(ShaderUtilities.ID_MainTex));
            Assert.AreSame(UiFonts.Display.atlasTexture, plain.GetTexture(ShaderUtilities.ID_MainTex));
            Assert.AreSame(UiFonts.Body.atlasTexture, body.GetTexture(ShaderUtilities.ID_MainTex));
            Assert.AreSame(UiFonts.Display.material.shader, sticker.shader);
        }

        [Test]
        public void WithoutTheBakedFonts_EveryLabelUsesTextMeshProsDefault()
        {
            UiFonts.Folder = "NoSuchFolder";
            Assert.IsTrue(UiFonts.Available, "the UI still works");
            Assert.IsFalse(UiFonts.HasDisplay);
            Assert.AreSame(TMP_Settings.defaultFontAsset, UiFonts.Display);
            Assert.AreSame(TMP_Settings.defaultFontAsset, UiFonts.Body);
            Assert.IsNull(UiFonts.Preset(UiFont.Display, true), "the stand-in keeps its own material");

            TextMeshProUGUI label = UiKit.Label(parent, "Label", "Fallback", UiFont.Display, 40f, UiTheme.Ink);
            Assert.AreSame(TMP_Settings.defaultFontAsset, label.font);

            UiFonts.Folder = UiFonts.DefaultFolder;
            Assert.IsTrue(UiFonts.HasDisplay);
            Assert.AreEqual(UiFonts.DisplayName, UiFonts.Display.name);
        }

        [Test]
        public void Labels_ShrinkToFit_DownToSixtyPercent_AndNeverTakeClicks()
        {
            TextMeshProUGUI title = UiKit.Label(parent, "Title", "THE CHEESE WEDGE", UiFont.Display, UiTheme.TitleSize, UiTheme.Ink, sticker: true);
            Assert.IsTrue(title.enableAutoSizing);
            Assert.AreEqual(40f, title.fontSizeMax);
            Assert.AreEqual(24f, title.fontSizeMin, 1e-4f);
            Assert.IsFalse(title.raycastTarget);
            Assert.AreSame(UiFonts.Display, title.font);
            Assert.AreSame(UiFonts.Preset(UiFont.Display, true), title.fontSharedMaterial);

            TextMeshProUGUI body = UiKit.Label(parent, "Body", "A hint.", UiFont.Body, UiTheme.BodySize, UiTheme.Ink);
            Assert.AreSame(UiFonts.Body, body.font);
            Assert.AreEqual(12f, body.fontSizeMin, 1e-4f);

            // Button labels: the display face up to 12 characters, the body face beyond.
            Assert.AreEqual(UiFont.Display, UiKit.ButtonFont("Level Select"));
            Assert.AreEqual(UiFont.Body, UiKit.ButtonFont("Click to play"));

            Assert.AreEqual("0:48.2", UiKit.Time(48.25f));
            Assert.AreEqual("1:05", UiKit.Time(65.9f, false));
            Assert.AreEqual("12:00", UiKit.Time(720.4f));
        }

        [Test]
        public void ASticker_IsThreeImages_ShadowBorderFace()
        {
            Sticker sticker = Sticker.Create(parent, "Card", StickerShape.Card, Palette.Mint.Mid, new Vector2(300f, 200f));
            Assert.AreEqual(new Vector2(300f, 200f), sticker.Rect.sizeDelta);

            // 1. The peel shadow: Ink at 18%, offset (4, -6), hard (the same shape, nothing blurred).
            Assert.AreEqual(Palette.Ink.r, sticker.Shadow.color.r, 1e-4f);
            Assert.AreEqual(0.18f, sticker.Shadow.color.a, 1e-4f);
            Assert.AreEqual(new Vector2(4f, -6f), sticker.ShadowOffset);
            Assert.AreEqual(new Vector2(4f, -6f), sticker.Shadow.rectTransform.anchoredPosition);
            // 2. The die-cut border: Paper, 5 px larger on every side.
            Assert.IsTrue(Palette.Same(Palette.Paper, sticker.Border.color));
            Assert.AreEqual(new Vector2(-5f, -5f), sticker.Border.rectTransform.offsetMin);
            Assert.AreEqual(new Vector2(5f, 5f), sticker.Border.rectTransform.offsetMax);
            Assert.AreEqual(new Vector2(10f, 10f), sticker.Shadow.rectTransform.sizeDelta, "the shadow is the border's shadow");
            // 3. The face.
            Assert.IsTrue(Palette.Same(Palette.Mint.Mid, sticker.Face.color));
            Assert.AreEqual(Vector2.zero, sticker.Face.rectTransform.offsetMin);

            foreach (Image image in new[] { sticker.Shadow, sticker.Border, sticker.Face, sticker.Slot })
                Assert.AreSame(UiAtlas.Texture, image.sprite.texture);
            // Drawn in that order, with the content on top.
            Assert.Less(sticker.Shadow.transform.GetSiblingIndex(), sticker.Body.GetSiblingIndex());
            Assert.Less(sticker.Border.transform.GetSiblingIndex(), sticker.Face.transform.GetSiblingIndex());
            Assert.Less(sticker.Face.transform.GetSiblingIndex(), sticker.Content.GetSiblingIndex());
            // A card is a panel with a hang-tab slot at the top.
            Assert.AreSame(UiAtlas.Sprite(UiShape.HangSlot), sticker.Slot.sprite);
            Assert.AreSame(UiAtlas.Sprite(UiShape.Panel), sticker.Face.sprite);
            Assert.AreEqual(Image.Type.Sliced, sticker.Face.type);

            // Pills are fully rounded: the sliced corner is half the height, whatever the height.
            Sticker pill = Sticker.Create(parent, "Pill", StickerShape.Pill, UiTheme.Ink, new Vector2(200f, 40f));
            float corner = UiAtlas.PillBorder / pill.Face.pixelsPerUnitMultiplier;
            Assert.AreEqual(20f, corner, 0.01f);
            pill.Size = new Vector2(380f, 96f);
            Assert.AreEqual(48f, UiAtlas.PillBorder / pill.Face.pixelsPerUnitMultiplier, 0.01f);
            Assert.AreEqual(53f, UiAtlas.PillBorder / pill.Border.pixelsPerUnitMultiplier, 0.01f, "and the border's is 5 more");
            Assert.IsNull(pill.Slot);
        }

        [Test]
        public void Stick_Is180ms_FromNinetyPercentAndMinusThreeDegrees_WithOvershoot()
        {
            Sticker sticker = Sticker.Create(parent, "Toast", StickerShape.Panel, UiTheme.Ink, new Vector2(300f, 60f));
            UiTween tween = sticker.Tween;
            tween.Hide(true);
            Assert.IsFalse(sticker.gameObject.activeSelf);
            Assert.AreEqual(TweenPhase.Hidden, tween.Phase);

            tween.Show();
            Assert.IsTrue(sticker.gameObject.activeSelf);
            Assert.IsTrue(tween.IsShown);
            Assert.AreEqual(0.9f, sticker.Motion.localScale.x, 1e-4f);
            Assert.AreEqual(-3f, UiTestKit.Angle(sticker.Motion), 1e-3f);

            float largest = 0f;
            for (int i = 0; i < 17; i++)
            {
                tween.Advance(0.01f);
                largest = Mathf.Max(largest, sticker.Motion.localScale.x);
            }
            Assert.AreEqual(TweenPhase.Entering, tween.Phase, "170 ms in it is still moving");
            Assert.Greater(largest, 1.001f, "back-out easing overshoots");
            tween.Advance(0.02f);
            Assert.AreEqual(TweenPhase.Shown, tween.Phase);
            Assert.AreEqual(Vector3.one, sticker.Motion.localScale);
            Assert.AreEqual(0f, UiTestKit.Angle(sticker.Motion), 1e-4f);
            Assert.AreEqual(1f, sticker.Alpha);

            // The easing itself: overshoot 1.56.
            Assert.AreEqual(0f, UiTheme.BackOut(0f), 1e-5f);
            Assert.AreEqual(1f, UiTheme.BackOut(1f), 1e-5f);
            Assert.AreEqual(1f + 2.56f * -0.125f + 1.56f * 0.25f, UiTheme.BackOut(0.5f), 1e-5f);
        }

        [Test]
        public void Peel_ShrinksTowardTheTopEdge_Fades_AndTakesTheStickerDown()
        {
            Sticker sticker = Sticker.Create(parent, "Toast", StickerShape.Panel, UiTheme.Ink, new Vector2(300f, 60f));
            UiTween tween = sticker.Tween;
            Assert.AreEqual(TweenPhase.Shown, tween.Phase, "a new sticker is up");
            Assert.AreEqual(1f, sticker.Motion.pivot.y, "it hinges on its top edge");

            tween.Hide();
            Assert.IsFalse(tween.IsShown, "it no longer counts as up");
            tween.Advance(0.09f);
            Assert.AreEqual(TweenPhase.Leaving, tween.Phase);
            Assert.AreEqual(1f, sticker.Motion.localScale.x, 1e-5f, "only its height goes");
            Assert.AreEqual(0.75f, sticker.Motion.localScale.y, 1e-4f, "ease-in: a quarter of the way at half time");
            Assert.AreEqual(0.75f, sticker.Alpha, 1e-4f);
            Assert.IsTrue(sticker.gameObject.activeSelf);
            tween.Advance(0.1f);
            Assert.AreEqual(TweenPhase.Hidden, tween.Phase);
            Assert.IsFalse(sticker.gameObject.activeSelf);
        }

        [Test]
        public void ReduceMotion_MakesStickAndPeel90msFades()
        {
            Settings.ReduceMotion = true;
            Sticker sticker = Sticker.Create(parent, "Toast", StickerShape.Panel, UiTheme.Ink, new Vector2(300f, 60f));
            UiTween tween = sticker.Tween;
            tween.Hide(true);

            tween.Show();
            Assert.AreEqual(Vector3.one, sticker.Motion.localScale, "no scale");
            Assert.AreEqual(0f, UiTestKit.Angle(sticker.Motion), 1e-4f, "no turn");
            Assert.AreEqual(0f, sticker.Alpha, 1e-4f);
            tween.Advance(0.045f);
            Assert.AreEqual(0.5f, sticker.Alpha, 1e-3f);
            tween.Advance(0.05f);
            Assert.AreEqual(TweenPhase.Shown, tween.Phase, "done in 90 ms");
            Assert.AreEqual(1f, sticker.Alpha);

            tween.Hide();
            tween.Advance(0.045f);
            Assert.AreEqual(Vector3.one, sticker.Motion.localScale);
            Assert.AreEqual(0.5f, sticker.Alpha, 1e-3f);
            tween.Advance(0.05f);
            Assert.IsFalse(sticker.gameObject.activeSelf);
        }

        [Test]
        public void AButton_LiftsWithTheFocus_AndGoesDownWhenPressed()
        {
            var screen = new UiScreen(parent, "Screen");
            int clicks = 0;
            UiButton first = UiButton.Create(screen.Root, "Resume", ButtonStyle.Primary, new Vector2(320f, 60f), () => clicks++);
            UiButton second = UiButton.Create(screen.Root, "Level Select", ButtonStyle.Paper, new Vector2(320f, 60f), () => clicks += 10);
            screen.AddRow(first);
            screen.AddRow(second);
            screen.Show();

            // Primary: Cherry with Paper text. Otherwise Ink on Paper.
            Assert.IsTrue(Palette.Same(Palette.Cherry, first.Sticker.FaceColor));
            Assert.IsTrue(Palette.Same(Palette.Paper, first.Label.color));
            Assert.IsTrue(Palette.Same(Palette.Paper, second.Sticker.FaceColor));
            Assert.IsTrue(Palette.Same(Palette.Ink, second.Label.color));
            Assert.AreSame(UiFonts.Display, second.Label.font, "twelve characters: still the display face");

            // The first control has the focus: lifted (-1, 2), shadow grown to (5, -8), in 90 ms; an Ink edge marks it.
            Assert.AreSame(first, screen.Focused);
            first.Sticker.Tween.Advance(0.09f);
            Assert.AreEqual(new Vector2(-1f, 2f), first.Sticker.Lift);
            Assert.AreEqual(new Vector2(5f, -8f), first.Sticker.ShadowOffset);
            Assert.IsTrue(Palette.Same(Palette.Ink, first.Sticker.BorderColor));
            Assert.AreEqual(Vector2.zero, second.Sticker.Lift);
            Assert.IsTrue(Palette.Same(Palette.Paper, second.Sticker.BorderColor));

            // The mouse: entering takes the focus, pressing pushes the sticker down onto its shadow.
            var pointer = new PointerEventData(null) { button = PointerEventData.InputButton.Left };
            second.OnPointerEnter(pointer);
            Assert.AreSame(second, screen.Focused);
            Assert.IsFalse(first.Focused);
            second.OnPointerDown(pointer);
            Assert.IsTrue(second.Pressed);
            second.Sticker.Tween.Advance(0.045f);
            Assert.AreNotEqual(new Vector2(2f, -3f), second.Sticker.Lift, "it takes 90 ms");
            second.Sticker.Tween.Advance(0.05f);
            Assert.AreEqual(new Vector2(2f, -3f), second.Sticker.Lift);
            Assert.AreEqual(new Vector2(2f, -3f), second.Sticker.ShadowOffset);
            second.OnPointerUp(pointer);
            second.OnPointerClick(pointer);
            Assert.AreEqual(10, clicks);
            first.Sticker.Tween.Advance(0.1f);
            Assert.AreEqual(Vector2.zero, first.Sticker.Lift, "what lost the focus is back at rest");
            Assert.AreEqual(new Vector2(4f, -6f), first.Sticker.ShadowOffset);

            // The keyboard: arrows move the focus (around the ends), Enter / Space activate.
            Assert.IsTrue(screen.Move(MoveDirection.Up));
            Assert.AreSame(first, screen.Focused);
            Assert.IsTrue(screen.Move(MoveDirection.Up));
            Assert.AreSame(second, screen.Focused, "around the top");
            screen.Move(MoveDirection.Down);
            screen.Submit();
            Assert.AreEqual(11, clicks);
            Assert.IsFalse(screen.Move(MoveDirection.Left), "nothing beside it");

            // A screen that is closing takes no more input.
            screen.Enabled = false;
            screen.Submit();
            second.OnPointerClick(pointer);
            Assert.AreEqual(11, clicks);
        }

        [Test]
        public void SettingsRows_AreWorkedWithLeftAndRight_OrActivated()
        {
            var screen = new UiScreen(parent, "Screen");
            float value = -1f;
            bool on = false;
            int choice = -1;
            UiSlider slider = UiSlider.Create(screen.Root, "Field of view", 50f, 90f, 1f, 70f, v => Mathf.RoundToInt(v) + "°", v => value = v);
            UiToggle toggle = UiToggle.Create(screen.Root, "Reduce motion", false, v => on = v);
            UiChoice quality = UiChoice.Create(screen.Root, "Quality", new[] { "Auto", "Low", "Medium", "High" }, 0, i => choice = i);
            screen.AddRow(slider);
            screen.AddRow(toggle);
            screen.AddRow(quality);
            screen.Show();

            Assert.AreEqual(70f, slider.Value);
            Assert.AreEqual("70°", slider.ValueLabel.text);
            Assert.AreEqual(0.5f, slider.Fraction, 1e-4f);
            Assert.AreEqual(-1f, value, "creating a row tells nobody");
            Assert.IsTrue(screen.Move(MoveDirection.Right), "the slider uses left and right itself");
            Assert.AreEqual(71f, value);
            screen.Move(MoveDirection.Left);
            screen.Move(MoveDirection.Left);
            Assert.AreEqual(69f, slider.Value);
            slider.Set(500f);
            Assert.AreEqual(90f, value, "clamped");
            slider.Set(63.4f);
            Assert.AreEqual(63f, slider.Value, "on the step grid");

            screen.Move(MoveDirection.Down);
            Assert.AreSame(toggle, screen.Focused);
            Assert.IsFalse(slider.Focused);
            screen.Submit();
            Assert.IsTrue(on);
            Assert.IsTrue(toggle.On);
            screen.Move(MoveDirection.Left);
            Assert.IsFalse(on, "left is off");
            screen.Move(MoveDirection.Right);
            Assert.IsTrue(on, "right is on");

            screen.Move(MoveDirection.Down);
            screen.Move(MoveDirection.Right);
            Assert.AreEqual(1, choice);
            Assert.AreEqual("Low", quality.Option);
            screen.Move(MoveDirection.Left);
            screen.Move(MoveDirection.Left);
            Assert.AreEqual(3, choice, "around the ends");
            screen.Submit();
            Assert.AreEqual(0, choice, "activating takes the next one");
        }

        [Test]
        public void TheMenuActions_BindMouseArrowsEnterSpaceAndEsc()
        {
            InputActionAsset actions = UiInput.CreateActions();
            try
            {
                InputActionMap map = actions.FindActionMap("UI");
                string Paths(string action)
                {
                    var paths = new List<string>();
                    foreach (InputBinding binding in map.FindAction(action).bindings) paths.Add(binding.path);
                    return string.Join(" ", paths);
                }
                StringAssert.Contains("<Keyboard>/enter", Paths("Submit"));
                StringAssert.Contains("<Keyboard>/space", Paths("Submit"));
                StringAssert.Contains("<Keyboard>/escape", Paths("Cancel"));
                foreach (string key in new[] { "upArrow", "downArrow", "leftArrow", "rightArrow" })
                    StringAssert.Contains("<Keyboard>/" + key, Paths("Navigate"));
                StringAssert.Contains("<Mouse>/position", Paths("Point"));
                StringAssert.Contains("<Mouse>/leftButton", Paths("Click"));
            }
            finally
            {
                Object.DestroyImmediate(actions);
            }
            Assert.IsNull(UiInput.Create(parent), "outside Play Mode nothing pumps an EventSystem, so none is made");
        }

        [Test]
        public void TheSetupSteps_AreFound_Idempotent_AndLeaveTheirAssets()
        {
            var names = new List<string>();
            foreach (SetupStep step in ProjectSetup.Discover())
                if (step.Name.StartsWith("UiSetup."))
                {
                    names.Add(step.Name);
                    Assert.IsTrue(step.Order >= 400 && step.Order < 500, step + ": the UI's orders are 400-499");
                }
            CollectionAssert.AreEqual(new[] { "UiSetup.ImportTextMeshProEssentials", "UiSetup.BakeFonts", "UiSetup.CreateMaterialPresets", "UiSetup.CreateFrameMaterial" }, names);

            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<TMP_Settings>(UiSetup.TmpSettingsPath), "the TMP essential resources are imported");
            string[] paths =
            {
                UiSetup.DisplayFontPath, UiSetup.BodyFontPath, UiSetup.PresetPath(UiFonts.DisplayStickerPreset), UiSetup.PresetPath(UiFonts.DisplayPlainPreset),
                UiSetup.PresetPath(UiFonts.BodyPlainPreset), UiSetup.FrameMaterialPath,
            };
            foreach (string path in paths)
            {
                Assert.IsNotNull(AssetDatabase.LoadMainAssetAtPath(path), path + " is missing: run Toybox.EditorTools.ProjectSetup.Run");
                StringAssert.StartsWith("Assets/Toybox/Resources/", path, "it has to ship in the build");
            }

            // Running the steps again changes nothing.
            var written = new List<DateTime>();
            foreach (string path in paths) written.Add(File.GetLastWriteTimeUtc(path));
            UiSetup.ImportTextMeshProEssentials();
            UiSetup.BakeFonts();
            UiSetup.CreateMaterialPresets();
            UiSetup.CreateFrameMaterial();
            for (int i = 0; i < paths.Length; i++)
            {
                Assert.IsFalse(EditorUtility.IsDirty(AssetDatabase.LoadMainAssetAtPath(paths[i])), paths[i] + " was changed");
                Assert.AreEqual(written[i], File.GetLastWriteTimeUtc(paths[i]), paths[i] + " was written again");
            }
        }
    }

    // ----------------------------------------------------------------------------------------------
    // The HUD (ART_BIBLE 10.5, 9.6), driven by the game through a Presentation
    // ----------------------------------------------------------------------------------------------

    public class HudTests
    {
        Game game;
        ScriptedInput input;
        Presentation presentation;
        HudPresenter hud;
        UiLevel level;

        [SetUp]
        public void Begin()
        {
            Settings.Use(new MemoryStore());
            UiCapture.Request = "";
            input = new ScriptedInput();
            game = Game.Create(new GameOptions { Input = input });
            level = new UiLevel(3);
            game.LoadLevel(level);
            presentation = Presentation.Create(game, new PresentationOptions { Presenters = new List<PresenterRegistry.Entry> { UiTestKit.Entry<HudPresenter>() } });
            hud = presentation.Get<HudPresenter>();
            presentation.Frame(0f, 1f);
        }

        [TearDown]
        public void End()
        {
            presentation?.Dispose();
            presentation = null;
            game?.Dispose();
            game = null;
            Game.Current?.Dispose();
            UiCapture.Reset();
            Settings.Use(null);
        }

        void Step(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                game.Tick();
                presentation.Frame(Sim.Dt, 1f);
            }
        }

        void Wait(float seconds)
        {
            for (int i = TestHelpers.Ticks(seconds); i > 0; i--) presentation.Frame(Sim.Dt, 1f);
        }

        void Grab(Prop prop)
        {
            TestHelpers.LookAt(game.Player, prop.Center);
            input.Once.GrabPressed = true;
            Step();
            Assert.AreSame(prop, game.Grabber.Held);
        }

        void Drop()
        {
            input.Once.GrabPressed = true;
            Step();
            Assert.IsNull(game.Grabber.Held);
        }

        [Test]
        public void TheHud_IsOneOverlayCanvas_WithoutARaycaster()
        {
            Assert.IsNotNull(hud, "the HUD presenter attached");
            Assert.IsTrue(hud.Visible);
            Canvas canvas = hud.Root.Canvas;
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
            Assert.IsNull(hud.Root.GetComponent<GraphicRaycaster>(), "the HUD takes no clicks");
            CanvasScaler scaler = hud.Root.Scaler;
            Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
            Assert.AreEqual(new Vector2(1920f, 1080f), scaler.referenceResolution);
            Assert.AreEqual(CanvasScaler.ScreenMatchMode.Expand, scaler.screenMatchMode, "the reference frame fits at any aspect");
            Assert.AreSame(presentation.Context.Root, hud.Root.transform.parent, "under the presentation root");
            Assert.AreEqual(0, hud.Root.GetComponentsInChildren<Collider>(true).Length, "nothing of it joins the simulation");
            foreach (Graphic graphic in hud.Root.GetComponentsInChildren<Graphic>(true))
                Assert.IsFalse(graphic.raycastTarget, graphic.name);

            PresenterAttribute attribute = typeof(HudPresenter).GetCustomAttribute<PresenterAttribute>();
            Assert.IsTrue(attribute.ProvidesHud);
            Assert.IsFalse(attribute.Fallback);
            Assert.IsTrue(attribute.Order >= 500 && attribute.Order < 600);
        }

        [Test]
        public void TheReticle_SpreadsAndTurnsOverAGrabbable_AndHidesWhileHolding()
        {
            HudReticle reticle = hud.Reticle;
            game.Player.Yaw = 180f;
            Step();
            Wait(0.2f);
            Assert.IsTrue(reticle.Visible);
            Assert.AreEqual(8f, reticle.Span, 1e-3f, "idle: 8 px across");
            Assert.AreEqual(0f, reticle.Angle, 1e-3f);
            Assert.AreEqual(4, reticle.Rect.childCount, "the four-pane mark");
            Assert.AreEqual(new Vector2(5f, 5f), ((RectTransform)reticle.Rect.GetChild(0)).sizeDelta, "3 px of Paper inside a 1 px Ink edge");

            TestHelpers.LookAt(game.Player, level.Block.Center);
            Step();
            Assert.Greater(reticle.Focus, 0f);
            Assert.Less(reticle.Focus, 1f, "it takes 120 ms");
            Wait(0.12f);
            Assert.AreEqual(14f, reticle.Span, 1e-3f, "on a grabbable: 14 px");
            Assert.AreEqual(45f, reticle.Angle, 1e-3f);
            Assert.AreEqual(45f, UiTestKit.Angle(reticle.Rect), 1e-2f);

            Grab(level.Block);
            Assert.IsFalse(reticle.Visible, "hidden while holding");
            Drop();
            Assert.IsTrue(reticle.Visible);
        }

        [Test]
        public void TheScaleReadout_IsUpWhileHolding_AndFor1Point2SecondsAfter()
        {
            ScaleReadout readout = hud.Readout;
            Assert.IsFalse(readout.Shown);
            Assert.IsFalse(readout.Pill.gameObject.activeSelf);
            // A Paper pill on the bottom edge of the frame, in the middle (HudLayoutTests hold it to its place).
            Assert.AreEqual(ReadoutDock.Low, readout.Dock);
            Assert.AreEqual(UiKit.Bottom, readout.Pill.Rect.anchorMin, "it hangs on the frame's edge, whatever the shape of the screen");
            Assert.AreEqual(UiKit.Bottom, readout.Pill.Rect.anchorMax);
            Assert.AreEqual(ScaleReadout.Size, readout.Pill.Size);
            Assert.AreEqual(StickerShape.Pill, readout.Pill.Shape);
            Assert.IsTrue(Palette.Same(Palette.Paper, readout.Pill.FaceColor));

            Grab(level.Block);
            Assert.IsTrue(readout.Shown, "it sticks on with the grab");
            Assert.AreEqual(game.Grabber.Held.Scale / game.Grabber.GrabScale, readout.Factor, 1e-3f);

            // Up at the far wall: the block lands there, several times its size.
            TestHelpers.LookAt(game.Player, new Vector3(0f, 6f, 20f));
            Step(3);
            float held = game.Grabber.Held.Scale / game.Grabber.GrabScale;
            Assert.Greater(held, 2f);
            Assert.AreEqual(held, readout.Factor, 1e-3f);
            Assert.IsTrue(Palette.Same(Palette.Cherry, readout.FactorColor), "larger: Cherry");
            Assert.AreEqual("×" + (held >= 10f ? Mathf.RoundToInt(held).ToString() : (Mathf.RoundToInt(held * 10f) / 10f).ToString("0.0")), readout.FactorText);
            Assert.GreaterOrEqual(readout.Jumps, 1, "the toy jumped to another surface: the readout ticked");
            Assert.AreEqual(game.Grabber.Held.Scale, readout.TrueHeight, 1e-3f, "a unit block is as tall as its scale");
            Wait(0.3f);
            Assert.AreEqual(readout.MarkerTarget, readout.MarkerPosition, 0.5f, "the solid marker caught up (60 ms)");

            Drop();
            Assert.IsTrue(readout.Shown, "it lingers");
            Wait(1.1f);
            Assert.IsTrue(readout.Shown);
            Wait(0.15f);
            Assert.IsFalse(readout.Shown, "1.2 s after the release it peels");
            Wait(0.25f);
            Assert.IsFalse(readout.Pill.gameObject.activeSelf);
        }

        [Test]
        public void TheScaleReadout_Ruler_IsLogarithmic_AndItsColoursSayWhichWay()
        {
            ScaleReadout readout = hud.Readout;
            PropHoldEvent At(float scale) => new PropHoldEvent { Prop = level.Block, OldScale = 1f, NewScale = scale, GrabDistance = 4f, DropDistance = 4f * scale };

            readout.Grab(At(1f));
            Assert.AreEqual(0f, readout.MarkerTarget, 1e-4f, "the hollow marker's place: the grab");

            // 40 px per octave, x1/8 to x8.
            readout.Hold(At(2f));
            Assert.AreEqual(40f, readout.MarkerTarget, 1e-3f);
            readout.Hold(At(4f));
            Assert.AreEqual(80f, readout.MarkerTarget, 1e-3f);
            readout.Hold(At(0.25f));
            Assert.AreEqual(-80f, readout.MarkerTarget, 1e-3f);
            readout.Hold(At(100f));
            Assert.AreEqual(120f, readout.MarkerTarget, 1e-3f, "pinned at x8");
            readout.Hold(At(0.001f));
            Assert.AreEqual(-120f, readout.MarkerTarget, 1e-3f, "pinned at x1/8");

            readout.Hold(At(1.04f));
            Assert.IsTrue(Palette.Same(Palette.Ink, readout.FactorColor));
            readout.Hold(At(0.96f));
            Assert.IsTrue(Palette.Same(Palette.Ink, readout.FactorColor));
            readout.Hold(At(0.94f));
            Assert.IsTrue(Palette.Same(Palette.Lagoon, readout.FactorColor), "smaller: Lagoon");
            readout.Hold(At(1.06f));
            Assert.IsTrue(Palette.Same(Palette.Cherry, readout.FactorColor), "larger: Cherry");

            readout.Hold(At(3.2f));
            Assert.AreEqual("×3.2", readout.FactorText);
            readout.Hold(At(12.4f));
            Assert.AreEqual("×12", readout.FactorText);
            readout.Hold(At(9.97f));
            Assert.AreEqual("×10", readout.FactorText);
            readout.Hold(At(0.31f));
            Assert.AreEqual("×0.31", readout.FactorText);
            readout.Hold(At(0.031f));
            Assert.AreEqual("×0.031", readout.FactorText);
        }

        [Test]
        public void TheScaleReadout_FigureForScale_ShowsAbsoluteSize()
        {
            ScaleReadout readout = hud.Readout;
            // The block is a unit cube lying flat: as tall as its scale.
            PropHoldEvent At(float scale) => new PropHoldEvent { Prop = level.Block, OldScale = 1f, NewScale = scale, GrabDistance = 4f, DropDistance = 4f * scale };
            readout.Grab(At(1f));

            readout.Hold(At(1.7f));
            Assert.AreEqual(14f, readout.BarHeight, 1e-3f, "as tall as the figure: 14 px");
            Assert.AreEqual(14f, readout.FigureSize, 1e-3f);
            Assert.AreEqual(14f * 1.3f / 1.7f, readout.NotchHeight, 1e-3f, "the notch: the apex of a jump");

            readout.Hold(At(3.4f));
            Assert.AreEqual(28f, readout.BarHeight, 1e-3f);
            readout.Hold(At(0.85f));
            Assert.AreEqual(7f, readout.BarHeight, 1e-3f);

            // Past 56 px the bar stays and the figure shrinks instead.
            readout.Hold(At(13.6f));
            Assert.AreEqual(56f, readout.BarHeight, 1e-3f);
            Assert.AreEqual(7f, readout.FigureSize, 1e-3f);
            Assert.AreEqual(7f * 1.3f / 1.7f, readout.NotchHeight, 1e-3f);
            readout.Hold(At(500f));
            Assert.AreEqual(56f, readout.BarHeight, 1e-3f);
            Assert.AreEqual(3f, readout.FigureSize, 1e-3f, "never below 3 px");
        }

        [Test]
        public void AHold_AllocatesNothingInTheHud()
        {
            ScaleReadout readout = hud.Readout;
            var e = new PropHoldEvent { Prop = level.Block, OldScale = 1f, NewScale = 3.2f, GrabDistance = 4f, DropDistance = 12.8f };
            readout.Grab(e);
            for (int i = 0; i < 10; i++)
            {
                readout.Hold(e);
                readout.Frame(Sim.Dt);
                hud.Reticle.Frame(Sim.Dt, true, i % 2 == 0);
            }
            // The toy keeps moving a little; the number shown stays "x3.2". (When the number changes it is
            // written into a char buffer - no string at runtime; in the editor TextMeshPro builds one for
            // its inspector, which is why this test holds the number still.)
            int step = 0;
            Assert.That(() =>
            {
                for (int i = 0; i < 30; i++)
                {
                    e.NewScale = 3.2f + (step++ % 4) * 0.01f;
                    readout.Hold(e);
                    readout.Frame(Sim.Dt);
                    hud.Reticle.Frame(Sim.Dt, true, i % 3 == 0);
                    hud.Hints.Frame(Sim.Dt);
                    hud.LevelCard.Frame(Sim.Dt);
                    hud.Root.Advance(Sim.Dt);
                }
            }, Is.Not.AllocatingGCMemory());
            Assert.AreEqual("×3.2", readout.FactorText);
        }

        [Test]
        public void HintToasts_StickOnForMessages_AndPeelWhenTheirTimeIsUp()
        {
            HintToasts hints = hud.Hints;
            Assert.AreEqual(0, hints.Count);

            game.Events.RaiseMessage(new MessageEvent { Text = "Step back to make it bigger.", Seconds = 2f });
            Assert.AreEqual(1, hints.Count);
            Assert.AreEqual("Step back to make it bigger.", hints.Latest);
            Sticker toast = null;
            for (int i = 0; i < HintToasts.Capacity; i++)
                if (hints.StickerAt(i).Tween.IsShown) toast = hints.StickerAt(i);
            // Bottom left, an Ink sticker with Paper text in the body face at 20.
            Assert.AreEqual(Vector2.zero, toast.Rect.anchorMin);
            Assert.IsTrue(Palette.Same(Palette.Ink, toast.FaceColor));
            TextMeshProUGUI label = toast.GetComponentInChildren<TextMeshProUGUI>();
            Assert.AreSame(UiFonts.Body, label.font);
            Assert.AreEqual(20f, label.fontSize);
            Assert.IsTrue(Palette.Same(Palette.Paper, label.color));
            Assert.Greater(toast.Size.x, 200f, "sized to its text");
            Assert.Less(toast.Size.x, 600f);

            Wait(1.9f);
            Assert.AreEqual(1, hints.Count);
            Wait(0.15f);
            Assert.AreEqual(0, hints.Count, "it peels after the time the message asked for");
            Wait(0.25f);
            Assert.IsFalse(toast.gameObject.activeSelf);

            // A message that does not say how long stays for 5 s.
            game.Events.RaiseMessage(new MessageEvent { Text = "Five seconds.", Seconds = 0f });
            Wait(4.9f);
            Assert.AreEqual(1, hints.Count);
            Wait(0.15f);
            Assert.AreEqual(0, hints.Count);

            // The same thing said twice is one toast; at most three are up, the oldest making room.
            game.Events.RaiseMessage(new MessageEvent { Text = "One", Seconds = 4f });
            game.Events.RaiseMessage(new MessageEvent { Text = "One", Seconds = 4f });
            Assert.AreEqual(1, hints.Count);
            game.Events.RaiseMessage(new MessageEvent { Text = "Two", Seconds = 4f });
            game.Events.RaiseMessage(new MessageEvent { Text = "Three", Seconds = 4f });
            game.Events.RaiseMessage(new MessageEvent { Text = "Four", Seconds = 4f });
            Assert.AreEqual(3, hints.Count);
            Assert.AreEqual("Four", hints.Latest);
            // The newest is the lowest.
            float lowest = float.MaxValue, newestY = 0f;
            for (int i = 0; i < HintToasts.Capacity; i++)
            {
                Sticker sticker = hints.StickerAt(i);
                lowest = Mathf.Min(lowest, sticker.Rect.anchoredPosition.y);
                if (sticker.GetComponentInChildren<TextMeshProUGUI>().text == "Four") newestY = sticker.Rect.anchoredPosition.y;
            }
            Assert.AreEqual(lowest, newestY);

            // A new level starts with a clean slate.
            game.LoadLevel(new UiLevel(4));
            Assert.AreEqual(0, hud.Hints.Count);
        }

        [Test]
        public void TheLevelCard_ShowsNumberTitleAndBlurb_AndPeelsAfter3Point5Seconds()
        {
            LevelCard card = hud.LevelCard;
            Assert.IsTrue(card.Shown, "it stuck on when the level began to be played");
            Assert.AreEqual("TEST LEVEL 3", card.Title);
            Assert.AreEqual("Carry the block to the door.", card.Blurb);
            Assert.AreEqual(2, card.Number.Length, "two digits");
            Assert.AreEqual(StickerShape.Card, card.Card.Shape);
            Assert.IsTrue(Palette.Same(Palette.Paper, card.Card.FaceColor));
            TextMeshProUGUI[] labels = card.Card.GetComponentsInChildren<TextMeshProUGUI>();
            Assert.AreEqual(96f, labels[0].fontSizeMax, "the number at 96");
            Assert.AreEqual(40f, labels[1].fontSizeMax, "the title at 40");
            Assert.AreSame(UiFonts.Body, labels[2].font, "the blurb in the body face");

            Wait(3.4f);
            Assert.IsTrue(card.Shown);
            Wait(0.15f);
            Assert.IsFalse(card.Shown);
            Wait(0.25f);
            Assert.IsFalse(card.Card.gameObject.activeSelf);

            game.LoadLevel(new UiLevel(7));
            presentation.Frame(Sim.Dt, 1f);
            Assert.IsTrue(hud.LevelCard.Shown, "and again for the next level");
            Assert.AreEqual("TEST LEVEL 7", hud.LevelCard.Title);
        }

        [Test]
        public void TheControlPills_StopAppearingAfterThreeUses()
        {
            ControlPills pills = hud.Controls;
            Assert.IsFalse(pills.Shown(ControlPills.Control.Turn));

            Grab(level.Block);
            Assert.IsTrue(pills.Shown(ControlPills.Control.Turn));
            Assert.IsTrue(pills.Shown(ControlPills.Control.Flip));
            Assert.IsTrue(pills.Shown(ControlPills.Control.Drop));
            // The words are the levels' words: toys are let go, not dropped (Phase1CampaignTests, Phase2CampaignTests).
            var words = new List<string>();
            foreach (TMPro.TextMeshProUGUI label in pills.Pill(ControlPills.Control.Drop).Rect.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true)) words.Add(label.text);
            CollectionAssert.Contains(words, "let go");
            CollectionAssert.DoesNotContain(words, "drop");
            // Bottom right, stacked.
            Assert.AreEqual(new Vector2(1f, 0f), pills.Pill(ControlPills.Control.Drop).Rect.anchorMin);
            Assert.Less(pills.Pill(ControlPills.Control.Drop).Rect.anchoredPosition.y, pills.Pill(ControlPills.Control.Turn).Rect.anchoredPosition.y);
            Assert.IsTrue(Palette.Same(Palette.Ink, pills.Pill(ControlPills.Control.Turn).FaceColor), "HUD pills are Ink");

            for (int i = 0; i < 3; i++)
            {
                input.Once.RotateYaw = 1;
                Step();
            }
            Assert.AreEqual(3, pills.UsedCount(ControlPills.Control.Turn));
            input.Once.RotatePitch = true;
            Step();
            Assert.AreEqual(1, pills.UsedCount(ControlPills.Control.Flip));
            Drop();
            Assert.AreEqual(1, pills.UsedCount(ControlPills.Control.Drop));
            Assert.IsFalse(pills.Shown(ControlPills.Control.Drop), "nothing is held: no pills");

            Grab(level.Block);
            Assert.IsFalse(pills.Shown(ControlPills.Control.Turn), "turned three times: the player knows");
            Assert.IsTrue(pills.Shown(ControlPills.Control.Flip));
            Assert.IsTrue(pills.Shown(ControlPills.Control.Drop));
            Drop();
            Grab(level.Block);
            Drop();
            Assert.AreEqual(3, pills.UsedCount(ControlPills.Control.Drop));
            Grab(level.Block);
            Assert.IsFalse(pills.Shown(ControlPills.Control.Drop));
            Assert.IsTrue(pills.Shown(ControlPills.Control.Flip));
            Drop();

            // A toy that cannot be flipped does not offer it.
            Grab(level.Plate);
            Assert.IsFalse(pills.Shown(ControlPills.Control.Flip));
            Drop();

            // The counts are the player's: a store that already has them never shows the pill again.
            var store = new MemoryStore();
            store.SetInt("toybox.hud.uses.turn", 3);
            var later = new ControlPills(hud.Root.Rect, store);
            later.Grab(level.Block);
            Assert.IsFalse(later.Shown(ControlPills.Control.Turn));
            Assert.IsTrue(later.Shown(ControlPills.Control.Flip));
        }

        [Test]
        public void ALevelSwitchWithAToyInHand_ClearsTheHud()
        {
            Grab(level.Block);
            Assert.IsTrue(hud.Readout.Shown);
            game.LoadLevel(new UiLevel(5));
            presentation.Frame(Sim.Dt, 1f);
            Assert.IsFalse(hud.Readout.Shown);
            Assert.IsFalse(hud.Controls.Shown(ControlPills.Control.Drop));
            Assert.IsTrue(hud.Reticle.Visible);
        }
    }

    // ----------------------------------------------------------------------------------------------
    // Where the HUD puts the scale readout (ART_BIBLE 9.6): never on what the player aims with or at
    // ----------------------------------------------------------------------------------------------

    /// <summary>A walled room with one block to pick up, standing on the floor straight ahead of the spawn.</summary>
    sealed class HudStage : LevelDefinition
    {
        public readonly Vector3 Size;
        public readonly float Distance;
        public Prop Toy;

        public HudStage(Vector3 size, float distance)
        {
            Size = size;
            Distance = distance;
        }

        public override string Title => "The Stage";
        public override string Blurb => "Pick the block up.";

        public override void Build(LevelContext ctx)
        {
            TestHelpers.Room(ctx, 20f, 12f);
            Toy = ctx.AddProp(BasicToys.Block(Size), new Vector3(0f, Size.y * 0.5f, Distance), new PropOptions { Name = "Toy" });
            ctx.SetSpawn(Vector3.zero, 0f);
        }
    }

    public class HudLayoutTests
    {
        /// <summary>Screens by width over height: ultra-wide, 16:9, 16:10, 4:3, square, portrait.</summary>
        static readonly float[] Shapes = { 21f / 9f, 16f / 9f, 16f / 10f, 4f / 3f, 1f, 9f / 16f };
        /// <summary>A domino, as Level 4 has one: twice as tall as it is wide.</summary>
        static readonly Vector3 Domino = new Vector3(1f, 2f, 0.3f);

        Game game;
        ScriptedInput input;
        Presentation presentation;
        HudPresenter hud;
        Vector2 canvas;

        [SetUp]
        public void Begin()
        {
            Settings.Use(new MemoryStore());
        }

        [TearDown]
        public void End()
        {
            Close();
            UiCapture.Reset();
            Settings.Use(null);
        }

        void Close()
        {
            presentation?.Dispose();
            presentation = null;
            game?.Dispose();
            game = null;
            Game.Current?.Dispose();
        }

        // The HUD on a screen of this shape. A capture's canvas is the one the game's scaler would make
        // (UiCaptureTests), and unlike the overlay's it does not need a screen to get its size from.
        void Open(float shape, LevelDefinition level, bool scripted = true)
        {
            Close();
            UiCapture.Request = "hud";
            UiCapture.Aspect = shape;
            input = scripted ? new ScriptedInput() : null;
            game = Game.Create(scripted ? new GameOptions { Input = input } : new GameOptions());
            game.LoadLevel(level);
            presentation = Presentation.Create(game, new PresentationOptions { Presenters = new List<PresenterRegistry.Entry> { UiTestKit.Entry<HudPresenter>() } });
            hud = presentation.Get<HudPresenter>();
            presentation.Frame(0f, 1f);
            canvas = HudFrame.Size(hud.Root.Rect);
        }

        void Step(int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                game.Tick();
                presentation.Frame(Sim.Dt, 1f);
            }
        }

        // Lets the stickers finish sticking on, so that they can be measured where they lie.
        void Settle()
        {
            for (int i = TestHelpers.Ticks(0.3f); i > 0; i--) presentation.Frame(Sim.Dt, 1f);
        }

        void Grab(Prop prop)
        {
            TestHelpers.LookAt(game.Player, prop.Center);
            input.Once.GrabPressed = true;
            Step();
            Assert.AreSame(prop, game.Grabber.Held);
        }

        Rect Dock(ReadoutDock dock) => HudFrame.Footprint(ScaleReadout.FaceIn(dock, canvas));

        // The part of a held toy the player aims with: its foot.
        static Rect LowerThird(Rect toy) => Rect.MinMaxRect(toy.xMin, toy.yMin, toy.xMax, toy.yMin + toy.height / 3f);

        // Everything else the HUD can have up, each with what it takes of the canvas.
        IEnumerable<KeyValuePair<string, Rect>> Furniture(bool onlyWhatIsUp)
        {
            if (!onlyWhatIsUp || hud.Reticle.Visible) yield return new KeyValuePair<string, Rect>("the reticle", UiTestKit.OnCanvas(hud.Root, hud.Reticle.Rect));
            if (!onlyWhatIsUp || hud.LevelCard.Shown) yield return new KeyValuePair<string, Rect>("the level card", UiTestKit.Footprint(hud.Root, hud.LevelCard.Card));
            for (int i = 0; i < HintToasts.Capacity; i++)
                if (hud.Hints.StickerAt(i).Tween.IsShown) yield return new KeyValuePair<string, Rect>("a toast", UiTestKit.Footprint(hud.Root, hud.Hints.StickerAt(i)));
            foreach (ControlPills.Control control in Enum.GetValues(typeof(ControlPills.Control)))
                if (hud.Controls.Shown(control)) yield return new KeyValuePair<string, Rect>("the " + control + " pill", UiTestKit.Footprint(hud.Root, hud.Controls.Pill(control)));
            if (!onlyWhatIsUp || hud.LookPrompt.Tween.IsShown) yield return new KeyValuePair<string, Rect>("the look prompt", UiTestKit.Footprint(hud.Root, hud.LookPrompt));
            if (!onlyWhatIsUp || hud.AutoplayPill.Tween.IsShown) yield return new KeyValuePair<string, Rect>("the autoplay pill", UiTestKit.Footprint(hud.Root, hud.AutoplayPill));
        }

        [Test]
        public void TheScaleReadout_IsDockedOutsideTheAimingArea_AtEveryShapeOfScreen()
        {
            // The middle of the frame is for aiming: two thirds of it at the least, each way.
            Assert.GreaterOrEqual(HudFrame.AimShare, 2f / 3f);

            foreach (float shape in Shapes)
            {
                string at = " on a " + shape.ToString("0.00") + " screen";
                var stage = new HudStage(Vector3.one, 6f);
                Open(shape, stage);
                Vector2 expected = UiRoot.CanvasSize(shape);
                Assert.AreEqual(expected.x, canvas.x, 0.01f, at);
                Assert.AreEqual(expected.y, canvas.y, 0.01f, at);
                var frame = new Rect(-canvas * 0.5f, canvas);

                Rect aim = HudFrame.AimArea(canvas);
                Assert.AreEqual(0f, aim.center.magnitude, 1e-3f, "the aiming area is around the crosshair" + at);
                Assert.AreEqual(canvas.x * HudFrame.AimShare, aim.width, 0.01f);
                Assert.AreEqual(canvas.y * HudFrame.AimShare, aim.height, 0.01f);
                Rect reticle = UiTestKit.OnCanvas(hud.Root, hud.Reticle.Rect);
                Assert.IsTrue(aim.Contains(reticle.min) && aim.Contains(reticle.max), "the reticle is in it" + at);

                // Everything the HUD has: the level card of a level just begun, three long hints, the
                // control pills of a hold; the prompt and the autoplay pill are measured where they would be.
                game.Events.RaiseMessage(new MessageEvent { Text = "Step back while you hold a toy: it lands farther away, and bigger than it was.", Seconds = 20f });
                game.Events.RaiseMessage(new MessageEvent { Text = "It lands on whatever is behind it. Look around.", Seconds = 20f });
                game.Events.RaiseMessage(new MessageEvent { Text = "Press R to start the level over.", Seconds = 20f });
                Grab(stage.Toy);
                Settle();
                Assert.IsTrue(hud.LevelCard.Shown);
                Assert.AreEqual(3, hud.Hints.Count);
                Assert.IsTrue(hud.Controls.Shown(ControlPills.Control.Turn) && hud.Controls.Shown(ControlPills.Control.Flip) && hud.Controls.Shown(ControlPills.Control.Drop));

                // A toy that fits the aiming area, sticker and all, leaves the readout where it is.
                Assert.AreEqual(ReadoutDock.Low, ScaleReadout.DockFor(HudFrame.StickerOf(aim, canvas.y), canvas), "a toy as large as the aiming area" + at);

                // What the HUD says about the game itself hangs at the top, out of the aiming area as well.
                foreach (Sticker note in new[] { hud.LookPrompt, hud.AutoplayPill })
                {
                    Rect taken = UiTestKit.Footprint(hud.Root, note);
                    Assert.IsFalse(HudFrame.Meet(taken, aim), note.name + " reaches into the aiming area" + at);
                    Assert.IsTrue(frame.Contains(taken.min) && frame.Contains(taken.max), note.name + " is wholly in the frame" + at);
                    Assert.Greater(taken.yMin, aim.yMax, note.name + " is above it" + at);
                    Assert.IsFalse(HudFrame.Meet(taken, UiTestKit.Footprint(hud.Root, hud.LevelCard.Card)), note.name + " lies on the level card" + at);
                }

                foreach (ReadoutDock dock in Enum.GetValues(typeof(ReadoutDock)))
                {
                    Rect taken = Dock(dock);
                    Assert.IsFalse(HudFrame.Meet(taken, aim), "the " + dock + " dock reaches into the aiming area" + at);
                    Assert.IsTrue(frame.Contains(taken.min) && frame.Contains(taken.max), "the " + dock + " dock is wholly in the frame" + at);
                    foreach (KeyValuePair<string, Rect> other in Furniture(false))
                        Assert.IsFalse(HudFrame.Meet(taken, other.Value), "the " + dock + " dock lies on " + other.Key + at);
                }

                // The pill is where its dock says, drawn: the block is small and far, so that is the low dock.
                ScaleReadout readout = hud.Readout;
                Assert.IsTrue(readout.Shown);
                Assert.AreEqual(ReadoutDock.Low, readout.Dock, at);
                UiTestKit.AreEqual(Dock(ReadoutDock.Low), UiTestKit.Footprint(hud.Root, readout.Pill), 0.5f, "the pill in the low dock" + at);
                Rect low = Dock(ReadoutDock.Low);
                Assert.AreEqual(0f, low.center.x, UiTheme.ShadowOffset.x, "under the crosshair" + at);
                Assert.Less(low.yMax, aim.yMin, "below the aiming area" + at);

                // In the corner it is drawn where that dock says, too: top right. (Let go first: while a toy
                // is held the HUD tells the readout every frame where that toy is.)
                input.Once.GrabPressed = true;
                Step();
                Assert.IsNull(game.Grabber.Held);
                readout.Avoid(Rect.MinMaxRect(-300f, -canvas.y, 300f, canvas.y * 0.4f), canvas);
                Settle();
                Assert.IsTrue(readout.Shown, "it lingers");
                Assert.AreEqual(ReadoutDock.Corner, readout.Dock, at);
                Rect corner = Dock(ReadoutDock.Corner);
                UiTestKit.AreEqual(corner, UiTestKit.Footprint(hud.Root, readout.Pill), 0.5f, "the pill in the corner dock" + at);
                Assert.Greater(corner.xMin, 0f, "right of the crosshair" + at);
                Assert.Greater(corner.yMin, 0f, "above it" + at);
            }
        }

        [Test]
        public void TheScaleReadout_NeverLiesOnTheHeldToy_HoweverNearItWasPickedUp()
        {
            var used = new HashSet<ReadoutDock>();
            foreach (float shape in new[] { 21f / 9f, 16f / 9f, 4f / 3f, 9f / 16f })
                foreach (float distance in new[] { 8f, 4f, 3f, 2.5f, 2.2f, 2f, 1.8f, 1.5f, 1.2f })
                {
                    string at = " (a domino picked up from " + distance + " away, on a " + shape.ToString("0.00") + " screen)";
                    var stage = new HudStage(Domino, distance);
                    Open(shape, stage);
                    Grab(stage.Toy);
                    // Carried about and looked up with, the toy stays what it was on the screen.
                    TestHelpers.LookAt(game.Player, new Vector3(3f, 5f, 20f));
                    Step(20);
                    Settle();

                    Assert.IsTrue(hud.HeldSticker(canvas, out Rect toy), at);
                    Camera camera = presentation.Camera;
                    Assert.IsTrue(HudFrame.BoxOf(stage.Toy, camera.transform.position, camera.transform.rotation, camera.fieldOfView, canvas.y, out Rect box));
                    UiTestKit.AreEqual(Seen(camera, stage.Toy, canvas.y), box, 0.5f, "the toy's box is what the camera shows of it" + at);
                    UiTestKit.AreEqual(HudFrame.StickerOf(box, canvas.y), toy, 1e-3f, "with its border and its peel shadow" + at);
                    Assert.IsTrue(toy.Contains(Vector2.zero), "a held toy is on the crosshair" + at);

                    ScaleReadout readout = hud.Readout;
                    Rect pill = UiTestKit.Footprint(hud.Root, readout.Pill);
                    UiTestKit.AreEqual(Dock(readout.Dock), pill, 0.5f, "the pill is in its dock" + at);
                    Assert.IsFalse(HudFrame.Meet(pill, HudFrame.AimArea(canvas)), "the readout is in the aiming area" + at);
                    Assert.IsFalse(HudFrame.Meet(pill, LowerThird(toy)), "the readout lies on the toy's foot" + at);

                    // Low unless the toy comes down that far; then the corner, which a toy this narrow does
                    // not reach on a 16:9 screen or a wider one, however near it was picked up.
                    bool lowIsFree = !HudFrame.Meet(toy, Dock(ReadoutDock.Low), ScaleReadout.Clearance);
                    bool cornerIsFree = !HudFrame.Meet(toy, Dock(ReadoutDock.Corner), ScaleReadout.Clearance);
                    Assert.AreEqual(lowIsFree ? ReadoutDock.Low : ReadoutDock.Corner, readout.Dock, at);
                    if (shape > 1.7f) Assert.IsTrue(cornerIsFree, "a domino does not reach the corner" + at);
                    if (lowIsFree || cornerIsFree) Assert.IsFalse(HudFrame.Meet(pill, toy, ScaleReadout.Clearance - 0.5f), "the readout touches the toy" + at);
                    foreach (KeyValuePair<string, Rect> other in Furniture(true))
                        Assert.IsFalse(HudFrame.Meet(pill, other.Value), "the readout lies on " + other.Key + at);
                    used.Add(readout.Dock);
                }
            Assert.AreEqual(2, used.Count, "dominoes that leave the low dock alone and dominoes that come down into it");
        }

        // The box of a prop's bounds on a canvas this tall, by the camera's own projection.
        static Rect Seen(Camera camera, Prop prop, float canvasHeight)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            Vector3 half = prop.LocalHalfExtents;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
                Vector3 view = camera.WorldToViewportPoint(prop.Transform.TransformPoint(prop.LocalCenter + corner));
                Assert.Greater(view.z, 0f, "the whole toy is in front of the eye");
                // The field of view is the frame's height, at any shape.
                var point = new Vector2((view.x - 0.5f) * camera.aspect, view.y - 0.5f) * canvasHeight;
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        [Test]
        public void TheScaleReadout_KeepsOffTheFootOfAToyThatFillsTheFrame()
        {
            foreach (float shape in new[] { 16f / 9f, 4f / 3f })
            {
                // A crate three across from two and a half away: wider and taller than the frame's aiming area.
                var stage = new HudStage(new Vector3(3f, 3f, 1f), 2.5f);
                Open(shape, stage);
                Grab(stage.Toy);
                Settle();
                Assert.IsTrue(hud.HeldSticker(canvas, out Rect toy));
                Assert.IsTrue(HudFrame.Meet(toy, Dock(ReadoutDock.Low)), "it comes down into the low dock");
                Assert.AreEqual(ReadoutDock.Corner, hud.Readout.Dock);
                Assert.IsFalse(HudFrame.Meet(UiTestKit.Footprint(hud.Root, hud.Readout.Pill), LowerThird(toy)), "the foot stays in view");
            }
        }

        [Test]
        public void TheScaleReadout_ComesBackDown_OnlyOnceTheToyHasLeftTheLowDockWellAlone()
        {
            canvas = new Vector2(1920f, 1080f);
            Rect low = Dock(ReadoutDock.Low);
            // A toy on the crosshair whose foot ends this far above the low dock.
            Rect Toy(float above) => Rect.MinMaxRect(-200f, low.yMax + above, 200f, 300f);

            Assert.AreEqual(ReadoutDock.Low, ScaleReadout.DockFor(Toy(ScaleReadout.Clearance + 1f), canvas));
            Assert.AreEqual(ReadoutDock.Corner, ScaleReadout.DockFor(Toy(ScaleReadout.Clearance - 1f), canvas), "nearer than the clearance: out of its way");
            Assert.AreEqual(ReadoutDock.Corner, ScaleReadout.DockFor(Toy(-40f), canvas));

            // At the limit it stays where it is.
            Assert.AreEqual(ReadoutDock.Low, ScaleReadout.DockFor(Toy(ScaleReadout.Clearance + 1f), canvas, ReadoutDock.Low));
            Assert.AreEqual(ReadoutDock.Corner, ScaleReadout.DockFor(Toy(ScaleReadout.Clearance + 1f), canvas, ReadoutDock.Corner));
            Assert.AreEqual(ReadoutDock.Corner, ScaleReadout.DockFor(Toy(ScaleReadout.Clearance + ScaleReadout.ComeBack - 1f), canvas, ReadoutDock.Corner));
            Assert.AreEqual(ReadoutDock.Low, ScaleReadout.DockFor(Toy(ScaleReadout.Clearance + ScaleReadout.ComeBack + 1f), canvas, ReadoutDock.Corner));
            Assert.Greater(ScaleReadout.ComeBack, 0f);

            // Something that comes down beside the dock, not into it, does not move the readout.
            Assert.AreEqual(ReadoutDock.Low, ScaleReadout.DockFor(Rect.MinMaxRect(low.xMax + ScaleReadout.Clearance + 1f, -540f, 900f, 300f), canvas));
        }

        [Test]
        public void TheScaleReadout_SticksOnAgainWhereItMovesTo_AndStaysThereOnceTheToyIsLetGo()
        {
            var stage = new HudStage(Domino, 1.6f);
            Open(16f / 9f, stage);
            ScaleReadout readout = hud.Readout;
            Assert.AreEqual(ReadoutDock.Low, readout.Dock, "nothing is held: where it is by default");

            // Picked up from this near the domino is taller than the aiming area: the readout sticks on in the corner.
            Grab(stage.Toy);
            Assert.AreEqual(ReadoutDock.Corner, readout.Dock, "placed before it is first drawn");
            Assert.AreEqual(TweenPhase.Entering, readout.Pill.Tween.Phase);
            Settle();
            Assert.AreEqual(TweenPhase.Shown, readout.Pill.Tween.Phase);
            // Looking about with it changes nothing: it is as tall on the screen as it was.
            TestHelpers.LookAt(game.Player, new Vector3(0f, game.Player.Eye.y, 20f));
            Step(5);
            Assert.AreEqual(ReadoutDock.Corner, readout.Dock);
            Assert.AreEqual(TweenPhase.Shown, readout.Pill.Tween.Phase);

            // Flipped, it points away from the player and is a low thing on the screen: the low dock is free again.
            input.Once.RotatePitch = true;
            Step();
            Assert.AreEqual(ReadoutDock.Low, readout.Dock);
            Assert.AreEqual(TweenPhase.Entering, readout.Pill.Tween.Phase, "it sticks on again in its new place");
            Assert.IsTrue(readout.Shown);
            Settle();
            UiTestKit.AreEqual(Dock(ReadoutDock.Low), UiTestKit.Footprint(hud.Root, readout.Pill), 0.5f, "in the low dock");

            // And upright once more.
            input.Once.RotatePitch = true;
            Step();
            Assert.AreEqual(ReadoutDock.Corner, readout.Dock);
            Settle();
            Assert.AreEqual(TweenPhase.Shown, readout.Pill.Tween.Phase, "it does not keep sticking on while nothing changes");
            Step(10);
            Assert.AreEqual(TweenPhase.Shown, readout.Pill.Tween.Phase);

            // Let go, it stays where it is for as long as it lingers.
            input.Once.GrabPressed = true;
            Step();
            Assert.IsNull(game.Grabber.Held);
            for (int i = 0; i < 30; i++)
            {
                Step();
                Assert.IsTrue(readout.Shown);
                Assert.AreEqual(ReadoutDock.Corner, readout.Dock, "nothing is held: nothing moves it");
            }
            Assert.IsTrue(hud.Reticle.Visible, "the reticle is back");
            Assert.IsFalse(HudFrame.Meet(UiTestKit.Footprint(hud.Root, readout.Pill), UiTestKit.OnCanvas(hud.Root, hud.Reticle.Rect)));

            // The next toy starts afresh, and so does the next level.
            game.LoadLevel(new HudStage(Vector3.one, 6f));
            presentation.Frame(Sim.Dt, 1f);
            Assert.IsFalse(readout.Shown);
            Assert.AreEqual(ReadoutDock.Low, readout.Dock);
        }

        [Test]
        public void TheBoxOfAToy_IsCutOffAtTheEye()
        {
            var stage = new HudStage(Vector3.one, 6f);
            Open(16f / 9f, stage);
            Prop toy = stage.Toy;
            Vector3 eye = new Vector3(0f, 0.5f, 0f);
            float half = 540f / Mathf.Tan(35f * Mathf.Deg2Rad);

            // Straight ahead: its near face, 5.5 away, is the widest thing of it.
            Assert.IsTrue(HudFrame.BoxOf(toy, eye, Quaternion.identity, 70f, 1080f, out Rect box));
            UiTestKit.AreEqual(Rect.MinMaxRect(-0.5f / 5.5f * half, -0.5f / 5.5f * half, 0.5f / 5.5f * half, 0.5f / 5.5f * half), box, 0.01f, "a unit block six away");
            // Twice the canvas, twice the pixels; off to the right when the view turns left.
            Assert.IsTrue(HudFrame.BoxOf(toy, eye, Quaternion.identity, 70f, 2160f, out Rect twice));
            Assert.AreEqual(box.width * 2f, twice.width, 0.01f);
            Assert.IsTrue(HudFrame.BoxOf(toy, eye, Quaternion.Euler(0f, -20f, 0f), 70f, 1080f, out Rect right));
            Assert.Greater(right.xMin, box.xMax);

            // From inside it, part of it is behind the eye: what is in front runs out of any frame.
            Assert.IsTrue(HudFrame.BoxOf(toy, toy.Center, Quaternion.identity, 70f, 1080f, out Rect around));
            Assert.IsTrue(around.xMin < -5000f && around.xMax > 5000f && around.yMin < -5000f && around.yMax > 5000f, around.ToString());
            Assert.IsFalse(float.IsInfinity(around.width) || float.IsNaN(around.width));

            // Behind the player there is nothing of it to see.
            Assert.IsFalse(HudFrame.BoxOf(toy, eye, Quaternion.Euler(0f, 180f, 0f), 70f, 1080f, out _));
            Assert.IsFalse(HudFrame.BoxOf(null, eye, Quaternion.identity, 70f, 1080f, out _));

            // The held toy's sticker: 4.5 px of border all round, the peel shadow 10 to the right and 12 down,
            // at 1080; a frame half as tall again has them half as large again.
            UiTestKit.AreEqual(Rect.MinMaxRect(-104.5f, -216.5f, 114.5f, 204.5f), HudFrame.StickerOf(Rect.MinMaxRect(-100f, -200f, 100f, 200f), 1080f), 1e-3f, "at 1080");
            UiTestKit.AreEqual(Rect.MinMaxRect(-106.75f, -224.75f, 121.75f, 206.75f), HudFrame.StickerOf(Rect.MinMaxRect(-100f, -200f, 100f, 200f), 1620f), 1e-3f, "at 1620");
            Assert.IsFalse(hud.HeldSticker(canvas, out _), "nothing is held");
        }

        // The acceptance of the readout's place, on the levels it was found on: while the bot holds the
        // toy over its target the readout lies on nothing the player needs to see.
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        public void InTheCampaign_TheReadoutLiesOnNothingWhileTheBotAims(int id)
        {
            if (!LevelRegistry.Has(id)) Assert.Ignore("There is no level " + id + ".");
            foreach (float shape in new[] { 16f / 9f, 4f / 3f, 9f / 16f })
            {
                string at = " (level " + id + " on a " + shape.ToString("0.00") + " screen";
                Open(shape, LevelRegistry.Get(id), false);
                var bot = new Bot(game);
                var script = new BotRunner(game.Level.Solve(bot));
                int held = 0;
                bool letGo = false;
                game.Events.PropDropped += e => letGo = true;
                for (int tick = 0; tick < TestHelpers.Ticks(30f) && !letGo; tick++)
                {
                    if (script != null && !script.Advance()) script = null;
                    game.Tick();
                    presentation.Frame(Sim.Dt, 1f);
                    if (!game.Grabber.IsHolding) continue;
                    held++;
                    string when = at + ", tick " + tick + ")";
                    Assert.IsTrue(hud.Readout.Shown, when);
                    Assert.IsTrue(hud.HeldSticker(canvas, out Rect toy), when);
                    Rect pill = Dock(hud.Readout.Dock);
                    Assert.IsFalse(HudFrame.Meet(pill, HudFrame.AimArea(canvas)), "the readout is in the aiming area" + when);
                    Assert.IsFalse(HudFrame.Meet(pill, toy), "the readout lies on the held toy" + when);
                    foreach (KeyValuePair<string, Rect> other in Furniture(true))
                        Assert.IsFalse(HudFrame.Meet(pill, other.Value), "the readout lies on " + other.Key + when);
                }
                Assert.IsTrue(letGo, "the bot picked a toy up and let it go" + at + ")");
                Assert.Greater(held, 30, "and held it for a while" + at + ")");
            }
        }
    }

    // ----------------------------------------------------------------------------------------------
    // The menus (ART_BIBLE 10.5, 9.7), driven by the real GameRunner with fake devices
    // ----------------------------------------------------------------------------------------------

    public class MenuTests
    {
        GameObject host;
        GameRunner runner;
        FakeDevices devices;
        MemoryStore store;
        MenuPresenter menu;
        HudPresenter hud;

        [SetUp]
        public void NewStores()
        {
            store = new MemoryStore();
            Settings.Use(new MemoryStore());
            UiCapture.Request = "";
        }

        [TearDown]
        public void Shutdown()
        {
            runner?.Shutdown();
            runner = null;
            if (host != null) Object.DestroyImmediate(host);
            host = null;
            Game.Current?.Dispose();
            UiCapture.Reset();
            Settings.Use(null);
        }

        void Begin(string url, int levels = 3, bool refuseLock = false)
        {
            host = new GameObject("UI Test Runner") { hideFlags = HideFlags.DontSave };
            runner = host.AddComponent<GameRunner>();
            devices = new FakeDevices { RefuseLock = refuseLock };
            runner.Begin(LaunchOptions.FromUrl(url), new RunnerOptions
            {
                Devices = devices,
                Levels = UiTestKit.Levels(levels),
                Store = store,
                Presenters = UiTestKit.Own(),
            });
            menu = runner.Presentation.Get<MenuPresenter>();
            hud = runner.Presentation.Get<HudPresenter>();
            Assert.IsNotNull(menu, "the menu presenter attached");
            Assert.IsNotNull(hud, "the HUD presenter attached");
        }

        void Frames(int count = 1)
        {
            for (int i = 0; i < count; i++) runner.Frame(Sim.Dt);
        }

        void Seconds(float seconds) => Frames(TestHelpers.Ticks(seconds));

        void Press(UiButton button)
        {
            button.Activate();
            Frames();
        }

        void Esc()
        {
            devices.State.EscapePressed = true;
            Frames();
        }

        FlowState State => runner.Flow.State;
        Camera Camera => runner.Presentation.Camera;

        [Test]
        public void ThePresenters_ProvideTheHud_AndRetireTheDebugHud()
        {
            PresenterAttribute attribute = typeof(MenuPresenter).GetCustomAttribute<PresenterAttribute>();
            Assert.IsTrue(attribute.ProvidesHud);
            Assert.IsFalse(attribute.Fallback);
            Assert.IsTrue(attribute.Order >= 500 && attribute.Order < 600);

            var types = new List<Type>();
            foreach (PresenterRegistry.Entry entry in PresenterRegistry.All) types.Add(entry.Type);
            CollectionAssert.Contains(types, typeof(HudPresenter));
            CollectionAssert.Contains(types, typeof(MenuPresenter));

            var active = new List<Type>();
            foreach (PresenterRegistry.Entry entry in PresenterRegistry.Select(PresenterRegistry.All, false)) active.Add(entry.Type);
            CollectionAssert.Contains(active, typeof(MenuPresenter));
            CollectionAssert.DoesNotContain(active, typeof(DebugHudPresenter), "the game's own HUD retires the stand-in");
            var plain = new List<Type>();
            foreach (PresenterRegistry.Entry entry in PresenterRegistry.Select(PresenterRegistry.All, true)) plain.Add(entry.Type);
            CollectionAssert.DoesNotContain(plain, typeof(MenuPresenter), "the plain look keeps the debug HUD");
            CollectionAssert.DoesNotContain(plain, typeof(HudPresenter));
            CollectionAssert.Contains(plain, typeof(DebugHudPresenter));

            Begin("");
            Assert.IsTrue(runner.Presentation.HudProvided);
            Assert.IsNull(runner.Hud, "no debug HUD");
            Assert.IsNotNull(menu.Root.GetComponent<GraphicRaycaster>(), "the menu canvas takes clicks");
            Assert.Greater(menu.Root.Canvas.sortingOrder, hud.Root.Canvas.sortingOrder, "above the HUD");
            Assert.IsFalse(runner.Flow.AutoAdvance, "the level-complete card has a Next button: the flow waits for it");
        }

        [Test]
        public void TheTitle_IsUpAtTheStart_AndClickToPlayStartsTheGame()
        {
            Begin("");
            Assert.AreEqual(FlowState.Title, State);
            Assert.AreSame(menu.Title, menu.Current);
            Assert.IsTrue(menu.Root.Visible);
            Assert.IsFalse(hud.Visible, "the HUD stays behind the menus");
            // Every menu exists from the start (opening one creates nothing); only the one that is up is active.
            foreach (MenuScreen screen in new MenuScreen[] { menu.Pause, menu.SettingsCard, menu.Catalogue, menu.Complete })
            {
                Assert.IsNotNull(screen);
                Assert.IsFalse(screen.Screen.Visible);
                Assert.IsFalse(screen.IsOpen);
            }
            Assert.IsTrue(menu.Title.Screen.Visible);

            TitleMenu title = menu.Title;
            // "TINKER'S TOYBOX" at 120 in the sticker text style; the O is the four-pane mark.
            TextMeshProUGUI[] words = title.Logo.GetComponentsInChildren<TextMeshProUGUI>();
            Assert.AreEqual("TINKER’S", words[0].text);
            Assert.AreEqual(120f, words[0].fontSizeMax);
            Assert.AreSame(UiFonts.Preset(UiFont.Display, true), words[0].fontSharedMaterial);
            StringAssert.StartsWith("T<space=", title.Wordmark.text);
            StringAssert.EndsWith("YBOX", title.Wordmark.text);
            Assert.AreSame(UiAtlas.Sprite(UiShape.FourPane), title.Mark.sprite);
            TMP_TextInfo info = title.Wordmark.textInfo;
            float markX = title.Mark.rectTransform.anchoredPosition.x;
            Assert.Greater(markX, info.characterInfo[0].topRight.x, "the mark stands between the T");
            Assert.Less(markX, info.characterInfo[1].bottomLeft.x, "and the Y");
            // One primary action: the Cherry pill, and it has the focus.
            Assert.IsTrue(Palette.Same(Palette.Cherry, title.Play.Sticker.FaceColor));
            Assert.AreEqual("Click to play", title.Play.Label.text);
            Assert.AreSame(title.Play, menu.Active.Focused);
            Assert.IsFalse(Palette.Same(Palette.Cherry, title.LevelsButton.Sticker.FaceColor));

            // The pill pulses.
            float before = title.Play.Sticker.Rect.localScale.x;
            Seconds(0.25f);
            Assert.AreNotEqual(before, title.Play.Sticker.Rect.localScale.x);

            // A click elsewhere on the title is the runner's to ignore; the pill starts the game - in the frame, not on the spot.
            int ticks = runner.Game.TickCount;
            title.Play.Activate();
            Assert.AreEqual(FlowState.Title, State);
            Assert.AreEqual(1, menu.PendingActions);
            Frames();
            Assert.AreEqual(FlowState.Playing, State);
            Assert.IsTrue(devices.PointerLocked, "play takes the mouse");
            Assert.IsNull(menu.Current);
            Assert.IsTrue(hud.Visible);
            Frames(5);
            Assert.Greater(runner.Game.TickCount, ticks);
            // The logo peels off; then the menu canvas is gone.
            Assert.IsFalse(title.Logo.Tween.IsShown);
            Seconds(0.3f);
            Assert.IsFalse(menu.Root.Visible);
            Assert.IsFalse(title.Screen.Visible);
        }

        [Test]
        public void TheTitle_WorksFromTheKeyboardAlone()
        {
            Begin("");
            TitleMenu title = menu.Title;
            Assert.IsTrue(menu.Move(MoveDirection.Down));
            Assert.AreSame(title.LevelsButton, menu.Active.Focused);
            Assert.IsTrue(menu.Move(MoveDirection.Right));
            Assert.AreSame(title.SettingsButton, menu.Active.Focused);
            menu.Submit();
            Frames();
            Assert.IsTrue(menu.SettingsOpen);
            Assert.AreSame(menu.SettingsCard, menu.Current);
            Assert.AreEqual(FlowState.Title, State);
            // Esc backs out of the settings.
            menu.Cancel();
            Frames();
            Assert.AreSame(menu.Title, menu.Current);

            Assert.AreSame(title.Play, menu.Active.Focused, "the focus starts over on the primary action");
            menu.Submit();
            Frames();
            Assert.AreEqual(FlowState.Playing, State);
        }

        [Test]
        public void TheTitle_HasAGiantToyOnATurntable_AndTheCameraDropsIntoFirstPerson()
        {
            Begin("");
            TitleStage stage = menu.Stage;
            Assert.IsTrue(stage.Active);
            if (runner.Presentation.Context.HasGraphics)
            {
                Assert.IsNotNull(stage.Toy, "the toy stands in the level behind the title");
                Assert.AreEqual(0, stage.Toy.GetComponentsInChildren<Collider>(true).Length, "a renderer only: a collider would join the simulation");
                Assert.Greater(stage.Toy.transform.localScale.x, 1.4f, "giant");
                float turned = stage.Toy.transform.localEulerAngles.y;
                Seconds(0.5f);
                Assert.AreNotEqual(turned, stage.Toy.transform.localEulerAngles.y, "it turns");
            }
            Vector3 eye = runner.Game.Player.EyeAt(runner.Game.Alpha);
            Assert.AreEqual(eye.y + TitleStage.Raise, Camera.transform.position.y, 1e-3f, "seen from a little above the figure's eyes");

            Press(menu.Title.Play);
            Seconds(0.1f);
            float height = Camera.transform.position.y - runner.Game.Player.EyeAt(runner.Game.Alpha).y;
            Assert.Greater(height, 0f, "the camera is on its way down");
            Assert.Less(height, TitleStage.Raise);
            Seconds(0.4f);
            Assert.AreEqual(runner.Game.Player.EyeAt(runner.Game.Alpha).y, Camera.transform.position.y, 1e-4f, "first person");
            Assert.IsNull(stage.Toy);
            Assert.IsFalse(stage.Active);
        }

        [Test]
        public void ALaunchThatNamesALevel_ShowsNoTitle()
        {
            Begin("?level=2");
            Assert.AreEqual(FlowState.Playing, State);
            Assert.IsNull(menu.Current);
            Assert.IsFalse(menu.Title.Screen.Visible, "the title never came up");
            Assert.IsFalse(menu.Stage.Active);
            Assert.IsFalse(menu.Root.Visible);
            Assert.IsTrue(hud.Visible);
            Assert.AreEqual("02", hud.LevelCard.Number, "the level card carries the level's number");
            Assert.AreEqual(runner.Game.Player.EyeAt(runner.Game.Alpha).y, Camera.transform.position.y, 1e-4f);
        }

        [Test]
        public void ThePause_PutsTheFrameOnACard_TurnsTheWorldCameraOff_AndListsFiveActions()
        {
            Begin("?level=1");
            Frames(10);
            Assert.IsTrue(Camera.enabled);
            Esc();
            Assert.AreEqual(FlowState.Paused, State);
            Assert.AreSame(menu.Pause, menu.Current);
            Assert.IsFalse(hud.Visible);

            MenuBackdrop backdrop = menu.Backdrop;
            Assert.AreEqual(BackdropMode.Frozen, backdrop.Mode);
            Assert.IsTrue(backdrop.CameraOff);
            Assert.IsFalse(Camera.enabled, "while paused the GPU draws the UI only");
            // Something still has to render to the screen for the pipeline to draw the menus with: a camera that sees nothing.
            Camera screen = backdrop.ScreenCamera;
            Assert.IsNotNull(screen);
            Assert.IsTrue(screen.enabled);
            Assert.AreEqual(0, screen.cullingMask);
            Assert.IsNull(screen.targetTexture);
            Assert.Greater(screen.depth, Camera.depth);
            Assert.IsFalse(screen.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing);
            if (runner.Presentation.Context.HasGraphics)
            {
                Assert.IsNotNull(backdrop.Target, "the frame was captured");
                Assert.AreEqual(0.86f, backdrop.CardSize, 1e-4f, "shown as a card at 86%");
                Assert.AreEqual(-2f, backdrop.CardTilt, 1e-3f, "tilted -2 degrees");
            }

            PauseMenu pause = menu.Pause;
            Assert.AreEqual(StickerShape.Card, pause.Card.Shape, "a hang-tab card");
            var labels = new List<string>();
            for (int i = 0; i < pause.Screen.RowCount; i++) labels.Add(((UiButton)pause.Screen.Row(i)[0]).Label.text);
            CollectionAssert.AreEqual(new[] { "Resume", "Restart", "Hints", "Settings", "Level Select" }, labels);
            Assert.AreSame(pause.Resume, menu.Active.Focused);
            Assert.IsTrue(Palette.Same(Palette.Cherry, pause.Resume.Sticker.FaceColor), "the one primary action");
            Assert.IsFalse(Palette.Same(Palette.Cherry, pause.Restart.Sticker.FaceColor));

            // Nothing ticks behind the card.
            int ticks = runner.Game.TickCount;
            Frames(20);
            Assert.AreEqual(ticks, runner.Game.TickCount);

            Press(pause.Resume);
            Assert.AreEqual(FlowState.Playing, State);
            Assert.IsTrue(Camera.enabled);
            Assert.AreEqual(BackdropMode.None, backdrop.Mode);
            Assert.IsNull(backdrop.Target, "the texture is given back");
            Assert.IsTrue(backdrop.ScreenCamera == null && screen == null, "and the stand-in camera is gone");
            Assert.IsTrue(devices.PointerLocked);
            Assert.IsTrue(hud.Visible);
            Frames(5);
            Assert.Greater(runner.Game.TickCount, ticks);

            // Esc resumes as well (that is the runner's), and the menu follows.
            Esc();
            Assert.AreSame(menu.Pause, menu.Current);
            Esc();
            Assert.AreEqual(FlowState.Playing, State);
            Assert.IsNull(menu.Current);
            Assert.IsTrue(Camera.enabled);
        }

        [Test]
        public void ThePause_Restart_Hints_AndLevelSelect()
        {
            Begin("?level=1");
            Frames(30);
            Assert.Greater(runner.Game.LevelTicks, 0);
            Esc();
            PauseMenu pause = menu.Pause;

            // Hints: one at a time, beside the card; after the last comes the first again.
            Assert.AreEqual(-1, pause.HintIndex);
            menu.Move(MoveDirection.Down);
            menu.Move(MoveDirection.Down);
            Assert.AreSame(pause.Hints, menu.Active.Focused);
            menu.Submit();
            Frames();
            Assert.AreEqual(0, pause.HintIndex);
            Assert.AreEqual("HINT 1 / 2", pause.HintTitle.text);
            Assert.AreEqual("The block is light.", pause.HintText.text);
            Press(pause.Hints);
            Assert.AreEqual("The door is to the right.", pause.HintText.text);
            Press(pause.Hints);
            Assert.AreEqual(0, pause.HintIndex);
            Assert.AreEqual(FlowState.Paused, State);

            // Level Select opens the catalogue; Esc there closes it again - and does not also resume the game.
            Press(pause.LevelSelect);
            Assert.AreEqual(FlowState.LevelSelect, State);
            Assert.AreSame(menu.Catalogue, menu.Current);
            Assert.AreEqual(BackdropMode.Surface, menu.Backdrop.Mode);
            Assert.IsFalse(Camera.enabled);
            menu.Cancel();
            devices.State.EscapePressed = true;
            Frames();
            Assert.AreEqual(FlowState.Paused, State);
            Assert.AreSame(menu.Pause, menu.Current);
            Assert.AreEqual(-1, pause.HintIndex, "the hint is put away when the card comes back");

            Press(pause.Restart);
            Assert.AreEqual(FlowState.Playing, State);
            Assert.Less(runner.Game.LevelTicks, 5, "the level started over");
            Assert.IsTrue(Camera.enabled);
        }

        [Test]
        public void TheSettings_WriteTheSettings_AndSaveWhenTheCardCloses()
        {
            var settingsStore = new MemoryStore();
            Settings.Use(settingsStore);
            Begin("?level=1");
            Esc();
            Press(menu.Pause.SettingsButton);
            Assert.AreSame(menu.SettingsCard, menu.Current);
            Assert.AreEqual(FlowState.Paused, State);
            Assert.AreEqual(BackdropMode.Frozen, menu.Backdrop.Mode, "still over the frozen frame");
            SettingsMenu card = menu.SettingsCard;

            // Quality, lens blur strength, sensitivity, field of view, volume, music, reduce motion, high-visibility toys.
            Assert.AreEqual(9, card.Screen.RowCount, "eight settings and Done");
            Assert.AreSame(card.Quality, menu.Active.Focused);
            Assert.AreEqual("Auto", card.Quality.Option);
            Assert.AreEqual("60%", card.LensBlur.ValueLabel.text);
            Assert.AreEqual("70°", card.FieldOfView.ValueLabel.text);

            menu.Move(MoveDirection.Right);
            Assert.AreEqual(QualitySetting.Low, Settings.Quality);
            menu.Move(MoveDirection.Down);
            menu.Move(MoveDirection.Left);
            Assert.AreEqual(0.55f, Settings.LensBlur, 1e-4f);
            card.Sensitivity.Set(0.2f);
            Assert.AreEqual(0.2f, Settings.MouseSensitivity, 1e-4f);
            card.FieldOfView.Set(85f);
            Assert.AreEqual(85f, Settings.FieldOfView);
            card.Volume.Set(0.25f);
            Assert.AreEqual(0.25f, Settings.MasterVolume, 1e-4f);
            card.Music.Set(0f);
            Assert.AreEqual(0f, Settings.MusicVolume);
            card.ReduceMotion.Activate();
            Assert.IsTrue(Settings.ReduceMotion);
            card.HighVisibility.Activate();
            Assert.IsTrue(Settings.HighVisibility);

            int saves = settingsStore.Saves;
            Press(card.Done);
            Assert.AreSame(menu.Pause, menu.Current, "back on the pause card");
            Assert.Greater(settingsStore.Saves, saves, "closing saves");

            // Opened again, it shows what is set.
            Settings.FieldOfView = 60f;
            Press(menu.Pause.SettingsButton);
            Assert.AreEqual(60f, card.FieldOfView.Value);
            Assert.IsTrue(card.ReduceMotion.On);
            Assert.AreEqual("Low", card.Quality.Option);
            // Esc with the settings up is the card's Esc, not the runner's: the game stays paused, the card
            // closes (the EventSystem hands the key over as Cancel) and the settings are saved.
            saves = settingsStore.Saves;
            Esc();
            Assert.AreEqual(FlowState.Paused, State, "the runner leaves this Esc to the card");
            Assert.IsTrue(menu.SettingsOpen);
            menu.Cancel();
            Frames();
            Assert.IsFalse(menu.SettingsOpen);
            Assert.AreSame(menu.Pause, menu.Current, "back on the pause card");
            Assert.Greater(settingsStore.Saves, saves);
            Esc();
            Assert.AreEqual(FlowState.Playing, State, "and from the pause card Esc resumes");
        }

        [Test]
        public void TheCatalogue_IsFiveCardsToARow_Locked_Open_AndCollected()
        {
            var progress = new Progress(store);
            progress.RecordCompletion(1, 48.2f);
            progress.RecordCompletion(2, 65.9f);
            Begin("", levels: 15);
            Press(menu.Title.LevelsButton);
            Assert.AreEqual(FlowState.LevelSelect, State);
            CatalogueMenu catalogue = menu.Catalogue;
            Assert.AreSame(catalogue, menu.Current);
            Assert.AreEqual(BackdropMode.Surface, menu.Backdrop.Mode);

            // A 5 x 3 grid of blister cards.
            Assert.AreEqual(15, catalogue.Entries.Count);
            Assert.AreEqual(4, catalogue.Screen.RowCount, "three rows of cards and Back");
            for (int row = 0; row < 3; row++) Assert.AreEqual(5, catalogue.Screen.Row(row).Count);
            CatalogueMenu.Entry first = catalogue.Find(1), second = catalogue.Find(2), third = catalogue.Find(3), fourth = catalogue.Find(4), sixth = catalogue.Find(6);
            Assert.AreEqual(first.Card.Rect.anchoredPosition.y, catalogue.Find(5).Card.Rect.anchoredPosition.y, "1 to 5 share a row");
            Assert.Less(sixth.Card.Rect.anchoredPosition.y, first.Card.Rect.anchoredPosition.y);
            Assert.AreEqual(first.Card.Rect.anchoredPosition.x, sixth.Card.Rect.anchoredPosition.x, "6 starts the next");
            Assert.AreEqual("2 of 15 collected", catalogue.CountText);

            // Backing in the level's dip (mid); the number at 96; a Paper circle with the hero toy in the hero candy.
            Assert.AreEqual(StickerShape.Card, first.Card.Shape);
            Assert.IsTrue(Palette.Same(Palette.Mint.Mid, first.Card.FaceColor), "level 1: sunny-rug, Mint");
            Assert.IsTrue(Palette.Same(Palette.Pool.Mid, second.Card.FaceColor), "level 2: pegboard-workbench, Pool");
            Assert.IsTrue(Palette.Same(Palette.Peach.Mid, third.Card.FaceColor), "level 3: cardboard-box, Peach");
            Assert.AreEqual("01", first.Number.text);
            Assert.AreEqual(96f, first.Number.fontSizeMax);
            Assert.AreSame(UiFonts.Display, first.Number.font);
            Assert.AreSame(UiAtlas.Sprite(UiShape.Circle), first.Circle.sprite);
            Assert.IsTrue(Palette.Same(Palette.Paper, first.Circle.color));
            Assert.AreSame(UiAtlas.Sprite(UiGlyph.Wedge), first.Glyph.sprite);
            Assert.IsTrue(Palette.Same(Palette.Mint.Hero, first.Glyph.color), "Mint's hero candy: Cherry");
            Assert.IsTrue(Palette.Same(Palette.Tangerine, second.Glyph.color));
            Assert.IsTrue(Palette.Same(Palette.Plum.Mid, catalogue.Find(14).Dip.Mid), "the night levels are Plum");

            // Completed: a "COLLECTED" sticker turned 6 degrees one way or the other, and the best time.
            Assert.IsTrue(first.Completed);
            Assert.IsTrue(first.Stamp.Tween.IsShown);
            Assert.AreEqual("COLLECTED", first.Stamp.GetComponentInChildren<TextMeshProUGUI>().text);
            Assert.AreEqual(6f, Mathf.Abs(UiTestKit.Angle(first.Stamp.Rect)), 1e-3f);
            Assert.AreEqual(CatalogueMenu.StampSign(1) * 6f, UiTestKit.Angle(first.Stamp.Rect), 1e-3f, "seeded: the same every time");
            int plus = 0;
            for (int id = 1; id <= 15; id++)
                if (CatalogueMenu.StampSign(id) > 0) plus++;
            Assert.IsTrue(plus > 2 && plus < 13, "both ways occur");
            Assert.AreEqual("0:48", first.Best.text);
            Assert.AreEqual("1:05", second.Best.text);

            // Open but not collected: no sticker.
            Assert.IsTrue(third.Unlocked);
            Assert.IsFalse(third.Completed);
            Assert.IsFalse(third.Stamp.Tween.IsShown);
            Assert.AreEqual("", third.Best.text);

            // Locked: a Kraft card with "?".
            Assert.IsFalse(fourth.Unlocked);
            Assert.IsTrue(Palette.Same(Palette.Kraft, fourth.Card.FaceColor));
            Assert.IsTrue(fourth.Question.gameObject.activeSelf);
            Assert.AreEqual("?", fourth.Question.text);
            Assert.IsFalse(fourth.Number.gameObject.activeSelf);
            Assert.IsFalse(fourth.Circle.gameObject.activeSelf);
            Assert.IsFalse(third.Question.gameObject.activeSelf);

            // The keyboard walks the grid; locked cards do not take the focus.
            Assert.AreSame(first.Button, menu.Active.Focused, "the focus starts on the level the player is at");
            menu.Move(MoveDirection.Right);
            Assert.AreSame(second.Button, menu.Active.Focused);
            menu.Move(MoveDirection.Right);
            menu.Move(MoveDirection.Right);
            Assert.AreSame(third.Button, menu.Active.Focused, "4 is locked");
            menu.Move(MoveDirection.Down);
            Assert.AreSame(catalogue.Back, menu.Active.Focused, "below it everything is locked: on to Back");
            menu.Move(MoveDirection.Up);
            Assert.IsTrue(menu.Active.Focused == first.Button || menu.Active.Focused == third.Button || menu.Active.Focused == second.Button);

            // With the focus on it a card lifts and a four-pane glint slides across its circle.
            catalogue.Screen.Focus(second.Button);
            Frames(3);
            Assert.IsTrue(second.Glint.gameObject.activeSelf);
            Assert.AreSame(UiAtlas.Sprite(UiShape.FourPane), second.Glint.sprite);
            float x = second.Glint.rectTransform.anchoredPosition.x;
            Frames(6);
            Assert.Greater(second.Glint.rectTransform.anchoredPosition.x, x, "it slides");
            Assert.AreEqual(UiTheme.HoverOffset, second.Card.Lift, "the card is lifted");
            Seconds(0.4f);
            Assert.IsFalse(second.Glint.gameObject.activeSelf, "one glint");

            // A locked card does nothing; an open one starts its level.
            fourth.Button.Activate();
            Frames();
            Assert.AreEqual(FlowState.LevelSelect, State);
            Press(third.Button);
            Assert.AreEqual(FlowState.Playing, State);
            Assert.AreEqual(3, runner.LevelId);
            Assert.IsTrue(Camera.enabled);
            Assert.IsNull(menu.Current);
        }

        [Test]
        public void TheCatalogue_Back_ReturnsToWhereItWasOpenedFrom()
        {
            Begin("", levels: 15);
            Press(menu.Title.LevelsButton);
            Assert.AreEqual("0 of 15 collected", menu.Catalogue.CountText);
            Assert.IsTrue(menu.Catalogue.Find(1).Unlocked, "the first level is always open");
            Assert.IsFalse(menu.Catalogue.Find(2).Unlocked);
            Press(menu.Catalogue.Back);
            Assert.AreEqual(FlowState.Title, State);
            Assert.AreSame(menu.Title, menu.Current);
            Assert.IsTrue(Camera.enabled, "the title's world is live again");
            Assert.IsNotNull(menu.Title.Logo);

            // Esc does the same.
            Press(menu.Title.LevelsButton);
            Assert.AreEqual(FlowState.LevelSelect, State);
            menu.Cancel();
            Frames();
            Assert.AreEqual(FlowState.Title, State);
        }

        [Test]
        public void LevelComplete_RunsItsTimeline_AndWaitsForNext()
        {
            Begin("?level=1");
            Game g = runner.Game;
            Frames(20);
            // One grab on the way.
            TestHelpers.LookAt(g.Player, ((UiLevel)g.Level).Block.Center);
            devices.State.GrabKeyPressed = true;
            Frames(2);
            Assert.IsNotNull(g.Grabber.Held);
            Assert.AreEqual(1, menu.Grabs);
            devices.State.GrabKeyPressed = true;
            Frames(2);

            g.CompleteLevel();
            Assert.AreEqual(FlowState.LevelComplete, State);
            float played = runner.Flow.LastCompletion.Time;
            Frames();
            CompleteMenu complete = menu.Complete;
            Assert.AreSame(complete, menu.Current);
            Assert.IsFalse(hud.Visible);
            MenuBackdrop backdrop = menu.Backdrop;
            Assert.AreEqual(BackdropMode.Live, backdrop.Mode);
            bool graphics = runner.Presentation.Context.HasGraphics;
            if (graphics)
            {
                // 0 ms: the camera's output goes to a texture shown on a Paper card; the scene stays live in it.
                Assert.IsTrue(backdrop.CameraRedirected);
                Assert.AreSame(backdrop.Target, Camera.targetTexture);
                Assert.IsTrue(Camera.enabled);
                Assert.IsNotNull(backdrop.ScreenCamera, "and a camera that sees nothing carries the menus to the screen");
                Assert.IsTrue(Palette.Same(Palette.Paper, backdrop.Card.FaceColor));
                Assert.IsNotNull(backdrop.Card.Slot, "with a hang-tab hole");
                Assert.Greater(backdrop.CardSize, 0.9f, "it starts as the whole screen");
            }
            Assert.Greater(complete.FlashAlpha, 0.5f, "a white flash");
            Assert.IsFalse(complete.Stamp.Tween.IsShown);
            Assert.IsFalse(complete.Info.Tween.IsShown);

            Seconds(0.26f);
            if (graphics)
            {
                Assert.AreEqual(0.86f, backdrop.CardSize, 1e-3f, "240 ms: shrunk to 86%");
                Assert.AreEqual(2f, backdrop.CardTilt, 1e-2f, "and tilted 2 degrees");
                Assert.IsNotNull(complete.Confetti, "100 ms: confetti");
                Assert.AreEqual(150, complete.Confetti.Count, "Medium: 150");
                Assert.AreEqual(150, complete.Confetti.System.particleCount, "all of them in the air");
                Assert.AreEqual(0.3f, complete.Confetti.System.main.gravityModifier.constant, 1e-4f);
                Assert.AreEqual(2.2f, complete.Confetti.System.main.startLifetime.constant, 1e-4f);
                ParticleSystemRenderer confetti = complete.Confetti.System.GetComponent<ParticleSystemRenderer>();
                Assert.AreEqual(ParticleSystemRenderMode.Mesh, confetti.renderMode, "mesh quads");
                Assert.IsFalse(confetti.enableGPUInstancing, "not instanced");
            }
            Assert.IsFalse(complete.Stamp.Tween.IsShown, "not before 400 ms");
            Seconds(0.1f);
            Assert.AreEqual(0f, complete.FlashAlpha, "the flash has faded (300 ms)");
            Seconds(0.08f);
            Assert.IsTrue(complete.Stamp.Tween.IsShown, "400 ms: COLLECTED stamps on");
            Assert.Greater(complete.Stamp.Rect.localScale.x, 1f, "from 1.4");
            Assert.IsFalse(complete.Info.Tween.IsShown, "not before 600 ms");
            Seconds(0.2f);
            Assert.AreEqual(1f, complete.Stamp.Rect.localScale.x, 1e-4f, "to 1 in 90 ms");
            Assert.IsTrue(complete.Info.Tween.IsShown, "600 ms: title, time, grab count and Next");
            Assert.AreEqual("TEST LEVEL 1", complete.Title);
            StringAssert.Contains(UiKit.Time(played), complete.Stats);
            StringAssert.Contains("1 pick-up", complete.Stats);
            Assert.IsFalse(complete.Stats.Contains("pick-ups"));
            Assert.IsFalse(complete.Stats.Contains("grab"), "the card uses the levels' word");
            Assert.AreEqual("Next", complete.Next.Label.text);
            Assert.IsTrue(Palette.Same(Palette.Cherry, complete.Next.Sticker.FaceColor), "a Cherry Next pill");
            Assert.AreSame(complete.Next, menu.Active.Focused);
            Assert.IsFalse(complete.BestShown, "a first completion is not a new best");

            // The flow waits for the card; the card counts down on its Next pill, and stops counting for good
            // as soon as the player does anything on it.
            Assert.Greater(complete.AutoNextIn, 4f);
            Seconds(4f);
            Assert.AreEqual(FlowState.LevelComplete, State);
            Assert.AreEqual(1, runner.LevelId);
            Assert.IsTrue(runner.Flow.Progress.IsCompleted(1));
            Assert.Less(complete.AutoNextIn, 1.1f);
            Assert.IsFalse(complete.Held);
            menu.Move(MoveDirection.Left);
            Frames();
            Assert.IsTrue(complete.Held);
            Seconds(6f);
            Assert.AreEqual(FlowState.LevelComplete, State, "it waits for a button now");
            menu.Move(MoveDirection.Right);
            Assert.AreSame(complete.Next, menu.Active.Focused);

            Press(complete.Next);
            Assert.AreEqual(FlowState.Playing, State);
            Assert.AreEqual(2, runner.LevelId);
            Assert.IsNull(Camera.targetTexture, "the camera renders to the screen again");
            Assert.IsTrue(backdrop.ScreenCamera == null);
            Assert.AreEqual(BackdropMode.None, backdrop.Mode);
            Assert.IsNull(complete.Confetti);
            Assert.AreEqual(0, menu.Grabs, "a new level, a new count");
            Assert.IsTrue(hud.Visible);
        }

        [Test]
        public void LevelComplete_Again_Levels_AndReduceMotion()
        {
            Settings.ReduceMotion = true;
            Begin("?level=1");
            Frames(10);
            runner.Game.CompleteLevel();
            Frames();
            CompleteMenu complete = menu.Complete;
            Assert.AreEqual(0f, complete.FlashAlpha, "no flash");
            Seconds(0.7f);
            Assert.AreEqual(0f, menu.Backdrop.CardTilt, 1e-4f, "no tilt");
            Assert.AreEqual(1f, complete.Stamp.Rect.localScale.x, "no punch");
            Assert.IsTrue(complete.Info.Tween.IsShown);

            // Left of Next: Again and Levels.
            menu.Move(MoveDirection.Left);
            Assert.AreSame(complete.LevelsButton, menu.Active.Focused);
            menu.Submit();
            Frames();
            Assert.AreEqual(FlowState.LevelSelect, State);
            Assert.IsTrue(menu.Catalogue.Find(1).Completed);
            Assert.IsTrue(menu.Catalogue.Find(2).Unlocked, "completing a level opens the next");
            menu.Cancel();
            Frames();
            Assert.AreEqual(FlowState.LevelComplete, State);
            Assert.AreSame(complete, menu.Current);

            Seconds(0.7f);
            Press(complete.Again);
            Assert.AreEqual(FlowState.Playing, State);
            Assert.AreEqual(1, runner.LevelId);
            Assert.Less(runner.Game.LevelTicks, 5);
        }

        [Test]
        public void LevelComplete_LeftAlone_MovesOnByItself()
        {
            Begin("?level=1");
            Frames(10);
            runner.Game.CompleteLevel();
            Frames();
            Assert.IsFalse(runner.Flow.AdvancePending, "the flow itself is not counting: the card is");
            Seconds(CompleteMenu.InfoAt + CompleteMenu.AutoNextSeconds - 0.3f);
            Assert.AreEqual(FlowState.LevelComplete, State);
            Assert.AreEqual(1, runner.LevelId);
            Seconds(0.5f);
            Assert.AreEqual(FlowState.Playing, State, "nobody touched the card for five seconds");
            Assert.AreEqual(2, runner.LevelId);
            Assert.IsNull(Camera.targetTexture);
        }

        [Test]
        public void WhileABotPlays_TheCardComesAndGoesByItself()
        {
            Begin("?level=1&autoplay=1");
            bool celebrated = false;
            for (int i = 0; i < TestHelpers.Ticks(40f) && runner.LevelId == 1; i++)
            {
                runner.Frame(Sim.Dt);
                celebrated |= menu.Current != null && menu.Current == menu.Complete;
            }
            Assert.IsTrue(celebrated, "the card was up");
            Assert.AreEqual(2, runner.LevelId, "and the bot's run moved on without anybody pressing Next");
            Frames(2);
            Assert.AreEqual(FlowState.Playing, State);
            Assert.IsNull(menu.Current);
            Assert.IsNull(Camera.targetTexture);
            Assert.IsTrue(Camera.enabled);
            Assert.AreEqual(0, store.Count, "and the bot leaves nothing in the player's store - no progress, no pill counts");
        }

        // The middle of the bottom edge is the scale readout's and nothing goes over the toy (HudLayoutTests):
        // what the HUD says about the game itself - the mouse is not captured, a bot is playing - hangs from
        // the middle of the top edge. One place for the two, because they are never up together.
        [Test]
        public void TheLookPromptAndTheAutoplayPill_ShareTheTopOfTheFrame_AndAreNeverUpTogether()
        {
            Begin("?level=1", refuseLock: true);
            Frames();
            Assert.AreEqual(FlowState.Playing, State);
            Assert.IsFalse(devices.PointerLocked, "the browser did not hand the mouse over");
            Assert.IsFalse(hud.LookPrompt.Tween.IsShown, "not at once");
            Seconds(HudPresenter.LookPromptDelay + 0.1f);
            Assert.IsTrue(hud.LookPrompt.Tween.IsShown, "click to look around");
            Assert.IsFalse(hud.AutoplayPill.Tween.IsShown);
            foreach (Sticker note in new[] { hud.LookPrompt, hud.AutoplayPill })
            {
                Assert.AreEqual(UiKit.Top, note.Rect.anchorMin, note.name);
                Assert.AreEqual(UiKit.Top, note.Rect.anchorMax, note.name);
                Assert.AreEqual(new Vector2(0f, -HudPresenter.NoteMargin), note.Rect.anchoredPosition, note.name);
                Assert.IsTrue(Palette.Same(Palette.Ink, note.FaceColor), "HUD pills are Ink");
            }

            // P: the bot takes over. Its pill comes, the prompt goes - the bot does not need the mouse.
            devices.State.AutoplayPressed = true;
            Frames();
            Assert.IsTrue(runner.Autoplay);
            Assert.IsTrue(hud.AutoplayPill.Tween.IsShown);
            Assert.IsFalse(hud.LookPrompt.Tween.IsShown);
            Seconds(1f);
            Assert.IsTrue(hud.AutoplayPill.Tween.IsShown);
            Assert.IsFalse(hud.LookPrompt.Tween.IsShown, "never while the bot plays");
            Assert.IsFalse(hud.LookPrompt.gameObject.activeSelf);

            // P again: the player is back, still without the mouse.
            devices.State.AutoplayPressed = true;
            Frames();
            Assert.IsFalse(runner.Autoplay);
            Assert.IsFalse(hud.AutoplayPill.Tween.IsShown);
            Assert.IsFalse(hud.LookPrompt.Tween.IsShown, "the prompt waits its 0.6 s: the pill has peeled off by then");
            Seconds(HudPresenter.LookPromptDelay + 0.1f);
            Assert.IsTrue(hud.LookPrompt.Tween.IsShown);
            Assert.IsFalse(hud.AutoplayPill.gameObject.activeSelf);
        }

        [Test]
        public void Shutdown_GivesTheCameraAndTheFlowBack()
        {
            Game game = Game.Create(new GameOptions());
            var flow = new GameFlow(game, UiTestKit.Levels(3), new Progress(store));
            Presentation presentation = null;
            try
            {
                presentation = Presentation.Create(game, new PresentationOptions { Flow = flow, Presenters = UiTestKit.Own() });
                Assert.IsFalse(flow.AutoAdvance);
                flow.StartLevel(1);
                presentation.Frame(Sim.Dt, 1f);
                flow.Pause();
                presentation.Frame(Sim.Dt, 1f);
                Camera camera = presentation.Camera;
                Assert.IsFalse(camera.enabled);
                MenuPresenter attached = presentation.Get<MenuPresenter>();
                GameObject canvas = attached.Root.gameObject;

                attached.Dispose();
                Assert.IsTrue(camera.enabled, "the camera is on again");
                Assert.IsNull(camera.targetTexture);
                Assert.IsTrue(flow.AutoAdvance, "and the flow moves on by itself again");
                Assert.IsTrue(canvas == null, "the canvas is gone");
            }
            finally
            {
                presentation?.Dispose();
                flow.Dispose();
                game.Dispose();
            }
        }
    }

    // ----------------------------------------------------------------------------------------------
    // Looking at the UI through the screenshot tool (-toyboxUi)
    // ----------------------------------------------------------------------------------------------

    public class UiCaptureTests
    {
        Game game;
        Presentation presentation;

        [SetUp]
        public void Begin()
        {
            Settings.Use(new MemoryStore());
        }

        [TearDown]
        public void End()
        {
            presentation?.Dispose();
            presentation = null;
            game?.Dispose();
            game = null;
            Game.Current?.Dispose();
            UiCapture.Reset();
            Settings.Use(null);
        }

        void Present(string request, float aspect)
        {
            UiCapture.Request = request;
            UiCapture.Aspect = aspect;
            game = Game.Create(new GameOptions());
            game.LoadLevel(new UiLevel(1));
            presentation = Presentation.Create(game, new PresentationOptions { Presenters = UiTestKit.Own() });
            presentation.Frame(0f, 1f);
        }

        [Test]
        public void ACapture_PutsTheCanvasesInFrontOfTheGameCamera()
        {
            Assert.IsFalse(UiCapture.Active, "nothing is captured unless it is asked for");
            Present("hud", 2f);
            Assert.IsTrue(UiCapture.Active);
            HudPresenter hud = presentation.Get<HudPresenter>();
            Assert.IsTrue(hud.Root.InWorld);
            Canvas canvas = hud.Root.Canvas;
            Assert.AreEqual(RenderMode.WorldSpace, canvas.renderMode);
            Assert.IsNull(hud.Root.Scaler, "the canvas is sized by hand");

            // 1080 reference pixels fill the view's height; the width is that of the picture asked for.
            Camera camera = presentation.Camera;
            RectTransform rect = hud.Root.Rect;
            Assert.AreEqual(new Vector2(2160f, 1080f), rect.sizeDelta);
            float distance = Vector3.Dot(rect.position - camera.transform.position, camera.transform.forward);
            Assert.Greater(distance, camera.nearClipPlane);
            Assert.Less(distance, camera.nearClipPlane * 2f, "right behind the near plane: nothing of the world gets in front of it");
            float viewHeight = 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Assert.AreEqual(viewHeight, 1080f * rect.lossyScale.y, viewHeight * 1e-3f);
            Assert.AreEqual(0f, Quaternion.Angle(camera.transform.rotation, rect.rotation), 1e-3f);
            Assert.IsTrue(hud.Visible);
            Assert.IsNull(presentation.Get<MenuPresenter>().Current, "hud: no menu");

            // It follows the camera.
            game.Player.Yaw = 90f;
            presentation.Frame(0f, 1f);
            Assert.AreEqual(0f, Quaternion.Angle(camera.transform.rotation, rect.rotation), 1e-3f);
        }

        // A picture that is not 16:9 has to show the UI the size the game shows it on such a screen: the
        // scaler's Expand mode makes the canvas taller there, not narrower (the capture's canvas used to be
        // 1080 tall at any shape, and a 4:3 or portrait picture showed a HUD the game never has).
        [Test]
        public void ACapturedCanvas_IsTheOneTheGamesScalerMakesOfThatScreen()
        {
            foreach (Vector2 screen in new[]
                     {
                         new Vector2(1920f, 1080f), new Vector2(1280f, 720f), new Vector2(2560f, 1080f), new Vector2(1024f, 768f), new Vector2(800f, 800f), new Vector2(540f, 960f),
                     })
            {
                // CanvasScaler, Scale With Screen Size, Expand: the smaller of the two scale factors.
                float factor = Mathf.Min(screen.x / 1920f, screen.y / 1080f);
                Vector2 canvas = UiRoot.CanvasSize(screen.x / screen.y);
                Assert.AreEqual(screen.x / factor, canvas.x, 0.01f, screen.ToString());
                Assert.AreEqual(screen.y / factor, canvas.y, 0.01f, screen.ToString());
                Assert.IsTrue(canvas.x >= 1919.99f && canvas.y >= 1079.99f, "the reference frame is wholly on it: " + screen);
            }
            Assert.AreEqual(1920f, UiRoot.CanvasSize(0f).x, 0.01f, "no shape: the reference frame");
            Assert.AreEqual(1080f, UiRoot.CanvasSize(0f).y, 0.01f);

            Present("hud", 3f / 4f);
            Camera camera = presentation.Camera;
            RectTransform rect = presentation.Get<HudPresenter>().Root.Rect;
            Assert.AreEqual(1920f, rect.sizeDelta.x, 0.01f);
            Assert.AreEqual(2560f, rect.sizeDelta.y, 0.01f);
            // It still fills the picture exactly: all of its height is the view's height, its width three quarters of that.
            float distance = Vector3.Dot(rect.position - camera.transform.position, camera.transform.forward);
            float viewHeight = 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Assert.AreEqual(viewHeight, 2560f * rect.lossyScale.y, viewHeight * 1e-3f);
            Assert.AreEqual(viewHeight * 0.75f, 1920f * rect.lossyScale.x, viewHeight * 1e-3f);
        }

        // A picture below 16:9 has a taller canvas, and a canvas put right behind the near plane is the smaller
        // the taller it is. Below a scale of about 7e-5 TextMeshPro draws nothing on a world-space canvas:
        // every picture narrower than 6:5 showed the level card, the toasts and the pills without a word on
        // them (measured: drawn at 7.2e-5, gone at 6.7e-5). The canvas now keeps the scale it has at 16:9 and
        // stands farther off instead.
        [Test]
        public void ACapturedPicture_HasItsWords_AtEveryShape()
        {
            float reference = 0f;
            foreach (float aspect in new[] { 16f / 9f, 4f / 3f, 6f / 5f, 1f, 9f / 16f })
            {
                Present("hud", aspect);
                for (int i = 0; i < 30; i++) presentation.Frame(1f / 60f, 1f);
                HudPresenter hud = presentation.Get<HudPresenter>();
                Assert.IsTrue(hud.LevelCard.Shown, "test setup: the level card is up");
                Camera camera = presentation.Camera;
                RectTransform rect = hud.Root.Rect;
                string shape = "a picture " + aspect.ToString("0.###") + " wide for 1 high";

                // The same scale at every shape, the canvas filling the picture, the UI on top of the world.
                if (reference == 0f) reference = rect.lossyScale.y;
                Assert.AreEqual(reference, rect.lossyScale.y, reference * 1e-3f, shape + ": the canvas is scaled as it is at 16:9");
                float distance = Vector3.Dot(rect.position - camera.transform.position, camera.transform.forward);
                float viewHeight = 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                Assert.AreEqual(viewHeight, rect.sizeDelta.y * rect.lossyScale.y, viewHeight * 1e-3f, shape + ": the canvas fills the picture's height");
                Assert.Greater(distance, camera.nearClipPlane, shape);
                Assert.IsTrue(UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(UiCapture.Overlay).clearDepth,
                    "the UI's camera clears the depth: nothing of the world gets in front of a canvas that stands farther off");

                // The words of the level card are in the picture: dark pixels that are gone when its labels are.
                const int height = 960;
                int width = Mathf.RoundToInt(height * aspect);
                int with = InkInTheLevelCard(hud, width, height);
                TMPro.TextMeshProUGUI[] labels = hud.LevelCard.Card.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
                Assert.AreEqual(3, labels.Length, "test setup: number, title and blurb");
                foreach (TMPro.TextMeshProUGUI label in labels) label.enabled = false;
                int without = InkInTheLevelCard(hud, width, height);
                Assert.Greater(with - without, 100, shape + ": the level card has no words on it (" + with + " dark pixels with its labels, " + without + " without)");
                End();
            }
        }

        // Dark pixels where the level card is: the top left of the canvas, 48 to 740 by 48 to 260 reference pixels.
        int InkInTheLevelCard(HudPresenter hud, int width, int height)
        {
            // The first picture after a change is of the canvas as it was.
            Object.DestroyImmediate(Toybox.EditorTools.Shots.Photograph(presentation.Camera, width, height));
            Texture2D image = Toybox.EditorTools.Shots.Photograph(presentation.Camera, width, height);
            float perUnit = width / hud.Root.Rect.sizeDelta.x;
            int ink = 0;
            for (int y = height - 1 - Mathf.RoundToInt(260f * perUnit); y < height - Mathf.RoundToInt(48f * perUnit); y++)
                for (int x = Mathf.RoundToInt(48f * perUnit); x < Mathf.RoundToInt(740f * perUnit); x++)
                {
                    Color c = image.GetPixel(x, y);
                    if (c.r + c.g + c.b < 1f) ink++;
                }
            Object.DestroyImmediate(image);
            return ink;
        }

        [Test]
        public void ACapture_CanAskForAnyMenu()
        {
            Present("pause", 16f / 9f);
            MenuPresenter menu = presentation.Get<MenuPresenter>();
            Assert.AreSame(menu.Pause, menu.Current);
            Assert.IsFalse(presentation.Get<HudPresenter>().Visible);
            Assert.IsTrue(presentation.Camera.enabled, "a capture needs the camera: it draws the canvas");
            End();

            Present("settings", 16f / 9f);
            menu = presentation.Get<MenuPresenter>();
            Assert.AreSame(menu.SettingsCard, menu.Current);
            End();

            Present("title", 16f / 9f);
            menu = presentation.Get<MenuPresenter>();
            Assert.AreSame(menu.Title, menu.Current);
            End();

            Present("complete", 16f / 9f);
            menu = presentation.Get<MenuPresenter>();
            Assert.AreSame(menu.Complete, menu.Current);
            presentation.Frame(Sim.Dt, 1f);
            Assert.IsTrue(menu.Complete.Info.Tween.IsShown, "the finished card");
            End();

            // The catalogue as it will be: the fifteen levels of the plan, some of them collected.
            Present("catalogue", 16f / 9f);
            menu = presentation.Get<MenuPresenter>();
            Assert.AreSame(menu.Catalogue, menu.Current);
            Assert.AreEqual(15, menu.Catalogue.Entries.Count);
            Assert.IsTrue(menu.Catalogue.Find(6).Completed);
            Assert.IsFalse(menu.Catalogue.Find(8).Unlocked);
            Assert.AreSame(UiAtlas.Sprite(UiGlyph.Key), menu.Catalogue.Find(12).Glyph.sprite);
        }

        [Test]
        public void TheScreenshotTool_GetsTheUiIntoItsPicture()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("needs a graphics device");
            UiCapture.Request = "select";
            UiCapture.Aspect = 640f / 360f;
            string directory = Path.Combine(Path.GetTempPath(), "toybox-ui-test-shots");
            List<string> files = Shots.Run(new ShotRequest
            {
                Definition = new UiLevel(1),
                Level = 1,
                Width = 640,
                Height = 360,
                OutputDirectory = directory,
                Presenters = UiTestKit.Own(),
            });
            Assert.AreEqual(1, files.Count);
            var image = new Texture2D(2, 2);
            try
            {
                image.LoadImage(File.ReadAllBytes(files[0]));
                Assert.AreEqual(640, image.width);
                // The catalogue's surface is Paper from edge to edge; its title is Ink.
                foreach (Vector2Int at in new[] { new Vector2Int(6, 180), new Vector2Int(633, 180), new Vector2Int(320, 6), new Vector2Int(320, 353) })
                {
                    Color pixel = image.GetPixel(at.x, at.y);
                    Assert.IsTrue(pixel.r > 0.9f && pixel.g > 0.9f && pixel.b > 0.85f, "Paper at " + at + ", got " + pixel);
                }
                int ink = 0;
                for (int x = 24; x < 300; x++)
                    for (int y = 318; y < 352; y++)
                    {
                        Color pixel = image.GetPixel(x, y);
                        if (pixel.r < 0.5f && pixel.b < 0.6f) ink++;
                    }
                Assert.Greater(ink, 20, "the title is written on it");
            }
            finally
            {
                Object.DestroyImmediate(image);
                File.Delete(files[0]);
            }
        }
    }

    // ----------------------------------------------------------------------------------------------
    // Pictures of the scale readout's corner dock, for looking at (not a test)
    // ----------------------------------------------------------------------------------------------

    /// <summary>A level played another way: its own Build, somebody else's script.</summary>
    sealed class Replayed : LevelDefinition
    {
        readonly LevelDefinition level;
        readonly Func<Bot, IEnumerator> script;

        public Replayed(LevelDefinition level, Func<Bot, IEnumerator> script)
        {
            this.level = level;
            this.script = script;
        }

        public override string Slug => level.Slug;
        public override string Title => level.Title;
        public override string Blurb => level.Blurb;
        public override string[] Hints => level.Hints;
        public override string Environment => level.Environment;
        public override int EnvironmentVisit => level.EnvironmentVisit;
        public override float GroundY => level.GroundY;
        public override float KillY => level.KillY;

        public override void Build(LevelContext ctx) => level.Build(ctx);

        public override IEnumerator Solve(Bot bot) => script(bot);
    }

    public static class HudStills
    {
        /// <summary>
        /// Level 4 with the domino picked up from nearer than its stand mark, so that it is taller on the
        /// screen than the frame's aiming area and the scale readout has to leave its low dock
        /// (ART_BIBLE 9.6); held over the painted footprint as the level's own solution holds it:
        ///
        ///   tools/unity.ps1 exec -Method Toybox.Tests.HudStills.Capture
        ///       -UnityArgs '-toyboxUi','hud','-toyboxSize','1280x720','-toyboxOut','tools/out/shots/prelude-hud/near'
        ///
        /// -toyboxNear how far in front of the domino the bot stands to pick it up (0.75; the stand mark is
        /// 1.06 away), -toyboxTimes (4.5), -toyboxSize and -toyboxOut as for Shots.Capture. Without
        /// -toyboxUi there is no HUD in the picture.
        /// </summary>
        public static void Capture()
        {
            float near = Toybox.EditorTools.ToyboxArgs.GetFloat("-toyboxNear", 0.75f);
            int width = 1280, height = 720;
            Shots.ParseSize(Toybox.EditorTools.ToyboxArgs.Get("-toyboxSize", "1280x720"), ref width, ref height);
            var level = new Toybox.Levels.Level04DominoEffect();
            IEnumerator Script(Bot bot)
            {
                Vector3 stand = Toybox.Levels.Level04DominoEffect.PickupSpot;
                stand.z = level.Domino.Center.z - near;
                yield return bot.WalkTo(stand, 0.03f);
                yield return bot.LookAt(level.Domino);
                yield return bot.Grab(level.Domino);
                yield return bot.WalkTo(Toybox.Levels.Level04DominoEffect.EdgeSpot, 0.1f);
                yield return bot.LookAt(Toybox.Levels.Level04DominoEffect.AimPoint);
                yield return bot.Wait(30f);
            }
            Shots.Run(new ShotRequest
            {
                Level = 4,
                Definition = new Replayed(level, Script),
                Times = Shots.ParseTimes(Toybox.EditorTools.ToyboxArgs.Get("-toyboxTimes", "4.5")),
                Width = width,
                Height = height,
                OutputDirectory = Toybox.EditorTools.ToyboxArgs.Get("-toyboxOut", "tools/out/shots/prelude-hud/near"),
            });
        }

        /// <summary>
        /// A level at a moment of its solution with everything the HUD has up at once - the level card,
        /// three hints, the control pills, the scale readout, the autoplay pill and the look prompt (which
        /// a tool never shows by itself: they belong to the game's flow) - for seeing that nothing lies on
        /// anything:
        ///
        ///   tools/unity.ps1 exec -Method Toybox.Tests.HudStills.Everything
        ///       -UnityArgs '-toyboxUi','hud','-toyboxLevel','1','-toyboxTimes','1.3','-toyboxSize','1024x768'
        ///
        /// Written to -toyboxOut (tools/out/shots/prelude-hud/all) as levelNN-all.png.
        /// </summary>
        public static void Everything()
        {
            int id = Toybox.EditorTools.ToyboxArgs.GetInt("-toyboxLevel", 1);
            float time = Shots.ParseTimes(Toybox.EditorTools.ToyboxArgs.Get("-toyboxTimes", "1.3"))[0];
            int width = 1280, height = 720;
            Shots.ParseSize(Toybox.EditorTools.ToyboxArgs.Get("-toyboxSize", "1280x720"), ref width, ref height);
            string directory = Toybox.EditorTools.ToyboxArgs.Get("-toyboxOut", "tools/out/shots/prelude-hud/all");
            if (!Path.IsPathRooted(directory)) directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), directory);
            Directory.CreateDirectory(directory);

            Game.Current?.Dispose();
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            Game game = null;
            Presentation presentation = null;
            try
            {
                game = Game.Create(new GameOptions());
                game.LoadLevel(LevelRegistry.Get(id));
                presentation = Presentation.Create(game, new PresentationOptions());
                presentation.Context.Autoplay = true;
                presentation.Frame(0f, 1f);
                int msaa = Toybox.Render.TierSpec.Of(presentation.Context.Quality).Msaa;
                // The first render after a script reload is not to be trusted (see Shots.Warm).
                Object.DestroyImmediate(Shots.Photograph(presentation.Camera, width, height, msaa));

                var bot = new Bot(game);
                var script = new BotRunner(game.Level.Solve(bot));
                for (int tick = Mathf.RoundToInt(time / Sim.Dt); tick > 0; tick--)
                {
                    if (script != null && !script.Advance()) script = null;
                    game.Tick();
                    presentation.Frame(Sim.Dt, 1f);
                }
                game.Events.RaiseMessage(new MessageEvent { Text = "Step back while you hold a toy: it lands farther away, and bigger than it was.", Seconds = 20f });
                game.Events.RaiseMessage(new MessageEvent { Text = "Press R to start the level over.", Seconds = 20f });
                presentation.Frame(0.5f, 1f);
                HudPresenter hud = presentation.Get<HudPresenter>();
                if (hud != null && hud.Root != null)
                {
                    hud.LevelCard.Card.Tween.Show(true);
                    hud.LookPrompt.Tween.Show(true);
                }

                Texture2D image = Shots.Photograph(presentation.Camera, width, height, msaa);
                string path = Path.Combine(directory, "level" + id.ToString("00") + "-all.png");
                File.WriteAllBytes(path, image.EncodeToPNG());
                Object.DestroyImmediate(image);
                Debug.Log("[Toybox] shot: " + path);
            }
            finally
            {
                presentation?.Dispose();
                game?.Dispose();
                ShaderUtil.allowAsyncCompilation = previousAsync;
                UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            }
        }
    }
}
