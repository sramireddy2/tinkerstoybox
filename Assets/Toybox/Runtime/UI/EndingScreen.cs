using System;
using System.Collections.Generic;
using TMPro;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Platform;
using UnityEngine;
using UnityEngine.UI;

namespace Toybox.UI
{
    /// <summary>
    /// The end of the game (FlowState.Ending): the catalogue, full. It is what the last level's complete
    /// card leads to - the campaign does not wrap round to its first level. In the menus' own language
    /// (ART_BIBLE 10): a Paper surface like the catalogue's, the campaign's blister cards with their hero
    /// toys, a "COLLECTED" stamp, and one result card.
    ///
    ///     0 ms  "THE END" and the count ("15 OF 15 COLLECTED") stick on
    ///   250     the cards stick on in campaign order, one every 60 ms; a level that was not collected
    ///           is a Kraft card with a "?", as in the catalogue
    ///   +200    every level collected: "COLLECTED" stamps on (scale 1.4 to 1 in 90 ms) with the level-complete
    ///           flourish, and paper confetti in the candy colours falls across the screen
    ///   +300    the result card: a thank-you line, the total time and the pick-ups of the campaign, and
    ///           Play again (Cherry) / Levels / Title
    ///
    /// "Reduce motion" drops the stamp's punch and the confetti. The numbers are the player's saved
    /// progress (<see cref="GameFlow.Report"/>): the best run of every level, however many sessions it took
    /// ("Best runs": it is not the time spent playing). While a bot plays they are the bot's runs of this
    /// session, the card says so, and nothing is stamped. That card tells the watcher to press P: when
    /// they do (the bot lets go), the screen does what it promised and starts the first level for them.
    ///
    /// Coming back from the catalogue (Levels, then Back) the screen is as it was left: everything up,
    /// no second build-up, fanfare or confetti.
    ///
    /// Play again starts the campaign's first level; Levels opens the catalogue (Back returns here); Title
    /// and Esc go back to the title, which then stands over the first level. Nothing here moves on by itself.
    /// </summary>
    public sealed class EndingMenu : MenuScreen
    {
        public const int Columns = 5;
        public const float CardsAt = 0.25f, CardEvery = 0.06f, StampAfter = 0.2f, InfoAfter = 0.3f;
        public const float StampTime = CompleteMenu.StampTime, StampFrom = CompleteMenu.StampFrom;
        public const int ConfettiCount = 54;
        public const float ConfettiLife = 2.6f;
        public const string Heading = "THE END", Thanks = "THANK YOU FOR PLAYING", WatchedThanks = "THE BOT PLAYED IT THROUGH";
        public const string FullLine = "Every toy is back in its box.";
        public const string WatchedLine = "None of it counts as yours. Press P and the toys are in your hands.";
        /// <summary>What the two figures of the result card are: the player's best runs added up, or the bot's runs of this session.</summary>
        public const string BestLabel = "Best runs ", WatchedLabel = "Total time ";

        static readonly Vector2 CardSize = new Vector2(240f, 150f);
        const float Gap = 20f, CircleSize = 92f, Top = 462f, GridTop = Top - 168f, RoomForGrid = 490f;

        public sealed class Entry
        {
            public int Id;
            public Sticker Card;
            public TextMeshProUGUI Number, Question;
            public Image Circle, Glyph;
            public Dip Dip;
            public UiGlyph Hero;
            public bool Collected;
        }

        struct Piece
        {
            public RectTransform Rect;
            public Image Image;
            public Vector2 Position, Velocity;
            public float Angle, Spin, Delay;
        }

        readonly List<Entry> entries = new List<Entry>();
        readonly RectTransform grid, confettiRoot;
        readonly TextMeshProUGUI heading, count, thanks, line, stats;
        readonly Piece[] pieces = new Piece[ConfettiCount];
        Vector2 gridSize;
        float time, stampAt, infoAt, confettiTime = -1f;
        int shown;
        bool stamped, informed, watching, handedOver;

