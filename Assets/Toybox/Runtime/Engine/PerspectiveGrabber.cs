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
        /// <summary>Ticks a GrabPose takes to stand a tumbled prop up: 0.15 s.</summary>
        public const int PoseTicks = 9;

        readonly Game game;
        readonly Collider[] candidates = new Collider[256];
        readonly List<Prop> passingThroughPlayer = new List<Prop>();

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
        /// A prop much heavier than the player that comes down on them (let go overhead, it grows and falls)
        /// squeezes the capsule against the floor; the solver then squirts the player out sideways at many
        /// times their running speed. Nothing can crush the player in this game, so such a prop passes
        /// through them instead, exactly like one that is released around them, until they have separated.
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
