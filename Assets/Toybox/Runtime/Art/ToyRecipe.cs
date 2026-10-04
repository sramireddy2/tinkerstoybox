using System;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Art
{
    /// <summary>The packed detail textures of ART_BIBLE 4.2 (R albedo, G smoothness, B height; 128 is neutral).</summary>
    public enum DetailTexture
    {
        Neutral,
        Peel,
        Brush,
        Stipple,
        Streak,
        Fibre,
        Speckle,
        Pore,
        Barb,
    }

    /// <summary>How a toy material is blended into the frame.</summary>
    public enum ToyBlend
    {
        /// <summary>One, Zero; depth written; queue 2000.</summary>
        Opaque,
        /// <summary>SrcAlpha, OneMinusSrcAlpha; no depth write; queue 3000 (glass, water).</summary>
        Alpha,
    }

    /// <summary>What a toy sounds like when it lands (ART_BIBLE 11.3, "Land materials").</summary>
    public enum LandSound
    {
        Plastic,
        Wood,
        Rubber,
        Metal,
        Glass,
        Felt,
        Cardboard,
        Feather,
    }

    /// <summary>
    /// A material recipe for <c>Toybox/ToyLit</c>: every per-material property of ART_BIBLE 3.2, with the
    /// rows of 4.3 (toys) and 4.4 (non-grabbable props, gadgets, water) as ready-made instances. A recipe
    /// plus a colour is one shared material: <see cref="Materials.Toy"/>.
    ///
    /// Recipes are values. The ready-made ones must not be changed; derive a variant with
    /// <see cref="With"/>, which copies. Two recipes with the same numbers are equal and share materials.
    /// </summary>
    public sealed class ToyRecipe : IEquatable<ToyRecipe>
    {
        public string Name = "Custom";

        // ---- Base colour: how _BaseColor follows from the colour passed to Materials.Toy ----------------
        /// <summary>The colour the candy is mixed toward (in linear light) ...</summary>
        public Color BaseTint = Color.white;
        /// <summary>... and by how much: 0 is the pure candy colour, 1 the tint.</summary>
        public float BaseTintAmount;
        /// <summary>Multiplier on the result (rubber is candy x 0.92).</summary>
        public float BaseGain = 1f;

        // ---- Surface ------------------------------------------------------------------------------------
        public float Metallic;
        public float Smoothness = 0.6f;
        /// <summary>Diffuse wrap: saturate((N.L + w) / (1 + w)).</summary>
        public float Wrap = 0.2f;
        /// <summary>StudioEnv reflection gain, 0..2.</summary>
        public float Env = 0.5f;
        /// <summary>Clear-coat lobe, 0..1.</summary>
        public float Coat;
        /// <summary>Shifts the StudioEnv bands by detail.g (brushed metal), 0..0.3.</summary>
        public float Streak;
        /// <summary>Four-pane window glint: gain 0..2, edge softness, horizontal stretch.</summary>
        public float Glint = 1f, GlintSoft = 0.02f, GlintStretch = 1f;
        /// <summary>Kicker rim. 0 on anything that cannot be grabbed.</summary>
        public float Rim = 0.55f, RimPow = 3f;
        /// <summary>The rim colour is this ...</summary>
        public Color RimTint = Palette.Paper;
        /// <summary>... mixed toward the candy colour by this much (glass 1, rubber 0.4, felt 0.65).</summary>
        public float RimCandy;
        /// <summary>Emissive = albedo x SelfGlow x the global toy glow gain.</summary>
        public float SelfGlow = 0.06f;
        /// <summary>Back-lighting for thin sheets (paper 0.5, feather 0.8).</summary>
        public float Translucency;

        // ---- Detail texture -----------------------------------------------------------------------------
        public DetailTexture Detail = DetailTexture.Neutral;
        public float DetailAlbedo, DetailSmooth;
        /// <summary>Forced to 0 below the High tier by the material system.</summary>
        public float DetailBump;
        /// <summary>_DetailMap_ST: scale (xy) and offset (zw) on the object-space UVs.</summary>
        public Vector4 DetailST = new Vector4(1f, 1f, 0f, 0f);

        // ---- Transparency and render state ---------------------------------------------------------------
        public float AlphaFace = 1f, AlphaEdge = 1f, AlphaPow = 2.5f;
        /// <summary>50% checker discard in the shadow pass (glass casts a half-density shadow).</summary>
        public bool ShadowDither;
        public ToyBlend Blend = ToyBlend.Opaque;
        public CullMode Cull = CullMode.Back;
        /// <summary>Added to the blend mode's render queue (2000 opaque, 3000 alpha).</summary>
        public int QueueOffset;
        /// <summary>
        /// Medium and High add a second material on the same renderer: front faces culled, flat alpha
        /// <see cref="BackShellAlpha"/>, one queue step earlier. <see cref="Materials.ToySet"/> returns both.
        /// </summary>
        public bool BackShell;
        public float BackShellAlpha = 0.18f;

        // ---- What the rest of the game needs to know about the material ----------------------------------
        /// <summary>Candy + rim + pool means "you can lift this". False for props, gadgets and water.</summary>
        public bool Pool = true;
        /// <summary>Multiplier on the toy's colour in its colour pool (glass 1.6: it reads as a caustic).</summary>
        public float PoolTint = 1f;
        /// <summary>Squash on first impact, as a fraction (sponge 0.18, rubber 0.14, plastic 0.06).</summary>
        public float Squash;
        public LandSound Sound = LandSound.Plastic;

        string signature;

        /// <summary>A copy with some values changed: <c>ToyRecipe.GlossyPlastic.With(r => r.Smoothness = 0.7f)</c>.</summary>
        public ToyRecipe With(Action<ToyRecipe> change)
        {
            var copy = (ToyRecipe)MemberwiseClone();
            copy.signature = null;
            change?.Invoke(copy);
            copy.signature = null;
            return copy;
        }

        /// <summary>A copy under another name (names are part of what makes two recipes different).</summary>
        public ToyRecipe Named(string name) => With(r => r.Name = name);

        /// <summary>_BaseColor for a toy of this colour, linear.</summary>
        public Color BaseColor(Color candy)
        {
            Color linear = Color.LerpUnclamped(Palette.Lin(candy), Palette.Lin(BaseTint), BaseTintAmount) * BaseGain;
            linear.a = 1f;
            return linear;
        }

        /// <summary>_RimColor for a toy of this colour, linear.</summary>
        public Color RimColor(Color candy)
        {
            Color linear = Color.LerpUnclamped(Palette.Lin(RimTint), Palette.Lin(candy), RimCandy);
            linear.a = 1f;
            return linear;
        }

        /// <summary>The colour of the toy's pool on the floor, linear (ART_BIBLE 9.4).</summary>
        public Color PoolColor(Color candy) => BaseColor(candy) * PoolTint;

        public int RenderQueue => (Blend == ToyBlend.Alpha ? 3000 : 2000) + QueueOffset;

        // ---- 4.3 Toy recipes ------------------------------------------------------------------------------

        /// <summary>The default toy material. Its window glint is the brand mark.</summary>
        public static readonly ToyRecipe GlossyPlastic = new ToyRecipe
        {
            Name = "Glossy Plastic", Smoothness = 0.62f, Wrap = 0.20f, Env = 0.5f, Coat = 1.0f,
            Glint = 1.0f, GlintSoft = 0.02f, GlintStretch = 1f, Rim = 0.55f, RimPow = 3f, SelfGlow = 0.06f,
            Detail = DetailTexture.Peel, DetailBump = 0.03f, Squash = 0.06f, Sound = LandSound.Plastic,
        };

        /// <summary>The toy builder tints bevel-ring vertices 30% toward Birch with seeded noise: chipped edges.</summary>
        public static readonly ToyRecipe PaintedWood = new ToyRecipe
        {
            Name = "Painted Wood", Smoothness = 0.45f, Wrap = 0.25f, Env = 0.3f, Coat = 0.35f,
            Glint = 0.35f, GlintSoft = 0.25f, GlintStretch = 1f, Rim = 0.40f, RimPow = 3f, SelfGlow = 0.03f,
            Detail = DetailTexture.Brush, DetailAlbedo = 0.08f, DetailSmooth = 0.2f, DetailBump = 0.6f, Sound = LandSound.Wood,
        };

        public static readonly ToyRecipe Rubber = new ToyRecipe
        {
            Name = "Rubber", BaseGain = 0.92f, Smoothness = 0.18f, Wrap = 0.50f, Env = 0.1f, Coat = 0f,
            Glint = 0.12f, GlintSoft = 0.5f, GlintStretch = 1f, Rim = 0.65f, RimPow = 2.5f,
            RimTint = Color.white, RimCandy = 0.4f, SelfGlow = 0.04f,
            Detail = DetailTexture.Stipple, DetailSmooth = 0.3f, DetailBump = 0.15f, Squash = 0.14f, Sound = LandSound.Rubber,
        };

        /// <summary>Anodised candy, never bare silver. UVs run along the brush direction.</summary>
        public static readonly ToyRecipe BrushedMetal = new ToyRecipe
        {
            Name = "Brushed Metal", Metallic = 1f, Smoothness = 0.66f, Wrap = 0f, Env = 1.2f, Coat = 0.3f, Streak = 0.18f,
            Glint = 0.8f, GlintSoft = 0.15f, GlintStretch = 3f, Rim = 0.30f, RimPow = 3f, SelfGlow = 0f,
            Detail = DetailTexture.Streak, DetailAlbedo = 0.06f, DetailSmooth = 0.5f, Sound = LandSound.Metal,
        };

        public static readonly ToyRecipe Glass = new ToyRecipe
        {
            Name = "Glass", BaseTint = Color.white, BaseTintAmount = 0.65f, Smoothness = 0.96f, Wrap = 0f, Env = 1.6f, Coat = 1.0f,
            Glint = 1.4f, GlintSoft = 0.01f, GlintStretch = 1f, Rim = 0.8f, RimPow = 2.5f, RimCandy = 1f, SelfGlow = 0f,
            Blend = ToyBlend.Alpha, AlphaFace = 0.22f, AlphaEdge = 0.85f, AlphaPow = 2.5f, ShadowDither = true,
            BackShell = true, BackShellAlpha = 0.18f, PoolTint = 1.6f, Sound = LandSound.Glass,
        };

        /// <summary>Felt and fabric: the rim is the fuzz.</summary>
        public static readonly ToyRecipe Felt = new ToyRecipe
        {
            Name = "Felt", BaseTint = Palette.Grey, BaseTintAmount = 0.1f, Smoothness = 0f, Wrap = 0.60f, Env = 0f, Coat = 0f,
            Glint = 0f, Rim = 0.9f, RimPow = 2f, RimTint = Color.white, RimCandy = 0.65f, SelfGlow = 0.05f,
            Detail = DetailTexture.Fibre, DetailAlbedo = 0.16f, DetailBump = 1.0f, Sound = LandSound.Felt,
        };

        /// <summary>Pass Kraft as the colour. Every cardboard toy also carries a <see cref="TapeStrip"/> over at least 40%.</summary>
        public static readonly ToyRecipe Cardboard = new ToyRecipe
        {
            Name = "Cardboard", Smoothness = 0.08f, Wrap = 0.30f, Env = 0.05f, Coat = 0f,
            Glint = 0f, Rim = 0.35f, RimPow = 3f, SelfGlow = 0.02f,
            Detail = DetailTexture.Speckle, DetailAlbedo = 0.12f, DetailBump = 0.3f, Sound = LandSound.Cardboard,
        };

        /// <summary>A paper sheet: cardboard's numbers, two-sided and back-lit. Pass Paper (or a candy) as the colour.</summary>
        public static readonly ToyRecipe PaperSheet = Cardboard.With(r =>
        {
            r.Name = "Paper";
            r.Cull = CullMode.Off;
            r.Translucency = 0.5f;
        });

        /// <summary>The candy tape on a cardboard toy: plastic with smoothness 0.7.</summary>
        public static readonly ToyRecipe TapeStrip = GlossyPlastic.With(r =>
        {
            r.Name = "Tape";
            r.Smoothness = 0.7f;
        });

        /// <summary>The builder displaces a subdivided rounded box by 1.5% seeded noise for a lumpy silhouette.</summary>
        public static readonly ToyRecipe Sponge = new ToyRecipe
        {
            Name = "Sponge", BaseTint = Color.white, BaseTintAmount = 0.15f, Smoothness = 0f, Wrap = 0.60f, Env = 0f, Coat = 0f,
            Glint = 0f, Rim = 0.5f, RimPow = 2.5f, SelfGlow = 0.05f,
            Detail = DetailTexture.Pore, DetailAlbedo = 0.9f, DetailBump = 1.0f, Squash = 0.18f, Sound = LandSound.Felt,
        };

        /// <summary>Real geometry, no alpha test; two-sided and back-lit.</summary>
        public static readonly ToyRecipe Feather = new ToyRecipe
        {
            Name = "Feather", BaseTint = Palette.Paper, BaseTintAmount = 0.35f, Smoothness = 0.40f, Wrap = 0.50f, Env = 0.1f, Coat = 0f,
            Glint = 0.25f, GlintSoft = 0.2f, GlintStretch = 4f, Rim = 0.7f, RimPow = 2f, SelfGlow = 0.03f,
            Detail = DetailTexture.Barb, DetailAlbedo = 0.25f, DetailSmooth = 0.3f, DetailBump = 0.4f,
            Cull = CullMode.Off, Translucency = 0.8f, Sound = LandSound.Feather,
        };

        /// <summary>The nine toy recipes of ART_BIBLE 4.3, plus the paper sheet and the tape strip.</summary>
        public static readonly ToyRecipe[] Toys =
        {
            GlossyPlastic, PaintedWood, Rubber, BrushedMetal, Glass, Felt, Cardboard, Sponge, Feather, PaperSheet, TapeStrip,
        };

        // ---- 4.4 Things that are not toys -----------------------------------------------------------------

        /// <summary>A physics prop that cannot be grabbed: painted-wood numbers, no rim, a faint glint, no pool. Colour: Birch, Kraft, Steel or a dip tone.</summary>
        public static readonly ToyRecipe PlainProp = PaintedWood.With(r =>
        {
            r.Name = "Plain Prop";
            r.Rim = 0f;
            r.Glint = 0.3f;
            r.Pool = false;
        });

        /// <summary>Gadget body: Ink satin. Use with Palette.Ink.</summary>
        public static readonly ToyRecipe GadgetBody = new ToyRecipe
        {
            Name = "Gadget Body", Metallic = 0.1f, Smoothness = 0.5f, Env = 0.3f, Coat = 0f,
            Glint = 0.3f, GlintSoft = 0.2f, GlintStretch = 1f, Rim = 0f, SelfGlow = 0f, Pool = false, Sound = LandSound.Plastic,
        };

        /// <summary>Gadget signal element: an Ink base that only emits (the emission is passed to <see cref="Materials.Emissive"/>).</summary>
        public static readonly ToyRecipe GadgetSignal = GadgetBody.With(r =>
        {
            r.Name = "Gadget Signal";
            r.Glint = 0f;
        });

        /// <summary>Moving gadget metal: brushed-metal numbers without the rim. Use with Palette.Steel.</summary>
        public static readonly ToyRecipe GadgetMetal = BrushedMetal.With(r =>
        {
            r.Name = "Gadget Metal";
            r.Rim = 0f;
            r.Pool = false;
        });

        /// <summary>Water surface: glass numbers on the dip's deep tone, more opaque, no rim, no back shell.</summary>
        public static readonly ToyRecipe Water = Glass.With(r =>
        {
            r.Name = "Water";
            r.BaseTintAmount = 0f;
            r.AlphaFace = 0.45f;
            r.AlphaEdge = 0.9f;
            r.Rim = 0f;
            r.BackShell = false;
            r.ShadowDither = false;
            r.Pool = false;
            r.PoolTint = 1f;
        });

        /// <summary>Things that glow without being a gadget signal (fairy lights, the night-light): matte, no glint.</summary>
        public static readonly ToyRecipe Lamp = new ToyRecipe
        {
            Name = "Lamp", Smoothness = 0.3f, Wrap = 0.4f, Env = 0.1f, Coat = 0f, Glint = 0f, Rim = 0f, SelfGlow = 0f, Pool = false,
        };

        // ---- Equality ---------------------------------------------------------------------------------------

        /// <summary>Every value in one string: what two recipes must agree on to share a material.</summary>
        public string Signature
        {
            get
            {
                if (signature != null) return signature;
                var s = new StringBuilder(256);
                s.Append(Name).Append('|');
                Put(s, BaseTint); Put(s, BaseTintAmount); Put(s, BaseGain);
                Put(s, Metallic); Put(s, Smoothness); Put(s, Wrap); Put(s, Env); Put(s, Coat); Put(s, Streak);
                Put(s, Glint); Put(s, GlintSoft); Put(s, GlintStretch);
                Put(s, Rim); Put(s, RimPow); Put(s, RimTint); Put(s, RimCandy); Put(s, SelfGlow); Put(s, Translucency);
                s.Append((int)Detail).Append('|');
                Put(s, DetailAlbedo); Put(s, DetailSmooth); Put(s, DetailBump);
                Put(s, DetailST.x); Put(s, DetailST.y); Put(s, DetailST.z); Put(s, DetailST.w);
                Put(s, AlphaFace); Put(s, AlphaEdge); Put(s, AlphaPow);
                s.Append(ShadowDither ? 1 : 0).Append('|').Append((int)Blend).Append('|').Append((int)Cull).Append('|').Append(QueueOffset).Append('|');
                s.Append(BackShell ? 1 : 0).Append('|');
                Put(s, BackShellAlpha);
                s.Append(Pool ? 1 : 0).Append('|');
                Put(s, PoolTint); Put(s, Squash);
                s.Append((int)Sound);
                return signature = s.ToString();
            }
        }

        static void Put(StringBuilder s, float value) => s.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('|');

        static void Put(StringBuilder s, Color value)
        {
            Put(s, value.r);
            Put(s, value.g);
            Put(s, value.b);
            Put(s, value.a);
        }

        public bool Equals(ToyRecipe other) => other != null && (ReferenceEquals(this, other) || Signature == other.Signature);
        public override bool Equals(object obj) => Equals(obj as ToyRecipe);
        public override int GetHashCode() => Signature.GetHashCode();
        public override string ToString() => Name;
    }
}
