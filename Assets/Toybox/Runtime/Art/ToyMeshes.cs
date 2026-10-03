using System.Collections.Generic;
using UnityEngine;

namespace Toybox.Art
{
    /// <summary>
    /// Unit-sized procedural meshes, built once and shared: every one fits the box [-0.5, 0.5]^3 with its
    /// origin at the center, so a toy sizes it with a child transform's scale. Do not modify or destroy them.
    /// </summary>
    public static class ToyMeshes
    {
        const int CylinderSides = 24;
        const int SphereRings = 16, SphereSegments = 24;

        static Mesh cube, sphere, cylinder, wedge;

        public static Mesh Cube => cube != null ? cube : cube = BuildCube();
        public static Mesh Sphere => sphere != null ? sphere : sphere = BuildSphere();
        /// <summary>Axis along Y.</summary>
        public static Mesh Cylinder => cylinder != null ? cylinder : cylinder = BuildCylinder();
        /// <summary>A ramp: full height at +Z, sloping down to nothing at -Z.</summary>
        public static Mesh Wedge => wedge != null ? wedge : wedge = BuildWedge();

        sealed class Builder
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<int> triangles = new List<int>();

            public int Vertex(Vector3 position, Vector3 normal)
            {
                vertices.Add(position);
                normals.Add(normal);
                return vertices.Count - 1;
            }

            public void Triangle(int a, int b, int c)
            {
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }

            /// <summary>A flat polygon with its own vertices. Corners are given clockwise as seen from outside.</summary>
            public void Face(params Vector3[] corners)
            {
                Vector3 normal = Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]).normalized;
                int first = vertices.Count;
                foreach (Vector3 corner in corners) Vertex(corner, normal);
                for (int i = 1; i + 1 < corners.Length; i++) Triangle(first, first + i, first + i + 1);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        static Mesh BuildCube()
        {
            const float h = 0.5f;
            var b = new Builder();
            b.Face(new Vector3(-h, h, -h), new Vector3(-h, h, h), new Vector3(h, h, h), new Vector3(h, h, -h));      // top
            b.Face(new Vector3(-h, -h, h), new Vector3(-h, -h, -h), new Vector3(h, -h, -h), new Vector3(h, -h, h));  // bottom
            b.Face(new Vector3(-h, -h, -h), new Vector3(-h, h, -h), new Vector3(h, h, -h), new Vector3(h, -h, -h));  // back (-Z)
            b.Face(new Vector3(h, -h, h), new Vector3(h, h, h), new Vector3(-h, h, h), new Vector3(-h, -h, h));      // front (+Z)
            b.Face(new Vector3(-h, -h, h), new Vector3(-h, h, h), new Vector3(-h, h, -h), new Vector3(-h, -h, -h));  // left
            b.Face(new Vector3(h, -h, -h), new Vector3(h, h, -h), new Vector3(h, h, h), new Vector3(h, -h, h));      // right
            return b.Build("Toy Cube");
        }

        static Mesh BuildWedge()
        {
            const float h = 0.5f;
            Vector3 backLeft = new Vector3(-h, -h, -h), backRight = new Vector3(h, -h, -h);
            Vector3 frontLeft = new Vector3(-h, -h, h), frontRight = new Vector3(h, -h, h);
            Vector3 topLeft = new Vector3(-h, h, h), topRight = new Vector3(h, h, h);
            var b = new Builder();
            b.Face(frontLeft, backLeft, backRight, frontRight);  // bottom
            b.Face(backLeft, topLeft, topRight, backRight);      // slope
            b.Face(frontRight, topRight, topLeft, frontLeft);    // tall face (+Z)
            b.Face(frontLeft, topLeft, backLeft);                // left side
            b.Face(backRight, topRight, frontRight);             // right side
            return b.Build("Toy Wedge");
        }

        static Mesh BuildCylinder()
        {
            const float h = 0.5f, r = 0.5f;
            var b = new Builder();
            var top = new Vector3[CylinderSides];
            var bottom = new Vector3[CylinderSides];
            for (int i = 0; i < CylinderSides; i++)
            {
                // Clockwise seen from above, which is the outward winding for the top cap.
                float angle = -i * Mathf.PI * 2f / CylinderSides;
                var rim = new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
                top[i] = rim + Vector3.up * h;
                bottom[CylinderSides - 1 - i] = rim + Vector3.down * h;
            }
            b.Face(top);
            b.Face(bottom);
            for (int i = 0; i < CylinderSides; i++)
            {
                int next = (i + 1) % CylinderSides;
                Vector3 a = top[i], c = top[next];
                Vector3 normalA = new Vector3(a.x, 0f, a.z).normalized, normalC = new Vector3(c.x, 0f, c.z).normalized;
                int v0 = b.Vertex(a, normalA);
                int v1 = b.Vertex(new Vector3(a.x, -h, a.z), normalA);
                int v2 = b.Vertex(new Vector3(c.x, -h, c.z), normalC);
                int v3 = b.Vertex(c, normalC);
                b.Triangle(v0, v2, v3);
                b.Triangle(v0, v1, v2);
            }
            return b.Build("Toy Cylinder");
        }

        static Mesh BuildSphere()
        {
            const float r = 0.5f;
            var b = new Builder();
            for (int ring = 0; ring <= SphereRings; ring++)
            {
                float polar = Mathf.PI * ring / SphereRings;
                float y = Mathf.Cos(polar), ringRadius = Mathf.Sin(polar);
                for (int segment = 0; segment <= SphereSegments; segment++)
                {
                    float angle = Mathf.PI * 2f * segment / SphereSegments;
                    var normal = new Vector3(Mathf.Cos(angle) * ringRadius, y, Mathf.Sin(angle) * ringRadius);
                    b.Vertex(normal * r, normal);
                }
            }
            int stride = SphereSegments + 1;
            for (int ring = 0; ring < SphereRings; ring++)
            {
                for (int segment = 0; segment < SphereSegments; segment++)
                {
                    int a = ring * stride + segment, c = a + stride;
                    if (ring > 0) b.Triangle(a, a + 1, c);
                    if (ring < SphereRings - 1) b.Triangle(a + 1, c + 1, c);
                }
            }
            return b.Build("Toy Sphere");
        }
    }
}
