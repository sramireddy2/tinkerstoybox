using System.Collections.Generic;
using UnityEngine;

namespace Toybox.Engine
{
    /// <summary>
    /// The forced-perspective mechanic. While a prop is held, k = scale / distance-from-eye never changes,
    /// so its apparent size is constant; every tick it is pushed along the view ray to the farthest spot
    /// where it fits, which makes it grow (far) or shrink (near).
    ///
    /// The held prop is placed after the physics step, from the eye and the view the tick ends with, so at
    /// the end of every tick it sits exactly on the view ray. A drop places it once more first, because
    /// the view may have turned since (the mouse turns it between ticks).
    /// </summary>
    public sealed class PerspectiveGrabber
    {
        public const float GrabRange = 150f;
        public const float MaxHoldDistance = 150f;
        public const float YawStepDegrees = 15f;
        public const float PitchStepDegrees = 90f;

        // Half angle of the cone that catches props the crosshair narrowly misses.
        const float AssistDegrees = 2.5f;
        // Closest the held prop's center may come to the eye, per unit of player scale.
        const float NearDistance = 0.35f;
        const float MarchRatio = 1.08f;
        // Finest step of the march where something is near. A prop thinner than this (relative to its
        // distance) can still slip past an obstacle that is just as thin.
        const float MinSubRatio = 1.004f;
        // Fine steps one placement may spend; when they are used up the march goes on in coarse steps.
        const int MaxSubSteps = 256;
        const int BisectIterations = 8;
        // ComputePenetration reports grazing contacts with a near-zero depth; those are not overlaps.
        const float OverlapEpsilon = 1e-4f;
        // A prop heavier than the player that has sunk this far into the capsule (per unit of player scale)
        // is squeezing them against something. It passes through instead (see LetHeavyPropsPass).
        const float CrushDepth = 0.15f;
        /// <summary>
        /// A prop heavier than the player whose surface comes at them faster than this (units per second, per
        /// unit of player scale) passes through them instead of hitting them (see LetComingPropsPass).
        /// Measured (tools/out/notes/prelude-engine-fling.md): a hit from the side hands the player the
        /// prop's speed, and an edge that comes down on the capsule's round top squirts them out at up to
        /// four times it (1.0 gives 3.8, 1.7 gives 7.0, 5.5 gives 20), so this has to stay well below 2.
        /// </summary>
        public const float PassSpeed = 1f;
        /// <summary>
        /// Slower than <see cref="PassSpeed"/> a heavy prop is solid and shoves the player along. One that
        /// goes on shoving faster than this for <see cref="PushSeconds"/> passes through as well: a boulder
        /// rolling at walking pace would otherwise bulldoze them for as long as it rolls.
        /// </summary>
        public const float PushSpeed = 0.3f;
        public const float PushSeconds = 0.25f;
        /// <summary>
        /// What a heavy prop under the player's feet may hand them (units per second, per unit of player
        /// scale): it carries them, lifts them or launches them at up to this speed - less than their own
        /// jump (7.6). Faster than this it is no ground: it passes through them like any other prop, so
        /// nothing a prop does ever moves the player at 9 or more. Measured before this limit
        /// (tools/out/notes/prelude-verify.md): standing on a block that another one slides into, the
        /// player was carried off at up to 25 (21 units away); on the low end of a plank whose high end a
        /// block came down on, they were flung upward at up to 37.
        /// </summary>
        public const float CarrySpeed = 6f;
        // How far ahead a hit is seen coming, and the slack on top of it (per unit of player scale). Early
        // costs nothing - a prop that passes and never arrives has done nothing - and late is a hit.
        const float LookAhead = 0.05f;
        const float ReachMargin = 0.1f;
        // Closer than this (per unit of player scale) a prop is touching the capsule.
        const float TouchGap = 0.03f;
        /// <summary>Ticks a GrabPose takes to stand a tumbled prop up: 0.15 s.</summary>
        public const int PoseTicks = 9;

        static readonly float WalkableNormalY = Mathf.Cos(Player.MaxSlopeDegrees * Mathf.Deg2Rad);
        static readonly int PushTicks = Mathf.CeilToInt(PushSeconds / Sim.Dt - 1e-3f);

        struct Pusher
        {
            public Prop Prop;
            public int Ticks;
            public bool Seen;
        }

        readonly Game game;
        readonly Collider[] candidates = new Collider[256];
        readonly List<Prop> passingThroughPlayer = new List<Prop>();
        // Heavy props that are shoving the player right now, and for how many ticks they have been.
        readonly List<Pusher> pushers = new List<Pusher>();

        float ratio;
        float grabScale, grabDistance;
        Quaternion baseRotation = Quaternion.identity;
        Quaternion stepRotation = Quaternion.identity;
        // GrabPose: the hold eases baseRotation from the tilt at the grab to the pose the prop asks for.
        Quaternion poseFrom = Quaternion.identity, poseTo = Quaternion.identity;
        int poseTick = PoseTicks;
        Prop focus;
        int focusStamp = -1;
        Vector3 placedCenter;
        Quaternion placedRotation = Quaternion.identity;
        float placedScale;
        // The prop's transform shows a pose made for a rendered frame (Present), not the simulation's.
        bool presented;