        public IReadOnlyList<Entry> Entries => entries;
        public RectTransform Grid => grid;
        public Sticker Head { get; }
        public Sticker Stamp { get; }
        public Sticker Info { get; }
        public UiButton Again { get; }
        public UiButton LevelsButton { get; }
        public UiButton TitleButton { get; }
        /// <summary>What the screen is telling: the campaign as it stood when the screen opened.</summary>
        public CampaignReport Report { get; private set; }
        public float Elapsed => time;
        public string HeadingText => heading.text;
        public string CountText => count.text;
        public string ThanksText => thanks.text;
        public string LineText => line.text;
        public string StatsText => stats.text;
        /// <summary>How many of the cards have stuck on so far.</summary>
        public int CardsShown => shown;
        /// <summary>True once the result card with its buttons is up.</summary>
        public bool Informed => informed;
        /// <summary>True while paper confetti is falling.</summary>
        public bool ConfettiFalling => confettiTime >= 0f;
        /// <summary>Seconds from the opening to the result card, for this many cards.</summary>
        public float InfoAt => infoAt;

        public EndingMenu(MenuPresenter menu) : base(menu, "Ending")
        {
            RectTransform root = Screen.Root;

            var headSize = new Vector2(1280f, 150f);
            Head = Stick(Sticker.Create(root, "Heading", StickerShape.Panel, Color.clear, headSize));
            Head.Rect.Place(UiKit.Centre, UiKit.Top, new Vector2(0f, Top), headSize);
            Head.Shadow.enabled = false;
            Head.Border.enabled = false;
            Head.Face.enabled = false;
            heading = UiKit.Label(Head.Content, "The End", Heading, UiFont.Display, UiTheme.NumberSize, UiTheme.Ink, sticker: true);
            heading.rectTransform.Place(UiKit.Top, new Vector2(0f, 0f), new Vector2(headSize.x, 108f));
            count = UiKit.Label(Head.Content, "Count", "", UiFont.Display, UiTheme.ButtonSize, UiTheme.Ink);
            count.rectTransform.Place(UiKit.Top, new Vector2(0f, -110f), new Vector2(headSize.x, 36f));

            grid = UiKit.Rect(root, "Grid");
            BuildCards();

            var stampSize = new Vector2(420f, 96f);
            Stamp = Sticker.Create(root, "Collected", StickerShape.Panel, UiTheme.Ink, stampSize);
            Stamp.Rect.Place(UiKit.Centre, UiKit.Centre, new Vector2(500f, Top - 94f), stampSize);
            Stamp.Rect.localRotation = Quaternion.Euler(0f, 0f, -CatalogueMenu.StampAngle);
            TextMeshProUGUI stampLabel = UiKit.Label(Stamp.Content, "Label", "COLLECTED", UiFont.Display, UiTheme.TitleSize, UiTheme.Paper);
            stampLabel.rectTransform.Fill(12f);
            Stamp.Tween.Hide(true);

            var infoSize = new Vector2(1000f, 200f);
            Info = Sticker.Create(root, "Result", StickerShape.Panel, UiTheme.Paper, infoSize);
            Info.Rect.Place(UiKit.Centre, UiKit.Top, new Vector2(0f, GridTop - RoomForGrid - 24f), infoSize);
            // A Paper card on a Paper surface: the border is what sets it off (as with the Paper pills on a Paper card).
            Info.BorderColor = UiTheme.Faint;
            thanks = UiKit.Label(Info.Content, "Thanks", Thanks, UiFont.Display, UiTheme.ButtonSize, UiTheme.Ink, TextAlignmentOptions.Left);
            thanks.rectTransform.Place(UiKit.TopLeft, new Vector2(40f, -24f), new Vector2(500f, 36f));
            line = UiKit.Label(Info.Content, "Line", FullLine, UiFont.Body, UiTheme.BodySize, UiTheme.Ink, TextAlignmentOptions.Left);
            line.rectTransform.Place(UiKit.TopLeft, new Vector2(40f, -62f), new Vector2(920f, 30f));
            stats = UiKit.Label(Info.Content, "Stats", "", UiFont.Body, UiTheme.BodySize, UiTheme.Ink, TextAlignmentOptions.Right);
            stats.rectTransform.Place(UiKit.TopRight, new Vector2(-40f, -28f), new Vector2(420f, 30f));

            Again = Button(Info.Content, "Play again", ButtonStyle.Primary, new Vector2(300f, 66f), PlayAgain);
            Again.Sticker.Rect.Place(UiKit.BottomRight, new Vector2(-40f, 26f), new Vector2(300f, 66f));
            TitleButton = Button(Info.Content, "Title", ButtonStyle.Paper, new Vector2(220f, 58f), ToTitle);
            TitleButton.RestBorder = UiTheme.Faint;
            TitleButton.Refresh(true);
            TitleButton.Sticker.Rect.Place(UiKit.BottomLeft, new Vector2(40f, 30f), new Vector2(220f, 58f));
            LevelsButton = Button(Info.Content, "Levels", ButtonStyle.Paper, new Vector2(220f, 58f), () => Menu.Flow?.OpenLevelSelect());
            LevelsButton.RestBorder = UiTheme.Faint;
            LevelsButton.Refresh(true);
            LevelsButton.Sticker.Rect.Place(UiKit.BottomLeft, new Vector2(282f, 30f), new Vector2(220f, 58f));
            Info.Tween.Hide(true);

            // In focus order the primary comes first; on screen it sits at the right (as on the complete card).
            Screen.AddRow(TitleButton, LevelsButton, Again);
            Screen.First = Again;
            Screen.Cancelled = ToTitle;

            // Made up front like everything else of the menus: opening the ending creates nothing.
            confettiRoot = UiKit.Rect(root, "Confetti");
            confettiRoot.Fill();
            for (int i = 0; i < pieces.Length; i++)
            {
                Image image = UiKit.Image(confettiRoot, "Piece", UiShape.Panel, Palette.Candy[i % Palette.Candy.Length]);
                pieces[i] = new Piece { Rect = image.rectTransform, Image = image };
                image.gameObject.SetActive(false);
            }
            confettiRoot.gameObject.SetActive(false);
        }

