using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Levels
{
    /// <summary>
    /// The looks of a level's set: boxes, discs and books that are only drawn. Nothing added here has a
    /// collider - whatever must stop the player or a held toy is a collider the level adds itself, and the
    /// looks stay inside it. Parts are collected per material and merged into one mesh each when the set is
    /// finished (level statics are a draw per material, ART_BIBLE 12.1).
    ///
    /// Shared: it was written for Level 1 and keeps that name (a class and its file are renamed together,
    /// in the editor), but most levels build their sets with it by now. Add to it; do not change what an
    /// existing call draws for one level's sake, and run the levels' tests after a change (each checks
    /// what its level draws: no colliders, the draw budget). Levels 7, 12 and 15 have their own
    /// (Level07Set, Level12Set, Level15Set); all of them keep their meshes in <see cref="SetMeshes"/>.
    /// </summary>
    sealed class Level01Set
    {
        /// <summary>Which side of a book lying flat shows its spine; the other three show the pages.</summary>
        public enum Side
        {
            NegX,
            PosX,
            NegZ,
            PosZ,
        }

        readonly List<Material> materials = new List<Material>();
        readonly List<List<MeshPart>> parts = new List<List<MeshPart>>();

        /// <summary>A box. <paramref name="tone"/> darkens it (1 leaves the material's tone alone).</summary>
        public void Box(Material material, Vector3 centre, Vector3 size, Quaternion rotation, float tone = 1f)
        {
            Mesh mesh = MeshKit.Cached(MeshKit.Key("Box", size.x, size.y, size.z), () => MeshKit.Box(size));
            Add(material, mesh, centre, rotation, tone);
        }

        public void Box(Material material, Vector3 centre, Vector3 size, float tone = 1f) =>
            Box(material, centre, size, Quaternion.identity, tone);

        /// <summary>A flat disc: a cylinder with its axis along the rotation's up, round enough to stand next to.</summary>
        public void Disc(Material material, Vector3 centre, float radius, float thickness, Quaternion rotation, float tone = 1f)
        {
            const int sides = 64;
            Mesh mesh = MeshKit.Cached(MeshKit.Key("Disc", radius, thickness, sides), () => MeshKit.Cylinder(radius, thickness, sides));
            Add(material, mesh, centre, rotation, tone);
        }

        /// <summary>
        /// A shape of the level's own: a mesh that is shared (MeshKit.Cached) or lives as long as the level
        /// does, where it is to be drawn.
        /// </summary>
        public void Shape(Material material, Mesh mesh, Vector3 centre, Quaternion rotation, float tone = 1f) =>
            Add(material, mesh, centre, rotation, tone);

        /// <summary>
        /// A picture book in the box centre +- size / 2, turned by <paramref name="rotation"/> about that
        /// centre: two covers and a spine in the cover material, and a block of pages that stands back from
        /// the other three sides. The covers fill the box, so a collider of the same box is never smaller
        /// than what is drawn.
        /// </summary>
        public void Book(Material cover, Material pages, Vector3 centre, Vector3 size, Side spine, Quaternion rotation, float tone = 1f, Material topCover = null)
        {
            float board = Mathf.Min(0.12f, size.y * 0.12f);
            // The pages stand back by a hair only: a deeper reveal lies in the cover's own shadow, which the
            // shadow map draws as a comb.
            float inset = Mathf.Min(0.04f, Mathf.Min(size.x, size.z) * 0.05f);
            float inner = size.y - 2f * board;
            Vector3 up = Vector3.up * ((size.y - board) * 0.5f);
            Place(topCover != null ? topCover : cover, centre, rotation, up, new Vector3(size.x, board, size.z), tone);
            Place(cover, centre, rotation, -up, new Vector3(size.x, board, size.z), tone);

            // The spine closes the back between the two covers, flush with their edges.
            bool alongX = spine == Side.NegX || spine == Side.PosX;
            float sign = spine == Side.NegX || spine == Side.NegZ ? -1f : 1f;
            Vector3 spineSize = alongX ? new Vector3(board, inner, size.z) : new Vector3(size.x, inner, board);
            Vector3 spineAt = alongX ? new Vector3(sign * (size.x - board) * 0.5f, 0f, 0f) : new Vector3(0f, 0f, sign * (size.z - board) * 0.5f);
            Place(cover, centre, rotation, spineAt, spineSize, tone);

            // The pages: up against the spine, standing back from the other three sides.
            Vector3 pageSize = alongX ? new Vector3(size.x - inset - board, inner, size.z - 2f * inset) : new Vector3(size.x - 2f * inset, inner, size.z - inset - board);
            Vector3 pageAt = alongX ? new Vector3(sign * (inset - board) * 0.5f, 0f, 0f) : new Vector3(0f, 0f, sign * (inset - board) * 0.5f);
            Place(pages, centre, rotation, pageAt, pageSize, 1f);
        }

        /// <summary>
        /// A dashed line from <paramref name="from"/> to <paramref name="to"/> painted on a surface whose
        /// outward direction is <paramref name="normal"/>: flat strips of the given width, lying a hair proud
        /// of the surface. Both ends carry a dash.
        /// </summary>
        public void Dashes(Material paint, Vector3 from, Vector3 to, Vector3 normal, float width, float dash, float gap, float tone = 1f)
        {
            const float proud = 0.03f, thickness = 0.02f;
            Vector3 along = to - from;
            float length = along.magnitude;
            if (length < 1e-4f) return;
            along /= length;
            // As many whole dashes as fit, the gaps stretched so that the first and the last end on the ends.
            int count = Mathf.Max(1, Mathf.FloorToInt((length + gap) / (dash + gap)));
            float step = count > 1 ? (length - dash) / (count - 1) : 0f;
            Quaternion rotation = Quaternion.LookRotation(along, normal);
            for (int i = 0; i < count; i++)
            {
                Vector3 centre = from + along * (dash * 0.5f + step * i) + normal * (proud + thickness * 0.5f);
                Box(paint, centre, new Vector3(width, thickness, Mathf.Min(dash, length)), rotation, tone);
            }
        }

        /// <summary>
        /// Takes over the looks of a set piece that is already in the level (a block, a spool): its meshes
        /// join the set where they stand and its own renderers go, so a dozen pieces of one material are one
        /// draw. Its colliders stay where they are.
        /// </summary>
        public void Absorb(GameObject piece) => Absorb(piece, null);

        /// <summary>
        /// As <see cref="Absorb(GameObject)"/>, for a piece some of whose looks cast no shadow (a block's
        /// paper labels, a post's foot): those join <paramref name="unshadowed"/> - a set that is finished
        /// with castShadows: false - and the rest join this one. What cast a shadow before still does, and
        /// what did not does not.
        /// </summary>
        public void Absorb(GameObject piece, Level01Set unshadowed)
        {
            foreach (MeshRenderer renderer in piece.GetComponentsInChildren<MeshRenderer>())
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                Level01Set into = unshadowed != null && renderer.shadowCastingMode == ShadowCastingMode.Off ? unshadowed : this;
                into.Add(renderer.sharedMaterial, filter.sharedMesh, renderer.transform.localToWorldMatrix, Color.white);
                Sim.Destroy(renderer);
                Sim.Destroy(filter);
            }
        }

        /// <summary>
        /// Merges what was collected into one renderer per material under a new object of the level. The
        /// meshes are kept for the level's next build (<see cref="SetMeshes"/>): a restart draws the same
        /// set again. A set that stands between the sun and the level can be drawn without casting a shadow.
        /// </summary>
        public GameObject Finish(LevelContext ctx, string name, bool castShadows = true)
        {
            var root = new GameObject(name);
            for (int i = 0; i < materials.Count; i++)
            {
                Mesh merged = SetMeshes.Take(ctx, name + " " + i, parts[i]);
                var visual = new GameObject(materials[i] != null ? materials[i].name : "Visual");
                visual.transform.SetParent(root.transform, false);
                visual.AddComponent<MeshFilter>().sharedMesh = merged;
                MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = materials[i];
                if (!castShadows) renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            materials.Clear();
            parts.Clear();
            return ctx.AddStatic(root, Vector3.zero);
        }

        void Place(Material material, Vector3 centre, Quaternion rotation, Vector3 offset, Vector3 size, float tone) =>
            Box(material, centre + rotation * offset, size, rotation, tone);

        void Add(Material material, Mesh mesh, Vector3 centre, Quaternion rotation, float tone) =>
            Add(material, mesh, Matrix4x4.TRS(centre, rotation, Vector3.one), new Color(tone, tone, tone, 1f));

        void Add(Material material, Mesh mesh, Matrix4x4 matrix, Color tint)
        {
            int index = materials.IndexOf(material);
            if (index < 0)
            {
                index = materials.Count;
                materials.Add(material);
                parts.Add(new List<MeshPart>());
            }
            parts[index].Add(new MeshPart(mesh, matrix, tint));
        }
    }

    /// <summary>
    /// The merged meshes of the levels' sets, kept from one build of a level to its next. R is pressed
    /// often in a puzzle game, a restart is a whole new build, and merging a set anew every time was most
    /// of what a restart cost (Level 5: 15 MB of lists for the same meshes as before).
    ///
    /// A mesh is handed out again when the same level asks under the same name for the same parts: the
    /// same source meshes, in the same places, in the same tones - compared part by part, so a set that
    /// did change is simply merged again. What nobody uses any more goes when another level builds a set,
    /// or when the level it was made for has built again without asking for it: at most the sets of the
    /// levels that are loaded and of the one that was loaded last are alive.
    ///
    /// Do not modify what this returns.
    /// </summary>
    public static class SetMeshes
    {
        sealed class Entry
        {
            public string Owner, Name;
            public Mesh Mesh;
            public MeshPart[] Parts;
            public int Users, Build;
        }

        static readonly List<Entry> Entries = new List<Entry>();
        static readonly System.WeakReference LastBuild = new System.WeakReference(null);
        static int build;

        /// <summary>How many meshes were merged since the session began (a restart adds none).</summary>
        public static int Merges { get; private set; }

        /// <summary>How many merged meshes are kept right now, in use or not.</summary>
        public static int Kept => Entries.Count;

        /// <summary>
        /// The parts merged into one mesh (<see cref="MeshKit.Merge"/>): the one from the level's last build
        /// when the parts are the same, a new one otherwise. The level's use of it ends with the level.
        /// </summary>
        public static Mesh Take(LevelContext ctx, string name, List<MeshPart> parts)
        {
            string owner = ctx.Level != null ? ctx.Level.GetType().FullName : "";
            if (!ReferenceEquals(LastBuild.Target, ctx))
            {
                // The first set of a build: a new build has begun.
                LastBuild.Target = ctx;
                build++;
                Sweep(owner);
            }

            Entry entry = null;
            for (int i = 0; i < Entries.Count && entry == null; i++)
            {
                Entry candidate = Entries[i];
                // (A mesh can be a destroyed object if something unloaded all assets: it is merged again then.)
                if (candidate.Owner == owner && candidate.Name == name && candidate.Mesh != null && Same(candidate.Parts, parts)) entry = candidate;
            }
            if (entry == null)
            {
                entry = new Entry { Owner = owner, Name = name, Mesh = MeshKit.Merge(name, parts), Parts = parts.ToArray() };
                Entries.Add(entry);
                Merges++;
            }
            entry.Users++;
            entry.Build = build;
            Entry taken = entry;
            ctx.OnDispose(() => taken.Users--);
            return entry.Mesh;
        }

        // What nobody uses and nobody will ask for again: the sets of other levels, and those of this level
        // that its last build did not ask for.
        static void Sweep(string owner)
        {
            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                Entry entry = Entries[i];
                if (entry.Users > 0) continue;
                if (entry.Mesh != null && entry.Owner == owner && entry.Build >= build - 1) continue;
                MeshKit.Release(entry.Mesh);
                Entries.RemoveAt(i);
            }
        }

        static bool Same(MeshPart[] kept, List<MeshPart> asked)
        {
            if (kept.Length != asked.Count) return false;
            for (int i = 0; i < kept.Length; i++)
            {
                MeshPart a = kept[i], b = asked[i];
                // To the bit (Matrix4x4 == and Color == are approximate).
                if (!ReferenceEquals(a.Mesh, b.Mesh) || !a.Matrix.Equals(b.Matrix) || !a.Color.Equals(b.Color)) return false;
            }
            return true;
        }
    }
}
