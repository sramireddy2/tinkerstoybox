using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    /// <summary>
    /// What gadgets show to presentation and nothing else reads: the list of a level's gadgets, and the
    /// few accessors a presenter needs (a lamp to tint, a surface to stand a waterline on, where the
    /// station is, how wide a pad is).
    /// </summary>
    public class GadgetListTests : SimTest
    {
        [Test]
        public void ALevelsGadgets_AreListedInConstructionOrder_AndGoWithTheLevel()
        {
            WindStream wind = null;
            HazardZone hazard = null;
            WaterVolume water = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx, 30f);
                wind = new WindStream(ctx, new WindStreamOptions { Center = new Vector3(0f, 2f, 8f) });
                hazard = new HazardZone(ctx, new HazardZoneOptions { Shape = Zone.Sphere(new Vector3(0f, -50f, 0f), 1f) });
                water = new WaterVolume(ctx, new WaterVolumeOptions { Footprint = Zone.Cylinder(8f, 8f, 2f, 0f, 2f), Volume = 5f, Area = 12f });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            LevelContext first = Game.Context;
            IReadOnlyList<Gadget> gadgets = GadgetList.Of(Game);
            CollectionAssert.AreEqual(new Gadget[] { wind, hazard, water }, gadgets);
            Assert.AreSame(gadgets, GadgetList.Of(first), "one list per level, by game or by context");

            // A gadget made while the level runs joins the list.
            LaserRain late = null;
            Game.Context.OnUpdate(dt =>
            {
                if (late == null) late = new LaserRain(Game.Context, new LaserRainOptions { Center = new Vector3(0f, 5f, 0f), Range = 4f, Visual = false, OnZapped = () => { } });
            });
            Run(2);
            Assert.AreEqual(4, GadgetList.Of(Game).Count);
            Assert.AreSame(late, GadgetList.Of(Game)[3]);

            // A restart builds a new level with a list of its own; the old one is gone.
            WindStream firstWind = wind;
            Game.RestartLevel();
            Assert.AreEqual(0, GadgetList.Of(first).Count, "the unloaded level's list is dropped");
            Assert.AreEqual(3, GadgetList.Of(Game).Count);
            Assert.AreNotSame(firstWind, GadgetList.Of(Game)[0], "the rebuilt level has gadgets of its own");
            Assert.AreSame(wind, GadgetList.Of(Game)[0]);
            Assert.IsTrue(firstWind.Disposed);

            Game.Dispose();
            Assert.AreEqual(0, GadgetList.Of(Game).Count);
            Assert.AreEqual(0, GadgetList.Of((LevelContext)null).Count);
            Assert.AreEqual(0, GadgetList.Of((Game)null).Count);
        }

        [Test]
        public void Gadgets_ShowPresentationWhatItNeeds()
        {
            Socket lit = null, dark = null;
            FitGauge gauge = null, plain = null;
            WaterVolume drawn = null, hidden = null;
            Train train = null;
            RecallPad pad = null;
            BouncePad toyPad = null, fixedPad = null;
            Prop bouncy = null;
            Collider trampoline = null;
            Build(ctx =>
            {
                trampoline = TestHelpers.Box(ctx, new Vector3(20f, 0.2f, 0f), new Vector3(3f, 0.4f, 3f)).GetComponent<Collider>();
                bouncy = ctx.AddProp(BasicToys.Block(1f), new Vector3(12f, 0.5f, 0f));
                toyPad = new BouncePad(ctx, new BouncePadOptions { Prop = bouncy });
                fixedPad = new BouncePad(ctx, new BouncePadOptions { Surface = trampoline, Scale = 2f });
                TestHelpers.Floor(ctx, 60f);
                lit = new Socket(ctx, new SocketOptions { Capture = Zone.Sphere(new Vector3(0f, -40f, 0f), 1f), SeatPose = s => new Pose(Vector3.zero, Quaternion.identity), Lamp = new Vector3(2f, 2f, 5f) });
                dark = new Socket(ctx, new SocketOptions { Capture = Zone.Sphere(new Vector3(0f, -40f, 0f), 1f), SeatPose = s => new Pose(Vector3.zero, Quaternion.identity) });
                gauge = new FitGauge(ctx, new FitGaugeOptions { MinScale = 1f, MaxScale = 2f, Near = Zone.Sphere(Vector3.zero, 5f), Lamp = new Vector3(-2f, 2f, 5f) });
                plain = new FitGauge(ctx, new FitGaugeOptions { MinScale = 1f, MaxScale = 2f, Near = Zone.Sphere(Vector3.zero, 5f) });
                drawn = new WaterVolume(ctx, new WaterVolumeOptions { Footprint = Zone.Cylinder(8f, 8f, 2f, 0f, 2f), Volume = 5f, Area = 12f });
                hidden = new WaterVolume(ctx, new WaterVolumeOptions { Footprint = Zone.Cylinder(-8f, 8f, 2f, 0f, 2f), Volume = 5f, Area = 12f, Visual = false });
                train = new Train(ctx, new TrainOptions { Center = new Vector2(0f, 20f), Radius = 6f, DeckY = 1f, Cars = 1, StationBearing = 135f });
                Prop block = ctx.AddProp(BasicToys.Block(1f), new Vector3(5f, 0.5f, 0f));
                pad = new RecallPad(ctx, new RecallPadOptions { Position = new Vector3(-5f, 0f, 0f), Radius = 0.9f, Prop = block });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Assert.IsNotNull(lit.Lamp);
            Assert.IsNotNull(lit.Lamp.Renderer, "the lamp is a renderer of the level");
            Assert.That(Vector3.Distance(new Vector3(2f, 2f, 5f), lit.Lamp.Renderer.transform.position), Is.LessThan(1e-4f));
            Assert.IsNull(dark.Lamp);
            Assert.IsNotNull(gauge.Lamp);
            Assert.IsNull(plain.Lamp);
            Assert.IsNotNull(drawn.Surface, "a volume that draws its surface shows it");
            Assert.AreEqual(drawn.SurfaceY, drawn.Surface.position.y, 1e-4f);
            Assert.IsNull(hidden.Surface, "one that draws nothing has none");
            Assert.AreEqual(135f, train.StationBearing);
            Assert.AreEqual(0.9f, pad.Radius);
            Assert.AreSame(bouncy, toyPad.Prop, "a bounce pad shows what it is made of: the toy");
            Assert.IsNull(toyPad.Surface);
            Assert.AreSame(trampoline, fixedPad.Surface, "or the piece of the level");
            Assert.IsNull(fixedPad.Prop);
        }
    }
}
