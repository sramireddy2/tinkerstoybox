using TMPro;
using Toybox.Engine;
using Toybox.Platform;
using UnityEngine;
using UnityEngine.UI;

namespace Toybox.UI
{
    /// <summary>
    /// The game's HUD (ART_BIBLE 10.5): one overlay canvas without a raycaster, holding the reticle, the
    /// scale readout (9.6), the hint toasts, the level card and the held-control pills. It is there while
    /// the flow is Playing and hidden behind every menu. Everything it shows comes from game events and
    /// from the grabber's focus candidate; it changes nothing in the game.
    /// </summary>
    [Presenter(510, ProvidesHud = true)]
    public sealed class HudPresenter : IPresenter
    {
        public const float LookPromptDelay = 0.6f;

        Game game;
        PresentationContext context;
        string capture;
        bool pendingCard;
        float pointerFreeFor;

        public UiRoot Root { get; private set; }
        public HudReticle Reticle { get; private set; }
        public ScaleReadout Readout { get; private set; }
        public HintToasts Hints { get; private set; }
        public LevelCard LevelCard { get; private set; }
        public ControlPills Controls { get; private set; }
        /// <summary>"Click to look": the game is being played but the mouse is not captured.</summary>
        public Sticker LookPrompt { get; private set; }
        public Sticker AutoplayPill { get; private set; }
        /// <summary>True while the HUD is on screen (the flow is Playing and a level is loaded).</summary>
        public bool Visible => Root != null && Root.Visible;

        public void Attach(Game attachedGame, PresentationContext presentationContext)
        {
            game = attachedGame;
            context = presentationContext;
            capture = UiCapture.Active ? UiCapture.Request : null;
            if (!UiFonts.Available)
            {
                Debug.LogWarning("[Toybox] The HUD is not shown: TextMeshPro's resources are missing. Run Toybox.EditorTools.ProjectSetup.Run.");
                return;
            }

            Root = UiRoot.Create(context, "HUD", 10, false);
            Reticle = new HudReticle(Root.Rect);
            Readout = new ScaleReadout(Root.Rect);
            Hints = new HintToasts(Root.Rect);
            LevelCard = new LevelCard(Root.Rect);
            IPrefStore store = context.Flow != null ? context.Flow.Progress.Store : new MemoryStore();
            Controls = new ControlPills(Root.Rect, store);

            LookPrompt = Pill(Root.Rect, "Look Prompt", "Click to look around", new Vector2(0f, 150f));
            AutoplayPill = Pill(Root.Rect, "Autoplay", "Autoplay  ·  P to take over", new Vector2(0f, 48f));

            game.Events.LevelLoaded += OnLevelLoaded;
            game.Events.LevelUnloading += OnLevelUnloading;
            game.Events.PropGrabbed += OnPropGrabbed;
            game.Events.PropHeld += OnPropHeld;
            game.Events.PropDropped += OnPropDropped;
            game.Events.Message += OnMessage;
            if (game.Level != null) OnLevelLoaded(new LevelEvent { Level = game.Level, Id = game.Level.Id });
            // For looking at the toasts through the screenshot tool, whatever the level says or does not say.
            if (capture == "toast")
            {
                Hints.Say("Step back while you hold a toy: it lands farther away, and bigger.", 600f);
                Hints.Say("Press R to start the level over.", 600f);
            }
            Frame(0f, 1f);
        }

        static Sticker Pill(RectTransform parent, string name, string text, Vector2 position)
        {
            Sticker pill = Sticker.Create(parent, name, StickerShape.Pill, UiTheme.Ink, new Vector2(320f, 40f));
            pill.Rect.Place(UiKit.Bottom, position, new Vector2(320f, 40f));
            TextMeshProUGUI label = UiKit.Label(pill.Content, "Label", text, UiFont.Body, UiTheme.SmallSize, UiTheme.Paper);
            label.rectTransform.Fill(8f);
            pill.Tween.Hide(true);
            return pill;
        }

