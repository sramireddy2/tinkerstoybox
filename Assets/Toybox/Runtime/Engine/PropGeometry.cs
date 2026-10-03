using System.Collections.Generic;
using UnityEngine;

namespace Toybox.Engine
{
    /// <summary>
    /// Measures a toy's colliders in the toy's own space at scale 1: total volume (for mass), the center and
    /// half extents of their bounding box, and a bounding radius around that center.
    /// </summary>
    public static class PropGeometry
    {
        // A collider is reduced to "support spheres": the convex hull of these contains the collider.
        struct Support
        {
            public Vector3 Point;
            public float Radius;
        }

        public static void Measure(Transform root, Collider[] colliders, out float volume, out Vector3 center, out Vector3 halfExtents, out float radius)
        {
            var supports = new List<Support>();
            volume = 0f;
            Matrix4x4 toRoot = root.worldToLocalMatrix;
            foreach (Collider collider in colliders)
            {
                if (collider == null || collider.isTrigger) continue;
                Matrix4x4 m = toRoot * collider.transform.localToWorldMatrix;
                switch (collider)
                {
                    case BoxCollider box:
                        volume += MeasureBox(box, m, supports);
                        break;
                    case SphereCollider sphere:
                        volume += MeasureSphere(sphere, m, supports);
                        break;
                    case CapsuleCollider capsule:
                        volume += MeasureCapsule(capsule, m, supports);
                        break;
                    case MeshCollider mesh:
                        volume += MeasureMesh(mesh, m, supports);
                        break;
                }
            }

            if (supports.Count == 0)
            {
                volume = 1f;
                center = Vector3.zero;
                halfExtents = Vector3.one * 0.5f;
                radius = halfExtents.magnitude;
                return;
            }

            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (Support s in supports)
            {
                Vector3 r = new Vector3(s.Radius, s.Radius, s.Radius);
                min = Vector3.Min(min, s.Point - r);
                max = Vector3.Max(max, s.Point + r);
            }
            center = (min + max) * 0.5f;
            halfExtents = (max - min) * 0.5f;

            radius = 0f;
            foreach (Support s in supports)
                radius = Mathf.Max(radius, Vector3.Distance(s.Point, center) + s.Radius);
            volume = Mathf.Max(volume, 1e-6f);
        }

        /// <summary>
        /// Half the smallest dimension of the thinnest collider, measured along each collider's own axes
        /// (so a tilted slat counts as thin). Never more than <paramref name="limit"/>.
        /// </summary>
        public static float Thinness(Collider[] colliders, float limit)
        {
            float thinnest = limit;
            foreach (Collider collider in colliders)
            {
                if (collider == null || collider.isTrigger) continue;
                Vector3 s = collider.transform.lossyScale;
                s = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
                float smallest = Mathf.Min(s.x, Mathf.Min(s.y, s.z));
                switch (collider)
                {
                    case BoxCollider box:
                        Vector3 half = Vector3.Scale(box.size, s) * 0.5f;
                        thinnest = Mathf.Min(thinnest, Mathf.Min(Mathf.Abs(half.x), Mathf.Min(Mathf.Abs(half.y), Mathf.Abs(half.z))));
                        break;
                    case SphereCollider sphere:
                        thinnest = Mathf.Min(thinnest, sphere.radius * smallest);
                        break;
                    case CapsuleCollider capsule:
                        thinnest = Mathf.Min(thinnest, capsule.radius * smallest);
                        break;
                    case MeshCollider mesh when mesh.sharedMesh != null:
                        Vector3 extents = Vector3.Scale(mesh.sharedMesh.bounds.extents, s);
                        thinnest = Mathf.Min(thinnest, Mathf.Min(extents.x, Mathf.Min(extents.y, extents.z)));
                        break;
                }
            }
            return Mathf.Max(thinnest, 1e-4f);
        }

        static Vector3 AxisScale(Matrix4x4 m) =>
            new Vector3(m.GetColumn(0).magnitude, m.GetColumn(1).magnitude, m.GetColumn(2).magnitude);

        static float MeasureBox(BoxCollider box, Matrix4x4 m, List<Support> supports)
        {
            Vector3 half = box.size * 0.5f;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -half.x : half.x, (i & 2) == 0 ? -half.y : half.y, (i & 4) == 0 ? -half.z : half.z);
                supports.Add(new Support { Point = m.MultiplyPoint3x4(box.center + corner) });
            }
            Vector3 s = AxisScale(m);
            return Mathf.Abs(box.size.x * s.x * box.size.y * s.y * box.size.z * s.z);
        }

        // Unity scales a sphere collider by the largest axis of its transform.
        static float MeasureSphere(SphereCollider sphere, Matrix4x4 m, List<Support> supports)
        {
            Vector3 s = AxisScale(m);
            float r = sphere.radius * Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            supports.Add(new Support { Point = m.MultiplyPoint3x4(sphere.center), Radius = r });
            return 4f / 3f * Mathf.PI * r * r * r;
        }

        // Unity scales a capsule's height by its own axis and its radius by the larger of the other two.
        static float MeasureCapsule(CapsuleCollider capsule, Matrix4x4 m, List<Support> supports)
        {
            Vector3 s = AxisScale(m);
            int axis = capsule.direction;
            float along = s[axis];
            float across = Mathf.Max(s[(axis + 1) % 3], s[(axis + 2) % 3]);
            float r = capsule.radius * across;
            float height = Mathf.Max(capsule.height * along, 2f * r);
            float halfSegment = height * 0.5f - r;

            Vector3 localAxis = Vector3.zero;
            localAxis[axis] = 1f;
            Vector3 worldAxis = m.MultiplyVector(localAxis).normalized;
            Vector3 middle = m.MultiplyPoint3x4(capsule.center);
            supports.Add(new Support { Point = middle + worldAxis * halfSegment, Radius = r });
            supports.Add(new Support { Point = middle - worldAxis * halfSegment, Radius = r });
            return Mathf.PI * r * r * (2f * halfSegment) + 4f / 3f * Mathf.PI * r * r * r;
        }

        // Signed tetrahedron volumes against the origin; exact for a closed mesh (which a convex hull is).
        static float MeasureMesh(MeshCollider collider, Matrix4x4 m, List<Support> supports)
        {
            Mesh mesh = collider.sharedMesh;
            if (mesh == null) return 0f;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = m.MultiplyPoint3x4(vertices[i]);
                supports.Add(new Support { Point = vertices[i] });
            }
            double sum = 0.0;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]], b = vertices[triangles[i + 1]], c = vertices[triangles[i + 2]];
                sum += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
            }
            return Mathf.Abs((float)sum);
        }
    }
}
