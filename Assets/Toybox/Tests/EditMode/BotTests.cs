using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests
{
    public class BotTests : SimTest
    {
        Bot bot;

        void BuildWithBot(Action<LevelContext> build, float killY = -30f)
        {
            Game = Game.Create();
            Game.LoadLevel(new AdHocLevel(build, killY));
            bot = new Bot(Game);
            Assert.AreSame(bot, Game.Input, "the bot installs itself as the input source");
        }

        // Two legs and a bar across: the middle of its bounds is empty space.
        static BoxCollider AddBox(GameObject root, Vector3 center, Vector3 size, Quaternion? rotation = null)
        {
            var child = new GameObject("Part");
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = center;
            child.transform.localRotation = rotation ?? Quaternion.identity;
            BoxCollider box = child.AddComponent<BoxCollider>();
            box.size = size;
            return box;
        }

        static GameObject Arch()
        {
            var root = new GameObject("Arch");
            AddBox(root, new Vector3(-1f, 0.75f, 0f), new Vector3(0.2f, 1.5f, 0.2f));
            AddBox(root, new Vector3(1f, 0.75f, 0f), new Vector3(0.2f, 1.5f, 0.2f));
            AddBox(root, new Vector3(0f, 1.6f, 0f), new Vector3(2.2f, 0.2f, 0.2f));
            return root;
        }


        [Test]
        public void WalkToReachesTargets()
        {
            BuildWithBot(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            var targets = new[]
            {
                new Vector3(0f, 0f, 6f), new Vector3(7f, 0f, 6f), new Vector3(-5f, 0f, -4f), new Vector3(-5f, 0f, -3.5f),
            };
            foreach (Vector3 target in targets)
            {
                BotRunner.Run(Game, bot.WalkTo(target), 15f);
                Vector3 offset = Game.Player.Position - target;
                offset.y = 0f;
                Assert.LessOrEqual(offset.magnitude, 0.3f, "did not arrive at " + target);
            }

            // A tight tolerance and a sprint.
            BotRunner.Run(Game, bot.WalkTo(new Vector3(10f, 0f, 10f), 0.05f, 10f, true), 15f);
            Vector3 miss = Game.Player.Position - new Vector3(10f, 0f, 10f);
            miss.y = 0f;
            Assert.LessOrEqual(miss.magnitude, 0.05f);
        }

        [Test]
        public void LookAtTurnsAtAHumanRate()
        {
            BuildWithBot(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            int start = Game.TickCount;
            var target = new Vector3(-6f, 4f, -6f);
            BotRunner.Run(Game, bot.LookAt(target), 5f);

            Vector3 wanted = (target - Game.Player.Eye).normalized;
            Assert.Less(Vector3.Angle(wanted, Game.Player.Forward), 0.1f);
            // 135 degrees of yaw at 360 degrees per second cannot be done in under 0.375 s.
            int ticks = Game.TickCount - start;
            Assert.GreaterOrEqual(ticks, 22, "the bot snapped its view around faster than a person could");
            Assert.LessOrEqual(ticks, 40);
        }

        [Test]
        public void GrabAndDropAtWorkEndToEnd()
        {
            Prop block = null;
            BuildWithBot(ctx =>
            {
                TestHelpers.Room(ctx, 20f);
                block = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(2f, 0.25f, 4f), new PropOptions { Name = "crate" });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            PropHoldEvent? dropped = null;
            Vector3 droppedAt = Vector3.zero;
            Game.Events.PropDropped += e =>
            {
                dropped = e;
                droppedAt = e.Prop.Center;
            };

            var target = new Vector3(-4f, 0f, 14f);
            BotRunner.Run(Game, Script(), 20f);

            IEnumerator Script()
            {
                yield return bot.Wait(0.2f);
                yield return bot.Grab(block);
                Assert.AreSame(block, Game.Grabber.Held);
                yield return bot.RotateHeld(3, 1);
                yield return bot.DropAt(target);
                yield return bot.Wait(2f);
            }

            Assert.IsNull(Game.Grabber.Held);
            Assert.IsTrue(dropped.HasValue, "PropDropped never fired");
            Assert.Greater(block.Scale, 1.5f, "dropping it far away should have made it bigger");
            Assert.Greater(Quaternion.Angle(Quaternion.identity, block.Rotation), 30f, "RotateHeld should have turned it");
            Assert.AreEqual(dropped.Value.DropDistance / dropped.Value.GrabDistance, block.Scale, block.Scale * 0.05f);
            // It was released on the line of sight to the target, where it first touched the floor.
            Vector3 eye = Game.Player.Eye;
            Vector3 ray = (target - eye).normalized;
            float along = Vector3.Dot(droppedAt - eye, ray);
            Assert.Less(Vector3.Distance(droppedAt, eye + ray * along), 0.02f, "the crate was not released on the view ray");
            Assert.AreEqual(dropped.Value.DropDistance, along, 0.02f);
            Assert.Less(along, Vector3.Distance(eye, target));
            Assert.Less(Vector3.Distance(droppedAt, block.Center), 2f, "the crate did not stay near where it was put down");
            Assert.Less(block.Body.linearVelocity.magnitude, 0.5f);
        }

        [Test]
        public void GrabFailsLoudlyWhenThePropIsHidden()
        {
            Prop block = null;
            BuildWithBot(ctx =>
            {
                TestHelpers.Floor(ctx);
                TestHelpers.Box(ctx, new Vector3(0f, 2f, 4f), new Vector3(8f, 4f, 0.5f));
                block = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 7f), new PropOptions { Name = "crate" });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            var e = Assert.Throws<BotException>(() => BotRunner.Run(Game, bot.Grab(block), 10f));
            StringAssert.Contains("Could not grab crate", e.Message);
            StringAssert.Contains("bot at", e.Message);
        }

        [Test]
        public void BlockedWalkToTimesOutWithAUsefulMessage()
        {
            BuildWithBot(ctx =>
            {
                TestHelpers.Floor(ctx);
                TestHelpers.Box(ctx, new Vector3(0f, 2f, 4f), new Vector3(30f, 4f, 1f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            var e = Assert.Throws<BotException>(() => BotRunner.Run(Game, bot.WalkTo(new Vector3(0f, 0f, 9f), 0.3f, 2f), 10f));
            StringAssert.Contains("WalkTo timed out after 2 s", e.Message);
            StringAssert.Contains("bot at (0.00, 0.00, 3.2", e.Message, "the message should say where the bot got stuck");
            StringAssert.Contains("target (0.00, 0.00, 9.00)", e.Message);
            // Two seconds of walking, not a tick more.
            Assert.AreEqual(2f, Game.Time, 0.05f);
        }

        [Test]
        public void TheBotOnlyActsThroughInputFrames()
        {
            BuildWithBot(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            // Advancing a script without ticking the game must not move anything.
            var runner = new BotRunner(bot.WalkTo(new Vector3(0f, 0f, 5f)));
            Vector3 before = Game.Player.Position;
            float yaw = Game.Player.Yaw;
            for (int i = 0; i < 10; i++) Assert.IsTrue(runner.Advance());
            Assert.AreEqual(before, Game.Player.Position);
            Assert.AreEqual(yaw, Game.Player.Yaw);

            // Paced by the game loop instead: the runner is the input source and Step drives it.
            BotRunner paced = BotRunner.Attach(Game, bot, bot.WalkTo(new Vector3(0f, 0f, 5f)));
            for (int i = 0; i < 200 && !paced.Finished; i++) Game.Step(Sim.Dt);
            Assert.IsTrue(paced.Finished);
            Assert.AreEqual(5f, Game.Player.Position.z, 0.35f);
        }

        // ------------------------------------------------------------------------------------------
        // Aiming
        // ------------------------------------------------------------------------------------------

        static IEnumerable<TestCaseData> LookAtCases()
        {
            // start yaw, start pitch, target relative to the eye, name
            yield return new TestCaseData(0f, 0f, new Vector3(0f, 10f, 0f)).SetName("LookAt_StraightUp");
            yield return new TestCaseData(37f, 10f, new Vector3(1e-4f, 10f, -1e-4f)).SetName("LookAt_AlmostStraightUp");
            yield return new TestCaseData(-120f, 0f, new Vector3(0f, -1.55f, 0f)).SetName("LookAt_OwnFeet");
            yield return new TestCaseData(0f, 0f, new Vector3(0f, 0f, -10f)).SetName("LookAt_DirectlyBehind");
            yield return new TestCaseData(170f, 0f, new Vector3(Mathf.Sin(-170f * Mathf.Deg2Rad), 0f, Mathf.Cos(-170f * Mathf.Deg2Rad)) * 10f).SetName("LookAt_AcrossTheWrap");
            yield return new TestCaseData(-179.99f, -89f, new Vector3(Mathf.Sin(179.99f * Mathf.Deg2Rad), 5f, Mathf.Cos(179.99f * Mathf.Deg2Rad)) * 10f).SetName("LookAt_AcrossTheWrapTheOtherWay");
            yield return new TestCaseData(90f, 89f, new Vector3(-3f, -40f, 0.5f)).SetName("LookAt_FromZenithToNadir");
        }

        [TestCaseSource(nameof(LookAtCases))]
        public void LookAtConverges(float startYaw, float startPitch, Vector3 offset)
        {
            BuildWithBot(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            TestHelpers.Run(Game, 20);
            Game.Player.Teleport(Game.Player.Position, startYaw, startPitch);
            Vector3 target = Game.Player.Eye + offset;

            float wantedYaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            float wantedPitch = Mathf.Clamp(Mathf.Atan2(offset.y, new Vector2(offset.x, offset.z).magnitude) * Mathf.Rad2Deg, -89f, 89f);
            float yawTravel = Mathf.Abs(Mathf.DeltaAngle(startYaw, wantedYaw));
            float pitchTravel = Mathf.Abs(wantedPitch - startPitch);
            int needed = Mathf.CeilToInt(Mathf.Max(yawTravel, pitchTravel) / (Bot.TurnRate * Sim.Dt));

            int start = Game.TickCount;
            Assert.DoesNotThrow(() => BotRunner.Run(Game, bot.LookAt(target), 6f));
            int ticks = Game.TickCount - start;

            Vector3 wanted = Quaternion.Euler(-wantedPitch, wantedYaw, 0f) * Vector3.forward;
            Assert.Less(Vector3.Angle(wanted, Game.Player.Forward), 0.1f, "the view does not point at the target");
            Assert.LessOrEqual(ticks, needed + 2, "the bot took a detour (" + ticks + " ticks for " + yawTravel + " deg yaw, " + pitchTravel + " deg pitch)");
        }

        [Test]
        public void GrabTakesAPropThatIsMoving()
        {
            Prop crate = null;
            BuildWithBot(ctx =>
            {
                TestHelpers.Floor(ctx);
                // A crate riding a conveyor-like platform that travels left to right at a steady 1 u/s,
                // eight units in front of the bot: it crosses the screen at about 7 degrees per second.
                Mover platform = ctx.AddKinematic(BasicToys.Slab(new Vector3(3f, 0.4f, 3f)), new Vector3(-6f, 0.2f, 8f));
                crate = ctx.AddProp(BasicToys.Block(0.6f), new Vector3(-6f, 0.7f, 8f), new PropOptions { Name = "crate", Friction = 1f });
                ctx.OnUpdate(dt => platform.MoveTo(new Vector3(-6f + Mathf.Min(12f, ctx.Game.LevelTicks * Sim.Dt), 0.2f, 8f)));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            TestHelpers.Run(Game, 60);
            Assert.AreEqual(0.7f, crate.Center.y, 0.05f, "the crate rides the platform");
            Assert.AreEqual(1f, crate.Velocity.x, 0.1f);
            // By hand this is an easy click. The bot has to lead its aim, or it trails the crate for ever.
            Assert.DoesNotThrow(() => BotRunner.Run(Game, bot.Grab(crate), 20f));
            Assert.AreSame(crate, Game.Grabber.Held);
            BotRunner.Run(Game, bot.Drop(), 5f);
            TestHelpers.RunSeconds(Game, 0.5f);

            // LookAt(Prop) follows it: lined up on its center although it keeps moving.
            Assert.AreEqual(1f, crate.Velocity.x, 0.2f, "test setup: the crate is riding again");
            Assert.DoesNotThrow(() => BotRunner.Run(Game, bot.LookAt(crate), 20f));
            Assert.Less(Vector3.Angle(Game.Player.Forward, crate.Center - Game.Player.Eye), 0.1f);
        }

        [Test]
        public void LookAtLinesUpWhileTheBotRidesAMover()
        {
            Mover platform = null;
            BuildWithBot(ctx =>
            {
                TestHelpers.Floor(ctx);
                // The bot stands on a platform that carries it sideways at a steady 2 u/s for 12 seconds.
                platform = ctx.AddKinematic(BasicToys.Slab(new Vector3(3f, 0.4f, 3f)), new Vector3(-12f, 0.2f, 0f));
                Mover m = platform;
                ctx.OnUpdate(dt => m.MoveTo(new Vector3(-12f + Mathf.Min(24f, 2f * ctx.Game.LevelTicks * Sim.Dt), 0.2f, 0f)));
                ctx.SetSpawn(new Vector3(-12f, 0.4f, 0f), 0f);
            });
            TestHelpers.Run(Game, 60);
            Assert.AreEqual(2f, Game.Player.Velocity.x, 0.1f, "the bot rides the platform");
            Vector3 riding = Game.Player.Position - platform.Position;
            // A fixed point on the floor ten units ahead of the track: its direction changes by 0.2 degrees a tick.
            var target = new Vector3(0f, 0f, 10f);
            Assert.DoesNotThrow(() => BotRunner.Run(Game, bot.LookAt(target), 20f));
            Assert.AreEqual(2f, Game.Player.Velocity.x, 0.1f, "still moving when the view lined up");
            Assert.Less(Vector3.Angle(Game.Player.Forward, target - Game.Player.Eye), 0.1f, "the view does not point at the target");
            Assert.Less(Vector3.Distance(riding, Game.Player.Position - platform.Position), 0.05f, "still riding");
        }

        // ------------------------------------------------------------------------------------------
        // Grabbing, turning and dropping
        // ------------------------------------------------------------------------------------------

        [TestCase(0.1f, 40f)]
        [TestCase(0.05f, 100f)]
        [TestCase(0.02f, 140f)]
        public void GrabTakesAFarTinyProp(float size, float distance)
        {
            Prop speck = null;
            BuildWithBot(ctx =>
            {
                ctx.AddStatic(BasicToys.Slab(new Vector3(20f, 1f, 300f)), new Vector3(0f, -0.5f, 0f));
                speck = ctx.AddProp(BasicToys.Block(size), new Vector3(3f, size * 0.5f, distance), new PropOptions { Name = "speck" });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            TestHelpers.Run(Game, 30);
            Assert.DoesNotThrow(() => BotRunner.Run(Game, bot.Grab(speck), 10f));
            Assert.AreSame(speck, Game.Grabber.Held);
        }

        [Test]
        public void GrabTakesAPropWhoseCentreIsHidden()
        {
            Prop plank = null;
            BuildWithBot(ctx =>
            {
                TestHelpers.Floor(ctx);
                // A plank lying across the view, its middle hidden behind a pillar, both ends in plain sight.
                plank = ctx.AddProp(BasicToys.Block(new Vector3(6f, 0.5f, 0.5f)), new Vector3(0f, 0.25f, 8f), new PropOptions { Name = "plank" });
                TestHelpers.Box(ctx, new Vector3(0f, 1.5f, 6f), new Vector3(1.5f, 3f, 0.5f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            TestHelpers.Run(Game, 20);

            // A person clicks on the end that sticks out. Grab finds such a spot by itself ...
            Assert.DoesNotThrow(() => BotRunner.Run(Game, bot.Grab(plank), 10f));
            Assert.AreSame(plank, Game.Grabber.Held);
            BotRunner.Run(Game, bot.DropAt(new Vector3(0f, 0f, 3f)), 10f);
            TestHelpers.RunSeconds(Game, 1f);

            // ... and GrabAt lets the script say where to take hold.
            Vector3 end = plank.Transform.TransformPoint(new Vector3(-2.5f, 0f, 0f));
            Assert.DoesNotThrow(() => BotRunner.Run(Game, bot.GrabAt(plank, end), 10f));
            Assert.AreSame(plank, Game.Grabber.Held);
            Assert.Less(Vector3.Angle(Game.Player.Forward, end - Game.Player.Eye), 2f, "GrabAt aims at the point it was given");
        }

        [Test]
        public void ClickPressesGrabOnceWhateverTheViewPointsAt()
        {
            Prop crate = null;
            BuildWithBot(ctx =>
            {
                TestHelpers.Room(ctx, 12f);
                crate = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 4f), new PropOptions { Name = "crate" });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            TestHelpers.Run(Game, 20);
            int start = Game.TickCount;
            BotRunner.Run(Game, bot.Click(), 5f);
            Assert.AreEqual(1, Game.TickCount - start, "a click is one tick");
            Assert.IsNull(Game.Grabber.Held, "the view points at the wall");

            IEnumerator Script()
            {
                yield return bot.LookAt(crate);
                yield return bot.Click();
                Assert.AreSame(crate, Game.Grabber.Held);
                yield return bot.LookAt(new Vector3(3f, 0f, 8f));
                yield return bot.Click();
            }
            BotRunner.Run(Game, Script(), 10f);
            Assert.IsNull(Game.Grabber.Held, "the second click let go");
            Assert.Greater(crate.Center.x, 1f, "the crate was put down where the view pointed");
        }

        [Test]
        public void AForgottenYieldReturnIsReported()
        {
            BuildWithBot(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            IEnumerator Script()
            {
                yield return bot.WalkTo(new Vector3(0f, 0f, 2f));
                bot.Jump();     // does nothing: the command is created and thrown away
                yield return bot.Wait(0.2f);
            }
            var e = Assert.Throws<BotException>(() => BotRunner.Run(Game, Script(), 10f));
            StringAssert.Contains("yield return", e.Message);

            // A command that is made first and yielded later is fine.
            IEnumerator Proper()
            {
                IEnumerator wait = bot.Wait(0.1f);
                yield return bot.WalkTo(new Vector3(0f, 0f, 3f));
                yield return wait;
            }
            Assert.DoesNotThrow(() => BotRunner.Run(Game, Proper(), 10f));
        }

        /// <summary>
        /// Prop.Center of an arch, a hoop or a table is empty space: a view ray at it goes through the hole.
        /// Grab aims at a part of the prop instead (here a leg), as a person would.
        /// </summary>
        [Test]
        public void GrabTakesAHollowProp()
        {
            Prop arch = null;
            BuildWithBot(ctx =>
            {
                TestHelpers.Room(ctx, 20f);
                arch = ctx.AddProp(Arch(), new Vector3(0f, 0f, 5f));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            TestHelpers.Run(Game, 30);
            Assert.Less(arch.Body.linearVelocity.magnitude, 0.01f, "test setup: the arch stands");
            Assert.IsFalse(Game.PhysicsScene.Raycast(Game.Player.Eye, (arch.Center - Game.Player.Eye).normalized, out RaycastHit through, 30f, Layers.SolidMask)
                           && PropRef.Of(through.collider) == arch, "test setup: the middle of the arch is a hole");
            Assert.DoesNotThrow(() => BotRunner.Run(Game, bot.Grab(arch), 10f), "Bot.Grab cannot take a prop whose center is not inside one of its colliders");
            Assert.AreSame(arch, Game.Grabber.Held);
        }

        [Test]
        public void RotateHeldWithNegativePitchStepsTurnsBack()
        {
            Prop wedge = null;
            BuildWithBot(ctx =>
            {
                TestHelpers.Room(ctx, 20f);
                wedge = ctx.AddProp(BasicToys.Wedge(1f, 0.6f, 0.8f), new Vector3(0f, 0.3f, 5f), new PropOptions { Name = "wedge" });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            TestHelpers.Run(Game, 20);
            Quaternion original = Quaternion.identity;
            IEnumerator Script()
            {
                yield return bot.Grab(wedge);
                yield return bot.Wait(0.1f);
                original = wedge.Rotation;
                yield return bot.RotateHeld(0, 1);
                yield return bot.RotateHeld(0, -1);
                yield return bot.Wait(0.1f);
            }
            BotRunner.Run(Game, Script(), 10f);
            Assert.Less(Quaternion.Angle(original, wedge.Rotation), 1f,
                "one pitch step forward and one back should cancel (yaw steps do: RotateHeld(1) then RotateHeld(-1))");
        }

        [Test]
        public void DropAtPutsThePropOnTheViewRayThroughThePoint_EvenRightAfterWalking()
        {
            Prop block = null;
            BuildWithBot(ctx =>
            {
                TestHelpers.Room(ctx, 30f);
                block = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(2f, 0.25f, 4f), new PropOptions { Name = "crate" });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Vector3 droppedAt = Vector3.zero, eyeAtDrop = Vector3.zero;
            Game.Events.PropDropped += e =>
            {
                droppedAt = e.Prop.Center;
                eyeAtDrop = Game.Player.Eye;
            };
            var target = new Vector3(-6f, 0f, 20f);
            IEnumerator Script()
            {
                yield return bot.Grab(block);
                yield return bot.WalkTo(new Vector3(3f, 0f, -6f), 0.3f, 10f, true);
                yield return bot.DropAt(target);
            }
            BotRunner.Run(Game, Script(), 20f);
            Vector3 ray = (target - eyeAtDrop).normalized;
            float along = Vector3.Dot(droppedAt - eyeAtDrop, ray);
            float off = Vector3.Distance(droppedAt, eyeAtDrop + ray * along);
            // 0.03 degrees of aim tolerance at ~27 units is 1.4 cm.
            Assert.Less(off, 0.05f, "the prop was released " + off + " off the line of sight to the target");
        }

        // ------------------------------------------------------------------------------------------
        // Scripts that go wrong
        // ------------------------------------------------------------------------------------------

        [Test]
        public void WalkToIntoAPitFailsWithABotExceptionInsteadOfHanging()
        {
            BuildWithBot(ctx =>
            {
                ctx.AddStatic(BasicToys.Slab(new Vector3(6f, 1f, 6f)), new Vector3(0f, -0.5f, 0f));
                ctx.AddStatic(BasicToys.Slab(new Vector3(6f, 1f, 6f)), new Vector3(0f, -0.5f, 30f));
                ctx.SetSpawn(Vector3.zero, 0f);
            }, -5f);
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            var e2 = Assert.Throws<BotException>(() => BotRunner.Run(Game, bot.WalkTo(new Vector3(0f, 0f, 30f), 0.3f, 4f), 20f));
            StringAssert.Contains("WalkTo timed out", e2.Message);
            Assert.Greater(respawns, 0);
            Assert.AreEqual(4f, Game.Time, 0.05f);
        }

        [Test]
        public void RunThrowsTimeoutWhenTheScriptNeverEnds()
        {
            BuildWithBot(ctx => TestHelpers.Floor(ctx));
            IEnumerator Forever()
            {
                while (true) yield return null;
            }
            Assert.Throws<TimeoutException>(() => BotRunner.Run(Game, Forever(), 1f));
            Assert.AreEqual(1f, Game.Time, 0.02f);
        }

        [Test]
        public void AScriptLoopOfCommandsThatSpendNoTickFails()
        {
            BuildWithBot(ctx =>
            {
                TestHelpers.Floor(ctx);
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            TestHelpers.Run(Game, 10);
            var target = new Vector3(0f, 1.55f, 10f);
            const int GiveUp = 300000;
            int spins = 0;
            // "Keep looking at the thing until the door opens." LookAt returns without a tick once the view is lined
            // up, so this loop never yields to the game, and Run's timeout (counted in ticks) can never fire.
            // The counter only exists so that this test ends even if the runner does not stop the loop.
            IEnumerator Script()
            {
                while (!Game.LevelCompleted && spins < GiveUp)
                {
                    spins++;
                    yield return bot.LookAt(target);
                }
            }
            int before = Game.TickCount;
            var e = Assert.Throws<BotException>(() => BotRunner.Run(Game, Script(), 2f));
            StringAssert.Contains("without a tick passing", e.Message);
            Assert.Less(spins, BotRunner.MaxStepsWithoutATick, "BotRunner ran " + spins + " commands in a row without a single tick (" +
                                                               (Game.TickCount - before) + " ticks passed)");
        }

        [Test]
        public void AFailedCommandEndsAnAttachedScript()
        {
            Prop block = null;
            BuildWithBot(ctx =>
            {
                TestHelpers.Floor(ctx);
                TestHelpers.Box(ctx, new Vector3(0f, 2f, 4f), new Vector3(8f, 4f, 0.5f));
                block = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 7f), new PropOptions { Name = "crate" });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            bool continued = false;
            IEnumerator Script()
            {
                yield return bot.Grab(block);     // hidden behind the wall: throws BotException
                continued = true;                 // must never be reached
                yield return bot.Wait(1f);
            }

            // The way the real game loop paces a script (autoplay).
            BotRunner runner = BotRunner.Attach(Game, bot, Script());
            bool failed = false;
            for (int i = 0; i < 200; i++)
            {
                try
                {
                    Game.Step(Sim.Dt);
                }
                catch (BotException)
                {
                    failed = true;
                }
            }
            Assert.IsTrue(failed, "Grab of a hidden prop should have failed");
            Assert.IsFalse(continued, "after Grab threw, the script went on with the code after it");
            Assert.IsTrue(runner.Finished);
            Assert.IsTrue(runner.Failed);
            Assert.IsInstanceOf<BotException>(runner.Error);
        }

        sealed class CheatingLevel : LevelDefinition
        {
            public static readonly Vector3 ExitPosition = new Vector3(0f, 0f, 60f);

            public override void Build(LevelContext ctx)
            {
                // Two islands 50 units apart and nothing to cross with: the level is impossible.
                ctx.AddStatic(BasicToys.Slab(new Vector3(8f, 1f, 8f)), new Vector3(0f, -0.5f, 0f));
                ctx.AddStatic(BasicToys.Slab(new Vector3(8f, 1f, 8f)), new Vector3(0f, -0.5f, 60f));
                ctx.AddExit(ExitPosition + Vector3.up * 1.5f, new Vector3(3f, 3f, 3f));
                ctx.SetSpawn(Vector3.zero, 0f);
            }

            public override IEnumerator Solve(Bot bot)
            {
                yield return bot.Wait(0.1f);
                bot.Player.Teleport(ExitPosition);
                yield return bot.Wait(0.1f);
            }
        }

        [Test]
        public void ASolveScriptThatTouchesTheSimulationIsRejected()
        {
            // docs/ARCHITECTURE.md: "it cannot teleport or cheat, so a passing solver proves a human can do it".
            Game = Game.Create();
            Game.LoadLevel(new CheatingLevel());
            var e = Assert.Throws<BotException>(() => TestHelpers.PlayLevel(Game, 10f),
                "PlayLevel accepted a Solve() that called bot.Player.Teleport on an impossible level");
            StringAssert.Contains("changed the player directly", e.Message);
            Assert.IsFalse(Game.LevelCompleted);
            Game.Dispose();

            // The other shortcuts: moving or scaling a prop, completing the level, unlocking an exit.
            Prop crate = null;
            Exit exit = null;
            BuildWithBot(ctx =>
            {
                TestHelpers.Floor(ctx);
                crate = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 4f), new PropOptions { Name = "crate" });
                exit = ctx.AddExit(new Vector3(0f, 1.5f, 30f), new Vector3(2f, 3f, 2f)).Lock();
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            TestHelpers.Run(Game, 20);
            IEnumerator After(System.Action cheat)
            {
                yield return bot.Wait(0.1f);
                cheat();
                yield return bot.Wait(0.1f);
            }
            StringAssert.Contains("a prop", Assert.Throws<BotException>(() => BotRunner.Run(Game, After(() => crate.SetPose(new Vector3(3f, 0.25f, 3f), Quaternion.identity)), 5f)).Message);
            StringAssert.Contains("a prop", Assert.Throws<BotException>(() => BotRunner.Run(Game, After(() => crate.SetScale(3f)), 5f)).Message);
            StringAssert.Contains("the state of the level", Assert.Throws<BotException>(() => BotRunner.Run(Game, After(() => exit.Unlock()), 5f)).Message);
            StringAssert.Contains("the state of the level", Assert.Throws<BotException>(() => BotRunner.Run(Game, After(() => Game.CompleteLevel()), 5f)).Message);
            StringAssert.Contains("the player", Assert.Throws<BotException>(() => BotRunner.Run(Game, After(() => Game.Player.AddLook(10f, 0f)), 5f)).Message);
            // Reading is fine, and so is everything a script does through the bot.
            Assert.DoesNotThrow(() => BotRunner.Run(Game, After(() => Assert.IsNotNull(bot.Game.Grabber.FindTarget() ?? crate)), 5f));
        }
    }
}
