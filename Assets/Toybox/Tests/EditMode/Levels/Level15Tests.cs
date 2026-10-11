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
using Toybox.Render;
using Toybox.UI;
using UnityEngine;

namespace Toybox.Tests
{
    /// <summary>
    /// Level 15, "The Rube Goldberg Machine": four toys on four stations, one pedal, and a run that goes as
    /// far as the sizes let it. Measurements are in tools/out/notes/level15-build.md.
    /// </summary>
    public class Level15Tests : SimTest
    {
        Level15RubeGoldberg level;
        Bot bot;
        string lastError;

        void Load()
        {
            Game = Game.Create();
            Game.LoadLevel(15);
            level = (Level15RubeGoldberg)Game.Level;
            bot = new Bot(Game);
        }

        void Unload()
        {
            Game.Dispose();
            Game = null;
        }

        // Pumps a bot script for at most this long. True if it ran to its end; a command that fails ends it
        // too (the message is kept in lastError), and that is not an error here.
        bool Drive(IEnumerator script, float seconds)
        {
            lastError = null;
            var runner = new BotRunner(script, bot, Game);
            try
            {
                for (int i = TestHelpers.Ticks(seconds); i > 0; i--)
                {
                    if (!runner.Advance()) return true;
                    Game.Tick();
                }
                lastError = "ran out of time after " + seconds + " s";
            }
            catch (BotException e)
            {
                lastError = e.Message;
            }
            return false;
        }

        List<string> Listen()
        {
            var said = new List<string>();
            Game.Events.Message += e => said.Add(e.Text);
            return said;
        }

        static string F(float value) => value.ToString("0.000", CultureInfo.InvariantCulture);
        static string F(Vector3 v) => "(" + v.x.ToString("0.00", CultureInfo.InvariantCulture) + ", " + v.y.ToString("0.00", CultureInfo.InvariantCulture) + ", " + v.z.ToString("0.00", CultureInfo.InvariantCulture) + ")";

        static void Note(string name, string text)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../tools/out/notes/level15-probe-" + name + ".txt"));
            File.WriteAllText(path, text);
        }

        // Every gadget event of a run, with its time, as lines of text.
        StringBuilder Trace()
        {
            var log = new StringBuilder();
            void Line(string what) => log.AppendLine(F(Game.Time) + "  " + what);
            GameEvents events = Game.Events;
            events.Message += e => Line("SAY " + e.Text);
            events.SocketSeated += e => Line("seated " + e.Gadget.Name + " " + (e.Prop != null ? e.Prop.Name + " s=" + F(e.Prop.Scale) + " at " + F(e.Prop.Position) : ""));
            events.SocketUnseated += e => Line("unseated " + e.Gadget.Name);
            events.SocketRejected += e => Line("rejected " + e.Gadget.Name + " " + (FitState)e.Index + " s=" + F(e.Value));
            events.PropGrabbed += e => Line("grab " + e.Prop.Name + " s=" + F(e.OldScale) + " g=" + F(e.GrabDistance) + " k=" + F(e.OldScale / e.GrabDistance));
            events.PropDropped += e => Line("drop " + e.Prop.Name + " s=" + F(e.NewScale) + " d=" + F(e.DropDistance) + " at " + F(e.Prop.Center));
            events.PropRespawned += e => Line("respawn " + e.Prop.Name);
            events.MachineStarted += e => Line("MACHINE started");
            events.MachineFizzled += e => Line("MACHINE fizzled after " + e.Text);
            events.MachineReset += e => Line("MACHINE reset");
            events.MachineDone += e => Line("MACHINE done");
            events.DoorOpened += e => Line("door opened " + e.Gadget.Name);
            events.DoorClosed += e => Line("door closed " + e.Gadget.Name);
            events.PathSegmentEnded += e => Line("path " + e.Gadget.Name + " leg " + e.Index + " at " + F(e.Position));
            events.PathFinished += e => Line("path " + e.Gadget.Name + " finished at " + F(e.Position));
            events.SeesawStruck += e => Line("seesaw struck M=" + F(e.Value) + " f=" + F(e.Value2));
            events.SeesawProjectile += e => Line("seesaw projectile v=" + F(e.Value) + " from " + F(e.Position));
            events.SeesawReturned += e => Line("seesaw returned");
            events.WindChanged += e => Line("wind " + F(e.Value));
            events.ChainPieceFell += e => Line("domino " + (e.Index + 1) + " fell");
            events.ChainTargetStruck += e => Line("target struck");
            events.ChainFellShort += e => Line("chain fell short");
            events.BreakableBroke += e => Line("BROKE " + e.Gadget.Name + " speed " + F(e.Value));
            events.BreakableBonked += e => Line("bonk " + e.Gadget.Name);
            events.PortEjected += e => Line("port ejected " + (e.Prop != null ? e.Prop.Name : ""));
            events.LeashReturned += e => Line("leash returned " + (e.Prop != null ? e.Prop.Name : ""));
            events.LevelCompleted += e => Line("LEVEL COMPLETED");
            return log;
        }

        // ---- Fixtures --------------------------------------------------------------------------------------------------

        // A test fixture's shortcut for a perfect placement: the toy at a size, let go at a pose inside its station.
        // (Not something a bot script may do: these run between ticks, outside any BotRunner.)
        void Put(Socket socket, Prop prop, float scale, Vector3 position, Quaternion rotation)
        {
            if (socket.SeatedProp == prop) socket.Release();
            if (prop.Frozen) prop.Unfreeze();
            // Out of the station for a tick, as a toy that was picked up is: the next let-go is judged afresh.
            prop.SetPose(new Vector3(0f, 6f, -2f), rotation);
            Game.Tick();
            prop.SetScale(scale);
            prop.SetPose(position, rotation);
        }

