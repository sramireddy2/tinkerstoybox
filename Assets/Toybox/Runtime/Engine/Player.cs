using UnityEngine;

namespace Toybox.Engine
{
    /// <summary>
    /// First-person body: a rotation-frozen dynamic capsule whose transform position is the feet. It is a
    /// real Rigidbody so that seesaws, trampolines, wind and moving platforms act on it; the controller only
    /// steers the velocity along the ground (hard) or in the air (gently), and decides after every physics
    /// step what the solver's result means: still standing, deflected by a bump, carried, or launched.
    /// </summary>
    public sealed class Player
    {
        // Metrics from docs/ARCHITECTURE.md. Levels are designed against these.
        public const float BaseRadius = 0.3f;
        public const float BaseHeight = 1.7f;
        public const float BaseEyeHeight = 1.55f;
        public const float WalkSpeed = 5f;
        public const float SprintSpeed = 8f;
        public const float JumpSpeed = 7.6f;
        public const float Mass = 3f;
        public const float MaxSlopeDegrees = 50f;
        public const float MaxPitch = 89f;
        /// <summary>
        /// A kinematic Mover keeps its rider through a sudden change of its speed along the ground normal
        /// of up to this much (an elevator that starts, stops or reverses). Above it the rider is thrown.
        /// </summary>
        public const float MoverGripSpeed = 6f;
        /// <summary>
        /// Walkable ground this far below the feet (per unit of player scale) is still stood on: the feet
        /// are pulled down onto it instead of the player going airborne (steps down, the far side of a crest).
        /// </summary>
        public const float SnapDistance = 0.2f;

        const float GroundAcceleration = 70f;
        // Must stay well below gravity * tan(MaxSlope), or air steering could push the capsule up a slope
        // that is too steep to walk on.
        const float AirAcceleration = 14f;
        const float CoyoteTime = 0.1f;
        const float JumpBufferTime = 0.12f;
        // After a jump the feet are still within probe range for a few ticks; ignore the ground meanwhile.
        const int JumpLockTicks = 5;
        // Upward speed relative to the ground above which a push from outside counts as a launch.
        const float LaunchSpeed = 1.5f;
        const int MinAirTicksForLanding = 2;
        // On the ground a steady push shifts the walking target by this many seconds' worth of it.
        const float PushDriftTime = 0.25f;
        // Fastest the feet are pulled back onto the ground, per unit of player scale.
        const float SnapSpeed = 6f;
        // A bump redirects the player's speed, a launch adds to it. Up to this much gain still counts as a bump.
        const float DeflectionTolerance = 0.25f;
        // Faces whose normal points less upward than this are walls, not slopes: they cannot lift the capsule.
        const float SteepMinNormalY = 0.05f;
        const int MaxSteepContacts = 4;

        static readonly float WalkableNormalY = Mathf.Cos(MaxSlopeDegrees * Mathf.Deg2Rad);

        readonly Game game;
        readonly PhysicsMaterial material;
        readonly Collider[] nearby = new Collider[32];
        readonly RaycastHit[] probeHits = new RaycastHit[16];
        readonly Vector3[] steepNormals = new Vector3[MaxSteepContacts];
        readonly Collider[] steepColliders = new Collider[MaxSteepContacts];
        int steepCount;
        // Walkable things the foot leans on that are not flat under it: the lip of a step, the start of a ramp.
        readonly Vector3[] lipPoints = new Vector3[MaxSteepContacts];
        readonly Vector3[] lipNormals = new Vector3[MaxSteepContacts];
        int lipCount;

        float yaw, pitch;
        Vector3 previousPosition;
        // Where the previous tick's position has been carried to by whatever the player stands on.
        Vector3 interpolationStart;
        // The velocity the controller set for the running step, before and after one step of gravity.
        Vector3 commandedVelocity;
        Vector3 expectedVelocity;
        // Ground velocity that commandedVelocity was built on.
        Vector3 commandedGroundVelocity;
        // The controller itself let the player rise off the ground (an updraft, speed a bounce left behind):
        // then nothing holds the feet down.
        bool commandedRise;
        // How fast the controller let the player come down onto the ground (a landing in progress).
        float commandedApproach;
        Vector3 groundVelocity;
        bool groundIsMover;
        // Contact normal of what holds the capsule up. It differs from GroundNormal on an edge.
        Vector3 supportNormal = Vector3.up;
        // How far the feet are above the ground they stand on, along the ground normal.
        float groundGap;
        // Decided after the step: upward speed relative to the ground is a bump or the ground's own doing
        // and is taken out, and the feet are pulled back down.
        bool holdGround;
        // Decided after the step: something walkable in the way (a lip, a ramp) is lifting the capsule over
        // it. The rise is the solver's business and no reason to let go of the ground.
        bool steppingUp;
        Vector3 push;
        float coyote, jumpBuffer;
        bool jumpHeld;
        int jumpLock;
        int airTicks;
        Vector3 checkpointPosition;
        float checkpointYaw, checkpointPitch;

