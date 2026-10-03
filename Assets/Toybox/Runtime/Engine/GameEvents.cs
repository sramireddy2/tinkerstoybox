using System;
using System.Collections.Generic;
using UnityEngine;

namespace Toybox.Engine
{
    public struct LevelEvent
    {
        public LevelDefinition Level;
        public int Id;
        /// <summary>Seconds and ticks since the level was loaded.</summary>
        public float Time;
        public int Ticks;
    }

    /// <summary>
    /// Payload of PropGrabbed, PropHeld and PropDropped. For PropGrabbed the "new" values equal the "old"
    /// ones; for PropHeld they describe the current placement; for PropDropped the final one.
    /// </summary>
    public struct PropHoldEvent
    {
        public Prop Prop;
        public float OldScale, NewScale;
        /// <summary>Distance from the eye to the prop's center when it was grabbed / now.</summary>
        public float GrabDistance, DropDistance;
    }

    public struct PropEvent
    {
        public Prop Prop;
    }

    public struct PlayerJumpEvent
    {
        public Vector3 Position;
    }

    public struct PlayerLandEvent
    {
        public Vector3 Position;
        /// <summary>Speed along the ground normal, relative to the ground, just before touching down.</summary>
        public float ImpactSpeed;
        public Collider Ground;
        /// <summary>The prop that was landed on, or null for world geometry.</summary>
        public Prop Prop;
    }

    public struct PlayerRespawnEvent
    {
        public Vector3 From, To;
    }

    public struct TriggerEvent
    {
        public Trigger Trigger;
        public bool IsPlayer;
        /// <summary>The prop that entered or left; null when it was the player.</summary>
        public Prop Prop;
    }

    public struct MessageEvent
    {
        public string Text;
        public float Seconds;
    }

    public struct ExitEvent
    {
        public Exit Exit;
        /// <summary>The exit's state after the change.</summary>
        public bool Locked;
    }

    /// <summary>
    /// The simulation's only outlet toward presentation. Events raised during a tick are queued and
    /// delivered in order at the end of that tick, so a subscriber can never disturb a half-finished step;
    /// outside a tick they are delivered immediately. Partial so gadgets can add their own channels.
    ///
    /// Subscriptions made by level and gadget code - that is, while a level is being built or during a
    /// tick - are recorded with the level and removed when it is unloaded (see <see cref="Scope"/>).
    /// Presentation code subscribes outside ticks and keeps its subscriptions for as long as it likes.
    /// </summary>
    public sealed partial class GameEvents
    {
        public event Action<LevelEvent> LevelLoaded { add => Ch(ref levelLoaded).Add(value); remove => Ch(ref levelLoaded).Remove(value); }
        public event Action<LevelEvent> LevelCompleted { add => Ch(ref levelCompleted).Add(value); remove => Ch(ref levelCompleted).Remove(value); }
        public event Action<LevelEvent> LevelRestarted { add => Ch(ref levelRestarted).Add(value); remove => Ch(ref levelRestarted).Remove(value); }
        public event Action<PropHoldEvent> PropGrabbed { add => Ch(ref propGrabbed).Add(value); remove => Ch(ref propGrabbed).Remove(value); }
        public event Action<PropHoldEvent> PropHeld { add => Ch(ref propHeld).Add(value); remove => Ch(ref propHeld).Remove(value); }
        public event Action<PropHoldEvent> PropDropped { add => Ch(ref propDropped).Add(value); remove => Ch(ref propDropped).Remove(value); }
        public event Action<PropEvent> PropRespawned { add => Ch(ref propRespawned).Add(value); remove => Ch(ref propRespawned).Remove(value); }
        public event Action<PlayerJumpEvent> PlayerJumped { add => Ch(ref playerJumped).Add(value); remove => Ch(ref playerJumped).Remove(value); }
        public event Action<PlayerLandEvent> PlayerLanded { add => Ch(ref playerLanded).Add(value); remove => Ch(ref playerLanded).Remove(value); }
        public event Action<PlayerRespawnEvent> PlayerRespawned { add => Ch(ref playerRespawned).Add(value); remove => Ch(ref playerRespawned).Remove(value); }
        public event Action<TriggerEvent> TriggerEntered { add => Ch(ref triggerEntered).Add(value); remove => Ch(ref triggerEntered).Remove(value); }
        public event Action<TriggerEvent> TriggerExited { add => Ch(ref triggerExited).Add(value); remove => Ch(ref triggerExited).Remove(value); }
        public event Action<MessageEvent> Message { add => Ch(ref message).Add(value); remove => Ch(ref message).Remove(value); }
        public event Action<ExitEvent> ExitLockChanged { add => Ch(ref exitLockChanged).Add(value); remove => Ch(ref exitLockChanged).Remove(value); }

