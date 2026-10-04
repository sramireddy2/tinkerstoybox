using Toybox.Art;
using UnityEngine;

namespace Toybox.Toys
{
    /// <summary>
    /// The plain building blocks. Each factory returns a toy: a GameObject with renderers and colliders,
    /// authored at scale 1 with its origin at the center of its bounds and no Rigidbody. Pass it to
    /// LevelContext.AddProp to make it a physics prop, or to AddStatic to make it part of the world.
    ///
    /// Meshes are built at their real size (no scaled visual), so their UVs are one repeat per unit, and
    /// they are shared between toys of the same size. Toys are glossy plastic in a candy colour and carry
    /// a <see cref="ToyInfo"/>; slabs are room surfaces in the dip of the level being built.
    /// </summary>
    public static class BasicToys
    {
        /// <summary>Every edge of a toy is bevelled by this fraction of its smallest dimension (ART_BIBLE 4.1).</summary>
        public const float BevelFraction = 0.04f;

        public static GameObject Block(Vector3 size, Color? color = null)
        {
            var root = new GameObject("Block");
            root.AddComponent<BoxCollider>().size = size;
            float bevel = Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * BevelFraction;
            Mesh mesh = MeshKit.Cached(MeshKit.Key("RoundedBox", size.x, size.y, size.z, bevel), () => MeshKit.RoundedBox(size, bevel));
            Toy(root, mesh, color ?? Palette.Cherry);
            return root;
        }

        public static GameObject Block(float size, Color? color = null) => Block(new Vector3(size, size, size), color);

        public static GameObject Ball(float radius, Color? color = null)
        {
            var root = new GameObject("Ball");
            root.AddComponent<SphereCollider>().radius = radius;
            Mesh mesh = MeshKit.Cached(MeshKit.Key("Sphere", radius), () => MeshKit.Sphere(radius));
            Toy(root, mesh, color ?? Palette.Lagoon);
            return root;
        }

        /// <summary>An upright cylinder (axis along Y) with a convex mesh collider.</summary>
        public static GameObject Cylinder(float radius, float height, Color? color = null)
        {
            var root = new GameObject("Cylinder");
            float bevel = Mathf.Min(radius * 2f, height) * BevelFraction;
            Mesh mesh = MeshKit.Cached(MeshKit.Key("Cylinder", radius, height, bevel), () => MeshKit.Cylinder(radius, height, 24, bevel));
            Toy(root, mesh, color ?? Palette.Bubblegum);
            Hull(root, MeshKit.UnitCylinderHull, new Vector3(radius * 2f, height, radius * 2f));
            return root;
        }

        /// <summary>
        /// A ramp with a convex mesh collider: `length` along Z, `width` along X, rising from nothing at
        /// the -Z end to `height` at the +Z end.
        /// </summary>
        public static GameObject Wedge(float length, float height, float width, Color? color = null)
        {
            var root = new GameObject("Wedge");
            Mesh mesh = MeshKit.Cached(MeshKit.Key("Wedge", width, height, length), () => MeshKit.Wedge(width, height, length));
            Toy(root, mesh, color ?? Palette.Lemon);
            Hull(root, MeshKit.UnitWedgeHull, new Vector3(width, height, length));
            return root;
        }

        /// <summary>
        /// A box meant for floors, walls and platforms (use with AddStatic or AddKinematic): a level static
        /// in the room's dip, light on top and deep on the sides.
        /// </summary>
        public static GameObject Slab(Vector3 size) => Slab(size, Materials.Room(RoomSurface.LevelStatic));

        /// <summary>A slab in one plain colour (test geometry, a platform that has to stand out).</summary>
        public static GameObject Slab(Vector3 size, Color color) => Slab(size, Materials.Room(RoomRecipe.Solid(color)));

        public static GameObject Slab(Vector3 size, Material material)
        {
            var root = new GameObject("Slab");
            root.AddComponent<BoxCollider>().size = size;
            Mesh mesh = MeshKit.Cached(MeshKit.Key("Box", size.x, size.y, size.z), () => MeshKit.Box(size));
            Visual(root, mesh, material);
            return root;
        }

        /// <summary>
        /// A ramp that is part of the world (use with AddStatic): the wedge's shape - `length` along Z, rising
        /// to `height` at the +Z end - as a level static in the room's dip.
        /// </summary>
        public static GameObject Ramp(float length, float height, float width, Material material = null)
        {
            var root = new GameObject("Ramp");
            Mesh mesh = MeshKit.Cached(MeshKit.Key("Wedge", width, height, length), () => MeshKit.Wedge(width, height, length));
            Visual(root, mesh, material != null ? material : Materials.Room(RoomSurface.LevelStatic));
            Hull(root, MeshKit.UnitWedgeHull, new Vector3(width, height, length));
            return root;
        }

        static void Toy(GameObject root, Mesh mesh, Color candy)
        {
            Visual(root, mesh, Materials.Toy(ToyRecipe.GlossyPlastic, candy));
            ToyInfo.Tag(root, ToyRecipe.GlossyPlastic, candy);
        }

        static GameObject Visual(GameObject root, Mesh mesh, Material material)
        {
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            visual.AddComponent<MeshRenderer>().sharedMaterial = material;
            return visual;
        }

        // The collision hulls are unit-sized and take their size from the transform, as they always have.
        static void Hull(GameObject root, Mesh hull, Vector3 size)
        {
            var holder = new GameObject("Collider");
            holder.transform.SetParent(root.transform, false);
            holder.transform.localScale = size;
            MeshCollider collider = holder.AddComponent<MeshCollider>();
            collider.convex = true;
            collider.sharedMesh = hull;
        }
    }
}
