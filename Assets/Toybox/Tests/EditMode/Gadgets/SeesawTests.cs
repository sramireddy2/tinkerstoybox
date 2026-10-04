using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    /// <summary>The lever of Level 7: the geometry of LEVELS.md, real physics, and the bot on the bullseye.</summary>
    public class SeesawTests : SimTest
    {
        Seesaw seesaw;
        Prop pebble;

        // The cubby of Level 7: ruler over a marker, a backstop wall behind the short arm, the bookend to the right.
        void BuildCubby(LevelContext ctx, float pebbleScale = 0.6f)
        {
            TestHelpers.Floor(ctx);
            TestHelpers.Box(ctx, new Vector3(0f, 10f, 16f), new Vector3(20f, 20f, 1f));                 // backstop, face at z = 15.5
            TestHelpers.Box(ctx, new Vector3(6f, 5.5f, -0.5f), new Vector3(8f, 11f, 11f));              // bookend: x 2..10, z -6..5, top 11
            ctx.AddStatic(BasicToys.Cylinder(0.9f, 4f), new Vector3(0f, 0.9f, 10f), Quaternion.Euler(0f, 0f, 90f));   // the marker
            ctx.AddStatic(BasicToys.Cylinder(0.25f, 2.3f), new Vector3(-2.1f, 1.15f, 0.7f));                           // the bobbin
            seesaw = new Seesaw(ctx, new SeesawOptions
            {
                Pivot = new Vector3(0f, 1.8f, 10f), ArmADirection = Vector3.back, ArmA = 10f, ArmB = 5f, Width = 3f, Thickness = 0.2f,
                PlankBias = 1f, SeatArm = 9.3f,
            });
            pebble = ToyCatalog.Add(ctx, ToyId.Pebble, new Vector3(-2.1f, 2.3f + 0.5f * pebbleScale, 0.7f), pebbleScale, null, o =>
            {
                o.Tags = new[] { "weight" };
                o.FrozenUntilGrabbed = true;
            });
            ctx.SetSpawn(new Vector3(-5f, 0f, -3.5f), 0f);
        }

        [Test]
        public void TheRule_IsTheOneInTheLevelSpec()
        {
            Build(ctx => BuildCubby(ctx));
            Assert.AreEqual(10.4f, seesaw.RestAngle, 0.1f, "at rest the long tip is on the floor: asin(1.8 / 10)");
            Assert.AreEqual(-21.1f, seesaw.TippedAngle, 0.1f, "tipped, the short tip is: asin(1.8 / 5)");
            Assert.AreEqual(2.7f, seesaw.StrikeTravel, 0.01f, "the short tip travels 2.7");
            Assert.AreEqual(0.89f, seesaw.F(151f, 3f), 0.005f, "f = (151 - 2 * 3) / (151 + 4 * 3)");
            Assert.AreEqual(0.05f, seesaw.F(6.5f, 3f), 0.01f, "barely heavier than the rider: the floor of the clamp");
            Assert.AreEqual(19.1f, seesaw.LaunchSpeed(0.89f, 9.3f), 0.15f, "2 * sqrt(2 * 22 * 0.89 * 2.7) * 9.3 / 10");
            Assert.AreEqual(0.30f, seesaw.PointAt(9.3f, 0.2f, seesaw.RestAngle).y, 0.03f, "the bullseye's top is 0.30 above the floor");
            Assert.AreEqual(5.35f, seesaw.PointAt(9.3f, 0.2f, seesaw.TippedAngle).y, 0.05f, "and 5.35 when tipped");
            RunSeconds(1f);
            Assert.AreEqual(SeesawState.Rest, seesaw.State, "nothing on it: nothing happens");
            Assert.AreEqual(seesaw.RestAngle, seesaw.Angle, 1e-4f);
        }

        [Test]
        public void TheBot_OnTheBullseye_IsThrownOntoTheBookend()
        {
            Build(ctx => BuildCubby(ctx));
            float struckM = 0f, struckF = 0f, launchSpeed = 0f;
            seesaw.SeesawStruck += (m, f) =>
            {
                struckM = m;
                struckF = f;
            };
            seesaw.SeesawLaunched += (rider, speed) => launchSpeed = speed;
            var bot = new Bot(Game);
            float apex = 0f, seatHeight = 0f;
            Game.Context.OnUpdate(dt => apex = Mathf.Max(apex, Game.Player.Position.y));
            IEnumerator Script()
            {
                yield return bot.WalkTo(new Vector3(-2.1f, 0f, -0.7f), 0.15f);
                yield return bot.Grab(pebble);
                yield return bot.WalkTo(new Vector3(0f, 0f, -0.6f));
                yield return bot.WalkTo(new Vector3(0f, 0.3f, 0.7f), 0.15f);
                seatHeight = bot.Player.Position.y;
                Assert.IsTrue(bot.Player.Grounded, "standing on the ruler");
                yield return bot.DropAt(new Vector3(0f, 7.5f, 15.5f));
                yield return bot.Until(() => seesaw.Launched, 4f);
                yield return bot.WalkTo(new Vector3(5f, 11f, 0.7f), 0.5f, 5f);
                yield return bot.Until(() => bot.Player.Grounded && bot.Player.Position.y > 10.5f, 4f);
                yield return bot.WalkTo(new Vector3(7f, 11f, -2f));
            }
            BotRunner.Run(Game, Script(), 40f);

            Assert.AreEqual(0.3f, seatHeight, 0.08f, "the bot walked on over the tapered tip, without a jump");
            Assert.AreEqual(4.6f, pebble.Scale, 0.5f, "the boulder LEVELS predicts (window 3.3 .. 7)");
            Assert.AreEqual(1.571f * Mathf.Pow(pebble.Scale, 3f), struckM, 2f, "the strike load is the boulder");
            Assert.AreEqual(seesaw.F(struckM, 3f), struckF, 1e-3f);
            Assert.AreEqual(0.89f, struckF, 0.03f, "LEVELS appendix B: f = 0.89 +- 0.03");
            Assert.AreEqual(seesaw.LaunchSpeed(struckF, 9.3f), launchSpeed, 1f, "launched from the bullseye");
            Assert.GreaterOrEqual(apex, 12.5f, "the apex the level needs is 11.6; the rule promises 13.7 (was " + apex + ")");
            Assert.LessOrEqual(apex, 14.5f, "and no more than the rule gives");
            Assert.Greater(Game.Player.Position.x, 2f, "on the bookend");
            Assert.AreEqual(11f, Game.Player.Position.y, 0.05f);
            Assert.AreEqual(SeesawState.Tipped, seesaw.State, "the boulder keeps the short arm down");
        }

        [Test]
        public void ASmallBoulder_ThrowsToTheHeightTheRuleSays_AndNoHigher()
        {
            // Scale 2.4: M = 21.7, f = 0.466: thrown to about 9.7 - visibly short of the bookend (11).
            Build(ctx => BuildCubby(ctx, 2.4f));
            float f = 0f;
            seesaw.SeesawStruck += (m, value) => f = value;
            // The player on the bullseye, the boulder let go above the short arm.
            Game.Player.Teleport(seesaw.PointAt(9.3f, 0.21f, seesaw.RestAngle), 0f);
            RunSeconds(0.5f);
            Assert.IsTrue(Game.Player.Grounded);
            pebble.Unfreeze();
            pebble.SetPose(new Vector3(0f, 5.5f, 13.2f), Quaternion.identity);
            float apex = 0f;
            Game.Context.OnUpdate(dt => apex = Mathf.Max(apex, Game.Player.Position.y));
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => seesaw.Launched, 4f));
            RunSeconds(2f);
            Assert.AreEqual(0.466f, f, 0.02f);
            float speed = seesaw.LaunchSpeed(f, 9.3f);
            float expected = 5.35f + speed * speed / (2f * Game.Gravity);
            Assert.AreEqual(expected, apex, 0.6f, "the apex follows the closed form (speed " + speed + ")");
            Assert.Less(apex, 10.5f, "which is not enough for the bookend");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Game.Player.Grounded, 3f));
            // (Measured: 0.85 toward the pivot - the solver's parting push along the tilted plank. Left in the
            // plank's own swing the rider would land some nine units away, beyond the pivot.)
            Assert.Less(Mathf.Abs(Game.Player.Position.z - 0.7f), 1.2f, "thrown straight up: the rider comes down about where they stood (z " + Game.Player.Position.z + ")");
            Assert.Less(Mathf.Abs(Game.Player.Position.x), 0.3f);
        }

        [Test]
        public void TooLightAWeight_DoesNothing_AndThePlayerTipsItGently()
        {
            Build(ctx => BuildCubby(ctx, 1.2f));
            var log = new List<string>();
            seesaw.SeesawStruck += (m, f) => log.Add("struck " + m.ToString("0.0") + " " + f.ToString("0.00"));
            seesaw.SeesawReturned += () => log.Add("returned");

            // Mass 1.6 on the short arm against the plank's own bias (1 at the long tip): 1.6 * 5 < 1 * 10.
            pebble.SetScale(1f);
            pebble.Unfreeze();
            pebble.SetPose(new Vector3(0f, 3.4f, 13.2f), Quaternion.identity);
            RunSeconds(2f);
            Assert.AreEqual(SeesawState.Rest, seesaw.State, "1.6 on the short arm does not lift the plank");
            Assert.AreEqual(0, log.Count);
            Game.Context.RemoveProp(pebble);

            // The player walks out along the short arm: 3 * 5 against 1 * 10. The ruler tips under them, gently.
            Game.Player.Teleport(seesaw.PointAt(-3.5f, 0.21f, seesaw.RestAngle), 0f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => seesaw.State == SeesawState.Tipped, 4f));
            Assert.AreEqual("struck 3.0 0.14", log[0], "f = (3 - 1 * 2) / (3 + 1 * 4): the bias is what it lifts");
            Assert.IsTrue(Game.Player.Grounded, "the player rode it down");
            Assert.IsFalse(seesaw.Launched);

            // They step off: it swings back.
            Game.Player.Teleport(new Vector3(-6f, 0f, 3f), 0f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => seesaw.State == SeesawState.Rest, 3f));
            Assert.AreEqual("returned", log[log.Count - 1]);
            Assert.AreEqual(seesaw.RestAngle, seesaw.Angle, 1e-3f);
        }

        [Test]
        public void TheReturnSwing_WaitsWhileThePlayerIsUnderTheLongArm()
        {
            Build(ctx => BuildCubby(ctx));
            Game.Context.RemoveProp(pebble);
            // Tip it by standing on the short arm, then stand under the raised long arm.
            Game.Player.Teleport(seesaw.PointAt(-3.5f, 0.21f, seesaw.RestAngle), 0f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => seesaw.State == SeesawState.Tipped, 4f));
            Game.Player.Teleport(new Vector3(0f, 0f, 2.5f), 0f);
            RunSeconds(3f);
            Assert.AreEqual(SeesawState.Return, seesaw.State, "it wants to come back");
            Assert.Less(seesaw.Angle, seesaw.RestAngle - 1f, "but waits above the player's head");
            Assert.AreEqual(0f, Game.Player.Position.y, 0.02f, "who is not pressed into the floor");
            Assert.AreEqual(2.5f, Game.Player.Position.z, 0.05f, "nor pushed aside");
            Game.Player.Teleport(new Vector3(-6f, 0f, 3f), 0f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => seesaw.State == SeesawState.Rest, 3f), "once they step away it returns");
        }

        [Test]
        public void ARulerProp_AsThePlank_ShootsItsProjectile()
        {
            // Level 15 in small: a seated ruler becomes the lever; the ball lands on its short arm; the marble leaves the cap.
            Seesaw lever = null;
            Prop ruler = null, ball = null;
            Vector3 from = default, velocity = default;
            float f = 0f;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.AddStatic(BasicToys.Cylinder(0.4f, 2f), new Vector3(-5f, 0.4f, 18.25f), Quaternion.Euler(90f, 0f, 0f));
                // The ruler lies on the marker, a third of it on the short side (toward -X), the cap end (+Z of the toy) on the floor toward +X.
                float tilt = Mathf.Asin(0.8f / 2.667f) * Mathf.Rad2Deg;
                Quaternion pose = Quaternion.LookRotation(new Vector3(Mathf.Cos(tilt * Mathf.Deg2Rad), -Mathf.Sin(tilt * Mathf.Deg2Rad), 0f), new Vector3(Mathf.Sin(tilt * Mathf.Deg2Rad), Mathf.Cos(tilt * Mathf.Deg2Rad), 0f));
                // Its centre is 0.667 along the long arm from the pivot, 0.04 above the underside.
                Vector3 centre = new Vector3(-5f, 0.8f, 18.25f) + pose * new Vector3(0f, 0.04f, 0.667f);
                ruler = ToyCatalog.Add(ctx, ToyId.CatapultRuler, centre, pose, 1f);
                lever = new Seesaw(ctx, new SeesawOptions
                {
                    Pivot = new Vector3(-5f, 0.8f, 18.25f), ArmADirection = Vector3.right, ArmA = 2.667f, ArmB = 1.333f, Width = 0.6f, Thickness = 0.08f,
                    Plank = ruler, PlankBias = 0.06f, RiderBias = 0.81f, ProjectileArm = 2.667f - 0.35f, TipMargin = 1.5f,
                });
                lever.SeesawStruck += (m, value) => f = value;
                lever.ProjectileLaunched += (p, v) =>
                {
                    from = p;
                    velocity = v;
                };
                ball = ToyCatalog.Add(ctx, ToyId.BouncyBall, new Vector3(-5.85f, 4.5f, 18.25f), 3f);
                ctx.SetSpawn(new Vector3(0f, 0f, 10f), 0f);
            });
            Assert.IsTrue(ruler.Driven, "the seesaw drives the ruler");
            Assert.AreEqual(56.5f, ball.Mass, 0.5f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => lever.State == SeesawState.Tipped, 4f), "the ball lands on the short arm and the ruler flips");
            Assert.AreEqual(0.92f, f, 0.02f, "f = (56.5 - 2 * 0.81) / (56.5 + 4 * 0.81)");
            // The cap is a little short of the tip; a full-length arm would give 14.53 * sqrt(f).
            Assert.AreEqual(14.53f * Mathf.Sqrt(f) * (2.667f - 0.35f) / 2.667f, velocity.y, 0.2f);
            Assert.AreEqual(0f, velocity.x, 1e-4f, "straight up");
            Assert.Greater(from.x, -3.5f, "from the cap end");
            Assert.AreEqual(2.1f, from.y, 0.35f, "which is up in the air now");
        }
    }
}
