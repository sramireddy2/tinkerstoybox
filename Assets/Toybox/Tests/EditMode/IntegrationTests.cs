using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using TMPro;
using Toybox.Art;
using Toybox.Audio;
using Toybox.EditorTools;
using Toybox.Engine;
using Toybox.Platform;
using Toybox.Render;
using Toybox.Toys;
using Toybox.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Toybox.Tests
{
    // ----------------------------------------------------------------------------------------------
    // Milestone 2 integration: what only shows once every area's work runs together.
    // ----------------------------------------------------------------------------------------------

    /// <summary>The look, where two areas meet (or where the art bible's numbers met the first-person camera).</summary>
    public class IntegrationLookTests : RoomFixture
    {
        static void Ground(LevelContext ctx)
        {
            ctx.AddStatic(BasicToys.Slab(new Vector3(60f, 1f, 60f)), new Vector3(0f, -0.5f, 0f));
            ctx.SetSpawn(Vector3.zero, 0f);
        }

        void Frames(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                Game.Tick();
                Presentation.Frame(Sim.Dt, 1f);
            }
        }

        [Test]
        public void ASignalKeepsItsHue_AtEveryGain()
        {
            foreach (Signal signal in new[] { Palette.Amber, Palette.Go, Palette.Hazard })
            {
                Color linear = Palette.Lin(signal.Color);
                float largest = Mathf.Max(linear.r, Mathf.Max(linear.g, linear.b));
                Color full = signal.Emission;
                float top = Mathf.Max(full.r, Mathf.Max(full.g, full.b));
                Assert.AreEqual(signal.Gain * largest, top, 1e-4f, signal.Name + ": its largest channel carries the gain (what bloom sees)");
                Assert.Greater(top, 1.2f, signal.Name + " blooms");

                // What the screen shows is every channel cut off at 1: that has to be the signal's own hue.
                for (int c = 0; c < 3; c++)
                {
                    float shown = Mathf.Min(1f, full[c]);
                    Assert.AreEqual(linear[c] / largest, shown, 1e-4f, signal.Name + " channel " + c + " after clipping");
                }
            }

            // The idle pulse is seen in the core, not only in the bloom: dimmer at its low, same hue.
            Color low = Palette.Amber.EmissionAt(Palette.Amber.PulseMin), high = Palette.Amber.EmissionAt(Palette.Amber.Gain);
            Assert.Less(low.r, 0.7f, "the low of the pulse is visibly dimmer");
            Assert.LessOrEqual(Mathf.Max(low.r, Mathf.Max(low.g, low.b)), 1f, "and nothing of it clips");
            Assert.AreEqual(high.g / Mathf.Min(1f, high.r), low.g / low.r, 1e-3f, "amber stays amber through the pulse");
            Assert.AreEqual(Color.black.r, Palette.Go.EmissionAt(0f).g, "gain 0 is dark");

            // White has no hue to keep: all three channels carry the gain.
            Color exit = Palette.Exit.Emission;
            Assert.AreEqual(3f, exit.r, 1e-4f);
            Assert.AreEqual(3f, exit.g, 1e-4f);
            Assert.AreEqual(3f, exit.b, 1e-4f);
        }

        [Test]
        public void AFlatSideNextToARoundedCorner_IsShadedFlat()
        {
            // A long box outline with one finely rounded corner, extruded: the normals along the long
            // sides must be those sides' own, not tilted toward the corner's first chord.
            var outline = new List<Vector2> { new Vector2(-2f, -0.25f), new Vector2(2f, -0.25f) };
            for (int k = 0; k <= 6; k++)
            {
                float angle = Mathf.PI * 0.5f * k / 6f;
                outline.Add(new Vector2(2f + 0.05f * (Mathf.Sin(angle) - 0f) - 0.05f + 0.05f, 0.2f + 0.05f * (1f - Mathf.Cos(angle)) - 0.05f));
            }
            outline.Add(new Vector2(-2f, 0.25f));
            Mesh mesh = MeshKit.Extrude(outline, 1f, 0f, 60f);
            try
            {
                Vector3[] vertices = mesh.vertices, normals = mesh.normals;
                int checkedCount = 0;
                for (int i = 0; i < vertices.Length; i++)
                {
                    // Wall vertices on the bottom side, away from its ends' hard corners.
                    if (Mathf.Abs(normals[i].z) > 0.5f || Mathf.Abs(vertices[i].y + 0.25f) > 1e-4f) continue;
                    if (normals[i].y > -0.5f) continue;
                    Assert.Greater(-normals[i].y, 0.9995f, "a vertex of the long bottom side at " + vertices[i] + " has normal " + normals[i]);
                    checkedCount++;
                }
                Assert.Greater(checkedCount, 0, "the test found the side it is about");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void ALatheProfile_KeepsItsCapFlat()
        {
            // A cylinder with a small bevel: the cap's rim must point up like the cap's middle.
            Mesh mesh = MeshKit.Cylinder(1f, 1f, 24, 0.04f, 2);
            try
            {
                Vector3[] vertices = mesh.vertices, normals = mesh.normals;
                float worst = 1f;
                for (int i = 0; i < vertices.Length; i++)
                {
                    bool onCap = Mathf.Abs(vertices[i].y - 0.5f) < 1e-4f && new Vector2(vertices[i].x, vertices[i].z).magnitude <= 0.96f + 1e-4f;
                    if (onCap) worst = Mathf.Min(worst, normals[i].y);
                }
                Assert.Greater(worst, 0.999f, "the cap's own vertices are tilted by " + Mathf.Acos(worst) * Mathf.Rad2Deg + " degrees toward the bevel");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void EveryExitShowsTheFourPaneMark_SmallWhileLocked_AndItIsNoCollider()
        {
            Exit open = null, shut = null;
            Load("sunny-rug", ctx =>
            {
                Ground(ctx);
                open = ctx.AddExit(new Vector3(-4f, 1.5f, 12f), new Vector3(4f, 3f, 2f));
                shut = ctx.AddExit(new Vector3(4f, 1.5f, 12f), new Vector3(4f, 3f, 2f)).Lock();
            });
            Present(QualityTier.Medium, typeof(ExitMarks));
            ExitMarks marks = Presentation.Get<ExitMarks>();
            Assert.IsNotNull(marks);
            Presentation.Frame(0f, 1f);
            if (!Presentation.Context.HasGraphics) Assert.Ignore("no graphics device: nothing is drawn");

            Assert.AreEqual(2, marks.Count);
            Renderer first = marks.RendererOf(0), second = marks.RendererOf(1);
            Assert.Less(Vector3.Distance(open.Position, first.transform.position), 1e-4f, "the mark stands in the middle of its exit");
            Assert.AreEqual(1f, marks.OpenOf(0));
            Assert.AreEqual(0f, marks.OpenOf(1));
            Assert.AreNotSame(first.sharedMaterial, second.sharedMaterial, "open and locked are drawn differently");
            Assert.Less(second.transform.localScale.x, first.transform.localScale.x, "a locked mark is smaller");
            Assert.AreEqual(3f * ExitMarks.Fill, first.transform.localScale.x, 1e-4f, "the smaller side of a 4 x 3 x 2 exit, mostly filled");
            Assert.AreEqual("Toybox/Flat", first.sharedMaterial.shader.name);
            Assert.AreEqual((float)FlatShape.FourPane, first.sharedMaterial.GetFloat("_Shape"));
            Assert.AreEqual(ExitMarks.OpenGain, first.sharedMaterial.GetVector("_Color").x, 1e-4f, "the Exit signal: white x 3");
            Assert.AreEqual(ShadowCastingMode.Off, first.shadowCastingMode);
            // It faces the camera, upright.
            Vector3 toCamera = Presentation.Camera.transform.position - first.transform.position;
            toCamera.y = 0f;
            Assert.Greater(Vector3.Dot(-first.transform.forward, toCamera.normalized), 0.999f);
            Assert.AreEqual(0, Presentation.Context.Root.GetComponentsInChildren<Collider>(true).Length, "presentation adds no colliders");

            // Unlocking pops it to its size in 180 ms.
            shut.Unlock();
            Frames(TestHelpers.Ticks(0.3f));
            Assert.AreEqual(1f, marks.OpenOf(1), 1e-4f);
            Assert.AreSame(first.sharedMaterial, second.sharedMaterial);
            Assert.AreEqual(first.transform.localScale.x, second.transform.localScale.x, 1e-4f);

            // A new level, new marks; none are left behind.
            Game.LoadLevel(new RoomLevel("sunny-rug", Ground));
            Presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(0, marks.Count);
        }

        [Test]
        public void ALongToyWithoutProxies_GetsThreePoolsAlongItsLength()
        {
            Prop plank = null, cube = null;
            Load("sunny-rug", ctx =>
            {
                Ground(ctx);
                plank = ctx.AddProp(BasicToys.Block(new Vector3(1.2f, 0.15f, 4f), Palette.Tangerine), new Vector3(0f, 0.3f, 8f), new PropOptions { Scale = 4f });
                cube = ctx.AddProp(BasicToys.Block(1f, Palette.Cherry), new Vector3(12f, 0.5f, 8f));
            });
            Present(QualityTier.High, typeof(PoolSystem));
            PoolSystem pools = Presentation.Get<PoolSystem>();
            Presentation.Frame(0f, 1f);

            Assert.AreEqual(4, pools.Count, "three for the plank, one for the cube");
            float single = PoolSystem.RadiusFactor * plank.Radius;
            int along = 0;
            for (int i = 0; i < pools.Count; i++)
            {
                Vector4 pool = pools.Position(i);
                if (Mathf.Abs(pool.x - cube.Center.x) < 0.01f)
                {
                    Assert.AreEqual(PoolSystem.RadiusFactor * cube.Radius, pool.w, 1e-4f, "a compact toy keeps its one sphere");
                    continue;
                }
                along++;
                Assert.Less(pool.w, single * 0.5f, "each of the three is far smaller than one sphere around the whole plank");
                Assert.AreEqual(plank.Center.x, pool.x, 1e-3f);
                Assert.LessOrEqual(Mathf.Abs(pool.z - plank.Center.z), 2f / 3f * 2f * 4f + 1e-3f, "inside the plank's length");
            }
            Assert.AreEqual(3, along);
        }

        [Test]
        public void TheToyJustLetGo_TakesNoFocusSweep_UntilItIsLookedAtAgain()
        {
            Prop block = null;
            Load("sunny-rug", ctx =>
            {
                Ground(ctx);
                block = ctx.AddProp(BasicToys.Block(1f, Palette.Cherry), new Vector3(0f, 0.5f, 4f));
            });
            Present(QualityTier.Medium, typeof(LightingRig), typeof(HeldLook));
            HeldLook held = Presentation.Get<HeldLook>();
            TestHelpers.LookAt(Game.Player, block.Center);
            Frames(2);
            Assert.AreEqual(1, held.SweepCount, "aiming at a toy sweeps it once (9.1)");
            Frames(TestHelpers.Ticks(1f));
            Assert.AreEqual(0, held.SweepCount);

            Input.Once.GrabPressed = true;
            Frames(TestHelpers.Ticks(0.3f));
            Assert.AreSame(block, held.Held);
            Input.Once.GrabPressed = true;
            Frames(3);
            Assert.IsNull(held.Held);
            Assert.AreSame(block, Game.Grabber.Focus, "the crosshair is still on it");
            Assert.AreEqual(0, held.SweepCount, "the release is its own reveal: no white band across the toy");

            // Away and back: now it is a new look at it.
            Game.Player.AddLook(60f, 0f);
            Frames(3);
            TestHelpers.LookAt(Game.Player, block.Center);
            Frames(3);
            Assert.AreEqual(1, held.SweepCount);
        }

        [Test]
        public void AToyThatCanNoLongerBeLifted_LosesItsRim_In300ms()
        {
            Prop block = null, fixedOne = null;
            Load("sunny-rug", ctx =>
            {
                Ground(ctx);
                block = ctx.AddProp(BasicToys.Block(1f, Palette.Cherry), new Vector3(0f, 0.5f, 4f));
                fixedOne = ctx.AddProp(BasicToys.Block(1f, Palette.Grape), new Vector3(3f, 0.5f, 4f), new PropOptions { Grabbable = false });
            });
            Present(QualityTier.Medium, typeof(RimFade));
            RimFade fade = Presentation.Get<RimFade>();
            Renderer renderer = block.GameObject.GetComponentInChildren<Renderer>();
            float rim = renderer.sharedMaterial.GetFloat("_Rim");
            Assert.Greater(rim, 0.3f, "a candy toy has a rim");
            var read = new MaterialPropertyBlock();

            Game.Tick();
            Presentation.Frame(Sim.Dt, 1f);
            Assert.AreEqual(1f, fade.ShownOf(block));
            Assert.AreEqual(0f, fade.ShownOf(fixedOne), "not liftable from the start: no rim, and no fade to get there");
            fixedOne.GameObject.GetComponentInChildren<Renderer>().GetPropertyBlock(read);
            Assert.AreEqual(0f, read.GetFloat("_Rim"));

            block.Grabbable = false;
            Frames(TestHelpers.Ticks(0.15f));
            Assert.That(fade.ShownOf(block), Is.InRange(0.4f, 0.6f), "half way after 150 ms");
            renderer.GetPropertyBlock(read);
            Assert.AreEqual(rim * fade.ShownOf(block), read.GetFloat("_Rim"), 1e-4f, "written per renderer, the shared material untouched");
            Assert.AreEqual(rim, renderer.sharedMaterial.GetFloat("_Rim"));
            Frames(TestHelpers.Ticks(0.2f));
            Assert.AreEqual(0f, fade.ShownOf(block));

            block.Grabbable = true;
            Frames(TestHelpers.Ticks(0.35f));
            Assert.AreEqual(1f, fade.ShownOf(block), "and back when it can be lifted again");
            renderer.GetPropertyBlock(read);
            Assert.AreEqual(rim, read.GetFloat("_Rim"), 1e-4f);
        }

        [Test]
        public void AToyLetGoAtAnotherSize_GetsItsDimensionCallout()
        {
            Prop block = null;
            Load("sunny-rug", ctx =>
            {
                Ground(ctx);
                ctx.AddStatic(BasicToys.Slab(new Vector3(30f, 12f, 1f)), new Vector3(0f, 6f, 20f));
                block = ctx.AddProp(BasicToys.Block(1f, Palette.Cherry), new Vector3(0f, 0.5f, 4f));
            });
            Present(QualityTier.Medium, typeof(DimensionCallout));
            DimensionCallout callout = Presentation.Get<DimensionCallout>();
            if (!Presentation.Context.HasGraphics) Assert.Ignore("no graphics device: nothing is drawn");

            // Picked up and put down where it was: no change of size, nothing to call out.
            TestHelpers.LookAt(Game.Player, block.Center);
            Input.Once.GrabPressed = true;
            Frames(12);
            Input.Once.GrabPressed = true;
            Frames(3);
            Assert.IsNull(callout.Shown, "x" + block.Scale + ": within 15% of the size it was picked up with");

            // Carried to the far wall: several times the size.
            Frames(TestHelpers.Ticks(1f));
            TestHelpers.LookAt(Game.Player, block.Center);
            Input.Once.GrabPressed = true;
            Frames(5);
            TestHelpers.LookAt(Game.Player, new Vector3(0f, 4f, 19f));
            Frames(5);
            float grabbedAt = Game.Grabber.GrabScale;
            Input.Once.GrabPressed = true;
            Frames(3);
            Assert.Greater(block.Scale, grabbedAt * 1.5f);
            Assert.AreSame(block, callout.Shown);
            Assert.AreEqual(block.Scale, callout.Height, block.Scale * 0.05f, "the line measures the toy's true height (a cube of that scale)");
            if (callout.Text != null) StringAssert.StartsWith("×", callout.Text);
            Frames(TestHelpers.Ticks(0.2f));
            Assert.AreEqual(1f, callout.Opacity, 1e-3f, "in after 120 ms");
            Assert.AreEqual(0, Presentation.Context.Root.GetComponentsInChildren<Collider>(true).Length, "a drawing, not a body");
            // Beside the toy, on the level of its base, facing the camera.
            Assert.Greater(Vector3.Distance(callout.Root.position, block.Center), block.Scale * 0.5f);
            Frames(TestHelpers.Ticks(1.4f));
            Assert.IsNull(callout.Shown, "gone after a second and a half");
            Assert.AreEqual(0f, callout.Opacity);
        }

        // Level 3 makes an 11.4 apple a 0.45 marble: a factor of 0.04, which one decimal wrote as "×0.0".
        [Test]
        public void TheDimensionCallout_WritesAVerySmallFactor_WithADigitInIt()
        {
            Assert.AreEqual("×12", DimensionCallout.FactorText(12.3f));
            Assert.AreEqual("×3.2", DimensionCallout.FactorText(3.21f));
            Assert.AreEqual("×0.5", DimensionCallout.FactorText(0.52f));
            Assert.AreEqual("×0.1", DimensionCallout.FactorText(0.097f));
            Assert.AreEqual("×0.04", DimensionCallout.FactorText(0.0395f));
            Assert.AreEqual("×0.004", DimensionCallout.FactorText(0.0041f));
        }

        // Level 3 lets the marble go a step and a half from the eye. The 1.7 figure stood there too, half the
        // picture tall, between the player and the flap that falls open; the factor's letters were as tall.
        [Test]
        public void TheDimensionCallout_CloseToTheCamera_DoesWithoutItsFigure_AndWritesSmall()
        {
            Assert.AreEqual(0f, DimensionCallout.FigureAt(1.5f / DimensionCallout.FigureHeight), "a step and a half away: no figure");
            Assert.AreEqual(0f, DimensionCallout.FigureAt(DimensionCallout.FigureGone));
            Assert.AreEqual(1f, DimensionCallout.FigureAt(DimensionCallout.FigureNear), "in full from here on");
            Assert.AreEqual(1f, DimensionCallout.FigureAt(9f / DimensionCallout.FigureHeight), "Level 4's domino, nine away");
            Assert.That(DimensionCallout.FigureAt((DimensionCallout.FigureGone + DimensionCallout.FigureNear) * 0.5f), Is.InRange(0.4f, 0.6f), "it fades in between");

            Assert.AreEqual(DimensionCallout.LabelRest, DimensionCallout.LabelSizeFor(0.45f, 20f), 1e-4f, "far away the letters keep their least size");
            Assert.AreEqual(8f * 1.4f, DimensionCallout.LabelSizeFor(8f, 20f), 1e-4f, "and grow with the toy");
            Assert.AreEqual(DimensionCallout.LabelMax, DimensionCallout.LabelSizeFor(40f, 60f), 1e-4f);
            Assert.AreEqual(1.5f * DimensionCallout.LabelPerDistance, DimensionCallout.LabelSizeFor(0.45f, 1.5f), 1e-4f, "close by they are sized for the distance");
            Assert.AreEqual(DimensionCallout.LabelMin, DimensionCallout.LabelSizeFor(0.1f, 0.3f), 1e-4f);
            Assert.AreEqual(2f * 1.4f, DimensionCallout.LabelSizeFor(2f, 1.5f), 1e-4f, "a big toy close by keeps the size its height asks for");

            // The same in a level: a block taken from far off and let go at the feet.
            Prop block = null;
            Load("sunny-rug", ctx =>
            {
                Ground(ctx);
                block = ctx.AddProp(BasicToys.Block(4f, Palette.Cherry), new Vector3(0f, 2f, 24f));
            });
            Present(QualityTier.Medium, typeof(DimensionCallout));
            DimensionCallout callout = Presentation.Get<DimensionCallout>();
            if (!Presentation.Context.HasGraphics) Assert.Ignore("no graphics device: nothing is drawn");

            TestHelpers.LookAt(Game.Player, block.Center);
            Input.Once.GrabPressed = true;
            Frames(5);
            Assert.IsTrue(block.Held);
            TestHelpers.LookAt(Game.Player, new Vector3(0f, 0f, 1.2f));
            Frames(5);
            Input.Once.GrabPressed = true;
            Frames(3);
            Assert.Less(block.Scale, 1f, "a small block at the feet");
            Assert.AreSame(block, callout.Shown);
            Frames(TestHelpers.Ticks(0.2f));
            Assert.AreEqual(1f, callout.Opacity, 1e-3f, "the line and the factor are there");
            Assert.AreEqual(0f, callout.FigureShown, "the figure is not: it would stand " +
                            Vector3.Distance(Presentation.Context.Camera.transform.position, callout.Root.position).ToString("0.0") + " from the eye");
            if (callout.Text != null)
            {
                float away = Vector3.Distance(Presentation.Context.Camera.transform.position, block.Center);
                Assert.LessOrEqual(callout.LabelSize, Mathf.Max(DimensionCallout.LabelMin, away * DimensionCallout.LabelPerDistance) + 1e-3f,
                    "the factor is written for a reader " + away.ToString("0.0") + " away");
            }
        }

        [Test]
        public void AHeavyToyComingDown_ShakesThePicture_ALightOneDoesNot()
        {
            Assert.AreEqual(0f, ImpactShake.AmplitudeFor(0.9f), "nothing below mass 1");
            Assert.AreEqual(0.05f, ImpactShake.AmplitudeFor(10f), 1e-5f, "0.05 per decade of mass");
            Assert.AreEqual(0.25f, ImpactShake.AmplitudeFor(1e9f), "never more than 0.25");

            Prop block = null;
            Load("sunny-rug", ctx =>
            {
                Ground(ctx);
                block = ctx.AddProp(BasicToys.Block(1f, Palette.Cherry), new Vector3(0f, 0.5f, 4f), new PropOptions { Density = 40f });
            });
            Present(QualityTier.Medium, typeof(ImpactShake));
            ImpactShake shake = Presentation.Get<ImpactShake>();
            Camera camera = Presentation.Camera;

            // Picked up and let go a little above the floor: its first landing is felt.
            TestHelpers.LookAt(Game.Player, block.Center);
            Input.Once.GrabPressed = true;
            Frames(5);
            Assert.AreSame(block, Game.Grabber.Held);
            TestHelpers.LookAt(Game.Player, new Vector3(0f, 3f, 5f));
            Frames(5);
            Input.Once.GrabPressed = true;
            float felt = 0f;
            for (int i = 0; i < 120; i++)
            {
                Game.Tick();
                Presentation.Frame(Sim.Dt, 1f);
                felt = Mathf.Max(felt, shake.Offset.magnitude);
                Assert.Less(Vector3.Distance(camera.transform.position - shake.Offset, Game.Player.EyeAt(1f)), 1e-3f, "the camera is the eye plus the shake");
            }
            Assert.Greater(block.Mass, 10f);
            Assert.Greater(felt, 0.01f, "a block of mass " + block.Mass + " coming down shakes the picture");
            Assert.LessOrEqual(felt, ImpactShake.MaxAmplitude * 1.5f);
            Assert.AreEqual(Vector3.zero, shake.Offset, "and it is over 180 ms later");

            // With Reduce Motion nothing shakes.
            Settings.ReduceMotion = true;
            TestHelpers.LookAt(Game.Player, block.Center);
            Input.Once.GrabPressed = true;
            Frames(5);
            TestHelpers.LookAt(Game.Player, new Vector3(0f, 3f, 5f));
            Frames(5);
            Input.Once.GrabPressed = true;
            felt = 0f;
            for (int i = 0; i < 120; i++)
            {
                Game.Tick();
                Presentation.Frame(Sim.Dt, 1f);
                felt = Mathf.Max(felt, shake.Offset.magnitude);
            }
            Assert.AreEqual(0f, felt);
        }

        [Test]
        public void TheScreenshotTool_ShowsWhatTheGameShows_HdrIncluded()
        {
            RequireGraphics();
            // A lamp far over the bloom threshold: through an 8-bit target nothing would glow around it.
            Load("none", ctx =>
            {
                Ground(ctx);
                GameObject lamp = BasicToys.Ball(0.5f);
                foreach (Renderer renderer in lamp.GetComponentsInChildren<Renderer>())
                    renderer.sharedMaterial = Materials.Emissive(ToyRecipe.Lamp, Palette.Paper, Color.white * 6f);
                foreach (Collider collider in lamp.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(collider);
                lamp.AddComponent<BoxCollider>();
                ctx.AddStatic(lamp, new Vector3(0f, 1.55f, 6f));
                ctx.AddStatic(BasicToys.Slab(new Vector3(8f, 6f, 0.5f), Palette.Ink), new Vector3(0f, 2f, 8f));
            });
            Presentation = Presentation.Create(Game, new PresentationOptions { Quality = QualityTier.Medium });
            Presentation.Frame(0f, 1f);
            Object.DestroyImmediate(Shots.Photograph(Presentation.Camera, 320, 180));
            Texture2D image = Shots.Photograph(Presentation.Camera, 320, 180);
            try
            {
                // Beside the lamp's disc (about 11 px in radius here) and further along the dark wall behind
                // it (which reaches 64 px to either side).
                Color beside = image.GetPixel(160 + 17, 90), far = image.GetPixel(160 + 55, 90);
                Assert.Greater(beside.grayscale, far.grayscale + 0.03f, "bloom around the lamp: " + beside + " against " + far);
                Assert.Greater(image.GetPixel(160, 90).grayscale, 0.97f, "the lamp itself is white");
            }
            finally
            {
                Object.DestroyImmediate(image);
            }
        }
    }

    /// <summary>What levels compute from, checked in the physics world they will run in.</summary>
    public class IntegrationPhysicsTests : SimTest
    {
        // The acceleration a flat box on a level floor ends up with under a steady sideways push.
        float Slide(float friction, float push)
        {
            Prop box = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.SetSpawn(new Vector3(0f, 0f, -20f), 0f);
                box = ctx.AddProp(BasicToys.Block(new Vector3(1f, 0.2f, 1f)), new Vector3(0f, 0.1f, 0f), new PropOptions { Friction = friction });
                ctx.OnUpdate(dt => box.Body.AddForce(Vector3.right * push, ForceMode.Acceleration));
            });
            RunSeconds(0.5f);
            float before = box.Velocity.x;
            RunSeconds(0.5f);
            float acceleration = (box.Velocity.x - before) / 0.5f;
            Game.Dispose();
            Game = null;
            return acceleration;
        }

        [Test]
        public void FrictionOfAFlatContact_IsItsCoefficient_NotTwiceThat()
        {
            // Coefficient 0.6 on the box; the floor's is TestHelpers' (the engine combines the two by their
            // average). Whatever that pair comes to, a push below friction x gravity must not move the box,
            // one above it must leave exactly the difference - and the threshold must be mu x g, not 2 mu x g.
            const float g = Game.Gravity;
            float stuck = Slide(0.6f, 0.45f * g);
            Assert.AreEqual(0f, stuck, 0.05f, "a push of 0.45 g against a coefficient of 0.6 moves nothing");
            float sliding = Slide(0.6f, 0.9f * g);
            Assert.AreEqual((0.9f - 0.6f) * g, sliding, 0.1f * g, "a push of 0.9 g leaves 0.3 g: the coefficient is 0.6, not 1.2");
        }
    }

    /// <summary>The flow from end to end with every presenter of the game, driven through the real GameRunner.</summary>
    public class IntegrationFlowTests
    {
        GameObject host;
        GameRunner runner;
        FakeDevices devices;

        [SetUp]
        public void CleanSlate()
        {
            Game.Current?.Dispose();
            Settings.Use(new MemoryStore());
            UiCapture.Request = "";
            Materials.Plain = false;
        }

        [TearDown]
        public void Shutdown()
        {
            runner?.Shutdown();
            runner = null;
            if (host != null) Object.DestroyImmediate(host);
            host = null;
            Game.Current?.Dispose();
            AudioPresenter.Overrides = null;
            UiCapture.Reset();
            Settings.Use(null);
            Materials.Plain = false;
            Materials.Tier = QualityTier.Medium;
        }

        void Begin(string url)
        {
            host = new GameObject("Integration Runner") { hideFlags = HideFlags.DontSave };
            runner = host.AddComponent<GameRunner>();
            devices = new FakeDevices();
            AudioPresenter.Overrides = new AudioPresenter.Options { Audible = false, Unlocked = true };
            runner.Begin(LaunchOptions.FromUrl(url), new RunnerOptions { Devices = devices, Levels = UiTestKit.Levels(3), Store = new MemoryStore() });
            AudioPresenter.Overrides = null;
        }

        void Frames(int count = 1, float dt = Sim.Dt)
        {
            for (int i = 0; i < count; i++) runner.Frame(dt);
        }

        [Test]
        public void EveryPresenterOfTheGame_RunsTogether()
        {
            Begin("?level=1");
            Frames(5);
            Presentation presentation = runner.Presentation;
            Assert.IsTrue(presentation.LookProvided);
            Assert.IsTrue(presentation.HudProvided);
            foreach (System.Type type in new[]
            {
                typeof(QualityPresenter), typeof(PostLook), typeof(LightingRig), typeof(RoomVisuals), typeof(PoolSystem), typeof(ExitMarks),
                typeof(DimensionCallout), typeof(HeldLook), typeof(RimFade), typeof(ToySquash), typeof(ImpactShake), typeof(AudioPresenter), typeof(MenuPresenter), typeof(HudPresenter),
            })
            {
                bool found = false;
                foreach (IPresenter presenter in presentation.Presenters) found |= presenter.GetType() == type;
                Assert.IsTrue(found, type.Name + " is not among the active presenters");
            }
            foreach (IPresenter presenter in presentation.Presenters)
            {
                Assert.AreNotEqual(typeof(PlainLook), presenter.GetType(), "the stand-ins are retired");
                Assert.AreNotEqual(typeof(DebugHudPresenter), presenter.GetType());
            }
        }

        [Test]
        public void ACompletedLevel_FlashesOnce_AndTheMenusAreHeard()
        {
            Begin("?level=1");
            Frames(10);
            Presentation presentation = runner.Presentation;
            PostLook post = presentation.Get<PostLook>();
            MenuPresenter menu = presentation.Get<MenuPresenter>();
            AudioPresenter audio = presentation.Get<AudioPresenter>();
            Assert.IsNotNull(post);
            Assert.IsNotNull(menu);
            Assert.IsNotNull(audio);
            float normal = post.Exposure;

            runner.Game.CompleteLevel();
            Frames();
            Assert.AreEqual(FlowState.LevelComplete, runner.Flow.State);
            Assert.AreSame(menu.Complete, menu.Current);
            Assert.AreEqual(normal + PostLook.CompleteFlashEv, post.Exposure, 1e-4f, "the flash of 9.7 is the exposure's");
            Assert.AreEqual(0f, menu.Complete.FlashAlpha, "and the Paper overlay stays away: one flash, not two");
            Assert.AreEqual(1, audio.Output.Count(SoundId.LevelComplete), "shutter, arpeggio and stamp");

            // The card's picture is in the game's own colour format, or bloom would stop while it shows.
            if (presentation.Context.HasGraphics && SystemInfo.IsFormatSupported(UnityEngine.Experimental.Rendering.GraphicsFormat.B10G11R11_UFloatPack32, UnityEngine.Experimental.Rendering.GraphicsFormatUsage.Render))
            {
                Assert.IsNotNull(menu.Backdrop.Target);
                Assert.AreEqual(UnityEngine.Experimental.Rendering.GraphicsFormat.B10G11R11_UFloatPack32, menu.Backdrop.Target.graphicsFormat);
            }

            // The card's buttons click; moving the focus ticks.
            Frames(TestHelpers.Ticks(0.8f));
            int clicks = audio.Output.Count(SoundId.UiClick), hovers = audio.Output.Count(SoundId.UiHover);
            menu.Move(UnityEngine.EventSystems.MoveDirection.Left);
            Frames();
            Assert.AreEqual(hovers + 1, audio.Output.Count(SoundId.UiHover), "the focus moved: a tick");
            menu.Move(UnityEngine.EventSystems.MoveDirection.Right);
            menu.Submit();
            Frames(2);
            Assert.AreEqual(clicks + 1, audio.Output.Count(SoundId.UiClick), "Next was pressed: a click");
            Assert.AreEqual(FlowState.Playing, runner.Flow.State);
            Assert.AreEqual(2, runner.LevelId);
        }

        /// <summary>
        /// The menus' focus tick is rate-limited (a dragged slider must purr, not buzz), by the clock. That
        /// limit is one game's own: the first tick of the next game is heard even when no time has passed
        /// since the last tick of the game before it - two tests in one editor frame, a game restarted at
        /// once. (It was a static, and ACompletedLevel_FlashesOnce_AndTheMenusAreHeard lost its tick
        /// whenever the test that ran before it had moved a focus in the same frame.)
        /// </summary>
        [Test]
        public void TheFirstFocusTickOfAGame_IsHeard_HoweverSoonAfterTheLastGamesTick()
        {
            for (int game = 0; game < 2; game++)
            {
                Begin("?level=1");
                Frames(10);
                MenuPresenter menu = runner.Presentation.Get<MenuPresenter>();
                AudioPresenter audio = runner.Presentation.Get<AudioPresenter>();
                runner.Game.CompleteLevel();
                Frames(TestHelpers.Ticks(0.8f));
                Assert.AreSame(menu.Complete, menu.Current);
                int hovers = audio.Output.Count(SoundId.UiHover);
                Assert.IsTrue(menu.Move(UnityEngine.EventSystems.MoveDirection.Left), "test setup: the focus moved");
                Frames();
                Assert.AreEqual(hovers + 1, audio.Output.Count(SoundId.UiHover), "game " + (game + 1) + ": its first focus tick");
                // Within one game the limit holds: a second move in the same instant is not a second tick.
                Assert.IsTrue(menu.Move(UnityEngine.EventSystems.MoveDirection.Right));
                Frames();
                Assert.AreEqual(hovers + 1, audio.Output.Count(SoundId.UiHover), "game " + (game + 1) + ": two ticks in one instant are one");
                Shutdown();
                CleanSlate();
            }
        }

        [Test]
        public void GadgetEvents_AreHeard()
        {
            // Every name in the table is a mirror that exists on GameEvents, with the one payload type.
            foreach (KeyValuePair<string, SoundId> pair in GadgetSounds.Named)
            {
                System.Reflection.EventInfo mirror = typeof(GameEvents).GetEvent(pair.Key);
                Assert.IsNotNull(mirror, "GameEvents has no event called " + pair.Key);
                Assert.AreEqual(typeof(System.Action<Toybox.Gadgets.GadgetEvent>), mirror.EventHandlerType, pair.Key);
                Assert.AreNotEqual(SoundId.None, pair.Value, pair.Key);
                Assert.AreEqual(pair.Value, GadgetSounds.SoundFor(pair.Key));
            }
            Assert.AreEqual(SoundId.ButtonPress, GadgetSounds.SoundFor("PlatePressed"));
            Assert.AreEqual(SoundId.ButtonRelease, GadgetSounds.SoundFor("PlateReleased"));

            // In the game: a plate pressed by a crate is heard where the plate is.
            Begin("?level=1");
            Frames(10);
            AudioPresenter audio = runner.Presentation.Get<AudioPresenter>();
            int presses = audio.Output.Count(SoundId.ButtonPress), chimes = audio.Output.Count(SoundId.ExitOpen);
            Game game = runner.Game;
            game.Events.RaisePlatePressed(new Toybox.Gadgets.GadgetEvent { Position = new Vector3(1f, 0f, 2f) });
            game.Events.RaiseDoorOpened(new Toybox.Gadgets.GadgetEvent { Position = new Vector3(3f, 0f, 2f) });
            Frames(2);
            Assert.AreEqual(presses + 1, audio.Output.Count(SoundId.ButtonPress), "PlatePressed: the button");
            Assert.AreEqual(chimes + 1, audio.Output.Count(SoundId.ExitOpen), "DoorOpened: the rising chime");
        }

        [Test]
        public void EscOnTheSettingsCard_GoesBackToThePauseCard_NotOnToTheGame()
        {
            Begin("?level=1");
            Frames(10);
            MenuPresenter menu = runner.Presentation.Get<MenuPresenter>();
            devices.State.EscapePressed = true;
            Frames();
            devices.State.EscapePressed = false;
            Frames();
            Assert.AreEqual(FlowState.Paused, runner.Flow.State);
            Assert.AreSame(menu.Pause, menu.Current);

            menu.OpenSettings();
            Frames(2);
            Assert.AreSame(menu.SettingsCard, menu.Current);
            Assert.IsTrue(runner.Presentation.Context.EscapeClaimed);

            // Esc: the runner leaves it to the card (in the game the EventSystem hands it over as Cancel).
            devices.State.EscapePressed = true;
            Frames();
            devices.State.EscapePressed = false;
            Assert.AreEqual(FlowState.Paused, runner.Flow.State, "still paused");
            menu.Cancel();
            Frames(2);
            Assert.AreSame(menu.Pause, menu.Current, "back on the pause card");
            Assert.IsFalse(runner.Presentation.Context.EscapeClaimed);

            // And from there Esc resumes, as before.
            devices.State.EscapePressed = true;
            Frames();
            devices.State.EscapePressed = false;
            Assert.AreEqual(FlowState.Playing, runner.Flow.State);
        }

        [Test]
        public void ThePlainLook_StillWorks_AsTheFallback()
        {
            Begin("?level=1&plain=1");
            Frames(10);
            Presentation presentation = runner.Presentation;
            Assert.IsTrue(presentation.Context.Plain);
            bool plainLook = false, debugHud = false;
            foreach (IPresenter presenter in presentation.Presenters)
            {
                plainLook |= presenter is PlainLook;
                debugHud |= presenter is DebugHudPresenter;
                Assert.IsFalse(presenter is LightingRig || presenter is RoomVisuals || presenter is PostLook || presenter is MenuPresenter || presenter is HudPresenter,
                    presenter.GetType().Name + " belongs to the look and must sit the plain look out");
            }
            Assert.IsTrue(plainLook, "the plain sun");
            Assert.IsTrue(debugHud, "the debug HUD");
            Assert.IsNotNull(presentation.Get<AudioPresenter>(), "audio is neither look nor HUD: it always runs");

            // A toy is grabbed, held and put down without anything of the look being needed.
            Game game = runner.Game;
            Prop block = ((UiLevel)game.Level).Block;
            Assert.AreEqual("Universal Render Pipeline/Lit", block.GameObject.GetComponentInChildren<Renderer>().sharedMaterial.shader.name);
            TestHelpers.LookAt(game.Player, block.Center);
            devices.State.GrabKeyPressed = true;
            Frames(3);
            Assert.AreSame(block, game.Grabber.Held);
            Assert.IsFalse(StickerLook.Border, "no sticker border in the plain look");
            devices.State.GrabKeyPressed = true;
            Frames(3);
            Assert.IsNull(game.Grabber.Held);
            game.CompleteLevel();
            Frames(TestHelpers.Ticks(3f));
            Assert.AreEqual(2, runner.LevelId, "with the debug HUD the flow moves on by itself");
        }
    }

    /// <summary>
    /// ART_BIBLE 3.8 and 7.5, item by item, against the assets on disk: what a WebGL build needs so that
    /// the look survives it. (The build itself - 7.5 item 8 - can only be looked at in a browser.)
    /// </summary>
    public class BuildSurvivalTests
    {
        static T Asset<T>(string path) where T : Object
        {
            Assert.IsTrue(File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath), path)), path + " is not on disk - run Toybox.EditorTools.ProjectSetup.Run");
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, path + " is not a " + typeof(T).Name);
            return asset;
        }

        [Test]
        public void EverythingTheWebBuildNeeds_IsOnDisk()
        {
            // 3.8 (1): a template material under Resources for each of the five shaders.
            var shaders = new Dictionary<string, string>
            {
                { "ToyLit", "Toybox/ToyLit" }, { "RoomLit", "Toybox/RoomLit" }, { "Sticker", "Toybox/Sticker" }, { "Flat", "Toybox/Flat" }, { "MacroBand", "Toybox/MacroBand" },
            };
            foreach (KeyValuePair<string, string> pair in shaders)
            {
                Material material = Asset<Material>("Assets/Toybox/Resources/Materials/" + pair.Key + ".mat");
                Assert.AreEqual(pair.Value, material.shader.name, pair.Key + ".mat");
                Assert.AreSame(material, Resources.Load<Material>("Materials/" + pair.Key), "what runtime code clones");
                Assert.IsFalse(ShaderUtil.ShaderHasError(material.shader), pair.Value + " has compile errors");
            }
            Assert.AreEqual("Universal Render Pipeline/Lit", Asset<Material>("Assets/Toybox/Resources/Materials/PlainLit.mat").shader.name, "the ?plain=1 look");

            // 3.8 (2) / 7.5 (4): three URP assets on three Quality levels, all enabled for WebGL; each keeps
            // main-light shadows with two or three cascades and soft shadows, so the shaders' shadow
            // variants are in the build.
            CollectionAssert.AreEqual(new[] { "Low", "Medium", "High" }, QualitySettings.names);
            UniversalRendererData renderer = Asset<UniversalRendererData>("Assets/Toybox/Settings/ToyboxRenderer.asset");
            foreach (TierSpec tier in TierSpec.All)
            {
                var asset = Asset<UniversalRenderPipelineAsset>(PipelineSetup.TierAssetPath(tier.Tier));
                int level = Quality.LevelIndex(tier.Tier);
                Assert.AreSame(asset, QualitySettings.GetRenderPipelineAssetAt(level), tier.Name + ": the Quality level's pipeline");
                Assert.IsTrue(QualitySettings.IsPlatformIncluded("WebGL", level), tier.Name + " is enabled for WebGL");
                Assert.IsTrue(asset.supportsMainLightShadows, tier.Name);
                Assert.IsTrue(asset.supportsSoftShadows, tier.Name);
                Assert.That(asset.shadowCascadeCount, Is.InRange(2, 3), tier.Name + ": never a single cascade");
                Assert.IsTrue(asset.supportsHDR, tier.Name);
                Assert.IsFalse(asset.supportsCameraDepthTexture, tier.Name + ": no depth texture");
                Assert.IsFalse(asset.supportsCameraOpaqueTexture, tier.Name + ": no opaque texture");
                Assert.AreSame(renderer, asset.rendererDataList[0], tier.Name + " uses the shared renderer");
            }

            // 3.8 (3): the variant collection lists the five shaders.
            var variants = Asset<ShaderVariantCollection>("Assets/Toybox/Resources/ToyboxVariants.shadervariants");
            Assert.AreSame(variants, Resources.Load<ShaderVariantCollection>(ShaderWarmup.Resource));
            Assert.AreEqual(5, variants.shaderCount);
            Assert.LessOrEqual(variants.variantCount, TierSpec.MaxShaderPrograms);

            // 7.5 (1): URP's post shaders and the registered global settings.
            Assert.IsNotNull(renderer.postProcessData, "ToyboxRenderer.postProcessData");
            Assert.IsNotNull(GraphicsSettings.GetSettingsForRenderPipeline<UniversalRenderPipeline>(), "URP global settings are registered");
            Asset<Object>("Assets/UniversalRenderPipelineGlobalSettings.asset");

            // 7.5 (2): the volume profile as an asset, every override active and overridden.
            var profile = Asset<VolumeProfile>("Assets/Toybox/Resources/Volumes/ToyboxPost.asset");
            Assert.AreSame(profile, Resources.Load<VolumeProfile>(PostLook.ProfileResource));
            Assert.IsTrue(profile.TryGet(out Bloom bloom) && bloom.active && bloom.intensity.overrideState && bloom.threshold.overrideState, "Bloom");
            Assert.IsTrue(profile.TryGet(out Tonemapping tonemapping) && tonemapping.active && tonemapping.mode.overrideState, "Tonemapping");
            Assert.IsTrue(profile.TryGet(out ColorAdjustments adjustments) && adjustments.active && adjustments.postExposure.overrideState, "Color Adjustments");
            Assert.IsTrue(profile.TryGet(out Vignette vignette) && vignette.active && vignette.intensity.overrideState, "Vignette");

            // 7.5 (3): post variant stripping off.
            var stripping = GraphicsSettings.GetRenderPipelineSettings<URPShaderStrippingSetting>();
            Assert.IsNotNull(stripping);
            Assert.IsFalse(stripping.stripUnusedPostProcessingVariants);

            // 7.5 (5), (6): the two renderer features, each holding its material by a serialized reference,
            // MacroBand before Sticker; the Held layer left to the sticker pass.
            FullScreenPassRendererFeature macroBand = null;
            StickerFeature sticker = null;
            int macroIndex = -1, stickerIndex = -1;
            for (int i = 0; i < renderer.rendererFeatures.Count; i++)
            {
                if (renderer.rendererFeatures[i] is FullScreenPassRendererFeature full && full.name == PostLook.MacroBandFeature)
                {
                    macroBand = full;
                    macroIndex = i;
                }
                if (renderer.rendererFeatures[i] is StickerFeature feature)
                {
                    sticker = feature;
                    stickerIndex = i;
                }
            }
            Assert.IsNotNull(macroBand, "the MacroBand full-screen feature");
            Assert.AreSame(Resources.Load<Material>("Materials/MacroBand"), macroBand.passMaterial);
            Assert.IsNotNull(sticker, "the Sticker feature");
            Assert.AreSame(Resources.Load<Material>("Materials/Sticker"), sticker.Material);
            Assert.Less(macroIndex, stickerIndex, "the lens blur runs before the sticker: the sticker is on the lens");
            Assert.AreEqual(0, renderer.opaqueLayerMask.value & (1 << Layers.Held), "the ordinary passes skip the Held layer");
            Assert.AreEqual(0, renderer.transparentLayerMask.value & (1 << Layers.Held));

            // 7.5 (7): TextMeshPro's resources, the two baked fonts and the three material presets - the
            // sticker preset with its outline and underlay keywords on the asset, where the build finds them.
            Asset<TMP_Settings>("Assets/TextMesh Pro/Resources/TMP Settings.asset");
            Asset<TMP_FontAsset>("Assets/Toybox/Resources/Fonts/Unbounded-Bold SDF.asset");
            Asset<TMP_FontAsset>("Assets/Toybox/Resources/Fonts/Figtree-SemiBold SDF.asset");
            Material stickerText = Asset<Material>("Assets/Toybox/Resources/Fonts/Display-Sticker.mat");
            Assert.IsTrue(stickerText.IsKeywordEnabled(ShaderUtilities.Keyword_Outline), "Display-Sticker: OUTLINE_ON");
            Assert.IsTrue(stickerText.IsKeywordEnabled(ShaderUtilities.Keyword_Underlay), "Display-Sticker: UNDERLAY_ON");
            StringAssert.StartsWith("TextMeshPro/", stickerText.shader.name);
            Asset<Material>("Assets/Toybox/Resources/Fonts/Display-Plain.mat");
            Asset<Material>("Assets/Toybox/Resources/Fonts/Body-Plain.mat");
            // One more material the UI ships: the opaque frame for a camera picture on the 8-bit fallback.
            Asset<Material>("Assets/Toybox/Resources/UI/Frame.mat");

            // The page shell (10.5 "Loading screen").
            Assert.IsTrue(Directory.Exists(Path.Combine(Application.dataPath, "WebGLTemplates/Toybox")), "Assets/WebGLTemplates/Toybox");
        }
    }
}