        public void Frame(float dt, float alpha)
        {
            if (Root == null || game == null || game.IsDisposed) return;
            FlowState state = UiCapture.StateFor(context, capture);
            bool playing = state == FlowState.Playing && game.Level != null;
            Root.Visible = playing;
            if (playing)
            {
                if (pendingCard)
                {
                    pendingCard = false;
                    // The number is the level's place in the list where there is one (an ad-hoc level has no id of its own).
                    LevelCard.Show(game.Level, context.Flow != null ? context.Flow.LevelId : game.Level.Id);
                }
                bool holding = game.Grabber.IsHolding;
                Reticle.Frame(dt, !holding, !holding && game.Grabber.Focus != null);
                Readout.Frame(dt);
                Hints.Frame(dt);
                LevelCard.Frame(dt);

                // Only where a real loop drives the game: the screenshot tool never captures the mouse.
                bool free = context.Flow != null && !context.Autoplay && !context.PointerLocked && !context.LookHeld;
                pointerFreeFor = free ? pointerFreeFor + dt : 0f;
                LookPrompt.Tween.Set(pointerFreeFor >= LookPromptDelay);
                AutoplayPill.Tween.Set(context.Autoplay);
            }
            Root.Advance(dt);
        }

        public void Dispose()
        {
            if (game != null && Root != null && !game.IsDisposed)
            {
                game.Events.LevelLoaded -= OnLevelLoaded;
                game.Events.LevelUnloading -= OnLevelUnloading;
                game.Events.PropGrabbed -= OnPropGrabbed;
                game.Events.PropHeld -= OnPropHeld;
                game.Events.PropDropped -= OnPropDropped;
                game.Events.Message -= OnMessage;
            }
            if (Root != null)
            {
                Root.Unhook();
                UiKit.Destroy(Root.gameObject);
            }
            Root = null;
        }

        void OnLevelLoaded(LevelEvent e)
        {
            Readout.Reset();
            Controls.Reset();
            Hints.Clear();
            LevelCard.Hide();
            pendingCard = e.Level != null;
        }

        void OnLevelUnloading(LevelEvent e)
        {
            Readout.Reset();
            Controls.Reset();
        }

        void OnPropGrabbed(PropHoldEvent e)
        {
            Readout.Grab(e);
            Controls.Grab(e.Prop);
        }

        void OnPropHeld(PropHoldEvent e)
        {
            Readout.Hold(e);
            // Events are delivered when the tick ends, so this is the input of the tick they belong to.
            if (!context.Autoplay) Controls.Used(game.LastInput);
        }

        void OnPropDropped(PropHoldEvent e)
        {
            Readout.Drop(e);
            Controls.Drop(!context.Autoplay && e.Prop != null && !e.Prop.Removed);
        }

        void OnMessage(MessageEvent e) => Hints.Say(e.Text, e.Seconds);
    }

    /// <summary>
    /// The reticle: the four-pane mark, four 3 px rounded squares in Paper with a 1 px Ink edge. Idle it is
    /// 8 px across; on a grabbable it spreads to 14 px and turns 45 degrees, in 120 ms. Hidden while holding.
    /// </summary>
    public sealed class HudReticle
    {
        public const float IdleSpan = 8f, FocusSpan = 14f, PaneSize = 3f, EdgeSize = 1f, FocusAngle = 45f, FocusTime = 0.12f;

        readonly RectTransform root;
        readonly RectTransform[] panes = new RectTransform[4];
        float focus;

        public RectTransform Rect => root;
        public bool Visible => root.gameObject.activeSelf;
        /// <summary>0 idle .. 1 on a grabbable.</summary>
        public float Focus => focus;
        /// <summary>Across the four panes, in reference pixels.</summary>
        public float Span => Mathf.Lerp(IdleSpan, FocusSpan, UiTheme.Smooth(focus));
        public float Angle => FocusAngle * UiTheme.Smooth(focus);

        public HudReticle(RectTransform parent)
        {
            root = UiKit.Rect(parent, "Reticle");
            root.Place(UiKit.Centre, Vector2.zero, new Vector2(FocusSpan, FocusSpan));
            float size = PaneSize + EdgeSize * 2f;
            for (int i = 0; i < panes.Length; i++)
            {
                Image pane = UiKit.Image(root, "Pane", UiShape.Pane, Color.white);
                panes[i] = pane.rectTransform;
                panes[i].Place(UiKit.Centre, Vector2.zero, new Vector2(size, size));
            }
            Pose();
        }

