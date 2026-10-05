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

    /// <summary>What a grab does to the prop's orientation (LEVELS 0.4, request 1).</summary>
    public enum GrabPose
    {
        /// <summary>The prop keeps the tilt it was picked up with.</summary>
        Keep,
        /// <summary>
        /// Over the first 0.15 s of the hold, pitch and roll ease to the nearest multiple of 90 degrees:
        /// whichever of the prop's own axes points most nearly up is stood up straight. The heading is kept.
        /// </summary>
        Snap90,
        /// <summary>Over the first 0.15 s of the hold the prop eases onto its authored up axis. The heading is kept.</summary>
        Upright,
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
        /// <summary>What a grab does to a tumbled prop's tilt.</summary>
        public GrabPose GrabPose = GrabPose.Keep;
        /// <summary>
        /// Dynamic props only: the prop stays put (kinematic) at its authored pose until it is grabbed for
        /// the first time, and is an ordinary dynamic prop from then on. A respawn freezes it again.
        /// </summary>
        public bool FrozenUntilGrabbed;
        /// <summary>False: the pitch key (F) does nothing while this prop is held.</summary>
        public bool AllowPitch = true;
        /// <summary>The prop cannot tip over: rotation about X and Z is frozen while it is dynamic.</summary>
        public bool KeepUpright;
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

        /// <summary>
        /// A change of velocity within one physics step of at least this much (units per second), beyond what
        /// gravity and damping account for, is an impact and raises PropImpact. Resting contact is 0.37.
        /// </summary>
        public const float ImpactSpeed = 1.5f;

        // A prop thinner than this (half extent), or one that moves more than its own half thickness per
        // tick, would tunnel through thin geometry with discrete collision detection.
        const float ThinHalfExtent = 0.1f;
        // After an impact the same prop reports no other for this many ticks (a crate rattling to rest).
        const int ImpactCooldownTicks = 6;

        readonly Game game;
        readonly HashSet<string> tags = new HashSet<string>();
        readonly Vector3 spawnPosition;
        readonly Quaternion spawnRotation;
        readonly float spawnScale;
        readonly float baseMinHalfExtent;
        readonly bool frozenUntilGrabbed;
        readonly Mover kinematicMover;
        Mover driveMover;
        Vector3 stepVelocity, stepForce;
        bool stepTracked;

        /// <summary>The velocity the prop went into the physics step with (zero while it is held or kinematic).</summary>
        internal Vector3 StepVelocity => stepTracked ? stepVelocity : Vector3.zero;
        int impactCooldown;

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
        /// <summary>
        /// Drives the body: always there when BodyKind is Kinematic, and for a Dynamic prop while it is
        /// <see cref="Driven"/>. Null otherwise.
        /// </summary>
        public Mover Mover => kinematicMover ?? (Driven ? driveMover : null);
        /// <summary>The mover of BeginDrive, once there has been one (it stays registered with the Game).</summary>
        internal Mover DriveMover => driveMover;

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
        public GrabPose GrabPose { get; set; }
        /// <summary>False: the pitch key does nothing while this prop is held.</summary>
        public bool AllowPitch { get; set; }
        /// <summary>Rotation about X and Z is frozen while the prop is dynamic.</summary>
        public bool KeepUpright { get; }
        /// <summary>True while a FrozenUntilGrabbed prop waits for its first grab (it is kinematic meanwhile).</summary>
        public bool Frozen { get; private set; }
        /// <summary>True between BeginDrive and EndDrive: a gadget moves the prop through <see cref="Mover"/>.</summary>
        public bool Driven { get; private set; }

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
            GrabPose = options.GrabPose;
            AllowPitch = options.AllowPitch;
            KeepUpright = options.KeepUpright;
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
            if (KeepUpright) Body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            if (BodyKind != PropBody.Dynamic) Body.isKinematic = true;
            if (BodyKind == PropBody.Kinematic) kinematicMover = new Mover(Body);
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

            frozenUntilGrabbed = options.FrozenUntilGrabbed && BodyKind == PropBody.Dynamic;
            if (frozenUntilGrabbed) Freeze();
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

        /// <summary>
        /// Back to the pose and scale the level gave it. A held prop is released first, a drive ends, and a
        /// FrozenUntilGrabbed prop is frozen again.
        /// </summary>
        public void Respawn()
        {
            if (Removed) return;
            if (Held) game.Grabber.Forget(this);
            if (Driven) EndDrive(Vector3.zero);
            SetPose(spawnPosition, spawnRotation);
            SetScale(spawnScale);
            if (frozenUntilGrabbed) Freeze();
            game.Events.RaisePropRespawned(new PropEvent { Prop = this });
        }

        /// <summary>
        /// A gadget takes over a Dynamic prop (LEVELS 0.4, request 2): the body becomes kinematic and is moved
        /// through the returned <see cref="Mover"/> - call MoveTo every tick, exactly as for AddKinematic
        /// geometry - so whatever rides it inherits its velocity. While driven the prop is ground for the
        /// player whatever its mass (the light-prop rules only apply to simulated bodies), triggers go on
        /// sensing it, and it can still be grabbed: a grab ends the drive, so check <see cref="Driven"/>
        /// every tick. Returns null, and changes nothing, if the prop is held or removed. Throws for Fixed
        /// and Kinematic props.
        /// </summary>
        public Mover BeginDrive()
        {
            if (BodyKind != PropBody.Dynamic)
                throw new System.InvalidOperationException(this + " is " + BodyKind + "; only a Dynamic prop can be driven.");
            if (Removed || Held) return null;
            if (Driven) return driveMover;

            Frozen = false;
            if (!Body.isKinematic)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
            }
            if (driveMover == null)
            {
                // The Mover makes the body kinematic itself.
                driveMover = new Mover(Body);
                game.Register(driveMover);
            }
            else
            {
                Body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                Body.isKinematic = true;
            }
            driveMover.Suspended = false;
            Driven = true;
            return driveMover;
        }

        /// <summary>
        /// Hands a driven prop back to the simulation with the given velocity (usually the mover's last
        /// one, so a carried prop keeps going). Does nothing if the prop is not being driven.
        /// </summary>
        public void EndDrive(Vector3 velocity)
        {
            if (!Driven) return;
            Driven = false;
            driveMover.Suspended = true;
            if (Removed || Held) return;
            Body.isKinematic = false;
            // Mass, inertia and the collision mode at the current scale; wakes the body.
            SetScale(Scale);
            Body.linearVelocity = velocity;
            Body.angularVelocity = Vector3.zero;
            RefreshCollisionMode();
        }

        /// <summary>Releases a FrozenUntilGrabbed prop without a grab; it is dynamic from now on.</summary>
        public void Unfreeze()
        {
            if (!Frozen) return;
            Frozen = false;
            if (Removed || Held || Driven) return;
            Body.isKinematic = false;
            SetScale(Scale);
        }

        void Freeze()
        {
            if (Held || Driven) return;
            if (!Body.isKinematic)
            {
                Body.linearVelocity = Vector3.zero;
                Body.angularVelocity = Vector3.zero;
                // Continuous modes are not valid on kinematic bodies; switch before changing the kind.
                Body.collisionDetectionMode = CollisionDetectionMode.Discrete;
                Body.isKinematic = true;
            }
            Frozen = true;
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
            if (kinematicMover != null) kinematicMover.Suspended = held;
            if (held)
            {
                // The first grab thaws a frozen prop, and a grab takes a driven one away from its gadget.
                Frozen = false;
                if (Driven)
                {
                    Driven = false;
                    driveMover.Suspended = true;
                }
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

        /// <summary>Right before the physics step: remembers the velocity the step starts from.</summary>
        internal void BeginStep()
        {
            stepTracked = !Held && !Body.isKinematic;
            if (!stepTracked) return;
            stepVelocity = Body.linearVelocity;
            // What level code pushes it with this tick (wind, a launcher) is not a collision either.
            stepForce = Body.GetAccumulatedForce(Sim.Dt);
        }

        /// <summary>
        /// Right after the physics step: did the prop hit something? Whatever the step did to the velocity
        /// beyond gravity, applied forces and damping came from contacts. No physics callback is involved, so the answer
        /// depends on nothing but the simulation's own state.
        /// </summary>
        internal bool EndStep(out PropImpactEvent impact)
        {
            impact = default;
            if (impactCooldown > 0) impactCooldown--;
            if (!stepTracked || Held || Body.isKinematic) return false;

            Vector3 expected = stepVelocity + stepForce * (Sim.Dt / Body.mass);
            if (Body.useGravity) expected += Physics.gravity * Sim.Dt;
            expected *= Mathf.Clamp01(1f - Body.linearDamping * Sim.Dt);
            Vector3 change = Body.linearVelocity - expected;
            float speed = change.magnitude;
            if (speed < ImpactSpeed || impactCooldown > 0) return false;
            impactCooldown = ImpactCooldownTicks;

            Vector3 normal = change / speed;
            // The spot of the prop that faces what pushed it: the nearest point of its colliders to a point
            // well outside it on that side.
            Vector3 center = Center;
            Vector3 probe = center - normal * (Radius * 2f + 0.01f);
            Vector3 point = center - normal * Radius;
            float best = float.MaxValue;
            for (int i = 0; i < Colliders.Length; i++)
            {
                Collider collider = Colliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger) continue;
                Vector3 nearest = collider.ClosestPoint(probe);
                float distance = (nearest - probe).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                point = nearest;
            }
            impact = new PropImpactEvent { Prop = this, Speed = speed, Mass = Body.mass, Point = point, Normal = normal };
            return true;
        }

        internal void Destroy()
        {
            Removed = true;
            Held = false;
            Driven = false;
            Frozen = false;
            Sim.Destroy(GameObject);
            Sim.Destroy(Material);
        }

        public override string ToString() => "Prop '" + Name + "'";
    }
}
