using System;
using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    public enum BreakMode
    {
        /// <summary>The collider goes (and the visual with it): the way is open.</summary>
        RemoveCollider,
        /// <summary>The body is squashed to <see cref="BreakableOptions.FlattenedHeight"/>: a crushed alarm clock.</summary>
        SwapCollider,
    }

    public sealed class BreakableOptions
    {
        public string Name = "Breakable";
        /// <summary>The box that breaks: centre, size and orientation.</summary>
        public Vector3 Center;
        public Vector3 Size = Vector3.one;
        public Quaternion Rotation = Quaternion.identity;
        /// <summary>Only props with this tag can break it (null: any body).</summary>
        public string AcceptTag;
        /// <summary>The mass and the speed along <see cref="Direction"/> a hit needs.</summary>
        public float MinMass = 1f, MinSpeed = 1f;
        /// <summary>The unit vector speed is measured along: +Z into a barricade, -Y onto a clock.</summary>
        public Vector3 Direction = Vector3.forward;
        /// <summary>A body this close to the box is touching it.</summary>
        public float Skin = 0.15f;
        /// <summary>Ticks of speed that are remembered: by the time the touch is seen the hit has already slowed the body.</summary>
        public int History = 3;
        public BreakMode OnBreak = BreakMode.RemoveCollider;
        public float FlattenedHeight = 0.3f;
        /// <summary>The body (colliders and renderers, no Rigidbody), placed by the gadget. Null: a wall of blocks is built.</summary>
        public GameObject Body;
    }

    /// <summary>
    /// Something that gives way to a heavy, fast hit and to nothing else (LEVELS 2.1): the barricade of
    /// Level 4, the alarm clock of Level 15. No collision callbacks: every tick it follows the bodies near
    /// it, keeps the last few ticks of the speed of their nearest point along its direction, and judges a
    /// body when it first touches - mass >= MinMass and the largest remembered speed >= MinSpeed.
    /// </summary>
    public sealed class Breakable : Gadget
    {
        sealed class Track
        {
            public float[] Speeds;
            public int Next;
            public bool Inside;
            public bool Bonked;
            public float Best;
        }

        // How far ahead of the box a body starts being followed, beyond what it covers in the history.
        const float WatchMargin = 1f;
        const float BonkSpeed = 1f;

        readonly BreakableOptions options;
        readonly Dictionary<Prop, Track> tracks = new Dictionary<Prop, Track>();
        readonly HashSet<Prop> watched = new HashSet<Prop>();
        readonly GameObject body;
        readonly Collider[] colliders;
        readonly Renderer[] renderers;
        readonly Zone bounds;
        readonly Vector3 direction;
        readonly Vector3 bodyScale;
        readonly int history;

        public Breakable(LevelContext ctx, BreakableOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            bounds = Zone.Box(options.Center, options.Size, options.Rotation);
            direction = options.Direction.sqrMagnitude > 1e-8f ? options.Direction.normalized : Vector3.forward;
            history = Mathf.Max(1, options.History);
            body = options.Body != null ? options.Body : BuildWall(options.Size);
            body.name = "Breakable " + Name;
            ctx.AddStatic(body, options.Center, options.Rotation);
            bodyScale = body.transform.localScale;
            colliders = body.GetComponentsInChildren<Collider>(true);
            renderers = body.GetComponentsInChildren<Renderer>(true);
        }

        public bool Broken { get; private set; }
        public Zone Bounds => bounds;
        public GameObject Body => body;
        public Vector3 Direction => direction;
        /// <summary>The prop that broke it and its speed into it.</summary>
        public Prop BrokenBy { get; private set; }
        public float BreakSpeed { get; private set; }

        /// <summary>Inside the tick: the prop and its speed along the direction.</summary>
        public event Action<Prop, float> Broke;
        /// <summary>A hit that was too light or too slow: the prop and mass / MinMass. Once per approach.</summary>
        public event Action<Prop, float> Bonked;

        /// <summary>Also follow this prop while a gadget drives it (driven props are otherwise ignored), whatever its tag.</summary>
        public void Watch(Prop prop)
        {
            if (prop != null) watched.Add(prop);
        }

        protected override void Tick(float dt)
        {
            if (Broken) return;
            IReadOnlyList<Prop> props = Game.Props;
            for (int i = 0; i < props.Count; i++)
            {
                Prop prop = props[i];
                bool isWatched = watched.Contains(prop);
                if (!GadgetKit.IsLoose(prop) || (!isWatched && (prop.Body.isKinematic || (options.AcceptTag != null && !prop.HasTag(options.AcceptTag)))))
                {
                    tracks.Remove(prop);
                    continue;
                }

                Vector3 centre = prop.Center;
                Vector3 onBox = bounds.ClosestPoint(centre);
                float reach = options.Skin + WatchMargin + prop.Velocity.magnitude * Sim.Dt * history;
                if ((centre - onBox).magnitude - prop.Radius > reach)
                {
                    tracks.Remove(prop);
                    continue;
                }

                // The point of the body that touches first. Two candidates: its foremost point along the
                // direction (the tip of a toppling domino, the underside of a falling ball), and the point
                // nearest to the box, found by alternating between the two shapes a few times.
                Vector3 point = GadgetKit.ClosestPoint(prop, centre + direction * (prop.Radius * 64f + 100f));
                float gap = (point - bounds.ClosestPoint(point)).magnitude;
                Vector3 near = GadgetKit.ClosestPoint(prop, onBox);
                for (int k = 0; k < 3; k++) near = GadgetKit.ClosestPoint(prop, bounds.ClosestPoint(near));
                float nearGap = (near - bounds.ClosestPoint(near)).magnitude;
                if (nearGap < gap - 1e-4f)
                {
                    point = near;
                    gap = nearGap;
                }
                float into = Mathf.Max(0f, Vector3.Dot(GadgetKit.PointVelocity(prop, point), direction));

                if (!tracks.TryGetValue(prop, out Track track)) tracks[prop] = track = new Track { Speeds = new float[history] };
                track.Speeds[track.Next] = into;
                track.Next = (track.Next + 1) % history;

                bool inside = gap <= options.Skin;
                if (!inside)
                {
                    track.Inside = false;
                    track.Bonked = false;
                    track.Best = 0f;
                    continue;
                }
                float best = 0f;
                for (int s = 0; s < history; s++) best = Mathf.Max(best, track.Speeds[s]);
                track.Best = Mathf.Max(track.Best, best);
                bool first = !track.Inside;
                track.Inside = true;

                if (prop.Mass >= options.MinMass && track.Best >= options.MinSpeed)
                {
                    Break(prop, track.Best, point);
                    return;
                }
                if (first && !track.Bonked && best >= BonkSpeed)
                {
                    track.Bonked = true;
                    float share = options.MinMass > 0f ? prop.Mass / options.MinMass : 1f;
                    Bonked?.Invoke(prop, share);
                    Game.Events.RaiseBreakableBonked(Event(point, prop, share, best));
                }
            }
        }

        void Break(Prop prop, float speed, Vector3 point)
        {
            Broken = true;
            BrokenBy = prop;
            BreakSpeed = speed;
            tracks.Clear();
            if (options.OnBreak == BreakMode.RemoveCollider)
            {
                for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = false;
                for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = false;
            }
            else
            {
                // Squashed onto its own foot.
                float share = Mathf.Clamp(options.FlattenedHeight / Mathf.Max(options.Size.y, 1e-4f), 0.01f, 1f);
                Transform t = body.transform;
                t.localScale = new Vector3(bodyScale.x, bodyScale.y * share, bodyScale.z);
                t.position = options.Center - options.Rotation * Vector3.up * (options.Size.y * (1f - share) * 0.5f);
            }
            Physics.SyncTransforms();
            // Whatever leaned on it has lost its support.
            Game.WakeAll();
            Broke?.Invoke(prop, speed);
            Game.Events.RaiseBreakableBroke(Event(point, prop, speed, prop != null ? prop.Mass : 0f));
        }

        /// <summary>Whole again.</summary>
        public override void Reset()
        {
            base.Reset();
            tracks.Clear();
            if (!Broken) return;
            Broken = false;
            BrokenBy = null;
            BreakSpeed = 0f;
            for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = true;
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = true;
            body.transform.localScale = bodyScale;
            body.transform.SetPositionAndRotation(options.Center, options.Rotation);
            Physics.SyncTransforms();
        }

        // A wall of building blocks in Birch (ART_BIBLE 2.5 rule 4: a non-grabbable prop), one box collider.
        static GameObject BuildWall(Vector3 size)
        {
            var root = new GameObject("Breakable");
            root.AddComponent<BoxCollider>().size = size;
            int columns = Mathf.Clamp(Mathf.RoundToInt(size.x / 1.8f), 1, 8);
            int rows = Mathf.Clamp(Mathf.RoundToInt(size.y / 1.2f), 1, 10);
            Mesh mesh = MeshKit.Cached(MeshKit.Key("BreakableWall", size.x, size.y, size.z), () =>
            {
                var parts = new List<MeshPart>();
                var block = new Vector3(size.x / columns, size.y / rows, size.z);
                float bevel = Mathf.Min(block.x, Mathf.Min(block.y, block.z)) * 0.06f;
                Mesh whole = MeshKit.RoundedBox(block, bevel, 1);
                Mesh half = MeshKit.RoundedBox(new Vector3(block.x * 0.5f, block.y, block.z), bevel, 1);
                for (int r = 0; r < rows; r++)
                {
                    float y = -size.y * 0.5f + (r + 0.5f) * block.y;
                    // Every other course is shifted by half a block, like a real wall.
                    if (r % 2 == 0)
                    {
                        for (int c = 0; c < columns; c++)
                            parts.Add(new MeshPart(whole, new Vector3(-size.x * 0.5f + (c + 0.5f) * block.x, y, 0f)));
                    }
                    else
                    {
                        parts.Add(new MeshPart(half, new Vector3(-size.x * 0.5f + block.x * 0.25f, y, 0f)));
                        for (int c = 0; c < columns - 1; c++)
                            parts.Add(new MeshPart(whole, new Vector3(-size.x * 0.5f + (c + 1f) * block.x, y, 0f)));
                        parts.Add(new MeshPart(half, new Vector3(size.x * 0.5f - block.x * 0.25f, y, 0f)));
                    }
                }
                return MeshKit.Merge("BreakableWall", parts);
            });
            GadgetKit.Visual(root.transform, "Visual", mesh, Materials.Toy(ToyRecipe.PlainProp, Palette.Birch));
            return root;
        }
    }
}