        public Prop Held { get; private set; }
        public bool IsHolding => Held != null;
        /// <summary>scale / distance of the held prop: its apparent size.</summary>
        public float Ratio => ratio;
        /// <summary>Distance from the eye to the held prop's center after the last placement.</summary>
        public float HoldDistance { get; private set; }
        /// <summary>False while no free spot exists on the view ray and the prop stays where it last fitted.</summary>
        public bool PlacementValid { get; private set; }
        /// <summary>The held prop's scale when it was grabbed (0 while nothing is held).</summary>
        public float GrabScale => Held != null ? grabScale : 0f;
        /// <summary>Distance from the eye to the held prop's center when it was grabbed.</summary>
        public float GrabDistance => Held != null ? grabDistance : 0f;

        /// <summary>
        /// The focus candidate: the prop a grab would take right now, or null - also while something is held.
        /// It is worked out at most once per tick (on first use after the tick, from the view at that
        /// moment) and remembered until the next one, so presentation code may ask every frame.
        /// </summary>
        public Prop Focus
        {
            get
            {
                if (Held != null) return null;
                if (focusStamp != game.TickCount)
                {
                    focusStamp = game.TickCount;
                    focus = game.Level != null ? FindTarget() : null;
                    FocusQueries++;
                }
                if (focus != null && (focus.Removed || !focus.Grabbable)) focus = null;
                return focus;
            }
        }

        /// <summary>How many times <see cref="Focus"/> has actually searched (it is cached per tick).</summary>
        public int FocusQueries { get; private set; }

        internal PerspectiveGrabber(Game game) => this.game = game;

        /// <summary>The prop a grab would take right now, or null.</summary>
        public Prop FindTarget()
        {
            Player player = game.Player;
            Vector3 eye = player.Eye;
            Vector3 direction = player.Forward;

            Prop target = null;
            if (game.PhysicsScene.Raycast(eye, direction, out RaycastHit hit, GrabRange, Layers.SolidMask, QueryTriggerInteraction.Ignore))
            {
                target = PropRef.Of(hit.collider);
                if (target != null && !target.Grabbable) target = null;
            }
            if (target == null) target = AssistTarget(eye, direction);
            if (target == null) return null;
            // Lifting the thing you stand on would let you fly.
            if (player.Grounded && player.GroundProp == target) return null;
            return target;
        }

        // The grabbable prop whose surface is closest to the view ray, within a narrow cone, and in plain view
        // at that spot. Measured to the colliders, not to the bounding sphere: a long plank is not "close"
        // to a crosshair that points past its end or over it.
        Prop AssistTarget(Vector3 eye, Vector3 direction)
        {
            Prop best = null;
            float bestAngle = AssistDegrees;
            IReadOnlyList<Prop> props = game.Props;
            for (int i = 0; i < props.Count; i++)
            {
                Prop prop = props[i];
                if (prop.Removed || prop.Held || !prop.Grabbable) continue;
                Vector3 to = prop.Center - eye;
                float distance = to.magnitude;
                if (distance < 1e-3f || distance - prop.Radius > GrabRange) continue;
                // Cheap rejection: not even the bounding sphere comes near the ray.
                float angularRadius = Mathf.Asin(Mathf.Clamp01(prop.Radius / distance)) * Mathf.Rad2Deg;
                if (Vector3.Angle(direction, to) - angularRadius > bestAngle) continue;

                if (!NearestToRay(prop, eye, direction, out Vector3 point, out Vector3 inside, out float angle)) continue;
                if (angle >= bestAngle) continue;
                float range = Vector3.Distance(eye, point);
                if (range < 1e-3f || range > GrabRange) continue;
                // It has to be visible there. The ray goes for a point just inside the surface so that it
                // cannot slip past a silhouette edge.
                Vector3 toward = inside - eye;
                float reach = toward.magnitude;
                if (!game.PhysicsScene.Raycast(eye, toward / reach, out RaycastHit hit, reach + 0.01f, Layers.SolidMask, QueryTriggerInteraction.Ignore)) continue;
                if (PropRef.Of(hit.collider) != prop) continue;
                best = prop;
                bestAngle = angle;
            }
            return best;
        }

        // The point of the prop's colliders that the view ray passes closest to (as an angle from the eye),
        // and a point slightly inside that collider.
        static bool NearestToRay(Prop prop, Vector3 eye, Vector3 direction, out Vector3 point, out Vector3 inside, out float angle)
        {
            point = default;
            inside = default;
            angle = float.MaxValue;
            Collider[] colliders = prop.Colliders;
            for (int c = 0; c < colliders.Length; c++)
            {
                Collider collider = colliders[c];
                if (collider == null || !collider.enabled || collider.isTrigger) continue;
                Vector3 middle = collider.bounds.center;
                // Closest point of the collider to the ray, by alternating between the two a few times.
                Vector3 onRay = eye + direction * Mathf.Max(0f, Vector3.Dot(middle - eye, direction));
                Vector3 nearest = collider.ClosestPoint(onRay);
                for (int i = 0; i < 2; i++)
                {
                    onRay = eye + direction * Mathf.Max(0f, Vector3.Dot(nearest - eye, direction));
                    nearest = collider.ClosestPoint(onRay);
                }
                float off = Vector3.Angle(direction, nearest - eye);
                if (off >= angle) continue;
                angle = off;
                point = nearest;
                inside = Vector3.MoveTowards(nearest, middle, 0.02f * prop.Radius);
            }
            return angle < float.MaxValue;
        }