        public Entry Find(int levelId)
        {
            foreach (Entry entry in entries)
                if (entry.Id == levelId) return entry;
            return null;
        }

        void PlayAgain()
        {
            GameFlow flow = Menu.Flow;
            if (flow != null && flow.State == FlowState.Ending) flow.StartLevel(flow.Levels.First);
        }

        void ToTitle()
        {
            GameFlow flow = Menu.Flow;
            if (flow != null && flow.State == FlowState.Ending) flow.ReturnToTitle();
        }

        void BuildCards()
        {
            IReadOnlyList<int> ids = Menu.Levels.Campaign;
            int columns = Mathf.Min(Columns, Mathf.Max(1, ids.Count));
            int rows = Mathf.Max(1, (ids.Count + Columns - 1) / Columns);
            gridSize = new Vector2(columns * CardSize.x + (columns - 1) * Gap, rows * CardSize.y + (rows - 1) * Gap);
            grid.Place(UiKit.Centre, UiKit.Top, new Vector2(0f, GridTop), gridSize);
            // More than three rows (a campaign that grew): the grid is scaled into the room it has.
            float scale = Mathf.Min(1f, RoomForGrid / gridSize.y);
            grid.localScale = new Vector3(scale, scale, 1f);

            for (int i = 0; i < ids.Count; i++)
            {
                int id = ids[i];
                var entry = new Entry { Id = id };
                string slug = null, environment = null;
                try
                {
                    LevelDefinition level = Menu.Levels.Create(id);
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
                int column = i % Columns, row = i / Columns;
                entry.Card.Rect.Place(UiKit.TopLeft, new Vector2(column * (CardSize.x + Gap), -row * (CardSize.y + Gap)), CardSize);
                RectTransform content = entry.Card.Content;
                entry.Number = UiKit.Label(content, "Number", id.ToString("00"), UiFont.Display, UiTheme.TitleSize, ink, TextAlignmentOptions.Left);
                entry.Number.rectTransform.Place(UiKit.Left, new Vector2(18f, -8f), new Vector2(100f, 60f));
                entry.Circle = UiKit.Image(content, "Circle", UiShape.Circle, UiTheme.Paper);
                entry.Circle.rectTransform.Place(UiKit.Right, new Vector2(-18f, -8f), new Vector2(CircleSize, CircleSize));
                entry.Glyph = UiKit.Image(entry.Circle.rectTransform, "Hero", entry.Hero, entry.Dip.Hero);
                entry.Glyph.rectTransform.Place(UiKit.Centre, Vector2.zero, new Vector2(60f, 60f));
                entry.Question = UiKit.Label(content, "Missing", "?", UiFont.Display, UiTheme.TitleSize, UiTheme.Ink);
                entry.Question.rectTransform.Place(UiKit.Centre, new Vector2(0f, -8f), new Vector2(120f, 70f));
                entry.Card.Tween.Hide(true);
                entries.Add(entry);
            }
        }

        /// <summary>Brings the cards and the words up to date with the campaign as it stands.</summary>
        public void Refresh()
        {
            GameFlow flow = Menu.Flow;
            Report = flow != null ? flow.Report() : GameFlow.ReportOf(Menu.Levels, Menu.Progress);
            CampaignReport report = Report;
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                entry.Collected = i < report.Total && report.Collected[i];
                entry.Card.FaceColor = entry.Collected ? entry.Dip.Mid : UiTheme.Locked;
                entry.Number.gameObject.SetActive(entry.Collected);
                entry.Circle.gameObject.SetActive(entry.Collected);
                entry.Question.gameObject.SetActive(!entry.Collected);
            }
            count.text = report.CollectedCount + " OF " + report.Total + (report.Watched ? " PLAYED" : " COLLECTED");
            thanks.text = report.Watched ? WatchedThanks : Thanks;
            int missing = report.Total - report.CollectedCount;
            line.text = report.Watched ? WatchedLine
                : report.Full ? FullLine
                : missing == 1 ? "One toy is still waiting in the catalogue." : missing + " toys are still waiting in the catalogue.";
            stats.text = report.CollectedCount == 0 ? ""
                : (report.Watched ? WatchedLabel : BestLabel) + UiKit.Time(report.Seconds) + "  ·  " + report.PickUps + (report.PickUps == 1 ? " pick-up" : " pick-ups");
        }

