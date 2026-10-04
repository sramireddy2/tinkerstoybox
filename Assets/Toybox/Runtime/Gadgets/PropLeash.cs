using System;
using System.Collections.Generic;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    public enum LeashAction
    {
        /// <summary>Back to the pose and the scale the level gave it.</summary>
        Respawn,
        /// <summary>Out of the return port, at the scale it has.</summary>
        Eject,
    }

    public sealed class PropLeashOptions
    {
        public string Name = "Leash";
        /// <summary>The props on the leash. Leave null to use <see cref="Tag"/>.</summary>
        public IList<Prop> Props;
        /// <summary>Every prop with this tag is on the leash (also props added later).</summary>
        public string Tag;
        /// <summary>If any are given, the prop's centre must be inside one of them.</summary>
        public IList<Zone> Allowed;
        /// <summary>The prop's centre must not be inside any of these.</summary>
        public IList<Zone> Forbidden;
        /// <summary>Seconds a prop may stay out of bounds before it is returned.</summary>
        public float Grace = 2f;
        /// <summary>0: out of bounds counts at any speed. Otherwise only while the prop is slower than this.</summary>
        public float RestSpeed;
        /// <summary>While this says true nothing is returned ("the player is standing up there too").</summary>
        public Func<bool> Unless;
        public LeashAction Action = LeashAction.Respawn;
        /// <summary>Where <see cref="LeashAction.Eject"/> sends the prop.</summary>
        public ReturnPort Port;
        /// <summary>Runs right before a prop is returned (a sponge gives its water back).</summary>
        public Action<Prop> OnReturn;
    }

    /// <summary>
    /// No strand, no restart (LEVELS 0.3): a toy that ends up where the player cannot see or reach it is
    /// brought back. Per prop that is neither held nor driven an out-of-bounds timer runs; when it reaches
    /// the grace time the prop respawns (origin pose and scale) or is ejected through a return port.
    /// </summary>
    public sealed class PropLeash : Gadget
    {
        readonly PropLeashOptions options;
        readonly Dictionary<Prop, int> timers = new Dictionary<Prop, int>();
        readonly List<Prop> scratch = new List<Prop>();
        readonly int graceTicks;

        public PropLeash(LevelContext ctx, PropLeashOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            if (options.Action == LeashAction.Eject && options.Port == null)
                throw new ArgumentException("A leash that ejects needs a ReturnPort.", nameof(options));
            graceTicks = Ticks(options.Grace, 1);
        }

        /// <summary>How many props it has returned.</summary>
        public int Returns { get; private set; }

        /// <summary>Inside the tick, after the prop was returned.</summary>
        public event Action<Prop> PropReturned;

        /// <summary>Is the prop out of bounds where it is now?</summary>
        public bool OutOfBounds(Prop prop)
        {
            Vector3 centre = prop.Center;
            if (options.Forbidden != null)
                for (int i = 0; i < options.Forbidden.Count; i++)
                    if (options.Forbidden[i].Contains(centre)) return true;
            if (options.Allowed == null || options.Allowed.Count == 0) return false;
            for (int i = 0; i < options.Allowed.Count; i++)
                if (options.Allowed[i].Contains(centre)) return false;
            return true;
        }

        /// <summary>Seconds the prop has been out of bounds (0 if it is not).</summary>
        public float TimeOut(Prop prop) => timers.TryGetValue(prop, out int ticks) ? ticks * Sim.Dt : 0f;

        protected override void Tick(float dt)
        {
            scratch.Clear();
            if (options.Props != null)
            {
                for (int i = 0; i < options.Props.Count; i++) scratch.Add(options.Props[i]);
            }
            else if (options.Tag != null)
            {
                IReadOnlyList<Prop> all = Game.Props;
                for (int i = 0; i < all.Count; i++)
                    if (all[i].HasTag(options.Tag)) scratch.Add(all[i]);
            }

            bool excused = options.Unless != null && options.Unless();
            for (int i = 0; i < scratch.Count; i++)
            {
                Prop prop = scratch[i];
                if (prop == null) continue;
                if (excused || !GadgetKit.IsLoose(prop) || prop.Driven || prop.Frozen || !OutOfBounds(prop) ||
                    (options.RestSpeed > 0f && prop.Velocity.sqrMagnitude >= options.RestSpeed * options.RestSpeed))
                {
                    timers.Remove(prop);
                    continue;
                }
                timers.TryGetValue(prop, out int ticks);
                ticks++;
                if (ticks < graceTicks)
                {
                    timers[prop] = ticks;
                    continue;
                }
                timers.Remove(prop);
                Return(prop);
            }
        }

        void Return(Prop prop)
        {
            Vector3 from = prop.Center;
            options.OnReturn?.Invoke(prop);
            if (options.Action == LeashAction.Eject) options.Port.Eject(prop);
            else prop.Respawn();
            Returns++;
            PropReturned?.Invoke(prop);
            Game.Events.RaiseLeashReturned(Event(from, prop));
        }

        public override void Reset()
        {
            base.Reset();
            timers.Clear();
        }
    }
}
