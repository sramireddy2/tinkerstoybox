using Toybox.Platform;
using UnityEngine;

namespace Toybox.UI
{
    public enum TweenPhase
    {
        Hidden,
        /// <summary>Sticking on.</summary>
        Entering,
        Shown,
        /// <summary>Peeling off.</summary>
        Leaving,
    }

    /// <summary>
    /// The motion of one sticker (ART_BIBLE 10.4), on unscaled time:
    ///
    ///   enter ("stick")  scale 0.9 to 1 and rotate -3 degrees to 0, back-out easing (overshoot 1.56), 180 ms
    ///   exit ("peel")    scale y 1 to 0 about the top edge with alpha 1 to 0, ease-in, 180 ms
    ///   press            the body moves (2, -3) while the peel shadow shrinks to (2, -3), 90 ms
    ///   hover            the body lifts (-1, 2) and the shadow grows to (5, -8), 90 ms
    ///
    /// With "Reduce motion" stick and peel are 90 ms fades. Text never moves apart from its sticker: all of
    /// this is applied to the sticker's Motion and Body rects. It has no Update of its own: the
    /// <see cref="UiRoot"/> it belongs to advances it with the presenter's frame time, which is what makes
    /// it run the same in the game, in the screenshot tool and in tests.
    /// </summary>
    public sealed class UiTween : MonoBehaviour
    {
        Sticker sticker;
        float time, duration;
        bool fade;
        Vector2 liftFrom, liftTo, shadowFrom, shadowTo;
        float liftTime = UiTheme.Fast;

        public TweenPhase Phase { get; private set; } = TweenPhase.Shown;
        /// <summary>Shown or on its way in: what the code that owns the sticker means by "it is up".</summary>
        public bool IsShown => Phase == TweenPhase.Shown || Phase == TweenPhase.Entering;
        public bool IsMoving => Phase == TweenPhase.Entering || Phase == TweenPhase.Leaving || liftTime < UiTheme.Fast;
        /// <summary>Seconds into the current stick or peel.</summary>
        public float Elapsed => time;

        internal void Bind(Sticker owner)
        {
            sticker = owner;
            liftTo = Vector2.zero;
            shadowTo = UiTheme.ShadowOffset;
            UiRoot root = GetComponentInParent<UiRoot>(true);
            if (root != null) root.Register(this);
        }

        /// <summary>Sticks the sticker on (or puts it up at once). Does nothing if it is already up.</summary>
        public void Show(bool instant = false)
        {
            if (Phase == TweenPhase.Shown || (Phase == TweenPhase.Entering && !instant)) return;
            gameObject.SetActive(true);
            if (instant)
            {
                Phase = TweenPhase.Shown;
                Apply(1f, 1f, 0f, 1f);
                return;
            }
            fade = Settings.ReduceMotion;
            duration = fade ? UiTheme.Fast : UiTheme.Medium;
            time = 0f;
            Phase = TweenPhase.Entering;
            Pose();
        }

        /// <summary>Peels the sticker off (or takes it down at once); it is inactive afterwards.</summary>
        public void Hide(bool instant = false)
        {
            if (Phase == TweenPhase.Hidden || (Phase == TweenPhase.Leaving && !instant)) return;
            if (instant || !gameObject.activeInHierarchy)
            {
                Phase = TweenPhase.Hidden;
                Apply(1f, 1f, 0f, 1f);
                gameObject.SetActive(false);
                return;
            }
            fade = Settings.ReduceMotion;
            duration = fade ? UiTheme.Fast : UiTheme.Medium;
            time = 0f;
            Phase = TweenPhase.Leaving;
            Pose();
        }

        public void Set(bool shown, bool instant = false)
        {
            if (shown) Show(instant);
            else Hide(instant);
        }

        /// <summary>Where the body and the peel shadow should go (hover, press, rest); they get there in 90 ms.</summary>
        public void MoveTo(Vector2 lift, Vector2 shadow, bool instant = false)
        {
            if (liftTo == lift && shadowTo == shadow && !instant) return;
            liftFrom = sticker.Lift;
            shadowFrom = sticker.ShadowOffset;
            liftTo = lift;
            shadowTo = shadow;
            liftTime = instant ? UiTheme.Fast : 0f;
            if (instant)
            {
                sticker.Lift = lift;
                sticker.ShadowOffset = shadow;
            }
        }

        /// <summary>Advances the motion by real seconds.</summary>
        public void Advance(float dt)
        {
            if (sticker == null) return;
            if (liftTime < UiTheme.Fast)
            {
                liftTime = Mathf.Min(UiTheme.Fast, liftTime + dt);
                float k = UiTheme.EaseOut(liftTime / UiTheme.Fast);
                sticker.Lift = Vector2.LerpUnclamped(liftFrom, liftTo, k);
                sticker.ShadowOffset = Vector2.LerpUnclamped(shadowFrom, shadowTo, k);
            }

            if (Phase != TweenPhase.Entering && Phase != TweenPhase.Leaving) return;
            time += dt;
            if (time < duration)
            {
                Pose();
                return;
            }
            if (Phase == TweenPhase.Entering)
            {
                Phase = TweenPhase.Shown;
                Apply(1f, 1f, 0f, 1f);
            }
            else
            {
                Phase = TweenPhase.Hidden;
                Apply(1f, 1f, 0f, 1f);
                gameObject.SetActive(false);
            }
        }

        void Pose()
        {
            float t = duration > 0f ? Mathf.Clamp01(time / duration) : 1f;
            if (Phase == TweenPhase.Entering)
            {
                if (fade)
                {
                    Apply(1f, 1f, 0f, t);
                    return;
                }
                float k = UiTheme.BackOut(t);
                float scale = Mathf.LerpUnclamped(UiTheme.StickScale, 1f, k);
                Apply(scale, scale, Mathf.LerpUnclamped(UiTheme.StickAngle, 0f, k), 1f);
            }
            else
            {
                if (fade)
                {
                    Apply(1f, 1f, 0f, 1f - t);
                    return;
                }
                float k = UiTheme.EaseIn(t);
                Apply(1f, 1f - k, 0f, 1f - k);
            }
        }

        void Apply(float scaleX, float scaleY, float angle, float alpha)
        {
            RectTransform motion = sticker.Motion;
            motion.localScale = new Vector3(scaleX, scaleY, 1f);
            motion.localRotation = angle == 0f ? Quaternion.identity : Quaternion.Euler(0f, 0f, angle);
            sticker.Alpha = alpha;
        }
    }
}
