using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using Toybox.Engine;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Tests
{
    /// <summary>
    /// Attacks on "nothing throws the player" (ARCHITECTURE, Props and the player's body), written by the
    /// checker of that rule and kept as tests. Each is a way a prop that outweighs the player gets at them
    /// that HeavyPropTests does not try: awkward shapes (a long plank that swings, a tall one whose tip
    /// comes down on them, a real domino, a ball down a ramp), a player who is walking, sprinting or in the
    /// air, a toy that grew in the hand, a wall or a corner behind them, and a prop under their feet that
    /// is hit by another.
    ///
    /// In every one of them:
    /// - the prop never moves the player faster than 9 units a second, sideways or upward;
    /// - the player is never inside a prop that is solid for them for more than a second;
    /// - they can walk away;
    /// - once the prop rests and they are out of it, it is solid and they can stand on it;
    /// - the same run twice gives the same positions, bit for bit.
    /// Every run writes its measurements to Temp/ToyboxHeavyPropAttacks.txt (the tables of the run that
    /// checked the rule are kept in tools/out/notes/prelude-verify-attacks.txt).
    /// </summary>
    public class HeavyPropAttackTests : SimTest
    {
        /// <summary>Faster than this the player has been thrown (sprinting is 8, a jump leaves the ground at 7.6).</summary>
        const float ThrownSpeed = 9f;
        /// <summary>Deeper than this in a prop that is solid for the capsule is "inside it" (a resting contact is under 0.02).</summary>
        const float InsideDepth = 0.05f;
        const int ASecond = 60;

        sealed class Seen
        {
            /// <summary>The player's highest speed along the ground and upward: velocity, and distance covered per tick.</summary>
            public float Sideways, Up;
            /// <summary>Farthest from where they stood when the watch began.</summary>
            public float Moved;
            /// <summary>Longest run of ticks inside a prop that was solid for them, and which.</summary>
            public int Inside;
            public string InsideOf = "";
            /// <summary>Some prop passed through the player at some time.</summary>
            public bool Passed;
            public uint Hash = 2166136261u;
            public Vector3 From, Last;
            public int InsideNow;
            public bool Skip;
        }

        readonly List<Prop> heavies = new List<Prop>();
        Seen seen;
        /// <summary>How fast the player is going by themselves, the way they face (0: they stand still). Not the prop's doing.</summary>
        float own;

        // ---- The harness ------------------------------------------------------------------------------------------

        void Begin(Action<LevelContext> build)
        {
            heavies.Clear();
            own = 0f;
            Build(build);
            seen = new Seen { From = Game.Player.Position, Last = Game.Player.Position };
        }

        void End()
        {
            Game.Dispose();
            Game = null;
        }

        /// <summary>A block, ball or plank of so many times the player's mass.</summary>
        static PropOptions Weighing(float ratio, float volume, float friction = 0.6f) =>
            new PropOptions { Density = ratio * Player.Mass / volume, Friction = friction, Grabbable = false };

        Prop Heavy(Prop prop)
        {
            heavies.Add(prop);
            return prop;
        }

        void Step(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                Game.Tick();
                Look();
            }
        }

        void StepSeconds(float seconds) => Step(TestHelpers.Ticks(seconds));

        /// <summary>What one tick did to the player.</summary>
        void Look()
        {
            Player player = Game.Player;
            Vector3 at = player.Position, velocity = player.Velocity;
            Vector3 covered = (at - seen.Last) / Sim.Dt;
            seen.Last = at;
            if (seen.Skip)
            {
                // A fixture's teleport is not the prop's doing.
                seen.Skip = false;
                covered = Vector3.zero;
            }
            seen.Sideways = Mathf.Max(seen.Sideways, Mathf.Max(NotTheirOwn(velocity), NotTheirOwn(covered)));
            seen.Up = Mathf.Max(seen.Up, Mathf.Max(velocity.y, covered.y));
            seen.Moved = Mathf.Max(seen.Moved, Vector3.Distance(seen.From, at));

            bool inside = false;
            IReadOnlyList<Prop> props = Game.Props;
            for (int i = 0; i < props.Count; i++)
            {
                Prop prop = props[i];
                if (prop.Removed) continue;
                Mix(prop.Position);
                Quaternion rotation = prop.Rotation;
                Mix(rotation.x); Mix(rotation.y); Mix(rotation.z); Mix(rotation.w);
                if (prop.Held) continue;
                if (Ghost(prop))
                {
                    seen.Passed = true;
                    continue;
                }
                if (Depth(prop) <= InsideDepth * player.Scale) continue;
                inside = true;
                if (seen.InsideNow + 1 > seen.Inside) seen.InsideOf = prop.Name;
            }
            seen.InsideNow = inside ? seen.InsideNow + 1 : 0;
            seen.Inside = Mathf.Max(seen.Inside, seen.InsideNow);
            Mix(at);
            Mix(velocity);
        }

        /// <summary>The speed along the ground that is not the player's own walking, sprinting or jumping the way they face.</summary>
        float NotTheirOwn(Vector3 velocity)
        {
            var flat = new Vector2(velocity.x, velocity.z);
            if (own <= 0f) return flat.magnitude;
            float yaw = Game.Player.Yaw * Mathf.Deg2Rad;
            var heading = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            return (flat - heading * Mathf.Clamp(Vector2.Dot(flat, heading), 0f, own)).magnitude;
        }

        void Mix(Vector3 v)
        {
            Mix(v.x); Mix(v.y); Mix(v.z);
        }

        void Mix(float value)
        {
            unchecked
            {
                seen.Hash = (seen.Hash ^ (uint)BitConverter.SingleToInt32Bits(value)) * 16777619u;
            }
        }

        /// <summary>The prop does not collide with the player's capsule right now.</summary>
        bool Ghost(Prop prop)
        {
            foreach (Collider collider in prop.Colliders)
                if (collider != null && Physics.GetIgnoreCollision(collider, Game.Player.Collider)) return true;
            return false;
        }

        /// <summary>How deep the prop and the capsule overlap (0: not at all).</summary>
        float Depth(Prop prop)
        {
            Player player = Game.Player;
            float deepest = 0f;
            foreach (Collider collider in prop.Colliders)
            {
                if (collider == null || !collider.enabled || collider.isTrigger) continue;
                Transform t = collider.transform;
                if (Physics.ComputePenetration(collider, t.position, t.rotation, player.Collider, player.Position, Quaternion.identity, out _, out float depth))
                    deepest = Mathf.Max(deepest, depth);
            }
            return deepest;
        }

        static string F(float value, string format = "0.00") => value.ToString(format, CultureInfo.InvariantCulture);

        string Numbers() =>
            "sideways " + F(seen.Sideways) + ", up " + F(seen.Up) + ", moved " + F(seen.Moved) + ", inside a solid prop for " + seen.Inside + " ticks"
            + (seen.Passed ? ", passed" : ", solid throughout");

        /// <summary>The first two promises: not thrown, not stuck in something solid. `still`: the player gave no input, so any movement is the prop's.</summary>
        void Judge(string what, List<string> failures, bool still, float moved = 1.5f)
        {
            if (seen.Sideways >= ThrownSpeed) failures.Add(what + ": thrown sideways at " + F(seen.Sideways));
            if (seen.Up >= ThrownSpeed) failures.Add(what + ": thrown upward at " + F(seen.Up));
            if (seen.Inside > ASecond) failures.Add(what + ": inside " + seen.InsideOf + ", which was solid for them, for " + seen.Inside + " ticks");
            if (still && seen.Moved > moved) failures.Add(what + ": carried off by " + F(seen.Moved));
        }

        /// <summary>
        /// The other two promises. The player walks away (some way out of eight must take them 2.5 units in a
        /// second and a half); when the props rest and the player is out of them, each is solid and can be
        /// stood on (the player is put on top of it: a fixture's shortcut for climbing).
        /// </summary>
        void Afterwards(string what, List<string> failures, bool stand = true)
        {
            Input.Hold = default;
            own = Player.WalkSpeed;
            Player player = Game.Player;
            bool away = false;
            for (int way = 0; way < 8 && !away; way++)
            {
                Vector3 from = player.Position;
                player.Yaw = way * 45f;
                player.Pitch = 0f;
                Input.Hold.MoveZ = 1f;
                Step(90);
                Input.Hold = default;
                Step(6);
                Vector3 to = player.Position;
                away = new Vector2(to.x - from.x, to.z - from.z).magnitude >= 2.5f;
            }
            if (!away) failures.Add(what + ": cannot walk away (at " + player.Position.ToString("F2") + ")");

            // To rest.
            bool rest = false;
            for (int i = 0; i < 12 * ASecond && !rest; i++)
            {
                Step(1);
                rest = true;
                foreach (Prop prop in heavies)
                    rest &= prop.Removed || (prop.Body.linearVelocity.magnitude < 0.05f && prop.Body.angularVelocity.magnitude * prop.Radius < 0.05f);
            }
            if (!rest)
            {
                failures.Add(what + ": the props never came to rest");
                return;
            }

            foreach (Prop prop in heavies)
            {
                if (prop.Removed) continue;
                // Out of it, straight away from its middle.
                for (int i = 0; i < 10 * ASecond && (Ghost(prop) || Depth(prop) > 0f); i++)
                {
                    Vector3 out_ = player.Position - prop.Center;
                    player.Yaw = Mathf.Abs(out_.x) + Mathf.Abs(out_.z) < 1e-3f ? 0f : Mathf.Atan2(out_.x, out_.z) * Mathf.Rad2Deg;
                    Input.Hold.MoveZ = 1f;
                    Step(1);
                }
                Input.Hold = default;
                Step(10);
                if (Ghost(prop))
                {
                    failures.Add(what + ": " + prop.Name + " rests, the player is out of it (overlap " + F(Depth(prop)) + ") and it still passes through them");
                    continue;
                }
                if (!stand) continue;
                string stood = StandOn(prop);
                if (stood != null) failures.Add(what + ": " + stood);
            }
            if (seen.Sideways >= ThrownSpeed) failures.Add(what + ": thrown sideways at " + F(seen.Sideways) + " afterwards");
            if (seen.Up >= ThrownSpeed) failures.Add(what + ": thrown upward at " + F(seen.Up) + " afterwards");
            if (seen.Inside > ASecond) failures.Add(what + ": inside " + seen.InsideOf + ", solid, for " + seen.Inside + " ticks afterwards");
        }

        /// <summary>Puts the player on top of the prop and says what is wrong with standing there, or null.</summary>
        string StandOn(Prop prop)
        {
            Physics.SyncTransforms();
            Vector3 middle = prop.Center;
            float top = float.MinValue;
            Vector3 normal = Vector3.up;
            foreach (Collider collider in prop.Colliders)
            {
                if (collider == null || !collider.enabled || collider.isTrigger) continue;
                Bounds bounds = collider.bounds;
                var ray = new Ray(new Vector3(middle.x, bounds.max.y + 1f, middle.z), Vector3.down);
                if (!collider.Raycast(ray, out RaycastHit hit, bounds.size.y + 2f) || hit.point.y <= top) continue;
                top = hit.point.y;
                normal = hit.normal;
            }
            if (top == float.MinValue) return "no top of " + prop.Name + " was found";
            // Another of the props lies on top of it there (one domino on the other): that one's turn comes.
            foreach (Prop other in heavies)
            {
                if (other == prop || other.Removed) continue;
                foreach (Collider collider in other.Colliders)
                {
                    if (collider == null || !collider.enabled || collider.isTrigger) continue;
                    var ray = new Ray(new Vector3(middle.x, collider.bounds.max.y + 1f, middle.z), Vector3.down);
                    if (collider.Raycast(ray, out RaycastHit hit, collider.bounds.size.y + 2f) && hit.point.y > top) return null;
                }
            }
            // Leaning too steeply to stand on (a slab propped up against something): nothing to check.
            if (normal.y < Mathf.Cos(Player.MaxSlopeDegrees * Mathf.Deg2Rad)) return null;
            Game.Player.Teleport(new Vector3(middle.x, top + 0.25f, middle.z));
            seen.Skip = true;
            Step(90);
            Player player = Game.Player;
            if (Ghost(prop)) return prop.Name + " gave way under the player who was put on it";
            if (player.GroundProp != prop)
                return "the player cannot stand on " + prop.Name + " at rest (they are at " + player.Position.ToString("F2") + ", its top is at " + F(top)
                       + ", they stand on " + (player.Grounded ? player.GroundCollider.name : "nothing") + ")";
            return null;
        }

        string Signature()
        {
            var text = new StringBuilder();
            text.Append(seen.Hash.ToString("X8")).Append(' ').Append(Game.Player.Position.ToString("R"));
            foreach (Prop prop in Game.Props) text.Append(' ').Append(prop.Position.ToString("R")).Append(prop.Rotation.ToString("R"));
            return text.ToString();
        }

        static readonly string NotesPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/ToyboxHeavyPropAttacks.txt"));

        /// <summary>Appends a test's table to the notes (each test replaces its own section).</summary>
        static void Note(string title, List<string> lines)
        {
            try
            {
                string begin = "## " + title, text = File.Exists(NotesPath) ? File.ReadAllText(NotesPath) : "";
                int at = text.IndexOf(begin, StringComparison.Ordinal);
                if (at >= 0)
                {
                    int next = text.IndexOf("\n## ", at + begin.Length, StringComparison.Ordinal);
                    text = text.Remove(at, (next < 0 ? text.Length : next + 1) - at);
                }
                File.WriteAllText(NotesPath, text + begin + "\n" + string.Join("\n", lines) + "\n\n");
            }
            catch (IOException)
            {
                // The notes are a convenience; the test's verdict does not depend on them.
            }
        }

        void Finish(string title, List<string> lines, List<string> failures)
        {
            Note(title, lines);
            Assert.IsEmpty(failures, failures.Count + " of " + lines.Count + ":\n" + string.Join("\n", failures));
        }

        // ---- 1. A long plank that swings ---------------------------------------------------------------------------

        static readonly float[] Ratios = { 1.5f, 20f, 200f };

        /// <summary>
        /// A plank eight units long that spins flat about its middle: its end sweeps round into the player.
        /// On the floor it takes them at the foot; `raised` (on a pedestal) across the body.
        /// </summary>
        string Sweep(float ratio, float tipSpeed, bool raised, bool afterwards, List<string> failures)
        {
            var size = new Vector3(8f, 0.6f, 0.6f);
            Prop plank = null;
            Begin(ctx =>
            {
                TestHelpers.Floor(ctx, 200f);
                float y = size.y * 0.5f + 0.002f;
                if (raised)
                {
                    TestHelpers.Box(ctx, new Vector3(-3f, 0.4f, 0.75f), new Vector3(2f, 0.8f, 2f));
                    y += 0.8f;
                }
                // Its near face is 0.15 from the capsule; the player is three units from its middle.
                plank = Heavy(ctx.AddProp(BasicToys.Block(size), new Vector3(-3f, y, 0.75f), Weighing(ratio, size.x * size.y * size.z, 0.2f)));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            // A test fixture's shortcut for "it has been set spinning".
            plank.Body.angularVelocity = new Vector3(0f, tipSpeed / (size.x * 0.5f), 0f);
            StepSeconds(4f);
            string what = "a plank " + F(ratio, "0.#") + " x the player's mass sweeping round at " + F(tipSpeed, "0") + (raised ? ", at chest height" : ", along the floor");
            Judge(what, failures, true);
            string line = what + ": " + Numbers();
            if (afterwards) Afterwards(what, failures);
            string signature = Signature();
            End();
            return line + " | " + signature;
        }

        [Test]
        public void ALongPlankThatSwingsIntoThePlayer_DoesNotThrowThem([Values(false, true)] bool raised)
        {
            var failures = new List<string>();
            var lines = new List<string>();
            foreach (float ratio in Ratios)
                foreach (float tip in new[] { 2f, 6f, 12f, 25f })
                    lines.Add(Sweep(ratio, tip, raised, true, failures));
            Finish("A long plank swinging" + (raised ? " (chest height)" : " (on the floor)"), lines, failures);
        }

        // ---- 2. A tall plank or a domino that topples: the foot, the middle, the tip ------------------------------

        /// <summary>
        /// A beam that stands on its end `foot` in front of the player, leaning their way, and comes down.
        /// Near its foot it arrives slowly, at its tip at up to 20 units a second and edge first.
        /// </summary>
        string Topple(bool domino, float ratio, float length, float foot, bool afterwards, List<string> failures)
        {
            Prop beam = null;
            // Past its balance: a beam 0.6 thick and 8 tall tips at 4.3 degrees, the domino (0.3 to 2) at 8.5.
            float lean = domino ? 12f : 8f;
            Begin(ctx =>
            {
                TestHelpers.Floor(ctx, 200f);
                float sin = Mathf.Sin(lean * Mathf.Deg2Rad), cos = Mathf.Cos(lean * Mathf.Deg2Rad);
                Quaternion rotation = Quaternion.Euler(-lean, 0f, 0f);
                if (domino)
                {
                    // The real toy (1 x 2 x 0.3 at scale 1), as big as Level 4 makes it and bigger.
                    float scale = length / ToyFactory.DominoSize.y;
                    float half = length * 0.5f, depth = ToyFactory.DominoSize.z * scale * 0.5f;
                    var centre = new Vector3(0f, half * cos + depth * sin + 0.002f, foot - half * sin + depth * cos);
                    beam = Heavy(ToyCatalog.Add(ctx, ToyId.Domino, centre, rotation, scale, options: o =>
                    {
                        o.MaxScale = 80f;
                        o.Grabbable = false;
                    }));
                }
                else
                {
                    var size = new Vector3(0.6f, length, 0.6f);
                    float half = length * 0.5f, depth = 0.3f;
                    var centre = new Vector3(0f, half * cos + depth * sin + 0.002f, foot - half * sin + depth * cos);
                    beam = Heavy(ctx.AddProp(BasicToys.Block(size), centre, rotation, Weighing(ratio, size.x * size.y * size.z)));
                }
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            StepSeconds(5f);
            string what = (domino ? "a domino " + F(length, "0.#") + " tall (" + F(beam.Mass / Player.Mass, "0.#") + " x the player's mass)" : "a beam " + F(length, "0.#") + " tall, " + F(ratio, "0.#") + " x the player's mass,")
                          + " toppling from " + F(foot, "0.0#") + " away";
            if (Mathf.Abs(beam.Transform.up.y) > 0.5f) failures.Add(what + ": test setup: it still stands");
            Judge(what, failures, true);
            string line = what + ": " + Numbers();
            if (afterwards) Afterwards(what, failures);
            string signature = Signature();
            End();
            return line + " | " + signature;
        }

        [Test]
        public void ATallBeamThatTopplesOntoThePlayer_FootMiddleOrTip_DoesNotThrowThem()
        {
            var failures = new List<string>();
            var lines = new List<string>();
            const float length = 8f;
            foreach (float ratio in Ratios)
                // Near its foot, its middle, its tip, the tip grazing the player's front, and just out of reach.
                foreach (float foot in new[] { 0.8f, 4f, 7.5f, 8.2f, 8.5f })
                    lines.Add(Topple(false, ratio, length, foot, true, failures));
            Finish("A tall beam toppling", lines, failures);
        }

        [Test]
        public void ARealDominoThatTopplesOntoThePlayer_DoesNotThrowThem()
        {
            var failures = new List<string>();
            var lines = new List<string>();
            foreach (float height in new[] { 5f, 8f, 12f })
                foreach (float part in new[] { 0.15f, 0.5f, 0.92f })
                    lines.Add(Topple(true, 0f, height, 0.3f + part * (height - 0.3f), true, failures));
            Finish("A real domino toppling", lines, failures);
        }

        /// <summary>One domino knocks the next one down, and that one comes down on the player.</summary>
        string Chain(float height, bool afterwards, List<string> failures)
        {
            Prop first = null, second = null;
            const float lean = 12f;
            Begin(ctx =>
            {
                TestHelpers.Floor(ctx, 200f);
                float scale = height / ToyFactory.DominoSize.y;
                float half = height * 0.5f, depth = ToyFactory.DominoSize.z * scale * 0.5f;
                float sin = Mathf.Sin(lean * Mathf.Deg2Rad), cos = Mathf.Cos(lean * Mathf.Deg2Rad);
                Action<PropOptions> options = o =>
                {
                    o.MaxScale = 80f;
                    o.Grabbable = false;
                };
                // The second stands upright with the player at its middle's reach; the first leans onto it.
                float secondFoot = 0.3f + 0.5f * height, firstFoot = secondFoot + 0.55f * height;
                second = Heavy(ToyCatalog.Add(ctx, ToyId.Domino, new Vector3(0f, half + 0.002f, secondFoot + depth), Quaternion.identity, scale, options: options));
                first = Heavy(ToyCatalog.Add(ctx, ToyId.Domino, new Vector3(0f, half * cos + depth * sin + 0.002f, firstFoot - half * sin + depth * cos),
                    Quaternion.Euler(-lean, 0f, 0f), scale, options: options));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            StepSeconds(6f);
            string what = "two dominoes " + F(height, "0.#") + " tall, the first knocking the second onto the player";
            if (Mathf.Abs(second.Transform.up.y) > 0.5f) failures.Add(what + ": test setup: the second domino still stands");
            Judge(what, failures, true);
            string line = what + ": " + Numbers();
            if (afterwards) Afterwards(what, failures);
            string signature = Signature();
            End();
            return line + " | " + signature;
        }

        [Test]
        public void ADominoKnockedDownByAnother_DoesNotThrowThePlayer()
        {
            var failures = new List<string>();
            var lines = new List<string>();
            foreach (float height in new[] { 5f, 10f }) lines.Add(Chain(height, true, failures));
            Finish("A chain of two dominoes", lines, failures);
        }

        // ---- 3. A ball that rolls down a ramp at the player --------------------------------------------------------

        public enum Stand
        {
            /// <summary>On the floor at the foot of the ramp, open floor behind.</summary>
            AtTheFoot,
            /// <summary>On the ramp itself, in the ball's path.</summary>
            OnTheRamp,
            /// <summary>At the foot of the ramp with a wall right behind: the ball ends up in the notch they stand in.</summary>
            InTheNotch,
        }

        string RampBall(float ratio, float upRamp, Stand stand, bool afterwards, List<string> failures)
        {
            const float angle = 25f, radius = 1f, rampStart = 1.5f;
            Prop ball = null;
            float sin = Mathf.Sin(angle * Mathf.Deg2Rad), cos = Mathf.Cos(angle * Mathf.Deg2Rad);
            var along = new Vector3(0f, sin, cos);
            var normal = new Vector3(0f, cos, -sin);
            Begin(ctx =>
            {
                TestHelpers.Floor(ctx, 200f);
                TestHelpers.Ramp(ctx, angle, rampStart, 16f, 8f);
                // The wall the ball ends against: right behind the player, or across the room.
                float wall = stand == Stand.InTheNotch ? -(Player.BaseRadius + 0.06f) : -12f;
                TestHelpers.Box(ctx, new Vector3(0f, 3f, wall - 0.5f), new Vector3(30f, 6f, 1f));
                ball = Heavy(ctx.AddProp(BasicToys.Ball(radius), new Vector3(0f, 0f, rampStart) + along * upRamp + normal * (radius + 0.002f),
                    Weighing(ratio, 4f / 3f * Mathf.PI * radius * radius * radius)));
                Vector3 feet = stand == Stand.OnTheRamp ? new Vector3(0f, 0f, rampStart) + along * 1.5f + Vector3.up * 0.02f : Vector3.zero;
                ctx.SetSpawn(feet, 0f);
            });
            StepSeconds(6f);
            string what = "a ball " + F(ratio, "0.#") + " x the player's mass rolling " + F(upRamp, "0") + " down a ramp at a player "
                          + (stand == Stand.AtTheFoot ? "at its foot" : stand == Stand.OnTheRamp ? "on the ramp" : "between its foot and a wall");
            Judge(what, failures, true);
            string line = what + ": " + Numbers() + ", the ball ended at " + ball.Center.ToString("F1");
            if (afterwards) Afterwards(what, failures);
            string signature = Signature();
            End();
            return line + " | " + signature;
        }

        [Test]
        public void ABallThatRollsDownARampAtThePlayer_DoesNotThrowThem([Values] Stand stand)
        {
            var failures = new List<string>();
            var lines = new List<string>();
            foreach (float ratio in Ratios)
                foreach (float upRamp in new[] { 3f, 8f, 14f })
                    lines.Add(RampBall(ratio, upRamp, stand, true, failures));
            Finish("A ball down a ramp (" + stand + ")", lines, failures);
        }

        // ---- 4. Dropped from a height onto a player who walks ------------------------------------------------------

        /// <summary>
        /// A block let go high above the player's path, timed to come down where they will be (`offset`:
        /// that much ahead of them or behind), while they walk or sprint on.
        /// </summary>
        string DropOnWalker(float ratio, float height, float offset, bool sprint, bool afterwards, List<string> failures)
        {
            const float size = 2f;
            float speed = sprint ? Player.SprintSpeed : Player.WalkSpeed;
            float fall = Mathf.Sqrt(2f * height / Game.Gravity);
            Begin(ctx =>
            {
                TestHelpers.Floor(ctx, 200f);
                Heavy(ctx.AddProp(BasicToys.Block(size), new Vector3(0f, Player.BaseHeight + height + size * 0.5f, speed * fall + offset), Weighing(ratio, size * size * size)));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            own = speed;
            Input.Hold.MoveZ = 1f;
            Input.Hold.Sprint = sprint;
            StepSeconds(fall + 2f);
            Input.Hold = default;
            string what = "a block " + F(ratio, "0.#") + " x the player's mass dropped from " + F(height, "0") + " onto a player who " + (sprint ? "sprints" : "walks")
                          + ", " + F(offset, "+0.0;-0.0") + " from where they will be";
            Judge(what, failures, false);
            if (Mathf.Abs(Game.Player.Position.x) > 1.5f) failures.Add(what + ": knocked " + F(Game.Player.Position.x) + " off their line");
            string line = what + ": " + Numbers() + ", the player ended at " + Game.Player.Position.ToString("F1");
            if (afterwards) Afterwards(what, failures);
            string signature = Signature();
            End();
            return line + " | " + signature;
        }

        [Test]
        public void AHeavyPropDroppedFromAHeightOntoAWalkingPlayer_DoesNotThrowThem([Values(false, true)] bool sprint)
        {
            var failures = new List<string>();
            var lines = new List<string>();
            foreach (float ratio in Ratios)
                foreach (float height in new[] { 1.5f, 8f, 30f })
                    // Its far edge on their head, squarely, its near edge, and coming down right in front of their feet.
                    foreach (float offset in new[] { -1.2f, 0f, 1.2f, 1.6f })
                        lines.Add(DropOnWalker(ratio, height, offset, sprint, offset == 0f, failures));
            Finish("Dropped onto a player who " + (sprint ? "sprints" : "walks"), lines, failures);
        }

        // ---- 5. A toy that grew in the hand, let go point-blank -----------------------------------------------------

        /// <summary>
        /// The real mechanic: a small toy is picked up from an arm's length, the player turns round and looks
        /// up or down at `pitch`, and lets it go. Held against the ceiling or the far wall it has grown to
        /// tens of times the player's mass; let go overhead it comes down on them, at their feet round them.
        /// </summary>
        string GrowAndLetGo(float pitch, float density, bool afterwards, List<string> failures)
        {
            Prop toy = null;
            Begin(ctx =>
            {
                TestHelpers.Room(ctx, 6f, 5f);
                // A ceiling at 5.
                TestHelpers.Box(ctx, new Vector3(0f, 5.5f, 0f), new Vector3(14f, 1f, 14f));
                TestHelpers.Box(ctx, new Vector3(0f, 0.6f, 0.9f), new Vector3(0.5f, 1.2f, 0.5f));
                toy = Heavy(ctx.AddProp(BasicToys.Block(0.4f), new Vector3(0f, 1.402f, 0.9f), new PropOptions { Density = density }));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Step(10);
            LookAt(toy.Center);
            Input.Once.GrabPressed = true;
            Step(1);
            Assert.AreSame(toy, Game.Grabber.Held, "test setup: the toy was picked up");
            // Turning round and looking up or down: a test fixture's shortcut for moving the mouse.
            Game.Player.Yaw = 180f;
            Game.Player.Pitch = pitch;
            Step(6);
            float held = toy.Scale;
            Input.Once.GrabPressed = true;
            Step(1);
            Assert.IsNull(Game.Grabber.Held, "test setup: the toy was let go");
            seen = new Seen { From = Game.Player.Position, Last = Game.Player.Position };
            StepSeconds(3f);
            string what = "a toy grown " + F(held, "0.0") + " times in the hand (" + F(toy.Mass / Player.Mass, "0.#") + " x the player's mass) let go looking " + F(pitch, "0") + " degrees "
                          + (pitch >= 0f ? "up" : "down");
            Judge(what, failures, true);
            string line = what + ": " + Numbers() + ", it lies at " + toy.Center.ToString("F1");
            if (afterwards) Afterwards(what, failures);
            string signature = Signature();
            End();
            return line + " | " + signature;
        }

        [Test]
        public void AToyThatGrewInTheHand_LetGoOverheadOrAtTheFeet_DoesNotThrowThePlayer()
        {
            var failures = new List<string>();
            var lines = new List<string>();
            foreach (float density in new[] { 30f, 400f })
                foreach (float pitch in new[] { 89f, 82f, 76f, 70f, 55f, 0f, -35f, -55f, -70f, -85f })
                    lines.Add(GrowAndLetGo(pitch, density, true, failures));
            Finish("A toy grown in the hand", lines, failures);
        }

        // ---- 6. The player jumps into a heavy prop that moves ------------------------------------------------------

        public enum Way
        {
            /// <summary>The block slides at the player.</summary>
            Toward,
            /// <summary>It slides away from them.</summary>
            Away,
            /// <summary>A long block slides across their way; they jump into its side.</summary>
            Across,
            /// <summary>A long low block slides across their way; they jump onto it.</summary>
            AcrossLow,
        }

        string JumpInto(Way way, float ratio, float speed, bool afterwards, List<string> failures)
        {
            Prop block = null;
            Vector3 velocity = Vector3.zero;
            bool dragged = true;
            Begin(ctx =>
            {
                TestHelpers.Floor(ctx, 400f);
                Vector3 size, centre;
                switch (way)
                {
                    case Way.Toward:
                        size = new Vector3(2f, 2f, 2f);
                        centre = new Vector3(0f, 1.002f, 3.6f + speed * 0.45f + 1f);
                        velocity = new Vector3(0f, 0f, -speed);
                        break;
                    case Way.Away:
                        size = new Vector3(2f, 2f, 2f);
                        centre = new Vector3(0f, 1.002f, 2.2f + 1f);
                        velocity = new Vector3(0f, 0f, speed);
                        break;
                    case Way.Across:
                        size = new Vector3(40f, 2f, 2f);
                        centre = new Vector3(-10f, 1.002f, 3.6f + 1f);
                        velocity = new Vector3(speed, 0f, 0f);
                        break;
                    default:
                        size = new Vector3(40f, 0.8f, 4f);
                        centre = new Vector3(-10f, 0.402f, 3.2f + 2f);
                        velocity = new Vector3(speed, 0f, 0f);
                        break;
                }
                block = Heavy(ctx.AddProp(BasicToys.Block(size), centre, Weighing(ratio, size.x * size.y * size.z)));
                // Something drags it along at a steady speed until they have met (a test fixture's shortcut for
                // a sled, a raft, a slope): friction would have stopped it before the player got there.
                ctx.OnUpdate(dt =>
                {
                    if (dragged && !block.Removed) block.Body.linearVelocity = new Vector3(velocity.x, block.Body.linearVelocity.y, velocity.z);
                });
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            own = Player.SprintSpeed;
            Input.Hold.MoveZ = 1f;
            Input.Hold.Sprint = true;
            Step(12);
            Input.Once.Jump = true;
            StepSeconds(1.5f);
            Input.Hold = default;
            dragged = false;
            StepSeconds(1.5f);
            string what = "sprint-jumping into a block " + F(ratio, "0.#") + " x the player's mass that slides " + way + " at " + F(speed, "0.#");
            Judge(what, failures, false);
            string line = what + ": " + Numbers() + ", the player ended at " + Game.Player.Position.ToString("F1") + " on " + (Game.Player.GroundProp != null ? "the block" : "the floor");
            if (afterwards) Afterwards(what, failures, false);
            string signature = Signature();
            End();
            return line + " | " + signature;
        }

        [Test]
        public void ThePlayerWhoJumpsIntoAMovingHeavyProp_IsNotThrown([Values] Way way)
        {
            var failures = new List<string>();
            var lines = new List<string>();
            foreach (float ratio in Ratios)
                foreach (float speed in new[] { 0.8f, 3f, 6f, 12f, 20f })
                    lines.Add(JumpInto(way, ratio, speed, true, failures));
            Finish("Jumping into a moving block (" + way + ")", lines, failures);
        }

        // ---- 7. Pinned between a heavy prop and a wall -------------------------------------------------------------

        string Pin(bool ball, bool corner, float ratio, float speed, bool afterwards, List<string> failures)
        {
            // Fast ones come from a little way off; slow ones are already there, so that they arrive that slowly.
            const float size = 2f, friction = 0.6f;
            float gap = speed < PerspectiveGrabber.PassSpeed ? 0.01f : 0.4f;
            Prop prop = null;
            Vector3 velocity = Vector3.zero, spin = Vector3.zero;
            Begin(ctx =>
            {
                TestHelpers.Floor(ctx, 200f);
                float back = -(Player.BaseRadius + 0.06f);
                TestHelpers.Box(ctx, new Vector3(0f, 3f, back - 0.5f), new Vector3(30f, 6f, 1f));
                if (corner) TestHelpers.Box(ctx, new Vector3(back - 0.5f, 3f, 5f), new Vector3(1f, 6f, 12f));
                // In the corner the block runs along the side wall, a hair clear of it.
                float x = corner ? back + 0.02f + size * 0.5f : 0f;
                if (ball)
                {
                    PropOptions rolling = Weighing(ratio, 4f / 3f * Mathf.PI * 0.125f * size * size * size);
                    // Rolling resistance, so that the ball comes to rest at all.
                    rolling.AngularDamping = 1.5f;
                    prop = Heavy(ctx.AddProp(BasicToys.Ball(size * 0.5f), new Vector3(x, size * 0.5f + 0.002f, Player.BaseRadius + gap + size * 0.5f), rolling));
                    velocity = new Vector3(0f, 0f, -speed);
                    spin = new Vector3(-speed / (size * 0.5f), 0f, 0f);
                }
                else
                {
                    prop = Heavy(ctx.AddProp(BasicToys.Block(size), new Vector3(x, size * 0.5f + 0.002f, Player.BaseRadius + gap + size * 0.5f), Weighing(ratio, size * size * size, friction)));
                    velocity = new Vector3(0f, 0f, -Mathf.Sqrt(speed * speed + 2f * friction * Game.Gravity * gap));
                }
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            prop.Body.linearVelocity = velocity;
            prop.Body.angularVelocity = spin;
            StepSeconds(3f);
            string what = (ball ? "a ball " : "a block ") + F(ratio, "0.#") + " x the player's mass at " + F(speed, "0.#") + " against a player with their back to a " + (corner ? "corner" : "wall");
            Judge(what, failures, true);
            string line = what + ": " + Numbers() + ", it ended at z " + F(prop.Center.z) + ", the player at " + Game.Player.Position.ToString("F2");
            if (afterwards) Afterwards(what, failures);
            string signature = Signature();
            End();
            return line + " | " + signature;
        }

        [Test]
        public void PinnedBetweenAHeavyPropAndAWall_ThePlayerIsNotCrushedThrownOrTrapped([Values(false, true)] bool ball, [Values(false, true)] bool corner)
        {
            var failures = new List<string>();
            var lines = new List<string>();
            foreach (float ratio in Ratios)
                foreach (float speed in new[] { 0.2f, 0.5f, 0.9f, 3f, 12f })
                    lines.Add(Pin(ball, corner, ratio, speed, true, failures));
            Finish("Pinned by a " + (ball ? "ball" : "block") + (corner ? " in a corner" : " against a wall"), lines, failures);
        }

        // ---- 8. Standing on a heavy prop that is hit by another ----------------------------------------------------

        public enum Blow
        {
            /// <summary>A block slides into the side of the one the player stands on.</summary>
            Slide,
            /// <summary>A ball rolls into it.</summary>
            Roll,
            /// <summary>A block comes down on it, beside the player.</summary>
            Fall,
            /// <summary>The player stands on the low end of a plank across a fulcrum; a block comes down on the high end.</summary>
            Catapult,
        }

        string HitTheStand(Blow blow, float standRatio, float ratio, float speed, bool afterwards, List<string> failures)
        {
            Prop stand = null, hitter = null;
            Vector3 velocity = Vector3.zero, spin = Vector3.zero;
            const float friction = 0.6f, gap = 0.1f;
            Begin(ctx =>
            {
                TestHelpers.Floor(ctx, 200f);
                if (blow == Blow.Catapult)
                {
                    // Eight long on a fulcrum 0.6 high, tilted so that the player's end is on the floor.
                    var plank = new Vector3(1.5f, 0.3f, 8f);
                    TestHelpers.Box(ctx, new Vector3(0f, 0.3f, 0f), new Vector3(3f, 0.6f, 0.4f));
                    float tilt = Mathf.Asin(0.6f / 4f) * Mathf.Rad2Deg;
                    stand = Heavy(ctx.AddProp(BasicToys.Block(plank), new Vector3(0f, 0.6f + 0.16f, 0f), Quaternion.Euler(-tilt, 0f, 0f), Weighing(standRatio, plank.x * plank.y * plank.z)));
                    float height = speed * speed / (2f * Game.Gravity);
                    hitter = Heavy(ctx.AddProp(BasicToys.Block(1f), new Vector3(0f, 1.5f + height + 0.5f, 3.2f), Weighing(ratio, 1f)));
                    ctx.SetSpawn(new Vector3(0f, 0.5f, -3.2f), 0f);
                    return;
                }
                var size = new Vector3(4f, 1f, 4f);
                stand = Heavy(ctx.AddProp(BasicToys.Block(size), new Vector3(0f, 0.502f, 0f), Weighing(standRatio, size.x * size.y * size.z, friction)));
                switch (blow)
                {
                    case Blow.Slide:
                        hitter = Heavy(ctx.AddProp(BasicToys.Block(2f), new Vector3(-(2f + gap + 1f), 1.002f, 0f), Weighing(ratio, 8f, friction)));
                        velocity = new Vector3(Mathf.Sqrt(speed * speed + 2f * friction * Game.Gravity * gap), 0f, 0f);
                        break;
                    case Blow.Roll:
                    {
                        PropOptions rolling = Weighing(ratio, 4f / 3f * Mathf.PI);
                        // Rolling resistance, so that the ball comes to rest at all.
                        rolling.AngularDamping = 1.5f;
                        hitter = Heavy(ctx.AddProp(BasicToys.Ball(1f), new Vector3(-(2f + gap + 1f), 1.002f, 0f), rolling));
                        velocity = new Vector3(speed, 0f, 0f);
                        spin = new Vector3(0f, 0f, -speed);
                        break;
                    }
                    default:
                    {
                        // Beside the player on the block, from the height that makes it arrive at this speed.
                        float height = speed * speed / (2f * Game.Gravity);
                        hitter = Heavy(ctx.AddProp(BasicToys.Block(1f), new Vector3(1.1f, 1f + height + 0.5f + 0.004f, 0f), Weighing(ratio, 1f)));
                        break;
                    }
                }
                ctx.SetSpawn(new Vector3(0f, 1.05f, 0f), 0f);
            });
            if (blow != Blow.Catapult && blow != Blow.Fall)
            {
                // Standing on it first, then it is set going.
                Step(20);
                Assert.AreSame(stand, Game.Player.GroundProp, "test setup: the player stands on the block");
                seen = new Seen { From = Game.Player.Position, Last = Game.Player.Position };
            }
            hitter.Body.linearVelocity = velocity;
            hitter.Body.angularVelocity = spin;
            StepSeconds(5f);
            string what = "standing on a prop " + F(standRatio, "0.#") + " x the player's mass that is hit (" + blow + ") by one of " + F(ratio, "0.#") + " x at " + F(speed, "0");
            // Carried is not thrown: the distance is not held against it, the speed is.
            Judge(what, failures, false);
            string line = what + ": " + Numbers() + ", the player ended at " + Game.Player.Position.ToString("F1") + ", what they stood on moved to " + stand.Center.ToString("F1");
            if (afterwards) Afterwards(what, failures);
            string signature = Signature();
            End();
            return line + " | " + signature;
        }

        [Test]
        public void StandingOnAHeavyPropThatIsHitByAnother_ThePlayerIsNotThrown([Values] Blow blow)
        {
            var failures = new List<string>();
            var lines = new List<string>();
            foreach (float standRatio in new[] { 5f, 60f })
                foreach (float ratio in new[] { 20f, 200f })
                    foreach (float speed in new[] { 3f, 12f, 25f })
                        lines.Add(HitTheStand(blow, standRatio, ratio, speed, true, failures));
            Finish("Standing on a prop that is hit (" + blow + ")", lines, failures);
        }

        // ---- 9. A heavy prop at rest beside the player, struck by another ------------------------------------------

        /// <summary>
        /// A block rests beside the player, touching them or a hand's width away; another slides into its
        /// far side. Nothing moves toward the player when the step begins: the step itself sets the near
        /// block going and has it hit them, so it cannot be seen coming.
        /// </summary>
        string Cradle(float nearRatio, float ratio, float speed, float gap, bool afterwards, List<string> failures)
        {
            const float size = 2f, friction = 0.6f, run = 0.1f;
            Prop hitter = null;
            Begin(ctx =>
            {
                TestHelpers.Floor(ctx, 200f);
                float face = -(Player.BaseRadius + gap);
                Heavy(ctx.AddProp(BasicToys.Block(size), new Vector3(face - size * 0.5f, size * 0.5f + 0.002f, 0f), Weighing(nearRatio, size * size * size, friction)));
                hitter = Heavy(ctx.AddProp(BasicToys.Block(size), new Vector3(face - size - run - size * 0.5f, size * 0.5f + 0.002f, 0f), Weighing(ratio, size * size * size, friction)));
                ctx.SetSpawn(Vector3.zero, 0f);
            });
            Step(10);
            seen = new Seen { From = Game.Player.Position, Last = Game.Player.Position };
            // A test fixture's shortcut for "it is on its way".
            hitter.Body.linearVelocity = new Vector3(Mathf.Sqrt(speed * speed + 2f * friction * Game.Gravity * run), 0f, 0f);
            StepSeconds(4f);
            string what = "a block " + F(nearRatio, "0.#") + " x the player's mass resting " + F(gap, "0.###") + " from them, struck by one of " + F(ratio, "0.#") + " x at " + F(speed, "0");
            Judge(what, failures, true);
            string line = what + ": " + Numbers() + ", the player ended at " + Game.Player.Position.ToString("F2");
            if (afterwards) Afterwards(what, failures);
            string signature = Signature();
            End();
            return line + " | " + signature;
        }

        [Test]
        public void AHeavyPropAtRestBesideThePlayer_StruckByAnother_DoesNotThrowThem()
        {
            var failures = new List<string>();
            var lines = new List<string>();
            // The block in between may itself be lighter than the player: it is the heavy one behind it that throws.
            foreach (float nearRatio in new[] { 0.5f, 0.9f, 1.5f, 20f })
                foreach (float ratio in new[] { 20f, 200f })
                    foreach (float speed in new[] { 3f, 12f, 25f })
                        foreach (float gap in new[] { 0.005f, 0.08f })
                            lines.Add(Cradle(nearRatio, ratio, speed, gap, true, failures));
            Finish("A resting block struck by another", lines, failures);
        }

        // ---- What must still work ---------------------------------------------------------------------------------

        /// <summary>
        /// A platform that a gadget moves launches its rider when it shoots up and stops (Level 7's seesaw
        /// does, at 19): that is not a prop's throw and is not taken back - not even with a heavy toy on the
        /// same platform, right beside the player, going up with them.
        /// </summary>
        [Test]
        public void AKinematicPlatformStillLaunchesItsRider_WithAHeavyToyBesideThem()
        {
            Mover platform = null;
            Prop toy = null;
            var at = new Vector3(0f, 0.25f, 0f);
            float speed = 0f;
            Begin(ctx =>
            {
                TestHelpers.Floor(ctx, 200f);
                platform = ctx.AddKinematic(BasicToys.Slab(new Vector3(6f, 0.5f, 6f)), at);
                ctx.OnUpdate(dt =>
                {
                    at += Vector3.up * (speed * dt);
                    platform.MoveTo(at);
                });
                toy = Heavy(ctx.AddProp(BasicToys.Block(1f), new Vector3(Player.BaseRadius + 0.01f + 0.5f, 1.002f, 0f), Weighing(20f, 1f)));
                ctx.SetSpawn(new Vector3(0f, 0.52f, 0f), 0f);
            });
            Step(30);
            Assert.AreSame(platform.Body, Game.Player.GroundCollider.attachedRigidbody, "test setup: the player rides the platform");
            float rest = Game.Player.Position.y;
            speed = 12f;
            Step(15);
            speed = 0f;
            float highest = rest, fastest = 0f;
            for (int i = 0; i < TestHelpers.Ticks(2f); i++)
            {
                Step(1);
                highest = Mathf.Max(highest, Game.Player.Position.y);
                fastest = Mathf.Max(fastest, Game.Player.Velocity.y);
            }
            Assert.AreEqual(3f, platform.Position.y - 0.25f, 0.05f, "test setup: the platform went up 3 and stopped");
            Assert.Greater(seen.Up, 11f, "the platform took its rider up at its own speed");
            Assert.Greater(highest - rest, 3f + 2f, "and threw them on when it stopped");
            Assert.Greater(toy.Mass, Player.Mass * 10f);
        }

        /// <summary>
        /// A bounce off a trampoline leaves the player much faster than they were going by themselves, and
        /// upward. That is the trampoline's doing: a heavy ball that happens to roll past them at that
        /// moment has thrown nobody, and the bounce is not taken back.
        /// </summary>
        [Test]
        public void ABounceOffATrampoline_IsNotTakenBack_BecauseAHeavyBallRollsPast([Values(false, true)] bool withBall)
        {
            var bouncy = new PhysicsMaterial("Trampoline") { bounciness = 0.8f, bounceCombine = PhysicsMaterialCombine.Maximum };
            try
            {
                Prop ball = null;
                Begin(ctx =>
                {
                    TestHelpers.Floor(ctx, 200f);
                    GameObject pad = TestHelpers.Box(ctx, new Vector3(0f, 0.25f, 0f), new Vector3(4f, 0.5f, 4f));
                    pad.GetComponent<Collider>().sharedMaterial = bouncy;
                    // Rolling along the pad, a hand's width beside where the player comes down, and there just then
                    // (the fall of 3 units takes 0.52 s).
                    if (withBall)
                        ball = Heavy(ctx.AddProp(BasicToys.Ball(0.5f), new Vector3(Player.BaseRadius + 0.1f + 0.5f, 1.002f, -1.57f), Weighing(20f, 4f / 3f * Mathf.PI * 0.125f)));
                    ctx.SetSpawn(new Vector3(0f, 3.5f, 0f), 0f);
                });
                if (withBall)
                {
                    ball.Body.linearVelocity = new Vector3(0f, 0f, 3f);
                    ball.Body.angularVelocity = new Vector3(3f / 0.5f, 0f, 0f);
                }
                float nearest = float.MaxValue;
                bool bounced = false;
                for (int i = 0; i < TestHelpers.Ticks(2f) && !bounced; i++)
                {
                    Step(1);
                    bounced = Game.Player.Velocity.y > 1f;
                    if (withBall) nearest = Mathf.Min(nearest, new Vector2(ball.Center.x - Game.Player.Position.x, ball.Center.z - Game.Player.Position.z).magnitude - 0.5f - Player.BaseRadius);
                }
                Assert.IsTrue(bounced, "never bounced");
                if (withBall)
                {
                    Assert.Less(Mathf.Abs(ball.Center.z), 0.5f, "test setup: the ball is beside the player when they bounce (it is at " + ball.Center.ToString("F2") + ")");
                    Assert.Less(nearest, 0.2f, "test setup: and close by");
                    Assert.Greater(ball.Velocity.magnitude, 2f, "test setup: and rolling");
                }
                float highest = 0f;
                for (int i = 0; i < TestHelpers.Ticks(1f); i++)
                {
                    Step(1);
                    highest = Mathf.Max(highest, Game.Player.Position.y);
                }
                // Falling 3 units and keeping 0.8 of the speed gives 0.64 of the height back.
                Assert.Greater(highest, 0.5f + 1.4f, "the bounce was swallowed");
                Assert.Less(highest, 0.5f + 2.2f);
                if (withBall) Assert.IsFalse(seen.Passed, "the ball that rolled past was never anything but solid");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(bouncy);
            }
        }

        // ---- Twice the same ----------------------------------------------------------------------------------------

        /// <summary>One run of every attack, twice: the same positions to the last bit, tick by tick (the hash) and at the end.</summary>
        [Test]
        public void EveryAttack_PlayedTwice_GivesTheSamePositions()
        {
            var runs = new List<Func<List<string>, string>>
            {
                f => Sweep(20f, 12f, false, true, f),
                f => Sweep(200f, 6f, true, true, f),
                f => Topple(false, 20f, 8f, 7.5f, true, f),
                f => Topple(true, 0f, 8f, 4f, true, f),
                f => Chain(5f, true, f),
                f => RampBall(20f, 8f, Stand.AtTheFoot, true, f),
                f => RampBall(200f, 14f, Stand.InTheNotch, true, f),
                f => RampBall(20f, 8f, Stand.OnTheRamp, true, f),
                f => DropOnWalker(20f, 8f, 0f, false, true, f),
                f => DropOnWalker(200f, 30f, 1.2f, true, true, f),
                f => GrowAndLetGo(89f, 30f, true, f),
                f => GrowAndLetGo(-70f, 400f, true, f),
                f => JumpInto(Way.Toward, 20f, 6f, true, f),
                f => JumpInto(Way.AcrossLow, 200f, 12f, true, f),
                f => Pin(false, true, 200f, 0.9f, true, f),
                f => Pin(true, false, 20f, 12f, true, f),
                f => HitTheStand(Blow.Slide, 5f, 200f, 12f, true, f),
                f => HitTheStand(Blow.Catapult, 60f, 200f, 12f, true, f),
                f => Cradle(20f, 200f, 12f, 0.005f, true, f),
                f => Cradle(0.5f, 200f, 25f, 0.08f, true, f),
            };
            var differences = new List<string>();
            foreach (Func<List<string>, string> run in runs)
            {
                var ignored = new List<string>();
                string first = run(ignored), second = run(ignored);
                if (first != second) differences.Add(first + "\n  and then\n" + second);
            }
            Assert.IsEmpty(differences, differences.Count + " of " + runs.Count + " attacks came out differently the second time:\n" + string.Join("\n", differences));
        }
    }
}
