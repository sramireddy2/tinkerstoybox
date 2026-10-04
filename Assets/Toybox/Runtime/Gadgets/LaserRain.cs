using System;
using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    public sealed class LaserRainOptions
    {
        public string Name = "Lasers";
        /// <summary>The emitter: the middle of a rectangle the beams leave from.</summary>
        public Vector3 Center;
        /// <summary>Its size across the beams (x and y of its own frame). A single laser is the zero-size case.</summary>
        public Vector2 Size;
        /// <summary>Which way the beams go. Straight down for the rain of Level 11.</summary>
        public Vector3 Direction = Vector3.down;
        /// <summary>With beams that are not vertical: the emitter's "up", to fix how the rectangle is turned.</summary>
        public Vector3 Up = Vector3.forward;
        /// <summary>How far a beam reaches.</summary>
        public float Range = 8f;
        /// <summary>Distance between neighbouring beams of the triangular lattice.</summary>
        public float LatticePitch = 0.45f;
        /// <summary>Seconds a beam has to rest on the player before it zaps.</summary>
        public float ZapDelay = 0.1f;
        /// <summary>The part of the footprint the player walks through, for <see cref="LaserRain.Coverage01"/> (invalid: all of it).</summary>
        public Zone Lane;
        /// <summary>What happens to a zapped player. Default: back to the checkpoint.</summary>
        public Action OnZapped;
        /// <summary>Draws the emitter plate, its lenses and the beams (additive, as long as they reach).</summary>
        public bool Visual = true;
        public float BeamWidth = 0.05f;
    }

    /// <summary>
    /// Laser emitters whose beams are blocked by whatever is in the world (LEVELS 2.3, Level 11) - and by
    /// nothing that is in the player's hand: a held toy is not in the world, the roof only works once it
    /// is let go. A beam that rests on the player for the zap delay sends them back.
    ///
    /// The hazard test treats the rain as continuous: rays are cast at the player from the emitter plane
    /// (the top of the capsule for beams from above, its axis as well for slanted ones), so no gap between
    /// lattice beams is wide enough to stand in. A sparse emitter (16 beams or fewer) casts every beam.
    /// The lattice itself is for the eye: <see cref="BeamLength"/> is refreshed a fifth per tick.
    /// </summary>
    public sealed class LaserRain : Gadget
    {
        const int SparseBeams = 16;
        const int RefreshShare = 5;
        // Rays start this far in front of the emitter plane: the emitter usually sits flush on a ceiling,
        // and a ray that starts exactly on a surface may or may not hit it.
        const float RayStart = 0.02f;

        readonly LaserRainOptions options;
        readonly Vector3 direction, right, up;
        readonly Vector3[] origins;
        readonly float[] lengths;
        readonly bool[] inLane;
        readonly int laneBeams;
        readonly int zapTicks;
        readonly List<Vector3> beamVertices;
        readonly Mesh beamMesh;
        int exposedTicks;
        int covered;
        int refresh;
        bool beamsDirty;
        bool allClear;

        public LaserRain(LevelContext ctx, LaserRainOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            direction = options.Direction.sqrMagnitude > 1e-8f ? options.Direction.normalized : Vector3.down;
            Vector3 hint = Mathf.Abs(Vector3.Dot(direction, options.Up.normalized)) > 0.99f ? Vector3.right : options.Up;
            right = Vector3.Cross(hint, direction).normalized;
            up = Vector3.Cross(direction, right).normalized;
            zapTicks = Ticks(options.ZapDelay, 1);

            origins = Lattice();
            lengths = new float[origins.Length];
            inLane = new bool[origins.Length];
            for (int i = 0; i < origins.Length; i++)
            {
                lengths[i] = options.Range;
                inLane[i] = !options.Lane.IsValid || options.Lane.ContainsXZ(origins[i] + direction * options.Range);
                if (inLane[i]) laneBeams++;
            }

            if (options.Visual)
            {
                BuildEmitter();
                beamVertices = new List<Vector3>(origins.Length * 8);
                beamMesh = BuildBeams();
                ctx.OnDispose(() => Sim.Destroy(beamMesh));
            }
            UpdateBeams();
        }

        public int BeamCount => origins.Length;
        public Vector3 Direction => direction;
        public float Range => options.Range;
        /// <summary>True while a beam rests on the player.</summary>
        public bool Exposed { get; private set; }
        public int Zaps { get; private set; }
        /// <summary>The share of the lane's beams that end at least a player's height before their full reach: how much of the lane is roofed.</summary>
        public float Coverage01 => laneBeams > 0 ? (float)covered / laneBeams : 0f;

        /// <summary>Where beam i leaves the emitter.</summary>
        public Vector3 BeamOrigin(int beam) => origins[beam];
        /// <summary>How far beam i gets before something in the world stops it (held toys never do).</summary>
        public float BeamLength(int beam) => lengths[beam];
        public Vector3 BeamEnd(int beam) => origins[beam] + direction * lengths[beam];

        /// <summary>Inside the tick, right before the player is sent back.</summary>
        public event Action Zapped;
        /// <summary>Every beam over the lane is blocked.</summary>
        public event Action AllClear;

        protected override void Tick(float dt)
        {
            // A fifth of the lattice per tick, in a fixed order. The first tick looks at every beam: only now
            // is the whole level built (and its colliders where physics can see them).
            int n = origins.Length;
            int share = Age == 0 ? n : Mathf.Max(1, (n + RefreshShare - 1) / RefreshShare);
            for (int k = 0; k < share && n > 0; k++)
            {
                Measure(refresh);
                refresh = (refresh + 1) % n;
            }
            CountCovered();
            bool clear = laneBeams > 0 && covered == laneBeams;
            if (clear && !allClear)
            {
                AllClear?.Invoke();
                Game.Events.RaiseLaserAllClear(Event(options.Center));
            }
            allClear = clear;
            if (beamsDirty) UpdateBeams();

            Exposed = PlayerExposed();
            exposedTicks = Exposed ? exposedTicks + 1 : 0;
            if (exposedTicks < zapTicks) return;
            exposedTicks = 0;
            Zaps++;
            Zapped?.Invoke();
            Game.Events.RaiseLaserZapped(Event(Game.Player.Position));
            if (options.OnZapped != null) options.OnZapped();
            else Game.Player.Respawn();
        }

        bool PlayerExposed()
        {
            Player player = Game.Player;
            CapsuleCollider capsule = player.Collider;
            PhysicsScene physics = Game.PhysicsScene;
            const int mask = Layers.SolidMask | Layers.PlayerMask;

            if (origins.Length <= SparseBeams)
            {
                for (int i = 0; i < origins.Length; i++)
                    if (physics.Raycast(origins[i] + direction * RayStart, direction, out RaycastHit hit, options.Range - RayStart, mask, QueryTriggerInteraction.Ignore) && hit.collider == capsule)
                        return true;
                return false;
            }

            // Points of the capsule a beam would land on: its top, and for slanted beams its axis too.
            Vector3 feet = player.Position;
            float radius = player.Radius * 0.8f, height = player.Height;
            Vector3 top = feet + Vector3.up * (height - 0.02f * player.Scale);
            int samples = Mathf.Abs(direction.y) > 0.9f ? 5 : 8;
            for (int s = 0; s < samples; s++)
            {
                Vector3 point;
                switch (s)
                {
                    case 0: point = top; break;
                    case 1: point = top + new Vector3(radius, -player.Radius * 0.4f, 0f); break;
                    case 2: point = top + new Vector3(-radius, -player.Radius * 0.4f, 0f); break;
                    case 3: point = top + new Vector3(0f, -player.Radius * 0.4f, radius); break;
                    case 4: point = top + new Vector3(0f, -player.Radius * 0.4f, -radius); break;
                    default: point = feet + Vector3.up * (height * (s - 4) * 0.25f) - direction * (player.Radius * 0.9f); break;
                }
                // Back along the beam onto the emitter plane; is that inside the emitter?
                Vector3 offset = point - options.Center;
                float along = Vector3.Dot(offset, direction);
                if (along < RayStart || along > options.Range) continue;
                if (Mathf.Abs(Vector3.Dot(offset, right)) > options.Size.x * 0.5f || Mathf.Abs(Vector3.Dot(offset, up)) > options.Size.y * 0.5f) continue;
                Vector3 origin = point - direction * (along - RayStart);
                if (physics.Raycast(origin, direction, out RaycastHit hit, options.Range - RayStart, mask, QueryTriggerInteraction.Ignore) && hit.collider == capsule)
                    return true;
            }
            return false;
        }

        void Measure(int beam)
        {
            float length = Game.PhysicsScene.Raycast(origins[beam] + direction * RayStart, direction, out RaycastHit hit, options.Range - RayStart, Layers.SolidMask, QueryTriggerInteraction.Ignore)
                ? hit.distance + RayStart
                : options.Range;
            if (Mathf.Approximately(length, lengths[beam])) return;
            lengths[beam] = length;
            beamsDirty = true;
        }

        void CountCovered()
        {
            float needed = options.Range - Game.Player.Height;
            covered = 0;
            for (int i = 0; i < lengths.Length; i++)
                if (inLane[i] && lengths[i] <= needed) covered++;
        }

        // A triangular lattice over the rectangle: rows half a pitch apart in turn.
        Vector3[] Lattice()
        {
            var points = new List<Vector3>();
            float pitch = Mathf.Max(0.05f, options.LatticePitch);
            float rowStep = pitch * 0.8660254f;
            int rows = Mathf.Max(1, Mathf.FloorToInt(options.Size.y / rowStep) + 1);
            int columns = Mathf.Max(1, Mathf.FloorToInt(options.Size.x / pitch) + 1);
            float rowSpan = (rows - 1) * rowStep;
            for (int r = 0; r < rows; r++)
            {
                bool odd = (r & 1) == 1 && columns > 1;
                int count = odd ? columns - 1 : columns;
                float span = (count - 1) * pitch;
                for (int c = 0; c < count; c++)
                    points.Add(options.Center + right * (-span * 0.5f + c * pitch) + up * (-rowSpan * 0.5f + r * rowStep));
            }
            return points.ToArray();
        }

        void BuildEmitter()
        {
            var root = new GameObject("Lasers " + Name);
            Transform t = root.transform;
            t.SetParent(Ctx.Root, false);
            t.SetPositionAndRotation(options.Center, Quaternion.LookRotation(direction, up));
            // The plate behind the lenses, and every lens in one mesh: Ink body, one hazard signal.
            var plate = new Vector3(options.Size.x + 0.4f, options.Size.y + 0.4f, 0.1f);
            GadgetKit.Visual(t, "Plate", GadgetKit.BoxMesh(plate), GadgetKit.Body, new Vector3(0f, 0f, -0.06f));
            var parts = new List<MeshPart>();
            Mesh lens = MeshKit.Cylinder(Mathf.Min(0.12f, options.LatticePitch * 0.3f), 0.04f, 8);
            Quaternion facing = Quaternion.Euler(90f, 0f, 0f);
            Quaternion inverse = Quaternion.Inverse(t.rotation);
            for (int i = 0; i < origins.Length; i++)
                parts.Add(new MeshPart(lens, inverse * (origins[i] - options.Center), facing));
            GadgetKit.Visual(t, "Lenses", MeshKit.Merge("LaserLenses", parts), Materials.Gadget(Palette.Hazard));
        }

        // Two crossed strips per beam, in world space; only the far ends move.
        Mesh BuildBeams()
        {
            var go = new GameObject("Beams " + Name);
            go.transform.SetParent(Ctx.Root, false);
            var mesh = new Mesh { name = "LaserBeams", hideFlags = HideFlags.DontSave };
            int n = origins.Length;
            if (n * 8 > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var uvs = new List<Vector2>(n * 8);
            var triangles = new List<int>(n * 12);
            for (int i = 0; i < n; i++)
            {
                for (int strip = 0; strip < 2; strip++)
                {
                    int first = beamVertices.Count;
                    for (int v = 0; v < 4; v++) beamVertices.Add(Vector3.zero);
                    uvs.Add(new Vector2(0f, 0f));
                    uvs.Add(new Vector2(1f, 0f));
                    uvs.Add(new Vector2(1f, 1f));
                    uvs.Add(new Vector2(0f, 1f));
                    triangles.Add(first);
                    triangles.Add(first + 1);
                    triangles.Add(first + 2);
                    triangles.Add(first);
                    triangles.Add(first + 2);
                    triangles.Add(first + 3);
                }
            }
            FillBeamVertices();
            mesh.SetVertices(beamVertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            // ART_BIBLE 4.4: additive, Hazard x 3, soft across the beam.
            go.AddComponent<MeshRenderer>().sharedMaterial = Materials.Flat(new FlatRecipe
            {
                Name = "Laser Beam", Color = Palette.Hazard.Emission, Blend = FlatBlend.Additive, Soft = 0.5f,
            });
            return mesh;
        }

        void FillBeamVertices()
        {
            float half = options.BeamWidth * 0.5f;
            for (int i = 0; i < origins.Length; i++)
            {
                Vector3 from = origins[i], to = from + direction * lengths[i];
                int first = i * 8;
                beamVertices[first] = from - right * half;
                beamVertices[first + 1] = from + right * half;
                beamVertices[first + 2] = to + right * half;
                beamVertices[first + 3] = to - right * half;
                beamVertices[first + 4] = from - up * half;
                beamVertices[first + 5] = from + up * half;
                beamVertices[first + 6] = to + up * half;
                beamVertices[first + 7] = to - up * half;
            }
        }

        void UpdateBeams()
        {
            beamsDirty = false;
            if (beamMesh == null) return;
            FillBeamVertices();
            beamMesh.SetVertices(beamVertices);
            beamMesh.RecalculateBounds();
        }

        public override void Reset()
        {
            base.Reset();
            exposedTicks = 0;
            Exposed = false;
        }
    }
}
