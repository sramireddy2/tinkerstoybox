using System;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    public sealed class HazardZoneOptions
    {
        public string Name = "Hazard";
        /// <summary>The region the player's feet must not stay in.</summary>
        public Zone Shape;
        /// <summary>Seconds the feet have to be inside, without interruption, before the player is caught.</summary>
        public float Delay = 0.1f;
        /// <summary>What happens to a caught player. Default: back to the checkpoint.</summary>
        public Action OnCaught;
    }

    /// <summary>
    /// A place the player must not be: the floor of a pit, the inside of a bell jar. Feet inside for
    /// <see cref="HazardZoneOptions.Delay"/> and the player is sent back (they keep their size and whatever
    /// they hold). Props are ignored - a <see cref="PropLeash"/> looks after those.
    /// </summary>
    public sealed class HazardZone : Gadget
    {
        readonly HazardZoneOptions options;
        readonly int delayTicks;
        int inside;

        public HazardZone(LevelContext ctx, HazardZoneOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            delayTicks = Ticks(options.Delay, 1);
        }

        public Zone Shape => options.Shape;
        /// <summary>True while the player's feet are inside.</summary>
        public bool PlayerInside => inside > 0;
        /// <summary>How many times it has caught the player.</summary>
        public int Catches { get; private set; }

        /// <summary>Inside the tick, right before the player is sent back.</summary>
        public event Action PlayerCaught;

        protected override void Tick(float dt)
        {
            Vector3 feet = Game.Player.Position;
            if (!options.Shape.Contains(feet))
            {
                inside = 0;
                return;
            }
            if (++inside < delayTicks) return;
            inside = 0;
            Catches++;
            PlayerCaught?.Invoke();
            Game.Events.RaiseHazardCaught(Event(feet));
            if (options.OnCaught != null) options.OnCaught();
            else Game.Player.Respawn();
        }

        public override void Reset()
        {
            base.Reset();
            inside = 0;
        }
    }
}
