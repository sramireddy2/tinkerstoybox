using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Toybox.Engine
{
    /// <summary>
    /// Pumps a bot script. A script is an iterator that yields once per tick; yielding another IEnumerator
    /// runs it as a sub-command without spending a tick on the hand-over.
    ///
    /// Tests use Run, which ticks the game itself. The real game loop instead installs the runner as the
    /// game's input source (Attach): the script then advances exactly once per tick, however the loop paces
    /// its ticks, which replays the solution in real time.
    ///
    /// The runner is also what makes "the bot cannot cheat" true. A script runs between ticks, when nothing
    /// in the simulation moves, so the runner compares the simulation before and after every step of the
    /// script; if anything differs, the script has changed it directly instead of through input, and the
    /// run fails with a BotException.
    /// </summary>
    public sealed class BotRunner : IInputSource
    {
        /// <summary>Sub-commands a script may start or finish in a row without a tick passing.</summary>
        public const int MaxStepsWithoutATick = 10000;

        readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        readonly Bot bot;
        readonly Game game;

        /// <param name="bot">Whose frames Sample hands out (needed for Attach; null when only Advance is used).</param>
        /// <param name="game">The game to guard against a script that changes it directly; null for no guard.</param>
        public BotRunner(IEnumerator script, Bot bot = null, Game game = null)
        {
            if (script == null) throw new ArgumentNullException(nameof(script));
            stack.Push(script);
            this.bot = bot;
            this.game = game;
        }

        /// <summary>True once the script has ended, normally or by failing.</summary>
        public bool Finished => stack.Count == 0;
        /// <summary>What ended the script, if it did not end by itself.</summary>
        public Exception Error { get; private set; }
        public bool Failed => Error != null;

        /// <summary>
        /// Runs the script up to its next yield, which leaves the bot's input for the coming tick in place.
        /// Returns false once the script has ended. An exception thrown by the script passes through, and
        /// the script is over: a failed command is never skipped and carried on from.
        /// </summary>
        public bool Advance()
        {
            if (stack.Count == 0) return false;
            Fingerprint before = game != null ? Fingerprint.Of(game) : default;
            bool running;
            try
            {
                running = Pump();
                if (game != null)
                {
                    string changed = before.DifferenceTo(Fingerprint.Of(game));
                    if (changed != null)
                        throw new BotException("The bot script changed " + changed + " directly. A script may only act through bot commands " +
                                               "(it may read bot.Game and bot.Player, not move, scale or complete anything).");
                }
            }
            catch (Exception e)
            {
                stack.Clear();
                Error = e;
                throw;
            }
            return running;
        }

        bool Pump()
        {
            int steps = 0;
            while (stack.Count > 0)
            {
                if (++steps > MaxStepsWithoutATick)
                    throw new BotException("The bot script ran " + MaxStepsWithoutATick + " commands in a row without a tick passing. " +
                                           "A loop of commands that end at once (LookAt when already aligned, Wait(0)) never lets the game run.");
                IEnumerator top = stack.Peek();
                if (!top.MoveNext())
                {
                    stack.Pop();
                    continue;
                }
                if (top.Current is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }
                return true;
            }
            return false;
        }

        /// <summary>IInputSource: one script step per tick, then the bot's frame for that tick.</summary>
        public InputFrame Sample()
        {
            Advance();
            return bot != null ? bot.Sample() : default;
        }

        /// <summary>Makes the game pace the script: every game tick advances it by one step.</summary>
        public static BotRunner Attach(Game game, Bot bot, IEnumerator script)
        {
            var runner = new BotRunner(script, bot, game);
            game.Input = runner;
            return runner;
        }

        /// <summary>
        /// Runs a script to its end, one game tick per yield. The script's bot must be the game's input
        /// source (the Bot constructor sees to that). Throws TimeoutException if it runs too long, and
        /// BotException if a command fails, if the script touches the simulation directly, or if it created
        /// commands it never ran (a missing `yield return`).
        /// </summary>
        public static void Run(Game game, IEnumerator script, float timeoutSeconds = 120f)
        {
            var bot = game.Input as Bot;
            int unstartedBefore = bot != null ? bot.UnstartedCommands : 0;
            var runner = new BotRunner(script, bot, game);
            int limit = (int)Math.Ceiling(timeoutSeconds / Sim.Dt);
            int ticks = 0;
            while (runner.Advance())
            {
                if (ticks++ >= limit)
                    throw new TimeoutException("The bot script was still running after " + timeoutSeconds.ToString("0.#") +
                                               " s (bot at " + game.Player.Position + ").");
                game.Tick();
            }
            if (bot != null && bot.UnstartedCommands > unstartedBefore)
                throw new BotException((bot.UnstartedCommands - unstartedBefore) + " bot command(s) were created but never run. " +
                                       "A command only acts when the script yields it: is a 'yield return' missing?");
        }

        /// <summary>What a script must leave alone, boiled down to three numbers.</summary>
        readonly struct Fingerprint
        {
            readonly ulong player, props, level;

            Fingerprint(ulong player, ulong props, ulong level)
            {
                this.player = player;
                this.props = props;
                this.level = level;
            }

            public static Fingerprint Of(Game game)
            {
                Player p = game.Player;
                ulong player = Seed;
                Add(ref player, p.Position);
                Add(ref player, p.Velocity);
                Add(ref player, p.Yaw);
                Add(ref player, p.Pitch);
                Add(ref player, p.Scale);
                Add(ref player, p.CheckpointPosition);

                ulong props = Seed;
                IReadOnlyList<Prop> all = game.Props;
                Add(ref props, all.Count);
                for (int i = 0; i < all.Count; i++)
                {
                    Prop prop = all[i];
                    Add(ref props, prop.Id);
                    Add(ref props, (prop.Removed ? 1 : 0) | (prop.Held ? 2 : 0) | (prop.Grabbable ? 4 : 0));
                    if (prop.Removed) continue;
                    Add(ref props, prop.Position);
                    Quaternion rotation = prop.Rotation;
                    Add(ref props, new Vector3(rotation.x, rotation.y, rotation.z));
                    Add(ref props, rotation.w);
                    Add(ref props, prop.Scale);
                }

                ulong level = Seed;
                Add(ref level, game.Level != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(game.Level) : 0);
                Add(ref level, game.LevelCompleted ? 1 : 0);
                Add(ref level, game.LevelTicks);
                Add(ref level, game.TickCount);
                Add(ref level, game.Grabber.Held != null ? game.Grabber.Held.Id : -1);
                IReadOnlyList<Exit> exits = game.Exits;
                for (int i = 0; i < exits.Count; i++) Add(ref level, exits[i].Locked ? 1 : 0);
                IReadOnlyList<Trigger> triggers = game.Triggers;
                Add(ref level, triggers.Count);
                for (int i = 0; i < triggers.Count; i++) Add(ref level, (triggers[i].Enabled ? 1 : 0) | (triggers[i].Removed ? 2 : 0));
                IReadOnlyList<Mover> movers = game.Movers;
                for (int i = 0; i < movers.Count; i++) Add(ref level, movers[i].Position);
                return new Fingerprint(player, props, level);
            }

            /// <summary>Names what differs, or null.</summary>
            public string DifferenceTo(in Fingerprint after)
            {
                if (player != after.player) return "the player";
                if (props != after.props) return "a prop";
                if (level != after.level) return "the state of the level";
                return null;
            }

            // FNV-1a over 32-bit words.
            const ulong Seed = 14695981039346656037UL;

            static void Add(ref ulong hash, int value)
            {
                unchecked
                {
                    hash = (hash ^ (uint)value) * 1099511628211UL;
                }
            }

            static void Add(ref ulong hash, float value) => Add(ref hash, BitConverter.SingleToInt32Bits(value));

            static void Add(ref ulong hash, Vector3 value)
            {
                Add(ref hash, value.x);
                Add(ref hash, value.y);
                Add(ref hash, value.z);
            }
        }
    }
}
