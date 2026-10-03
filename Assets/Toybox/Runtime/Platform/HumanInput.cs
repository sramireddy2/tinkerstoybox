using Toybox.Engine;
using UnityEngine;

namespace Toybox.Platform
{
    /// <summary>
    /// The human player as an input source.
    ///
    /// Rendered frames and simulation ticks do not line up: a frame may run no tick at all, or several.
    /// <see cref="Update"/> is called once per rendered frame and latches every press; <see cref="Sample"/>
    /// is called once per tick and hands each latched press to exactly one tick. Held keys are simply
    /// repeated for every tick.
    ///
    /// Mouse look does not travel through InputFrame. It is applied to the player right away in Update,
    /// so the view answers the mouse at the display rate rather than at the tick rate.
    ///
    /// Pointer lock: a click locks the mouse (that click does not grab). While it is not locked, moving
    /// the mouse does not turn the view; holding the right button does, for environments that refuse the
    /// lock. The keyboard works either way.
    /// </summary>
    public sealed class HumanInput : IInputSource
    {
        /// <summary>Degrees of view rotation per pixel of mouse movement.</summary>
        public const float DefaultSensitivity = 0.1f;

        // Wheel notches that have not reached a tick yet. More than this is a runaway wheel, not intent.
        const int MaxPendingYawSteps = 12;
        // The first delta after the lock engages can be a jump of hundreds of pixels in some browsers.
        const float MaxLookPixelsPerFrame = 2000f;

        readonly IInputDevices devices;

        DeviceFrame last;
        float moveX, moveZ;
        bool jump, sprint;
        bool jumpLatched, grabLatched, restartLatched, pitchLatched;
        int yawPending;
        bool wasLooking;

        public float Sensitivity = DefaultSensitivity;
        public bool InvertY;

        public HumanInput(IInputDevices devices = null)
        {
            this.devices = devices ?? new UnityInputDevices();
        }

        public IInputDevices Devices => devices;

        /// <summary>True while the mouse is captured and turns the view.</summary>
        public bool PointerLocked => devices.PointerLocked;

        /// <summary>What the devices reported in the last Update (the debug keys are read from here).</summary>
        public DeviceFrame Frame => last;

        /// <summary>
        /// Polls the devices. Call once per rendered frame, before the simulation is stepped. Mouse look is
        /// applied to <paramref name="player"/>; pass null to keep the view still (a bot is playing).
        /// </summary>
        public void Update(Player player)
        {
            DeviceFrame frame = devices.Poll();
            last = frame;

            if (!frame.Focused)
            {
                // A key that is down when focus goes away never reports its release.
                Clear();
                return;
            }

            bool locked = devices.PointerLocked;
            bool click = frame.ClickPressed;
            if (!locked && click)
            {
                // The click that captures the mouse is not also a grab.
                devices.PointerLocked = true;
                locked = devices.PointerLocked;
                click = false;
            }
            else if (locked && frame.EscapePressed)
            {
                devices.PointerLocked = false;
                locked = devices.PointerLocked;
            }

            moveX = Mathf.Clamp(frame.MoveX, -1f, 1f);
            moveZ = Mathf.Clamp(frame.MoveZ, -1f, 1f);
            jump = frame.Jump;
            sprint = frame.Sprint;

            jumpLatched |= frame.JumpPressed;
            grabLatched |= click || frame.GrabKeyPressed;
            restartLatched |= frame.RestartPressed;
            pitchLatched |= frame.RotatePitchPressed;
            yawPending = Mathf.Clamp(yawPending + frame.RotateYawSteps, -MaxPendingYawSteps, MaxPendingYawSteps);

            bool looking = locked || frame.LookButton;
            // Skip the frame in which looking starts: its delta covers movement from before.
            if (looking && wasLooking && player != null)
            {
                Vector2 look = Vector2.ClampMagnitude(frame.Look, MaxLookPixelsPerFrame);
                player.AddLook(look.x * Sensitivity, look.y * Sensitivity * (InvertY ? -1f : 1f));
            }
            wasLooking = looking;
        }

        /// <summary>IInputSource: the frame for one tick. Every latched press is delivered here, once.</summary>
        public InputFrame Sample()
        {
            var frame = new InputFrame
            {
                MoveX = moveX,
                MoveZ = moveZ,
                // A tap that began and ended between two ticks still has to reach one.
                Jump = jump || jumpLatched,
                Sprint = sprint,
                GrabPressed = grabLatched,
                RestartPressed = restartLatched,
                RotatePitch = pitchLatched,
            };
            // The simulation takes one yaw step per tick; a burst of wheel notches is paid out over the next ticks.
            if (yawPending != 0)
            {
                frame.RotateYaw = yawPending > 0 ? 1 : -1;
                yawPending -= frame.RotateYaw;
            }
            jumpLatched = false;
            grabLatched = false;
            restartLatched = false;
            pitchLatched = false;
            return frame;
        }

        /// <summary>Forgets held keys and presses that have not reached a tick (focus was lost, a bot took over).</summary>
        public void Clear()
        {
            moveX = 0f;
            moveZ = 0f;
            jump = false;
            sprint = false;
            jumpLatched = false;
            grabLatched = false;
            restartLatched = false;
            pitchLatched = false;
            yawPending = 0;
            wasLooking = false;
        }

        /// <summary>Gives the mouse back (the runner is shutting down).</summary>
        public void ReleasePointer()
        {
            // Unconditionally: the devices also have to show the cursor again if they hid it for a lock
            // that is no longer held.
            devices.PointerLocked = false;
        }
    }
}
