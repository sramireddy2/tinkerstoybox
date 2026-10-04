using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Platform;
using UnityEngine;

namespace Toybox.Render
{
    /// <summary>
    /// The small things gadgets do that would otherwise happen without a trace. A ring where it happened,
    /// in the colour of what it means - Paper for "something moved", the Go signal for "accepted", the
    /// wrong-size red for "refused", the Hazard signal where a hazard caught the player:
    ///
    /// - a leash or a return port puts a toy back: a ring where it vanished and one where it reappears;
    /// - a socket seats or refuses a toy, a plate goes down or finds a toy too light, a carrier grips;
    /// - a nested toy appears, a recall pad fetches its toy, a portal has no room, a sail stalls or flies;
    /// - a laser, a hazard zone or deep water sends the player back.
    ///
    /// And the one piece of state among them: a <see cref="RecallPad"/> shows how long the player still
    /// has to stand on it, as a ring that closes on the pad from far enough out to be seen without looking down.
    /// Two draws while cues are on show, one per pad in use.
    /// </summary>
    [Presenter(248, ProvidesLook = true)]
    public sealed class GadgetCues : GadgetVisual
    {
        public const float Seconds = 0.55f;
        /// <summary>
        /// A recall pad's ring starts this far out, per unit of player size: the floor comes into a level
        /// gaze about 2.2 player sizes ahead (eye height over the tangent of half the default field of view).
        /// </summary>
        public const float PadSweep = 4.2f;

        sealed class Pad
        {
            public RecallPad Recall;
            public MeshRenderer Ring;
            public MaterialPropertyBlock Block;
            public float Shown;
        }

        readonly List<Pad> pads = new List<Pad>();
        CuePool pool;

        /// <summary>Cues started since the level was loaded.</summary>
        public int Fired => pool != null ? pool.Started : 0;
        public int Alive => pool != null ? pool.Alive : 0;
        public int PadCount => pads.Count;
        /// <summary>How closed the i-th pad's ring is drawn, 0 (hidden) .. 1.</summary>
        public float PadProgressOf(int index) => pads[index].Shown;
        public Renderer PadRingOf(int index) => pads[index].Ring;

        protected override void Subscribe(GameEvents events)
        {
            events.LeashReturned += OnReturned;
            events.PortEjected += OnMoved;
            events.Recalled += OnMoved;
            events.NestRevealed += OnMoved;
            events.SocketSeated += OnAccepted;
            events.PlatePressed += OnAccepted;
            events.CarrierCaptured += OnAccepted;
            events.SailLaunched += OnAccepted;
            events.MachineDone += OnAccepted;
            events.SocketRejected += OnRefused;
            events.PlateRejected += OnRefused;
            events.PortalBlocked += OnRefused;
            events.SailStalled += OnRefused;
            events.MachineFizzled += OnRefused;
            events.LaserZapped += OnCaught;
            events.HazardCaught += OnCaught;
            events.WaterSwept += OnCaught;
        }

        protected override void Unsubscribe(GameEvents events)
        {
            events.LeashReturned -= OnReturned;
            events.PortEjected -= OnMoved;
            events.Recalled -= OnMoved;
            events.NestRevealed -= OnMoved;
            events.SocketSeated -= OnAccepted;
            events.PlatePressed -= OnAccepted;
            events.CarrierCaptured -= OnAccepted;
            events.SailLaunched -= OnAccepted;
            events.MachineDone -= OnAccepted;
            events.SocketRejected -= OnRefused;
            events.PlateRejected -= OnRefused;
            events.PortalBlocked -= OnRefused;
            events.SailStalled -= OnRefused;
            events.MachineFizzled -= OnRefused;
            events.LaserZapped -= OnCaught;
            events.HazardCaught -= OnCaught;
            events.WaterSwept -= OnCaught;
        }

        protected override void Begin() => pool = new CuePool("Gadget Cues", Root, GadgetFx.Pick(Tier, 6, 10, 16), GadgetFx.Edge(Materials.Dip));

