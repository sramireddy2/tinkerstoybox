using System;
using System.Collections.Generic;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    public enum MachineState
    {
        Idle,
        Running,
        /// <summary>The run stopped at a link; everything is being set up again.</summary>
        Resetting,
        Done,
    }

    public sealed class MachineDirectorOptions
    {
        public string Name = "Machine";
        /// <summary>The pedal: a press starts a run (only while the machine is idle).</summary>
        public PressurePlate Start;
        /// <summary>The toys of the machine: not grabbable while it runs.</summary>
        public IList<Prop> Toys;
        /// <summary>The link names in chain order. The run is done when the last one reports.</summary>
        public IList<string> Links;
        /// <summary>The gadgets that Reset() is called on, in this order, when a run fizzles.</summary>
        public IList<Gadget> Resets;
        /// <summary>Seconds without a report after which the run has fizzled.</summary>
        public float FizzleTimeout = 2.5f;
        /// <summary>Seconds the machine takes to set itself up again.</summary>
        public float ResetSeconds = 1.5f;
        /// <summary>What to say when the run stops after a link: keyed by the last link that fired ("" for none at all).</summary>
        public IDictionary<string, string> Messages;
    }

    /// <summary>
    /// The conductor of a chain reaction (LEVELS 2.3, Level 15). A press of the pedal starts a run; the
    /// links report as they fire; when nothing has reported for the fizzle timeout the run has stopped at
    /// the first wrong link - the machine says which, resets every link and gives the toys back. Running
    /// the machine is the diagnostic: never a softlock, never a restart.
    /// </summary>
    public sealed class MachineDirector : Gadget
    {
        readonly MachineDirectorOptions options;
        readonly List<Prop> toys = new List<Prop>();
        readonly List<bool> wasGrabbable = new List<bool>();
        readonly List<string> fired = new List<string>();
        readonly int fizzleTicks, resetTicks;
        int quiet, resetting;
        string failure;

        public MachineDirector(LevelContext ctx, MachineDirectorOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            fizzleTicks = Ticks(options.FizzleTimeout, 1);
            resetTicks = Ticks(options.ResetSeconds, 1);
            if (options.Start != null) options.Start.OnPressed += prop => Begin();
        }

        public MachineState State { get; private set; } = MachineState.Idle;
        /// <summary>The last link that reported in this run (or the last one), or null.</summary>
        public string LastLink { get; private set; }
        /// <summary>How many runs have been started.</summary>
        public int Runs { get; private set; }
        /// <summary>The links that fired in this run (or the last one), in order.</summary>
        public IReadOnlyList<string> Fired => fired;
        /// <summary>What was said when the last run fizzled.</summary>
        public string LastMessage { get; private set; }

        /// <summary>Inside the tick.</summary>
        public event Action MachineStarted;
        /// <summary>The last link that fired (null: none).</summary>
        public event Action<string> MachineFizzled;
        public event Action MachineReset;
        public event Action MachineDone;

        /// <summary>Starts a run. False (and nothing happens) unless the machine is idle.</summary>
        public bool Begin()
        {
            if (State != MachineState.Idle || !Enabled) return false;
            State = MachineState.Running;
            Runs++;
            quiet = 0;
            failure = null;
            LastLink = null;
            fired.Clear();
            toys.Clear();
            wasGrabbable.Clear();
            if (options.Toys != null)
            {
                for (int i = 0; i < options.Toys.Count; i++)
                {
                    Prop toy = options.Toys[i];
                    if (toy == null || toy.Removed) continue;
                    toys.Add(toy);
                    wasGrabbable.Add(toy.Grabbable);
                    // A toy that is in the player's hand right now stays theirs; it is simply missing from the run.
                    if (!toy.Held) toy.Grabbable = false;
                }
            }
            MachineStarted?.Invoke();
            Game.Events.RaiseMachineStarted(Event(Position()));
            return true;
        }

        /// <summary>A link fired: the run is alive. The last link of the chain ends it.</summary>
        public void Report(string link)
        {
            if (State != MachineState.Running) return;
            LastLink = link;
            fired.Add(link);
            quiet = 0;
            if (options.Links == null || options.Links.Count == 0 || options.Links[options.Links.Count - 1] != link) return;
            State = MachineState.Done;
            MachineDone?.Invoke();
            Game.Events.RaiseMachineDone(Event(Position(), null, 0f, 0f, fired.Count, link));
        }

        /// <summary>A link knows it has failed (the marble fell short): the run stops now, with this message, instead of timing out.</summary>
        public void Fail(string message = null)
        {
            if (State != MachineState.Running) return;
            failure = message;
            Fizzle();
        }

        protected override void Tick(float dt)
        {
            if (State == MachineState.Running)
            {
                if (++quiet >= fizzleTicks) Fizzle();
            }
            else if (State == MachineState.Resetting)
            {
                if (++resetting < resetTicks) return;
                RestoreToys();
                State = MachineState.Idle;
                MachineReset?.Invoke();
                Game.Events.RaiseMachineReset(Event(Position()));
            }
        }

        void Fizzle()
        {
            State = MachineState.Resetting;
            resetting = 0;
            string message = failure;
            if (message == null && options.Messages != null) options.Messages.TryGetValue(LastLink ?? "", out message);
            LastMessage = message;
            if (!string.IsNullOrEmpty(message)) Ctx.Say(message);
            MachineFizzled?.Invoke(LastLink);
            Game.Events.RaiseMachineFizzled(Event(Position(), null, 0f, 0f, fired.Count, LastLink));
            if (options.Resets == null) return;
            for (int i = 0; i < options.Resets.Count; i++) options.Resets[i]?.Reset();
        }

        void RestoreToys()
        {
            for (int i = 0; i < toys.Count; i++)
                if (!toys[i].Removed) toys[i].Grabbable = wasGrabbable[i];
            toys.Clear();
            wasGrabbable.Clear();
        }

        Vector3 Position() => options.Start != null ? options.Start.Position : Vector3.zero;

        /// <summary>Idle again, at once, with the toys given back (does not reset the links).</summary>
        public override void Reset()
        {
            base.Reset();
            RestoreToys();
            State = MachineState.Idle;
            quiet = 0;
            resetting = 0;
        }
    }
}
