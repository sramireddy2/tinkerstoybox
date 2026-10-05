using System;
using UnityEngine;

namespace Toybox.Art
{
    /// <summary>
    /// A dip: the four tones one room is dipped in (ART_BIBLE 2.4), plus the candy colour that pops against
    /// it (the level's key toy takes it) and the one that is too close to the room's hue to be used there.
    /// Colours are sRGB, as authored; convert with <see cref="Palette.Lin(Color)"/> before pushing to a shader.
    /// </summary>
    public sealed class Dip
    {
        public string Name { get; }
        public Color Light { get; }
        public Color Mid { get; }
        public Color Deep { get; }
        public Color Haze { get; }
        public Color Hero { get; }
        /// <summary>Null for the night dip, which bans nothing.</summary>
        public Color? Banned { get; }
        public bool Night { get; }

        internal Dip(string name, string light, string mid, string deep, string haze, Color hero, Color? banned, bool night = false)
        {
            Name = name;
            Light = Palette.Hex(light);
            Mid = Palette.Hex(mid);
            Deep = Palette.Hex(deep);
            Haze = Palette.Hex(haze);
            Hero = hero;
            Banned = banned;
            Night = night;
        }

        /// <summary>True if a toy in this room may carry the candy colour.</summary>
        public bool Allows(Color candy) => Banned == null || !Palette.Same(Banned.Value, candy);

        public override string ToString() => Name;
    }

    /// <summary>
    /// A gadget signal (ART_BIBLE 2.3): always emissive on an Ink body. The colour is sRGB; the emission
    /// pushed to a shader is <see cref="EmissionAt"/> of a gain - the signal's hue with only its largest
    /// channel above 1. Idle pulses between PulseMin and Gain.
    /// </summary>
    public readonly struct Signal : IEquatable<Signal>
    {
        public readonly string Name;
        public readonly Color Color;
        /// <summary>HDR gain (the steady value, or the top of the pulse).</summary>
        public readonly float Gain;
        /// <summary>Bottom of the pulse; equal to Gain for a steady signal.</summary>
        public readonly float PulseMin;
        /// <summary>Pulses per second; 0 for a steady signal.</summary>
        public readonly float PulseHz;
        /// <summary>Hazard surfaces also carry 45 degree Ink stripes (pitch 0.6 units, 50% duty).</summary>
        public readonly bool Striped;

        public Signal(string name, Color color, float gain, float pulseMin = -1f, float pulseHz = 0f, bool striped = false)
        {
            Name = name;
            Color = color;
            Gain = gain;
            PulseMin = pulseMin < 0f ? gain : pulseMin;
            PulseHz = pulseHz;
            Striped = striped;
        }

        /// <summary>Linear HDR emission at the top of the pulse.</summary>
        public Color Emission => EmissionAt(Gain);

        /// <summary>
        /// Linear HDR emission at a gain between 0 and <see cref="Gain"/> (or beyond).
        ///
        /// Not simply colour times gain: the picture has no tonemapping curve, so what exceeds 1 clips
        /// channel by channel and the hue turns - Amber at 2.2 comes out lemon, Go at 2.6 cyan. Instead
        /// the signal's hue is shown at full brightness when the gain is the signal's own (dimmer below
        /// it, which is what a pulse looks like where there is no bloom), and only its largest channel
        /// carries the gain above 1, which is what bloom picks up. The channels that stay below 1 keep
        /// their ratio, so the lamp's core is the signal's colour on every tier.
        /// </summary>
        public Color EmissionAt(float gain)
        {
            Color linear = Palette.Lin(Color);
            float largest = Mathf.Max(linear.r, Mathf.Max(linear.g, linear.b));
            if (gain <= 0f || largest <= 0f) return new Color(0f, 0f, 0f, 1f);
            // How bright the core is shown: full at the signal's own gain.
            float shown = Gain > 0f ? Mathf.Clamp01(gain / Gain) : 1f;
            // The largest channel goes over 1 only near full brightness, so a dim core keeps its hue too.
            float ramp = Mathf.Clamp01((shown - 0.75f) / 0.25f);
            float hdr = Mathf.Lerp(shown, Mathf.Max(shown, gain * largest), ramp * ramp * (3f - 2f * ramp));
            float r = linear.r / largest, g = linear.g / largest, b = linear.b / largest;
            return new Color(r >= 0.999f ? hdr : r * shown, g >= 0.999f ? hdr : g * shown, b >= 0.999f ? hdr : b * shown, 1f);
        }

        /// <summary>Gain at a moment of presentation time (a cosine between PulseMin and Gain).</summary>
        public float GainAt(float seconds)
        {
            if (PulseHz <= 0f) return Gain;
            float wave = 0.5f - 0.5f * Mathf.Cos(seconds * PulseHz * Mathf.PI * 2f);
            return Mathf.Lerp(PulseMin, Gain, wave);
        }

        public bool Equals(Signal other) => Name == other.Name && Palette.Same(Color, other.Color) && Gain == other.Gain;
        public override bool Equals(object obj) => obj is Signal other && Equals(other);
        public override int GetHashCode() => (Name ?? "").GetHashCode() ^ ((Color32)Color).GetHashCode();
        public override string ToString() => Name;
    }

