using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    public class ReturnPortTests : SimTest
    {
        [Test]
        public void EjectedProps_AppearAtTheMouth_OneAtATime_ClearOfTheFloor()
        {
            ReturnPort port = null;
            Prop small = null, big = null;
            var order = new List<string>();
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                port = new ReturnPort(ctx, new ReturnPortOptions { Mouth = new Vector3(0f, 1.1f, 8f), EjectVelocity = new Vector3(0f, 0f, -3f), RetrySeconds = 0.25f });
                port.Ejected += p => order.Add(p.Name);
                small = ctx.AddProp(BasicToys.Ball(0.5f), new Vector3(6f, 0.3f, 0f), new PropOptions { Name = "Small", Scale = 0.6f });
                big = ctx.AddProp(BasicToys.Ball(0.5f), new Vector3(-6f, 1.5f, 0f), new PropOptions { Name = "Big", Scale = 3f });
                ctx.SetSpawn(new Vector3(0f, 0f, -8f), 0f);
            });
            RunSeconds(0.5f);
            port.Eject(big);
            port.Eject(small, 0.1f);
            port.Eject(big);
            Assert.AreEqual(2, port.Waiting, "asking twice queues once");
            Run(2);
            Assert.AreEqual(1, port.Ejections);
            Assert.AreEqual(8f, big.Center.z, 0.2f);
            Assert.GreaterOrEqual(big.Center.y, 1.55f, "a ball of radius 1.5 is lifted to clear the floor by a tenth");
            Assert.AreEqual(3f, big.Scale, 1e-4f);
            // The small one has to wait until the big one has rolled out of the mouth.
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => port.Ejections == 2, 4f));
            CollectionAssert.AreEqual(new[] { "Big", "Small" }, order);
            Assert.Less(big.Center.z, 7f, "the first had left the mouth by then");
            RunSeconds(1f);
            Assert.Less(small.Center.z, 7.5f, "and the second rolls out after it");
        }

        [Test]
        public void ThePlayer_IsPutAtThePortsPose()
        {
            ReturnPort port = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                port = new ReturnPort(ctx, new ReturnPortOptions { Mouth = new Vector3(-13f, 1f, 0f), PlayerPosition = new Vector3(-12.5f, 0f, 0f), PlayerYaw = 90f });
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            });
            Game.Player.SetScale(1f);
            port.Eject(Game.Player);
            Assert.AreEqual(-12.5f, Game.Player.Position.x, 1e-3f);
            Assert.AreEqual(90f, Game.Player.Yaw, 1e-3f);
        }
    }
}
