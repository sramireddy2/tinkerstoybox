using System;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    public sealed class BouncePadOptions
    {
        public string Name = "Bounce Pad";
        /// <summary>The bouncy toy. Its scale decides how high it throws.</summary>
        public Prop Prop;
        /// <summary>Instead of a prop: a fixed trampoline (any collider of the level), with <see cref="Scale"/> standing in for the prop's scale.</summary>
        public Collider Surface;
        public float Scale = 1f;
        /// <summary>The bounce carries the player this many units up per unit of scale.</summary>
        public float GainPerScale = 1.75f;
        /// <summary>The player has to come down at least this fast.</summary>
        public float MinImpact = 3f;
        /// <summary>Only faces that point this much upward bounce: the slanted ends of the eraser (0.77) do not.</summary>
        public float MinNormalY = 0.9f;
        /// <summary>Seconds after a bounce before the next one can happen.</summary>
        public float Cooldown = 0.1f;
        /// <summary>The pad does not bounce while it moves faster than this itself.</summary>
        public float MaxPadSpeed = 1f;
    }

    /// <summary>
    /// Size is stored bounce (LEVELS 2.2, Level 6): landing on the toy throws the player straight up at
    /// sqrt(2 g Gain scale), so the apex is Gain x scale above the pad. The launch speed depends on the
    /// scale only, not on how hard the player came down: bounces do not accumulate.
    /// </summary>
    public sealed class BouncePad : Gadget
    {
        readonly BouncePadOptions options;
        readonly int cooldownTicks;
        bool wasOn;
        float lastFall;
        int cooldown;

        public BouncePad(LevelContext ctx, BouncePadOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            if (options.Prop == null && options.Surface == null) throw new ArgumentException("A bounce pad needs a Prop or a Surface.", nameof(options));
            cooldownTicks = Ticks(options.Cooldown);
        }

        public int BounceCount { get; private set; }
        /// <summary>The speed the last bounce launched the player with.</summary>
        public float LastLaunchSpeed { get; private set; }

        /// <summary>Inside the tick: launch speed and the speed the player came down with.</summary>
        public event Action<float, float> Bounced;

        /// <summary>The speed a pad of this scale launches with.</summary>
        public float LaunchSpeed(float scale) => Mathf.Sqrt(2f * Game.Gravity * options.GainPerScale * Mathf.Max(0f, scale));

        /// <summary>How high above the pad a bounce at this scale carries the player's feet.</summary>
        public float Apex(float scale) => options.GainPerScale * Mathf.Max(0f, scale);

        protected override void Tick(float dt)
        {
            Player player = Game.Player;
            if (cooldown > 0) cooldown--;

            bool on = false;
            float scale = options.Scale;
            float padSpeed = 0f;
            if (player.Grounded && player.GroundCollider != null)
            {
                if (options.Prop != null)
                {
                    Prop prop = options.Prop;
                    on = !prop.Removed && !prop.Held && player.GroundProp == prop;
                    scale = prop.Scale;
                    padSpeed = on ? prop.Velocity.magnitude : 0f;
                }
                else
                {
                    on = player.GroundCollider == options.Surface;
                }
            }

            // The fall speed of the tick before: by now the landing has already taken it out of the body.
            float fall = lastFall;
            // The controller ran before this in the tick; what it left is what the next step starts from.
            lastFall = Mathf.Max(0f, -player.Velocity.y);

            bool landed = on && !wasOn;
            wasOn = on;
            if (!landed || cooldown > 0) return;
            if (player.GroundNormal.y < options.MinNormalY || fall < options.MinImpact * Mathf.Sqrt(player.Scale) || padSpeed > options.MaxPadSpeed) return;

            float launch = LaunchSpeed(scale);
            Vector3 velocity = player.Velocity;
            player.SetVelocity(new Vector3(velocity.x, launch, velocity.z));
            cooldown = cooldownTicks;
            wasOn = false;
            BounceCount++;
            LastLaunchSpeed = launch;
            Bounced?.Invoke(launch, fall);
            Game.Events.RaiseBounced(Event(player.Position, options.Prop, launch, fall));
        }

        public override void Reset()
        {
            base.Reset();
            wasOn = false;
            lastFall = 0f;
            cooldown = 0;
            BounceCount = 0;
        }
    }
}
