using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Platform;
using UnityEngine;

namespace Toybox.Render
{
    /// <summary>
    /// Being thrown, drawn (LEVELS 2.2, Levels 6, 7 and 15). A bounce pad and a seesaw set the player's
    /// velocity in one tick; without a mark the eye has nothing to hold on to. The marks are made for a
    /// first-person eye - whoever is launched is looking out of the thing that flies:
    ///
    /// - a ring bursts on the pad as it throws (seen by whoever looks down at their landing), and at the
    ///   struck tip of a seesaw, turned to the eye (seen from the seat at the other end);
    /// - the pad itself gives: it flattens by a thirtieth of the landing speed and springs back within
    ///   150 ms (LEVELS, Level 6) - the toy shader's own squash, so its collider never changes and a pad
    ///   in the hand never gives;
    /// - hoops hang over the launch point, the last one where the flight will top out (v^2 / 2g): the
    ///   player rises through them - speed, read off the hoops going by - and the top hoop, brighter,
    ///   settles round them as they hang at the apex: "this is as high as it goes";
    /// - what a seesaw shoots (a marble, not the player) gets a streak from the lever up to its apex
    ///   instead, because that is watched from the side.
    ///
    /// Paper (die-cut with a deeper edge in the day rooms, where Paper alone is white on white), in the
    /// world, depth-tested; two draws while anything is on show. The marks stand where the
    /// launch happened - never on a held toy.
    /// </summary>
    [Presenter(242, ProvidesLook = true)]
    public sealed class LaunchCues : GadgetVisual
    {
        public const float HoopSeconds = 1.3f, BurstSeconds = 0.45f;
        /// <summary>Hoops are this far apart (per unit of player size), at most this many, this wide.</summary>
        public const float HoopSpacing = 1.6f, HoopRadius = 2.2f;
        public const int MaxHoops = 4;
        /// <summary>The top hoop hangs this far (in eye heights) above where the feet top out: below the eye, in view.</summary>
        public const float ApexLift = 0.45f;
        /// <summary>A pad flattens by the landing speed times this (at most <see cref="SquashMax"/>) and is back after <see cref="SquashSeconds"/>.</summary>
        public const float SquashPerSpeed = 1f / 30f, SquashMax = 0.3f, SquashSeconds = 0.15f;

        static readonly int SquashId = Shader.PropertyToID("_SquashA");

        readonly List<Renderer> squashed = new List<Renderer>();
        readonly List<Renderer> scratch = new List<Renderer>();
        MaterialPropertyBlock block;
        BouncePad squashPad;
        float squashAmount, squashAge;
        CuePool pool;

        /// <summary>Launches and strikes marked since the level was loaded.</summary>
        public int Fired { get; private set; }
        /// <summary>Marks on show right now.</summary>
        public int Alive => pool != null ? pool.Alive : 0;
        /// <summary>How high the last launch was marked to carry (above where it started).</summary>
        public float LastApex { get; private set; }
        /// <summary>Hoops hung for the last launch of the player.</summary>
        public int LastHoops { get; private set; }
        public Renderer RingRenderer => pool?.RingRenderer;
        public Renderer StreakRenderer => pool?.StreakRenderer;
        /// <summary>How flat the pad of the last bounce is drawn right now: 0 at rest, up to <see cref="SquashMax"/> at the landing.</summary>
        public float PadSquash => squashPad != null ? squashAmount * ToySquash.Shape(squashAge / SquashSeconds) : 0f;
        /// <summary>The pad that is giving right now, or null.</summary>
        public BouncePad SquashedPad => squashPad;

        /// <summary>How high a launch at this speed carries: v^2 / 2g.</summary>
        public static float Apex(float speed) => speed * speed / (2f * Game.Gravity);

        /// <summary>How many hoops a flight of this height gets, for a player of this size.</summary>
        public static int Hoops(float apex, float size) => Mathf.Clamp(Mathf.RoundToInt(apex / (HoopSpacing * Mathf.Max(0.1f, size))), 1, MaxHoops);

        protected override void Subscribe(GameEvents events)
        {
            events.Bounced += OnBounced;
            events.SeesawStruck += OnSeesawStruck;
            events.SeesawLaunched += OnSeesawLaunched;
            events.SeesawProjectile += OnSeesawProjectile;
        }

        protected override void Unsubscribe(GameEvents events)
        {
            events.Bounced -= OnBounced;
            events.SeesawStruck -= OnSeesawStruck;
            events.SeesawLaunched -= OnSeesawLaunched;
            events.SeesawProjectile -= OnSeesawProjectile;
        }

        protected override void Begin()
        {
            pool = new CuePool("Launch Cues", Root, GadgetFx.Pick(Tier, 6, 10, 14), GadgetFx.Edge(Materials.Dip));
            Fired = 0;
            LastApex = 0f;
            LastHoops = 0;
        }

        protected override void Draw(float dt, float alpha)
        {
            pool?.Update(dt, Context.Camera != null ? Context.Camera.transform : null);
            if (squashPad == null) return;
            squashAge += dt;
            Prop prop = squashPad.Prop;
            if (squashAge >= SquashSeconds || squashPad.Disposed || (prop != null && (prop.Removed || prop.Held))) EndSquash();
            else WriteSquash(PadSquash);
        }

