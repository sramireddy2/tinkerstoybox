using System;
using System.Collections;
using UnityEngine;

namespace Toybox.Engine
{
    /// <summary>A bot command failed: it timed out or the world did not react as the script expected.</summary>
    public sealed class BotException : Exception
    {
        public BotException(string message) : base(message) { }
    }

    /// <summary>
    /// A scripted player. It reads the game state like a human reads the screen, but the only thing it can
    /// do is produce InputFrames, so a level its script solves is solvable by hand. Commands are iterators;
    /// every yield is one tick. A script that reaches past the bot and changes the simulation itself
    /// (teleports the player, moves a prop, completes the level) is caught by the BotRunner, which compares
    /// the simulation before and after every step of the script.
    /// </summary>
    public sealed class Bot : IInputSource
    {
        /// <summary>How fast the bot turns its view, in degrees per second. A brisk but human mouse movement.</summary>
        public const float TurnRate = 360f;

        const float AimTolerance = 0.03f;
        // Inside this distance from a walk target the bot eases off, so tight tolerances do not overshoot.
        const float SlowRadius = 0.6f;

        readonly Game game;
        InputFrame frame;
        InputFrame last;
        int unstarted;

        /// <summary>Creates the bot and makes it the game's input source.</summary>
        public Bot(Game game)
        {
            this.game = game;
            game.Input = this;
        }

        /// <summary>For reading. Whatever a script changes through it is refused by the BotRunner.</summary>
        public Game Game => game;
        public Player Player => game.Player;

        /// <summary>
        /// Commands that were created and never run. A command only does something when the script yields
        /// it; `bot.Jump();` without `yield return` in front is silently nothing. BotRunner.Run checks this.
        /// </summary>
        public int UnstartedCommands => unstarted;

        public InputFrame Sample()
        {
            last = frame;
            frame = default;
            return last;
        }

        /// <summary>Turns the view until it points at a world position (also while the bot itself is being carried).</summary>
        public IEnumerator LookAt(Vector3 target, float timeoutSeconds = 5f) =>
            Command(Aim(() => target, timeoutSeconds, "LookAt"));

        /// <summary>Turns the view until it points at a prop's center, following it if it moves.</summary>
        public IEnumerator LookAt(Prop prop, float timeoutSeconds = 5f) =>
            Command(Aim(() => prop.Center, timeoutSeconds, "LookAt(" + prop.Name + ")"));

        /// <summary>
        /// Walks (or sprints) in a straight line until the feet are within tolerance of the point,
        /// measured horizontally. Throws if that takes longer than the timeout.
        /// </summary>
        public IEnumerator WalkTo(Vector3 point, float tolerance = 0.3f, float timeoutSeconds = 10f, bool sprint = false) =>
            Command(Walk(point, tolerance, timeoutSeconds, sprint));