        public GameObject GameObject { get; }
        public Transform Transform { get; }
        public Rigidbody Body { get; }
        public CapsuleCollider Collider { get; }

        public float Scale { get; private set; } = 1f;
        public float Radius => BaseRadius * Scale;
        public float Height => BaseHeight * Scale;
        public float EyeHeight => BaseEyeHeight * Scale;

        /// <summary>Feet position.</summary>
        public Vector3 Position => Body.position;
        public Vector3 Eye => Body.position + Vector3.up * EyeHeight;
        /// <summary>Degrees; 0 looks down +Z, positive turns right.</summary>
        public float Yaw
        {
            get => yaw;
            set => yaw = Mathf.Repeat(value + 180f, 360f) - 180f;
        }
        /// <summary>Degrees; positive looks up. Clamped to +-89.</summary>
        public float Pitch
        {
            get => pitch;
            set => pitch = Mathf.Clamp(value, -MaxPitch, MaxPitch);
        }
        public Quaternion LookRotation => Quaternion.Euler(-pitch, yaw, 0f);
        public Vector3 Forward => LookRotation * Vector3.forward;
        public Vector3 Velocity => Body.linearVelocity;

        public bool Grounded { get; private set; }
        /// <summary>What the player stands on, or null when airborne.</summary>
        public Collider GroundCollider { get; private set; }
        /// <summary>Normal of the surface the player walks on (the plane the controller steers in).</summary>
        public Vector3 GroundNormal { get; private set; } = Vector3.up;
        /// <summary>The point of the ground under the feet. Only meaningful while Grounded.</summary>
        public Vector3 GroundPoint { get; private set; }
        /// <summary>The prop under the feet, or null when airborne or on world geometry.</summary>
        public Prop GroundProp => Grounded ? PropRef.Of(GroundCollider) : null;
        /// <summary>Velocity of the ground at the feet (a mover, a prop that carries the player); zero on the world and in the air.</summary>
        internal Vector3 GroundVelocity => groundVelocity;
        /// <summary>The simulation this body is part of (for the contact handling, which is handed the player only).</summary>
        internal Game Game => game;
        /// <summary>The velocity the player has after the running step if nothing touches them: what the controller set, and gravity.</summary>
        internal Vector3 ExpectedVelocity => expectedVelocity;
        public Vector3 CheckpointPosition => checkpointPosition;

        internal Player(Game game, Transform parent)
        {
            this.game = game;
            GameObject = new GameObject("Player") { layer = Layers.Player };
            Transform = GameObject.transform;
            Transform.SetParent(parent, false);

            // Zero friction with the Minimum combine wins against whatever it touches, so the capsule never
            // sticks to walls; standing still on slopes is handled by the controller instead.
            material = new PhysicsMaterial("Player")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Average,
            };

            Collider = GameObject.AddComponent<CapsuleCollider>();
            Collider.direction = 1;
            Collider.sharedMaterial = material;

            Body = GameObject.AddComponent<Rigidbody>();
            Body.mass = Mass;
            Body.linearDamping = 0f;
            Body.angularDamping = 0f;
            Body.useGravity = true;
            Body.constraints = RigidbodyConstraints.FreezeRotation;
            Body.interpolation = RigidbodyInterpolation.None;
            Body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            Body.sleepThreshold = 0f;
            Body.solverIterations = Physics.defaultSolverIterations;
            Body.solverVelocityIterations = Physics.defaultSolverVelocityIterations;
            Body.maxDepenetrationVelocity = Physics.defaultMaxDepenetrationVelocity;

            ApplyScale(1f);
        }

        public void AddLook(float yawDegrees, float pitchDegrees)
        {
            Yaw = yaw + yawDegrees;
            Pitch = pitch + pitchDegrees;
        }

