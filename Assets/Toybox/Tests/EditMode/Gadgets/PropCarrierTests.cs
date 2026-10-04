using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    public class PropCarrierTests : TrainSetTest
    {
        [Test]
        public void TheBot_DropsThePlankOnTheMovingTrain_AndWalksItToTheLighthouse()
        {
            Build(ctx => BuildSet(ctx), -12f);
            int captured = -1;
            Carrier.PropCaptured += (prop, bed) => captured = bed;
            var bot = new Bot(Game);
            var aim = new Vector3(0f, 0.9f, 18f);
            var tower = new Vector3(0f, -1f, 20f);
            float releasedScale = 0f;
            IEnumerator Script()
            {
                yield return bot.WalkTo(new Vector3(2.5f, 0f, -2.4f), 0.1f);
                yield return bot.Grab(Plank);
                yield return bot.WalkTo(new Vector3(0f, 0f, 2.9f), 0.15f);
                yield return bot.LookAt(aim);
                // Wagon 4 must be at the station when the plank lands, 10 degrees after release.
                yield return bot.Until(() => Mathf.Abs(Mathf.DeltaAngle(Train.CarBearing(4), -10f)) < 1f, 20f);
                yield return bot.DropAt(aim);
                releasedScale = Plank.Scale;
                yield return bot.Until(() => Carrier.Captured, 2f);
                // One lap later, lead the plank by the walk-and-jump time.
                yield return bot.Until(() => Mathf.Abs(Mathf.DeltaAngle(Train.CarBearing(captured + 1), -24f)) < 1f, 20f);
                yield return bot.WalkTo(new Vector3(0f, 0f, 3.15f), 0.1f);
                yield return bot.Jump();
                yield return bot.WalkTo(new Vector3(0f, -0.5f, 6.9f), 0.4f, 3f);
                yield return bot.Until(() => bot.Player.GroundProp == Plank, 2f);
                yield return bot.WalkTo(tower, 2.6f, 12f);
                yield return bot.Wait(0.5f);
            }
            BotRunner.Run(Game, Script(), 90f);

            Assert.AreEqual(4.03f, releasedScale, 0.35f, "the tower sets the size: LEVELS says 4.03 (window 3.7 .. 4.4)");
            Assert.IsTrue(Carrier.IsCarrying(Plank), "the plank is still on its wagon");
            Assert.IsTrue(Plank.Driven);
            Assert.GreaterOrEqual(captured, 0);
            Assert.Less(RadiusOf(Game.Player.Position), 3.6f, "the bot walked in along the turning plank to the doorstep");
            Assert.Greater(Game.Player.Position.y, -1.2f, "and did not fall");
            Assert.IsTrue(Game.Player.Grounded);
        }

        [Test]
        public void AFlatPlankOnABed_IsGripped_EasedFlat_AndMovesRigidlyWithItsWagon()
        {
            Build(ctx => BuildSet(ctx), -12f);
            int bed = -1;
            Carrier.PropCaptured += (prop, index) => bed = index;
            // Radially across wagon 3, tower end at r = 2.2, a little tilted and a little above the deck.
            Plank.SetScale(4f);
            Mover car = Train.CarMover(3);
            Vector3 outward = (car.Position - new Vector3(0f, car.Position.y, 20f)).normalized;
            Vector3 centre = new Vector3(0f, -0.6f, 20f) + outward * (2.2f + 6f);
            Plank.SetPose(centre, Quaternion.LookRotation(outward) * Quaternion.Euler(4f, 0f, 3f));
            Run(3);
            Assert.IsTrue(Carrier.Captured, "gripped while it is still falling onto the deck");
            Assert.AreEqual(2, bed, "by the wagon it lies on");
            Assert.AreEqual(2, Carrier.CarIndex);
            RunSeconds(0.2f);
            Assert.AreEqual(-1f + 0.06f * 4f, Plank.Center.y, 2e-3f, "its underside on the deck");
            Assert.Greater((Plank.Rotation * Vector3.up).y, 0.9999f, "flat");

            // A second later it has turned with the train: still radial, same distance from the wagon.
            Vector3 before = Quaternion.Inverse(car.Rotation) * (Plank.Center - car.Position);
            RunSeconds(4f);
            Vector3 after = Quaternion.Inverse(car.Rotation) * (Plank.Center - car.Position);
            Assert.Less(Vector3.Distance(before, after), 2e-3f, "rigid on its wagon");
            Vector3 along = Plank.Rotation * Vector3.forward;
            Vector3 radial = (Plank.Center - new Vector3(0f, Plank.Center.y, 20f)).normalized;
            Assert.Greater(Mathf.Abs(Vector3.Dot(along, radial)), 0.999f, "a spoke that keeps pointing at the lighthouse");
            Assert.AreEqual(Train.DeckSpeed * RadiusOf(Plank.Center) / 14f, Plank.Velocity.magnitude, 0.05f, "moving with the train");
        }

        [Test]
        public void APlankThatWouldSweepTheStationOrCutTheTower_IsNotGripped()
        {
            Build(ctx => BuildSet(ctx), -12f);
            Plank.SetScale(4.4f);
            Mover car = Train.CarMover(3);
            Vector3 outward = (car.Position - new Vector3(0f, car.Position.y, 20f)).normalized;
            // Centred on the deck: 13.2 long, from r = 7.4 out to 20.6.
            Plank.SetPose(new Vector3(0f, -0.7f, 20f) + outward * 14f, Quaternion.LookRotation(outward));
            RunSeconds(0.5f);
            Assert.IsFalse(Carrier.Captured, "its outer end would sweep the station (r > 16.5)");

            // Standing on edge on a bed: not flat.
            Plank.SetScale(1f);
            Plank.SetPose(Train.CarMover(5).Position + Vector3.up * 0.4f, Quaternion.Euler(0f, 0f, 90f));
            Run(3);
            Assert.IsFalse(Carrier.Captured, "a plank on its edge is not lying on the bed");

            // The wrong tag.
            Prop crate = Game.Context.AddProp(BasicToys.Block(new Vector3(1f, 0.2f, 1f)), Train.CarMover(6).Position + Vector3.up * 0.2f);
            RunSeconds(0.3f);
            Assert.IsFalse(crate.Driven, "only planks stick to the velcro");
        }

        [Test]
        public void AGrab_TakesThePlankOffItsWagon()
        {
            Build(ctx => BuildSet(ctx, -30f), -12f);
            var released = new List<Prop>();
            Carrier.PropReleased += released.Add;
            Plank.SetScale(3f);
            Mover car = Train.CarMover(2);
            Plank.SetPose(car.Position + Vector3.up * 0.3f, car.Rotation);
            Run(5);
            Assert.IsTrue(Carrier.IsCarrying(Plank));

            Game.Player.Teleport(new Vector3(0f, 0f, 2.5f), 0f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () =>
            {
                LookAt(Plank.Center);
                return Game.Grabber.FindTarget() == Plank;
            }, 8f), "the plank comes past the station");
            Click();
            Assert.AreSame(Plank, Game.Grabber.Held);
            Run(2);
            Assert.IsFalse(Carrier.Captured, "a grab releases it from the wagon");
            CollectionAssert.AreEqual(new[] { Plank }, released);
            Assert.IsFalse(Plank.Driven);
        }
    }
}
