using System;
using System.Collections.Generic;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    public sealed class WindStreamOptions
    {
        public string Name = "Wind";
        /// <summary>The stream: an oriented box.</summary>
        public Vector3 Center;
        public Vector3 Size = new Vector3(4f, 4f, 10f);
        public Quaternion Rotation = Quaternion.identity;
        /// <summary>Which way it blows.</summary>
        public Vector3 Direction = Vector3.forward;
        /// <summary>Scales everything the wind does. 0 is calm.</summary>
        public float Strength = 1f;
        /// <summary>Acceleration of an airborne player inside the stream, per unit of strength.</summary>
        public float PlayerAirPush = 3f;
        /// <summary>The same for a player standing on the ground (LEVELS: 0, the wind only takes you in the air).</summary>
        public float PlayerGroundPush;
        /// <summary>A loose prop of scale s is accelerated by PropDrag / s: area over mass goes as 1 / s.</summary>
        public float PropDrag = 12f;
        /// <summary>The most the wind accelerates anything.</summary>
        public float MaxPropAcceleration = 40f;
        /// <summary>Props heavier than this stand in the wind unmoved.</summary>
        public float MaxPropMass = 1f;
        /// <summary>
        /// The wind gets under what it moves: this share of gravity is taken off a prop it blows. Without it
        /// a flat toy lying on a deck does not slide at all - friction on a flat contact is worth 26 units
        /// per second squared here (measured), more than the wind gives anything bigger than a crumb.
        /// </summary>
        public float PropLift = 0.9f;
    }

    /// <summary>
    /// A stream of air (LEVELS 2.2): the desk fan's gale in Level 5, the nozzle jet in Level 15. Size is
    /// area: what the wind does to a toy depends on its scale, a small light one is blown away, a big one
    /// lies still. The player is pushed through Player.AddPush. Deterministic: props in Id order, forces
    /// only (the wind is not an impact).
    /// </summary>
    public sealed class WindStream : Gadget
    {
        readonly WindStreamOptions options;
        readonly Zone box;
        readonly Vector3 direction;
        float strength;

        public WindStream(LevelContext ctx, WindStreamOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            box = Zone.Box(options.Center, options.Size, options.Rotation);
            direction = options.Direction.sqrMagnitude > 1e-8f ? options.Direction.normalized : Vector3.forward;
            strength = Mathf.Max(0f, options.Strength);
        }

        public Zone Box => box;
        public Vector3 Direction => direction;
        /// <summary>True while the player is inside the stream.</summary>
        public bool PlayerInside { get; private set; }

        /// <summary>How hard it blows; setting it raises WindChanged.</summary>
        public float Strength
        {
            get => strength;
            set
            {
                value = Mathf.Max(0f, value);
                if (Mathf.Approximately(value, strength)) return;
                strength = value;
                WindChanged?.Invoke(value);
                Game.Events.RaiseWindChanged(Event(options.Center, null, value));
            }
        }

        /// <summary>Inside the tick (or whenever the strength is set): the new strength.</summary>
        public event Action<float> WindChanged;

        public bool Contains(Vector3 point) => box.Contains(point);

        /// <summary>The acceleration the wind gives a loose prop of this scale and mass (0 if it is too heavy).</summary>
        public float PropAcceleration(float scale, float mass)
        {
            if (strength <= 0f || mass > options.MaxPropMass || scale <= 0f) return 0f;
            return Mathf.Min(options.MaxPropAcceleration, options.PropDrag * strength / scale);
        }

        protected override void Tick(float dt)
        {
            Player player = Game.Player;
            PlayerInside = box.Contains(player.Position + Vector3.up * (player.Height * 0.5f));
            if (PlayerInside && strength > 0f)
            {
                float push = player.Grounded ? options.PlayerGroundPush : options.PlayerAirPush;
                if (push > 0f) player.AddPush(direction * (push * strength));
            }

            if (strength <= 0f || options.PropDrag <= 0f) return;
            IReadOnlyList<Prop> props = Game.Props;
            for (int i = 0; i < props.Count; i++)
            {
                Prop prop = props[i];
                // Held, moored or carried props are somebody else's; frozen and fixed ones do not move.
                if (!GadgetKit.IsLoose(prop) || prop.Body.isKinematic) continue;
                // A thin toy lying on the floor of the stream has its centre in that floor: a little above counts too.
                Vector3 centre = prop.Center;
                if (!box.Contains(centre) && !box.Contains(centre + Vector3.up * Mathf.Min(prop.Radius, 0.25f))) continue;
                float acceleration = PropAcceleration(prop.Scale, prop.Mass);
                if (acceleration <= 0f) continue;
                prop.Body.AddForce(direction * acceleration - Physics.gravity * Mathf.Clamp01(options.PropLift), ForceMode.Acceleration);
            }
        }

        public override void Reset()
        {
            base.Reset();
            Strength = options.Strength;
        }
    }
}
