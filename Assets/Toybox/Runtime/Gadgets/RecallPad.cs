using System;
using Toybox.Art;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    public sealed class RecallPadOptions
    {
        public string Name = "Recall Pad";
        /// <summary>The middle of the disc, on the floor.</summary>
        public Vector3 Position;
        public float Radius = 0.6f;
        /// <summary>The prop it fetches (the doorway).</summary>
        public Prop Prop;
        /// <summary>Seconds the player has to stand on it.</summary>
        public float HoldSeconds = 0.5f;
        /// <summary>The fetched prop is set down this far in front of the player (plus the sizes of both).</summary>
        public float Clearance = 0.3f;
        public bool Visual = true;
    }

    /// <summary>
    /// A floor plate that brings a lost toy back (LEVELS 2.3, Level 14): stand on it for half a second and
    /// the prop is put down in front of you, at your own size, facing you. One fetch per visit: step off
    /// and on again for another.
    /// </summary>
    public sealed class RecallPad : Gadget
    {
        static readonly Collider[] Hits = new Collider[8];

        readonly RecallPadOptions options;
        readonly int holdTicks;
        readonly SignalLamp lamp;
        int standing;
        bool spent;
        int flash;

        public RecallPad(LevelContext ctx, RecallPadOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            if (options.Prop == null) throw new ArgumentException("A recall pad needs the prop it fetches.", nameof(options));
            holdTicks = Ticks(options.HoldSeconds, 1);
            if (!options.Visual) return;
            var root = new GameObject("Recall Pad " + Name);
            root.transform.SetParent(ctx.Root, false);
            root.transform.position = options.Position;
            GadgetKit.Visual(root.transform, "Base", GadgetKit.DiscMesh(options.Radius, 0.03f), GadgetKit.Body, new Vector3(0f, 0.015f, 0f));
            lamp = GadgetKit.Lamp(root.transform, GadgetKit.DiscMesh(options.Radius * 0.55f, 0.02f), Palette.Amber, new Vector3(0f, 0.035f, 0f));
        }

        public Vector3 Position => options.Position;
        public float Radius => options.Radius;
        /// <summary>True while the player's feet are on the disc.</summary>
        public bool PlayerOn { get; private set; }
        /// <summary>How far the wait has got, 0..1.</summary>
        public float Progress01 => spent ? 1f : Mathf.Clamp01((float)standing / holdTicks);
        public int Recalls { get; private set; }

        /// <summary>Inside the tick, after the prop was put down.</summary>
        public event Action Recalled;

        protected override void Tick(float dt)
        {
            Player player = Game.Player;
            Vector3 feet = player.Position;
            Vector3 offset = feet - options.Position;
            float reach = options.Radius * Mathf.Max(1f, player.Scale);
            PlayerOn = player.Grounded && Mathf.Abs(offset.y) < 0.3f * Mathf.Max(1f, player.Scale) && offset.x * offset.x + offset.z * offset.z <= reach * reach;

            if (!PlayerOn)
            {
                standing = 0;
                spent = false;
            }
            else if (!spent && !options.Prop.Removed && !options.Prop.Held)
            {
                if (++standing >= holdTicks) Fetch(player);
            }

            if (lamp == null) return;
            if (flash > 0) flash--;
            lamp.Set(flash > 0 ? Palette.Go : Palette.Amber);
            lamp.Pulse(Seconds);
        }

        void Fetch(Player player)
        {
            Prop prop = options.Prop;
            spent = true;
            standing = 0;
            if (prop.Driven) prop.EndDrive(Vector3.zero);
            prop.SetScale(player.Scale);
            // In front of the player, turned to face them, a little above the floor (it settles). If a wall is
            // in the way there: to one side, behind, or failing all that on the pad itself.
            Vector3 position = options.Position + Vector3.up * (0.02f * player.Scale);
            Quaternion rotation = Quaternion.Euler(0f, player.Yaw + 180f, 0f);
            float distance = player.Radius + prop.LocalHalfExtents.z * prop.Scale + options.Clearance * player.Scale;
            for (int turn = 0; turn < 4; turn++)
            {
                // Ahead, right, left, behind.
                float yaw = player.Yaw + (turn == 0 ? 0f : turn == 1 ? 90f : turn == 2 ? -90f : 180f);
                Vector3 ahead = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                Vector3 candidate = player.Position + ahead * distance + Vector3.up * (0.02f * player.Scale);
                Quaternion facing = Quaternion.LookRotation(-ahead, Vector3.up);
                Vector3 half = prop.LocalHalfExtents * (prop.Scale * 0.98f);
                Vector3 centre = candidate + facing * (prop.LocalCenter * prop.Scale) + Vector3.up * (0.03f * player.Scale);
                if (Game.PhysicsScene.OverlapBox(centre, half, Hits, facing, Layers.DefaultMask, QueryTriggerInteraction.Ignore) > 0) continue;
                position = candidate;
                rotation = facing;
                break;
            }
            prop.SetPose(position, rotation);
            Recalls++;
            flash = Ticks(0.5f);
            Recalled?.Invoke();
            Game.Events.RaiseRecalled(Event(position, prop, prop.Scale));
        }

        public override void Reset()
        {
            base.Reset();
            standing = 0;
            spent = false;
        }
    }
}
