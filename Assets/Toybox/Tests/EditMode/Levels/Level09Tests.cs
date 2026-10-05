using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Levels;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests
{
    /// <summary>
    /// Level 9, The Moving Train: the solver, the gap that cannot be crossed without the plank, the plank's
    /// working sizes (LEVELS Appendix B: 3.7 to the clamp at 4.4, intended 4.03), and what keeps the level
    /// from being soft-locked.
    /// </summary>
    public class Level09Tests : SimTest
    {
        Level09MovingTrain level;
        Bot bot;

        void Load()
        {
            Game = Game.Create();
            Game.LoadLevel(9);
            level = (Level09MovingTrain)Game.Level;
            bot = new Bot(Game);
        }

        List<string> Listen()
        {
            var said = new List<string>();
            Game.Events.Message += e => said.Add(e.Text);
            return said;
        }

        // Pumps a bot script for at most this long. True if it ran to its end; a command that fails (a walk
        // that never arrives) ends it too, and that is not an error here.
        bool Drive(IEnumerator script, float seconds)
        {
            var runner = new BotRunner(script, bot, Game);
            try
            {
                for (int i = TestHelpers.Ticks(seconds); i > 0; i--)
                {
                    if (!runner.Advance()) return true;
                    Game.Tick();
                }
            }
            catch (BotException)
            {
            }
            return false;
        }

        // A spot on the platform this far behind the plank's middle, in line with it.
        static Vector3 Behind(float distance) =>
            Level09MovingTrain.SpoolBase - Quaternion.Euler(0f, Level09MovingTrain.PlankYaw, 0f) * Vector3.forward * distance;

        bool OnTheSpool() =>
            level.Plank.Scale == Level09MovingTrain.PlankStart && level.Plank.Frozen &&
            Vector3.Distance(level.Plank.Center, Level09MovingTrain.SpoolBase + Vector3.up * 1.14f) < 0.03f;

        bool MiddleWagonComing() => level.MiddleWagonComing();

        // From the platform's edge down onto a wagon in the middle of the train.
        IEnumerator OntoTheTrain(bool sprint = true)
        {
            yield return bot.WalkTo(Level09MovingTrain.AroundSpool, 0.3f, 12f);
            yield return bot.WalkTo(Level09MovingTrain.EdgeSpot, 0.15f, 12f);
            yield return bot.Until(() => level.WagonAt(0f, Level09MovingTrain.StepSeconds, 2f) >= 3 && level.WagonAt(0f, Level09MovingTrain.StepSeconds, 2f) <= 5, 20f);
            var onto = new Vector3(0f, -1f, 5.4f);
            yield return Level09MovingTrain.Chase(bot, () => onto, () => Game.Player.Grounded && Game.Player.Position.y < -0.9f, 2f, sprint);
        }

        // LEVELS: "a step off lands on the deck". (With the track at LEVELS' radius of 14 it did not: at a
        // walk the player came down 0.97 from the edge, in the 1.2 gap, and at a run 0.2 inside the deck.)
        [TestCase(false, TestName = "SteppingOffThePlatform_AtAWalk_LandsOnAWagon")]
        [TestCase(true, TestName = "SteppingOffThePlatform_AtARun_LandsOnAWagon")]
        public void SteppingOffThePlatform(bool sprint)
        {
            Load();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            BotRunner.Run(Game, OntoTheTrain(sprint), 40f);
            float landed = Level09MovingTrain.RadiusOf(Game.Player.Position);
            TestHelpers.RunSeconds(Game, 1f);
            Debug.Log("[Level09] a step off the platform " + (sprint ? "at a run" : "at a walk") + " lands " + (Level09MovingTrain.DeckOuter - landed).ToString("0.00") +
                      " inside the deck's outer edge");
            Assert.AreEqual(0, respawns);
            Assert.IsTrue(Game.Player.Grounded);
            Assert.AreEqual(Level09MovingTrain.DeckY, Game.Player.Position.y, 0.03f);
            Assert.Greater(Level09MovingTrain.DeckOuter - landed, 0.25f, "well onto the deck");
            Assert.Greater(landed - Level09MovingTrain.DeckInner, 0.5f, "and not over its far side");
        }

        // Picks the plank up (from the start, or from somewhere else), carries it to a standing place and
        // holds it toward a point.
        IEnumerator Hold(Vector3? pickupFrom, Vector3 stand, Vector3 aim)
        {
            if (pickupFrom.HasValue) yield return bot.WalkTo(pickupFrom.Value, 0.05f, 12f);
            yield return bot.Grab(level.Plank);
            yield return bot.WalkTo(Level09MovingTrain.AroundSpool, 0.3f, 12f);
            yield return bot.WalkTo(stand, 0.15f, 12f);
            yield return bot.LookAt(aim);
        }

        // ... and lets go when a wagon will be under its near end.
        IEnumerator Bridge(Vector3? pickupFrom, Vector3 stand, Vector3 aim)
        {
            yield return Hold(pickupFrom, stand, aim);
            yield return bot.Until(MiddleWagonComing, 20f);
            yield return bot.DropAt(aim);
        }

        IEnumerator RideIn()
        {
            yield return bot.Until(() => level.Carrier.IsCarrying(level.Plank), 3f);
            yield return bot.WalkTo(Level09MovingTrain.EdgeSpot, 0.15f, 12f);
            yield return level.Board(bot);
            yield return level.WalkIn(bot);
        }

        // The prop a ray from the eye to the point meets first, or null.
        Prop Seen(Vector3 eye, Vector3 point)
        {
            Vector3 to = point - eye;
            return Game.PhysicsScene.Raycast(eye, to.normalized, out RaycastHit hit, to.magnitude + 0.1f, Layers.SolidMask, QueryTriggerInteraction.Ignore)
                ? PropRef.Of(hit.collider) : null;
        }

        // What stands between the eye and a point, or null if the point is in view.
        string Blocker(Vector3 eye, Vector3 point)
        {
            Vector3 to = point - eye;
            return Game.PhysicsScene.Raycast(eye, to.normalized, out RaycastHit hit, to.magnitude - 0.05f, Layers.SolidMask, QueryTriggerInteraction.Ignore)
                ? hit.collider.name + " at " + hit.point : null;
        }

        [Test]
        public void TheLevelIsBuiltAsSpecified()
        {
            Load();
            Assert.AreEqual("moving-train", level.Slug);
            Assert.AreEqual("The Moving Train", level.Title);
            Assert.AreEqual(2, level.Phase);
            Assert.AreEqual("high-shelf", level.Environment);
            Assert.AreEqual(-14f, level.GroundY);
            Assert.AreEqual(-12f, level.KillY);
            Assert.AreEqual(3, level.Hints.Length);
            Assert.IsNotEmpty(level.Blurb);

            Prop plank = level.Plank;
            Assert.AreEqual(1, Game.Props.Count, "the plank is the only prop: the spool, the luggage and the train are scenery");
            Assert.AreEqual(0.65f, plank.Scale, 1e-4f);
            Assert.AreEqual(0.3f, plank.MinScale, 1e-4f);
            Assert.AreEqual(4.4f, plank.MaxScale, 1e-4f, "13.2 long at the clamp: exactly the distance from the platform to the doorstep");
            Assert.IsTrue(plank.HasTag(Level09MovingTrain.PlankTag));
            Assert.AreEqual(GrabPose.Snap90, plank.GrabPose);
            Assert.IsTrue(plank.Grabbable);
            Assert.IsTrue(plank.Frozen, "it stays on its spool until it is picked up");
            Assert.IsTrue(Toybox.Art.Palette.Same(Toybox.Art.Palette.Lilac.Hero, Toybox.Art.ToyInfo.Of(plank.GameObject).Candy), "Lemon: the Lilac room's hero candy");
            TestHelpers.RunSeconds(Game, 1f);
            Assert.Less(Vector3.Distance(plank.Center, new Vector3(2.5f, 1.14f, -1f)), 0.01f, "on the spool, as LEVELS places it");
            // It points at the lighthouse, and the player starts a step behind it, looking along it.
            Vector3 along = plank.Rotation * Vector3.forward;
            Vector3 toTower = (Level09MovingTrain.Axis - Level09MovingTrain.SpoolBase).normalized;
            Assert.Greater(Vector3.Dot(along, toTower), 0.9999f);
            Assert.AreEqual(1.46f, Vector3.Distance(Game.Player.Eye, plank.Center), 0.02f, "LEVELS: g = 1.46");
            Assert.Greater(Vector3.Dot(Game.Player.Forward, toTower), 0.97f);
            Assert.AreSame(plank, Game.Grabber.FindTarget(), "the view the level starts with is on the plank: a click picks it up");

            Train train = level.Train;
            Assert.AreEqual(8, train.Cars);
            Assert.AreEqual(15f, train.LapSeconds, 1e-3f);
            Assert.AreEqual(Level09MovingTrain.TrackRadius, train.Radius);
            Assert.AreEqual(14.6f, train.Radius, 1e-4f, "0.6 beyond LEVELS: a step off the platform has to land on a wagon");
            Assert.AreEqual(-1f, train.DeckY);
            Assert.AreEqual(0f, train.StationBearing);
            Assert.AreEqual(20f, train.Center.y);
            // This level's engine: slick (a plank let go onto it is not carried off), and nothing of it stands
            // above its roof for a held plank to stop against (the funnel is looks only).
            int solid = 0;
            foreach (Collider collider in train.Engine.Body.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled) continue;
                solid++;
                Assert.IsNotNull(collider.sharedMaterial, collider.name);
                Assert.AreEqual(0f, collider.sharedMaterial.dynamicFriction);
                Assert.AreEqual(PhysicsMaterialCombine.Minimum, collider.sharedMaterial.frictionCombine);
                Assert.LessOrEqual(collider.bounds.max.y, train.DeckY + ToyFactory.TrainEngineHeight + 0.01f);
            }
            Assert.AreEqual(2, solid, "its deck and its body");
            Assert.IsFalse(level.Exit.Locked);
            Assert.Less(Vector3.Distance(level.Exit.Position, new Vector3(0f, 0.25f, 20f)), 1e-3f);
            Assert.AreEqual(new Vector3(5f, 3.5f, 5f), level.Exit.Trigger.Volume.Size);

            // From the start the player sees the toy, the lighthouse with the outline on it, and the track beyond the gap.
            Vector3 eye = Game.Player.Eye;
            Assert.AreSame(plank, Seen(eye, plank.Center), "the plank is in plain view");
            Assert.IsNull(Blocker(eye, new Vector3(0f, Level09MovingTrain.OutlineY, 18f)), "the outline on the lighthouse is in view");
            Assert.IsNull(Blocker(eye, new Vector3(0f, -0.9f, 16.8f)), "and so is the doorstep");
            Assert.IsNull(Blocker(eye, new Vector3(-Level09MovingTrain.TrackRadius, -0.9f, 20f)), "and the track where it comes round");
            Assert.IsNull(Blocker(eye, new Vector3(Level09MovingTrain.TrackRadius, -0.9f, 20f)));

            // The geometry of LEVELS: platform top 0, doorstep and decks one below, nothing under the track.
            Assert.IsTrue(Game.PhysicsScene.Raycast(new Vector3(0f, 5f, 3.2f), Vector3.down, out RaycastHit hit, 30f, Layers.SolidMask));
            Assert.AreEqual(0f, hit.point.y, 1e-3f, "the platform's edge");
            Assert.IsTrue(Game.PhysicsScene.Raycast(new Vector3(0f, 5f, 17f), Vector3.down, out hit, 30f, Layers.SolidMask));
            Assert.AreEqual(-1f, hit.point.y, 1e-3f, "the doorstep");
            Assert.IsTrue(Game.PhysicsScene.Raycast(new Vector3(0f, 5f, 10f), Vector3.down, out hit, 30f, Layers.SolidMask));
            Assert.AreEqual(-14f, hit.point.y, 0.01f, "between the track and the doorstep there is only the shelf");
            Assert.IsTrue(Game.PhysicsScene.Raycast(new Vector3(0f, 5f, 10f), Vector3.up, out hit, 30f, Layers.SolidMask));
            Assert.AreEqual(16f, hit.point.y, 0.01f, "the sky cap closes the top");
            Assert.IsTrue(Game.PhysicsScene.Raycast(new Vector3(0f, 5f, 10f), Vector3.forward, out hit, 30f, Layers.SolidMask));
            Assert.AreEqual(18f, hit.point.z, 0.01f, "the lighthouse: a sheer wall two units from its axis");
            Assert.IsTrue(Game.PhysicsScene.Raycast(new Vector3(0f, 5f, 10f), Vector3.right, out hit, 30f, Layers.SolidMask));
            Assert.AreEqual(20f, hit.point.x, 0.01f, "the level's own wall");
        }

        [Test]
        public void TheText_IsPlain_AndSaysPickUp()
        {
            Load();
            var texts = new List<string>(level.Hints)
            {
                level.Blurb, level.Title, Level09MovingTrain.FarLine, Level09MovingTrain.NearLine, Level09MovingTrain.ShortLine,
                Level09MovingTrain.MissedLine, Level09MovingTrain.ReachLine, Level09MovingTrain.CrumbLine, Level09MovingTrain.StrandedLine, Level09MovingTrain.HopLine,
                Level09MovingTrain.TrainShortLine, Level09MovingTrain.FullSizeLine, Level09MovingTrain.UnderLine, Level09MovingTrain.LeapLine,
            };
            Assert.AreEqual(texts.Count, new HashSet<string>(texts).Count, "no two lines are the same");
            foreach (string text in texts)
            {
                foreach (char c in text) Assert.IsTrue(c >= ' ' && c <= '~', "not plain ASCII: '" + c + "' in \"" + text + "\"");
                StringAssert.DoesNotContain("grab", text.ToLowerInvariant());
            }
            Assert.AreEqual(Level02ThimbleChasm.FarLine, Level09MovingTrain.FarLine, "the sentence for a pick-up from too far is the campaign's");
            StringAssert.Contains("let go", level.Hints[2], "the third hint ends with when to let go");
            StringAssert.DoesNotContain("plank", level.Hints[0].ToLowerInvariant(), "the first hint does not give the toy's job away");
            // The third hint is the whole solution, in the order it is done, and does not send anybody jumping
            // off the platform (a jump goes over the train).
            string third = level.Hints[2].ToLowerInvariant();
            foreach (string step in new[] { "pick the plank up", "platform edge", "foot of the lighthouse door", "wagons", "let go", "step down", "running jump", "walk in" })
                StringAssert.Contains(step, third);
            StringAssert.DoesNotContain("hop on", third);
            Assert.Less(third.IndexOf("platform edge"), third.IndexOf("let go"));
            Assert.Less(third.IndexOf("let go"), third.IndexOf("step down"));
            // Short enough for the hint card (it shrinks its type to fit): the third hints of Levels 1 to 7 run
            // from 140 to 220 characters.
            foreach (string hint in level.Hints) Assert.Less(hint.Length, 240, hint);
        }

        [Test]
        public void SolveCompletesTheLevel()
        {
            Load();
            float dropped = 0f, ratio = 0f;
            Vector3 centre = default;
            int dropTick = 0, captureTick = 0, captureBed = -1;
            Game.Events.PropDropped += e =>
            {
                dropped = e.NewScale;
                ratio = e.OldScale / e.GrabDistance;
                centre = e.Prop.Center;
                dropTick = Game.LevelTicks;
            };
            level.Carrier.PropCaptured += (prop, bed) =>
            {
                captureTick = Game.LevelTicks;
                captureBed = bed;
            };
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            List<string> said = Listen();
            TestHelpers.PlayLevel(Game, 90f);
            Debug.Log("[Level09] solve: k " + ratio.ToString("0.000") + ", plank " + dropped.ToString("0.000") + " at " + centre.ToString("0.00") +
                      ", let go at tick " + dropTick + ", gripped by wagon " + (captureBed + 1) + " after " + (captureTick - dropTick) + " ticks, done in " +
                      (Game.LevelTicks * Sim.Dt).ToString("0.0") + " s");
            Assert.AreEqual(0.445f, ratio, 0.02f, "picked up from the start, a step behind the plank (LEVELS: k = 0.445)");
            Assert.AreEqual(4.03f, dropped, 4.03f * 0.05f, "the solver's drop (LEVELS Appendix B: 4.03 +- 5 %)");
            Assert.AreEqual(Level09MovingTrain.OutlineY, level.Plank.Center.y, 0.08f, "lying on the doorstep its end is on the painted outline");
            Assert.Less(centre.y, Player.BaseEyeHeight - 1f, "held more than a unit below the eye: seen from above, not end-on");
            Assert.Less(captureTick - dropTick, 30, "gripped within half a second of being let go");
            Assert.IsTrue(level.Carrier.IsCarrying(level.Plank));
            Assert.IsTrue(level.Bridged, "the plank rides a wagon with its inner end over the doorstep");
            Assert.AreEqual(0, respawns, "nobody fell");
            Assert.AreEqual(0, level.Crumbs.Returns + level.Stranded.Returns);
            Assert.IsEmpty(said, "the intended solution needs no telling off");
            Assert.Less(Level09MovingTrain.RadiusOf(Game.Player.Position), 3.6f, "the player is on the doorstep");
            Assert.Less(Game.LevelTicks * Sim.Dt, 45f);
        }

        [Test]
        public void TheLighthouseCannotBeReachedWithoutTheBridge()
        {
            Load();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            float nearest = float.MaxValue;
            // How near the lighthouse the player comes before they are below anything they could land on.
            void Watch()
            {
                if (Game.Player.Position.y > -1.6f) nearest = Mathf.Min(nearest, Level09MovingTrain.RadiusOf(Game.Player.Position));
            }

            // Straight at the exit, sprinting, with a jump from the platform's edge.
            bool jumped = false;
            IEnumerator Straight()
            {
                yield return bot.WalkTo(new Vector3(0f, 0f, -2f), 0.3f, 6f);
                IEnumerator run = bot.WalkTo(Level09MovingTrain.ExitCentre, 0.3f, 8f, true);
                while (run.MoveNext())
                {
                    Watch();
                    if (!jumped && Game.Player.Grounded && Game.Player.Position.z > Level09MovingTrain.StationEdge - 0.1f)
                    {
                        jumped = true;
                        yield return bot.Jump();
                        continue;
                    }
                    yield return run.Current;
                }
            }
            Assert.IsFalse(Drive(Straight(), 12f), "the run never arrives");
            Assert.IsTrue(jumped);
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Greater(nearest, 9f, "a sprint jump from the platform (16.7 from the lighthouse) comes nowhere near the doorstep at 3.5");
            Debug.Log("[Level09] sprint jump from the platform: nearest " + nearest.ToString("0.00") + ", respawns " + respawns);

            // From the train: get on a wagon, then sprint at the lighthouse and jump from the deck's inner edge.
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Game.Player.Grounded && Game.Player.Position.y > -0.1f && Game.Player.Position.z < 3f, 20f), "back on the platform");
            nearest = float.MaxValue;
            int before = respawns;
            bool leapt = false;
            IEnumerator FromTheTrain()
            {
                yield return OntoTheTrain();
                // The wagon carries the player a little way round, then: run and jump.
                yield return bot.Wait(1f);
                IEnumerator run = bot.WalkTo(Level09MovingTrain.Axis, 0.5f, 6f, true);
                while (run.MoveNext())
                {
                    Watch();
                    if (!leapt && Game.Player.Grounded && Level09MovingTrain.RadiusOf(Game.Player.Position) < Level09MovingTrain.DeckInner + 0.35f)
                    {
                        leapt = true;
                        yield return bot.Jump();
                        continue;
                    }
                    yield return run.Current;
                }
            }
            Assert.IsFalse(Drive(FromTheTrain(), 40f), "the run never arrives");
            Assert.IsTrue(leapt, "the bot got onto the train and jumped from its inner edge (at " + Game.Player.Position + ")");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawns > before, 4f), "the jump ended in the drop");
            Assert.IsFalse(Game.LevelCompleted);
            Debug.Log("[Level09] sprint jump from a wagon: nearest " + nearest.ToString("0.00"));
            Assert.Less(nearest, 9f, "the jump did leave the deck");
            Assert.Greater(nearest, 4.5f, "9 units from the deck to the doorstep; a sprint jump carries 5.5");
        }

        [Test]
        public void ThePlayer_RidesTheBareTrain_AndHopsBackUp()
        {
            Load();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            BotRunner.Run(Game, OntoTheTrain(), 40f);
            TestHelpers.RunSeconds(Game, 0.5f);
            Assert.IsNotNull(Game.Player.GroundCollider);
            Rigidbody wagon = Game.Player.GroundCollider.attachedRigidbody;
            Assert.IsNotNull(wagon, "standing on a wagon");

            // A whole lap: carried round, never nearer the lighthouse than the deck, and not through the exit.
            float nearest = float.MaxValue;
            bool always = true;
            for (int i = 0; i < TestHelpers.Ticks(13.5f); i++)
            {
                Game.Tick();
                nearest = Mathf.Min(nearest, Level09MovingTrain.RadiusOf(Game.Player.Position));
                always &= Game.Player.Grounded;
            }
            Assert.IsTrue(always, "grounded on the wagon all the way round");
            Assert.Greater(nearest, Level09MovingTrain.DeckInner);
            Assert.IsFalse(Game.LevelCompleted);
            Assert.AreEqual(0, respawns);

            // Back up onto the platform as the wagon passes it: across the deck and a jump from its outer edge.
            IEnumerator HopUp()
            {
                Player player = Game.Player;
                yield return bot.Until(() => Mathf.Abs(Mathf.DeltaAngle(Level09MovingTrain.BearingOf(player.Position), -10f)) < 1.5f, 20f);
                // Straight outward, whatever way the wagon has turned by then.
                Vector3 Outward() => Level09MovingTrain.Axis + (player.Position - Level09MovingTrain.Axis - Vector3.up * player.Position.y).normalized * 18.5f;
                yield return Level09MovingTrain.Chase(bot, Outward, () => player.Grounded && player.Position.y > -0.01f && Level09MovingTrain.RadiusOf(player.Position) > 17.2f, 3f, false,
                    () => player.Grounded && player.Position.y < -0.5f && Level09MovingTrain.RadiusOf(player.Position) > Level09MovingTrain.DeckOuter - 0.45f);
            }
            BotRunner.Run(Game, HopUp(), 40f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => Game.Player.Grounded, 1f));
            Assert.AreEqual(0f, Game.Player.Position.y, 0.05f, "back on the platform (at " + Game.Player.Position + ")");
            Assert.AreEqual(0, respawns);

            // And from there the level is solved the ordinary way.
            IEnumerator Again()
            {
                yield return bot.WalkTo(Level09MovingTrain.AroundSpool, 0.3f, 12f);
                yield return bot.WalkTo(Level09MovingTrain.Spawn, 0.15f, 12f);
                yield return level.Solve(bot);
            }
            BotRunner.Run(Game, Again(), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
        }

        // The ends of the window (LEVELS Appendix B: works from 3.7 to the clamp at 4.4), and the middle.
        // The lighthouse wall sets the size: standing back from the edge grows the plank, and so does a
        // pick-up from closer.
        [TestCase(1.4f, 2.9f, 3.9f, 4.2f, TestName = "TheIntendedSize_FromTheEdge_Bridges")]
        [TestCase(1.4f, 0.9f, 4.39f, 4.4f, TestName = "TheHighEndOfTheWindow_TheClamp_Bridges")]
        [TestCase(1.52f, 2.9f, 3.75f, 3.98f, TestName = "TheLowEndOfTheWindow_Bridges")]
        public void TheWindow(float pickupDistance, float standZ, float least, float most)
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            int completed = 0;
            Game.Events.LevelCompleted += e => completed++;
            IEnumerator Script()
            {
                yield return Bridge(Behind(pickupDistance), new Vector3(0f, 0f, standZ), Level09MovingTrain.AimPoint);
                yield return RideIn();
            }
            BotRunner.Run(Game, Script(), 120f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            level.Reach(out float inner, out float outer);
            Debug.Log("[Level09] picked up from " + pickupDistance + ", let go from z = " + standZ + ": plank " + dropped.ToString("0.000") +
                      ", from r = " + inner.ToString("0.00") + " to " + outer.ToString("0.00"));
            Assert.GreaterOrEqual(dropped, least);
            Assert.LessOrEqual(dropped, most);
            Assert.IsTrue(level.Bridged);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(1, completed);
            Assert.IsEmpty(said);
        }

        [Test]
        public void AClearlyWrongSize_DoesNotReach_IsSaid_AndThePlankComesBack()
        {
            Load();
            List<string> said = Listen();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            int respawned = 0;
            Game.Events.PropRespawned += e => respawned++;

            // Picked up from two steps farther back it looks little more than half the size, and against
            // the lighthouse it is that much shorter: it ends well short of the track.
            BotRunner.Run(Game, Bridge(Behind(2.6f), Level09MovingTrain.EdgeSpot, Level09MovingTrain.AimPoint), 60f);
            Debug.Log("[Level09] picked up from 2.6: plank " + dropped.ToString("0.000"));
            Assert.Greater(dropped, 2f);
            Assert.Less(dropped, 3.2f, "clearly too small (the window starts at 3.7)");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawned == 1, 4f), "the plank fell and came back (it is at " + level.Plank.Center + ")");
            Assert.IsFalse(level.Carrier.Captured, "no wagon ever had it");
            Assert.IsFalse(Game.LevelCompleted);
            CollectionAssert.AreEqual(new[] { Level09MovingTrain.ShortLine }, said, "told what was wrong, once");
            TestHelpers.RunSeconds(Game, 0.5f);
            Assert.IsTrue(OnTheSpool(), "back on its spool at the size it started with");

            // The gate itself: a plank against the lighthouse reaches the wagons' decks from 3.5 up (10.5 long).
            Assert.Less(2f + 3f * dropped, Level09MovingTrain.DeckInner, "its outer end was inside the track");
            Assert.Greater(2f + 3f * 3.8f, Level09MovingTrain.DeckInner + 0.25f, "at 3.8 it lies a quarter of a unit onto the deck");

            // And taken from the start, the ordinary way works.
            IEnumerator Again()
            {
                yield return bot.WalkTo(Level09MovingTrain.AroundSpool, 0.3f, 12f);
                yield return bot.WalkTo(Level09MovingTrain.Spawn, 0.15f, 12f);
                yield return level.Solve(bot);
            }
            BotRunner.Run(Game, Again(), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(1, said.Count);
        }

        [Test]
        public void LetGoWithNoWagonUnderIt_ThePlankFalls_IsSaid_AndComesBack()
        {
            Load();
            List<string> said = Listen();
            int respawned = 0;
            Game.Events.PropRespawned += e => respawned++;
            IEnumerator TooEarly()
            {
                yield return Hold(null, Level09MovingTrain.EdgeSpot, Level09MovingTrain.AimPoint);
                // The track in front of the platform is empty, and stays so for a second.
                yield return bot.Until(() => level.WagonAt(0f, 0f, 25f) == 0 && level.WagonAt(0f, 1f, 25f) == 0 &&
                                             Mathf.Abs(Mathf.DeltaAngle(level.Train.EngineBearing, 0f)) > 30f &&
                                             Mathf.Abs(Mathf.DeltaAngle(level.Train.EngineBearing + 24f, 0f)) > 30f, 20f);
                yield return bot.DropAt(Level09MovingTrain.AimPoint);
            }
            BotRunner.Run(Game, TooEarly(), 60f);
            Assert.AreEqual(4.03f, level.Plank.Scale, 0.2f, "the right size");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawned == 1, 4f), "the plank fell and came back (it is at " + level.Plank.Center + ")");
            Assert.IsFalse(level.Carrier.Captured);
            CollectionAssert.AreEqual(new[] { Level09MovingTrain.MissedLine }, said);
            TestHelpers.RunSeconds(Game, 0.5f);
            Assert.IsTrue(OnTheSpool());
            Assert.IsFalse(Game.LevelCompleted);

            IEnumerator Again()
            {
                yield return bot.WalkTo(Level09MovingTrain.AroundSpool, 0.3f, 12f);
                yield return bot.WalkTo(Level09MovingTrain.Spawn, 0.15f, 12f);
                yield return level.Solve(bot);
            }
            BotRunner.Run(Game, Again(), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(1, said.Count);
        }

        // On the train the plank is a half-unit step that rides along. From the wagon behind it a hop from a
        // standstill comes down where it went up (the train is faster than anybody walks, and in the air
        // nobody gains on it); the level says so, and a running jump gets up.
        [Test]
        public void AStandingHopAtThePlankFromBehind_GetsNobodyUp_IsSaid_AndARunDoes()
        {
            Load();
            List<string> said = Listen();
            Player player = Game.Player;
            Vector3 Target() => level.OnPlank(Level09MovingTrain.RadiusOf(player.Position) - 0.3f);
            IEnumerator Hop()
            {
                yield return Bridge(null, Level09MovingTrain.EdgeSpot, Level09MovingTrain.AimPoint);
                yield return bot.Until(() => level.Carrier.IsCarrying(level.Plank), 3f);
                yield return level.OntoTheTrain(bot);
                // Up to the back of the plank, and stand against it.
                yield return Level09MovingTrain.Chase(bot, Target, () => player.Grounded && level.GapToPlank(player, out _, out _) < 0.36f, 4f);
                yield return bot.Wait(0.3f);
                // A hop with the forward key down, and on pressing forward until down again.
                IEnumerator press = bot.WalkTo(Target(), 0.05f, 1f);
                yield return press.MoveNext() ? press.Current : null;
                yield return bot.Jump();
                for (int i = 0; i < 50; i++)
                {
                    press = bot.WalkTo(Target(), 0.05f, 1f);
                    yield return press.MoveNext() ? press.Current : null;
                }
            }
            BotRunner.Run(Game, Hop(), 60f);
            Assert.IsTrue(player.Grounded, "down again");
            Assert.AreNotSame(level.Plank, player.GroundProp, "not on the plank");
            Assert.AreEqual(Level09MovingTrain.DeckY, player.Position.y, 0.03f, "on the wagon's deck still, beside it (at " + player.Position + ")");
            Assert.Less(level.GapToPlank(player, out _, out _), 0.6f, "and not left behind by the train either");
            CollectionAssert.AreEqual(new[] { Level09MovingTrain.HopLine }, said);

            IEnumerator Run()
            {
                yield return level.OntoThePlank(bot);
                yield return level.WalkIn(bot);
            }
            BotRunner.Run(Game, Run(), 60f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(1, said.Count, "the running jump is not told off");
        }

        [Test]
        public void HeldSideways_ItReachesNothing_IsSaid_AndThePlankComesBack()
        {
            Load();
            List<string> said = Listen();
            int respawned = 0;
            Game.Events.PropRespawned += e => respawned++;
            IEnumerator Sideways()
            {
                yield return Hold(null, Level09MovingTrain.EdgeSpot, Level09MovingTrain.AimPoint);
                // A quarter turn: across the view instead of along it.
                yield return bot.RotateHeld(6);
                yield return bot.Wait(0.1f);
                yield return bot.DropAt(Level09MovingTrain.AimPoint);
            }
            BotRunner.Run(Game, Sideways(), 60f);
            level.Reach(out float inner, out float outer);
            Debug.Log("[Level09] sideways: plank " + level.Plank.Scale.ToString("0.000") + ", from r = " + inner.ToString("0.00") + " to " + outer.ToString("0.00"));
            Assert.AreEqual(4.4f, level.Plank.Scale, 0.01f, "across the view it is at its full size long before it is as deep as the gap");
            Assert.Greater(inner, Level09MovingTrain.RingRadius + 1f, "nowhere near the doorstep");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawned == 1, 4f), "the plank fell and came back (it is at " + level.Plank.Center + ")");
            CollectionAssert.AreEqual(new[] { Level09MovingTrain.ReachLine }, said);
            TestHelpers.RunSeconds(Game, 0.5f);
            Assert.IsTrue(OnTheSpool());
            Assert.IsFalse(Game.LevelCompleted);
        }

        [Test]
        public void APickUpFromTooFarOrTooNear_IsSaidAtOnce()
        {
            Load();
            List<string> said = Listen();
            // From across the platform the plank looks tiny, and it only ever gets as big as it looks.
            IEnumerator Far()
            {
                yield return bot.WalkTo(Behind(4.2f), 0.1f, 12f);
                yield return bot.Grab(level.Plank);
                yield return bot.Wait(0.1f);
            }
            BotRunner.Run(Game, Far(), 30f);
            Assert.Less(Game.Grabber.Ratio, Level09MovingTrain.FarRatio);
            CollectionAssert.AreEqual(new[] { Level09MovingTrain.FarLine }, said);

            // Held against the lighthouse from the edge it is nowhere near long enough; let go, it falls and comes back.
            int respawned = 0;
            Game.Events.PropRespawned += e => respawned++;
            IEnumerator Try()
            {
                yield return bot.WalkTo(Level09MovingTrain.AroundSpool, 0.3f, 12f);
                yield return bot.WalkTo(Level09MovingTrain.EdgeSpot, 0.15f, 12f);
                yield return bot.DropAt(Level09MovingTrain.AimPoint);
            }
            BotRunner.Run(Game, Try(), 30f);
            Assert.Less(level.Plank.Scale, 2.5f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawned == 1, 4f));
            TestHelpers.RunSeconds(Game, 0.5f);
            Assert.IsTrue(OnTheSpool());
            CollectionAssert.AreEqual(new[] { Level09MovingTrain.FarLine, Level09MovingTrain.ShortLine }, said);

            // From right beside the spool it looks very big: it is at its full size before it gets far enough.
            said.Clear();
            Vector3 beside = Level09MovingTrain.SpoolBase + Quaternion.Euler(0f, Level09MovingTrain.PlankYaw, 0f) * Vector3.left * 0.74f;
            IEnumerator Near()
            {
                yield return bot.WalkTo(Behind(2.5f) + Vector3.left * 1.2f, 0.2f, 12f);
                yield return bot.WalkTo(beside, 0.06f, 12f);
                yield return bot.Grab(level.Plank);
                yield return bot.Wait(0.1f);
            }
            BotRunner.Run(Game, Near(), 30f);
            Assert.Greater(Game.Grabber.Ratio, Level09MovingTrain.NearRatio, "from " + Game.Player.Position);
            CollectionAssert.AreEqual(new[] { Level09MovingTrain.NearLine }, said);

            // The pick-up from the start is not told off.
            said.Clear();
            IEnumerator PutBack()
            {
                yield return bot.DropAt(new Vector3(-2f, 0.2f, -4f));
                yield return bot.Wait(1f);
            }
            BotRunner.Run(Game, PutBack(), 30f);
            Assert.IsEmpty(said);
        }

        [Test]
        public void APlankTooSmallToUse_GoesBackToItsSpool()
        {
            Load();
            List<string> said = Listen();
            // Picked up from far off and put down at the player's feet it is a crumb.
            IEnumerator Shrink()
            {
                yield return bot.WalkTo(Behind(4.2f), 0.1f, 12f);
                yield return bot.Grab(level.Plank);
                yield return bot.WalkTo(new Vector3(-2f, 0f, -5f), 0.3f, 12f);
                yield return bot.DropAt(new Vector3(-2f, 0f, -3.6f));
            }
            BotRunner.Run(Game, Shrink(), 30f);
            Assert.Less(level.Plank.Scale, Level09MovingTrain.CrumbScale);
            Assert.IsFalse(OnTheSpool());
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Crumbs.Returns == 1, 3f), "the crumb was not returned (at " + level.Plank.Center + ")");
            TestHelpers.RunSeconds(Game, 0.2f);
            Assert.IsTrue(OnTheSpool());
            CollectionAssert.AreEqual(new[] { Level09MovingTrain.FarLine, Level09MovingTrain.CrumbLine }, said);
        }

        [Test]
        public void RestartingMidSolve_AndSolvingAgain_Works()
        {
            Load();
            // Restart with the plank in hand at the platform edge.
            BotRunner.Run(Game, Hold(null, Level09MovingTrain.EdgeSpot, Level09MovingTrain.AimPoint), 30f);
            Assert.AreSame(level.Plank, Game.Grabber.Held);
            Assert.Greater(level.Plank.Scale, 3.5f);
            Game.RestartLevel();
            Game.Tick();
            Assert.IsNull(Game.Grabber.Held);
            Assert.AreSame(level, Game.Level, "the same level instance is built again");
            Assert.AreEqual(0.65f, level.Plank.Scale, 1e-4f);
            Assert.IsTrue(level.Plank.Frozen);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level09MovingTrain.Spawn), 0.1f);
            Assert.AreEqual(Level09MovingTrain.TrainStart, level.Train.EngineBearing, 0.5f, "the train starts over too");

            // Restart once more with the plank riding the train.
            BotRunner.Run(Game, Bridge(null, Level09MovingTrain.EdgeSpot, Level09MovingTrain.AimPoint), 60f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Bridged, 3f));
            TestHelpers.RunSeconds(Game, 2f);
            Game.RestartLevel();
            Game.Tick();
            Assert.IsFalse(level.Carrier.Captured);
            Assert.IsFalse(level.Bridged);
            Assert.IsFalse(level.Plank.Driven);
            Assert.IsFalse(Game.LevelCompleted);
            TestHelpers.RunSeconds(Game, 0.5f);
            Assert.IsTrue(OnTheSpool());

            TestHelpers.PlayLevel(Game, 90f);
            Assert.IsTrue(level.Bridged);
        }

        [Test]
        public void APlankThatLeavesTheWorld_ComesBack()
        {
            Load();
            Prop plank = level.Plank;
            Vector3 home = plank.Position;
            int respawned = 0;
            Game.Events.PropRespawned += e => respawned++;

            // Below the kill plane, at any size.
            plank.Unfreeze();
            plank.SetScale(4f);
            plank.SetPose(new Vector3(8f, level.KillY - 1f, 12f), Quaternion.identity);
            TestHelpers.Run(Game, 2);
            Assert.AreEqual(1, respawned);
            Assert.Less(Vector3.Distance(plank.Position, home), 0.02f, "back on the spool");
            Assert.AreEqual(0.65f, plank.Scale, 1e-4f, "at the size it started with");
            Assert.IsTrue(plank.Frozen);

            // Dropped into the gap between the track and the doorstep: it falls all the way and comes back.
            plank.Unfreeze();
            plank.SetScale(2f);
            plank.SetPose(new Vector3(0f, 1f, 11f), Quaternion.identity);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawned == 2, 3f), "the plank is at " + plank.Center);
            Assert.Less(Vector3.Distance(plank.Position, home), 0.02f);

            // Lying on the doorstep behind the lighthouse, out of sight and out of reach: the leash brings it back.
            plank.Unfreeze();
            plank.SetScale(0.8f);
            plank.SetPose(new Vector3(0f, -0.9f, 22.8f), Quaternion.Euler(0f, 90f, 0f));
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Stranded.Returns == 1, 5f), "the plank is at " + plank.Center);
            Assert.Less(Vector3.Distance(plank.Position, home), 0.02f);
            Assert.AreEqual(0.65f, plank.Scale, 1e-4f);

            // The player who falls is back at the start, and can carry on.
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            Game.Player.Teleport(new Vector3(0f, 0f, 10f), 0f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawns == 1, 4f));
            Assert.Less(Vector3.Distance(Game.Player.Position, Level09MovingTrain.Spawn), 0.2f);
            Assert.IsFalse(Game.LevelCompleted);

            TestHelpers.PlayLevel(Game, 90f);
        }

        [Test]
        public void FallingWithThePlankInHand_ThePlayerKeepsIt_AndCarriesOn()
        {
            Load();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            IEnumerator Fall()
            {
                yield return bot.Grab(level.Plank);
                yield return bot.WalkTo(Level09MovingTrain.AroundSpool, 0.3f, 12f);
                yield return bot.WalkTo(new Vector3(-19.5f, 0f, 0f), 0.3f, 6f, true);
            }
            Assert.IsFalse(Drive(Fall(), 5f), "the run never arrives: off the side of the platform");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawns >= 1 && Game.Player.Grounded, 4f));
            Assert.AreSame(level.Plank, Game.Grabber.Held, "still in hand");
            IEnumerator CarryOn()
            {
                yield return bot.WalkTo(Level09MovingTrain.AroundSpool, 0.3f, 12f);
                yield return bot.WalkTo(Level09MovingTrain.EdgeSpot, 0.15f, 12f);
                yield return bot.LookAt(Level09MovingTrain.AimPoint);
                yield return bot.Until(MiddleWagonComing, 20f);
                yield return bot.DropAt(Level09MovingTrain.AimPoint);
                yield return RideIn();
            }
            BotRunner.Run(Game, CarryOn(), 120f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
        }

        // Robustness: the same run every time, and again after a restart.
        [Test]
        public void TheSolve_IsTheSameFiveTimesOver_AndAfterARestart()
        {
            float firstScale = 0f;
            int firstTicks = 0;
            for (int run = 0; run < 5; run++)
            {
                Game?.Dispose();
                Load();
                float dropped = 0f;
                Game.Events.PropDropped += e => dropped = e.NewScale;
                TestHelpers.PlayLevel(Game, 90f);
                if (run == 0)
                {
                    firstScale = dropped;
                    firstTicks = Game.LevelTicks;
                }
                Assert.AreEqual(firstScale, dropped, 0f, "run " + run + ": the plank is let go at the same size every time");
                Assert.AreEqual(firstTicks, Game.LevelTicks, "run " + run + ": and it takes the same number of ticks");
            }
            Game.RestartLevel();
            Game.Tick();
            Assert.IsFalse(Game.LevelCompleted);
            float again = 0f;
            Game.Events.PropDropped += e => again = e.NewScale;
            TestHelpers.PlayLevel(Game, 90f);
            Assert.AreEqual(firstScale, again, 0.02f);
            // (One tick on either side: the restart was asked for outside a tick.)
            Assert.AreEqual(firstTicks, Game.LevelTicks, 2f, "and after a restart it takes as long");
        }

        // Robustness: where a player would stand and aim, give or take (LEVELS: from the edge back to
        // z = 0.3, any pitch from -6 to +15 degrees).
        [TestCase(0f, 2.9f, 0f, 0.9f)]
        [TestCase(0f, 2f, 0f, 0.9f)]
        [TestCase(0f, 0.5f, 0f, 0.9f)]
        [TestCase(1.5f, 2.9f, 0f, 0.9f)]
        [TestCase(-2f, 2.5f, 0f, 0.9f)]
        [TestCase(0f, 2.9f, 0f, 0.2f)]
        [TestCase(0f, 2.9f, 0f, 3.5f)]
        [TestCase(0f, 2.9f, 0.9f, 1.2f)]
        [TestCase(0f, 2.9f, -0.9f, 0.6f)]
        [TestCase(3f, 1.5f, 0.5f, 2f)]
        public void TheSolve_ToleratesWhereThePlayerStandsAndAims(float standX, float standZ, float aimX, float aimY)
        {
            Load();
            float dropped = 0f;
            Game.Events.PropDropped += e => dropped = e.NewScale;
            var aim = new Vector3(aimX, aimY, Level09MovingTrain.AxisZ - Level09MovingTrain.TowerRadius);
            IEnumerator Script()
            {
                yield return Bridge(null, new Vector3(standX, 0f, standZ), aim);
                yield return RideIn();
            }
            BotRunner.Run(Game, Script(), 120f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            level.Reach(out float inner, out float outer);
            Debug.Log("[Level09] from (" + standX + ", " + standZ + ") at (" + aimX + ", " + aimY + "): plank " + dropped.ToString("0.000") +
                      ", from r = " + inner.ToString("0.00") + " to " + outer.ToString("0.00"));
            Assert.GreaterOrEqual(dropped, 3.7f);
            Assert.IsTrue(level.Bridged);
            Assert.IsTrue(Game.LevelCompleted);
        }

        // ==== Review (tools/out/notes/level09-review.md): what a player would try, and what each try now does ====

        // Holds the intended plank from the edge and lets go when a part of the train will be under its outer
        // end as it comes down: the engine (car 0) or wagon n, already gone by so many degrees (negative: not
        // there yet).
        IEnumerator LetGoOver(int car, float goneBy)
        {
            yield return Hold(null, Level09MovingTrain.EdgeSpot, Level09MovingTrain.AimPoint);
            yield return bot.Until(() =>
            {
                float bearing = car == 0 ? level.Train.EngineBearing : level.Train.CarBearing(car);
                return Mathf.Abs(Mathf.DeltaAngle(bearing + Level09MovingTrain.TrainSpeed * Level09MovingTrain.FallSeconds, level.PlankBearing + goneBy)) < 0.25f;
            }, 20f);
            yield return bot.DropAt(Level09MovingTrain.AimPoint);
        }

        // The engine is what arrives first. A plank let go onto it used to ride its roof for five to nine
        // seconds, looking like a bridge, and then fall. The engine is slick now: it runs out from under the
        // plank and the first wagon takes it.
        [TestCase(-5f, TestName = "LetGoOverTheEngine_ItsFront_TheFirstWagonTakesThePlank")]
        [TestCase(0f, TestName = "LetGoOverTheEngine_ItsMiddle_TheFirstWagonTakesThePlank")]
        [TestCase(5f, TestName = "LetGoOverTheEngine_ItsCab_TheFirstWagonTakesThePlank")]
        public void LetGoOverTheEngine(float goneBy)
        {
            Load();
            List<string> said = Listen();
            int respawned = 0;
            Game.Events.PropRespawned += e => respawned++;
            BotRunner.Run(Game, LetGoOver(0, goneBy), 60f);
            int dropTick = Game.LevelTicks;
            Assert.Greater(level.Plank.Scale, 3.7f, "over the engine's roof it is still long enough (it rests on the roof while held)");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Carrier.IsCarrying(level.Plank), Level09MovingTrain.LimboTicks * Sim.Dt - 0.1f),
                "the plank was not gripped before it would have been sent back (it is at " + level.Plank.Center + ")");
            Debug.Log("[Level09] let go over the engine (gone by " + goneBy + "): gripped by wagon " + (level.Carrier.CarIndex + 1) + " after " + (Game.LevelTicks - dropTick) + " ticks");
            Assert.AreEqual(0, level.Carrier.CarIndex, "the first wagon has it");
            Assert.IsTrue(level.Bridged);
            Assert.AreEqual(0, respawned);
            Assert.IsEmpty(said);
            BotRunner.Run(Game, RideIn(), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.IsEmpty(said);
        }

        // Every try is decided quickly. Let go just ahead of the engine the plank is shoved along in front
        // of it (it used to be, for up to nine seconds): it is back on its spool within three, with the line.
        [Test]
        public void LetGoAheadOfTheEngine_ThePlankIsBackWithinThreeSeconds_AndTheLineSaysWhy()
        {
            Load();
            List<string> said = Listen();
            int respawned = 0;
            Game.Events.PropRespawned += e => respawned++;
            BotRunner.Run(Game, LetGoOver(0, -13f), 60f);
            Assert.AreEqual(4.03f, level.Plank.Scale, 0.2f, "the right size");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawned == 1, 3f), "the plank is still out there (at " + level.Plank.Center + ")");
            Assert.IsFalse(level.Carrier.Captured);
            CollectionAssert.AreEqual(new[] { Level09MovingTrain.MissedLine }, said);
            TestHelpers.RunSeconds(Game, 0.5f);
            Assert.IsTrue(OnTheSpool());
        }

        // Standing too far back the plank is at its full size before it touches the lighthouse and hangs short
        // of it: 13.2 long, it fits exactly between the doorstep and the platform and rests on neither. That
        // used to be told "turn it end-on" (it was end-on).
        [TestCase(0f)]
        [TestCase(-3f)]
        [TestCase(-6f)]
        public void LetGoFromTooFarBack_ThePlankHangsShortOfTheLighthouse_IsSaid_AndComesBack(float standZ)
        {
            Load();
            List<string> said = Listen();
            int respawned = 0;
            Game.Events.PropRespawned += e => respawned++;
            BotRunner.Run(Game, Bridge(null, new Vector3(0f, 0f, standZ), Level09MovingTrain.AimPoint), 60f);
            level.Reach(out float inner, out _);
            Assert.AreEqual(4.4f, level.Plank.Scale, 1e-3f, "at its full size");
            Assert.Greater(inner, Level09MovingTrain.RingRadius, "and short of the doorstep");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawned == 1, 5f), "the plank did not come back (it is at " + level.Plank.Center + ")");
            CollectionAssert.AreEqual(new[] { Level09MovingTrain.FullSizeLine }, said);
            TestHelpers.RunSeconds(Game, 0.5f);
            Assert.IsTrue(OnTheSpool());
            Assert.IsFalse(Game.LevelCompleted);
        }

        // The band the builder measured: from the edge back to z = 0.5 the plank bridges (at 0.4 it is gripped
        // askew, its inner end a step from the doorstep; farther back it is the test above).
        [Test]
        public void TheStandingBand_FromTheEdgeBackToHalfAUnit_Bridges()
        {
            foreach (float standZ in new[] { 3.2f, 2f, 1f, 0.5f })
            {
                Game?.Dispose();
                Load();
                List<string> said = Listen();
                BotRunner.Run(Game, Bridge(null, new Vector3(0f, 0f, standZ), Level09MovingTrain.AimPoint), 60f);
                Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Bridged, 2f), "from z = " + standZ + " the plank (" + level.Plank.Scale + ") did not bridge");
                Assert.IsEmpty(said, "from z = " + standZ);
            }
        }

        // Let go below the wagons' decks (against the lighthouse under the doorstep): it used to be told that
        // nothing was under its near end.
        [Test]
        public void LetGoBelowTheTrain_IsSaid()
        {
            Load();
            List<string> said = Listen();
            int respawned = 0;
            Game.Events.PropRespawned += e => respawned++;
            var low = new Vector3(0f, -5f, 18f);
            IEnumerator Low()
            {
                yield return Hold(null, Level09MovingTrain.EdgeSpot, low);
                // With nothing of the train in the way: a wagon there would stop the plank on its deck.
                yield return bot.Until(() => level.WagonAt(0f, 0f, 30f) == 0 && level.WagonAt(0f, 0.5f, 30f) == 0 &&
                                             Mathf.Abs(Mathf.DeltaAngle(level.Train.EngineBearing, 0f)) > 40f &&
                                             Mathf.Abs(Mathf.DeltaAngle(level.Train.EngineBearing + 12f, 0f)) > 40f, 20f);
                yield return bot.DropAt(low);
            }
            BotRunner.Run(Game, Low(), 60f);
            Assert.Less(level.Plank.Center.y, Level09MovingTrain.DeckY - 0.5f, "let go below the decks");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawned == 1, 4f));
            CollectionAssert.AreEqual(new[] { Level09MovingTrain.UnderLine }, said);
        }

        // ---- From the train -------------------------------------------------------------------------------

        // With the plank in hand down onto a wagon in the middle of the train, and there: let go looking at
        // the lighthouse's axis at a height (the same point whichever way the wagon has turned).
        IEnumerator FromAWagon(float aimY)
        {
            yield return bot.Grab(level.Plank);
            yield return OntoTheTrain();
            yield return bot.Wait(0.4f);
            yield return bot.DropAt(new Vector3(0f, aimY, Level09MovingTrain.AxisZ));
        }

        // On the carried plank: at a run to its inner end and a jump at the lighthouse.
        IEnumerator Dive()
        {
            Player player = Game.Player;
            int fell = 0;
            Game.Events.PlayerRespawned += e => fell++;
            if (player.GroundProp != level.Plank) yield return level.OntoThePlank(bot);
            bool jumped = false;
            yield return Level09MovingTrain.Chase(bot, () =>
            {
                level.Ends(out Vector3 inner, out Vector3 outer);
                return jumped ? Level09MovingTrain.Axis : inner + (inner - outer).normalized * 3f;
            }, () => Game.LevelCompleted || fell > 0, 10f, true, () =>
            {
                if (jumped || !player.Grounded || player.GroundProp != level.Plank) return false;
                level.Ends(out Vector3 inner, out _);
                if (Level09MovingTrain.RadiusOf(player.Position) > Level09MovingTrain.RadiusOf(inner) + 0.4f) return false;
                jumped = true;
                return true;
            });
        }

        // The builder's "second route": carry the plank onto the train and hold it against the lighthouse
        // from a wagon. It does not work with the pick-up from the start: the wagon is two units nearer the
        // lighthouse than the platform's edge and the plank comes out that much shorter. It was told "step
        // back: it grows" - on a three-unit deck.
        [Test]
        public void HeldAgainstTheLighthouseFromAWagon_ThePlankComesOutTooShort_AndTheLineSaysSo()
        {
            Load();
            List<string> said = Listen();
            int respawned = 0;
            Game.Events.PropRespawned += e => respawned++;
            BotRunner.Run(Game, FromAWagon(Level09MovingTrain.AimPoint.y), 60f);
            Debug.Log("[Level09] against the lighthouse from a wagon: plank " + level.Plank.Scale.ToString("0.000"));
            Assert.Greater(level.Plank.Scale, 3f);
            Assert.Less(level.Plank.Scale, 3.6f, "too short (it has to be 3.7 to lie on a deck)");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawned == 1, 4f), "the plank did not fall and come back (it is at " + level.Plank.Center + ")");
            Assert.IsFalse(level.Carrier.Captured);
            CollectionAssert.AreEqual(new[] { Level09MovingTrain.TrainShortLine }, said);
            Assert.IsFalse(Game.LevelCompleted);
        }

        // The second route that does work, and that LEVELS accepts (the diving board): put down on the wagon
        // pointing at the lighthouse the plank is gripped where it lies, a pier that rides the train, and a
        // sprint jump off its end lands on the doorstep. It needs a scaled plank on a moving wagon - the
        // owner's brief - but neither the lighthouse wall nor the timing.
        [Test]
        public void PutDownOnAWagonPointingAtTheLighthouse_ThePlankIsADivingBoard_AndASprintJumpLandsOnTheDoorstep()
        {
            Load();
            List<string> said = Listen();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            Player player = Game.Player;
            BotRunner.Run(Game, FromAWagon(-4f), 60f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Carrier.IsCarrying(level.Plank), 1f), "the plank was not gripped (it is at " + level.Plank.Center + ")");
            level.Reach(out float inner, out float outer);
            Debug.Log("[Level09] put down on a wagon: plank " + level.Plank.Scale.ToString("0.000") + ", from r = " + inner.ToString("0.00") + " to " + outer.ToString("0.00"));
            Assert.Greater(level.Plank.Scale, 1.8f, "more than three times the size it started with");
            Assert.Less(level.Plank.Scale, 2.6f);
            Assert.IsFalse(level.Bridged, "it does not reach the doorstep");
            Assert.Less(inner, Level09MovingTrain.DivingReach, "but ends near enough to jump from");
            Assert.IsEmpty(said, "and so nothing is said against it");
            BotRunner.Run(Game, Dive(), 30f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted, "the jump did not reach (the player is at " + player.Position + ")");
            Assert.AreEqual(0, respawns);
            Assert.Greater(player.Position.y, Level09MovingTrain.RingTop - 0.3f, "through the exit at the doorstep's height, not falling past it");
            Assert.Less(Level09MovingTrain.RadiusOf(player.Position), Level09MovingTrain.RingRadius + 0.35f);
            Assert.IsEmpty(said);
        }

        // A pier that ends too far out: told at once that it has to reach, and the jump off its end slides
        // down the doorstep's rim. (The player is put on the pier by hand: it is a hand's width, and getting
        // onto it is not what is tested. The run and the jump are the bot's.)
        [Test]
        public void ADivingBoardThatEndsFarFromTheDoorstep_IsToldAtOnce_AndTheJumpFalls()
        {
            Load();
            List<string> said = Listen();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            BotRunner.Run(Game, FromAWagon(-6f), 60f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => level.Carrier.IsCarrying(level.Plank), 1f));
            TestHelpers.Run(Game, 2);
            level.Reach(out float inner, out _);
            Debug.Log("[Level09] a pier that ends far out: plank " + level.Plank.Scale.ToString("0.000") + ", inner end at r = " + inner.ToString("0.00"));
            Assert.Greater(inner, Level09MovingTrain.DivingReach + 1f);
            CollectionAssert.AreEqual(new[] { Level09MovingTrain.ReachLine }, said);
            Game.Player.Teleport(level.OnPlank(13.2f) + Vector3.up * 0.02f);
            TestHelpers.Run(Game, 3);
            Assert.AreSame(level.Plank, Game.Player.GroundProp);
            Drive(Dive(), 20f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawns >= 1, 4f), "the jump ended in the drop");
            Assert.IsFalse(Game.LevelCompleted, "nobody is through the exit from below the doorstep: its rim is in the way");
            Assert.IsTrue(level.Carrier.IsCarrying(level.Plank), "the pier rides on: it can be picked up from the platform as it passes");
        }

        // ---- Getting onto the train ---------------------------------------------------------------------------

        // From the platform toward (0, 0, 12) at a walk or a run, with a jump at a line (99: none), timed so
        // that a middle wagon is there to land on. Ends on a wagon's deck or after a fall.
        IEnumerator AtTheTrain(bool sprint, float jumpAt, bool withTheTrain = true)
        {
            Player player = Game.Player;
            int fell = 0;
            Game.Events.PlayerRespawned += e => fell++;
            float lead = jumpAt > 50f ? Level09MovingTrain.StepSeconds : 0.8f + (Level09MovingTrain.StationEdge - jumpAt) / (sprint ? Player.SprintSpeed : Player.WalkSpeed);
            yield return bot.WalkTo(Level09MovingTrain.AroundSpool, 0.3f, 12f);
            yield return bot.WalkTo(new Vector3(0f, 0f, Mathf.Min(jumpAt, Level09MovingTrain.StationEdge) - 0.05f), 0.1f, 12f);
            if (withTheTrain) yield return bot.Until(() => level.WagonAt(0f, lead, 2f) == 4, 20f);
            else
                yield return bot.Until(() => level.WagonAt(0f, 0f, 40f) == 0 && level.WagonAt(0f, 1f, 40f) == 0 &&
                                             Mathf.Abs(Mathf.DeltaAngle(level.Train.EngineBearing, 0f)) > 50f &&
                                             Mathf.Abs(Mathf.DeltaAngle(level.Train.EngineBearing + 24f, 0f)) > 50f, 20f);
            bool jumped = false;
            yield return Level09MovingTrain.Chase(bot, () => new Vector3(0f, 0f, 12f), () => (player.Grounded && player.Position.y < -0.9f) || fell > 0, 5f, sprint, () =>
            {
                if (jumped || !player.Grounded || player.Position.z < jumpAt) return false;
                jumped = true;
                return true;
            });
        }

        // What a first-time player does at a platform edge with a train going by: run and jump. Measured: a
        // jump at a walk from the last 0.3 of the edge, and any jump at a run from within 2.5 of it, goes
        // clean over the three-unit deck. It is said now, once; a step off the edge lands and is not told off.
        [TestCase(false, 3.25f, TestName = "AJumpAtTheTrain_AtAWalkFromTheEdge_GoesOverIt_AndIsSaid")]
        [TestCase(true, 3.25f, TestName = "AJumpAtTheTrain_AtARunFromTheEdge_GoesOverIt_AndIsSaid")]
        [TestCase(true, 1.5f, TestName = "AJumpAtTheTrain_AtARunFromTwoUnitsBack_GoesOverIt_AndIsSaid")]
        public void AJumpAtTheTrain(bool sprint, float jumpAt)
        {
            Load();
            List<string> said = Listen();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            BotRunner.Run(Game, AtTheTrain(sprint, jumpAt), 60f);
            Assert.AreEqual(1, respawns, "over the train and down");
            CollectionAssert.AreEqual(new[] { Level09MovingTrain.LeapLine }, said);
            Assert.Less(Vector3.Distance(Game.Player.Position, Level09MovingTrain.Spawn), 0.5f, "back at the start");
        }

        [Test]
        public void TheLeapLine_IsOnlyForAJumpOverATrainThatWasThere()
        {
            // A step off the edge lands on the wagon.
            Load();
            List<string> said = Listen();
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            BotRunner.Run(Game, AtTheTrain(false, 99f), 60f);
            Assert.AreEqual(0, respawns);
            TestHelpers.RunSeconds(Game, 1f);
            Assert.IsEmpty(said);

            // A jump from farther back lands on it too.
            Game.Dispose();
            Load();
            said = Listen();
            Game.Events.PlayerRespawned += e => respawns++;
            BotRunner.Run(Game, AtTheTrain(false, 1.5f), 60f);
            TestHelpers.RunSeconds(Game, 1f);
            Assert.AreEqual(0, respawns);
            Assert.AreEqual(Level09MovingTrain.DeckY, Game.Player.Position.y, 0.05f);
            Assert.IsEmpty(said);

            // And a jump into the gap with no train anywhere near is not a jump over the train.
            Game.Dispose();
            Load();
            said = Listen();
            Game.Events.PlayerRespawned += e => respawns++;
            BotRunner.Run(Game, AtTheTrain(true, 3.25f, false), 60f);
            Assert.AreEqual(1, respawns);
            TestHelpers.RunSeconds(Game, 1f);
            Assert.IsEmpty(said);
        }

        // The hop line was said on the hop, also when it worked: with sprint held a hop from a standstill does
        // get up (in the air a sprinter may add up to 8, and the wagon goes 6). It is said now when the hop
        // has come down beside the plank again.
        [Test]
        public void AStandingHopAtThePlankWithSprintHeld_GetsUp_AndIsNotToldOff()
        {
            Load();
            List<string> said = Listen();
            Player player = Game.Player;
            Vector3 Target() => level.OnPlank(Level09MovingTrain.RadiusOf(player.Position) - 0.3f);
            IEnumerator Hop()
            {
                yield return Bridge(null, Level09MovingTrain.EdgeSpot, Level09MovingTrain.AimPoint);
                yield return bot.Until(() => level.Carrier.IsCarrying(level.Plank), 3f);
                yield return level.OntoTheTrain(bot);
                yield return Level09MovingTrain.Chase(bot, Target, () => player.Grounded && level.GapToPlank(player, out _, out _) < 0.36f, 4f);
                yield return bot.Wait(0.3f);
                IEnumerator press = bot.WalkTo(Target(), 0.05f, 1f, true);
                yield return press.MoveNext() ? press.Current : null;
                yield return bot.Jump();
                for (int i = 0; i < 60 && player.GroundProp != level.Plank; i++)
                {
                    press = bot.WalkTo(Target(), 0.05f, 1f, true);
                    yield return press.MoveNext() ? press.Current : null;
                }
            }
            BotRunner.Run(Game, Hop(), 60f);
            TestHelpers.RunSeconds(Game, 0.5f);
            Assert.AreSame(level.Plank, player.GroundProp, "on the plank (the player is at " + player.Position + ")");
            Assert.IsEmpty(said);
            BotRunner.Run(Game, level.WalkIn(bot), 30f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
        }

        // ---- Without the train -------------------------------------------------------------------------------

        // A diving board off the platform: the plank at its full size laid over the edge pointing at the
        // lighthouse (put there by hand, as well as it can be), a run along it and a jump off its tip. The
        // plank tips as the runner passes the edge; the jump ends against the doorstep's rim.
        [TestCase(0.4f)]
        [TestCase(1f)]
        public void ADivingBoardOffThePlatform_DoesNotReachTheDoorstep(float inside)
        {
            Load();
            Player player = Game.Player;
            Prop plank = level.Plank;
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            // When the engine has just gone by (it would knock the board about).
            TestHelpers.RunUntil(Game, () => Mathf.Abs(Mathf.DeltaAngle(level.Train.EngineBearing, 25f)) < 1f, 20f);
            plank.Unfreeze();
            plank.SetScale(Level09MovingTrain.PlankMax);
            plank.SetPose(new Vector3(0f, 0.3f, Level09MovingTrain.StationEdge - inside), Quaternion.identity);
            TestHelpers.RunSeconds(Game, 1f);
            float nearest = float.MaxValue;
            bool jumped = false;
            IEnumerator Run()
            {
                yield return bot.WalkTo(new Vector3(3.2f, 0f, -3.6f), 0.2f, 10f);
                yield return bot.WalkTo(new Vector3(3.2f, 0f, 0.8f), 0.2f, 10f);
                yield return Level09MovingTrain.Chase(bot, () => new Vector3(0f, 0.6f, 0.8f), () => player.GroundProp == plank && Mathf.Abs(player.Position.x) < 0.5f, 4f, false,
                    () => player.Grounded && player.GroundProp != plank && player.Position.x < 2.35f);
                yield return Level09MovingTrain.Chase(bot, () => new Vector3(0f, 0f, 40f), () =>
                {
                    if (player.Position.y > -1.2f) nearest = Mathf.Min(nearest, Level09MovingTrain.RadiusOf(player.Position));
                    return Game.LevelCompleted || respawns > 0;
                }, 8f, true, () =>
                {
                    if (jumped || !player.Grounded) return false;
                    level.Ends(out Vector3 inner, out _);
                    if (player.Position.z < inner.z - 0.45f) return false;
                    jumped = true;
                    return true;
                });
            }
            BotRunner.Run(Game, Run(), 60f);
            Debug.Log("[Level09] diving board off the platform, " + inside + " inside the edge: nearest at the doorstep's height " + nearest.ToString("0.00"));
            Assert.IsTrue(jumped);
            Assert.AreEqual(1, respawns);
            Assert.IsFalse(Game.LevelCompleted);
            Assert.Greater(nearest, Level09MovingTrain.RingRadius + 0.8f, "at the doorstep's height the jumper is a unit short of its rim");
        }

        // A plank let go where its middle is beyond the platform's edge and that no wagon grips is back on its
        // spool within three seconds: there is no leaving it out over the track to try things with.
        [Test]
        public void APlankLeftOutOverTheEdge_IsBackOnItsSpool()
        {
            Load();
            List<string> said = Listen();
            int respawned = 0;
            Game.Events.PropRespawned += e => respawned++;
            // From the back of the platform it is at its full size with its middle just beyond the edge.
            IEnumerator FromTheBack()
            {
                yield return bot.Grab(level.Plank);
                yield return bot.WalkTo(Level09MovingTrain.AroundSpool, 0.3f, 12f);
                yield return bot.WalkTo(new Vector3(0f, 0f, -6f), 0.15f, 12f);
                yield return bot.DropAt(Level09MovingTrain.AimPoint);
            }
            BotRunner.Run(Game, FromTheBack(), 60f);
            Assert.AreEqual(Level09MovingTrain.PlankMax, level.Plank.Scale, 1e-3f);
            Assert.Greater(level.Plank.Center.z, Level09MovingTrain.StationEdge + 0.2f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => respawned == 1, (Level09MovingTrain.LimboTicks + 30) * Sim.Dt), "still out there (at " + level.Plank.Center + ")");
            CollectionAssert.AreEqual(new[] { Level09MovingTrain.FullSizeLine }, said);
        }

        // ---- Awkward moments ---------------------------------------------------------------------------------

        [Test]
        public void RestartWhileThePlankFalls_AndWhileWalkingIt_AndSolvingAgain_Works()
        {
            Load();
            // The plank let go with nothing under it, on its way down.
            IEnumerator TooEarly()
            {
                yield return Hold(null, Level09MovingTrain.EdgeSpot, Level09MovingTrain.AimPoint);
                yield return bot.Until(() => level.WagonAt(0f, 0f, 30f) == 0 && level.WagonAt(0f, 1f, 30f) == 0 &&
                                             Mathf.Abs(Mathf.DeltaAngle(level.Train.EngineBearing, 0f)) > 40f &&
                                             Mathf.Abs(Mathf.DeltaAngle(level.Train.EngineBearing + 24f, 0f)) > 40f, 20f);
                yield return bot.DropAt(Level09MovingTrain.AimPoint);
                yield return bot.Wait(0.5f);
            }
            BotRunner.Run(Game, TooEarly(), 60f);
            Assert.IsFalse(level.Plank.Frozen);
            List<string> said = Listen();
            Game.RestartLevel();
            Game.Tick();
            TestHelpers.RunSeconds(Game, 3f);
            Assert.IsTrue(OnTheSpool());
            Assert.IsEmpty(said, "the try that the restart ended is not judged afterwards");

            // On the plank, halfway to the lighthouse.
            Player player = Game.Player;
            IEnumerator Halfway()
            {
                yield return Bridge(null, Level09MovingTrain.EdgeSpot, Level09MovingTrain.AimPoint);
                yield return bot.Until(() => level.Carrier.IsCarrying(level.Plank), 3f);
                yield return level.Board(bot);
                yield return Level09MovingTrain.Chase(bot, () => level.OnPlank(Level09MovingTrain.RadiusOf(player.Position) - 1.5f),
                    () => Level09MovingTrain.RadiusOf(player.Position) < 8f, 8f);
            }
            BotRunner.Run(Game, Halfway(), 60f);
            Assert.AreSame(level.Plank, player.GroundProp);
            Game.RestartLevel();
            Game.Tick();
            Assert.Less(Vector3.Distance(player.Position, Level09MovingTrain.Spawn), 0.1f);
            Assert.IsFalse(level.Carrier.Captured);
            TestHelpers.RunSeconds(Game, 0.5f);
            Assert.IsTrue(OnTheSpool());
            Assert.IsTrue(player.Grounded);
            Assert.IsFalse(Game.LevelCompleted);

            TestHelpers.PlayLevel(Game, 90f);
        }

        [Test]
        public void SteppingOffThePlankMidWalk_ThePlayerIsBackAtTheStart_TheBridgeRidesOn_AndCanBeBoardedAgain()
        {
            Load();
            Player player = Game.Player;
            int respawns = 0;
            Game.Events.PlayerRespawned += e => respawns++;
            IEnumerator Halfway()
            {
                yield return Bridge(null, Level09MovingTrain.EdgeSpot, Level09MovingTrain.AimPoint);
                yield return bot.Until(() => level.Carrier.IsCarrying(level.Plank), 3f);
                yield return level.Board(bot);
                yield return Level09MovingTrain.Chase(bot, () => level.OnPlank(Level09MovingTrain.RadiusOf(player.Position) - 1.5f),
                    () => Level09MovingTrain.RadiusOf(player.Position) < 8f, 8f);
                // And off its side.
                yield return Level09MovingTrain.Chase(bot, () => player.Position + Level09Sideways(player), () => respawns > 0, 4f);
            }
            BotRunner.Run(Game, Halfway(), 60f);
            Assert.AreEqual(1, respawns);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => player.Grounded, 1f));
            Assert.Less(Vector3.Distance(player.Position, Level09MovingTrain.Spawn), 0.3f);
            Assert.IsTrue(level.Bridged, "the bridge rides on");
            Assert.IsFalse(Game.LevelCompleted);
            IEnumerator Again()
            {
                yield return bot.WalkTo(Level09MovingTrain.AroundSpool, 0.3f, 12f);
                yield return RideIn();
            }
            BotRunner.Run(Game, Again(), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
            Assert.AreEqual(1, respawns);
        }

        // Across the plank: the way the train travels where the player is.
        static Vector3 Level09Sideways(Player player)
        {
            float a = Level09MovingTrain.BearingOf(player.Position) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 3f;
        }

        // The riding bridge can be taken back: from the platform as it passes, and it is the plank's size then
        // that counts (picked up from as far as it is long it looks the size it did at the start).
        [Test]
        public void TheRidingBridge_CanBePickedUpFromThePlatform_AndPutBack()
        {
            Load();
            List<string> said = Listen();
            IEnumerator Retake()
            {
                yield return Bridge(null, Level09MovingTrain.EdgeSpot, Level09MovingTrain.AimPoint);
                yield return bot.Until(() => level.Carrier.IsCarrying(level.Plank), 3f);
                // A lap later, as it comes round.
                yield return bot.Until(() => Mathf.Abs(Mathf.DeltaAngle(level.PlankBearing, -20f)) < 2f, 20f);
                yield return bot.Grab(level.Plank, 3f);
            }
            BotRunner.Run(Game, Retake(), 90f);
            Assert.AreSame(level.Plank, Game.Grabber.Held);
            Assert.IsFalse(level.Carrier.Captured, "a pick-up takes it off the wagon");
            Debug.Log("[Level09] the riding bridge picked up from the platform: k " + Game.Grabber.Ratio.ToString("0.000"));
            Assert.Greater(Game.Grabber.Ratio, Level09MovingTrain.FarRatio);
            Assert.Less(Game.Grabber.Ratio, Level09MovingTrain.NearRatio);
            Assert.IsEmpty(said);
            IEnumerator PutBack()
            {
                // A step back from the edge: taken from ten units off it looks a little smaller than at the start.
                yield return bot.WalkTo(new Vector3(0f, 0f, 2f), 0.15f, 5f);
                yield return bot.LookAt(Level09MovingTrain.AimPoint);
                yield return bot.Until(MiddleWagonComing, 20f);
                yield return bot.DropAt(Level09MovingTrain.AimPoint);
                yield return RideIn();
            }
            BotRunner.Run(Game, PutBack(), 90f);
            TestHelpers.RunUntil(Game, () => Game.LevelCompleted, 2f);
            Assert.IsTrue(Game.LevelCompleted);
        }


        // ==== Pictures of other moments than the solver's (explicit: -Filter "Level09Tests.Tour") ================

        // The level with a script of the test's choosing in place of its Solve.
        sealed class TourLevel : LevelDefinition
        {
            public readonly Level09MovingTrain Inner = new Level09MovingTrain();
            readonly System.Func<Bot, Level09MovingTrain, IEnumerator> script;

            public TourLevel(System.Func<Bot, Level09MovingTrain, IEnumerator> script)
            {
                this.script = script;
            }

            public override string Slug => Inner.Slug;
            public override string Title => Inner.Title;
            public override string Blurb => Inner.Blurb;
            public override string[] Hints => Inner.Hints;
            public override string Environment => Inner.Environment;
            public override int EnvironmentVisit => 1;
            public override float GroundY => Inner.GroundY;
            public override float KillY => Inner.KillY;
            public override void Build(LevelContext ctx) => Inner.Build(ctx);
            public override IEnumerator Solve(Bot bot) => script(bot, Inner);
        }

        // Looking about: back over the platform, down at the track from the edge, a ride on a wagon, and the
        // plank let go with no wagon under it.
        static IEnumerator LookAbout(Bot bot, Level09MovingTrain level)
        {
            Player player = bot.Player;
            yield return bot.Wait(0.5f);
            yield return bot.LookAt(new Vector3(-6f, 1f, -9f));
            yield return bot.Wait(1f);        // 2: back over the platform
            yield return bot.LookAt(level.Plank.Center);
            yield return bot.Wait(1f);        // 3.5: down at the plank and the shoe prints
            yield return bot.WalkTo(Level09MovingTrain.AroundSpool, 0.3f, 12f);
            yield return bot.WalkTo(new Vector3(0f, 0f, 3.1f), 0.1f, 12f);
            yield return bot.LookAt(new Vector3(-6f, -1.5f, 7f));
            yield return bot.Until(() => bot.Game.Time >= 6.4f, 10f);   // 6.4: the engine comes up to the platform
            yield return bot.LookAt(new Vector3(0f, -1f, 5.5f));
            yield return bot.Until(() => bot.Game.Time >= 7.6f, 10f);   // 7.6: down at the wagons passing
            yield return bot.Until(() => level.WagonAt(0f, Level09MovingTrain.StepSeconds, 2f) == 5, 20f);
            var onto = new Vector3(0f, -1f, 5.4f);
            yield return Level09MovingTrain.Chase(bot, () => onto, () => player.Grounded && player.Position.y < -0.9f, 2f);
            yield return bot.LookAt(Level09MovingTrain.Axis + Vector3.up * 0.5f);
            yield return bot.Until(() => bot.Game.Time >= 11f, 10f);    // 11: from the wagon, the lighthouse across the gap
            yield return bot.LookAt(new Vector3(0f, 0.5f, 0f));
            yield return bot.Until(() => bot.Game.Time >= 14f, 10f);    // 14: from the wagon, back at the station
            yield return bot.Wait(30f);
        }

        static IEnumerator Missed(Bot bot, Level09MovingTrain level)
        {
            yield return bot.Grab(level.Plank);
            yield return bot.WalkTo(Level09MovingTrain.AroundSpool, 0.3f, 12f);
            yield return bot.WalkTo(Level09MovingTrain.EdgeSpot, 0.15f, 12f);
            yield return bot.DropAt(Level09MovingTrain.AimPoint);       // about 3.2: nothing under it
            yield return bot.Wait(30f);
        }

        [Test, Explicit]
        public void Tour_Pictures()
        {
            Toybox.EditorTools.Shots.Run(new Toybox.EditorTools.ShotRequest
            {
                Level = 9, Definition = new TourLevel(LookAbout), Times = new[] { 2f, 3.5f, 6.4f, 7.6f, 11f, 14f }, Overview = false,
                OutputDirectory = "tools/out/shots/level09/tour",
            });
            Toybox.EditorTools.Shots.Run(new Toybox.EditorTools.ShotRequest
            {
                Level = 9, Definition = new TourLevel(Missed), Times = new[] { 3.6f, 4.2f, 6f }, Overview = true,
                OutputDirectory = "tools/out/shots/level09/missed",
            });
        }
    }
}