        public void Frame(float dt, bool visible, bool focused)
        {
            if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
            if (!visible)
            {
                focus = 0f;
                return;
            }
            float target = focused ? 1f : 0f;
            if (focus == target) return;
            focus = Mathf.MoveTowards(focus, target, dt / FocusTime);
            Pose();
        }

        void Pose()
        {
            float offset = (Span - PaneSize) * 0.5f;
            panes[0].anchoredPosition = new Vector2(-offset, offset);
            panes[1].anchoredPosition = new Vector2(offset, offset);
            panes[2].anchoredPosition = new Vector2(-offset, -offset);
            panes[3].anchoredPosition = new Vector2(offset, -offset);
            root.localRotation = Quaternion.Euler(0f, 0f, Angle);
        }
    }

    /// <summary>
    /// The scale readout (ART_BIBLE 9.6): a sticker pill centred at 72% of the screen's height, there while
    /// a toy is held and for 1.2 s after it was let go.
    ///
    ///   factor   "x3.2" = projected scale now / scale at the grab; Ink within 5% of x1, Lagoon below, Cherry above
    ///   ruler    logarithmic, x1/8 to x8 at 40 px per octave, ticks at 1/4, 1/2, 1, 2, 4; the solid marker is
    ///            now, the hollow one the grab
    ///   figure   a 14 px figure beside a bar as tall as the toy really is, with a notch at the jump apex;
    ///            past 56 px the bar stays and the figure shrinks (to 3 px at the least)
    /// </summary>
    public sealed class ScaleReadout
    {
        public const float ScreenHeight = 0.72f, Linger = 1.2f;
        public const float PixelsPerOctave = 40f, Octaves = 3f;
        public const float FigureHeight = 14f, MaxBar = 56f, MinFigure = 3f;
        public const float NeutralBand = 0.05f, JumpStep = 0.05f;
        const float MarkerTime = 0.06f, FlashTime = 0.08f;
        const float RulerX = -36f, RulerY = -18f, FigureX = 134f, BaseY = -30f;

        readonly Sticker pill;
        readonly TextMeshProUGUI factorLabel;
        readonly RectTransform marker, figure, bar, notch;
        readonly Image markerImage;
        readonly char[] buffer = new char[96];

        float factor = 1f, lastScale, trueHeight, lingering, markerX, flash;
        bool holding;
        int shownValue = int.MinValue, shownDecimals = -1;

        public Sticker Pill => pill;
        public bool Shown => pill.Tween.IsShown;
        /// <summary>Scale now relative to the scale at the grab.</summary>
        public float Factor => factor;
        /// <summary>What the factor label says, without its spacing tags: "×3.2".</summary>
        public string FactorText
        {
            get
            {
                if (shownDecimals < 0) return "";
                int divisor = Pow10(shownDecimals);
                string whole = (shownValue / divisor).ToString();
                return "×" + (shownDecimals == 0 ? whole : whole + "." + (shownValue % divisor).ToString(new string('0', shownDecimals)));
            }
        }
        public Color FactorColor => factorLabel.color;
        /// <summary>The held toy's true height in units.</summary>
        public float TrueHeight => trueHeight;
        /// <summary>Where the solid marker is heading, in pixels from the "x1" tick.</summary>
        public float MarkerTarget => Mathf.Clamp(Mathf.Log(Mathf.Max(factor, 1e-4f), 2f), -Octaves, Octaves) * PixelsPerOctave;
        public float MarkerPosition => markerX;
        public float BarHeight => bar.sizeDelta.y;
        public float FigureSize => figure.sizeDelta.y;
        public float NotchHeight => notch.anchoredPosition.y - BaseY;
        /// <summary>How often the toy jumped to another surface (its scale changed by more than 5% in one tick).</summary>
        public int Jumps { get; private set; }

