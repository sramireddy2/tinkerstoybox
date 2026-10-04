using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    public class PropLeashTests : SimTest
    {
        [Test]
        public void AStrandedProp_ComesBack_AtItsOriginalPoseAndScale()
        {
            PropLeash leash = null;
            Prop crate = null;
            var returned = new List<Prop>();
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                crate = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 3f), new PropOptions { Name = "Crate", Tags = new[] { "toy" } });
                leash = new PropLeash(ctx, new PropLeashOptions
                {
                    Tag = "toy", Forbidden = new[] { Zone.MinMax(new Vector3(5f, -1f, -5f), new Vector3(15f, 5f, 15f)) }, Grace = 1f, RestSpeed = 0.3f,
                });
                leash.PropReturned += returned.Add;
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            });
            RunSeconds(2f);
            Assert.AreEqual(0, leash.Returns, "in bounds nothing happens");

            // Put it out of bounds at another size, as a throw would.
            crate.SetScale(2.5f);
            crate.SetPose(new Vector3(9f, 1.3f, 3f), Quaternion.identity);
            RunSeconds(0.9f);
            Assert.AreEqual(0, leash.Returns, "still within the grace time");
            Assert.Greater(leash.TimeOut(crate), 0f);
            RunSeconds(1.5f);
            Assert.AreEqual(1, leash.Returns);
            Assert.AreSame(crate, returned[0]);
            Assert.AreEqual(1f, crate.Scale, 1e-4f, "back at its original scale");
            Assert.AreEqual(0f, crate.Position.x, 0.05f, "and at its original place");
        }

        [Test]
        public void AHeldProp_AMovingProp_AndAnExcusedProp_AreLeftAlone()
        {
            PropLeash leash = null;
            Prop crate = null;
            bool excused = false;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                crate = ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 0.5f, 3f), new PropOptions { Name = "Crate" });
                leash = new PropLeash(ctx, new PropLeashOptions
                {
                    Props = new[] { crate }, Allowed = new[] { Zone.MinMax(new Vector3(-4f, -1f, -4f), new Vector3(4f, 6f, 6f)) },
                    Grace = 0.5f, RestSpeed = 0.3f, Unless = () => excused,
                });
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            });

            // Out of bounds but excused.
            excused = true;
            crate.SetPose(new Vector3(0f, 0.5f, 12f), Quaternion.identity);
            RunSeconds(1.5f);
            Assert.AreEqual(0, leash.Returns, "Unless says the player is up there too");

            // Out of bounds and held.
            excused = false;
            LookAt(crate.Center);
            Click();
            Assert.AreSame(crate, Game.Grabber.Held);
            RunSeconds(1.5f);
            Assert.AreEqual(0, leash.Returns, "a toy in the hand is never taken away");
            Click();
            Assert.IsNull(Game.Grabber.Held);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => leash.Returns == 1, 3f), "once let go out of bounds and at rest, it comes back");
        }

        [Test]
        public void Eject_SendsThePropOutOfThePort_AtTheScaleItHas()
        {
            PropLeash leash = null;
            ReturnPort port = null;
            Prop marble = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                port = new ReturnPort(ctx, new ReturnPortOptions { Mouth = new Vector3(-6f, 1f, 0f), EjectVelocity = new Vector3(3f, 0f, 0f) });
                marble = ctx.AddProp(BasicToys.Ball(0.5f), new Vector3(6f, 0.5f, 6f), new PropOptions { Name = "Marble", Scale = 0.7f });
                leash = new PropLeash(ctx, new PropLeashOptions
                {
                    Props = new[] { marble }, Forbidden = new[] { Zone.Sphere(new Vector3(6f, 0f, 6f), 2f) }, Grace = 0.5f, Action = LeashAction.Eject, Port = port,
                });
                ctx.SetSpawn(new Vector3(0f, 0f, -6f), 0f);
            });
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => port.Ejections == 1, 3f));
            Assert.AreEqual(0.7f, marble.Scale, 1e-4f, "ejected at the scale it has");
            Assert.Less(marble.Center.x, -4f);
            Assert.Greater(marble.Velocity.x, 2f, "it rolls out into the room");
        }
    }
}
