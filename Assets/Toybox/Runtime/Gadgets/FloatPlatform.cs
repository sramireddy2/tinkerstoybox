using System;
using Toybox.Art;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    public sealed class FloatPlatformOptions
    {
        public string Name = "Float";
        /// <summary>The platform (renderers and colliders, no Rigidbody). Null: a slab of <see cref="Size"/> is made.</summary>
        public GameObject Platform;
        public Vector3 Size = new Vector3(3f, 0.4f, 3f);
        /// <summary>Where it floats: x and z of its centre.</summary>
        public Vector2 Position;
        /// <summary>The water it floats on.</summary>
        public WaterVolume Volume;
        /// <summary>The lowest and the highest its top goes.</summary>
        public float RestY, MaxY = 8f;
        /// <summary>The fastest it rises or sinks.</summary>
        public float MaxSpeed = 3f;
        /// <summary>How fast it changes speed: a rider stays on it (the grip is 6 units a second per tick).</summary>
        public float Acceleration = 30f;
        /// <summary>Its top rides this far above the surface.</summary>
        public float Freeboard = 0.1f;
        /// <summary>How far the top is above the platform's origin (a made slab: half its height).</summary>
        public float? TopOffset;
    }

    /// <summary>
    /// A cork on the water (LEVELS 2.2, Level 13): its top follows the surface of a water volume between a
    /// rest height and a maximum, at a limited speed, and carries the player up with it.
    /// </summary>
    public sealed class FloatPlatform : Gadget
    {
        const float Arrive = 1e-3f;

        readonly FloatPlatformOptions options;
        readonly Mover mover;
        readonly Collider[] colliders;
        readonly float topOffset;
        float top, speed;
        bool wasAtRest = true, wasAtTop;

        public FloatPlatform(LevelContext ctx, FloatPlatformOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            if (options.Volume == null) throw new ArgumentException("A float platform needs its water volume.", nameof(options));
            GameObject platform = options.Platform;
            if (platform == null)
            {
                platform = new GameObject("Float " + Name);
                platform.AddComponent<BoxCollider>().size = options.Size;
                float bevel = Mathf.Min(options.Size.x, Mathf.Min(options.Size.y, options.Size.z)) * 0.08f;
                GadgetKit.Visual(platform.transform, "Visual", GadgetKit.RoundedBoxMesh(options.Size, bevel), Materials.Toy(ToyRecipe.PlainProp, Palette.Kraft));
                topOffset = options.TopOffset ?? options.Size.y * 0.5f;
            }
            else
            {
                topOffset = options.TopOffset ?? 0f;
            }
            top = Mathf.Clamp(Target(), options.RestY, options.MaxY);
            mover = ctx.AddKinematic(platform, PositionAt(top));
            colliders = platform.GetComponentsInChildren<Collider>(true);
            wasAtRest = AtRest;
            wasAtTop = AtTop;
        }

        public Mover Mover => mover;
        /// <summary>Height of the platform's top.</summary>
        public float TopY => top;
        public bool AtRest => top <= options.RestY + Arrive;
        public bool AtTop => top >= options.MaxY - Arrive;
        public float Speed => speed;

        /// <summary>Inside the tick.</summary>
        public event Action FloatLeft;
        public event Action FloatArrived;

        // Its top rides the freeboard above the surface; with no water under it, it lies at rest.
        float Target() => options.Volume.Depth <= 1e-4f
            ? options.RestY
            : Mathf.Clamp(options.Volume.SurfaceY + options.Freeboard, options.RestY, options.MaxY);

        Vector3 PositionAt(float topY) => new Vector3(options.Position.x, topY - topOffset, options.Position.y);

        protected override void Tick(float dt)
        {
            float target = Target();
            float distance = target - top;
            if (Mathf.Abs(distance) > 1e-5f || Mathf.Abs(speed) > 1e-5f)
            {
                // Toward the target, never faster than MaxSpeed, never changing speed abruptly, and slowing
                // down in time to stop on it.
                // (It brakes at half of what it can, so that the last step is a small one.)
                float wanted = Mathf.Sign(distance) * Mathf.Min(options.MaxSpeed, Mathf.Sqrt(options.Acceleration * Mathf.Abs(distance)));
                speed = Mathf.MoveTowards(speed, wanted, options.Acceleration * dt);
                float step = speed * dt;
                if (Mathf.Abs(step) >= Mathf.Abs(distance) && Mathf.Sign(step) == Mathf.Sign(distance))
                {
                    step = distance;
                    speed = 0f;
                }
                float next = Mathf.Clamp(top + step, options.RestY, options.MaxY);
                Vector3 position = PositionAt(next);
                // Sinking onto a player underneath: wait (the crush guard).
                if (next < top && GadgetKit.WouldCrush(Game, mover.Transform, colliders, position, mover.Rotation))
                {
                    speed = 0f;
                }
                else
                {
                    top = next;
                    mover.MoveTo(position);
                }
            }

            bool atRest = AtRest, atTop = AtTop;
            if (wasAtRest && !atRest)
            {
                FloatLeft?.Invoke();
                Game.Events.RaiseFloatLeft(Event(mover.Position));
            }
            if (!wasAtTop && atTop)
            {
                FloatArrived?.Invoke();
                Game.Events.RaiseFloatArrived(Event(mover.Position));
            }
            wasAtRest = atRest;
            wasAtTop = atTop;
        }

        public override void Reset()
        {
            base.Reset();
            top = Mathf.Clamp(Target(), options.RestY, options.MaxY);
            speed = 0f;
            mover.Teleport(PositionAt(top), mover.Rotation);
            wasAtRest = AtRest;
            wasAtTop = AtTop;
        }
    }
}
