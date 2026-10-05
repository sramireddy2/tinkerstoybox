using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    /// <summary>Level 5: a feather laid in the gale carries the player across the canyon if it is wide enough.</summary>
    public class SailRaftTests : SimTest
    {
        WindStream wind;
        SailRaft raft;
        Prop feather;
        readonly List<string> log = new List<string>();

        void BuildCanyon(LevelContext ctx, float scale, float settle = 0f)
        {
            log.Clear();
            TestHelpers.Box(ctx, new Vector3(-1f, -1f, 6f), new Vector3(16f, 2f, 24f));     // deck A: x -9..7, z -6..18
            TestHelpers.Box(ctx, new Vector3(-1f, -1f, 50f), new Vector3(16f, 2f, 16f));    // deck C: z 42..58
            // MaxPropMass 0.08: a feather big enough to moor (0.084 at scale 2.5) is not also pushed along.
            wind = new WindStream(ctx, new WindStreamOptions { Center = new Vector3(-1f, 4.5f, 19f), Size = new Vector3(10f, 9f, 50f), Direction = Vector3.forward, MaxPropMass = 0.08f });
            feather = ToyCatalog.Add(ctx, ToyId.Feather, new Vector3(-1f, 0.2f + 0.015f * scale, 12f), scale, null, o => o.Tags = new[] { "sail" });
            raft = new SailRaft(ctx, new SailRaftOptions
            {
                Prop = feather, Stream = wind, Launch = Zone.MinMax(new Vector3(-6f, 0f, -5f), new Vector3(4f, 1.5f, 17.5f)), Landing = new Vector3(-1f, 0f, 49f),
                Settle = settle,
            });
            raft.SailMoored += () => log.Add("moored");
            raft.SailStalled += share => log.Add("stalled " + share.ToString("0.00"));
            raft.SailLaunched += () => log.Add("launched");
            raft.SailDocked += () => log.Add("docked");
            ctx.SetSpawn(new Vector3(5f, 0f, 15f), -90f);
        }

        [Test]
        public void TheRule_IsLiftMinusWeight()
        {
            Build(ctx => BuildCanyon(ctx, 9.2f), -13f);
            // capacity(s) = 7.78 * 0.30 * s^2 / 22 - 0.0054 s^3
            Assert.AreEqual(0.106f * 9.2f * 9.2f - 0.0054f * 9.2f * 9.2f * 9.2f, raft.Capacity(9.2f), 0.05f);
            Assert.IsFalse(raft.Carries(6.3f), "LEVELS: it carries the player from s = 6.5");
            Assert.IsTrue(raft.Carries(6.6f));
            Assert.IsTrue(raft.Carries(12f), "up to the clamp");
        }

        [Test]
        public void ABigFeather_Moors_AndCarriesItsRiderAcrossTheCanyon()
        {
            Build(ctx => BuildCanyon(ctx, 9.2f), -13f);
            Assert.AreEqual(4.2f, feather.Mass, 0.2f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => raft.State == SailState.Moored, 4f), "it settles and is pinned");
            Assert.IsTrue(feather.Driven);
            Assert.IsTrue(feather.Grabbable, "moored, it can still be taken back");
            Vector3 moored = feather.Center;
            RunSeconds(1f);
            Assert.Less(Vector3.Distance(moored, feather.Center), 1e-4f, "the wind does not move a moored sail");

            // Step onto the quill.
            Game.Player.Teleport(new Vector3(-1f, feather.Center.y + 0.15f, 12f), 0f);
            int boarded = Game.LevelTicks;
            float highest = 0f, fastestChange = 0f;
            bool always = true;
            Vector3 previous = Vector3.zero;
            int launchTick = -1;
            for (int i = 0; i < 16 * 60 && raft.State != SailState.Docked; i++)
            {
                Game.Tick();
                if (raft.State != SailState.Glide) continue;
                if (launchTick < 0) launchTick = Game.LevelTicks;
                highest = Mathf.Max(highest, feather.Center.y);
                always &= Game.Player.Grounded && Game.Player.GroundProp == feather;
                Vector3 velocity = feather.Velocity;
                if (Game.LevelTicks > launchTick) fastestChange = Mathf.Max(fastestChange, (velocity - previous).magnitude);
                previous = velocity;
            }
            CollectionAssert.AreEqual(new[] { "moored", "launched", "docked" }, log);
            Assert.AreEqual(0.6f, (launchTick - boarded) * Sim.Dt, 0.1f, "launched after the board delay");
            Assert.IsTrue(always, "the rider stood on the feather for the whole glide");
            Assert.AreEqual(moored.y + 1.2f, highest, 0.05f, "cruising 1.2 above where it lay");
            Assert.Less(fastestChange, 1f, "no change of speed a rider would feel (the grip is 6): " + fastestChange);
            Assert.AreEqual(49f, feather.Center.z, 0.05f, "it came down on the landing");
            Assert.AreEqual(-1f, feather.Center.x, 0.05f);
            Assert.IsFalse(feather.Driven, "and is an ordinary prop again");
            RunSeconds(0.5f);
            Assert.Greater(Game.Player.Position.z, 47f, "the rider is across");
            Assert.AreEqual(feather.Center.y + 0.14f, Game.Player.Position.y, 0.1f);
            Assert.IsTrue(Game.Player.Grounded);
        }

        [Test]
        public void ASmallerFeather_Stalls_UnderItsRider()
        {
            Build(ctx => BuildCanyon(ctx, 5f), -13f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => raft.State == SailState.Moored, 4f));
            Game.Player.Teleport(new Vector3(-1f, feather.Center.y + 0.1f, 12f), 0f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => raft.State == SailState.Stall, 2f), "aboard for the board delay, it tries");
            float lifted = 0f;
            float start = feather.Center.y;
            while (raft.State == SailState.Stall)
            {
                Game.Tick();
                lifted = Mathf.Max(lifted, feather.Center.y - start);
            }
            Assert.AreEqual(0.3f, lifted, 0.01f, "it lifts 0.3 and flops back");
            Assert.AreEqual(SailState.Moored, raft.State);
            // capacity(5) = 0.106 * 25 - 0.0054 * 125 = 1.98; over the rider's 3: 0.66
            CollectionAssert.AreEqual(new[] { "moored", "stalled 0.66" }, log);
            RunSeconds(2f);
            Assert.AreEqual(2, log.Count, "it does not try again until the rider has stepped off and on");
            Assert.Less(feather.Center.z, 13f, "and it went nowhere");
        }

        [Test]
        public void ALittleFeather_IsBlownOffTheDeck()
        {
            // Scale 1.5: the wind gives it 12 / 1.5 = 8 along the deck and takes nine tenths of its weight off it.
            Build(ctx => BuildCanyon(ctx, 1.5f), -13f);
            float farthest = 0f;
            for (int i = 0; i < 180; i++)
            {
                Game.Tick();
                farthest = Mathf.Max(farthest, feather.Center.z);
            }
            Assert.AreEqual(SailState.Loose, raft.State, "below MoorAbove it is never pinned");
            Assert.AreEqual(0, log.Count);
            Assert.Greater(farthest, 18f, "the wind slid it across the deck and over the edge (it is at " + feather.Center + ", mass " + feather.Mass + ", in the stream " + wind.Contains(feather.Center) + ")");
        }

        // Found building Level 5: a feather comes to rest balanced on its quill, its outline 0.013 s up in the
        // air. Loose, it tips under a foot and is walked onto (ToyCatalogTests); moored it is pinned, and at
        // the sizes that fly its outline is a lip of 0.11 to 0.2 that stops a player who walks at it.
        // Settle presses the moored sail down until its outline lies on the deck.
        [TestCase(6.6f, 0f, false, TestName = "AMooredFeather_PinnedAsItLies_StopsAPlayerWhoWalksAtIt(6.6)")]
        [TestCase(9.2f, 0f, false, TestName = "AMooredFeather_PinnedAsItLies_StopsAPlayerWhoWalksAtIt(9.2)")]
        [TestCase(6.6f, 0.013f, true, TestName = "AMooredFeather_PressedFlat_IsWalkedOnto_AndCarriesItsRider(6.6)")]
        [TestCase(9.2f, 0.013f, true, TestName = "AMooredFeather_PressedFlat_IsWalkedOnto_AndCarriesItsRider(9.2)")]
        [TestCase(12f, 0.013f, true, TestName = "AMooredFeather_PressedFlat_IsWalkedOnto_AndCarriesItsRider(12)")]
        public void AMooredFeather_AndAPlayerWhoWalksAtIt(float scale, float settle, bool boards)
        {
            Build(ctx => BuildCanyon(ctx, scale, settle), -13f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => raft.State == SailState.Moored, 4f));
            RunSeconds(0.5f);
            GadgetKit.VerticalExtent(feather, out float underside, out float top);
            // The outline is 0.002 s above and below the feather's middle, the quill 0.015 s.
            float lip = feather.Center.y + 0.002f * scale;
            if (settle > 0f)
            {
                Assert.AreEqual(-0.013f * scale, underside, 0.01f, "its quill is pressed into the deck");
                Assert.AreEqual(0.004f * scale, lip, 0.01f, "and its outline lies on it");
                Assert.Less(Vector3.Angle(feather.Rotation * Vector3.up, Vector3.up), 0.1f, "level");
                Assert.IsTrue(feather.Grabbable, "it can still be taken back");
            }
            else
            {
                Assert.AreEqual(0f, underside, 0.01f, "it stands on its quill");
                Assert.Greater(lip, 0.1f, "with its outline in the air");
            }

            // From beside it, level with its middle and looking at -X: straight at its edge and on toward its quill.
            Game.Player.Teleport(new Vector3(5f, 0f, 12f), -90f);
            Input.Hold.MoveZ = 1f;
            bool arrived = TestHelpers.RunUntil(Game, () => Game.Player.Position.x <= -0.9f, 4f);
            Input.Hold.MoveZ = 0f;
            Assert.AreEqual(boards, arrived, "the player stopped at " + Game.Player.Position + " (lip " + lip.ToString("0.000") + ", top " + top.ToString("0.000") + ")");
            if (!boards)
            {
                Assert.AreEqual(SailState.Moored, raft.State);
                Assert.IsFalse(raft.RiderAboard);
                return;
            }
            Assert.IsTrue(raft.RiderAboard, "standing on the feather");
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => raft.State == SailState.Docked, 16f), "it flew: " + string.Join(", ", log));
            CollectionAssert.AreEqual(new[] { "moored", "launched", "docked" }, log);
            RunSeconds(0.5f);
            GadgetKit.VerticalExtent(feather, out underside, out _);
            Assert.AreEqual(0f, underside, 0.03f, "it came down on the far deck, not into it");
            Assert.Greater(Game.Player.Position.z, 47f, "the rider is across");
            Assert.IsTrue(Game.Player.Grounded);
            Assert.AreSame(feather, Game.Player.GroundProp);
        }

        // Found reviewing Level 5: a jump pressed just before landing goes off on the tick the feet touch down.
        // Hop after hop the sail never saw anybody standing on it, counted a second without a rider and let
        // itself go under them, in the middle of the canyon. A rider in the air over the sail has not left it.
        [Test]
        public void ARiderWhoHops_IsNotLetGoOf()
        {
            Build(ctx => BuildCanyon(ctx, 9.2f, 0.013f), -13f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => raft.State == SailState.Moored, 4f));
            // Behind its middle: the wind pushes whoever is in the air, and three hops in a row carry the
            // rider four units forward along the feather (a fourth would carry them off its tip).
            Game.Player.Teleport(new Vector3(-1f, feather.Center.y + 0.15f, 9.5f), 0f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => raft.State == SailState.Glide, 2f));
            RunSeconds(1.5f);
            int hops = 0, standing = 0, inTheAir = 0;
            string elsewhere = null;
            float ahead = Game.Player.Position.z - feather.Center.z;
            for (int i = 0; i < 200 && raft.State == SailState.Glide; i++)
            {
                if (Game.Player.Grounded && hops < 3)
                {
                    // Off again at once, as a jump pressed just before landing is.
                    Input.Once.Jump = true;
                    hops++;
                }
                else if (Game.Player.Grounded && hops == 3) break;
                Game.Tick();
                if (raft.RiderAboard) standing++;
                else if (raft.RiderOverhead) inTheAir++;
                else if (elsewhere == null)
                    elsewhere = "tick " + i + ": player " + Game.Player.Position.ToString("0.000") + (Game.Player.Grounded ? " grounded on " + Game.Player.GroundCollider : " in the air") + ", feather " + feather.Center.ToString("0.000");
            }
            Assert.AreEqual(3, hops);
            Assert.Greater(inTheAir * Sim.Dt, 1.8f, "three hops: twice as long in the air as the sail waits for a rider");
            Assert.Less(standing, 6, "with hardly a tick of standing between them");
            Assert.IsNull(elsewhere, "all the while in the air over the sail");
            Assert.AreEqual(SailState.Glide, raft.State, "it did not let go of a rider who was only hopping");
            Assert.IsTrue(raft.RiderAboard, "who has come down on it again");
            Assert.Greater(Game.Player.Position.z - feather.Center.z, ahead + 2f, "farther forward: the wind pushed them while they were in the air");
            Assert.IsTrue(feather.Driven);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => raft.State == SailState.Docked, 16f), "it flew on: " + string.Join(", ", log));
            RunSeconds(0.5f);
            Assert.Greater(Game.Player.Position.z, 44f, "the rider is across");
            Assert.IsTrue(Game.Player.Grounded);
            Assert.AreSame(feather, Game.Player.GroundProp);
        }

        // The other side of that rule: a rider who is beside the sail, or under it, has left it.
        [Test]
        public void ARiderWhoGoesOverTheSide_IsLeftBehind_ASecondLater()
        {
            Build(ctx => BuildCanyon(ctx, 9.2f, 0.013f), -13f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => raft.State == SailState.Moored, 4f));
            Game.Player.Teleport(new Vector3(-1f, feather.Center.y + 0.15f, 12f), 0f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => raft.State == SailState.Glide && feather.Center.z > 24f, 8f));
            Assert.IsTrue(raft.RiderAboard);
            Assert.IsFalse(raft.RiderOverhead, "standing is not hopping");
            // Beside it, in the air: half its width and a foot more.
            Game.Player.Teleport(feather.Center + new Vector3(0.2f * 9.2f + 0.8f, 0.4f, 0f), 0f);
            Assert.IsFalse(raft.RiderOverhead);
            int ticks = 0;
            while (raft.State == SailState.Glide && ticks < 200)
            {
                Game.Tick();
                ticks++;
            }
            Assert.AreEqual(SailState.Loose, raft.State, "nobody aboard, nobody over it: it is let go");
            Assert.AreEqual(1f, ticks * Sim.Dt, 0.05f, "after the second it waits for a rider");
            Assert.IsFalse(feather.Driven);
            Assert.IsTrue(feather.Grabbable);
            CollectionAssert.AreEqual(new[] { "moored", "launched" }, log);
        }

        [Test]
        public void AGrab_TakesAMooredSailBack()
        {
            Build(ctx => BuildCanyon(ctx, 7f), -13f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => raft.State == SailState.Moored, 4f));
            LookAt(feather.Center);
            Click();
            Assert.AreSame(feather, Game.Grabber.Held);
            Run(2);
            Assert.AreEqual(SailState.Loose, raft.State);
            Assert.IsFalse(feather.Driven);
        }
    }
}
