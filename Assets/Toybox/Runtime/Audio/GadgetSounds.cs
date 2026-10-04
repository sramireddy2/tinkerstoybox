using System;
using System.Collections.Generic;
using System.Reflection;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Audio
{
    /// <summary>
    /// The sounds of gadgets. Gadgets add their events to the partial <c>GameEvents</c> in their own files
    /// (LEVELS 2.0: every gadget event has a mirror there), and audio finds them by their names: every
    /// public event of the form <c>event Action&lt;T&gt; ...Pressed</c> plays the button's press, every
    /// <c>...Released</c> its release, and the mirrors listed in <see cref="Named"/> play one of the
    /// clips the bank already has - a rising chime for what was achieved, a falling one for what was
    /// refused, a landing for what struck something, a bell for what stung. Nothing else is bound.
    ///
    /// Where the sound comes from is read off the payload: a Vector3 called Position or Point, else a Prop
    /// in it (its centre), else something in it that has a Position (the gadget itself). Without any of
    /// these the sound is played in 2D.
    /// </summary>
    public sealed class GadgetSounds
    {
        /// <param name="placed">True if the payload said where the gadget is.</param>
        public delegate void Handler(SoundId sound, bool placed, Vector3 position);

        struct Binding
        {
            public EventInfo Event;
            public Delegate Listener;
        }

        readonly object source;
        readonly Handler handler;
        readonly List<Binding> bindings = new List<Binding>();

        GadgetSounds(object source, Handler handler)
        {
            this.source = source;
            this.handler = handler;
        }

        /// <summary>How many events were bound.</summary>
        public int Count => bindings.Count;

        /// <summary>The names of the bound events.</summary>
        public IEnumerable<string> EventNames
        {
            get
            {
                foreach (Binding binding in bindings) yield return binding.Event.Name;
            }
        }

        /// <summary>The gadget mirrors that have a sound of their own (the names of GadgetEvents.cs).</summary>
        public static readonly IReadOnlyDictionary<string, SoundId> Named = new Dictionary<string, SoundId>
        {
            // Achieved: the rising chime of an exit that opens.
            { "DoorOpened", SoundId.ExitOpen }, { "LaserAllClear", SoundId.ExitOpen }, { "MachineDone", SoundId.ExitOpen },
            { "SailDocked", SoundId.ExitOpen }, { "FloatArrived", SoundId.ExitOpen },
            // Refused or undone: the falling one.
            { "PlateRejected", SoundId.ExitClose }, { "SocketRejected", SoundId.ExitClose }, { "SocketUnseated", SoundId.ExitClose },
            { "DoorClosed", SoundId.ExitClose }, { "MachineFizzled", SoundId.ExitClose }, { "ChainFellShort", SoundId.ExitClose },
            { "PortalBlocked", SoundId.ExitClose },
            // Something clicked into place or let go.
            { "SocketSeated", SoundId.ButtonPress }, { "CarrierCaptured", SoundId.ButtonPress }, { "CarrierDropped", SoundId.ButtonRelease },
            // Something struck something.
            { "Bounced", SoundId.LandRubber }, { "BreakableBroke", SoundId.LandCardboard }, { "BreakableBonked", SoundId.LandWood },
            { "SeesawStruck", SoundId.LandWood }, { "ChainPieceFell", SoundId.KitWood }, { "ChainTargetStruck", SoundId.LandWood },
            // Something stung, or took the player somewhere.
            { "LaserZapped", SoundId.ReleaseBell }, { "HazardCaught", SoundId.ReleaseBell }, { "WaterSwept", SoundId.ReleaseBell },
            { "PortalCrossed", SoundId.Grab }, { "Recalled", SoundId.Grab }, { "NestRevealed", SoundId.Grab }, { "SeesawLaunched", SoundId.ReleaseSub },
        };

        /// <summary>The sound an event of this name makes; None for an event that has none.</summary>
        public static SoundId SoundFor(string eventName)
        {
            if (string.IsNullOrEmpty(eventName)) return SoundId.None;
            if (Named.TryGetValue(eventName, out SoundId named)) return named;
            if (eventName.EndsWith("Pressed", StringComparison.Ordinal)) return SoundId.ButtonPress;
            if (eventName.EndsWith("Released", StringComparison.Ordinal)) return SoundId.ButtonRelease;
            return SoundId.None;
        }

        /// <summary>
        /// Subscribes to every matching event of <paramref name="source"/> (the game's GameEvents). An event
        /// that cannot be bound is reported and left alone; the rest still sound.
        /// </summary>
        public static GadgetSounds Bind(object source, Handler handler)
        {
            var sounds = new GadgetSounds(source, handler);
            if (source == null || handler == null) return sounds;
            foreach (EventInfo info in source.GetType().GetEvents(BindingFlags.Public | BindingFlags.Instance))
            {
                SoundId sound = SoundFor(info.Name);
                if (sound == SoundId.None) continue;
                Type type = info.EventHandlerType;
                if (type == null || !type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Action<>)) continue;
                try
                {
                    Type payload = type.GetGenericArguments()[0];
                    object listener = Activator.CreateInstance(typeof(Listener<>).MakeGenericType(payload), sounds, sound, Locator.For(payload));
                    Delegate call = Delegate.CreateDelegate(type, listener, nameof(Listener<int>.Handle));
                    info.AddEventHandler(source, call);
                    sounds.bindings.Add(new Binding { Event = info, Listener = call });
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Toybox] audio could not listen to GameEvents." + info.Name + ": " + e.Message);
                }
            }
            return sounds;
        }

        /// <summary>Leaves every event again.</summary>
        public void Unbind()
        {
            foreach (Binding binding in bindings)
            {
                try
                {
                    binding.Event.RemoveEventHandler(source, binding.Listener);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Toybox] audio could not leave GameEvents." + binding.Event.Name + ": " + e.Message);
                }
            }
            bindings.Clear();
        }

        sealed class Listener<T>
        {
            readonly GadgetSounds owner;
            readonly SoundId sound;
            readonly Locator locator;

            public Listener(GadgetSounds owner, SoundId sound, Locator locator)
            {
                this.owner = owner;
                this.sound = sound;
                this.locator = locator;
            }

            public void Handle(T payload)
            {
                bool placed = locator.Find(payload, out Vector3 position);
                owner.handler(sound, placed, position);
            }
        }

        /// <summary>Where in a payload type the position of the event is found; resolved once per type.</summary>
        public sealed class Locator
        {
            static readonly string[] PositionNames = { "Position", "Point" };
            const BindingFlags Members = BindingFlags.Public | BindingFlags.Instance;

            // The member of the payload to read, and - if that is not the position itself - the Position of its value.
            Func<object, object> member;
            Func<object, object> inner;
            bool isProp;

            public static Locator For(Type payload)
            {
                var locator = new Locator();
                foreach (string name in PositionNames)
                {
                    locator.member = Reader(payload, name, typeof(Vector3));
                    if (locator.member != null) return locator;
                }
                foreach (MemberInfo candidate in payload.GetMembers(Members))
                {
                    Type type = TypeOf(candidate);
                    if (type != typeof(Prop)) continue;
                    locator.member = Reader(candidate);
                    locator.isProp = true;
                    return locator;
                }
                foreach (MemberInfo candidate in payload.GetMembers(Members))
                {
                    Type type = TypeOf(candidate);
                    if (type == null || type.IsPrimitive || type == typeof(string)) continue;
                    Func<object, object> position = Reader(type, "Position", typeof(Vector3));
                    if (position == null) continue;
                    locator.member = Reader(candidate);
                    locator.inner = position;
                    return locator;
                }
                return locator;
            }

            public bool Find(object payload, out Vector3 position)
            {
                position = default;
                if (member == null || payload == null) return false;
                try
                {
                    object value = member(payload);
                    if (value == null) return false;
                    if (isProp)
                    {
                        var prop = (Prop)value;
                        if (prop.Removed) return false;
                        position = prop.Center;
                        return true;
                    }
                    if (inner != null) value = inner(value);
                    if (!(value is Vector3 found)) return false;
                    position = found;
                    return true;
                }
                catch (Exception)
                {
                    // An object that is already gone has no place; the sound is played in 2D.
                    return false;
                }
            }

            static Type TypeOf(MemberInfo candidate)
            {
                if (candidate is FieldInfo field) return field.FieldType;
                if (candidate is PropertyInfo property && property.CanRead && property.GetIndexParameters().Length == 0) return property.PropertyType;
                return null;
            }

            static Func<object, object> Reader(MemberInfo candidate)
            {
                if (candidate is FieldInfo field) return field.GetValue;
                if (candidate is PropertyInfo property) return target => property.GetValue(target, null);
                return null;
            }

            static Func<object, object> Reader(Type type, string name, Type wanted)
            {
                FieldInfo field = type.GetField(name, Members);
                if (field != null && field.FieldType == wanted) return field.GetValue;
                PropertyInfo property = type.GetProperty(name, Members);
                if (property != null && property.PropertyType == wanted && property.CanRead && property.GetIndexParameters().Length == 0)
                    return target => property.GetValue(target, null);
                return null;
            }
        }
    }
}
