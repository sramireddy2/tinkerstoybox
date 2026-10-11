using System.Collections;
using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Levels
{
    /// <summary>
    /// Level 4, "Domino Effect" (LEVELS.md): size is weight, and where a toy stands has consequences.
    ///
    /// A hall of building blocks. The exit lies behind a tall arch that is walled up with blocks; between
    /// the start balcony and the arch a drawing board slopes down toward the wall. The one toy is a small
    /// domino on a pedestal. Held from the balcony edge with its foot on the footprint painted on the
    /// board it is as tall as a house; let go there it cannot stand on the slope, topples toward the arch
    /// like a drawbridge and smashes the barricade with its tip - if it is heavy enough and has room to
    /// fall. Too small it only bonks; too close to the wall it just leans.
    ///
    /// Measured in the simulation (tools/out/notes/level04-build.md): the wall needs a mass of 14, which
    /// is a scale of 3.08; from the balcony edge a view pitch of -12 to -3 degrees breaks through (scale
    /// 3.2 to 5.3), lower is too light or falls short, higher only leans. The solver's drop is 4.1 and
    /// strikes the wall at a speed of 8.
    ///
    /// All of that holds for a domino taken from right beside its pedestal (tools/out/notes/level04-review.md):
    /// it only ever gets as big as it looks, and taken from two steps away or more it cannot be made heavy
    /// with room to fall. So the level says so on such a grab, sends a domino that is too small to use
    /// back to the pedestal, and has a word for every try that fails (too light, no room, fell short,
    /// stood on the flat, laid flat). A domino that topples onto the player passes through them and comes
    /// to rest round them; that is the engine's doing (PerspectiveGrabber: a prop that outweighs the
    /// player and comes at them), the level has no guard of its own.
    ///
    /// The level starts where the first click works (the final review): on the shoe prints in front of
    /// the pedestal, a step from the domino and looking at it, as Level 1 starts at its cheese. From the
    /// back of the balcony, where it used to start, the domino was 11 away and no pick-up could work.
    /// </summary>
    [Level(4, "domino-effect", "Domino Effect", Phase = 1)]
    public sealed class Level04DominoEffect : LevelDefinition
    {
        // ---- Layout (hall floor y = 0, travel +Z) -----------------------------------------------------------
        public const float HalfWidth = 7f;
        public const float BalconyTop = 3f, BalconyBack = -6f, BalconyEdge = 10f;
        public const float BoardStart = 15f, BoardEnd = 25f;
        public const float PitY = -2.5f;
        public const float WallTop = 14f;
        public const float ArchHalfWidth = 5.5f, ArchTop = 9.5f, ArchDepth = 1.5f;
        public const float BarricadeDepth = 1.2f;
        public const float CorridorEnd = 40f;
        const float WallThickness = 1f;

        /// <summary>Drop per unit of travel down the board: 2.5 over 10, a slope of 14 degrees.</summary>
        public const float BoardSlope = (0f - PitY) / (BoardEnd - BoardStart);

        // ---- The domino -------------------------------------------------------------------------------------
        public const float DominoStartScale = 0.5f;
        public const string SmasherTag = "smasher";
        public const float BarricadeMinMass = 14f, BarricadeMinSpeed = 2.5f;
        /// <summary>The size the painted footprint is drawn for, and the least size that is heavy enough.</summary>
        public const float IntendedScale = 4.1f, LeastScale = 3.1f;
        /// <summary>Where the middle of the painted footprint lies on the board.</summary>
        public const float FootprintZ = 18.5f;

        static readonly Vector3 PedestalSize = new Vector3(0.8f, 0.9f, 0.8f);
        static readonly Vector3 PedestalAt = new Vector3(-3f, BalconyTop, 8f);

        /// <summary>
        /// Where the solver stands to pick the domino up, and to let it go. The bot stops a few hundredths
        /// short of a walk target: 1.1 in front of the domino, eye to centre 1.11, scale / distance 0.45.
        /// </summary>
        public static readonly Vector3 PickupSpot = new Vector3(-3f, BalconyTop, 6.94f);
        public static readonly Vector3 EdgeSpot = new Vector3(0f, BalconyTop, 9.5f);
        /// <summary>
        /// What the solver looks at when it lets go: a pitch of about -7 degrees from where the bot comes to
        /// a stop at the balcony edge, which stands the domino on the painted footprint at 4.1.
        /// </summary>
        public static readonly Vector3 AimPoint = new Vector3(0f, 3.35f, 19f);

        /// <summary>
        /// The start: on the shoe prints in front of the pedestal, 1.1 from the domino and looking at it
        /// (scale / distance 0.45: picked up from here it is big enough). It is where the solver's walk to
        /// <see cref="PickupSpot"/> comes to a stop, so that walk is no step at all.
        /// </summary>
        public static readonly Vector3 SpawnPoint = new Vector3(-3f, BalconyTop, 6.9f);
        public const float SpawnPitch = -8f;
        public static readonly Vector3 ExitCenter = new Vector3(0f, -1f, 36f);
        public static readonly Vector3 ExitSize = new Vector3(6f, 3f, 4f);

        /// <summary>
        /// scale / distance below which the domino looks too small in the hand. Measured from the balcony
        /// edge (tools/out/notes/level04-review.md): taken from 1.3 away or nearer (0.39 and up) a domino
        /// stood anywhere on the painted footprint is heavy enough and reaches the wall; at 0.34 only the
        /// far end of the footprint still works, from 0.25 down nothing works from anywhere, and from the
        /// spawn (0.044) it never passes 0.73.
        /// </summary>
        public const float LooksTooSmall = 0.38f;
        /// <summary>
        /// A domino let go smaller than this goes back to its pedestal. On a floor it cannot be taken from
        /// nearer than the eye is high: below 0.6 it would look too small from wherever it lies, and "pick
        /// it up from closer" would be advice nobody can follow.
        /// </summary>
        public const float CrumbScale = 0.6f;

        /// <summary>Said whenever the domino is taken looking too small (the same words as in Level 1).</summary>
        public const string SmallLine = "It only ever gets as big as it looks. Pick it up from closer.";
        /// <summary>Said when a domino too small to use has gone back to the pedestal.</summary>
        public const string CrumbLine = "That domino was too small to use. It is back on its block.";
        public const string TooLight = "Too light. That wall won't move for something so small.";
        public const string NoRoom = "Heavy enough, but it needs room to fall.";
        /// <summary>Said when it toppled on the board and came down before the wall, or only brushed it lying flat.</summary>
        public const string FellShort = "It fell short of the wall. Stand it farther down the board.";
        /// <summary>Said when it was stood on the flat hall floor, where it has no reason to fall.</summary>
        public const string OnTheFlat = "It stands firm on the flat floor. The board beyond is tilted.";
        /// <summary>Said when it was let go lying down (the flip key) and stayed where it was put.</summary>
        public const string LyingFlat = "Lying flat it has nowhere to fall. Stand it on its foot.";
        /// <summary>
        /// Said once, for a click at one of the wooden dominoes of the set: they look like the toy, some of
        /// them bigger than it, and a click at them did nothing and said nothing.
        /// </summary>
        public const string StuckLine = "Those are stuck fast. The purple one is yours.";

        /// <summary>The domino, once the level is built.</summary>
        public Prop Domino { get; private set; }
        /// <summary>The wall of blocks in the arch.</summary>
        public Breakable Barricade { get; private set; }
        public Exit Exit { get; private set; }
        /// <summary>Brings a domino that is too small to use back to the pedestal.</summary>
        public PropLeash Crumbs { get; private set; }

        Level04Barricade wall;
        readonly List<GameObject> spares = new List<GameObject>();
        string lastLine;
        int lastSaid, restTicks;
        bool judged, laidFlat, wasHolding, stuckSaid;

        public override string Blurb => "That wall won't move for something small.";

        public override string[] Hints => new[]
        {
            "A domino only knocks down what it out-weighs. Bigger is heavier. Much heavier.",
            "Stand the domino on the tilted board, a little way back from the wall. It needs to cover the painted footprint, and it needs room to fall.",
            "Pick the domino up from right beside it. From the balcony edge, hold it so its foot covers the painted footprint on the board, and let go.",
        };

        public override string Environment => "block-hall";

        // The foot of the board and the corridor behind the arch lie 2.5 below the hall floor: that is
        // where the room's own boards are.
        public override float GroundY => PitY;
        public override float KillY => -30f;

        /// <summary>Height of the ground along the hall: the hall floor, the board's slope, the corridor.</summary>
        public static float BoardY(float z) => -BoardSlope * (Mathf.Clamp(z, BoardStart, BoardEnd) - BoardStart);

        public override void Build(LevelContext ctx)
        {
            Domino = null;
            Barricade = null;
            Exit = null;
            Crumbs = null;
            wall = null;
            lastLine = null;
            lastSaid = -100000;
            restTicks = 0;
            judged = true;
            laidFlat = false;
            wasHolding = false;
            stuckSaid = false;
            spares.Clear();

            // What is only drawn is merged per material (ART_BIBLE 12.1): the hall and everything that
            // stands in it in one set, the paint - which casts no shadow - in another. The colliders stay
            // where the pieces stand. (The barricade is not in it: it shudders and breaks.)
            var hall = new Level01Set();
            var paint = new Level01Set();
            BuildHall(ctx, hall);
            BuildDressing(ctx, hall);
            BuildPaint(ctx, paint);

            // The pedestal: a building block on the balcony, on the way to the edge.
            Piece(ctx, hall, ToyFactory.WoodenBlock(PedestalSize, grabbable: false), PedestalAt + Vector3.up * (PedestalSize.y * 0.5f), Quaternion.identity);
            hall.Finish(ctx, "Hall");
            paint.Finish(ctx, "Paint", castShadows: false);
            ToyDef def = ToyCatalog.Get(ToyId.Domino);
            Vector3 dominoAt = PedestalAt + Vector3.up * (PedestalSize.y + def.RestHeight * DominoStartScale);
            Domino = ToyCatalog.Add(ctx, ToyId.Domino, dominoAt, DominoStartScale, null, o => o.Tags = new[] { SmasherTag });

            var barricadeSize = new Vector3(ArchHalfWidth * 2f, ArchTop - PitY, BarricadeDepth);
            wall = new Level04Barricade(barricadeSize);
            Barricade = new Breakable(ctx, new BreakableOptions
            {
                Name = "barricade",
                Center = new Vector3(0f, (PitY + ArchTop) * 0.5f, BoardEnd + BarricadeDepth * 0.5f),
                Size = barricadeSize,
                AcceptTag = SmasherTag,
                MinMass = BarricadeMinMass,
                MinSpeed = BarricadeMinSpeed,
                Direction = Vector3.forward,
                OnBreak = BreakMode.RemoveCollider,
                Body = wall.Body,
            });
            Barricade.Bonked += (prop, share) => OnBonked(ctx, share);

            // Should the domino ever leave the hall (it cannot by any means the player has), it comes back.
            new PropLeash(ctx, new PropLeashOptions
            {
                Name = "hall leash",
                Props = new[] { Domino },
                Allowed = new[]
                {
                    Zone.MinMax(new Vector3(-HalfWidth - 0.5f, PitY - 1f, BalconyBack - 0.5f), new Vector3(HalfWidth + 0.5f, WallTop + 1f, CorridorEnd + 0.5f)),
                },
                Grace = 2f,
            });

            // The one way a try goes quietly wrong: the domino only ever gets as big as it looks, and taken
            // from across the balcony it looks tiny. Said on every such grab, in Level 1's words.
            ctx.Game.Events.PropGrabbed += e =>
            {
                if (e.Prop != Domino) return;
                judged = true;
                if (!Barricade.Broken && e.OldScale < LooksTooSmall * e.GrabDistance) Say(ctx, SmallLine, 6f);
            };
            ctx.Game.Events.PropDropped += e =>
            {
                if (e.Prop != Domino) return;
                judged = false;
                restTicks = 0;
                laidFlat = Mathf.Abs(Domino.Transform.up.y) < 0.5f;
            };

            // "Closer" has to be possible. A domino let go at a crumb's size looks too small from anywhere
            // it can lie, so it goes back to the pedestal, where it can be taken from beside. (On the
            // pedestal it is the size it started with, which is below the crumb's: that place is excused.)
            Crumbs = new PropLeash(ctx, new PropLeashOptions
            {
                Name = "crumb",
                Props = new[] { Domino },
                Allowed = new[] { Zone.Box(dominoAt + Vector3.up * 0.05f, new Vector3(PedestalSize.x + 0.3f, def.Size.y * DominoStartScale + 0.3f, PedestalSize.z + 0.3f)) },
                Unless = () => Domino.Scale >= CrumbScale || Barricade.Broken,
                Grace = 1.5f,
            });
            Crumbs.PropReturned += prop =>
            {
                judged = true;
                Say(ctx, CrumbLine, 5f);
            };

            ctx.OnUpdate(dt =>
            {
                Watch(ctx);
                WatchTheSpares(ctx);
            });

            Exit = ctx.AddExit(ExitCenter, ExitSize);
            ctx.SetSpawn(SpawnPoint, 0f, SpawnPitch);
        }

        // ---- Geometry ---------------------------------------------------------------------------------------

        void BuildHall(LevelContext ctx, Level01Set set)
        {
            // Floors are boards; whatever stands up is built from blocks: columns of them, with joints.
            Material floor = Materials.Room(RoomSurface.LevelStatic, pattern: new PatternSpec(RoomPattern.Planks, 2f, 8f, 0.08f));
            Material blocks = Materials.Room(RoomSurface.LevelStatic, pattern: new PatternSpec(RoomPattern.Planks, 2f, 2.5f, 0.1f, 0.07f));
            // The drawing board takes the furniture tones (mid on top), so it reads apart from the hall floor.
            Material board = Materials.Room(RoomSurface.Furniture);

            // The balcony the level starts on, and the hall floor at its foot.
            Piece(ctx, set, BasicToys.Slab(new Vector3(HalfWidth * 2f, BalconyTop - PitY, BalconyEdge - BalconyBack), floor),
                new Vector3(0f, (BalconyTop + PitY) * 0.5f, (BalconyEdge + BalconyBack) * 0.5f), Quaternion.identity);
            Piece(ctx, set, BasicToys.Slab(new Vector3(HalfWidth * 2f, -PitY, BoardStart - BalconyEdge), floor),
                new Vector3(0f, PitY * 0.5f, (BoardStart + BalconyEdge) * 0.5f), Quaternion.identity);

            // Two blocks as a stair down (and back up: each step is a hop of 1).
            Piece(ctx, set, ToyFactory.WoodenBlock(new Vector3(2f, 2f, 1.5f), grabbable: false), new Vector3(6f, 1f, 10.75f), Quaternion.identity);
            Piece(ctx, set, ToyFactory.WoodenBlock(new Vector3(2f, 1f, 1.5f), grabbable: false), new Vector3(6f, 0.5f, 12.25f), Quaternion.identity);

            // The drawing board: 14 degrees down toward the arch. A domino cannot stand on it.
            GameObject ramp = Piece(ctx, set, BasicToys.Ramp(BoardEnd - BoardStart, -PitY, HalfWidth * 2f, board),
                new Vector3(0f, PitY * 0.5f, (BoardStart + BoardEnd) * 0.5f), Quaternion.Euler(0f, 180f, 0f));
            var grip = new PhysicsMaterial("Drawing Board") { dynamicFriction = 0.7f, staticFriction = 0.7f, frictionCombine = PhysicsMaterialCombine.Average };
            foreach (Collider collider in ramp.GetComponentsInChildren<Collider>()) collider.sharedMaterial = grip;
            ctx.OnDispose(() => Sim.Destroy(grip));

            // The arch wall, as tall as the sky cap: two towers and a lintel. The barricade fills the opening.
            float outer = HalfWidth + WallThickness;
            float wallHeight = WallTop - PitY, wallY = (WallTop + PitY) * 0.5f;
            float archZ = BoardEnd + ArchDepth * 0.5f, tower = outer - ArchHalfWidth;
            Piece(ctx, set, BasicToys.Slab(new Vector3(tower, wallHeight, ArchDepth), blocks), new Vector3(-ArchHalfWidth - tower * 0.5f, wallY, archZ), Quaternion.identity);
            Piece(ctx, set, BasicToys.Slab(new Vector3(tower, wallHeight, ArchDepth), blocks), new Vector3(ArchHalfWidth + tower * 0.5f, wallY, archZ), Quaternion.identity);
            Piece(ctx, set, BasicToys.Slab(new Vector3(ArchHalfWidth * 2f, WallTop - ArchTop, ArchDepth), blocks), new Vector3(0f, (WallTop + ArchTop) * 0.5f, archZ), Quaternion.identity);

            // The walls of the hall and of the corridor behind the arch. What stops a held toy reaches the
            // sky cap everywhere (LEVELS: walls to 14); what is drawn are rows of blocks only as tall as a
            // playroom fort, so the sun gets in and the room shows over them. Nothing the player can stand
            // on comes within reach of a block's top.
            float back = BalconyBack - WallThickness, end = CorridorEnd + WallThickness, corridor = BoardEnd + ArchDepth;
            Solid(ctx, "Wall -X", new Vector3(-outer, PitY, back), new Vector3(-HalfWidth, WallTop, BoardEnd));
            Solid(ctx, "Wall +X", new Vector3(HalfWidth, PitY, back), new Vector3(outer, WallTop, BoardEnd));
            Solid(ctx, "Wall Back", new Vector3(-HalfWidth, PitY, back), new Vector3(HalfWidth, WallTop, BalconyBack));
            Solid(ctx, "Corridor -X", new Vector3(-ArchHalfWidth - WallThickness, PitY, corridor), new Vector3(-ArchHalfWidth, WallTop, end));
            Solid(ctx, "Corridor +X", new Vector3(ArchHalfWidth, PitY, corridor), new Vector3(ArchHalfWidth + WallThickness, WallTop, end));
            Solid(ctx, "Corridor End", new Vector3(-ArchHalfWidth, PitY, CorridorEnd), new Vector3(ArchHalfWidth, WallTop, end));
            SkyCap.Add(ctx, -outer, outer, back, end, WallTop);

            // The sun comes from +X: that side is kept low beside the board, so the footprint lies in the light.
            BlockRow(set, -outer, -HalfWidth, back, BoardEnd, 7f, 0.8f, blocks);
            BlockRow(set, -HalfWidth, HalfWidth, back, BalconyBack, 7f, 0.8f, blocks);
            BlockRow(set, HalfWidth, outer, back, BalconyEdge, 6.2f, 0.7f, blocks);
            BlockRow(set, HalfWidth, outer, BalconyEdge, 13f, 5.2f, 0f, blocks);
            BlockRow(set, HalfWidth, outer, 13f, BoardEnd, 3.4f, 0.6f, blocks);
            BlockRow(set, -ArchHalfWidth - WallThickness, -ArchHalfWidth, corridor, end, 1f, 0.6f, blocks);
            BlockRow(set, ArchHalfWidth, ArchHalfWidth + WallThickness, corridor, end, 1f, 0.6f, blocks);
            BlockRow(set, -ArchHalfWidth, ArchHalfWidth, CorridorEnd, end, 1f, 0.6f, blocks);
        }

        // A set piece: its colliders stand in the level, its looks join the set.
        static GameObject Piece(LevelContext ctx, Level01Set set, GameObject piece, Vector3 centre, Quaternion rotation)
        {
            ctx.AddStatic(piece, centre, rotation);
            set.Absorb(piece);
            return piece;
        }

        // A collider and nothing to see, like the sky cap.
        static void Solid(LevelContext ctx, string name, Vector3 min, Vector3 max)
        {
            var solid = new GameObject(name) { layer = Layers.Default };
            solid.AddComponent<BoxCollider>().size = max - min;
            ctx.AddStatic(solid, (min + max) * 0.5f);
        }

        // Building blocks standing side by side along a wall line, every other one a notch lower: looks
        // only (the wall's collider is a Solid). They stand on the room's floor.
        static void BlockRow(Level01Set set, float xMin, float xMax, float zMin, float zMax, float top, float notch, Material material)
        {
            bool alongZ = zMax - zMin >= xMax - xMin;
            float length = alongZ ? zMax - zMin : xMax - xMin;
            int count = Mathf.Max(1, Mathf.RoundToInt(length / 2f));
            float each = length / count;
            for (int i = 0; i < count; i++)
            {
                float height = top - (i % 2 == 1 ? notch : 0f) - PitY;
                float along = (alongZ ? zMin : xMin) + (i + 0.5f) * each;
                Vector3 size = alongZ ? new Vector3(xMax - xMin, height, each) : new Vector3(each, height, zMax - zMin);
                Vector3 centre = alongZ ? new Vector3((xMin + xMax) * 0.5f, PitY + height * 0.5f, along) : new Vector3(along, PitY + height * 0.5f, (zMin + zMax) * 0.5f);
                set.Box(material, centre, size);
            }
        }

        // A playroom fort: spare blocks, the rest of the domino set, a roof on each tower. Everything stands
        // well to the side of the way from the pedestal to the balcony edge and of the domino's path.
        void BuildDressing(LevelContext ctx, Level01Set set)
        {
            // The rest of the set, in wood: not the player's, and a size too small each. (They look like
            // the toy all the same: a click at one of them is answered, see WatchTheSpares.)
            float[] sizes = { 0.35f, 0.5f, 0.7f, 0.95f };
            float[] along = { 0.6f, 1.7f, 3f, 4.6f };
            for (int i = 0; i < sizes.Length; i++)
            {
                GameObject piece = ctx.AddStatic(ToyFactory.Domino(grabbable: false),
                    new Vector3(-5.9f, BalconyTop + ToyFactory.DominoSize.y * 0.5f * sizes[i], along[i]));
                piece.transform.localScale = Vector3.one * sizes[i];
                set.Absorb(piece);
                spares.Add(piece);
            }

            // Spare blocks: a pile on the balcony, two more at the foot of it.
            Piece(ctx, set, ToyFactory.WoodenBlock(1.4f, grabbable: false), new Vector3(5.5f, BalconyTop + 0.7f, 5.2f), Quaternion.Euler(0f, 12f, 0f));
            Piece(ctx, set, ToyFactory.WoodenBlock(0.9f, grabbable: false), new Vector3(5.45f, BalconyTop + 1.85f, 5.15f), Quaternion.Euler(0f, -20f, 0f));
            Piece(ctx, set, ToyFactory.WoodenBlock(1.2f, grabbable: false), new Vector3(-5.9f, 0.6f, 11.3f), Quaternion.Euler(0f, 8f, 0f));
            Piece(ctx, set, ToyFactory.WoodenBlock(0.8f, grabbable: false), new Vector3(-4.6f, 0.4f, 12.1f), Quaternion.Euler(0f, 35f, 0f));

            // A roof block on each tower, above the sky cap: no toy and no player ever gets there.
            float tower = HalfWidth + WallThickness - ArchHalfWidth;
            const float roofHeight = 1.4f;
            Mesh roof = MeshKit.Cached(MeshKit.Key("Level04/Roof", tower, roofHeight, ArchDepth), () => MeshKit.Extrude(
                new List<Vector2> { new Vector2(-tower * 0.5f, 0f), new Vector2(tower * 0.5f, 0f), new Vector2(0f, roofHeight) }, ArchDepth, 0.05f));
            Material wood = Materials.Toy(ToyRecipe.PlainProp, Palette.Birch);
            for (int side = -1; side <= 1; side += 2)
                set.Shape(wood, roof, new Vector3(side * (ArchHalfWidth + tower * 0.5f), WallTop, BoardEnd + ArchDepth * 0.5f), Quaternion.identity);
        }

        // The size language of the campaign: the footprint of the domino at the size that works, painted on
        // the board where it should stand, with the least size that is heavy enough dashed inside it.
        void BuildPaint(LevelContext ctx, Level01Set set)
        {
            Dip dip = ctx.Dip;
            Color hero = ToyCatalog.Get(ToyId.Domino).ColorIn(dip);
            // Strong enough to be found from the balcony edge, nine units off and at a shallow angle, in full sun.
            Material fill = Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Light, hero, 0.38f)));
            Material line = Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Light, hero, 0.9f)));

            float slopeDegrees = Mathf.Atan(BoardSlope) * Mathf.Rad2Deg;
            Quaternion onBoard = Quaternion.Euler(slopeDegrees, 0f, 0f);
            Vector3 normal = onBoard * Vector3.up;
            // The footprint's own space: its middle on the board, a hair proud of it, tilted with it. Every
            // dash used to be an object of its own (38 draws of one material); they are one mesh now.
            Matrix4x4 board = Matrix4x4.TRS(new Vector3(0f, BoardY(FootprintZ), FootprintZ) + normal * 0.012f, onBoard, Vector3.one);

            Vector3 domino = ToyFactory.DominoSize;
            float width = IntendedScale * domino.x, depth = IntendedScale * domino.z;
            Quad(set, board, new Vector2(width, depth), Vector3.zero, fill);
            DashedRectangle(set, board, width, depth, 0.16f, 0.5f, 0.22f, 0.006f, line);
            DashedRectangle(set, board, LeastScale * domino.x, LeastScale * domino.z, 0.08f, 0.22f, 0.2f, 0.006f, line);

            // Where to stand: a round pad with a pair of shoe prints, in front of the pedestal (take the
            // domino from here and it is big enough in the hand) and at the balcony edge in front of the
            // footprint. They are in the room's own tones: the toy's colour is kept for where the toy goes,
            // so the dashed footprint on the board is the only thing that asks for the domino.
            Material pad = Materials.Room(RoomRecipe.Solid(dip.Deep));
            Material print = Materials.Room(RoomRecipe.Solid(Palette.Paper));
            StandMark(set, PedestalMark, pad, print);
            StandMark(set, EdgeMark, pad, print);
        }

        /// <summary>The two pairs of shoe prints: where to pick the domino up (the level starts on it), and where to let it go.</summary>
        public static readonly Vector3 PedestalMark = new Vector3(PickupSpot.x, BalconyTop, PickupSpot.z);
        public static readonly Vector3 EdgeMark = new Vector3(EdgeSpot.x, BalconyTop, BalconyEdge - PadRadius - 0.08f);

        const float PadRadius = 0.62f;

        // Looks only: the pad and a shoe print for each foot, facing +Z, toes a little apart.
        static void StandMark(Level01Set set, Vector3 at, Material pad, Material print)
        {
            at += Vector3.up * 0.012f;
            Mesh disc = MeshKit.Cached(MeshKit.Key("Level04/Pad", PadRadius), () => MeshKit.Cylinder(PadRadius, 0.004f, 40));
            set.Shape(pad, disc, at, Quaternion.identity);
            Mesh sole = MeshKit.Cached(MeshKit.Key("Level04/Sole"), () =>
            {
                // A sole in the XY plane, toe up: half a circle for the toe, a smaller half for the heel.
                var outline = new List<Vector2>();
                for (int i = 0; i <= 12; i++)
                {
                    float a = Mathf.PI * i / 12f;
                    outline.Add(new Vector2(Mathf.Cos(a) * 0.16f, 0.17f + Mathf.Sin(a) * 0.16f));
                }
                for (int i = 0; i <= 12; i++)
                {
                    float a = Mathf.PI + Mathf.PI * i / 12f;
                    outline.Add(new Vector2(Mathf.Cos(a) * 0.115f, -0.2f + Mathf.Sin(a) * 0.115f));
                }
                return MeshKit.Extrude(outline, 0.004f);
            });
            for (int side = -1; side <= 1; side += 2)
            {
                Quaternion flat = Quaternion.Euler(0f, side * 9f, 0f) * Quaternion.Euler(90f, 0f, 0f);
                set.Shape(print, sole, at + new Vector3(side * 0.24f, 0.006f, 0f), flat);
            }
        }

        static void DashedRectangle(Level01Set set, Matrix4x4 frame, float width, float depth, float stroke, float dash, float gap, float lift, Material material)
        {
            DashedLine(set, frame, new Vector3(-width * 0.5f, lift, depth * 0.5f), new Vector3(width * 0.5f, lift, depth * 0.5f), stroke, dash, gap, material);
            DashedLine(set, frame, new Vector3(-width * 0.5f, lift, -depth * 0.5f), new Vector3(width * 0.5f, lift, -depth * 0.5f), stroke, dash, gap, material);
            DashedLine(set, frame, new Vector3(-width * 0.5f, lift, -depth * 0.5f), new Vector3(-width * 0.5f, lift, depth * 0.5f), stroke, dash, gap, material);
            DashedLine(set, frame, new Vector3(width * 0.5f, lift, -depth * 0.5f), new Vector3(width * 0.5f, lift, depth * 0.5f), stroke, dash, gap, material);
        }

        // Dashes along a line in the frame's XZ plane; a dash sits on each end.
        static void DashedLine(Level01Set set, Matrix4x4 frame, Vector3 from, Vector3 to, float stroke, float dash, float gap, Material material)
        {
            Vector3 along = to - from;
            float length = along.magnitude;
            if (length < 1e-4f) return;
            along /= length;
            int count = Mathf.Max(2, Mathf.RoundToInt((length + gap) / (dash + gap)));
            float each = Mathf.Max(stroke, (length - gap * (count - 1)) / count);
            bool alongX = Mathf.Abs(along.x) > Mathf.Abs(along.z);
            var size = alongX ? new Vector2(each, stroke) : new Vector2(stroke, each);
            for (int i = 0; i < count; i++)
                Quad(set, frame, size, from + along * (each * 0.5f + i * (each + gap)), material);
        }

        // A flat patch of paint at a place in the frame, lying in its XZ plane.
        static void Quad(Level01Set set, Matrix4x4 frame, Vector2 size, Vector3 localPosition, Material material)
        {
            Mesh mesh = MeshKit.Cached(MeshKit.Key("Level04/Quad", size.x, size.y), () => MeshKit.Grid(size, 1, 1));
            set.Shape(material, mesh, frame.MultiplyPoint3x4(localPosition), frame.rotation);
        }

        // ---- What the wall says -----------------------------------------------------------------------------

        // The barricade shuddered and held: say why. Too light is too light. Heavy enough but too slow
        // into the face has two causes that want opposite advice: it stood so close that it could only
        // lean over (still nearly upright when it touched), or so far off that it came down almost flat.
        void OnBonked(LevelContext ctx, float share)
        {
            wall.Shudder(ctx, share);
            if (share < 1f) Say(ctx, TooLight);
            else if (laidFlat) Say(ctx, LyingFlat);
            else Say(ctx, Mathf.Abs(Domino.Transform.up.y) > 0.6f ? NoRoom : FellShort);
        }

        // One line, and not the same one over and over.
        void Say(LevelContext ctx, string text, float seconds = 5f)
        {
            if (text == lastLine && ctx.Ticks - lastSaid < 360) return;
            lastLine = text;
            lastSaid = ctx.Ticks;
            ctx.Say(text, seconds);
        }

        // Every tick: the wall's own motion, and a word about a try that ended without the wall hearing
        // of it - no attempt ends in silence. The domino is judged once, when it has come to rest after
        // being let go.
        void Watch(LevelContext ctx)
        {
            wall.Tick(ctx);
            if (judged || Barricade.Broken || Domino == null || Domino.Removed || Domino.Held)
            {
                restTicks = 0;
                return;
            }
            bool still = Domino.Velocity.sqrMagnitude < 0.01f && Domino.Body.angularVelocity.sqrMagnitude < 0.01f;
            restTicks = still ? restTicks + 1 : 0;
            if (restTicks < 45) return;
            judged = true;
            string line = Judge();
            if (line != null) Say(ctx, line);
        }

        // A click with empty hands whose ray ends on one of the wooden dominoes: nothing is picked up, and
        // the reticle's few pixels of difference do not say why. One line, once. (The click that picked the
        // purple one up, and the click that let it go, are not clicks at the set.)
        void WatchTheSpares(LevelContext ctx)
        {
            Game game = ctx.Game;
            bool holding = game.Grabber.IsHolding;
            bool clicked = game.LastInput.GrabPressed && !holding && !wasHolding;
            wasHolding = holding;
            if (!clicked || stuckSaid) return;
            Player player = game.Player;
            if (!game.PhysicsScene.Raycast(player.Eye, player.Forward, out RaycastHit hit, PerspectiveGrabber.GrabRange, Layers.SolidMask, QueryTriggerInteraction.Ignore)) return;
            foreach (GameObject spare in spares)
            {
                if (spare == null || !hit.collider.transform.IsChildOf(spare.transform)) continue;
                stuckSaid = true;
                ctx.Say(StuckLine, 5f);
                return;
            }
        }

        // Why the domino that lies or stands there did not bring the wall down (null: it is not a try at
        // the wall at all - it was put down on the balcony, or is a crumb on its way back to the pedestal).
        string Judge()
        {
            if (Domino.Scale < CrumbScale) return null;
            GadgetKit.VerticalExtent(Domino, out float bottom, out _);
            Vector3 centre = Domino.Center;
            if (centre.z < BalconyEdge + 0.5f && bottom > BalconyTop - 0.3f) return null;

            bool heavy = Domino.Mass >= BarricadeMinMass;
            float up = Mathf.Abs(Domino.Transform.up.y);
            Vector3 foremost = GadgetKit.ClosestPoint(Domino, centre + Vector3.forward * 1000f);
            bool against = foremost.z > BoardEnd - 0.3f;
            // Lying down: it toppled, and either never reached the wall or was too light for it.
            if (up < 0.5f) return against && !heavy ? TooLight : laidFlat ? LyingFlat : FellShort;
            // Still on its foot. Against the wall it can only lean.
            if (against) return heavy ? NoRoom : TooLight;
            // On the flat floor of the hall it simply stands.
            if (up > 0.95f && centre.z < BoardStart + 0.25f && bottom < 0.3f) return OnTheFlat;
            return null;
        }

        // ---- The solution -----------------------------------------------------------------------------------

        public override IEnumerator Solve(Bot bot)
        {
            // Pick the domino up from close by, square on: it is small in the hand, so it is big far away.
            yield return bot.WalkTo(PickupSpot, 0.05f);
            yield return bot.LookAt(Domino);
            yield return bot.Grab(Domino);

            // From the balcony edge, with its foot on the painted footprint, it is as tall as a house.
            yield return bot.WalkTo(EdgeSpot, 0.1f);
            yield return bot.DropAt(AimPoint);
            yield return bot.Until(() => Barricade.Broken, 8f);

            yield return WalkOut(bot);
        }

        /// <summary>
        /// The second half of the solution, once the barricade is down: wait for the domino to come to
        /// rest, go down the two blocks, pass the fallen domino on whichever side leaves more room, and
        /// walk into the exit.
        /// </summary>
        public IEnumerator WalkOut(Bot bot)
        {
            Game game = bot.Game;
            yield return bot.Until(() => Domino.Velocity.sqrMagnitude < 0.04f && Domino.Body.angularVelocity.sqrMagnitude < 0.04f, 8f);
            yield return bot.Wait(0.5f);

            // How much of the arch is free on either side of the domino that lies in it.
            float left = ArchHalfWidth, right = ArchHalfWidth;
            foreach (Collider collider in Domino.Colliders)
            {
                Bounds bounds = collider.bounds;
                if (bounds.max.z < BoardEnd - 1.5f || bounds.min.z > BoardEnd + ArchDepth + 1.5f) continue;
                left = Mathf.Min(left, bounds.min.x + ArchHalfWidth);
                right = Mathf.Min(right, ArchHalfWidth - bounds.max.x);
            }
            if (Mathf.Max(left, right) < 1.2f)
            {
                // It lies right across the way: take it out of it (small again, on the balcony behind the bot).
                yield return bot.Grab(Domino);
                yield return bot.DropAt(bot.Player.Position + new Vector3(0f, 0.3f, -2f));
                yield return bot.Wait(0.5f);
                left = right = ArchHalfWidth;
            }
            float lane = right >= left ? ArchHalfWidth - right * 0.5f : -ArchHalfWidth + left * 0.5f;

            yield return bot.WalkTo(new Vector3(6f, BalconyTop, 9.5f));
            yield return bot.WalkTo(new Vector3(6f, 0f, 14.2f));
            yield return bot.WalkTo(new Vector3(lane, 0f, 15.5f));
            yield return bot.WalkTo(new Vector3(lane, PitY, BoardEnd + ArchDepth + 2f), 0.3f, 15f);
            // Along the corridor wall until level with the exit (a long domino reaches far into the corridor), then in.
            yield return bot.WalkTo(new Vector3(lane, PitY, ExitCenter.z), 0.3f, 15f);
            yield return bot.WalkTo(ExitCenter, 0.5f, 15f);
            yield return bot.Until(() => game.LevelCompleted, 3f);
        }
    }
}
