using Toybox.Art;
using UnityEngine;

namespace Toybox.Toys
{
    /// <summary>
    /// The plain building blocks. Each factory returns a toy: a GameObject with renderers and colliders,
    /// authored at scale 1 with its origin at the center of its bounds and no Rigidbody. Pass it to
    /// LevelContext.AddProp to make it a physics prop, or to AddStatic to make it part of the world.
    /// </summary>
    public static class BasicToys
    {
        public static GameObject Block(Vector3 size, Color? color = null)
        {
            var root = new GameObject("Block");
            root.AddComponent<BoxCollider>().size = size;
            Visual(root, ToyMeshes.Cube, size, color ?? ToyMaterials.Red);
            return root;
        }

        public static GameObject Block(float size, Color? color = null) => Block(new Vector3(size, size, size), color);

        public static GameObject Ball(float radius, Color? color = null)
        {
            var root = new GameObject("Ball");
            root.AddComponent<SphereCollider>().radius = radius;
            Visual(root, ToyMeshes.Sphere, Vector3.one * (radius * 2f), color ?? ToyMaterials.Teal);
            return root;
        }

        /// <summary>An upright cylinder (axis along Y) with a convex mesh collider.</summary>
        public static GameObject Cylinder(float radius, float height, Color? color = null)
        {
            var root = new GameObject("Cylinder");
            GameObject visual = Visual(root, ToyMeshes.Cylinder, new Vector3(radius * 2f, height, radius * 2f), color ?? ToyMaterials.Blue);
            Convex(visual, ToyMeshes.Cylinder);
            return root;
        }

        /// <summary>
        /// A ramp with a convex mesh collider: `length` along Z, `width` along X, rising from nothing at
        /// the -Z end to `height` at the +Z end.
        /// </summary>
        public static GameObject Wedge(float length, float height, float width, Color? color = null)
        {
            var root = new GameObject("Wedge");
            GameObject visual = Visual(root, ToyMeshes.Wedge, new Vector3(width, height, length), color ?? ToyMaterials.Yellow);
            Convex(visual, ToyMeshes.Wedge);
            return root;
        }

        /// <summary>A box meant for floors, walls and platforms (use with AddStatic or AddKinematic).</summary>
        public static GameObject Slab(Vector3 size, Color? color = null)
        {
            var root = new GameObject("Slab");
            root.AddComponent<BoxCollider>().size = size;
            Visual(root, ToyMeshes.Cube, size, color ?? ToyMaterials.Floor, ToyMaterialKind.Matte);
            return root;
        }

        static GameObject Visual(GameObject root, Mesh mesh, Vector3 size, Color color, ToyMaterialKind kind = ToyMaterialKind.Plastic)
        {
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = size;
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            visual.AddComponent<MeshRenderer>().sharedMaterial = ToyMaterials.Get(kind, color);
            return visual;
        }

        static void Convex(GameObject target, Mesh mesh)
        {
            MeshCollider collider = target.AddComponent<MeshCollider>();
            collider.convex = true;
            collider.sharedMesh = mesh;
        }
    }
}