    /// <summary>
    /// Every colour of the art bible's palette (section 2) by name. All values are sRGB exactly as written
    /// in the document - right for UGUI and for comparing with a screenshot. Shaders take linear values:
    /// <see cref="Lin(string)"/> / <see cref="Lin(Color)"/> convert once, and the result is pushed with
    /// SetVector / SetGlobalVector, never through an implicit SetColor conversion.
    ///
    /// Pure data and arithmetic: usable from simulation code, headless, and off the main thread.
    /// </summary>
    public static class Palette
    {
        // ---- 2.1 Neutrals ----------------------------------------------------------------------------
        /// <summary>Sticker border, UI surfaces, skirting, window frames, toy secondary parts, rim light.</summary>
        public static readonly Color Paper = Hex("#FFFDF7");
        /// <summary>UI text, gadget bodies, vignette, peel shadow. The darkest value allowed anywhere.</summary>
        public static readonly Color Ink = Hex("#2B2140");
        /// <summary>Cardboard toys, locked level cards.</summary>
        public static readonly Color Kraft = Hex("#C99A62");
        /// <summary>Raw wood at chipped edges; non-grabbable wooden physics props.</summary>
        public static readonly Color Birch = Hex("#E9C9A0");
        /// <summary>Gadget metal parts; non-grabbable metal props.</summary>
        public static readonly Color Steel = Hex("#C9CED8");
        /// <summary>
        /// Bare toy metal: the body of the Level 2 thimble, the one toy that is not candy all over (it wears
        /// its candy as a band). A step darker and greyer than Steel: the room gives a mirror its tint, and
        /// the die-cut border of a held toy has to show against it.
        /// </summary>
        public static readonly Color Silver = Hex("#B4B6B9");

        // ---- 2.2 Candy: grabbable toys only ----------------------------------------------------------
        public static readonly Color Cherry = Hex("#FF2E55");
        public static readonly Color Tangerine = Hex("#FF7A1A");
        public static readonly Color Lemon = Hex("#FFCE1F");
        public static readonly Color Lime = Hex("#7FDB2E");
        public static readonly Color Lagoon = Hex("#18A8FF");
        public static readonly Color Grape = Hex("#8A4BFF");
        public static readonly Color Bubblegum = Hex("#FF5FB0");

        /// <summary>The seven candy colours in the order of the document, for picking by index.</summary>
        public static readonly Color[] Candy = { Cherry, Tangerine, Lemon, Lime, Lagoon, Grape, Bubblegum };
        public static readonly string[] CandyNames = { "Cherry", "Tangerine", "Lemon", "Lime", "Lagoon", "Grape", "Bubblegum" };

        // ---- 2.3 Signal: gadgets only ----------------------------------------------------------------
        /// <summary>Idle / waiting: pulses 1.2 to 2.2 once a second.</summary>
        public static readonly Signal Amber = new Signal("Amber", Hex("#FFB627"), 2.2f, 1.2f, 1f);
        /// <summary>Satisfied: steady.</summary>
        public static readonly Signal Go = new Signal("Go", Hex("#2BE8A6"), 2.6f);
        /// <summary>Lasers, kill surfaces: steady, and striped with Ink.</summary>
        public static readonly Signal Hazard = new Signal("Hazard", Hex("#FF2BD6"), 3.0f, striped: true);
        /// <summary>The exit portal, drawn as the four-pane mark.</summary>
        public static readonly Signal Exit = new Signal("Exit", Hex("#FFFFFF"), 3.0f);

        public static readonly Signal[] Signals = { Amber, Go, Hazard, Exit };

        /// <summary>Hazard stripes: 45 degrees, this pitch in units, half Ink.</summary>
        public const float HazardStripePitch = 0.6f;

        // ---- 2.4 Dips: the room ----------------------------------------------------------------------
        public static readonly Dip Mint = new Dip("Mint", "#DDF5EA", "#B4E6D2", "#7CCDB3", "#EAF8F1", Cherry, Lime);
        public static readonly Dip Butter = new Dip("Butter", "#FFF4CC", "#FFE699", "#F2CC5C", "#FFF9E3", Grape, Lemon);
        public static readonly Dip Pool = new Dip("Pool", "#DCEEFB", "#B5D9F5", "#7FB8E8", "#EAF5FD", Tangerine, Lagoon);
        public static readonly Dip Peach = new Dip("Peach", "#FFE6D6", "#FFCDB2", "#F2A88A", "#FFF1E8", Lagoon, Tangerine);
        public static readonly Dip Lilac = new Dip("Lilac", "#E9E2FA", "#CFC2F2", "#A996E0", "#F1ECFC", Lemon, Grape);
        public static readonly Dip Plum = new Dip("Plum", "#5A4A82", "#453769", "#2F2550", "#3A2E5C", Lime, null, night: true);