        public void RaiseLevelLoaded(LevelEvent e) => Ch(ref levelLoaded).Raise(e);
        public void RaiseLevelCompleted(LevelEvent e) => Ch(ref levelCompleted).Raise(e);
        public void RaiseLevelRestarted(LevelEvent e) => Ch(ref levelRestarted).Raise(e);
        public void RaisePropGrabbed(PropHoldEvent e) => Ch(ref propGrabbed).Raise(e);
        public void RaisePropHeld(PropHoldEvent e) => Ch(ref propHeld).Raise(e);
        public void RaisePropDropped(PropHoldEvent e) => Ch(ref propDropped).Raise(e);
        public void RaisePropRespawned(PropEvent e) => Ch(ref propRespawned).Raise(e);
        public void RaisePlayerJumped(PlayerJumpEvent e) => Ch(ref playerJumped).Raise(e);
        public void RaisePlayerLanded(PlayerLandEvent e) => Ch(ref playerLanded).Raise(e);
        public void RaisePlayerRespawned(PlayerRespawnEvent e) => Ch(ref playerRespawned).Raise(e);
        public void RaiseTriggerEntered(TriggerEvent e) => Ch(ref triggerEntered).Raise(e);
        public void RaiseTriggerExited(TriggerEvent e) => Ch(ref triggerExited).Raise(e);
        public void RaiseMessage(MessageEvent e) => Ch(ref message).Raise(e);
        public void RaiseExitLockChanged(ExitEvent e) => Ch(ref exitLockChanged).Raise(e);

        Channel<LevelEvent> levelLoaded, levelCompleted, levelRestarted;
        Channel<PropHoldEvent> propGrabbed, propHeld, propDropped;
        Channel<PropEvent> propRespawned;
        Channel<PlayerJumpEvent> playerJumped;
        Channel<PlayerLandEvent> playerLanded;
        Channel<PlayerRespawnEvent> playerRespawned;
        Channel<TriggerEvent> triggerEntered, triggerExited;
        Channel<MessageEvent> message;
        Channel<ExitEvent> exitLockChanged;

        readonly List<IChannel> pending = new List<IChannel>();
        int deferDepth;
        bool flushing;
        Scope recording;

        /// <summary>
        /// A set of subscriptions that end together. While a scope is being recorded (<see cref="Record"/>),
        /// every handler added to any event of this GameEvents is noted in it; <see cref="Release"/> removes
        /// them all. The Game records a scope per level around Build and around every tick.
        /// </summary>
        public sealed class Scope
        {
            struct Entry
            {
                public IChannel Channel;
                public Delegate Handler;
            }

            readonly List<Entry> entries = new List<Entry>();

            public int Count => entries.Count;

            internal void Add(IChannel channel, Delegate handler) => entries.Add(new Entry { Channel = channel, Handler = handler });

            /// <summary>Unsubscribes everything recorded so far.</summary>
            public void Release()
            {
                for (int i = entries.Count - 1; i >= 0; i--) entries[i].Channel.Remove(entries[i].Handler);
                entries.Clear();
            }
        }

        /// <summary>
        /// Makes <paramref name="scope"/> the one that new subscriptions are recorded in (null: none) and
        /// returns the one that was being recorded before, to be passed back when done.
        /// </summary>
        public Scope Record(Scope scope)
        {
            Scope previous = recording;
            recording = scope;
            return previous;
        }

        /// <summary>
        /// Channels are created on first use, so another part of this partial class can add an event with
        /// just a field, an event accessor and a Raise method.
        /// </summary>
        Channel<T> Ch<T>(ref Channel<T> channel) => channel ??= new Channel<T>(this);

        /// <summary>Starts queueing. Calls nest; delivery resumes when the outermost EndDefer runs.</summary>
        public void BeginDefer() => deferDepth++;

        /// <summary>Ends one BeginDefer and, at the outermost level, delivers everything queued, in order.</summary>
        public void EndDefer()
        {
            if (deferDepth > 0) deferDepth--;
            if (deferDepth == 0) Flush();
        }

        void Flush()
        {
            if (flushing) return;
            flushing = true;
            try
            {
                // Handlers may raise further events; those are appended and delivered in the same pass.
                for (int i = 0; i < pending.Count; i++) pending[i].DeliverNext();
            }
            finally
            {
                pending.Clear();
                flushing = false;
            }
        }

        internal interface IChannel
        {
            void DeliverNext();
            void Remove(Delegate handler);
        }

        /// <summary>One event type: its subscribers and its share of the queue.</summary>
        sealed class Channel<T> : IChannel
        {
            static readonly Action<T>[] None = new Action<T>[0];

            readonly GameEvents owner;
            readonly Queue<T> queue = new Queue<T>();
            // Copy-on-write, so delivery can iterate without allocating while handlers subscribe or leave.
            Action<T>[] handlers = None;

            public Channel(GameEvents owner) => this.owner = owner;

            public void Add(Action<T> handler)
            {
                if (handler == null) return;
                var next = new Action<T>[handlers.Length + 1];
                Array.Copy(handlers, next, handlers.Length);
                next[handlers.Length] = handler;
                handlers = next;
                owner.recording?.Add(this, handler);
            }

            void IChannel.Remove(Delegate handler) => Remove(handler as Action<T>);

            public void Remove(Action<T> handler)
            {
                if (handler == null) return;
                int index = Array.LastIndexOf(handlers, handler);
                if (index < 0) return;
                var next = new Action<T>[handlers.Length - 1];
                Array.Copy(handlers, next, index);
                Array.Copy(handlers, index + 1, next, index, next.Length - index);
                handlers = next;
            }

            public void Raise(T payload)
            {
                queue.Enqueue(payload);
                owner.pending.Add(this);
                if (owner.deferDepth == 0) owner.Flush();
            }

            void IChannel.DeliverNext()
            {
                T payload = queue.Dequeue();
                Action<T>[] current = handlers;
                for (int i = 0; i < current.Length; i++)
                {
                    // A faulty listener must neither stop the simulation nor starve the other listeners.
                    try
                    {
                        current[i](payload);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
        }
    }
}
