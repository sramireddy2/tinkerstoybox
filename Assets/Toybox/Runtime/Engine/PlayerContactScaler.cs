using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Toybox.Engine
{
    /// <summary>
    /// Adjusts the contacts PhysX generates for the player's capsule, in two ways.
    ///
    /// 1. It keeps the player from sinking through light props.
    ///
    /// An iterative solver cannot hold a heavy body up on a much lighter one: the player (mass 3) landing
    /// on a small crate (mass 0.1) goes straight through it. The mechanic makes such pairs routinely, since
    /// mass goes with scale cubed. So in contacts between the player and a prop, the prop is treated as if
    /// it weighed at least a fixed fraction of the player; only that one contact is affected, the prop's
    /// real mass still governs everything else it touches.
    ///
    /// 2. It drops "ghost" contacts at seams. While the capsule still stands on one collider, PhysX already
    /// generates a speculative contact against the edge of the next one (two flush floor boxes, a ramp
    /// box meeting a platform box). That contact's normal leans back against the walking direction, so
    /// the solver turns walking speed into a bump. Such a contact lies in the plane the player is walking
    /// on and has not been reached yet; a real obstacle (a lip, a step, a wall) sticks out of that plane.
    ///
    /// 3. It makes the foot of the capsule shove light props aside instead of climbing them. Against the
    /// rounded foot a small prop makes a contact that points up as well as back: it pins the prop to the
    /// floor and throws the walking player into the air. While the player stands on something else, such
    /// a contact is made horizontal: the prop is kicked along the floor and the player stays on it.
    ///
    /// PhysX calls the modification callback while the scene is being stepped, possibly from a worker
    /// thread, so it only reads plain data that was prepared on the main thread before the step.
    /// </summary>
    internal sealed class PlayerContactScaler : IDisposable
    {
        /// <summary>A prop counts as at least playerMass / MaxRatio in a contact with the player.</summary>
        public const float MaxRatio = 8f;

        readonly Dictionary<EntityId, float> propMasses = new Dictionary<EntityId, float>();
        readonly Action<PhysicsScene, NativeArray<ModifiableContactPair>> handler;
        PhysicsScene scene;
        EntityId playerBody;
        float playerMass;
        bool grounded;
        Vector3 groundPoint, groundNormal;
        float seamTolerance;
        bool standsOnBody;
        EntityId groundBody;
        float footCenterY;
        bool disposed;

        // cos of the angle by which a contact normal must differ from the ground normal to be a seam ghost.
        const float SameNormal = 0.9986f;

        public PlayerContactScaler(Player player)
        {
            player.Collider.hasModifiableContacts = true;
            handler = Modify;
            Physics.ContactModifyEvent += handler;
        }

        /// <summary>Main thread, right before the physics step: snapshot what the callback needs.</summary>
        public void Prepare(PhysicsScene physicsScene, Player player, IReadOnlyList<Prop> props)
        {
            scene = physicsScene;
            playerBody = player.Body.GetEntityId();
            playerMass = player.Body.mass;
            grounded = player.Grounded;
            groundPoint = player.GroundPoint;
            groundNormal = player.GroundNormal;
            seamTolerance = 0.005f * player.Scale;
            Rigidbody under = grounded && player.GroundCollider != null ? player.GroundCollider.attachedRigidbody : null;
            standsOnBody = under != null;
            groundBody = standsOnBody ? under.GetEntityId() : default;
            footCenterY = player.Position.y + player.Radius;
            propMasses.Clear();
            for (int i = 0; i < props.Count; i++)
            {
                Prop prop = props[i];
                if (prop.Removed || prop.Body.isKinematic) continue;
                propMasses[prop.BodyId] = prop.Body.mass;
            }
        }

        void Modify(PhysicsScene physicsScene, NativeArray<ModifiableContactPair> pairs)
        {
            if (physicsScene != scene) return;
            float floor = playerMass / MaxRatio;
            for (int i = 0; i < pairs.Length; i++)
            {
                ModifiableContactPair pair = pairs[i];
                bool playerFirst = pair.bodyEntityId == playerBody;
                if (!playerFirst && pair.otherBodyEntityId != playerBody) continue;
                if (grounded) DropSeamContacts(pair);
                EntityId other = playerFirst ? pair.otherBodyEntityId : pair.bodyEntityId;
                if (!propMasses.TryGetValue(other, out float mass) || mass >= floor) continue;
                if (grounded && !(standsOnBody && other == groundBody)) ShoveAside(pair);

                // Scaling the inverse mass down makes the prop heavier for this contact only.
                float scale = mass / floor;
                ModifiableMassProperties properties = pair.massProperties;
                if (playerFirst)
                {
                    properties.otherInverseMassScale = scale;
                    properties.otherInverseInertiaScale = scale;
                }
                else
                {
                    properties.inverseMassScale = scale;
                    properties.inverseInertiaScale = scale;
                }
                pair.massProperties = properties;
            }
        }

        void DropSeamContacts(ModifiableContactPair pair)
        {
            int count = pair.contactCount;
            for (int c = 0; c < count; c++)
            {
                float separation = pair.GetSeparation(c);
                if (separation <= 0f) continue;
                // Whichever body the normal points away from, a ghost's normal is not the ground's.
                if (Mathf.Abs(Vector3.Dot(pair.GetNormal(c), groundNormal)) >= SameNormal) continue;
                // The point is on one of the two surfaces, so it is within `separation` of the other one.
                float height = Vector3.Dot(pair.GetPoint(c) - groundPoint, groundNormal);
                if (height > seamTolerance + separation) continue;
                pair.IgnoreContact(c);
            }
        }

        void ShoveAside(ModifiableContactPair pair)
        {
            int count = pair.contactCount;
            for (int c = 0; c < count; c++)
            {
                if (pair.GetPoint(c).y >= footCenterY) continue;
                Vector3 normal = pair.GetNormal(c);
                if (Mathf.Abs(normal.y) < 0.05f) continue;
                var flat = new Vector3(normal.x, 0f, normal.z);
                float length = flat.magnitude;
                // Straight underneath without being what the player stands on: it has no say at all.
                if (length < 0.1f) pair.IgnoreContact(c);
                else pair.SetNormal(c, flat / length);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Physics.ContactModifyEvent -= handler;
        }
    }
}
