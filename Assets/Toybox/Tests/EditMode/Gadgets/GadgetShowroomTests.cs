using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Toybox.EditorTools;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Tests.Gadgets
{
    /// <summary>
    /// Every gadget that has a body, side by side, for looking at: Ink bodies, Steel for what moves, one
    /// signal element each (amber waiting, green satisfied, hazard for the lasers). Not a campaign level.
    /// </summary>
    public sealed class GadgetShowroomLevel : LevelDefinition
    {
        public override string Environment => "sunny-rug";
        public override string Slug => "gadget-showroom";
        public override string Title => "Gadget Showroom";

        public override void Build(LevelContext ctx)
        {
            ctx.AddStatic(BasicToys.Slab(new Vector3(44f, 1f, 44f)), new Vector3(0f, -0.5f, 8f));

            // Front row: plates, doors, a breakable wall, a recall pad.
            new PressurePlate(ctx, new PressurePlateOptions { Name = "waiting", Sensor = Zone.Cylinder(-12f, 0f, 1.2f, 0f, 1.5f), MinMass = 5f });
            new PressurePlate(ctx, new PressurePlateOptions { Name = "pressed", Sensor = Zone.Cylinder(-8.5f, 0f, 1.2f, 0f, 1.5f), MinMass = 0.5f });
            ctx.AddProp(BasicToys.Block(1f), new Vector3(-8.5f, 0.7f, 0f), new PropOptions { Name = "Weight" });
            new Door(ctx, new DoorOptions { Name = "closed", Size = new Vector3(2f, 3f, 0.3f), ClosedPosition = new Vector3(-5f, 1.5f, 0f), OpenPosition = new Vector3(-5f, 4.6f, 0f) });
            var open = new Door(ctx, new DoorOptions { Name = "open", Size = new Vector3(2f, 3f, 0.3f), ClosedPosition = new Vector3(-2f, 1.5f, 0f), OpenPosition = new Vector3(-2f, 3.2f, 0f), StartsOpen = true });
            new Breakable(ctx, new BreakableOptions { Center = new Vector3(3f, 2f, 0.5f), Size = new Vector3(5f, 4f, 0.8f), MinMass = 14f, MinSpeed = 2.5f });
            Prop doorway = ToyCatalog.Add(ctx, ToyId.Doorway, new Vector3(12f, 0f, 3f), Quaternion.Euler(0f, 200f, 0f));
            new PortalDoorway(ctx, new PortalDoorwayOptions { Prop = doorway });
            new RecallPad(ctx, new RecallPadOptions { Position = new Vector3(9f, 0f, 0f), Prop = doorway });
            new Socket(ctx, new SocketOptions { Capture = Zone.Sphere(new Vector3(0f, -40f, 0f), 1f), SeatPose = s => new Pose(Vector3.zero, Quaternion.identity), Lamp = new Vector3(7f, 2.5f, 0.5f), LampRadius = 0.3f });

            // Middle row: seesaw, funnel, water with a cork.
            ctx.AddStatic(BasicToys.Cylinder(0.5f, 3f), new Vector3(-9f, 0.5f, 9f), Quaternion.Euler(90f, 0f, 0f));
            new Seesaw(ctx, new SeesawOptions { Pivot = new Vector3(-9f, 1f, 9f), ArmADirection = Vector3.left, ArmA = 5f, ArmB = 2.5f, Width = 2f, SeatArm = 4.4f });
            Funnel.Build(ctx, new FunnelOptions { Axis = new Vector2(0f, 9f), RimY = 1.6f, MouthRadius = 1.4f, ThroatRadius = 0.5f, ConeDepth = 0.63f, TubeLength = 0.5f, OuterRadius = 1.6f, OuterBaseY = 0f });
            ToyCatalog.Add(ctx, ToyId.Marble, new Vector3(0.6f, 2.4f, 9.3f), 1.3f);
            var pond = new WaterVolume(ctx, new WaterVolumeOptions { Footprint = Zone.MinMax(new Vector3(5f, 0f, 6f), new Vector3(11f, 2f, 12f)), FloorY = 0f, Area = 36f, Volume = 25f });
            new FloatPlatform(ctx, new FloatPlatformOptions { Size = new Vector3(2.2f, 0.4f, 2.2f), Position = new Vector2(8f, 9f), Volume = pond, RestY = 0.4f, MaxY = 3f });

            // Back row: lasers over a low roof, and the train.
            new LaserRain(ctx, new LaserRainOptions { Center = new Vector3(-10f, 5f, 17f), Size = new Vector2(5f, 4f), Range = 5f, LatticePitch = 0.45f, OnZapped = () => { } });
            ctx.AddStatic(BasicToys.Slab(new Vector3(0.4f, 2f, 3f)), new Vector3(-11.5f, 1f, 17f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(0.4f, 2f, 3f)), new Vector3(-9f, 1f, 17f));
            ToyCatalog.Add(ctx, ToyId.PlayingCard, new Vector3(-10.25f, 2.1f, 17f), 2.2f);
            new Train(ctx, new TrainOptions { Center = new Vector2(4f, 21f), Radius = 7f, DeckY = 0.6f, Cars = 5, StartBearing = 80f, CarArc = 28f, EngineArc = 32f });

            ctx.SetSpawn(new Vector3(0f, 0f, -9f), 0f);
        }

        public override IEnumerator Solve(Bot bot)
        {
            yield return bot.LookAt(new Vector3(-3f, 1.2f, 0f));
            yield return bot.Wait(2f);
            yield return bot.WalkTo(new Vector3(-4f, 0f, 4.5f));
            yield return bot.LookAt(new Vector3(-9.5f, 1.5f, 13f));
            yield return bot.Until(() => bot.Game.Time > 7.5f, 10f);
            yield return bot.WalkTo(new Vector3(3.5f, 0f, 4f));
            yield return bot.LookAt(new Vector3(4.5f, 0.8f, 10f));
            yield return bot.Until(() => bot.Game.Time > 13.5f, 10f);
            yield return bot.LookAt(new Vector3(4f, 1f, 21f));
            yield return bot.Until(() => bot.Game.Time > 18.5f, 10f);
        }
    }

    public class GadgetShowroomTests
    {
        [TearDown]
        public void DisposeGame() => Game.Current?.Dispose();

        [Test]
        public void TheShowroom_BuildsAndRuns_Headless()
        {
            Game game = Game.Create();
            game.LoadLevel(new GadgetShowroomLevel());
            var bot = new Bot(game);
            BotRunner.Run(game, game.Level.Solve(bot), 40f);
            Assert.Greater(game.Time, 18f);
            // Every gadget visual is a renderer without a collider of its own, in a gadget, toy or room material.
            int signals = 0;
            foreach (Renderer renderer in game.LevelRoot.GetComponentsInChildren<Renderer>(true))
            {
                Assert.IsNotNull(renderer.sharedMaterial, renderer.name + " has a material");
                if (renderer.name == "Signal") signals++;
            }
            Assert.GreaterOrEqual(signals, 7, "plates, doors, the seat, the pad and the socket lamp each show one signal element");
        }

        [Test]
        public void TheShowroom_ReplaysBitForBit()
        {
            // Gadgets are deterministic: the same level with the same inputs ends in identical transforms.
            string first = Fingerprint();
            Game.Current?.Dispose();
            string second = Fingerprint();
            Assert.AreEqual(first, second);
        }

        static string Fingerprint()
        {
            Game game = Game.Create();
            game.LoadLevel(new GadgetShowroomLevel());
            var bot = new Bot(game);
            BotRunner.Run(game, game.Level.Solve(bot), 40f);
            var text = new System.Text.StringBuilder();
            text.Append(Bits(game.Player.Position));
            foreach (Prop prop in game.Props)
                text.Append('|').Append(prop.Name).Append(Bits(prop.Position)).Append(Bits(prop.Rotation.eulerAngles)).Append(prop.Scale.ToString("R"));
            foreach (Mover mover in game.Movers)
                text.Append('|').Append(Bits(mover.Position));
            return text.ToString();
        }

        static string Bits(Vector3 v) =>
            System.BitConverter.SingleToInt32Bits(v.x).ToString("x8") + System.BitConverter.SingleToInt32Bits(v.y).ToString("x8") + System.BitConverter.SingleToInt32Bits(v.z).ToString("x8");

        [Test]
        public void TheShowroom_Renders()
        {
            Assume.That(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "rendering needs a graphics device (-nographics run)");
            string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "ToyboxGadgetShots");
            List<string> files = Render(directory, 320, 180, new[] { 1.5f });
            Assert.AreEqual(2, files.Count);
            foreach (string file in files) Assert.IsTrue(File.Exists(file), file);
        }

        /// <summary>
        /// Renders the showroom for looking at, into tools/out/shots/m2-gadgets:
        ///   tools\unity.ps1 exec -Method Toybox.Tests.Gadgets.GadgetShowroomTests.Capture
        /// </summary>
        public static void Capture()
        {
            string directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "tools", "out", "shots", "m2-gadgets");
            Render(directory, 1280, 720, new[] { 1.5f, 7f, 13f, 18f });
        }

        static List<string> Render(string directory, int width, int height, float[] times) => Shots.Run(new ShotRequest
        {
            Level = 98,
            Definition = new GadgetShowroomLevel(),
            Times = times,
            Width = width,
            Height = height,
            OutputDirectory = directory,
            Overview = true,
        });
    }
}