        /// <summary>
        /// Capsule, eye height, speeds and probes scale by s; jump speed by sqrt(s), so the apex scales by s.
        /// The feet stay where they are; if the bigger capsule touches something it is moved clear of it.
        /// Returns false and changes nothing if there is no room for the new size (a low ceiling).
        /// </summary>
        public bool SetScale(float scale)
        {
            scale = Mathf.Max(0.01f, scale);
            float oldScale = Scale;
            Vector3 oldPosition = Body.position;
            ApplyScale(scale);
            Physics.SyncTransforms();
            bool fits = scale <= oldScale || MakeRoom();
            if (!fits)
            {
                ApplyScale(oldScale);
                MoveBody(oldPosition);
            }
            previousPosition = Body.position;
            interpolationStart = previousPosition;
            FindFootContacts();
            ProbeGround(false, false);
            return fits;
        }

        void ApplyScale(float scale)
        {
            Scale = scale;
            Collider.radius = BaseRadius * scale;
            Collider.height = BaseHeight * scale;
            Collider.center = new Vector3(0f, BaseHeight * scale * 0.5f, 0f);
        }

        void MoveBody(Vector3 position)
        {
            Transform.position = position;
            Body.position = position;
            Physics.SyncTransforms();
        }

        // Pushes the capsule out of whatever it overlaps at its current size: sideways or upward, never down
        // (that way lies the other side of the floor) and never far. False if it cannot get free like that.
        bool MakeRoom()
        {
            const int iterations = 12;
            float skin = 0.01f * Scale;
            Vector3 start = Body.position;
            Vector3 position = start;
            for (int i = 0; i < iterations; i++)
            {
                float radius = Radius;
                Vector3 low = position + Vector3.up * radius;
                Vector3 high = position + Vector3.up * (Height - radius);
                int count = game.PhysicsScene.OverlapCapsule(low, high, radius, nearby, Layers.SolidMask, QueryTriggerInteraction.Ignore);
                Vector3 correction = Vector3.zero;
                float deepest = 0f;
                for (int c = 0; c < count; c++)
                {
                    Collider other = nearby[c];
                    if (PassesThrough(other)) continue;
                    Transform otherTransform = other.transform;
                    if (!Physics.ComputePenetration(Collider, position, Quaternion.identity, other, otherTransform.position, otherTransform.rotation,
                            out Vector3 direction, out float depth))
                        continue;
                    correction += direction * depth;
                    deepest = Mathf.Max(deepest, depth);
                }
                if (deepest <= skin)
                {
                    if (i > 0) MoveBody(position);
                    return true;
                }
                position += correction;
                if (position.y < start.y - skin || (position - start).magnitude > 2f * Radius) return false;
            }
            return false;
        }

        /// <summary>
        /// The step that just ended threw the player (a heavy prop: PerspectiveGrabber.TakeBackThrows) and
        /// that is undone: they are where, and as fast as, they would be had nothing touched them in it.
        /// Called before <see cref="PostPhysics"/>, with whatever threw them already passing through them.
        /// </summary>
        internal void TakeBack()
        {
            Vector3 velocity = expectedVelocity;
            // What they stood on took the fall out of that; it is not put back in.
            if (Grounded)
            {
                float into = Vector3.Dot(velocity, GroundNormal);
                if (into < 0f) velocity -= GroundNormal * into;
            }
            Vector3 solved = Body.position;
            Body.linearVelocity = velocity;
            MoveBody(previousPosition + velocity * Sim.Dt);
            // Anything else in the way there (a wall they were walking into): out of it, or left where the
            // solver put them.
            if (!MakeRoom()) MoveBody(solved);
        }

        /// <summary>Moves the feet to a point and stops. Look direction is unchanged.</summary>
        public void Teleport(Vector3 position)
        {
            Transform.position = position;
            Body.position = position;
            Body.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
            previousPosition = position;
            interpolationStart = position;
            commandedVelocity = Vector3.zero;
            expectedVelocity = Vector3.zero;
            commandedGroundVelocity = Vector3.zero;
            commandedRise = false;
            commandedApproach = 0f;
            jumpLock = 0;
            jumpBuffer = 0f;
            coyote = 0f;
            airTicks = 0;
            FindFootContacts();
            ProbeGround(false, false);
        }

        public void Teleport(Vector3 position, float yawDegrees, float pitchDegrees = 0f)
        {
            Yaw = yawDegrees;
            Pitch = pitchDegrees;
            Teleport(position);
        }

        /// <summary>
        /// A steady external push - wind, a current, a conveyor - as an acceleration. Call it every tick it
        /// acts (from a level update); it applies to the following tick. In the air it simply accelerates the
        /// player. On the ground, where the controller would brake a sideways push away at once, it shifts
        /// the velocity the player is steered toward: a push of 20 makes them drift at 5.
        /// </summary>
        public void AddPush(Vector3 acceleration) => push += acceleration;

        public void AddImpulse(Vector3 impulse) => SetVelocity(Body.linearVelocity + impulse / Body.mass);