        public bool TryGrab()
        {
            if (Held != null) return false;
            Prop prop = FindTarget();
            if (prop == null) return false;
            float distance = Vector3.Distance(game.Player.Eye, prop.Center);
            if (distance < 1e-3f) return false;

            Held = prop;
            grabScale = prop.Scale;
            grabDistance = distance;
            ratio = grabScale / distance;
            HoldDistance = distance;
            baseRotation = Quaternion.Inverse(Quaternion.Euler(0f, game.Player.Yaw, 0f)) * prop.Rotation;
            stepRotation = Quaternion.identity;
            poseFrom = baseRotation;
            poseTo = PoseTarget(baseRotation, prop.GrabPose);
            poseTick = Quaternion.Angle(poseFrom, poseTo) < 0.01f ? PoseTicks : 0;
            placedCenter = prop.Center;
            placedRotation = prop.Rotation;
            placedScale = prop.Scale;
            PlacementValid = true;
            presented = false;

            prop.SetHeld(true);
            Physics.SyncTransforms();
            // Whatever rested on the prop has lost its support.
            game.WakeAll();
            game.Events.RaisePropGrabbed(HoldEvent(prop, distance));
            return true;
        }

        /// <summary>Releases the held prop where the view puts it right now, at the scale it has there.</summary>
        public void Drop()
        {
            Prop prop = Held;
            if (prop == null) return;
            // Let go early, the prop is still put down the way its GrabPose wants it.
            if (poseTick < PoseTicks)
            {
                poseTick = PoseTicks;
                baseRotation = poseTo;
            }
            UpdatePlacement();
            Held = null;
            float distance = Vector3.Distance(game.Player.Eye, prop.Center);

            prop.SetHeld(false);
            if (OverlapsPlayer(prop))
            {
                // Released on top of or around the player: let it pass through instead of launching them.
                SetPlayerCollision(prop, false);
                passingThroughPlayer.Add(prop);
            }
            game.WakeAll();
            game.Events.RaisePropDropped(HoldEvent(prop, distance));
        }

        /// <summary>Turns the held prop in 15 degree yaw steps and 90 degree pitch steps, relative to the view.</summary>
        public void RotateHeld(int yawSteps, int pitchSteps)
        {
            if (Held == null) return;
            if (!Held.AllowPitch) pitchSteps = 0;
            if (yawSteps != 0)
                stepRotation = Quaternion.AngleAxis(YawStepDegrees * yawSteps, Vector3.up) * stepRotation;
            if (pitchSteps != 0)
                stepRotation = Quaternion.AngleAxis(PitchStepDegrees * pitchSteps, Vector3.right) * stepRotation;
        }

        /// <summary>
        /// For the render layer: shows the held prop where a camera at this eye, looking this way, sees it on
        /// the crosshair - between ticks the view turns and the eye is interpolated, and a prop left at its
        /// tick pose would trail behind. This only moves what is drawn. The simulation keeps its own
        /// placement and puts the prop back there when the next tick begins.
        /// </summary>
        public void Present(Vector3 eye, Quaternion look)
        {
            Prop prop = Held;
            if (prop == null || !PlacementValid) return;
            Quaternion rotation = Quaternion.Euler(0f, look.eulerAngles.y, 0f) * stepRotation * baseRotation;
            prop.PlaceHeld(eye + look * Vector3.forward * HoldDistance, rotation, placedScale);
            presented = true;
        }

        /// <summary>Before anything of the tick runs: the held prop is where the simulation placed it, not where a frame drew it.</summary>
        internal void BeginTick() => RestorePlacement();

        internal void Tick(in InputFrame input)
        {
            if (Held != null)
            {
                RotateHeld(input.RotateYaw, input.RotatePitch ? 1 : 0);
                if (input.GrabPressed) Drop();
            }
            else if (input.GrabPressed)
            {
                TryGrab();
            }
        }

        /// <summary>
        /// Right before the physics step, when the player, the level and the gadgets have all had their say
        /// about this tick's velocities: the props that would hit the player in it pass through instead.
        /// </summary>
        internal void BeforePhysics() => LetComingPropsPass();

        /// <summary>
        /// Right after the physics step, before the player's controller reads what it did: a throw that could
        /// not be seen coming is taken back (see <see cref="TakeBackThrows"/>).
        /// </summary>
        internal void AfterPhysics() => TakeBackThrows();

