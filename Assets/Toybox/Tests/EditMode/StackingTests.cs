using NUnit.Framework;
using Toybox.Engine;
using Toybox.Toys;
using UnityEditor;
using UnityEngine;

namespace Toybox.Tests
{
    /// <summary>
    /// The mechanic changes mass with the cube of scale, so very unequal masses end up on top of each
    /// other all the time. These tests pin down what the physics setup has to cope with.
    /// </summary>
    public class StackingTests : SimTest
    {
        [Test]
        public void ProjectUsesTheTemporalGaussSeidelSolver()
        {
            // Not readable at runtime; ProjectSetup.ConfigurePhysics writes it. Everything below depends on it.
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);
            Assert.AreEqual(1, settings.FindProperty("m_SolverType").intValue, "run Toybox.EditorTools.ProjectSetup.ConfigurePhysics");
        }

        [Test]
        public void TowerOfBlocksStaysStanding()
        {
            var tower = new Prop[8];
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                for (int i = 0; i < tower.Length; i++)
                    tower[i] = ctx.AddProp(BasicToys.Block(0.6f), new Vector3(5f, 0.3f + i * 0.6f, 5f));
            });
            Vector3 top = tower[7].Position;
            RunSeconds(6f);
            Assert.Less(Vector3.Distance(top, tower[7].Position), 0.02f, "the tower sagged or leaned");
            foreach (Prop block in tower)
                Assert.Less(block.Body.linearVelocity.magnitude, 0.05f, "the tower is still jittering");
        }

        [Test]
        public void HeavyPropRestsOnLightOnes()
        {
            // 40 against 0.125: a ratio of 320, what a block enlarged about sevenfold weighs against its old self.
            Prop heavy = null;
            var light = new Prop[4];
            var starts = new Vector3[4];
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                for (int i = 0; i < 4; i++)
                {
                    starts[i] = new Vector3(i % 2 * 1.2f - 0.6f, 0.25f, 5f + i / 2 * 1.2f - 0.6f);
                    light[i] = ctx.AddProp(BasicToys.Block(0.5f), starts[i]);
                }
                heavy = ctx.AddProp(BasicToys.Block(new Vector3(2.5f, 1f, 2.5f)), new Vector3(0f, 1.2f, 5f), new PropOptions { Density = 40f / 6.25f });
            });
            Assert.AreEqual(320f, heavy.Mass / light[0].Mass, 1f);
            RunSeconds(5f);

            Assert.AreEqual(1f, heavy.Center.y, 0.02f, "the heavy block should lie on top of the small ones, not sink through them");
            Assert.Less(heavy.Body.linearVelocity.magnitude, 0.05f);
            for (int i = 0; i < 4; i++)
            {
                Assert.Less(Vector3.Distance(starts[i], light[i].Center), 0.1f, "a small block was squeezed out from under the heavy one");
                Assert.Less(light[i].Body.linearVelocity.magnitude, 0.05f);
            }
        }

        [Test]
        public void PlayerStandsAndLandsOnLightProps()
        {
            // 0.125 is an ordinary half-unit crate; 0.01 is the lightest a prop can be.
            foreach (float mass in new[] { 0.125f, Prop.MinMass })
            {
                Prop crate = null;
                Build(ctx =>
                {
                    TestHelpers.Floor(ctx);
                    crate = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(0f, 0.25f, 0f), new PropOptions { Density = mass / 0.125f });
                    ctx.SetSpawn(new Vector3(0f, 0.52f, 0f), 0f);
                });
                string what = " (crate of mass " + mass + ")";
                Assert.AreEqual(mass, crate.Mass, 1e-5f);
                RunSeconds(2f);
                Assert.AreSame(crate, Game.Player.GroundProp, "not standing on the crate" + what);
                Assert.AreEqual(0.5f, Game.Player.Position.y, 0.01f, "sank into the crate" + what);

                int landings = 0;
                Game.Events.PlayerLanded += e => landings++;
                for (int jump = 0; jump < 2; jump++)
                {
                    Input.Once.Jump = true;
                    float lowest = float.MaxValue;
                    for (int i = 0; i < 90; i++)
                    {
                        Game.Tick();
                        lowest = Mathf.Min(lowest, Game.Player.Position.y);
                    }
                    Assert.AreSame(crate, Game.Player.GroundProp, "fell through the crate after a jump" + what);
                    Assert.Greater(lowest, 0.35f, "dipped deep into the crate on landing" + what);
                    Assert.AreEqual(0.5f, Game.Player.Position.y, 0.01f);
                }
                Assert.AreEqual(2, landings, "each jump should land exactly once" + what);

                // A long fall onto it.
                Game.Player.Teleport(new Vector3(0f, 6f, 0f));
                RunSeconds(2f);
                Assert.AreSame(crate, Game.Player.GroundProp, "fell through the crate after a long drop" + what);
                Assert.AreEqual(0.5f, Game.Player.Position.y, 0.02f);
                Assert.Less(Vector3.Distance(new Vector3(0f, 0.25f, 0f), crate.Center), 0.1f, "the crate was kicked away" + what);

                Game.Dispose();
                Game = null;
            }
        }

        [Test]
        public void TinyPropsRestOnTheFloor()
        {
            Prop block = null, ball = null, wedge = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                block = ctx.AddProp(BasicToys.Block(0.5f), new Vector3(2f, 1f, 3f), new PropOptions { Scale = 0.05f });
                ball = ctx.AddProp(BasicToys.Ball(0.25f), new Vector3(2.5f, 1f, 3f), new PropOptions { Scale = 0.05f });
                wedge = ctx.AddProp(BasicToys.Wedge(1f, 0.5f, 0.5f), new Vector3(3f, 3f, 3f), new PropOptions { Scale = 0.02f });
            });
            Assert.AreEqual(CollisionDetectionMode.ContinuousDynamic, block.Body.collisionDetectionMode, "tiny props need continuous collision detection");
            Assert.AreEqual(Prop.MinMass, block.Mass, 1e-6f, "mass has a floor");
            RunSeconds(3f);
            Assert.AreEqual(0.0125f, block.Center.y, 0.005f);
            Assert.AreEqual(0.0125f, ball.Center.y, 0.005f);
            Assert.That(wedge.Center.y, Is.InRange(0f, 0.02f), "the tiny wedge fell through the floor or is floating");
        }
    }
}
