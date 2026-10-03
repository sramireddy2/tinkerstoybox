using UnityEngine;

namespace Toybox.Engine
{
    /// <summary>The level's goal: completes the level while the player is inside and it is unlocked.</summary>
    public sealed class Exit
    {
        readonly Game game;
        bool locked;

        public Trigger Trigger { get; }
        public string Name => Trigger.Name;
        public Vector3 Position => Trigger.Transform.position;

        /// <summary>A locked exit does not complete the level. Changing it raises ExitLockChanged.</summary>
        public bool Locked
        {
            get => locked;
            set
            {
                if (locked == value) return;
                locked = value;
                game.Events.RaiseExitLockChanged(new ExitEvent { Exit = this, Locked = value });
            }
        }

        internal Exit(Game game, Trigger trigger)
        {
            this.game = game;
            Trigger = trigger;
        }

        /// <summary>Locks the exit and returns it, so `ctx.AddExit(...).Lock()` reads in one line.</summary>
        public Exit Lock()
        {
            Locked = true;
            return this;
        }

        public Exit Unlock()
        {
            Locked = false;
            return this;
        }

        internal void Evaluate(Game game)
        {
            if (!Locked && Trigger.PlayerInside) game.CompleteLevel();
        }
    }
}
