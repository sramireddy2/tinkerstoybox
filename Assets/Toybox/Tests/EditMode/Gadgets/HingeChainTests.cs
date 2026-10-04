using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    /// <summary>The domino run of Level 15: seven pieces falling by a law, each bigger and slower than the last.</summary>
    public class HingeChainTests : SimTest
    {
        HingeChain chain;
        Prop set;
        readonly List<string> log = new List<string>();
        readonly List<float> times = new List<float>();

        void BuildLane(LevelContext ctx, float scale, Vector3? targetFromHinge)
        {
            log.Clear();
            times.Clear();
            TestHelpers.Floor(ctx);
            // The set stands on the floor: its origin is the middle of its 3.2 x 1.5 x 0.8 bounds.
            set = ToyCatalog.Add(ctx, ToyId.DominoSet, new Vector3(0f, 0.75f * scale, 6f), scale, null, o => o.Tags = new[] { "dominoes" });
            HingeChainOptions options = HingeChain.ForDominoSet(set);
            if (targetFromHinge.HasValue)
            {
                Vector3 hinge = set.Transform.TransformPoint(ToyFactory.DominoSetHinge(6));
                options.Target = hinge + targetFromHinge.Value;
                options.TargetRadius = 0.6f;
            }
            chain = new HingeChain(ctx, options);
            chain.PieceFell += i =>
            {
                log.Add("fell " + i);
                times.Add(ctx.Game.LevelTicks * Sim.Dt);
            };
            chain.TargetStruck += () => log.Add("struck");
            chain.FellShort += () => log.Add("short");
            ctx.SetSpawn(new Vector3(0f, 0f, -6f), 0f);
        }

        [Test]
        public void ThePiecesFallInOrder_EachBeatSlower_AndTheLastSwatsTheBaseball()
        {
            // LEVELS: the baseball is 3.34 from the last hinge, at (2.47, 2.25); the domino is 4.37 tall at scale 3.02.
            Build(ctx => BuildLane(ctx, 3.02f, new Vector3(2.47f, 2.25f, 0f)));
            Transform pieces = set.Transform.Find(ToyFactory.DominoSetPieces);
            Assert.IsTrue(pieces.gameObject.activeSelf);
            Assert.IsTrue(chain.Reaches());
            Assert.AreEqual(7, chain.PieceCount);
            RunSeconds(0.5f);
            Assert.AreEqual(ChainState.Idle, chain.State, "spring-latched: nothing but Start lets them go");

            Assert.IsTrue(chain.Start());
            Assert.IsFalse(chain.Start());
            Assert.IsFalse(pieces.gameObject.activeSelf, "the set's own pieces are hidden; kinematic stand-ins take their place");
            Mover first = chain.PieceMover(0);
            Assert.IsTrue(first.GameObject.activeSelf);
            Assert.AreEqual(3.02f, first.Transform.localScale.x, 1e-4f);
            Assert.Less(Vector3.Distance(chain.PieceCenter(0), first.Position), 0.03f, "standing where the set's own piece stood");

            Assert.IsTrue(TestHelpers.RunUntil(Game, () => chain.State == ChainState.Done, 12f));
            CollectionAssert.AreEqual(new[] { "fell 0", "fell 1", "fell 2", "fell 3", "fell 4", "fell 5", "fell 6", "struck" }, log);
            Assert.IsTrue(chain.Struck);
            for (int i = 2; i < 6; i++)
                Assert.Greater(times[i] - times[i - 1], times[i - 1] - times[i - 2], "beat " + i + " is slower than the one before: a ritardando");
            // Each piece is 1.3 times the one before; the time to fall goes with the square root of the height.
            Assert.AreEqual(Mathf.Sqrt(ToyFactory.DominoSetHeight(5) / ToyFactory.DominoSetHeight(4)), (times[5] - times[4]) / (times[4] - times[3]), 0.08f);
            Assert.AreEqual(72f, chain.Angle(0), 1e-3f, "the fallen ones lean on their neighbours");
            Assert.AreEqual(37f, chain.Angle(6), 6f, "LEVELS: domino 7 falls 37 degrees and meets the baseball");
            float total = times[6] - (times[0] - (times[1] - times[0]));
            Assert.Less(total, 6f, "the whole run takes a few seconds (" + total + ")");

            // Reset stands them up again and gives the set its own pieces back.
            chain.Reset();
            Assert.AreEqual(ChainState.Resetting, chain.State);
            RunSeconds(1.1f);
            Assert.AreEqual(ChainState.Idle, chain.State);
            Assert.IsTrue(pieces.gameObject.activeSelf);
            Assert.IsFalse(first.GameObject.activeSelf);
            Assert.AreEqual(0, chain.Fallen);
            Assert.IsTrue(chain.Start(), "and it can run again");
        }

        [Test]
        public void ASetThatIsTooSmall_FallsShortOfTheBaseball()
        {
            // The same baseball, 3.34 from where the hinge of a 3.02 set would be - but the set is 1.5: the last domino is 2.2 tall.
            Build(ctx => BuildLane(ctx, 1.5f, new Vector3(2.9f, 2.6f, 0f)));
            Assert.IsFalse(chain.Reaches());
            chain.Start();
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => chain.State == ChainState.Done, 12f));
            Assert.AreEqual("short", log[log.Count - 1]);
            Assert.AreEqual(90f, chain.Angle(6), 1e-3f, "it slams down flat in front of the shelf");
            Assert.IsFalse(chain.Struck);
            Assert.AreEqual(7, chain.Fallen);
        }

        [Test]
        public void SmallerDominoes_FallFaster()
        {
            Build(ctx => BuildLane(ctx, 1f, null));
            chain.Start();
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => chain.State == ChainState.Done, 12f));
            float small = times[6] - times[0];
            CollectionAssert.AreEqual(new[] { "fell 0", "fell 1", "fell 2", "fell 3", "fell 4", "fell 5", "fell 6" }, log, "without a target the last one just falls flat");
            Game.Dispose();
            Game = null;

            Build(ctx => BuildLane(ctx, 3f, null));
            chain.Start();
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => chain.State == ChainState.Done, 12f));
            float big = times[6] - times[0];
            Assert.AreEqual(Mathf.Sqrt(3f), big / small, 0.05f, "three times the size, root three times the time");
        }

        [Test]
        public void AHeldSet_CannotBeStarted()
        {
            Build(ctx => BuildLane(ctx, 1f, null));
            Game.Player.Teleport(new Vector3(0f, 0f, 2f), 0f);
            LookAt(set.Center);
            Click();
            Assert.AreSame(set, Game.Grabber.Held);
            Assert.IsFalse(chain.Start());
            Assert.AreEqual(ChainState.Idle, chain.State);
        }
    }
}
