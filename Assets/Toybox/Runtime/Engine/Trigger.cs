using System;
using System.Collections.Generic;
using UnityEngine;

namespace Toybox.Engine
{
    /// <summary>
    /// A sensor volume. It is evaluated with an overlap query after every physics step and the result is
    /// diffed against the previous tick, so it works without Play Mode and without physics callbacks.
    /// OnEnter / OnExit run inside the tick (level logic); game.Events gets the same notifications at the
    /// end of the tick (presentation). A held prop is not sensed until it is dropped, and a prop that is
    /// removed from the level leaves at once.
    /// </summary>
    public sealed class Trigger
    {
        static readonly Collider[] Buffer = new Collider[256];
        static readonly Comparison<Prop> ById = (a, b) => a.Id.CompareTo(b.Id);

        readonly List<Prop> inside = new List<Prop>();
        readonly List<Prop> now = new List<Prop>();

        public string Name { get; }
        public Volume Volume { get; }
        public GameObject GameObject { get; }
        /// <summary>
        /// Pose of the volume. Parent it to a moving object to make the trigger follow. If that object is
        /// destroyed (a prop removed from the level), the trigger goes with it: see <see cref="Removed"/>.
        /// </summary>
        public Transform Transform { get; }
        /// <summary>A disabled trigger senses nothing; whatever was inside leaves.</summary>
        public bool Enabled { get; set; } = true;
        public bool SensesPlayer { get; set; } = true;
        public bool SensesProps { get; set; } = true;
        /// <summary>
        /// True once the trigger is gone: it was removed through LevelContext.RemoveTrigger, or its
        /// GameObject was destroyed together with whatever it was parented to.
        /// </summary>
        public bool Removed { get; private set; }

        public bool PlayerInside { get; private set; }
        /// <summary>Props touching the volume, ordered by Prop.Id. Never contains a removed prop.</summary>
        public IReadOnlyList<Prop> PropsInside => inside;
        public bool Occupied => PlayerInside || inside.Count > 0;

        public event Action<TriggerEvent> OnEnter;
        public event Action<TriggerEvent> OnExit;

        internal Trigger(string name, Volume volume, Transform parent, Vector3 position, Quaternion rotation)
        {
            Name = name;
            Volume = volume;
            GameObject = new GameObject("Trigger " + name) { layer = Layers.Trigger };
            Transform = GameObject.transform;
            Transform.SetParent(parent, false);
            Transform.SetPositionAndRotation(position, rotation);
        }

        public bool Contains(Prop prop) => inside.Contains(prop);

        internal void Evaluate(Game game)
        {
            if (Removed) return;
            // Destroyed along with its parent: everything inside leaves, and the Game drops the trigger.
            bool alive = GameObject != null;

            bool player = false;
            now.Clear();
            if (alive && Enabled)
            {
                int count = Volume.Overlap(game.PhysicsScene, Transform.position, Transform.rotation, Buffer, Layers.SensedMask);
                Collider playerCollider = game.Player.Collider;
                for (int i = 0; i < count; i++)
                {
                    Collider collider = Buffer[i];
                    if (collider == playerCollider)
                    {
                        player = SensesPlayer;
                        continue;
                    }
                    if (!SensesProps) continue;
                    Prop prop = PropRef.Of(collider);
                    if (prop != null && !prop.Removed && !now.Contains(prop)) now.Add(prop);
                }
            }

            // Handlers are level code: they may remove props (which shortens both lists, see Forget) or
            // disable this trigger. Every loop therefore re-checks its bounds and the state it relies on.
            if (PlayerInside && !player)
            {
                PlayerInside = false;
                Notify(game, false, null);
            }
            for (int i = inside.Count - 1; i >= 0; i--)
            {
                if (i >= inside.Count) continue;
                Prop prop = inside[i];
                if (now.Contains(prop)) continue;
                inside.RemoveAt(i);
                Notify(game, false, prop);
            }
            if (!PlayerInside && player)
            {
                PlayerInside = true;
                Notify(game, true, null);
            }
            if (now.Count > 1) now.Sort(ById);
            for (int i = 0; i < now.Count; i++)
            {
                Prop prop = now[i];
                if (prop.Removed || inside.Contains(prop)) continue;
                inside.Add(prop);
                Notify(game, true, prop);
            }
            if (inside.Count > 1) inside.Sort(ById);

            if (!alive) Removed = true;
        }

        /// <summary>The prop is being removed from the level: it leaves now, not at the next evaluation.</summary>
        internal void Forget(Game game, Prop prop)
        {
            now.Remove(prop);
            if (inside.Remove(prop)) Notify(game, false, prop);
        }

        /// <summary>Removal through the level: whatever is inside leaves, then the volume is destroyed.</summary>
        internal void Remove(Game game)
        {
            if (Removed) return;
            Removed = true;
            now.Clear();
            if (PlayerInside)
            {
                PlayerInside = false;
                Notify(game, false, null);
            }
            for (int i = inside.Count - 1; i >= 0; i--)
            {
                if (i >= inside.Count) continue;
                Prop prop = inside[i];
                inside.RemoveAt(i);
                Notify(game, false, prop);
            }
            if (GameObject != null) Sim.Destroy(GameObject);
        }

        void Notify(Game game, bool entered, Prop prop)
        {
            var e = new TriggerEvent { Trigger = this, IsPlayer = prop == null, Prop = prop };
            if (entered)
            {
                OnEnter?.Invoke(e);
                game.Events.RaiseTriggerEntered(e);
            }
            else
            {
                OnExit?.Invoke(e);
                game.Events.RaiseTriggerExited(e);
            }
        }
    }
}
