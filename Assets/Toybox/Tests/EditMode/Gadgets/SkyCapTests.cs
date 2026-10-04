using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    /// <summary>Zones: the regions every sensing gadget is made of.</summary>
    public class ZoneTests
    {
        [Test]
        public void Shapes_ContainWhatTheyShould()
        {
            Zone box = Zone.MinMax(new Vector3(-1f, 0f, -2f), new Vector3(1f, 3f, 2f));
            Assert.IsTrue(box.Contains(new Vector3(0.9f, 2.9f, -1.9f)));
            Assert.IsFalse(box.Contains(new Vector3(1.1f, 1f, 0f)));
            Assert.AreEqual(0f, box.MinY, 1e-5f);
            Assert.AreEqual(3f, box.MaxY, 1e-5f);

            Zone turned = Zone.Box(Vector3.zero, new Vector3(4f, 1f, 1f), Quaternion.Euler(0f, 90f, 0f));
            Assert.IsTrue(turned.Contains(new Vector3(0f, 0f, 1.9f)), "a turned box is long along z");
            Assert.IsFalse(turned.Contains(new Vector3(1.9f, 0f, 0f)));

            Zone cylinder = Zone.Cylinder(2f, 3f, 1f, -1f, 1f);
            Assert.IsTrue(cylinder.Contains(new Vector3(2.7f, 0.9f, 3.7f)));
            Assert.IsFalse(cylinder.Contains(new Vector3(2.8f, 0f, 3.8f)), "outside the radius");
            Assert.IsFalse(cylinder.Contains(new Vector3(2f, 1.1f, 3f)), "above it");
            Assert.IsTrue(cylinder.ContainsXZ(new Vector3(2f, 50f, 3f)));

            Zone sphere = Zone.Sphere(Vector3.up, 2f);
            Assert.IsTrue(sphere.Contains(new Vector3(0f, 2.9f, 0f)));
            Assert.IsFalse(sphere.Contains(new Vector3(1.5f, 2.5f, 0f)));

            Assert.IsFalse(default(Zone).Contains(Vector3.zero), "the default zone contains nothing");
            Assert.AreEqual(new Vector3(1f, 3f, 0f), box.ClosestPoint(new Vector3(5f, 9f, 0f)));
            Assert.IsTrue(box.Grown(0.5f).Contains(new Vector3(1.4f, 1f, 0f)));
        }
    }

    public class SkyCapTests : SimTest
    {
        [Test]
        public void AToyHeldAtTheSky_StopsAtTheCap()
        {
            Prop block = null;
            GameObject cap = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                cap = SkyCap.Add(ctx, -10f, 10f, -10f, 10f, 12f);
                block = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 1.5f, 2f), new PropOptions { Name = "Block" });
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            });
            Assert.AreEqual(0, cap.GetComponentsInChildren<Renderer>().Length, "the cap is invisible");
            Assert.AreEqual(Layers.Default, cap.layer);
            Assert.AreEqual(12f, cap.GetComponent<Collider>().bounds.min.y, 1e-4f, "its underside is where the level asked");

            LookAt(block.Center);
            Click();
            Assert.AreSame(block, Game.Grabber.Held);
            Game.Player.Pitch = 80f;
            Run(3);
            Assert.Less(block.Center.y + block.Scale * 0.25f, 12.01f, "the held block stops under the cap");
            Assert.Greater(block.Center.y, 9f, "and it did get that far");
        }
    }
}