        public void SetVelocity(Vector3 velocity)
        {
            Body.linearVelocity = velocity;
            commandedVelocity = velocity;
            expectedVelocity = velocity;
            commandedGroundVelocity = groundVelocity;
            // Leaving the ground on purpose: without this the ground handling would cancel the launch.
            if (Vector3.Dot(velocity - groundVelocity, GroundNormal) > LaunchSpeed * Mathf.Sqrt(Scale)) LeaveGround();
        }

        public void SetCheckpoint(Vector3 position, float yawDegrees, float pitchDegrees = 0f)
        {
            checkpointPosition = position;
            checkpointYaw = yawDegrees;
            checkpointPitch = pitchDegrees;
        }

        /// <summary>Back to the last checkpoint.</summary>
        public void Respawn()
        {
            Vector3 from = Body.position;
            Teleport(checkpointPosition, checkpointYaw, checkpointPitch);
            game.Events.RaisePlayerRespawned(new PlayerRespawnEvent { From = from, To = checkpointPosition });
        }

        /// <summary>
        /// Eye position blended between the previous and the current tick, for rendering. The blend is
        /// relative to what the player stands on: the ground's own movement during the tick is not
        /// interpolated, so a platform that carries the player does not shake against the camera.
        /// </summary>
        public Vector3 EyeAt(float alpha) =>
            Vector3.Lerp(interpolationStart, Body.position, Mathf.Clamp01(alpha)) + Vector3.up * EyeHeight;

        /// <summary>Back to scale 1, at rest, looking down +Z. Used when a level is loaded.</summary>
        internal void ResetState()
        {
            ApplyScale(1f);
            jumpHeld = false;
            push = Vector3.zero;
            ClearGround();
            steepCount = 0;
            lipCount = 0;
            Teleport(Vector3.zero, 0f, 0f);
            SetCheckpoint(Vector3.zero, 0f, 0f);
        }

        internal void Tick(in InputFrame input)
        {
            float dt = Sim.Dt;
            previousPosition = Body.position;
            AddLook(input.LookYaw, input.LookPitch);

            bool jumpPressed = input.Jump && !jumpHeld;
            jumpHeld = input.Jump;
            jumpBuffer = jumpPressed ? JumpBufferTime : jumpBuffer - dt;
            coyote = Grounded ? CoyoteTime : coyote - dt;

            Vector3 wish = Quaternion.Euler(0f, yaw, 0f) * new Vector3(input.MoveX, 0f, input.MoveZ);
            float wishAmount = Mathf.Min(1f, wish.magnitude);
            float speed = (input.Sprint ? SprintSpeed : WalkSpeed) * Scale * wishAmount;
            Vector3 velocity = Body.linearVelocity;
            Vector3 pushed = push;
            push = Vector3.zero;
            commandedRise = false;
            commandedApproach = 0f;
            commandedGroundVelocity = groundVelocity;

            if (Grounded)
            {
                Vector3 normal = GroundNormal;
                Vector3 gravity = Physics.gravity;
                // Off the ground a push is a plain acceleration; that part can lift the player off.
                float lift = Vector3.Dot(pushed, normal);
                if (lift > 0f) velocity += normal * (lift * dt);
                bool lifted = lift > -Vector3.Dot(gravity, normal);

                Vector3 relative = velocity - groundVelocity;
                float along = Vector3.Dot(relative, normal);
                Vector3 tangential = relative - normal * along;

                Vector3 target = Vector3.ProjectOnPlane(pushed, normal) * PushDriftTime;
                if (wishAmount > 1e-4f)
                {
                    Vector3 onSlope = Vector3.ProjectOnPlane(wish, normal);
                    if (onSlope.sqrMagnitude > 1e-8f) target += onSlope.normalized * speed;
                }
                // Walking (or being blown) into a face too steep to stand on must not lift the capsule up it.
                target = AwayFromSteepFaces(target);
                tangential = Vector3.MoveTowards(tangential, target, GroundAcceleration * Scale * dt);

                // The speed along the ground normal belongs to the solver, unless the last step showed that it
                // is not a launch: a bump (a seam, a lip, the top of a ramp), the ground dropping away under
                // the feet, or a platform changing its speed. Then it is taken out and the feet are put back.
                commandedApproach = Mathf.Max(0f, -along);
                if (holdGround && !lifted)
                {
                    if (along > 0f) along = 0f;
                    if (groundGap > 0f) along -= Mathf.Min(groundGap / dt, SnapSpeed * Scale);
                }
                commandedRise = along > 0.01f * Scale && !steppingUp;
                velocity = groundVelocity + tangential + normal * along;

                // The capsule has no friction, so gravity would drag it down any slope. Cancel the part of
                // gravity that lies in the plane it rests on; the contact takes care of the rest.
                Vector3 support = supportNormal;
                velocity -= (gravity - support * Vector3.Dot(gravity, support)) * dt;
            }
            else
            {
                velocity += pushed * dt;
                if (wishAmount > 1e-4f)
                {
                    // Air steering turns the velocity toward the wish direction and adds speed up to the
                    // walking speed, never beyond: momentum from a launch or a gust is kept, not added to.
                    // A steep face ahead takes the part of the wish that points into it, like a wall would.
                    Vector3 direction = AwayFromSteepFaces(wish / wish.magnitude);
                    float share = direction.magnitude;
                    if (share > 1e-4f)
                    {
                        direction /= share;
                        var horizontal = new Vector3(velocity.x, 0f, velocity.z);
                        float limit = Mathf.Max(horizontal.magnitude, speed);
                        float current = Vector3.Dot(horizontal, direction);
                        float add = Mathf.Clamp(speed * share - current, 0f, AirAcceleration * Scale * dt);
                        horizontal += direction * add;
                        float steeredSpeed = horizontal.magnitude;
                        if (steeredSpeed > limit) horizontal *= limit / steeredSpeed;
                        velocity = new Vector3(horizontal.x, velocity.y, horizontal.z);
                    }
                }
            }

            if (jumpBuffer > 0f && coyote > 0f)
            {
                float carried = Grounded ? Mathf.Max(0f, groundVelocity.y) : 0f;
                velocity.y = carried + JumpSpeed * Mathf.Sqrt(Scale);
                jumpBuffer = 0f;
                LeaveGround();
                game.Events.RaisePlayerJumped(new PlayerJumpEvent { Position = Body.position });
            }

            Body.linearVelocity = velocity;
            commandedVelocity = velocity;
            expectedVelocity = velocity + Physics.gravity * dt;
        }

