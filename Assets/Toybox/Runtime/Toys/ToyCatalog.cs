using System;
using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Toys
{
    /// <summary>Every toy of the catalog. The order is the order the campaign meets them in.</summary>
    public enum ToyId
    {
        WoodenBlock,
        CheeseWedge,
        Thimble,
        Apple,
        Domino,
        Feather,
        Eraser,
        Pebble,
        Marble,
        Plank,
        GiftBox,
        PlayingCard,
        Key,
        ThreadSpool,
        Sponge,
        Doorway,
        BouncyBall,
        DeskFan,
        CatapultRuler,
        DominoSet,
        Baseball,
        Marker,
        TrainEngine,
        TrainCar,
    }

    /// <summary>
    /// One toy of the catalog (LEVELS.md section 3): what it is called, how to build it, what it is made
    /// of, and the physical properties a level hands to AddProp. Everything is at scale 1.
    /// </summary>
    public sealed class ToyDef
    {
        readonly Func<Color?, GameObject> build;
        readonly float catalogVolume, catalogDensity;
        bool measured;
        QualityTier measuredTier;
        float volume, radius;
        Vector3 center, halfExtents;
        int triangles, draws;

        public ToyId Id { get; }
        public string Name { get; }
        /// <summary>Lower case with dashes: "cheese-wedge".</summary>
        public string Slug { get; }
        /// <summary>The authored size, X x Y x Z: the box around the colliders.</summary>
        public Vector3 Size { get; }
        /// <summary>The recipe of the toy's main material (ART_BIBLE 4.3); for the silver thimble, of its candy band.</summary>
        public ToyRecipe Recipe { get; }
        /// <summary>
        /// The toy's own colour: a candy colour for a toy (all over it; the silver thimble wears it as a
        /// band), Birch for a set piece. sRGB.
        /// </summary>
        public Color Color { get; }
        public float Friction { get; }
        public float Bounciness { get; }
        /// <summary>The scale clamps of the catalog. A level may narrow them (they are often the puzzle).</summary>
        public float MinScale { get; }
        public float MaxScale { get; }
        public GrabPose GrabPose { get; }
        public bool KeepUpright { get; }
        public bool AllowPitch { get; }
        /// <summary>False for set pieces: the baseball, the marker, the train.</summary>
        public bool Grabbable { get; }
        public PropBody Body { get; }
        /// <summary>The levels that use the toy.</summary>
        public IReadOnlyList<int> Levels { get; }
        /// <summary>
        /// How to turn the toy so that a viewer looking along +Z sees it at its most recognisable: the pose of
        /// its silhouette (<see cref="ToySilhouette"/>) and a good start for a turntable.
        /// </summary>
        public Quaternion Portrait { get; }

        internal ToyDef(ToyId id, string name, Vector3 size, ToyRecipe recipe, Color color, Func<Color?, GameObject> build,
            float volume, float density, float friction, float bounciness, float minScale, float maxScale, GrabPose grabPose,
            int[] levels, Quaternion portrait, bool keepUpright = false, bool allowPitch = true, bool grabbable = true, PropBody body = PropBody.Dynamic)
        {
            Id = id;
            Name = name;
            Slug = name.ToLowerInvariant().Replace(' ', '-');
            Size = size;
            Recipe = recipe;
            Color = color;
            this.build = build;
            catalogVolume = volume;
            catalogDensity = density;
            Friction = friction;
            Bounciness = bounciness;
            MinScale = minScale;
            MaxScale = maxScale;
            GrabPose = grabPose;
            Levels = levels ?? new int[0];
            Portrait = portrait;
            KeepUpright = keepUpright;
            AllowPitch = allowPitch;
            Grabbable = grabbable;
            Body = body;
        }

        /// <summary>
        /// A new toy: a GameObject with renderers and colliders at scale 1, no Rigidbody. Without a colour
        /// it takes its own; pass a candy colour (a set piece: Birch, Kraft, Steel or a dip tone).
        /// </summary>
        public GameObject Build(Color? color = null) => build(color);

        /// <summary>
        /// Mass at scale 1: the catalog's density times the catalog's volume. It goes with scale cubed and
        /// is never less than Prop.MinMass.
        /// </summary>
        public float Mass => catalogDensity * (catalogVolume > 0f ? catalogVolume : Volume);

        /// <summary>
        /// The density handed to PropOptions: <see cref="Mass"/> over the volume of the colliders, so the
        /// prop weighs what the catalog says. It is the catalog's own density wherever the colliders are
        /// exact, and a little more where a hull is smaller than the ideal shape (a 16-sided thimble).
        /// </summary>
        public float Density => Mass / Volume;

        /// <summary>Volume of the colliders, as the simulation measures it.</summary>
        public float Volume
        {
            get
            {
                Measure();
                return volume;
            }
        }

        /// <summary>Middle of the colliders' bounds in the toy's own space: what perspective distances are measured to.</summary>
        public Vector3 Center
        {
            get
            {
                Measure();
                return center;
            }
        }

        /// <summary>Half size of the box around the colliders.</summary>
        public Vector3 HalfExtents
        {
            get
            {
                Measure();
                return halfExtents;
            }
        }

        /// <summary>Bounding radius about <see cref="Center"/>: Prop.BaseRadius of a prop made from this toy.</summary>
        public float Radius
        {
            get
            {
                Measure();
                return radius;
            }
        }

        /// <summary>
        /// How far the toy's origin is above a floor it stands on in its authored pose (0 for the toys whose
        /// origin is on the floor: the doorway, the fan). Times the scale.
        /// </summary>
        public float RestHeight => HalfExtents.y - Center.y;

        /// <summary>Triangles and draw calls of the toy's visuals (the budget is 3,000 and 3).</summary>
        public int Triangles
        {
            get
            {
                Measure();
                return triangles;
            }
        }

        public int Draws
        {
            get
            {
                Measure();
                return draws;
            }
        }

        /// <summary>
        /// The colour the toy takes in a room: its own, unless the room's dip bans it (it would be too close
        /// to the walls) - then the dip's hero colour (ART_BIBLE 2.5 rule 3).
        /// </summary>
        public Color ColorIn(Dip dip) => !Grabbable || dip == null || dip.Allows(Color) ? Color : dip.Hero;

        /// <summary>
        /// The options of the catalog for AddProp, a fresh instance every time: name, density, friction,
        /// bounciness, clamps, grab pose and body kind. Change what the level needs (tags, FrozenUntilGrabbed,
        /// narrower clamps) on the result.
        /// </summary>
        public PropOptions Options(float scale = 1f) => new PropOptions
        {
            Name = Name,
            Scale = scale,
            Density = Density,
            Friction = Friction,
            Bounciness = Bounciness,
            MinScale = MinScale,
            MaxScale = MaxScale,
            GrabPose = GrabPose,
            KeepUpright = KeepUpright,
            AllowPitch = AllowPitch,
            Body = Body,
            Grabbable = Grabbable ? (bool?)null : false,
        };

        // Builds one toy that never becomes active - its colliders never enter a physics scene - measures
        // it the way Prop does, and throws it away.
        void Measure()
        {
            // The draw count depends on the tier (the glass back shell is a second material on Medium and
            // High): a figure measured while the game ran on Low must not be handed out on Medium. Play
            // Mode's statics outlive Play Mode in the editor, so this happens (a play check, then tests).
            if (measured && measuredTier == Materials.Tier) return;
            bool previous = ToyKit.Dormant;
            ToyKit.Dormant = true;
            GameObject toy = null;
            try
            {
                toy = build(null);
                PropGeometry.Measure(toy.transform, toy.GetComponentsInChildren<Collider>(true), out volume, out center, out halfExtents, out radius);
                triangles = 0;
                draws = 0;
                foreach (MeshFilter filter in toy.GetComponentsInChildren<MeshFilter>(true))
                    triangles += MeshUtil.TriangleCount(filter.sharedMesh);
                foreach (Renderer renderer in toy.GetComponentsInChildren<Renderer>(true))
                    draws += renderer.sharedMaterials.Length;
                measured = true;
                measuredTier = Materials.Tier;
            }
            finally
            {
                ToyKit.Dormant = previous;
                if (toy != null) Sim.Destroy(toy);
            }
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// The toy catalog: every toy by id, for levels (build one and add it with the right options), for the
    /// UI (name, colour, silhouette) and for the showroom.
    ///
    ///   Prop thimble = ToyCatalog.Add(ctx, ToyId.Thimble, new Vector3(0f, 1.42f, -4.5f), 0.8f);
    ///   // or, in two steps:
    ///   Prop apple = ctx.AddProp(ToyFactory.Apple(), position, ToyCatalog.Options(ToyId.Apple, 11.4f));
    /// </summary>
    public static class ToyCatalog
    {
        static readonly ToyDef[] Defs = Create();
        static readonly Dictionary<int, ToyId> Heroes = new Dictionary<int, ToyId>
        {
            { 1, ToyId.CheeseWedge }, { 2, ToyId.Thimble }, { 3, ToyId.Apple }, { 4, ToyId.Domino }, { 5, ToyId.Feather },
            { 6, ToyId.Eraser }, { 7, ToyId.Pebble }, { 8, ToyId.Marble }, { 9, ToyId.Plank }, { 10, ToyId.GiftBox },
            { 11, ToyId.PlayingCard }, { 12, ToyId.Key }, { 13, ToyId.Sponge }, { 14, ToyId.Doorway }, { 15, ToyId.DominoSet },
        };

        /// <summary>Every toy, in the order of <see cref="ToyId"/>.</summary>
        public static IReadOnlyList<ToyDef> All => Defs;

        public static ToyDef Get(ToyId id)
        {
            int index = (int)id;
            if (index < 0 || index >= Defs.Length) throw new ArgumentOutOfRangeException(nameof(id), "There is no toy " + id + ".");
            return Defs[index];
        }

        /// <summary>The toy with this slug ("cheese-wedge") or name, or null.</summary>
        public static ToyDef Find(string slug)
        {
            if (string.IsNullOrEmpty(slug)) return null;
            foreach (ToyDef def in Defs)
                if (string.Equals(def.Slug, slug, StringComparison.OrdinalIgnoreCase) || string.Equals(def.Name, slug, StringComparison.OrdinalIgnoreCase))
                    return def;
            return null;
        }

        /// <summary>The key toy of a campaign level (1..15) - the one on its card in the level select - or null.</summary>
        public static ToyDef HeroOf(int levelId) => Heroes.TryGetValue(levelId, out ToyId id) ? Get(id) : null;

        public static GameObject Build(ToyId id, Color? color = null) => Get(id).Build(color);

        public static PropOptions Options(ToyId id, float scale = 1f) => Get(id).Options(scale);

        /// <summary>
        /// Builds a toy and adds it to the level as a prop with the catalog's options. The position is the
        /// toy's origin (the centre of its colliders unless its factory says otherwise). Without a colour the
        /// toy takes its own, or the room's hero colour where the room bans its own. <paramref name="options"/>
        /// adjusts the options before the prop is made: tags, FrozenUntilGrabbed, narrower clamps.
        /// </summary>
        public static Prop Add(LevelContext ctx, ToyId id, Vector3 position, Quaternion rotation, float scale = 1f, Color? color = null, Action<PropOptions> options = null)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            ToyDef def = Get(id);
            PropOptions propOptions = def.Options(scale);
            options?.Invoke(propOptions);
            return ctx.AddProp(def.Build(color ?? def.ColorIn(ctx.Dip)), position, rotation, propOptions);
        }

        public static Prop Add(LevelContext ctx, ToyId id, Vector3 position, float scale = 1f, Color? color = null, Action<PropOptions> options = null) =>
            Add(ctx, id, position, Quaternion.identity, scale, color, options);

        // ---- The table (LEVELS.md section 3) ----------------------------------------------------------------

        static Quaternion View(float pitch, float yaw) => Quaternion.Euler(pitch, 0f, 0f) * Quaternion.Euler(0f, yaw, 0f);

        // Seen from above, turned in the picture by the roll.
        static Quaternion Above(float roll) => Quaternion.Euler(0f, 0f, roll) * Quaternion.Euler(-90f, 0f, 0f);

        static ToyDef[] Create()
        {
            const float ball = 4f / 3f * Mathf.PI * 0.125f;
            var defs = new[]
            {
                new ToyDef(ToyId.WoodenBlock, "Wooden Block", Vector3.one, ToyRecipe.PaintedWood, Palette.Cherry, c => ToyFactory.WoodenBlock(1f, c),
                    1f, 0.6f, 0.7f, 0f, 0.2f, 10f, GrabPose.Snap90, new[] { 4, 11 }, View(-25f, 30f)),
                new ToyDef(ToyId.CheeseWedge, "Cheese Wedge", ToyFactory.CheeseWedgeSize, ToyRecipe.GlossyPlastic, Palette.Lemon, ToyFactory.CheeseWedge,
                    0.20f, 0.3f, 0.9f, 0f, 0.2f, 14f, GrabPose.Upright, new[] { 1 }, View(0f, 90f)),
                // Silver, by the owner's brief ("a tiny silver thimble"): its body is ToyFactory.Silver in
                // Palette.Silver whatever the room. Recipe and colour here are those of its candy - the
                // anodised band round its rim, its pool, its tag - so a room that bans Tangerine recolours
                // the band and nothing else.
                new ToyDef(ToyId.Thimble, "Thimble", new Vector3(1f, 0.8f, 1f), ToyRecipe.BrushedMetal, Palette.Tangerine, ToyFactory.Thimble,
                    0.616f, 0.2f, 0.6f, 0f, 0.3f, 13f, GrabPose.Upright, new[] { 2 }, View(-18f, 0f)),
                new ToyDef(ToyId.Apple, "Apple", Vector3.one, ToyRecipe.GlossyPlastic, Palette.Cherry, ToyFactory.Apple,
                    ball, 4f, 0.6f, 0.1f, 0.15f, 12f, GrabPose.Keep, new[] { 3 }, View(0f, 0f)),
                new ToyDef(ToyId.Domino, "Domino", ToyFactory.DominoSize, ToyRecipe.PaintedWood, Palette.Grape, c => ToyFactory.Domino(c),
                    0.60f, 0.8f, 0.7f, 0f, 0.3f, 6f, GrabPose.Upright, new[] { 4 }, View(0f, 0f)),
                new ToyDef(ToyId.Feather, "Feather", ToyFactory.FeatherSize, ToyRecipe.Feather, Palette.Cherry, ToyFactory.Feather,
                    0.0036f, 1.5f, 0.6f, 0f, 0.5f, 12f, GrabPose.Upright, new[] { 5 }, Above(-35f)),
                new ToyDef(ToyId.Eraser, "Eraser", ToyFactory.EraserSize, ToyRecipe.Rubber, Palette.Bubblegum, ToyFactory.Eraser,
                    0.15f, 1.2f, 0.9f, 0.3f, 0.4f, 10.5f, GrabPose.Snap90, new[] { 6 }, View(-22f, 25f)),
                new ToyDef(ToyId.Pebble, "Pebble", Vector3.one, ToyFactory.Stone, Palette.Lemon, ToyFactory.Pebble,
                    ball, 3f, 0.8f, 0f, 0.2f, 7f, GrabPose.Keep, new[] { 7 }, View(0f, 0f)),
                new ToyDef(ToyId.Marble, "Marble", Vector3.one, ToyRecipe.Glass, Palette.Cherry, ToyFactory.Marble,
                    ball, 2.5f, 0.2f, 0.2f, 0.3f, 6f, GrabPose.Keep, new[] { 8 }, View(0f, 0f)),
                new ToyDef(ToyId.Plank, "Plank", ToyFactory.PlankSize, ToyRecipe.PaintedWood, Palette.Lemon, c => ToyFactory.Plank(c),
                    0.216f, 0.3f, 0.8f, 0f, 0.3f, 4.4f, GrabPose.Snap90, new[] { 9 }, View(-30f, 60f)),
                new ToyDef(ToyId.GiftBox, "Gift Box", Vector3.one, ToyRecipe.Cardboard, Palette.Cherry, ToyFactory.GiftBox,
                    1f, 5.44f, 0.9f, 0f, 0.3f, 2f, GrabPose.Upright, new[] { 10 }, View(-20f, 30f)),
                new ToyDef(ToyId.PlayingCard, "Playing Card", ToyFactory.PlayingCardSize, ToyRecipe.Cardboard, Palette.Tangerine, ToyFactory.PlayingCard,
                    0.112f, 10f, 0.8f, 0f, 0.2f, 7f, GrabPose.Snap90, new[] { 11 }, Above(-12f)),
                new ToyDef(ToyId.Key, "Key", ToyFactory.KeySize, ToyRecipe.BrushedMetal, Palette.Lemon, ToyFactory.Key,
                    0.028f, 2f, 0.6f, 0f, 0.05f, 8f, GrabPose.Snap90, new[] { 12 }, Above(-40f)),
                new ToyDef(ToyId.ThreadSpool, "Thread Spool", new Vector3(ToyFactory.ThreadSpoolRadius * 2f, ToyFactory.ThreadSpoolHeight, ToyFactory.ThreadSpoolRadius * 2f),
                    ToyRecipe.PaintedWood, Palette.Tangerine, c => ToyFactory.ThreadSpool(color: c),
                    0.950f, 0.4f, 0.8f, 0f, 0.5f, 9.9f, GrabPose.Upright, new[] { 12 }, View(-20f, 0f), keepUpright: true),
                new ToyDef(ToyId.Sponge, "Sponge", ToyFactory.SpongeSize, ToyRecipe.Sponge, Palette.Lagoon, ToyFactory.Sponge,
                    0.35f, 0.15f, 0.9f, 0f, 0.5f, 12f, GrabPose.Snap90, new[] { 13 }, View(-25f, 30f)),
                new ToyDef(ToyId.Doorway, "Doorway", ToyFactory.DoorwaySize, ToyRecipe.PaintedWood, Palette.Lime, ToyFactory.Doorway,
                    0f, 1f, 0.6f, 0f, 0.1f, 3.2f, GrabPose.Upright, new[] { 14 }, View(0f, 0f), allowPitch: false, body: PropBody.Fixed),
                new ToyDef(ToyId.BouncyBall, "Bouncy Ball", Vector3.one, ToyRecipe.Rubber, Palette.Cherry, ToyFactory.BouncyBall,
                    ball, 4f, 0.6f, 0.5f, 0.6f, 3f, GrabPose.Keep, new[] { 15 }, View(0f, 0f)),
                new ToyDef(ToyId.DeskFan, "Desk Fan", ToyFactory.DeskFanSize, ToyRecipe.GlossyPlastic, Palette.Lagoon, c => ToyFactory.DeskFan(c),
                    0.353f, 0.3f, 0.6f, 0f, 0.5f, 5f, GrabPose.Upright, new[] { 5, 15 }, View(-5f, 160f)),
                new ToyDef(ToyId.CatapultRuler, "Catapult Ruler", new Vector3(0.6f, 0.28f, 4f), ToyRecipe.PaintedWood, Palette.Lemon, ToyFactory.CatapultRuler,
                    0.25f, 0.6f, 0.6f, 0f, 0.6f, 1.2f, GrabPose.Snap90, new[] { 7, 15 }, View(-35f, 55f)),
                new ToyDef(ToyId.DominoSet, "Domino Set", ToyFactory.DominoSetSize, ToyRecipe.PaintedWood, Palette.Lime, ToyFactory.DominoSet,
                    0.544f, 0.8f, 0.7f, 0f, 0.5f, 3.5f, GrabPose.Upright, new[] { 15 }, View(-12f, 15f)),
                new ToyDef(ToyId.Baseball, "Baseball", Vector3.one * (ToyFactory.BaseballRadius * 2f), ToyRecipe.PlainProp, Palette.Birch, ToyFactory.Baseball,
                    4f / 3f * Mathf.PI * 0.216f, 10f, 0.6f, 0.2f, 0.02f, 80f, GrabPose.Keep, new[] { 15 }, View(0f, 0f), grabbable: false),
                new ToyDef(ToyId.Marker, "Marker", new Vector3(0.8f, 0.8f, 2f), ToyRecipe.PlainProp, Palette.Birch, c => ToyFactory.Marker(color: c),
                    0f, 1f, 0.8f, 0f, 0.02f, 80f, GrabPose.Keep, new[] { 7, 15 }, View(-10f, 80f), grabbable: false, body: PropBody.Fixed),
                new ToyDef(ToyId.TrainEngine, "Train Engine", new Vector3(ToyFactory.TrainWidth, ToyFactory.TrainDeckThickness + ToyFactory.TrainFunnelHeight, ToyFactory.TrainEngineLength),
                    ToyRecipe.PlainProp, Palette.Birch, ToyFactory.TrainEngine,
                    0f, 1f, 0.8f, 0f, 0.02f, 80f, GrabPose.Keep, new[] { 9 }, View(-8f, 75f), grabbable: false, body: PropBody.Kinematic),
                new ToyDef(ToyId.TrainCar, "Train Car", new Vector3(ToyFactory.TrainWidth, ToyFactory.TrainDeckThickness, ToyFactory.TrainCarLength),
                    ToyRecipe.PlainProp, Palette.Birch, ToyFactory.TrainCar,
                    0f, 1f, 0.8f, 0f, 0.02f, 80f, GrabPose.Keep, new[] { 9 }, View(-8f, 75f), grabbable: false, body: PropBody.Kinematic),
            };
            for (int i = 0; i < defs.Length; i++)
                if ((int)defs[i].Id != i) throw new InvalidOperationException("The toy table is out of order at " + defs[i].Id + ".");
            return defs;
        }
    }
}
