using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests.Gadgets
{
    /// <summary>Size is stored bounce: the launch speed depends on the pad's scale and on nothing else.</summary>
    public class BouncePadTests : SimTest
    {
        [Test]
        public void TheBot_BouncesOffAnEraserTheSizeOfABus_OntoTheCabinet()
        {
            // Level 6: the cabinet is 14 high; an eraser of scale 9.5 lies against its foot.
            BouncePad pad = null;
            Prop eraser = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                TestHelpers.Box(ctx, new Vector3(0f, 7f, 24f), new Vector3(24f, 14f, 12f));     // cabinet: face at z = 18, top 14
                eraser = ToyCatalog.Add(ctx, ToyId.Eraser, new Vector3(0f, 0.125f * 9.5f + 0.01f, 18f - 0.25f * 9.5f - 0.02f), 9.5f, null, o => o.Tags = new[] { "bouncy" });
                pad = new BouncePad(ctx, new BouncePadOptions { Prop = eraser, GainPerScale = 1.75f, MinImpact = 3f, MinNormalY = 0.9f, Cooldown = 0.1f });
                ctx.SetSpawn(new Vector3(-9.5f, 0f, 8f), 0f);
            });
            Assert.AreEqual(154f, eraser.Mass, 3f);
            Assert.AreEqual(27.05f, pad.LaunchSpeed(9.5f), 0.05f, "sqrt(2 * 22 * 1.75 * 9.5)");
            Assert.AreEqual(16.6f, pad.Apex(9.5f), 0.05f);
            float launch = 0f, impact = 0f, apex = 0f;
            pad.Bounced += (l, i) =>
            {
                launch = l;
                impact = i;
            };
            Game.Context.OnUpdate(dt => apex = Mathf.Max(apex, Game.Player.Position.y));
            var bot = new Bot(Game);
            IEnumerator Script()
            {
                yield return bot.Wait(0.5f);
                yield return bot.WalkTo(new Vector3(-9.5f, 0f, 15.6f));
                yield return bot.WalkTo(new Vector3(-3.5f, 2.4f, 15.6f));          // up the slanted end
                Assert.AreEqual(0, pad.BounceCount, "walking up the ramp and onto the top is not a bounce");
                yield return bot.WalkTo(new Vector3(0f, 2.4f, 16.6f));
                Assert.AreEqual(2.4f, bot.Player.Position.y, 0.1f, "on top of the slab");
                yield return bot.Jump(false);
                yield return bot.Until(() => pad.BounceCount >= 1, 3f);
                yield return bot.WalkTo(new Vector3(0f, 14f, 21f), 0.5f, 6f);      // steer in the air
                yield return bot.Until(() => bot.Player.Grounded && bot.Player.Position.y > 13.5f, 4f);
                yield return bot.WalkTo(new Vector3(0f, 14f, 27f));
            }
            BotRunner.Run(Game, Script(), 40f);
            Assert.AreEqual(27.05f, launch, 0.05f, "LEVELS appendix B: first bounce launch speed 27.0");
            Assert.AreEqual(7.6f, impact, 0.8f, "landing from an ordinary jump");
            Assert.AreEqual(2.4f + 16.6f, apex, 0.6f, "apex above the floor = thickness + bounce = 2.0 x scale");
            Assert.AreEqual(14f, Game.Player.Position.y, 0.05f, "on the cabinet");
            Assert.AreEqual(1, pad.BounceCount);
        }

        [Test]
        public void TheLaunch_DependsOnScaleOnly_SoBouncesDoNotAccumulate()
        {
            BouncePad pad = null;
            GameObject trampoline = null;
            var launches = new List<float>();
            var impacts = new List<float>();
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                trampoline = TestHelpers.Box(ctx, new Vector3(0f, 0.25f, 0f), new Vector3(4f, 0.5f, 4f));
                pad = new BouncePad(ctx, new BouncePadOptions { Surface = trampoline.GetComponent<Collider>(), Scale = 2f });
                pad.Bounced += (l, i) =>
                {
                    launches.Add(l);
                    impacts.Add(i);
                };
                ctx.SetSpawn(new Vector3(0f, 0.5f, 0f), 0f);
            });
            RunSeconds(0.5f);
            Assert.AreEqual(0, pad.BounceCount, "standing on it does nothing");

            // One jump, then the pad keeps the player going: every landing is harder than a jump's, every launch the same.
            Input.Once.Jump = true;
            float apex = 0f;
            for (int i = 0; i < 6 * 60; i++)
            {
                Game.Tick();
                apex = Mathf.Max(apex, Game.Player.Position.y);
            }
            Assert.GreaterOrEqual(launches.Count, 3, "it bounced again and again");
            float expected = Mathf.Sqrt(2f * 22f * 1.75f * 2f);
            foreach (float launch in launches) Assert.AreEqual(expected, launch, 1e-3f);
            Assert.Greater(impacts[1], impacts[0] + 3f, "the second landing came from much higher than the jump");
            Assert.AreEqual(0.5f + 3.5f, apex, 0.25f, "and still the apex is 1.75 x scale above the pad");
        }

        [Test]
        public void ASoftLanding_ASlantedFace_AndAHeldPad_DoNotBounce()
        {
            BouncePad pad = null;
            Prop eraser = null;
            Build(ctx =>
            {
                TestHelpers.Floor(ctx);
                eraser = ToyCatalog.Add(ctx, ToyId.Eraser, new Vector3(0f, 0.5f + 0.01f, 5f), 4f);
                pad = new BouncePad(ctx, new BouncePadOptions { Prop = eraser });
                ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            });
            // Dropped from 0.15 above its top: 2.6 units a second at touchdown, under MinImpact.
            Game.Player.Teleport(new Vector3(0f, 1.01f + 0.15f, 5f), 0f);
            RunSeconds(1f);
            Assert.AreEqual(0, pad.BounceCount, "a soft landing is just a landing");
            Assert.AreSame(eraser, Game.Player.GroundProp);

            // Onto the slanted end (39.8 degrees: normal y 0.77) from high up.
            Game.Player.Teleport(new Vector3(-2.4f, 4f, 5f), 0f);
            RunSeconds(1.5f);
            Assert.AreEqual(0, pad.BounceCount, "the ramps never bounce");

            // From high up onto the top: now it does.
            Game.Player.Teleport(new Vector3(0f, 4f, 5f), 0f);
            Assert.IsTrue(TestHelpers.RunUntil(Game, () => pad.BounceCount == 1, 2f));
            Assert.AreEqual(Mathf.Sqrt(2f * 22f * 1.75f * 4f), pad.LastLaunchSpeed, 1e-3f);
        }
    }
}
