using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Platform;
using UnityEngine;

namespace Toybox.Render
{
    /// <summary>
    /// The squash of ART_BIBLE 9.5, the other half of the release: the first time a toy that was let go
    /// hits something, it flattens by its recipe's amount (sponge 18%, rubber 14%, plastic 6%, everything
    /// else nothing) and recovers over 180 ms with one overshoot. It is drawn by the toy shader's vertex
    /// stage through <c>_SquashA</c> on the renderers' property blocks; colliders never change, and the
    /// shader ignores it inside the sticker pass - there is no squash in the hand.
    ///
    /// Like <see cref="HeldLook"/> it does nothing with the plain look, whose materials know no squash.
    /// </summary>
    [Presenter(255)]
    public sealed class ToySquash : IPresenter
    {
        public const float Seconds = 0.180f;
        /// <summary>An impact at this speed or faster squashes by the recipe's full amount; slower ones by less.</summary>
        public const float FullSpeed = 4f;
        const int MaxSquashes = 8;

        static readonly int SquashId = Shader.PropertyToID("_SquashA");

        readonly List<Prop> released = new List<Prop>();
        readonly List<Entry> entries = new List<Entry>();
        readonly Stack<Entry> pool = new Stack<Entry>();
        MaterialPropertyBlock block;
        Game game;
        bool active;

        sealed class Entry
        {
            public Prop Prop;
            public float Amount, Age;
            public readonly List<Renderer> Renderers = new List<Renderer>();
        }

        /// <summary>Squashes running right now.</summary>
        public int Count => entries.Count;

        public void Attach(Game game, PresentationContext context)
        {
            this.game = game;
            if (context.Plain) return;
            active = true;
            block = new MaterialPropertyBlock();
            game.Events.PropDropped += OnDropped;
            game.Events.PropGrabbed += OnGrabbed;
            game.Events.PropImpact += OnImpact;
            game.Events.LevelUnloading += OnLevelUnloading;
        }

        public void Frame(float dt, float alpha)
        {
            if (!active) return;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry entry = entries[i];
                entry.Age += dt;
                if (entry.Prop.Removed || entry.Prop.Held || entry.Age >= Seconds)
                {
                    Remove(i);
                    continue;
                }
                Write(entry, Shape(entry.Age / Seconds) * entry.Amount);
            }
        }

        public void Dispose()
        {
            if (!active) return;
            active = false;
            game.Events.PropDropped -= OnDropped;
            game.Events.PropGrabbed -= OnGrabbed;
            game.Events.PropImpact -= OnImpact;
            game.Events.LevelUnloading -= OnLevelUnloading;
            Clear();
        }

        /// <summary>
        /// The squash as a fraction of its amount, <paramref name="t"/> in 0..1 through the recovery: full at
        /// the impact, through zero, one stretch the other way (about a ninth), and back to rest.
        /// </summary>
        public static float Shape(float t)
        {
            t = Mathf.Clamp01(t);
            return (1f - t) * (1f - t) * Mathf.Cos(1.5f * Mathf.PI * t);
        }

        void OnDropped(PropHoldEvent e)
        {
            if (e.Prop == null || e.Prop.Removed || released.Contains(e.Prop)) return;
            released.Add(e.Prop);
        }

        void OnGrabbed(PropHoldEvent e) => released.Remove(e.Prop);

        void OnImpact(PropImpactEvent e)
        {
            // The first impact after a release, and only that one.
            if (!released.Remove(e.Prop)) return;
            Prop prop = e.Prop;
            if (prop.Removed || prop.Held || entries.Count >= MaxSquashes) return;
            ToyInfo info = ToyInfo.Of(prop.GameObject);
            float amount = (info != null ? info.Recipe.Squash : 0f) * Mathf.Clamp01(e.Speed / FullSpeed);
            if (amount <= 0f) return;

            Entry entry = pool.Count > 0 ? pool.Pop() : new Entry();
            entry.Prop = prop;
            entry.Amount = amount;
            entry.Age = 0f;
            prop.GameObject.GetComponentsInChildren(true, entry.Renderers);
            entries.Add(entry);
            Write(entry, amount);
        }

        void OnLevelUnloading(LevelEvent e)
        {
            Clear();
            released.Clear();
        }

        void Clear()
        {
            for (int i = entries.Count - 1; i >= 0; i--) Remove(i);
        }

        void Remove(int index)
        {
            Entry entry = entries[index];
            Write(entry, 0f);
            entry.Prop = null;
            entry.Renderers.Clear();
            entries.RemoveAt(index);
            pool.Push(entry);
        }

        // (centre.x, pivot.y, centre.z, amount): the toy flattens toward the lowest point of what is drawn
        // and spreads about its middle, wherever it has got to since the impact.
        void Write(Entry entry, float amount)
        {
            Vector4 value = Vector4.zero;
            if (amount != 0f)
            {
                bool any = false;
                Bounds bounds = default;
                for (int i = 0; i < entry.Renderers.Count; i++)
                {
                    Renderer renderer = entry.Renderers[i];
                    if (renderer == null) continue;
                    if (any) bounds.Encapsulate(renderer.bounds);
                    else bounds = renderer.bounds;
                    any = true;
                }
                if (any) value = new Vector4(bounds.center.x, bounds.min.y, bounds.center.z, amount);
            }
            for (int i = 0; i < entry.Renderers.Count; i++)
            {
                Renderer renderer = entry.Renderers[i];
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block);
                block.SetVector(SquashId, value);
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
