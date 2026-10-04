using System;
using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Gadgets
{
    /// <summary>One falling piece of a chain, in the prop's own space at scale 1.</summary>
    public struct HingePiece
    {
        /// <summary>A point of the hinge line, and the line's direction. The piece turns about it by Unity's rule (AngleAxis).</summary>
        public Vector3 Hinge, Axis;
        /// <summary>The piece standing up: its centre, its box and how it is turned.</summary>
        public Vector3 Center, Size;
        public Quaternion Rotation;
        /// <summary>How tall it is: what decides how fast it falls and how far it reaches.</summary>
        public float Height;
    }

    public enum ChainState
    {
        Idle,
        Running,
        /// <summary>Every piece is down.</summary>
        Done,
        /// <summary>Standing the pieces up again.</summary>
        Resetting,
    }

    public sealed class HingeChainOptions
    {
        public string Name = "Chain";
        /// <summary>The prop that carries the pieces (the domino set). It has to stay where it is while the chain runs.</summary>
        public Prop Prop;
        public IList<HingePiece> Pieces;
        /// <summary>Name of the prop's child that holds its own pieces; it is hidden while the stand-ins are out.</summary>
        public string PiecesChild;
        /// <summary>Builds the stand-in for piece i at scale 1 (renderers, no collider needed). Null: a plain box.</summary>
        public Func<int, GameObject> PieceFactory;
        /// <summary>Degrees a released piece starts from.</summary>
        public float StartAngle = 3f;
        /// <summary>A piece releases the next when the sine of its angle reaches this.</summary>
        public float ContactSin = 0.55f;
        /// <summary>Degrees at which a piece comes to rest on the next one.</summary>
        public float RestAngle = 72f;
        /// <summary>What the last piece is meant to hit (world space), and how big it is. Null: it just falls flat.</summary>
        public Vector3? Target;
        public float TargetRadius = 0.5f;
        /// <summary>Seconds Reset takes to stand the pieces up again.</summary>
        public float ResetSeconds = 1f;
    }

    /// <summary>
    /// A row of dominoes as a law, not as rigid bodies (LEVELS 2.2, Level 15). Each released piece turns
    /// about its hinge with theta'' = (3 g / 2 h) sin theta, h its height at the prop's scale; it releases
    /// the next one when it has leaned far enough to touch it. Bigger pieces fall slower: a ritardando.
    /// While it runs the prop's own pieces are hidden and one kinematic body per piece takes their place.
    /// </summary>
    public sealed class HingeChain : Gadget
    {
        readonly HingeChainOptions options;
        readonly Prop prop;
        readonly HingePiece[] pieces;
        readonly Mover[] movers;
        readonly float[] angles, speeds, resetFrom;
        readonly bool[] released, fell;
        readonly Transform piecesChild;
        readonly float contactAngle;
        int resetTick;

        public HingeChain(LevelContext ctx, HingeChainOptions options) : base(ctx, options?.Name)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            prop = options.Prop ?? throw new ArgumentException("A hinge chain needs its prop.", nameof(options));
            if (options.Pieces == null || options.Pieces.Count == 0) throw new ArgumentException("A hinge chain needs pieces.", nameof(options));
            pieces = new HingePiece[options.Pieces.Count];
            options.Pieces.CopyTo(pieces, 0);
            int n = pieces.Length;
            movers = new Mover[n];
            angles = new float[n];
            speeds = new float[n];
            resetFrom = new float[n];
            released = new bool[n];
            fell = new bool[n];
            contactAngle = Mathf.Asin(Mathf.Clamp01(options.ContactSin)) * Mathf.Rad2Deg;
            if (!string.IsNullOrEmpty(options.PiecesChild)) piecesChild = prop.Transform.Find(options.PiecesChild);

            for (int i = 0; i < n; i++)
            {
                GameObject piece = options.PieceFactory != null ? options.PieceFactory(i) : DefaultPiece(pieces[i]);
                piece.name = Name + " Piece " + (i + 1);
                // The stand-in gets exactly one collider: the piece's box.
                foreach (Collider collider in piece.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                piece.AddComponent<BoxCollider>().size = pieces[i].Size;
                Pose(i, 0f, out Vector3 position, out Quaternion rotation);
                movers[i] = ctx.AddKinematic(piece, position, rotation);
                piece.SetActive(false);
            }
        }

        /// <summary>Options for the domino set of the toy catalog: seven pieces that fall toward the set's +X.</summary>
        public static HingeChainOptions ForDominoSet(Prop set)
        {
            var list = new List<HingePiece>();
            for (int i = 0; i < ToyFactory.DominoSetCount; i++)
            {
                float height = ToyFactory.DominoSetHeight(i);
                list.Add(new HingePiece
                {
                    Hinge = ToyFactory.DominoSetHinge(i),
                    // About -Z the top goes toward +X.
                    Axis = Vector3.back,
                    Center = ToyFactory.DominoSetPieceCenter(i),
                    // The box in the piece's own frame, which the set turns by 90 degrees about Y.
                    Size = ToyFactory.DominoPieceSize(height),
                    Rotation = ToyFactory.DominoSetPieceRotation,
                    Height = height,
                });
            }
            ToyInfo info = ToyInfo.Of(set.GameObject);
            Color color = info != null ? info.Candy : Palette.Lime;
            return new HingeChainOptions
            {
                Prop = set,
                Pieces = list,
                PiecesChild = ToyFactory.DominoSetPieces,
                PieceFactory = i =>
                {
                    Vector2Int pips = ToyFactory.DominoSetPips(i);
                    return ToyFactory.DominoPiece(ToyFactory.DominoSetHeight(i), color, false, pips.x, pips.y, i == 0);
                },
            };
        }

        public ChainState State { get; private set; } = ChainState.Idle;
        public int PieceCount => pieces.Length;
        /// <summary>How many pieces have gone over.</summary>
        public int Fallen { get; private set; }
        /// <summary>True once the last piece has hit the target.</summary>
        public bool Struck { get; private set; }
        public Prop Prop => prop;

        /// <summary>Inside the tick: piece i (0 based) went over far enough to push the next.</summary>
        public event Action<int> PieceFell;
        public event Action TargetStruck;
        public event Action FellShort;

        /// <summary>Degrees piece i leans.</summary>
        public float Angle(int piece) => angles[piece];

        /// <summary>The mover that stands in for piece i while the chain runs.</summary>
        public Mover PieceMover(int piece) => movers[piece];

        /// <summary>World position of piece i's centre, standing up, at the prop's pose and scale now.</summary>
        public Vector3 PieceCenter(int piece) => prop.Transform.TransformPoint(pieces[piece].Center);

        /// <summary>World position of a point of piece i's hinge line now.</summary>
        public Vector3 HingePoint(int piece) => prop.Transform.TransformPoint(pieces[piece].Hinge);

        /// <summary>Would the last piece, at the prop's scale and pose now, reach the target?</summary>
        public bool Reaches()
        {
            if (!options.Target.HasValue) return false;
            int last = pieces.Length - 1;
            return Vector3.Distance(HingePoint(last), options.Target.Value) <= pieces[last].Height * prop.Scale + options.TargetRadius;
        }

        /// <summary>Lets the first piece go. False unless the chain is idle and its prop is in the world.</summary>
        public bool Start()
        {
            if (State != ChainState.Idle || prop.Removed || prop.Held) return false;
            for (int i = 0; i < pieces.Length; i++)
            {
                angles[i] = 0f;
                speeds[i] = 0f;
                released[i] = false;
                fell[i] = false;
                Pose(i, 0f, out Vector3 position, out Quaternion rotation);
                GameObject piece = movers[i].GameObject;
                piece.transform.localScale = Vector3.one * prop.Scale;
                piece.SetActive(true);
                movers[i].Teleport(position, rotation);
            }
            if (piecesChild != null) piecesChild.gameObject.SetActive(false);
            Physics.SyncTransforms();
            Fallen = 0;
            Struck = false;
            released[0] = true;
            angles[0] = options.StartAngle;
            State = ChainState.Running;
            return true;
        }

        protected override void Tick(float dt)
        {
            if (State == ChainState.Running) TickRunning(dt);
            else if (State == ChainState.Resetting) TickResetting();
        }

        void TickRunning(float dt)
        {
            int last = pieces.Length - 1;
            bool moving = false;
            for (int i = 0; i <= last; i++)
            {
                if (!released[i]) continue;
                float limit = i == last ? 90f : options.RestAngle;
                if (angles[i] >= limit) continue;
                moving = true;
                float height = Mathf.Max(0.01f, pieces[i].Height * prop.Scale);
                speeds[i] += 1.5f * Game.Gravity / height * Mathf.Sin(angles[i] * Mathf.Deg2Rad) * dt;
                float next = Mathf.Min(limit, angles[i] + speeds[i] * Mathf.Rad2Deg * dt);

                if (i == last && options.Target.HasValue && TouchesTarget(i, next))
                {
                    angles[i] = next;
                    MovePiece(i);
                    fell[i] = true;
                    Fallen++;
                    Struck = true;
                    State = ChainState.Done;
                    PieceFell?.Invoke(i);
                    Game.Events.RaiseChainPieceFell(Event(options.Target.Value, prop, 0f, 0f, i));
                    TargetStruck?.Invoke();
                    Game.Events.RaiseChainTargetStruck(Event(options.Target.Value, prop, speeds[i] * height));
                    return;
                }

                angles[i] = next;
                MovePiece(i);
                if (!fell[i] && angles[i] >= (i == last ? 90f : contactAngle))
                {
                    fell[i] = true;
                    Fallen++;
                    if (i < last)
                    {
                        released[i + 1] = true;
                        angles[i + 1] = options.StartAngle;
                    }
                    PieceFell?.Invoke(i);
                    Game.Events.RaiseChainPieceFell(Event(movers[i].Position, prop, 0f, 0f, i));
                    if (State != ChainState.Running) return;
                }
            }
            if (moving) return;
            State = ChainState.Done;
            if (!options.Target.HasValue) return;
            FellShort?.Invoke();
            Game.Events.RaiseChainFellShort(Event(movers[last].Position, prop));
        }

        // Does the piece, leaning at this angle, come within the target's radius? It is a slab from its
        // hinge to its top.
        bool TouchesTarget(int piece, float atAngle)
        {
            HingePiece p = pieces[piece];
            Quaternion turn = Quaternion.AngleAxis(atAngle, p.Axis.normalized);
            Transform t = prop.Transform;
            Vector3 foot = t.TransformPoint(p.Hinge);
            Vector3 top = t.TransformPoint(p.Hinge + turn * (Vector3.up * p.Height));
            Vector3 along = top - foot;
            float length = along.magnitude;
            if (length < 1e-5f) return false;
            Vector3 target = options.Target.Value;
            float s = Mathf.Clamp(Vector3.Dot(target - foot, along) / (length * length), 0f, 1f);
            return Vector3.Distance(target, foot + along * s) <= options.TargetRadius;
        }

        void MovePiece(int i)
        {
            Pose(i, angles[i], out Vector3 position, out Quaternion rotation);
            movers[i].MoveTo(position, rotation);
        }

        // World pose of a piece leaning by an angle, at the prop's pose and scale.
        void Pose(int i, float atAngle, out Vector3 position, out Quaternion rotation)
        {
            HingePiece p = pieces[i];
            Quaternion turn = Quaternion.AngleAxis(atAngle, p.Axis.sqrMagnitude > 1e-8f ? p.Axis.normalized : Vector3.back);
            Transform t = prop.Transform;
            position = t.TransformPoint(p.Hinge + turn * (p.Center - p.Hinge));
            rotation = t.rotation * turn * (p.Rotation.w == 0f && p.Rotation.x == 0f && p.Rotation.y == 0f && p.Rotation.z == 0f ? Quaternion.identity : p.Rotation);
        }

        /// <summary>Stands the pieces up again, the last one first, over ResetSeconds, then gives the prop its own pieces back.</summary>
        public override void Reset()
        {
            base.Reset();
            if (State == ChainState.Idle || State == ChainState.Resetting) return;
            for (int i = 0; i < pieces.Length; i++) resetFrom[i] = angles[i];
            resetTick = 0;
            State = ChainState.Resetting;
        }

        /// <summary>The same, at once.</summary>
        public void ResetNow()
        {
            for (int i = 0; i < pieces.Length; i++)
            {
                angles[i] = 0f;
                speeds[i] = 0f;
                released[i] = false;
                fell[i] = false;
                movers[i].GameObject.SetActive(false);
            }
            if (piecesChild != null && !prop.Removed) piecesChild.gameObject.SetActive(true);
            Physics.SyncTransforms();
            Fallen = 0;
            Struck = false;
            State = ChainState.Idle;
        }

        void TickResetting()
        {
            resetTick++;
            int n = pieces.Length;
            float total = Mathf.Max(Sim.Dt, options.ResetSeconds);
            float t = resetTick * Sim.Dt;
            // Each piece takes half the time; they start one after the other, the last first.
            float each = total * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float delay = n > 1 ? (total - each) * (n - 1 - i) / (n - 1) : 0f;
                float u = GadgetKit.Smooth((t - delay) / each);
                angles[i] = Mathf.Lerp(resetFrom[i], 0f, u);
                MovePiece(i);
            }
            if (t >= total) ResetNow();
        }

        static GameObject DefaultPiece(HingePiece piece)
        {
            var root = new GameObject("Piece");
            float bevel = Mathf.Min(piece.Size.x, Mathf.Min(piece.Size.y, piece.Size.z)) * 0.08f;
            GadgetKit.Visual(root.transform, "Visual", GadgetKit.RoundedBoxMesh(piece.Size, bevel), Materials.Toy(ToyRecipe.PlainProp, Palette.Birch));
            return root;
        }
    }
}
