using System.Collections.Generic;
using System.Globalization;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.UI
{
    /// <summary>
    /// A minimal IMGUI overlay: crosshair, level title and objective, timed messages, the scale of the
    /// held prop, the click-to-play prompt and the level-complete banner. Temporary - a later milestone
    /// replaces it. It reads Game state and listens to Game events; the two flags come from the runner.
    /// </summary>
    public sealed class DebugHud : MonoBehaviour
    {
        // The layout is designed for a screen 720 units high and scaled to the real one.
        const float ReferenceHeight = 720f;
        const float MessageFade = 0.4f;
        const int MaxMessages = 4;

        static readonly Color Ink = new Color(0.10f, 0.07f, 0.20f);
        static readonly Color Paper = new Color(1f, 0.98f, 0.94f);
        static readonly Color TargetColor = new Color(1f, 0.82f, 0.40f);
        static readonly Color HeldColor = new Color(0.02f, 0.84f, 0.63f);

        struct Message
        {
            public string Text;
            public float Until;
        }

        readonly List<Message> messages = new List<Message>();
        Game game;
        string title = "", blurb = "";
        string banner, bannerDetail;
        float heldFactor = 1f;
        Prop target;
        GUIStyle titleStyle, textStyle, bigStyle, smallStyle;

        /// <summary>Show the "click to play" prompt (the mouse is not captured).</summary>
        public bool ClickToPlay;
        /// <summary>Show that a bot is playing.</summary>
        public bool Autoplay;
        /// <summary>Second line of the level-complete banner (what happens next), set by the runner.</summary>
        public string BannerNote;

        /// <summary>The prop a click would grab, as of the last Update.</summary>
        public Prop Target => target;
        public bool BannerVisible => banner != null;
        public int MessageCount => messages.Count;
        /// <summary>How many times the overlay has been drawn (a headless run never draws it).</summary>
        public int DrawCount { get; private set; }

        public void Bind(Game newGame)
        {
            Unbind();
            game = newGame;
            if (game == null) return;
            game.Events.LevelLoaded += OnLevelLoaded;
            game.Events.LevelCompleted += OnLevelCompleted;
            game.Events.Message += OnMessage;
            game.Events.PropGrabbed += OnPropHeld;
            game.Events.PropHeld += OnPropHeld;
            if (game.Level != null) ShowLevel(game.Level);
        }

        public void Unbind()
        {
            if (game == null) return;
            game.Events.LevelLoaded -= OnLevelLoaded;
            game.Events.LevelCompleted -= OnLevelCompleted;
            game.Events.Message -= OnMessage;
            game.Events.PropGrabbed -= OnPropHeld;
            game.Events.PropHeld -= OnPropHeld;
            game = null;
            target = null;
        }

        void OnDestroy() => Unbind();

        void OnLevelLoaded(LevelEvent e)
        {
            banner = null;
            bannerDetail = null;
            BannerNote = null;
            messages.Clear();
            target = null;
            heldFactor = 1f;
            if (e.Level != null) ShowLevel(e.Level);
        }

        void ShowLevel(LevelDefinition level)
        {
            title = level.Title ?? "";
            blurb = level.Blurb ?? "";
        }

        void OnLevelCompleted(LevelEvent e)
        {
            banner = "Level complete!";
            bannerDetail = e.Time.ToString("0.0", CultureInfo.InvariantCulture) + " s";
        }

        void OnMessage(MessageEvent e)
        {
            if (string.IsNullOrEmpty(e.Text)) return;
            if (messages.Count >= MaxMessages) messages.RemoveAt(0);
            messages.Add(new Message { Text = e.Text, Until = Time.unscaledTime + Mathf.Max(0.5f, e.Seconds) });
        }

        void OnPropHeld(PropHoldEvent e)
        {
            heldFactor = e.OldScale > 0f ? e.NewScale / e.OldScale : 1f;
        }

        void Update()
        {
            if (game == null || game.IsDisposed)
            {
                target = null;
                return;
            }
            // Once per rendered frame, not per GUI event: it costs a few physics queries.
            target = game.Level != null && !game.Grabber.IsHolding ? game.Grabber.FindTarget() : null;
            float now = Time.unscaledTime;
            messages.RemoveAll(m => m.Until <= now);
        }

        void OnGUI()
        {
            if (game == null || game.IsDisposed || Event.current.type != EventType.Repaint) return;
            EnsureStyles();
            DrawCount++;

            float scale = Screen.height / ReferenceHeight;
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            float height = ReferenceHeight;

            DrawLevel();
            DrawCrosshair(width * 0.5f, height * 0.5f);
            DrawMessages(width, height);
            if (Autoplay) Label(new Rect(0f, height - 34f, width - 16f, 24f), "AUTOPLAY  -  P to take over", smallStyle, TextAnchor.MiddleRight, Paper);
            if (banner != null) DrawBanner(width, height);
            else if (ClickToPlay) DrawClickToPlay(width, height);

            GUI.matrix = previous;
            GUI.color = Color.white;
        }

        void DrawLevel()
        {
            if (title.Length > 0) Label(new Rect(18f, 12f, 800f, 34f), title, titleStyle, TextAnchor.UpperLeft, Paper);
            if (blurb.Length > 0) Label(new Rect(18f, 46f, 800f, 26f), blurb, textStyle, TextAnchor.UpperLeft, Paper);
        }

        void DrawCrosshair(float x, float y)
        {
            bool holding = game.Grabber.IsHolding;
            if (holding)
            {
                // A hollow square while something is in hand, with its size relative to when it was grabbed.
                Frame(x, y, 9f, 2f, Ink, 1f);
                Frame(x, y, 9f, 2f, HeldColor, 0f);
                string factor = "x" + heldFactor.ToString(heldFactor >= 10f ? "0" : "0.00", CultureInfo.InvariantCulture);
                Label(new Rect(x - 100f, y + 16f, 200f, 26f), factor, textStyle, TextAnchor.UpperCenter, HeldColor);
            }
            else if (target != null)
            {
                // A wide plus over something that can be picked up.
                Box(new Rect(x - 11f, y - 3f, 22f, 6f), Ink);
                Box(new Rect(x - 3f, y - 11f, 6f, 22f), Ink);
                Box(new Rect(x - 10f, y - 2f, 20f, 4f), TargetColor);
                Box(new Rect(x - 2f, y - 10f, 4f, 20f), TargetColor);
            }
            else
            {
                Box(new Rect(x - 3f, y - 3f, 6f, 6f), Ink);
                Box(new Rect(x - 2f, y - 2f, 4f, 4f), Paper);
            }
        }

        void DrawMessages(float width, float height)
        {
            float now = Time.unscaledTime;
            float y = height * 0.72f;
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                Message message = messages[i];
                float alpha = Mathf.Clamp01((message.Until - now) / MessageFade);
                if (alpha <= 0f) continue;
                Color color = Paper;
                color.a = alpha;
                Label(new Rect(0f, y, width, 30f), message.Text, textStyle, TextAnchor.MiddleCenter, color);
                y -= 30f;
            }
        }

        void DrawBanner(float width, float height)
        {
            Box(new Rect(0f, height * 0.30f, width, 120f), new Color(Ink.r, Ink.g, Ink.b, 0.72f));
            Label(new Rect(0f, height * 0.30f + 14f, width, 56f), banner, bigStyle, TextAnchor.MiddleCenter, TargetColor);
            string detail = string.IsNullOrEmpty(BannerNote) ? bannerDetail : bannerDetail + "   -   " + BannerNote;
            Label(new Rect(0f, height * 0.30f + 74f, width, 30f), detail, textStyle, TextAnchor.MiddleCenter, Paper);
        }

        void DrawClickToPlay(float width, float height)
        {
            Box(new Rect(0f, height * 0.56f, width, 96f), new Color(Ink.r, Ink.g, Ink.b, 0.6f));
            Label(new Rect(0f, height * 0.56f + 8f, width, 46f), "Click to play", bigStyle, TextAnchor.MiddleCenter, Paper);
            Label(new Rect(0f, height * 0.56f + 54f, width, 20f),
                "WASD move  -  Space jump  -  Shift run  -  Click or E grab and drop  -  Q or wheel turn  -  F flip  -  R restart",
                smallStyle, TextAnchor.MiddleCenter, Paper);
            Label(new Rect(0f, height * 0.56f + 72f, width, 20f), "No mouse capture? Hold the right mouse button to look around.",
                smallStyle, TextAnchor.MiddleCenter, Paper);
        }

        void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = Style(26, FontStyle.Bold);
            textStyle = Style(19, FontStyle.Normal);
            bigStyle = Style(40, FontStyle.Bold);
            smallStyle = Style(14, FontStyle.Normal);
        }

        static GUIStyle Style(int size, FontStyle fontStyle)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                fontStyle = fontStyle,
                wordWrap = false,
                richText = false,
            };
            // The colour comes from GUI.color at draw time.
            style.normal.textColor = Color.white;
            return style;
        }

        static void Box(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }

        // A square outline centred on a point; `grow` widens it by that much on every side.
        static void Frame(float x, float y, float half, float thickness, Color color, float grow)
        {
            float h = half + grow, t = thickness + grow * 2f;
            Box(new Rect(x - h, y - h, h * 2f, t), color);
            Box(new Rect(x - h, y + h - t, h * 2f, t), color);
            Box(new Rect(x - h, y - h, t, h * 2f), color);
            Box(new Rect(x + h - t, y - h, t, h * 2f), color);
        }

        // Text with a one-unit drop shadow, so it stays readable over any part of the scene.
        static void Label(Rect rect, string text, GUIStyle style, TextAnchor anchor, Color color)
        {
            style.alignment = anchor;
            GUI.color = new Color(Ink.r, Ink.g, Ink.b, color.a * 0.9f);
            GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text, style);
            GUI.color = color;
            GUI.Label(rect, text, style);
        }
    }
}
