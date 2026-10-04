using System;
using System.Collections.Generic;
using UnityEngine;

namespace Toybox.Engine
{
    /// <summary>
    /// The authoring surface a level (and its gadgets) builds against. Everything added through it lives
    /// under <see cref="Root"/> and is torn down when the level is unloaded.
    /// </summary>
    public sealed class LevelContext
    {
        readonly List<Action<float>> updates = new List<Action<float>>();
        readonly List<Action> disposers = new List<Action>();
        readonly List<MessageEvent> said = new List<MessageEvent>();
        bool building;

        public Game Game { get; }
        public Rng Rng => Game.Rng;
        /// <summary>Parent of everything the level creates.</summary>
        public Transform Root { get; }
        /// <summary>Seconds of simulation since the level was loaded (Game.Time).</summary>
        public float Time => Game.Time;
        /// <summary>Ticks since the level was loaded (Game.LevelTicks).</summary>
        public int Ticks => Game.LevelTicks;
        /// <summary>The level that is being built.</summary>
        public LevelDefinition Level => Game.Level;
        /// <summary>The dip of the level's environment preset: the four tones its room surfaces are made of.</summary>
        public Toybox.Art.Dip Dip => Toybox.Art.Palette.DipOf(Game.Level != null ? Game.Level.Environment : null);
        /// <summary>The level's play plane (LevelDefinition.GroundY).</summary>
        public float GroundY => Game.Level != null ? Game.Level.GroundY : 0f;
        /// <summary>
        /// The level's subscriptions to game.Events. Everything level or gadget code subscribes while the
        /// level is built or during a tick is recorded here and unsubscribed when the level is unloaded.
        /// </summary>
        internal GameEvents.Scope Subscriptions { get; } = new GameEvents.Scope();

        public Vector3 SpawnPosition { get; private set; }
        public float SpawnYaw { get; private set; }
        public float SpawnPitch { get; private set; }

        internal LevelContext(Game game, Transform root)
        {
            Game = game;
            Root = root;
        }

        /// <summary>Adds immovable world geometry (colliders without a Rigidbody).</summary>
        public GameObject AddStatic(GameObject gameObject, Vector3 position, Quaternion rotation)
        {
            Adopt(gameObject, position, rotation);
            return gameObject;
        }

        public GameObject AddStatic(GameObject gameObject, Vector3 position) => AddStatic(gameObject, position, Quaternion.identity);

        /// <summary>Turns a toy (renderers + colliders authored at scale 1, no Rigidbody) into a physics prop.</summary>
        public Prop AddProp(GameObject toy, Vector3 position, Quaternion rotation, PropOptions options = null) =>
            Game.CreateProp(toy, Root, position, rotation, options);

        public Prop AddProp(GameObject toy, Vector3 position, PropOptions options = null) =>
            AddProp(toy, position, Quaternion.identity, options);

        /// <summary>Removes a prop from the level for good.</summary>
        public void RemoveProp(Prop prop) => Game.RemoveProp(prop);

        /// <summary>
        /// Adds geometry that level code moves: call MoveTo on the returned mover every tick (from OnUpdate).
        /// It pushes props and carries the player.
        /// </summary>
        public Mover AddKinematic(GameObject gameObject, Vector3 position, Quaternion rotation)
        {
            Adopt(gameObject, position, rotation);
            Rigidbody body = gameObject.GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            var mover = new Mover(body);
            Game.Register(mover);
            return mover;
        }

        public Mover AddKinematic(GameObject gameObject, Vector3 position) => AddKinematic(gameObject, position, Quaternion.identity);

        /// <summary>Adds a sensor volume centered on the given pose.</summary>
        public Trigger AddTrigger(string name, Volume volume, Vector3 position, Quaternion rotation)
        {
            var trigger = new Trigger(name, volume, Root, position, rotation);
            Game.Register(trigger);
            return trigger;
        }

        public Trigger AddTrigger(string name, Volume volume, Vector3 position) => AddTrigger(name, volume, position, Quaternion.identity);

        /// <summary>Removes a trigger for good. Whatever is inside leaves first (OnExit fires).</summary>
        public void RemoveTrigger(Trigger trigger) => Game.RemoveTrigger(trigger);

