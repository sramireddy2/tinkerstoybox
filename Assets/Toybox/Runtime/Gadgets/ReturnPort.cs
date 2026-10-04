using System;
using System.Collections.Generic;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    public sealed class ReturnPortOptions
    {
        public string Name = "Return Port";
        /// <summary>Where an ejected prop's centre appears.</summary>
        public Vector3 Mouth;
        /// <summary>The velocity it leaves with.</summary>
        public Vector3 EjectVelocity;
        /// <summary>Where an ejected player is put (feet) and which way they face.</summary>
        public Vector3 PlayerPosition;
        public float PlayerYaw;
        /// <summary>How long to wait before trying again while something is in the mouth.</summary>
        public float RetrySeconds = 0.5f;
    }

    /// <summary>
    /// A hole in the wall that gives things back: a marble that fell through the wrong funnel rolls out of
    /// it at the size it has, and a player who dropped into a chamber steps out beside it.
    /// </summary>
    public sealed class ReturnPort : Gadget
    {
        struct Pending
        {
            public Prop Prop;
            public int Due;
        }

        static readonly Collider[] Hits = new Collider[32];

        readonly ReturnPortOptions options;
        readonly List<Pending> pending = new List<Pending>();
        readonly int retryTicks;

        public ReturnPort(LevelContext ctx, ReturnPortOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            retryTicks = Ticks(options.RetrySeconds, 1);
        }

        public Vector3 Mouth => options.Mouth;
        /// <summary>Props waiting to come out.</summary>
        public int Waiting => pending.Count;
        public int Ejections { get; private set; }

        /// <summary>Inside the tick, after the prop was put at the mouth.</summary>
        public event Action<Prop> Ejected;

        /// <summary>Queues a prop: it appears at the mouth after the delay, as soon as the mouth is free.</summary>
        public void Eject(Prop prop, float delaySeconds = 0f)
        {
            if (prop == null || prop.Removed) return;
            for (int i = 0; i < pending.Count; i++)
                if (pending[i].Prop == prop) return;
            pending.Add(new Pending { Prop = prop, Due = Age + Ticks(delaySeconds) });
        }

        /// <summary>Puts the player at the port's player pose, at once.</summary>
        public void Eject(Player player)
        {
            if (player == null) return;
            Vector3 from = player.Position;
            player.Teleport(options.PlayerPosition, options.PlayerYaw, 0f);
            Game.Events.RaisePlayerRespawned(new PlayerRespawnEvent { From = from, To = options.PlayerPosition });
        }

        /// <summary>Is the prop on its way out of this port?</summary>
        public bool IsPending(Prop prop)
        {
            for (int i = 0; i < pending.Count; i++)
                if (pending[i].Prop == prop) return true;
            return false;
        }

        protected override void Tick(float dt)
        {
            for (int i = 0; i < pending.Count; i++)
            {
                Pending entry = pending[i];
                Prop prop = entry.Prop;
                // In the player's hand again, or gone: nothing to give back.
                if (prop.Removed || prop.Held || !prop.GameObject.activeInHierarchy)
                {
                    pending.RemoveAt(i--);
                    continue;
                }
                if (Age < entry.Due) continue;

                Vector3 centre = MouthFor(prop);
                if (Blocked(prop, centre))
                {
                    entry.Due = Age + retryTicks;
                    pending[i] = entry;
                    continue;
                }

                if (prop.Driven) prop.EndDrive(Vector3.zero);
                if (prop.Frozen) prop.Unfreeze();
                Quaternion rotation = prop.Rotation;
                prop.SetPose(centre - rotation * (prop.LocalCenter * prop.Scale), rotation);
                if (!prop.Body.isKinematic) prop.Body.linearVelocity = options.EjectVelocity;
                pending.RemoveAt(i);
                Ejections++;
                Ejected?.Invoke(prop);
                Game.Events.RaisePortEjected(Event(centre, prop));
                // One a tick: the next one finds this one in the mouth and waits its turn.
                break;
            }
        }

        // The mouth, lifted so that the prop clears the floor under it by a tenth.
        Vector3 MouthFor(Prop prop)
        {
            Vector3 centre = options.Mouth;
            float radius = prop.Radius;
            if (Game.PhysicsScene.Raycast(centre, Vector3.down, out RaycastHit hit, radius + 50f, Layers.DefaultMask, QueryTriggerInteraction.Ignore))
                centre.y = Mathf.Max(centre.y, hit.point.y + radius + 0.1f);
            return centre;
        }

        bool Blocked(Prop prop, Vector3 centre)
        {
            int count = Game.PhysicsScene.OverlapSphere(centre, prop.Radius * 0.9f, Hits, Layers.PropMask | Layers.PlayerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (PropRef.Of(Hits[i]) != prop) return true;
            return false;
        }

        public override void Reset()
        {
            base.Reset();
            pending.Clear();
        }
    }
}