        void SetLive(bool live)
        {
            Again.Interactable = live;
            LevelsButton.Interactable = live;
            TitleButton.Interactable = live;
        }

        protected override void Opening()
        {
            // Back from the catalogue: the same ending, already told.
            bool back = Menu.CameFrom == Menu.Catalogue && Menu.CameFrom != null;
            Refresh();
            time = 0f;
            shown = 0;
            stamped = false;
            informed = false;
            watching = Menu.Context.Autoplay;
            handedOver = false;
            confettiTime = -1f;
            foreach (Entry entry in entries) entry.Card.Tween.Hide(true);
            Stamp.Tween.Hide(true);
            Stamp.Rect.localScale = Vector3.one;
            Info.Tween.Hide(true);
            confettiRoot.gameObject.SetActive(false);
            // Nothing can be pressed before it can be seen: a key held over from the last card must not skip the ending.
            SetLive(false);
            stampAt = CardsAt + entries.Count * CardEvery + StampAfter;
            infoAt = stampAt + InfoAfter;
            Screen.First = Again;
            // A capture shows the finished screen.
            if (UiCapture.Wants("ending")) time = infoAt + 1f;
            if (!back) return;

            time = infoAt + 1f;
            shown = entries.Count;
            foreach (Entry entry in entries) entry.Card.Tween.Show(true);
            stamped = true;
            if (Report.Full && !Report.Watched)
            {
                Stamp.Tween.Show(true);
                Stamp.Rect.SetAsLastSibling();
            }
            informed = true;
            Info.Tween.Show(true);
            SetLive(true);
        }

        protected override void Closing()
        {
            foreach (Entry entry in entries) entry.Card.Tween.Hide();
            Stamp.Tween.Hide();
            Info.Tween.Hide();
            confettiTime = -1f;
            confettiRoot.gameObject.SetActive(false);
        }

