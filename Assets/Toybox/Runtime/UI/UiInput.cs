using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace Toybox.UI
{
    /// <summary>
    /// The EventSystem of the menus, with the Input System's UI module (the legacy input manager is off in
    /// this project) and a set of actions made in code: the mouse points and clicks, the arrow keys (and
    /// WASD) move the focus, Enter and Space activate, Esc cancels. It only exists in Play Mode and in the
    /// build; outside it nothing pumps an EventSystem, and tests drive the menus through
    /// <see cref="MenuPresenter"/> directly.
    /// </summary>
    public sealed class UiInput
    {
        GameObject holder;
        InputActionAsset actions;

        public EventSystem EventSystem { get; private set; }
        public InputSystemUIInputModule Module { get; private set; }
        public InputAction Submit { get; private set; }

        /// <summary>The actions the module is given. Public so a test can check the bindings without an EventSystem.</summary>
        public static InputActionAsset CreateActions()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "Toybox UI Actions";
            asset.hideFlags = HideFlags.HideAndDontSave;
            InputActionMap map = asset.AddActionMap("UI");

            InputAction point = map.AddAction("Point", InputActionType.PassThrough, "<Mouse>/position");
            point.expectedControlType = "Vector2";
            point.AddBinding("<Pen>/position");
            point.AddBinding("<Touchscreen>/touch*/position");

            InputAction click = map.AddAction("Click", InputActionType.PassThrough, "<Mouse>/leftButton");
            click.expectedControlType = "Button";
            click.AddBinding("<Pen>/tip");
            click.AddBinding("<Touchscreen>/touch*/press");

            InputAction scroll = map.AddAction("ScrollWheel", InputActionType.PassThrough, "<Mouse>/scroll");
            scroll.expectedControlType = "Vector2";

            InputAction navigate = map.AddAction("Navigate", InputActionType.PassThrough);
            navigate.expectedControlType = "Vector2";
            navigate.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            navigate.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");

            InputAction submit = map.AddAction("Submit", InputActionType.Button, "<Keyboard>/enter");
            submit.AddBinding("<Keyboard>/numpadEnter");
            submit.AddBinding("<Keyboard>/space");

            map.AddAction("Cancel", InputActionType.Button, "<Keyboard>/escape");
            return asset;
        }

        /// <summary>Creates the EventSystem under the parent. Returns null outside Play Mode.</summary>
        public static UiInput Create(Transform parent)
        {
            if (!Application.isPlaying) return null;
            var input = new UiInput();
            // Inactive while it is put together: the module would otherwise take the package's default actions on enable.
            input.holder = new GameObject("UI Input") { hideFlags = HideFlags.DontSave };
            input.holder.SetActive(false);
            input.holder.transform.SetParent(parent, false);
            input.EventSystem = input.holder.AddComponent<EventSystem>();
            input.EventSystem.sendNavigationEvents = true;
            input.Module = input.holder.AddComponent<InputSystemUIInputModule>();
            input.actions = CreateActions();
            InputActionMap map = input.actions.FindActionMap("UI");
            input.Module.actionsAsset = input.actions;
            input.Module.point = InputActionReference.Create(map.FindAction("Point"));
            input.Module.leftClick = InputActionReference.Create(map.FindAction("Click"));
            input.Module.scrollWheel = InputActionReference.Create(map.FindAction("ScrollWheel"));
            input.Module.move = InputActionReference.Create(map.FindAction("Navigate"));
            input.Module.submit = InputActionReference.Create(map.FindAction("Submit"));
            input.Module.cancel = InputActionReference.Create(map.FindAction("Cancel"));
            // A click beside every button must not take the keyboard's focus away.
            input.Module.deselectOnBackgroundClick = false;
            input.Submit = map.FindAction("Submit");
            input.holder.SetActive(true);
            return input;
        }

        /// <summary>True while a key that activates is still down (so what it started can wait for the release).</summary>
        public bool SubmitHeld => Submit != null && Submit.enabled && Submit.IsPressed();

        public void Dispose()
        {
            if (holder != null) Object.Destroy(holder);
            holder = null;
            if (actions != null) Object.Destroy(actions);
            actions = null;
        }
    }
}
