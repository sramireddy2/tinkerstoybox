using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Levels;
using Toybox.Platform;
using Toybox.Toys;
using Toybox.UI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Toybox.Tests
{
    /// <summary>
    /// Level 11, "Blocking Lasers": a held toy is not in the world. The card has to lie across the two low
    /// walls to be a roof: 4.7 and up (LEVELS appendix B; measured, 4.4 just lies on both), 7 is its clamp.
    /// Picked up from the start and held flat against the far wall it is 0.28 times the distance the player
    /// stands from that wall.
    ///
    /// Measured tables: tools/out/notes/level11-probe-*.txt (the explicit Probe tests below write them).
    /// </summary>
    public class Level11Tests : SimTest
    {
        static readonly Vector3 Spawn = Level11BlockingLasers.Spawn;
        /// <summary>The pitch of the solver's view when it lets go.</summary>
        const float SolverPitch = 14.4f;

        Level11BlockingLasers Load()
        {
            Game = Game.Create();
            Game.LoadLevel(11);
            return (Level11BlockingLasers)Game.Level;
        }

        List<string> Listen()
        {
            var said = new List<string>();
            Game.Events.Message += e => said.Add(e.Text);
            return said;
        }

        /// <summary>A point twenty units along a view from a place to stand: what DropAt is given to let go at a pitch and a yaw.</summary>
        static Vector3 Along(Vector3 stand, float pitch, float yaw = 0f)
        {
            float p = pitch * Mathf.Deg2Rad, y = yaw * Mathf.Deg2Rad;
            return stand + Vector3.up * Player.BaseEyeHeight + new Vector3(Mathf.Sin(y) * Mathf.Cos(p), Mathf.Sin(p), Mathf.Cos(y) * Mathf.Cos(p)) * 20f;
        }

        // Walks to a place in the dry half, round the table if it is in the way.
        static IEnumerator GoTo(Bot bot, Vector3 stand, float tolerance = 0.05f)
        {
            Vector3 from = bot.Player.Position;
            if ((stand - from).magnitude <= tolerance) yield break;
            float table = Level11BlockingLasers.TableBase.z;
            if ((from.z - table) * (stand.z - table) < 0f && Mathf.Abs(stand.x) < 1.5f && Mathf.Abs(from.x) < 1.5f)
                yield return bot.WalkTo(new Vector3(1.9f, 0f, table), 0.3f, 15f);
            yield return bot.WalkTo(stand, tolerance, 20f);
        }

        // The first half of the solution with the stand point and the view of the test's choosing. The card
        // is picked up from the start, or from straight in front of the table at the given distance.
        static IEnumerator TakeAndDrop(Bot bot, Level11BlockingLasers level, Vector3 stand, Vector3 aim, float pickUpFrom = 0f)
        {
            if (pickUpFrom > 0f)
            {
                Vector3 table = Level11BlockingLasers.TableBase;
                yield return bot.WalkTo(new Vector3(table.x, 0f, table.z - pickUpFrom), 0.03f, 20f);
                yield return bot.LookAt(level.Card);
            }
            yield return bot.Grab(level.Card);
            yield return GoTo(bot, stand);
            yield return bot.DropAt(aim);
        }

        // Lets go as told, then waits for the card to lie still and for the level to have had its say.
        Bot Drop(Level11BlockingLasers level, Vector3 stand, Vector3 aim, float pickUpFrom = 0f)
        {
            var bot = new Bot(Game);
            BotRunner.Run(Game, TakeAndDrop(bot, level, stand, aim, pickUpFrom), 40f);
            Settle(level);
            return bot;
        }

        // The level looks at a card that was let go when it has lain still for half a second, or after two
        // and a half seconds if it goes on rocking: wait for that, and a moment more.
        void Settle(Level11BlockingLasers level)
        {
            int still = 0;
            for (int i = 0; i < 170 && still < 34; i++)
            {
                Game.Tick();
                still = Level11BlockingLasers.AtRest(level.Card) ? still + 1 : 0;
            }
            TestHelpers.Run(Game, 6);
        }

        /// <summary>How far the card's long side is from pointing down the corridor, in degrees.</summary>
        static float Askew(Prop card)
        {
            Vector3 along = card.Transform.forward;
            return Mathf.Abs(Mathf.DeltaAngle(0f, Mathf.Repeat(Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg + 90f, 180f) - 90f));
        }

        // Runs a script that may give up half way (a bot that is zapped is back at the start and never
        // arrives); says whether the level was completed.
        bool Attempt(IEnumerator script, float timeoutSeconds = 60f)
        {
            try
            {
                BotRunner.Run(Game, script, timeoutSeconds);
            }
            catch (BotException)
            {
            }
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            return Game.LevelCompleted;
        }

        // From a place in the dry half straight down the corridor into the rain, on the stick, and not a
        // step further once the beams have had their say. (A bot's WalkTo goes on walking from the start.)
        // The game's input is the stick afterwards: make a new Bot to go on.
        void IntoTheRain(Level11BlockingLasers level, Bot bot, Vector3 from)
        {
            BotRunner.Run(Game, Sequence(GoTo(bot, from, 0.2f), bot.LookAt(from + new Vector3(0f, Player.BaseEyeHeight, 10f))), 30f);
            IntoTheRain(level);
        }

        // The same from where the player stands, the way they face.
        void IntoTheRain(Level11BlockingLasers level)
        {
            int before = level.Rain.Zaps;
            Input = new ScriptedInput();
            Game.Input = Input;
            Input.Hold.MoveZ = 1f;
            for (int i = 0; i < TestHelpers.Ticks(4f) && level.Rain.Zaps == before; i++) Game.Tick();
            Input.Hold = default;
            TestHelpers.Run(Game, 3);
        }

        // ---- The level ---------------------------------------------------------------------------------------

        [Test]
        public void TheLevelIsWhatTheCampaignSays()
        {
            Assert.IsTrue(LevelRegistry.Has(11));
            Level11BlockingLasers level = Load();
            Assert.AreEqual("blocking-lasers", level.Slug);
            Assert.AreEqual("Blocking Lasers", level.Title);
            Assert.AreEqual(3, level.Phase);
            Assert.AreEqual("pegboard-workbench", level.Environment);
            Assert.AreEqual(-Level11BlockingLasers.FloorThickness, level.GroundY, "the box stands on the bench on its own bottom; the corridor's floor is y = 0");
            Assert.AreEqual(-30f, level.KillY);
            Assert.AreEqual(3, level.Hints.Length);
            Assert.AreEqual("It's raining lasers. All you have is a playing card.", level.Blurb, "LEVELS: the blurb");

            Prop card = level.Card;
            Assert.AreSame(ToyCatalog.HeroOf(11), ToyCatalog.Get(ToyId.PlayingCard));
            Assert.AreEqual("Playing Card", card.Name);
            Assert.AreEqual(0.5f, card.Scale);
            Assert.AreEqual(0.2f, card.MinScale);
            Assert.AreEqual(7f, card.MaxScale, "LEVELS: 9.8 wide at most, it cannot wedge between the box's sides");
            Assert.AreEqual(GrabPose.Snap90, card.GrabPose);
            Assert.IsTrue(card.HasTag(Level11BlockingLasers.CardTag));
            Assert.AreEqual(0.14f, card.Mass, 0.005f, "1.12 x 0.5^3");
            Assert.Less(Vector3.Distance(card.Center, Level11BlockingLasers.CardHome), 0.01f, "flat on its table");
            Assert.AreEqual(1f, card.Transform.up.y, 1e-4f, "face up");
            Assert.AreEqual(1f, card.Transform.forward.z, 1e-4f, "long side along the corridor: it is a roof the way it lies");
            Assert.AreEqual(1, Game.Props.Count, "the card is the one thing that can be picked up");

            Assert.AreEqual(1, Game.Exits.Count);
            Assert.AreEqual(new Vector3(0f, 1.3f, 25.5f), Game.Exits[0].Position, "LEVELS: the exit in the tunnel");
            Assert.IsFalse(Game.Exits[0].Locked);

            LaserRain rain = level.Rain;
            Assert.AreEqual(Vector3.down, rain.Direction);
            Assert.AreEqual(8f, rain.Range, "from the lid to the floor");
            Assert.AreEqual(540, rain.BeamCount, 40, "LEVELS: about 550 beams at a pitch of 0.45");
            for (int i = 0; i < rain.BeamCount; i++)
            {
                Vector3 origin = rain.BeamOrigin(i);
                Assert.AreEqual(8f, origin.y, 1e-4f);
                Assert.IsTrue(Mathf.Abs(origin.x) <= 6.001f && origin.z >= Level11BlockingLasers.RainNear - 0.001f && origin.z <= Level11BlockingLasers.RainFar + 0.001f, "inside the rig: " + origin);
            }

            // The card lies on its table and stays there; the player stands a step in front of it.
            RunSeconds(2f);
            Assert.Less(Vector3.Distance(card.Center, Level11BlockingLasers.CardHome), 0.01f, "the card should rest on the table");
            Assert.Less(Vector3.Distance(Game.Player.Position, Spawn), 0.05f);
            Assert.IsTrue(Game.Player.Grounded);
            Assert.AreEqual(0f, rain.Coverage01, 1e-4f, "nothing roofs the lane");
            Assert.AreEqual(0, rain.Zaps);
            int onFloor = 0, onWalls = 0;
            for (int i = 0; i < rain.BeamCount; i++)
            {
                if (Mathf.Abs(rain.BeamLength(i) - 8f) < 1e-3f) onFloor++;
                else if (Mathf.Abs(rain.BeamLength(i) - 5f) < 1e-3f) onWalls++;
                else Assert.Fail("beam " + i + " at " + rain.BeamOrigin(i) + " ends after " + rain.BeamLength(i));
            }
            Assert.Greater(onWalls, 60, "the beams over the low walls end on their tops (3 high): the tops are in the rain");
            Assert.Greater(onFloor, 400);
        }

        [Test]
        public void SolveCompletesTheLevel()
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            float dropped = 0f, k = 0f;
            FitState shown = FitState.Idle;
            Game.Events.PropGrabbed += e => k = e.OldScale / e.GrabDistance;
            Game.Events.PropDropped += e =>
            {
                dropped = e.NewScale;
                shown = level.Gauge;
            };
            TestHelpers.PlayLevel(Game, 60f);
            Assert.AreEqual(FitState.Good, shown, "the outline was green when the solver let go");
            Assert.AreEqual(FitState.Good, level.Gauge, "and is green over the way out");
            Assert.AreEqual(0.377f, k, 0.02f, "LEVELS: picked up from 1.33 away");
            Assert.AreEqual(Level11BlockingLasers.IntendedScale, dropped, Level11BlockingLasers.IntendedScale * 0.05f, "LEVELS appendix B: the solver's drop is 5.9 within 5 %");
            Assert.AreEqual(Level11BlockingLasers.SolverScale, dropped, 0.1f, "the outline on the floor is painted for the solver's card");
            Assert.AreEqual(0, level.Rain.Zaps, "nobody is zapped under the roof");
            Assert.AreEqual(0, level.Seatings, "let go right, the card falls where it is: nothing helps it");
            Assert.IsEmpty(said, "the intended solution needs no telling off");
            Assert.Less(Game.Time, 20f, "the solution takes well under a minute");
            Debug.Log("[Toybox] Level 11: solved in " + Game.Time.ToString("0.00", CultureInfo.InvariantCulture) + " s, card " + dropped.ToString("0.000", CultureInfo.InvariantCulture) + ", k " + k.ToString("0.000", CultureInfo.InvariantCulture));
            Assert.Greater(Game.Player.Position.z, Level11BlockingLasers.FarZ, "the level ends in the tunnel behind the little door");
        }

        [Test]
        public void TheSolve_TakesTheSameTimeEveryTime()
        {
            Load();
            TestHelpers.PlayLevel(Game, 60f);
            float first = Game.Time;
            Game.Dispose();
            Load();
            TestHelpers.PlayLevel(Game, 60f);
            Assert.AreEqual(first, Game.Time, "the same solution, to the tick");
        }

        [Test]
        public void TheSolve_FiveTimesInARow_NeverDiffers()
        {
            int ticks = -1;
            float scale = 0f;
            Vector3 rest = Vector3.zero;
            for (int run = 0; run < 5; run++)
            {
                Game?.Dispose();
                Level11BlockingLasers level = Load();
                TestHelpers.PlayLevel(Game, 60f);
                if (run == 0)
                {
                    ticks = Game.LevelTicks;
                    scale = level.Card.Scale;
                    rest = level.Card.Center;
                    continue;
                }
                Assert.AreEqual(ticks, Game.LevelTicks, "run " + (run + 1) + ": the same solution, to the tick");
                Assert.AreEqual(scale, level.Card.Scale, "run " + (run + 1) + ": the same roof");
                Assert.AreEqual(rest, level.Card.Center, "run " + (run + 1) + ": in the same place");
            }
        }

        // Restart at any moment of the solution - card on the table, in the hand, in the air, on the walls, the
        // bot round the table, in the lane, under the roof, in the door: the level is as it was built, and
        // the solution takes as long as it did the first time.
        [Test]
        public void RestartingAtAnyMomentOfTheSolve_LeavesTheLevelAsBuilt_AndSolvable()
        {
            Load();
            TestHelpers.PlayLevel(Game, 60f);
            int whole = Game.LevelTicks;
            Game.Dispose();
            Load();
            for (int at = 1; at < whole; at += 19)
            {
                var bot = new Bot(Game);
                var script = new BotRunner(((Level11BlockingLasers)Game.Level).Solve(bot), bot, Game);
                for (int tick = 0; tick < at && script.Advance(); tick++) Game.Tick();
                Assert.IsFalse(Game.LevelCompleted, "setup: restarted " + at + " ticks in, before the end");
                Game.RestartLevel();
                Game.Tick();
                AssertAsBuilt();
                Assert.IsFalse(((Level11BlockingLasers)Game.Level).Rain.Exposed, "restarted " + at + " ticks in: nobody is in the rain");
            }
            int before = Game.LevelTicks;
            TestHelpers.PlayLevel(Game, 60f);
            Assert.AreEqual(whole, Game.LevelTicks - before, "after all those restarts the solution is the one it was");
        }

        // What a player does with "stand on the shoe prints and look at the far wall over the two walls": a
        // step off the mark to either side or toward the card, the pick-up their own from there, the
        // crosshair anywhere on the upper half of the far wall, two units either side of its middle. (The
        // reviewer's sweep, tools/out/notes/level11-review-probe-jitter.txt: before the seat was widened,
        // 0.6 off the mark and two units to the same side missed a wall or stopped short. The lower edge is
        // a crosshair two to three units over the walls' tops, 8 to 12 degrees up: 8 looking dead straight
        // from the mark, 11.5 turned five degrees or picked up from right beside the spool. Under it a wall
        // catches the card while it is still narrower than the lane, and that has its own line.)
        [TestCase(0f, 1.75f, 0f, 5.6f)]
        [TestCase(0f, 1.75f, 2f, 7.5f)]
        [TestCase(0f, 1.75f, -2f, 5.6f)]
        [TestCase(0.6f, 1.75f, 2f, 6.5f)]
        [TestCase(-0.6f, 1.75f, -2f, 7.5f)]
        [TestCase(0.6f, 2.2f, -2f, 6.5f)]
        [TestCase(-0.6f, 2.2f, -2f, 6.5f)]
        [TestCase(0.6f, 2.2f, 1f, 6.5f)]
        public void TheSolution_ALittleOff_IsStillARoof(float standX, float standZ, float aimX, float aimY)
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            var stand = new Vector3(standX, 0f, standZ);

            IEnumerator Script()
            {
                yield return bot.WalkTo(stand, 0.05f, 10f);
                yield return bot.Grab(level.Card);
                yield return bot.DropAt(new Vector3(aimX, aimY, Level11BlockingLasers.FarZ));
                yield return bot.Until(() => level.Roofed, 6f);
                yield return level.WalkOut(bot);
            }

            Assert.IsTrue(Attempt(Script()), "from " + stand + " looking at (" + aimX + ", " + aimY + ") the card is " + level.Card.Scale + " at " + level.Card.Center + "; said: " + string.Join(" / ", said));
            Assert.AreEqual(0, level.Rain.Zaps);
            Assert.IsEmpty(said);
        }

        [Test]
        public void TheStartShowsTheCardTheRainAndWhereTheCardGoes()
        {
            Level11BlockingLasers level = Load();
            RunSeconds(0.2f);
            Player player = Game.Player;
            Vector3 eye = player.Eye;

            // The card under the crosshair: the first click is the right one, and from here it looks big enough.
            Assert.AreSame(level.Card, Game.Grabber.FindTarget(), "a click at the start picks the card up");
            Assert.IsTrue(Game.PhysicsScene.Raycast(eye, player.Forward, out RaycastHit hit, 200f, Layers.SolidMask, QueryTriggerInteraction.Ignore));
            Assert.AreSame(level.Card, PropRef.Of(hit.collider), "the crosshair is on the card");
            float looks = level.Card.Scale / Vector3.Distance(eye, level.Card.Center);
            Assert.AreEqual(0.377f, looks, 0.01f, "LEVELS: k = 0.377 at the pick-up");
            Assert.Greater(looks, Level11BlockingLasers.LooksTooSmall + 0.1f);

            // In the same picture (70 degrees up and down, 102 across), with nothing in front of them: the
            // foot and the middle of the rain, the two walls' near ends with the marks on them, the near
            // side of the outline on the floor, and the little door.
            float near = Level11BlockingLasers.OutlineNear, half = Level11BlockingLasers.OutlineHalfWidth;
            foreach (Vector3 point in new[]
                     {
                         new Vector3(0f, 0f, Level11BlockingLasers.RainNear), new Vector3(0f, 3.5f, Level11BlockingLasers.RainNear),
                         new Vector3(-3.5f, 2.8f, Level11BlockingLasers.LowWallNear), new Vector3(3.5f, 2.8f, Level11BlockingLasers.LowWallNear),
                         new Vector3(-half, 0f, near), new Vector3(half, 0f, near), new Vector3(0f, 0f, near),
                         new Vector3(0f, 1.3f, Level11BlockingLasers.FarZ),
                     })
            {
                Vector3 to = point - eye;
                float yaw = Mathf.DeltaAngle(player.Yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                float pitch = Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg - player.Pitch;
                Assert.Less(Mathf.Abs(yaw), 50f, point + " is off the side of the picture");
                Assert.Less(Mathf.Abs(pitch), 34f, point + " is off the top of the picture");
                if (Game.PhysicsScene.Raycast(eye, to.normalized, out hit, to.magnitude - 0.05f, Layers.SolidMask, QueryTriggerInteraction.Ignore))
                    Assert.Fail("something stands in front of " + point + ": " + hit.collider.name);
            }
            Assert.AreEqual(11.1f, near, 0.15f, "the roof begins two units in front of the walls");
            Assert.Greater(half, Level11BlockingLasers.LaneHalfWidth + Level11BlockingLasers.LowWallThickness, "the outline is wider than both walls together");

            // Paint, the stand mark and the blocks of the low walls are looks only.
            foreach (string name in new[] { "Paint", "Outline", "Stand Mark", "Low Walls" })
            {
                Transform looks0 = Game.LevelRoot.Find(name);
                Assert.IsNotNull(looks0, name);
                Assert.AreEqual(0, looks0.GetComponentsInChildren<Collider>(true).Length, name + " must not have colliders");
                Assert.Greater(looks0.GetComponentsInChildren<Renderer>(true).Length, 0, name);
            }
        }

        // ---- Not without a roof ------------------------------------------------------------------------------

        [Test]
        public void WalkingSprintingAndJumpingAtTheDoor_DoesNotCompleteTheLevel()
        {
            Level11BlockingLasers level = Load();
            Input = new ScriptedInput();
            Game.Input = Input;
            float farthest = float.MinValue;

            // Sprint and jump into the rain: down the lane, along one of its walls, and down the strips outside them.
            foreach (Vector3 from in new[] { new Vector3(0f, 0f, 11f), new Vector3(2.5f, 0f, 11f), new Vector3(-5f, 0f, 11f), new Vector3(5f, 0f, 11f) })
            {
                TestHelpers.RunUntil(Game, () => Game.Player.Grounded, 3f);
                // (A test fixture's shortcut: the player could have walked there.)
                Game.Player.Teleport(from, 0f);
                int before = level.Rain.Zaps;
                Input.Hold.MoveZ = 1f;
                Input.Hold.Sprint = true;
                for (int i = 0; i < TestHelpers.Ticks(2.5f) && level.Rain.Zaps == before; i++)
                {
                    if (i % 20 == 10) Input.Once.Jump = true;
                    Game.Tick();
                    farthest = Mathf.Max(farthest, Game.Player.Position.z);
                }
                Input.Hold = default;
                Assert.AreEqual(before + 1, level.Rain.Zaps, "from " + from + " the rain should have sent the player back");
                Assert.Less(Vector3.Distance(Game.Player.Position, Spawn), 0.5f, "back at the start");
            }
            Assert.IsFalse(Game.LevelCompleted, "nobody gets through the rain");
            Assert.Less(farthest, Level11BlockingLasers.RainNear + 1.6f, "LEVELS: a sprint buys 0.8 of a unit, not seven and a half (got to z = " + farthest + ")");
            Assert.AreEqual(Level11BlockingLasers.StartScale, level.Card.Scale);
        }

        [Test]
        public void ACardInTheHand_StopsNothing()
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            float covered = 0f;
            Game.Context.OnUpdate(dt => covered = Mathf.Max(covered, level.Rain.Coverage01));

            // Held over the head like an umbrella, down the lane.
            IEnumerator Umbrella()
            {
                yield return bot.Grab(level.Card);
                yield return GoTo(bot, new Vector3(0f, 0f, 12.5f), 0.2f);
                yield return bot.LookAt(new Vector3(0f, 6f, 17f));
                yield return bot.Wait(0.3f);
                Assert.Greater(level.Card.Center.z, Level11BlockingLasers.RainNear, "the card is held out over the lane");
            }

            BotRunner.Run(Game, Umbrella(), 30f);
            Assert.AreSame(level.Card, Game.Grabber.Held);
            IntoTheRain(level);
            Assert.IsFalse(Game.LevelCompleted, "a held card is no roof");
            Assert.AreEqual(1, level.Rain.Zaps);
            Assert.AreEqual(0f, covered, 1e-4f, "the beams went straight through the card the whole time");
            CollectionAssert.AreEqual(new[] { Level11BlockingLasers.HeldLine }, said);
            // Zapped with the card in the hand, both start over: the player on the shoe prints, the card on
            // its table under the crosshair, as the level began.
            RunSeconds(0.3f);
            AssertAsBuilt(zaps: 1);
            Assert.AreSame(level.Card, Game.Grabber.FindTarget(), "the next click picks it up from where it looks right");
            // And from there the level is solved as from the start.
            TestHelpers.PlayLevel(Game, 60f);
        }

        // The umbrella, tried in earnest: a card small enough to be carried by a head (lighter than the
        // player: picked up from far off, 1.2 big under the lid) let go straight overhead at the edge of the
        // rain. The reviewer's first try at this: it came down on the head off its middle, tipped, leaned on
        // the bot and, sprinted with, kept the beams off for two units. A card lighter than the player does
        // not touch them since (Level11BlockingLasers.Ghost): it falls past them and lies where it lands.
        // The last case runs under it while it is still in the air: a falling card is a roof for as long as it falls.
        [TestCase(false, 1.5f, 1.6f)]
        [TestCase(true, 1.5f, 1.6f)]
        [TestCase(true, 0.9f, 1.6f)]
        [TestCase(true, 0f, 4f)]
        public void ASmallCardLetGoOverhead_FallsPastThePlayer_AndIsNoUmbrella(bool sprint, float wait, float atMost)
        {
            Level11BlockingLasers level = Load();
            Prop card = level.Card;
            var bot = new Bot(Game);
            var under = new Vector3(0f, 0f, Level11BlockingLasers.DryZ - 0.1f);

            IEnumerator Hat()
            {
                yield return bot.WalkTo(new Vector3(0f, 0f, Level11BlockingLasers.TableBase.z - 2.6f), 0.03f, 20f);
                yield return bot.Grab(card);
                yield return GoTo(bot, under);
                yield return bot.LookAt(under + Vector3.up * 30f);
                yield return bot.Wait(0.3f);
                yield return bot.Drop();
                if (wait > 0f) yield return bot.Wait(wait);
            }

            BotRunner.Run(Game, Hat(), 40f);
            Assert.Less(card.Mass, Player.Mass, "setup: light enough to lie on a head (it is " + card.Scale + ")");
            Assert.Greater(card.Scale, 1f, "setup: and big enough to cover one");
            if (wait >= 0.9f)
            {
                Assert.Less(card.Center.y, 0.2f, "it fell past the bot and lies on the floor (it is at " + card.Center + ")");
                Assert.AreEqual(1f, Mathf.Abs(card.Transform.up.y), 0.02f, "flat: it leans on nobody");
            }
            Assert.Less(Vector3.Distance(Game.Player.Position, under), 0.1f, "and the bot was not pushed about");
            float deepest = 0f;
            Game.Context.OnUpdate(dt => deepest = Mathf.Max(deepest, Game.Player.Position.z));
            Assert.IsFalse(Attempt(bot.WalkTo(new Vector3(0f, 0f, Level11BlockingLasers.FarZ - 0.4f), 0.3f, 4f, sprint), 10f), "nobody walks through the rain under a hat");
            Assert.Greater(level.Rain.Zaps, 0);
            Assert.Less(deepest, Level11BlockingLasers.RainNear + atMost, "zapped near where the rain begins (got to z = " + deepest + ")");
        }

        // ---- The window ------------------------------------------------------------------------------------

        // LEVELS appendix B: works from 4.7 to 7 (the clamp). Picked up from the start: 4.8 from z = 6, the
        // solver's 5.95 from the start, 6.9 from the back wall. Picked up from touching distance it reaches
        // its clamp, 7, a unit in front of the start, with its far edge half a unit past the rain.
        [TestCase(6f, 0f, 4.77f)]
        [TestCase(1.75f, 0f, 5.95f)]
        [TestCase(-1.6f, 0f, 6.87f)]
        [TestCase(1f, 0.95f, 7f)]
        public void BothEndsOfTheWindow_AndTheMiddle_AreARoof(float standZ, float pickUpFrom, float scale)
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            var stand = new Vector3(0f, 0f, standZ);
            Bot bot = Drop(level, stand, Along(stand, SolverPitch), pickUpFrom);
            Assert.AreEqual(scale, level.Card.Scale, 0.1f, "the size from z = " + standZ);
            Assert.IsTrue(level.OnBothWalls, "a card of " + level.Card.Scale + " should lie on both walls (it is at " + level.Card.Center + ")");
            Assert.IsTrue(level.Roofed, "and roof the whole lane (covered " + level.Rain.Coverage01 + ")");
            Assert.AreEqual(0f, level.LongestGap(Game), 1e-4f);
            Assert.IsTrue(Attempt(level.WalkOut(bot)), "the bot should have walked out under it (it is at " + Game.Player.Position + ")");
            Assert.AreEqual(0, level.Rain.Zaps);
            Assert.IsEmpty(said);
        }

        // Clearly the wrong size: let go from too near the rain (3.65), and picked up from too far away
        // (0.18 of its distance: 3.3 from the start). Both are narrower than the lane and fall into it.
        [TestCase(10f, 0f, 3.65f, Level11BlockingLasers.NarrowLine)]
        [TestCase(1.75f, 2.8f, 3.28f, Level11BlockingLasers.FarLine)]
        public void AClearlyWrongSize_IsNoRoof(float standZ, float pickUpFrom, float scale, string line)
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            var stand = new Vector3(0f, 0f, standZ);
            Bot bot = Drop(level, stand, Along(stand, SolverPitch), pickUpFrom);
            Assert.AreEqual(scale, level.Card.Scale, 0.1f);
            Assert.IsFalse(level.OnBothWalls);
            Assert.IsFalse(level.Roofed);
            Assert.Less(level.Card.Center.y, 0.3f, "it lies on the floor of the lane");
            CollectionAssert.AreEqual(new[] { line }, said, "one line, saying why");
            Assert.IsFalse(Attempt(level.WalkOut(bot), 30f), "a doormat stops no beam");
            Assert.Greater(level.Rain.Zaps, 0);
            Assert.IsFalse(level.Card.Frozen, "it is big enough to pick up again from the edge of the rain: it stays where it lies");
        }

        // Every way of holding it wrong that looks reasonable gets its own line, once, when the card has come to rest.
        // Held level or low: between the walls (and nine degrees up but five to one side: a corner catches
        // on a wall's inner face before the card is over it). Too steep: on the lid. Looking nine degrees to one side: a
        // corner meets the box's side. From well off to one side: past one wall. Picked up from touching
        // distance and held from the back wall: at its largest half way. From too near the rain: too narrow.
        [TestCase(0f, 1.75f, 5f, 0f, 0f, Level11BlockingLasers.LowLine)]
        [TestCase(0f, 1.75f, 0f, 0f, 0f, Level11BlockingLasers.LowLine)]
        [TestCase(0f, 1.75f, 9.4f, 5f, 0f, Level11BlockingLasers.LowLine)]
        [TestCase(0f, 1.75f, 32f, 0f, 0f, Level11BlockingLasers.HighLine)]
        [TestCase(0f, 1.75f, 14.4f, 9f, 0f, Level11BlockingLasers.ShortLine)]
        [TestCase(2.4f, 7f, 22f, 0f, 0f, Level11BlockingLasers.SlippedLine)]
        [TestCase(1.9f, 6f, 14.4f, 0f, 0f, Level11BlockingLasers.LowLine)]
        [TestCase(0f, -1.6f, 14.4f, 0f, 0.95f, Level11BlockingLasers.FullSizeLine)]
        [TestCase(0f, 8f, 14.4f, 0f, 0f, Level11BlockingLasers.NarrowLine)]
        public void ATryThatFails_IsToldWhy(float standX, float standZ, float pitch, float yaw, float pickUpFrom, string line)
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            var stand = new Vector3(standX, 0f, standZ);
            Drop(level, stand, Along(stand, pitch, yaw), pickUpFrom);
            Assert.IsFalse(level.Roofed, "the card is " + level.Card.Scale + " at " + level.Card.Center);
            Assert.AreEqual(0, level.Seatings, "this far off it is left to fall as it will");
            CollectionAssert.AreEqual(new[] { line }, said);
            RunSeconds(4f);
            CollectionAssert.AreEqual(new[] { line }, said, "said once");
            Assert.IsFalse(level.Card.Frozen, "the card stays where it lies: it can be picked up again from there");
            Assert.IsTrue(level.Recoverable(Game));
        }

        [Test]
        public void ACardTurnedAcrossTheCorridor_IsToldToBeTurned()
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            // Six steps of the turn key: a quarter turn. Twelve wide, it stops between the box's sides.
            BotRunner.Run(Game, Sequence(bot.Grab(level.Card), bot.RotateHeld(6), bot.DropAt(Level11BlockingLasers.AimPoint)), 20f);
            Assert.AreEqual(90f, Vector3.Angle(level.Card.Transform.forward, Vector3.forward), 0.5f);
            Settle(level);
            Assert.IsFalse(level.Roofed);
            CollectionAssert.AreEqual(new[] { Level11BlockingLasers.TurnedLine }, said);
        }

        // F stands the card on end. However it is held then, it is a wall, not a roof, and it falls over where it will.
        [TestCase(14.4f)]
        [TestCase(0f)]
        public void ACardLetGoOnEnd_IsToldToBeFlippedFlat(float pitch)
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            float up = 1f;
            Game.Events.PropDropped += e => up = Mathf.Abs(level.Card.Transform.up.y);
            BotRunner.Run(Game, Sequence(bot.Grab(level.Card), bot.RotateHeld(0, 1), bot.DropAt(Along(Spawn, pitch))), 20f);
            Assert.Less(up, 0.05f, "setup: let go standing on end");
            Settle(level);
            Assert.IsFalse(level.Roofed);
            CollectionAssert.AreEqual(new[] { Level11BlockingLasers.OnEndLine }, said);
            Assert.IsFalse(level.Card.Frozen, "it stays where it fell: it can be picked up again");
            Assert.IsTrue(level.Recoverable(Game));
        }

        // Flipped twice it lies face down: as good a roof as face up.
        [Test]
        public void FaceDown_TheCardIsARoofAllTheSame()
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            BotRunner.Run(Game, Sequence(bot.Grab(level.Card), bot.RotateHeld(0, 2), bot.DropAt(Level11BlockingLasers.AimPoint)), 20f);
            Settle(level);
            Assert.AreEqual(-1f, level.Card.Transform.up.y, 1e-3f, "face down");
            Assert.IsTrue(level.Roofed, "the card is " + level.Card.Scale + " at " + level.Card.Center);
            Assert.IsTrue(Attempt(level.WalkOut(bot)));
            Assert.IsEmpty(said);
        }

        // "Pick it up again": the roof overhangs the dry half, and from under its near end it looks two to
        // four times too big - at its full size long before the far wall. That is said on the pick-up, in
        // Level 7's words; put down and picked up again from the start it looks as it did the first time.
        [Test]
        public void TheRoofPickedUpAgainFromUnderneath_IsToldItLooksTooBig_AndFromFurtherBackItIsARoofAgain()
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            Prop card = level.Card;
            var bot = new Bot(Game);
            float k = 0f;
            Game.Events.PropGrabbed += e => k = e.OldScale / e.GrabDistance;
            var under = new Vector3(0f, 0f, 12.8f);

            IEnumerator FromUnderneath()
            {
                yield return bot.Grab(card);
                yield return bot.DropAt(Level11BlockingLasers.AimPoint);
                yield return bot.Until(() => level.Roofed, 6f);
                yield return GoTo(bot, under, 0.1f);
                yield return bot.Grab(card);
            }

            BotRunner.Run(Game, FromUnderneath(), 40f);
            Assert.Greater(k, Level11BlockingLasers.LooksTooBig, "from under its near end the roof looks too big");
            CollectionAssert.AreEqual(new[] { Level11BlockingLasers.NearLine }, said, "said on the pick-up");
            Assert.AreEqual(Level07TeeterTotter.NearLine, Level11BlockingLasers.NearLine, "the sentence for a pick-up from too near is Level 7's");

            // Held at the far wall from there as the hints say, it is no roof.
            BotRunner.Run(Game, bot.DropAt(Along(under, SolverPitch)), 10f);
            Settle(level);
            Assert.IsFalse(level.Roofed, "the card is " + card.Scale + " at " + card.Center);
            RunSeconds(6f);
            Assert.IsFalse(card.Frozen, "it stays where it lies");

            // From the start it looks what it looked the first time.
            IEnumerator FromTheStart()
            {
                yield return GoTo(bot, Spawn, 0.05f);
                yield return bot.Grab(card);
                yield return bot.DropAt(Level11BlockingLasers.AimPoint);
                yield return bot.Until(() => level.Roofed, 6f);
                yield return level.WalkOut(bot);
            }

            Assert.IsTrue(Attempt(FromTheStart()), "picked up from the start it looks " + k + "; the card is " + card.Scale + " at " + card.Center + "; said: " + string.Join(" / ", said));
            Assert.Less(k, 0.6f);
            Assert.Greater(k, Level11BlockingLasers.LooksTooSmall);
            Assert.AreEqual(0, level.Rain.Zaps);
        }

        // The roof taken away from over one's own head, in the lane: zapped at once, and both start over -
        // the player on the shoe prints, the card on its table. (The first review kept it in the hand, as a
        // respawn does everywhere else: it came back 5 big and a hand from the eyes, the whole view was its
        // face, and let go there it lay on the spool like a table top round the player's chest.)
        [Test]
        public void TheRoofPickedUpFromUnderIt_InTheLane_IsAZap_AndBothStartOver()
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            Prop card = level.Card;
            var bot = new Bot(Game);
            float k = 0f;
            int respawned = 0;
            Game.Events.PropGrabbed += e => k = e.OldScale / e.GrabDistance;
            Game.Events.PropRespawned += e => respawned++;

            IEnumerator Take()
            {
                yield return bot.Grab(card);
                yield return bot.DropAt(Level11BlockingLasers.AimPoint);
                yield return bot.Until(() => level.Roofed, 6f);
                yield return GoTo(bot, new Vector3(0f, 0f, 12f), 0.3f);
                yield return bot.WalkTo(new Vector3(0f, 0f, 17f), 0.2f, 10f);
                yield return bot.Grab(card);
                yield return bot.Until(() => level.Rain.Zaps > 0, 1f);
                yield return bot.Wait(0.2f);
            }

            BotRunner.Run(Game, Take(), 40f);
            Assert.Greater(k, Level11BlockingLasers.LooksTooBig);
            Assert.AreEqual(1, level.Rain.Zaps, "LEVELS: the beams reach whoever takes the roof away from underneath");
            CollectionAssert.AreEqual(new[] { Level11BlockingLasers.HeldLine }, said, "one line: how big it looked from there no longer matters");
            Assert.AreEqual(1, respawned, "the card went back to its table");
            AssertAsBuilt(zaps: 1);
            Assert.AreSame(card, Game.Grabber.FindTarget(), "back at the start the crosshair is on the card");
            Assert.AreEqual(0f, level.Rain.Coverage01, 1e-4f, "and the roof is gone");

            // From there it is the level's first picture: the solution as it is.
            TestHelpers.PlayLevel(Game, 60f);
            Assert.AreEqual(0.377f, k, 0.02f, "picked up from the shoe prints it looks as it did the first time");
            Assert.AreEqual(1, level.Rain.Zaps);
            Assert.AreEqual(1, said.Count, "nothing more was said");
        }

        // Zapped without the card in the hand, the card stays where it lies (LEVELS) - and the player comes
        // back looking down the corridor at it, not at the empty table.
        [Test]
        public void ZappedWithNothingInTheHand_TheCardStaysWhereItLies_AndThePlayerLooksDownTheCorridor()
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            Prop card = level.Card;
            // A doormat in the lane, and a walk into the rain beside it.
            Bot bot = Drop(level, Spawn, Along(Spawn, 3f));
            Vector3 lay = card.Center;
            float scale = card.Scale;
            IntoTheRain(level, bot, new Vector3(5f, 0f, 12f));
            Assert.AreEqual(1, level.Rain.Zaps);
            Assert.Less(Vector3.Distance(Game.Player.Position, Spawn), 0.05f, "back on the shoe prints");
            Assert.AreEqual(0f, Game.Player.Yaw, 0.01f);
            Assert.AreEqual(0f, Game.Player.Pitch, 0.01f, "looking level, down the corridor");
            Assert.Less(Vector3.Distance(card.Center, lay), 0.05f, "the card lies where it lay");
            Assert.AreEqual(scale, card.Scale);
            Assert.IsFalse(card.Frozen);
            CollectionAssert.AreEqual(new[] { Level11BlockingLasers.LowLine, Level11BlockingLasers.BareLine }, said);
            // From the start it is in plain view over the table: one click picks it up again.
            bot = new Bot(Game);
            BotRunner.Run(Game, Sequence(bot.Grab(card), bot.DropAt(Level11BlockingLasers.AimPoint), bot.Until(() => level.Roofed, 6f), level.WalkOut(bot)), 60f);
            Assert.IsTrue(Game.LevelCompleted);
        }

        // A whole roof, and a walk down one of the strips outside the walls: the roof is not what is wrong.
        [TestCase(5f)]
        [TestCase(-5f)]
        public void ZappedBesideAWholeRoof_IsToldToWalkUnderIt(float x)
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            Bot bot = Drop(level, Spawn, Level11BlockingLasers.AimPoint);
            Assert.IsTrue(level.Roofed);
            IntoTheRain(level, bot, new Vector3(x, 0f, 12f));
            Assert.AreEqual(1, level.Rain.Zaps, "the strips outside the walls are in the rain, roof or no roof");
            CollectionAssert.AreEqual(new[] { Level11BlockingLasers.BesideLine }, said);
            Assert.IsTrue(level.Roofed, "the roof lies where it lay");
            Assert.Less(Vector3.Distance(Game.Player.Position, Spawn), 0.05f);
            Assert.AreEqual(0f, Game.Player.Pitch, 0.01f, "looking down the corridor at it");
            // Under it, down the middle, it is the way out.
            bot = new Bot(Game);
            Assert.IsTrue(Attempt(level.WalkOut(bot)));
            Assert.AreEqual(1, level.Rain.Zaps);
        }

        [Test]
        public void ARoofThatStopsShort_IsToldAtOnce_AndAgainUnderIt()
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            // Held too steeply it stops on the lid: on both walls, and three units short of the far wall.
            Bot bot = Drop(level, Spawn, Along(Spawn, 26f));
            Assert.IsTrue(level.OnBothWalls);
            Assert.IsFalse(level.Roofed);
            Assert.Greater(level.LongestGap(Game), 1.5f);
            CollectionAssert.AreEqual(new[] { Level11BlockingLasers.HighLine }, said);
            // Down the lane: dry as far as the roof goes, then not.
            float deepest = 0f;
            Game.Context.OnUpdate(dt => deepest = Mathf.Max(deepest, Game.Player.Position.z));
            Assert.IsFalse(Attempt(Sequence(GoTo(bot, new Vector3(0f, 0f, 12f), 0.3f), bot.WalkTo(new Vector3(0f, 0f, 22.5f), 0.3f, 3f)), 30f));
            Assert.AreEqual(1, level.Rain.Zaps, "zapped where the roof ends");
            Assert.Greater(deepest, level.Card.Colliders[0].bounds.max.z - 0.3f, "it kept the bot dry all the way to its far edge (got to " + deepest + ")");
            CollectionAssert.AreEqual(new[] { Level11BlockingLasers.HighLine, Level11BlockingLasers.GapLine }, said);
        }

        // ---- Forgiving ---------------------------------------------------------------------------------------

        // The click may land anywhere on the card: in the hand it is square to the view (PropOptions.SquareOnGrab).
        [TestCase(0.3f, 0.42f)]
        [TestCase(-0.3f, -0.4f)]
        [TestCase(0.32f, -0.45f)]
        public void PickedUpByACorner_TheCardIsHeldSquare_AndIsARoof(float cornerX, float cornerZ)
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            Prop card = level.Card;
            var bot = new Bot(Game);
            float yawAtPickUp = 0f;
            Game.Events.PropGrabbed += e => yawAtPickUp = Game.Player.Yaw;

            IEnumerator ByTheCorner()
            {
                yield return bot.GrabAt(card, Level11BlockingLasers.CardHome + new Vector3(cornerX, 0f, cornerZ));
                yield return bot.LookAt(Level11BlockingLasers.AimPoint);
                yield return bot.Wait(0.3f);
                Assert.Less(Askew(card), 0.1f, "held square to the view, which looks down the corridor");
                yield return bot.DropAt(Level11BlockingLasers.AimPoint);
                yield return bot.Until(() => level.Roofed, 6f);
                yield return level.WalkOut(bot);
            }

            Assert.IsTrue(Attempt(ByTheCorner()), "the card is " + card.Scale + " at " + card.Center + ", " + Askew(card) + " degrees askew");
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(0f, yawAtPickUp)), 8f, "setup: the view was well off the corridor's line at the pick-up (" + yawAtPickUp + ")");
            Assert.AreEqual(0, level.Seatings, "square in the hand, it needed no help");
            Assert.AreEqual(0, level.Rain.Zaps);
            Assert.IsEmpty(said);
        }

        // LEVELS reckons with six degrees of yaw. Looking that far to one side the card is a unit and a
        // half off the middle and its corner is at the box's side; from the near end of the window three
        // degrees miss a wall. A card let go that close finds its seat as it comes down.
        [TestCase(1.75f, 5f)]
        [TestCase(1.75f, -6f)]
        [TestCase(6f, 3f)]
        [TestCase(-1.6f, 3f)]
        public void LetGoNearlyRight_TheCardFindsItsSeat(float standZ, float yaw)
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            Prop card = level.Card;
            var stand = new Vector3(0f, 0f, standZ);
            float offCentre = 0f, askew = 0f;
            Game.Events.PropDropped += e =>
            {
                offCentre = Mathf.Abs(card.Center.x);
                askew = Askew(card);
            };
            Bot bot = Drop(level, stand, Along(stand, SolverPitch, yaw));
            Assert.Greater(offCentre, 0.5f, "setup: let go well off the middle");
            Assert.AreEqual(Mathf.Abs(yaw), askew, 0.2f, "and as askew as the view");
            Assert.AreEqual(1, level.Seatings);
            Assert.AreEqual(0f, card.Center.x, 0.03f, "in the middle");
            Assert.Less(Askew(card), 0.3f, "square");
            Assert.AreEqual(Level11BlockingLasers.FarZ, card.Colliders[0].bounds.max.z, 0.08f, "against the far wall");
            Assert.AreEqual(0f, TestHelpers.DeepestOverlap(Game, card), 0.02f, "and in nothing");
            Assert.IsTrue(level.OnBothWalls);
            Assert.IsTrue(level.Roofed);
            Assert.IsTrue(Attempt(level.WalkOut(bot)));
            Assert.AreEqual(0, level.Rain.Zaps);
            Assert.IsEmpty(said);
        }

        [Test]
        public void TheRoof_KeepsTheLaneDry_AndNothingElse()
        {
            Level11BlockingLasers level = Load();
            LaserRain rain = level.Rain;
            Drop(level, Spawn, Level11BlockingLasers.AimPoint);
            Prop card = level.Card;
            Assert.IsTrue(level.Roofed);
            GadgetKit.VerticalExtent(card, out float bottom, out float top);
            Assert.AreEqual(Level11BlockingLasers.LowWallTop, bottom, 0.03f, "it lies on the walls' tops");
            Assert.AreEqual(1f, card.Transform.up.y, 1e-3f, "flat");
            Assert.AreEqual(230f, card.Mass, 12f, "LEVELS: mass 227");
            Bounds bounds = card.Colliders[0].bounds;
            Assert.AreEqual(Level11BlockingLasers.OutlineNear, bounds.min.z, 0.15f, "its near edge is over the near side of the outline on the floor");
            Assert.AreEqual(Level11BlockingLasers.OutlineHalfWidth, bounds.max.x, 0.1f);
            Assert.AreEqual(-Level11BlockingLasers.OutlineHalfWidth, bounds.min.x, 0.1f);
            int onCard = 0, onFloor = 0;
            for (int i = 0; i < rain.BeamCount; i++)
            {
                float x = rain.BeamOrigin(i).x;
                if (Mathf.Abs(x) < 4f) Assert.AreEqual(8f - top, rain.BeamLength(i), 0.02f, "a beam over the lane or a wall ends on the card");
                if (Mathf.Abs(rain.BeamLength(i) - (8f - top)) < 0.02f) onCard++;
                else if (Mathf.Abs(rain.BeamLength(i) - 8f) < 1e-3f) onFloor++;
            }
            Assert.Greater(onCard, 300);
            Assert.Greater(onFloor, 100, "outside the card the rain goes on");
            Assert.AreEqual(1f, rain.Coverage01, 1e-4f);
        }

        // ---- Nothing is lost -----------------------------------------------------------------------------------

        [Test]
        public void RestartingMidSolve_AndSolvingAgain_Works()
        {
            Level11BlockingLasers level = Load();
            var bot = new Bot(Game);
            // With the card in the hand, out over the lane.
            BotRunner.Run(Game, Sequence(bot.Grab(level.Card), bot.LookAt(Level11BlockingLasers.AimPoint), bot.Wait(0.2f)), 20f);
            Assert.AreSame(level.Card, Game.Grabber.Held);
            Game.RestartLevel();
            Game.Tick();
            AssertAsBuilt();
            // With a doormat in the lane and a zap on the clock.
            level = (Level11BlockingLasers)Game.Level;
            bot = Drop(level, Spawn, Along(Spawn, 3f));
            Assert.IsFalse(Attempt(Sequence(GoTo(bot, new Vector3(5f, 0f, 12f), 0.3f), bot.WalkTo(new Vector3(5f, 0f, 20f), 0.3f, 3f)), 30f));
            Assert.Greater(level.Rain.Zaps, 0);
            Game.RestartLevel();
            Game.Tick();
            AssertAsBuilt();
            TestHelpers.PlayLevel(Game, 60f);
        }

        static IEnumerator Sequence(params IEnumerator[] steps)
        {
            foreach (IEnumerator step in steps) yield return step;
        }

        void AssertAsBuilt(int zaps = 0)
        {
            var level = (Level11BlockingLasers)Game.Level;
            Assert.IsFalse(Game.LevelCompleted);
            Assert.IsNull(Game.Grabber.Held);
            Assert.AreEqual(Level11BlockingLasers.StartScale, level.Card.Scale);
            Assert.IsTrue(level.Card.Frozen);
            Assert.Less(Vector3.Distance(level.Card.Center, Level11BlockingLasers.CardHome), 0.01f);
            Assert.AreEqual(1f, level.Card.Transform.up.y, 1e-4f, "face up");
            Assert.AreEqual(1f, level.Card.Transform.forward.z, 1e-4f, "long side along the corridor");
            Assert.Less(Vector3.Distance(Game.Player.Position, Spawn), 0.05f);
            Assert.AreEqual(0f, Game.Player.Yaw, 0.01f);
            Assert.AreEqual(Level11BlockingLasers.SpawnPitch, Game.Player.Pitch, 0.01f, "looking at the card");
            Assert.AreEqual(zaps, level.Rain.Zaps);
            Assert.AreEqual(1, Game.Props.Count);
            Assert.AreEqual(FitState.Idle, level.Gauge, "the outline is paint");
        }

        [Test]
        public void ACardThatFallsOutOfTheWorld_ComesBack()
        {
            Level11BlockingLasers level = Load();
            Prop card = level.Card;
            // A doormat in the lane; then - a test fixture's shortcut, nothing the player can do - under the bench.
            Drop(level, Spawn, Along(Spawn, 3f));
            Assert.IsFalse(card.Frozen);
            int respawned = 0;
            Game.Events.PropRespawned += e => respawned++;
            card.SetPose(new Vector3(0f, level.KillY - 3f, 10f), Quaternion.identity);
            RunSeconds(0.2f);
            Assert.AreEqual(1, respawned);
            Assert.AreEqual(Level11BlockingLasers.StartScale, card.Scale, "back at the size it started with");
            Assert.Less(Vector3.Distance(card.Center, Level11BlockingLasers.CardHome), 0.02f, "on its table");
            // And the level is as solvable as before.
            Game.Player.Respawn();
            RunSeconds(0.2f);
            TestHelpers.PlayLevel(Game, 60f);
        }

        [Test]
        public void ACardTooSmallToUse_GoesBackToItsTable()
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            Prop card = level.Card;
            // Picked up from the back wall, four and a half away, and put down at the feet: a fifth of its size.
            var stand = new Vector3(0f, 0f, Level11BlockingLasers.TableBase.z - 4.5f);
            var bot = new Bot(Game);
            BotRunner.Run(Game, TakeAndDrop(bot, level, stand, stand + new Vector3(0f, 0f, 1f), 4.5f), 30f);
            Assert.Less(card.Scale, 0.3f);
            Assert.IsFalse(card.Frozen);
            CollectionAssert.AreEqual(new[] { Level11BlockingLasers.FarLine }, said, "said on the pick-up");
            TestHelpers.RunUntil(Game, () => Level11BlockingLasers.AtRest(card), 4f);
            Assert.Less(level.BestRatio(Game), Level11BlockingLasers.LooksTooSmall, "from right beside it, it still looks too small");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => card.Frozen, 3f), "it should have gone back by itself");
            Assert.AreEqual(1, level.Useless.Returns);
            Assert.AreEqual(Level11BlockingLasers.StartScale, card.Scale);
            Assert.Less(Vector3.Distance(card.Center, Level11BlockingLasers.CardHome), 0.02f);
            CollectionAssert.AreEqual(new[] { Level11BlockingLasers.FarLine, Level11BlockingLasers.CrumbLine }, said);
            // From there the level is solved as from the start.
            BotRunner.Run(Game, Sequence(bot.WalkTo(Spawn, 0.03f, 15f), level.Solve(bot)), 60f);
            Assert.IsTrue(Game.LevelCompleted);
        }

        [Test]
        public void ACardOutOfReachInTheRain_GoesBackToItsTable()
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            Prop card = level.Card;
            // Picked up from the far side of the dry half, looking back at the table: seven away. Let go
            // against the far wall it is under a unit long, deep in the rain.
            var stand = new Vector3(0f, 0f, 9.8f);
            var bot = new Bot(Game);

            IEnumerator Throw()
            {
                yield return GoTo(bot, stand);
                yield return bot.Grab(card);
                yield return bot.DropAt(Along(stand, 3f));
            }

            BotRunner.Run(Game, Throw(), 30f);
            Assert.Less(card.Scale, 1.2f);
            Assert.Greater(card.Scale, Level11BlockingLasers.CrumbScale);
            TestHelpers.RunUntil(Game, () => Level11BlockingLasers.AtRest(card), 4f);
            Assert.Greater(card.Center.z, Level11BlockingLasers.RainNear + 3f, "it lies deep in the rain (at " + card.Center + ")");
            Assert.IsFalse(level.Recoverable(Game));
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => card.Frozen, 3f), "it should have gone back by itself");
            Assert.Less(Vector3.Distance(card.Center, Level11BlockingLasers.CardHome), 0.02f);
            CollectionAssert.AreEqual(new[] { Level11BlockingLasers.FarLine, Level11BlockingLasers.LostLine }, said);
            BotRunner.Run(Game, Sequence(GoTo(bot, Spawn, 0.03f), level.Solve(bot)), 60f);
            Assert.IsTrue(Game.LevelCompleted);
        }

        [Test]
        public void ADoormat_IsPickedUpAgainFromTheEdgeOfTheRain_AndMadeARoof()
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            Prop card = level.Card;
            // The first try, held level: the card stops between the walls, as wide as the lane, and lies in it.
            Bot bot = Drop(level, Spawn, Along(Spawn, 0f));
            Assert.AreEqual(Level11BlockingLasers.SpanScale, card.Scale, 0.05f, "as wide as the lane");
            Assert.IsTrue(level.Recoverable(Game));
            float k = 0f;
            Game.Events.PropGrabbed += e => k = e.OldScale / e.GrabDistance;

            // LEVELS: re-grabbed from the edge of the rain, where it looks plenty big. From in front of it,
            // held up over the walls against the far wall.
            IEnumerator Again()
            {
                var stand = new Vector3(0f, 0f, card.Colliders[0].bounds.min.z - 0.6f);
                yield return GoTo(bot, stand, 0.1f);
                yield return bot.Grab(card);
                yield return bot.DropAt(Along(stand, 30f));
                yield return bot.Until(() => level.Roofed, 6f);
                yield return level.WalkOut(bot);
            }

            Assert.IsTrue(Attempt(Again(), 60f), "the second try should have worked (card " + card.Scale + " at " + card.Center + ", k " + k + ")");
            Assert.Greater(k, 0.5f);
            Assert.AreEqual(0, level.Rain.Zaps);
            CollectionAssert.AreEqual(new[] { Level11BlockingLasers.LowLine }, said, "only the first try was told off");
        }

        // ---- The outline is the gauge -------------------------------------------------------------------------

        // Level 2's language: paint while nothing is judged, red and blinking while the card is held out as
        // far as the walls and would be no roof, green and steady while it would be one if it were let go
        // where it is - and then all the way down, and for as long as it lies there as a roof. (The first
        // review's open point: held, the card looks the same whether it has reached the far wall or caught
        // on a wall half way, and from the shoe prints the lower third of the far wall over the walls fails.)
        [Test]
        public void TheOutlineSaysWhatTheCardInTheHandWouldBe()
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            Prop card = level.Card;
            Renderer outline = level.Outline;
            Assert.IsNotNull(outline, "the outline has a renderer of its own");
            Assert.AreEqual(0, outline.GetComponentsInParent<Collider>(true).Length, "and is looks only");
            Material paint = outline.sharedMaterial;
            Material red = Toybox.Art.Materials.Gadget(Level02ThimbleChasm.Wrong), green = Toybox.Art.Materials.Gadget(Toybox.Art.Palette.Go);
            var bot = new Bot(Game);
            RunSeconds(0.2f);
            Assert.AreEqual(FitState.Idle, level.Gauge);

            void Hold(Vector3 at) => BotRunner.Run(Game, Sequence(bot.LookAt(at), bot.Wait(0.3f)), 10f);

            // In the hand, over its table: carried, not judged.
            BotRunner.Run(Game, Sequence(bot.Grab(card), bot.Wait(0.3f)), 10f);
            Assert.AreEqual(FitState.Idle, level.Gauge, "picked up and looked at: nothing to judge");
            Assert.AreSame(paint, outline.sharedMaterial);

            // Held low: it catches between the walls, as wide as the lane. Red.
            Hold(Along(Spawn, 3f));
            Assert.AreEqual(FitState.TooSmall, level.Gauge, "held low the card is " + card.Scale + " at " + card.Center);
            Assert.AreSame(red, outline.sharedMaterial, "the red of Level 2's outline");

            // Higher, as the solver holds it: green.
            Hold(Level11BlockingLasers.AimPoint);
            Assert.AreEqual(FitState.Good, level.Gauge, "held right the card is " + card.Scale + " at " + card.Center);
            Assert.AreSame(green, outline.sharedMaterial);

            // Too steep: against the lid, short of the far wall. Red again.
            Hold(Along(Spawn, 32f));
            Assert.AreEqual(FitState.TooSmall, level.Gauge, "held steep the card is " + card.Scale + " at " + card.Center);

            // Turned a quarter in the hand (its long side across the corridor): no roof, red.
            BotRunner.Run(Game, Sequence(bot.RotateHeld(6), bot.LookAt(Level11BlockingLasers.AimPoint), bot.Wait(0.3f)), 10f);
            Assert.AreEqual(FitState.TooSmall, level.Gauge, "turned across the card is " + card.Scale + " at " + card.Center);
            BotRunner.Run(Game, bot.RotateHeld(-6), 10f);

            // Looking the other way: carried.
            Hold(Along(Spawn, 0f, 180f));
            Assert.AreEqual(FitState.Idle, level.Gauge);
            Assert.AreSame(paint, outline.sharedMaterial);

            // Let go while it is green: green all the way down, and for as long as the roof lies there.
            Hold(Level11BlockingLasers.AimPoint);
            Assert.AreEqual(FitState.Good, level.Gauge);
            BotRunner.Run(Game, bot.Drop(), 10f);
            for (int tick = 0; tick < 150; tick++)
            {
                Game.Tick();
                Assert.AreEqual(FitState.Good, level.Gauge, tick + " ticks after it was let go");
            }
            Assert.IsTrue(level.Roofed);
            Assert.AreSame(green, outline.sharedMaterial);
            Assert.IsEmpty(said);

            // Picked up again from the start and held low: judged again; let go there, it is paint once it lies.
            BotRunner.Run(Game, bot.Grab(card), 10f);
            Hold(Along(Spawn, 3f));
            Assert.AreEqual(FitState.TooSmall, level.Gauge);
            BotRunner.Run(Game, bot.Drop(), 10f);
            Settle(level);
            Assert.IsFalse(level.Roofed);
            Assert.AreEqual(FitState.Idle, level.Gauge);
            Assert.AreSame(paint, outline.sharedMaterial);
            CollectionAssert.AreEqual(new[] { Level11BlockingLasers.LowLine }, said);
        }

        // Green means roof: what the outline says right before the card is let go is what the card then is.
        // (The second review's sweep, tools/out/notes/level11-review2-gauge.txt: nine places to stand, the
        // crosshair anywhere on the far wall and the lid. The one green that fell - a card of 4.49 let go
        // dead straight from z = 7.4 and from high up, a seventh of a unit on either wall - is why a card
        // narrower than 5 is always put exactly in its seat.)
        [TestCase(0f, 1.75f, 0f, 7f, true)]
        [TestCase(0.6f, 1.75f, 2f, 6.5f, true)]
        [TestCase(-0.6f, 2.05f, -1f, 7.5f, true)]
        [TestCase(0f, -1.6f, 0f, 7.5f, true)]
        [TestCase(0f, 1.75f, 1f, 5.3f, true)]
        [TestCase(0f, 6f, 2f, 5.3f, true)]
        [TestCase(0f, 7.4f, 0f, 7.5f, true)]
        [TestCase(0f, 7.4f, 0f, 6.5f, true)]
        [TestCase(0f, 7.4f, 2f, 7.5f, true)]
        [TestCase(2.4f, 7f, 0f, 6.5f, true)]
        [TestCase(0f, 1.75f, 0f, 3.6f, false)]
        [TestCase(0f, 1.75f, 1f, 4.4f, false)]
        [TestCase(0f, 1.75f, 3f, 6.5f, false)]
        [TestCase(0f, 1.75f, 0f, 13f, false)]
        [TestCase(0f, 7.4f, 0f, 4.4f, false)]
        [TestCase(0f, 10f, 0f, 7f, false)]
        public void GreenMeansRoof(float standX, float standZ, float aimX, float aimY, bool roof)
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            var bot = new Bot(Game);
            var stand = new Vector3(standX, 0f, standZ);
            var aim = new Vector3(aimX, aimY, Level11BlockingLasers.FarZ);
            BotRunner.Run(Game, Sequence(bot.Grab(level.Card), GoTo(bot, stand), bot.LookAt(aim), bot.Wait(0.3f)), 30f);
            string held = "from " + stand + " looking at " + aim + " the card is " + level.Card.Scale + " at " + level.Card.Center;
            Assert.AreEqual(roof ? FitState.Good : FitState.TooSmall, level.Gauge, held);
            BotRunner.Run(Game, bot.DropAt(aim), 10f);
            Settle(level);
            Assert.AreEqual(roof, level.Roofed, held + "; it came to rest at " + level.Card.Center + "; said: " + string.Join(" / ", said));
            Assert.AreEqual(roof ? FitState.Good : FitState.Idle, level.Gauge, "and the outline says so");
            if (!roof)
            {
                Assert.AreEqual(1, said.Count, "a try that fails is told why: " + string.Join(" / ", said));
                return;
            }
            Assert.IsEmpty(said);
            Assert.IsTrue(Attempt(level.WalkOut(bot)), "under a green outline the way is dry");
            Assert.AreEqual(0, level.Rain.Zaps);
        }

        // ---- No way round ----------------------------------------------------------------------------------------

        // The dash: a card narrower than the lane, held up against the lid over the lane and let go at a
        // sprint, is a roof for as long as it falls - three quarters of a second, six units of the seven and
        // a half. (The first review had this by arithmetic, 20.8 of 22; measured over 120 tries of how big
        // it looks, how steeply it is held and where it is let go, the best got to 21.31 - the capsule is
        // dry from 22.24 - tools/out/notes/level11-review2-dash.txt. A card wide enough for the walls lands
        // on them and is a roof like any other.)
        [TestCase(0.5f, 60f, 14.6f)]
        [TestCase(0.5f, 65f, 14.6f)]
        [TestCase(0.55f, 60f, 14.6f)]
        [TestCase(0.6f, 65f, 14.6f)]
        [TestCase(0.55f, 60f, 14.2f)]
        public void ASprintUnderAFallingCard_NarrowerThanTheLane_EndsInTheRain(float looks, float pitch, float letGoAt)
        {
            Level11BlockingLasers level = Load();
            Prop card = level.Card;
            var bot = new Bot(Game);
            float k = 0f;
            Game.Events.PropGrabbed += e => k = e.OldScale / e.GrabDistance;
            var start = new Vector3(0f, 0f, 9.6f);

            IEnumerator Prepare()
            {
                // A card of two on the floor of the dry half, picked up again from as far as makes it look as wanted,
                // and carried to under the lid's edge, held up against the lid.
                yield return bot.Grab(card);
                yield return GoTo(bot, new Vector3(0f, 0f, 5f));
                yield return bot.DropAt(new Vector3(0f, 0f, 10f));
                yield return bot.Until(() => Level11BlockingLasers.AtRest(card), 4f);
                yield return bot.Wait(0.3f);
                Vector3 centre = card.Center;
                float away = card.Scale / looks, down = Player.BaseEyeHeight - centre.y;
                yield return bot.WalkTo(new Vector3(0f, 0f, centre.z - Mathf.Sqrt(away * away - down * down)), 0.03f, 15f);
                yield return bot.Grab(card);
                yield return bot.WalkTo(start, 0.05f, 15f);
                yield return bot.LookAt(Along(start, pitch));
                yield return bot.Wait(0.2f);
            }

            BotRunner.Run(Game, Prepare(), 40f);
            Assert.AreEqual(looks, k, 0.03f, "setup: how big it looks");

            // Flat out for the door, letting go on the way.
            Input = new ScriptedInput();
            Game.Input = Input;
            Input.Hold.MoveZ = 1f;
            Input.Hold.Sprint = true;
            float farthest = 0f, letGo = 0f, top = 0f;
            bool released = false;
            for (int i = 0; i < TestHelpers.Ticks(5f) && level.Rain.Zaps == 0 && !Game.LevelCompleted; i++)
            {
                if (!released && Game.Player.Position.z >= letGoAt)
                {
                    released = true;
                    letGo = card.Scale;
                    GadgetKit.VerticalExtent(card, out _, out top);
                    Input.Once.GrabPressed = true;
                }
                Game.Tick();
                farthest = Mathf.Max(farthest, Game.Player.Position.z);
            }
            Assert.IsTrue(released);
            Assert.Less(letGo, Level11BlockingLasers.SpanScale, "setup: narrower than the lane - it cannot lie on the walls");
            Assert.Greater(letGo, 3.3f, "setup: and long enough to be over the runner most of the way");
            Assert.Greater(top, Level11BlockingLasers.LidY - 0.4f, "setup: let go from right under the lid");
            Assert.AreEqual(1, level.Rain.Zaps, "the card is on the floor before the runner is through");
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Greater(farthest, Level11BlockingLasers.RainNear + 4f, "it did keep them dry most of the way (got to z = " + farthest + ")");
            Assert.Less(farthest, Level11BlockingLasers.RainFar - 0.35f, "and not to the end (got to z = " + farthest + "; the rain ends at 22)");
        }

        // The lean-to: a second way through, found by the third pass and kept (its tables:
        // tools/out/notes/level11-review3-leanto.txt and -hops-leanto.txt). A card too narrow for both
        // walls, let go a step off the middle, comes down with one side on a wall and the other on the
        // lane's floor, and stays there. It is no roof, and is told so, and the outline does not call it
        // one. But it is 4.2 big, it is in the world, and it reaches back over the edge of the rain: under
        // its high side the way is dry to the far wall - the brief's shadow, cast another way. There its
        // low side is across the little door; the last step before the far wall is out of the rain, and
        // picked up from there the card is out of the door's way. The card had to be made big and let go,
        // which is the level; what this pins is that the way under it is no dead end. (Smaller ones begin
        // behind the rain's edge: 4.1 is dry at a sprint only, 3.95 and less not at all.)
        [TestCase(0.8f)]
        [TestCase(-0.8f)]
        public void ACardLeaningOnOneWall_IsAShelterToo_AndTheDoorBehindItIsNoDeadEnd(float standX)
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            Prop card = level.Card;
            var stand = new Vector3(standX, 0f, 8.5f);
            Bot bot = Drop(level, stand, new Vector3(standX, 7.5f, Level11BlockingLasers.FarZ));
            RunSeconds(1.5f);
            string lies = "the card is " + card.Scale + " at " + card.Center + ", " + card.Colliders[0].bounds;
            Assert.IsFalse(level.OnBothWalls, "setup: on one wall only; " + lies);
            Assert.IsFalse(level.Roofed, "setup: no roof; " + lies);
            Assert.IsTrue(Level11BlockingLasers.AtRest(card), "setup: it stays as it fell; " + lies);
            Assert.Less(card.Scale, Level11BlockingLasers.SpanScale, "setup: narrower than the lane");
            float flat = Mathf.Abs(card.Transform.up.y);
            Assert.IsTrue(flat > 0.5f && flat < 0.95f, "setup: leaning (its face is " + flat + " up); " + lies);
            Bounds lean = card.Colliders[0].bounds;
            Assert.Less(lean.min.y, 0.05f, "setup: one side on the floor");
            Assert.Greater(lean.max.y, Level11BlockingLasers.LowWallTop, "setup: the other up on a wall");
            Assert.Less(lean.min.z, Level11BlockingLasers.RainNear + 0.1f, "setup: it reaches back to the edge of the rain");
            CollectionAssert.AreEqual(new[] { Level11BlockingLasers.NarrowLine }, said, "it is told what it is not");
            Assert.AreEqual(FitState.Idle, level.Gauge, "and the outline does not call it a roof");
            Assert.IsTrue(level.Recoverable(Game));

            // Under its high side, seven tenths of a unit from the wall it leans on, to the far wall: dry.
            float side = Mathf.Sign(standX);
            BotRunner.Run(Game, Sequence(bot.WalkTo(new Vector3(side * 2.3f, 0f, 12.5f), 0.2f, 15f), bot.WalkTo(new Vector3(side * 2.3f, 0f, 22.55f), 0.2f, 6f)), 30f);
            Assert.AreEqual(0, level.Rain.Zaps, "under the lean-to the way is dry");
            // Along the far wall to the little door: the card's low side is in the way.
            Assert.IsFalse(Attempt(bot.WalkTo(new Vector3(0f, 0f, 22.6f), 0.2f, 2f), 5f), "nobody walks out from under it (the bot is at " + Game.Player.Position + ")");
            Assert.Greater(Mathf.Abs(Game.Player.Position.x), Level11BlockingLasers.DoorHalfWidth - Player.BaseRadius, "setup: stopped by the card short of the door (at " + Game.Player.Position + ")");
            Assert.AreEqual(0, level.Rain.Zaps);
            // Picked up from there - the last step before the far wall is out of the rain - the door is free.
            bot = new Bot(Game);
            Assert.IsTrue(Attempt(Sequence(bot.Grab(card), bot.WalkTo(new Vector3(0f, 0f, 22.6f), 0.2f, 5f), bot.WalkTo(Level11BlockingLasers.ExitCentre, 0.5f, 5f)), 20f), "the card in the hand, through the door (the bot is at " + Game.Player.Position + ")");
            Assert.AreEqual(0, level.Rain.Zaps, "nobody is zapped for picking it up at the far wall");
            Assert.AreEqual(1, said.Count, "and nothing more was said: " + string.Join(" / ", said));
        }

        // People jump as they walk. The roof's underside is 3.0 and a jump puts the top of the head at
        // 2.95; a card that only just spans the walls can wedge between them a little lower (2.90 to 2.93),
        // and the head reaches that. Nothing comes loose: jumping all the way down the lane, and thirty
        // hops in one place, moved such a card a twentieth of a unit (the third pass's probes,
        // tools/out/notes/level11-review3-jumpunder.txt, -jumpunder-wedged.txt, -hops-leanto.txt).
        // The solver's roof; a snug one, set down on the tops; and one let go at a sprint, which wedges.
        [TestCase(1.75f, false)]
        [TestCase(7.4f, false)]
        [TestCase(7.5f, true)]
        public void JumpingAllTheWayUnderTheRoof_KnocksNothingLoose(float letGoAt, bool onTheRun)
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            Prop card = level.Card;
            Bot bot;
            if (onTheRun)
            {
                // Down the corridor from beside the table at a sprint, the view up at the far wall, letting go on the way.
                bot = new Bot(Game);
                var from = new Vector3(0f, 0f, 3.9f);
                BotRunner.Run(Game, Sequence(bot.Grab(card), GoTo(bot, from, 0.1f), bot.LookAt(Along(from, 16f)), bot.Wait(0.3f)), 20f);
                Input = new ScriptedInput();
                Game.Input = Input;
                Input.Hold.MoveZ = 1f;
                Input.Hold.Sprint = true;
                for (int i = 0; i < 300 && Game.Player.Position.z < letGoAt; i++) Game.Tick();
                Input.Hold = default;
                Input.Once.GrabPressed = true;
                Game.Tick();
                Settle(level);
                bot = new Bot(Game);
            }
            else
            {
                var stand = new Vector3(0f, 0f, letGoAt);
                bot = Drop(level, stand, new Vector3(0f, 7.5f, Level11BlockingLasers.FarZ));
            }
            GadgetKit.VerticalExtent(card, out float bottom, out _);
            string lies = "the card is " + card.Scale + " at " + card.Center + ", its underside at " + bottom;
            Assert.IsTrue(level.Roofed, "setup: a roof; " + lies);
            if (onTheRun) Assert.Less(bottom, 2.96f, "setup: wedged a little under the walls' tops, where a jumping head reaches it; " + lies);
            Vector3 before = card.Center;

            BotRunner.Run(Game, Sequence(GoTo(bot, new Vector3(0f, 0f, 12f), 0.2f), bot.LookAt(new Vector3(0f, Player.BaseEyeHeight, 40f))), 30f);
            Input = new ScriptedInput();
            Game.Input = Input;
            Input.Hold.MoveZ = 1f;
            float highest = 0f;
            for (int i = 0; i < TestHelpers.Ticks(8f) && level.Rain.Zaps == 0 && !Game.LevelCompleted; i++)
            {
                if (i % 14 == 3) Input.Once.Jump = true;
                Game.Tick();
                highest = Mathf.Max(highest, Game.Player.Position.y);
            }
            Input.Hold = default;
            Assert.Greater(highest, 1.1f, "setup: jumped (the feet got to " + highest + ")");
            Assert.AreEqual(0, level.Rain.Zaps, "nobody is zapped for jumping under the roof; " + lies);
            Assert.IsTrue(Game.LevelCompleted, "and the way out is the way out (the player is at " + Game.Player.Position + ")");
            Assert.IsTrue(level.OnBothWalls, "the roof lies where it lay; it is at " + card.Center);
            Assert.Less(Vector3.Distance(before, card.Center), 0.1f, "a head moves it a twentieth of a unit at most");
            Assert.IsEmpty(said);
        }

        // ---- Nothing is lost, continued ----------------------------------------------------------------------------

        // A card let go straight overhead at the start comes down round the player and leans on the spool,
        // over the shoe prints. Zapped after that, the player comes back into it: on their feet, where the
        // start is, free to walk off and to pick it up.
        [Test]
        public void ZappedBackUnderACardThatLiesOverTheStart_ThePlayerStandsAndWalksOff()
        {
            Level11BlockingLasers level = Load();
            Prop card = level.Card;
            var bot = new Bot(Game);
            BotRunner.Run(Game, Sequence(bot.Grab(card), bot.LookAt(Spawn + Vector3.up * 30f), bot.Wait(0.3f), bot.Drop(), bot.Wait(2.5f)), 20f);
            Assert.Greater(card.Mass, Player.Mass, "setup: a card that is solid for the player (it is " + card.Scale + ")");
            Bounds lies = card.Colliders[0].bounds;
            Assert.IsTrue(lies.min.z < Spawn.z && lies.max.z > Spawn.z && Mathf.Abs(lies.center.x) < 1f, "setup: it lies over the start (" + lies + ")");

            IntoTheRain(level, bot, new Vector3(5f, 0f, 12f));
            Assert.AreEqual(1, level.Rain.Zaps);
            float highest = 0f, fastest = 0f;
            for (int i = 0; i < 120; i++)
            {
                Game.Tick();
                highest = Mathf.Max(highest, Game.Player.Position.y);
                fastest = Mathf.Max(fastest, Game.Player.Velocity.magnitude);
            }
            Assert.Less(Vector3.Distance(Game.Player.Position, Spawn), 0.5f, "where the start is (at " + Game.Player.Position + ")");
            Assert.IsTrue(Game.Player.Grounded);
            Assert.Less(highest, 0.5f, "not thrown");
            Assert.Less(fastest, 3f, "nor shot out sideways");
            Assert.IsFalse(card.Frozen, "the card lies where it lay");

            // Off to the side wall, the card picked up from there, and the level solved.
            bot = new Bot(Game);
            float k = 0f;
            Game.Events.PropGrabbed += e => k = e.OldScale / e.GrabDistance;

            IEnumerator Again()
            {
                yield return bot.WalkTo(new Vector3(-4.5f, 0f, 5f), 0.3f, 10f);
                yield return bot.Grab(card);
                // Picked up from the side its long side is across the view: a quarter turn in the hand.
                yield return bot.RotateHeld(6);
                // Where to hold it from follows from how big it looks (the backstop rule), looking well up.
                const float pitch = 22f;
                float z = Level11BlockingLasers.FarZ - 5.6f * (Mathf.Cos(pitch * Mathf.Deg2Rad) + k) / k;
                var stand = new Vector3(0f, 0f, Mathf.Clamp(z, 4.5f, 12f));
                yield return bot.WalkTo(stand, 0.1f, 15f);
                yield return bot.LookAt(Along(stand, pitch));
                yield return bot.Wait(0.3f);
                Assert.AreEqual(FitState.Good, level.Gauge, "picked up again it looks " + k + "; from " + stand + " the card is " + card.Scale + " at " + card.Center);
                yield return bot.Drop();
                yield return bot.Until(() => level.Roofed, 6f);
                yield return level.WalkOut(bot);
            }

            Assert.IsTrue(Attempt(Again()), "picked up again it looks " + k + "; the card is " + card.Scale + " at " + card.Center + "; bot at " + Game.Player.Position);
        }

        // The card one stands on cannot be picked up (the engine lifts nothing from under its player's
        // feet), and nothing shows why: the crosshair does not light up, the click does nothing. One line.
        [Test]
        public void AClickOnTheCardOneStandsOn_IsToldWhy()
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            Prop card = level.Card;
            // A doormat in the lane (held too low), and up on to it.
            Bot bot = Drop(level, Spawn, Along(Spawn, 3f));
            Bounds mat = card.Colliders[0].bounds;
            var on = new Vector3(0f, 0f, mat.min.z + 1.2f);

            IEnumerator StepOn()
            {
                yield return GoTo(bot, new Vector3(0f, 0f, mat.min.z - 0.8f), 0.1f);
                yield return bot.WalkTo(new Vector3(0f, 0f, mat.min.z - 0.35f), 0.05f, 3f);
                yield return bot.Jump();
                yield return bot.WalkTo(on, 0.2f, 4f);
                yield return bot.Wait(0.3f);
                yield return bot.LookAt(on + new Vector3(0f, 0.1f, 1.5f));
                yield return bot.Click();
                yield return bot.Wait(0.3f);
            }

            BotRunner.Run(Game, StepOn(), 30f);
            Assert.AreSame(card, Game.Player.GroundProp, "setup: standing on the card (at " + Game.Player.Position + ")");
            Assert.IsNull(Game.Grabber.Held, "it is not lifted from under the feet");
            CollectionAssert.AreEqual(new[] { Level11BlockingLasers.LowLine, Level11BlockingLasers.StandingLine }, said);

            // A click at something else from up there says nothing; and from beside it the card comes up.
            BotRunner.Run(Game, Sequence(bot.LookAt(on + new Vector3(0f, 6f, 8f)), bot.Click(), bot.Wait(0.2f)), 10f);
            Assert.AreEqual(2, said.Count);
            BotRunner.Run(Game, Sequence(bot.WalkTo(new Vector3(0f, 0f, mat.min.z - 0.8f), 0.2f, 5f), bot.Grab(card)), 15f);
            Assert.AreSame(card, Game.Grabber.Held);
            Assert.AreEqual(2, said.Count, "the pick-up from in front of it needs no words (it looks " + card.Scale + ")");
        }

        // ---- The look ---------------------------------------------------------------------------------------

        // The pixel of a picture (origin bottom left, as Texture2D counts) a world point lands on for the player's view.
        static Vector2Int PixelOf(Player player, Vector3 world, int width, int height)
        {
            Vector3 view = Quaternion.Inverse(Quaternion.Euler(-player.Pitch, player.Yaw, 0f)) * (world - player.Eye);
            Assert.Greater(view.z, 0.5f, world + " is behind the eye");
            float tan = Mathf.Tan(Settings.FieldOfView * 0.5f * Mathf.Deg2Rad);
            float x = view.x / (view.z * tan * width / height), y = view.y / (view.z * tan);
            Assert.IsTrue(Mathf.Abs(x) < 0.95f && Mathf.Abs(y) < 0.95f, world + " is out of the picture");
            return new Vector2Int(Mathf.FloorToInt((x * 0.5f + 0.5f) * width), Mathf.FloorToInt((y * 0.5f + 0.5f) * height));
        }

        static float Luminance(Color srgb)
        {
            Color linear = srgb.linear;
            return 0.2126f * linear.r + 0.7152f * linear.g + 0.0722f * linear.b;
        }

        // The brief's "massive shadow": the lane the roof keeps dry is drawn in shade, on every tier, drawn
        // the way the game draws it (every presenter, the tier's own MSAA). In the first pictures of this
        // level, on Medium (MSAA 2, Direct3D 12, Intel Arc), the shade made the lane 1.7 times BRIGHTER than
        // the floor without it - a pale patch where the shadow should be - and darker only on Low and without
        // MSAA. Toybox/Flat's multiply preset is written Zero, SrcColor since (Sticker.shader's remedy for
        // the same thing). It did not come back afterwards with either spelling, so this guards the look; it
        // is not known to fail on the old one.
        [TestCase(Toybox.Art.QualityTier.Low)]
        [TestCase(Toybox.Art.QualityTier.Medium)]
        [TestCase(Toybox.Art.QualityTier.High)]
        public void TheLaneUnderTheRoof_IsDrawnInShade_OnEveryTier(Toybox.Art.QualityTier tier)
        {
            Assume.That(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null, "needs a graphics device (-nographics run)");
            const int width = 640, height = 360;
            Level11BlockingLasers level = Load();
            Presentation presentation = Presentation.Create(Game, new PresentationOptions { Quality = tier });
            try
            {
                int msaa = Toybox.Render.TierSpec.Of(presentation.Context.Quality).Msaa;
                Camera camera = presentation.Rig.Camera;
                presentation.Frame(0f, 1f);
                // The first render after a script reload is thrown away (see Shots.Warm).
                Object.DestroyImmediate(Toybox.EditorTools.Shots.Photograph(camera, width, height, msaa));

                // The solution, a frame per tick, until the bot is under the roof with the lane ahead of it.
                var bot = new Bot(Game);
                var script = new BotRunner(level.Solve(bot));
                bool running = true;
                Player player = Game.Player;
                for (int tick = 0; tick < TestHelpers.Ticks(20f) && player.Position.z < Level11BlockingLasers.RainNear + 0.3f; tick++)
                {
                    if (running) running = script.Advance();
                    Game.Tick();
                    presentation.Frame(Sim.Dt, 1f);
                }
                Assert.Greater(player.Position.z, Level11BlockingLasers.RainNear + 0.2f, "setup: the bot is in the lane");
                Assert.IsTrue(level.Roofed, "setup: under the roof");
                presentation.Frame(0f, 1f);

                var lasers = presentation.Get<Toybox.Render.LaserVisuals>();
                Assert.IsNotNull(lasers, "the lasers are drawn by their presenter");
                Assert.Greater(lasers.ShadedCount(0), 150, "the ground under every roofed beam is shaded");
                Assert.Greater(lasers.GlowCount(0), 150, "and every beam the roof keeps off the lane is a pinpoint of light on its underside");
                Renderer shade = lasers.ShadeRendererOf(0);
                Texture2D with = Toybox.EditorTools.Shots.Photograph(camera, width, height, msaa);
                shade.forceRenderingOff = true;
                Texture2D without = Toybox.EditorTools.Shots.Photograph(camera, width, height, msaa);
                shade.forceRenderingOff = false;
                try
                {
                    // Three places on the lane's floor well ahead (the bot still looks a little upward), under roofed beams.
                    float ahead = Level11BlockingLasers.RainFar - 1.4f;
                    foreach (Vector3 place in new[] { new Vector3(0f, 0f, ahead), new Vector3(-1.3f, 0f, ahead + 0.4f), new Vector3(1.3f, 0f, ahead + 0.4f) })
                    {
                        Vector2Int pixel = PixelOf(player, place, width, height);
                        float shaded = Luminance(with.GetPixel(pixel.x, pixel.y)), plain = Luminance(without.GetPixel(pixel.x, pixel.y));
                        Assert.Less(shaded, plain * 0.9f, tier + ": the lane at " + place + " should be darker with the shade than without (" + shaded + " against " + plain + ")");
                        Assert.Greater(shaded, plain * 0.4f, tier + ": a shade, not a hole (" + shaded + " against " + plain + ")");
                    }
                    // And past the last row of beams, against the far wall, nothing is shaded.
                    Vector2Int beyond = PixelOf(player, new Vector3(0f, 0f, Level11BlockingLasers.FarZ - 0.35f), width, height);
                    Assert.AreEqual(Luminance(without.GetPixel(beyond.x, beyond.y)), Luminance(with.GetPixel(beyond.x, beyond.y)), 0.01f, "outside the rain the floor is as it was");
                }
                finally
                {
                    Object.DestroyImmediate(with);
                    Object.DestroyImmediate(without);
                }
            }
            finally
            {
                presentation.Dispose();
            }
        }

        // ---- The words ---------------------------------------------------------------------------------------

        [Test]
        public void TheTextIsPlainShortAndInTheCampaignsWords()
        {
            var level = new Level11BlockingLasers();
            string[] lines =
            {
                Level11BlockingLasers.FarLine, Level11BlockingLasers.HeldLine, Level11BlockingLasers.BareLine, Level11BlockingLasers.LowLine,
                Level11BlockingLasers.HighLine, Level11BlockingLasers.FullSizeLine, Level11BlockingLasers.ShortLine, Level11BlockingLasers.SlippedLine,
                Level11BlockingLasers.NarrowLine, Level11BlockingLasers.GapLine, Level11BlockingLasers.CrumbLine, Level11BlockingLasers.LostLine,
                Level11BlockingLasers.TurnedLine, Level11BlockingLasers.OnEndLine, Level11BlockingLasers.NearLine, Level11BlockingLasers.BesideLine,
                Level11BlockingLasers.StandingLine,
            };
            CollectionAssert.AllItemsAreUnique(lines);
            Assert.AreEqual(Level07TeeterTotter.NearLine, Level11BlockingLasers.NearLine, "and the one for a pick-up from too near is Level 7's");
            // What a line asks for is something the player can see and do: where to look, where to stand.
            foreach (string line in lines) StringAssert.DoesNotContain("square", line, "\"hold it square\" says nothing to whoever does not know the card follows the view");
            var all = new List<string>(lines);
            all.AddRange(level.Hints);
            all.Add(level.Blurb);
            foreach (string text in all)
            {
                foreach (char c in text) Assert.IsTrue(c >= ' ' && c <= '~', "not plain ASCII: '" + c + "' in \"" + text + "\"");
                StringAssert.DoesNotContain("grab", text.ToLowerInvariant());
                StringAssert.DoesNotContain("drop", text.ToLowerInvariant());
            }
            foreach (string line in lines) Assert.LessOrEqual(line.Length, 110, "a toast is three lines at most: \"" + line + "\"");
            Assert.AreEqual(Level01CheeseWedge.SmallLine, Level11BlockingLasers.FarLine, "the sentence for a pick-up from too far away is Level 1's");

            string[] hints = level.Hints;
            StringAssert.DoesNotContain("wall", hints[0], "the first hint gives nothing away");
            StringAssert.DoesNotContain("shoe", hints[0]);
            StringAssert.Contains("hand", hints[0], "it names the rule");
            StringAssert.Contains("roof", hints[1]);
            StringAssert.StartsWith("Pick the card up", hints[2], "the third begins with the pick-up");
            StringAssert.Contains("Let go", hints[2], "and says when to let go");
            StringAssert.Contains("green", hints[2], "when the outline says so");
            StringAssert.Contains("shoe prints", hints[2]);
            StringAssert.DoesNotContain("green", hints[0]);
            StringAssert.DoesNotContain("green", hints[1], "the second hint names the idea, not the cue");
            StringAssert.Contains("green", Level11BlockingLasers.LowLine, "the commonest miss is sent to the gauge");
            foreach (string hint in hints) Assert.LessOrEqual(hint.Length, 205, "five lines on the pause card: \"" + hint + "\"");
        }

        // ---- Probes (explicit: they write tables to tools/out/notes) -----------------------------------------

        static string F(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);

        static void WriteNote(string name, StringBuilder text)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../tools/out/notes/" + name));
            File.WriteAllText(path, text.ToString());
            Debug.Log("[Toybox] probe: " + path);
        }

        // One try: pick up (from the start or from a distance), stand, look, let go, wait; then walk for the door.
        string Try(Vector3 stand, float pitch, float yaw, float pickUpFrom, bool walk)
        {
            Level11BlockingLasers level = Load();
            List<string> said = Listen();
            float k = 0f, dropped = 0f;
            Bounds atDrop = default;
            Game.Events.PropGrabbed += e => k = e.OldScale / e.GrabDistance;
            Game.Events.PropDropped += e =>
            {
                dropped = e.NewScale;
                atDrop = level.Card.Colliders[0].bounds;
            };
            string error = "";
            Bot bot = null;
            try
            {
                bot = Drop(level, stand, Along(stand, pitch, yaw), pickUpFrom);
            }
            catch (Exception e)
            {
                error = " ERROR " + e.Message;
            }
            Prop card = level.Card;
            bool onWalls = level.OnBothWalls, roofed = level.Roofed;
            float coverage = level.Rain.Coverage01, gap = level.LongestGap(Game);
            Vector3 rest = card.Center;
            float scale = card.Scale, up = card.Transform.up.y;
            bool completed = false;
            if (walk && bot != null && error == "")
            {
                completed = Attempt(level.WalkOut(bot), 40f);
            }
            string line = "z " + F(stand.z) + " pitch " + F(pitch) + " yaw " + F(yaw) + " from " + F(pickUpFrom) + " | k " + k.ToString("0.000", CultureInfo.InvariantCulture) +
                          " drop " + F(dropped) + " y " + F(atDrop.min.y) + ".." + F(atDrop.max.y) + " z " + F(atDrop.min.z) + ".." + F(atDrop.max.z) +
                          " | rest " + F(scale) + " at (" + F(rest.x) + ", " + F(rest.y) + ", " + F(rest.z) + ") up " + up.ToString("0.000", CultureInfo.InvariantCulture) +
                          " walls " + (onWalls ? 1 : 0) + " cover " + F(coverage) + " gap " + F(gap) + " roof " + (roofed ? 1 : 0) +
                          (walk ? " done " + (completed ? 1 : 0) + " zaps " + level.Rain.Zaps : "") +
                          " | " + string.Join(" / ", said) + error;
            Game.Dispose();
            Game = null;
            return line;
        }

        [Test, Explicit]
        public void Probe_Grid()
        {
            var text = new StringBuilder();
            foreach (float z in new[] { -1.6f, 0f, 1.75f, 4.5f, 6f, 7f, 8f, 10f })
            {
                foreach (float pitch in new[] { 5f, 8f, 10f, 12f, 14.4f, 18f, 21f, 23f, 26f, 32f })
                    text.AppendLine(Try(new Vector3(0f, 0f, z), pitch, 0f, 0f, false));
                text.AppendLine();
            }
            WriteNote("level11-probe-grid.txt", text);
        }

        [Test, Explicit]
        public void Probe_Yaw()
        {
            var text = new StringBuilder();
            foreach (float z in new[] { -1.6f, 1.75f, 6f })
            {
                foreach (float yaw in new[] { 0f, 2f, 3f, 4f, 5f, 6f, 8f, 10f, 14f, 20f })
                    text.AppendLine(Try(new Vector3(0f, 0f, z), SolverPitch, yaw, 0f, true));
                text.AppendLine();
            }
            WriteNote("level11-probe-yaw.txt", text);
        }

        [Test, Explicit]
        public void Probe_PickUp()
        {
            var text = new StringBuilder();
            foreach (float from in new[] { 0.95f, 1.1f, 1.25f, 1.5f, 1.8f, 2.1f, 2.4f, 2.8f, 3.4f, 4.5f })
            {
                foreach (float z in new[] { -1.6f, 1f, 1.75f })
                    text.AppendLine(Try(new Vector3(0f, 0f, z), SolverPitch, 0f, from, true));
                text.AppendLine();
            }
            WriteNote("level11-probe-pickup.txt", text);
        }
    }

    /// <summary>
    /// Level 11 on the pause card: every hint is set at the panel's full size, as in the levels before it
    /// (Phase2CampaignTests.EveryHint_FitsThePauseCardsPanel_AtFullSize does the same for levels 5 to 9).
    /// </summary>
    public class Level11UiTests
    {
        GameObject host;
        GameRunner runner;

        [SetUp]
        public void NewStores()
        {
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

        [Test]
        public void EveryHint_FitsThePauseCardsPanel_AtFullSize()
        {
            host = new GameObject("Level 11 Test Runner") { hideFlags = HideFlags.DontSave };
            runner = host.AddComponent<GameRunner>();
            var devices = new FakeDevices();
            runner.Begin(LaunchOptions.FromUrl("?level=11"), new RunnerOptions { Devices = devices, Store = new MemoryStore(), Presenters = UiTestKit.Own() });
            MenuPresenter menu = runner.Presentation.Get<MenuPresenter>();
            Assert.IsNotNull(menu, "the menu presenter attached");
            for (int i = 0; i < 30; i++) runner.Frame(Sim.Dt);
            devices.State.EscapePressed = true;
            runner.Frame(Sim.Dt);
            devices.State.EscapePressed = false;
            PauseMenu pause = menu.Pause;
            Assert.AreSame(pause, menu.Current, "paused");
            Assert.AreEqual(11, runner.Game.Level.Id);
            string[] hints = runner.Game.Level.Hints;
            for (int h = 0; h < hints.Length; h++)
            {
                pause.NextHint();
                runner.Frame(Sim.Dt);
                Assert.AreEqual(hints[h], pause.HintText.text);
                pause.HintText.ForceMeshUpdate();
                float size = pause.HintText.fontSize;
                Debug.Log("[Toybox] Level 11, hint " + (h + 1) + ": " + hints[h].Length + " characters, " + pause.HintText.textInfo.lineCount + " lines at size " + size.ToString("0.0"));
                Assert.Greater(pause.HintText.textInfo.lineCount, 0, "the text was laid out");
                Assert.GreaterOrEqual(size, UiTheme.BodySize - 0.05f, "hint " + (h + 1) + " is set smaller to fit (" + hints[h].Length + " characters): \"" + hints[h] + "\"");
                Assert.IsFalse(pause.HintText.isTextOverflowing, "hint " + (h + 1) + " is cut off");
            }
        }
    }
}