        protected override void Adopt(Gadget gadget)
        {
            if (!(gadget is RecallPad recall)) return;
            MeshRenderer ring = Visual("Recall Ring " + recall.Name, GadgetFx.Quad, GadgetFx.Flat("Recall Ring", FlatBlend.Alpha, FlatShape.Ring, 0.2f, 1));
            ring.transform.SetPositionAndRotation(recall.Position + Vector3.up * 0.07f, Quaternion.Euler(90f, 0f, 0f));
            ring.enabled = false;
            pads.Add(new Pad { Recall = recall, Ring = ring, Block = new MaterialPropertyBlock() });
        }

        protected override void Draw(float dt, float alpha)
        {
            for (int i = 0; i < pads.Count; i++)
            {
                Pad pad = pads[i];
                if (pad.Ring == null) continue;
                RecallPad recall = pad.Recall;
                // Only while somebody stands there and the wait is still running.
                float progress = !recall.Disposed && recall.PlayerOn ? recall.Progress01 : 0f;
                pad.Shown = progress;
                bool visible = progress > 0f && progress < 1f;
                pad.Ring.enabled = visible;
                if (!visible) continue;
                // Whoever stands on the pad is looking ahead, not at their feet: the ring starts out on the floor
                // in front of them, where a level gaze still sees it, and snaps shut on the pad as the wait ends.
                float wide = Mathf.Max(recall.Radius * 2.2f, PadSweep * Game.Player.Scale);
                float radius = Mathf.Lerp(wide, recall.Radius * 1.05f, progress * progress);
                pad.Ring.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
                Color amber = Palette.Amber.Emission;
                amber.a = 0.65f + 0.35f * progress;
                pad.Block.SetVector(GadgetFx.ColorId, amber);
                pad.Ring.SetPropertyBlock(pad.Block);
            }
            pool?.Update(dt, Context.Camera != null ? Context.Camera.transform : null);
        }

        static float SizeOf(GadgetEvent e) => e.Prop != null && !e.Prop.Removed ? Mathf.Clamp(e.Prop.Radius, 0.3f, 6f) : 0.7f;

        // The toy went from e.Position to where it is now.
        void OnReturned(GadgetEvent e)
        {
            if (pool == null) return;
            float size = SizeOf(e);
            Color paper = GadgetFx.Lin(Palette.Paper, 1.1f, 0.85f);
            pool.Ring(e.Position, Vector3.zero, size * 1.2f, size * 0.3f, paper, Seconds, true);
            if (e.Prop != null && !e.Prop.Removed) pool.Ring(e.Prop.Center, Vector3.zero, size * 0.4f, size * 1.5f, paper, Seconds, true);
        }

        void OnMoved(GadgetEvent e)
        {
            if (pool == null) return;
            float size = SizeOf(e);
            pool.Ring(e.Position, Vector3.zero, size * 0.4f, size * 1.5f, GadgetFx.Lin(Palette.Paper, 1.1f, 0.85f), Seconds, true);
        }

        void OnAccepted(GadgetEvent e)
        {
            if (pool == null) return;
            float size = SizeOf(e);
            Color go = Palette.Go.Emission;
            go.a = 0.9f;
            pool.Ring(e.Position, Vector3.zero, size * 0.5f, size * 1.6f, go, Seconds);
        }

        void OnRefused(GadgetEvent e)
        {
            if (pool == null) return;
            float size = SizeOf(e);
            Color wrong = GadgetFx.Wrong.Emission;
            wrong.a = 0.9f;
            // Twice, quickly: a refusal is a double blink, like its lamp.
            pool.Ring(e.Position, Vector3.zero, size * 0.6f, size * 1.3f, wrong, Seconds * 0.6f);
            pool.Ring(e.Position, Vector3.zero, size * 0.3f, size * 1.7f, wrong, Seconds);
        }

        void OnCaught(GadgetEvent e)
        {
            if (pool == null) return;
            Color hazard = Palette.Hazard.Emission;
            hazard.a = 0.9f;
            float size = Game.Player.Scale;
            pool.Ring(e.Position + Vector3.up * (0.9f * size), Vector3.zero, 0.3f * size, 1.8f * size, hazard, Seconds);
        }

        protected override void Forget()
        {
            pads.Clear();
            pool?.Destroy();
            pool = null;
        }
    }
}
