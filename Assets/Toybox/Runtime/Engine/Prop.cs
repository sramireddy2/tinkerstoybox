using System.Collections.Generic;
using UnityEngine;

namespace Toybox.Engine
{
    public enum PropBody
    {
        /// <summary>Simulated: falls, stacks, gets pushed.</summary>
        Dynamic,
        /// <summary>Never moves on its own. If it is grabbable it stays wherever it is dropped.</summary>
        Fixed,
        /// <summary>Moved by level code through <see cref="Prop.Mover"/>.</summary>
        Kinematic,
    }

    public sealed class PropOptions
    {
        public string Name;
        public float Scale = 1f;
        /// <summary>mass = Density * volume * Scale^3</summary>
        public float Density = 1f;
        public float Friction = 0.6f, Bounciness = 0f;
        public float LinearDamping = 0f, AngularDamping = 0.05f;
        /// <summary>
        /// Whether the perspective mechanic can take it. Left unset it is true for Dynamic and Fixed props
        /// and false for Kinematic ones (a platform the level moves must not come off its path by a click).
        /// </summary>
        public bool? Grabbable;
        public PropBody Body = PropBody.Dynamic;
        public float MinScale = 0.02f, MaxScale = 80f;
        public string[] Tags;
    }

    /// <summary>
    /// A physics object of the level: a toy (renderers + colliders authored at scale 1) plus a Rigidbody,
    /// with a uniform scale that the perspective mechanic changes.
    /// </summary>
    public sealed class Prop
    {
        /// <summary>
        /// Lower bound on Rigidbody.mass. The solver can only hold a heavy body up on a light one while their
        /// mass ratio stays in the hundreds; this keeps every prop within reach of the player's mass (3).
        /// </summary>
        public const float MinMass = 0.01f;

        // A prop thinner than this (half extent), or one that moves more than its own half thickness per
        // tick, would tunnel through thin geometry with discrete collision detection.
        const float ThinHalfExtent = 0.1f;

        readonly Game game;
        readonly HashSet<string> tags = new HashSet<string>();
        readonly Vector3 spawnPosition;
        readonly Quaternion spawnRotation;
        readonly float spawnScale;
        readonly float baseMinHalfExtent;

        /// <summary>Creation index within the level; gives props a deterministic order.</summary>
        public int Id { get; }
        public string Name { get; }
        public GameObject GameObject { get; }
        public Transform Transform { get; }
        public Rigidbody Body { get; }
        /// <summary>Identity of the Rigidbody, usable off the main thread (contact callbacks).</summary>
        public EntityId BodyId { get; }
        public Collider[] Colliders { get; }
        public PhysicsMaterial Material { get; }
        public PropBody BodyKind { get; }
        /// <summary>Drives the body when BodyKind is Kinematic; null otherwise.</summary>
        public Mover Mover { get; }

        public float Density { get; }
        /// <summary>Collider volume at scale 1.</summary>
        public float BaseVolume { get; }
        /// <summary>Radius, at scale 1, of the sphere around <see cref="LocalCenter"/> that contains every collider.</summary>
        public float BaseRadius { get; }
        /// <summary>Center of the colliders' bounds in the toy's own (unscaled) space.</summary>
        public Vector3 LocalCenter { get; }
        /// <summary>Half size, at scale 1, of the box around <see cref="LocalCenter"/> (in the toy's axes) that contains every collider.</summary>
        public Vector3 LocalHalfExtents { get; }
        /// <summary>Half the smallest dimension, at scale 1, of the thinnest collider: how thin an obstacle the prop can be stopped by.</summary>
        public float BaseThinness { get; }

        public bool Grabbable { get; set; }
        public float MinScale { get; set; }
        public float MaxScale { get; set; }

        /// <summary>Absolute uniform scale; 1 is the authored size.</summary>
        public float Scale { get; private set; }
        public bool Held { get; private set; }
        /// <summary>True once the prop has been removed from the level.</summary>
        public bool Removed { get; private set; }

