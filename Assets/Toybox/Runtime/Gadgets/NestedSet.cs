using System;
using System.Collections.Generic;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    public sealed class NestedSetOptions
    {
        public string Name = "Nested Set";
        /// <summary>The toys, outermost first, each at the pose and scale it has inside the one before.</summary>
        public IList<Prop> Props;
    }

    /// <summary>
    /// Matryoshka toys without nested colliders (LEVELS 2.3, Level 10): every toy but the first is hidden -
    /// inactive, so it has no collider and cannot be grabbed - until the one before it is picked up for the
    /// first time. Then it is simply there, at rest, where the level put it. Because a held toy keeps its
    /// footprint on screen, the reveal happens behind it.
    /// </summary>
    public sealed class NestedSet : Gadget
    {
        readonly Prop[] props;
        readonly bool[] revealed;
        readonly bool[] grabbable;
        readonly Vector3[] positions;
        readonly Quaternion[] rotations;
        readonly float[] scales;

        public NestedSet(LevelContext ctx, NestedSetOptions options) : base(ctx, options?.Name)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.Props == null || options.Props.Count == 0) throw new ArgumentException("A nested set needs props.", nameof(options));
            int n = options.Props.Count;
            props = new Prop[n];
            options.Props.CopyTo(props, 0);
            revealed = new bool[n];
            grabbable = new bool[n];
            positions = new Vector3[n];
            rotations = new Quaternion[n];
            scales = new float[n];
            for (int i = 0; i < n; i++)
            {
                grabbable[i] = props[i].Grabbable;
                positions[i] = props[i].Position;
                rotations[i] = props[i].Rotation;
                scales[i] = props[i].Scale;
            }
            revealed[0] = true;
            for (int i = 1; i < n; i++) Hide(i);
        }

        public int Count => props.Length;
        /// <summary>How many of the toys are out (1 at the start).</summary>
        public int RevealedCount { get; private set; } = 1;
        public bool IsRevealed(int index) => revealed[index];
        public Prop this[int index] => props[index];

        /// <summary>Inside the tick: the toy at this index (1 or more) has appeared.</summary>
        public event Action<int> Revealed;

        protected override void Tick(float dt)
        {
            for (int i = 0; i + 1 < props.Length; i++)
            {
                if (!revealed[i] || revealed[i + 1]) continue;
                if (props[i].Removed || !props[i].Held) continue;
                Reveal(i + 1);
            }
        }

        void Hide(int i)
        {
            Prop prop = props[i];
            if (prop.Removed) return;
            revealed[i] = false;
            prop.Grabbable = false;
            prop.GameObject.SetActive(false);
        }

        void Reveal(int i)
        {
            Prop prop = props[i];
            revealed[i] = true;
            RevealedCount++;
            if (prop.Removed) return;
            prop.GameObject.SetActive(true);
            prop.SetScale(scales[i]);
            prop.SetPose(positions[i], rotations[i]);
            prop.Grabbable = grabbable[i];
            Revealed?.Invoke(i);
            Game.Events.RaiseNestRevealed(Event(prop.Center, prop, 0f, 0f, i));
        }

        /// <summary>Puts every toy that is not in the player's hand back inside: only the first is out again.</summary>
        public override void Reset()
        {
            base.Reset();
            RevealedCount = 1;
            for (int i = 1; i < props.Length; i++)
            {
                if (props[i].Removed) continue;
                if (props[i].Held)
                {
                    // It stays out; so does everything before it.
                    for (int k = 1; k <= i; k++)
                        if (!revealed[k]) Reveal(k);
                    RevealedCount = Mathf.Max(RevealedCount, i + 1);
                    continue;
                }
                Hide(i);
            }
            RevealedCount = 0;
            for (int i = 0; i < props.Length; i++)
                if (revealed[i]) RevealedCount++;
        }
    }
}