        public static readonly Dip[] Dips = { Mint, Butter, Pool, Peach, Lilac, Plum };

        // ---- Light and sky colours named elsewhere in the document (5.1, 6.3, 6.4) ----------------------
        /// <summary>The key light by day.</summary>
        public static readonly Color Sun = Hex("#FFF1DC");
        /// <summary>The key light and the glint at night.</summary>
        public static readonly Color Moon = Hex("#BFD0FF");
        /// <summary>Window glint by day.</summary>
        public static readonly Color GlintDay = Hex("#FFF6E8");
        /// <summary>Ambient sky colour of the night rig.</summary>
        public static readonly Color NightAmbient = Hex("#8C7FD0");
        /// <summary>Fairy lights, the night-light and the default window patch: warm white, never a Signal colour.</summary>
        public static readonly Color WarmWhite = Hex("#FFE9C4");
        public static readonly Color SkyTop = Hex("#BFE3FF");
        public static readonly Color SkyBottom = Hex("#FFF6E0");
        public static readonly Color NightSkyTop = Hex("#1E1650");
        public static readonly Color NightSkyBottom = Hex("#3A2E5C");
        /// <summary>The neutral the felt recipe is mixed toward.</summary>
        public static readonly Color Grey = Hex("#808080");

        /// <summary>The dip of an environment preset key (ART_BIBLE 6.4); Mint for anything unknown.</summary>
        public static Dip DipOf(string presetKey)
        {
            switch (presetKey)
            {
                case "sunny-rug": return Mint;
                case "block-hall": return Butter;
                case "pegboard-workbench": return Pool;
                case "cardboard-box": return Peach;
                case "high-shelf": return Lilac;
                case "night-light": return Plum;
                default: return Mint;
            }
        }

        /// <summary>Name of a candy colour, or null if the colour is not one.</summary>
        public static string CandyName(Color color)
        {
            for (int i = 0; i < Candy.Length; i++)
                if (Same(Candy[i], color)) return CandyNames[i];
            return null;
        }

        public static bool IsCandy(Color color) => CandyName(color) != null;

        /// <summary>"#RRGGBB" or "#RRGGBBAA" (the '#' is optional) as an sRGB colour. Throws on anything else.</summary>
        public static Color Hex(string hex)
        {
            if (hex == null) throw new ArgumentNullException(nameof(hex));
            int start = hex.Length > 0 && hex[0] == '#' ? 1 : 0;
            int digits = hex.Length - start;
            if (digits != 6 && digits != 8) throw new FormatException("Not a colour: '" + hex + "' (expected #RRGGBB or #RRGGBBAA).");
            int r = Byte(hex, start), g = Byte(hex, start + 2), b = Byte(hex, start + 4);
            int a = digits == 8 ? Byte(hex, start + 6) : 255;
            return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
        }

        /// <summary>The linear value of an sRGB hex colour, for SetVector and friends.</summary>
        public static Color Lin(string hex) => Lin(Hex(hex));

        /// <summary>sRGB to linear. Alpha is left alone.</summary>
        public static Color Lin(Color srgb) => new Color(ToLinear(srgb.r), ToLinear(srgb.g), ToLinear(srgb.b), srgb.a);

        /// <summary>Linear to sRGB. Alpha is left alone.</summary>
        public static Color Srgb(Color linear) => new Color(ToSrgb(linear.r), ToSrgb(linear.g), ToSrgb(linear.b), linear.a);

        /// <summary>Mixes two sRGB colours in linear light (what a shader's lerp of the linear values gives) and returns sRGB.</summary>
        public static Color Mix(Color from, Color to, float t) => Srgb(Color.LerpUnclamped(Lin(from), Lin(to), t));

        /// <summary>"#RRGGBB" of an sRGB colour.</summary>
        public static string ToHex(Color color)
        {
            Color32 c = color;
            return "#" + c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2");
        }

        /// <summary>Equal as 8-bit colours.</summary>
        public static bool Same(Color a, Color b)
        {
            Color32 x = a, y = b;
            return x.r == y.r && x.g == y.g && x.b == y.b && x.a == y.a;
        }

        // The exact sRGB transfer function (not the 2.2 approximation), so a hex colour pushed linear and
        // written to an sRGB target comes out as the same hex.
        static float ToLinear(float c)
        {
            if (c <= 0.04045f) return c / 12.92f;
            return Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        static float ToSrgb(float c)
        {
            if (c <= 0f) return 0f;
            if (c <= 0.0031308f) return c * 12.92f;
            return 1.055f * Mathf.Pow(c, 1f / 2.4f) - 0.055f;
        }

        static int Byte(string text, int index) => Nibble(text[index]) * 16 + Nibble(text[index + 1]);

        static int Nibble(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            throw new FormatException("Not a hex digit: '" + c + "'.");
        }
    }
}
