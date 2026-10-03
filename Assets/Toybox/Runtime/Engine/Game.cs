using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Toybox.Engine
{
    public sealed class GameOptions
    {
        /// <summary>Who steers the player. Can be replaced later through Game.Input.</summary>
        public IInputSource Input;
        /// <summary>Base seed; every level load reseeds Game.Rng from it and the level id.</summary>
        public int Seed = 1;
        /// <summary>Most ticks one Step call may run to catch up after a slow frame.</summary>
        public int MaxCatchUpTicks = 4;
        /// <summary>
        /// True (the default): the simulation gets a scene and a physics scene of its own, renewed on every
        /// level load, which is what makes it replay identically. False puts everything into the open
        /// scene and the default physics scene instead - only for tools that need exactly that, since the
        /// result then depends on what was simulated before.
        /// </summary>
        public bool Isolated = true;
    }

    /// <summary>
    /// The simulation. It owns the physics world while it exists: Create takes over the global physics
    /// settings and Dispose puts every one of them back. Nothing here depends on the MonoBehaviour
    /// lifecycle - whoever owns the Game calls Tick (tests, the bot runner) or Step (the real game loop).
    ///
    /// Everything it creates lives under <see cref="Root"/> in <see cref="Scene"/>, and is simulated and
    /// queried through <see cref="PhysicsScene"/> - not through the static Physics.Raycast and friends,
    /// which only see the default physics scene.
    /// </summary>
    public sealed class Game : IDisposable
    {
        public const float Gravity = 22f;

        // Stacks of toys whose masses differ by orders of magnitude need far more than PhysX's default 6 / 1
        // (StackingTests fails at 16 and passes from 24). Every body gets the same count, because one body
        // with a higher count raises it for others as well - measured: the player's count alone decided
        // whether a stack of props it was not touching held. Going from 16 to 32 costs about a tenth more.
        const int SolverIterations = 32;
        // Fewer than 4 leaves more of the depenetration speed in the bodies (things pop after landing).
        const int SolverVelocityIterations = 4;
        // How fast the solver may push overlapping bodies apart. That speed stays in the bodies afterwards,
        // so anything higher makes things pop up after a hard landing (measured: 6 gives a hop of 2 u/s).
        const float MaxDepenetrationVelocity = 2f;

        /// <summary>The one live Game, or null.</summary>
        public static Game Current { get; private set; }

        readonly PhysicsSettings previousSettings;
        readonly List<Prop> props = new List<Prop>();
        readonly List<Trigger> triggers = new List<Trigger>();
        readonly List<Exit> exits = new List<Exit>();
        readonly List<Mover> movers = new List<Mover>();
        readonly int seed;
        readonly int maxCatchUpTicks;
        readonly bool isolated;
        readonly PlayerContactScaler contactScaler;

        SimScene world;

        GameObject levelRoot;
        int nextPropId;
        float accumulator;
        bool ticking;
        bool propsRemoved;
        bool triggersRemoved;
        bool disposed;
        LevelDefinition pendingLevel;

        /// <summary>Parent of everything in the simulation. It is a scene root; do not reparent it.</summary>
        public GameObject Root { get; }
        /// <summary>The scene the simulation's objects are in. A fresh one is made for every level load.</summary>
        public Scene Scene => world.Scene;
        /// <summary>The physics world of the simulation. All queries (raycasts, overlaps) must go through it.</summary>
        public PhysicsScene PhysicsScene => world.Physics;
        /// <summary>Root of the loaded level's objects, or null.</summary>
        public Transform LevelRoot => levelRoot != null ? levelRoot.transform : null;
        public Player Player { get; }
        public PerspectiveGrabber Grabber { get; }
        public GameEvents Events { get; } = new GameEvents();
        public Rng Rng { get; }
        public IInputSource Input { get; set; }
        /// <summary>The frame the last tick ran with.</summary>
        public InputFrame LastInput { get; private set; }

        public IReadOnlyList<Prop> Props => props;
        public IReadOnlyList<Trigger> Triggers => triggers;
        public IReadOnlyList<Exit> Exits => exits;
        public IReadOnlyList<Mover> Movers => movers;

        public LevelDefinition Level { get; private set; }
        public LevelContext Context { get; private set; }
        public bool LevelCompleted { get; private set; }
        public float KillY => Level != null ? Level.KillY : -30f;

        /// <summary>Ticks since the Game was created.</summary>
        public int TickCount { get; private set; }
        /// <summary>Ticks since the current level was loaded.</summary>
        public int LevelTicks { get; private set; }
        /// <summary>Seconds of simulation since the current level was loaded.</summary>
        public float Time => LevelTicks * Sim.Dt;
        /// <summary>How far the last Step was between two ticks (0..1), for render interpolation.</summary>
        public float Alpha { get; private set; }
        public bool IsDisposed => disposed;

        Game(GameOptions options)
        {
            previousSettings = PhysicsSettings.Capture();
            Physics.simulationMode = SimulationMode.Script;
            Physics.gravity = new Vector3(0f, -Gravity, 0f);
            Physics.defaultSolverIterations = SolverIterations;
            Physics.defaultSolverVelocityIterations = SolverVelocityIterations;
            Physics.defaultMaxDepenetrationVelocity = MaxDepenetrationVelocity;
            Layers.Configure();

            seed = options.Seed;
            maxCatchUpTicks = Mathf.Max(1, options.MaxCatchUpTicks);
            isolated = options.Isolated;
            Input = options.Input;
            Rng = new Rng(seed);

            try
            {
                world = SimScene.Create(isolated);
                Root = new GameObject("Toybox Game") { hideFlags = HideFlags.DontSave };
                world.Adopt(Root);
                Player = new Player(this, Root.transform);
                Grabber = new PerspectiveGrabber(this);
                contactScaler = new PlayerContactScaler(Player);
                Player.ResetState();
            }
            catch
            {
                contactScaler?.Dispose();
                if (Root != null) Sim.Destroy(Root);
                world?.Close();
                previousSettings.Restore();
                throw;
            }
        }

        /// <summary>Creates the Game and takes over global physics. Throws if another Game is alive.</summary>
        public static Game Create(GameOptions options = null)
        {
            if (Current != null)
                throw new InvalidOperationException("Only one Game may exist at a time. Dispose the current one first.");
            var game = new Game(options ?? new GameOptions());
            Current = game;
            return game;
        }

        public void LoadLevel(int id) => LoadLevel(LevelRegistry.Get(id));

        /// <summary>
        /// Tears the current level down completely and builds this one. Called from inside a tick (a level
        /// loading its successor) the switch happens when that tick ends.
        /// </summary>
        public void LoadLevel(LevelDefinition level)
        {
            ThrowIfDisposed();
            if (level == null) throw new ArgumentNullException(nameof(level));
            if (ticking)
            {
                pendingLevel = level;
                return;
            }

            Events.BeginDefer();
            try
            {
                UnloadLevel();
                RenewScene();
                Level = level;
                LevelCompleted = false;
                LevelTicks = 0;
                Rng.Reseed(unchecked(seed * 486187739 + level.Id));
                nextPropId = 0;

                levelRoot = new GameObject("Level " + level.Slug) { hideFlags = HideFlags.DontSave };
                levelRoot.transform.SetParent(Root.transform, false);
                Context = new LevelContext(this, levelRoot.transform);
                Player.ResetState();

                Context.BeginBuild();
                // What the level subscribes to belongs to the level and ends with it.
                GameEvents.Scope outer = Events.Record(Context.Subscriptions);
                try
                {
                    level.Build(Context);
                }
                finally
                {
                    Events.Record(outer);
                }

                Physics.SyncTransforms();
                Player.Teleport(Context.SpawnPosition, Context.SpawnYaw, Context.SpawnPitch);
                Player.SetCheckpoint(Context.SpawnPosition, Context.SpawnYaw, Context.SpawnPitch);
                Events.RaiseLevelLoaded(LevelEventNow());
                Context.EndBuild();
            }
            catch
            {
                // A level whose Build throws must not stay behind half-built.
                UnloadLevel();
                throw;
            }
            finally
            {
                Events.EndDefer();
            }
        }

        /// <summary>Rebuilds the current level from scratch (same LevelDefinition instance, Build runs again).</summary>
        public void RestartLevel()
        {
            ThrowIfDisposed();
            if (Level == null) return;
            Events.RaiseLevelRestarted(LevelEventNow());
            LoadLevel(Level);
        }

        /// <summary>Marks the level as solved. LevelCompleted is raised exactly once per load.</summary>
        public void CompleteLevel()
        {
            if (LevelCompleted || Level == null) return;
            LevelCompleted = true;
            Events.RaiseLevelCompleted(LevelEventNow());
        }

        /// <summary>
        /// Advances the simulation by exactly one fixed step:
        /// input, player, grab / drop, level and gadgets, physics, ground probe, held-prop placement,
        /// triggers, exits, kill plane, events.
        /// </summary>
        public void Tick()
        {
            ThrowIfDisposed();
            if (ticking) throw new InvalidOperationException("Game.Tick was called from inside a tick.");

            // The input source may be a script that reads the world: show it the simulation's own state.
            Grabber.BeginTick();
            InputFrame input = Input != null ? Input.Sample() : default;
            LastInput = input;
            if (input.RestartPressed) RestartLevel();

            Events.BeginDefer();
            // Whatever subscribes to an event during the tick is level or gadget code.
            GameEvents.Scope outer = Events.Record(Context?.Subscriptions);
            ticking = true;
            try
            {
                Player.Tick(input);
                Grabber.Tick(input);

                for (int i = 0; i < movers.Count; i++) movers[i].BeginTick();
                Context?.RunUpdates(Sim.Dt);

                contactScaler.Prepare(world.Physics, Player, props);
                world.Physics.Simulate(Sim.Dt);

                Player.PostPhysics();
                Grabber.PostPhysics();
                for (int i = 0; i < props.Count; i++)
                    if (!props[i].Removed) props[i].RefreshCollisionMode();
                for (int i = 0; i < triggers.Count; i++)
                {
                    triggers[i].Evaluate(this);
                    if (triggers[i].Removed) triggersRemoved = true;
                }
                for (int i = 0; i < exits.Count; i++) exits[i].Evaluate(this);
                ApplyKillPlane();

                TickCount++;
                LevelTicks++;
            }
            finally
            {
                ticking = false;
                Events.Record(outer);
                try
                {
                    if (propsRemoved)
                    {
                        props.RemoveAll(p => p.Removed);
                        propsRemoved = false;
                    }
                    if (triggersRemoved)
                    {
                        triggers.RemoveAll(t => t.Removed);
                        triggersRemoved = false;
                    }
                }
                finally
                {
                    // The tick's events go out before a pending level switch destroys what they refer to.
                    Events.EndDefer();
                }
                if (pendingLevel != null && !disposed)
                {
                    LevelDefinition next = pendingLevel;
                    pendingLevel = null;
                    LoadLevel(next);
                }
            }
        }

        /// <summary>
        /// Feeds real elapsed time into the simulation: runs as many ticks as have become due (at most
        /// MaxCatchUpTicks; a longer backlog is dropped) and returns the interpolation alpha, the fraction
        /// of a step that real time is ahead of the last tick.
        /// </summary>
        public float Step(float realDeltaTime)
        {
            ThrowIfDisposed();
            if (realDeltaTime > 0f) accumulator += realDeltaTime;
            int ran = 0;
            while (accumulator >= Sim.Dt && ran < maxCatchUpTicks)
            {
                accumulator -= Sim.Dt;
                ran++;
                Tick();
                // A listener may have disposed the Game when the tick's events went out.
                if (disposed) return 0f;
            }
            if (accumulator >= Sim.Dt) accumulator %= Sim.Dt;
            Alpha = Mathf.Clamp01(accumulator / Sim.Dt);
            return Alpha;
        }

        /// <summary>Wakes every dynamic body. Used when support may have vanished without a contact change.</summary>
        public void WakeAll()
        {
            for (int i = 0; i < props.Count; i++)
            {
                Prop prop = props[i];
                if (!prop.Removed && !prop.Body.isKinematic) prop.Body.WakeUp();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try
            {
                UnloadLevel();
                Player.Destroy();
                Sim.Destroy(Root);
                world.Close();
            }
            finally
            {
                contactScaler.Dispose();
                previousSettings.Restore();
                if (Current == this) Current = null;
            }
        }

        internal Prop CreateProp(GameObject toy, Transform parent, Vector3 position, Quaternion rotation, PropOptions options)
        {
            var prop = new Prop(this, nextPropId++, toy, parent, position, rotation, options);
            props.Add(prop);
            if (prop.Mover != null) movers.Add(prop.Mover);
            return prop;
        }

        internal void RemoveProp(Prop prop)
        {
            if (prop == null || prop.Removed) return;
            Grabber.Forget(prop);
            if (prop.Mover != null) movers.Remove(prop.Mover);
            prop.Destroy();
            // It leaves every trigger now, so that no level code is handed a destroyed prop.
            for (int i = 0; i < triggers.Count; i++) triggers[i].Forget(this, prop);
            // The prop list may be in the middle of being iterated; compact it when the tick is over.
            if (ticking) propsRemoved = true;
            else props.Remove(prop);
        }

        internal void RemoveTrigger(Trigger trigger)
        {
            if (trigger == null || trigger.Removed) return;
            trigger.Remove(this);
            if (ticking) triggersRemoved = true;
            else triggers.Remove(trigger);
        }

        internal void Register(Trigger trigger) => triggers.Add(trigger);
        internal void Register(Exit exit) => exits.Add(exit);
        internal void Register(Mover mover) => movers.Add(mover);

        void UnloadLevel()
        {
            Grabber.Reset();
            LevelContext context = Context;
            Context = null;
            context?.Dispose();

            foreach (Prop prop in props) prop.Destroy();
            props.Clear();
            propsRemoved = false;
            triggersRemoved = false;
            triggers.Clear();
            exits.Clear();
            movers.Clear();

            if (levelRoot != null) Sim.Destroy(levelRoot);
            levelRoot = null;
            Level = null;
            LevelCompleted = false;
        }

        // A used PhysX scene does not replay like a new one, so each level starts in a new one. The player
        // is the first body to enter it, then the level's objects in build order - the same every time.
        void RenewScene()
        {
            if (!isolated) return;
            SimScene previous = world;
            world = SimScene.Create(true);
            world.Adopt(Root);
            previous.Close();
        }

        void ApplyKillPlane()
        {
            float killY = KillY;
            if (Player.Position.y < killY) Player.Respawn();
            for (int i = 0; i < props.Count; i++)
            {
                Prop prop = props[i];
                if (prop.Removed || prop.Held || prop.BodyKind != PropBody.Dynamic) continue;
                if (prop.Center.y < killY) prop.Respawn();
            }
        }

        LevelEvent LevelEventNow() => new LevelEvent { Level = Level, Id = Level != null ? Level.Id : -1, Time = Time, Ticks = LevelTicks };

        void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(Game));
        }

        /// <summary>The global physics state a Game changes, captured so Dispose can put it back.</summary>
        readonly struct PhysicsSettings
        {
            readonly SimulationMode simulationMode;
            readonly Vector3 gravity;
            readonly int solverIterations, solverVelocityIterations;
            readonly float maxDepenetrationVelocity;
            readonly bool[] ignoredLayerPairs;

            PhysicsSettings(bool[] ignoredLayerPairs)
            {
                simulationMode = Physics.simulationMode;
                gravity = Physics.gravity;
                solverIterations = Physics.defaultSolverIterations;
                solverVelocityIterations = Physics.defaultSolverVelocityIterations;
                maxDepenetrationVelocity = Physics.defaultMaxDepenetrationVelocity;
                this.ignoredLayerPairs = ignoredLayerPairs;
            }

            public static PhysicsSettings Capture()
            {
                var ignored = new bool[32 * 32];
                for (int a = 0; a < 32; a++)
                    for (int b = a; b < 32; b++)
                        ignored[a * 32 + b] = Physics.GetIgnoreLayerCollision(a, b);
                return new PhysicsSettings(ignored);
            }

            public void Restore()
            {
                Physics.simulationMode = simulationMode;
                Physics.gravity = gravity;
                Physics.defaultSolverIterations = solverIterations;
                Physics.defaultSolverVelocityIterations = solverVelocityIterations;
                Physics.defaultMaxDepenetrationVelocity = maxDepenetrationVelocity;
                for (int a = 0; a < 32; a++)
                    for (int b = a; b < 32; b++)
                        Physics.IgnoreLayerCollision(a, b, ignoredLayerPairs[a * 32 + b]);
            }
        }
    }
}
