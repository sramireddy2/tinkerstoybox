using System;
using System.Collections.Generic;
using TMPro;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Platform;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Toybox.UI
{
    /// <summary>A part of the screen that takes a click without being a control (the title: click anywhere).</summary>
    public sealed class UiClickArea : MonoBehaviour, IPointerClickHandler
    {
        public Action Clicked;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) Clicked?.Invoke();
        }
    }

    /// <summary>
    /// The title (ART_BIBLE 10.5): "TINKER'S TOYBOX" in the display face at 120 with the sticker text
    /// style, the "O" of TOYBOX being the four-pane mark; a pulsing "Click to play" pill; the catalogue and
    /// the settings beside it. A click anywhere starts the game too (and unlocks audio in the browser).
    /// </summary>
    public sealed class TitleMenu : MenuScreen
    {
        const float PulseHz = 1f, PulseDepth = 0.035f;

        float time;

        public Sticker Logo { get; }
        /// <summary>The four-pane mark standing in for the "O" of TOYBOX.</summary>
        public Image Mark { get; private set; }
        public UiButton Play { get; }
        public UiButton LevelsButton { get; }
        public UiButton SettingsButton { get; }

        public TitleMenu(MenuPresenter menu) : base(menu, "Title")
        {
            RectTransform root = Screen.Root;
            Image area = UiKit.Image(root, "Click Area", UiShape.White, Color.clear, raycast: true);
            area.rectTransform.Fill();
            area.gameObject.AddComponent<UiClickArea>().Clicked = () =>
            {
                if (Screen.Enabled) Screen.Run(StartGame);
            };

            Logo = Stick(BuildLogo(root));

            Play = Button(root, "Click to play", ButtonStyle.Primary, new Vector2(420f, 84f), StartGame);
            Play.Label.fontSizeMax = 30f;
            Play.Sticker.Rect.Place(UiKit.Bottom, UiKit.Centre, new Vector2(0f, 236f), new Vector2(420f, 84f));
            Stick(Play.Sticker);
            LevelsButton = Button(root, "Levels", ButtonStyle.Paper, new Vector2(230f, 58f), () => Menu.Flow?.OpenLevelSelect());
            LevelsButton.Sticker.Rect.Place(UiKit.Bottom, UiKit.Centre, new Vector2(-130f, 124f), new Vector2(230f, 58f));
            Stick(LevelsButton.Sticker);
            SettingsButton = Button(root, "Settings", ButtonStyle.Paper, new Vector2(230f, 58f), Menu.OpenSettings);
            SettingsButton.Sticker.Rect.Place(UiKit.Bottom, UiKit.Centre, new Vector2(130f, 124f), new Vector2(230f, 58f));
            Stick(SettingsButton.Sticker);

            Screen.AddRow(Play);
            Screen.AddRow(LevelsButton, SettingsButton);
        }

        void StartGame()
        {
            GameFlow flow = Menu.Flow;
            if (flow != null && flow.State == FlowState.Title) flow.StartLevel(flow.LevelId);
        }

        // Two lines. The second is "T YBOX" with a gap where the "O" would be; the mark is put into the gap
        // from where the face really set the letters, so it sits right whichever face it is.
        Sticker BuildLogo(RectTransform root)
        {
            var size = new Vector2(1240f, 330f);
            Sticker logo = Sticker.Create(root, "Logo", StickerShape.Panel, Color.clear, size);
            logo.Rect.Place(UiKit.Top, new Vector2(0f, -96f), size);
            logo.Shadow.enabled = false;
            logo.Border.enabled = false;
            logo.Face.enabled = false;

            TextMeshProUGUI first = UiKit.Label(logo.Content, "Tinker's", "TINKER’S", UiFont.Display, UiTheme.LogoSize, UiTheme.Ink, sticker: true);
            first.rectTransform.Place(UiKit.Top, new Vector2(0f, 0f), new Vector2(size.x, 160f));

            TextMeshProUGUI second = UiKit.Label(logo.Content, "Toybox", "T<space=" + MarkGap + "em>YBOX", UiFont.Display, UiTheme.LogoSize, UiTheme.Ink, sticker: true);
            second.rectTransform.Place(UiKit.Top, new Vector2(0f, -150f), new Vector2(size.x, 160f));
            Wordmark = second;

            Image shadow = UiKit.Image(second.rectTransform, "Mark Shadow", UiShape.FourPane, UiTheme.Shadow);
            Image edge = UiKit.Image(second.rectTransform, "Mark Edge", UiShape.FourPane, UiTheme.Paper);
            Mark = UiKit.Image(second.rectTransform, "Mark", UiShape.FourPane, UiTheme.Ink);
            markParts = new[] { shadow.rectTransform, edge.rectTransform, Mark.rectTransform };
            PlaceMark();
            return logo;
        }

        /// <summary>The gap left for the mark between "T" and "YBOX", in ems.</summary>
        public const float MarkGap = 0.92f;
        RectTransform[] markParts;

        /// <summary>The second line of the logo: "T", a gap, "YBOX".</summary>
        public TextMeshProUGUI Wordmark { get; private set; }

        // Between the right edge of the "T" and the left edge of the "Y", as tall as a capital.
        void PlaceMark()
        {
            Wordmark.ForceMeshUpdate();
            TMP_TextInfo info = Wordmark.textInfo;
            if (info == null || info.characterCount < 2) return;
            TMP_CharacterInfo t = info.characterInfo[0], y = info.characterInfo[1];
            float cap = Mathf.Max(8f, t.topLeft.y - t.bottomLeft.y);
            float middle = (t.topRight.x + y.bottomLeft.x) * 0.5f;
            float centre = (t.topLeft.y + t.bottomLeft.y) * 0.5f;
            float edgeGrow = cap * 0.16f;
            Place(markParts[0], middle + cap * 0.045f, centre - cap * 0.065f, cap + edgeGrow);
            Place(markParts[1], middle, centre, cap + edgeGrow);
            Place(markParts[2], middle, centre, cap);
        }

        // Character positions are measured from the label's pivot, the middle of its top edge.
        static void Place(RectTransform rect, float x, float y, float size) =>
            rect.Place(UiKit.Top, UiKit.Centre, new Vector2(x, y), new Vector2(size, size));

        protected override void Opening()
        {
            time = 0f;
            Play.Sticker.Rect.localScale = Vector3.one;
            PlaceMark();
        }

        protected override void Tick(float dt)
        {
            if (Settings.ReduceMotion) return;
            time += dt;
            float pulse = 1f + PulseDepth * Mathf.Sin(time * PulseHz * Mathf.PI * 2f);
            Play.Sticker.Rect.localScale = new Vector3(pulse, pulse, 1f);
        }
    }

    /// <summary>
    /// The pause card (ART_BIBLE 10.5): a hang-tab card listing Resume, Restart, Hints, Settings and Level
    /// Select, over the frame the pause froze. Hints shows the level's hints one at a time beside the card.
    /// </summary>
    public sealed class PauseMenu : MenuScreen
    {
        int hintIndex = -1;

        public Sticker Card { get; }
        public UiButton Resume { get; }
        public UiButton Restart { get; }
        public UiButton Hints { get; }
        public UiButton SettingsButton { get; }
        public UiButton LevelSelect { get; }
        public Sticker HintPanel { get; }
        public TextMeshProUGUI HintTitle { get; }
        public TextMeshProUGUI HintText { get; }

        public PauseMenu(MenuPresenter menu) : base(menu, "Pause")
        {
            RectTransform root = Screen.Root;
            var size = new Vector2(420f, 566f);
            Card = Stick(Sticker.Create(root, "Pause Card", StickerShape.Card, UiTheme.Paper, size));
            TextMeshProUGUI title = UiKit.Label(Card.Content, "Title", "PAUSED", UiFont.Display, UiTheme.TitleSize, UiTheme.Ink);
            title.rectTransform.Place(UiKit.Top, new Vector2(0f, -46f), new Vector2(340f, 60f));

            var buttonSize = new Vector2(324f, 62f);
            float y = -128f;
            Resume = Entry("Resume", ButtonStyle.Primary, buttonSize, ref y, () => Menu.Flow?.Resume());
            Restart = Entry("Restart", ButtonStyle.Paper, buttonSize, ref y, () => Menu.Flow?.Restart());
            Hints = Entry("Hints", ButtonStyle.Paper, buttonSize, ref y, NextHint);
            SettingsButton = Entry("Settings", ButtonStyle.Paper, buttonSize, ref y, Menu.OpenSettings);
            LevelSelect = Entry("Level Select", ButtonStyle.Paper, buttonSize, ref y, () => Menu.Flow?.OpenLevelSelect());

            var hintSize = new Vector2(430f, 230f);
            HintPanel = Sticker.Create(root, "Hint", StickerShape.Panel, UiTheme.Paper, hintSize);
            HintPanel.Rect.Place(UiKit.Centre, UiKit.Left, new Vector2(size.x * 0.5f + 40f, 0f), hintSize);
            HintTitle = UiKit.Label(HintPanel.Content, "Title", "", UiFont.Display, UiTheme.ButtonSize, UiTheme.Ink, TextAlignmentOptions.Left);
            HintTitle.rectTransform.Place(UiKit.TopLeft, new Vector2(26f, -20f), new Vector2(hintSize.x - 52f, 36f));
            HintText = UiKit.Label(HintPanel.Content, "Text", "", UiFont.Body, UiTheme.BodySize, UiTheme.Ink, TextAlignmentOptions.TopLeft, wrap: true);
            HintText.rectTransform.Place(UiKit.TopLeft, new Vector2(26f, -66f), new Vector2(hintSize.x - 52f, hintSize.y - 88f));
            HintPanel.Tween.Hide(true);
        }

        UiButton Entry(string text, ButtonStyle style, Vector2 size, ref float y, Action clicked)
        {
            UiButton button = Button(Card.Content, text, style, size, clicked);
            // Paper pills on a Paper card: the border is what sets them off.
            if (style == ButtonStyle.Paper)
            {
                button.RestBorder = UiTheme.Faint;
                button.Refresh(true);
            }
            button.Sticker.Rect.Place(UiKit.Top, new Vector2(0f, y), size);
            y -= size.y + 18f;
            Screen.AddRow(button);
            return button;
        }

        /// <summary>The hint that is showing (0-based), or -1.</summary>
        public int HintIndex => HintPanel.Tween.IsShown ? hintIndex : -1;

        /// <summary>Shows the level's next hint; after the last comes the first again.</summary>
        public void NextHint()
        {
            LevelDefinition level = Menu.Game.Level;
            string[] hints = level != null ? level.Hints : null;
            if (hints == null || hints.Length == 0)
            {
                hintIndex = -1;
                HintTitle.text = "HINTS";
                HintText.text = "This level keeps its secrets.";
            }
            else
            {
                hintIndex = (hintIndex + 1) % hints.Length;
                HintTitle.text = "HINT " + (hintIndex + 1) + " / " + hints.Length;
                HintText.text = hints[hintIndex] ?? "";
            }
            HintPanel.Tween.Hide(true);
            HintPanel.Tween.Show();
        }

        /// <summary>A new level: its hints start over.</summary>
        public void ResetHints()
        {
            hintIndex = -1;
            HintPanel.Tween.Hide(true);
        }

        protected override void Opening()
        {
            HintPanel.Tween.Hide(true);
            if (UiCapture.Wants("hints")) NextHint();
        }

        protected override void Closing() => HintPanel.Tween.Hide();
    }

    /// <summary>
    /// The settings card (ART_BIBLE 10.5): quality, lens blur strength, sensitivity, field of view, volume,
    /// music, reduce motion, high-visibility toys. It only writes <see cref="Settings"/>; whoever applies a
    /// setting listens there. Closing it saves.
    /// </summary>
    public sealed class SettingsMenu : MenuScreen
    {
        static readonly string[] Qualities = { "Auto", "Low", "Medium", "High" };

        public Sticker Card { get; }
        public UiChoice Quality { get; }
        public UiSlider LensBlur { get; }
        public UiSlider Sensitivity { get; }
        public UiSlider FieldOfView { get; }
        public UiSlider Volume { get; }
        public UiSlider Music { get; }
        public UiToggle ReduceMotion { get; }
        public UiToggle HighVisibility { get; }
        public UiButton Done { get; }

        public SettingsMenu(MenuPresenter menu) : base(menu, "Settings")
        {
            RectTransform root = Screen.Root;
            var size = new Vector2(720f, 716f);
            Card = Stick(Sticker.Create(root, "Settings Card", StickerShape.Card, UiTheme.Paper, size));
            TextMeshProUGUI title = UiKit.Label(Card.Content, "Title", "SETTINGS", UiFont.Display, UiTheme.TitleSize, UiTheme.Ink);
            title.rectTransform.Place(UiKit.Top, new Vector2(0f, -46f), new Vector2(500f, 60f));

            float y = -122f;
            Quality = Row(UiChoice.Create(Card.Content, "Quality", Qualities, (int)Settings.Quality, i => Settings.Quality = (QualitySetting)i), ref y);
            LensBlur = Row(UiSlider.Create(Card.Content, "Lens blur", 0f, 1f, 0.05f, Settings.LensBlur, Percent, v => Settings.LensBlur = v), ref y);
            Sensitivity = Row(UiSlider.Create(Card.Content, "Mouse speed", Settings.MinMouseSensitivity, Settings.MaxMouseSensitivity, 0.01f,
                Settings.MouseSensitivity, v => "×" + (v / Settings.DefaultMouseSensitivity).ToString("0.0"), v => Settings.MouseSensitivity = v), ref y);
            FieldOfView = Row(UiSlider.Create(Card.Content, "Field of view", Settings.MinFieldOfView, Settings.MaxFieldOfView, 1f,
                Settings.FieldOfView, v => Mathf.RoundToInt(v) + "°", v => Settings.FieldOfView = v), ref y);
            Volume = Row(UiSlider.Create(Card.Content, "Volume", 0f, 1f, 0.05f, Settings.MasterVolume, Percent, v => Settings.MasterVolume = v), ref y);
            Music = Row(UiSlider.Create(Card.Content, "Music", 0f, 1f, 0.05f, Settings.MusicVolume, Percent, v => Settings.MusicVolume = v), ref y);
            ReduceMotion = Row(UiToggle.Create(Card.Content, "Reduce motion", Settings.ReduceMotion, v => Settings.ReduceMotion = v), ref y);
            HighVisibility = Row(UiToggle.Create(Card.Content, "High-visibility toys", Settings.HighVisibility, v => Settings.HighVisibility = v), ref y);

            Done = Button(Card.Content, "Done", ButtonStyle.Primary, new Vector2(260f, 60f), Menu.CloseSettings);
            Done.Sticker.Rect.Place(UiKit.Bottom, new Vector2(0f, 34f), new Vector2(260f, 60f));
            Screen.AddRow(Done);
            Screen.Cancelled = Menu.CloseSettings;
        }

        static string Percent(float value) => Mathf.RoundToInt(value * 100f) + "%";

        T Row<T>(T row, ref float y) where T : UiRow
        {
            row.Rect.Place(UiKit.Top, new Vector2(0f, y), new Vector2(UiRow.Width, UiRow.Height));
            y -= UiRow.Height + 6f;
            Screen.AddRow(row);
            return row;
        }

        // The settings may have changed elsewhere since the card was last up.
        protected override void Opening()
        {
            Quality.Set((int)Settings.Quality, false);
            LensBlur.Set(Settings.LensBlur, false);
            Sensitivity.Set(Settings.MouseSensitivity, false);
            FieldOfView.Set(Settings.FieldOfView, false);
            Volume.Set(Settings.MasterVolume, false);
            Music.Set(Settings.MusicVolume, false);
            ReduceMotion.Set(Settings.ReduceMotion, false);
            HighVisibility.Set(Settings.HighVisibility, false);
        }
    }

    /// <summary>
    /// Level select, "The Catalogue" (ART_BIBLE 10.5): a grid of blister cards, five to a row. A card's
    /// backing is its level's dip (mid); it carries the number at 96 and a Paper circle with the silhouette
    /// of the level's hero toy in the preset's hero candy. A locked card is Kraft with a "?"; a completed
    /// one wears a "COLLECTED" sticker turned 6 degrees one way or the other and shows the best time.
    /// With the focus on it a card lifts and one four-pane glint slides across its circle.
    /// </summary>
    public sealed class CatalogueMenu : MenuScreen
    {
        public const int Columns = 5;
        public const float StampAngle = 6f, GlintTime = UiTheme.Slow;
        static readonly Vector2 CardSize = new Vector2(300f, 236f);
        const float GapX = 28f, GapY = 30f, CircleSize = 104f;

        public sealed class Entry
        {
            public int Id;
            public Sticker Card;
            public UiButton Button;
            public TextMeshProUGUI Number, Name, Best, Question;
            public Image Circle, Glyph, Glint;
            public Sticker Stamp;
            public Dip Dip;
            public UiGlyph Hero;
            public bool Unlocked, Completed;
        }

        readonly List<Entry> entries = new List<Entry>();
        readonly RectTransform grid;
        readonly TextMeshProUGUI count;
        Vector2 gridSize;
        Entry glinting;
        float glint;
        float fittedWidth = -1f, fittedHeight = -1f;

        public IReadOnlyList<Entry> Entries => entries;
        public UiButton Back { get; }
        public RectTransform Grid => grid;
        public string CountText => count.text;

        public CatalogueMenu(MenuPresenter menu) : base(menu, "Catalogue")
        {
            RectTransform root = Screen.Root;
            TextMeshProUGUI title = UiKit.Label(root, "Title", "THE CATALOGUE", UiFont.Display, UiTheme.TitleSize, UiTheme.Ink, TextAlignmentOptions.Left);
            title.rectTransform.Place(UiKit.TopLeft, new Vector2(96f, -54f), new Vector2(760f, 64f));
            count = UiKit.Label(root, "Count", "", UiFont.Body, UiTheme.BodySize, UiTheme.Ink, TextAlignmentOptions.Right);
            count.rectTransform.Place(UiKit.TopRight, new Vector2(-96f, -66f), new Vector2(460f, 40f));

            grid = UiKit.Rect(root, "Grid");
            Back = Button(root, "Back", ButtonStyle.Paper, new Vector2(200f, 58f), () => Menu.Flow?.CloseLevelSelect());
            Back.RestBorder = UiTheme.Faint;
            Back.Refresh(true);
            Back.Sticker.Rect.Place(UiKit.BottomLeft, new Vector2(96f, 44f), new Vector2(200f, 58f));
            Stick(Back.Sticker);
            Screen.Cancelled = () => Menu.Flow?.CloseLevelSelect();
            Build();
        }

        public Entry Find(int levelId)
        {
            foreach (Entry entry in entries)
                if (entry.Id == levelId) return entry;
            return null;
        }

        void Build()
        {
            IReadOnlyList<int> ids = Menu.Levels.Campaign;
            int rows = Mathf.Max(1, (ids.Count + Columns - 1) / Columns);
            int columns = Mathf.Min(Columns, Mathf.Max(1, ids.Count));
            gridSize = new Vector2(columns * CardSize.x + (columns - 1) * GapX, rows * CardSize.y + (rows - 1) * GapY);
            grid.Place(UiKit.Centre, new Vector2(0f, -8f), gridSize);

            var row = new List<UiControl>();
            for (int i = 0; i < ids.Count; i++)
            {
                Entry entry = BuildCard(ids[i], i);
                entries.Add(entry);
                row.Add(entry.Button);
                if (row.Count == Columns || i == ids.Count - 1)
                {
                    Screen.AddRow(row.ToArray());
                    row.Clear();
                }
            }
            Screen.AddRow(Back);
        }

        Entry BuildCard(int id, int index)
        {
            var entry = new Entry { Id = id };
            string title = "Level " + id, slug = null, environment = null;
            try
            {
                LevelDefinition level = Menu.Levels.Create(id);
                title = level.Title ?? title;
                slug = level.Slug;
                environment = level.Environment;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            entry.Dip = Palette.DipOf(environment);
            entry.Hero = UiAtlas.GlyphFor(slug, id);
            Color ink = entry.Dip.Night ? UiTheme.Paper : UiTheme.Ink;

            entry.Card = Sticker.Create(grid, "Card " + id, StickerShape.Card, entry.Dip.Mid, CardSize);
            int column = index % Columns, line = index / Columns;
            entry.Card.Rect.Place(UiKit.TopLeft, new Vector2(column * (CardSize.x + GapX), -line * (CardSize.y + GapY)), CardSize);
            Stick(entry.Card);
            RectTransform content = entry.Card.Content;

            entry.Number = UiKit.Label(content, "Number", id.ToString("00"), UiFont.Display, UiTheme.NumberSize, ink, TextAlignmentOptions.Left);
            entry.Number.rectTransform.Place(UiKit.Left, new Vector2(20f, 2f), new Vector2(154f, 112f));
            entry.Circle = UiKit.Image(content, "Circle", UiShape.Circle, UiTheme.Paper);
            entry.Circle.rectTransform.Place(UiKit.Right, new Vector2(-20f, 2f), new Vector2(CircleSize, CircleSize));
            entry.Glyph = UiKit.Image(entry.Circle.rectTransform, "Hero", entry.Hero, entry.Dip.Hero);
            entry.Glyph.rectTransform.Place(UiKit.Centre, Vector2.zero, new Vector2(68f, 68f));
            entry.Glint = UiKit.Image(entry.Circle.rectTransform, "Glint", UiShape.FourPane, UiTheme.Paper);
            entry.Glint.rectTransform.Place(UiKit.Centre, Vector2.zero, new Vector2(34f, 34f));
            entry.Glint.gameObject.SetActive(false);
            entry.Name = UiKit.Label(content, "Name", title, UiFont.Body, UiTheme.SmallSize, ink, TextAlignmentOptions.Left);
            entry.Name.rectTransform.Place(UiKit.BottomLeft, new Vector2(20f, 14f), new Vector2(196f, 26f));
            entry.Best = UiKit.Label(content, "Best", "", UiFont.Body, UiTheme.SmallSize, ink, TextAlignmentOptions.Right);
            entry.Best.rectTransform.Place(UiKit.BottomRight, new Vector2(-20f, 14f), new Vector2(64f, 26f));
            entry.Question = UiKit.Label(content, "Locked", "?", UiFont.Display, UiTheme.NumberSize, UiTheme.Ink);
            entry.Question.rectTransform.Place(UiKit.Centre, new Vector2(0f, -6f), new Vector2(140f, 120f));

            var stampSize = new Vector2(138f, 34f);
            entry.Stamp = Sticker.Create(content, "Collected", StickerShape.Panel, UiTheme.Ink, stampSize);
            entry.Stamp.Radius = 9f;
            entry.Stamp.Rect.Place(UiKit.TopRight, UiKit.Centre, new Vector2(-66f, -30f), stampSize);
            entry.Stamp.Rect.localRotation = Quaternion.Euler(0f, 0f, StampSign(id) * StampAngle);
            TextMeshProUGUI stampLabel = UiKit.Label(entry.Stamp.Content, "Label", "COLLECTED", UiFont.Display, 15f, UiTheme.Paper);
            stampLabel.rectTransform.Fill(4f);

            int levelId = id;
            entry.Button = UiButton.On(entry.Card, () => Menu.Flow?.StartLevel(levelId));
            return entry;
        }

        /// <summary>+1 or -1 for a level, the same every time: which way its "COLLECTED" sticker is turned.</summary>
        public static int StampSign(int id)
        {
            uint hash = (uint)id * 2654435761u;
            return (hash >> 13 & 1u) == 0u ? 1 : -1;
        }

        /// <summary>Brings every card up to date with the player's progress.</summary>
        public void Refresh()
        {
            Progress progress = Menu.Progress;
            LevelList levels = Menu.Levels;
            int done = 0;
            foreach (Entry entry in entries)
            {
                entry.Unlocked = progress.IsUnlocked(entry.Id, levels);
                entry.Completed = progress.IsCompleted(entry.Id);
                if (entry.Completed) done++;
                bool open = entry.Unlocked;
                entry.Card.FaceColor = open ? entry.Dip.Mid : UiTheme.Locked;
                entry.Button.Interactable = open;
                entry.Number.gameObject.SetActive(open);
                entry.Circle.gameObject.SetActive(open);
                entry.Name.gameObject.SetActive(open);
                entry.Question.gameObject.SetActive(!open);
                entry.Stamp.Tween.Set(entry.Completed, true);
                float best = entry.Completed ? progress.BestTime(entry.Id) : -1f;
                entry.Best.text = best >= 0f ? UiKit.Time(best, false) : "";
            }
            count.text = done + " of " + entries.Count + " collected";
        }

        protected override void Opening()
        {
            Refresh();
            // The focus starts on the level the player is at.
            Entry at = Menu.Flow != null ? Find(Menu.Flow.LevelId) : null;
            if (at == null || !at.Unlocked)
            {
                at = null;
                foreach (Entry entry in entries)
                    if (entry.Unlocked && at == null) at = entry;
            }
            Screen.First = at != null ? at.Button : Back;
            Screen.Focus(Screen.First);
            glinting = null;
        }

        protected override void Tick(float dt)
        {
            Fit();
            // One glint per arrival of the focus.
            Entry focused = null;
            foreach (Entry entry in entries)
                if (entry.Button.Focused) focused = entry;
            if (focused != glinting)
            {
                if (glinting != null) glinting.Glint.gameObject.SetActive(false);
                glinting = focused;
                glint = 0f;
                if (focused != null && focused.Unlocked && !Settings.ReduceMotion) focused.Glint.gameObject.SetActive(true);
            }
            if (glinting == null || !glinting.Glint.gameObject.activeSelf) return;
            glint += dt;
            float t = glint / GlintTime;
            if (t >= 1f)
            {
                glinting.Glint.gameObject.SetActive(false);
                return;
            }
            float reach = CircleSize * 0.36f;
            glinting.Glint.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-reach, reach, t), Mathf.Lerp(-reach * 0.5f, reach * 0.5f, t));
            glinting.Glint.color = UiTheme.Alpha(UiTheme.Paper, Mathf.Sin(t * Mathf.PI) * 0.9f);
        }

        // The grid is designed for 16:9; on a narrower screen (or with more than three rows) it is scaled to fit.
        void Fit()
        {
            Rect area = Menu.Root.Rect.rect;
            float width = area.width > 1f ? area.width : UiTheme.ReferenceWidth, height = area.height > 1f ? area.height : UiTheme.ReferenceHeight;
            if (width == fittedWidth && height == fittedHeight) return;
            fittedWidth = width;
            fittedHeight = height;
            float scale = Mathf.Min(1f, (width - 120f) / gridSize.x, (height - 290f) / gridSize.y);
            grid.localScale = new Vector3(scale, scale, 1f);
        }
    }

    /// <summary>
    /// Level complete (ART_BIBLE 9.7), about 2.6 s:
    ///
    ///     0 ms  a white flash that fades over 300 ms
    ///   0-240   the scene goes onto a Paper card that shrinks to 86% and tilts 2 degrees (the backdrop does that)
    ///   100     confetti in the level's toy colours
    ///   400     a "COLLECTED" sticker stamps on (scale 1.4 to 1 in 90 ms)
    ///   600     the card shows title, time and grab count, and a Cherry "Next" pill
    ///
    /// "Reduce motion" drops the flash, the tilt and the stamp's punch.
    ///
    /// The card then waits for the player - but not for ever: left alone for <see cref="AutoNextSeconds"/>
    /// it moves on by itself, as the game did before it had menus, with a bar on the Next pill running
    /// down to say so. Anything the player does on the card (moving the focus, by key or by mouse) stops
    /// that, and the card stays until a button is pressed.
    /// </summary>
    public sealed class CompleteMenu : MenuScreen
    {
        public const float FlashTime = 0.3f, ConfettiAt = 0.1f, StampAt = 0.4f, StampTime = 0.09f, StampFrom = 1.4f, InfoAt = 0.6f;
        /// <summary>Seconds the finished card waits untouched before it presses Next itself; 0 to wait for ever.</summary>
        public const float AutoNextSeconds = 5f;
        const float BarWidth = 150f;

        readonly Image flash;
        readonly TextMeshProUGUI title, stats, best;
        readonly RectTransform bar;
        float time, waited;
        bool stamped, informed, burst, held, advanced;
        Confetti confetti;

        /// <summary>True once the player has touched the card: it no longer moves on by itself.</summary>
        public bool Held => held;
        /// <summary>Seconds left before the untouched card moves on (0 when it is held or not counting yet).</summary>
        public float AutoNextIn => informed && !held && !advanced && AutoNextSeconds > 0f ? Mathf.Max(0f, AutoNextSeconds - waited) : 0f;

        public Sticker Stamp { get; }
        public Sticker Info { get; }
        public UiButton Next { get; }
        public UiButton Again { get; }
        public UiButton LevelsButton { get; }
        public float Elapsed => time;
        public string Title => title.text;
        public string Stats => stats.text;
        public bool BestShown => best.gameObject.activeSelf;
        public float FlashAlpha => flash.gameObject.activeSelf ? flash.color.a : 0f;
        public Confetti Confetti => confetti;

        public CompleteMenu(MenuPresenter menu) : base(menu, "Level Complete")
        {
            RectTransform root = Screen.Root;

            var stampSize = new Vector2(420f, 96f);
            Stamp = Sticker.Create(root, "Collected", StickerShape.Panel, UiTheme.Ink, stampSize);
            Stamp.Rect.Place(new Vector2(0.5f, 0.5f), UiKit.Centre, new Vector2(-330f, 250f), stampSize);
            Stamp.Rect.localRotation = Quaternion.Euler(0f, 0f, -CatalogueMenu.StampAngle);
            TextMeshProUGUI stampLabel = UiKit.Label(Stamp.Content, "Label", "COLLECTED", UiFont.Display, UiTheme.TitleSize, UiTheme.Paper);
            stampLabel.rectTransform.Fill(12f);
            Stamp.Tween.Hide(true);

            var infoSize = new Vector2(900f, 214f);
            Info = Sticker.Create(root, "Result", StickerShape.Panel, UiTheme.Paper, infoSize);
            Info.Rect.Place(UiKit.Bottom, new Vector2(0f, 54f), infoSize);
            title = UiKit.Label(Info.Content, "Title", "", UiFont.Display, UiTheme.TitleSize, UiTheme.Ink, TextAlignmentOptions.Left);
            title.rectTransform.Place(UiKit.TopLeft, new Vector2(40f, -22f), new Vector2(560f, 56f));
            stats = UiKit.Label(Info.Content, "Stats", "", UiFont.Body, UiTheme.BodySize, UiTheme.Ink, TextAlignmentOptions.Right);
            stats.rectTransform.Place(UiKit.TopRight, new Vector2(-40f, -22f), new Vector2(270f, 30f));
            best = UiKit.Label(Info.Content, "Best", "New best time", UiFont.Body, UiTheme.SmallSize, UiTheme.Smaller, TextAlignmentOptions.Right);
            best.rectTransform.Place(UiKit.TopRight, new Vector2(-40f, -52f), new Vector2(270f, 26f));

            Next = Button(Info.Content, "Next", ButtonStyle.Primary, new Vector2(300f, 66f), GoOn);
            Next.Sticker.Rect.Place(UiKit.BottomRight, new Vector2(-40f, 30f), new Vector2(300f, 66f));
            Image barImage = UiKit.Image(Next.Sticker.Content, "Countdown", UiShape.White, UiTheme.Alpha(UiTheme.Paper, 0.7f));
            bar = barImage.rectTransform;
            bar.Place(UiKit.Bottom, new Vector2(0f, 9f), new Vector2(BarWidth, 3f));
            Again = Button(Info.Content, "Again", ButtonStyle.Paper, new Vector2(220f, 58f), () => Menu.Flow?.Restart());
            Again.RestBorder = UiTheme.Faint;
            Again.Refresh(true);
            Again.Sticker.Rect.Place(UiKit.BottomLeft, new Vector2(40f, 34f), new Vector2(220f, 58f));
            LevelsButton = Button(Info.Content, "Levels", ButtonStyle.Paper, new Vector2(220f, 58f), () => Menu.Flow?.OpenLevelSelect());
            LevelsButton.RestBorder = UiTheme.Faint;
            LevelsButton.Refresh(true);
            LevelsButton.Sticker.Rect.Place(UiKit.BottomLeft, new Vector2(282f, 34f), new Vector2(220f, 58f));
            Info.Tween.Hide(true);

            // In focus order the primary comes first; on screen it sits at the right.
            Screen.AddRow(Again, LevelsButton, Next);
            Screen.First = Next;

            flash = UiKit.Image(root, "Flash", UiShape.White, UiTheme.Paper);
            flash.rectTransform.Fill();
            flash.gameObject.SetActive(false);
        }

        void GoOn()
        {
            advanced = true;
            Menu.Flow?.NextLevel();
        }

        protected override void Opening()
        {
            time = 0f;
            waited = 0f;
            stamped = false;
            informed = false;
            burst = false;
            held = false;
            advanced = false;
            bar.gameObject.SetActive(AutoNextSeconds > 0f);
            bar.sizeDelta = new Vector2(BarWidth, 3f);
            Stamp.Tween.Hide(true);
            Info.Tween.Hide(true);
            Stamp.Rect.localScale = Vector3.one;

            GameFlow flow = Menu.Flow;
            Game game = Menu.Game;
            LevelDefinition level = flow != null && flow.LastCompletion.Level != null ? flow.LastCompletion.Level : game.Level;
            float seconds = flow != null && flow.LastCompletion.Level != null ? flow.LastCompletion.Time : game.Time;
            title.text = level != null ? (level.Title ?? "").ToUpperInvariant() : "";
            int grabs = Menu.Grabs;
            // (The levels' word: toys are picked up and let go.)
            stats.text = UiKit.Time(seconds) + "  ·  " + grabs + (grabs == 1 ? " pick-up" : " pick-ups");
            best.gameObject.SetActive(flow != null && flow.LastCompletionWasBest && flow.Progress.Completions(flow.LevelId) > 1);

            // The flash of 9.7 is an exposure flash and the post-processing's to show (+1.5 EV in the card's
            // picture, fading over 300 ms). The Paper overlay stands in only where there is none (the
            // plain look) - both at once would be two flashes.
            Toybox.Render.PostLook post = Menu.Context.Presentation != null ? Menu.Context.Presentation.Get<Toybox.Render.PostLook>() : null;
            bool motion = !Settings.ReduceMotion && (post == null || !post.CompleteFlash);
            flash.gameObject.SetActive(motion);
            flash.color = UiTheme.Alpha(UiTheme.Paper, 0.9f);
            flash.rectTransform.SetAsLastSibling();
            // A capture shows the finished card.
            if (UiCapture.Wants("complete")) time = 3f;
        }

        protected override void Closing()
        {
            Stamp.Tween.Hide();
            Info.Tween.Hide();
            flash.gameObject.SetActive(false);
            confetti?.Dispose();
            confetti = null;
        }

        protected override void Tick(float dt)
        {
            time += dt;
            if (flash.gameObject.activeSelf)
            {
                float alpha = 0.9f * (1f - time / FlashTime);
                if (alpha <= 0f) flash.gameObject.SetActive(false);
                else flash.color = UiTheme.Alpha(UiTheme.Paper, alpha);
            }
            if (!burst && time >= ConfettiAt)
            {
                burst = true;
                confetti?.Dispose();
                confetti = Confetti.Burst(Menu.Game, Menu.Context);
            }
            confetti?.Frame(dt);
            if (!stamped && time >= StampAt)
            {
                stamped = true;
                Stamp.Tween.Show(true);
            }
            if (stamped && !Settings.ReduceMotion)
            {
                float k = Mathf.Clamp01((time - StampAt) / StampTime);
                float scale = Mathf.Lerp(StampFrom, 1f, UiTheme.EaseOut(k));
                Stamp.Rect.localScale = new Vector3(scale, scale, 1f);
            }
            if (!informed && time >= InfoAt)
            {
                informed = true;
                Info.Tween.Show();
                Screen.Focus(Next);
                return;
            }
            if (!informed || held || advanced || AutoNextSeconds <= 0f) return;
            // The focus left the Next pill: the player is here and choosing.
            if (Screen.Focused != Next || Menu.Flow == null)
            {
                held = true;
                bar.gameObject.SetActive(false);
                return;
            }
            waited += dt;
            bar.sizeDelta = new Vector2(BarWidth * Mathf.Clamp01(1f - waited / AutoNextSeconds), 3f);
            if (waited < AutoNextSeconds) return;
            advanced = true;
            Screen.Run(GoOn);
        }

        public override void Dispose()
        {
            confetti?.Dispose();
            confetti = null;
        }
    }

    /// <summary>
    /// The confetti of a completed level (ART_BIBLE 9.7): a ParticleSystem built in code, non-instanced
    /// mesh quads with the Flat shader's gloss disc, in the candy colours of the level's toys; the burst
    /// is 60 / 150 / 300 by tier, gravity modifier 0.3, turning about all three axes, lifetime 2.2 s. It
    /// stands in the world in front of the camera, so it is in the picture on the card.
    /// </summary>
    public sealed class Confetti
    {
        public const float Lifetime = 2.2f, GravityModifier = 0.3f;

        static Mesh quad;
        GameObject holder;

        public ParticleSystem System { get; private set; }
        public int Count { get; private set; }

        public static int CountFor(QualityTier tier) => tier == QualityTier.Low ? 60 : tier == QualityTier.High ? 300 : 150;

        /// <summary>Makes what every burst shares (the quad, the material) ahead of the first one.</summary>
        public static void Prewarm()
        {
            Quad();
            Materials.Flat(Color.white, FlatShape.GlossDisc, FlatBlend.Alpha);
        }

        public static Confetti Burst(Game game, PresentationContext context)
        {
            if (!context.HasGraphics || context.Camera == null || game.IsDisposed) return null;
            var confetti = new Confetti();
            confetti.Build(game, context);
            return confetti;
        }

        void Build(Game game, PresentationContext context)
        {
            float scale = game.Player.Scale;
            Transform view = context.Camera.transform;
            holder = new GameObject("Confetti") { hideFlags = HideFlags.DontSave };
            holder.transform.SetParent(context.Root, false);
            // Below the picture and a little ahead, firing up through it.
            holder.transform.SetPositionAndRotation(view.position + view.forward * (2.6f * scale) - view.up * (1.9f * scale), Quaternion.LookRotation(view.up, -view.forward));
            // Set up while it is off, so nothing plays before it is ready.
            holder.SetActive(false);
            System = holder.AddComponent<ParticleSystem>();
            Count = CountFor(context.Quality);

            ParticleSystem.MainModule main = System.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.useUnscaledTime = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Count;
            main.startLifetime = Lifetime;
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f * scale, 6.6f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f * scale, 0.13f * scale);
            main.gravityModifier = GravityModifier;
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Colours(game);

            ParticleSystem.EmissionModule emission = System.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Count) });

            ParticleSystem.ShapeModule shape = System.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 34f;
            shape.radius = 0.5f * scale;

            ParticleSystem.RotationOverLifetimeModule rotation = System.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-9f, 9f);
            rotation.y = new ParticleSystem.MinMaxCurve(-9f, 9f);
            rotation.z = new ParticleSystem.MinMaxCurve(-9f, 9f);

            ParticleSystemRenderer renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Quad();
            renderer.enableGPUInstancing = false;
            renderer.alignment = ParticleSystemRenderSpace.World;
            renderer.sharedMaterial = Materials.Flat(Color.white, FlatShape.GlossDisc, FlatBlend.Alpha);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            holder.SetActive(true);
            System.Play();
        }

        /// <summary>Outside Play Mode nothing advances a particle system: the frame does.</summary>
        public void Frame(float dt)
        {
            if (System == null || Application.isPlaying || dt <= 0f) return;
            System.Simulate(dt, true, false, false);
        }

        public void Dispose()
        {
            UiKit.Destroy(holder);
            holder = null;
            System = null;
        }

        // The candy colours of the level's toys; every candy colour if it has none.
        static ParticleSystem.MinMaxGradient Colours(Game game)
        {
            var colours = new List<Color>();
            foreach (Prop prop in game.Props)
            {
                ToyInfo info = prop.Removed ? null : ToyInfo.Of(prop.GameObject);
                if (info == null || !Palette.IsCandy(info.Candy)) continue;
                bool known = false;
                foreach (Color colour in colours) known |= Palette.Same(colour, info.Candy);
                if (!known && colours.Count < 8) colours.Add(info.Candy);
            }
            if (colours.Count == 0) colours.AddRange(Palette.Candy);

            var keys = new GradientColorKey[Mathf.Min(8, colours.Count)];
            for (int i = 0; i < keys.Length; i++) keys[i] = new GradientColorKey(colours[i], (i + 1f) / keys.Length);
            var gradient = new Gradient { mode = GradientMode.Fixed };
            gradient.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return new ParticleSystem.MinMaxGradient(gradient) { mode = ParticleSystemGradientMode.RandomColor };
        }

        static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "Confetti Quad", hideFlags = HideFlags.HideAndDontSave };
            quad.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
            quad.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            quad.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            quad.RecalculateBounds();
            return quad;
        }
    }
}