        /// <summary>After the physics step: the player has moved, so this is where the held prop is placed.</summary>
        internal void PostPhysics()
        {
            LetHeavyPropsPass();
            for (int i = passingThroughPlayer.Count - 1; i >= 0; i--)
            {
                Prop prop = passingThroughPlayer[i];
                if (prop.Removed)
                {
                    passingThroughPlayer.RemoveAt(i);
                    continue;
                }
                if (prop.Held || OverlapsPlayer(prop)) continue;
                // Out of the player, but still on its way at them: not yet.
                if (CanThrow(prop) && ComesAtPlayer(prop, out _)) continue;
                SetPlayerCollision(prop, true);
                passingThroughPlayer.RemoveAt(i);
            }

            if (Held != null)
            {
                if (poseTick < PoseTicks)
                {
                    poseTick++;
                    float t = (float)poseTick / PoseTicks;
                    baseRotation = poseTick >= PoseTicks ? poseTo : Quaternion.Slerp(poseFrom, poseTo, t * t * (3f - 2f * t));
                }
                UpdatePlacement();
                game.Events.RaisePropHeld(HoldEvent(Held, Vector3.Distance(game.Player.Eye, Held.Center)));
            }
        }

        /// <summary>
        /// Ends the hold without placing the prop (it is about to be respawned or removed). PropDropped is
        /// raised all the same, so that whoever pairs it with PropGrabbed is not left waiting.
        /// </summary>
        internal void Forget(Prop prop)
        {
            if (Held != prop) return;
            RestorePlacement();
            Held = null;
            float distance = Vector3.Distance(game.Player.Eye, prop.Center);
            prop.SetHeld(false);
            game.Events.RaisePropDropped(HoldEvent(prop, distance));
        }

        /// <summary>Drops all state; the level is being torn down.</summary>
        internal void Reset()
        {
            Held = null;
            presented = false;
            passingThroughPlayer.Clear();
            pushers.Clear();
            focus = null;
            focusStamp = -1;
            poseTick = PoseTicks;
        }

        // Where a GrabPose takes a rotation (given in the yaw frame, in which up is still up): the smallest
        // turn that stands the chosen axis of the prop upright, which leaves its heading alone.
        static Quaternion PoseTarget(Quaternion rotation, GrabPose pose)
        {
            if (pose == GrabPose.Keep) return rotation;
            Vector3 axis = Vector3.up;
            if (pose == GrabPose.Snap90)
            {
                // Whichever of the prop's own six axes points most nearly up.
                Vector3 up = Quaternion.Inverse(rotation) * Vector3.up;
                float x = Mathf.Abs(up.x), y = Mathf.Abs(up.y), z = Mathf.Abs(up.z);
                if (y >= x && y >= z) axis = new Vector3(0f, Mathf.Sign(up.y), 0f);
                else if (x >= z) axis = new Vector3(Mathf.Sign(up.x), 0f, 0f);
                else axis = new Vector3(0f, 0f, Mathf.Sign(up.z));
            }
            Vector3 current = rotation * axis;
            float dot = Vector3.Dot(current, Vector3.up);
            if (dot > 0.999999f) return rotation;
            // Upside down there is no smallest turn: roll it over about its own forward axis.
            if (dot < -0.9999f) return Quaternion.AngleAxis(180f, rotation * Vector3.forward) * rotation;
            return Quaternion.FromToRotation(current, Vector3.up) * rotation;
        }

        PropHoldEvent HoldEvent(Prop prop, float distance) => new PropHoldEvent
        {
            Prop = prop,
            OldScale = grabScale,
            NewScale = prop.Scale,
            GrabDistance = grabDistance,
            DropDistance = distance,
        };

        // A pose shown for a rendered frame is not the simulation's: back to the last placement.
        void RestorePlacement()
        {
            if (!presented) return;
            presented = false;
            Held.PlaceHeld(placedCenter, placedRotation, placedScale);
        }

