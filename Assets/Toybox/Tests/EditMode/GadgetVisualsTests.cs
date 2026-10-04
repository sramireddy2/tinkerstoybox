using System;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Levels;
using Toybox.Platform;
using Toybox.Render;
using Toybox.Toys;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Toybox.Tests
{
    /// <summary>
    /// The render side of the gadgets: every presenter attaches without Play Mode, draws from its
    /// gadget's public state, follows that state as it changes, and leaves nothing behind when the level
    /// is unloaded. None of them has a collider; none of them is read by the simulation.
    /// </summary>
    public class GadgetVisualsTests
    {
        static readonly Type[] Presenters =
        {
            typeof(LaserVisuals), typeof(WindVisuals), typeof(FitLamps), typeof(WaterVisuals), typeof(PortalView),
            typeof(LaunchCues), typeof(TrainMarks), typeof(BreakDebris), typeof(GadgetCues),
        };

        Game game;
        ScriptedInput input;
        Presentation presentation;

        [TearDown]
        public void Dispose()
        {
            presentation?.Dispose();
            presentation = null;
            game?.Dispose();
            game = null;
            Game.Current?.Dispose();
            Materials.Tier = QualityTier.Medium;
            Materials.Plain = false;
            PortalView.AllowCamera = true;
        }

        // ------------------------------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------------------------------

        Game Build(Action<LevelContext> build)
        {
            input = new ScriptedInput();
            game = Game.Create(new GameOptions { Input = input });
            game.LoadLevel(new AdHocLevel(build));
            return game;
        }

        static List<PresenterRegistry.Entry> Only(params Type[] types)
        {
            var list = new List<PresenterRegistry.Entry>();
            foreach (PresenterRegistry.Entry entry in PresenterRegistry.All)
                if (Array.IndexOf(types, entry.Type) >= 0) list.Add(entry);
            Assert.AreEqual(types.Length, list.Count, "the presenters exist");
            return list;
        }

        T Present<T>(QualityTier tier = QualityTier.Medium) where T : class, IPresenter
        {
            presentation = Presentation.Create(game, new PresentationOptions { Quality = tier, Presenters = Only(typeof(T)) });
            T presenter = presentation.Get<T>();
            Assert.IsNotNull(presenter, typeof(T).Name + " attached");
            return presenter;
        }

        // A tick is a frame, as in the screenshot tool.
        void Frames(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                game.Tick();
                presentation.Frame(Sim.Dt, 1f);
            }
        }

        void Seconds(float seconds) => Frames(TestHelpers.Ticks(seconds));

        bool Until(Func<bool> condition, float seconds)
        {
            for (int i = TestHelpers.Ticks(seconds); i > 0 && !condition(); i--) Frames();
            return condition();
        }

        void Click()
        {
            input.Once.GrabPressed = true;
            Frames();
        }

        void Unload() => game.LoadLevel(new AdHocLevel(ctx =>
        {
            TestHelpers.Floor(ctx, 10f);
            ctx.SetSpawn(Vector3.zero, 0f);
        }));

        Transform RootOf<T>() => presentation.Context.Root.Find(typeof(T).Name);

        static Mesh MeshOf(Renderer renderer) => renderer.GetComponent<MeshFilter>().sharedMesh;

        static Color EmissionOf(Renderer renderer)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Vector4 v = block.GetVector(Materials.EmissionId);
            return new Color(v.x, v.y, v.z, 1f);
        }

        static void AssertColor(Color expected, Color actual, string message)
        {
            Assert.AreEqual(expected.r, actual.r, 1e-3f, message + " (red)");
            Assert.AreEqual(expected.g, actual.g, 1e-3f, message + " (green)");
            Assert.AreEqual(expected.b, actual.b, 1e-3f, message + " (blue)");
        }

        void AssertSignal(Signal signal, Renderer lamp, string message) =>
            AssertColor(signal.EmissionAt(signal.GainAt(presentation.Context.UnscaledTime)), EmissionOf(lamp), message);

        static void AssertEffect(Renderer renderer, string what)
        {
            Assert.IsNotNull(renderer, what);
            Assert.AreEqual("Toybox/Flat", renderer.sharedMaterial.shader.name, what + " is drawn with the Flat shader");
            Assert.AreEqual(ShadowCastingMode.Off, renderer.shadowCastingMode, what + " casts no shadow");
            Assert.IsNull(renderer.GetComponent<Collider>(), what + " has no collider");
        }

        // ------------------------------------------------------------------------------------------
        // Discovery
        // ------------------------------------------------------------------------------------------

        [Test]
        public void EveryGadgetPresenter_IsFound_AsPartOfTheLook()
        {
            List<PresenterRegistry.Entry> plain = PresenterRegistry.Select(PresenterRegistry.All, true);
            List<PresenterRegistry.Entry> look = PresenterRegistry.Select(PresenterRegistry.All, false);
            foreach (Type type in Presenters)
            {
                PresenterRegistry.Entry entry = null;
                foreach (PresenterRegistry.Entry candidate in PresenterRegistry.All)
                    if (candidate.Type == type) entry = candidate;
                Assert.IsNotNull(entry, type.Name + " is discovered");
                Assert.IsTrue(typeof(GadgetVisual).IsAssignableFrom(type), type.Name);
                Assert.That(entry.Order, Is.InRange(230, 249), type.Name + ": after the exit marks, before the held look");
                Assert.IsTrue(entry.ProvidesLook, type.Name + " is drawn with the look's blended materials");
                Assert.IsFalse(entry.Fallback || entry.ProvidesHud, type.Name);
                Assert.IsFalse(plain.Contains(entry), type.Name + " sits the plain look out");
                Assert.IsTrue(look.Contains(entry), type.Name + " runs with the game's look");
            }
        }

        // ------------------------------------------------------------------------------------------
        // Lasers
        // ------------------------------------------------------------------------------------------

        [Test]
        public void Lasers_DotEveryBeam_AndShadeTheGroundUnderARoof()
        {
            LaserRain rain = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 40f);
                // A roof over the left half of the emitter.
                TestHelpers.Box(ctx, new Vector3(-1.5f, 2.5f, 8f), new Vector3(3f, 0.3f, 6f));
                rain = new LaserRain(ctx, new LaserRainOptions { Center = new Vector3(0f, 5f, 8f), Size = new Vector2(5f, 4f), Range = 5f, OnZapped = () => { } });
                ctx.SetSpawn(new Vector3(0f, 0f, -4f), 0f);
            });
            LaserVisuals lasers = Present<LaserVisuals>();
            Frames(8);

            Assert.AreEqual(1, lasers.Count);
            Assert.AreEqual(rain.BeamCount, lasers.DotCount(0), "a dot per beam");
            int Roofed()
            {
                int n = 0;
                for (int i = 0; i < rain.BeamCount; i++)
                    if (rain.BeamLength(i) <= rain.Range - game.Player.Height) n++;
                return n;
            }
            int roofed = Roofed();
            Assert.Greater(roofed, 0);
            Assert.Less(roofed, rain.BeamCount);
            Assert.AreEqual(roofed, lasers.ShadedCount(0), "the ground under every roofed beam is shaded, and no other");
            Assert.AreEqual(rain.BeamCount * 8, lasers.DotMeshOf(0).vertexCount, "a dot and its wash per beam");
            Assert.AreEqual(roofed * 24, lasers.ShadeMeshOf(0).vertexCount, "a hexagon per roofed beam");

            // The beams: two crossed strips each, from the emitter to where the simulation says they end;
            // the gadget's own plain ones are switched off meanwhile.
            Assert.AreEqual(rain.BeamCount * 8, lasers.BeamMeshOf(0).vertexCount);
            AssertEffect(lasers.BeamRendererOf(0), "the beams");
            Assert.IsNotNull(rain.BeamRenderer);
            Assert.IsFalse(rain.BeamRenderer.enabled, "one set of beams, not two");
            Bounds beams = lasers.BeamMeshOf(0).bounds;
            Assert.AreEqual(5f, beams.max.y, 1e-3f, "from the emitter");
            Assert.AreEqual(0f, beams.min.y, 0.01f, "to the floor");
            Assert.Less(lasers.ShadeRendererOf(0).sharedMaterial.renderQueue, lasers.BeamRendererOf(0).sharedMaterial.renderQueue, "the shade is laid down before the beams, so it never darkens one");

            // The dots lie where the beams end: on the floor and on the roof. The shade lies on the floor, under the roof.
            Bounds dots = lasers.DotMeshOf(0).bounds;
            Assert.AreEqual(0.02f, dots.min.y, 0.03f, "dots on the floor");
            Assert.AreEqual(2.67f, dots.max.y, 0.03f, "dots on the roof");
            Bounds shade = lasers.ShadeMeshOf(0).bounds;
            Assert.AreEqual(0.03f, shade.center.y, 0.03f, "the shade lies on the ground the beams would have reached");
            Assert.Less(shade.max.x, 0.4f, "only under the roof");
            Assert.IsTrue(lasers.ShadeRendererOf(0).enabled);
            AssertEffect(lasers.DotRendererOf(0), "the dots");
            AssertEffect(lasers.ShadeRendererOf(0), "the shade");

            // A second roof over the rest: every beam is roofed, and the picture follows within a refresh of the lattice.
            Prop lid = game.Context.AddProp(BasicToys.Block(new Vector3(3f, 0.3f, 6f)), new Vector3(1.5f, 2.5f, 8f), new PropOptions { Name = "Lid", Body = PropBody.Fixed });
            Frames(8);
            Assert.AreEqual(rain.BeamCount, Roofed());
            Assert.AreEqual(rain.BeamCount, lasers.ShadedCount(0));
            Assert.Greater(lasers.ShadeMeshOf(0).bounds.max.x, 2f);
            game.Context.RemoveProp(lid);
            Frames(8);
            Assert.AreEqual(roofed, lasers.ShadedCount(0), "and back");

            // Low: no wash, half the quads.
            presentation.Context.Quality = QualityTier.Low;
            Frames(2);
            Assert.AreEqual(1, lasers.Count, "rebuilt for the tier");
            Assert.AreEqual(rain.BeamCount * 4, lasers.DotMeshOf(0).vertexCount);
            Assert.IsFalse(rain.BeamRenderer.enabled);

            Unload();
            Assert.AreEqual(0, lasers.Count);
            Assert.AreEqual(0, lasers.KeptCount);
            Assert.AreEqual(0, RootOf<LaserVisuals>().childCount, "nothing of the level is left");
        }

        [Test]
        public void WithoutThePresenter_TheGadgetsOwnBeamsAreBack()
        {
            LaserRain rain = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 40f);
                rain = new LaserRain(ctx, new LaserRainOptions { Center = new Vector3(0f, 4f, 8f), Size = new Vector2(1f, 1f), Range = 4f, OnZapped = () => { } });
                ctx.SetSpawn(new Vector3(0f, 0f, -4f), 0f);
            });
            Assert.IsTrue(rain.BeamRenderer.enabled);
            Present<LaserVisuals>();
            Frames(2);
            Assert.IsFalse(rain.BeamRenderer.enabled);
            presentation.Dispose();
            presentation = null;
            Assert.IsTrue(rain.BeamRenderer.enabled, "the plain beams stand in again");
        }

        // ------------------------------------------------------------------------------------------
        // Wind
        // ------------------------------------------------------------------------------------------

        [Test]
        public void Wind_ShowsItsStream_AndTurnsItsFan_AsHardAsItBlows()
        {
            WindStream wind = null;
            GameObject fan = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 60f);
                fan = ctx.AddStatic(ToyFactory.DeskFan(null, false), new Vector3(0f, 0f, 4f));
                fan.transform.localScale = Vector3.one * 2f;
                wind = new WindStream(ctx, new WindStreamOptions { Center = new Vector3(0f, 1.4f, 9.5f), Size = new Vector3(2f, 2f, 10f), Direction = Vector3.forward });
                ctx.SetSpawn(new Vector3(8f, 0f, 0f), 0f);
            });
            WindVisuals visuals = Present<WindVisuals>();
            Frames(30);

            Assert.AreEqual(1, visuals.Count);
            Assert.AreEqual(1, visuals.FanCount, "the fan's blades were found");
            Assert.AreEqual(1f, visuals.ShownOf(0), 1e-3f);
            AssertEffect(visuals.RibbonRendererOf(0), "the streamers");
            AssertEffect(visuals.VolumeRendererOf(0), "the stream's volume");
            Assert.IsTrue(visuals.RibbonRendererOf(0).enabled && visuals.VolumeRendererOf(0).enabled);
            Mesh ribbons = MeshOf(visuals.RibbonRendererOf(0));
            // Two passes (edge and core) of five quads per streamer.
            Assert.AreEqual(visuals.StreamerCount(0) * 2 * 5 * 4, ribbons.vertexCount);
            Bounds bounds = ribbons.bounds;
            Assert.GreaterOrEqual(bounds.min.z, 4.5f - 0.01f, "streamers stay inside the stream");
            Assert.LessOrEqual(bounds.max.z, 14.5f + 0.01f);
            Assert.LessOrEqual(Mathf.Max(Mathf.Abs(bounds.min.x), Mathf.Abs(bounds.max.x)), 1.3f);
            Bounds volume = MeshOf(visuals.VolumeRendererOf(0)).bounds;
            Assert.That(Vector3.Distance(volume.center, new Vector3(0f, 1.4f, 9.5f)), Is.LessThan(1e-3f));
            Assert.That(Vector3.Distance(volume.size, new Vector3(2f, 2f, 10f)), Is.LessThan(1e-3f), "the volume is the stream's own box");

            // The streamers move.
            Vector3 before = ribbons.vertices[ribbons.vertexCount - 1];
            Frames(1);
            Assert.AreNotEqual(before, ribbons.vertices[ribbons.vertexCount - 1]);

            // The fan turns at the stream's strength.
            Assert.AreEqual(WindVisuals.FanSpeed, visuals.FanSpeedOf(0), 1f);
            Quaternion blades = visuals.FanBladesOf(0).localRotation;
            Frames(1);
            Assert.AreNotEqual(blades, visuals.FanBladesOf(0).localRotation);

            // Calm: the stream fades out and the fan coasts to a stop.
            wind.Strength = 0f;
            Seconds(3f);
            Assert.AreEqual(0f, visuals.ShownOf(0));
            Assert.IsFalse(visuals.RibbonRendererOf(0).enabled || visuals.VolumeRendererOf(0).enabled);
            Assert.AreEqual(0f, visuals.FanSpeedOf(0));
            blades = visuals.FanBladesOf(0).localRotation;
            Frames(2);
            Assert.AreEqual(blades, visuals.FanBladesOf(0).localRotation, "a fan that stands still is left alone");

            // A gale: faster.
            wind.Strength = 2f;
            Seconds(2f);
            Assert.AreEqual(1f, visuals.ShownOf(0), 1e-3f);
            Assert.AreEqual(WindVisuals.FanSpeed * Mathf.Sqrt(2f), visuals.FanSpeedOf(0), 1f);

            // More streamers on a better tier.
            int medium = visuals.StreamerCount(0);
            presentation.Context.Quality = QualityTier.High;
            Frames(1);
            Assert.Greater(visuals.StreamerCount(0), medium);
            presentation.Context.Quality = QualityTier.Low;
            Frames(1);
            Assert.Less(visuals.StreamerCount(0), medium);

            Unload();
            Assert.AreEqual(0, visuals.Count);
            Assert.AreEqual(0, visuals.FanCount);
            Assert.AreEqual(0, visuals.KeptCount);
            Assert.AreEqual(0, RootOf<WindVisuals>().childCount);
        }

        // ------------------------------------------------------------------------------------------
        // Fit lamps
        // ------------------------------------------------------------------------------------------

        [Test]
        public void AGaugesLamp_SaysWaitingFitsOrWrong_AndItsReadoutFollowsTheHeldToy()
        {
            FitGauge gauge = null;
            Prop block = null;
            Build(ctx =>
            {
                TestHelpers.Room(ctx, 20f, 12f);
                gauge = new FitGauge(ctx, new FitGaugeOptions
                {
                    MinScale = 1f, MaxScale = 2f, Tag = "plug", Near = Zone.MinMax(new Vector3(-20f, 0f, 2f), new Vector3(20f, 12f, 20f)), Lamp = new Vector3(0f, 5f, 19f),
                });
                block = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 1.5f, 2f), new PropOptions { Name = "Block", Scale = 0.5f, Tags = new[] { "plug" }, MaxScale = 30f });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            FitLamps lamps = Present<FitLamps>();
            Frames(12);

            FitLamps.Window(gauge, out float min, out float max);
            Assert.AreEqual(1f, min, 1e-3f, "the window is read off the gauge itself");
            Assert.AreEqual(2f, max, 1e-3f);
            Vector2 band = lamps.BandOf(gauge);
            Assert.AreEqual(Mathf.Pow(2f, -0.5f * FitLamps.Spread), band.x, 1e-3f);
            Assert.AreEqual(Mathf.Pow(2f, 0.5f * FitLamps.Spread), band.y, 1e-3f);

            Assert.AreEqual(1, lamps.GaugeCount);
            Assert.AreEqual(FitTint.Waiting, lamps.TintOf(gauge));
            Assert.AreEqual(0f, lamps.ReadoutOf(gauge), "nothing is judged: no read-out");
            AssertSignal(Palette.Amber, gauge.Lamp.Renderer, "waiting: amber, pulsing");
            Assert.IsFalse(lamps.RingRendererOf(gauge).gameObject.activeInHierarchy);

            TestHelpers.LookAt(game.Player, block.Center);
            Click();
            Assert.AreSame(block, game.Grabber.Held);

            // On the floor 6 ahead it is about 1.2: it fits.
            TestHelpers.LookAt(game.Player, new Vector3(0f, 0f, 6f));
            Seconds(0.4f);
            Assert.AreEqual(FitState.Good, gauge.State, "scale " + block.Scale);
            Assert.AreEqual(FitTint.Fits, lamps.TintOf(gauge));
            AssertSignal(Palette.Go, gauge.Lamp.Renderer, "fits: green, steady");
            Assert.AreEqual(1f, lamps.ReadoutOf(gauge), 1e-3f);
            Assert.That(lamps.RingOf(gauge), Is.InRange(band.x, band.y), "the ring sits on the band");
            Assert.IsTrue(lamps.RingRendererOf(gauge).gameObject.activeInHierarchy);
            AssertEffect(lamps.RingRendererOf(gauge), "the ring");

            // Closer: too small. The ring is inside the band's hole, the lamp red and blinking.
            TestHelpers.LookAt(game.Player, new Vector3(0f, 0f, 3.6f));
            Seconds(0.2f);
            Assert.AreEqual(FitState.TooSmall, gauge.State);
            Assert.AreEqual(FitTint.Wrong, lamps.TintOf(gauge));
            AssertSignal(GadgetFx.Wrong, gauge.Lamp.Renderer, "wrong size: red, blinking");
            Assert.Less(lamps.RingOf(gauge), band.x);

            // Against the far wall: too big. The ring is outside the band.
            TestHelpers.LookAt(game.Player, new Vector3(0f, 3f, 20f));
            Seconds(0.2f);
            Assert.AreEqual(FitState.TooBig, gauge.State);
            Assert.AreEqual(FitTint.Wrong, lamps.TintOf(gauge));
            Assert.Greater(lamps.RingOf(gauge), band.y);

            // Let go: waiting again, the read-out folds away.
            Click();
            Seconds(0.4f);
            Assert.AreEqual(FitTint.Waiting, lamps.TintOf(gauge));
            Assert.AreEqual(0f, lamps.ReadoutOf(gauge));
            AssertSignal(Palette.Amber, gauge.Lamp.Renderer, "waiting again");

            Unload();
            Assert.AreEqual(0, lamps.GaugeCount);
            Assert.AreEqual(0, lamps.KeptCount);
            Assert.AreEqual(0, RootOf<FitLamps>().childCount);
        }

        [Test]
        public void ASocketsLamp_IsRed_WhileAToyItRefusedLiesInIt()
        {
            Socket waiting = null, refusing = null, seated = null;
            Prop small = null;
            Socket Make(LevelContext ctx, float x) => new Socket(ctx, new SocketOptions
            {
                AcceptTag = "peg", Capture = Zone.Box(new Vector3(x, 0.7f, 5f), new Vector3(1.4f, 1.4f, 1.4f)), MinScale = 0.8f, MaxScale = 1.2f,
                SeatPose = s => new Pose(new Vector3(x, 0.5f * s, 5f), Quaternion.identity), Lamp = new Vector3(x, 2f, 6f),
            });
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 40f);
                waiting = Make(ctx, -4f);
                refusing = Make(ctx, 0f);
                seated = Make(ctx, 4f);
                small = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.26f, 5f), new PropOptions { Name = "Small", Scale = 0.5f, Tags = new[] { "peg" } });
                ctx.AddProp(BasicToys.Block(1f), new Vector3(4f, 0.51f, 5f), new PropOptions { Name = "Right", Tags = new[] { "peg" } });
                ctx.SetSpawn(new Vector3(0f, 0f, -3f), 0f);
            });
            FitLamps lamps = Present<FitLamps>();
            Seconds(0.6f);

            Assert.AreEqual(3, lamps.SocketCount);
            Assert.IsTrue(seated.Seated);
            Assert.AreEqual(FitTint.Waiting, lamps.TintOf(waiting));
            Assert.AreEqual(FitTint.Wrong, lamps.TintOf(refusing));
            Assert.AreEqual(FitTint.Fits, lamps.TintOf(seated));
            AssertSignal(Palette.Amber, waiting.Lamp.Renderer, "an empty socket");
            AssertSignal(GadgetFx.Wrong, refusing.Lamp.Renderer, "a socket with the wrong size in it");
            AssertSignal(Palette.Go, seated.Lamp.Renderer, "a seated socket");

            // It stays red for as long as the toy lies there, and goes back to waiting once it is gone.
            Seconds(2f);
            Assert.AreEqual(FitTint.Wrong, lamps.TintOf(refusing));
            game.Context.RemoveProp(small);
            Seconds(FitLamps.RejectSeconds + 0.1f);
            Assert.AreEqual(FitTint.Waiting, lamps.TintOf(refusing));
            AssertSignal(Palette.Amber, refusing.Lamp.Renderer, "empty again");

            Unload();
            Assert.AreEqual(0, lamps.SocketCount);
        }

        // ------------------------------------------------------------------------------------------
        // Water
        // ------------------------------------------------------------------------------------------

        [Test]
        public void Water_HasABodyAndAWaterline_ThatFollowItsLevel()
        {
            WaterVolume basin = null, tower = null, hidden = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 60f);
                basin = new WaterVolume(ctx, new WaterVolumeOptions { Name = "Basin", Footprint = Zone.Box(new Vector3(0f, 1f, 8f), new Vector3(4f, 2f, 4f)), Area = 16f, Volume = 16f });
                tower = new WaterVolume(ctx, new WaterVolumeOptions { Name = "Tower", Footprint = Zone.Cylinder(8f, 8f, 1.5f, 0f, 6f), Area = 7f, Volume = 7f });
                hidden = new WaterVolume(ctx, new WaterVolumeOptions { Name = "Drain", Footprint = Zone.Cylinder(-8f, 8f, 1.5f, 0f, 6f), Area = 7f, Volume = 7f, Visual = false });
                ctx.SetSpawn(new Vector3(0f, 0f, -3f), 0f);
            });
            WaterVisuals water = Present<WaterVisuals>();
            Frames(3);

            Assert.AreEqual(2, water.Count, "the two volumes that draw a surface");
            Assert.IsNull(water.BodyOf(hidden), "plumbing is not drawn");
            Renderer body = water.BodyOf(basin), line = water.LineOf(basin);
            AssertEffect(body, "the water's body");
            AssertEffect(line, "the waterline");
            Assert.IsTrue(body.enabled && line.enabled);
            Assert.AreEqual(1f, body.transform.localScale.y, 1e-4f, "the body is as tall as the water is deep");
            Assert.AreEqual(0f, body.transform.position.y, 1e-4f, "and stands on the bottom");
            Assert.AreEqual(basin.SurfaceY, line.transform.position.y, 1e-4f, "the line is at the surface");
            Bounds bounds = body.bounds;
            Assert.AreEqual(1f, bounds.max.y, 1e-3f);
            Assert.LessOrEqual(bounds.size.x, 4f, "inside the footprint");
            Assert.Greater(bounds.size.x, 3.9f);
            Assert.AreEqual(0f, water.MovingOf(basin));
            Assert.AreEqual(1f, water.BodyOf(tower).transform.localScale.y, 1e-4f);
            Assert.AreEqual(8f, water.BodyOf(tower).bounds.center.x, 1e-3f);

            // Half of it runs into the tower: both follow, and the lines light up while they move.
            for (int i = 0; i < 30; i++)
            {
                tower.Add(basin.Take(8f / 30f));
                Frames();
            }
            Assert.AreEqual(0.5f, basin.Depth, 1e-3f);
            Assert.AreEqual(0.5f, body.transform.localScale.y, 1e-3f);
            Assert.AreEqual(0.5f, line.transform.position.y, 1e-3f);
            Assert.AreEqual(tower.Depth, water.BodyOf(tower).transform.localScale.y, 1e-3f);
            Assert.Greater(tower.Depth, 2f);
            Assert.AreEqual(1f, water.MovingOf(basin), 1e-3f, "the level is seen to move");
            Seconds(1f);
            Assert.AreEqual(0f, water.MovingOf(basin), "and to stand still again");

            // Dry: nothing is drawn.
            basin.Take(100f);
            Frames(2);
            Assert.IsFalse(body.enabled || line.enabled);
            basin.Add(4f);
            Frames(2);
            Assert.IsTrue(body.enabled && line.enabled);

            Unload();
            Assert.AreEqual(0, water.Count);
            Assert.AreEqual(0, water.KeptCount);
            Assert.AreEqual(2, RootOf<WaterVisuals>().childCount, "only the (empty) ripple pool of the new level is left");
            Assert.AreEqual(0, water.Ripples);
        }

        [Test]
        public void ASponge_LooksAsSoakedAsItIs()
        {
            WaterVolume pond = null;
            Sponge wet = null, dry = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 60f);
                pond = new WaterVolume(ctx, new WaterVolumeOptions { Footprint = Zone.Box(new Vector3(0f, 0.5f, 6f), new Vector3(6f, 1f, 6f)), Area = 36f, Volume = 36f });
                float rest = ToyCatalog.Get(ToyId.Sponge).RestHeight;
                Prop a = ToyCatalog.Add(ctx, ToyId.Sponge, new Vector3(0f, rest * 2f + 0.01f, 6f), 2f, Palette.Lemon);
                Prop b = ToyCatalog.Add(ctx, ToyId.Sponge, new Vector3(9f, rest * 2f + 0.01f, 6f), 2f, Palette.Lemon);
                wet = new Sponge(ctx, new SpongeOptions { Name = "Wet", Prop = a, Volumes = new[] { pond } });
                dry = new Sponge(ctx, new SpongeOptions { Name = "Dry", Prop = b, Volumes = new[] { pond } });
                ctx.SetSpawn(new Vector3(0f, 0f, -3f), 0f);
            });
            WaterVisuals water = Present<WaterVisuals>();
            Seconds(0.5f);

            Assert.AreEqual(2, water.SpongeCount);
            Assert.AreEqual(1f, wet.Saturation01, 1e-3f, "it lies in the pond and has drunk its fill");
            Assert.AreEqual(1f, water.WetnessOf(wet), 1e-3f);
            Assert.AreEqual(0f, water.WetnessOf(dry), 1e-3f);
            Assert.Greater(water.RipplesStarted, 0, "ripples while it drank");

            int baseColor = Shader.PropertyToID("_BaseColor"), smoothness = Shader.PropertyToID("_Smoothness");
            var block = new MaterialPropertyBlock();
            Renderer soaked = wet.Prop.GameObject.GetComponentInChildren<Renderer>();
            Vector4 material = soaked.sharedMaterial.GetVector(baseColor);
            soaked.GetPropertyBlock(block);
            Vector4 shown = block.GetVector(baseColor);
            Vector4 expected = WaterVisuals.Wet(material);
            Assert.That(Vector4.Distance(expected, shown), Is.LessThan(1e-4f), "the soaked colour is on the renderer, not on the shared material");
            Assert.Less(shown.x + shown.y + shown.z, material.x + material.y + material.z, "soaked is darker");
            Assert.AreEqual(WaterVisuals.WetSmoothness, block.GetFloat(smoothness), 1e-4f, "and glossy");

            Renderer other = dry.Prop.GameObject.GetComponentInChildren<Renderer>();
            other.GetPropertyBlock(block);
            Assert.That(Vector4.Distance(other.sharedMaterial.GetVector(baseColor), block.GetVector(baseColor)), Is.LessThan(1e-4f), "a dry sponge keeps its colour");

            // Wrung out (emptied by the level): dry again.
            wet.EmptyInto(null);
            wet.Enabled = false;
            Frames(2);
            Assert.AreEqual(0f, water.WetnessOf(wet), 1e-3f);
            soaked.GetPropertyBlock(block);
            Assert.That(Vector4.Distance(material, block.GetVector(baseColor)), Is.LessThan(1e-4f));

            Unload();
            Assert.AreEqual(0, water.SpongeCount);
            Assert.AreEqual(0, water.RipplesStarted);
        }

        // ------------------------------------------------------------------------------------------
        // Portal
        // ------------------------------------------------------------------------------------------

        PortalDoorway Hall(float doorScale, out Prop door)
        {
            PortalDoorway portal = null;
            Prop prop = null;
            Build(ctx =>
            {
                TestHelpers.Room(ctx, 20f, 12f);
                ctx.AddProp(BasicToys.Block(2f), new Vector3(3f, 1f, 14f));
                prop = ToyCatalog.Add(ctx, ToyId.Doorway, new Vector3(0f, 0f, 6f), doorScale);
                portal = new PortalDoorway(ctx, new PortalDoorwayOptions { Prop = prop });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            door = prop;
            return portal;
        }

        [Test]
        public void APortal_ShowsACard_WhereThereIsNoSecondCamera()
        {
            PortalDoorway portal = Hall(0.5f, out Prop door);
            PortalView view = Present<PortalView>(QualityTier.Low);
            Seconds(0.5f);

            Assert.AreEqual(1, view.Count);
            Assert.IsTrue(portal.Active, "the door has settled");
            Assert.IsTrue(view.IsOpen(portal));
            Assert.IsFalse(view.CameraAvailable, "Low has no second camera");
            Assert.IsFalse(view.IsLive(portal));
            Assert.IsNull(view.Camera, "and never makes one");
            Assert.IsNull(view.Texture);
            Assert.AreEqual(1f, view.CardOf(portal));
            Renderer card = view.CardRendererOf(portal), figure = view.FigureRendererOf(portal);
            AssertEffect(card, "the card");
            AssertEffect(figure, "the figure");
            Assert.IsTrue(card.gameObject.activeInHierarchy && card.enabled && figure.enabled);
            Assert.IsFalse(view.RimRendererOf(portal).enabled, "the rim belongs to the live view");

            // The card fills the opening; the figure on it is as tall as the player will be.
            Vector2 opening = portal.OpeningSize;
            Assert.That(Vector3.Distance(portal.OpeningCenter, card.transform.position), Is.LessThan(1e-3f));
            Assert.AreEqual(opening.x, card.bounds.size.x, 0.01f);
            Assert.AreEqual(opening.y, card.bounds.size.y, 0.01f);
            Assert.AreEqual(Player.BaseHeight * portal.TargetScale, figure.bounds.size.y, 0.01f);
            Assert.AreEqual(0f, figure.bounds.min.y, 0.01f, "standing on the threshold");

            // In the hand a door is no portal: the figure goes, and the opening is closed with an opaque card that
            // is part of the sticker (the Held layer) - nothing of the room shows through a frame that is carried.
            Renderer inHand = view.HeldCardRendererOf(portal);
            Assert.IsNotNull(inHand);
            Assert.IsFalse(inHand.enabled, "a door that stands is never closed");
            Assert.IsFalse(view.IsInHand(portal));
            TestHelpers.LookAt(game.Player, door.Position + new Vector3(0.35f, 0.5f, 0f));
            Click();
            Assert.AreSame(door, game.Grabber.Held);
            Frames(2);
            Assert.IsFalse(view.IsOpen(portal));
            Assert.IsTrue(view.IsInHand(portal));
            Assert.AreEqual(0f, view.CardOf(portal));
            Assert.IsFalse(card.enabled || figure.enabled || view.RimRendererOf(portal).enabled, "no figure, no rim: it leads nowhere yet");
            Assert.IsTrue(inHand.enabled && inHand.gameObject.activeInHierarchy);
            Assert.AreEqual(Layers.Held, inHand.gameObject.layer, "drawn by the sticker pass, with the door");
            Assert.AreEqual("Toybox/Flat", inHand.sharedMaterial.shader.name);
            Assert.Less(inHand.sharedMaterial.renderQueue, 2500, "opaque: nothing behind it shows");
            Assert.AreEqual(ShadowCastingMode.Off, inHand.shadowCastingMode);
            Assert.IsNull(inHand.GetComponent<Collider>());
            Vector2 carried = portal.OpeningSize;
            Assert.That(Vector3.Distance(portal.OpeningCenter, inHand.transform.position), Is.LessThan(1e-3f), "it rides in the frame, wherever the hand holds it");
            Assert.AreEqual(carried.x, inHand.transform.lossyScale.x, carried.x * 0.01f, "at the size the door has in the hand");
            Assert.AreEqual(carried.y, inHand.transform.lossyScale.y, carried.y * 0.01f);

            // Put down again: the card of the hand is gone at once.
            Click();
            Assert.IsNull(game.Grabber.Held);
            Frames(2);
            Assert.IsFalse(view.IsInHand(portal));
            Assert.IsFalse(inHand.enabled);

            Unload();
            Assert.AreEqual(0, view.Count);
            Assert.AreEqual(0, view.KeptCount);
            Assert.AreEqual(0, RootOf<PortalView>().childCount);
        }

        [Test]
        public void ADoorOfThePlayersOwnSize_IsJustADoor()
        {
            PortalDoorway portal = Hall(1f, out _);
            PortalView view = Present<PortalView>(QualityTier.Low);
            Seconds(0.5f);
            Assert.IsTrue(portal.Active);
            Assert.IsFalse(view.IsOpen(portal));
            Assert.AreEqual(0f, view.CardOf(portal));
            Assert.IsFalse(view.CardRendererOf(portal).gameObject.activeInHierarchy);
        }

        [Test]
        public void APortal_ShowsTheRoomFromTheOtherSidesEye_OnMedium()
        {
            Assume.That(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "rendering needs a graphics device (-nographics run)");
            Assume.That(Quality.HdrRenderable, "the picture is an HDR texture");
            PortalDoorway portal = Hall(0.5f, out _);
            PortalView view = Present<PortalView>(QualityTier.Medium);
            Seconds(0.5f);

            Assert.IsTrue(view.CameraAvailable);
            Assert.IsTrue(view.IsLive(portal), "the nearest open door gets the camera");
            Assert.AreEqual(0f, view.CardOf(portal), "and no card");
            Assert.IsFalse(view.CardRendererOf(portal).enabled || view.FigureRendererOf(portal).enabled);
            AssertEffect(view.RimRendererOf(portal), "the rim");
            Assert.IsTrue(view.RimRendererOf(portal).enabled && view.RimRendererOf(portal).gameObject.activeInHierarchy, "a rim of light marks the open portal");
            Assert.IsFalse(Materials.Dip.Night);
            Assert.AreEqual(8 * 4, MeshOf(view.RimRendererOf(portal)).vertexCount, "in a day room the light ends in a hairline of the room's deep tone");
            Assert.Greater(view.Renders, 0);
            Camera second = view.Camera;
            Assert.IsNotNull(second);
            Assert.IsFalse(second.enabled, "rendered by hand, never by the pipeline's loop");
            Assert.AreEqual(CameraType.Reflection, second.cameraType);
            Assert.AreSame(view.Texture, second.targetTexture);
            Assert.AreEqual(PortalView.TextureSize(QualityTier.Medium), new Vector2Int(view.Texture.width, view.Texture.height));

            // It stands at the eye, scaled about the threshold, and its near plane is the door's plane.
            Vector3 eye = presentation.Camera.transform.position;
            Assert.That(Vector3.Distance(portal.ViewEye(eye), second.transform.position), Is.LessThan(1e-3f));
            Assert.AreEqual(Mathf.Abs(Vector3.Dot(eye - portal.Threshold, portal.Normal)) * portal.ViewRatio, second.nearClipPlane, 1e-3f);
            Assert.That(Vector3.Angle(second.transform.forward, portal.Normal), Is.LessThan(0.01f), "squarely through the door, away from the eye");

            // The picture lies in the opening and is cut to what is on screen.
            Renderer picture = view.ViewRenderer;
            Assert.IsTrue(picture.enabled);
            Assert.AreEqual(PortalView.ViewShader, picture.sharedMaterial.shader.name);
            Assert.AreSame(view.Texture, picture.sharedMaterial.mainTexture);
            Vector2 opening = portal.OpeningSize;
            Bounds bounds = picture.bounds;
            Assert.AreEqual(opening.x, bounds.size.x, 0.01f, "the whole opening is in view from here");
            Assert.AreEqual(opening.y, bounds.size.y, 0.01f);
            Assert.AreEqual(6f, bounds.center.z, 1e-3f);
            Assert.That(view.ViewPixels.x, Is.InRange(32, view.Texture.width));
            Assert.That(view.ViewPixels.y, Is.InRange(32, view.Texture.height));
            Assert.Greater(view.ViewPixels.y, view.ViewPixels.x, "an upright door gets an upright picture");

            // Something was rendered into it.
            Assert.Greater(Brightness(view.Texture, view.ViewPixels), 0.01f, "the picture is not black");

            // Close up the picture has more pixels; looking away renders nothing.
            int far = view.ViewPixels.y;
            game.Player.Teleport(new Vector3(0f, 0f, 4.5f), 0f, -20f);
            Frames(2);
            Assert.IsTrue(view.IsLive(portal));
            Assert.Greater(view.ViewPixels.y, far);
            int rendered = view.Renders;
            game.Player.Teleport(new Vector3(0f, 0f, 4.5f), 180f);
            Frames(2);
            Assert.IsFalse(view.IsLive(portal), "the door is behind the player");
            Assert.AreEqual(rendered, view.Renders);
            Assert.IsFalse(picture.enabled);

            // Far away the card takes over.
            game.Player.Teleport(new Vector3(0f, 0f, 6f - PortalView.RangeInDoorScales * 0.5f - 1f), 0f);
            Frames(2);
            Assert.IsFalse(view.IsLive(portal));
            Assert.AreEqual(1f, view.CardOf(portal));
            Assert.IsTrue(view.CardRendererOf(portal).enabled);
            Assert.IsFalse(view.RimRendererOf(portal).enabled);

            // The switch that keeps every door on its card.
            game.Player.Teleport(Vector3.zero, 0f);
            PortalView.AllowCamera = false;
            Frames(2);
            Assert.IsFalse(view.IsLive(portal));
            Assert.AreEqual(1f, view.CardOf(portal));
            PortalView.AllowCamera = true;
            Frames(2);
            Assert.IsTrue(view.IsLive(portal));

            // The camera and its texture outlive levels and go with the presenter.
            Unload();
            Assert.AreEqual(0, view.Count);
            Assert.IsFalse(picture.enabled);
            RenderTexture texture = view.Texture;
            presentation.Dispose();
            presentation = null;
            Assert.IsTrue(second == null, "the camera is destroyed");
            Assert.IsTrue(texture == null, "the texture is destroyed");
        }

        // Mean brightness of the used part of the portal's texture.
        static float Brightness(RenderTexture source, Vector2Int used)
        {
            var small = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear) { hideFlags = HideFlags.HideAndDontSave };
            var image = new Texture2D(16, 16, TextureFormat.RGBAFloat, false, true);
            RenderTexture active = RenderTexture.active;
            try
            {
                Graphics.Blit(source, small, new Vector2((float)used.x / source.width, (float)used.y / source.height), Vector2.zero);
                RenderTexture.active = small;
                image.ReadPixels(new Rect(0f, 0f, 16f, 16f), 0, 0);
                image.Apply();
                float sum = 0f;
                foreach (Color pixel in image.GetPixels()) sum += pixel.r + pixel.g + pixel.b;
                return sum / (16f * 16f * 3f);
            }
            finally
            {
                RenderTexture.active = active;
                small.Release();
                Object.DestroyImmediate(small);
                Object.DestroyImmediate(image);
            }
        }

        // ------------------------------------------------------------------------------------------
        // Launch cues
        // ------------------------------------------------------------------------------------------

        [Test]
        public void ABounce_LeavesARingOnThePad_AndHoopsUpToItsApex()
        {
            BouncePad pad = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 40f);
                GameObject slab = TestHelpers.Box(ctx, new Vector3(0f, 0.2f, 5f), new Vector3(4f, 0.4f, 4f));
                pad = new BouncePad(ctx, new BouncePadOptions { Surface = slab.GetComponent<Collider>(), Scale = 2f });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            LaunchCues cues = Present<LaunchCues>();
            Frames(2);
            Assert.AreEqual(0, cues.Fired);
            Assert.AreEqual(0, cues.Alive);
            Assert.IsFalse(cues.RingRenderer.enabled || cues.StreakRenderer.enabled, "nothing is drawn while nothing happens");

            game.Player.Teleport(new Vector3(0f, 3f, 5f), 0f);
            Assert.IsTrue(Until(() => pad.BounceCount > 0, 2f), "the player lands on the pad and is thrown");
            Frames(1);
            Assert.AreEqual(1, cues.Fired);
            Assert.AreEqual(pad.Apex(2f), cues.LastApex, 0.01f, "the hoops go as high as the bounce carries");
            Assert.AreEqual(LaunchCues.Apex(pad.LastLaunchSpeed), cues.LastApex, 1e-3f);
            Assert.AreEqual(2, LaunchCues.Hoops(pad.Apex(2f), 1f));
            Assert.AreEqual(2, cues.LastHoops);
            Assert.AreEqual(3, cues.Alive, "a burst on the pad and two hoops above it");
            Assert.IsNull(cues.SquashedPad, "a slab of the room knows no squash: it is left alone");
            Assert.AreEqual(0f, cues.PadSquash);
            AssertEffect(cues.RingRenderer, "the burst");
            AssertEffect(cues.StreakRenderer, "the hoops");
            Assert.IsTrue(cues.RingRenderer.enabled && cues.StreakRenderer.enabled);
            Bounds burst = MeshOf(cues.RingRenderer).bounds, hoops = MeshOf(cues.StreakRenderer).bounds;
            // (A hard landing dips the feet a little into the pad for a tick: the marks start where the feet were.)
            Assert.AreEqual(0.44f, burst.center.y, 0.13f, "the burst lies on the pad");
            Assert.AreEqual(burst.center.y - 0.04f + pad.Apex(2f) + LaunchCues.ApexLift * Player.BaseEyeHeight, hoops.max.y, 0.01f, "the top hoop hangs just under where the eye will be at the apex");
            Assert.AreEqual(0f, hoops.center.x, 0.01f);
            Assert.AreEqual(5f, hoops.center.z, 0.05f, "over the spot it happened");
            Assert.Less(hoops.size.x, 2f * LaunchCues.HoopRadius + 0.01f);
            float top = hoops.max.y;

            // Rising through them: the eye tops out just above the upper hoop.
            float highest = 0f;
            for (int i = 0; i < 40; i++)
            {
                Frames();
                highest = Mathf.Max(highest, game.Player.Eye.y);
            }
            Assert.That(highest, Is.InRange(top, top + 1.2f), "the top hoop hangs just under the eye at the apex");

            // Away from the pad, the marks fade and are gone.
            game.Player.Teleport(new Vector3(12f, 0f, 0f), 0f);
            Seconds(LaunchCues.HoopSeconds + 0.1f);
            Assert.AreEqual(0, cues.Alive);
            Assert.IsFalse(cues.RingRenderer.enabled || cues.StreakRenderer.enabled);
            Assert.AreEqual(1, cues.Fired);

            Unload();
            Assert.AreEqual(0, RootOf<LaunchCues>().childCount - 2, "only the (empty) pool of the new level is left");
            Assert.AreEqual(0, cues.Fired);
        }

        [Test]
        public void ABouncePad_GivesUnderTheLanding_AndSpringsBack()
        {
            BouncePad pad = null;
            Prop eraser = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 40f);
                eraser = ToyCatalog.Add(ctx, ToyId.Eraser, new Vector3(0f, ToyCatalog.Get(ToyId.Eraser).RestHeight * 4f + 0.01f, 5f), 4f);
                pad = new BouncePad(ctx, new BouncePadOptions { Prop = eraser });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            LaunchCues cues = Present<LaunchCues>();
            Seconds(0.5f);
            Assert.AreEqual(0f, cues.PadSquash);
            Assert.IsNull(cues.SquashedPad);

            int squash = Shader.PropertyToID("_SquashA");
            var block = new MaterialPropertyBlock();
            Renderer rubber = eraser.GameObject.GetComponentInChildren<Renderer>();
            Bounds collider = eraser.GameObject.GetComponentInChildren<Collider>().bounds;
            Vector4 Written()
            {
                rubber.GetPropertyBlock(block);
                return block.GetVector(squash);
            }

            game.Player.Teleport(new Vector3(0f, 4f, 5f), 0f);
            Assert.IsTrue(Until(() => pad.BounceCount > 0, 2f), "the player lands on the eraser and is thrown");
            Assert.AreSame(pad, cues.SquashedPad);
            Assert.That(cues.PadSquash, Is.GreaterThan(0.1f).And.LessThanOrEqualTo(LaunchCues.SquashMax), "the pad flattens by a thirtieth of the landing speed");
            Vector4 written = Written();
            Assert.AreEqual(cues.PadSquash, written.w, 1e-5f, "through the toy shader's squash, on the pad's own renderers");
            Assert.AreEqual(0f, written.y, 0.02f, "toward its foot");
            Assert.AreEqual(0f, written.x, 0.05f, "about its middle");
            Assert.AreEqual(5f, written.z, 0.05f);
            Assert.AreEqual(collider.size.y, eraser.GameObject.GetComponentInChildren<Collider>().bounds.size.y, 1e-4f, "the collider never changes");

            // Springing back: through zero, a little the other way, and at rest after 150 ms.
            float least = float.MaxValue;
            for (int i = 0; i < TestHelpers.Ticks(LaunchCues.SquashSeconds) + 2; i++)
            {
                Frames();
                least = Mathf.Min(least, cues.PadSquash);
            }
            Assert.Less(least, 0f, "one overshoot");
            Assert.AreEqual(0f, cues.PadSquash);
            Assert.IsNull(cues.SquashedPad);
            Assert.AreEqual(0f, Written().w, "the renderers have their zero back");

            // The level goes while a pad is giving: it is let go of.
            game.Player.Teleport(new Vector3(0f, 4f, 5f), 0f);
            Assert.IsTrue(Until(() => pad.BounceCount > 1, 2f));
            Assert.AreSame(pad, cues.SquashedPad);
            Unload();
            Assert.IsNull(cues.SquashedPad, "nothing of the unloaded level is held on to");
            Assert.AreEqual(0f, cues.PadSquash);
        }

        [Test]
        public void ASeesawStrike_IsMarkedAtTheTip()
        {
            Seesaw seesaw = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 60f);
                seesaw = new Seesaw(ctx, new SeesawOptions { Pivot = new Vector3(0f, 1f, 8f), ArmADirection = Vector3.left, ArmA = 5f, ArmB = 2.5f, Width = 2f });
                ctx.SetSpawn(new Vector3(0f, 0f, -2f), 0f);
            });
            LaunchCues cues = Present<LaunchCues>();
            Frames(2);
            game.Context.AddProp(BasicToys.Block(2f), new Vector3(1.7f, 3.5f, 8f), new PropOptions { Name = "Striker" });
            Assert.IsTrue(Until(() => seesaw.State != SeesawState.Rest, 3f), "the block comes down on the short arm");
            Frames(1);
            Assert.AreEqual(1, cues.Fired);
            Assert.AreEqual(1, cues.Alive);
            Bounds ring = MeshOf(cues.RingRenderer).bounds;
            Assert.AreEqual(2.4f, ring.center.x, 0.3f, "at the struck tip");
            Assert.AreEqual(8f, ring.center.z, 0.1f);
        }

        [Test]
        public void PaperMarks_AreDieCutInADayRoom_AndPlainAtNight()
        {
            // Paper on a pale wall is white on white: in the day rooms a Paper mark is cut from a deeper band.
            foreach (Dip dip in Palette.Dips)
            {
                if (dip.Night) Assert.AreEqual(0f, GadgetFx.Edge(dip).a, dip.Name + ": Paper stands out at night by itself");
                else Assert.Greater(GadgetFx.Edge(dip).a, 0.3f, dip.Name);
            }
            Color mint = GadgetFx.Edge(Palette.Mint), deep = GadgetFx.Lin(Palette.Mint.Deep);
            Assert.Less(mint.r + mint.g + mint.b, deep.r + deep.g + deep.b, "the edge is deeper than the room's deep tone");

            var parent = new GameObject("Cue Pools") { hideFlags = HideFlags.DontSave };
            var day = new CuePool("Day", parent.transform, 4, GadgetFx.Edge(Palette.Mint));
            var night = new CuePool("Night", parent.transform, 4, GadgetFx.Edge(Palette.Plum));
            try
            {
                Assert.IsTrue(day.Outlines);
                Assert.IsFalse(night.Outlines);
                foreach (CuePool pool in new[] { day, night })
                {
                    pool.Hoop(Vector3.zero, Vector3.up, 2f, 2f, Color.white, 1f, true);
                    pool.Ring(Vector3.zero, Vector3.up, 1f, 1f, Color.white, 1f, true);
                    pool.Streak(Vector3.zero, 3f, 0.5f, Color.white, 1f, true);
                    // A signal's ring carries its own hue: never outlined.
                    pool.Ring(Vector3.zero, Vector3.up, 1f, 1f, Palette.Go.Emission, 1f);
                    pool.Update(0.1f, null);
                    Assert.AreEqual(4, pool.Alive);
                    Assert.AreEqual(4, pool.Capacity);
                }
                Assert.AreEqual((1 + 1) * 4, night.RingMesh.vertexCount);
                Assert.AreEqual((2 + 1) * 4, day.RingMesh.vertexCount, "the Paper ring lies on its edge; the signal's ring has none");
                Assert.AreEqual((CuePool.HoopSegments + 1) * 4, night.StreakMesh.vertexCount);
                Assert.AreEqual((2 * CuePool.HoopSegments + 2) * 4, day.StreakMesh.vertexCount, "a band and a core per hoop, an edge and a core per streak");
                // The hoop is as big either way (the band grows inward); the ring's edge shows round its outside.
                Assert.AreEqual(4f, night.StreakMesh.bounds.size.x, 0.01f);
                Assert.AreEqual(4f, day.StreakMesh.bounds.size.x, 0.01f);
                Assert.AreEqual(2f, night.RingMesh.bounds.size.x, 0.01f);
                Assert.AreEqual(2f * CuePool.RingEdge, day.RingMesh.bounds.size.x, 0.01f);
                Assert.IsNull(day.RingRenderer.GetComponent<Collider>());
            }
            finally
            {
                day.Destroy();
                night.Destroy();
                Object.DestroyImmediate(parent);
            }
        }

        // ------------------------------------------------------------------------------------------
        // Train
        // ------------------------------------------------------------------------------------------

        [Test]
        public void ATrain_HasATrack_AndAStationLamp_ThatIsGreenWhileTheEngineStandsThere()
        {
            Train train = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 60f);
                train = new Train(ctx, new TrainOptions { Center = new Vector2(0f, 14f), Radius = 6f, DeckY = 1.02f, Cars = 2, CarArc = 34f, EngineArc = 40f, StartBearing = 290f, StationBearing = 0f });
                ctx.SetSpawn(new Vector3(0f, 0f, -4f), 0f);
            });
            TrainMarks marks = Present<TrainMarks>();
            Frames(2);

            Assert.AreEqual(1, marks.Count);
            Renderer track = marks.TrackOf(0), station = marks.StationOf(0), lamp = marks.LampOf(0);
            Assert.AreSame(Materials.Gadget(GadgetPart.Body), track.sharedMaterial, "the track is gadget Ink");
            Assert.AreEqual(ShadowCastingMode.Off, track.shadowCastingMode);
            Bounds bounds = track.bounds;
            Assert.AreEqual(0f, bounds.center.x, 0.05f);
            Assert.AreEqual(14f, bounds.center.z, 0.05f);
            float gauge = train.BedSize.x * 0.5f - TrainMarks.WheelInset;
            Assert.AreEqual(2f * (6f + gauge), bounds.size.x, 0.3f, "two rails round the circle the train runs on");
            Assert.AreEqual(train.DeckY - TrainMarks.WheelDrop, bounds.max.y, 0.01f, "the rails end where the wheels do");
            Assert.AreEqual(0f, bounds.min.y, 0.01f, "and the sleepers lie on the floor");

            train.PoseAt(train.StationBearing, out Vector3 point, out _);
            Assert.That(Vector3.Distance(point, marks.StationPointOf(0)), Is.LessThan(1e-4f));
            Assert.Less(Mathf.Abs(station.bounds.center.x - point.x), 0.2f, "the caps are at the station");
            Assert.Less(Mathf.Abs(station.bounds.center.z - point.z), 0.6f);
            Assert.Less(Vector3.Distance(lamp.bounds.center, point), train.BedSize.x * 0.5f + 1.5f, "so is the lamp");
            Assert.Less(lamp.bounds.max.y, train.DeckY, "below the decks that pass it");

            // The engine is away: amber. It arrives: green. It leaves: amber again.
            Assert.IsFalse(marks.AtStation(0));
            Assert.AreSame(Materials.Gadget(Palette.Amber), lamp.sharedMaterial);
            Assert.IsTrue(Until(() => marks.AtStation(0), 4f), "the engine reaches the station");
            Assert.AreSame(Materials.Gadget(Palette.Go), lamp.sharedMaterial);
            Assert.Less(Vector3.Distance(train.Engine.Position, point), train.BedSize.z);
            Assert.IsTrue(Until(() => !marks.AtStation(0), 5f), "and leaves");
            Assert.AreSame(Materials.Gadget(Palette.Amber), lamp.sharedMaterial);
            Assert.Greater(Vector3.Distance(train.Engine.Position, point), train.BedSize.z * 0.5f);

            Assert.AreEqual(0, RootOf<TrainMarks>().GetComponentsInChildren<Collider>(true).Length, "nothing to stand on or trip over");
            Unload();
            Assert.AreEqual(0, marks.Count);
            Assert.AreEqual(0, marks.KeptCount);
            Assert.AreEqual(0, RootOf<TrainMarks>().childCount);
        }

        // ------------------------------------------------------------------------------------------
        // Debris
        // ------------------------------------------------------------------------------------------

        [Test]
        public void ABrokenBarricade_FliesApart_AndIsGoneTwoSecondsLater()
        {
            Breakable wall = null;
            Build(ctx =>
            {
                // An ice floor: the blocks arrive at the speed they were launched with.
                var ice = new PhysicsMaterial("Ice") { dynamicFriction = 0f, staticFriction = 0f, frictionCombine = PhysicsMaterialCombine.Minimum };
                TestHelpers.Floor(ctx, 60f).GetComponent<Collider>().sharedMaterial = ice;
                ctx.OnDispose(() => Object.DestroyImmediate(ice));
                wall = new Breakable(ctx, new BreakableOptions { Center = new Vector3(0f, 2f, 10f), Size = new Vector3(5f, 4f, 0.8f), MinMass = 5f, MinSpeed = 2.5f });
                ctx.SetSpawn(new Vector3(9f, 0f, 0f), 0f);
            });
            BreakDebris debris = Present<BreakDebris>();
            Frames(2);
            Assert.AreEqual(0, debris.Count);
            Material birch = wall.Body.GetComponentInChildren<Renderer>().sharedMaterial;

            // Too slow: a bonk, no debris.
            Prop slow = game.Context.AddProp(BasicToys.Block(2f), new Vector3(0f, 1.01f, 7.5f), new PropOptions { Name = "Slow", Friction = 0f });
            slow.Body.linearVelocity = new Vector3(0f, 0f, 1.6f);
            Seconds(1.5f);
            Assert.IsFalse(wall.Broken);
            Assert.AreEqual(1, debris.Bonks);
            Assert.AreEqual(0, debris.Count);
            game.Context.RemoveProp(slow);

            Prop ram = game.Context.AddProp(BasicToys.Block(2f), new Vector3(0f, 1.01f, 6f), new PropOptions { Name = "Ram", Friction = 0f });
            ram.Body.linearVelocity = new Vector3(0f, 0f, 9f);
            Assert.IsTrue(Until(() => wall.Broken, 2f));
            Frames(1);
            Assert.AreEqual(1, debris.Broken);
            Assert.AreEqual(1, debris.Count);
            Assert.AreEqual(9, debris.ChunkCount(0), "three courses of three blocks");
            Renderer chunks = debris.RendererOf(0);
            Assert.AreSame(birch, chunks.sharedMaterial, "the chunks are made of what the wall was made of");
            Assert.AreEqual(9 * 24, MeshOf(chunks).vertexCount);
            Assert.IsNull(chunks.GetComponent<Collider>());

            // They fly on the way the blow went, and come down on the ground the wall stood on.
            Vector3 start = debris.ChunkPosition(0, 4);
            Seconds(0.3f);
            Vector3 flying = debris.ChunkPosition(0, 4);
            Assert.Greater(flying.z, start.z + 0.5f);
            Seconds(1f);
            for (int i = 0; i < 9; i++)
            {
                Assert.GreaterOrEqual(debris.ChunkPosition(0, i).y, 0f, "no chunk falls through the floor");
                Assert.Greater(debris.ChunkPosition(0, i).z, 10f);
            }

            Seconds(BreakDebris.Seconds);
            Assert.AreEqual(0, debris.Count, "spent");
            Assert.IsFalse(chunks.enabled);

            Unload();
            Assert.AreEqual(0, debris.KeptCount);
            Assert.AreEqual(0, debris.Broken);
        }

        // ------------------------------------------------------------------------------------------
        // Cues
        // ------------------------------------------------------------------------------------------

        [Test]
        public void ARecallPad_ShowsItsWait_AndGadgetsLeaveARingWhenTheyAct()
        {
            RecallPad pad = null;
            Socket socket = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 40f);
                Prop block = ctx.AddProp(BasicToys.Block(1f), new Vector3(6f, 0.51f, 6f), new PropOptions { Name = "Fetched" });
                pad = new RecallPad(ctx, new RecallPadOptions { Position = new Vector3(0f, 0f, 4f), Radius = 0.8f, Prop = block, HoldSeconds = 0.5f });
                socket = new Socket(ctx, new SocketOptions
                {
                    AcceptTag = "peg", Capture = Zone.Box(new Vector3(-6f, 0.7f, 6f), new Vector3(1.4f, 1.4f, 1.4f)), MinScale = 0.8f, MaxScale = 1.2f,
                    SeatPose = s => new Pose(new Vector3(-6f, 0.5f * s, 6f), Quaternion.identity),
                });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            GadgetCues cues = Present<GadgetCues>();
            Frames(2);

            Assert.AreEqual(1, cues.PadCount);
            Renderer ring = cues.PadRingOf(0);
            AssertEffect(ring, "the pad's ring");
            Assert.IsFalse(ring.enabled, "nobody stands on the pad");
            Assert.AreEqual(0, cues.Fired);

            // Standing on it: the ring closes in on the pad as the wait runs.
            game.Player.Teleport(pad.Position, 0f);
            Frames(10);
            Assert.IsTrue(pad.PlayerOn);
            Assert.That(cues.PadProgressOf(0), Is.GreaterThan(0f).And.LessThan(1f));
            Assert.IsTrue(ring.enabled);
            float wide = ring.bounds.size.x;
            // Whoever stands on a pad looks ahead: the ring starts where a level gaze still sees the floor.
            float floorInView = Player.BaseEyeHeight / Mathf.Tan(Settings.DefaultFieldOfView * 0.5f * Mathf.Deg2Rad);
            Assert.Greater(wide * 0.5f * 0.8f, floorInView, "the whole band of the ring starts out in view of a player who looks straight ahead");
            Frames(10);
            Assert.Less(ring.bounds.size.x, wide, "closing");
            Assert.GreaterOrEqual(ring.bounds.size.x, pad.Radius * 2f, "onto the pad");
            Assert.IsTrue(Until(() => pad.Recalls > 0, 2f));
            Frames(1);
            Assert.IsFalse(ring.enabled, "done");
            Assert.AreEqual(1, cues.Fired, "the fetched toy arrives with a ring");
            Assert.AreEqual(1, cues.Alive);

            // A socket refuses a toy (two quick rings) and seats another (one).
            game.Context.AddProp(BasicToys.Block(1f), new Vector3(-6f, 0.26f, 6f), new PropOptions { Name = "Small", Scale = 0.5f, Tags = new[] { "peg" } });
            Frames(3);
            Assert.AreEqual(3, cues.Fired);
            Seconds(GadgetCues.Seconds + 0.1f);
            Assert.AreEqual(0, cues.Alive, "cues are short");

            Unload();
            Assert.AreEqual(0, cues.PadCount);
            Assert.AreEqual(0, cues.KeptCount);
        }

        // ------------------------------------------------------------------------------------------
        // The gallery
        // ------------------------------------------------------------------------------------------

        [Test]
        public void TheGallery_IsToured_WithEveryPresenterAttached_AndLeavesNothingBehind()
        {
            game = Game.Create();
            game.LoadLevel(98);
            var level = (Level98GadgetGallery)game.Level;
            Assert.AreEqual("gadget-gallery", level.Slug);
            presentation = Presentation.Create(game, new PresentationOptions { Quality = QualityTier.Low, Presenters = Only(Presenters) });

            int completed = 0, launches = 0, bounces = 0, breaks = 0, recalls = 0;
            game.Events.LevelCompleted += e => completed++;
            game.Events.SeesawLaunched += e => launches++;
            game.Events.Bounced += e => bounces++;
            game.Events.BreakableBroke += e => breaks++;
            game.Events.Recalled += e => recalls++;

            var lasers = presentation.Get<LaserVisuals>();
            var wind = presentation.Get<WindVisuals>();
            var lamps = presentation.Get<FitLamps>();
            var water = presentation.Get<WaterVisuals>();
            var portals = presentation.Get<PortalView>();
            var launch = presentation.Get<LaunchCues>();
            var trains = presentation.Get<TrainMarks>();
            var debris = presentation.Get<BreakDebris>();
            var cues = presentation.Get<GadgetCues>();

            // The tour, a frame per tick; what the presenters showed along the way is collected as it happens.
            var bot = new Bot(game);
            var script = new BotRunner(game.Level.Solve(bot));
            var tints = new HashSet<FitTint>();
            float basinMoved = 0f, towerMoved = 0f;
            int chunks = 0, launchMarks = 0;
            bool running = true, doorClosed = false;
            for (int tick = 0; tick < TestHelpers.Ticks(150f) && (running || !game.LevelCompleted); tick++)
            {
                if (running) running = script.Advance();
                game.Tick();
                presentation.Frame(Sim.Dt, 1f);
                tints.Add(lamps.TintOf(level.Gauge));
                basinMoved = Mathf.Max(basinMoved, water.MovingOf(level.Basin));
                towerMoved = Mathf.Max(towerMoved, water.MovingOf(level.Tower));
                if (debris.Count > 0) chunks = Mathf.Max(chunks, debris.ChunkCount(0));
                launchMarks = Mathf.Max(launchMarks, launch.Fired);
                if (portals.IsInHand(level.SmallDoor)) doorClosed |= portals.HeldCardRendererOf(level.SmallDoor).enabled && !portals.IsOpen(level.SmallDoor);
                if (tick == TestHelpers.Ticks(1.5f))
                {
                    // The front row as it stands when the level opens.
                    Assert.AreEqual(1, lasers.Count);
                    Assert.Greater(lasers.ShadedCount(0), 0, "the lane under the card is shaded");
                    Assert.Less(lasers.ShadedCount(0), lasers.DotCount(0), "the floor either side of it is not");
                    Assert.AreEqual(1, wind.Count);
                    Assert.AreEqual(1, wind.FanCount);
                    Assert.Greater(wind.FanSpeedOf(0), 0f, "the fan at the mouth of the stream turns");
                    Assert.AreEqual(1, lamps.GaugeCount);
                    Assert.AreEqual(3, lamps.SocketCount);
                    Assert.AreEqual(FitTint.Waiting, lamps.TintOf(level.WaitingSocket));
                    Assert.AreEqual(FitTint.Wrong, lamps.TintOf(level.RefusingSocket));
                    Assert.AreEqual(FitTint.Fits, lamps.TintOf(level.SeatedSocket));
                    Assert.AreEqual(4, water.Count);
                    Assert.AreEqual(3, water.SpongeCount);
                    Assert.AreEqual(0f, water.WetnessOf(level.DrySponge), 1e-3f);
                    Assert.That(water.WetnessOf(level.HalfSponge), Is.InRange(0.3f, 0.7f));
                    Assert.AreEqual(1f, water.WetnessOf(level.SoakedSponge), 1e-3f);
                    Assert.AreEqual(2, portals.Count);
                    Assert.IsTrue(portals.IsOpen(level.SmallDoor) && portals.IsOpen(level.BigDoor));
                    Assert.AreEqual(1f, portals.CardOf(level.SmallDoor), "Low: cards");
                    Assert.AreEqual(1, trains.Count);
                    Assert.AreEqual(1, cues.PadCount);

                    // The budget (ART_BIBLE 12.1: draws are the scarce thing): every gadget of the game at once -
                    // one laser lattice, a stream, four waters, two portals, a train - is a couple of dozen draws,
                    // and none of the effects casts a shadow.
                    int drawn = 0, casting = 0;
                    foreach (Renderer renderer in presentation.Context.Root.GetComponentsInChildren<Renderer>(false))
                    {
                        if (!renderer.enabled || renderer.forceRenderingOff) continue;
                        drawn++;
                        if (renderer.shadowCastingMode != ShadowCastingMode.Off) casting++;
                    }
                    Assert.That(drawn, Is.InRange(12, 26), "draws of all gadget presenters together");
                    Assert.AreEqual(0, casting, "no effect casts a shadow while nothing is breaking");
                }
            }
            Assert.IsFalse(script.Failed, "the tour ran to its end: " + script.Error);
            Assert.IsTrue(game.LevelCompleted, "and out of the exit (bot at " + game.Player.Position + ")");
            Assert.AreEqual(1, completed);

            // When each thing happened, for whoever wants to take pictures of the tour (Temp/ToyboxGalleryBeats.txt).
            var lines = new List<string>();
            foreach (KeyValuePair<string, float> beat in level.Beats) lines.Add(beat.Key + " " + beat.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            lines.Add("end " + game.Time.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            System.IO.File.WriteAllLines(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.dataPath), "Temp", "ToyboxGalleryBeats.txt"), lines);
            foreach (string beat in new[] { "gauge-small", "gauge-good", "gauge-big", "launch", "bounce", "break", "portal", "recall", "door-held" })
                Assert.GreaterOrEqual(level.BeatTime(beat), 0f, "the tour reached '" + beat + "'");

            CollectionAssert.IsSubsetOf(new[] { FitTint.Waiting, FitTint.Fits, FitTint.Wrong }, tints, "the gauge's lamp showed every state");
            Assert.AreEqual(1f, basinMoved, 1e-3f, "the basin was seen to drain");
            Assert.AreEqual(1f, towerMoved, 1e-3f, "and the tower to fill");
            Assert.AreEqual(1, launches);
            Assert.GreaterOrEqual(bounces, 1);
            Assert.AreEqual(1, breaks);
            Assert.AreEqual(1, recalls);
            Assert.GreaterOrEqual(launchMarks, 3, "the strike, the launch and the bounce were marked");
            Assert.Greater(chunks, 0, "the barricade flew apart");
            Assert.IsTrue(doorClosed, "the door's opening was closed while it was carried");
            Assert.Greater(cues.Fired, 0);

            // Renderers only.
            Assert.AreEqual(0, presentation.Context.Root.GetComponentsInChildren<Collider>(true).Length, "no presenter adds a collider");
            Assert.AreEqual(0, presentation.Context.Root.GetComponentsInChildren<Rigidbody>(true).Length);

            // The level goes: every presenter lets go of it.
            Unload();
            Assert.AreEqual(0, lasers.Count + wind.Count + wind.FanCount + lamps.GaugeCount + lamps.SocketCount + water.Count + water.SpongeCount + portals.Count + trains.Count + debris.Count + cues.PadCount);
            foreach (IPresenter presenter in presentation.Presenters)
            {
                var visual = (GadgetVisual)presenter;
                Assert.AreEqual(0, visual.KeptCount, presenter.GetType().Name + " kept something of the unloaded level");
                Assert.IsTrue(visual.Attached);
            }
            foreach (Renderer renderer in presentation.Context.Root.GetComponentsInChildren<Renderer>(true))
                Assert.IsFalse(renderer.enabled && renderer.gameObject.activeInHierarchy, renderer.name + " is still drawn in a level without gadgets");

            // And the presentation goes: nothing is left at all.
            var presenters = new List<IPresenter>(presentation.Presenters);
            presentation.Dispose();
            presentation = null;
            foreach (IPresenter presenter in presenters) Assert.IsFalse(((GadgetVisual)presenter).Attached);
            Assert.IsNull(game.Root.transform.Find("Presentation"));
        }

        // ART_BIBLE 12.2: zero managed allocation per frame in Render.
        [Test]
        public void TheGadgetPresenters_AllocateNothingPerFrame()
        {
            // (What the second camera's own render allocates is the pipeline's business: every door on its card.)
            PortalView.AllowCamera = false;
            game = Game.Create();
            game.LoadLevel(98);
            presentation = Presentation.Create(game, new PresentationOptions { Quality = QualityTier.Medium, Presenters = Only(Presenters) });

            // The opening: sockets seat and refuse (rings in the air), sponges drink (ripples), streamers run.
            Seconds(0.4f);
            Assert.Greater(presentation.Get<GadgetCues>().Alive + presentation.Get<WaterVisuals>().Ripples, 0, "cues are on show");
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 40; i++) presentation.Frame(1f / 120f, 1f);
            Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before, "bytes allocated by 40 frames with cues on show");

            // Later: the water is running, the train is under way, every lamp is pulsing.
            Seconds(5f);
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 120; i++) presentation.Frame(1f / 60f, 1f);
            Assert.AreEqual(0, GC.GetAllocatedBytesForCurrentThread() - before, "bytes allocated by 120 frames of the gallery");
        }

        [Test]
        public void TheGallery_ReplaysTheSame_WithAndWithoutItsPresenters()
        {
            // Presentation reads; it never writes. The simulation ends in the same state either way.
            string Fingerprint(bool present)
            {
                game = Game.Create();
                game.LoadLevel(98);
                if (present) presentation = Presentation.Create(game, new PresentationOptions { Quality = QualityTier.Low, Presenters = Only(Presenters) });
                var bot = new Bot(game);
                var script = new BotRunner(game.Level.Solve(bot));
                for (int tick = 0; tick < TestHelpers.Ticks(25f); tick++)
                {
                    script.Advance();
                    game.Tick();
                    presentation?.Frame(Sim.Dt, 1f);
                }
                var text = new System.Text.StringBuilder();
                text.Append(game.Player.Position.ToString("R"));
                foreach (Prop prop in game.Props)
                    text.Append('|').Append(prop.Name).Append(prop.Position.ToString("R")).Append(prop.Rotation.eulerAngles.ToString("R")).Append(prop.Scale.ToString("R"));
                foreach (Mover mover in game.Movers) text.Append('|').Append(mover.Position.ToString("R"));
                presentation?.Dispose();
                presentation = null;
                game.Dispose();
                game = null;
                return text.ToString();
            }
            Assert.AreEqual(Fingerprint(false), Fingerprint(true));
        }

        // ------------------------------------------------------------------------------------------
        // Pictures
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// The gallery in another room, seen from a fixed spot: it builds like the gallery, and nobody walks.
        /// With <c>holdDoor</c> the small portal door is picked up (by its lintel) after half a second and carried.
        /// </summary>
        public sealed class GalleryStill : LevelDefinition
        {
            readonly Level98GadgetGallery inner = new Level98GadgetGallery();
            readonly string environment;
            readonly Vector3 position;
            readonly float yaw, pitch;
            readonly bool holdDoor;

            public GalleryStill(string environment, Vector3 position, float yaw, float pitch, bool holdDoor = false)
            {
                this.environment = environment;
                this.position = position;
                this.yaw = yaw;
                this.pitch = pitch;
                this.holdDoor = holdDoor;
            }

            public override string Slug => "gadget-gallery-still";
            public override string Title => "Gadget Gallery";
            public override string Environment => environment;
            public override float GroundY => inner.GroundY;

            public override void Build(LevelContext ctx)
            {
                inner.Build(ctx);
                ctx.SetSpawn(position, yaw, pitch);
            }

            public override System.Collections.IEnumerator Solve(Bot bot)
            {
                if (holdDoor)
                {
                    yield return bot.Wait(0.5f);
                    Prop door = inner.SmallDoor.Prop;
                    float lintel = ToyFactory.DoorwayOpening.y + (ToyFactory.DoorwaySize.y - ToyFactory.DoorwayOpening.y) * 0.5f;
                    yield return bot.GrabAt(door, door.Position + door.Rotation * new Vector3(0f, lintel * door.Scale, 0f));
                }
                yield return bot.Wait(600f);
            }
        }

        // name, x, y, z, yaw, pitch, then the times to take the picture at.
        static readonly object[][] Views =
        {
            new object[] { "lasers", -30f, 0f, 0.6f, 0f, 4f, new[] { 1.5f } },
            new object[] { "lasers-side", -37f, 0f, 3f, 55f, 2f, new[] { 1.5f } },
            new object[] { "wind", -16.5f, 0f, 1.2f, -8f, 8f, new[] { 1.5f } },
            new object[] { "sockets", 6.2f, 0f, -0.6f, 0f, 2f, new[] { 1.5f, 1.67f } },
            new object[] { "water", 18.5f, 0f, -1.2f, -14f, -4f, new[] { 1.5f, 6f, 12f } },
            new object[] { "sponges", 15f, 0f, -0.4f, 0f, -14f, new[] { 0.1f, 1.5f } },
            new object[] { "water-top", 13.2f, 0f, 4.6f, 20f, -22f, new[] { 1.5f, 8f } },
            new object[] { "portal", 28.6f, 0f, -0.5f, -6f, -2f, new[] { 1.5f } },
            new object[] { "portal-near", 27.2f, 0f, 3.6f, -2f, -18f, new[] { 1.5f } },
            new object[] { "portal-big", 33f, 0f, 5.5f, 0f, 14f, new[] { 1.5f } },
            new object[] { "portal-held", 27.2f, 0f, 3.6f, -2f, -18f, new[] { 2.5f } },
            new object[] { "train", 2f, 0f, 25.5f, 0f, -6f, new[] { 1.5f, 6.7f } },
        };

        /// <summary>
        /// Pictures of the gallery from fixed spots, in one or more rooms, into tools/out/shots/level98:
        ///   tools\unity.ps1 exec -Method Toybox.Tests.GadgetVisualsTests.Capture -UnityArgs -toyboxEnv,night-light,-toyboxViews,lasers
        /// -toyboxEnv room keys (default "sunny-rug,night-light"), -toyboxViews names from the table above
        /// (default all), -toyboxQuality low|medium|high, -toyboxOut a directory, -toyboxSize WxH.
        /// Files are named room-view-tSS.png. (The tour itself is photographed with Shots.Capture.)
        /// </summary>
        public static void Capture()
        {
            string[] rooms = Toybox.EditorTools.ToyboxArgs.Get("-toyboxEnv", "sunny-rug,night-light").Split(',');
            string wanted = Toybox.EditorTools.ToyboxArgs.Get("-toyboxViews", "");
            var names = new HashSet<string>(wanted.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            string directory = Toybox.EditorTools.ToyboxArgs.Get("-toyboxOut", "tools/out/shots/level98");
            QualityTier? tier = Toybox.EditorTools.Shots.ParseQuality(Toybox.EditorTools.ToyboxArgs.Get("-toyboxQuality"));
            int width = 1280, height = 720;
            Toybox.EditorTools.Shots.ParseSize(Toybox.EditorTools.ToyboxArgs.Get("-toyboxSize", "1280x720"), ref width, ref height);
            string suffix = tier.HasValue ? "-" + tier.Value.ToString().ToLowerInvariant() : "";
            foreach (string raw in rooms)
            {
                string room = raw.Trim();
                foreach (object[] view in Views)
                {
                    var name = (string)view[0];
                    if (names.Count > 0 && !names.Contains(name)) continue;
                    var still = new GalleryStill(room, new Vector3((float)view[1], (float)view[2], (float)view[3]), (float)view[4], (float)view[5], name == "portal-held");
                    List<string> files = Toybox.EditorTools.Shots.Run(new Toybox.EditorTools.ShotRequest
                    {
                        Level = 98, Definition = still, Times = (float[])view[6], Width = width, Height = height, OutputDirectory = directory, Quality = tier,
                    });
                    foreach (string file in files)
                    {
                        string fileName = System.IO.Path.GetFileName(file);
                        int cut = fileName.IndexOf("-t", StringComparison.Ordinal);
                        string renamed = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file), room + "-" + name + suffix + (cut >= 0 ? fileName.Substring(cut) : "-" + fileName));
                        if (System.IO.File.Exists(renamed)) System.IO.File.Delete(renamed);
                        System.IO.File.Move(file, renamed);
                        Debug.Log("[Toybox] gallery shot: " + renamed);
                    }
                }
            }
        }
    }
}