        void SeatBall(float scale)
        {
            Put(level.Hopper, level.Ball, scale, Level15RubeGoldberg.HopperFoot + Vector3.up * (0.5f * scale + 0.8f), Quaternion.identity);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Hopper.Seated, 2f), "the hopper takes a ball of " + scale);
        }

        void SeatDominoes(float scale)
        {
            Pose seat = Level15RubeGoldberg.DominoSeat(scale);
            Put(level.Lane, level.Dominoes, scale, seat.position + new Vector3(-1f, 0.6f, 0.2f), seat.rotation);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Lane.Seated, 2f), "the lane takes dominoes of " + scale);
        }

        void SeatRuler(float scale)
        {
            Pose seat = Level15RubeGoldberg.RulerSeat(scale);
            Put(level.Fulcrum, level.Ruler, scale, new Vector3(-4.6f, 1.3f, Level15RubeGoldberg.AxisZ), Quaternion.Euler(0f, 90f, 0f));
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Fulcrum.Seated && level.Seesaw != null, 2f), "the fulcrum takes a ruler of " + scale);
            Assert.Less(Vector3.Distance(level.Ruler.Position, seat.position), 0.01f);
        }

        void SeatFan(float scale)
        {
            Put(level.Collar, level.Fan, scale, Level15RubeGoldberg.FanSeat + Vector3.up * 0.1f, Quaternion.identity);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Collar.Seated, 2f), "the garage takes a fan of " + scale);
        }

        // All four at the solver's sizes (or the ones given).
        void SeatAll(float dominoes = 3.04f, float ball = 3f, float ruler = 1.03f, float fan = 0.93f)
        {
            SeatDominoes(dominoes);
            SeatBall(ball);
            SeatRuler(ruler);
            SeatFan(fan);
        }

        // Puts the player on the pedal (a fixture's shortcut for the walk), and off it again.
        void StepOnThePedal()
        {
            Game.Player.Teleport(Level15RubeGoldberg.PedalPoint, 0f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Pedal.Pressed, 1f), "the pedal goes down under the player");
        }

        void StepOffThePedal()
        {
            Game.Player.Teleport(Level15RubeGoldberg.PedalPoint + new Vector3(0f, 0f, -3f), 0f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => !level.Pedal.Pressed, 1f));
        }

        // One press, and what came of it: true if the clock was crushed, false if the machine set itself up again.
        bool RunTheMachine(float seconds = 30f)
        {
            int runs = level.Director.Runs;
            StepOnThePedal();
            Assert.AreEqual(runs + 1, level.Director.Runs, "the press starts a run");
            StepOffThePedal();
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Director.State == MachineState.Done || level.Director.State == MachineState.Idle, seconds),
                "the run ends one way or the other (state " + level.Director.State + ", last link " + level.Director.LastLink + ")");
            return level.Director.State == MachineState.Done;
        }

        static bool IsAscii(string text)
        {
            foreach (char c in text)
                if (c < 32 || c > 126) return false;
            return true;
        }

        // ---- The solution -------------------------------------------------------------------------------------------------

        [Test]
        public void TheBotSolvesTheLevel()
        {
            Load();
            Assert.AreEqual("rube-goldberg", level.Slug);
            Assert.AreEqual(3, level.Phase);
            Assert.AreEqual("night-light", level.Environment);
            Assert.IsTrue(level.Exit.Locked);
            TestHelpers.PlayLevel(Game, 120f);
            Assert.IsTrue(level.Clock.Broken);
            Assert.IsFalse(level.Exit.Locked);
            Assert.Less(Game.Time, 60f, "the solver takes under a minute of game time (" + Game.Time + ")");
        }

        [Test]
        public void TheSolversSizes_AreTheOnesTheSpecAsksFor_AndTheLinksFireInOrder()
        {
            Load();
            var fell = new List<int>();
            Game.Events.ChainPieceFell += e => fell.Add(e.Index);
            var said = Listen();

            Assert.IsTrue(Drive(level.PlaceDominoes(bot), 30f), lastError);
            Assert.AreEqual(3.02f, level.Dominoes.Scale, 0.15f, "LEVELS: the dominoes come out at 3.02 against the wall above the lane");
            Assert.Less(Level15RubeGoldberg.FirstDominoX(level.Dominoes.Scale), Level15RubeGoldberg.WindMaxX, "domino 1 stands in the wind");
            Assert.IsTrue(Drive(level.PlaceBall(bot), 40f), lastError);
            Assert.AreEqual(3f, level.Ball.Scale, 0.01f, "LEVELS: the ball stops at its largest size above the funnel");
            Assert.AreEqual(56.5f, level.Ball.Mass, 0.5f);
            Assert.IsTrue(Drive(level.ToTheTrench(bot), 30f), lastError);
            Assert.IsTrue(Drive(level.PlaceRuler(bot), 30f), lastError);
            Assert.That(level.Ruler.Scale, Is.InRange(0.9f, 1.15f), "the ruler is put down about as big as it was picked up");
            Assert.IsNotNull(level.Seesaw, "the seated ruler is the lever");
            Assert.IsTrue(Drive(level.PlaceFan(bot), 30f), lastError);
            Assert.AreEqual(0.94f, level.Fan.Scale, 0.06f, "LEVELS: the fan comes out at 0.94 on the garage floor");
            Assert.IsTrue(Drive(level.ToThePedal(bot), 30f), lastError);
            Assert.IsTrue(Drive(bot.Until(() => level.Clock.Broken, 25f), 26f), lastError);

            string[] expected =
            {
                Level15RubeGoldberg.LinkGate, Level15RubeGoldberg.LinkChute, Level15RubeGoldberg.LinkDrum, Level15RubeGoldberg.LinkBall,
                Level15RubeGoldberg.LinkStrike, Level15RubeGoldberg.LinkMarble, Level15RubeGoldberg.LinkSwitch, Level15RubeGoldberg.LinkFan,
                Level15RubeGoldberg.LinkWind,
                Level15RubeGoldberg.LinkDomino, Level15RubeGoldberg.LinkDomino, Level15RubeGoldberg.LinkDomino, Level15RubeGoldberg.LinkDomino,
                Level15RubeGoldberg.LinkDomino, Level15RubeGoldberg.LinkDomino, Level15RubeGoldberg.LinkDomino,
                Level15RubeGoldberg.LinkTarget, Level15RubeGoldberg.LinkBaseball, Level15RubeGoldberg.LinkBaseball, Level15RubeGoldberg.LinkBaseball,
                Level15RubeGoldberg.LinkClock,
            };
            CollectionAssert.AreEqual(expected, level.Director.Fired, "every link once, in chain order: " + string.Join(",", level.Director.Fired));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5, 6 }, fell);
            Assert.AreEqual(MachineState.Done, level.Director.State);
            Assert.AreEqual(0.92f, level.LastF, 0.02f, "LEVELS: f = (56.5 - 2 m) / (56.5 + 4 m)");
            Assert.AreEqual(6.8f, level.MarbleApex, 0.2f, "LEVELS: the marble tops out at 6.8");
            Assert.IsTrue(level.MarbleThrough);
            Assert.AreEqual(2.4f, level.Wind.Strength, 0.3f, "LEVELS: the fan blows with (0.94 / 0.6)^2");
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.DoneLine }, said, "nothing is said in a clean solve but the ending");
            Assert.IsTrue(Drive(level.ToTheExit(bot), 40f), lastError);
            Assert.IsTrue(Game.LevelCompleted);
        }

        [Test]
        public void TheFirstPicture_HasTheDominoesUnderTheCrosshair_AStepAway()
        {
            Load();
            Assert.AreSame(level.Dominoes, Game.Grabber.FindTarget(), "the first click picks the dominoes up");
            float ratio = level.Dominoes.Scale / Vector3.Distance(Game.Player.Eye, level.Dominoes.Center);
            Assert.AreEqual(0.353f, ratio, 0.01f, "LEVELS' own pick-up: 0.6 from 1.70 away");
            foreach (Prop prop in Game.Props)
                Assert.AreEqual(prop == level.Dominoes || prop == level.Ball || prop == level.Ruler || prop == level.Fan, prop.Grabbable, prop.Name);
            Assert.IsTrue(level.Ball.Frozen, "the ball waits on its spool");
            Assert.IsTrue(level.Baseball.Frozen, "the baseball waits in its putty");
        }

        [Test]
        public void TheWords_ArePlain_Short_AndTheCampaigns()
        {
            Load();
            Assert.AreEqual(3, level.Hints.Length);
            foreach (string hint in level.Hints)
            {
                Assert.IsTrue(IsAscii(hint), hint);
                Assert.LessOrEqual(hint.Length, 205, "five lines on the pause card: " + hint);
            }
            StringAssert.DoesNotContain("wall", level.Hints[0], "the first hint gives no place away");
            StringAssert.StartsWith("Pick up", level.Hints[2]);
            StringAssert.Contains("let go", level.Hints[2]);
            StringAssert.Contains("quilt's edge", level.Hints[2], "the third hint says where to let go from");
            StringAssert.Contains("garage door", level.Hints[2], "and where the fan is picked up from (it is not in the garage)");
            Assert.IsTrue(IsAscii(level.Blurb));
            string[] lines =
            {
                Level15RubeGoldberg.FarLine, Level15RubeGoldberg.NearLine, Level15RubeGoldberg.BallSmallLine, Level15RubeGoldberg.RulerSmallLine,
                Level15RubeGoldberg.RulerTurnLine, Level15RubeGoldberg.FanBigLine, Level15RubeGoldberg.DominoSmallLine, Level15RubeGoldberg.DominoTurnLine,
                Level15RubeGoldberg.ParkingLine, Level15RubeGoldberg.GateLine, Level15RubeGoldberg.PivotLine, Level15RubeGoldberg.MarbleLine,
                Level15RubeGoldberg.MarbleWideLine, Level15RubeGoldberg.PlayerLine, Level15RubeGoldberg.FanMissingLine, Level15RubeGoldberg.FanSmallLine,
                Level15RubeGoldberg.WindLine, Level15RubeGoldberg.ShortLine, Level15RubeGoldberg.DoneLine,
                Level15RubeGoldberg.WindShortLine, Level15RubeGoldberg.DominoTurnShortLine, Level15RubeGoldberg.DominoEdgeLine,
                Level15RubeGoldberg.BallTowerLine, Level15RubeGoldberg.RulerMissLine, Level15RubeGoldberg.FanMissLine,
                Level15RubeGoldberg.DominoFullLine, Level15RubeGoldberg.BallFullLine, Level15RubeGoldberg.RiderLine,
                Level15RubeGoldberg.OtherWeightLine, Level15RubeGoldberg.ShortArmLine,
            };
            foreach (string line in lines)
            {
                Assert.IsTrue(IsAscii(line), line);
                Assert.LessOrEqual(line.Length, 110, line);
                StringAssert.DoesNotContain("grab", line.ToLowerInvariant());
                StringAssert.DoesNotContain("drop ", line.ToLowerInvariant());
            }
            Assert.AreEqual("It only ever gets as big as it looks. Pick it up from closer.", Level15RubeGoldberg.FarLine, "the sentence of Level 1");
        }

        // ---- No way round the machine ----------------------------------------------------------------------------------------

        [Test]
        public void WalkingToTheExit_WithoutRunningTheMachine_GetsNowhere()
        {
            Load();
            Assert.IsTrue(Drive(level.ToTheTrench(bot), 30f), lastError);
            // Straight at the door, sprinting and jumping at it.
            Drive(bot.WalkTo(new Vector3(27f, 0f, Level15RubeGoldberg.ExitCentre.z), 0.3f, 12f, true), 13f);
            Drive(Ram(new Vector3(Level15RubeGoldberg.ExitCentre.x, 0f, Level15RubeGoldberg.ExitCentre.z)), 4f);
            Assert.IsFalse(Game.LevelCompleted, "the door is shut");
            Assert.Less(Game.Player.Position.x, Level15RubeGoldberg.HalfWidth, "and nobody gets through it");
            Assert.IsTrue(level.ExitDoor.IsClosed);
            // Even inside the exit's box nothing happens while the clock is whole.
            Game.Player.Teleport(Level15RubeGoldberg.ExitCentre - Vector3.up * 1.5f, 90f);
            RunSeconds(1f);
            Assert.IsFalse(Game.LevelCompleted, "the exit is locked until the clock is crushed");
        }

        IEnumerator Ram(Vector3 at)
        {
            for (int i = 0; i < 6; i++)
            {
                yield return bot.WalkTo(at, 0.2f, 0.5f, true);
                yield return bot.Jump();
            }
        }

        [Test]
        public void OnlyTheBaseball_CrushesTheClock_AndThePlayerIsSentBackOutOfTheJar()
        {
            Load();
            // The ball, as heavy as it gets, let go right over the clock inside the jar (it cannot get in there by itself).
            level.Ball.Unfreeze();
            level.Ball.SetScale(2.2f);
            level.Ball.SetPose(Level15RubeGoldberg.ClockCentre + Vector3.up * 3.3f, Quaternion.identity);
            RunSeconds(2f);
            Assert.IsFalse(level.Clock.Broken, "a ball of 22 that falls on the clock does nothing to it");
            Assert.IsTrue(level.Exit.Locked);
            // No parking in the jar: the ball goes back to its spool.
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Ball.Frozen, 4f), "and it does not stay in there");
            Assert.AreEqual(Level15RubeGoldberg.BallStart, level.Ball.Scale, 1e-4f);

            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            Game.Player.Teleport(new Vector3(Level15RubeGoldberg.JarX + 1.1f, 0.2f, Level15RubeGoldberg.AxisZ), 0f);
            RunSeconds(0.5f);
            Assert.AreEqual(1, respawns, "a player inside the bell jar is sent back");
            Assert.Less(Vector3.Distance(Game.Player.Position, Level15RubeGoldberg.SpawnPoint), 0.5f);
        }

        // ---- The machine as its own diagnostic ------------------------------------------------------------------------------

        [Test]
        public void AnEmptyMachine_OpensItsGate_SaysSo_AndSetsItselfUpAgain()
        {
            Load();
            var said = Listen();
            Assert.IsFalse(RunTheMachine(), "nothing to run");
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.GateLine }, said);
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.LinkGate }, level.Director.Fired);
            Assert.IsTrue(level.Gate.IsClosed, "the gate is down again");
            Assert.IsTrue(level.Dominoes.Grabbable && level.Ball.Grabbable && level.Ruler.Grabbable && level.Fan.Grabbable, "the toys are the player's");
            Assert.IsFalse(RunTheMachine(), "and it runs again: never a softlock");
            Assert.AreEqual(2, level.Director.Runs);
        }

        [Test]
        public void APressDuringARun_OrWhileItSetsItselfUp_IsIgnored()
        {
            Load();
            StepOnThePedal();
            StepOffThePedal();
            Assert.AreEqual(MachineState.Running, level.Director.State);
            StepOnThePedal();
            Assert.AreEqual(1, level.Director.Runs);
            StepOffThePedal();
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Director.State == MachineState.Resetting, 4f));
            StepOnThePedal();
            Assert.AreEqual(1, level.Director.Runs);
            StepOffThePedal();
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Director.State == MachineState.Idle, 3f));
        }

        // The ball's window (LEVELS: works from 2.25 to its largest size, 3.0; at 2.0 the marble dies under the hoop).
        // (The review: the builder's gauge was green from 2.15, and a ball of 2.15 on a ruler of 0.8 fell short of
        // the builder's 6.2 by 0.005. Green is LEVELS' 2.25 again, and that works on every ruler with 0.05 to spare.)
        [TestCase(2.25f, 0.8f)]
        [TestCase(2.25f, 1f)]
        [TestCase(2.25f, 1.2f)]
        [TestCase(2.6f, 1.03f)]
        [TestCase(3f, 0.8f)]
        [TestCase(3f, 1.2f)]
        public void ABallInItsWindow_FlipsTheMarbleThroughTheHoop(float ball, float ruler)
        {
            Load();
            SeatAll(ball: ball, ruler: ruler);
            Assert.IsTrue(RunTheMachine(), "ball " + ball + " on a ruler of " + ruler + ": apex " + level.MarbleApex + ", stopped after " + level.Director.LastLink + " (" + level.Director.LastMessage + ")");
            Assert.GreaterOrEqual(level.MarbleApex, Level15RubeGoldberg.HoopNeed + 0.04f, "and not by a hair");
            Assert.IsFalse(level.Exit.Locked);
        }

        [TestCase(2f, 0.8f)]
        [TestCase(2f, 1.2f)]
        [TestCase(1.3f, 1.03f)]
        public void ABallThatIsTooLight_ShowsTheMarbleFallingShort_AndTheMachineSetsItselfUp(float ball, float ruler)
        {
            Load();
            var said = Listen();
            int resets = 0;
            Game.Events.MachineReset += e => resets++;
            SeatAll(ball: ball, ruler: ruler);
            Assert.IsFalse(RunTheMachine(), "a ball of " + ball + " is too light (apex " + level.MarbleApex + ")");
            Assert.Less(level.MarbleApex, Level15RubeGoldberg.HoopNeed);
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.MarbleLine }, said);
            Assert.AreEqual(Level15RubeGoldberg.LinkMarble, level.Director.LastLink);
            Assert.AreEqual(1, resets);
            Assert.IsTrue(level.Exit.Locked);

            // Everything is where the player put it: the ball back in the hopper at its size, the others seated.
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Hopper.Seated, 1f), "the ball was lifted back into the hopper");
            Assert.AreEqual(ball, level.Ball.Scale, 1e-4f);
            Assert.IsTrue(level.Lane.Seated && level.Collar.Seated && level.Fulcrum.Seated);
            Assert.IsFalse(level.MarbleFlying, "the marble is back in its cap");
            Assert.IsTrue(level.Ball.Grabbable);

            // The proof that this is never a softlock: make the ball bigger and press again.
            SeatBall(3f);
            Assert.IsTrue(RunTheMachine(), "with the ball re-sized the same machine runs through (stopped after " + level.Director.LastLink + ": " + level.Director.LastMessage + ")");
        }

        // The dominoes' window (LEVELS: 2.2 to 3.5; smaller, domino 1 stands outside the wind).
        [TestCase(2.2f)]
        [TestCase(2.6f)]
        [TestCase(3.5f)]
        public void DominoesInTheirWindow_FallAllTheWayToTheBaseball(float scale)
        {
            Load();
            SeatAll(dominoes: scale);
            Assert.LessOrEqual(Level15RubeGoldberg.FirstDominoX(scale), Level15RubeGoldberg.WindMaxX);
            Assert.IsTrue(level.Chain.Reaches());
            Assert.IsTrue(RunTheMachine(40f), "dominoes of " + scale + ": stopped after " + level.Director.LastLink + " (" + level.Director.LastMessage + ")");
        }

        [TestCase(1.1f)]
        [TestCase(1.6f)]
        [TestCase(2.05f)]
        public void DominoesThatAreTooSmall_StandOutsideTheWind_AndTheRunSaysSo(float scale)
        {
            Load();
            var said = Listen();
            SeatAll(dominoes: scale);
            Assert.Greater(Level15RubeGoldberg.FirstDominoX(scale), Level15RubeGoldberg.WindMaxX);
            Assert.IsFalse(RunTheMachine());
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.WindShortLine }, said);
            Assert.AreEqual(ChainState.Idle, level.Chain.State, "no domino fell");
            Assert.AreEqual(0f, level.Wind.Strength, "the fan is off again");
            Assert.IsFalse(level.SwitchClosed);
            // Bigger, and the same machine runs through.
            SeatDominoes(3f);
            Assert.IsTrue(RunTheMachine(40f), level.Director.LastMessage);
        }

        // The fan's window (LEVELS: 0.6 to 1.2; the garage takes it from 0.5).
        [TestCase(0.6f)]
        [TestCase(1.2f)]
        public void AFanInItsWindow_BlowsTheSailOver(float scale)
        {
            Load();
            SeatAll(fan: scale);
            Assert.IsTrue(RunTheMachine(40f), "a fan of " + scale + ": stopped after " + level.Director.LastLink + " (" + level.Director.LastMessage + ")");
            Assert.GreaterOrEqual(level.Wind.Strength, 1f);
        }

        [Test]
        public void AFanThatIsTooSmall_Wheezes_AndOneThatIsMissing_Clicks()
        {
            Load();
            var said = Listen();
            SeatAll(fan: 0.5f);
            Assert.IsFalse(RunTheMachine());
            Assert.AreEqual(Level15RubeGoldberg.FanSmallLine, said[said.Count - 1]);
            Assert.AreEqual(Level15RubeGoldberg.LinkFan, level.Director.LastLink);

            // Take the fan out of its garage altogether.
            level.Collar.Release();
            level.Fan.SetPose(new Vector3(4f, 0.1f, 13f), Quaternion.identity);
            RunSeconds(0.5f);
            Assert.IsFalse(level.Collar.Seated);
            Assert.IsFalse(RunTheMachine());
            Assert.AreEqual(Level15RubeGoldberg.FanMissingLine, said[said.Count - 1]);
            Assert.AreEqual(Level15RubeGoldberg.LinkSwitch, level.Director.LastLink);

            SeatFan(0.9f);
            Assert.IsTrue(RunTheMachine(40f), level.Director.LastMessage);
        }

        [Test]
        public void WithoutTheRuler_TheBallLandsOnABarePivot()
        {
            Load();
            var said = Listen();
            SeatDominoes(3f);
            SeatBall(3f);
            SeatFan(0.9f);
            Assert.IsFalse(RunTheMachine());
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.PivotLine }, said);
            Assert.AreEqual(Level15RubeGoldberg.LinkBall, level.Director.LastLink);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Hopper.Seated, 1f), "the ball is back in the hopper");
            SeatRuler(1f);
            Assert.IsTrue(RunTheMachine(40f), level.Director.LastMessage);
        }

        [Test]
        public void TheRulersWindow_IsWhatTheFulcrumTakes()
        {
            Load();
            Assert.AreEqual(FitState.TooSmall, level.Fulcrum.Fit(0.75f));
            Assert.AreEqual(FitState.Good, level.Fulcrum.Fit(0.8f));
            Assert.AreEqual(FitState.Good, level.Fulcrum.Fit(Level15RubeGoldberg.RulerMax));
            Assert.AreEqual(Level15RubeGoldberg.FulcrumMaxScale, Level15RubeGoldberg.RulerMax, "too far back is harmless: its largest size still fits");
            // Lying the wrong way round (cap toward the books) it is refused, and told.
            var said = Listen();
            Put(level.Fulcrum, level.Ruler, 1f, new Vector3(-4.6f, 1.3f, Level15RubeGoldberg.AxisZ), Quaternion.Euler(0f, -90f, 0f));
            RunSeconds(1f);
            Assert.IsFalse(level.Fulcrum.Seated);
            CollectionAssert.Contains(said, Level15RubeGoldberg.RulerTurnLine);
            // Too small it is refused as well.
            said.Clear();
            Put(level.Fulcrum, level.Ruler, 0.7f, new Vector3(-4.6f, 1.3f, Level15RubeGoldberg.AxisZ), Quaternion.Euler(0f, 90f, 0f));
            RunSeconds(1f);
            Assert.IsFalse(level.Fulcrum.Seated);
            CollectionAssert.Contains(said, Level15RubeGoldberg.RulerSmallLine);
        }

        // ---- What the player's own hands do: where a toy is picked up from, where it is let go ------------------------

        // The dominoes' window by hand (LEVELS: 2.2 to 3.5, picked up from 1.4 to 2.4 away).
        [TestCase(4.3f, 3.1f, TestName = "TheDominoes_FromTheStart_ComeOutAtThree_AndTheMachineRuns")]
        [TestCase(3.6f, 2.31f, TestName = "TheDominoes_FromTwoAndAHalfAway_TheLowEnd_AndTheMachineRuns")]
        [TestCase(4.7f, 3.5f, TestName = "TheDominoes_FromOneStepNearer_TheirLargestSize_AndTheMachineRuns")]
        [TestCase(5.3f, 3.5f, TestName = "TheDominoes_FromBesideTheirBook_HangShortOfTheWall_AreTakenAllTheSame")]
        public void TheDominoesByHand(float pickZ, float scale)
        {
            Load();
            var said = Listen();
            Assert.IsTrue(Drive(DominoesFrom(pickZ, 11.1f, Level15RubeGoldberg.DominoAim), 30f), lastError);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Lane.Seated, 2f), "the lane has them (they are at " + level.Dominoes.Center + ")");
            Assert.AreEqual(scale, level.Dominoes.Scale, 0.06f);
            Assert.GreaterOrEqual(level.Dominoes.Scale, Level15RubeGoldberg.DominoWorks);
            CollectionAssert.IsEmpty(said, "nothing needs saying");
            SeatBall(3f);
            SeatRuler(1.03f);
            SeatFan(0.93f);
            Assert.IsTrue(RunTheMachine(40f), "stopped after " + level.Director.LastLink + " (" + level.Director.LastMessage + ")");
        }

        [TestCase(2.8f)]
        [TestCase(1.5f)]
        public void TheDominoes_PickedUpFromTooFar_AreToldAtOnce_TheRunStopsAtTheWind_AndTheyCanBeMadeBigger(float pickZ)
        {
            Load();
            var said = Listen();
            Assert.IsTrue(Drive(DominoesFrom(pickZ, 11.1f, Level15RubeGoldberg.DominoAim), 30f), lastError);
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.FarLine }, said, "told at the pick-up, and nothing else");
            Assert.IsTrue(level.Lane.Seated, "the lane takes them all the same");
            Assert.Less(level.Dominoes.Scale, Level15RubeGoldberg.DominoWorks);
            SeatBall(3f);
            SeatRuler(1.03f);
            SeatFan(0.93f);
            Assert.IsFalse(RunTheMachine(), "a clearly wrong size does not run through");
            Assert.AreEqual(Level15RubeGoldberg.WindShortLine, said[said.Count - 1]);
            Assert.IsTrue(level.Lane.Seated, "they stand where the player put them");

            // What the line asks for, by hand: take them back from the edge, put them down on the quilt, pick
            // them up again from a step away, turn them until they lie like the outline and hold them to the wall.
            Game.Player.Teleport(Level15RubeGoldberg.DominoStand, 0f);
            Prop set = level.Dominoes;
            IEnumerator Again()
            {
                yield return bot.Grab(set);
                yield return bot.DropAt(new Vector3(18.5f, 3f, 7.5f));
                yield return bot.Until(() => !set.Held && set.Velocity.sqrMagnitude < 0.01f, 4f);
                yield return bot.WalkTo(new Vector3(set.Center.x - 0.6f, 3f, set.Center.z + 1.5f), 0.1f);
                yield return bot.LookAt(set.Center);
                yield return bot.Grab(set);
                yield return bot.WalkTo(Level15RubeGoldberg.DominoStand, 0.15f);
                yield return bot.LookAt(Level15RubeGoldberg.DominoAim);
                yield return bot.RotateHeld(Mathf.RoundToInt(Mathf.DeltaAngle(set.Rotation.eulerAngles.y, 0f) / 15f));
                yield return bot.DropAt(Level15RubeGoldberg.DominoAim);
                yield return bot.Until(() => level.Lane.Seated, 4f);
            }
            Assert.IsTrue(Drive(Again(), 40f), lastError);
            Assert.GreaterOrEqual(set.Scale, Level15RubeGoldberg.DominoWorks, "big enough now");
            Assert.IsTrue(RunTheMachine(40f), "stopped after " + level.Director.LastLink + " (" + level.Director.LastMessage + ")");
        }

        [TestCase(5.5f)]
        [TestCase(7.5f)]
        public void TheDominoes_HeldTooLow_CatchOnThePlinthsEdge_AreTold_AndCanBePickedUpAgainFromTheEdge(float aimY)
        {
            Load();
            var said = Listen();
            Vector3 aim = Level15RubeGoldberg.DominoAim;
            Assert.IsTrue(Drive(DominoesFrom(4.3f, 11.1f, new Vector3(aim.x, aimY, aim.z)), 30f), lastError);
            RunSeconds(1.5f);
            Assert.IsFalse(level.Lane.Seated || level.Lane.Seating || level.LaneApproach.Seating);
            Assert.Less(level.Dominoes.Scale, Level15RubeGoldberg.DominoWorks, "it stopped at the plinth, at half its size");
            Assert.Less(level.Dominoes.Center.y, 3f, "and lies in the trench, in view from the edge");
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.DominoEdgeLine }, said);
            IEnumerator Again()
            {
                yield return bot.Grab(level.Dominoes);
                yield return bot.DropAt(aim);
                yield return bot.Until(() => level.Lane.Seated, 4f);
            }
            Assert.IsTrue(Drive(Again(), 20f), lastError);
            Assert.GreaterOrEqual(level.Dominoes.Scale, Level15RubeGoldberg.DominoWorks);
        }

        [Test]
        public void TheDominoes_TheWrongWayRound_GoBackToTheirBook_AndEndOn_AreToldInTheTrench()
        {
            Load();
            var said = Listen();
            // Half a turn: the big end under the nozzle. They land on the lane, out of reach: back to the book.
            Assert.IsTrue(Drive(DominoesFrom(4.3f, 11.1f, Level15RubeGoldberg.DominoAim, 12), 30f), lastError);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.LaneLeash.Returns == 1, 4f), "the dominoes are at " + level.Dominoes.Center);
            Assert.AreEqual(Level15RubeGoldberg.DominoStart, level.Dominoes.Scale, 1e-4f);
            Assert.Less(Vector3.Distance(level.Dominoes.Position, Level15RubeGoldberg.BookPedestal + Vector3.up * (Level15RubeGoldberg.PedestalHeight + 0.45f)), 0.05f);
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.DominoTurnLine }, said);
            RunSeconds(3f);
            Assert.AreEqual(1, said.Count, "said once");

            // A quarter turn: end on to the wall. They stop short and fall into the trench.
            Unload();
            Load();
            said = Listen();
            Assert.IsTrue(Drive(DominoesFrom(4.3f, 11.1f, Level15RubeGoldberg.DominoAim, 6), 30f), lastError);
            RunSeconds(3f);
            Assert.IsFalse(level.Lane.Seated);
            Assert.AreEqual(0, level.LaneLeash.Returns);
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.DominoTurnShortLine }, said);
            // A little askew is put straight by the lane.
            Unload();
            Load();
            said = Listen();
            Assert.IsTrue(Drive(DominoesFrom(4.3f, 11.1f, Level15RubeGoldberg.DominoAim, 2), 30f), lastError);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Lane.Seated, 2f));
            Assert.Less(Quaternion.Angle(level.Dominoes.Rotation, Quaternion.identity), 0.5f);
            CollectionAssert.IsEmpty(said);
        }

        // The ball's window by hand (LEVELS: 2.25 to 3.0, picked up from 1.4 to 2.5 away; measured from 2.15).
        [TestCase(4.5f, 11.1f, 3f, TestName = "TheBall_FromItsMark_StopsAtItsLargestSize_AndTheMachineRuns")]
        [TestCase(3.6f, 11.1f, 2.37f, TestName = "TheBall_FromTwoAndAHalfAway_TheLowEnd_AndTheMachineRuns")]
        [TestCase(5.25f, 11.1f, 3f, TestName = "TheBall_FromBesideItsSpool_HangsShortOfTheFunnel_IsTakenAllTheSame")]
        [TestCase(4.5f, 7f, 3f, TestName = "TheBall_LetGoFromFourStepsBehindTheEdge_IsTakenAllTheSame")]
        public void TheBallByHand(float pickZ, float standZ, float scale)
        {
            Load();
            var said = Listen();
            Assert.IsTrue(Drive(BallFrom(pickZ, standZ, Level15RubeGoldberg.BallAim), 40f), lastError);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Hopper.Seated, 2f), "the hopper has it (it is at " + level.Ball.Center + ")");
            Assert.AreEqual(scale, level.Ball.Scale, 0.05f);
            CollectionAssert.IsEmpty(said, "nothing needs saying");
            SeatDominoes(3.04f);
            SeatRuler(1.03f);
            SeatFan(0.93f);
            Assert.IsTrue(RunTheMachine(40f), "apex " + level.MarbleApex + ", stopped after " + level.Director.LastLink + " (" + level.Director.LastMessage + ")");
        }

        [Test]
        public void TheBall_PickedUpFromTooFar_IsToldAtOnce_TheMarbleFallsShort_AndFromTheEdgeItCanBeMadeBigger()
        {
            Load();
            var said = Listen();
            Assert.IsTrue(Drive(BallFrom(3.2f, 11.1f, Level15RubeGoldberg.BallAim), 40f), lastError);
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.FarLine }, said);
            Assert.IsTrue(level.Hopper.Seated);
            Assert.Less(level.Ball.Scale, Level15RubeGoldberg.BallWorks);
            float light = level.Ball.Scale;
            SeatDominoes(3.04f);
            SeatRuler(1.03f);
            SeatFan(0.93f);
            Assert.IsFalse(RunTheMachine(), "a clearly wrong size does not run through");
            Assert.AreEqual(Level15RubeGoldberg.MarbleLine, said[said.Count - 1]);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Hopper.Seated, 1f), "the ball is back in the hopper");
            Assert.AreEqual(light, level.Ball.Scale, 1e-4f, "at the size the player gave it");

            // From the edge it is nearer than the wall behind it: taken back and held to the wall again it grows.
            Game.Player.Teleport(Level15RubeGoldberg.BallStand, 0f);
            IEnumerator Again()
            {
                yield return bot.Grab(level.Ball);
                yield return bot.DropAt(Level15RubeGoldberg.BallAim);
                yield return bot.Until(() => level.Hopper.Seated, 4f);
            }
            Assert.IsTrue(Drive(Again(), 20f), lastError);
            Assert.GreaterOrEqual(level.Ball.Scale, Level15RubeGoldberg.BallWorks, "heavy enough now");
            Assert.IsTrue(RunTheMachine(40f), "apex " + level.MarbleApex + ", stopped after " + level.Director.LastLink + " (" + level.Director.LastMessage + ")");
        }

        [Test]
        public void TheBall_HeldAtTheTowersFace_StopsShort_IsTold_AndIsNotSeatedTooLight()
        {
            Load();
            var said = Listen();
            Vector3 aim = Level15RubeGoldberg.BallAim;
            Assert.IsTrue(Drive(BallFrom(4.5f, 11.1f, new Vector3(aim.x, 8.5f, aim.z)), 40f), lastError);
            RunSeconds(1.5f);
            Assert.IsFalse(level.Hopper.Seated, "a ball that stopped on the tower at two thirds of its size is not seated");
            Assert.Less(level.Ball.Center.y, 3f, "it is in the trench, in view from the edge");
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.BallTowerLine }, said);
            // Anywhere on the painted ring it goes in at its largest size.
            foreach (float y in new[] { aim.y - Level15RubeGoldberg.BallRingRadius + 0.1f, aim.y + Level15RubeGoldberg.BallRingRadius })
            {
                Unload();
                Load();
                Assert.IsTrue(Drive(BallFrom(4.5f, 11.1f, new Vector3(aim.x, y, aim.z)), 40f), lastError);
                Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Hopper.Seated, 2f), "aimed at y " + y);
                Assert.AreEqual(3f, level.Ball.Scale, 0.01f);
            }
        }

        [Test]
        public void ABallTooSmallForTheHopper_DropsBetweenTheBars_AndRollsOutAtTheTowersFoot()
        {
            Load();
            var said = Listen();
            int ejected = 0;
            Game.Events.PortEjected += e => ejected++;
            Put(level.Hopper, level.Ball, 1f, Level15RubeGoldberg.HopperFoot + Vector3.up * 1.2f, Quaternion.identity);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => ejected == 1, 3f), "the ball is at " + level.Ball.Center);
            RunSeconds(1.5f);
            Assert.IsFalse(level.Hopper.Seated);
            Assert.AreEqual(1f, level.Ball.Scale, 1e-4f, "at the size it has");
            Assert.Less(level.Ball.Center.y, 1f, "on the floorboards");
            Assert.Less(level.Ball.Center.z, Level15RubeGoldberg.BandZ, "in the walkway, in front of the tower");
            Assert.IsTrue(level.Ball.Grabbable);
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.BallSmallLine }, said);
            Assert.AreEqual(FitState.TooSmall, level.Hopper.Fit(1.19f));
            Assert.AreEqual(FitState.Good, level.Hopper.Fit(1.2f));
        }

        // The ruler by hand (LEVELS: put down about as big as it was picked up, cap within two clicks of the hoop).
        [TestCase(6, TestName = "TheRuler_SixClicks_CapToTheHoop")]
        [TestCase(4, TestName = "TheRuler_FourClicks_IsPutStraightByTheMarker")]
        [TestCase(8, TestName = "TheRuler_EightClicks_IsPutStraightByTheMarker")]
        public void TheRulerByHand(int clicks)
        {
            Load();
            var said = Listen();
            Assert.IsTrue(Drive(RulerFrom(Level15RubeGoldberg.RulerStand, Level15RubeGoldberg.RulerAim, clicks), 60f), lastError);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Fulcrum.Seated && level.Seesaw != null, 2f));
            Assert.That(level.Ruler.Scale, Is.InRange(0.9f, 1.15f));
            Assert.Greater((level.Ruler.Rotation * Vector3.forward).x, 0.9f, "the cap points at the hoop");
            CollectionAssert.IsEmpty(said);
            SeatDominoes(3.04f);
            SeatBall(3f);
            SeatFan(0.93f);
            Assert.IsTrue(RunTheMachine(40f), "stopped after " + level.Director.LastLink + " (" + level.Director.LastMessage + ")");
        }

        [Test]
        public void TheRuler_NotTurned_IsRefused_IsTold_AndTurnedItIsTaken()
        {
            Load();
            var said = Listen();
            Assert.IsTrue(Drive(RulerFrom(Level15RubeGoldberg.RulerStand, Level15RubeGoldberg.RulerAim, 0), 60f), lastError);
            RunSeconds(1f);
            Assert.IsFalse(level.Fulcrum.Seated);
            Assert.IsNull(level.Seesaw);
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.RulerTurnLine }, said, "one line, and it says what to do");
            IEnumerator Turn()
            {
                yield return bot.Grab(level.Ruler);
                yield return bot.LookAt(Level15RubeGoldberg.RulerAim);
                yield return bot.RotateHeld(6);
                yield return bot.DropAt(Level15RubeGoldberg.RulerAim);
                yield return bot.Until(() => level.Fulcrum.Seated, 4f);
            }
            Assert.IsTrue(Drive(Turn(), 20f), lastError);
            Assert.IsNotNull(level.Seesaw);
        }

        [Test]
        public void TheRuler_LetGoFromTooNear_IsTooShort_IsTold_AndFromItsLargestSizeStillFits()
        {
            Load();
            var said = Listen();
            // From right in front of the marker it comes out at 0.72.
            Assert.IsTrue(Drive(RulerFrom(new Vector3(-5f, 0f, 15.6f), Level15RubeGoldberg.RulerAim, 6), 60f), lastError);
            RunSeconds(1f);
            Assert.IsFalse(level.Fulcrum.Seated);
            Assert.Less(level.Ruler.Scale, Level15RubeGoldberg.FulcrumMinScale);
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.RulerSmallLine }, said);
            // From a step behind the mark it is as big as it gets, and that fits.
            Unload();
            Load();
            said = Listen();
            Assert.IsTrue(Drive(RulerFrom(new Vector3(-5f, 0f, 13f), Level15RubeGoldberg.RulerAim, 6), 60f), lastError);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Fulcrum.Seated, 2f));
            Assert.AreEqual(Level15RubeGoldberg.RulerMax, level.Ruler.Scale, 0.01f);
            CollectionAssert.IsEmpty(said);
            // Put down on the floorboards in front of the marker it is told where it goes.
            Unload();
            Load();
            said = Listen();
            Vector3 aim = Level15RubeGoldberg.RulerAim;
            Assert.IsTrue(Drive(RulerFrom(Level15RubeGoldberg.RulerStand, new Vector3(aim.x, 0.1f, 16.6f), 6), 60f), lastError);
            RunSeconds(1f);
            Assert.IsFalse(level.Fulcrum.Seated);
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.RulerMissLine }, said);
        }

        // The fan by hand (LEVELS: 0.6 to 1.2; let go at 0.12 to 0.24 of the distance it was picked up from).
        [TestCase(13f, 0.934f, TestName = "TheFan_FromItsMark_ComesOutAtNineTenths_AndTheMachineRuns")]
        [TestCase(12f, 1.056f, TestName = "TheFan_FromAStepBack_IsBigger_AndTheMachineRuns")]
        [TestCase(14.5f, 0.678f, TestName = "TheFan_FromAStepNearer_IsSmaller_AndTheMachineRuns")]
        public void TheFanByHand(float standZ, float scale)
        {
            Load();
            var said = Listen();
            Assert.IsTrue(Drive(FanFrom(new Vector3(1.25f, 0f, standZ), Level15RubeGoldberg.FanAim), 60f), lastError);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Collar.Seated, 2f), "the garage has it (it is at " + level.Fan.Center + ")");
            Assert.AreEqual(scale, level.Fan.Scale, 0.03f);
            Assert.Less(Quaternion.Angle(level.Fan.Rotation, Quaternion.identity), 0.5f, "its guard faces the duct");
            CollectionAssert.IsEmpty(said);
            SeatDominoes(3.04f);
            SeatBall(3f);
            SeatRuler(1.03f);
            Assert.IsTrue(RunTheMachine(40f), "stopped after " + level.Director.LastLink + " (" + level.Director.LastMessage + ")");
        }

        [Test]
        public void TheFan_LetGoFromTheGaragesDoor_Wheezes_AndFromFartherBackItBlows()
        {
            Load();
            var said = Listen();
            Assert.IsTrue(Drive(FanFrom(new Vector3(1.25f, 0f, 15.6f), Level15RubeGoldberg.FanAim), 60f), lastError);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Collar.Seated, 2f));
            Assert.Less(level.Fan.Scale, Level15RubeGoldberg.FanReference, "a thimble of a fan");
            SeatDominoes(3.04f);
            SeatBall(3f);
            SeatRuler(1.03f);
            Assert.IsFalse(RunTheMachine(), "a clearly wrong size does not run through");
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.FanSmallLine }, said);
            Assert.IsTrue(level.Collar.Seated, "it stands where the player put it");

            // Taken out again and let go from the far side of the walkway it is bigger.
            Game.Player.Teleport(Level15RubeGoldberg.FanStand, 0f);
            IEnumerator Again()
            {
                yield return bot.Grab(level.Fan);
                yield return bot.WalkTo(new Vector3(1.25f, 0f, 11.95f), 0.1f);
                yield return bot.DropAt(Level15RubeGoldberg.FanAim);
                yield return bot.Until(() => level.Collar.Seated, 4f);
            }
            Assert.IsTrue(Drive(Again(), 20f), lastError);
            Assert.GreaterOrEqual(level.Fan.Scale, Level15RubeGoldberg.FanReference);
            Assert.IsTrue(RunTheMachine(40f), "stopped after " + level.Director.LastLink + " (" + level.Director.LastMessage + ")");
        }

        [Test]
        public void TheFan_PutDownInFrontOfTheGarage_OrPickedUpFromBesideIt_IsTold()
        {
            Load();
            var said = Listen();
            Assert.IsTrue(Drive(FanFrom(Level15RubeGoldberg.FanStand, new Vector3(1.25f, 0.05f, 15.5f)), 60f), lastError);
            RunSeconds(1f);
            Assert.IsFalse(level.Collar.Seated);
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.FanMissLine }, said);

            // Picked up from beside it, it looks as big as a house and cannot be made to fit.
            Unload();
            Load();
            said = Listen();
            IEnumerator Near()
            {
                yield return level.ToTheTrench(bot);
                yield return bot.WalkTo(new Vector3(-19.2f, 0f, 12.6f), 0.2f, 14f, true);
                yield return bot.LookAt(level.Fan.Center);
                yield return bot.Grab(level.Fan);
            }
            Assert.IsTrue(Drive(Near(), 60f), lastError);
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.NearLine }, said);
            IEnumerator Carry()
            {
                yield return bot.WalkTo(Level15RubeGoldberg.FanStand, 0.2f, 14f, true);
                yield return bot.DropAt(Level15RubeGoldberg.FanAim);
                yield return bot.Wait(1f);
            }
            Assert.IsTrue(Drive(Carry(), 60f), lastError);
            Assert.IsFalse(level.Collar.Seated);
            Assert.Greater(level.Fan.Scale, Level15RubeGoldberg.CollarMaxScale);
            Assert.AreEqual(Level15RubeGoldberg.FanBigLine, said[said.Count - 1]);
            Assert.LessOrEqual(level.Fan.Scale, Level15RubeGoldberg.FanStart, "never bigger than it starts");
        }

        // ---- Taking a toy back, and starting the machine by hand ------------------------------------------------------------

        [Test]
        public void ASeatedToy_CanBeTakenBack_WhileTheMachineIsIdle_AndNotWhileItRuns()
        {
            Load();
            Assert.IsTrue(Drive(level.PlaceDominoes(bot), 30f), lastError);
            Assert.IsTrue(level.Lane.Seated);
            IEnumerator TakeBack()
            {
                yield return bot.Grab(level.Dominoes);
                yield return bot.Wait(0.2f);
            }
            Assert.IsTrue(Drive(TakeBack(), 10f), lastError);
            Assert.AreSame(level.Dominoes, Game.Grabber.Held);
            Assert.IsFalse(level.Lane.Seated, "the lane is empty again");
            Assert.AreEqual(ChainState.Idle, level.Chain.State);
            IEnumerator PutBack()
            {
                yield return bot.DropAt(Level15RubeGoldberg.DominoAim);
                yield return bot.Until(() => level.Lane.Seated, 4f);
            }
            Assert.IsTrue(Drive(PutBack(), 10f), lastError);
            Assert.GreaterOrEqual(level.Dominoes.Scale, Level15RubeGoldberg.DominoWorks);

            // The ruler: with it goes the lever.
            SeatRuler(1f);
            Assert.IsNotNull(level.Seesaw);
            Game.Player.Teleport(Level15RubeGoldberg.RulerStand, 0f);
            IEnumerator TakeRuler()
            {
                yield return bot.Grab(level.Ruler);
                yield return bot.Wait(0.2f);
            }
            Assert.IsTrue(Drive(TakeRuler(), 10f), lastError);
            Assert.IsFalse(level.Fulcrum.Seated);
            Assert.IsNull(level.Seesaw, "no ruler, no lever");
            IEnumerator PutRuler()
            {
                yield return bot.DropAt(Level15RubeGoldberg.RulerAim);
                yield return bot.Until(() => level.Fulcrum.Seated, 4f);
            }
            Assert.IsTrue(Drive(PutRuler(), 10f), lastError);
            Assert.IsNotNull(level.Seesaw);

            // In a run the toys are the machine's.
            StepOnThePedal();
            StepOffThePedal();
            Assert.AreEqual(MachineState.Running, level.Director.State);
            Assert.IsFalse(level.Dominoes.Grabbable || level.Ball.Grabbable || level.Ruler.Grabbable || level.Fan.Grabbable);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Director.State == MachineState.Idle, 8f));
            Assert.IsTrue(level.Dominoes.Grabbable && level.Ball.Grabbable && level.Ruler.Grabbable && level.Fan.Grabbable);
        }

        [Test]
        public void TheBall_LetGoStraightOntoTheShortArm_FiresTheCatapult_AndTheRestOfTheChainStillRuns()
        {
            Load();
            SeatDominoes(3.04f);
            SeatRuler(1.03f);
            SeatFan(0.93f);
            // (LEVELS: tolerated. The other three toys are still needed.)
            level.Ball.Unfreeze();
            level.Ball.SetScale(3f);
            level.Ball.SetPose(new Vector3(-5.9f, 3.4f, Level15RubeGoldberg.AxisZ), Quaternion.identity);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Director.State == MachineState.Running, 2f), "the ball on the short arm starts a run");
            Assert.IsTrue(level.Gate.IsClosed, "the gate stays shut: nobody pressed the pedal");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Director.State != MachineState.Running, 30f));
            Assert.AreEqual(MachineState.Done, level.Director.State, "stopped after " + level.Director.LastLink + " (" + level.Director.LastMessage + ")");
            Assert.AreEqual(Level15RubeGoldberg.LinkStrike, level.Director.Fired[0]);
            Assert.IsTrue(level.Clock.Broken);
            Assert.IsFalse(level.Exit.Locked);
        }

        [Test]
        public void ThePlayerOnTheShortArm_IsNotHeavyEnough_AndIsToldSo_AndTheLeverComesBack()
        {
            Load();
            var said = Listen();
            SeatAll();
            Game.Player.Teleport(new Vector3(-6.05f, 1.6f, Level15RubeGoldberg.AxisZ), 90f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Director.Runs == 1, 2f), "stepping on the short arm tips it");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Director.State == MachineState.Idle, 8f));
            Assert.IsFalse(level.MarbleThrough);
            Assert.Less(level.MarbleApex, Level15RubeGoldberg.HoopNeed);
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.PlayerLine }, said);
            Assert.IsTrue(level.Exit.Locked);
            // Off it again, the ruler comes back to rest and the pedal runs the machine through.
            Game.Player.Teleport(Level15RubeGoldberg.PedalPoint + new Vector3(0f, 0f, -3f), 0f);
            RunSeconds(2f);
            Assert.IsTrue(level.Seesaw.Enabled, "the lever is live again");
            Assert.IsTrue(level.Hopper.Seated, "the ball never left the hopper");
            Assert.IsTrue(RunTheMachine(40f), "stopped after " + level.Director.LastLink + " (" + level.Director.LastMessage + ")");
        }

        // ---- Never a softlock: restarts, toys that leave the world, toys left where nobody can get at them ------------

        [Test]
        public void RestartingMidSolve_AndSolvingAgain_Works()
        {
            Load();
            // With the dominoes in hand at the edge.
            IEnumerator Hold()
            {
                yield return bot.Grab(level.Dominoes);
                yield return bot.WalkTo(new Vector3(16f, 3f, 6f));
                yield return bot.WalkTo(Level15RubeGoldberg.DominoStand, 0.15f);
                yield return bot.LookAt(Level15RubeGoldberg.DominoAim);
            }
            Assert.IsTrue(Drive(Hold(), 30f), lastError);
            Assert.AreSame(level.Dominoes, Game.Grabber.Held);
            Assert.Greater(level.Dominoes.Scale, 2.5f);
            Game.RestartLevel();
            Game.Tick();
            AssertFresh("with the dominoes in hand");

            // With the ball in the air and the ruler about to be struck.
            SeatAll();
            StepOnThePedal();
            StepOffThePedal();
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.BallRun.Running && level.BallRun.Segment == 2, 4f), "the ball is over the books");
            Game.RestartLevel();
            Game.Tick();
            AssertFresh("with the ball in the air");

            // With the marble in the tube, then with the dominoes falling.
            SeatAll();
            StepOnThePedal();
            StepOffThePedal();
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.MarbleFlying, 6f));
            Game.RestartLevel();
            Game.Tick();
            AssertFresh("with the marble in the air");
            SeatAll();
            StepOnThePedal();
            StepOffThePedal();
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Chain.Fallen >= 3, 14f));
            Game.RestartLevel();
            Game.Tick();
            AssertFresh("with the dominoes falling");

            // With the clock crushed and the door open, a step from the way out.
            SeatAll();
            Assert.IsTrue(RunTheMachine(40f));
            Assert.IsFalse(level.Exit.Locked);
            Game.RestartLevel();
            Game.Tick();
            AssertFresh("with the door open");

            TestHelpers.PlayLevel(Game, 120f);
            Assert.IsTrue(level.Clock.Broken);
        }

        void AssertFresh(string when)
        {
            Assert.AreSame(level, Game.Level, when + ": the same level instance is built again");
            level = (Level15RubeGoldberg)Game.Level;
            Assert.IsNull(Game.Grabber.Held, when);
            Assert.IsFalse(Game.LevelCompleted, when);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level15RubeGoldberg.SpawnPoint), 0.1f, when);
            Assert.AreEqual(MachineState.Idle, level.Director.State, when);
            Assert.AreEqual(0, level.Director.Runs, when);
            Assert.IsNull(level.Seesaw, when);
            Assert.IsFalse(level.MarbleFlying || level.MarbleThrough || level.SwitchClosed, when);
            Assert.AreEqual(0f, level.Wind.Strength, when);
            Assert.IsFalse(level.Clock.Broken, when);
            Assert.IsTrue(level.Exit.Locked, when);
            Assert.IsTrue(level.ExitDoor.IsClosed && level.Gate.IsClosed, when);
            Assert.IsFalse(level.Hopper.Seated || level.Fulcrum.Seated || level.Collar.Seated || level.Lane.Seated, when);
            Assert.AreEqual(Level15RubeGoldberg.DominoStart, level.Dominoes.Scale, 1e-4f, when);
            Assert.AreEqual(Level15RubeGoldberg.BallStart, level.Ball.Scale, 1e-4f, when);
            Assert.AreEqual(Level15RubeGoldberg.RulerStart, level.Ruler.Scale, 1e-4f, when);
            Assert.AreEqual(Level15RubeGoldberg.FanStart, level.Fan.Scale, 1e-4f, when);
            Assert.IsTrue(level.Ball.Frozen && level.Baseball.Frozen, when);
            Assert.Less(Vector3.Distance(level.Baseball.Center, Level15RubeGoldberg.BaseballCentre), 0.01f, when);
            Assert.AreSame(level.Dominoes, Game.Grabber.FindTarget(), when + ": the first click picks the dominoes up");
        }

        [Test]
        public void AToyThatLeavesTheWorld_ComesBack_AtTheSizeItStartedWith()
        {
            Load();
            int respawned = 0;
            Game.Events.PropRespawned += e => respawned++;
            var toys = new[] { level.Dominoes, level.Ball, level.Ruler, level.Fan };
            var starts = new[] { Level15RubeGoldberg.DominoStart, Level15RubeGoldberg.BallStart, Level15RubeGoldberg.RulerStart, Level15RubeGoldberg.FanStart };
            for (int i = 0; i < toys.Length; i++)
            {
                Prop toy = toys[i];
                Vector3 home = toy.Position;
                Quaternion facing = toy.Rotation;
                if (toy.Frozen) toy.Unfreeze();
                toy.SetScale(Mathf.Clamp(1.1f, toy.MinScale, toy.MaxScale));
                toy.SetPose(new Vector3(-3f + i, level.KillY - 1f, 2f), Quaternion.Euler(20f, 50f, 0f));
                TestHelpers.Run(Game, 2);
                Assert.AreEqual(i + 1, respawned, toy.Name);
                Assert.Less(Vector3.Distance(toy.Position, home), 0.02f, toy.Name + " is back where it started");
                Assert.Less(Quaternion.Angle(toy.Rotation, facing), 0.1f, toy.Name);
                Assert.AreEqual(starts[i], toy.Scale, 1e-4f, toy.Name + " at the size it started with");
                Assert.IsTrue(toy.Grabbable, toy.Name);
            }
            Assert.IsTrue(level.Ball.Frozen, "the ball waits on its spool again");

            // A seated toy that is taken out of the world leaves its station empty, and comes back as well.
            SeatFan(0.9f);
            level.Fan.SetPose(new Vector3(0f, level.KillY - 1f, 0f), Quaternion.identity);
            TestHelpers.Run(Game, 3);
            Assert.IsFalse(level.Collar.Seated);
            Assert.AreEqual(Level15RubeGoldberg.FanStart, level.Fan.Scale, 1e-4f);
            Assert.Less(Vector3.Distance(level.Fan.Position, Level15RubeGoldberg.FanStartPoint), 0.02f);

            // The player cannot fall out (there are no pits), and the level is still there to be solved.
            RunSeconds(0.5f);
            TestHelpers.PlayLevel(Game, 120f);
        }

        [Test]
        public void AToyLeftWhereNobodyCanGetAtIt_GoesBackToWhereItStarted()
        {
            Load();
            var said = Listen();
            // The ball on the shelf beside the baseball.
            level.Ball.Unfreeze();
            level.Ball.SetScale(0.8f);
            level.Ball.SetPose(new Vector3(23.6f, Level15RubeGoldberg.ShelfTop + 0.5f, Level15RubeGoldberg.AxisZ), Quaternion.identity);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Parking.Returns == 1, 5f), "the ball is at " + level.Ball.Center);
            Assert.IsTrue(level.Ball.Frozen);
            Assert.AreEqual(Level15RubeGoldberg.BallStart, level.Ball.Scale, 1e-4f);
            // The ruler on top of the slide tower.
            level.Ruler.SetScale(0.7f);
            level.Ruler.SetPose(new Vector3(-19.6f, Level15RubeGoldberg.TowerTop + 0.3f, 17.4f), Quaternion.identity);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Parking.Returns == 2, 5f), "the ruler is at " + level.Ruler.Center);
            Assert.Less(Vector3.Distance(level.Ruler.Position, Level15RubeGoldberg.RulerStartPoint), 0.02f);
            // The fan on the lane: that is for the dominoes.
            level.Fan.SetScale(0.8f);
            level.Fan.SetPose(new Vector3(15f, Level15RubeGoldberg.LaneTop + 0.1f, Level15RubeGoldberg.AxisZ), Quaternion.identity);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.LaneLeash.Returns == 1, 5f), "the fan is at " + level.Fan.Center);
            Assert.AreEqual(Level15RubeGoldberg.FanStart, level.Fan.Scale, 1e-4f);
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.ParkingLine }, said, "one line for the three (not the same one over and over)");

            // Dominoes too small for the lane lie on it out of reach: back to their book, with the reason.
            said.Clear();
            RunSeconds(6f);
            Put(level.Lane, level.Dominoes, 0.8f, new Vector3(16f, Level15RubeGoldberg.LaneTop + 0.7f, Level15RubeGoldberg.AxisZ), Quaternion.identity);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.LaneLeash.Returns == 2, 5f), "the dominoes are at " + level.Dominoes.Center);
            Assert.AreEqual(Level15RubeGoldberg.DominoStart, level.Dominoes.Scale, 1e-4f);
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.DominoSmallLine }, said);
            // Seated toys stay where they are.
            SeatAll();
            RunSeconds(4f);
            Assert.IsTrue(level.Lane.Seated && level.Hopper.Seated && level.Fulcrum.Seated && level.Collar.Seated);
        }

        // Robustness: the same solve every time, and again after a restart.
        [Test]
        public void TheSolve_IsTheSameFiveTimesOver_AndAfterARestart()
        {
            int firstTicks = 0;
            var first = new float[4];
            for (int run = 0; run < 5; run++)
            {
                Game?.Dispose();
                Load();
                TestHelpers.PlayLevel(Game, 120f);
                var sizes = new[] { level.Dominoes.Scale, level.Ball.Scale, level.Ruler.Scale, level.Fan.Scale };
                if (run == 0)
                {
                    firstTicks = Game.LevelTicks;
                    first = sizes;
                }
                CollectionAssert.AreEqual(first, sizes, "run " + run + ": the four toys come out the same size every time");
                Assert.AreEqual(firstTicks, Game.LevelTicks, "run " + run + ": and the solve takes the same number of ticks");
            }
            Game.RestartLevel();
            Game.Tick();
            Assert.IsFalse(Game.LevelCompleted);
            TestHelpers.PlayLevel(Game, 120f);
            Assert.AreEqual(first[0], level.Dominoes.Scale, 0.02f);
            Assert.AreEqual(first[1], level.Ball.Scale, 0.02f);
            // (One tick on either side: the restart was asked for outside a tick.)
            Assert.AreEqual(firstTicks, Game.LevelTicks, 2f, "and after a restart it takes as long");
        }

        // ---- Bot scripts with a variation ---------------------------------------------------------------------------------

        // The dominoes, picked up from (14, 3, pickZ) and let go from the edge (or behind it) at a point of the wall.
        IEnumerator DominoesFrom(float pickZ, float standZ, Vector3 aim, int turns = 0)
        {
            if (Mathf.Abs(pickZ - Level15RubeGoldberg.SpawnPoint.z) > 0.01f) yield return bot.WalkTo(new Vector3(14f, 3f, pickZ), 0.08f);
            // (Square on: a toy keeps the angle it was picked up at, and the bot clicks as soon as it can.)
            yield return bot.LookAt(level.Dominoes.Center);
            yield return bot.Grab(level.Dominoes);
            yield return bot.WalkTo(new Vector3(16f, 3f, 6f));
            yield return bot.WalkTo(new Vector3(Level15RubeGoldberg.DominoStand.x, 3f, standZ), 0.15f);
            if (turns != 0)
            {
                yield return bot.LookAt(aim);
                yield return bot.RotateHeld(turns);
            }
            yield return bot.DropAt(aim);
            yield return bot.Wait(0.75f);
        }

        // The ball, picked up from (-18, 3, pickZ) and let go from the edge (or behind it) at a point of the wall.
        IEnumerator BallFrom(float pickZ, float standZ, Vector3 aim)
        {
            yield return bot.WalkTo(new Vector3(10f, 3f, 4.5f), 0.4f, 10f, true);
            yield return bot.WalkTo(new Vector3(-16f, 3f, Mathf.Min(4.5f, pickZ)), 0.4f, 12f, true);
            yield return bot.WalkTo(new Vector3(-18f, 3f, pickZ), 0.08f);
            yield return bot.LookAt(level.Ball.Center);
            yield return bot.Grab(level.Ball);
            yield return bot.WalkTo(new Vector3(-16.6f, 3f, 6f));
            yield return bot.WalkTo(new Vector3(-18f, 3f, standZ), 0.15f);
            yield return bot.DropAt(aim);
            yield return bot.Wait(0.75f);
        }

        // The ruler, picked up from a place in the trench, turned by so many clicks and let go over a point.
        IEnumerator RulerFrom(Vector3 stand, Vector3 aim, int turns)
        {
            yield return level.ToTheTrench(bot);
            yield return bot.WalkTo(new Vector3(-3.5f, 0f, 14.2f), 0.4f, 10f, true);
            yield return bot.WalkTo(stand, 0.1f);
            yield return bot.LookAt(level.Ruler.Center);
            yield return bot.Grab(level.Ruler);
            yield return bot.LookAt(aim);
            if (turns != 0) yield return bot.RotateHeld(turns);
            yield return bot.DropAt(aim);
            yield return bot.Wait(0.75f);
        }

        // The fan, picked up from a place in the trench and let go at a point.
        IEnumerator FanFrom(Vector3 stand, Vector3 aim)
        {
            yield return level.ToTheTrench(bot);
            yield return bot.WalkTo(stand, 0.1f, 12f, true);
            yield return bot.LookAt(level.Fan.Center);
            yield return bot.Grab(level.Fan);
            yield return bot.DropAt(aim);
            yield return bot.Wait(0.75f);
        }

        // ---- Probes (explicit: they measure, they do not judge) ------------------------------------------------------

        [Test]
        public void TheLampBesideAStation_SaysWhetherAToyOfThatSizeWouldWork()
        {
            Load();
            FitGauge hopper = level.Gauges[0], fulcrum = level.Gauges[1], collar = level.Gauges[2], lane = level.Gauges[3];
            Assert.AreEqual(FitState.TooSmall, lane.Fit(2.1f), "the lane seats a set of 2.1, but the wind does not reach it");
            Assert.AreEqual(FitState.Good, level.Lane.Fit(2.1f));
            Assert.AreEqual(FitState.Good, lane.Fit(Level15RubeGoldberg.DominoWorks));
            Assert.AreEqual(FitState.Good, lane.Fit(Level15RubeGoldberg.DominoMax));
            Assert.AreEqual(FitState.TooSmall, hopper.Fit(2f));
            Assert.AreEqual(FitState.Good, hopper.Fit(Level15RubeGoldberg.BallWorks));
            Assert.AreEqual(FitState.Good, hopper.Fit(Level15RubeGoldberg.BallMax));
            Assert.AreEqual(FitState.TooSmall, collar.Fit(0.55f));
            Assert.AreEqual(FitState.Good, collar.Fit(0.6f));
            Assert.AreEqual(FitState.TooBig, collar.Fit(1.3f));
            Assert.AreEqual(FitState.TooSmall, fulcrum.Fit(0.75f));
            Assert.AreEqual(FitState.Good, fulcrum.Fit(1f));

            // Held against the wall above the lane the dominoes light it green; nothing is lit when nothing is held.
            Assert.AreEqual(FitState.Idle, lane.State);
            IEnumerator Hold()
            {
                yield return bot.Grab(level.Dominoes);
                yield return bot.WalkTo(new Vector3(16f, 3f, 6f));
                yield return bot.WalkTo(Level15RubeGoldberg.DominoStand, 0.15f);
                yield return bot.LookAt(Level15RubeGoldberg.DominoAim);
                yield return bot.Wait(0.2f);
            }
            Assert.IsTrue(Drive(Hold(), 30f), lastError);
            Assert.AreEqual(FitState.Good, lane.State, "a set of " + level.Dominoes.Scale);
            Assert.AreEqual(FitState.Idle, hopper.State);
            // Held low, where it stops on the plinth's edge at half the size, the lamp is amber.
            IEnumerator Low()
            {
                yield return bot.LookAt(new Vector3(Level15RubeGoldberg.DominoAim.x, 6.5f, Level15RubeGoldberg.BackZ));
                yield return bot.Wait(0.2f);
            }
            Assert.IsTrue(Drive(Low(), 10f), lastError);
            Assert.AreEqual(FitState.TooSmall, lane.State, "a set of " + level.Dominoes.Scale);
        }

        // The whole place is a handful of materials: the paint, the lit windows and the lamps are one, the
        // statics one per kind of surface (the frame allows 40 with the room and the presenters' own; with
        // 35 of its own the level went over).
        [Test]
        public void TheLevelsOwnMaterials_AreFew()
        {
            Load();
            var distinct = new HashSet<Material>();
            foreach (Renderer renderer in Game.Root.GetComponentsInChildren<Renderer>(false))
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null) distinct.Add(material);
            var names = new List<string>();
            foreach (Material material in distinct) names.Add(material.name);
            Assert.LessOrEqual(distinct.Count, 28, string.Join(", ", names));
        }

        [Test, Explicit]
        public void Probe_Pickups()
        {
            var log = new StringBuilder();
            void Try(string name, Func<IEnumerator> script, Func<Prop> toy, Func<Socket> socket)
            {
                Load();
                var said = Listen();
                float k = 0f, d = 0f, s = 0f;
                Vector3 at = Vector3.zero;
                Game.Events.PropGrabbed += e => k = e.OldScale / e.GrabDistance;
                Game.Events.PropDropped += e =>
                {
                    d = e.DropDistance;
                    s = e.NewScale;
                    at = e.Prop.Center;
                };
                bool ok = Drive(script(), 60f);
                RunSeconds(4f);
                log.AppendLine(name + ": " + (ok ? "" : "FAILED " + lastError + " ") + "k=" + F(k) + " d=" + F(d) + " s=" + F(s) + " let go at " + F(at) +
                    " -> seated " + socket().Seated + " scale " + F(toy().Scale) + " at " + F(toy().Center) + " frozen " + toy().Frozen + "  said: " + string.Join(" | ", said));
                Unload();
            }
            Vector3 dAim = Level15RubeGoldberg.DominoAim, bAim = Level15RubeGoldberg.BallAim;
            foreach (float z in new[] { 4.3f, 3.9f, 3.6f, 3.3f, 2.8f, 1.5f, 4.7f, 5f, 5.3f })
                Try("dominoes pickZ " + z, () => DominoesFrom(z, 11.1f, dAim), () => level.Dominoes, () => level.Lane);
            foreach (float y in new[] { 5.5f, 6.5f, 7.5f, 7.9f, 8.2f, 8.5f, 11f, 14f })
                Try("dominoes aimY " + y, () => DominoesFrom(4.3f, 11.1f, new Vector3(dAim.x, y, dAim.z)), () => level.Dominoes, () => level.Lane);
            foreach (float z in new[] { 10f, 8.5f, 6.5f })
                Try("dominoes standZ " + z, () => DominoesFrom(4.3f, z, dAim), () => level.Dominoes, () => level.Lane);
            foreach (int turns in new[] { 12, 6, 3, 1 })
                Try("dominoes turned " + turns, () => DominoesFrom(4.3f, 11.1f, dAim, turns), () => level.Dominoes, () => level.Lane);
            foreach (float z in new[] { 4.5f, 4f, 3.6f, 3.2f, 2.5f, 1f, 4.9f, 5.25f })
                Try("ball pickZ " + z, () => BallFrom(z, 11.1f, bAim), () => level.Ball, () => level.Hopper);
            foreach (float y in new[] { 7f, 8.5f, 9.5f, 12.5f, 15f })
                Try("ball aimY " + y, () => BallFrom(4.5f, 11.1f, new Vector3(bAim.x, y, bAim.z)), () => level.Ball, () => level.Hopper);
            foreach (float z in new[] { 10f, 8.5f, 7f })
                Try("ball standZ " + z, () => BallFrom(4.5f, z, bAim), () => level.Ball, () => level.Hopper);
            foreach (float x in new[] { -20.5f, -19.5f, -19f, -17f, -16.5f, -15.5f })
                Try("ball aimX " + x, () => BallFrom(4.5f, 11.1f, new Vector3(x, bAim.y, bAim.z)), () => level.Ball, () => level.Hopper);
            foreach (float x in new[] { 11f, 12.5f, 16f, 18f })
                Try("dominoes aimX " + x, () => DominoesFrom(4.3f, 11.1f, new Vector3(x, dAim.y, dAim.z)), () => level.Dominoes, () => level.Lane);
            Note("pickups", log.ToString());

            log.Clear();
            Vector3 rStand = Level15RubeGoldberg.RulerStand, rAim = Level15RubeGoldberg.RulerAim;
            foreach (int turns in new[] { 6, 5, 4, 3, 7, 8, 9, 0, 12 })
                Try("ruler turns " + turns, () => RulerFrom(rStand, rAim, turns), () => level.Ruler, () => level.Fulcrum);
            foreach (float z in new[] { 17.35f, 17.8f, 18.7f, 19.15f })
                Try("ruler aimZ " + z, () => RulerFrom(rStand, new Vector3(rAim.x, rAim.y, z), 6), () => level.Ruler, () => level.Fulcrum);
            foreach (float x in new[] { -7f, -6f, -4f, -3f })
                Try("ruler aimX " + x, () => RulerFrom(rStand, new Vector3(x, rAim.y, rAim.z), 6), () => level.Ruler, () => level.Fulcrum);
            foreach (float y in new[] { 0.1f, 0.5f, 1.4f, 2.2f })
                Try("ruler aimY " + y, () => RulerFrom(rStand, new Vector3(rAim.x, y, rAim.z), 6), () => level.Ruler, () => level.Fulcrum);
            foreach (Vector3 at in new[] { new Vector3(-5f, 0f, 13f), new Vector3(-5f, 0f, 15.6f), new Vector3(-6.5f, 0f, 14.4f), new Vector3(-3f, 0f, 14.4f), new Vector3(-8.1f, 0f, 12.8f), new Vector3(-1f, 0f, 13f) })
                Try("ruler stand " + F(at), () => RulerFrom(at, rAim, 6), () => level.Ruler, () => level.Fulcrum);
            Note("ruler", log.ToString());

            log.Clear();
            Vector3 fStand = Level15RubeGoldberg.FanStand, fAim = Level15RubeGoldberg.FanAim;
            foreach (Vector3 at in new[] { fStand, new Vector3(1.25f, 0f, 12f), new Vector3(1.25f, 0f, 14.5f), new Vector3(1.25f, 0f, 15.6f), new Vector3(-3f, 0f, 13.5f), new Vector3(-8f, 0f, 13.5f), new Vector3(-14f, 0f, 13.5f), new Vector3(6f, 0f, 13.5f), new Vector3(12f, 0f, 13.5f), new Vector3(3f, 0f, 14f), new Vector3(0f, 0f, 14f) })
                Try("fan stand " + F(at), () => FanFrom(at, fAim), () => level.Fan, () => level.Collar);
            foreach (Vector3 aim in new[] { new Vector3(1.25f, 0.1f, 17.2f), new Vector3(1.25f, 0.3f, 17.9f), new Vector3(1.25f, 1f, 17.9f), new Vector3(1.25f, 1.4f, 17.9f), new Vector3(1.25f, 0.2f, 16.6f), new Vector3(0.8f, 0.6f, 17.9f), new Vector3(1.8f, 0.6f, 17.9f), new Vector3(1.25f, 0.05f, 15.5f) })
                Try("fan aim " + F(aim), () => FanFrom(fStand, aim), () => level.Fan, () => level.Collar);
            Note("fan", log.ToString());
        }

        // ---- The review: what a newcomer does first, and what somebody does who means harm ------------------------

        [Test]
        public void TheDominoes_LetGoAtTheOutlineFromTheStart_WithoutAStep_AreTakenAllTheSame()
        {
            Load();
            var said = Listen();
            Vector3 hung = Vector3.zero;
            Game.Events.PropDropped += e => hung = e.Prop.Center;
            IEnumerator NoStep()
            {
                yield return bot.Grab(level.Dominoes);
                yield return bot.DropAt(Level15RubeGoldberg.DominoAim);
                yield return bot.Until(() => level.Lane.Seated, 3f);
            }
            Assert.IsTrue(Drive(NoStep(), 10f), lastError);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level15RubeGoldberg.SpawnPoint), 0.2f, "not a step");
            Assert.AreEqual(Level15RubeGoldberg.DominoMax, level.Dominoes.Scale, 0.01f, "too far back is harmless: they stop growing");
            // (As found they hung here, short of the approach that began at z 14.4, and fell into the trench with the wrong line.)
            Assert.Less(hung.z, 14.4f);
            Assert.Greater(hung.z, Level15RubeGoldberg.ApproachZ);
            CollectionAssert.IsEmpty(said);
            SeatBall(3f);
            SeatRuler(1.03f);
            SeatFan(0.93f);
            Assert.IsTrue(RunTheMachine(40f), "stopped after " + level.Director.LastLink + " (" + level.Director.LastMessage + ")");
        }

        // As found: a set at its largest size in the trench under the edge could not be made bigger and, picked
        // up from the edge, hung at z 14.1: short of the lane's approach, again and again.
        [TestCase(14.25f, 11.1f)]
        [TestCase(12f, 11.1f)]
        [TestCase(17f, 11.1f)]
        [TestCase(14.25f, 9.8f)]
        public void TheDominoes_AtTheirLargestInTheTrench_PickedUpFromTheQuilt_GoOnTheLane(float standX, float standZ)
        {
            Load();
            var said = Listen();
            level.Dominoes.SetScale(Level15RubeGoldberg.DominoMax);
            level.Dominoes.SetPose(new Vector3(14.73f, 2.66f, 13.83f), Quaternion.identity);
            RunSeconds(1.5f);
            Assert.Less(level.Dominoes.Center.y, 3f, "they stand in the trench");
            IEnumerator Again()
            {
                yield return bot.WalkTo(new Vector3(16f, 3f, 6f));
                yield return bot.WalkTo(new Vector3(standX, 3f, standZ), 0.15f);
                yield return bot.LookAt(level.Dominoes.Center);
                yield return bot.Grab(level.Dominoes);
                yield return bot.DropAt(Level15RubeGoldberg.DominoAim);
                yield return bot.Until(() => level.Lane.Seated, 3f);
            }
            Assert.IsTrue(Drive(Again(), 30f), lastError + " (the dominoes are at " + level.Dominoes.Center + ")");
            // (From the side a corner meets the plinth first: 2.5 to 2.8. From in front they are still 3.5.)
            Assert.GreaterOrEqual(level.Dominoes.Scale, Level15RubeGoldberg.DominoWorks);
            CollectionAssert.IsEmpty(said);
        }

        [Test]
        public void TheDominoes_LetGoFarBackOnTheQuilt_AreToldToGoToTheEdge_AndFromThereTheyGoOn()
        {
            Load();
            var said = Listen();
            IEnumerator FarBack()
            {
                yield return bot.Grab(level.Dominoes);
                yield return bot.WalkTo(new Vector3(14f, 3f, 0.5f), 0.2f);
                yield return bot.DropAt(Level15RubeGoldberg.DominoAim);
                yield return bot.Wait(1.5f);
            }
            Assert.IsTrue(Drive(FarBack(), 20f), lastError);
            Assert.IsFalse(level.Lane.Seated || level.Lane.Seating || level.LaneApproach.Seating);
            Assert.AreEqual(Level15RubeGoldberg.DominoMax, level.Dominoes.Scale, 0.01f);
            Assert.Less(level.Dominoes.Center.z, Level15RubeGoldberg.EdgeZ, "they came down on the quilt, at " + level.Dominoes.Center);
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.DominoFullLine }, said, "not 'caught on the edge': nothing caught them");
            IEnumerator FromTheEdge()
            {
                yield return bot.LookAt(level.Dominoes.Center);
                yield return bot.Grab(level.Dominoes);
                yield return bot.WalkTo(new Vector3(16.5f, 3f, 6f));
                yield return bot.WalkTo(Level15RubeGoldberg.DominoStand, 0.15f);
                yield return bot.DropAt(Level15RubeGoldberg.DominoAim);
                yield return bot.Until(() => level.Lane.Seated, 3f);
            }
            Assert.IsTrue(Drive(FromTheEdge(), 30f), lastError);
            Assert.GreaterOrEqual(level.Dominoes.Scale, Level15RubeGoldberg.DominoWorks);
            Assert.AreEqual(1, said.Count);
        }

        [Test]
        public void TheBall_LetGoAtTheRingFromItsMark_WithoutAStep_IsToldToGoToTheEdge_AndFromThereItGoesIn()
        {
            Load();
            var said = Listen();
            IEnumerator NoStep()
            {
                yield return bot.WalkTo(new Vector3(10f, 3f, 4.5f), 0.4f, 10f, true);
                yield return bot.WalkTo(new Vector3(-16f, 3f, 4.5f), 0.4f, 12f, true);
                yield return bot.WalkTo(Level15RubeGoldberg.BallPickup, 0.1f);
                yield return bot.Grab(level.Ball);
                yield return bot.DropAt(Level15RubeGoldberg.BallAim);
                yield return bot.Wait(2.5f);
            }
            Assert.IsTrue(Drive(NoStep(), 40f), lastError);
            Assert.IsFalse(level.Hopper.Seated || level.Hopper.Seating || level.HopperApproach.Seating);
            Assert.AreEqual(Level15RubeGoldberg.BallMax, level.Ball.Scale, 0.01f, "as big as it gets");
            // (As found: nothing was said.)
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.BallFullLine }, said);
            IEnumerator FromTheEdge()
            {
                yield return bot.WalkTo(new Vector3(-16.6f, 3f, 6f));
                yield return bot.WalkTo(Level15RubeGoldberg.BallStand, 0.15f);
                yield return bot.LookAt(level.Ball.Center);
                yield return bot.Grab(level.Ball);
                yield return bot.DropAt(Level15RubeGoldberg.BallAim);
                yield return bot.Until(() => level.Hopper.Seated, 3f);
            }
            Assert.IsTrue(Drive(FromTheEdge(), 30f), lastError + " (the ball is at " + level.Ball.Center + ")");
            Assert.AreEqual(Level15RubeGoldberg.BallMax, level.Ball.Scale, 0.01f);
            Assert.AreEqual(1, said.Count);
        }

        // As found: picked up from right beside it on the quilt (it looks as big as a house) it hung at z 12.7,
        // y 5.7 from the edge: short of the hopper's approach (z 13.65, y 6).
        [Test]
        public void TheBall_AtItsLargestOnTheQuilt_PickedUpFromBesideIt_GoesInFromTheEdge()
        {
            Load();
            var said = Listen();
            Vector3 hung = Vector3.zero;
            Game.Events.PropDropped += e => hung = e.Prop.Center;
            level.Ball.Unfreeze();
            level.Ball.SetScale(Level15RubeGoldberg.BallMax);
            level.Ball.SetPose(new Vector3(-18f, 4.5f, 9.3f), Quaternion.identity);
            RunSeconds(1f);
            Game.Player.Teleport(new Vector3(-17.6f, 3f, level.Ball.Center.z - 2.3f), 0f);
            IEnumerator Beside()
            {
                yield return bot.LookAt(level.Ball.Center);
                yield return bot.Grab(level.Ball);
                yield return bot.WalkTo(Level15RubeGoldberg.BallStand, 0.2f, 6f);
                yield return bot.DropAt(Level15RubeGoldberg.BallAim);
                yield return bot.Until(() => level.Hopper.Seated, 3f);
            }
            Assert.IsTrue(Drive(Beside(), 30f), lastError + " (the ball is at " + level.Ball.Center + ", let go at " + hung + ")");
            Assert.Less(hung.z, 18.25f - Level15RubeGoldberg.HopperReach, "short of where the approach began: " + hung);
            CollectionAssert.IsEmpty(said);
        }

        // As found: the lever was only forgotten when the ruler was taken back. A prop has one mover for all who
        // drive it, so the next seating woke every earlier lever as well.
        [Test]
        public void ARulerSeatedAgain_AtAnotherSize_HasOneLever_AndLiesStillAfterTheRun()
        {
            Load();
            int struck = 0, shots = 0;
            Game.Events.SeesawStruck += e => struck++;
            Game.Events.SeesawProjectile += e => shots++;
            SeatDominoes(3.04f);
            SeatBall(3f);
            SeatFan(0.93f);
            SeatRuler(0.85f);
            Seesaw first = level.Seesaw;
            // By hand: taken back and put down again from a step behind the mark, where it comes out bigger.
            Game.Player.Teleport(Level15RubeGoldberg.RulerStand, 0f);
            IEnumerator Again()
            {
                yield return bot.Grab(level.Ruler);
                yield return bot.WalkTo(new Vector3(-5f, 0f, 13.2f), 0.1f);
                yield return bot.DropAt(Level15RubeGoldberg.RulerAim);
                yield return bot.Until(() => level.Fulcrum.Seated && level.Seesaw != null, 4f);
            }
            Assert.IsTrue(Drive(Again(), 20f), lastError);
            Assert.AreNotSame(first, level.Seesaw);
            Assert.IsFalse(first.Enabled, "the lever of the first seating is switched off");
            Assert.Greater(level.Ruler.Scale, 0.95f, "another size than before");
            Assert.IsTrue(Drive(Again(), 20f), lastError);

            Assert.IsTrue(RunTheMachine(40f), "stopped after " + level.Director.LastLink + " (" + level.Director.LastMessage + ")");
            Assert.AreEqual(1, struck, "one lever, one strike");
            Assert.AreEqual(1, shots, "one marble");
            Vector3 lies = level.Ruler.Position;
            Quaternion turned = level.Ruler.Rotation;
            for (int i = 0; i < 300; i++)
            {
                Game.Tick();
                Assert.Less(Vector3.Distance(lies, level.Ruler.Position), 1e-3f, "the ruler moved again " + i + " ticks after the run");
                Assert.Less(Quaternion.Angle(turned, level.Ruler.Rotation), 0.05f);
            }
            Assert.AreEqual(1, struck);
        }

        // As found: a toy in the hand when the pedal went down stayed the player's all through the run.
        [Test]
        public void AToyLetGoWhileTheMachineRuns_IsTheMachines_UntilItIsIdleAgain()
        {
            Load();
            IEnumerator Held()
            {
                yield return bot.Grab(level.Dominoes);
                yield return bot.WalkTo(new Vector3(10f, 3f, 4.5f), 0.4f, 10f, true);
                yield return bot.WalkTo(new Vector3(4f, 3f, 4.5f), 0.4f, 10f, true);
                yield return bot.WalkTo(Level15RubeGoldberg.PedalPoint, 0.2f, 8f);
                yield return bot.Until(() => level.Director.State == MachineState.Running, 2f);
            }
            Assert.IsTrue(Drive(Held(), 40f), lastError);
            Assert.AreSame(level.Dominoes, Game.Grabber.Held, "the pedal takes nothing out of the hand");
            IEnumerator LetGo()
            {
                yield return bot.DropAt(new Vector3(4f, 3.3f, 6f));
                yield return bot.Wait(0.2f);
            }
            Assert.IsTrue(Drive(LetGo(), 10f), lastError);
            Assert.AreEqual(MachineState.Running, level.Director.State);
            Assert.IsFalse(level.Dominoes.Grabbable, "let go in a run, they are the machine's");
            Assert.AreNotSame(level.Dominoes, Game.Grabber.FindTarget());
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Director.State == MachineState.Idle, 8f));
            Assert.IsTrue(level.Dominoes.Grabbable, "and the player's again when the machine is idle");
            IEnumerator Take()
            {
                yield return bot.Grab(level.Dominoes);
            }
            Assert.IsTrue(Drive(Take(), 10f), lastError);
        }

        // Level 7 taught the player to stand on the long end of a lever. Here that holds the marble down.
        [Test]
        public void ThePlayerOnTheRulersLongEnd_HoldsTheMarbleDown_IsToldSo_AndOffItTheMachineRuns()
        {
            Load();
            var said = Listen();
            SeatAll();
            StepOnThePedal();
            Game.Player.Teleport(new Vector3(-3.4f, 0.7f, Level15RubeGoldberg.AxisZ), -90f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Director.State != MachineState.Running, 12f));
            Assert.AreEqual(MachineState.Resetting, level.Director.State, "the run stopped");
            Assert.Less(level.MarbleApex, Level15RubeGoldberg.HoopNeed);
            // (As found: "The marble falls short. The ball is not heavy enough." It is.)
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.RiderLine }, said);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Director.State == MachineState.Idle && Game.Player.Grounded, 6f));
            Assert.Less(Game.Player.Position.y, 3.2f, "thrown, and down again on the floorboards or the quilt: " + Game.Player.Position);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Hopper.Seated, 2f), "the ball is back in the hopper");
            // Off the ruler it comes back to rest, and the machine runs through.
            Game.Player.Teleport(Level15RubeGoldberg.PedalPoint + new Vector3(0f, 0f, -3f), 0f);
            RunSeconds(2f);
            Assert.IsTrue(level.Seesaw.Enabled, "the lever is live again");
            Assert.IsTrue(RunTheMachine(40f), "stopped after " + level.Director.LastLink + " (" + level.Director.LastMessage + ")");
        }

        // A ball too light to lift the player off the long end does not tip the ruler at all: that is not a bare pivot.
        [Test]
        public void ThePlayerOnTheLongEnd_UnderALightBall_IsToldToStepOff_NotThatThePivotIsBare()
        {
            Load();
            var said = Listen();
            SeatAll(ball: 1.4f);
            StepOnThePedal();
            Game.Player.Teleport(new Vector3(-3.4f, 0.7f, Level15RubeGoldberg.AxisZ), -90f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Director.State != MachineState.Running, 12f));
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.RiderLine }, said);
            Assert.AreEqual(Level15RubeGoldberg.LinkBall, level.Director.LastLink);
        }

        // LEVELS: no other toy is heavy enough. The dominoes as big as they get, let go on the short arm by hand.
        [TestCase(0.8f)]
        [TestCase(1.03f)]
        [TestCase(1.2f)]
        public void NothingButTheBall_FlipsTheMarbleThroughTheHoop_AndWhatDoesNotIsToldSo(float ruler)
        {
            Load();
            var said = Listen();
            SeatRuler(ruler);
            SeatFan(0.93f);
            level.Dominoes.SetScale(Level15RubeGoldberg.DominoMax);
            level.Dominoes.SetPose(new Vector3(-6.3f, 3.2f, Level15RubeGoldberg.AxisZ), Quaternion.Euler(0f, 90f, 0f));
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Director.Runs == 1, 4f), "the dominoes on the short arm tip it");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Director.State == MachineState.Idle, 10f));
            Assert.IsFalse(level.MarbleThrough, "apex " + level.MarbleApex);
            Assert.Less(level.MarbleApex, Level15RubeGoldberg.HoopNeed - 0.04f, "and not by a hair");
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.OtherWeightLine }, said, "not 'the ball is not heavy enough'");
            Assert.IsTrue(level.Exit.Locked);

            // They still lie on the short arm: the ruler stays down. With the ball in the hopper the pedal says so.
            SeatBall(3f);
            said.Clear();
            Assert.IsFalse(RunTheMachine());
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.ShortArmLine }, said, "not 'a bare pivot'");
            // Taken off, the ruler comes back up, and with the dominoes on their lane the machine runs through.
            SeatDominoes(3.04f);
            RunSeconds(1f);
            Assert.IsTrue(level.Seesaw.Enabled);
            Assert.IsTrue(RunTheMachine(40f), "stopped after " + level.Director.LastLink + " (" + level.Director.LastMessage + ")");
        }

        [Test]
        public void WithoutTheDominoes_OrWithoutTheBall_TheRunStops_AndSaysWhere()
        {
            Load();
            var said = Listen();
            SeatBall(3f);
            SeatRuler(1.03f);
            SeatFan(0.93f);
            Assert.IsFalse(RunTheMachine());
            Assert.AreEqual(Level15RubeGoldberg.WindLine, said[said.Count - 1], "the wind has nothing to push");
            Assert.IsTrue(level.Exit.Locked);

            Unload();
            Load();
            said = Listen();
            SeatDominoes(3.04f);
            SeatRuler(1.03f);
            SeatFan(0.93f);
            Assert.IsFalse(RunTheMachine());
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.GateLine }, said);
            Assert.IsTrue(level.Exit.Locked);
        }

        // Nobody gets onto the machine from the quilt, and nothing a player throws at the baseball moves it.
        [Test]
        public void JumpingAtTheMachine_AndThrowingToysAtTheBaseball_GetNowhere()
        {
            Load();
            IEnumerator Leap(float x)
            {
                yield return bot.WalkTo(new Vector3(x, 3f, 11.35f), 0.25f, 4f, true);
                yield return bot.Jump();
                yield return bot.WalkTo(new Vector3(x, 0f, 19f), 0.2f, 1.6f, true);
            }
            // (Clear of the book and the spool: the lane twice, the shelf, the slide tower, the control tower, the duct.)
            foreach (float x in new[] { 16.5f, 11f, 22.5f, -19.5f, 1f, 5.5f })
            {
                Game.Player.Teleport(new Vector3(x, 3f, 3.5f), 0f);
                RunSeconds(0.1f);
                float top = 0f;
                var runner = new BotRunner(Leap(x), bot, Game);
                try
                {
                    for (int i = 0; i < 400 && runner.Advance(); i++)
                    {
                        Game.Tick();
                        if (Game.Player.Grounded) top = Mathf.Max(top, Game.Player.Position.y);
                    }
                }
                catch (BotException)
                {
                    // (It runs at a wall for as long as it is given.)
                }
                Assert.LessOrEqual(top, 3.05f, "a sprint jump from the quilt at x " + x + " ended standing at height " + top);
                Assert.Greater(Game.Player.Position.z, Level15RubeGoldberg.EdgeZ, "it leaves the quilt (x " + x + "): " + Game.Player.Position);
                Assert.Less(Game.Player.Position.y, 1.6f, "and ends in the trench, on nothing higher than a toy block (x " + x + "): " + Game.Player.Position);
                Assert.IsFalse(Game.LevelCompleted);
            }

            // The ball, as big as it gets, held against the baseball from the quilt's edge and let go.
            Unload();
            Load();
            IEnumerator AtTheBaseball()
            {
                yield return bot.WalkTo(new Vector3(10f, 3f, 4.5f), 0.4f, 10f, true);
                yield return bot.WalkTo(new Vector3(-16f, 3f, 4.5f), 0.4f, 12f, true);
                yield return bot.WalkTo(Level15RubeGoldberg.BallPickup, 0.12f);
                yield return bot.Grab(level.Ball);
                yield return bot.WalkTo(new Vector3(-16f, 3f, 3.5f), 0.4f, 6f);
                yield return bot.WalkTo(new Vector3(22f, 3f, 3.5f), 0.4f, 16f, true);
                yield return bot.WalkTo(new Vector3(22f, 3f, 11.1f), 0.2f, 6f);
                yield return bot.DropAt(Level15RubeGoldberg.BaseballCentre);
                yield return bot.Wait(5f);
            }
            Assert.IsTrue(Drive(AtTheBaseball(), 60f), lastError);
            Assert.IsTrue(level.Baseball.Frozen, "the baseball sits in its putty");
            Assert.Less(Vector3.Distance(level.Baseball.Center, Level15RubeGoldberg.BaseballCentre), 1e-3f);
            Assert.IsFalse(level.Clock.Broken);
            Assert.IsTrue(level.Exit.Locked);
            Assert.AreEqual(MachineState.Idle, level.Director.State);
            // The ball is somebody's to pick up again: on its spool, or in view in the trench.
            Assert.IsTrue(level.Ball.Frozen || level.Ball.Center.y < 3.2f, "the ball is at " + level.Ball.Center);
        }

        // LEVELS: reaching the plinth or the shelf (with the fan or the dominoes as a step) gains nothing.
        [Test]
        public void OnTheShelf_NobodyPushesTheBaseballOff_AndInTheWayOfTheDominoes_NobodyStopsTheRun()
        {
            Load();
            SeatAll();
            // Up on the shelf (a fixture's shortcut for the climb), shoving and jumping at the baseball.
            Game.Player.Teleport(new Vector3(23.8f, Level15RubeGoldberg.ShelfTop + 0.05f, Level15RubeGoldberg.AxisZ), -90f);
            RunSeconds(0.3f);
            Assert.IsTrue(Game.Player.Grounded, "on the shelf: " + Game.Player.Position);
            IEnumerator Shove()
            {
                for (int i = 0; i < 4; i++)
                {
                    yield return bot.WalkTo(Level15RubeGoldberg.BaseballCentre, 0.1f, 0.6f, true);
                    yield return bot.Jump();
                }
            }
            Drive(Shove(), 5f);
            RunSeconds(1f);
            Assert.IsTrue(level.Baseball.Frozen, "the baseball sits in its putty");
            Assert.Less(Vector3.Distance(level.Baseball.Center, Level15RubeGoldberg.BaseballCentre), 1e-3f);
            Assert.IsFalse(level.Clock.Broken);
            Assert.AreEqual(MachineState.Idle, level.Director.State);
            Assert.IsTrue(level.Exit.Locked);

            // In a run, standing on the lane between the last two dominoes: they fall all the same, the
            // baseball goes, and the player is still somewhere sane afterwards.
            StepOnThePedal();
            Game.Player.Teleport(new Vector3(18.4f, Level15RubeGoldberg.LaneTop + 0.3f, Level15RubeGoldberg.AxisZ), 90f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Director.State != MachineState.Running, 30f));
            Assert.AreEqual(MachineState.Done, level.Director.State, "stopped after " + level.Director.LastLink + " (" + level.Director.LastMessage + ")");
            Assert.IsTrue(level.Clock.Broken);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Game.Player.Grounded, 4f), "the player is at " + Game.Player.Position);
            Vector3 at = Game.Player.Position;
            Assert.IsTrue(at.y > -0.05f && at.y < 12f && Mathf.Abs(at.x) < Level15RubeGoldberg.HalfWidth && at.z > Level15RubeGoldberg.NearZ && at.z < Level15RubeGoldberg.BackZ, "in the room: " + at);
            // And the way out is still theirs to walk: off the lane, along the trench, through the door.
            IEnumerator Out()
            {
                yield return bot.WalkTo(new Vector3(at.x, 0f, 14f), 0.4f, 6f, true);
                yield return bot.WalkTo(new Vector3(26.5f, 0f, Level15RubeGoldberg.ExitCentre.z), 0.4f, 12f, true);
                yield return bot.WalkTo(new Vector3(Level15RubeGoldberg.ExitCentre.x, 0f, Level15RubeGoldberg.ExitCentre.z), 0.5f, 6f);
                yield return bot.Until(() => Game.LevelCompleted, 3f);
            }
            Assert.IsTrue(Drive(Out(), 30f), lastError);
        }

        [Test]
        public void AToyOnTheSlide_OnTheHoop_OrOnTheDuct_GoesBackToWhereItStarted()
        {
            Load();
            // (As found a ruler along the slide lay there for good, behind the front rail.)
            level.Ruler.SetScale(0.6f);
            level.Ruler.SetPose(new Vector3(-15.9f, 4.8f, Level15RubeGoldberg.AxisZ), Quaternion.Euler(0f, 90f, 0f));
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Parking.Returns == 1, 8f), "the ruler is at " + level.Ruler.Center);
            Assert.Less(Vector3.Distance(level.Ruler.Position, Level15RubeGoldberg.RulerStartPoint), 0.05f);
            Assert.AreEqual(Level15RubeGoldberg.RulerStart, level.Ruler.Scale, 1e-4f);
            level.Ruler.SetScale(0.6f);
            level.Ruler.SetPose(new Vector3(6f, 6.2f, Level15RubeGoldberg.AxisZ), Quaternion.Euler(0f, 90f, 0f));
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Parking.Returns == 2, 8f), "the ruler is at " + level.Ruler.Center);
            level.Ball.Unfreeze();
            level.Ball.SetScale(2f);
            level.Ball.SetPose(Level15RubeGoldberg.HoopCentre + Vector3.up * 1.3f, Quaternion.identity);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Parking.Returns == 3, 8f), "the ball is at " + level.Ball.Center);
            Assert.IsTrue(level.Ball.Frozen);
            // What rolls off the slide by itself is left alone: it ends on the floorboards, in view.
            level.Ball.Unfreeze();
            level.Ball.SetScale(1.1f);
            level.Ball.SetPose(new Vector3(-15.2f, 5.2f, Level15RubeGoldberg.AxisZ), Quaternion.identity);
            RunSeconds(6f);
            Assert.AreEqual(3, level.Parking.Returns);
            Assert.Less(level.Ball.Center.y, 2f);
            // And a run is not disturbed by any of it.
            SeatAll();
            Assert.IsTrue(RunTheMachine(40f), "stopped after " + level.Director.LastLink + " (" + level.Director.LastMessage + ")");
            Assert.AreEqual(3, level.Parking.Returns);
        }

        [Test]
        public void RestartingWhileTheMachineSetsItselfUp_OrWithTheFanInTheHand_AndSolvingAgain_Works()
        {
            Load();
            // The ball on its way back up to the hopper.
            SeatAll(ball: 2f);
            StepOnThePedal();
            StepOffThePedal();
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Director.State == MachineState.Resetting && level.Ball.Driven, 20f));
            RunSeconds(0.4f);
            Assert.IsTrue(level.Ball.Driven && !level.Hopper.Seated, "the ball is in the air");
            Game.RestartLevel();
            Game.Tick();
            AssertFresh("with the ball on its way back");

            // The fan in the hand, a thimble of a thing, in front of its garage.
            IEnumerator Hold()
            {
                yield return level.ToTheTrench(bot);
                yield return bot.WalkTo(Level15RubeGoldberg.FanStand, 0.12f, 12f, true);
                yield return bot.Grab(level.Fan);
                yield return bot.LookAt(Level15RubeGoldberg.FanAim);
                yield return bot.Wait(0.1f);
            }
            Assert.IsTrue(Drive(Hold(), 40f), lastError);
            Assert.AreSame(level.Fan, Game.Grabber.Held);
            Assert.Less(level.Fan.Scale, 1.5f);
            Game.RestartLevel();
            Game.Tick();
            AssertFresh("with the fan in the hand");

            // With the player thrown by the lever.
            SeatAll();
            StepOnThePedal();
            Game.Player.Teleport(new Vector3(-3.4f, 0.7f, Level15RubeGoldberg.AxisZ), -90f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.MarbleFlying, 8f));
            Game.RestartLevel();
            Game.Tick();
            AssertFresh("thrown by the lever");

            TestHelpers.PlayLevel(Game, 120f);
            Assert.IsTrue(level.Clock.Broken);
        }

        // The solver's four placements with the stand and the aim a little off, as a hand would have them.
        [TestCase(0.5f, 0.4f, 0.5f)]
        [TestCase(-0.5f, 0.4f, -0.5f)]
        [TestCase(0.4f, -0.4f, 0.6f)]
        [TestCase(-0.4f, -0.3f, -0.6f)]
        [TestCase(0f, 0.5f, 0f)]
        public void TheSolve_WithStandAndAimALittleOff_StillRunsThrough(float standOff, float aimX, float aimY)
        {
            Load();
            var said = Listen();
            Vector3 side = new Vector3(standOff, 0f, 0f), back = new Vector3(0f, 0f, -Mathf.Abs(standOff) * 0.6f);
            IEnumerator Solve()
            {
                yield return bot.Grab(level.Dominoes);
                yield return bot.WalkTo(new Vector3(16f, 3f, 6f));
                yield return bot.WalkTo(Level15RubeGoldberg.DominoStand + side + back, 0.2f);
                yield return bot.DropAt(Level15RubeGoldberg.DominoAim + new Vector3(aimX * 2f, aimY, 0f));
                yield return bot.Until(() => level.Lane.Seated, 5f);
                yield return bot.WalkTo(new Vector3(10f, 3f, 4.5f), 0.4f, 10f, true);
                yield return bot.WalkTo(new Vector3(-16f, 3f, 4.5f), 0.4f, 12f, true);
                yield return bot.WalkTo(Level15RubeGoldberg.BallPickup + side * 0.3f, 0.12f);
                yield return bot.Grab(level.Ball);
                yield return bot.WalkTo(new Vector3(-16.8f, 3f, 6f));
                yield return bot.WalkTo(Level15RubeGoldberg.BallStand + side + back, 0.2f);
                yield return bot.DropAt(Level15RubeGoldberg.BallAim + new Vector3(aimX * 1.6f, aimY * 1.6f, 0f));
                yield return bot.Until(() => level.Hopper.Seated, 5f);
                yield return level.ToTheTrench(bot);
                yield return bot.WalkTo(new Vector3(-3.5f, 0f, 14.2f), 0.4f, 10f, true);
                yield return bot.WalkTo(Level15RubeGoldberg.RulerStand + side * 0.6f + back * 0.5f, 0.15f);
                yield return bot.Grab(level.Ruler);
                yield return bot.LookAt(Level15RubeGoldberg.RulerAim);
                yield return bot.RotateHeld(aimX > 0f ? 7 : aimX < 0f ? 5 : 6);
                yield return bot.DropAt(Level15RubeGoldberg.RulerAim + new Vector3(aimX, 0f, aimY * 0.6f));
                yield return bot.Until(() => level.Fulcrum.Seated, 4f);
                yield return bot.WalkTo(Level15RubeGoldberg.FanStand + side * 0.6f + back, 0.15f);
                yield return bot.Grab(level.Fan);
                yield return bot.DropAt(Level15RubeGoldberg.FanAim + new Vector3(aimX * 0.3f, aimY * 0.4f, 0f));
                yield return bot.Until(() => level.Collar.Seated, 4f);
                yield return level.ToThePedal(bot);
                yield return bot.Until(() => level.Clock.Broken, 25f);
                yield return level.ToTheExit(bot);
            }
            Assert.IsTrue(Drive(Solve(), 110f), lastError + " (dominoes " + level.Dominoes.Scale + ", ball " + level.Ball.Scale + ", ruler " + level.Ruler.Scale + ", fan " + level.Fan.Scale +
                "; stopped after " + level.Director.LastLink + ": " + level.Director.LastMessage + "; said " + string.Join(" | ", said) + ")");
            Assert.IsTrue(Game.LevelCompleted);
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.DoneLine }, said, "nothing went wrong on the way");
        }

        [Test, Explicit]
        public void Probe_Solve()
        {
            Load();
            StringBuilder log = Trace();
            void Stage(string name, IEnumerator script, float seconds)
            {
                bool ok = Drive(script, seconds);
                log.AppendLine(F(Game.Time) + "  == " + name + (ok ? " ok" : " FAILED: " + lastError) + "  bot at " + F(bot.Player.Position));
            }
            log.AppendLine("start: target " + (Game.Grabber.FindTarget() != null ? Game.Grabber.FindTarget().Name : "nothing") + " eye " + F(Game.Player.Eye));
            Stage("dominoes", level.PlaceDominoes(bot), 30f);
            log.AppendLine("   dominoes s=" + F(level.Dominoes.Scale) + " at " + F(level.Dominoes.Position) + " seated " + level.Lane.Seated + " first domino x " + F(level.Chain.PieceCenter(0).x));
            Stage("ball", level.PlaceBall(bot), 40f);
            log.AppendLine("   ball s=" + F(level.Ball.Scale) + " m=" + F(level.Ball.Mass) + " at " + F(level.Ball.Position) + " seated " + level.Hopper.Seated);
            Stage("to the trench", level.ToTheTrench(bot), 30f);
            Stage("ruler", level.PlaceRuler(bot), 30f);
            log.AppendLine("   ruler s=" + F(level.Ruler.Scale) + " at " + F(level.Ruler.Position) + " seated " + level.Fulcrum.Seated + " seesaw " + (level.Seesaw != null));
            Stage("fan", level.PlaceFan(bot), 30f);
            log.AppendLine("   fan s=" + F(level.Fan.Scale) + " at " + F(level.Fan.Position) + " seated " + level.Collar.Seated);
            Stage("to the pedal", level.ToThePedal(bot), 30f);
            Stage("run", bot.Until(() => level.Clock.Broken, 25f), 26f);
            log.AppendLine("   apex " + F(level.MarbleApex) + " f " + F(level.LastF) + " through " + level.MarbleThrough + " links " + string.Join(",", level.Director.Fired));
            Stage("to the exit", level.ToTheExit(bot), 40f);
            log.AppendLine("completed " + Game.LevelCompleted + " at " + F(Game.Time));
            Note("solve", log.ToString());
        }

        // ---- The review, second pass: the machine's state when a run goes wrong at an odd moment -------------------

        // As found: the ball's run goes through the hopper's own drive, so until the ball has landed the hopper
        // counts it as seated; a run that stopped before that left the ball lying beside the ruler, on the short
        // arm, and every press after it said "The gate opens. Nothing rolls."
        // (On foot nobody is on the short arm 20 ticks after the pedal: a fixture's shortcut for the state. The
        // dominoes thrown at the ruler from the pedal, below, get there in time.)
        [TestCase(2)]
        [TestCase(20)]
        [TestCase(40)]
        public void ARunThatStopsWhileTheBallIsOnItsWay_LiftsItBackAllTheSame(int ticks)
        {
            Load();
            var said = Listen();
            SeatAll();
            StepOnThePedal();
            TestHelpers.Run(Game, ticks);
            Game.Player.Teleport(new Vector3(-6.05f, 1.6f, Level15RubeGoldberg.AxisZ), 90f);
            Assert.IsTrue(RunToTheStop(), "the run stopped with the ball on its way (last link " + level.Director.LastLink + ")");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Director.State == MachineState.Idle, 4f), "state " + level.Director.State);
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.PlayerLine }, said);
            RunSeconds(0.5f);
            Assert.IsTrue(level.Hopper.Seated, "the hopper has the ball again");
            Assert.Less(Vector3.Distance(level.Ball.Center, Level15RubeGoldberg.HopperFoot + Vector3.up * 1.5f), 0.05f, "the ball is back in the hopper: " + level.Ball.Center);
            Assert.IsTrue(level.Gate.IsClosed);
            Assert.IsTrue(level.Ball.Grabbable && level.Ruler.Grabbable && level.Fan.Grabbable && level.Dominoes.Grabbable, "the toys are the player's again");
            // Off the ruler it comes back to rest, and the next press runs the machine through.
            Game.Player.Teleport(Level15RubeGoldberg.PedalPoint + new Vector3(0f, 0f, -3f), 0f);
            RunSeconds(2f);
            Assert.IsTrue(level.Seesaw.Enabled, "the lever is live again");
            Assert.IsTrue(RunTheMachine(40f), "stopped after " + level.Director.LastLink + " (" + level.Director.LastMessage + ")");
        }

        // Ticks a run to where it stops. True if the ball was still on its way then (rolling, falling or in the air).
        bool RunToTheStop()
        {
            bool onItsWay = false;
            for (int i = TestHelpers.Ticks(20f); i > 0 && level.Director.State == MachineState.Running; i--)
            {
                onItsWay = level.BallRun.Running;
                Game.Tick();
            }
            Assert.AreEqual(MachineState.Resetting, level.Director.State, "the run stopped");
            return onItsWay;
        }

        // The same by something a player can do: on the pedal with the dominoes in the hand, and at once they
        // are let go over the ruler's short arm. They are not heavy enough; the marble is back in its cap
        // before the ball has come over the books.
        [Test]
        public void TheDominoes_ThrownAtTheRulerFromThePedal_StopTheRun_AndTheBallIsLiftedBack()
        {
            Load();
            var said = Listen();
            SeatBall(3f);
            SeatRuler(1.03f);
            SeatFan(0.93f);
            StepOnThePedal();
            StepOffThePedal();
            // (A fixture's shortcut for the let-go: as big as they get, over the short arm.)
            level.Dominoes.SetScale(Level15RubeGoldberg.DominoMax);
            level.Dominoes.SetPose(new Vector3(-6.3f, 3.2f, Level15RubeGoldberg.AxisZ), Quaternion.Euler(0f, 90f, 0f));
            Assert.IsTrue(RunToTheStop(), "the run stopped with the ball in the air (last link " + level.Director.LastLink + ")");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Director.State == MachineState.Idle, 4f), "state " + level.Director.State);
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.OtherWeightLine }, said);
            RunSeconds(0.5f);
            Assert.IsTrue(level.Hopper.Seated, "the hopper has the ball again");
            Assert.Less(Vector3.Distance(level.Ball.Center, Level15RubeGoldberg.HopperFoot + Vector3.up * 1.5f), 0.05f, "the ball is back in the hopper: " + level.Ball.Center);
            // The dominoes still lie on the short arm: the next press says so. On their lane, the machine runs through.
            said.Clear();
            Assert.IsFalse(RunTheMachine());
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.ShortArmLine }, said);
            RunSeconds(0.5f);
            Assert.IsTrue(level.Hopper.Seated, "and the ball is lifted back once more");
            SeatDominoes(3.04f);
            RunSeconds(1f);
            Assert.IsTrue(level.Seesaw.Enabled);
            Assert.IsTrue(RunTheMachine(40f), "stopped after " + level.Director.LastLink + " (" + level.Director.LastMessage + ")");
        }

        // As found: let go from the pedal in a run, the dominoes were taken by the lane's approach in the very
        // tick (the socket found them grabbable: the let-go is only heard when the tick ends) and came out of it
        // grabbable again, in the middle of the run they then took part in.
        [Test]
        public void TheDominoes_LetGoFromThePedalInARun_AreTaken_AreTheMachines_AndTheRunGoesThrough()
        {
            Load();
            var said = Listen();
            SeatBall(3f);
            SeatRuler(1.03f);
            SeatFan(0.93f);
            IEnumerator FromThePedal()
            {
                yield return bot.Grab(level.Dominoes);
                yield return bot.WalkTo(new Vector3(10f, 3f, 4.5f), 0.4f, 10f, true);
                yield return bot.WalkTo(new Vector3(4f, 3f, 4.5f), 0.4f, 10f, true);
                yield return bot.WalkTo(Level15RubeGoldberg.PedalPoint, 0.2f, 8f);
                yield return bot.Until(() => level.Director.State == MachineState.Running, 2f);
                yield return bot.LookAt(Level15RubeGoldberg.DominoAim);
                // (Seen from here the lane lies 49 degrees to the right: three clicks put the set square to it.)
                yield return bot.RotateHeld(-3);
                yield return bot.DropAt(Level15RubeGoldberg.DominoAim);
                yield return bot.Until(() => level.Lane.Seated, 3f);
                // Looking straight at them, clicking: nothing comes away.
                yield return bot.LookAt(level.Dominoes.Center);
                yield return bot.Click();
                yield return bot.Wait(0.2f);
                yield return bot.Until(() => level.Director.State != MachineState.Running, 30f);
            }
            var runner = new BotRunner(FromThePedal(), bot, Game);
            bool running = false;
            for (int i = TestHelpers.Ticks(70f); i > 0 && runner.Advance(); i--)
            {
                Game.Tick();
                running |= level.Director.State == MachineState.Running;
                if (level.Director.State == MachineState.Running && !level.Dominoes.Held)
                    Assert.IsFalse(level.Dominoes.Grabbable, "in a run the dominoes are the machine's (tick " + Game.LevelTicks + ", lane " + level.Lane.Seated + ")");
            }
            Assert.IsTrue(runner.Finished, "the script ran out of time");
            Assert.IsTrue(running);
            Assert.IsNull(Game.Grabber.Held, "the click took nothing");
            Assert.GreaterOrEqual(level.Dominoes.Scale, Level15RubeGoldberg.DominoWorks);
            Assert.AreEqual(MachineState.Done, level.Director.State, "stopped after " + level.Director.LastLink + " (" + level.Director.LastMessage + ")");
            Assert.IsTrue(level.Clock.Broken);
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.DoneLine }, said);
        }

        // As found: a toy that came into an approach while the machine was setting itself up was locked (the
        // machine's) when the socket took it, and the socket handed it on like that after the machine was idle:
        // seated on its station, at a size that works, and not to be picked up until the next run was over.
        [Test]
        public void AToyTakenByAnApproach_WhileTheMachineSetsItselfUp_IsThePlayersOnceTheMachineIsIdle()
        {
            Load();
            StepOnThePedal();
            StepOffThePedal();
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Director.State == MachineState.Resetting, 4f));
            // Three quarters of a second into the second and a half the machine takes: the dominoes come down
            // through the air in front of the lane (a fixture's shortcut for a let-go a moment earlier).
            TestHelpers.Run(Game, 80);
            Assert.AreEqual(MachineState.Resetting, level.Director.State);
            Pose seat = Level15RubeGoldberg.DominoSeat(3f);
            level.Dominoes.SetScale(3f);
            level.Dominoes.SetPose(new Vector3(seat.position.x, 8.5f, 14.5f), Quaternion.identity);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.LaneApproach.Seating, 0.2f), "the approach takes them");
            Assert.AreEqual(MachineState.Resetting, level.Director.State);
            Assert.IsFalse(level.Dominoes.Grabbable, "while the machine sets itself up they are the machine's");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Lane.Seated, 3f));
            Assert.AreEqual(MachineState.Idle, level.Director.State);
            Assert.IsTrue(level.Dominoes.Grabbable, "the machine is idle: they are the player's");
            IEnumerator TakeBack()
            {
                // (South of the ramp's notch.)
                yield return bot.WalkTo(new Vector3(4f, 3f, 4.5f), 0.4f, 6f);
                yield return bot.WalkTo(new Vector3(10f, 3f, 4.5f), 0.4f, 6f, true);
                yield return bot.WalkTo(new Vector3(16f, 3f, 6f), 0.4f, 6f, true);
                yield return bot.WalkTo(Level15RubeGoldberg.DominoStand, 0.15f);
                yield return bot.Grab(level.Dominoes);
                yield return bot.Wait(0.2f);
            }
            Assert.IsTrue(Drive(TakeBack(), 30f), lastError);
            Assert.AreSame(level.Dominoes, Game.Grabber.Held);
            Assert.IsFalse(level.Lane.Seated);
        }

        // The level's promise, end to end and by the bot alone (no fixture puts anything anywhere): the machine is
        // run five times, stops at a different link four times and says which, every toy is put right by hand,
        // and nothing is left over from one run to the next.
        [Test]
        public void AMessyPlaythrough_EveryStopIsToldAndPutRightByHand_AndTheFifthRunGoesThrough()
        {
            Load();
            var said = Listen();
            IEnumerator Press(int run)
            {
                yield return bot.WalkTo(Level15RubeGoldberg.PedalPoint, 0.2f, 8f);
                yield return bot.Until(() => level.Director.Runs == run, 2f);
                yield return bot.Until(() => level.Director.State != MachineState.Running, 30f);
            }
            IEnumerator UntilIdle()
            {
                yield return bot.Until(() => level.Director.State == MachineState.Idle, 4f);
            }

            // 1. The empty machine.
            IEnumerator Empty()
            {
                yield return bot.WalkTo(new Vector3(10f, 3f, 4.5f), 0.4f, 10f, true);
                yield return bot.WalkTo(new Vector3(4f, 3f, 4.5f), 0.4f, 10f, true);
                yield return Press(1);
                yield return UntilIdle();
            }
            Assert.IsTrue(Drive(Empty(), 30f), lastError);
            CollectionAssert.AreEqual(new[] { Level15RubeGoldberg.GateLine }, said);

            // 2. The dominoes held too low: they catch the lane's edge and fall. From the edge, at the outline, they go on.
            IEnumerator Dominoes()
            {
                yield return bot.WalkTo(new Vector3(4f, 3f, 4.5f), 0.4f, 8f);
                yield return bot.WalkTo(new Vector3(10f, 3f, 4.5f), 0.4f, 10f, true);
                yield return bot.WalkTo(Level15RubeGoldberg.SpawnPoint, 0.08f);
                yield return bot.LookAt(level.Dominoes.Center);
                yield return bot.Grab(level.Dominoes);
                yield return bot.WalkTo(new Vector3(16f, 3f, 6f));
                yield return bot.WalkTo(Level15RubeGoldberg.DominoStand, 0.15f);
                yield return bot.DropAt(new Vector3(Level15RubeGoldberg.DominoAim.x, 7f, Level15RubeGoldberg.BackZ));
                yield return bot.Wait(1.5f);
            }
            Assert.IsTrue(Drive(Dominoes(), 40f), lastError);
            Assert.IsFalse(level.Lane.Seated);
            Assert.AreEqual(Level15RubeGoldberg.DominoEdgeLine, said[said.Count - 1]);
            IEnumerator DominoesAgain()
            {
                yield return bot.Grab(level.Dominoes);
                yield return bot.DropAt(Level15RubeGoldberg.DominoAim);
                yield return bot.Until(() => level.Lane.Seated, 4f);
            }
            Assert.IsTrue(Drive(DominoesAgain(), 20f), lastError);
            Assert.GreaterOrEqual(level.Dominoes.Scale, Level15RubeGoldberg.DominoWorks);

            // 3. The ball picked up from too far: told, and seated too light. The ruler not turned: told, then turned.
            Assert.IsTrue(Drive(BallFrom(3.2f, 11.1f, Level15RubeGoldberg.BallAim), 40f), lastError);
            Assert.AreEqual(Level15RubeGoldberg.FarLine, said[said.Count - 1]);
            Assert.IsTrue(level.Hopper.Seated);
            Assert.Less(level.Ball.Scale, Level15RubeGoldberg.BallWorks);
            Assert.IsTrue(Drive(RulerFrom(Level15RubeGoldberg.RulerStand, Level15RubeGoldberg.RulerAim, 0), 60f), lastError);
            Assert.AreEqual(Level15RubeGoldberg.RulerTurnLine, said[said.Count - 1]);
            IEnumerator Turn()
            {
                yield return bot.Grab(level.Ruler);
                yield return bot.LookAt(Level15RubeGoldberg.RulerAim);
                yield return bot.RotateHeld(6);
                yield return bot.DropAt(Level15RubeGoldberg.RulerAim);
                yield return bot.Until(() => level.Fulcrum.Seated, 4f);
            }
            Assert.IsTrue(Drive(Turn(), 20f), lastError);

            // Second run: the marble falls short. The ball, taken back from the edge and held to its ring again, grows.
            IEnumerator Second()
            {
                yield return level.ToThePedal(bot);
                yield return bot.Until(() => level.Director.Runs == 2, 2f);
                yield return bot.Until(() => level.Director.State != MachineState.Running, 30f);
                yield return UntilIdle();
                yield return bot.WalkTo(new Vector3(4f, 3f, 4.5f), 0.4f, 8f);
                yield return bot.WalkTo(new Vector3(-16f, 3f, 4.5f), 0.4f, 12f, true);
                yield return bot.WalkTo(new Vector3(-16.6f, 3f, 6f));
                yield return bot.WalkTo(Level15RubeGoldberg.BallStand, 0.15f);
                yield return bot.Grab(level.Ball);
                yield return bot.DropAt(Level15RubeGoldberg.BallAim);
                yield return bot.Until(() => level.Hopper.Seated, 4f);
            }
            Assert.IsTrue(Drive(Second(), 80f), lastError + " (stopped after " + level.Director.LastLink + ": " + level.Director.LastMessage + ")");
            Assert.AreEqual(Level15RubeGoldberg.MarbleLine, said[said.Count - 1]);
            Assert.GreaterOrEqual(level.Ball.Scale, Level15RubeGoldberg.BallWorks);

            // Third run: the switch clicks on an empty garage. The fan from the garage's door: a thimble of a fan.
            IEnumerator Third()
            {
                yield return bot.WalkTo(new Vector3(-16.6f, 3f, 6f));
                yield return bot.WalkTo(new Vector3(-16f, 3f, 4.5f), 0.4f, 6f);
                yield return bot.WalkTo(new Vector3(4f, 3f, 4.5f), 0.4f, 12f, true);
                yield return Press(3);
                yield return UntilIdle();
            }
            Assert.IsTrue(Drive(Third(), 60f), lastError + " (stopped after " + level.Director.LastLink + ": " + level.Director.LastMessage + ")");
            Assert.AreEqual(Level15RubeGoldberg.FanMissingLine, said[said.Count - 1]);
            Assert.IsTrue(Drive(FanFrom(new Vector3(1.25f, 0f, 15.6f), Level15RubeGoldberg.FanAim), 60f), lastError);
            Assert.IsTrue(level.Collar.Seated);
            Assert.Less(level.Fan.Scale, Level15RubeGoldberg.FanReference);

            // Fourth run: it wheezes. Taken out and let go from the far side of the walkway, it blows.
            IEnumerator Fourth()
            {
                yield return level.ToThePedal(bot);
                yield return bot.Until(() => level.Director.Runs == 4, 2f);
                yield return bot.Until(() => level.Director.State != MachineState.Running, 30f);
                yield return UntilIdle();
                yield return level.ToTheTrench(bot);
                yield return bot.WalkTo(Level15RubeGoldberg.FanStand, 0.1f, 12f, true);
                yield return bot.Grab(level.Fan);
                yield return bot.WalkTo(new Vector3(1.25f, 0f, 11.95f), 0.1f);
                yield return bot.DropAt(Level15RubeGoldberg.FanAim);
                yield return bot.Until(() => level.Collar.Seated, 4f);
            }
            Assert.IsTrue(Drive(Fourth(), 80f), lastError + " (stopped after " + level.Director.LastLink + ": " + level.Director.LastMessage + ")");
            Assert.AreEqual(Level15RubeGoldberg.FanSmallLine, said[said.Count - 1]);
            Assert.GreaterOrEqual(level.Fan.Scale, Level15RubeGoldberg.FanReference);

            // Fifth run: all the way.
            IEnumerator Fifth()
            {
                yield return level.ToThePedal(bot);
                yield return bot.Until(() => level.Director.Runs == 5, 2f);
                yield return bot.Until(() => level.Clock.Broken, 25f);
                yield return level.ToTheExit(bot);
            }
            Assert.IsTrue(Drive(Fifth(), 80f), lastError + " (stopped after " + level.Director.LastLink + ": " + level.Director.LastMessage + ")");
            Assert.IsTrue(Game.LevelCompleted);
            CollectionAssert.AreEqual(new[]
            {
                Level15RubeGoldberg.GateLine, Level15RubeGoldberg.DominoEdgeLine, Level15RubeGoldberg.FarLine, Level15RubeGoldberg.RulerTurnLine,
                Level15RubeGoldberg.MarbleLine, Level15RubeGoldberg.FanMissingLine, Level15RubeGoldberg.FanSmallLine, Level15RubeGoldberg.DoneLine,
            }, said, "one line for each thing that went wrong, in the order it went wrong, and the ending: " + string.Join(" | ", said));
            Assert.AreEqual(5, level.Director.Runs);
        }
    }

    /// <summary>
    /// Level 15 on the pause card: every hint is set at the panel's full size, as in the levels before it
    /// (Phase2CampaignTests.EveryHint_FitsThePauseCardsPanel_AtFullSize does the same for levels 5 to 9).
    /// </summary>
    public class Level15UiTests
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
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            host = null;
            Game.Current?.Dispose();
            UiCapture.Reset();
            Settings.Use(null);
        }

        [Test]
        public void EveryHint_FitsThePauseCardsPanel_AtFullSize()
        {
            host = new GameObject("Level 15 Test Runner") { hideFlags = HideFlags.DontSave };
            runner = host.AddComponent<GameRunner>();
            var devices = new FakeDevices();
            runner.Begin(LaunchOptions.FromUrl("?level=15"), new RunnerOptions { Devices = devices, Store = new MemoryStore(), Presenters = UiTestKit.Own() });
            MenuPresenter menu = runner.Presentation.Get<MenuPresenter>();
            Assert.IsNotNull(menu, "the menu presenter attached");
            for (int i = 0; i < 30; i++) runner.Frame(Sim.Dt);
            devices.State.EscapePressed = true;
            runner.Frame(Sim.Dt);
            devices.State.EscapePressed = false;
            PauseMenu pause = menu.Pause;
            Assert.AreSame(pause, menu.Current, "paused");
            Assert.AreEqual(15, runner.Game.Level.Id);
            string[] hints = runner.Game.Level.Hints;
            for (int h = 0; h < hints.Length; h++)
            {
                pause.NextHint();
                runner.Frame(Sim.Dt);
                Assert.AreEqual(hints[h], pause.HintText.text);
                pause.HintText.ForceMeshUpdate();
                float size = pause.HintText.fontSize;
                Debug.Log("[Toybox] Level 15, hint " + (h + 1) + ": " + hints[h].Length + " characters, " + pause.HintText.textInfo.lineCount + " lines at size " + size.ToString("0.0"));
                Assert.Greater(pause.HintText.textInfo.lineCount, 0, "the text was laid out");
                Assert.GreaterOrEqual(size, UiTheme.BodySize - 0.05f, "hint " + (h + 1) + " is set smaller to fit (" + hints[h].Length + " characters): \"" + hints[h] + "\"");
                Assert.IsFalse(pause.HintText.isTextOverflowing, "hint " + (h + 1) + " is cut off");
            }
        }
    }
}
