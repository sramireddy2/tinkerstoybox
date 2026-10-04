using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Platform;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Render
{
    /// <summary>
    /// The held-object rule as seen from the game (ART_BIBLE 8.1, 9.1 - 9.3): everything about a held toy
    /// that is not the render pass itself.
    ///
    /// - Grab: the toy stops casting a shadow (URP's shadow pass ignores the renderer's layer masks, so
    ///   the Held layer alone does not do it); the border pops 0 -> 6.5 -> 4.5 px in 110 ms and the peel
    ///   shadow slides out from under the toy.
    /// - Hold: the peel shadow breathes by a pixel; when the projected scale jumps by more than 5% in one
    ///   tick the border flashes 1.5 px wider for 80 ms.
    /// - Release: shadows are back in the same frame and the border is simply gone.
    /// - Focus: one diagonal glint sweep crosses the toy a click would take (300 ms), and every toy when
    ///   the level is completed.
    /// - The "High-visibility toys" setting: the rim of every toy times 1.6.
    ///
    /// It writes <see cref="StickerLook"/> for <see cref="StickerFeature"/>. The lighting globals of the toy
    /// shader belong to the lighting rig; where there is none, <see cref="ToyLookStandIn"/> fills them in.
    /// With the plain look this presenter does nothing at all - the sticker pass then draws the held toy
    /// on top without a border.
    ///
    /// It is not marked ProvidesLook although it is part of the look: it brings no light of its own, so
    /// its presence must not retire the plain look's sun. It checks context.Plain itself instead.
    /// </summary>
    [Presenter(250)]
    public sealed class HeldLook : IPresenter
    {
        /// <summary>The border's pop on grab: 0 to <see cref="PopPeak"/> and back to its resting width, in this many seconds.</summary>
        public const float PopSeconds = 0.110f;
        public const float PopPeak = 6.5f;
        /// <summary>A jump of the held toy to another surface: the border is this much wider for <see cref="FlashSeconds"/>.</summary>
        public const float FlashPx = 1.5f;
        public const float FlashSeconds = 0.080f;
        /// <summary>The change of projected scale within one tick that counts as a jump.</summary>
        public const float JumpThreshold = 0.05f;
        public const float BreathePx = 1f;
        public const float BreatheHz = 0.5f;
        public const float SweepSeconds = 0.300f;
        /// <summary>Half the width of the sweep's band, in screen heights (the shader's constant).</summary>
        public const float SweepBand = 0.06f;
        /// <summary>The same toy is not swept again sooner than this after a sweep began.</summary>
        public const float SweepAgainAfter = 0.6f;
        /// <summary>Every toy sweeps this long after the level was completed (9.7).</summary>
        public const float CompleteSweepDelay = 0.2f;
        /// <summary>Rim gain of the "High-visibility toys" setting (2.6).</summary>
        public const float HighVisibilityRim = 1.6f;
        /// <summary>
        /// A toy thinner than this (its true size: a card, a feather, a key lying flat) casts no shadow. Its
        /// sliver of a shadow is narrower than two texels of the near cascade (ART_BIBLE 5.3: 0.03 units)
        /// and comes out as a row of teeth beside the toy; the contact core of its pool grounds it instead.
        /// Grown thick enough in the hand, it casts like anything else.
        /// </summary>
        public const float ThinCaster = 0.07f;
        const int MaxSweeps = 24;

        // easeOutBack with an overshoot of 6.5 / 4.5: 1 + 4 c^3 / (27 (c + 1)^2) = 1.444.
        const float PopOvershoot = 4.49f;

        static readonly int SweepId = Shader.PropertyToID("_Sweep");
        static readonly int RimBoostId = Shader.PropertyToID("_ToyRimBoost");

        readonly List<Renderer> heldRenderers = new List<Renderer>();
        readonly List<ShadowCastingMode> heldModes = new List<ShadowCastingMode>();
        readonly List<Sweep> sweeps = new List<Sweep>();
        readonly Stack<Sweep> sweepPool = new Stack<Sweep>();
        readonly Dictionary<Prop, List<Renderer>> thinned = new Dictionary<Prop, List<Renderer>>();
        readonly List<Renderer> scratch = new List<Renderer>();
        readonly ToyLookStandIn standIn = new ToyLookStandIn();
        MaterialPropertyBlock block;

        Game game;
        PresentationContext context;
        bool active;

        Prop held;
        float heldSeconds;
        float flashLeft;
        float lastScale;

        Prop lastFocus;
        float completeSweepIn = -1f;
        bool warming = true;

        sealed class Sweep
        {
            public Prop Prop;
            public float Seconds;
            /// <summary>The sweep is over and the renderers have their zero back; the entry only spaces out the next one.</summary>
            public bool Over;
            /// <summary>It has been drawn once: from then on a frame's time moves it.</summary>
            public bool Begun;
            public readonly List<Renderer> Renderers = new List<Renderer>();
        }

        /// <summary>The prop this presenter treats as held (shadows off, border on), or null.</summary>
        public Prop Held => held;
        /// <summary>Border width right now, in pixels at 1080p; 0 while nothing is held.</summary>
        public float BorderPx { get; private set; }
        /// <summary>Peel shadow offset right now, in pixels at 1080p (x right, y up).</summary>
        public Vector2 PeelPx { get; private set; }
        /// <summary>Sweeps running right now.</summary>
        public int SweepCount => sweeps.Count;

        public void Attach(Game game, PresentationContext context)
        {
            this.game = game;
            this.context = context;
            // The plain look is the game as it was before it had a look: nothing of this applies.
            if (context.Plain) return;
            active = true;
            block = new MaterialPropertyBlock();

            game.Events.PropGrabbed += OnGrabbed;
            game.Events.PropHeld += OnHeld;
            game.Events.PropDropped += OnDropped;
            game.Events.LevelUnloading += OnLevelUnloading;
            game.Events.LevelLoaded += OnLevelLoaded;
            game.Events.LevelCompleted += OnLevelCompleted;
            Settings.Changed += OnSettingChanged;
            context.QualityChanged += OnQualityChanged;
            ApplyHighVisibility();
            // The lighting rig attached before this (order) and has set its globals if it is there.
            standIn.Apply(game.Environment);

            ThinAll();

            // Attached in the middle of a hold (a tool): no pop, the sticker is simply there.
            Prop already = game.Grabber.Held;
            if (already != null)
            {
                BeginHold(already, already.Scale);
                heldSeconds = PopSeconds;
            }
            UpdateSticker();
            Publish();
        }

        public void Frame(float dt, float alpha)
        {
            if (!active) return;

            // A hold that ended without an event reaching us (the level went away with the toy in hand).
            if (held != null && (held.Removed || game.Grabber.Held != held)) EndHold();

            if (held != null)
            {
                heldSeconds += dt;
                flashLeft = Mathf.Max(0f, flashLeft - dt);
            }
            UpdateSticker();
            Publish();

            // The detail textures nobody has asked for yet, one a frame (ART_BIBLE 4.2), so the first toy of a
            // recipe does not pay for its texture in the middle of play. In the game only: tools and tests
            // make what they use.
            if (warming && Application.isPlaying) warming = TexCache.WarmNext(context.Quality);

            UpdateFocus();
            if (completeSweepIn >= 0f)
            {
                completeSweepIn -= dt;
                if (completeSweepIn < 0f) SweepAll();
            }
            UpdateSweeps(dt);
        }

        public void Dispose()
        {
            if (!active) return;
            active = false;
            game.Events.PropGrabbed -= OnGrabbed;
            game.Events.PropHeld -= OnHeld;
            game.Events.PropDropped -= OnDropped;
            game.Events.LevelUnloading -= OnLevelUnloading;
            game.Events.LevelLoaded -= OnLevelLoaded;
            game.Events.LevelCompleted -= OnLevelCompleted;
            Settings.Changed -= OnSettingChanged;
            context.QualityChanged -= OnQualityChanged;

            EndHold();
            ClearSweeps();
            ThickenAll();
            StickerLook.Reset();
            Shader.SetGlobalFloat(RimBoostId, 0f);
            standIn.Release();
        }

        // ---- The sticker ------------------------------------------------------------------------------------

        void OnGrabbed(PropHoldEvent e)
        {
            if (e.Prop == null || e.Prop.Removed) return;
            // Events arrive when their tick ends; a hold that is over again by then needs no sticker.
            if (game.Grabber.Held != e.Prop) return;
            BeginHold(e.Prop, e.GrabScale);
            // The toy in hand takes no sweep: its picture must not change.
            StopSweep(e.Prop);
            UpdateSticker();
            Publish();
        }

        void OnHeld(PropHoldEvent e)
        {
            if (e.Prop != held) return;
            float scale = e.Scale;
            // Not during the pop: the first placement after a grab nearly always moves the toy, and the pop
            // already says that something happened.
            if (heldSeconds >= PopSeconds && lastScale > 0f && Mathf.Abs(scale / lastScale - 1f) > JumpThreshold) flashLeft = FlashSeconds;
            lastScale = scale;
        }

        void OnDropped(PropHoldEvent e)
        {
            if (e.Prop != held) return;
            EndHold();
            // The crosshair is still on the toy that was just let go, which makes it the focus candidate at
            // once. It takes no sweep for that: the release has its own reveal (flood, ring, shutter), and a
            // white band across the toy in the same instant would cover it. Looking away and back sweeps it.
            if (e.Prop != null && !e.Prop.Removed)
            {
                lastFocus = e.Prop;
                // Its size has just changed: whether it is thick enough to cast is decided anew.
                Thin(e.Prop);
            }
            Publish();
        }

        void OnLevelUnloading(LevelEvent e)
        {
            // Everything of the level is about to be destroyed; let go of it while it still exists.
            EndHold();
            ClearSweeps();
            thinned.Clear();
            lastFocus = null;
            completeSweepIn = -1f;
            Publish();
        }

        void OnLevelLoaded(LevelEvent e)
        {
            lastFocus = null;
            completeSweepIn = -1f;
            standIn.Apply(game.Environment);
            ThinAll();
        }

        void OnLevelCompleted(LevelEvent e) => completeSweepIn = CompleteSweepDelay;

        // Another tier has other detail textures (their size, the orange peel on High).
        void OnQualityChanged(QualityTier tier) => warming = true;

        void BeginHold(Prop prop, float grabScale)
        {
            EndHold();
            held = prop;
            heldSeconds = 0f;
            flashLeft = 0f;
            lastScale = grabScale;

            // Stop it casting (8.1 step 3), and remember what each renderer did before.
            prop.GameObject.GetComponentsInChildren(true, heldRenderers);
            heldModes.Clear();
            for (int i = 0; i < heldRenderers.Count; i++)
            {
                heldModes.Add(heldRenderers[i].shadowCastingMode);
                heldRenderers[i].shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        void EndHold()
        {
            if (held == null) return;
            for (int i = 0; i < heldRenderers.Count; i++)
                if (heldRenderers[i] != null) heldRenderers[i].shadowCastingMode = heldModes[i];
            heldRenderers.Clear();
            heldModes.Clear();
            held = null;
            heldSeconds = 0f;
            flashLeft = 0f;
            BorderPx = 0f;
            PeelPx = Vector2.zero;
        }

        // ---- Toys too thin to cast -------------------------------------------------------------------------

        void ThinAll()
        {
            thinned.Clear();
            if (game.Level == null) return;
            IReadOnlyList<Prop> props = game.Props;
            for (int i = 0; i < props.Count; i++)
                if (!props[i].Removed && !props[i].Held) Thin(props[i]);
        }

        /// <summary>True if the prop is thick enough (at its scale now) to cast a shadow worth having.</summary>
        public static bool CastsShadow(Prop prop)
        {
            Vector3 half = prop.LocalHalfExtents;
            return 2f * Mathf.Min(half.x, Mathf.Min(half.y, half.z)) * prop.Scale >= ThinCaster;
        }

        void Thin(Prop prop)
        {
            bool casts = CastsShadow(prop);
            if (thinned.TryGetValue(prop, out List<Renderer> off))
            {
                if (!casts) return;
                // Thick enough now: the renderers that were switched off cast again.
                for (int i = 0; i < off.Count; i++)
                    if (off[i] != null) off[i].shadowCastingMode = ShadowCastingMode.On;
                thinned.Remove(prop);
                return;
            }
            if (casts) return;
            prop.GameObject.GetComponentsInChildren(true, scratch);
            off = new List<Renderer>();
            for (int i = 0; i < scratch.Count; i++)
            {
                if (scratch[i].shadowCastingMode != ShadowCastingMode.On) continue;
                scratch[i].shadowCastingMode = ShadowCastingMode.Off;
                off.Add(scratch[i]);
            }
            scratch.Clear();
            thinned[prop] = off;
        }

        void ThickenAll()
        {
            foreach (KeyValuePair<Prop, List<Renderer>> pair in thinned)
                for (int i = 0; i < pair.Value.Count; i++)
                    if (pair.Value[i] != null) pair.Value[i].shadowCastingMode = ShadowCastingMode.On;
            thinned.Clear();
        }

        void UpdateSticker()
        {
            if (held == null)
            {
                BorderPx = 0f;
                PeelPx = Vector2.zero;
                return;
            }
            float pop = Mathf.Clamp01(heldSeconds / PopSeconds);
            BorderPx = Mathf.Max(0f, StickerLook.BorderWidth * BackOut(pop)) + FlashPx * (flashLeft / FlashSeconds);

            // The shadow slides out from under the toy, then breathes along its own direction.
            float slide = pop * pop * (3f - 2f * pop);
            Vector2 rest = StickerLook.PeelOffset;
            float breathe = BreathePx * Mathf.Sin(2f * Mathf.PI * BreatheHz * Mathf.Max(0f, heldSeconds - PopSeconds));
            PeelPx = rest * slide + rest.normalized * (breathe * slide);
        }

        /// <summary>The border's width as a fraction of its resting width, <paramref name="t"/> in 0..1 through the pop.</summary>
        public static float BackOut(float t)
        {
            float u = Mathf.Clamp01(t) - 1f;
            return 1f + (PopOvershoot + 1f) * u * u * u + PopOvershoot * u * u;
        }

        void Publish()
        {
            StickerLook.Border = held != null;
            StickerLook.BorderPx = BorderPx;
            StickerLook.PeelPx = PeelPx;
            Color paper = Palette.Lin(Palette.Paper) * 1.1f;
            paper.a = 1f;
            StickerLook.BorderColor = paper;
            Color peel = Color.LerpUnclamped(Color.white, Palette.Lin(Palette.Ink), 0.22f);
            peel.a = 1f;
            StickerLook.PeelColor = peel;
        }

        // ---- High-visibility toys ---------------------------------------------------------------------------

        void OnSettingChanged(Setting setting)
        {
            if (setting == Setting.HighVisibility) ApplyHighVisibility();
        }

        void ApplyHighVisibility() =>
            Shader.SetGlobalFloat(RimBoostId, Settings.HighVisibility ? HighVisibilityRim - 1f : 0f);

        // ---- The focus sweep --------------------------------------------------------------------------------

        void UpdateFocus()
        {
            // Only while playing: a title backdrop or a paused game points at nothing.
            Prop focus = context.State == FlowState.Playing && game.Level != null ? game.Grabber.Focus : null;
            if (focus == lastFocus) return;
            lastFocus = focus;
            if (focus != null) StartSweep(focus, true);
        }

        /// <summary>Runs one glint sweep across a prop (it does nothing for the prop in hand).</summary>
        public void StartSweep(Prop prop) => StartSweep(prop, false);

        /// <summary>One glint sweep across every grabbable toy of the level, in sync (level complete).</summary>
        public void SweepAll()
        {
            if (!active || game.Level == null) return;
            IReadOnlyList<Prop> props = game.Props;
            for (int i = 0; i < props.Count; i++)
                if (props[i].Grabbable) StartSweep(props[i], false);
        }

        void StartSweep(Prop prop, bool spaced)
        {
            if (!active || prop == null || prop.Removed || prop == held) return;
            for (int i = 0; i < sweeps.Count; i++)
            {
                if (sweeps[i].Prop != prop) continue;
                // Aim wobbling on and off a toy must not make it flicker.
                if (spaced && sweeps[i].Seconds < SweepAgainAfter) return;
                sweeps[i].Seconds = 0f;
                sweeps[i].Begun = false;
                return;
            }
            if (sweeps.Count >= MaxSweeps) return;
            Sweep sweep = sweepPool.Count > 0 ? sweepPool.Pop() : new Sweep();
            sweep.Prop = prop;
            sweep.Seconds = 0f;
            sweep.Over = false;
            sweep.Begun = false;
            prop.GameObject.GetComponentsInChildren(true, sweep.Renderers);
            sweeps.Add(sweep);
        }

        void StopSweep(Prop prop)
        {
            for (int i = sweeps.Count - 1; i >= 0; i--)
                if (sweeps[i].Prop == prop) RemoveSweep(i);
        }

        void UpdateSweeps(float dt)
        {
            Camera camera = context.Camera;
            for (int i = sweeps.Count - 1; i >= 0; i--)
            {
                Sweep sweep = sweeps[i];
                Prop prop = sweep.Prop;
                // The frame a sweep starts in shows its first position; time moves it from the next one on.
                if (sweep.Begun) sweep.Seconds += dt;
                sweep.Begun = true;
                // An entry outlives its sweep for a while so that the same toy is not swept again at once.
                if (prop.Removed || prop == held || sweep.Seconds >= Mathf.Max(SweepSeconds, SweepAgainAfter))
                {
                    RemoveSweep(i);
                    continue;
                }
                if (sweep.Seconds >= SweepSeconds || camera == null)
                {
                    if (!sweep.Over) Write(sweep, Vector4.zero);
                    sweep.Over = true;
                }
                else
                {
                    sweep.Over = false;
                    // The band crosses the toy's circle on screen, from just outside it to just outside it.
                    Vector3 centre = prop.Center;
                    Vector3 viewport = camera.WorldToViewportPoint(centre);
                    float distance = Mathf.Max(1e-3f, Vector3.Distance(camera.transform.position, centre));
                    float reach = prop.Radius / (distance * 2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad)) + SweepBand;
                    float at = Mathf.Lerp(-reach, reach, sweep.Seconds / SweepSeconds);
                    Write(sweep, viewport.z > 0f ? new Vector4(viewport.x, viewport.y, at, 1f) : Vector4.zero);
                }
            }
        }

        void RemoveSweep(int index)
        {
            Sweep sweep = sweeps[index];
            if (!sweep.Over) Write(sweep, Vector4.zero);
            sweep.Prop = null;
            sweep.Renderers.Clear();
            sweeps.RemoveAt(index);
            sweepPool.Push(sweep);
        }

        void ClearSweeps()
        {
            for (int i = sweeps.Count - 1; i >= 0; i--) RemoveSweep(i);
        }

        // Per renderer, next to whatever else its property block holds (a signal's emission).
        void Write(Sweep sweep, Vector4 value)
        {
            for (int i = 0; i < sweep.Renderers.Count; i++)
            {
                Renderer renderer = sweep.Renderers[i];
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block);
                block.SetVector(SweepId, value);
                renderer.SetPropertyBlock(block);
            }
        }
    }
}