        public Vector3 Position => Transform.position;
        public Quaternion Rotation => Transform.rotation;
        /// <summary>World position of the middle of the prop; perspective distances are measured to it.</summary>
        public Vector3 Center => Transform.TransformPoint(LocalCenter);
        /// <summary>Bounding radius at the current scale.</summary>
        public float Radius => BaseRadius * Scale;
        public float Mass => Body.mass;
        public Vector3 Velocity => Body.isKinematic ? (Mover != null ? Mover.Velocity : Vector3.zero) : Body.linearVelocity;
        public IReadOnlyCollection<string> Tags => tags;

        internal Prop(Game game, int id, GameObject toy, Transform parent, Vector3 position, Quaternion rotation, PropOptions options)
        {
            options ??= new PropOptions();
            this.game = game;
            Id = id;
            Name = string.IsNullOrEmpty(options.Name) ? toy.name : options.Name;
            GameObject = toy;
            Transform = toy.transform;
            toy.name = Name;

            Transform.SetParent(parent, false);
            Transform.SetPositionAndRotation(position, rotation);
            Transform.localScale = Vector3.one;

            Colliders = toy.GetComponentsInChildren<Collider>(true);
            Material = new PhysicsMaterial(Name)
            {
                dynamicFriction = options.Friction,
                staticFriction = options.Friction,
                bounciness = options.Bounciness,
                frictionCombine = PhysicsMaterialCombine.Average,
                // Maximum, so a prop's bounciness is what the level author wrote regardless of what it hits.
                bounceCombine = PhysicsMaterialCombine.Maximum,
            };
            foreach (Collider collider in Colliders)
            {
                // PhysX only supports convex meshes on moving bodies.
                if (collider is MeshCollider mesh && !mesh.convex) mesh.convex = true;
                collider.sharedMaterial = Material;
            }

            PropGeometry.Measure(Transform, Colliders, out float volume, out Vector3 center, out Vector3 halfExtents, out float radius);
            BaseVolume = volume;
            LocalCenter = center;
            LocalHalfExtents = halfExtents;
            BaseRadius = radius;
            baseMinHalfExtent = Mathf.Min(halfExtents.x, Mathf.Min(halfExtents.y, halfExtents.z));
            BaseThinness = PropGeometry.Thinness(Colliders, baseMinHalfExtent);

            Density = options.Density;
            Grabbable = options.Grabbable ?? options.Body != PropBody.Kinematic;
            MinScale = options.MinScale;
            MaxScale = options.MaxScale;
            BodyKind = options.Body;
            if (options.Tags != null)
                foreach (string tag in options.Tags) tags.Add(tag);

            Body = toy.GetComponent<Rigidbody>();
            if (Body == null) Body = toy.AddComponent<Rigidbody>();
            Body.linearDamping = options.LinearDamping;
            Body.angularDamping = options.AngularDamping;
            Body.interpolation = RigidbodyInterpolation.None;
            Body.useGravity = true;
            Body.solverIterations = Physics.defaultSolverIterations;
            Body.solverVelocityIterations = Physics.defaultSolverVelocityIterations;
            Body.maxDepenetrationVelocity = Physics.defaultMaxDepenetrationVelocity;
            if (BodyKind != PropBody.Dynamic) Body.isKinematic = true;
            if (BodyKind == PropBody.Kinematic) Mover = new Mover(Body);
            BodyId = Body.GetEntityId();

            PropRef reference = toy.GetComponent<PropRef>();
            if (reference == null) reference = toy.AddComponent<PropRef>();
            reference.Prop = this;
            Layers.SetRecursively(Transform, Layers.Prop);

            Scale = 1f;
            SetScale(options.Scale);
            spawnPosition = position;
            spawnRotation = rotation;
            spawnScale = Scale;
        }