        // Removes the part of a horizontal direction or velocity that points into a steep face.
        Vector3 AwayFromSteepFaces(Vector3 vector)
        {
            for (int i = 0; i < steepCount; i++)
            {
                Vector3 normal = steepNormals[i];
                var outward = new Vector3(normal.x, 0f, normal.z);
                float length = outward.magnitude;
                if (length < 1e-4f) continue;
                outward /= length;
                float into = Vector3.Dot(vector, outward);
                if (into < 0f) vector -= outward * into;
            }
            return vector;
        }

        void ClearGround()
        {
            Grounded = false;
            GroundCollider = null;
            GroundNormal = Vector3.up;
            GroundPoint = Vector3.zero;
            supportNormal = Vector3.up;
            groundVelocity = Vector3.zero;
            groundIsMover = false;
            groundGap = 0f;
            holdGround = false;
            steppingUp = false;
        }

        void LeaveGround()
        {
            ClearGround();
            coyote = 0f;
            jumpLock = JumpLockTicks;
        }

        internal void PostPhysics()
        {
            bool wasGrounded = Grounded;
            Vector3 before = expectedVelocity;

            FindFootContacts();
            LimitSteepFaces(wasGrounded);
            ProbeGround(wasGrounded, true);

            if (Grounded)
            {
                if (!wasGrounded && airTicks >= MinAirTicksForLanding)
                {
                    float impact = Mathf.Max(0f, -Vector3.Dot(before - groundVelocity, GroundNormal));
                    game.Events.RaisePlayerLanded(new PlayerLandEvent
                    {
                        Position = Body.position,
                        ImpactSpeed = impact,
                        Ground = GroundCollider,
                        Prop = PropRef.Of(GroundCollider),
                    });
                }
                airTicks = 0;
            }
            else
            {
                airTicks++;
            }

            // What carried the player this tick is not interpolated by the camera (see EyeAt).
            interpolationStart = previousPosition + (Grounded ? groundVelocity * Sim.Dt : Vector3.zero);
        }

