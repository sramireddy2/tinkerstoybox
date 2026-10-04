using System;
using UnityEngine;

namespace Toybox.Art
{
    /// <summary>The analytic world-space patterns of <c>Toybox/RoomLit</c> (ART_BIBLE 3.3). The value is <c>_Pattern</c>.</summary>
    public enum RoomPattern
    {
        None = 0,
        /// <summary>Discs on a square grid (the rug).</summary>
        Dots = 1,
        /// <summary>Floorboards, each row offset.</summary>
        Planks = 2,
        /// <summary>A checker of the top and side tones with a grout line.</summary>
        Tiles = 3,
        /// <summary>Vertical bands, 50% duty.</summary>
        Stripes = 4,
        /// <summary>45 degree diamonds with a stitched seam.</summary>
        Quilt = 5,
        /// <summary>Dark holes on a square grid.</summary>
        Pegboard = 6,
        /// <summary>Fine flutes on vertical faces, broad faint ribs elsewhere (cardboard).</summary>
        Corrugated = 7,
    }

    /// <summary>
    /// A pattern and its numbers: <c>_Pattern</c> and <c>_PatternA</c> = (pitch, param1, param2, gain) of
    /// <c>Toybox/RoomLit</c>. All lengths are world units and never change with anything - the patterns
    /// are the game's absolute ruler.
    /// </summary>
    public readonly struct PatternSpec : IEquatable<PatternSpec>
    {
        /// <summary>The default contrast of a pattern against its tone.</summary>
        public const float DefaultGain = 0.04f;

        public readonly RoomPattern Pattern;
        /// <summary>Dots, Tiles, Stripes, Quilt, Pegboard: the pitch. Planks: board width. Corrugated: flute pitch.</summary>
        public readonly float Pitch;
        /// <summary>Dots, Pegboard: radius. Planks: board length. Tiles: grout width. Quilt: seam width. Corrugated: rib pitch.</summary>
        public readonly float Param1;
        /// <summary>Planks: gap width. Unused otherwise.</summary>
        public readonly float Param2;
        /// <summary>How much the pattern changes the albedo (the pattern function returns -1..1).</summary>
        public readonly float Gain;

        public PatternSpec(RoomPattern pattern, float pitch, float param1 = 0f, float param2 = 0f, float gain = DefaultGain)
        {
            Pattern = pattern;
            Pitch = pitch;
            Param1 = param1;
            Param2 = param2;
            Gain = gain;
        }

        /// <summary><c>_PatternA</c>.</summary>
        public Vector4 A => new Vector4(Pitch, Param1, Param2, Gain);

        public static readonly PatternSpec None = new PatternSpec(RoomPattern.None, 1f, 0f, 0f, 0f);
        /// <summary>Rug dots: pitch 8, radius 1.4.</summary>
        public static readonly PatternSpec Dots = new PatternSpec(RoomPattern.Dots, 8f, 1.4f);
        /// <summary>Floorboards: width 4, length 60, gap 0.12.</summary>
        public static readonly PatternSpec Planks = new PatternSpec(RoomPattern.Planks, 4f, 60f, 0.12f);
        /// <summary>Tiles: pitch 5, grout 0.15.</summary>
        public static readonly PatternSpec Tiles = new PatternSpec(RoomPattern.Tiles, 5f, 0.15f);
        /// <summary>Wallpaper stripes: pitch 12.</summary>
        public static readonly PatternSpec Stripes = new PatternSpec(RoomPattern.Stripes, 12f);
        /// <summary>Quilt: pitch 6, seam 0.1.</summary>
        public static readonly PatternSpec Quilt = new PatternSpec(RoomPattern.Quilt, 6f, 0.1f);
        /// <summary>Pegboard: pitch 0.85, hole radius 0.11, and the holes are dark (gain -0.25).</summary>
        public static readonly PatternSpec Pegboard = new PatternSpec(RoomPattern.Pegboard, 0.85f, 0.11f, 0f, -0.25f);
        /// <summary>Corrugated cardboard: flute 0.15, rib 0.5.</summary>
        public static readonly PatternSpec Corrugated = new PatternSpec(RoomPattern.Corrugated, 0.15f, 0.5f);

        /// <summary>The default numbers of a pattern (ART_BIBLE 3.3).</summary>
        public static PatternSpec Of(RoomPattern pattern)
        {
            switch (pattern)
            {
                case RoomPattern.Dots: return Dots;
                case RoomPattern.Planks: return Planks;
                case RoomPattern.Tiles: return Tiles;
                case RoomPattern.Stripes: return Stripes;
                case RoomPattern.Quilt: return Quilt;
                case RoomPattern.Pegboard: return Pegboard;
                case RoomPattern.Corrugated: return Corrugated;
                default: return None;
            }
        }

        public PatternSpec WithGain(float gain) => new PatternSpec(Pattern, Pitch, Param1, Param2, gain);
        public PatternSpec WithPitch(float pitch) => new PatternSpec(Pattern, pitch, Param1, Param2, Gain);

        public bool Equals(PatternSpec other) =>
            Pattern == other.Pattern && Pitch == other.Pitch && Param1 == other.Param1 && Param2 == other.Param2 && Gain == other.Gain;

        public override bool Equals(object obj) => obj is PatternSpec other && Equals(other);
        public override int GetHashCode() => ((int)Pattern * 397) ^ Pitch.GetHashCode() ^ (Param1.GetHashCode() << 3) ^ (Gain.GetHashCode() << 7);
        public override string ToString() => Pattern == RoomPattern.None ? "None" : Pattern + " " + Pitch.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
