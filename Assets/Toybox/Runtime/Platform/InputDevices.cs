using UnityEngine;
using UnityEngine.InputSystem;

namespace Toybox.Platform
{
    /// <summary>
    /// What keyboard and mouse report for one rendered frame. "Held" fields are the state at the moment of
    /// polling; "Pressed" fields are true if the key or button went down at any time during the frame.
    /// </summary>
    public struct DeviceFrame
    {
        /// <summary>False while the application does not have focus. Everything else is ignored then.</summary>
        public bool Focused;

        /// <summary>Held: strafe (-1 left .. +1 right) and forward (-1 back .. +1 forward).</summary>
        public float MoveX, MoveZ;
        /// <summary>Held.</summary>
        public bool Jump, Sprint;

        public bool JumpPressed;
        /// <summary>Left mouse button. Locks the pointer when it is free, grabs or drops when it is locked.</summary>
        public bool ClickPressed;
        /// <summary>E: grabs or drops whether or not the pointer is locked.</summary>
        public bool GrabKeyPressed;
        /// <summary>F.</summary>
        public bool RotatePitchPressed;
        /// <summary>R.</summary>
        public bool RestartPressed;
        public bool EscapePressed;
        /// <summary>Debug keys: [ and ] and P. Read by GameRunner, not part of the player's input.</summary>
        public bool PreviousLevelPressed, NextLevelPressed, AutoplayPressed;

        /// <summary>Q presses and mouse wheel notches during the frame, signed.</summary>
        public int RotateYawSteps;

        /// <summary>Held right mouse button: look around without pointer lock.</summary>
        public bool LookButton;
        /// <summary>Mouse movement during the frame in pixels; x to the right, y up.</summary>
        public Vector2 Look;
    }

    /// <summary>The seam between HumanInput and real hardware, so the input logic runs in tests without devices.</summary>
    public interface IInputDevices
    {
        /// <summary>Called once per rendered frame.</summary>
        DeviceFrame Poll();

        /// <summary>
        /// Whether the mouse is captured. Setting it is a request: a browser may refuse or revoke the lock,
        /// so read it back instead of remembering what was asked for. The cursor is hidden exactly while
        /// the lock is held.
        /// </summary>
        bool PointerLocked { get; set; }
    }

    /// <summary>Keyboard and mouse through the Input System package, polled directly (no action assets).</summary>
    public sealed class UnityInputDevices : IInputDevices
    {
        // A trackpad reports a stream of tiny scroll deltas; without this one swipe would spin the prop wildly.
        const float WheelRepeatSeconds = 0.05f;

        float nextWheelTime;
        // The cursor is hidden because this class holds the lock; it is this class's job to show it again.
        bool cursorHidden;

        public UnityInputDevices()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // By default the Web player keeps reporting CursorLockMode.Locked after the browser has taken
            // the pointer back (Esc, which the page never sees as a key, or the tab losing focus). The
            // game would go on believing the mouse is captured: no "click to play", the cursor stays
            // hidden, and the click that captures it again would also be a grab. With this off,
            // Cursor.lockState follows the browser and PointerLocked is a real read-back.
            WebGLInput.stickyCursorLock = false;
#endif
        }

        public bool PointerLocked
        {
            get => Cursor.lockState == CursorLockMode.Locked;
            set
            {
                Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
                SyncCursor();
            }
        }

        // The request for the lock may be refused, and a lock that was granted may be taken away from
        // outside at any time; either way a free mouse needs its cursor.
        void SyncCursor()
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Cursor.visible = false;
                cursorHidden = true;
            }
            else if (cursorHidden)
            {
                Cursor.visible = true;
                cursorHidden = false;
            }
        }

        public DeviceFrame Poll()
        {
            SyncCursor();
            var frame = new DeviceFrame { Focused = Application.isFocused };

            // Either device can be missing (a touch-only browser, a headless run): then it reports nothing.
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) frame.MoveX -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) frame.MoveX += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) frame.MoveZ -= 1f;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) frame.MoveZ += 1f;
                frame.Jump = keyboard.spaceKey.isPressed;
                frame.JumpPressed = keyboard.spaceKey.wasPressedThisFrame;
                frame.Sprint = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                frame.GrabKeyPressed = keyboard.eKey.wasPressedThisFrame;
                frame.RotatePitchPressed = keyboard.fKey.wasPressedThisFrame;
                frame.RestartPressed = keyboard.rKey.wasPressedThisFrame;
                frame.EscapePressed = keyboard.escapeKey.wasPressedThisFrame;
                frame.PreviousLevelPressed = keyboard.leftBracketKey.wasPressedThisFrame;
                frame.NextLevelPressed = keyboard.rightBracketKey.wasPressedThisFrame;
                frame.AutoplayPressed = keyboard.pKey.wasPressedThisFrame;
                if (keyboard.qKey.wasPressedThisFrame) frame.RotateYawSteps += 1;
            }

            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                frame.ClickPressed = mouse.leftButton.wasPressedThisFrame;
                frame.LookButton = mouse.rightButton.isPressed;
                frame.Look = mouse.delta.ReadValue();

                // The size of a wheel notch differs between platforms and browsers; only its direction is used.
                float wheel = mouse.scroll.ReadValue().y;
                float now = Time.unscaledTime;
                if (wheel != 0f && now >= nextWheelTime)
                {
                    frame.RotateYawSteps += wheel > 0f ? 1 : -1;
                    nextWheelTime = now + WheelRepeatSeconds;
                }
            }
            return frame;
        }
    }
}
