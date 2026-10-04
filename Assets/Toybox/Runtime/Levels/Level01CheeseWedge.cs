using System.Collections;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Levels
{
    /// <summary>
    /// Level 1 (LEVELS.md): inside an open toy chest, a canyon of rug lies between two stacks of picture
    /// books. A crumb of plastic cheese sits on a spool at the start. Held up against the far stack and
    /// let go it is a hillside nine units long, and its slope is the way up.
    ///
    /// The one idea: far away means big. The far stack's face is the forgiving target - the wedge stops
    /// against it at the same size whatever the pitch - and a dashed outline of the wedge's tall end is
    /// painted on it (the campaign's size language: the outline is on the surface the toy stops against).
    ///
    /// What the level does about the ways a first try goes quietly wrong (all measured, see
    /// tools/out/notes/level01-review.md): the player starts one step from the cheese, so the first click
    /// is a close one; a pick-up that looks too small is told so at the grab; a crumb too small to use goes
    /// back to its spool; a hillside let go the wrong way round is told so. Short of the far stack, on top
    /// of it, side-on or stood on end, the cheese is always in view from the start stack or the rug and is
    /// simply taken again.
    /// </summary>
    [Level(1, "cheese-wedge", "The Cheese Wedge", Phase = 1)]
    public sealed class Level01CheeseWedge : LevelDefinition
    {
        // ---- Layout (LEVELS.md, Level 1; travel is +Z, the rug is y = 0) ---------------------------------
        public const float HalfWidth = 7f, NearZ = -12f, FarZ = 46f;
        /// <summary>The start stack ends at <see cref="GapNear"/>, the goal stack's sheer face is at <see cref="GapFar"/>.</summary>
        public const float GapNear = 0f, GapFar = 30f;
        public const float StackTop = 4f, WallHeight = 12f;
        public const float StartScale = 0.4f;
        /// <summary>The scale the intended drop gives, and what the outline is painted for.</summary>
        public const float IntendedScale = 9.4f;

        const float WallThickness = 1f;
        const float SunDiscRadius = 2f;
        const float SpoolRadius = 0.35f, SpoolHeight = 1f;
        const float BookLean = 26.565f, BookThickness = 0.5f, BookMinX = -7f, BookMaxX = -4f, BookRun = 8f;

        static readonly Vector3 SpoolAt = new Vector3(0f, StackTop, -3f);

        /// <summary>
        /// Where the player starts and where the solver takes the cheese from: one unit in front of the
        /// spool. LEVELS.md starts the player six units back; a click from there takes the cheese looking a
        /// sixth as big, and the hillside comes out 2 long instead of 9 (measured). The campaign's pedestal
        /// rule (0.3: "the toy sits within one step of where the player stands, so the default grab is a
        /// close one") is applied here instead, so that the first click anybody makes is the one that works.
        /// </summary>
        public static readonly Vector3 GrabSpot = new Vector3(0f, StackTop, -4f);
        /// <summary>
        /// The view at the start: the crosshair on the cheese, so that the grab prompt is the first thing the
        /// game says, with the far stack, its outline and the exit above it in the same picture.
        /// </summary>
        public const float SpawnPitch = -20f;
        /// <summary>Where it lets go: 0.6 from the edge of the start stack.</summary>
        public static readonly Vector3 EdgeSpot = new Vector3(2f, StackTop, -0.6f);
        /// <summary>What it aims at: the middle of the wedge once it is seated against the far stack.</summary>
        public static readonly Vector3 Aim = new Vector3(0f, 2.4f, 25.3f);
        public static readonly Vector3 ExitCentre = new Vector3(0f, StackTop + 1f, 42f);
        public static readonly Vector3 ExitSize = new Vector3(4f, 2f, 4f);

        /// <summary>
        /// Apparent size (scale / distance) below which a hold is not sure to make the hillside. Measured from
        /// the edge of the start stack: below 0.197 the far stack turns it into less than 5.5, and the top of
        /// that is out of a jump's reach; at 0.23 it is 6.3 and the hop has 0.4 to spare. At its starting size
        /// that is a pick-up from more than 1.7 away.
        /// </summary>
        public const float LooksTooSmall = 0.23f;
        /// <summary>
        /// A cheese lying anywhere but on its spool at less than this goes back to the spool at its starting
        /// size. Lying on a floor it cannot be taken from nearer than the eye is high, 1.55: below 0.36 it
        /// looks too small from anywhere, and "pick it up from closer" would be advice nobody can follow; up
        /// to 0.45 (which takes in the crumb itself, knocked off its spool) only a pick-up from right above it
        /// is close enough. On the spool it is at eye height, and a step away is close.
        /// </summary>
        public const float CrumbScale = 0.45f;
        /// <summary>From this size up a wedge is a hillside, and which way its slope faces matters.</summary>
        public const float HillScale = 3f;
        /// <summary>Said the first time the cheese is taken at a size that can work.</summary>
        public const string HoldLine = "It lands on whatever is behind it. Look around.";
        /// <summary>Said whenever it is taken looking too small.</summary>
        public const string SmallLine = "It only ever gets as big as it looks. Pick it up from closer.";
        /// <summary>Said when a crumb smaller than the level's own has gone back to the spool.</summary>
        public const string CrumbLine = "That crumb was too small to use. It is back on its spool.";
        /// <summary>Said when the cheese went back to the spool at the size it started with (knocked off, or put down at the feet).</summary>
        public const string SpoolLine = "The cheese is back on its spool.";
        /// <summary>Said when a hillside is let go in the canyon with its tall end toward the start stack.</summary>
        public const string BackwardLine = "Its tall end is facing the wrong way. Pick it up again and turn it round.";
        /// <summary>
        /// Said when a hillside has come to rest with its tall end more than <see cref="ShortGap"/> short of
        /// the far stack: the crosshair was on the lowest part of the stack's face or on the rug in front of
        /// it, and the wedge's base met the rug first. The most likely first miss, and the one a look at the
        /// hillside does not explain.
        /// </summary>
        public const string ShortLine = "It fell short of the far books. Pick it up again and hold it over the dashed outline.";
        /// <summary>
        /// How far from the far stack's face a hillside's crest may stop without a word: up to here a hop
        /// from the crest reaches the stack (measured: 1.5 short is hopped, 3.5 short takes a sprint jump).
        /// </summary>
        public const float ShortGap = 2f;

        bool taught;
        /// <summary>The cheese has been judged since it was last let go (or has not been let go yet).</summary>
        bool judged;
        int restTicks;

        /// <summary>The cheese.</summary>
        public Prop Wedge { get; private set; }

        public override string Blurb => "Things are as big as they look. Pick up the cheese.";

        public override string[] Hints => new[]
        {
            "Hold the cheese and look around. It lands on whatever is behind it.",
            "Far away means bigger. Look at the tall books across the rug.",
            // LEVELS.md says "aim the cheese at the bottom of the far book stack"; in the simulation the lowest
            // third of that face is the one place on it that leaves the wedge short (its base meets the rug
            // first). The dashed outline is the wedge's tall end where it works: with the cheese held over it
            // the crosshair is on the middle of the face. The leaning book is the way down.
            "Stand at the edge, cover the dashed outline on the far books with the cheese, and let go. Then walk down the leaning book and climb the cheese.",
        };

        public override string Environment => "sunny-rug";
        public override float GroundY => 0f;
        public override float KillY => -30f;

        public override void Build(LevelContext ctx)
        {
            taught = false;
            judged = true;
            restTicks = 0;
            Wedge = null;

            BuildChest(ctx);
            BuildBooks(ctx);
            BuildClutter(ctx);

            // The crumb of cheese on top of its spool, thin end toward the spawn.
            ToyDef cheese = ToyCatalog.Get(ToyId.CheeseWedge);
            Wedge = ToyCatalog.Add(ctx, ToyId.CheeseWedge, SpoolAt + Vector3.up * (SpoolHeight + cheese.RestHeight * StartScale), StartScale, null, o =>
            {
                // 11.2 wide at most in a chest 14 wide; too short at any size to bridge the rug.
                o.MinScale = 0.2f;
                o.MaxScale = 14f;
                o.GrabPose = GrabPose.Upright;
            });

            ctx.AddExit(ExitCentre, ExitSize);
            ctx.SetSpawn(GrabSpot, 0f, SpawnPitch);

            // Two teaching beats on a grab. What a hold does, the first time the cheese is taken. And the one
            // way a try goes quietly wrong: taken from far off, the cheese looks small, and against the far
            // stack it comes out a couple of units long instead of nine. The same is true of any wedge taken
            // back from across the rug.
            ctx.Game.Events.PropGrabbed += e =>
            {
                if (e.Prop != Wedge) return;
                if (e.OldScale < LooksTooSmall * e.GrabDistance)
                {
                    ctx.Say(SmallLine, 6f);
                    return;
                }
                if (taught) return;
                taught = true;
                ctx.Say(HoldLine, 5f);
            };

            // "Closer" has to be possible. A crumb lying on a floor looks too small from anywhere a player can
            // stand, so it goes back to the spool, where it is at eye height and a step away is close.
            Zone everywhere = Zone.MinMax(new Vector3(-HalfWidth - 4f, -4f, NearZ - 4f), new Vector3(HalfWidth + 4f, WallHeight + 4f, FarZ + 4f));
            Vector3 home = Wedge.Center;
            float returned = StartScale;
            var crumbs = new PropLeash(ctx, new PropLeashOptions
            {
                Name = "Crumb", Props = new[] { Wedge }, Forbidden = new[] { everywhere },
                Unless = () => Wedge.Scale >= CrumbScale || (Wedge.Center - home).sqrMagnitude < 0.3f * 0.3f,
                Grace = 1.5f, Action = LeashAction.Respawn, OnReturn = prop => returned = prop.Scale,
            });
            crumbs.PropReturned += prop => ctx.Say(returned < StartScale - 0.01f ? CrumbLine : SpoolLine, 5f);

            // The hold keeps the cheese turned the way it was when it was taken: picked up from behind the
            // spool it lands in the canyon as a cliff, its slope running down toward the far stack. Said when
            // that has happened (a hillside lying side-on is climbed along its crest, and gets no line).
            ctx.Game.Events.PropDropped += e =>
            {
                if (e.Prop != Wedge) return;
                judged = false;
                restTicks = 0;
                if (Wedge.Held || Wedge.Scale < HillScale || Wedge.Center.z < GapNear) return;
                Vector3 up = Wedge.Rotation * Vector3.up, rise = Wedge.Rotation * Vector3.forward;
                rise.y = 0f;
                if (up.y > 0.7f && rise.normalized.z < -0.5f) ctx.Say(BackwardLine, 6f);
            };

            // The other way a good hold goes wrong: aimed low, the hillside lies on the rug the right way
            // round but short of the far stack. Judged once per drop, when it has come to rest.
            ctx.OnUpdate(dt =>
            {
                if (judged || Wedge.Held || Wedge.Removed)
                {
                    restTicks = 0;
                    return;
                }
                restTicks = AtRest(Wedge) ? restTicks + 1 : 0;
                if (restTicks < 20) return;
                judged = true;
                if (FellShort()) ctx.Say(ShortLine, 6f);
            });
        }

        // A hillside on the rug, slope toward the far stack, whose crest stops too far from the stack's face
        // for a hop. (Tall end first it has had its own line; side-on it is climbed along its crest; on top of
        // the far stack or back on the start stack it is not a try at the canyon at all.)
        bool FellShort()
        {
            if (Wedge.Scale < HillScale || Wedge.Center.z < GapNear || Wedge.Center.y > StackTop) return false;
            Vector3 up = Wedge.Rotation * Vector3.up, rise = Wedge.Rotation * Vector3.forward;
            rise.y = 0f;
            if (up.y < 0.7f || rise.normalized.z < 0.5f) return false;
            Vector3 half = ToyFactory.CheeseWedgeSize * 0.5f;
            Vector3 crest = Wedge.Transform.TransformPoint(new Vector3(0f, half.y, half.z));
            return GapFar - crest.z > ShortGap;
        }

        // ---- Geometry ---------------------------------------------------------------------------------

        static GameObject Solid(string name, Vector3 size)
        {
            var solid = new GameObject(name) { layer = Layers.Default };
            solid.AddComponent<BoxCollider>().size = size;
            return solid;
        }

        // The chest: four walls twelve high round the whole level, the room's rug for a floor, and a lid
        // of nothing at the height of the walls.
        void BuildChest(LevelContext ctx)
        {
            float length = FarZ - NearZ, midZ = (NearZ + FarZ) * 0.5f, t = WallThickness;
            // The rug floor is the room's own (the play plane is y = 0); the collider makes the level stand
            // on its own feet.
            ctx.AddStatic(Solid("Rug Floor", new Vector3(2f * (HalfWidth + t), 1f, length + 2f * t)), new Vector3(0f, -0.5f, midZ));

            // The sun stands 45 degrees up, to the left and ahead. The two walls on that side would lay their
            // shadow over three quarters of the rug and half of both stacks, and the hillside's own shadow -
            // the one cue to true size there is (ART_BIBLE 5: cast shadows are a gameplay cue) - would be
            // lost in it. Those walls are drawn without casting a shadow; the other two throw theirs outside
            // the chest.
            Vector3 sun = SunDirection();
            var set = new Level01Set();
            var sunSide = new Level01Set();
            Material wood = Materials.Room(RoomRecipe.For(RoomSurface.LevelStatic, ctx.Dip, new PatternSpec(RoomPattern.Planks, 2f, 40f, 0.1f)));
            var sideSize = new Vector3(t, WallHeight, length + 2f * t);
            var endSize = new Vector3(2f * HalfWidth, WallHeight, t);
            Vector3[] centres =
            {
                new Vector3(-HalfWidth - t * 0.5f, WallHeight * 0.5f, midZ), new Vector3(HalfWidth + t * 0.5f, WallHeight * 0.5f, midZ),
                new Vector3(0f, WallHeight * 0.5f, NearZ - t * 0.5f), new Vector3(0f, WallHeight * 0.5f, FarZ + t * 0.5f),
            };
            Vector3[] outward = { Vector3.left, Vector3.right, Vector3.back, Vector3.forward };
            for (int i = 0; i < centres.Length; i++)
            {
                Vector3 size = i < 2 ? sideSize : endSize;
                ctx.AddStatic(Solid("Chest Wall", size), centres[i]);
                (Vector3.Dot(sun, outward[i]) > 0f ? sunSide : set).Box(wood, centres[i], size);
            }

            // A painted band round the inside of the rim, and a sun rising behind the goal stack: the exit
            // mark stands in front of it from wherever the player looks across. The disc is no taller than
            // the highest point a hold can be aimed at from the edge of the start stack and still stop
            // against the far stack's face (above that the cheese goes over the edge and lands on top).
            Material paper = Materials.Room(RoomSurface.Trim, ctx.Dip);
            Material light = Paint(ctx.Dip);
            const float band = 0.5f, proud = 0.02f;
            float bandY = WallHeight - band * 0.5f - 0.35f;
            sunSide.Box(paper, new Vector3(-HalfWidth + proud, bandY, midZ), new Vector3(proud * 2f, band, length));
            sunSide.Box(paper, new Vector3(HalfWidth - proud, bandY, midZ), new Vector3(proud * 2f, band, length));
            sunSide.Box(paper, new Vector3(0f, bandY, NearZ + proud), new Vector3(2f * HalfWidth, band, proud * 2f));
            sunSide.Box(paper, new Vector3(0f, bandY, FarZ - proud), new Vector3(2f * HalfWidth, band, proud * 2f));
            sunSide.Disc(light, new Vector3(0f, StackTop + 0.6f, FarZ - 0.03f), SunDiscRadius, 0.04f, Quaternion.Euler(90f, 0f, 0f));
            set.Finish(ctx, "Chest");
            sunSide.Finish(ctx, "Chest Sun Side", castShadows: false);

            SkyCap.Add(ctx, -HalfWidth - t, HalfWidth + t, NearZ - t, FarZ + t, WallHeight);
        }

        /// <summary>Toward the sun of the room this level stands in (the direction the environment solver will give it).</summary>
        Vector3 SunDirection()
        {
            EnvironmentPreset preset = EnvironmentPreset.Find(Environment) ?? EnvironmentPreset.SunnyRug;
            EnvironmentSolver.Sun(preset, EnvironmentVisit, out float elevation, out float azimuth);
            return EnvironmentSolver.SunDirection(elevation, azimuth);
        }

        // The dip's light tone on every face: what is painted on walls and spines.
        static Material Paint(Dip dip) =>
            Materials.Room(new RoomRecipe { Name = dip.Name + " Paint", Top = dip.Light, Side = dip.Light, Dado = dip.Light });

        // The two stacks of picture books and the book that leans from the start stack down to the rug.
        void BuildBooks(LevelContext ctx)
        {
            Dip dip = ctx.Dip;
            Material cover = Materials.Room(RoomSurface.LevelStatic, dip);
            Material pages = Materials.Room(RoomSurface.Trim, dip);
            Material paint = Paint(dip);
            Material striped = Materials.Room(RoomRecipe.For(RoomSurface.LevelStatic, dip, new PatternSpec(RoomPattern.Stripes, 2f, 0f, 0f, 0.05f)));
            Material dotted = Materials.Room(RoomRecipe.For(RoomSurface.LevelStatic, dip, new PatternSpec(RoomPattern.Dots, 2f, 0.45f, 0f, 0.05f)));
            var set = new Level01Set();

            // Start stack: three books, pages toward the rug on the first and the last.
            float nearDepth = GapNear - NearZ, nearZ = (NearZ + GapNear) * 0.5f;
            ctx.AddStatic(Solid("Book Stack A", new Vector3(2f * HalfWidth, StackTop, nearDepth)), new Vector3(0f, StackTop * 0.5f, nearZ));
            Stack(set, cover, pages, striped, nearZ, nearDepth, new[] { 1.5f, 1.1f, 1.4f },
                new[] { Level01Set.Side.NegZ, Level01Set.Side.PosZ, Level01Set.Side.NegZ }, new[] { 0.9f, 1f, 0.95f });

            // Goal stack: three books with their spines toward the rug, so its sheer face is one plain
            // surface for the outline.
            float farDepth = FarZ - GapFar, farZ = (GapFar + FarZ) * 0.5f;
            ctx.AddStatic(Solid("Book Stack B", new Vector3(2f * HalfWidth, StackTop, farDepth)), new Vector3(0f, StackTop * 0.5f, farZ));
            Stack(set, cover, pages, dotted, farZ, farDepth, new[] { 1.2f, 1.6f, 1.2f },
                new[] { Level01Set.Side.NegZ, Level01Set.Side.NegZ, Level01Set.Side.NegZ }, new[] { 0.95f, 1f, 0.9f });

            // The leaning book: its upper face runs from the edge of the start stack (y 4, z 0) down to the
            // rug (y 0, z 8); the lower end goes on a little way into the rug so the face ends on it.
            Quaternion lean = Quaternion.Euler(BookLean, 0f, 0f);
            float faceLength = Mathf.Sqrt(BookRun * BookRun + StackTop * StackTop) + 0.6f;
            Vector3 topEdge = new Vector3((BookMinX + BookMaxX) * 0.5f, StackTop, GapNear);
            Vector3 centre = topEdge + lean * new Vector3(0f, -BookThickness * 0.5f, faceLength * 0.5f);
            var size = new Vector3(BookMaxX - BookMinX, BookThickness, faceLength);
            GameObject leaning = Solid("Leaning Book", size);
            ctx.AddStatic(leaning, centre, lean);
            set.Book(cover, pages, centre, size, Level01Set.Side.NegX, lean);

            // The size language: the wedge's tall end at the intended size, dashed, on the face it stops against.
            float half = IntendedScale * ToyFactory.CheeseWedgeSize.x * 0.5f;
            Vector3 face = Vector3.back;
            const float foot = 0.12f, head = StackTop - 0.1f;
            set.Dashes(pages, new Vector3(-half, foot, GapFar), new Vector3(-half, head, GapFar), face, 0.14f, 0.5f, 0.32f);
            set.Dashes(pages, new Vector3(half, foot, GapFar), new Vector3(half, head, GapFar), face, 0.14f, 0.5f, 0.32f);
            set.Dashes(pages, new Vector3(-half, foot, GapFar), new Vector3(half, foot, GapFar), face, 0.14f, 0.5f, 0.32f);

            // Title labels on the spines, clear of the outline.
            const float label = 0.03f;
            set.Box(paint, new Vector3(5.3f, 0.6f, GapFar - label * 0.5f), new Vector3(1.6f, 0.5f, label));
            set.Box(paint, new Vector3(-5.3f, 2f, GapFar - label * 0.5f), new Vector3(1.9f, 0.7f, label));
            set.Box(paint, new Vector3(5.1f, 3.4f, GapFar - label * 0.5f), new Vector3(2.2f, 0.5f, label));
            set.Box(paint, new Vector3(2.6f, 2.05f, GapNear + label * 0.5f), new Vector3(2.6f, 0.5f, label));

            set.Finish(ctx, "Books");
        }

        // Nobody's toys, left about in the corners: Birch blocks and dominoes (plain props - no candy, no
        // rim, no pool). They stand clear of the spawn's view of the cheese and of the wedge's way across.
        static void BuildClutter(LevelContext ctx)
        {
            var set = new Level01Set();

            // The pedestal: a spool standing on the start stack.
            Piece(ctx, set, ToyFactory.ThreadSpool(SpoolRadius, SpoolHeight, grabbable: false), SpoolAt + Vector3.up * (SpoolHeight * 0.5f), Quaternion.identity);

            // Behind the spawn, on the start stack.
            Block(ctx, set, new Vector3(1.5f, 1.5f, 1.5f), new Vector3(-5.6f, StackTop, -10.5f), 8f);
            Block(ctx, set, new Vector3(1f, 1f, 1f), new Vector3(-5.5f, StackTop + 1.5f, -10.4f), 31f);
            Block(ctx, set, new Vector3(1.6f, 0.8f, 0.8f), new Vector3(-3.6f, StackTop, -11f), -14f);
            Piece(ctx, set, ToyFactory.Domino(grabbable: false), new Vector3(5.5f, StackTop + 1f, -10.9f), Quaternion.Euler(0f, -24f, 0f));
            Piece(ctx, set, ToyFactory.Domino(grabbable: false), new Vector3(4.1f, StackTop + 0.15f, -9.9f), Quaternion.Euler(90f, 62f, 0f));
            // At the right-hand end of the start stack's edge.
            Block(ctx, set, new Vector3(1.2f, 1.2f, 1.2f), new Vector3(5.9f, StackTop, -1.4f), -12f);
            // On the rug at the foot of the start stack, across from the leaning book.
            Block(ctx, set, new Vector3(1.2f, 1.2f, 1.2f), new Vector3(5.6f, 0f, 2.1f), 18f);
            Block(ctx, set, new Vector3(0.8f, 0.8f, 1.6f), new Vector3(3.9f, 0f, 4.3f), -37f);
            // On the goal stack, behind the exit. The player cannot get up there without the cheese, so these
            // stand square in the corner and flat against the chest: nothing lies out of sight behind them
            // (a crumb thrown up there has to be seen from the start stack to be taken back).
            Block(ctx, set, new Vector3(1.4f, 1.4f, 1.4f), new Vector3(-HalfWidth + 0.7f, StackTop, FarZ - 0.7f), 0f);
            Block(ctx, set, new Vector3(0.9f, 0.9f, 0.9f), new Vector3(-HalfWidth + 1.4f + 0.45f, StackTop, FarZ - 0.45f), 0f);
            Piece(ctx, set, ToyFactory.Domino(grabbable: false), new Vector3(5.2f, StackTop + 1f, FarZ - 0.15f), Quaternion.identity);

            // A dozen pieces of two materials: two draws.
            set.Finish(ctx, "Clutter");
        }

        static void Block(LevelContext ctx, Level01Set set, Vector3 size, Vector3 foot, float yaw) =>
            Piece(ctx, set, ToyFactory.WoodenBlock(size, grabbable: false), foot + Vector3.up * (size.y * 0.5f), Quaternion.Euler(0f, yaw, 0f));

        // A set piece: its colliders stand in the level, its looks join the set.
        static void Piece(LevelContext ctx, Level01Set set, GameObject piece, Vector3 centre, Quaternion rotation)
        {
            ctx.AddStatic(piece, centre, rotation);
            set.Absorb(piece);
        }

        // A stack of books lying flat, filling the chest from wall to wall, bottom book first.
        static void Stack(Level01Set set, Material cover, Material pages, Material topCover, float z, float depth, float[] heights, Level01Set.Side[] spines, float[] tones)
        {
            float y = 0f;
            for (int i = 0; i < heights.Length; i++)
            {
                var centre = new Vector3(0f, y + heights[i] * 0.5f, z);
                var size = new Vector3(2f * HalfWidth, heights[i], depth);
                set.Book(cover, pages, centre, size, spines[i], Quaternion.identity, tones[i], i == heights.Length - 1 ? topCover : null);
                y += heights[i];
            }
        }

        // ---- The solution ---------------------------------------------------------------------------------

        public override IEnumerator Solve(Bot bot)
        {
            // The cheese is under the player's nose at the start: no walk, a look down and a click.
            yield return Take(bot, GrabSpot);
            // Round the spool, not through it.
            yield return bot.WalkTo(new Vector3(1.3f, StackTop, -3.2f), 0.3f);
            // Hold it up against the far stack for a beat - the crumb covers the outline - then let go, and
            // take in the hillside before walking down to it.
            yield return Place(bot, EdgeSpot, Aim, 0.6f);
            yield return bot.Wait(0.6f);
            yield return Climb(bot);
        }

        /// <summary>Walks to a spot on the start stack and takes the cheese from there.</summary>
        public IEnumerator Take(Bot bot, Vector3 from)
        {
            // How big the cheese looks is decided here, by how far away it is taken from: stand exactly.
            yield return bot.WalkTo(from, 0.05f);
            yield return bot.Grab(Wedge);
        }

        /// <summary>
        /// Carries the cheese to a spot, turns the view so its ray passes through <paramref name="aim"/>,
        /// keeps it there for <paramref name="holdSeconds"/>, lets go and waits for the cheese to lie still.
        /// </summary>
        public IEnumerator Place(Bot bot, Vector3 from, Vector3 aim, float holdSeconds = 0f)
        {
            yield return bot.WalkTo(from, 0.15f);
            if (holdSeconds > 0f)
            {
                yield return bot.LookAt(aim);
                yield return bot.Wait(holdSeconds);
            }
            yield return bot.DropAt(aim);
            yield return bot.Until(() => AtRest(Wedge), 8f);
        }

        /// <summary>From the start stack down the leaning book and then up the cheese: <see cref="Descend"/>, <see cref="Ascend"/>.</summary>
        public IEnumerator Climb(Bot bot)
        {
            yield return Descend(bot);
            yield return Ascend(bot);
        }

        /// <summary>From anywhere on the start stack: to the head of the leaning book and down it to the rug.</summary>
        public IEnumerator Descend(Bot bot)
        {
            float bookX = (BookMinX + BookMaxX) * 0.5f;
            yield return bot.WalkTo(new Vector3(bookX, StackTop, GapNear - 0.5f));
            yield return bot.WalkTo(new Vector3(bookX, 0f, BookRun + 1f));
        }

        /// <summary>
        /// From the rug: up the cheese wherever it lies, and over its tall end onto the goal stack - with a
        /// hop if the top is lower than the stack or stands off it - and on to the exit.
        /// </summary>
        public IEnumerator Ascend(Bot bot)
        {
            Game game = bot.Game;

            // The wedge's own axis: from the middle of its knife edge to the middle of its crest.
            Vector3 half = ToyFactory.CheeseWedgeSize * 0.5f;
            Vector3 toe = Wedge.Transform.TransformPoint(new Vector3(0f, -half.y, -half.z));
            Vector3 crest = Wedge.Transform.TransformPoint(new Vector3(0f, half.y, half.z));
            Vector3 along = crest - toe;
            along.y = 0f;
            along.Normalize();

            yield return bot.WalkTo(toe - along * 1f, 0.3f, 15f);
            // Up the slope at full speed: the walk ends 0.3 short of the crest without slowing down.
            yield return bot.WalkTo(crest + along * 0.45f, 0.75f, 15f);
            bool hop = crest.y < StackTop + 0.1f || GapFar - crest.z > 0.45f;
            if (hop) yield return bot.Jump();
            yield return bot.WalkTo(ExitCentre, 0.5f, 15f);
            yield return bot.Until(() => game.LevelCompleted, 3f);
        }

        /// <summary>True once a prop that was let go has stopped moving.</summary>
        public static bool AtRest(Prop prop) =>
            !prop.Held && prop.Velocity.sqrMagnitude < 0.05f * 0.05f && prop.Body.angularVelocity.sqrMagnitude < 0.05f * 0.05f;
    }
}