        // Value is the launch speed, Value2 the speed the player came down with.
        void OnBounced(GadgetEvent e)
        {
            LaunchPlayer(e.Position, e.Value, true);
            if (e.Gadget is BouncePad pad) Flatten(pad, e.Value2);
        }

        void Flatten(BouncePad pad, float fall)
        {
            EndSquash();
            Prop prop = pad.Prop;
            GameObject body = prop != null ? (prop.Removed || prop.Held ? null : prop.GameObject) : pad.Surface != null ? pad.Surface.gameObject : null;
            float amount = Mathf.Min(SquashMax, Mathf.Max(0f, fall) * SquashPerSpeed);
            if (body == null || amount <= 0f) return;
            // Only what is drawn with a shader that knows the squash (the toy shader; not a room slab).
            body.GetComponentsInChildren(true, scratch);
            for (int i = 0; i < scratch.Count; i++)
            {
                Material material = scratch[i].sharedMaterial;
                if (material != null && material.HasProperty(SquashId)) squashed.Add(scratch[i]);
            }
            scratch.Clear();
            if (squashed.Count == 0) return;
            squashPad = pad;
            squashAmount = amount;
            squashAge = 0f;
            WriteSquash(amount);
        }

        void EndSquash()
        {
            if (squashPad == null) return;
            WriteSquash(0f);
            squashPad = null;
            squashed.Clear();
        }

        // (centre.x, pivot.y, centre.z, amount), as ToySquash writes it: flat toward its foot, spread about its middle.
        void WriteSquash(float amount)
        {
            block ??= new MaterialPropertyBlock();
            Vector4 value = Vector4.zero;
            if (amount != 0f)
            {
                bool any = false;
                Bounds bounds = default;
                for (int i = 0; i < squashed.Count; i++)
                {
                    if (squashed[i] == null) continue;
                    if (any) bounds.Encapsulate(squashed[i].bounds);
                    else bounds = squashed[i].bounds;
                    any = true;
                }
                if (any) value = new Vector4(bounds.center.x, bounds.min.y, bounds.center.z, amount);
            }
            for (int i = 0; i < squashed.Count; i++)
            {
                Renderer renderer = squashed[i];
                if (renderer == null) continue;
                renderer.GetPropertyBlock(block);
                block.SetVector(SquashId, value);
                renderer.SetPropertyBlock(block);
            }
        }

        // A prop the seesaw threw would be watched from outside; the player is not.
        void OnSeesawLaunched(GadgetEvent e)
        {
            if (e.Prop == null) LaunchPlayer(e.Position, e.Value, false);
            else Shoot(e.Position, e.Value, Mathf.Clamp(e.Prop.Radius, 0.2f, 3f));
        }

        void OnSeesawProjectile(GadgetEvent e) => Shoot(e.Position, e.Value, 0.45f);

        void OnSeesawStruck(GadgetEvent e)
        {
            if (pool == null) return;
            // Value2 is f: 0.05 a nudge, 1 a full-blooded strike. Turned to the eye: it is watched from the far seat.
            float f = Mathf.Clamp01(e.Value2);
            Color paper = GadgetFx.Lin(Palette.Paper, 1.15f, 0.6f + 0.35f * f);
            pool.Ring(e.Position + Vector3.up * 0.3f, Vector3.zero, 0.4f, 1f + 2.4f * f, paper, BurstSeconds, true);
            Fired++;
        }

        void LaunchPlayer(Vector3 from, float speed, bool burst)
        {
            if (pool == null || speed <= 0f) return;
            float size = Mathf.Max(0.2f, Game.Player.Scale);
            float apex = Apex(speed);
            LastApex = apex;
            Fired++;
            Color paper = GadgetFx.Lin(Palette.Paper, 1.15f, 0.9f);
            if (burst) pool.Ring(from + Vector3.up * 0.04f, Vector3.up, 0.35f * size, Mathf.Clamp(0.7f + 0.09f * speed, 0.8f, 4f) * size, paper, BurstSeconds, true);

            int hoops = Hoops(apex, size);
            LastHoops = hoops;
            float lift = ApexLift * Player.BaseEyeHeight * size;
            Color faint = paper;
            faint.a = 0.55f;
            for (int k = 1; k <= hoops; k++)
            {
                // The top one is where the flight ends: it stays longest and shines brightest.
                bool top = k == hoops;
                Vector3 at = from + Vector3.up * (apex * k / hoops + lift);
                pool.Hoop(at, Vector3.up, HoopRadius * size * 0.8f, HoopRadius * size, top ? paper : faint, top ? HoopSeconds : HoopSeconds * 0.75f, true);
            }
        }

        void Shoot(Vector3 from, float speed, float size)
        {
            if (pool == null || speed <= 0f) return;
            float apex = Apex(speed);
            LastApex = apex;
            Fired++;
            Color paper = GadgetFx.Lin(Palette.Paper, 1.15f, 0.9f);
            Color faint = paper;
            faint.a = 0.6f;
            pool.Streak(from, apex, 0.6f * size, faint, HoopSeconds, true);
            pool.Ring(from + Vector3.up * apex, Vector3.zero, 0.3f * size, 0.9f * size, paper, HoopSeconds, true);
        }

        protected override void Forget()
        {
            EndSquash();
            pool?.Destroy();
            pool = null;
        }
    }
}
