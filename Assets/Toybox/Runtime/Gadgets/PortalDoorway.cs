using System;
using Toybox.Engine;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Gadgets
{
    public sealed class PortalDoorwayOptions
    {
        public string Name = "Portal";
        /// <summary>The door frame: a Fixed prop whose origin is the middle of its threshold, on the floor.</summary>
        public Prop Prop;
        /// <summary>The player's size is the doorway's, kept within these.</summary>
        public float MinPlayerScale = 0.3f, MaxPlayerScale = 3.2f;
        /// <summary>Seconds after a crossing before the doorway works again.</summary>
        public float Cooldown = 0.5f;
        /// <summary>The fastest the frame comes down while it settles.</summary>
        public float SettleSpeed = 30f;
        /// <summary>The opening and the frame at scale 1: width, height; frame width, depth.</summary>
        public Vector2 Opening = ToyFactory.DoorwayOpening;
        public float FrameWidth = ToyFactory.DoorwaySize.x, FrameDepth = ToyFactory.DoorwaySize.z;
    }

    /// <summary>What the last attempt to cross did.</summary>
    public enum PortalResult
    {
        None,
        /// <summary>The player came out on the far side.</summary>
        Through,
        /// <summary>No room on the far side: they came back out of the side they went in, turned round.</summary>
        TurnedBack,
        /// <summary>No room on either side: nothing happened.</summary>
        Blocked,
    }

    /// <summary>
    /// A door that is always the right size for whoever walks through it - so walking through makes the
    /// player that size (LEVELS 2.3, Level 14). Simulation side only: the frame settles after a drop, and
    /// a crossing rescales the player about the threshold. What a renderer needs to show the view through
    /// the door is exposed: <see cref="Threshold"/>, <see cref="Rotation"/>, <see cref="OpeningSize"/>,
    /// <see cref="TargetScale"/>, <see cref="ViewRatio"/>, <see cref="Active"/> and <see cref="ViewEye"/>.
    /// </summary>
    public sealed class PortalDoorway : Gadget
    {
        static readonly RaycastHit[] CastHits = new RaycastHit[32];

        readonly PortalDoorwayOptions options;
        readonly Prop prop;
        readonly int cooldownTicks;

        Vector3 settledPosition;
        Quaternion settledRotation;
        float settledScale;
        float fallSpeed;
        int side;
        int cooldown;

        public PortalDoorway(LevelContext ctx, PortalDoorwayOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            prop = options.Prop ?? throw new ArgumentException("A portal doorway needs its prop.", nameof(options));
            cooldownTicks = Ticks(options.Cooldown);
        }

        public Prop Prop => prop;
        /// <summary>True while the frame stands still on something: only then is it a portal.</summary>
        public bool Settled { get; private set; }
        /// <summary>The same as Settled, and not cooling down: a crossing would count right now.</summary>
        public bool Active => Settled && cooldown == 0 && !prop.Held && !prop.Removed;
        /// <summary>The scale the player comes out with: the doorway's, within the player's limits.</summary>
        public float TargetScale => Mathf.Clamp(prop.Scale, options.MinPlayerScale, options.MaxPlayerScale);
        /// <summary>Target scale over the player's scale now: how much bigger the world beyond the door looks... is walked into.</summary>
        public float ViewRatio => TargetScale / Mathf.Max(1e-4f, Game.Player.Scale);
        /// <summary>The middle of the threshold, on the floor.</summary>
        public Vector3 Threshold => prop.Position;
        public Quaternion Rotation => prop.Rotation;
        /// <summary>The direction through the door (its own +Z).</summary>
        public Vector3 Normal => prop.Rotation * Vector3.forward;
        /// <summary>Width and height of the opening now.</summary>
        public Vector2 OpeningSize => options.Opening * prop.Scale;
        /// <summary>Middle of the opening.</summary>
        public Vector3 OpeningCenter => prop.Position + prop.Rotation * new Vector3(0f, options.Opening.y * prop.Scale * 0.5f, 0f);
        public PortalResult LastResult { get; private set; }
        public int Crossings { get; private set; }

        /// <summary>
        /// Where a camera has to stand to show what the player will see after stepping through: the eye,
        /// scaled about the threshold by the ratio of the sizes (LEVELS, Level 14, portal rendering).
        /// </summary>
        public Vector3 ViewEye(Vector3 eye) => Threshold + (eye - Threshold) * ViewRatio;

        /// <summary>Inside the tick.</summary>
        public event Action OnSettled;
        /// <summary>The player's scale before and after.</summary>
        public event Action<float, float> Crossed;
        public event Action Blocked;

        protected override void Tick(float dt)
        {
            if (cooldown > 0) cooldown--;
            if (prop.Removed) return;
            if (prop.Held || !prop.GameObject.activeInHierarchy)
            {
                Unsettle();
                return;
            }
            // Moved by something else (a recall pad, a respawn): it has to settle again.
            if (Settled && (prop.Position != settledPosition || prop.Rotation != settledRotation || prop.Scale != settledScale)) Unsettle();
            if (!Settled)
            {
                Settle(dt);
                if (!Settled) return;
            }
            Watch();
        }

        void Unsettle()
        {
            Settled = false;
            fallSpeed = 0f;
            side = 0;
        }

        // The frame comes down under gravity until its foot rests; it never turns.
        void Settle(float dt)
        {
            float scale = prop.Scale;
            Quaternion rotation = prop.Rotation;
            Vector3 position = prop.Position;
            var half = new Vector3(options.FrameWidth * scale * 0.5f, 0.05f * scale, options.FrameDepth * scale * 0.5f);
            // From a little above the foot, so that a foot already on the floor finds it.
            float lift = half.y + 0.05f * scale;
            fallSpeed = Mathf.Min(options.SettleSpeed, fallSpeed + Game.Gravity * dt);
            float step = fallSpeed * dt;
            float gap = FootGap(position + Vector3.up * lift, half, rotation, lift + step + 0.01f) - (lift - half.y);

            if (gap <= step)
            {
                if (Mathf.Abs(gap) > 1e-5f) prop.SetPose(position + Vector3.down * Mathf.Max(gap, -lift), rotation);
                Settled = true;
                fallSpeed = 0f;
                settledPosition = prop.Position;
                settledRotation = prop.Rotation;
                settledScale = prop.Scale;
                side = SideOf(Game.Player.Position);
                OnSettled?.Invoke();
                Game.Events.RaisePortalSettled(Event(prop.Position, prop, TargetScale));
                return;
            }
            prop.SetPose(position + Vector3.down * step, rotation);
        }

        // Distance from the underside of a box to the first thing below it that is not the frame itself.
        float FootGap(Vector3 centre, Vector3 half, Quaternion rotation, float reach)
        {
            int count = Game.PhysicsScene.BoxCast(centre, half, Vector3.down, CastHits, rotation, reach, Layers.SolidMask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (PropRef.Of(CastHits[i].collider) == prop) continue;
                nearest = Mathf.Min(nearest, CastHits[i].distance);
            }
            return nearest;
        }

        // Which side of the door plane a point is on: +1 in front (+Z of the door), -1 behind.
        int SideOf(Vector3 point)
        {
            float z = Vector3.Dot(point - prop.Position, Normal);
            return z >= 0f ? 1 : -1;
        }

        void Watch()
        {
            Player player = Game.Player;
            int now = SideOf(player.Position);
            int before = side;
            side = now;
            if (before == 0 || now == before || cooldown > 0) return;

            // The axis crossed the plane. Did the capsule go through the opening?
            float scale = prop.Scale;
            Vector3 local = Quaternion.Inverse(prop.Rotation) * (player.Position - prop.Position);
            float halfWidth = options.Opening.x * scale * 0.5f;
            if (Mathf.Abs(local.x) > halfWidth + player.Radius) return;
            if (local.y > options.Opening.y * scale || local.y + player.Height < 0f) return;

            float target = TargetScale;
            float from = player.Scale;
            // Already the size of the door: it is just a door.
            if (Mathf.Abs(target - from) <= 1e-3f * from)
            {
                LastResult = PortalResult.Through;
                return;
            }
            float ratio = target / from;
            float beyond = Player.BaseRadius * target + options.FrameDepth * scale * 0.5f + 0.05f;
            float lateral = Mathf.Clamp(local.x * ratio, -Mathf.Max(0f, halfWidth - Player.BaseRadius * target), Mathf.Max(0f, halfWidth - Player.BaseRadius * target));
            Vector3 velocity = player.Velocity;

            // Out of the far side; failing that, back out of the near side, turned round.
            Vector3 far = prop.Position + prop.Rotation * new Vector3(lateral, 0f, now * beyond);
            Vector3 near = prop.Position + prop.Rotation * new Vector3(lateral, 0f, -now * beyond);
            if (TryPlace(player, far, target, velocity * ratio, player.Yaw))
            {
                Finish(PortalResult.Through, from, target);
                side = now;
            }
            else if (TryPlace(player, near, target, -velocity * ratio, player.Yaw + 180f))
            {
                Finish(PortalResult.TurnedBack, from, target);
                side = -now;
            }
            else
            {
                LastResult = PortalResult.Blocked;
                cooldown = cooldownTicks;
                Blocked?.Invoke();
                Game.Events.RaisePortalBlocked(Event(prop.Position, prop, target));
            }
        }

        bool TryPlace(Player player, Vector3 feet, float scale, Vector3 velocity, float yaw)
        {
            if (!GadgetKit.CapsuleFree(Game, feet, Player.BaseRadius * scale, Player.BaseHeight * scale, prop)) return false;
            Vector3 old = player.Position;
            float oldScale = player.Scale;
            // Shrink before the move, grow after it: the capsule is never bigger than the room it is in.
            if (scale < oldScale) player.SetScale(scale);
            player.Teleport(feet, yaw, player.Pitch);
            if (scale > oldScale && !player.SetScale(scale))
            {
                player.Teleport(old, yaw, player.Pitch);
                return false;
            }
            player.SetVelocity(velocity);
            return true;
        }

        void Finish(PortalResult result, float from, float to)
        {
            LastResult = result;
            Crossings++;
            cooldown = cooldownTicks;
            Crossed?.Invoke(from, to);
            Game.Events.RaisePortalCrossed(Event(prop.Position, prop, from, to, (int)result));
        }

        public override void Reset()
        {
            base.Reset();
            Unsettle();
            cooldown = 0;
            LastResult = PortalResult.None;
        }
    }
}
