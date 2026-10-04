using System;
using Toybox.Art;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Gadgets
{
    public enum DoorMotion
    {
        /// <summary>The panel travels from its closed pose to its open pose.</summary>
        Slide,
        /// <summary>The panel turns about a hinge line.</summary>
        Hinge,
    }

    public enum DoorEase
    {
        Linear,
        /// <summary>Fast first, settling at the end.</summary>
        EaseOut,
        Smooth,
    }

    public sealed class DoorOptions
    {
        public string Name = "Door";
        /// <summary>The panel: renderers and colliders, no Rigidbody. Null: a slab of <see cref="Size"/> is made.</summary>
        public GameObject Panel;
        public Vector3 Size = new Vector3(2f, 3f, 0.3f);
        /// <summary>Pose of the panel's centre when closed.</summary>
        public Vector3 ClosedPosition;
        public Quaternion ClosedRotation = Quaternion.identity;
        /// <summary>Slide: pose when open.</summary>
        public Vector3 OpenPosition;
        public Quaternion OpenRotation = Quaternion.identity;
        public DoorMotion Motion = DoorMotion.Slide;
        /// <summary>Hinge: a point on the hinge line, its direction, and how far the panel swings open (degrees).</summary>
        public Vector3 HingePivot;
        public Vector3 HingeAxis = Vector3.up;
        public float HingeAngle = 90f;
        /// <summary>Seconds a full swing takes.</summary>
        public float Seconds = 0.8f;
        public DoorEase Ease = DoorEase.EaseOut;
        public bool StartsOpen;
    }

    /// <summary>
    /// A kinematic panel with two poses (LEVELS 2.1): flap, sliding door, gate. It never closes or opens
    /// into the player: before each step it tests the capsule against the pose it is about to take and
    /// waits while they would overlap (the crush guard). A hinged panel lying flat is floor.
    /// </summary>
    public sealed class Door : Gadget
    {
        readonly DoorOptions options;
        readonly Mover mover;
        readonly Collider[] colliders;
        readonly SignalLamp lamp;
        float progress;
        bool wantOpen;

        public Door(LevelContext ctx, DoorOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            GameObject panel = options.Panel;
            if (panel == null)
            {
                panel = new GameObject("Door " + Name);
                panel.AddComponent<BoxCollider>().size = options.Size;
                GadgetKit.Visual(panel.transform, "Visual", GadgetKit.BoxMesh(options.Size), GadgetKit.Metal);
                // The one signal element: a bar across the panel, a little proud of both faces.
                var bar = new Vector3(options.Size.x * 0.6f, Mathf.Min(0.12f, options.Size.y * 0.1f), options.Size.z + 0.04f);
                lamp = GadgetKit.Lamp(panel.transform, GadgetKit.BoxMesh(bar), Palette.Amber, new Vector3(0f, options.Size.y * 0.2f, 0f));
            }
            wantOpen = options.StartsOpen;
            progress = wantOpen ? 1f : 0f;
            PoseAt(progress, out Vector3 position, out Quaternion rotation);
            mover = ctx.AddKinematic(panel, position, rotation);
            colliders = panel.GetComponentsInChildren<Collider>(true);
            lamp?.Set(Signal);
        }

        public Mover Mover => mover;
        public GameObject Panel => mover.GameObject;
        /// <summary>Fully open.</summary>
        public bool IsOpen => progress >= 1f;
        /// <summary>Fully closed.</summary>
        public bool IsClosed => progress <= 0f;
        /// <summary>On its way (also while the crush guard makes it wait).</summary>
        public bool Moving => wantOpen ? progress < 1f : progress > 0f;
        /// <summary>True while the player is in the way of its next step.</summary>
        public bool Waiting { get; private set; }
        /// <summary>0 closed .. 1 open.</summary>
        public float Openness => progress;
        public Signal Signal => IsOpen ? Palette.Go : Palette.Amber;

        /// <summary>Inside the tick, when it has arrived.</summary>
        public event Action Opened;
        public event Action Closed;

        public void Open() => Command(true);
        public void Close() => Command(false);

        void Command(bool open)
        {
            if (wantOpen == open) return;
            wantOpen = open;
            if (Moving) Game.Events.RaiseDoorStarted(Event(mover.Position, null, 0f, 0f, open ? 1 : 0));
        }

        protected override void Tick(float dt)
        {
            Waiting = false;
            float target = wantOpen ? 1f : 0f;
            if (!Mathf.Approximately(progress, target))
            {
                float next = Mathf.MoveTowards(progress, target, dt / Mathf.Max(options.Seconds, Sim.Dt));
                PoseAt(next, out Vector3 position, out Quaternion rotation);
                if (GadgetKit.WouldCrush(Game, mover.Transform, colliders, position, rotation))
                {
                    Waiting = true;
                }
                else
                {
                    progress = next;
                    mover.MoveTo(position, rotation);
                    if (Mathf.Approximately(progress, target))
                    {
                        progress = target;
                        if (wantOpen)
                        {
                            Opened?.Invoke();
                            Game.Events.RaiseDoorOpened(Event(position));
                        }
                        else
                        {
                            Closed?.Invoke();
                            Game.Events.RaiseDoorClosed(Event(position));
                        }
                    }
                }
            }
            if (lamp != null)
            {
                lamp.Set(Signal);
                lamp.Pulse(Seconds);
            }
        }

        /// <summary>The panel's pose at an openness of 0..1.</summary>
        public void PoseAt(float openness, out Vector3 position, out Quaternion rotation)
        {
            float e = Eased(Mathf.Clamp01(openness));
            if (options.Motion == DoorMotion.Hinge)
            {
                Quaternion turn = Quaternion.AngleAxis(options.HingeAngle * e, options.HingeAxis.normalized);
                position = options.HingePivot + turn * (options.ClosedPosition - options.HingePivot);
                rotation = turn * options.ClosedRotation;
                return;
            }
            position = Vector3.Lerp(options.ClosedPosition, options.OpenPosition, e);
            rotation = Quaternion.Slerp(options.ClosedRotation, options.OpenRotation, e);
        }

        float Eased(float t)
        {
            switch (options.Ease)
            {
                case DoorEase.EaseOut: return 1f - (1f - t) * (1f - t);
                case DoorEase.Smooth: return GadgetKit.Smooth(t);
                default: return t;
            }
        }

        /// <summary>Back to how Build left it, at once.</summary>
        public override void Reset()
        {
            base.Reset();
            wantOpen = options.StartsOpen;
            progress = wantOpen ? 1f : 0f;
            PoseAt(progress, out Vector3 position, out Quaternion rotation);
            mover.Teleport(position, rotation);
            Waiting = false;
        }
    }
}