        public bool HasTag(string tag) => tags.Contains(tag);
        public void AddTag(string tag) => tags.Add(tag);
        public void RemoveTag(string tag) => tags.Remove(tag);

        /// <summary>
        /// Sets the uniform scale (clamped to MinScale..MaxScale) and everything that follows from it: mass,
        /// inertia and the collision detection mode. Wakes the body.
        /// </summary>
        public void SetScale(float scale)
        {
            scale = Mathf.Clamp(scale, MinScale, MaxScale);
            Scale = scale;
            Transform.localScale = new Vector3(scale, scale, scale);
            Body.mass = Mathf.Max(MinMass, Density * BaseVolume * scale * scale * scale);
            // The colliders take the new size at the next transform sync; inertia must be derived after it.
            Physics.SyncTransforms();
            if (Body.isKinematic) return;
            Body.ResetCenterOfMass();
            Body.ResetInertiaTensor();
            RefreshCollisionMode();
            Body.WakeUp();
        }

        /// <summary>Teleports the prop and stops it.</summary>
        public void SetPose(Vector3 position, Quaternion rotation)
        {
            Transform.SetPositionAndRotation(position, rotation);
            Body.position = position;
            Body.rotation = rotation;
            if (!Body.isKinematic)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                Body.WakeUp();
            }
            Physics.SyncTransforms();
        }

        /// <summary>Back to the pose and scale the level gave it. A held prop is released first.</summary>
        public void Respawn()
        {
            if (Removed) return;
            if (Held) game.Grabber.Forget(this);
            SetPose(spawnPosition, spawnRotation);
            SetScale(spawnScale);
            game.Events.RaisePropRespawned(new PropEvent { Prop = this });
        }

        /// <summary>
        /// Held props are kinematic and sit on the Held layer, where nothing collides with them and no query
        /// sees them. Leaving the held state restores the body kind at the current scale, at rest.
        /// </summary>
        internal void SetHeld(bool held)
        {
            if (Held == held) return;
            Held = held;
            // A held prop follows the view, not the level's path.
            if (Mover != null) Mover.Suspended = held;
            if (held)
            {
                if (!Body.isKinematic)
                {
                    Body.linearVelocity = Vector3.zero;
                    Body.angularVelocity = Vector3.zero;
                    // Continuous modes are not valid on kinematic bodies; switch before changing the kind.
                    Body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                    Body.isKinematic = true;
                }
                Layers.SetRecursively(Transform, Layers.Held);
                return;
            }

            Layers.SetRecursively(Transform, Layers.Prop);
            if (BodyKind == PropBody.Dynamic)
            {
                Body.isKinematic = false;
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }
            SetScale(Scale);
        }

        /// <summary>Places a held prop so that its center is at the given point. Does not sync physics.</summary>
        internal void PlaceHeld(Vector3 center, Quaternion rotation, float scale)
        {
            Scale = scale;
            Transform.localScale = new Vector3(scale, scale, scale);
            Transform.SetPositionAndRotation(center - rotation * (LocalCenter * scale), rotation);
        }

        /// <summary>Small or fast props use continuous collision detection so they cannot tunnel.</summary>
        internal void RefreshCollisionMode()
        {
            if (Body.isKinematic) return;
            float halfThickness = baseMinHalfExtent * Scale;
            float travel = Body.linearVelocity.magnitude * Sim.Dt;
            bool continuous = halfThickness < ThinHalfExtent || travel > halfThickness;
            CollisionDetectionMode mode = continuous ? CollisionDetectionMode.ContinuousDynamic : CollisionDetectionMode.Discrete;
            if (Body.collisionDetectionMode != mode) Body.collisionDetectionMode = mode;
        }

        internal void Destroy()
        {
            Removed = true;
            Held = false;
            Sim.Destroy(GameObject);
            Sim.Destroy(Material);
        }

        public override string ToString() => "Prop '" + Name + "'";
    }
}
