using System;
using NUnit.Framework;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests
{
    /// <summary>A level built from a lambda, for tests that need a few pieces of geometry and nothing else.</summary>
    public sealed class AdHocLevel : LevelDefinition
    {
        readonly Action<LevelContext> build;
        readonly float killY;

        public AdHocLevel(Action<LevelContext> build, float killY = -30f)
        {
            this.build = build;
            this.killY = killY;
        }

        public int BuildCount { get; private set; }
        public override float KillY => killY;

        public override void Build(LevelContext ctx)
        {
            BuildCount++;
            build(ctx);
        }
    }

    /// <summary>
    /// Input for tests. <see cref="Hold"/> is returned every tick (keys kept down, a steady turn);
    /// <see cref="Once"/> is added for the next tick only (clicks, a single look delta).
    /// </summary>
    public sealed class ScriptedInput : IInputSource
    {
        public InputFrame Hold;
        public InputFrame Once;
        /// <summary>When set, produces the whole frame from the tick index instead.</summary>
        public Func<int, InputFrame> Script;

        int tick;

        public InputFrame Sample()
        {
            InputFrame frame;
            if (Script != null)
            {
                frame = Script(tick);
            }
            else
            {
                frame = Hold;
                frame.MoveX += Once.MoveX;
                frame.MoveZ += Once.MoveZ;
                frame.LookYaw += Once.LookYaw;
                frame.LookPitch += Once.LookPitch;
                frame.Jump |= Once.Jump;
                frame.Sprint |= Once.Sprint;
                frame.GrabPressed |= Once.GrabPressed;
                frame.RestartPressed |= Once.RestartPressed;
                frame.RotatePitch |= Once.RotatePitch;
                if (Once.RotateYaw != 0) frame.RotateYaw = Once.RotateYaw;
            }
            Once = default;
            tick++;
            return frame;
        }
    }

    /// <summary>Base fixture: whatever a test does, its Game is disposed afterwards so nothing leaks into the next test.</summary>
    public abstract class SimTest
    {
        protected Game Game;
        protected ScriptedInput Input;

        [TearDown]
        public void DisposeGame()
        {
            Game?.Dispose();
            Game = null;
            // A test that created a Game without storing it here must not poison the rest of the run.
            Game.Current?.Dispose();
        }

        /// <summary>Creates the Game with scripted input and loads an ad-hoc level.</summary>
        protected Game Build(Action<LevelContext> build, float killY = -30f)
        {
            Input = new ScriptedInput();
            Game = Game.Create(new GameOptions { Input = Input });
            Game.LoadLevel(new AdHocLevel(build, killY));
            return Game;
        }

        protected void Run(int ticks) => TestHelpers.Run(Game, ticks);
        protected void RunSeconds(float seconds) => TestHelpers.RunSeconds(Game, seconds);

        /// <summary>Points the view at a world position (test fixture shortcut, not player input).</summary>
        protected void LookAt(Vector3 target) => TestHelpers.LookAt(Game.Player, target);

        /// <summary>One tick with the grab button pressed.</summary>
        protected void Click()
        {
            Input.Once.GrabPressed = true;
            Game.Tick();
        }
    }

    public static class TestHelpers
    {
        public static int Ticks(float seconds) => Mathf.CeilToInt(seconds / Sim.Dt - 1e-3f);

        public static void Run(Game game, int ticks)
        {
            for (int i = 0; i < ticks; i++) game.Tick();
        }

        public static void RunSeconds(Game game, float seconds) => Run(game, Ticks(seconds));

        /// <summary>Ticks until the condition holds; false if it did not within the time limit.</summary>
        public static bool RunUntil(Game game, Func<bool> condition, float timeoutSeconds)
        {
            for (int i = Ticks(timeoutSeconds); i > 0 && !condition(); i--) game.Tick();
            return condition();
        }

        public static void LookAt(Player player, Vector3 target)
        {
            Vector3 to = target - player.Eye;
            player.Yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            player.Pitch = Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
        }

        /// <summary>A square floor whose top surface is at the given height, centered on the origin.</summary>
        public static GameObject Floor(LevelContext ctx, float size = 80f, float topY = 0f) =>
            ctx.AddStatic(BasicToys.Slab(new Vector3(size, 1f, size)), new Vector3(0f, topY - 0.5f, 0f));

        /// <summary>A static box given by its center and size.</summary>
        public static GameObject Box(LevelContext ctx, Vector3 center, Vector3 size) =>
            ctx.AddStatic(BasicToys.Slab(size, ToyMaterials.Wall), center);

        public static GameObject Box(LevelContext ctx, Vector3 center, Vector3 size, Quaternion rotation) =>
            ctx.AddStatic(BasicToys.Slab(size, ToyMaterials.Wall), center, rotation);

        /// <summary>A floor plus four walls: a closed room of the given inner half size and height.</summary>
        public static void Room(LevelContext ctx, float half = 20f, float height = 12f)
        {
            Floor(ctx, half * 2f + 2f);
            float y = height * 0.5f;
            Box(ctx, new Vector3(half + 0.5f, y, 0f), new Vector3(1f, height, half * 2f + 2f));
            Box(ctx, new Vector3(-half - 0.5f, y, 0f), new Vector3(1f, height, half * 2f + 2f));
            Box(ctx, new Vector3(0f, y, half + 0.5f), new Vector3(half * 2f + 2f, height, 1f));
            Box(ctx, new Vector3(0f, y, -half - 0.5f), new Vector3(half * 2f + 2f, height, 1f));
        }

        /// <summary>
        /// A ramp rising toward +Z at the given angle. Its surface starts at floor level (y = 0) at z = startZ.
        /// </summary>
        public static GameObject Ramp(LevelContext ctx, float angleDegrees, float startZ, float length = 12f, float width = 6f)
        {
            const float thickness = 1f;
            Quaternion rotation = Quaternion.Euler(-angleDegrees, 0f, 0f);
            Vector3 along = rotation * Vector3.forward;
            Vector3 normal = rotation * Vector3.up;
            Vector3 center = new Vector3(0f, 0f, startZ) + along * (length * 0.5f) - normal * (thickness * 0.5f);
            return Box(ctx, center, new Vector3(width, thickness, length), rotation);
        }

        /// <summary>Deepest penetration between the prop's colliders and any world or prop collider (0 if none).</summary>
        public static float DeepestOverlap(Game game, Prop prop)
        {
            Physics.SyncTransforms();
            float deepest = 0f;
            foreach (Collider other in game.Root.GetComponentsInChildren<Collider>())
            {
                int layer = other.gameObject.layer;
                if (layer != Layers.Default && layer != Layers.Prop) continue;
                if (PropRef.Of(other) == prop) continue;
                foreach (Collider own in prop.Colliders)
                {
                    if (Physics.ComputePenetration(own, own.transform.position, own.transform.rotation,
                            other, other.transform.position, other.transform.rotation, out _, out float depth))
                        deepest = Mathf.Max(deepest, depth);
                }
            }
            return deepest;
        }

        /// <summary>Runs the loaded level's own solution with a bot and asserts that it completes the level.</summary>
        public static void PlayLevel(Game game, float timeoutSeconds = 120f)
        {
            int completed = 0;
            game.Events.LevelCompleted += e => completed++;
            var bot = new Bot(game);
            BotRunner.Run(game, game.Level.Solve(bot), timeoutSeconds);
            RunUntil(game, () => game.LevelCompleted, 2f);
            Assert.IsTrue(game.LevelCompleted, "Solve() ran to its end but the level was not completed (bot at " + game.Player.Position + ")");
            Assert.AreEqual(1, completed, "LevelCompleted must fire exactly once");
        }
    }
}
