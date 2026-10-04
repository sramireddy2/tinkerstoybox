using System;
using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    public sealed class FunnelOptions
    {
        public string Name = "Funnel";
        /// <summary>The funnel's axis: x and z.</summary>
        public Vector2 Axis;
        /// <summary>Height of the rim (the mouth).</summary>
        public float RimY;
        public float MouthRadius = 1f;
        /// <summary>The size gate: a ball passes when its radius is smaller.</summary>
        public float ThroatRadius = 0.4f;
        /// <summary>The cone goes from the mouth down this far to the throat.</summary>
        public float ConeDepth = 0.4f;
        /// <summary>The straight tube under the throat.</summary>
        public float TubeLength = 1f;
        /// <summary>0: the tube is open at the bottom. Otherwise a closed chamber of this height hangs under it.</summary>
        public float ChamberHeight;
        /// <summary>Radius of the chamber (0: the mouth's radius).</summary>
        public float ChamberRadius;
        /// <summary>A flat lip from the mouth out to this radius (0: none).</summary>
        public float OuterRadius;
        /// <summary>With an <see cref="OuterRadius"/>: an outside wall from the lip down to this height (a free-standing cup).</summary>
        public float? OuterBaseY;
        /// <summary>
        /// Half size (x, z) of a rectangular collar at rim height around the mouth: the piece of floor the
        /// funnel is set in, so that a level can lay the rest of its floor from boxes around it.
        /// </summary>
        public Vector2 CollarHalfSize;
        public int Segments = 32;
        /// <summary>
        /// True: the size gate is exact for balls. A ball whose radius is not smaller than the throat's is
        /// kept from squeezing through it (the solver alone lets one that is a few percent too big slip
        /// through between opposing contacts). Costs one cheap check per ball near the funnel per tick.
        /// </summary>
        public bool HardGate = true;
        public float Friction = 0.05f;
        /// <summary>The surface it is drawn with (null: a level static in the room's dip).</summary>
        public Material Material;
    }

    /// <summary>What <see cref="Funnel.Build"/> made, and the zones gadgets around it need.</summary>
    public sealed class FunnelResult
    {
        public GameObject GameObject;
        /// <summary>The cylinder under the mouth, from the lowest point to the rim.</summary>
        public Zone Footprint;
        /// <summary>The inside of the chamber (invalid without one).</summary>
        public Zone ChamberBox;
        /// <summary>The middle of the chamber's floor - where the plate is - or of the tube's lower end.</summary>
        public Vector3 PlateCenter;
        /// <summary>Height of the throat (the top of the tube).</summary>
        public float ThroatY;
        /// <summary>The lowest point: the chamber's floor, or the tube's lower end.</summary>
        public float FloorY;
    }

    /// <summary>
    /// A funnel with a size gate (LEVELS 2.1): mouth, cone, throat, tube and an optional closed chamber, as
    /// one static non-convex mesh collider. Every ring is a polygon circumscribed about its nominal radius
    /// (apothem = radius), so no clearance is smaller than specified. Static builder, not ticked.
    /// </summary>
    public static class Funnel
    {
        public static FunnelResult Build(LevelContext ctx, FunnelOptions options)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.ThroatRadius <= 0f || options.MouthRadius < options.ThroatRadius)
                throw new ArgumentException("A funnel needs 0 < ThroatRadius <= MouthRadius.", nameof(options));

            int n = Mathf.Clamp(options.Segments, 6, 96);
            float chamberRadius = options.ChamberRadius > 0f ? options.ChamberRadius : options.MouthRadius;
            chamberRadius = Mathf.Max(chamberRadius, options.ThroatRadius);
            float outer = Mathf.Max(options.OuterRadius, options.MouthRadius);
            float throatY = -options.ConeDepth;
            float tubeY = throatY - options.TubeLength;
            float floorY = tubeY - Mathf.Max(0f, options.ChamberHeight);

            // The profile, from the outside in, in (radius, y) relative to the rim. Walking along it the
            // surface faces to the left: up on the lip and the cone, inward in the tube, down under the
            // chamber's ceiling, up on its floor.
            var profile = new List<Vector2>();
            if (options.OuterBaseY.HasValue && outer > 0f) profile.Add(new Vector2(outer, options.OuterBaseY.Value - options.RimY));
            if (outer > options.MouthRadius + 1e-4f || profile.Count > 0) profile.Add(new Vector2(outer, 0f));
            profile.Add(new Vector2(options.MouthRadius, 0f));
            profile.Add(new Vector2(options.ThroatRadius, throatY));
            if (options.TubeLength > 1e-4f) profile.Add(new Vector2(options.ThroatRadius, tubeY));
            if (options.ChamberHeight > 1e-4f)
            {
                if (chamberRadius > options.ThroatRadius + 1e-4f) profile.Add(new Vector2(chamberRadius, tubeY));
                profile.Add(new Vector2(chamberRadius, floorY));
                profile.Add(new Vector2(0f, floorY));
            }

            var builder = new ShellBuilder();
            float circumscribe = 1f / Mathf.Cos(Mathf.PI / n);
            for (int i = 0; i + 1 < profile.Count; i++)
            {
                Vector2 a = profile[i], b = profile[i + 1];
                Vector2 along = b - a;
                if (along.sqrMagnitude < 1e-10f) continue;
                var normal2 = new Vector2(along.y, -along.x).normalized;
                for (int j = 0; j < n; j++)
                {
                    float t0 = Mathf.PI * 2f * j / n, t1 = Mathf.PI * 2f * (j + 1) / n;
                    Vector3 a0 = Ring(a, t0, circumscribe), a1 = Ring(a, t1, circumscribe);
                    Vector3 b0 = Ring(b, t0, circumscribe), b1 = Ring(b, t1, circumscribe);
                    Vector3 n0 = Normal(normal2, t0), n1 = Normal(normal2, t1);
                    if (b.x < 1e-5f) builder.Triangle(a0, a1, b0, n0, n1, Normal(normal2, (t0 + t1) * 0.5f));
                    else if (a.x < 1e-5f) builder.Triangle(a0, b1, b0, n0, n1, n0);
                    else builder.Quad(a0, a1, b1, b0, n0, n1, n1, n0);
                }
            }

            // The collar: the floor from the outermost ring out to a rectangle, facing up.
            Vector2 collar = options.CollarHalfSize;
            if (collar.x > outer * circumscribe && collar.y > outer * circumscribe && !options.OuterBaseY.HasValue)
            {
                var ringPoint = new Vector2(outer, 0f);
                for (int j = 0; j < n; j++)
                {
                    float t0 = Mathf.PI * 2f * j / n, t1 = Mathf.PI * 2f * (j + 1) / n;
                    Vector3 v0 = Ring(ringPoint, t0, circumscribe), v1 = Ring(ringPoint, t1, circumscribe);
                    Vector3 r0 = OnRectangle(t0, collar, out int side0), r1 = OnRectangle(t1, collar, out int side1);
                    builder.Quad(r0, r1, v1, v0, Vector3.up, Vector3.up, Vector3.up, Vector3.up);
                    // The two rays leave through different sides: the quad cut the corner between them off.
                    if (side0 == side1) continue;
                    float mid = (t0 + t1) * 0.5f;
                    var corner = new Vector3(Mathf.Sign(Mathf.Cos(mid)) * collar.x, 0f, Mathf.Sign(Mathf.Sin(mid)) * collar.y);
                    builder.Triangle(r0, corner, r1, Vector3.up, Vector3.up, Vector3.up);
                }
            }

            Mesh mesh = builder.Build("Funnel " + options.Name);
            var root = new GameObject("Funnel " + options.Name) { layer = Layers.Default };
            MeshCollider collider = root.AddComponent<MeshCollider>();
            collider.convex = false;
            collider.sharedMesh = mesh;
            var physicsMaterial = new PhysicsMaterial("Funnel")
            {
                dynamicFriction = options.Friction,
                staticFriction = options.Friction,
                bounciness = 0f,
                // The slippery cone wins against whatever rolls in it.
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Average,
            };
            collider.sharedMaterial = physicsMaterial;
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterial = options.Material != null ? options.Material : Materials.Room(RoomSurface.LevelStatic);
            ctx.AddStatic(root, new Vector3(options.Axis.x, options.RimY, options.Axis.y));
            ctx.OnDispose(() =>
            {
                Sim.Destroy(physicsMaterial);
                Sim.Destroy(mesh);
            });

            if (options.HardGate) ctx.OnUpdate(dt => HoldBack(ctx.Game, options, options.RimY + throatY, options.RimY + tubeY));

            float bottom = options.RimY + floorY;
            var result = new FunnelResult
            {
                GameObject = root,
                ThroatY = options.RimY + throatY,
                FloorY = bottom,
                PlateCenter = new Vector3(options.Axis.x, bottom, options.Axis.y),
                Footprint = Zone.Cylinder(options.Axis.x, options.Axis.y, options.MouthRadius, bottom, options.RimY),
            };
            if (options.ChamberHeight > 1e-4f)
                result.ChamberBox = Zone.Box(new Vector3(options.Axis.x, bottom + options.ChamberHeight * 0.5f, options.Axis.y),
                    new Vector3(chamberRadius * 2f, options.ChamberHeight, chamberRadius * 2f));
            return result;
        }

        // A ball at least as big as the throat cannot have its centre lower than where it rests on the
        // throat's rim. There the rim carries it; one that has been pressed lower is put back.
        static void HoldBack(Game game, FunnelOptions options, float throatY, float tubeY)
        {
            IReadOnlyList<Prop> props = game.Props;
            for (int i = 0; i < props.Count; i++)
            {
                Prop prop = props[i];
                if (!GadgetKit.IsLoose(prop) || prop.Body.isKinematic || prop.Colliders.Length != 1) continue;
                if (!(prop.Colliders[0] is SphereCollider sphere)) continue;
                float radius = sphere.radius * prop.Scale;
                if (radius < options.ThroatRadius) continue;
                Vector3 centre = prop.Center;
                float dx = centre.x - options.Axis.x, dz = centre.z - options.Axis.y;
                if (dx * dx + dz * dz > options.MouthRadius * options.MouthRadius) continue;
                float lowest = throatY + Mathf.Sqrt(Mathf.Max(0f, radius * radius - options.ThroatRadius * options.ThroatRadius));
                // Above its resting height, or already under the tube (it was put there): not our business.
                if (centre.y >= lowest + 0.001f || centre.y < tubeY) continue;
                // It rests on the rim: the rim carries it. (The contacts there are nearly horizontal and
                // oppose each other, so the solver alone lets it sink.)
                Rigidbody body = prop.Body;
                body.AddForce(-Physics.gravity, ForceMode.Acceleration);
                Vector3 velocity = body.linearVelocity;
                if (velocity.y < 0f) body.linearVelocity = new Vector3(velocity.x, 0f, velocity.z);
                if (centre.y >= lowest - 0.002f) continue;
                Vector3 position = body.position;
                position.y += lowest - centre.y;
                body.position = position;
            }
        }

        /// <summary>Does a ball of this radius pass a throat of that radius?</summary>
        public static bool Passes(float ballRadius, float throatRadius) => ballRadius < throatRadius;

        static Vector3 Ring(Vector2 point, float angle, float circumscribe) =>
            new Vector3(Mathf.Cos(angle) * point.x * circumscribe, point.y, Mathf.Sin(angle) * point.x * circumscribe);

        static Vector3 Normal(Vector2 normal, float angle) =>
            new Vector3(Mathf.Cos(angle) * normal.x, normal.y, Mathf.Sin(angle) * normal.x);

        // Where the ray from the axis at this angle leaves the rectangle, and through which kind of side (0: an x side, 1: a z side).
        static Vector3 OnRectangle(float angle, Vector2 half, out int side)
        {
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
            float tx = Mathf.Abs(c) > 1e-6f ? half.x / Mathf.Abs(c) : float.MaxValue;
            float tz = Mathf.Abs(s) > 1e-6f ? half.y / Mathf.Abs(s) : float.MaxValue;
            side = tx <= tz ? 0 : 1;
            float t = Mathf.Min(tx, tz);
            return new Vector3(Mathf.Clamp(c * t, -half.x, half.x), 0f, Mathf.Clamp(s * t, -half.y, half.y));
        }

        /// <summary>Triangles with explicit normals; each is wound so that its front faces along them.</summary>
        sealed class ShellBuilder
        {
            readonly List<Vector3> positions = new List<Vector3>();
            readonly List<Vector3> normals = new List<Vector3>();
            readonly List<int> triangles = new List<int>();

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd)
            {
                Triangle(a, b, c, na, nb, nc);
                Triangle(a, c, d, na, nc, nd);
            }

            public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc)
            {
                Vector3 face = Vector3.Cross(b - a, c - a);
                if (face.sqrMagnitude < 1e-12f) return;
                int first = positions.Count;
                positions.Add(a);
                normals.Add(na.normalized);
                if (Vector3.Dot(face, na + nb + nc) >= 0f)
                {
                    positions.Add(b);
                    normals.Add(nb.normalized);
                    positions.Add(c);
                    normals.Add(nc.normalized);
                }
                else
                {
                    positions.Add(c);
                    normals.Add(nc.normalized);
                    positions.Add(b);
                    normals.Add(nb.normalized);
                }
                triangles.Add(first);
                triangles.Add(first + 1);
                triangles.Add(first + 2);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
                mesh.SetVertices(positions);
                mesh.SetNormals(normals);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                MeshUtil.ObjectSpaceUVs(mesh);
                return mesh;
            }
        }
    }
}