        public ScaleReadout(RectTransform parent)
        {
            var size = new Vector2(392f, 96f);
            pill = Sticker.Create(parent, "Scale Readout", StickerShape.Pill, UiTheme.Paper, size);
            pill.Rect.Place(new Vector2(0.5f, 1f - ScreenHeight), UiKit.Centre, Vector2.zero, size);
            RectTransform content = pill.Content;

            factorLabel = UiKit.Label(content, "Factor", "", UiFont.Display, UiTheme.ReadoutSize, UiTheme.Ink);
            factorLabel.rectTransform.Place(UiKit.Centre, new Vector2(RulerX, 20f), new Vector2(230f, 34f));

            float width = PixelsPerOctave * Octaves * 2f;
            Rule(content, "Ruler", new Vector2(RulerX, RulerY), new Vector2(width, 2f), UiTheme.Ink);
            for (int octave = -3; octave <= 3; octave++)
            {
                bool end = octave == -3 || octave == 3;
                float height = octave == 0 ? 14f : end ? 5f : 9f;
                Rule(content, "Tick", new Vector2(RulerX + octave * PixelsPerOctave, RulerY), new Vector2(2f, height), UiTheme.Ink);
            }
            Image hollow = UiKit.Image(content, "At Grab", UiShape.Ring, UiTheme.Ink);
            hollow.rectTransform.Place(UiKit.Centre, new Vector2(RulerX, RulerY), new Vector2(18f, 18f));
            markerImage = UiKit.Image(content, "Now", UiShape.Circle, UiTheme.Ink);
            marker = markerImage.rectTransform;
            marker.Place(UiKit.Centre, new Vector2(RulerX, RulerY), new Vector2(10f, 10f));

            // The figure and the bar stand on one line; pivots at their feet.
            Rule(content, "Ground", new Vector2(FigureX + 5f, BaseY - 1f), new Vector2(40f, 2f), UiTheme.Faint);
            Image figureImage = UiKit.Image(content, "Figure", UiShape.Figure, UiTheme.Ink);
            figure = figureImage.rectTransform;
            figure.Place(UiKit.Centre, UiKit.Bottom, new Vector2(FigureX - 6f, BaseY), new Vector2(FigureHeight * 0.5f, FigureHeight));
            Image barImage = UiKit.Image(content, "Toy Height", UiShape.White, UiTheme.Ink);
            bar = barImage.rectTransform;
            bar.Place(UiKit.Centre, UiKit.Bottom, new Vector2(FigureX + 12f, BaseY), new Vector2(8f, FigureHeight));
            Image notchImage = UiKit.Image(content, "Jump Apex", UiShape.White, UiTheme.Primary);
            notch = notchImage.rectTransform;
            notch.Place(UiKit.Centre, UiKit.Left, new Vector2(FigureX + 4f, BaseY), new Vector2(18f, 2f));

            pill.Tween.Hide(true);
            Refresh(true);
        }

        static void Rule(RectTransform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            Image rule = UiKit.Image(parent, name, UiShape.White, color);
            rule.rectTransform.Place(UiKit.Centre, position, size);
        }

        public void Grab(PropHoldEvent e)
        {
            holding = true;
            lingering = 0f;
            factor = 1f;
            lastScale = e.Scale;
            trueHeight = HeightOf(e.Prop, e.Scale);
            markerX = 0f;
            flash = 0f;
            Refresh(true);
            pill.Tween.Show();
        }

        public void Hold(PropHoldEvent e)
        {
            if (!holding) Grab(e);
            float scale = e.Scale;
            // The toy jumped to another surface: the readout ticks, without saying where.
            if (lastScale > 0f && Mathf.Abs(scale - lastScale) > JumpStep * lastScale)
            {
                Jumps++;
                flash = FlashTime;
            }
            lastScale = scale;
            factor = e.Factor;
            trueHeight = HeightOf(e.Prop, scale);
            Refresh(false);
        }

        public void Drop(PropHoldEvent e)
        {
            if (!holding) return;
            holding = false;
            if (e.Prop != null && !e.Prop.Removed)
            {
                factor = e.Factor;
                trueHeight = HeightOf(e.Prop, e.Scale);
                Refresh(false);
            }
            lingering = Linger;
        }

        /// <summary>A level came or went: nothing is held any more.</summary>
        public void Reset()
        {
            holding = false;
            lingering = 0f;
            pill.Tween.Hide(true);
        }

        public void Frame(float dt)
        {
            if (!holding && pill.Tween.IsShown)
            {
                lingering -= dt;
                if (lingering <= 0f) pill.Tween.Hide();
            }
            if (!pill.Tween.IsShown) return;

            float target = MarkerTarget;
            if (markerX != target)
            {
                markerX = Mathf.Abs(target - markerX) < 0.05f ? target : Mathf.Lerp(markerX, target, 1f - Mathf.Exp(-dt * 3f / MarkerTime));
                marker.anchoredPosition = new Vector2(RulerX + markerX, RulerY);
            }
            if (flash > 0f || marker.localScale.x != 1f)
            {
                flash = Mathf.Max(0f, flash - dt);
                float s = 1f + 0.6f * (flash / FlashTime);
                marker.localScale = new Vector3(s, s, 1f);
            }
        }