        /// <summary>
        /// What the foot of the capsule touches right now, other than flat ground:
        /// steep faces - too steep to stand on but leaning back enough to push the capsule upward (only what
        /// the player cannot shove aside counts), and lips - walkable edges and slopes it can be lifted over.
        /// </summary>
        void FindFootContacts()
        {
            steepCount = 0;
            lipCount = 0;
            float radius = Radius;
            float skin = 0.03f * Scale;
            Vector3 center = Body.position + Vector3.up * radius;
            int count = game.PhysicsScene.OverlapSphere(center, radius + skin, nearby, Layers.SolidMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider other = nearby[i];
                // ClosestPoint is defined for primitives and convex meshes only.
                if (other is MeshCollider mesh && !mesh.convex) continue;
                if (!(other is BoxCollider || other is SphereCollider || other is CapsuleCollider || other is MeshCollider)) continue;
                // The foot shoves light props aside (PlayerContactScaler); they neither block nor lift.
                if (IsLightProp(other)) continue;
                if (PassesThrough(other)) continue;

                Vector3 point = other.ClosestPoint(center);
                Vector3 toCenter = center - point;
                float distance = toCenter.magnitude;
                if (distance < 1e-5f || distance > radius + skin) continue;
                // Judged as it will be on touching: the lip of a low step is still that far off, and from
                // further away it looks steeper than the contact it will make.
                float normalY = toCenter.y / Mathf.Min(distance, radius);
                if (normalY <= SteepMinNormalY) continue;
                Vector3 normal = toCenter / distance;
                if (normalY >= WalkableNormalY)
                {
                    if (normal.y < 0.996f && lipCount < MaxSteepContacts)
                    {
                        lipPoints[lipCount] = point;
                        lipNormals[lipCount] = normal;
                        lipCount++;
                    }
                    continue;
                }
                Rigidbody body = other.attachedRigidbody;
                if (body != null && !body.isKinematic && body.mass < Mass * 4f) continue;
                if (steepCount >= MaxSteepContacts) continue;
                steepNormals[steepCount] = normal;
                steepColliders[steepCount] = other;
                steepCount++;
            }
        }

        // Is one of the lips something else than the ground itself, sticking up out of it?
        bool LiftedByALip(in GroundHit ground)
        {
            float above = 0.005f * Scale;
            for (int i = 0; i < lipCount; i++)
            {
                if (Vector3.Dot(lipNormals[i], ground.Normal) >= 0.996f) continue;
                if (Vector3.Dot(lipPoints[i] - ground.Point, ground.Normal) > above) return true;
            }
            return false;
        }

        /// <summary>
        /// A frictionless capsule turns speed into a steep face into speed up that face. Take that back: such
        /// a face may stop the player or turn them aside, it never adds height.
        /// </summary>
        void LimitSteepFaces(bool wasGrounded)
        {
            if (steepCount == 0) return;
            Vector3 velocity = Body.linearVelocity;
            // On the ground the contact cancels gravity, so the commanded speed is what may remain.
            // It may also stop a fall; it just cannot make the player go up.
            float ceiling = Mathf.Max(0f, wasGrounded ? commandedVelocity.y : expectedVelocity.y) + 0.01f;
            bool changed = false;
            if (velocity.y > ceiling)
            {
                velocity.y = ceiling;
                changed = true;
            }
            for (int i = 0; i < steepCount; i++)
            {
                Vector3 normal = steepNormals[i];
                // What counts is the speed into the face, not the speed through the world: the edge of a
                // step that rides the same platform as the player (a plank on the next wagon of a train)
                // travels along with them, and hopping at it must not cost them the platform's speed.
                // (Only what the face does sideways: one that rises under the foot is the solver's.)
                Vector3 face = steepColliders[i] != null ? GroundPointVelocity(steepColliders[i], Body.position, out _) : Vector3.zero;
                face.y = 0f;
                float into = Vector3.Dot(velocity - face, normal);
                if (into >= 0f) continue;
                // Sideways out of the face, leaving the vertical speed as it is.
                var outward = new Vector3(normal.x, 0f, normal.z);
                float length = outward.magnitude;
                if (length < 1e-4f) continue;
                velocity -= outward * (into / (length * length));
                changed = true;
            }
            if (changed) Body.linearVelocity = velocity;
        }

        struct GroundHit
        {
            public Collider Collider;
            public Vector3 Point;
            /// <summary>The surface to walk on.</summary>
            public Vector3 Normal;
            /// <summary>The contact that carries the capsule (differs from Normal on an edge).</summary>
            public Vector3 Support;
            /// <summary>Vertical distance from the feet down to resting on it.</summary>
            public float Gap;
        }