        IEnumerator Walk(Vector3 point, float tolerance, float timeoutSeconds, bool sprint)
        {
            int limit = Ticks(timeoutSeconds);
            for (int tick = 0; ; tick++)
            {
                Vector3 to = point - Player.Position;
                to.y = 0f;
                float distance = to.magnitude;
                if (distance <= tolerance) yield break;
                if (tick >= limit)
                    throw Fail("WalkTo timed out after " + timeoutSeconds.ToString("0.#") + " s, still " + distance.ToString("0.00") + " away", point);

                float error = Mathf.DeltaAngle(Player.Yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                float turn = Mathf.Clamp(error, -MaxTurn, MaxTurn);
                frame.LookYaw = turn;
                // Move straight at the target even while still turning toward it.
                float remaining = (error - turn) * Mathf.Deg2Rad;
                float throttle = Mathf.Clamp(distance / SlowRadius, 0.25f, 1f);
                frame.MoveX = Mathf.Sin(remaining) * throttle;
                frame.MoveZ = Mathf.Cos(remaining) * throttle;
                frame.Sprint = sprint;
                yield return null;
            }
        }

        /// <summary>Taps jump. By default the movement keys of the previous tick stay down, for running jumps.</summary>
        public IEnumerator Jump(bool keepMoving = true) => Command(JumpOnce(keepMoving));

        IEnumerator JumpOnce(bool keepMoving)
        {
            InputFrame moving = last;
            if (last.Jump) yield return null;
            frame.Jump = true;
            if (keepMoving)
            {
                frame.MoveX = moving.MoveX;
                frame.MoveZ = moving.MoveZ;
                frame.Sprint = moving.Sprint;
            }
            yield return null;
        }

        /// <summary>
        /// Aims at the prop and clicks as soon as a click would take it; it need not hold still. The bot aims
        /// at a part of the prop it can see: the center if that is in view, otherwise another point on it
        /// (the leg of an arch, the end of a plank whose middle is hidden). Throws if the prop is not in
        /// hand afterwards.
        /// </summary>
        public IEnumerator Grab(Prop prop, float timeoutSeconds = 5f) => Command(GrabProp(prop, null, timeoutSeconds));

        /// <summary>Like Grab, but aims at the given world point - for when the script knows better where to take hold.</summary>
        public IEnumerator GrabAt(Prop prop, Vector3 worldPoint, float timeoutSeconds = 5f) => Command(GrabProp(prop, worldPoint, timeoutSeconds));

        IEnumerator GrabProp(Prop prop, Vector3? worldPoint, float timeoutSeconds)
        {
            PerspectiveGrabber grabber = game.Grabber;
            if (grabber.Held == prop) yield break;
            if (grabber.Held != null)
                throw Fail("Grab(" + prop.Name + ") while already holding " + grabber.Held.Name, prop.Center);

            string what = "Grab(" + prop.Name + ")";
            if (worldPoint.HasValue)
            {
                Vector3 point = worldPoint.Value;
                yield return Aim(() => point, timeoutSeconds, what);
            }
            else
            {
                // Fixed on the prop, so the aim follows it when it moves.
                Vector3 local = prop.Transform.InverseTransformPoint(VisiblePoint(prop));
                yield return Aim(() => prop.Transform.TransformPoint(local), timeoutSeconds, what, () => grabber.FindTarget() == prop);
            }
            frame.GrabPressed = true;
            yield return null;
            if (grabber.Held != prop)
            {
                Prop seen = grabber.Held ?? grabber.FindTarget();
                throw Fail("Could not grab " + prop.Name + " (the click took " + (seen != null ? seen.Name : "nothing") + ")", prop.Center);
            }
        }

        /// <summary>Presses the grab button for one tick, whatever the view points at: takes, or lets go.</summary>
        public IEnumerator Click() => Command(ClickOnce());

        IEnumerator ClickOnce()
        {
            frame.GrabPressed = true;
            yield return null;
        }

        /// <summary>
        /// Turns the held prop: 15 degrees per yaw step (either way), 90 degrees per pitch step, one step
        /// per tick. The pitch key only turns one way, so a negative pitch step is three presses.
        /// </summary>
        public IEnumerator RotateHeld(int yawSteps, int pitchSteps = 0) => Command(Rotate(yawSteps, pitchSteps));

        IEnumerator Rotate(int yawSteps, int pitchSteps)
        {
            if (game.Grabber.Held == null) throw Fail("RotateHeld with empty hands", Player.Position);
            int yaw = Mathf.Abs(yawSteps);
            int pitch = (pitchSteps % 4 + 4) % 4;
            while (yaw > 0 || pitch > 0)
            {
                if (yaw > 0)
                {
                    frame.RotateYaw = yawSteps > 0 ? 1 : -1;
                    yaw--;
                }
                if (pitch > 0)
                {
                    frame.RotatePitch = true;
                    pitch--;
                }
                yield return null;
            }
        }

        /// <summary>Aims so the view ray passes through the point, then lets go of the held prop.</summary>
        public IEnumerator DropAt(Vector3 point, float timeoutSeconds = 5f) => Command(DropAtPoint(point, timeoutSeconds));

        IEnumerator DropAtPoint(Vector3 point, float timeoutSeconds)
        {
            if (game.Grabber.Held == null) throw Fail("DropAt with empty hands", point);
            yield return Aim(() => point, timeoutSeconds, "DropAt");
            yield return Release(point);
        }

        /// <summary>Lets go of the held prop where it is.</summary>
        public IEnumerator Drop() => Command(DropHeld());

        IEnumerator DropHeld()
        {
            if (game.Grabber.Held == null) throw Fail("Drop with empty hands", Player.Position);
            yield return Release(game.Grabber.Held.Center);
        }

        public IEnumerator Wait(float seconds) => Command(WaitTicks(seconds));

        IEnumerator WaitTicks(float seconds)
        {
            for (int tick = Ticks(seconds); tick > 0; tick--) yield return null;
        }

        /// <summary>Waits until the condition holds. Throws after the timeout.</summary>
        public IEnumerator Until(Func<bool> condition, float timeoutSeconds = 10f) => Command(WaitUntil(condition, timeoutSeconds));

        IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
        {
            int limit = Ticks(timeoutSeconds);
            for (int tick = 0; !condition(); tick++)
            {
                if (tick >= limit)
                    throw Fail("Until timed out after " + timeoutSeconds.ToString("0.#") + " s", Player.Position);
                yield return null;
            }
        }

        static float MaxTurn => TurnRate * Sim.Dt;

        static int Ticks(float seconds) => Mathf.Max(0, Mathf.CeilToInt(seconds / Sim.Dt - 1e-3f));

        // Counts the command as created now and as started when it is first pumped.
        IEnumerator Command(IEnumerator body)
        {
            unstarted++;
            return Started(body);
        }

        IEnumerator Started(IEnumerator body)
        {
            unstarted--;
            while (body.MoveNext()) yield return body.Current;
        }

        /// <summary>
        /// Turns the view toward a target until it is lined up. The target may move and so may the bot: the
        /// turn of each tick includes how far the direction to the target wandered during the last one, so a
        /// steady drift is followed exactly instead of trailing one tick behind for ever.
        /// </summary>
        IEnumerator Aim(Func<Vector3> target, float timeoutSeconds, string what, Func<bool> goodEnough = null)
        {
            int limit = Ticks(timeoutSeconds);
            bool tracking = false;
            float previousYaw = 0f, previousPitch = 0f;
            for (int tick = 0; ; tick++)
            {
                Vector3 point = target();
                Vector3 to = point - Player.Eye;
                float wantedYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float wantedPitch = Mathf.Clamp(Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg, -Player.MaxPitch, Player.MaxPitch);
                float yawError = Mathf.DeltaAngle(Player.Yaw, wantedYaw);
                float pitchError = wantedPitch - Player.Pitch;
                if (goodEnough != null && goodEnough()) yield break;
                if (Mathf.Abs(yawError) <= AimTolerance && Mathf.Abs(pitchError) <= AimTolerance) yield break;
                if (tick >= limit)
                    throw Fail(what + " could not line up the view within " + timeoutSeconds.ToString("0.#") + " s", point);

                float yawDrift = tracking ? Mathf.DeltaAngle(previousYaw, wantedYaw) : 0f;
                float pitchDrift = tracking ? wantedPitch - previousPitch : 0f;
                previousYaw = wantedYaw;
                previousPitch = wantedPitch;
                tracking = true;
                frame.LookYaw = Mathf.Clamp(yawError + yawDrift, -MaxTurn, MaxTurn);
                frame.LookPitch = Mathf.Clamp(pitchError + pitchDrift, -MaxTurn, MaxTurn);
                yield return null;
            }
        }

        IEnumerator Release(Vector3 point)
        {
            frame.GrabPressed = true;
            yield return null;
            if (game.Grabber.Held != null) throw Fail("Could not drop " + game.Grabber.Held.Name, point);
        }

        // A point of the prop that is in plain view from where the bot stands: its center if possible,
        // otherwise the middle of one of its colliders, otherwise some other spot on one of them.
        Vector3 VisiblePoint(Prop prop)
        {
            Vector3 center = prop.Center;
            if (Sees(prop, center)) return center;
            Collider[] colliders = prop.Colliders;
            for (int c = 0; c < colliders.Length; c++)
            {
                Collider collider = colliders[c];
                if (collider == null || !collider.enabled || collider.isTrigger) continue;
                Vector3 middle = collider.bounds.center;
                if (Sees(prop, middle)) return middle;
            }
            for (int c = 0; c < colliders.Length; c++)
            {
                Collider collider = colliders[c];
                if (collider == null || !collider.enabled || collider.isTrigger) continue;
                Bounds bounds = collider.bounds;
                // Toward the corners and the face centers of its bounds, but on the collider itself.
                for (int i = 0; i < 26; i++)
                {
                    int code = i < 13 ? i : i + 1;
                    var offset = new Vector3(code % 3 - 1, code / 3 % 3 - 1, code / 9 - 1);
                    Vector3 point = collider.ClosestPoint(bounds.center + Vector3.Scale(offset, bounds.extents) * 0.8f);
                    if (Sees(prop, point)) return point;
                }
            }
            return center;
        }

        bool Sees(Prop prop, Vector3 point)
        {
            Vector3 to = point - Player.Eye;
            float distance = to.magnitude;
            if (distance < 1e-3f) return false;
            return game.PhysicsScene.Raycast(Player.Eye, to / distance, out RaycastHit hit, distance + 0.05f, Layers.SolidMask, QueryTriggerInteraction.Ignore)
                   && PropRef.Of(hit.collider) == prop;
        }

        BotException Fail(string message, Vector3 target) =>
            new BotException(message + " [bot at " + Format(Player.Position) + ", target " + Format(target) +
                             ", t=" + game.Time.ToString("0.00") + " s]");

        static string Format(Vector3 v) => "(" + v.x.ToString("0.00") + ", " + v.y.ToString("0.00") + ", " + v.z.ToString("0.00") + ")";
    }
}