        // Height of the toy's bounds along world up, at this scale.
        static float HeightOf(Prop prop, float scale)
        {
            if (prop == null) return 0f;
            Vector3 half = prop.LocalHalfExtents;
            Quaternion rotation = prop.Removed || prop.Transform == null ? Quaternion.identity : prop.Transform.rotation;
            Vector3 x = rotation * Vector3.right, y = rotation * Vector3.up, z = rotation * Vector3.forward;
            return 2f * scale * (Mathf.Abs(x.y) * half.x + Mathf.Abs(y.y) * half.y + Mathf.Abs(z.y) * half.z);
        }

        void Refresh(bool snap)
        {
            Color colour = factor > 1f + NeutralBand ? UiTheme.Larger : factor < 1f - NeutralBand ? UiTheme.Smaller : UiTheme.Ink;
            if (factorLabel.color != colour) factorLabel.color = colour;
            if (markerImage.color != colour) markerImage.color = colour;
            WriteFactor();

            float tall = FigureHeight * trueHeight / Player.BaseHeight;
            float figureSize = FigureHeight;
            if (tall > MaxBar)
            {
                figureSize = Mathf.Max(MinFigure, FigureHeight * MaxBar / tall);
                tall = MaxBar;
            }
            tall = Mathf.Max(1f, tall);
            bar.sizeDelta = new Vector2(8f, tall);
            figure.sizeDelta = new Vector2(figureSize * 0.5f, figureSize);
            // The apex of a jump, 1.3 units, on the figure's own scale.
            notch.anchoredPosition = new Vector2(FigureX + 4f, BaseY + figureSize * JumpApex / Player.BaseHeight);

            if (snap)
            {
                markerX = MarkerTarget;
                marker.anchoredPosition = new Vector2(RulerX + markerX, RulerY);
                marker.localScale = Vector3.one;
            }
        }

        public const float JumpApex = 1.3f;
        const string Open = "<mspace=0.86em>", Close = "</mspace>";

        // "x3.2" with tabular digits, written into a buffer so a hold allocates nothing; only when the
        // number shown changes.
        void WriteFactor()
        {
            float value = Mathf.Clamp(factor, 0f, 9999f);
            int decimals = value >= 10f ? 0 : value >= 1f ? 1 : value >= 0.1f ? 2 : 3;
            int scaled = Mathf.RoundToInt(value * Pow10(decimals));
            if (decimals > 0 && scaled >= 10 * Pow10(decimals) && decimals == 1)
            {
                // 9.96 rounds to 10.0: show it as 10.
                decimals = 0;
                scaled = Mathf.RoundToInt(value);
            }
            if (scaled == shownValue && decimals == shownDecimals) return;
            shownValue = scaled;
            shownDecimals = decimals;

            int n = 0;
            buffer[n++] = '×';
            int divisor = Pow10(decimals);
            int whole = scaled / divisor, fraction = scaled % divisor;
            n = Append(buffer, n, Open);
            n = AppendInt(buffer, n, whole, 1);
            n = Append(buffer, n, Close);
            if (decimals > 0)
            {
                buffer[n++] = '.';
                n = Append(buffer, n, Open);
                n = AppendInt(buffer, n, fraction, decimals);
                n = Append(buffer, n, Close);
            }
            factorLabel.SetCharArray(buffer, 0, n);
        }

        static int Pow10(int exponent)
        {
            int result = 1;
            for (int i = 0; i < exponent; i++) result *= 10;
            return result;
        }

        static int Append(char[] target, int at, string text)
        {
            for (int i = 0; i < text.Length; i++) target[at++] = text[i];
            return at;
        }

        static int AppendInt(char[] target, int at, int value, int minDigits)
        {
            int digits = 1;
            for (int v = value; v >= 10; v /= 10) digits++;
            if (digits < minDigits) digits = minDigits;
            for (int i = digits - 1; i >= 0; i--)
            {
                target[at + i] = (char)('0' + value % 10);
                value /= 10;
            }
            return at + digits;
        }
    }

