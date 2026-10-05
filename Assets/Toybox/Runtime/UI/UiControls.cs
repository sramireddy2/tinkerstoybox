using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Toybox.UI
{
    /// <summary>
    /// The menus' two sounds (ART_BIBLE 11.3 "UI hover / click"), played through the audio presenter if
    /// there is one. Nothing sounds before the first gesture or outside a game.
    /// </summary>
    static class UiSounds
    {
        const float HoverGap = 0.04f;
        static float lastHover = float.NegativeInfinity;
        // Whose tick that was. The gap is between two ticks of one game: the first tick of another game (a
        // new runner; the next test, which may well run in the same editor frame) is never swallowed by
        // the last tick of the one before it.
        static readonly WeakReference<Toybox.Audio.AudioPresenter> lastListener = new WeakReference<Toybox.Audio.AudioPresenter>(null);

        /// <summary>The focus moved to another control, or a slider moved a step.</summary>
        public static void Hover()
        {
            Toybox.Audio.AudioPresenter audio = Toybox.Audio.AudioPresenter.Active;
            if (audio == null) return;
            // A dragged slider moves every frame; one tick per 40 ms is a purr, more is a buzz.
            float now = Time.unscaledTime;
            bool same = lastListener.TryGetTarget(out Toybox.Audio.AudioPresenter last) && ReferenceEquals(last, audio);
            if (same && now >= lastHover && now - lastHover < HoverGap) return;
            lastHover = now;
            lastListener.SetTarget(audio);
            audio.PlayUi(Toybox.Audio.UiSound.Hover);
        }

        /// <summary>A button was pressed, a switch thrown, an option taken.</summary>
        public static void Click() => Toybox.Audio.AudioPresenter.Active?.PlayUi(Toybox.Audio.UiSound.Click);
    }

    /// <summary>
    /// Something on a menu that can have the focus and be activated: a button, a slider, a switch, a
    /// choice. The mouse and the keyboard meet here. Pointer events arrive from the EventSystem (through
    /// the Input System UI module); Move, Submit and Cancel arrive the same way for whatever the
    /// EventSystem has selected, and are handed to the <see cref="UiScreen"/>, which keeps the focus - so a
    /// test (or anything else without an EventSystem) drives a menu through the screen's Move / Submit /
    /// Cancel and a control's Activate, and gets exactly what the player gets.
    /// </summary>
    public abstract class UiControl : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        IPointerClickHandler, ISelectHandler, IMoveHandler, ISubmitHandler, ICancelHandler
    {
        public UiScreen Screen { get; internal set; }
        /// <summary>The sticker a hover lifts and a press pushes down, if the control is one.</summary>
        public Sticker Sticker { get; protected set; }
        /// <summary>For controls that are a row of a card: a wash that marks the focus.</summary>
        public Image Highlight { get; protected set; }
        public bool Focused { get; private set; }
        public bool Pressed { get; private set; }
        public bool Interactable { get; set; } = true;
        /// <summary>Interactable, on a screen that is open: a screen that is peeling away takes no more input.</summary>
        public bool Live => Interactable && (Screen == null || Screen.Enabled);
        /// <summary>The colour of the die-cut border at rest; with the focus it turns Ink.</summary>
        public Color RestBorder { get; set; } = UiTheme.Paper;

        /// <summary>Enter, Space or a click.</summary>
        public virtual void Activate() { }

        /// <summary>Left (-1) or right (+1). Returns true if the control used it, false to let the focus move.</summary>
        public virtual bool Adjust(int direction) => false;

        internal void SetFocus(bool focused)
        {
            if (Focused == focused) return;
            Focused = focused;
            if (!focused) Pressed = false;
            Refresh(false);
        }

        /// <summary>Brings the looks in line with the state: rest, focus (hover) or pressed.</summary>
        public void Refresh(bool instant)
        {
            if (Sticker != null)
            {
                Sticker.BorderColor = Focused ? UiTheme.Ink : RestBorder;
                if (Pressed) Sticker.Tween.MoveTo(UiTheme.PressOffset, UiTheme.PressShadow, instant);
                else if (Focused) Sticker.Tween.MoveTo(UiTheme.HoverOffset, UiTheme.HoverShadow, instant);
                else Sticker.Tween.MoveTo(Vector2.zero, UiTheme.ShadowOffset, instant);
            }
            if (Highlight != null) Highlight.color = Focused ? UiTheme.Faint : Color.clear;
        }

        // Activating from the keyboard looks like a press too: down at once, then back up.
        protected void Flash()
        {
            if (Sticker == null) return;
            Sticker.Tween.MoveTo(UiTheme.PressOffset, UiTheme.PressShadow, true);
            Refresh(false);
        }

        public virtual void OnPointerEnter(PointerEventData eventData)
        {
            if (Live && Screen != null) Screen.Focus(this);
        }

        public virtual void OnPointerExit(PointerEventData eventData)
        {
            if (!Pressed) return;
            Pressed = false;
            Refresh(false);
        }

        public virtual void OnPointerDown(PointerEventData eventData)
        {
            if (!Live || eventData.button != PointerEventData.InputButton.Left) return;
            if (Screen != null) Screen.Focus(this);
            Pressed = true;
            Refresh(false);
        }

        public virtual void OnPointerUp(PointerEventData eventData)
        {
            if (!Pressed) return;
            Pressed = false;
            Refresh(false);
        }

        public virtual void OnPointerClick(PointerEventData eventData)
        {
            if (Live && eventData.button == PointerEventData.InputButton.Left) Activate();
        }

        public void OnSelect(BaseEventData eventData)
        {
            if (Screen != null) Screen.Focus(this);
        }

        public void OnMove(AxisEventData eventData)
        {
            if (Screen != null) Screen.Move(eventData.moveDir);
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (Screen != null) Screen.Submit();
        }

        public void OnCancel(BaseEventData eventData)
        {
            if (Screen != null) Screen.Cancel();
        }
    }

    public enum ButtonStyle
    {
        /// <summary>Paper with Ink text.</summary>
        Paper,
        /// <summary>The one primary action of a screen: Cherry with Paper text.</summary>
        Primary,
        /// <summary>Ink with Paper text.</summary>
        Ink,
    }

    /// <summary>A sticker that does something when it is clicked: a pill with a label, or any sticker handed to it.</summary>
    public sealed class UiButton : UiControl
    {
        public Action Clicked;
        public TextMeshProUGUI Label { get; private set; }
        public int Clicks { get; private set; }

        /// <summary>A pill button. Labels of up to 12 characters are set in the display face, longer ones in the body face.</summary>
        public static UiButton Create(Transform parent, string text, ButtonStyle style, Vector2 size, Action clicked)
        {
            Color face = style == ButtonStyle.Primary ? UiTheme.Primary : style == ButtonStyle.Ink ? UiTheme.Ink : UiTheme.Paper;
            Sticker sticker = Sticker.Create(parent, text + " Button", StickerShape.Pill, face, size);
            UiButton button = On(sticker, clicked);
            Color ink = style == ButtonStyle.Paper ? UiTheme.Ink : UiTheme.Paper;
            button.Label = UiKit.Label(sticker.Content, "Label", text, UiKit.ButtonFont(text), UiTheme.ButtonSize, ink);
            button.Label.rectTransform.Fill();
            button.Label.rectTransform.offsetMin = new Vector2(size.y * 0.4f, 4f);
            button.Label.rectTransform.offsetMax = new Vector2(-size.y * 0.4f, -4f);
            return button;
        }

        /// <summary>Makes an existing sticker a button: its face takes the clicks.</summary>
        public static UiButton On(Sticker sticker, Action clicked)
        {
            UiButton button = sticker.gameObject.AddComponent<UiButton>();
            button.Sticker = sticker;
            button.Clicked = clicked;
            sticker.Face.raycastTarget = true;
            return button;
        }

        public override void Activate()
        {
            if (!Live) return;
            Clicks++;
            Flash();
            UiSounds.Click();
            if (Clicked == null) return;
            if (Screen != null) Screen.Run(Clicked);
            else Clicked();
        }
    }

    /// <summary>
    /// A row of a settings card: a name on the left, a value on the right, and something in between that
    /// changes it. Rows are placed by their card; all of them are 620 wide.
    /// </summary>
    public abstract class UiRow : UiControl
    {
        public const float Width = 620f, Height = 52f, NameWidth = 250f;

        public RectTransform Rect { get; private set; }
        public TextMeshProUGUI Name { get; private set; }
        public TextMeshProUGUI ValueLabel { get; private set; }

        protected void Build(string label)
        {
            Rect = (RectTransform)transform;
            Rect.sizeDelta = new Vector2(Width, Height);
            Highlight = UiKit.Image(Rect, "Focus", UiShape.Panel, Color.clear, raycast: true);
            Highlight.rectTransform.Fill();
            Highlight.pixelsPerUnitMultiplier = UiTheme.PanelRadius / 12f;
            Name = UiKit.Label(Rect, "Name", label, UiFont.Body, UiTheme.BodySize, UiTheme.Ink, TextAlignmentOptions.Left);
            Name.rectTransform.Place(UiKit.Left, new Vector2(16f, 0f), new Vector2(NameWidth - 16f, Height));
            ValueLabel = UiKit.Label(Rect, "Value", "", UiFont.Body, UiTheme.BodySize, UiTheme.Ink, TextAlignmentOptions.Right);
            ValueLabel.rectTransform.Place(UiKit.Right, new Vector2(-16f, 0f), new Vector2(96f, Height));
        }

        protected static T Make<T>(Transform parent, string label) where T : UiRow
        {
            RectTransform rect = UiKit.Rect(parent, label);
            T row = rect.gameObject.AddComponent<T>();
            row.Build(label);
            return row;
        }
    }

    /// <summary>A value between two ends: drag or click the track, or left / right with the focus on the row.</summary>
    public sealed class UiSlider : UiRow, IDragHandler
    {
        const float TrackWidth = 230f, TrackHeight = 10f, KnobSize = 26f;

        RectTransform track, fill, knob;
        float min, max, step, value;
        Func<float, string> format;

        public Action<float> Changed;
        public float Value => value;
        public float Min => min;
        public float Max => max;
        /// <summary>0 at the left end of the track, 1 at the right.</summary>
        public float Fraction => max > min ? Mathf.InverseLerp(min, max, value) : 0f;

        public static UiSlider Create(Transform parent, string label, float min, float max, float step, float value, Func<float, string> format, Action<float> changed)
        {
            UiSlider slider = Make<UiSlider>(parent, label);
            slider.min = min;
            slider.max = max;
            slider.step = step;
            slider.format = format;

            Image trackImage = UiKit.Image(slider.Rect, "Track", UiShape.Pill, UiTheme.Faint);
            slider.track = trackImage.rectTransform;
            slider.track.Place(UiKit.Left, new Vector2(NameWidth + 8f, 0f), new Vector2(TrackWidth, TrackHeight));
            trackImage.pixelsPerUnitMultiplier = UiAtlas.PillBorder * 2f / TrackHeight;
            Image fillImage = UiKit.Image(slider.track, "Fill", UiShape.Pill, UiTheme.Ink);
            slider.fill = fillImage.rectTransform;
            slider.fill.Place(UiKit.Left, Vector2.zero, new Vector2(TrackHeight, TrackHeight));
            fillImage.pixelsPerUnitMultiplier = UiAtlas.PillBorder * 2f / TrackHeight;
            Image knobEdge = UiKit.Image(slider.track, "Knob", UiShape.Circle, UiTheme.Ink);
            slider.knob = knobEdge.rectTransform;
            slider.knob.Place(UiKit.Left, UiKit.Centre, Vector2.zero, new Vector2(KnobSize, KnobSize));
            Image knobFace = UiKit.Image(slider.knob, "Face", UiShape.Circle, UiTheme.Paper);
            knobFace.rectTransform.Fill(4f);

            slider.Set(value, false);
            slider.Changed = changed;
            return slider;
        }

        /// <summary>Sets the value (clamped, on the step grid) and tells the listener if it changed.</summary>
        public void Set(float newValue, bool notify = true)
        {
            newValue = Mathf.Clamp(newValue, min, max);
            if (step > 0f) newValue = Mathf.Clamp(min + Mathf.Round((newValue - min) / step) * step, min, max);
            bool changed = !Mathf.Approximately(newValue, value);
            value = newValue;
            float x = Fraction * TrackWidth;
            knob.anchoredPosition = new Vector2(x, 0f);
            fill.sizeDelta = new Vector2(Mathf.Max(TrackHeight, x), TrackHeight);
            ValueLabel.text = format != null ? format(value) : value.ToString("0.##");
            if (changed && notify)
            {
                UiSounds.Hover();
                Changed?.Invoke(value);
            }
        }

        public override bool Adjust(int direction)
        {
            float delta = step > 0f ? step : (max - min) / 20f;
            Set(value + direction * delta);
            return true;
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            Drag(eventData);
        }

        public void OnDrag(PointerEventData eventData) => Drag(eventData);

        void Drag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (!Live || !RectTransformUtility.ScreenPointToLocalPointInRectangle(track, eventData.position, eventData.pressEventCamera, out Vector2 local)) return;
            // The track's pivot is its left end. A click on the row's name is not a click on the track.
            if (local.x < -KnobSize || local.x > TrackWidth + KnobSize) return;
            Set(Mathf.Lerp(min, max, Mathf.Clamp01(local.x / TrackWidth)));
        }
    }

    /// <summary>On or off: Enter, Space or a click flips it; left turns it off and right on.</summary>
    public sealed class UiToggle : UiRow
    {
        const float SwitchWidth = 58f, SwitchHeight = 30f, KnobSize = 22f;

        Image track;
        RectTransform knob;
        bool on;

        public Action<bool> Changed;
        public bool On => on;

        public static UiToggle Create(Transform parent, string label, bool value, Action<bool> changed)
        {
            UiToggle toggle = Make<UiToggle>(parent, label);
            toggle.Name.rectTransform.sizeDelta = new Vector2(Width - SwitchWidth - 56f, Height);
            toggle.track = UiKit.Image(toggle.Rect, "Switch", UiShape.Pill, UiTheme.Faint);
            toggle.track.rectTransform.Place(UiKit.Right, new Vector2(-16f, 0f), new Vector2(SwitchWidth, SwitchHeight));
            toggle.track.pixelsPerUnitMultiplier = UiAtlas.PillBorder * 2f / SwitchHeight;
            Image knobEdge = UiKit.Image(toggle.track.rectTransform, "Knob", UiShape.Circle, UiTheme.Ink);
            toggle.knob = knobEdge.rectTransform;
            toggle.knob.Place(UiKit.Left, UiKit.Centre, Vector2.zero, new Vector2(KnobSize, KnobSize));
            Image knobFace = UiKit.Image(toggle.knob, "Face", UiShape.Circle, UiTheme.Paper);
            knobFace.rectTransform.Fill(3f);
            toggle.ValueLabel.gameObject.SetActive(false);
            toggle.Set(value, false);
            toggle.Changed = changed;
            return toggle;
        }

        public void Set(bool value, bool notify = true)
        {
            bool changed = on != value;
            on = value;
            track.color = on ? UiTheme.Ink : UiTheme.Faint;
            knob.anchoredPosition = new Vector2(on ? SwitchWidth - SwitchHeight * 0.5f : SwitchHeight * 0.5f, 0f);
            if (changed && notify)
            {
                UiSounds.Click();
                Changed?.Invoke(on);
            }
        }

        public override void Activate()
        {
            if (Live) Set(!on);
        }

        public override bool Adjust(int direction)
        {
            Set(direction > 0);
            return true;
        }
    }

    /// <summary>One of a few named options: left / right step through them, Enter, Space or a click takes the next.</summary>
    public sealed class UiChoice : UiRow
    {
        string[] options;
        int index;

        public Action<int> Changed;
        public int Index => index;
        public string Option => options[index];

        public static UiChoice Create(Transform parent, string label, string[] options, int index, Action<int> changed)
        {
            UiChoice choice = Make<UiChoice>(parent, label);
            choice.options = options;
            choice.ValueLabel.rectTransform.sizeDelta = new Vector2(Width - NameWidth - 32f, Height);
            choice.ValueLabel.alignment = TextAlignmentOptions.Right;
            choice.Set(index, false);
            choice.Changed = changed;
            return choice;
        }

        public void Set(int newIndex, bool notify = true)
        {
            newIndex = ((newIndex % options.Length) + options.Length) % options.Length;
            bool changed = newIndex != index;
            index = newIndex;
            ValueLabel.text = "<  " + options[index] + "  >";
            if (changed && notify)
            {
                UiSounds.Click();
                Changed?.Invoke(index);
            }
        }

        public override void Activate()
        {
            if (Live) Set(index + 1);
        }

        public override bool Adjust(int direction)
        {
            Set(index + direction);
            return true;
        }
    }

    /// <summary>
    /// One screen of a menu: its root, its controls in rows, and which of them has the focus. Up and down
    /// move between rows, left and right within a row (unless the control under the focus uses them
    /// itself, like a slider). Whatever a button does is handed to <see cref="Run"/>, which the menu
    /// presenter points at its queue, so actions happen at a fixed point of the frame.
    /// </summary>
    public sealed class UiScreen
    {
        readonly List<List<UiControl>> rows = new List<List<UiControl>>();

        public string Name { get; }
        public RectTransform Root { get; }
        public UiControl Focused { get; private set; }
        /// <summary>The control that has the focus when the screen opens.</summary>
        public UiControl First { get; set; }
        /// <summary>What Esc does on this screen (nothing if null).</summary>
        public Action Cancelled;
        /// <summary>Runs a button's action; the default runs it on the spot.</summary>
        public Action<Action> Run = action => action();
        /// <summary>False while the screen is closed or peeling away: it then ignores every input.</summary>
        public bool Enabled { get; set; } = true;
        public bool Visible => Root.gameObject.activeSelf;
        public int RowCount => rows.Count;

        public UiScreen(Transform parent, string name)
        {
            Name = name;
            Root = UiKit.Rect(parent, name);
            Root.Fill();
        }

        public IReadOnlyList<UiControl> Row(int index) => rows[index];

        /// <summary>Adds the controls as the next row down.</summary>
        public void AddRow(params UiControl[] controls)
        {
            var row = new List<UiControl>();
            foreach (UiControl control in controls)
            {
                if (control == null) continue;
                control.Screen = this;
                row.Add(control);
                if (First == null) First = control;
            }
            if (row.Count > 0) rows.Add(row);
        }

        public void ClearRows()
        {
            rows.Clear();
            Focused = null;
            First = null;
        }

        public void Show()
        {
            if (!Root.gameObject.activeSelf) Root.gameObject.SetActive(true);
            Focus(First);
        }

        public void Hide()
        {
            if (Root.gameObject.activeSelf) Root.gameObject.SetActive(false);
        }

        public void Focus(UiControl control)
        {
            if (control == null || !control.Interactable || control == Focused)
            {
                Select(Focused);
                return;
            }
            // The focus moving from one control to another is heard; the one a screen opens with is not.
            if (Focused != null && Enabled) UiSounds.Hover();
            if (Focused != null) Focused.SetFocus(false);
            Focused = control;
            control.SetFocus(true);
            Select(control);
        }

        // The EventSystem's selection follows the focus, so Move / Submit / Cancel keep arriving at this screen.
        static void Select(UiControl control)
        {
            EventSystem system = EventSystem.current;
            if (system == null || control == null || system.alreadySelecting) return;
            if (system.currentSelectedGameObject != control.gameObject) system.SetSelectedGameObject(control.gameObject);
        }

        /// <summary>Moves the focus (or adjusts the control under it). Returns true if anything happened.</summary>
        public bool Move(MoveDirection direction)
        {
            if (rows.Count == 0 || !Enabled) return false;
            if (Focused == null)
            {
                Focus(First);
                return Focused != null;
            }
            if (!Find(Focused, out int r, out int c)) return false;

            switch (direction)
            {
                case MoveDirection.Left:
                case MoveDirection.Right:
                {
                    int step = direction == MoveDirection.Right ? 1 : -1;
                    if (Focused.Adjust(step)) return true;
                    int to = c + step;
                    if (to < 0 || to >= rows[r].Count) return false;
                    Focus(rows[r][to]);
                    return true;
                }
                case MoveDirection.Up:
                case MoveDirection.Down:
                {
                    if (rows.Count == 1) return false;
                    int step = direction == MoveDirection.Down ? 1 : -1;
                    // Around the ends, skipping rows with nothing to focus on.
                    for (int i = 1; i < rows.Count; i++)
                    {
                        List<UiControl> row = rows[((r + step * i) % rows.Count + rows.Count) % rows.Count];
                        UiControl target = row[Mathf.Min(c, row.Count - 1)];
                        if (!target.Interactable) continue;
                        Focus(target);
                        return true;
                    }
                    return false;
                }
                default:
                    return false;
            }
        }

        public void Submit()
        {
            if (Enabled && Focused != null) Focused.Activate();
        }

        public void Cancel()
        {
            if (Enabled && Cancelled != null) Run(Cancelled);
        }

        bool Find(UiControl control, out int row, out int column)
        {
            for (row = 0; row < rows.Count; row++)
            {
                column = rows[row].IndexOf(control);
                if (column >= 0) return true;
            }
            row = -1;
            column = -1;
            return false;
        }
    }
}
