using System;
using System.Collections.Generic;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    public sealed class SpongeOptions
    {
        public string Name = "Sponge";
        public Prop Prop;
        /// <summary>The water it can drink from and wring into. The first whose footprint contains its centre counts.</summary>
        public IList<WaterVolume> Volumes;
        /// <summary>Capacity = this x scale cubed (0.315: 90 % of the bulk of a 1 x 0.5 x 0.7 sponge).</summary>
        public float CapacityPerScale3 = 0.315f;
        /// <summary>Units of water per second it drinks.</summary>
        public float AbsorbRate = 150f;
        /// <summary>Units of water per second it gives back when it holds more than it can.</summary>
        public float SqueezeRate = 24f;
    }

    public enum SpongeActivity
    {
        Idle,
        Soaking,
        Wringing,
    }

    /// <summary>
    /// Size is capacity (LEVELS 2.3, Level 13): a toy that carries a quantity through a resize. Lying in
    /// water it drinks up to 0.315 x scale cubed; made small while full it wrings the surplus out into
    /// whatever volume it lies over. A held sponge neither soaks nor leaks: it is not in the world.
    /// </summary>
    public sealed class Sponge : Gadget
    {
        readonly SpongeOptions options;
        SpongeActivity activity;
        bool reportedFull;

        public Sponge(LevelContext ctx, SpongeOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            if (options.Prop == null) throw new ArgumentException("A sponge needs its prop.", nameof(options));
        }

        public Prop Prop => options.Prop;
        /// <summary>The water it holds.</summary>
        public float Stored { get; private set; }
        /// <summary>What it can hold at the scale it has now.</summary>
        public float Capacity => CapacityAt(options.Prop.Scale);
        /// <summary>Stored / Capacity; above 1 while it holds more than it can (about to wring).</summary>
        public float Saturation => Capacity > 0f ? Stored / Capacity : 0f;
        /// <summary>The same, clamped to 0..1: what the sponge looks like.</summary>
        public float Saturation01 => Mathf.Clamp01(Saturation);
        public SpongeActivity Activity => activity;
        /// <summary>The volume it lies over, or null.</summary>
        public WaterVolume Over { get; private set; }

        /// <summary>Inside the tick.</summary>
        public event Action Soaking;
        public event Action Wringing;
        /// <summary>It has drunk all it can.</summary>
        public event Action Full;
        /// <summary>It holds nothing any more.</summary>
        public event Action Dry;

        public float CapacityAt(float scale) => options.CapacityPerScale3 * scale * scale * scale;

        /// <summary>Gives everything it holds to a volume (or throws it away: null), at once.</summary>
        public void EmptyInto(WaterVolume volume)
        {
            if (Stored <= 0f) return;
            volume?.Add(Stored);
            Stored = 0f;
            reportedFull = false;
            SetActivity(SpongeActivity.Idle);
            Dry?.Invoke();
            Game.Events.RaiseSpongeDry(Event(Position()));
        }

        protected override void Tick(float dt)
        {
            Prop prop = options.Prop;
            Over = null;
            if (prop.Removed || prop.Held || !prop.GameObject.activeInHierarchy)
            {
                SetActivity(SpongeActivity.Idle);
                return;
            }

            Vector3 centre = prop.Center;
            if (options.Volumes != null)
            {
                for (int i = 0; i < options.Volumes.Count; i++)
                {
                    WaterVolume volume = options.Volumes[i];
                    if (volume == null || !volume.Over(centre)) continue;
                    Over = volume;
                    break;
                }
            }
            if (Over == null)
            {
                SetActivity(SpongeActivity.Idle);
                return;
            }

            float capacity = Capacity;
            if (Stored > capacity + 1e-4f)
            {
                // Too small for what it holds: the surplus goes into the water it lies over.
                float amount = Mathf.Min(options.SqueezeRate * dt, Stored - capacity);
                Stored -= amount;
                Over.Add(amount);
                reportedFull = false;
                SetActivity(SpongeActivity.Wringing);
                return;
            }

            GadgetKit.VerticalExtent(prop, out float lowest, out _);
            if (Stored < capacity - 1e-4f && lowest < Over.SurfaceY && Over.Volume > 0f)
            {
                float taken = Over.Take(Mathf.Min(options.AbsorbRate * dt, capacity - Stored));
                Stored += taken;
                if (taken > 0f)
                {
                    SetActivity(SpongeActivity.Soaking);
                    if (Stored >= capacity - 1e-4f) ReportFull();
                    return;
                }
            }
            SetActivity(SpongeActivity.Idle);
        }

        void ReportFull()
        {
            if (reportedFull) return;
            reportedFull = true;
            Full?.Invoke();
            Game.Events.RaiseSpongeFull(Event(Position(), options.Prop, Stored));
        }

        void SetActivity(SpongeActivity next)
        {
            if (activity == next) return;
            activity = next;
            if (next == SpongeActivity.Soaking)
            {
                Soaking?.Invoke();
                Game.Events.RaiseSpongeSoaking(Event(Position(), options.Prop, Stored));
            }
            else if (next == SpongeActivity.Wringing)
            {
                Wringing?.Invoke();
                Game.Events.RaiseSpongeWringing(Event(Position(), options.Prop, Stored));
            }
        }

        Vector3 Position() => options.Prop.Removed ? Vector3.zero : options.Prop.Center;

        /// <summary>Dry again (the water it held is gone: a level that resets its water sets the volumes too).</summary>
        public override void Reset()
        {
            base.Reset();
            Stored = 0f;
            reportedFull = false;
            activity = SpongeActivity.Idle;
        }
    }
}