    /// <summary>
    /// Hint toasts: stickers at the bottom left, in the body face, driven by Message events. A toast
    /// sticks on, stays for as long as the message asks (5 s if it does not say) and peels off; up to three
    /// are up at a time, the newest lowest.
    /// </summary>
    public sealed class HintToasts
    {
        public const int Capacity = 3;
        public const float DefaultSeconds = 5f, MinSeconds = 1.5f, MaxSeconds = 20f;
        const float MaxTextWidth = 520f, PadX = 22f, PadY = 14f, Margin = 48f, Gap = 16f;

        sealed class Toast
        {
            public Sticker Sticker;
            public TextMeshProUGUI Label;
            public string Text;
            public float Remaining;
            public int Order;
            /// <summary>Up and not yet told to peel: it takes part in the stack.</summary>
            public bool Wanted;
        }

        readonly Toast[] toasts = new Toast[Capacity];
        int counter;

        public HintToasts(RectTransform parent)
        {
            for (int i = 0; i < Capacity; i++)
            {
                var toast = new Toast();
                toast.Sticker = Sticker.Create(parent, "Hint", StickerShape.Panel, UiTheme.Ink, new Vector2(200f, 52f));
                toast.Label = UiKit.Label(toast.Sticker.Content, "Text", "", UiFont.Body, UiTheme.BodySize, UiTheme.Paper, TextAlignmentOptions.Left, wrap: true);
                // Its box is fitted to the text, so there is nothing to shrink for.
                toast.Label.enableAutoSizing = false;
                toast.Label.fontSize = UiTheme.BodySize;
                toast.Label.rectTransform.Fill();
                toast.Label.rectTransform.offsetMin = new Vector2(PadX, PadY);
                toast.Label.rectTransform.offsetMax = new Vector2(-PadX, -PadY);
                toast.Sticker.Tween.Hide(true);
                toasts[i] = toast;
            }
        }

        /// <summary>How many toasts are up (not counting those peeling off).</summary>
        public int Count
        {
            get
            {
                int count = 0;
                foreach (Toast toast in toasts)
                    if (toast.Sticker.Tween.IsShown) count++;
                return count;
            }
        }

        /// <summary>The text of the newest toast that is up, or null.</summary>
        public string Latest
        {
            get
            {
                Toast newest = null;
                foreach (Toast toast in toasts)
                    if (toast.Sticker.Tween.IsShown && (newest == null || toast.Order > newest.Order)) newest = toast;
                return newest?.Text;
            }
        }

        public Sticker StickerAt(int index) => toasts[index].Sticker;

        public void Say(string text, float seconds)
        {
            if (string.IsNullOrEmpty(text)) return;
            float time = seconds > 0f ? Mathf.Clamp(seconds, MinSeconds, MaxSeconds) : DefaultSeconds;

            Toast target = null;
            // The same thing said again keeps its toast up; otherwise a free one, else the oldest.
            foreach (Toast toast in toasts)
                if (toast.Sticker.Tween.IsShown && toast.Text == text) target = toast;
            if (target != null)
            {
                target.Remaining = Mathf.Max(target.Remaining, time);
                return;
            }
            foreach (Toast toast in toasts)
                if (toast.Sticker.Tween.Phase == TweenPhase.Hidden) target = toast;
            if (target == null)
            {
                foreach (Toast toast in toasts)
                    if (target == null || toast.Order < target.Order) target = toast;
                target.Wanted = false;
                target.Sticker.Tween.Hide(true);
            }

            target.Text = text;
            target.Remaining = time;
            target.Order = ++counter;
            target.Wanted = true;
            target.Label.text = text;
            Vector2 preferred = target.Label.GetPreferredValues(text, MaxTextWidth, 0f);
            var size = new Vector2(Mathf.Min(preferred.x, MaxTextWidth) + PadX * 2f + 1f, Mathf.Max(preferred.y, UiTheme.BodySize) + PadY * 2f);
            target.Sticker.Size = size;
            Arrange();
            target.Sticker.Tween.Show();
        }

