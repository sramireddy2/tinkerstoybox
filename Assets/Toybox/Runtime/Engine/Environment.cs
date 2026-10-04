using System.Collections.Generic;
using UnityEngine;

namespace Toybox.Engine
{
    /// <summary>What a collider box of the environment is.</summary>
    public enum EnvironmentBoxKind
    {
        /// <summary>The shell's real floor.</summary>
        Floor,
        Ceiling,
        WallNegX,
        WallPosX,
        WallNegZ,
        WallPosZ,
        /// <summary>The glass in the window opening, flush with the inside of the window wall.</summary>
        WindowPane,
        /// <summary>The rug, bench top or shelf whose top is the play plane.</summary>
        Island,
        Furniture,
    }

    /// <summary>One collider of the environment: an oriented box in world space.</summary>
    public struct EnvironmentBox
    {
        public EnvironmentBoxKind Kind;
        public string Name;
        public Vector3 Center;
        public Vector3 Size;
        public Quaternion Rotation;
        /// <summary>Index into <see cref="EnvironmentDescriptor.Pieces"/> for furniture, else -1.</summary>
        public int Piece;

        public Bounds Bounds
        {
            get
            {
                // The axis-aligned box around the rotated one.
                Vector3 half = Size * 0.5f;
                Vector3 x = Rotation * new Vector3(half.x, 0f, 0f), y = Rotation * new Vector3(0f, half.y, 0f), z = Rotation * new Vector3(0f, 0f, half.z);
                var extent = new Vector3(Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x), Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y), Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z));
                return new Bounds(Center, extent * 2f);
            }
        }
    }

    /// <summary>
    /// One piece of backdrop furniture where the solve put it. Its collider boxes are
    /// <see cref="EnvironmentDescriptor.Boxes"/>[FirstBox .. FirstBox + BoxCount - 1]; the visual is built by
    /// the render side to fill exactly those boxes.
    /// </summary>
    public sealed class EnvironmentPiece
    {
        public FurnitureKind Kind { get; internal set; }
        /// <summary>Which of several pieces of the same kind this is (0, 1, ...), for seeding variations.</summary>
        public int Index { get; internal set; }
        /// <summary>
        /// The piece's origin: the middle of its footprint on the surface it stands on, or for pieces on a
        /// wall the point of the wall they hang from.
        /// </summary>
        public Vector3 Position { get; internal set; }
        /// <summary>The piece's local +Z points this way (for wall pieces: into the room).</summary>
        public Quaternion Rotation { get; internal set; }
        public int FirstBox { get; internal set; }
        public int BoxCount { get; internal set; }
    }

    /// <summary>
    /// The room a level stands in, solved around the level after its Build (ART_BIBLE 6.2): the sun lands
    /// on the level, the window is where that sun comes from, the shell and the furniture stand around
    /// the level's own geometry. Game builds this on every level load and creates the colliders it lists;
    /// the render side builds everything visible from the same numbers. Immutable once built.
    /// </summary>
    public sealed class EnvironmentDescriptor
    {
        // The window opening of ART_BIBLE 6.1: 70 wide, 105 high, its sill 30 above the play plane.
        public const float WindowWidth = 70f, WindowHeight = 105f, WindowSill = 30f;
        /// <summary>Half the mullion's width as a fraction of the half opening (<c>_WinMullion</c>).</summary>
        public const float WindowMullion = 0.04f;
        /// <summary>The shell: 400 x 170 x 400, the 170 measured from the play plane to the ceiling.</summary>
        public const float ShellSize = 400f, ShellHeight = 170f;
        /// <summary>The highest the window's centre is raised to when the wall has to stand further away.</summary>
        public const float MaxWindowCenter = 120f;

        public EnvironmentPreset Preset { get; internal set; }
        /// <summary>True if there is a room (false for the "none" preset: the sun is solved, nothing else exists).</summary>
        public bool HasRoom => Preset.HasRoom;
        /// <summary>How many earlier levels used the preset; it shifts the sun.</summary>
        public int Visit { get; internal set; }

        /// <summary>B: the bounds of the level's static geometry after Build.</summary>
        public Bounds LevelBounds { get; internal set; }
        /// <summary>The play plane: <see cref="LevelDefinition.GroundY"/>.</summary>
        public float GroundY { get; internal set; }
        /// <summary>F: the centre of the level's bounds on the play plane. The sun's ray through the window centre lands here.</summary>
        public Vector3 Focus { get; internal set; }

        /// <summary>Degrees above the horizon, after the repeat-visit adjustment.</summary>
        public float SunElevation { get; internal set; }
        /// <summary>Degrees clockwise from +Z seen from above, after the repeat-visit adjustment.</summary>
        public float SunAzimuth { get; internal set; }
        /// <summary>s: unit vector toward the sun. A directional light looks along -s.</summary>
        public Vector3 SunDirection { get; internal set; }
        /// <summary>The rotation of a directional light shining from the sun.</summary>
        public Quaternion SunRotation => Quaternion.LookRotation(-SunDirection, Vector3.up);

        /// <summary>The window wall: -1 is the shell's -X wall, +1 its +X wall.</summary>
        public int WindowSide { get; internal set; }
        /// <summary>Angle between the sun's azimuth and the window wall's outward direction, in degrees.</summary>
        public float Delta { get; internal set; }
        /// <summary>L: distance from F to the window wall.</summary>
        public float WallDistance { get; internal set; }
        /// <summary>True if the level was too wide for the wall to stand where the sun wanted it.</summary>
        public bool WallClamped { get; internal set; }
        /// <summary>Height of the window's centre above the play plane (82.5 unless the wall was clamped).</summary>
        public float WindowCenterHeight { get; internal set; }
        /// <summary>W: the centre of the window opening, on the inner face of the window wall (<c>_WinO</c>).</summary>
        public Vector3 WindowCenter { get; internal set; }
        /// <summary>
        /// How far along its wall the window had to slide from where the sun wanted it, because the back
        /// wall of an elevated preset stood in the way (a short level). 0 normally; then the ray through the
        /// window's centre lands exactly on <see cref="Focus"/>, otherwise this far beside it.
        /// </summary>
        public float WindowShift { get; internal set; }
        /// <summary>The window wall's inward normal (<c>_WinN</c>).</summary>
        public Vector3 WindowNormal { get; internal set; }
        /// <summary>Unit horizontal tangent of the window wall.</summary>
        public Vector3 WindowTangent { get; internal set; }
        /// <summary>Half the opening: (35, 52.5).</summary>
        public Vector2 WindowHalfSize => new Vector2(WindowWidth * 0.5f, WindowHeight * 0.5f);
        /// <summary><c>_WinU</c>: the horizontal tangent divided by the half width.</summary>
        public Vector3 WindowU => WindowTangent / (WindowWidth * 0.5f);
        /// <summary><c>_WinV</c>: up divided by the half height.</summary>
        public Vector3 WindowV => Vector3.up / (WindowHeight * 0.5f);

        /// <summary>The inside of the shell (<c>_ShellMin</c>, <c>_ShellMax</c>).</summary>
        public Vector3 ShellMin { get; internal set; }
        public Vector3 ShellMax { get; internal set; }
        /// <summary>Height of the shell's real floor: GroundY minus the preset's floor drop.</summary>
        public float FloorY => ShellMin.y;

        /// <summary>The glint basis: <c>_WinDir</c> = s, completed by a right and an up vector.</summary>
        public Vector3 GlintDir => SunDirection;
        public Vector3 GlintRight { get; internal set; }
        public Vector3 GlintUp { get; internal set; }

        /// <summary>Kind of the island under the level (rug, bench, shelf), or None.</summary>
        public IslandKind Island { get; internal set; }
        /// <summary>The island's box; its top face is the play plane. Size zero if there is none.</summary>
        public Bounds IslandBounds { get; internal set; }

        /// <summary>The backdrop furniture that found room, in a fixed order.</summary>
        public IReadOnlyList<EnvironmentPiece> Pieces => pieces;
        /// <summary>
        /// Every collider of the room: the six shell faces, the window pane, the island, then the furniture
        /// boxes piece by piece. Game creates one BoxCollider on the Default layer for each.
        /// </summary>
        public IReadOnlyList<EnvironmentBox> Boxes => boxes;

        internal readonly List<EnvironmentPiece> pieces = new List<EnvironmentPiece>();
        internal readonly List<EnvironmentBox> boxes = new List<EnvironmentBox>();

        /// <summary>The first box of a kind (the shell faces, the pane and the island exist at most once).</summary>
        public bool TryGetBox(EnvironmentBoxKind kind, out EnvironmentBox box)
        {
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Kind != kind) continue;
                box = boxes[i];
                return true;
            }
            box = default;
            return false;
        }
    }
}
