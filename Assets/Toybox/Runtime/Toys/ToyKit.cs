using System;
using System.Collections.Generic;
using Toybox.Art;
using UnityEngine;

namespace Toybox.Toys
{
    /// <summary>
    /// What a toy is painted with: its recipe and colour, and whether it is a toy at all. A grabbable toy
    /// carries a candy colour, the rim and a pool; the same shape as a set piece (a barricade block, a
    /// pedestal spool) is a plain prop in Birch, Kraft, Steel or a dip tone (ART_BIBLE 2.5 rules 3 and 4).
    /// </summary>
    internal readonly struct ToyLook
    {
        public readonly ToyRecipe Recipe;
        public readonly Color Color;
        public readonly bool Grabbable;

        ToyLook(ToyRecipe recipe, Color color, bool grabbable)
        {
            Recipe = recipe;
            Color = color;
            Grabbable = grabbable;
        }

        public static ToyLook Of(ToyRecipe recipe, Color candy, Color? color = null, bool grabbable = true) =>
            grabbable ? new ToyLook(recipe, color ?? candy, true) : new ToyLook(ToyRecipe.PlainProp, color ?? Palette.Birch, false);

        /// <summary>Machinery that is nobody's toy: an Ink satin body (ART_BIBLE 2.5 rule 5).</summary>
        public static ToyLook Gadget => new ToyLook(ToyRecipe.GadgetBody, Palette.Ink, false);

        public Material Main => Materials.Toy(Recipe, Color);

        /// <summary>The main material, plus the back shell of recipes that have one (glass).</summary>
        public Material[] MainSet => Materials.ToySet(Recipe, Color);

        /// <summary>The main colour in another recipe (the thread on a spool, the tape on a box).</summary>
        public Material Also(ToyRecipe recipe) => Materials.Toy(Grabbable ? recipe : ToyRecipe.PlainProp, Color);

        /// <summary>
        /// A detail in Paper or Ink - the only colours a toy carries besides its candy. On a set piece
        /// Paper becomes Kraft: nothing there may read brighter than the room.
        /// </summary>
        public Material Detail(Color color, ToyRecipe recipe = null)
        {
            if (!Grabbable) return Materials.Toy(ToyRecipe.PlainProp, Palette.Same(color, Palette.Paper) ? Palette.Kraft : color);
            return Materials.Toy(recipe ?? Recipe, color);
        }

        /// <summary>
        /// The toy's own colour in shade: holes, pores, anything that should read as a hollow. Matte, no
        /// rim and no glint, so nothing in it catches the light.
        /// </summary>
        public Material Shade(float gain = 0.5f)
        {
            ToyRecipe source = Recipe;
            ToyRecipe hollow = source.With(r =>
            {
                r.Name = source.Name + " Hollow";
                r.BaseGain = source.BaseGain * gain;
                r.Smoothness = Mathf.Min(source.Smoothness, 0.15f);
                r.Coat = 0f;
                r.Glint = 0f;
                r.Rim = 0f;
                r.Env = Mathf.Min(source.Env, 0.1f);
                r.SelfGlow = 0f;
            });
            return Materials.Toy(hollow, Color);
        }

        public string Hex => Palette.ToHex(Color);
    }

    /// <summary>
    /// Assembles a toy: a root GameObject at the toy's origin, visuals merged into one mesh per material
    /// (the draw budget is three), colliders, and the ToyInfo tag. Meshes are built once per key and
    /// shared by every toy with that key; the lambdas that build them only run on first use, and the
    /// part meshes they return are destroyed after the merge.
    /// </summary>
    internal sealed class ToyKit
    {
        /// <summary>
        /// While true, toys are created inactive and stay that way: the catalog measures a toy without
        /// its colliders ever entering a physics scene.
        /// </summary>
        public static bool Dormant;

        readonly string key;
        int visuals;

        public GameObject Root { get; }

        /// <param name="name">The toy's name (the prop takes it over).</param>
        /// <param name="key">Identifies the geometry: the name plus every number its meshes depend on.</param>
        public ToyKit(string name, string key)
        {
            this.key = key;
            Root = new GameObject(name);
            if (Dormant) Root.SetActive(false);
        }

        /// <summary>An empty child, for a part that moves by itself (fan blades) or that a gadget switches off.</summary>
        public Transform Child(string name, Vector3 localPosition = default, Transform parent = null)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent != null ? parent : Root.transform, false);
            child.transform.localPosition = localPosition;
            return child.transform;
        }

        /// <summary>
        /// One draw: the parts merged into one mesh with one material. Names that start with "Detail" mark
        /// what is printed or stuck on the toy's body (pips, panels, holes); silhouettes cut those out.
        /// </summary>
        public MeshRenderer Visual(string name, Material material, Func<IList<MeshPart>> parts, Transform parent = null) =>
            Visual(name, new[] { material }, parts, parent);

        public MeshRenderer Visual(string name, Material[] materials, Func<IList<MeshPart>> parts, Transform parent = null)
        {
            string meshKey = key + "/" + visuals++ + " " + name;
            string meshName = Root.name + " " + name;
            Mesh mesh = MeshKit.Cached(meshKey, () =>
            {
                IList<MeshPart> list = parts();
                Mesh merged = MeshKit.Merge(meshName, list);
                var released = new HashSet<Mesh>();
                foreach (MeshPart part in list)
                    if (part.Mesh != null && released.Add(part.Mesh)) MeshKit.Release(part.Mesh);
                return merged;
            });
            var visual = new GameObject(name);
            visual.transform.SetParent(parent != null ? parent : Root.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            return renderer;
        }

        public BoxCollider Box(Vector3 size, Vector3 center = default, Transform on = null)
        {
            BoxCollider box = (on != null ? on.gameObject : Root).AddComponent<BoxCollider>();
            box.size = size;
            box.center = center;
            return box;
        }

        public SphereCollider Sphere(float radius, Vector3 center = default)
        {
            SphereCollider sphere = Root.AddComponent<SphereCollider>();
            sphere.radius = radius;
            sphere.center = center;
            return sphere;
        }

        /// <summary>A convex mesh collider from the hull's corner points, in the toy's own space.</summary>
        public MeshCollider Hull(string name, Func<IList<Vector3>> points)
        {
            Mesh hull = ToyHull.Cached(key + "/" + name, points);
            MeshCollider collider = Root.AddComponent<MeshCollider>();
            collider.convex = true;
            collider.sharedMesh = hull;
            return collider;
        }

        /// <summary>Tags the toy for render and audio and hands it over.</summary>
        public GameObject Finish(ToyLook look, params Vector4[] poolProxies)
        {
            ToyInfo.Tag(Root, look.Recipe, look.Color, poolProxies);
            return Root;
        }

        // ---- Shorthand for parts -----------------------------------------------------------------------

        public static MeshPart At(Mesh mesh, Vector3 position) => new MeshPart(mesh, position);

        public static MeshPart At(Mesh mesh, Vector3 position, Quaternion rotation) => new MeshPart(mesh, position, rotation);

        /// <summary>A part whose own +Y axis is turned onto <paramref name="up"/> (a dot on a wall, a disc on a slope).</summary>
        public static MeshPart Facing(Mesh mesh, Vector3 position, Vector3 up) =>
            new MeshPart(mesh, position, Quaternion.FromToRotation(Vector3.up, up.normalized));
    }
}