        public void Frame(float dt)
        {
            bool changed = false;
            foreach (Toast toast in toasts)
            {
                UiTween tween = toast.Sticker.Tween;
                if (!tween.IsShown) continue;
                toast.Remaining -= dt;
                if (toast.Remaining > 0f) continue;
                toast.Wanted = false;
                tween.Hide();
                changed = true;
            }
            if (changed) Arrange();
        }

        public void Clear()
        {
            foreach (Toast toast in toasts)
            {
                toast.Wanted = false;
                toast.Sticker.Tween.Hide(true);
            }
        }

        // Bottom left, the newest lowest; a toast that is peeling keeps its place until it is gone.
        void Arrange()
        {
            float y = Margin;
            int before = int.MaxValue;
            for (int placed = 0; placed < Capacity; placed++)
            {
                Toast next = null;
                foreach (Toast toast in toasts)
                {
                    if (!toast.Wanted || toast.Order >= before) continue;
                    if (next == null || toast.Order > next.Order) next = toast;
                }
                if (next == null) break;
                before = next.Order;
                next.Sticker.Rect.Place(UiKit.BottomLeft, new Vector2(Margin, y), next.Sticker.Size);
                y += next.Sticker.Size.y + Gap;
            }
        }
    }

    /// <summary>
    /// The level card: "03 - THE CHEESE WEDGE" with the number at 96 and the title at 40 in the display
    /// face and the blurb in the body face, on a blister card at the top left. It sticks on when a level
    /// starts to be played and peels away after 3.5 s.
    /// </summary>
    public sealed class LevelCard
    {
        public const float Seconds = 3.5f;

        readonly Sticker card;
        readonly TextMeshProUGUI number, title, blurb;
        float remaining;

        public Sticker Card => card;
        public bool Shown => card.Tween.IsShown;
        public string Number => number.text;
        public string Title => title.text;
        public string Blurb => blurb.text;

        public LevelCard(RectTransform parent)
        {
            var size = new Vector2(680f, 196f);
            card = Sticker.Create(parent, "Level Card", StickerShape.Card, UiTheme.Paper, size);
            card.Rect.Place(UiKit.TopLeft, new Vector2(56f, -56f), size);
            number = UiKit.Label(card.Content, "Number", "", UiFont.Display, UiTheme.NumberSize, UiTheme.Ink);
            number.rectTransform.Place(UiKit.Left, new Vector2(24f, -12f), new Vector2(190f, 120f));
            title = UiKit.Label(card.Content, "Title", "", UiFont.Display, UiTheme.TitleSize, UiTheme.Ink, TextAlignmentOptions.BottomLeft, wrap: true);
            title.rectTransform.Place(UiKit.TopLeft, new Vector2(230f, -34f), new Vector2(426f, 86f));
            blurb = UiKit.Label(card.Content, "Blurb", "", UiFont.Body, UiTheme.BodySize, UiTheme.Ink, TextAlignmentOptions.TopLeft, wrap: true);
            blurb.rectTransform.Place(UiKit.TopLeft, new Vector2(232f, -126f), new Vector2(424f, 56f));
            card.Tween.Hide(true);
        }

        public void Show(LevelDefinition level, int id)
        {
            if (level == null) return;
            number.text = Mathf.Max(0, id).ToString("00");
            title.text = (level.Title ?? "").ToUpperInvariant();
            blurb.text = level.Blurb ?? "";
            remaining = Seconds;
            card.Tween.Hide(true);
            card.Tween.Show();
        }

        public void Hide() => card.Tween.Hide(true);

        public void Frame(float dt)
        {
            if (!card.Tween.IsShown) return;
            remaining -= dt;
            if (remaining <= 0f) card.Tween.Hide();
        }
    }

    /// <summary>
    /// The held-control pills: key caps at the bottom right while a toy is held - "Q / wheel: turn",
    /// "F: flip", "click: drop". Each stops appearing once its control has been used three times (kept in
    /// the player's store, so they stay away).
    /// </summary>
    public sealed class ControlPills
    {
        public const int Uses = 3;
        const string Prefix = "toybox.hud.uses.";
        const float Height = 40f, Margin = 48f, Gap = 14f;

        public enum Control
        {
            Turn,
            Flip,
            Drop,
        }

        static readonly string[] Keys = { "turn", "flip", "drop" };
        static readonly string[] Caps = { "Q / wheel", "F", "click" };
        static readonly string[] Actions = { "turn", "flip", "drop" };