        /// <summary>
        /// Looks for walkable ground under the feet with a sphere cast that starts inside the capsule, and
        /// with a ray straight down for what lies directly under the axis.
        /// </summary>
        bool FindGround(float extraReach, out GroundHit ground)
        {
            ground = default;
            float radius = Radius;
            float probeRadius = radius * 0.9f;
            float lift = radius * 0.5f;
            float tolerance = Mathf.Max(0.05f * Scale, 0.02f) + extraReach;
            Vector3 origin = Body.position + Vector3.up * (radius + lift);
            PhysicsScene physics = game.PhysicsScene;

            bool below = RayDown(physics, origin, radius + lift + tolerance, out RaycastHit ray) && ray.normal.y >= WalkableNormalY;

            // On a plane tilted by a, the probe sphere touches after lift + (radius - probeRadius) / cos(a).
            float reach = lift + (radius - probeRadius) / WalkableNormalY + tolerance;
            // A prop too light to carry the player counts only if it is right under the axis (the ray): the
            // side of the capsule does not climb it, it shoves it away (see PlayerContactScaler).
            if (SphereDown(physics, origin, probeRadius, reach, out RaycastHit hit)
                && hit.normal.y >= WalkableNormalY && !IsLightProp(hit.collider))
            {
                float resting = lift + (radius - probeRadius) / hit.normal.y;
                if (hit.distance <= resting + tolerance)
                {
                    // An edge is something to roll over or off: if there is ground right under the axis,
                    // that is what the player stands on.
                    if (!below || !IsEdge(hit))
                    {
                        ground.Collider = hit.collider;
                        ground.Point = hit.point;
                        ground.Normal = hit.normal;
                        ground.Support = hit.normal;
                        ground.Gap = Mathf.Max(0f, hit.distance - resting);
                        return true;
                    }
                }
            }
            // The sphere may have caught the lip of a step or a steep face while flat ground is right below.
            if (!below) return false;
            ground.Collider = ray.collider;
            ground.Point = ray.point;
            ground.Normal = ray.normal;
            ground.Support = ray.normal;
            ground.Gap = Mathf.Max(0f, ray.distance - (radius + lift));
            return true;
        }

        /// <summary>
        /// What the capsule does not collide with - a prop let go around the player, a heavy prop on its way
        /// through them (PerspectiveGrabber), a gadget part that would have crushed them - is no ground to
        /// stand on and no face in the way either. The queries below see it all the same, because ignoring
        /// a collision is a matter between two colliders and a query knows only one.
        /// </summary>
        bool PassesThrough(Collider other) => Physics.GetIgnoreCollision(other, Collider);

        // The first thing under the probe that the capsule can touch. Nearly always that is simply the
        // first thing; only when the capsule passes through that one are all of them looked at.
        bool RayDown(PhysicsScene physics, Vector3 origin, float distance, out RaycastHit hit)
        {
            if (!physics.Raycast(origin, Vector3.down, out hit, distance, Layers.SolidMask, QueryTriggerInteraction.Ignore)) return false;
            if (!PassesThrough(hit.collider)) return true;
            int count = physics.Raycast(origin, Vector3.down, probeHits, distance, Layers.SolidMask, QueryTriggerInteraction.Ignore);
            return NearestTouchable(count, out hit);
        }

        bool SphereDown(PhysicsScene physics, Vector3 origin, float radius, float distance, out RaycastHit hit)
        {
            if (!physics.SphereCast(origin, radius, Vector3.down, out hit, distance, Layers.SolidMask, QueryTriggerInteraction.Ignore)) return false;
            if (!PassesThrough(hit.collider)) return true;
            int count = physics.SphereCast(origin, radius, Vector3.down, probeHits, distance, Layers.SolidMask, QueryTriggerInteraction.Ignore);
            return NearestTouchable(count, out hit);
        }

