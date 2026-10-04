using System;
using System.Collections.Generic;
using System.Reflection;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Render;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Platform
{
    /// <summary>
    /// One piece of presentation: something that shows or sounds the game without the simulation knowing
    /// (lighting, the room's visuals, post-processing, the HUD, audio, ...). Mark the class with
    /// <see cref="PresenterAttribute"/> and give it a parameterless constructor; it is then found by
    /// reflection and driven by GameRunner, by Shots and by PlayCheck alike - nobody registers anything.
    ///
    /// Lifecycle: <see cref="Attach"/> once, then <see cref="Frame"/> once per rendered frame, then Dispose.
    /// A presenter reacts to levels through <c>game.Events</c> (LevelLoaded, LevelUnloading, ...), which it
    /// subscribes to in Attach and leaves in Dispose. Attach may happen while a level is already loaded:
    /// treat that like a LevelLoaded. Objects it creates go under <see cref="PresentationContext.Root"/>
    /// and are destroyed in Dispose; whatever global state it changes (RenderSettings, shader globals,
    /// quality settings) it puts back.
    /// </summary>
    public interface IPresenter : IDisposable
    {
        void Attach(Game game, PresentationContext context);

        /// <summary>
        /// Once per rendered frame, after the simulation was stepped and the camera placed.
        /// <paramref name="dt"/> is the real time since the last frame in seconds (0 or more);
        /// <paramref name="alpha"/> how far the frame lies between the last two ticks (0..1).
        /// </summary>
        void Frame(float dt, float alpha);
    }

    /// <summary>
    /// Marks an <see cref="IPresenter"/> for discovery. Presenters are attached and run in ascending
    /// <see cref="Order"/> (and disposed in reverse); ties go by type name.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class PresenterAttribute : Attribute
    {
        public int Order { get; }
        /// <summary>
        /// This presenter is part of the game's look (lighting, room, post-processing, the sticker pass).
        /// It is left out when the plain look is asked for, and its presence retires the plain look.
        /// </summary>
        public bool ProvidesLook { get; set; }
        /// <summary>
        /// This presenter is the game's HUD and menus. It is left out when the plain look is asked for,
        /// and its presence retires the debug HUD.
        /// </summary>
        public bool ProvidesHud { get; set; }
        /// <summary>
        /// A stand-in (the plain look, the debug HUD): active only when plain is asked for or when no other
        /// presenter provides the same thing.
        /// </summary>
        public bool Fallback { get; set; }

        public PresenterAttribute(int order) => Order = order;
    }

    /// <summary>What presenters work with. One per <see cref="Presentation"/>.</summary>
    public sealed class PresentationContext
    {
        QualityTier quality;

        public Game Game { get; internal set; }
        /// <summary>The game's camera (the rig's).</summary>
        public Camera Camera { get; internal set; }
        public CameraRig Rig { get; internal set; }
        /// <summary>
        /// Parent for everything presenters create. It is a child of game.Root, so it is in the
        /// simulation's scene and follows it through level loads; outside Play Mode only objects in that
        /// scene are rendered by the camera.
        /// </summary>
        public Transform Root { get; internal set; }
        /// <summary>False in a headless run (-nographics): there is no device to make textures or render with.</summary>
        public bool HasGraphics { get; internal set; }
        /// <summary>The debug look was asked for (URL ?plain=1, -toyboxPlain): only fallback presenters provide look and HUD.</summary>
        public bool Plain { get; internal set; }
        /// <summary>
        /// The quality tier in force. The pipeline's presenter sets it (starting tier, governor, the
        /// settings menu); everybody else reads it and listens to <see cref="QualityChanged"/>.
        /// </summary>
        public QualityTier Quality
        {
            get => quality;
            set
            {
                if (quality == value) return;
                quality = value;
                Materials.Tier = value;
                Action<QualityTier> handlers = QualityChanged;
                if (handlers == null) return;
                foreach (Delegate handler in handlers.GetInvocationList())
                {
                    // A faulty listener must not fall back on whoever changed the tier, nor starve the others.
                    try
                    {
                        ((Action<QualityTier>)handler)(value);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
        }

        public event Action<QualityTier> QualityChanged;

        /// <summary>Real seconds of presentation since the Presentation was created: the sum of every frame's dt.</summary>
        public float UnscaledTime { get; internal set; }
        /// <summary>The dt of the frame being presented.</summary>
        public float DeltaTime { get; internal set; }
        /// <summary>Frames presented so far.</summary>
        public int FrameCount { get; internal set; }

        /// <summary>The game flow, or null where there is none (Shots, tools): then the state is Playing.</summary>
        public GameFlow Flow { get; internal set; }
        public FlowState State => Flow != null ? Flow.State : FlowState.Playing;

        /// <summary>Set by the driver every frame: a bot is playing the level's own solution.</summary>
        public bool Autoplay { get; set; }
        /// <summary>Why the bot gave up on the current level, or null.</summary>
        public string AutoplayError { get; set; }
        /// <summary>The mouse is captured and turns the view.</summary>
        public bool PointerLocked { get; set; }
        /// <summary>The right mouse button is held to look around without capture.</summary>
        public bool LookHeld { get; set; }
        /// <summary>
        /// Set by a menu while Esc is its own to handle - a card that lies on top of the pause card (the
        /// settings): Esc then closes that card, and the driver must not also take it as "resume".
        /// </summary>
        public bool EscapeClaimed { get; set; }

        /// <summary>The presentation this context belongs to (to find another presenter: <c>Presentation.Get</c>).</summary>
        public Presentation Presentation { get; internal set; }

        internal void SetInitialQuality(QualityTier tier)
        {
            quality = tier;
            Materials.Tier = tier;
        }
    }

    /// <summary>Finds the presenters and applies the rule that picks which of them are active.</summary>
    public static class PresenterRegistry
    {
        public sealed class Entry
        {
            public Type Type { get; }
            public int Order { get; }
            public bool ProvidesLook { get; }
            public bool ProvidesHud { get; }
            public bool Fallback { get; }

            public Entry(Type type, PresenterAttribute attribute)
            {
                Type = type;
                Order = attribute.Order;
                ProvidesLook = attribute.ProvidesLook;
                ProvidesHud = attribute.ProvidesHud;
                Fallback = attribute.Fallback;
            }

            public override string ToString() => Type.Name + " (" + Order + ")";
        }

        static List<Entry> all;

        /// <summary>Every presenter of the game assembly, in order. Found once.</summary>
        public static IReadOnlyList<Entry> All => all ??= Discover(typeof(PresenterRegistry).Assembly);

        /// <summary>
        /// The presenters of these assemblies, in order: every non-abstract class that implements
        /// <see cref="IPresenter"/>, carries a [Presenter] attribute and has a parameterless constructor.
        /// A marked class that cannot be used is reported, not silently skipped.
        /// </summary>
        public static List<Entry> Discover(params Assembly[] assemblies)
        {
            var found = new List<Entry>();
            foreach (Assembly assembly in assemblies)
            {
                foreach (Type type in assembly.GetTypes())
                {
                    PresenterAttribute attribute = type.GetCustomAttribute<PresenterAttribute>();
                    if (attribute == null) continue;
                    if (type.IsAbstract || !typeof(IPresenter).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null)
                    {
                        Debug.LogError("[Toybox] " + type.FullName + " is marked [Presenter] but is not a concrete IPresenter with a parameterless constructor.");
                        continue;
                    }
                    found.Add(new Entry(type, attribute));
                }
            }
            found.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Type.FullName, b.Type.FullName));
            return found;
        }

        /// <summary>
        /// Which presenters run. With <paramref name="plain"/>: everything that provides neither look nor
        /// HUD, plus the fallbacks. Without: everything that is not a fallback, plus the fallback look if
        /// nothing else provides the look and the fallback HUD if nothing else provides the HUD.
        /// </summary>
        public static List<Entry> Select(IReadOnlyList<Entry> entries, bool plain)
        {
            bool look = false, hud = false;
            foreach (Entry entry in entries)
            {
                if (entry.Fallback) continue;
                look |= entry.ProvidesLook;
                hud |= entry.ProvidesHud;
            }

            var selected = new List<Entry>();
            foreach (Entry entry in entries)
            {
                bool active;
                if (entry.Fallback) active = plain || (entry.ProvidesLook && !look) || (entry.ProvidesHud && !hud);
                else active = !plain || !(entry.ProvidesLook || entry.ProvidesHud);
                if (active) selected.Add(entry);
            }
            return selected;
        }
    }

    public sealed class PresentationOptions
    {
        /// <summary>The debug look: URP Lit materials, the plain lighting and the debug HUD.</summary>
        public bool Plain;
        /// <summary>The game flow, where there is one.</summary>
        public GameFlow Flow;
        /// <summary>The presenters to choose from; null for those of the game assembly.</summary>
        public IReadOnlyList<PresenterRegistry.Entry> Presenters;
        /// <summary>The tier to start on; null for the player's setting (Medium for Auto).</summary>
        public QualityTier? Quality;
    }

    /// <summary>
    /// Everything that shows a Game: the camera rig and the active presenters. GameRunner, Shots and
    /// PlayCheck all create one and call <see cref="Frame"/>; that is the whole seam.
    /// </summary>
    public sealed class Presentation : IDisposable
    {
        readonly List<IPresenter> presenters = new List<IPresenter>();
        readonly List<PresenterRegistry.Entry> entries = new List<PresenterRegistry.Entry>();
        readonly GameObject root;
        bool disposed;

        public PresentationContext Context { get; }
        public CameraRig Rig { get; }
        public Camera Camera => Rig.Camera;
        /// <summary>The active presenters, in order.</summary>
        public IReadOnlyList<IPresenter> Presenters => presenters;
        /// <summary>True if a presenter other than a fallback provides the HUD and menus (false with the plain look).</summary>
        public bool HudProvided { get; }
        /// <summary>True if a presenter other than a fallback provides the look (false with the plain look).</summary>
        public bool LookProvided { get; }

        Presentation(Game game, PresentationOptions options)
        {
            Rig = CameraRig.Create(game);
            root = new GameObject("Presentation") { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(game.Root.transform, false);

            Context = new PresentationContext
            {
                Game = game,
                Rig = Rig,
                Camera = Rig.Camera,
                Root = root.transform,
                HasGraphics = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
                Plain = options.Plain,
                Flow = options.Flow,
                Presentation = this,
            };
            Context.SetInitialQuality(options.Quality ?? Settings.ForcedTier ?? QualityTier.Medium);

            List<PresenterRegistry.Entry> selected = PresenterRegistry.Select(options.Presenters ?? PresenterRegistry.All, options.Plain);
            foreach (PresenterRegistry.Entry entry in selected)
            {
                if (entry.Fallback) continue;
                LookProvided |= entry.ProvidesLook;
                HudProvided |= entry.ProvidesHud;
            }

            foreach (PresenterRegistry.Entry entry in selected)
            {
                // A presenter that cannot start is reported and left out; the game goes on without it.
                IPresenter presenter = null;
                try
                {
                    presenter = (IPresenter)Activator.CreateInstance(entry.Type);
                    presenter.Attach(game, Context);
                    presenters.Add(presenter);
                    entries.Add(entry);
                }
                catch (Exception e)
                {
                    Debug.LogException(e is TargetInvocationException && e.InnerException != null ? e.InnerException : e);
                    try
                    {
                        presenter?.Dispose();
                    }
                    catch (Exception inner)
                    {
                        Debug.LogException(inner);
                    }
                }
            }
        }

        /// <summary>Creates the camera rig and attaches every active presenter to the game.</summary>
        public static Presentation Create(Game game, PresentationOptions options = null)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));
            return new Presentation(game, options ?? new PresentationOptions());
        }

        /// <summary>The active presenter of this type, or null.</summary>
        public T Get<T>() where T : class, IPresenter
        {
            for (int i = 0; i < presenters.Count; i++)
                if (presenters[i] is T match) return match;
            return null;
        }

        /// <summary>
        /// One rendered frame: places the camera between the last two ticks, then runs the presenters in
        /// order. A presenter that throws is reported once and retired.
        /// </summary>
        public void Frame(float dt, float alpha)
        {
            if (disposed || Context.Game.IsDisposed) return;
            if (!(dt > 0f)) dt = 0f;
            Context.DeltaTime = dt;
            Context.UnscaledTime += dt;
            Context.FrameCount++;
            Rig.Apply(alpha);
            for (int i = 0; i < presenters.Count; i++)
            {
                try
                {
                    presenters[i].Frame(dt, alpha);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    IPresenter broken = presenters[i];
                    presenters.RemoveAt(i);
                    entries.RemoveAt(i);
                    i--;
                    try
                    {
                        broken.Dispose();
                    }
                    catch (Exception inner)
                    {
                        Debug.LogException(inner);
                    }
                }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int i = presenters.Count - 1; i >= 0; i--)
            {
                try
                {
                    presenters[i].Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            presenters.Clear();
            entries.Clear();
            // Game.Dispose destroys game.Root and these with it; then there is nothing left to do here.
            if (root != null) Sim.Destroy(root);
            Rig.Dispose();
        }
    }
}