        readonly IPrefStore store;
        readonly Sticker[] pills = new Sticker[3];
        readonly int[] used = new int[3];
        bool holding, canFlip;

        public ControlPills(RectTransform parent, IPrefStore prefStore)
        {
            store = prefStore;
            for (int i = 0; i < pills.Length; i++)
            {
                used[i] = Mathf.Max(0, store.GetInt(Prefix + Keys[i], 0));
                pills[i] = Build(parent, Caps[i], Actions[i]);
                pills[i].Tween.Hide(true);
            }
        }

        static Sticker Build(RectTransform parent, string cap, string action)
        {
            Sticker pill = Sticker.Create(parent, "Control " + action, StickerShape.Pill, UiTheme.Ink, new Vector2(200f, Height));
            TextMeshProUGUI capLabel = UiKit.Label(pill.Content, "Key", cap, UiFont.Body, UiTheme.SmallSize, UiTheme.Ink);
            capLabel.enableAutoSizing = false;
            capLabel.fontSize = UiTheme.SmallSize;
            float capWidth = Mathf.Ceil(capLabel.GetPreferredValues(cap).x) + 20f;
            TextMeshProUGUI actionLabel = UiKit.Label(pill.Content, "Action", action, UiFont.Body, UiTheme.SmallSize, UiTheme.Paper, TextAlignmentOptions.Left);
            actionLabel.enableAutoSizing = false;
            actionLabel.fontSize = UiTheme.SmallSize;
            float actionWidth = Mathf.Ceil(actionLabel.GetPreferredValues(action).x) + 2f;

            // The key cap: a small Paper chip on the Ink pill.
            Image chip = UiKit.Image(pill.Content, "Cap", UiShape.Pill, UiTheme.Paper);
            chip.rectTransform.Place(UiKit.Left, new Vector2(6f, 0f), new Vector2(capWidth, Height - 12f));
            chip.pixelsPerUnitMultiplier = UiAtlas.PillBorder * 2f / (Height - 12f);
            chip.rectTransform.SetAsFirstSibling();
            capLabel.rectTransform.Place(UiKit.Left, new Vector2(6f, 0f), new Vector2(capWidth, Height - 12f));
            actionLabel.rectTransform.Place(UiKit.Left, new Vector2(capWidth + 16f, 0f), new Vector2(actionWidth, Height));
            pill.Size = new Vector2(capWidth + actionWidth + 34f, Height);
            return pill;
        }

        public Sticker Pill(Control control) => pills[(int)control];
        public bool Shown(Control control) => pills[(int)control].Tween.IsShown;
        public int UsedCount(Control control) => used[(int)control];

        public void Grab(Prop prop)
        {
            holding = true;
            canFlip = prop != null && prop.AllowPitch;
            Sync();
        }

        /// <summary>The input of one tick of a hold.</summary>
        public void Used(InputFrame input)
        {
            if (!holding) return;
            if (input.RotateYaw != 0) Count(Control.Turn);
            if (input.RotatePitch && canFlip) Count(Control.Flip);
        }

        public void Drop(bool byThePlayer)
        {
            if (!holding) return;
            holding = false;
            if (byThePlayer) Count(Control.Drop);
            Sync();
        }

        /// <summary>A level came or went: nothing is held any more.</summary>
        public void Reset()
        {
            holding = false;
            foreach (Sticker pill in pills) pill.Tween.Hide(true);
        }

        void Count(Control control)
        {
            int i = (int)control;
            if (used[i] >= Uses) return;
            used[i]++;
            store.SetInt(Prefix + Keys[i], used[i]);
            if (used[i] >= Uses) store.Save();
        }

        // The pills that are still wanted, stacked upward from the bottom right corner.
        void Sync()
        {
            float y = Margin;
            for (int i = pills.Length - 1; i >= 0; i--)
            {
                bool wanted = holding && used[i] < Uses && (i != (int)Control.Flip || canFlip);
                Sticker pill = pills[i];
                if (wanted)
                {
                    pill.Rect.Place(UiKit.BottomRight, new Vector2(-Margin, y), pill.Size);
                    y += Height + Gap;
                }
                pill.Tween.Set(wanted);
            }
        }
    }
}