        bool NearestTouchable(int count, out RaycastHit hit)
        {
            hit = default;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit candidate = probeHits[i];
                // A cast for all hits also reports what the probe starts inside of, at distance 0; the cast
                // for the first hit does not, and neither does this.
                if (candidate.distance <= 0f || PassesThrough(candidate.collider)) continue;
                if (found && candidate.distance >= hit.distance) continue;
                hit = candidate;
                found = true;
            }
            return found;
        }

        // A sphere cast reports the direction from the touched point to the sphere's center. On a face that
        // is the face normal; on an edge or a corner it is not.
        bool IsEdge(in RaycastHit hit)
        {
            var outward = new Vector3(hit.normal.x, 0f, hit.normal.z);
            float length = outward.magnitude;
            if (length < 1e-3f) return false;
            float nudge = 0.004f * Scale, above = 0.02f * Scale;
            Vector3 origin = hit.point - outward * (nudge / length) + Vector3.up * above;
            if (!hit.collider.Raycast(new Ray(origin, Vector3.down), out RaycastHit face, above * 3f)) return true;
            return Vector3.Dot(face.normal, hit.normal) < 0.996f;
        }

        internal static bool IsLightProp(Collider collider)
        {
            Rigidbody body = collider.attachedRigidbody;
            return body != null && !body.isKinematic && body.mass < Mass / PlayerContactScaler.MaxRatio;
        }

        /// <summary>
        /// Decides whether the player stands on something. After a physics step it also decides what the
        /// speed away from the ground means (see <see cref="holdGround"/>).
        /// </summary>
        void ProbeGround(bool wasGrounded, bool afterStep)
        {
            bool wasOnMover = groundIsMover;
            ClearGround();

            if (jumpLock > 0)
            {
                jumpLock--;
                return;
            }

            bool held = false;
            if (!FindGround(0f, out GroundHit ground))
            {
                // Still standing a moment ago: ground a little further down is held on to.
                float snap = SnapDistance * Scale + (wasOnMover ? MoverGripSpeed * Sim.Dt : 0f);
                if (afterStep && wasGrounded && !commandedRise && FindGround(snap, out ground))
                {
                    held = true;
                }
                else
                {
                    Wedge();
                    return;
                }
            }

            Vector3 velocity = GroundPointVelocity(ground.Collider, ground.Point, out bool mover);
            // On the way over a lip the feet leave the floor for a moment. That is neither a bump to take
            // back nor a reason to pull them down again.
            bool lifted = LiftedByALip(ground);
            bool hold = held && !lifted;
            if (afterStep)
            {
                Vector3 actual = Body.linearVelocity;
                float rising = Vector3.Dot(actual - velocity, ground.Normal);
                float launch = LaunchSpeed * Mathf.Sqrt(Scale);
                if (!wasGrounded)
                {
                    // Near the ground but on the way up from it (thrown back by a trampoline): not a landing.
                    if (rising > launch)
                    {
                        LeaveGround();
                        jumpLock = 0;
                        return;
                    }
                }
                else if (!commandedRise && rising > 1e-3f)
                {
                    // Moving away from the ground. Did something add speed, or was the player's own speed
                    // only turned upward (a seam, a lip, the crest of a ramp)?
                    float gained = (actual - velocity).magnitude - (commandedVelocity - commandedGroundVelocity).magnitude;
                    if (rising > launch && commandedApproach > launch)
                    {
                        // Came down hard and is on the way up again: the ground is bouncy.
                        LeaveGround();
                        jumpLock = 0;
                        return;
                    }
                    if (gained <= DeflectionTolerance * Scale)
                    {
                        hold = !lifted;
                    }
                    else if (mover && rising <= MoverGripSpeed)
                    {
                        // The platform changed its speed; its rider stays on it.
                        hold = true;
                    }
                    else if (rising > launch)
                    {
                        // Thrown by a seesaw, a piston, a prop from below: let go.
                        LeaveGround();
                        jumpLock = 0;
                        return;
                    }
                    else if (held && !lifted)
                    {
                        // Drifting off something that is not within reach any more.
                        return;
                    }
                }
            }

            Grounded = true;
            GroundCollider = ground.Collider;
            GroundNormal = ground.Normal;
            GroundPoint = ground.Point;
            supportNormal = ground.Support;
            groundVelocity = velocity;
            groundIsMover = mover;
            groundGap = ground.Gap * ground.Normal.y;
            holdGround = hold;
            steppingUp = lifted;
        }

        /// <summary>
        /// No single surface to stand on, but steep faces that lean against each other (a crevice) carry the
        /// capsule just the same. Without this the player would sit in it for good: no ground, no jump.
        /// </summary>
        void Wedge()
        {
            if (steepCount < 2) return;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < steepCount; i++) sum += steepNormals[i];
            if (sum.sqrMagnitude < 1e-6f || sum.normalized.y < WalkableNormalY) return;
            Grounded = true;
            GroundCollider = steepColliders[0];
            GroundNormal = Vector3.up;
            GroundPoint = Body.position;
            supportNormal = Vector3.up;
            groundVelocity = GroundPointVelocity(GroundCollider, Body.position, out bool mover);
            groundIsMover = mover;
        }

        static Vector3 GroundPointVelocity(Collider collider, Vector3 point, out bool isMover)
        {
            isMover = false;
            Rigidbody body = collider.attachedRigidbody;
            if (body == null) return Vector3.zero;
            if (!body.isKinematic) return body.GetPointVelocity(point);
            MoverRef reference = body.GetComponent<MoverRef>();
            if (reference == null || reference.Mover == null) return Vector3.zero;
            isMover = true;
            return reference.Mover.PointVelocity(point);
        }

        internal void Destroy()
        {
            Sim.Destroy(GameObject);
            Sim.Destroy(material);
        }
    }
}