        protected override void Tick(float dt)
        {
            time += dt;
            while (shown < entries.Count && time >= CardsAt + shown * CardEvery)
            {
                entries[shown].Card.Tween.Show();
                shown++;
            }
            bool celebrate = Report.Full && !Report.Watched;
            if (!stamped && time >= stampAt)
            {
                stamped = true;
                if (celebrate)
                {
                    Stamp.Tween.Show(true);
                    Stamp.Rect.SetAsLastSibling();
                    UiSounds.Fanfare();
                    if (!Settings.ReduceMotion) Burst();
                }
            }
            if (stamped && celebrate && !Settings.ReduceMotion)
            {
                float k = Mathf.Clamp01((time - stampAt) / StampTime);
                float scale = Mathf.Lerp(StampFrom, 1f, UiTheme.EaseOut(k));
                Stamp.Rect.localScale = new Vector3(scale, scale, 1f);
            }
            if (!informed && time >= infoAt)
            {
                informed = true;
                Info.Tween.Show();
                SetLive(true);
                Screen.Focus(Again);
            }
            Fall(dt);

            // The bot's ending says "Press P and the toys are in your hands". P takes the game from the bot;
            // this is the other half: the first level, for the player. (Only when the bot was at it while
            // this screen was up - a player who took a level over from the bot and finished it sees the
            // ending they came to.)
            if (Menu.Context.Autoplay) watching = true;
            else if (watching && !handedOver && Report.Watched && Menu.Flow != null)
            {
                handedOver = true;
                Screen.Run(PlayAgain);
            }
        }

        // ---- Paper confetti, on the canvas (the world's camera is off behind a surface) ----------------------

        // The same every time: a little spread worked out from the piece's number.
        static float Spread(int i, float seed)
        {
            float x = Mathf.Sin(i * 12.9898f + seed * 78.233f) * 43758.5453f;
            return x - Mathf.Floor(x);
        }

        void Burst()
        {
            Rect area = Menu.Root.Rect.rect;
            float width = area.width > 1f ? area.width : UiTheme.ReferenceWidth, height = area.height > 1f ? area.height : UiTheme.ReferenceHeight;
            for (int i = 0; i < pieces.Length; i++)
            {
                Piece piece = pieces[i];
                float size = Mathf.Lerp(12f, 24f, Spread(i, 1f));
                piece.Rect.Place(UiKit.Centre, UiKit.Centre, Vector2.zero, new Vector2(size, size * Mathf.Lerp(0.5f, 1f, Spread(i, 2f))));
                piece.Position = new Vector2((Spread(i, 3f) - 0.5f) * width, height * 0.5f + 30f + Spread(i, 4f) * 260f);
                piece.Velocity = new Vector2((Spread(i, 5f) - 0.5f) * 220f, -Mathf.Lerp(260f, 520f, Spread(i, 6f)));
                piece.Angle = Spread(i, 7f) * 360f;
                piece.Spin = (Spread(i, 8f) - 0.5f) * 720f;
                piece.Delay = Spread(i, 9f) * 0.5f;
                piece.Image.color = Palette.Candy[i % Palette.Candy.Length];
                piece.Image.gameObject.SetActive(true);
                pieces[i] = piece;
            }
            confettiRoot.gameObject.SetActive(true);
            confettiRoot.SetAsLastSibling();
            confettiTime = 0f;
            Fall(0f);
        }

        void Fall(float dt)
        {
            if (confettiTime < 0f) return;
            confettiTime += dt;
            if (confettiTime >= ConfettiLife + 0.5f)
            {
                confettiTime = -1f;
                confettiRoot.gameObject.SetActive(false);
                return;
            }
            for (int i = 0; i < pieces.Length; i++)
            {
                Piece piece = pieces[i];
                float t = confettiTime - piece.Delay;
                if (t > 0f && dt > 0f)
                {
                    piece.Velocity.y -= 180f * dt;
                    piece.Position += piece.Velocity * dt;
                    piece.Angle += piece.Spin * dt;
                    pieces[i] = piece;
                }
                piece.Rect.anchoredPosition = piece.Position;
                piece.Rect.localRotation = Quaternion.Euler(0f, 0f, piece.Angle);
                piece.Image.color = UiTheme.Alpha(piece.Image.color, Mathf.Clamp01((ConfettiLife - t) / 0.4f));
            }
        }
    }
}