        /// <summary>Where the player starts (feet position) and where they look.</summary>
        public void SetSpawn(Vector3 position, float yawDeg, float pitchDeg = 0f)
        {
            SpawnPosition = position;
            SpawnYaw = yawDeg;
            SpawnPitch = pitchDeg;
        }

        /// <summary>
        /// A volume that, once the player touches it, becomes the place they respawn, facing the way they
        /// were looking. They respawn on the floor under the volume's center: the first static surface
        /// below the top of the volume, or its bottom center if there is none inside it. (So it does not
        /// matter whether the volume stands on the floor or is centered on it.)
        /// </summary>
        public Trigger AddCheckpoint(Volume volume, Vector3 position, string name = null)
        {
            Trigger trigger = AddTrigger(name ?? "Checkpoint", volume, position);
            trigger.SensesProps = false;
            trigger.OnEnter += e => Game.Player.SetCheckpoint(FloorUnder(volume, position), Game.Player.Yaw);
            return trigger;
        }

        /// <summary>A checkpoint with an explicit respawn point (feet) and facing.</summary>
        public Trigger AddCheckpoint(Volume volume, Vector3 position, Vector3 respawnPosition, float respawnYawDeg, string name = null)
        {
            Trigger trigger = AddTrigger(name ?? "Checkpoint", volume, position);
            trigger.SensesProps = false;
            trigger.OnEnter += e => Game.Player.SetCheckpoint(respawnPosition, respawnYawDeg);
            return trigger;
        }

        Vector3 FloorUnder(Volume volume, Vector3 position)
        {
            float half = volume.HalfHeight;
            Vector3 top = position + Vector3.up * half;
            if (Game.PhysicsScene.Raycast(top, Vector3.down, out RaycastHit hit, half * 2f + 0.01f, Layers.DefaultMask, QueryTriggerInteraction.Ignore))
                return hit.point;
            return position + Vector3.down * half;
        }

        /// <summary>
        /// Adds the goal: a box of the given size centered on position. The level completes when the player
        /// is inside while the exit is unlocked (exits start unlocked; `AddExit(...).Lock()` adds a locked one).
        /// </summary>
        public Exit AddExit(Vector3 position, Vector3 size, string name = null)
        {
            Trigger trigger = AddTrigger(name ?? "Exit", Volume.Box(size), position);
            trigger.SensesProps = false;
            var exit = new Exit(Game, trigger);
            Game.Register(exit);
            return exit;
        }

        /// <summary>Completes the level (once).</summary>
        public void Complete() => Game.CompleteLevel();

        /// <summary>Shows a line of text to the player. Lines said while the level is being built appear once it is loaded.</summary>
        public void Say(string text, float seconds = 4f)
        {
            var message = new MessageEvent { Text = text, Seconds = seconds };
            if (building) said.Add(message);
            else Game.Events.RaiseMessage(message);
        }

        /// <summary>Runs every tick before the physics step, with the fixed step length. This is where gadgets live.</summary>
        public void OnUpdate(Action<float> update)
        {
            if (update != null) updates.Add(update);
        }

        /// <summary>Runs when the level is unloaded, for cleanup the root's destruction does not cover.</summary>
        public void OnDispose(Action dispose)
        {
            if (dispose != null) disposers.Add(dispose);
        }

        void Adopt(GameObject gameObject, Vector3 position, Quaternion rotation)
        {
            Transform transform = gameObject.transform;
            transform.SetParent(Root, false);
            transform.SetPositionAndRotation(position, rotation);
        }

        internal void BeginBuild() => building = true;

        /// <summary>Called after LevelLoaded was raised, so listeners that reset on load still get the build's messages.</summary>
        internal void EndBuild()
        {
            building = false;
            foreach (MessageEvent message in said) Game.Events.RaiseMessage(message);
            said.Clear();
        }

        internal void RunUpdates(float dt)
        {
            // By index: an update may register further updates (a spawner creating a gadget).
            for (int i = 0; i < updates.Count; i++) updates[i](dt);
        }

        internal void Dispose()
        {
            updates.Clear();
            Subscriptions.Release();
            for (int i = disposers.Count - 1; i >= 0; i--)
            {
                try
                {
                    disposers[i]();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            disposers.Clear();
        }
    }
}