        void UpdatePlacement()
        {
            Prop prop = Held;
            presented = false;
            Player player = game.Player;
            Vector3 eye = player.Eye;
            Vector3 direction = player.Forward;
            Quaternion rotation = Quaternion.Euler(0f, player.Yaw, 0f) * stepRotation * baseRotation;

            float near = Mathf.Max(NearDistance * player.Scale, prop.MinScale / ratio);
            float far = Mathf.Min(MaxHoldDistance, prop.MaxScale / ratio);
            // The prop's center can never pass through a surface, however small the prop is on screen.
            if (game.PhysicsScene.Raycast(eye, direction, out RaycastHit hit, far, Layers.SolidMask, QueryTriggerInteraction.Ignore))
                far = Mathf.Min(far, hit.distance);

            PlacementValid = far >= near && !Overlaps(prop, eye, direction, rotation, near);
            if (PlacementValid)
            {
                // A step of the march only tests the pose it arrives at. Whatever the prop meets on the way
                // is seen as long as the depth it covers at one step overlaps the depth it covers at the
                // next: thin * 2 of its distance. A chunky prop held close manages that with the coarse
                // ratio. A thin one, or one grabbed from far away, would step right over a thin wall or a row
                // of bars - so where anything at all is near its path, the step is walked in finer ones.
                float thin = prop.BaseThinness * ratio;
                float subRatio = thin < 0.5f ? Mathf.Clamp((1f + thin) / (1f - thin), MinSubRatio, MarchRatio) : MarchRatio;
                bool subdivide = subRatio < MarchRatio - 1e-4f;
                int budget = MaxSubSteps;

                float free = near;
                float blocked = -1f;
                while (free < far && blocked < 0f)
                {
                    float next = Mathf.Min(free * MarchRatio, far);
                    if (subdivide && budget > 0 && SweepTouches(prop, eye, direction, rotation, free, next))
                    {
                        while (free < next)
                        {
                            float step = Mathf.Min(free * subRatio, next);
                            budget--;
                            if (Overlaps(prop, eye, direction, rotation, step))
                            {
                                blocked = step;
                                break;
                            }
                            free = step;
                        }
                    }
                    else if (Overlaps(prop, eye, direction, rotation, next))
                    {
                        blocked = next;
                    }
                    else
                    {
                        free = next;
                    }
                }
                if (blocked > 0f)
                {
                    for (int i = 0; i < BisectIterations; i++)
                    {
                        float middle = (free + blocked) * 0.5f;
                        if (Overlaps(prop, eye, direction, rotation, middle)) blocked = middle;
                        else free = middle;
                    }
                    // Leave a hair of clearance so the prop is released just off the surface, not in contact.
                    free = Mathf.Max(near, free - Mathf.Max(0.005f, 0.002f * free));
                }
                HoldDistance = free;
                placedCenter = eye + direction * free;
                placedRotation = rotation;
                placedScale = ratio * free;
            }

            // With no room anywhere on the ray the prop stays at the last pose that was free. No sync is
            // needed here: nothing queries a held prop, and both Drop and the physics step sync by themselves.
            prop.PlaceHeld(placedCenter, placedRotation, placedScale);
        }

        // Is anything near the path of the held prop between two distances on the view ray? Seen in the
        // prop's own axes every point of it moves along a straight line as the distance grows, so the box
        // around its bounds at the two ends contains its bounds at every distance in between.
        bool SweepTouches(Prop prop, Vector3 eye, Vector3 direction, Quaternion rotation, float from, float to)
        {
            Vector3 along = Quaternion.Inverse(rotation) * direction;
            Vector3 half = prop.LocalHalfExtents * ratio;
            Vector3 min = Vector3.Min((along - half) * from, (along - half) * to);
            Vector3 max = Vector3.Max((along + half) * from, (along + half) * to);
            Vector3 center = eye + rotation * ((min + max) * 0.5f);
            return game.PhysicsScene.OverlapBox(center, (max - min) * 0.5f, candidates, rotation, Layers.SolidMask, QueryTriggerInteraction.Ignore) > 0;
        }

