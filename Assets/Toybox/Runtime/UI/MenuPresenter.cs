using System;
using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Platform;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Toybox.UI
{
    /// <summary>
    /// The game's menus (ART_BIBLE 10.5, 9.7): the title, the pause card with its settings, the catalogue
    /// of levels and the level-complete card, on one canvas above the HUD. Which of them is up follows the
    /// <see cref="GameFlow"/>; their buttons call the flow, and nothing else.
    ///
    /// What a button does is not done on the spot but queued and run in <see cref="Frame"/>, which the
    /// runner calls after it has read the keys of the frame - so Esc closing the catalogue is not also the
    /// Esc that resumes the game behind it - and, when the key that pressed the button is Space or Enter,
    /// only once that key is up again: Space is also the jump.
    ///
    /// It runs before the HUD's presenter, so that what a button did is what both of them show that frame.
    ///
    /// Tests and tools drive it without an EventSystem: <see cref="Move"/>, <see cref="Submit"/>,
    /// <see cref="Cancel"/> are what the arrow keys, Enter / Space and Esc do, and every control has Activate.
    /// </summary>
    [Presenter(500, ProvidesHud = true)]
    public sealed class MenuPresenter : IPresenter
    {
        /// <summary>Longest a queued action waits for the key that started it to come up.</summary>
        public const float SubmitHoldLimit = 0.5f;

        readonly List<Action> pending = new List<Action>();
        readonly List<MenuScreen> screens = new List<MenuScreen>();
        Game game;
        PresentationContext context;
        GameFlow flow;
        string capture;
        UiInput input;
        MenuScreen current;
        LevelList ownLevels;
        Progress ownProgress;
        bool settingsOpen, previousAutoAdvance, built, disposed;
        float pendingAge;

        public UiRoot Root { get; private set; }
        public MenuBackdrop Backdrop { get; private set; }
        public TitleStage Stage { get; private set; }
        public TitleMenu Title { get; private set; }
        public PauseMenu Pause { get; private set; }
        public SettingsMenu SettingsCard { get; private set; }
        public CatalogueMenu Catalogue { get; private set; }
        public CompleteMenu Complete { get; private set; }

        public Game Game => game;
        public PresentationContext Context => context;
        /// <summary>The flow the buttons call, or null where there is none (the screenshot tool).</summary>
        public GameFlow Flow => flow;
        public LevelList Levels => flow != null ? flow.Levels : ownLevels ??= capture == "catalogue" ? UiCapture.DemoLevels() : LevelList.FromRegistry();
        public Progress Progress => flow != null ? flow.Progress : ownProgress ??= capture == "catalogue" ? UiCapture.DemoProgress() : new Progress(new MemoryStore());
        /// <summary>The menu that is up, or null while the game is being played.</summary>
        public MenuScreen Current => current;
        /// <summary>The screen the keyboard talks to.</summary>
        public UiScreen Active => current != null ? current.Screen : null;
        /// <summary>How often the player grabbed a toy since the level was loaded.</summary>
        public int Grabs { get; private set; }
        public int PendingActions => pending.Count;
        public bool SettingsOpen => settingsOpen;
        /// <summary>The EventSystem's side of things; null outside Play Mode.</summary>
        public UiInput Input => input;

        public void Attach(Game attachedGame, PresentationContext presentationContext)
        {
            game = attachedGame;
            context = presentationContext;
            flow = context.Flow;
            capture = UiCapture.Active ? UiCapture.Request : null;
            game.Events.LevelLoaded += OnLevelLoaded;
            game.Events.PropGrabbed += OnPropGrabbed;
            if (flow != null)
            {
                // The level-complete card has a Next button: the flow waits for it.
                previousAutoAdvance = flow.AutoAdvance;
                flow.AutoAdvance = false;
            }
            if (!UiFonts.Available)
            {
                Debug.LogWarning("[Toybox] The menus are not shown: TextMeshPro's resources are missing. Run Toybox.EditorTools.ProjectSetup.Run. A click or Enter starts and resumes the game.");
                return;
            }

            Root = UiRoot.Create(context, "Menus", 20, true);
            Backdrop = new MenuBackdrop(context, Root);
            Stage = new TitleStage(game, context);
            input = UiInput.Create(context.Root);
            Title = Add(new TitleMenu(this));
            Pause = Add(new PauseMenu(this));
            SettingsCard = Add(new SettingsMenu(this));
            Catalogue = Add(new CatalogueMenu(this));
            Complete = Add(new CompleteMenu(this));
            Confetti.Prewarm();
            settingsOpen = capture == "settings";
            built = true;
            // No frame yet: whoever drives the game presents once it has put the flow where it starts.
        }

        /// <summary>Queues what a button does; it runs in the next <see cref="Frame"/>.</summary>
        public void Queue(Action action)
        {
            if (action != null) pending.Add(action);
        }

        /// <summary>An arrow key: moves the focus of the menu that is up.</summary>
        public bool Move(MoveDirection direction) => Active != null && Active.Move(direction);

        /// <summary>Enter or Space: activates what has the focus.</summary>
        public void Submit()
        {
            if (Active != null) Active.Submit();
        }

        /// <summary>Esc, where the menu itself handles it (the catalogue, the settings).</summary>
        public void Cancel()
        {
            if (Active != null) Active.Cancel();
        }

        public void OpenSettings() => settingsOpen = true;

        public void CloseSettings()
        {
            if (!settingsOpen) return;
            settingsOpen = false;
            Settings.Save();
        }

        public void Frame(float dt, float alpha)
        {
            if (disposed || game == null || game.IsDisposed) return;
            if (!built)
            {
                Unbuilt();
                return;
            }
            RunPending(dt);
            if (game.IsDisposed) return;

            FlowState state = UiCapture.StateFor(context, capture);
            if (state != FlowState.Title && state != FlowState.Paused && settingsOpen) CloseSettings();
            // With the settings over the pause card, Esc goes back to the pause card, not on to the game.
            context.EscapeClaimed = settingsOpen && state == FlowState.Paused;
            MenuScreen wanted = Wanted(state);
            if (wanted != current) Switch(wanted);
            SyncWorld(state);

            bool busy = false;
            foreach (MenuScreen screen in screens)
            {
                screen.Frame(dt);
                busy |= screen.Busy;
            }
            Root.Visible = busy;
            Backdrop.Frame(dt);
            Stage.Frame(dt);
            KeepSelection();
            Root.Advance(dt);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (game != null)
            {
                game.Events.LevelLoaded -= OnLevelLoaded;
                game.Events.PropGrabbed -= OnPropGrabbed;
            }
            if (flow != null) flow.AutoAdvance = previousAutoAdvance;
            if (context != null) context.EscapeClaimed = false;
            if (settingsOpen) Settings.Save();
            pending.Clear();
            foreach (MenuScreen screen in screens) screen.Dispose();
            screens.Clear();
            current = null;
            Stage?.Dispose();
            Backdrop?.Dispose();
            input?.Dispose();
            input = null;
            if (Root != null)
            {
                Root.Unhook();
                UiKit.Destroy(Root.gameObject);
            }
            Root = null;
            built = false;
        }

        MenuScreen Wanted(FlowState state)
        {
            switch (state)
            {
                case FlowState.Title: return settingsOpen ? (MenuScreen)SettingsCard : Title;
                case FlowState.Paused: return settingsOpen ? (MenuScreen)SettingsCard : Pause;
                case FlowState.LevelSelect: return Catalogue;
                case FlowState.LevelComplete: return Complete;
                default: return null;
            }
        }

        // Every menu is made up front, while its root is active (so each label has its mesh), and then put
        // away: opening one later creates nothing - no hitch at the first pause or in the middle of a
        // celebration, and nothing that looks like a leak to whoever counts objects.
        T Add<T>(T screen) where T : MenuScreen
        {
            screens.Add(screen);
            screen.Screen.Hide();
            return screen;
        }

        void Switch(MenuScreen next)
        {
            if (current != null) current.Close(false);
            current = next;
            if (next != null) next.Open();
        }

        // What the world does behind the menu that is up.
        void SyncWorld(FlowState state)
        {
            switch (state)
            {
                case FlowState.Paused:
                    Backdrop.Set(BackdropMode.Frozen, UiTheme.Paper);
                    break;
                case FlowState.LevelSelect:
                    Backdrop.Set(BackdropMode.Surface, UiTheme.Paper);
                    break;
                case FlowState.LevelComplete:
                    Backdrop.Set(BackdropMode.Live, LevelDip.Light);
                    break;
                default:
                    Backdrop.Set(BackdropMode.None, UiTheme.Paper);
                    break;
            }
            if (state == FlowState.Title) Stage.Enter();
            else if (state != FlowState.LevelSelect) Stage.Leave();
        }

        /// <summary>The dip of the level that is loaded (Mint without one).</summary>
        public Dip LevelDip => Palette.DipOf(game != null && game.Level != null ? game.Level.Environment : null);

        void RunPending(float dt)
        {
            if (pending.Count == 0)
            {
                pendingAge = 0f;
                return;
            }
            if (input != null && input.SubmitHeld && pendingAge < SubmitHoldLimit)
            {
                pendingAge += dt;
                return;
            }
            pendingAge = 0f;
            // Only what was queued before this frame: an action may queue another.
            int count = pending.Count;
            for (int i = 0; i < count && !game.IsDisposed; i++)
            {
                try
                {
                    pending[i]();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            pending.RemoveRange(0, Mathf.Min(count, pending.Count));
        }

        // The EventSystem's selection is what the keyboard talks to: on the menu that is up, on nothing while playing.
        void KeepSelection()
        {
            if (input == null || input.EventSystem == null) return;
            EventSystem system = input.EventSystem;
            UiScreen active = Active;
            if (active == null || !active.Enabled)
            {
                if (system.currentSelectedGameObject != null && !system.alreadySelecting) system.SetSelectedGameObject(null);
                return;
            }
            UiControl focus = active.Focused != null ? active.Focused : active.First;
            if (focus != null && system.currentSelectedGameObject != focus.gameObject) active.Focus(focus);
        }

        // Without TextMeshPro's resources there is nothing to show; the game must still be startable.
        void Unbuilt()
        {
            if (flow == null || !Application.isPlaying) return;
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;
            bool go = (mouse != null && mouse.leftButton.wasPressedThisFrame) || (keyboard != null && keyboard.enterKey.wasPressedThisFrame);
            if (!go) return;
            if (flow.State == FlowState.Title) flow.StartLevel(flow.LevelId);
            else if (flow.State == FlowState.Paused) flow.Resume();
            else if (flow.State == FlowState.LevelComplete) flow.NextLevel();
            else if (flow.State == FlowState.LevelSelect) flow.CloseLevelSelect();
        }

        void OnLevelLoaded(LevelEvent e)
        {
            Grabs = 0;
            if (!built) return;
            Pause?.ResetHints();
            // The level behind the title changed (or only now arrived): the turntable stands in it.
            Stage.LevelLoaded(UiCapture.StateFor(context, capture) == FlowState.Title);
        }

        void OnPropGrabbed(PropHoldEvent e)
        {
            if (!context.Autoplay) Grabs++;
        }
    }

    /// <summary>
    /// One menu: a <see cref="UiScreen"/> and the stickers that stick on when it opens and peel when it
    /// closes. A closed menu stays in the hierarchy, inactive, until it is wanted again.
    /// </summary>
    public abstract class MenuScreen
    {
        protected readonly MenuPresenter Menu;
        protected readonly List<Sticker> Stickers = new List<Sticker>();
        float closing;

        public UiScreen Screen { get; }
        public bool IsOpen { get; private set; }
        /// <summary>Open, or still peeling away.</summary>
        public bool Busy => IsOpen || closing > 0f;

        protected MenuScreen(MenuPresenter menu, string name)
        {
            Menu = menu;
            // Left active while the menu is being built; the presenter puts it away afterwards.
            Screen = new UiScreen(menu.Root.Rect, name) { Run = menu.Queue, Enabled = false };
        }

        public void Open()
        {
            IsOpen = true;
            closing = 0f;
            Screen.Enabled = true;
            Screen.Root.gameObject.SetActive(true);
            Screen.Root.SetAsLastSibling();
            Opening();
            foreach (Sticker sticker in Stickers)
            {
                sticker.Tween.Hide(true);
                sticker.Tween.Show();
            }
            Screen.Show();
        }

        public void Close(bool instant)
        {
            if (!IsOpen && closing <= 0f) return;
            IsOpen = false;
            Screen.Enabled = false;
            Closing();
            foreach (Sticker sticker in Stickers) sticker.Tween.Hide(instant);
            closing = instant ? 0f : UiTheme.Medium + 0.02f;
            if (instant) Screen.Hide();
        }

        public void Frame(float dt)
        {
            if (IsOpen)
            {
                Tick(dt);
                return;
            }
            if (closing <= 0f) return;
            closing -= dt;
            if (closing <= 0f) Screen.Hide();
        }

        /// <summary>Right before the stickers stick on: bring the contents up to date.</summary>
        protected virtual void Opening() { }
        protected virtual void Closing() { }
        /// <summary>Every frame while the menu is open.</summary>
        protected virtual void Tick(float dt) { }
        public virtual void Dispose() { }

        /// <summary>A sticker that sticks on with the menu and peels with it.</summary>
        protected Sticker Stick(Sticker sticker)
        {
            Stickers.Add(sticker);
            return sticker;
        }

        protected UiButton Button(Transform parent, string text, ButtonStyle style, Vector2 size, Action clicked) =>
            UiButton.Create(parent, text, style, size, clicked);
    }
}
