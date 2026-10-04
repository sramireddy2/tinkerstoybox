using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    /// <summary>Matryoshka gift boxes: picking one up for the first time leaves the next one standing there.</summary>
    public class NestedSetTests : SimTest
    {
        NestedSet nest;
        Prop red, yellow, teal;

        void BuildBoxes(LevelContext ctx)
        {
            TestHelpers.Room(ctx, 10f, 8f);
            red = ToyCatalog.Add(ctx, ToyId.GiftBox, new Vector3(0f, 0.6f, 0f), 1.2f, Art.Palette.Cherry, o => o.Name = "Red");
            yellow = ToyCatalog.Add(ctx, ToyId.GiftBox, new Vector3(0f, 0.45f, 0f), 0.9f, Art.Palette.Lemon, o => o.Name = "Yellow");
            teal = ToyCatalog.Add(ctx, ToyId.GiftBox, new Vector3(0f, 0.3f, 0f), 0.6f, Art.Palette.Lagoon, o => o.Name = "Teal");
            nest = new NestedSet(ctx, new NestedSetOptions { Props = new[] { red, yellow, teal } });
            ctx.SetSpawn(new Vector3(0f, 0f, -4f), 0f);
        }

        [Test]
        public void OnlyTheOuterBoxIsThere_UntilItIsPickedUp()
        {
            Build(BuildBoxes);
            Assert.IsTrue(red.GameObject.activeSelf);
            Assert.IsFalse(yellow.GameObject.activeSelf, "hidden: no renderer, no collider");
            Assert.IsFalse(teal.GameObject.activeSelf);
            Assert.IsFalse(yellow.Grabbable);
            Assert.AreEqual(1, nest.RevealedCount);
            RunSeconds(1f);
            Assert.AreEqual(0.6f, red.Center.y, 0.01f, "three boxes in one place, and nothing explodes: only one of them is in the world");
            Assert.AreSame(red, Game.Grabber.FindTargetFrom(Game, red));
        }

        [Test]
        public void TheBot_UnpacksThreeBoxesFromOne()
        {
            Build(BuildBoxes);
            var revealed = new List<int>();
            nest.Revealed += revealed.Add;
            int mirrored = 0;
            Game.Events.NestRevealed += e => mirrored++;
            var bot = new Bot(Game);
            IEnumerator Script()
            {
                yield return bot.WalkTo(new Vector3(0f, 0f, -2.6f));
                yield return bot.Grab(red);
                yield return bot.Wait(0.1f);
                Assert.IsTrue(yellow.GameObject.activeSelf, "the yellow box is simply there");
                Assert.IsFalse(teal.GameObject.activeSelf, "the teal one not yet");
                Assert.IsTrue(yellow.Grabbable);
                Assert.Less(Vector3.Distance(new Vector3(0f, 0.45f, 0f), yellow.Center), 0.02f, "at its authored pose");
                Assert.AreEqual(0.9f, yellow.Scale, 1e-4f, "and scale");
                Assert.Less(yellow.Velocity.magnitude, 0.5f, "at rest");
                // The held red box now stops against the yellow one. Put it aside.
                yield return bot.DropAt(new Vector3(-5f, 0.6f, 3f));
                yield return bot.Grab(yellow);
                yield return bot.Wait(0.1f);
                Assert.IsTrue(teal.GameObject.activeSelf);
                yield return bot.DropAt(new Vector3(5f, 0.5f, 3f));
                yield return bot.Grab(teal);
                yield return bot.DropAt(new Vector3(0f, 0.3f, 4f));
                yield return bot.Wait(0.5f);
            }
            BotRunner.Run(Game, Script(), 40f);
            CollectionAssert.AreEqual(new[] { 1, 2 }, revealed);
            Assert.AreEqual(2, mirrored);
            Assert.AreEqual(3, nest.RevealedCount);
            Assert.Less(red.Center.x, -1f);
            Assert.Greater(yellow.Center.x, 1f);
            Assert.IsTrue(red.Grabbable && yellow.Grabbable && teal.Grabbable);

            // Grabbing the red box again reveals nothing new.
            LookAt(red.Center);
            Game.Input = Input;
            Click();
            Run(3);
            Assert.AreEqual(2, revealed.Count);
        }
    }

    static class GrabberProbe
    {
        /// <summary>What a click would take if the player looked straight at the prop from where they stand.</summary>
        public static Prop FindTargetFrom(this PerspectiveGrabber grabber, Game game, Prop prop)
        {
            TestHelpers.LookAt(game.Player, prop.Center);
            return grabber.FindTarget();
        }
    }
}
