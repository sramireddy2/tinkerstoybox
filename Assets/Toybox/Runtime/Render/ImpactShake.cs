using System.Collections.Generic;
using Toybox.Engine;
using Toybox.Platform;
using UnityEngine;

namespace Toybox.Render
{
    /// <summary>
    /// The camera shake of a first impact (ART_BIBLE 9.5): when a toy that was just let go comes down, the
    /// picture shakes by <c>clamp(log10(mass) x 0.05, 0, 0.25)</c> units for 180 ms - nothing below mass 1,
    /// so only what has grown heavy is felt. "Reduce motion" switches it off.
    ///
    /// The camera rig places the camera at the eye before the presenters run; this one moves it sideways
    /// and up by <see cref="Offset"/> afterwards, for the frame. Nothing of the simulation sees it.
    /// </summary>
    [Presenter(260)]
    public sealed class ImpactShake : IPresenter
    {
        public const float Seconds = 0.18f, PerDecade = 0.05f, MaxAmplitude = 0.25f, MinMass = 1f;

        readonly List<Prop> released = new List<Prop>();
        Game game;
        PresentationContext context;
        bool attached;
        float amplitude, age = float.MaxValue;

        /// <summary>How far the camera is from the eye this frame, in world space; zero while nothing shakes.</summary>
        public Vector3 Offset { get; private set; }
        /// <summary>The amplitude of the shake under way (0 when none is).</summary>
        public float Amplitude => age < Seconds ? amplitude : 0f;

        /// <summary>The amplitude an impact of this mass shakes with.</summary>
        public static float AmplitudeFor(float mass) => mass < MinMass ? 0f : Mathf.Clamp(Mathf.Log10(mass) * PerDecade, 0f, MaxAmplitude);

        public void Attach(Game game, PresentationContext context)
        {
            this.game = game;
            this.context = context;
            attached = true;
            game.Events.PropDropped += OnDropped;
            game.Events.PropGrabbed += OnGrabbed;
            game.Events.PropImpact += OnImpact;
            game.Events.LevelUnloading += OnLevelUnloading;
        }

        public void Frame(float dt, float alpha)
        {
            Offset = Vector3.zero;
            if (!attached || age >= Seconds) return;
            age += dt;
            Camera camera = context.Camera;
            if (age >= Seconds || camera == null || Settings.ReduceMotion) return;
            // Two sines that do not line up, dying away: a rattle, the same every time.
            float left = 1f - age / Seconds;
            float x = Mathf.Sin(age * 173f) * amplitude * left, y = Mathf.Cos(age * 131f + 1.3f) * amplitude * left;
            Transform view = camera.transform;
            Offset = view.right * x + view.up * y;
            view.position += Offset;
        }

        public void Dispose()
        {
            if (!attached) return;
            attached = false;
            game.Events.PropDropped -= OnDropped;
            game.Events.PropGrabbed -= OnGrabbed;
            game.Events.PropImpact -= OnImpact;
            game.Events.LevelUnloading -= OnLevelUnloading;
            released.Clear();
            Offset = Vector3.zero;
        }

        void OnDropped(PropHoldEvent e)
        {
            if (e.Prop == null || e.Prop.Removed || released.Contains(e.Prop)) return;
            released.Add(e.Prop);
        }

        void OnGrabbed(PropHoldEvent e) => released.Remove(e.Prop);

        void OnImpact(PropImpactEvent e)
        {
            // The first impact after a release only: a toy rolling about afterwards shakes nothing.
            if (!released.Remove(e.Prop)) return;
            float wanted = AmplitudeFor(e.Mass);
            if (wanted <= 0f || Settings.ReduceMotion) return;
            // A shake under way is replaced only by a stronger one.
            if (age < Seconds && wanted < amplitude * (1f - age / Seconds)) return;
            amplitude = wanted;
            age = 0f;
        }

        void OnLevelUnloading(LevelEvent e)
        {
            released.Clear();
            age = float.MaxValue;
        }
    }
}