        // Would the held prop, moved to this distance on the view ray, intersect the world or another prop?
        bool Overlaps(Prop prop, Vector3 eye, Vector3 direction, Quaternion rotation, float distance)
        {
            float scale = ratio * distance;
            Vector3 center = eye + direction * distance;

            // Broad phase: what touches the prop's bounding box at that pose? The held prop itself is on the
            // Held layer and the player on the Player layer; neither is in the mask. Most steps of the march
            // end here, which matters because the exact test below needs a transform sync, and that is slow.
            int count = game.PhysicsScene.OverlapBox(center, prop.LocalHalfExtents * scale, candidates, rotation, Layers.SolidMask, QueryTriggerInteraction.Ignore);
            if (count == 0) return false;

            // Exact phase: move the real colliders there and test each against each candidate.
            prop.PlaceHeld(center, rotation, scale);
            Physics.SyncTransforms();
            Collider[] colliders = prop.Colliders;
            for (int c = 0; c < colliders.Length; c++)
            {
                Collider own = colliders[c];
                if (own == null || !own.enabled || own.isTrigger) continue;
                Transform ownTransform = own.transform;
                Vector3 ownPosition = ownTransform.position;
                Quaternion ownRotation = ownTransform.rotation;
                for (int i = 0; i < count; i++)
                {
                    Collider other = candidates[i];
                    Transform otherTransform = other.transform;
                    if (Physics.ComputePenetration(own, ownPosition, ownRotation, other, otherTransform.position, otherTransform.rotation, out _, out float depth)
                        && depth > OverlapEpsilon)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Nothing throws the player. A dynamic prop that outweighs them hands them its own speed when it
        /// hits them, and squirts them out at several times that when it catches them against the floor (a
        /// domino the size of a house toppling onto them: 35 to 75 units a second). So a heavy prop whose
        /// surface comes at the player faster than <see cref="PassSpeed"/> stops colliding with them before
        /// it arrives, exactly like a prop that is let go around them: it passes through, and is solid for
        /// them again when it is no longer coming at them and they are out of it. A slower one is solid and
        /// shoves them, for <see cref="PushSeconds"/> at most; then it passes too.
        ///
        /// What counts is the prop's own movement, so walking into a heavy prop, or being carried into one,
        /// never makes it give way; nor does what the player stands on, whatever it does (it carries them,
        /// and a prop that comes up from below lifts or launches them, as a seesaw does). Kinematic props -
        /// held, fixed, frozen, driven by a gadget - are not this rule's business.
        /// </summary>
        void LetComingPropsPass()
        {
            for (int i = 0; i < pushers.Count; i++)
            {
                Pusher pusher = pushers[i];
                pusher.Seen = false;
                pushers[i] = pusher;
            }

            IReadOnlyList<Prop> props = game.Props;
            for (int i = 0; i < props.Count; i++)
            {
                Prop prop = props[i];
                if (!CanThrow(prop)) continue;
                if (!ComesAtPlayer(prop, out bool fast)) continue;
                if (fast || passingThroughPlayer.Contains(prop) || Pushed(prop) >= PushTicks) LetPass(prop);
            }

            for (int i = pushers.Count - 1; i >= 0; i--)
                if (!pushers[i].Seen) pushers.RemoveAt(i);
        }

        // Counts one more tick of this prop shoving the player, and says how many that makes in a row.
        int Pushed(Prop prop)
        {
            for (int i = 0; i < pushers.Count; i++)
            {
                Pusher pusher = pushers[i];
                if (pusher.Prop != prop) continue;
                pusher.Ticks++;
                pusher.Seen = true;
                pushers[i] = pusher;
                return pusher.Ticks;
            }
            pushers.Add(new Pusher { Prop = prop, Ticks = 1, Seen = true });
            return 1;
        }

        void LetPass(Prop prop)
        {
            for (int i = pushers.Count - 1; i >= 0; i--)
                if (pushers[i].Prop == prop) pushers.RemoveAt(i);
            // Said again even if it is on the list: a gadget may have made the prop solid in between.
            SetPlayerCollision(prop, false);
            if (!passingThroughPlayer.Contains(prop)) passingThroughPlayer.Add(prop);
        }

        // Is this a prop that could throw the player: simulated, and heavier than they are?
        bool CanThrow(Prop prop) =>
            !prop.Removed && !prop.Held && !prop.Body.isKinematic && prop.Mass > Player.Mass;

        /// <summary>
        /// Is a heavy prop about to hit the player (`fast`), or shoving them? Judged where the prop is
        /// nearest to the capsule, and where it is nearest to the capsule's foot and to its head - the part
        /// of a toppling slab that is nearest now is not always the part that arrives first.
        /// </summary>
        bool ComesAtPlayer(Prop prop, out bool fast)
        {
            fast = false;
            Player player = game.Player;
            float scale = player.Scale;
            Rigidbody body = prop.Body;
            // No point of the prop moves faster than this. (Nearly every prop is at rest and ends here.)
            float fastest = body.linearVelocity.magnitude + body.angularVelocity.magnitude * prop.Radius * 2f;
            if (fastest <= PushSpeed * scale) return false;
            // What the player stands on carries them; it is never in their way - unless it goes faster than
            // anything may carry them. Then it is no ground: it goes on without them.
            if (player.GroundProp == prop)
            {
                fast = body.GetPointVelocity(player.Position).magnitude > CarrySpeed * scale;
                return fast;
            }

            float radius = player.Radius;
            Vector3 low = player.Position + Vector3.up * radius;
            Vector3 high = player.Position + Vector3.up * (player.Height - radius);
            Vector3 middle = (low + high) * 0.5f;
            Vector3 playerVelocity = player.Velocity;
            float margin = ReachMargin * scale;
            // Too far away to get here in time, however it moves.
            float apart = Vector3.Distance(prop.Center, middle) - prop.Radius - player.Height * 0.5f;
            if (apart > (fastest + playerVelocity.magnitude) * LookAhead + margin) return false;

            bool pushing = false;
            Collider[] colliders = prop.Colliders;
            for (int c = 0; c < colliders.Length; c++)
            {
                Collider collider = colliders[c];
                if (collider == null || !collider.enabled || collider.isTrigger) continue;

                // The nearest pair of points of the collider and the capsule's axis, by going back and forth
                // between the two (both are convex, so this arrives at the nearest pair).
                Vector3 onAxis = middle;
                Vector3 onProp = collider.ClosestPoint(onAxis);
                Vector3 axis = high - low;
                for (int i = 0; i < 3; i++)
                {
                    onAxis = low + axis * Mathf.Clamp01(Vector3.Dot(onProp - low, axis) / axis.sqrMagnitude);
                    onProp = collider.ClosestPoint(onAxis);
                }
                Weigh(body, onAxis, onProp, playerVelocity, ref fast, ref pushing);
                Weigh(body, low, collider.ClosestPoint(low), playerVelocity, ref fast, ref pushing);
                Weigh(body, high, collider.ClosestPoint(high), playerVelocity, ref fast, ref pushing);
                if (fast) return true;
            }
            return pushing;
        }

        // One point of a heavy prop against one point of the capsule's axis: is it coming, and how soon?
        void Weigh(Rigidbody body, Vector3 onAxis, Vector3 onProp, Vector3 playerVelocity, ref bool fast, ref bool pushing)
        {
            Player player = game.Player;
            Vector3 to = onAxis - onProp;
            float distance = to.magnitude;
            // The axis itself is inside the prop: no telling which way is out. That is a squeeze (below).
            if (distance < 1e-5f) return;
            Vector3 normal = to / distance;
            float scale = player.Scale;
            float gap = distance - player.Radius;

            Vector3 velocity = body.GetPointVelocity(onProp);
            float closing = Mathf.Max(0f, Vector3.Dot(velocity - playerVelocity, normal));
            bool near = gap <= closing * LookAhead + ReachMargin * scale;
            // Under the foot, where the player would come to stand on it: faster than anything may carry
            // them (sliding, rolling or rising) it would whisk them off or launch them. It is no ground.
            if (near && normal.y >= WalkableNormalY)
            {
                float speed = velocity.magnitude;
                if (player.Grounded) speed = Mathf.Min(speed, (velocity - player.GroundVelocity).magnitude);
                if (speed > CarrySpeed * scale)
                {
                    fast = true;
                    return;
                }
            }

            float approach = Toward(velocity, normal);
            // A prop that rides along on what the player rides on is not coming at them either.
            if (player.Grounded) approach = Mathf.Min(approach, Toward(velocity - player.GroundVelocity, normal));
            if (approach <= PushSpeed * scale) return;

            if (approach > PassSpeed * scale && near) fast = true;
            else if (gap <= TouchGap * scale) pushing = true;
        }

        // How fast a point of a prop moves toward the player along the line between them. Under the foot -
        // where the player could stand on it - coming up does not count: that lifts them, or launches them,
        // and both are meant (up to CarrySpeed: see Weigh). Sliding into the foot from the side does.
        static float Toward(Vector3 velocity, Vector3 normal) =>
            normal.y >= WalkableNormalY ? velocity.x * normal.x + velocity.z * normal.z : Vector3.Dot(velocity, normal);

        /// <summary>
        /// Nothing throws the player, the part that cannot be seen coming. Before the step only a prop that
        /// is already moving can be told to pass. But a step can set a heavy prop going and have it hit the
        /// player all at once: a block that rests against them is struck by another; the block they stand on
        /// is knocked away, or tipped up under their feet by something that lands on its other end. So after
        /// every step: if it left the player more than <see cref="PassSpeed"/> faster than they were going by
        /// themselves, moving at more than <see cref="CarrySpeed"/> the way they were pushed, and a heavy
        /// prop that is moving is at the capsule, that was a throw. It is taken back - the player is where,
        /// and as fast as, they would be had the prop not been there - and the prop passes through them from
        /// now on, like one that was seen coming.
        ///
        /// The prop at the capsule may itself be a light one that a heavy one drove into the player (a crate
        /// between them and a sliding block): it is told by having sent the player off faster than its own
        /// momentum could (<see cref="DrivenIntoThePlayer"/>), and passes while it is being pushed through
        /// them. A light prop that flies at the player by itself pushes them as it always did.
        ///
        /// And what the player stood on during the step and has been set going faster than it may carry
        /// them passes too, before the controller takes its speed for the ground's.
        ///
        /// A kinematic platform that launches its rider is a gadget's business and is left alone.
        /// </summary>
        void TakeBackThrows()
        {
            Player player = game.Player;
            float scale = player.Scale;
            Vector3 actual = player.Velocity;
            Vector3 gained = actual - player.ExpectedVelocity;
            float gain = gained.magnitude;
            if (gain > PassSpeed * scale)
            {
                Vector3 way = gained / gain;
                float sent = Vector3.Dot(actual, way);
                if (sent > CarrySpeed * scale && !LaunchedByAMover(player, way, scale))
                {
                    bool thrown = false;
                    // They may have parted again by now, by as much as the throw covers in a step.
                    float reach = (TouchGap + ReachMargin) * scale + gain * Sim.Dt;
                    IReadOnlyList<Prop> props = game.Props;
                    for (int i = 0; i < props.Count; i++)
                    {
                        Prop prop = props[i];
                        if (prop.Removed || prop.Held || prop.Body.isKinematic) continue;
                        // A prop the player outweighs pushes them by its own momentum, as it always did. Only
                        // when it sent them off faster than that had something heavier driven it into them.
                        if (prop.Mass <= Player.Mass && !DrivenIntoThePlayer(prop, sent, scale)) continue;
                        if (!Threw(prop, way, reach, scale)) continue;
                        LetPass(prop);
                        thrown = true;
                    }
                    if (thrown) player.TakeBack();
                }
            }

            Prop ground = player.GroundProp;
            if (ground != null && CanThrow(ground) && ground.Body.GetPointVelocity(player.Position).magnitude > CarrySpeed * scale)
                LetPass(ground);
        }

        // A light prop that flies into the player hands them its momentum: at most m / (m + M) of the speed
        // it went into the step with (twice that if it is perfectly bouncy). If the player came out of the
        // step faster than that - with room for the solver's roughness - the prop did not have it to give:
        // a heavy one behind it (a crate between the player and a sliding block) pushed through it.
        static bool DrivenIntoThePlayer(Prop prop, float sent, float scale)
        {
            float mass = prop.Mass;
            float own = prop.StepVelocity.magnitude * mass / (mass + Player.Mass) * (1f + prop.Material.bounciness);
            return sent > own * 1.25f + 0.5f * scale;
        }

        // The platform under the player moves that way itself: the launch is its doing, not a prop's.
        static bool LaunchedByAMover(Player player, Vector3 way, float scale)
        {
            if (!player.Grounded || player.GroundCollider == null) return false;
            Rigidbody body = player.GroundCollider.attachedRigidbody;
            if (body == null || !body.isKinematic) return false;
            MoverRef reference = body.GetComponent<MoverRef>();
            if (reference == null || reference.Mover == null) return false;
            return Vector3.Dot(reference.Mover.PointVelocity(player.Position), way) > PassSpeed * scale;
        }

        // Could this prop have thrown the player that way in the step that just ended: is it at the capsule,
        // solid for it, and moving - on the side the push came from, or into them (a squeeze sends the
        // player out sideways, not the way the prop goes)? One that merely passes by while something else
        // sends the player off (a trampoline) is not it.
        bool Threw(Prop prop, Vector3 way, float reach, float scale)
        {
            Player player = game.Player;
            float radius = player.Radius;
            Vector3 low = player.Position + Vector3.up * radius;
            Vector3 high = player.Position + Vector3.up * (player.Height - radius);
            Vector3 middle = (low + high) * 0.5f;
            if (Vector3.Distance(prop.Center, middle) - prop.Radius - player.Height * 0.5f > reach) return false;

            Rigidbody body = prop.Body;
            CapsuleCollider capsule = player.Collider;
            Collider[] colliders = prop.Colliders;
            for (int c = 0; c < colliders.Length; c++)
            {
                Collider collider = colliders[c];
                if (collider == null || !collider.enabled || collider.isTrigger) continue;
                if (Physics.GetIgnoreCollision(collider, capsule)) continue;
                Vector3 onAxis = middle;
                Vector3 onProp = collider.ClosestPoint(onAxis);
                Vector3 axis = high - low;
                for (int i = 0; i < 3; i++)
                {
                    onAxis = low + axis * Mathf.Clamp01(Vector3.Dot(onProp - low, axis) / axis.sqrMagnitude);
                    onProp = collider.ClosestPoint(onAxis);
                }
                Vector3 to = onAxis - onProp;
                float distance = to.magnitude;
                if (distance - radius > reach) continue;
                Vector3 velocity = body.GetPointVelocity(onProp);
                if (velocity.magnitude <= PassSpeed * scale) continue;
                // The axis itself is inside it: no telling the side, and no doubt that it is at them.
                if (distance < 1e-5f) return true;
                Vector3 normal = to / distance;
                if (Vector3.Dot(normal, way) > 0.2f || Vector3.Dot(velocity, normal) > PassSpeed * scale) return true;
            }
            return false;
        }

        /// <summary>
        /// The squeeze, which has nothing to do with speed: a prop much heavier than the player that has come
        /// to lie on them (let go just overhead, or put there by a respawn) presses the capsule against the
        /// floor; the solver then squirts the player out sideways. Nothing can crush the player in this game,
        /// so such a prop passes through them as well, until they have separated.
        /// </summary>
        void LetHeavyPropsPass()
        {
            Player player = game.Player;
            float radius = player.Radius;
            Vector3 feet = player.Position;
            int count = game.PhysicsScene.OverlapCapsule(feet + Vector3.up * radius, feet + Vector3.up * (player.Height - radius), radius,
                candidates, Layers.PropMask, QueryTriggerInteraction.Ignore);
            if (count == 0) return;

            float limit = CrushDepth * player.Scale;
            CapsuleCollider capsule = player.Collider;
            for (int i = 0; i < count; i++)
            {
                Collider own = candidates[i];
                Prop prop = PropRef.Of(own);
                if (prop == null || prop.Removed || prop.Held || prop.Body.isKinematic) continue;
                if (prop.Mass <= Player.Mass || passingThroughPlayer.Contains(prop)) continue;
                Transform ownTransform = own.transform;
                if (!Physics.ComputePenetration(own, ownTransform.position, ownTransform.rotation, capsule, feet, Quaternion.identity,
                        out Vector3 direction, out float depth))
                    continue;
                // The way out for the prop is down: the player has landed on it, which is their own doing.
                if (depth <= limit || direction.y < -0.5f) continue;
                SetPlayerCollision(prop, false);
                passingThroughPlayer.Add(prop);
            }
        }

        bool OverlapsPlayer(Prop prop)
        {
            CapsuleCollider capsule = game.Player.Collider;
            Transform capsuleTransform = capsule.transform;
            foreach (Collider own in prop.Colliders)
            {
                if (own == null || !own.enabled || own.isTrigger) continue;
                Transform ownTransform = own.transform;
                if (Physics.ComputePenetration(own, ownTransform.position, ownTransform.rotation,
                        capsule, capsuleTransform.position, capsuleTransform.rotation, out _, out float depth)
                    && depth > OverlapEpsilon)
                    return true;
            }
            return false;
        }

        void SetPlayerCollision(Prop prop, bool collide)
        {
            CapsuleCollider capsule = game.Player.Collider;
            foreach (Collider own in prop.Colliders)
                if (own != null) Physics.IgnoreCollision(own, capsule, !collide);
        }
    }
}
