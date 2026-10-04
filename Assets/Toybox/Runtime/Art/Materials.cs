using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Art
{
    /// <summary>The parts a gadget is made of (ART_BIBLE 2.5 rule 5, 4.4).</summary>
    public enum GadgetPart
    {
        /// <summary>Ink satin: the body of every gadget.</summary>
        Body,
        /// <summary>Steel: moving metal.</summary>
        Metal,
    }

    /// <summary>
    /// The one place materials come from. Every call returns a material that is shared by everybody who
    /// asks with the same parameters, cached for the session: do not modify or destroy what this returns
    /// (per-object changes go through a MaterialPropertyBlock on the renderer). Colours passed in are sRGB
    /// as authored (Palette values) unless a parameter says "linear".
    ///
    /// The signatures are the contract; this is how they are kept:
    ///
    /// - A material is a clone of a template under Resources/Materials - ToyLit.mat for toys, props and
    ///   gadgets, RoomLit.mat for static surfaces, Flat.mat for unlit things - with every per-material
    ///   property of ART_BIBLE 3.2 / 3.3 / 3.5 set by name from the recipe. So a shader takes effect the
    ///   moment its owner's setup step points the template at it; nothing here changes.
    /// - Colours are pushed as linear vectors (SetVector). The game's shaders must declare their colour
    ///   properties as Vector, not Color: Unity converts a Color property from sRGB whichever setter is
    ///   used (measured), which would darken every colour a second time.
    /// - A template that is missing, or whose shader is not one of the game's ("Toybox/..."), gets the
    ///   stand-in treatment: URP Lit with the recipe's base colour, smoothness, metallic and emission -
    ///   enough for everything to render in the right colours before the shaders exist. The plain look
    ///   (<see cref="Plain"/>) always does that, from PlainLit.mat.
    /// </summary>
    public static class Materials
    {
        const string ToyTemplatePath = "Materials/ToyLit";
        const string RoomTemplatePath = "Materials/RoomLit";
        const string FlatTemplatePath = "Materials/Flat";
        const string PlainTemplatePath = "Materials/PlainLit";
        const string OwnShaders = "Toybox/";

        // Toybox/ToyLit (ART_BIBLE 3.2)
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        static readonly int WrapId = Shader.PropertyToID("_Wrap");
        static readonly int EnvId = Shader.PropertyToID("_Env");
        static readonly int CoatId = Shader.PropertyToID("_Coat");
        static readonly int StreakId = Shader.PropertyToID("_Streak");
        static readonly int GlintId = Shader.PropertyToID("_Glint");
        static readonly int GlintSoftId = Shader.PropertyToID("_GlintSoft");
        static readonly int GlintStretchId = Shader.PropertyToID("_GlintStretch");
        static readonly int RimId = Shader.PropertyToID("_Rim");
        static readonly int RimPowId = Shader.PropertyToID("_RimPow");
        static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        static readonly int SelfGlowId = Shader.PropertyToID("_SelfGlow");
        static readonly int TranslucencyId = Shader.PropertyToID("_Translucency");
        static readonly int DetailMapId = Shader.PropertyToID("_DetailMap");
        static readonly int DetailAlbedoId = Shader.PropertyToID("_DetailAlbedo");
        static readonly int DetailSmoothId = Shader.PropertyToID("_DetailSmooth");
        static readonly int DetailBumpId = Shader.PropertyToID("_DetailBump");
        static readonly int AlphaFaceId = Shader.PropertyToID("_AlphaFace");
        static readonly int AlphaEdgeId = Shader.PropertyToID("_AlphaEdge");
        static readonly int AlphaPowId = Shader.PropertyToID("_AlphaPow");
        static readonly int ShadowDitherId = Shader.PropertyToID("_ShadowDither");
        // Render state, shared by ToyLit, RoomLit and Flat
        static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        static readonly int CullId = Shader.PropertyToID("_Cull");
        // Toybox/RoomLit (3.3)
        static readonly int ColorTopId = Shader.PropertyToID("_ColorTop");
        static readonly int ColorSideId = Shader.PropertyToID("_ColorSide");
        static readonly int ColorDadoId = Shader.PropertyToID("_ColorDado");
        static readonly int DadoYId = Shader.PropertyToID("_DadoY");
        static readonly int PatternId = Shader.PropertyToID("_Pattern");
        static readonly int PatternAId = Shader.PropertyToID("_PatternA");
        static readonly int CornerId = Shader.PropertyToID("_Corner");
        // Toybox/Flat (3.5)
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int ShapeId = Shader.PropertyToID("_Shape");
        static readonly int SoftId = Shader.PropertyToID("_Soft");
        // The stand-in (URP Lit)
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        /// <summary><c>_Emission</c>, the per-material and per-renderer emission property of the toy shader.</summary>
        public static readonly int EmissionId = Shader.PropertyToID("_Emission");

        // What stands in for a room surface or an unlit thing while only the toy shader exists: matte, no
        // rim, no glint, no glow of its own.
        static readonly ToyRecipe Matte = new ToyRecipe
        {
            Name = "Matte Stand-in", Smoothness = 0.05f, Wrap = 0.3f, Env = 0f, Coat = 0f, Glint = 0f, Rim = 0f, SelfGlow = 0f, Pool = false,
        };

        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        // The toy materials made so far, for the one property that follows the quality tier.
        static readonly List<KeyValuePair<Material, ToyRecipe>> ToyMaterials = new List<KeyValuePair<Material, ToyRecipe>>();
        static readonly Dictionary<string, Material> Templates = new Dictionary<string, Material>();
        static readonly Dictionary<string, Material> Overrides = new Dictionary<string, Material>();
        static Material fallbackTemplate;
        static MaterialPropertyBlock emissionBlock;
        static QualityTier tier = QualityTier.Medium;
        static Dip dip;

        /// <summary>
        /// The debug look (?plain=1, -toyboxPlain): materials are plain URP Lit whatever shaders the game
        /// has. Set before a level is built; materials made earlier keep what they were made with.
        /// </summary>
        public static bool Plain { get; set; }

        /// <summary>
        /// The quality tier materials are made for (detail bump only on High, the glass back shell not on
        /// Low). Set through PresentationContext.Quality when the tier changes; existing materials follow.
        /// </summary>
        public static QualityTier Tier
        {
            get => tier;
            set
            {
                if (tier == value) return;
                tier = value;
                // Tier differences are deliberately tiny (ART_BIBLE 4.3): the bump is on only on High, the
                // detail maps are half size on Low, and the orange peel exists only where the bump does.
                ToyMaterials.RemoveAll(entry => entry.Key == null);
                foreach (KeyValuePair<Material, ToyRecipe> entry in ToyMaterials) entry.Key.SetFloat(DetailBumpId, DetailBump(entry.Value));
                RepointDetailMaps();
                TexCache.Trim(tier);
            }
        }

        /// <summary>
        /// Gives every toy material the detail texture of the tier in force again: after the tier changed,
        /// or after the texture cache was emptied. No material is ever left pointing at a destroyed texture.
        /// </summary>
        internal static void RepointDetailMaps()
        {
            foreach (KeyValuePair<Material, ToyRecipe> entry in ToyMaterials)
                if (entry.Key != null) entry.Key.SetTexture(DetailMapId, TexCache.For(entry.Value, tier));
        }

        /// <summary>
        /// The dip of the level that is being built or played: what <see cref="Room(RoomSurface, Dip, PatternSpec, float)"/>
        /// and <see cref="Water"/> use when they are not given one. Game sets it before a level's Build runs.
        /// </summary>
        public static Dip Dip
        {
            get => dip ?? Palette.Mint;
            set => dip = value;
        }

        /// <summary>How many different materials exist (the budget is 40 alive per level, ART_BIBLE 12.1).</summary>
        public static int Count => Cache.Count;

        /// <summary>
        /// For tests and tools: uses this material as the template called "ToyLit", "RoomLit", "Flat" or
        /// "PlainLit" instead of the one under Resources/Materials (null: the one under Resources again).
        /// Materials made earlier are not remade.
        /// </summary>
        public static void OverrideTemplate(string name, Material template)
        {
            string path = "Materials/" + name;
            if (template != null) Overrides[path] = template;
            else Overrides.Remove(path);
        }

        // ---- Toys, props and gadgets: Toybox/ToyLit -------------------------------------------------------

        /// <summary>The material of a toy made of this recipe in this colour (a candy colour for anything grabbable).</summary>
        public static Material Toy(ToyRecipe recipe, Color candy)
        {
            recipe ??= ToyRecipe.GlossyPlastic;
            string key = "T|" + recipe.Signature + "|" + Palette.ToHex(candy);
            return Cached(key, () => MakeToy("Toy " + recipe.Name + " " + Palette.ToHex(candy), recipe, candy, Color.black));
        }

        /// <summary>
        /// The materials to put on a toy's renderer, in order: the recipe's own, and before it the back shell
        /// for recipes that have one (glass) on Medium and High. Assign the array to renderer.sharedMaterials.
        /// </summary>
        public static Material[] ToySet(ToyRecipe recipe, Color candy)
        {
            recipe ??= ToyRecipe.GlossyPlastic;
            Material front = Toy(recipe, candy);
            if (!recipe.BackShell || Tier == QualityTier.Low || Plain) return new[] { front };
            ToyRecipe shell = recipe.With(r =>
            {
                r.Name = recipe.Name + " Back";
                r.Cull = CullMode.Front;
                r.AlphaFace = recipe.BackShellAlpha;
                r.AlphaEdge = recipe.BackShellAlpha;
                r.QueueOffset = recipe.QueueOffset - 1;
                r.BackShell = false;
                r.Rim = 0f;
                r.Glint = 0f;
            });
            return new[] { Toy(shell, candy), front };
        }

        /// <summary>
        /// A toy-shader material that also emits: <paramref name="emission"/> is linear and HDR
        /// (<c>Palette.Lin(colour) * gain</c>). For fairy lights and other glowing things that are not gadget
        /// signals; signals have <see cref="Gadget(Signal)"/>.
        /// </summary>
        public static Material Emissive(ToyRecipe recipe, Color color, Color emission)
        {
            recipe ??= ToyRecipe.Lamp;
            string key = "E|" + recipe.Signature + "|" + Palette.ToHex(color) + "|" + Number(emission.r) + "|" + Number(emission.g) + "|" + Number(emission.b);
            return Cached(key, () => MakeToy("Emissive " + recipe.Name + " " + Palette.ToHex(color), recipe, color, emission));
        }

        /// <summary>Gadget body (Ink satin) or moving metal (Steel).</summary>
        public static Material Gadget(GadgetPart part) =>
            part == GadgetPart.Metal ? Toy(ToyRecipe.GadgetMetal, Palette.Steel) : Toy(ToyRecipe.GadgetBody, Palette.Ink);

        /// <summary>A gadget's signal element at the signal's full gain: an Ink base that emits the signal colour.</summary>
        public static Material Gadget(Signal signal) => Gadget(signal, signal.Gain);

        /// <summary>
        /// A signal element at a gain of the caller's choosing (0 is off). To animate a pulse do not ask for
        /// a new material every tick: set <c>_Emission</c> on the renderer through <see cref="SetEmission"/>.
        /// </summary>
        public static Material Gadget(Signal signal, float gain) =>
            Emissive(ToyRecipe.GadgetSignal, Palette.Ink, signal.EmissionAt(gain));

        /// <summary>A water surface: glass numbers on the dip's deep tone (the current level's dip if none is given).</summary>
        public static Material Water(Dip waterDip = null) => Toy(ToyRecipe.Water, (waterDip ?? Dip).Deep);

        /// <summary>
        /// Overrides the emission of one renderer (linear, HDR) without touching the shared material -
        /// how a signal pulses or switches between gains.
        /// </summary>
        public static void SetEmission(Renderer renderer, Color emission)
        {
            if (renderer == null) return;
            // One block for every call: a pulsing signal comes through here every frame. Whatever else the
            // renderer's block holds (a sweep, a squash) is read first and kept.
            emissionBlock ??= new MaterialPropertyBlock();
            renderer.GetPropertyBlock(emissionBlock);
            emissionBlock.SetVector(EmissionId, new Vector4(emission.r, emission.g, emission.b, 1f));
            renderer.SetPropertyBlock(emissionBlock);
        }

        // ---- Static surfaces: Toybox/RoomLit ---------------------------------------------------------------

        public static Material Room(RoomRecipe recipe)
        {
            recipe ??= RoomRecipe.For(RoomSurface.LevelStatic, Dip);
            return Cached("R|" + recipe.Signature, () => MakeRoom(recipe));
        }

        /// <summary>
        /// The material of a static surface in a dip (ART_BIBLE 4.4): tone pair by <paramref name="surface"/>,
        /// an optional pattern, and the play plane for the wall's dado line. Without a dip it is the dip of
        /// the level being built.
        /// </summary>
        public static Material Room(RoomSurface surface, Dip roomDip = null, PatternSpec pattern = default, float groundY = 0f) =>
            Room(RoomRecipe.For(surface, roomDip ?? Dip, pattern, groundY));

        // ---- Unlit: Toybox/Flat ---------------------------------------------------------------------------

        public static Material Flat(FlatRecipe recipe)
        {
            recipe ??= new FlatRecipe();
            return Cached("F|" + recipe.Signature, () => MakeFlat(recipe));
        }

        /// <summary>An unlit material: <paramref name="color"/> is linear and may be HDR.</summary>
        public static Material Flat(Color color, FlatShape shape = FlatShape.Quad, FlatBlend blend = FlatBlend.Opaque, float soft = 0f) =>
            Flat(new FlatRecipe { Color = color, Shape = shape, Blend = blend, Soft = soft });

        // ---------------------------------------------------------------------------------------------------
        // Internals
        // ---------------------------------------------------------------------------------------------------

        static Material Cached(string key, System.Func<Material> make)
        {
            if (Plain) key = "P|" + key;
            // A cached entry can be a destroyed object if something cleared all assets; make it again then.
            if (Cache.TryGetValue(key, out Material cached) && cached != null) return cached;
            Material material = make();
            Cache[key] = material;
            return material;
        }

        static Material MakeToy(string name, ToyRecipe recipe, Color candy, Color emission)
        {
            Material material = CloneToy(name, out bool own);
            if (material == null) return null;
            if (own) ApplyToy(material, recipe, recipe.BaseColor(candy), recipe.RimColor(candy), emission);
            else StandIn(material, recipe.BaseColor(candy), recipe.Smoothness, recipe.Metallic, emission);
            return material;
        }

        static Material MakeRoom(RoomRecipe recipe)
        {
            string name = "Room " + recipe.Name;
            if (CloneOwn(RoomTemplatePath, name, out Material material))
            {
                material.SetVector(ColorTopId, Linear(recipe.Top));
                material.SetVector(ColorSideId, Linear(recipe.Side));
                material.SetVector(ColorDadoId, Linear(recipe.Dado));
                material.SetFloat(DadoYId, recipe.DadoY);
                material.SetFloat(PatternId, (float)recipe.Pattern.Pattern);
                material.SetVector(PatternAId, recipe.Pattern.A);
                material.SetFloat(CornerId, recipe.Corner ? 1f : 0f);
                material.SetFloat(CullId, (float)recipe.Cull);
                return material;
            }

            // No room shader (yet, or the plain look). One colour is all a stand-in has: halfway between the
            // two tones of the top-light rule.
            Color middle = Color.LerpUnclamped(Palette.Lin(recipe.Side), Palette.Lin(recipe.Top), 0.5f);
            middle.a = 1f;
            material = CloneToy(name, out bool own);
            if (material == null) return null;
            if (own) ApplyToy(material, Matte, middle, Color.black, Color.black);
            else StandIn(material, middle, 0.05f, 0f, Color.black);
            return material;
        }

        static Material MakeFlat(FlatRecipe recipe)
        {
            string name = "Flat " + recipe.Name;
            if (CloneOwn(FlatTemplatePath, name, out Material material))
            {
                Color color = recipe.Color;
                material.SetVector(ColorId, new Vector4(color.r, color.g, color.b, color.a));
                material.SetFloat(ShapeId, (float)recipe.Shape);
                material.SetFloat(SoftId, recipe.Soft);
                material.SetFloat(SrcBlendId, (float)recipe.SrcBlend);
                material.SetFloat(DstBlendId, (float)recipe.DstBlend);
                material.SetFloat(ZWriteId, recipe.WritesDepth ? 1f : 0f);
                material.SetFloat(ZTestId, (float)recipe.ZTest);
                material.SetFloat(CullId, (float)recipe.Cull);
                material.renderQueue = recipe.RenderQueue;
                return material;
            }

            // No unlit shader (yet, or the plain look): something that glows in the recipe's colour. Blending
            // is beyond a stand-in.
            Color emission = recipe.Color;
            float peak = Mathf.Max(1f, emission.maxColorComponent);
            var body = new Color(emission.r / peak, emission.g / peak, emission.b / peak, 1f);
            material = CloneToy(name, out bool own);
            if (material == null) return null;
            if (own) ApplyToy(material, Matte, Color.black, Color.black, emission);
            else StandIn(material, body, 0f, 0f, emission);
            return material;
        }

        // Every per-material property of ART_BIBLE 3.2, from the recipe. Colours are linear.
        static void ApplyToy(Material material, ToyRecipe recipe, Color baseColor, Color rimColor, Color emission)
        {
            material.SetVector(BaseColorId, new Vector4(baseColor.r, baseColor.g, baseColor.b, 1f));
            material.SetFloat(MetallicId, recipe.Metallic);
            material.SetFloat(SmoothnessId, recipe.Smoothness);
            material.SetFloat(WrapId, recipe.Wrap);
            material.SetFloat(EnvId, recipe.Env);
            material.SetFloat(CoatId, recipe.Coat);
            material.SetFloat(StreakId, recipe.Streak);
            material.SetFloat(GlintId, recipe.Glint);
            material.SetFloat(GlintSoftId, recipe.GlintSoft);
            material.SetFloat(GlintStretchId, recipe.GlintStretch);
            material.SetFloat(RimId, recipe.Rim);
            material.SetFloat(RimPowId, recipe.RimPow);
            material.SetVector(RimColorId, new Vector4(rimColor.r, rimColor.g, rimColor.b, 1f));
            material.SetFloat(SelfGlowId, recipe.SelfGlow);
            material.SetVector(EmissionId, new Vector4(emission.r, emission.g, emission.b, 1f));
            material.SetFloat(TranslucencyId, recipe.Translucency);

            // One packed detail texture per recipe (4.2), made on first use. Headless it is plain grey,
            // which is exactly neutral.
            material.SetTexture(DetailMapId, TexCache.For(recipe, tier));
            material.SetTextureScale(DetailMapId, new Vector2(recipe.DetailST.x, recipe.DetailST.y));
            material.SetTextureOffset(DetailMapId, new Vector2(recipe.DetailST.z, recipe.DetailST.w));
            material.SetFloat(DetailAlbedoId, recipe.DetailAlbedo);
            material.SetFloat(DetailSmoothId, recipe.DetailSmooth);
            material.SetFloat(DetailBumpId, DetailBump(recipe));

            material.SetFloat(AlphaFaceId, recipe.AlphaFace);
            material.SetFloat(AlphaEdgeId, recipe.AlphaEdge);
            material.SetFloat(AlphaPowId, recipe.AlphaPow);
            material.SetFloat(ShadowDitherId, recipe.ShadowDither ? 1f : 0f);
            bool blended = recipe.Blend == ToyBlend.Alpha;
            material.SetFloat(SrcBlendId, (float)(blended ? BlendMode.SrcAlpha : BlendMode.One));
            material.SetFloat(DstBlendId, (float)(blended ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            material.SetFloat(ZWriteId, blended ? 0f : 1f);
            material.SetFloat(CullId, (float)recipe.Cull);
            material.renderQueue = recipe.RenderQueue;
            ToyMaterials.Add(new KeyValuePair<Material, ToyRecipe>(material, recipe));
        }

        // Tier differences are deliberately tiny: the bump is the only number that changes.
        static float DetailBump(ToyRecipe recipe) => tier == QualityTier.High ? recipe.DetailBump : 0f;

        // URP Lit declares its colours as Color properties, which Unity converts from sRGB to linear
        // whichever setter is used. So a linear value goes in as its sRGB encoding.
        static void StandIn(Material material, Color linearBase, float smoothness, float metallic, Color emission)
        {
            SetConverted(material, BaseColorId, linearBase);
            material.SetFloat(SmoothnessId, smoothness);
            material.SetFloat(MetallicId, metallic);
            if (emission.maxColorComponent <= 0f) return;
            // URP Lit only emits with its keyword on; fine in the editor, and the game's shader has no keywords.
            material.EnableKeyword("_EMISSION");
            SetConverted(material, EmissionColorId, emission);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }

        static void SetConverted(Material material, int property, Color linear)
        {
            Color encoded = Palette.Srgb(linear);
            encoded.a = 1f;
            material.SetColor(property, encoded);
        }

        static Vector4 Linear(Color srgb)
        {
            Color linear = Palette.Lin(srgb);
            return new Vector4(linear.r, linear.g, linear.b, 1f);
        }

        // A clone of the toy template - or of the plain template while the plain look is on. "own" says
        // whether its shader is one of the game's.
        static Material CloneToy(string name, out bool own)
        {
            Material template = Template(Plain ? PlainTemplatePath : ToyTemplatePath);
            if (template == null) template = Fallback();
            own = false;
            if (template == null) return null;
            own = !Plain && IsOwn(template);
            return new Material(template) { name = name, hideFlags = HideFlags.DontSave };
        }

        // A clone of the template at the path, but only if it exists, carries one of the game's shaders and
        // the plain look is off.
        static bool CloneOwn(string path, string name, out Material material)
        {
            material = null;
            if (Plain) return false;
            Material template = Template(path);
            if (template == null || !IsOwn(template)) return false;
            material = new Material(template) { name = name, hideFlags = HideFlags.DontSave };
            return true;
        }

        static bool IsOwn(Material template) =>
            template.shader != null && template.shader.name.StartsWith(OwnShaders, System.StringComparison.Ordinal);

        static Material Template(string path)
        {
            if (Overrides.TryGetValue(path, out Material replaced) && replaced != null) return replaced;
            if (Templates.TryGetValue(path, out Material template) && template != null) return template;
            template = Resources.Load<Material>(path);
            // A missing template is looked for again next time: its owner's setup step may not have run yet.
            if (template != null) Templates[path] = template;
            return template;
        }

        static Material Fallback()
        {
            if (fallbackTemplate != null) return fallbackTemplate;
            // Only reachable if ProjectSetup has not run; in a player build the shader would be missing too.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;
            fallbackTemplate = new Material(shader) { hideFlags = HideFlags.DontSave };
            return fallbackTemplate;
        }

        static string Number(float value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }
}
