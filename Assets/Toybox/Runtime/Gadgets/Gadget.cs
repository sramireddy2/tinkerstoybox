using System;
using Toybox.Art;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    /// <summary>
    /// The shape every gadget has (LEVELS 2.0): a plain class created in a level's Build, ticked through
    /// ctx.OnUpdate - after the player and the grabber, before the physics step - and torn down with the
    /// level. Gadgets tick in construction order; one that feeds another is constructed first. No physics
    /// callbacks, no Time.*, no randomness: everything is a function of the simulation's own state.
    ///
    /// Every gadget event exists twice: a C# event on the gadget, invoked inside the tick (level logic
    /// subscribes there), and a mirror on game.Events, delivered when the tick ends (render, audio, UI).
    /// </summary>
    public abstract class Gadget
    {
        protected readonly LevelContext Ctx;
        protected readonly Game Game;

        protected Gadget(LevelContext ctx, string name)
        {
            Ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            Game = ctx.Game;
            Name = string.IsNullOrEmpty(name) ? GetType().Name : name;
            ctx.OnUpdate(Step);
            ctx.OnDispose(DisposeOnce);
        }

        public string Name { get; }

        /// <summary>A disabled gadget senses nothing and moves nothing; its age stands still.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Ticks this gadget has run since it was constructed or last Reset.</summary>
        public int Age { get; private set; }

        /// <summary>Age in seconds.</summary>
        public float Seconds => Age * Sim.Dt;

        /// <summary>True once the level it belongs to has been unloaded.</summary>
        public bool Disposed { get; private set; }

        /// <summary>One simulation step: dt is Sim.Dt.</summary>
        protected abstract void Tick(float dt);

        /// <summary>Back to the state Build left it in.</summary>
        public virtual void Reset() => Age = 0;

        /// <summary>The level is being unloaded. Objects under ctx.Root are destroyed by the level itself.</summary>
        protected virtual void Dispose() { }

        void Step(float dt)
        {
            if (Disposed || !Enabled) return;
            Tick(dt);
            Age++;
        }

        void DisposeOnce()
        {
            if (Disposed) return;
            Disposed = true;
            Dispose();
        }

        /// <summary>The payload of a mirror event on game.Events.</summary>
        protected GadgetEvent Event(Vector3 position, Prop prop = null, float value = 0f, float value2 = 0f, int index = 0, string text = null) =>
            new GadgetEvent { Gadget = this, Prop = prop, Position = position, Value = value, Value2 = value2, Index = index, Text = text };

        /// <summary>Seconds to whole ticks, at least <paramref name="least"/>.</summary>
        protected static int Ticks(float seconds, int least = 0) => Mathf.Max(least, Mathf.RoundToInt(seconds / Sim.Dt));

        public override string ToString() => GetType().Name + " '" + Name + "'";
    }

    /// <summary>
    /// What a gadget tells presentation through game.Events. Which fields mean something is said at each
    /// event; <see cref="Position"/> is always where it happened.
    /// </summary>
    public struct GadgetEvent
    {
        public Gadget Gadget;
        /// <summary>The prop involved, or null (the player, or nobody).</summary>
        public Prop Prop;
        public Vector3 Position;
        public float Value, Value2;
        public int Index;
        public string Text;
    }

    public enum ZoneShape
    {
        Box,
        Sphere,
        /// <summary>An upright cylinder.</summary>
        Cylinder,
    }

    /// <summary>
    /// A region of the world a gadget tests points against: an oriented box, a sphere or an upright
    /// cylinder, at a fixed world pose. The default value contains nothing.
    /// </summary>
    public readonly struct Zone
    {
        readonly bool valid;
        readonly Quaternion rotation;

        public readonly ZoneShape Shape;
        public readonly Vector3 Center;
        /// <summary>Full size of a box; for a cylinder y is its height.</summary>
        public readonly Vector3 Size;
        public readonly float Radius;

        Zone(ZoneShape shape, Vector3 center, Vector3 size, float radius, Quaternion rotation)
        {
            valid = true;
            Shape = shape;
            Center = center;
            Size = size;
            Radius = radius;
            this.rotation = rotation;
        }

        public bool IsValid => valid;
        public Quaternion Rotation => valid ? rotation : Quaternion.identity;

        public static Zone Box(Vector3 center, Vector3 size) => new Zone(ZoneShape.Box, center, size, 0f, Quaternion.identity);
        public static Zone Box(Vector3 center, Vector3 size, Quaternion rotation) => new Zone(ZoneShape.Box, center, size, 0f, rotation);
        /// <summary>The axis-aligned box between two corners.</summary>
        public static Zone MinMax(Vector3 min, Vector3 max) => Box((min + max) * 0.5f, Abs(max - min));
        public static Zone Sphere(Vector3 center, float radius) => new Zone(ZoneShape.Sphere, center, Vector3.one * (radius * 2f), radius, Quaternion.identity);
        /// <summary>An upright cylinder about the vertical axis through (x, z), from yMin to yMax.</summary>
        public static Zone Cylinder(float x, float z, float radius, float yMin, float yMax) =>
            new Zone(ZoneShape.Cylinder, new Vector3(x, (yMin + yMax) * 0.5f, z), new Vector3(radius * 2f, Mathf.Abs(yMax - yMin), radius * 2f), radius, Quaternion.identity);

        /// <summary>Lowest and highest point (of an upright zone).</summary>
        public float MinY => Center.y - (Shape == ZoneShape.Sphere ? Radius : Size.y * 0.5f);
        public float MaxY => Center.y + (Shape == ZoneShape.Sphere ? Radius : Size.y * 0.5f);

        public bool Contains(Vector3 point)
        {
            if (!valid) return false;
            switch (Shape)
            {
                case ZoneShape.Sphere:
                    return (point - Center).sqrMagnitude <= Radius * Radius;
                case ZoneShape.Cylinder:
                {
                    float dx = point.x - Center.x, dz = point.z - Center.z;
                    return Mathf.Abs(point.y - Center.y) <= Size.y * 0.5f && dx * dx + dz * dz <= Radius * Radius;
                }
                default:
                {
                    Vector3 local = Quaternion.Inverse(rotation) * (point - Center);
                    Vector3 half = Size * 0.5f;
                    return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
                }
            }
        }

        /// <summary>Is the point over (or under) the zone: inside its footprint, whatever its height?</summary>
        public bool ContainsXZ(Vector3 point) => Contains(new Vector3(point.x, Center.y, point.z));

        /// <summary>The point of the zone nearest to a point (the point itself if it is inside).</summary>
        public Vector3 ClosestPoint(Vector3 point)
        {
            if (!valid) return point;
            switch (Shape)
            {
                case ZoneShape.Sphere:
                {
                    Vector3 to = point - Center;
                    float distance = to.magnitude;
                    return distance <= Radius ? point : Center + to * (Radius / distance);
                }
                case ZoneShape.Cylinder:
                {
                    var flat = new Vector2(point.x - Center.x, point.z - Center.z);
                    float distance = flat.magnitude;
                    if (distance > Radius) flat *= Radius / distance;
                    float y = Mathf.Clamp(point.y, Center.y - Size.y * 0.5f, Center.y + Size.y * 0.5f);
                    return new Vector3(Center.x + flat.x, y, Center.z + flat.y);
                }
                default:
                {
                    Vector3 local = Quaternion.Inverse(rotation) * (point - Center);
                    Vector3 half = Size * 0.5f;
                    local = new Vector3(Mathf.Clamp(local.x, -half.x, half.x), Mathf.Clamp(local.y, -half.y, half.y), Mathf.Clamp(local.z, -half.z, half.z));
                    return Center + rotation * local;
                }
            }
        }

        /// <summary>The same zone, bigger by a margin on every side.</summary>
        public Zone Grown(float margin)
        {
            if (!valid) return this;
            return new Zone(Shape, Center, Size + Vector3.one * (margin * 2f), Shape == ZoneShape.Box ? 0f : Radius + margin, rotation);
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
    }

    /// <summary>
    /// The one signal element a gadget shows (ART_BIBLE 2.3): amber and pulsing while it waits, green and
    /// steady once satisfied, hazard for what hurts. It is a renderer with a shared signal material; the
    /// pulse is a per-renderer emission override driven by simulation time, so it replays identically.
    /// </summary>
    public sealed class SignalLamp
    {
        readonly Renderer renderer;
        Signal signal;
        bool lit;

        public SignalLamp(Renderer renderer, Signal signal)
        {
            this.renderer = renderer;
            Set(signal);
        }

        public Renderer Renderer => renderer;
        public Signal Signal => signal;
        public bool Lit => lit;

        public void Set(Signal next)
        {
            if (lit && signal.Equals(next)) return;
            signal = next;
            lit = true;
            if (renderer == null) return;
            renderer.sharedMaterial = Materials.Gadget(next);
            Materials.SetEmission(renderer, next.Emission);
        }

        /// <summary>Dark: the signal's material with no emission.</summary>
        public void Off()
        {
            if (!lit) return;
            lit = false;
            if (renderer != null) Materials.SetEmission(renderer, Color.black);
        }

        /// <summary>Call every tick with the gadget's time: animates the pulse of a signal that has one.</summary>
        public void Pulse(float seconds)
        {
            if (!lit || renderer == null || signal.PulseHz <= 0f) return;
            Materials.SetEmission(renderer, signal.EmissionAt(signal.GainAt(seconds)));
        }
    }

    /// <summary>Small helpers shared by the gadgets: visuals without colliders, the crush guard, prop extents.</summary>
    public static class GadgetKit
    {
        static readonly Collider[] Hits = new Collider[64];

        public static Material Body => Materials.Gadget(GadgetPart.Body);
        public static Material Metal => Materials.Gadget(GadgetPart.Metal);

        /// <summary>A renderer without a collider, as a child of <paramref name="parent"/>.</summary>
        public static MeshRenderer Visual(Transform parent, string name, Mesh mesh, Material material, Vector3 localPosition, Quaternion localRotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        public static MeshRenderer Visual(Transform parent, string name, Mesh mesh, Material material, Vector3 localPosition) =>
            Visual(parent, name, mesh, material, localPosition, Quaternion.identity);

        public static MeshRenderer Visual(Transform parent, string name, Mesh mesh, Material material) =>
            Visual(parent, name, mesh, material, Vector3.zero, Quaternion.identity);

        public static Mesh BoxMesh(Vector3 size) => MeshKit.Cached(MeshKit.Key("Box", size.x, size.y, size.z), () => MeshKit.Box(size));

        public static Mesh RoundedBoxMesh(Vector3 size, float bevel) =>
            MeshKit.Cached(MeshKit.Key("RoundedBox", size.x, size.y, size.z, bevel), () => MeshKit.RoundedBox(size, bevel));

        public static Mesh DiscMesh(float radius, float height, int sides = 24) =>
            MeshKit.Cached(MeshKit.Key("GadgetDisc", radius, height, sides), () => MeshKit.Cylinder(radius, height, sides));

        /// <summary>A signal lamp: a small renderer in the signal's material, child of <paramref name="parent"/>.</summary>
        public static SignalLamp Lamp(Transform parent, Mesh mesh, Signal signal, Vector3 localPosition, Quaternion localRotation)
        {
            MeshRenderer renderer = Visual(parent, "Signal", mesh, Materials.Gadget(signal), localPosition, localRotation);
            return new SignalLamp(renderer, signal);
        }

        public static SignalLamp Lamp(Transform parent, Mesh mesh, Signal signal, Vector3 localPosition) =>
            Lamp(parent, mesh, signal, localPosition, Quaternion.identity);

        /// <summary>How deep a collider at a pose sinks into the player's capsule (0: not at all).</summary>
        public static float Penetration(Game game, Collider collider, Vector3 position, Quaternion rotation)
        {
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) return 0f;
            CapsuleCollider capsule = game.Player.Collider;
            return Physics.ComputePenetration(collider, position, rotation, capsule, game.Player.Position, Quaternion.identity, out _, out float depth) ? Mathf.Max(0f, depth) : 0f;
        }

        /// <summary>
        /// The crush guard (LEVELS 0.3) for a body that is about to move rigidly to a new pose: would it
        /// sink into the player's capsule there, deeper than it does where it is now? Resting contact (the
        /// player standing on it) and moving away from the player do not count.
        /// </summary>
        public static bool WouldCrush(Game game, Transform body, Collider[] colliders, Vector3 position, Quaternion rotation, float tolerance = 0.005f)
        {
            Quaternion bodyRotation = body.rotation;
            Matrix4x4 next = Matrix4x4.TRS(position, rotation, body.lossyScale);
            Matrix4x4 toBody = body.worldToLocalMatrix;
            float deepestNext = 0f, deepestNow = 0f;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled) continue;
                Transform t = collider.transform;
                Matrix4x4 world = next * (toBody * t.localToWorldMatrix);
                Vector3 p = world.GetColumn(3);
                Quaternion r = rotation * (Quaternion.Inverse(bodyRotation) * t.rotation);
                deepestNext = Mathf.Max(deepestNext, Penetration(game, collider, p, r));
                deepestNow = Mathf.Max(deepestNow, Penetration(game, collider, t.position, t.rotation));
            }
            return deepestNext > tolerance * game.Player.Scale && deepestNext > deepestNow + 1e-4f;
        }

        /// <summary>Does the body, where it is, overlap the player's capsule by more than a resting contact?</summary>
        public static bool OverlapsPlayer(Game game, Collider[] colliders, float tolerance = 0.02f)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null) continue;
                if (Penetration(game, collider, collider.transform.position, collider.transform.rotation) > tolerance * game.Player.Scale) return true;
            }
            return false;
        }

        /// <summary>
        /// The player's ground velocity is the one their ground had a tick ago. On a mover that turns - a
        /// wagon on a circle, a plank that sweeps round a lighthouse - that leaves a rider drifting outward
        /// by one tick's turn of the velocity (measured: 0.68 units per lap of the Level 9 train). This
        /// adds the missing change as a push; call it after the mover's MoveTo.
        /// </summary>
        public static void SteadyRider(Game game, Mover mover)
        {
            Player player = game.Player;
            if (!player.Grounded || player.GroundCollider == null || player.GroundCollider.attachedRigidbody != mover.Body) return;
            Vector3 turning = mover.AngularVelocity;
            if (turning.sqrMagnitude < 1e-8f) return;
            Vector3 missing = Vector3.Cross(turning, mover.PointVelocity(player.Position)) * Sim.Dt;
            missing.y = 0f;
            // On the ground a push of a shifts the velocity by a / 4 (ARCHITECTURE: "a push of 20 is a drift of 5").
            player.AddPush(missing * 4f);
        }

        /// <summary>Makes a body's colliders ignore the player's capsule, or collide with it again.</summary>
        public static void IgnorePlayer(Game game, Collider[] colliders, bool ignore)
        {
            CapsuleCollider capsule = game.Player.Collider;
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null) Physics.IgnoreCollision(colliders[i], capsule, ignore);
        }

        /// <summary>Is the player standing on one of this body's colliders?</summary>
        public static bool PlayerStandsOn(Game game, Rigidbody body)
        {
            Player player = game.Player;
            return player.Grounded && player.GroundCollider != null && body != null && player.GroundCollider.attachedRigidbody == body;
        }

        /// <summary>Lowest and highest world y of a prop's colliders (needs synced transforms).</summary>
        public static void VerticalExtent(Prop prop, out float minY, out float maxY)
        {
            minY = float.MaxValue;
            maxY = float.MinValue;
            Collider[] colliders = prop.Colliders;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                Bounds bounds = collider.bounds;
                minY = Mathf.Min(minY, bounds.min.y);
                maxY = Mathf.Max(maxY, bounds.max.y);
            }
            if (minY > maxY)
            {
                minY = prop.Center.y - prop.Radius;
                maxY = prop.Center.y + prop.Radius;
            }
        }

        /// <summary>The point of a prop's colliders nearest to a world point.</summary>
        public static Vector3 ClosestPoint(Prop prop, Vector3 point)
        {
            Vector3 best = prop.Center;
            float bestDistance = float.MaxValue;
            Collider[] colliders = prop.Colliders;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                Vector3 nearest = collider.ClosestPoint(point);
                float distance = (nearest - point).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = nearest;
            }
            return best;
        }

        /// <summary>Velocity of the point of a prop that is at a world point, whether it is simulated or driven.</summary>
        public static Vector3 PointVelocity(Prop prop, Vector3 point)
        {
            if (!prop.Body.isKinematic) return prop.Body.GetPointVelocity(point);
            Mover mover = prop.Mover;
            return mover != null ? mover.PointVelocity(point) : Vector3.zero;
        }

        /// <summary>A loose prop a gadget may act on: in the level, shown, not in the player's hand.</summary>
        public static bool IsLoose(Prop prop) =>
            prop != null && !prop.Removed && !prop.Held && prop.GameObject.activeInHierarchy;

        /// <summary>
        /// Is a capsule of the given size free at these feet? World and props count, except the colliders
        /// of <paramref name="except"/>.
        /// </summary>
        public static bool CapsuleFree(Game game, Vector3 feet, float radius, float height, Prop except = null)
        {
            float skin = radius * 0.06f;
            float r = radius - skin;
            Vector3 low = feet + Vector3.up * (radius + skin);
            Vector3 high = feet + Vector3.up * Mathf.Max(radius + skin, height - radius);
            int count = game.PhysicsScene.OverlapCapsule(low, high, r, Hits, Layers.SolidMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (except != null && PropRef.Of(Hits[i]) == except) continue;
                return false;
            }
            return true;
        }

        /// <summary>
        /// A flat-shaded mesh of a convex solid: its corners and, per face, the corners around it (either
        /// winding; every face is turned to look away from the middle). Good for a renderer and, being
        /// convex, for a convex MeshCollider.
        /// </summary>
        public static Mesh ConvexMesh(string name, Vector3[] corners, int[][] faces)
        {
            Vector3 middle = Vector3.zero;
            for (int i = 0; i < corners.Length; i++) middle += corners[i];
            middle /= Mathf.Max(1, corners.Length);

            var positions = new System.Collections.Generic.List<Vector3>();
            var normals = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();
            for (int f = 0; f < faces.Length; f++)
            {
                int[] face = faces[f];
                if (face.Length < 3) continue;
                Vector3 a = corners[face[0]], b = corners[face[1]], c = corners[face[2]];
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                bool flip = Vector3.Dot(normal, a - middle) < 0f;
                if (flip) normal = -normal;
                int first = positions.Count;
                for (int i = 0; i < face.Length; i++)
                {
                    positions.Add(corners[face[i]]);
                    normals.Add(normal);
                }
                for (int i = 1; i + 1 < face.Length; i++)
                {
                    triangles.Add(first);
                    triangles.Add(first + (flip ? i + 1 : i));
                    triangles.Add(first + (flip ? i : i + 1));
                }
            }
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            MeshUtil.ObjectSpaceUVs(mesh);
            MeshUtil.BakeOutlineNormals(mesh);
            return mesh;
        }

        public static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
