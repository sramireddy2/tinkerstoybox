using System;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Art
{
    /// <summary>What a static surface is, which decides its tone pair under the top-light rule (ART_BIBLE 2.5, 4.4).</summary>
    public enum RoomSurface
    {
        /// <summary>The shell's floor: mid, corner gradient, the preset's floor pattern.</summary>
        ShellFloor,
        /// <summary>The shell's walls: light, mid below the dado line, corner gradient, the preset's wall pattern.</summary>
        ShellWall,
        /// <summary>Skirting, window and door frames: Paper, no pattern.</summary>
        Trim,
        /// <summary>Platforms, ramps and walls a level builds: light on top, deep on the sides.</summary>
        LevelStatic,
        /// <summary>Backdrop furniture: mid on top, deep on the sides, vertex AO.</summary>
        Furniture,
    }

    /// <summary>
    /// A material recipe for <c>Toybox/RoomLit</c>: every per-material property of ART_BIBLE 3.3. Colours
    /// are sRGB as authored (dip tones); they are converted when pushed to the shader. Recipes are values:
    /// two with the same numbers are equal and share one material (<see cref="Materials.Room(RoomRecipe)"/>).
    /// </summary>
    public sealed class RoomRecipe : IEquatable<RoomRecipe>
    {
        /// <summary><c>_DadoY</c> value that switches the dado off.</summary>
        public const float NoDado = -1e5f;
        /// <summary>Height of the dado line above the play plane.</summary>
        public const float DadoHeight = 30f;

        public string Name = "Room";
        /// <summary><c>_ColorTop</c>: faces whose normal points up (normal.y above about 0.7).</summary>
        public Color Top = Color.white;
        /// <summary><c>_ColorSide</c>: every other face.</summary>
        public Color Side = Color.white;
        /// <summary><c>_ColorDado</c>: side faces below <see cref="DadoY"/>.</summary>
        public Color Dado = Color.white;
        /// <summary><c>_DadoY</c>, a world height; <see cref="NoDado"/> disables it.</summary>
        public float DadoY = NoDado;
        /// <summary><c>_Pattern</c> and <c>_PatternA</c>.</summary>
        public PatternSpec Pattern = PatternSpec.None;
        /// <summary><c>_Corner</c>: the corner gradient of shell surfaces.</summary>
        public bool Corner;
        public CullMode Cull = CullMode.Back;

        string signature;

        /// <summary>A copy with some values changed.</summary>
        public RoomRecipe With(Action<RoomRecipe> change)
        {
            var copy = (RoomRecipe)MemberwiseClone();
            copy.signature = null;
            change?.Invoke(copy);
            copy.signature = null;
            return copy;
        }

        /// <summary>The recipe of ART_BIBLE 4.4 for a kind of surface in a dip. <paramref name="groundY"/> is the play plane (for the dado line).</summary>
        public static RoomRecipe For(RoomSurface surface, Dip dip, PatternSpec pattern = default, float groundY = 0f)
        {
            dip ??= Palette.Mint;
            switch (surface)
            {
                case RoomSurface.ShellFloor:
                    return new RoomRecipe { Name = dip.Name + " Floor", Top = dip.Mid, Side = dip.Mid, Dado = dip.Mid, Pattern = pattern, Corner = true };
                case RoomSurface.ShellWall:
                    return new RoomRecipe
                    {
                        Name = dip.Name + " Wall", Top = dip.Light, Side = dip.Light, Dado = dip.Mid, DadoY = groundY + DadoHeight,
                        Pattern = pattern, Corner = true,
                    };
                case RoomSurface.Trim:
                    return new RoomRecipe { Name = "Trim", Top = Palette.Paper, Side = Palette.Paper, Dado = Palette.Paper };
                case RoomSurface.Furniture:
                    return new RoomRecipe { Name = dip.Name + " Furniture", Top = dip.Mid, Side = dip.Deep, Dado = dip.Deep, Pattern = pattern };
                default:
                    return new RoomRecipe { Name = dip.Name + " Static", Top = dip.Light, Side = dip.Deep, Dado = dip.Deep, Pattern = pattern };
            }
        }

        /// <summary>One colour on every face, no pattern (test geometry, markers).</summary>
        public static RoomRecipe Solid(Color color) =>
            new RoomRecipe { Name = "Solid " + Palette.ToHex(color), Top = color, Side = color, Dado = color };

        /// <summary>Every value in one string: what two recipes must agree on to share a material.</summary>
        public string Signature
        {
            get
            {
                if (signature != null) return signature;
                var s = new StringBuilder(128);
                s.Append(Name).Append('|');
                Put(s, Top); Put(s, Side); Put(s, Dado); Put(s, DadoY);
                s.Append((int)Pattern.Pattern).Append('|');
                Put(s, Pattern.Pitch); Put(s, Pattern.Param1); Put(s, Pattern.Param2); Put(s, Pattern.Gain);
                s.Append(Corner ? 1 : 0).Append('|').Append((int)Cull);
                return signature = s.ToString();
            }
        }

        static void Put(StringBuilder s, float value) => s.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('|');

        static void Put(StringBuilder s, Color value)
        {
            Put(s, value.r);
            Put(s, value.g);
            Put(s, value.b);
        }

        public bool Equals(RoomRecipe other) => other != null && (ReferenceEquals(this, other) || Signature == other.Signature);
        public override bool Equals(object obj) => Equals(obj as RoomRecipe);
        public override int GetHashCode() => Signature.GetHashCode();
        public override string ToString() => Name;
    }

    /// <summary>The procedural shape mask of <c>Toybox/Flat</c> (ART_BIBLE 3.5). The value is <c>_Shape</c>.</summary>
    public enum FlatShape
    {
        Quad = 0,
        SoftDisc = 1,
        Ring = 2,
        /// <summary>The four-pane mark (exit portal, reticle).</summary>
        FourPane = 3,
        /// <summary>A disc with a gloss highlight (confetti).</summary>
        GlossDisc = 4,
    }

    /// <summary>The blend presets of <c>Toybox/Flat</c>.</summary>
    public enum FlatBlend
    {
        /// <summary>One, Zero; depth written; queue 2000.</summary>
        Opaque,
        /// <summary>SrcAlpha, OneMinusSrcAlpha; queue 3000.</summary>
        Alpha,
        /// <summary>One, One; queue 3000 (laser cores, the light shaft).</summary>
        Additive,
        /// <summary>DstColor, Zero; queue 3000 (hull shadows).</summary>
        Multiply,
    }

    /// <summary>
    /// A material recipe for <c>Toybox/Flat</c>: unlit, vertex-coloured, HDR tint, shape mask from UV
    /// (ART_BIBLE 3.5). The colour is linear and may exceed 1 - it is what is pushed to the shader.
    /// </summary>
    public sealed class FlatRecipe : IEquatable<FlatRecipe>
    {
        public string Name = "Flat";
        /// <summary><c>_Color</c>: linear, HDR (multiply a <see cref="Palette.Lin(Color)"/> value by its gain).</summary>
        public Color Color = Color.white;
        public FlatShape Shape = FlatShape.Quad;
        /// <summary><c>_Soft</c>: edge softness of the shape mask.</summary>
        public float Soft;
        public FlatBlend Blend = FlatBlend.Opaque;
        /// <summary><c>_ZWrite</c>. Unset: on for Opaque, off for everything else.</summary>
        public bool? ZWrite;
        public CompareFunction ZTest = CompareFunction.LessEqual;
        public CullMode Cull = CullMode.Off;
        /// <summary>Added to the blend mode's render queue (2000 opaque, 3000 otherwise).</summary>
        public int QueueOffset;

        string signature;

        public FlatRecipe With(Action<FlatRecipe> change)
        {
            var copy = (FlatRecipe)MemberwiseClone();
            copy.signature = null;
            change?.Invoke(copy);
            copy.signature = null;
            return copy;
        }

        public bool WritesDepth => ZWrite ?? Blend == FlatBlend.Opaque;
        public int RenderQueue => (Blend == FlatBlend.Opaque ? 2000 : 3000) + QueueOffset;

        public BlendMode SrcBlend
        {
            get
            {
                switch (Blend)
                {
                    case FlatBlend.Alpha: return BlendMode.SrcAlpha;
                    case FlatBlend.Multiply: return BlendMode.DstColor;
                    default: return BlendMode.One;
                }
            }
        }

        public BlendMode DstBlend
        {
            get
            {
                switch (Blend)
                {
                    case FlatBlend.Alpha: return BlendMode.OneMinusSrcAlpha;
                    case FlatBlend.Additive: return BlendMode.One;
                    default: return BlendMode.Zero;
                }
            }
        }

        public string Signature
        {
            get
            {
                if (signature != null) return signature;
                var s = new StringBuilder(96);
                s.Append(Name).Append('|');
                Put(s, Color.r); Put(s, Color.g); Put(s, Color.b); Put(s, Color.a);
                s.Append((int)Shape).Append('|');
                Put(s, Soft);
                s.Append((int)Blend).Append('|').Append(WritesDepth ? 1 : 0).Append('|').Append((int)ZTest).Append('|').Append((int)Cull).Append('|').Append(QueueOffset);
                return signature = s.ToString();
            }
        }

        static void Put(StringBuilder s, float value) => s.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('|');

        public bool Equals(FlatRecipe other) => other != null && (ReferenceEquals(this, other) || Signature == other.Signature);
        public override bool Equals(object obj) => Equals(obj as FlatRecipe);
        public override int GetHashCode() => Signature.GetHashCode();
        public override string ToString() => Name;
    }
}
