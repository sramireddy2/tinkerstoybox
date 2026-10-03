namespace Toybox.Engine
{
    /// <summary>
    /// Everything the player can do during one tick. Humans and the Bot both produce these; the simulation
    /// has no other way of being steered.
    /// </summary>
    public struct InputFrame
    {
        /// <summary>Strafe (-1 left .. +1 right) and forward (-1 back .. +1 forward).</summary>
        public float MoveX, MoveZ;

        /// <summary>Look deltas for this tick, in degrees. Positive yaw turns right, positive pitch looks up.</summary>
        public float LookYaw, LookPitch;

        /// <summary>Held state of the jump key; the player detects the press edge itself.</summary>
        public bool Jump;

        public bool Sprint;

        /// <summary>Edge: toggle grab / drop.</summary>
        public bool GrabPressed;

        /// <summary>Edge: restart the level.</summary>
        public bool RestartPressed;

        /// <summary>Edge: turn the held prop one 15 degree yaw step (-1, 0 or +1).</summary>
        public int RotateYaw;

        /// <summary>Edge: pitch the held prop 90 degrees.</summary>
        public bool RotatePitch;
    }

    public interface IInputSource
    {
        /// <summary>Called exactly once per tick. Edge fields must be reported once.</summary>
        InputFrame Sample();
    }
}
