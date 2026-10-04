using System.Collections.Generic;
using Toybox.Engine;
using Toybox.Platform;
using UnityEngine;

namespace Toybox.Render
{
    /// <summary>
    /// "Candy plus rim plus pool means you can lift this, always" (ART_BIBLE 2.5 rule 4): a toy that stops
    /// being grabbable - the thimble once it is seated, a crate a plate has locked - loses its kicker rim
    /// over 300 ms, and gets it back the same way if it can be lifted again. (Its pool goes the same way
    /// in <see cref="PoolSystem"/>.) A prop that is not grabbable when it is first seen has no rim from
    /// the start.
    ///
    /// The rim is the material's <c>_Rim</c> scaled per renderer through its property block, next to
    /// whatever else the block holds (a sweep, a squash, a signal's emission).
    /// </summary>
    [Presenter(252)]
    public sealed class RimFade : IPresenter
    {
        public const float Seconds = 0.3f;

        static readonly int RimId = Shader.PropertyToID("_Rim");

        sealed class Entry
        {
            public readonly List<Renderer> Renderers = new List<Renderer>();
            public readonly List<float> Rims = new List<float>();
            /// <summary>0 no rim .. 1 the material's own.</summary>
            public float Shown = 1f;
            public float Written = 1f;
        }

        readonly Dictionary<Prop, Entry> entries = new Dictionary<Prop, Entry>();
        readonly List<Prop> gone = new List<Prop>();
        readonly List<Renderer> scratch = new List<Renderer>();
        MaterialPropertyBlock block;
        Game game;
        bool active;
        // The first frame that sees a level: what cannot be lifted then never had a rim.
        bool firstLook = true;

        /// <summary>Props whose rim is, or has been, held below their material's.</summary>
        public int Count => entries.Count;
        /// <summary>How much of its rim a prop shows right now, 0..1.</summary>
        public float ShownOf(Prop prop) => prop != null && entries.TryGetValue(prop, out Entry entry) ? entry.Shown : 1f;

        public void Attach(Game game, PresentationContext context)
        {
            this.game = game;
            // The plain look has no rim to fade.
            if (context.Plain) return;
            active = true;
            block = new MaterialPropertyBlock();
            game.Events.LevelUnloading += OnLevelUnloading;
            game.Events.LevelLoaded += OnLevelLoaded;
        }

        public void Frame(float dt, float alpha)
        {
            if (!active || game.Level == null) return;
            IReadOnlyList<Prop> props = game.Props;
            for (int i = 0; i < props.Count; i++)
            {
                Prop prop = props[i];
                if (prop.Removed) continue;
                bool liftable = prop.Grabbable;
                if (!entries.TryGetValue(prop, out Entry entry))
                {
                    if (liftable) continue;
                    entry = Track(prop);
                    if (entry == null) continue;
                    // Not liftable at first sight (a set piece in candy, a toy that starts locked): no fade.
                    if (firstLook) entry.Shown = 0f;
                }
                entry.Shown = Mathf.MoveTowards(entry.Shown, liftable ? 1f : 0f, dt / Seconds);
                if (entry.Shown != entry.Written) Write(entry);
            }

            // Props that were removed take their entries with them.
            foreach (KeyValuePair<Prop, Entry> pair in entries)
                if (pair.Key.Removed) gone.Add(pair.Key);
            for (int i = 0; i < gone.Count; i++) entries.Remove(gone[i]);
            gone.Clear();
            firstLook = false;
        }

        public void Dispose()
        {
            if (!active) return;
            active = false;
            game.Events.LevelUnloading -= OnLevelUnloading;
            game.Events.LevelLoaded -= OnLevelLoaded;
            Restore();
        }

        void OnLevelUnloading(LevelEvent e) => Restore();

        void OnLevelLoaded(LevelEvent e) => firstLook = true;

        // The level's objects are about to go (or this presenter is): give every renderer its rim back.
        void Restore()
        {
            foreach (KeyValuePair<Prop, Entry> pair in entries)
            {
                pair.Value.Shown = 1f;
                Write(pair.Value);
            }
            entries.Clear();
        }

        // Null for a prop none of whose materials has a rim: there is nothing to fade.
        Entry Track(Prop prop)
        {
            prop.GameObject.GetComponentsInChildren(true, scratch);
            Entry entry = null;
            for (int i = 0; i < scratch.Count; i++)
            {
                Material material = scratch[i].sharedMaterial;
                if (material == null || !material.HasProperty(RimId)) continue;
                float rim = material.GetFloat(RimId);
                if (rim <= 0f) continue;
                entry ??= new Entry();
                entry.Renderers.Add(scratch[i]);
                entry.Rims.Add(rim);
            }
            scratch.Clear();
            if (entry != null) entries[prop] = entry;
            return entry;
        }

        void Write(Entry entry)
        {
            entry.Written = entry.Shown;
            for (int i = 0; i < entry.Renderers.Count; i++)
            {
                Renderer renderer = entry.Renderers[i];
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block);
                block.SetFloat(RimId, entry.Rims[i] * entry.Shown);
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
