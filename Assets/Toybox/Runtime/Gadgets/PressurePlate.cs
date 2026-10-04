using System;
using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    public enum PlateMode
    {
        /// <summary>Everything on the plate counts together.</summary>
        Sum,
        /// <summary>Only the heaviest single body counts: two light toys never add up.</summary>
        Heaviest,
    }

    public sealed class PressurePlateOptions
    {
        public string Name = "Plate";
        /// <summary>A body counts while its centre is inside (the player: while their feet are).</summary>
        public Zone Sensor;
        public PlateMode Mode = PlateMode.Sum;
        /// <summary>The load that presses it. Mass goes with scale cubed.</summary>
        public float MinMass = 1f;
        /// <summary>The player weighs <see cref="Player.Mass"/> (3) while standing in the sensor.</summary>
        public bool AcceptPlayer;
        /// <summary>Only props with this tag count (null: any prop).</summary>
        public string AcceptTag;
        /// <summary>A prop must have been slower than <see cref="SettleSpeed"/> for this long before it is judged.</summary>
        public float Settle;
        public float SettleSpeed = 0.5f;
        /// <summary>Once pressed it stays pressed.</summary>
        public bool Latch;
        /// <summary>Seconds the load has to be short before a pressed plate comes back up.</summary>
        public float Debounce = 0.2f;
        /// <summary>
        /// On a press the prop that pressed it is taken (BeginDrive), eased to <see cref="LockPoint"/> over
        /// <see cref="LockSeconds"/> and made non-grabbable: it never serves twice.
        /// </summary>
        public bool LockProp;
        public float LockSeconds = 0.25f;
        /// <summary>Where a locked prop's centre goes. Default: over the middle of the sensor, resting on its bottom.</summary>
        public Vector3? LockPoint;
        /// <summary>Draws the plate: an Ink base and a signal cap that sinks with the load.</summary>
        public bool Visual = true;
        /// <summary>Radius of the drawn plate (0: the sensor's footprint).</summary>
        public float VisualRadius;
        /// <summary>How far the cap sinks when pressed.</summary>
        public float Travel = 0.06f;
    }

    /// <summary>
    /// Pressure button, weight plate and pedal in one (LEVELS 2.1). It weighs what is inside its sensor -
    /// mass is density x volume x scale cubed, so the size of a toy decides - and is pressed while the load
    /// reaches <see cref="PressurePlateOptions.MinMass"/>. Deterministic: the props are read in Id order.
    /// </summary>
    public sealed class PressurePlate : Gadget
    {
        sealed class Entry
        {
            public int Slow;
            public bool Rejected;
            public bool Seen;
        }

        readonly PressurePlateOptions options;
        readonly Dictionary<Prop, Entry> entries = new Dictionary<Prop, Entry>();
        readonly List<Prop> judged = new List<Prop>();
        readonly List<Prop> gone = new List<Prop>();
        readonly int settleTicks, debounceTicks, lockTicks;

        Transform visual, cap;
        float capDown;
        SignalLamp lamp;
        int shortTicks;
        Prop lockedProp;
        Mover lockMover;
        bool lockedWasGrabbable;
        Vector3 lockFrom, lockTo;
        int lockTick;

        public PressurePlate(LevelContext ctx, PressurePlateOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            if (!options.Sensor.IsValid) throw new ArgumentException("A pressure plate needs a sensor zone.", nameof(options));
            settleTicks = Ticks(options.Settle);
            debounceTicks = Ticks(options.Debounce, 1);
            lockTicks = Ticks(options.LockSeconds, 1);
            Position = new Vector3(options.Sensor.Center.x, options.Sensor.MinY, options.Sensor.Center.z);
            if (options.Visual) BuildVisual();
        }

        /// <summary>The middle of the plate's top.</summary>
        public Vector3 Position { get; }
        public Zone Sensor => options.Sensor;
        public float MinMass => options.MinMass;
        public bool Pressed { get; private set; }
        /// <summary>The load this tick (a sum or the heaviest body, by the mode).</summary>
        public float Load { get; private set; }
        /// <summary>Load / MinMass, clamped to 0..1: how far the cap has travelled.</summary>
        public float Load01 { get; private set; }
        /// <summary>The prop that pressed it (the heaviest one at that moment), or null for the player.</summary>
        public Prop PressedBy { get; private set; }
        /// <summary>The prop it holds on to (LockProp), or null.</summary>
        public Prop LockedProp => lockedProp;
        /// <summary>Amber while it waits, green while pressed.</summary>
        public Signal Signal => Pressed ? Palette.Go : Palette.Amber;

        /// <summary>Inside the tick. The argument is the prop that pressed it, null for the player.</summary>
        public event Action<Prop> OnPressed;
        public event Action OnReleased;
        /// <summary>Once per settled prop that is too light: the prop and load / MinMass.</summary>
        public event Action<Prop, float> OnRejected;

        /// <summary>Would a prop of this mass press the plate by itself?</summary>
        public bool Accepts(float mass) => mass >= options.MinMass;

        protected override void Tick(float dt)
        {
            DriveLock();

            float sum = 0f, heaviest = 0f;
            Prop heaviestProp = null;
            judged.Clear();
            foreach (Entry entry in entries.Values) entry.Seen = false;

            IReadOnlyList<Prop> props = Game.Props;
            for (int i = 0; i < props.Count; i++)
            {
                Prop prop = props[i];
                if (!GadgetKit.IsLoose(prop)) continue;
                if (options.AcceptTag != null && !prop.HasTag(options.AcceptTag)) continue;
                if (!options.Sensor.Contains(prop.Center)) continue;

                if (!entries.TryGetValue(prop, out Entry entry)) entries[prop] = entry = new Entry();
                entry.Seen = true;
                bool locked = prop == lockedProp;
                if (!locked && settleTicks > 0)
                {
                    bool slow = prop.Velocity.sqrMagnitude < options.SettleSpeed * options.SettleSpeed;
                    entry.Slow = slow ? entry.Slow + 1 : 0;
                    if (entry.Slow < settleTicks) continue;
                }
                float mass = prop.Mass;
                sum += mass;
                if (mass > heaviest)
                {
                    heaviest = mass;
                    heaviestProp = prop;
                }
                judged.Add(prop);
            }
            // Whatever left the sensor (or was picked up) may be rejected again when it comes back.
            gone.Clear();
            foreach (KeyValuePair<Prop, Entry> pair in entries)
                if (!pair.Value.Seen) gone.Add(pair.Key);
            for (int i = 0; i < gone.Count; i++) entries.Remove(gone[i]);

            bool playerOn = false;
            if (options.AcceptPlayer)
            {
                Player player = Game.Player;
                Vector3 feet = player.Position;
                playerOn = player.Grounded && (options.Sensor.Contains(feet) || options.Sensor.Contains(feet + Vector3.up * (0.05f * player.Scale)));
                if (playerOn)
                {
                    sum += Player.Mass;
                    if (Player.Mass > heaviest)
                    {
                        heaviest = Player.Mass;
                        heaviestProp = null;
                    }
                }
            }

            Load = options.Mode == PlateMode.Sum ? sum : heaviest;
            Load01 = options.MinMass > 0f ? Mathf.Clamp01(Load / options.MinMass) : 1f;
            bool down = Load >= options.MinMass && (judged.Count > 0 || playerOn);

            if (!Pressed)
            {
                if (down) Press(heaviestProp);
            }
            else if (!options.Latch)
            {
                if (down) shortTicks = 0;
                else if (++shortTicks >= debounceTicks) Release();
            }

            Reject(down);
            UpdateVisual();
        }

        void Reject(bool down)
        {
            for (int i = 0; i < judged.Count; i++)
            {
                Prop prop = judged[i];
                if (prop == lockedProp || prop == PressedBy) continue;
                Entry entry = entries[prop];
                if (entry.Rejected) continue;
                // Too light by itself; in Sum mode only while the whole load is short. A latched plate that
                // already has its prop turns every other one away.
                bool tooLight = options.Mode == PlateMode.Heaviest ? prop.Mass < options.MinMass : !down;
                bool busy = Pressed && options.Latch && options.LockProp;
                if (!tooLight && !busy) continue;
                entry.Rejected = true;
                float load01 = options.MinMass > 0f ? prop.Mass / options.MinMass : 1f;
                OnRejected?.Invoke(prop, load01);
                Game.Events.RaisePlateRejected(Event(prop.Center, prop, load01));
            }
        }

        void Press(Prop prop)
        {
            Pressed = true;
            PressedBy = prop;
            shortTicks = 0;
            if (options.LockProp && prop != null && prop.BodyKind == PropBody.Dynamic) BeginLock(prop);
            OnPressed?.Invoke(prop);
            Game.Events.RaisePlatePressed(Event(Position, prop, options.MinMass > 0f ? Load / options.MinMass : 1f));
        }

        void Release()
        {
            Pressed = false;
            PressedBy = null;
            shortTicks = 0;
            OnReleased?.Invoke();
            Game.Events.RaisePlateReleased(Event(Position));
        }

        void BeginLock(Prop prop)
        {
            Mover mover = prop.BeginDrive();
            if (mover == null) return;
            lockedProp = prop;
            lockMover = mover;
            lockedWasGrabbable = prop.Grabbable;
            prop.Grabbable = false;
            lockFrom = prop.Position;
            GadgetKit.VerticalExtent(prop, out float minY, out float maxY);
            Vector3 centre = options.LockPoint ?? new Vector3(options.Sensor.Center.x, options.Sensor.MinY + (maxY - minY) * 0.5f, options.Sensor.Center.z);
            lockTo = centre - (prop.Center - prop.Position);
            lockTick = 0;
        }

        void DriveLock()
        {
            if (lockedProp == null) return;
            if (lockedProp.Removed || !lockedProp.Driven)
            {
                // Respawned or removed by level code: the plate lets go of it.
                if (!lockedProp.Removed) lockedProp.Grabbable = lockedWasGrabbable;
                lockedProp = null;
                lockMover = null;
                return;
            }
            if (lockTick >= lockTicks) return;
            lockTick++;
            lockMover.MoveTo(Vector3.Lerp(lockFrom, lockTo, GadgetKit.Smooth((float)lockTick / lockTicks)), lockedProp.Rotation);
        }

        /// <summary>Unlatches the plate and lets go of a locked prop (it is grabbable again).</summary>
        public override void Reset()
        {
            base.Reset();
            if (lockedProp != null)
            {
                if (!lockedProp.Removed)
                {
                    if (lockedProp.Driven) lockedProp.EndDrive(Vector3.zero);
                    lockedProp.Grabbable = lockedWasGrabbable;
                }
                lockedProp = null;
                lockMover = null;
            }
            entries.Clear();
            Pressed = false;
            PressedBy = null;
            Load = 0f;
            Load01 = 0f;
            shortTicks = 0;
            UpdateVisual();
        }

        void BuildVisual()
        {
            Zone sensor = options.Sensor;
            float radius = options.VisualRadius > 0f
                ? options.VisualRadius
                : sensor.Shape == ZoneShape.Box ? Mathf.Min(sensor.Size.x, sensor.Size.z) * 0.5f : sensor.Radius;
            var root = new GameObject("Plate " + Name);
            visual = root.transform;
            visual.SetParent(Ctx.Root, false);
            visual.position = Position;
            float baseHeight = Mathf.Max(0.02f, radius * 0.08f);
            GadgetKit.Visual(visual, "Base", GadgetKit.DiscMesh(radius, baseHeight), GadgetKit.Body, new Vector3(0f, baseHeight * 0.5f, 0f));
            float capHeight = baseHeight + options.Travel;
            lamp = GadgetKit.Lamp(visual, GadgetKit.DiscMesh(radius * 0.78f, capHeight), Palette.Amber, new Vector3(0f, capHeight * 0.5f + options.Travel, 0f));
            cap = lamp.Renderer.transform;
            capDown = capHeight * 0.5f;
        }

        void UpdateVisual()
        {
            if (lamp == null) return;
            lamp.Set(Signal);
            lamp.Pulse(Seconds);
            // The cap rides between its rest height and one travel lower.
            float y = capDown + options.Travel * (1f - (Pressed ? 1f : Load01));
            Vector3 local = cap.localPosition;
            if (!Mathf.Approximately(local.y, y)) cap.localPosition = new Vector3(local.x, y, local.z);
        }
    }
}
