using System;
using Toybox.Art;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    public sealed class WaterVolumeOptions
    {
        public string Name = "Water";
        /// <summary>Where the water is, seen from above (a cylinder or a box; only its footprint matters).</summary>
        public Zone Footprint;
        /// <summary>Height of the bottom.</summary>
        public float FloorY;
        /// <summary>Surface area: a unit of volume raises the surface by 1 / Area.</summary>
        public float Area = 1f;
        /// <summary>How much water it starts with.</summary>
        public float Volume;
        /// <summary>The surface never stands higher; what does not fit goes to <see cref="OverflowTo"/> (or is lost).</summary>
        public float MaxSurfaceY = float.MaxValue;
        public WaterVolume OverflowTo;
        /// <summary>A steady drain into another volume, in units per second.</summary>
        public WaterVolume LeakTo;
        public float LeakRate;
        /// <summary>The player wades up to this deep (times their scale). Deeper for <see cref="SweepDelay"/> and they are swept away.</summary>
        public float WadeDepth = 0.8f;
        public float SweepDelay = 0.3f;
        /// <summary>What happens to a swept player. Default: back to the checkpoint.</summary>
        public Action OnSwept;
        /// <summary>Draws the surface: one flat mesh in the water material.</summary>
        public bool Visual = true;
    }

    /// <summary>
    /// Water as bookkeeping, not simulation (LEVELS 2.3, Level 13): a footprint, a floor height and a
    /// number. The surface is flat at FloorY + Volume / Area. Water moves between volumes and the sponge by
    /// Add and Take, which return what actually moved, so the total is conserved.
    /// </summary>
    public sealed class WaterVolume : Gadget
    {
        readonly WaterVolumeOptions options;
        readonly int sweepTicks;
        Transform surface;
        // A double: thousands of small transfers per level have to add up to what went in.
        double volume;
        float maxSurfaceY;
        double lastReported;
        int deepTicks;

        public WaterVolume(LevelContext ctx, WaterVolumeOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            if (options.Area <= 0f) throw new ArgumentException("A water volume needs a positive Area.", nameof(options));
            volume = Mathf.Max(0f, options.Volume);
            maxSurfaceY = options.MaxSurfaceY;
            lastReported = volume;
            sweepTicks = Ticks(options.SweepDelay, 1);
            if (options.Visual) BuildVisual();
        }

        public Zone Footprint => options.Footprint;
        public float FloorY => options.FloorY;
        public float Area => options.Area;
        public float Volume => (float)volume;
        public float Depth => (float)(volume / options.Area);
        public float SurfaceY => options.FloorY + Depth;
        /// <summary>The most it holds at its current <see cref="MaxSurfaceY"/>.</summary>
        public float Capacity => maxSurfaceY >= float.MaxValue * 0.5f ? float.MaxValue : Mathf.Max(0f, (maxSurfaceY - options.FloorY) * options.Area);

        /// <summary>The highest the surface may stand. A level changes it (a flap that opens lets the tower run empty).</summary>
        public float MaxSurfaceY
        {
            get => maxSurfaceY;
            set => maxSurfaceY = value;
        }

        /// <summary>Inside the tick: the surface height, whenever the volume has changed.</summary>
        public event Action<float> LevelChanged;
        /// <summary>The amount passed on this tick because the volume is full.</summary>
        public event Action<float> Overflowing;
        public event Action PlayerSwept;

        /// <summary>Is the point over this water (inside its footprint)?</summary>
        public bool Over(Vector3 point) => options.Footprint.ContainsXZ(point);

        /// <summary>Pours water in. Returns how much went in (all of it: the excess runs over on the next tick).</summary>
        public float Add(float amount)
        {
            if (amount <= 0f) return 0f;
            volume += amount;
            return amount;
        }

        /// <summary>Takes water out. Returns how much there was to take.</summary>
        public float Take(float amount)
        {
            if (amount <= 0f) return 0f;
            double taken = System.Math.Min((double)amount, volume);
            volume -= taken;
            return (float)taken;
        }

        /// <summary>Sets the volume outright (a level resetting its water).</summary>
        public void Set(float amount) => volume = Mathf.Max(0f, amount);

        protected override void Tick(float dt)
        {
            if (options.LeakTo != null && options.LeakRate > 0f && volume > 0f)
                options.LeakTo.Add(Take(options.LeakRate * dt));

            float capacity = Capacity;
            if (volume > capacity)
            {
                float excess = (float)(volume - capacity);
                volume = capacity;
                options.OverflowTo?.Add(excess);
                Overflowing?.Invoke(excess);
                Game.Events.RaiseWaterOverflowing(Event(SurfacePoint(), null, excess));
            }

            if (System.Math.Abs(volume - lastReported) > 1e-6)
            {
                lastReported = volume;
                LevelChanged?.Invoke(SurfaceY);
                Game.Events.RaiseWaterLevelChanged(Event(SurfacePoint(), null, SurfaceY, Volume));
            }

            Player player = Game.Player;
            Vector3 feet = player.Position;
            bool deep = Over(feet) && feet.y >= options.FloorY - 0.5f && SurfaceY - feet.y > options.WadeDepth * player.Scale;
            deepTicks = deep ? deepTicks + 1 : 0;
            if (deepTicks >= sweepTicks)
            {
                deepTicks = 0;
                PlayerSwept?.Invoke();
                Game.Events.RaiseWaterSwept(Event(feet));
                if (options.OnSwept != null) options.OnSwept();
                else player.Respawn();
            }

            if (surface != null)
            {
                bool wet = Depth > 1e-3f;
                if (surface.gameObject.activeSelf != wet) surface.gameObject.SetActive(wet);
                if (wet) surface.position = SurfacePoint();
            }
        }

        Vector3 SurfacePoint() => new Vector3(options.Footprint.Center.x, SurfaceY, options.Footprint.Center.z);

        public override void Reset()
        {
            base.Reset();
            volume = Mathf.Max(0f, options.Volume);
            maxSurfaceY = options.MaxSurfaceY;
            deepTicks = 0;
        }

        void BuildVisual()
        {
            Zone footprint = options.Footprint;
            if (!footprint.IsValid) return;
            Mesh mesh;
            Quaternion rotation = Quaternion.identity;
            if (footprint.Shape == ZoneShape.Box)
            {
                var size = new Vector2(footprint.Size.x, footprint.Size.z);
                mesh = MeshKit.Cached(MeshKit.Key("WaterSheet", size.x, size.y), () => MeshKit.Grid(size, 1, 1));
                rotation = footprint.Rotation;
            }
            else
            {
                float radius = footprint.Radius;
                mesh = MeshKit.Cached(MeshKit.Key("WaterDisc", radius), () => MeshKit.Cylinder(radius, 0.01f, 32));
            }
            var go = new GameObject("Water " + Name);
            surface = go.transform;
            surface.SetParent(Ctx.Root, false);
            surface.SetPositionAndRotation(SurfacePoint(), rotation);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Materials.Water(Ctx.Dip);
            go.SetActive(Depth > 1e-3f);
        }
    }
}
