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
    /// Level 3 (LEVELS.md): near means small. The player stands in an open shipping box whose only way out
    /// is a flap held shut by a button at the bottom of an egg cup. The one toy is a boulder-sized apple on
    /// a wall shelf far above the box: grabbed from down here it only looks small enough to hold, and let
    /// go over the cup it IS that small - a marble that rolls down the cup onto the button.
    ///
    /// Box floor y = 0, interior x -3..3, z -4..4. The +Z wall is 4 high (the apple is seen over it), the
    /// other three 6; the flap is in the +X wall and falls outward onto the sheet the exit stands on.
    ///
    /// The level starts at the back wall looking down the box: the apple over the low wall at the top of
    /// the picture, the cup with its lit button on the floor ahead, its lead running to the flap on the
    /// right. The cup stands free (one can walk all round it) and is low and wide: from anywhere within
    /// reach of it a held apple passes the rim cleanly, so whether it fits is decided by how far away it
    /// was let go and by nothing else.
    /// </summary>
    [Level(3, "shrinking-apple", "Shrinking the Apple", Phase = 1)]
    public sealed class Level03ShrinkingApple : LevelDefinition
    {
        // ---- The box ------------------------------------------------------------------------------------
        public const float HalfX = 3f, HalfZ = 4f, WallThickness = 0.3f;
        public const float LowWall = 4f, HighWall = 6f;
        /// <summary>The box's cardboard bottom: the den's floor lies this far under the box floor (y = 0).</summary>
        public const float FloorThickness = 0.1f;
        const float DoorHalfWidth = 1f, DoorHeight = 3f, FlapThickness = 0.08f;

        // ---- The cup and its button ---------------------------------------------------------------------
        public const float CupX = 1.2f, CupZ = -0.2f;
        /// <summary>
        /// A low, wide dish (spec: rim 0.6, mouth 0.6 inside a 0.1 lip, throat 0.26). The mouth is the whole
        /// top of the body, so there is no flat lip for a marble to come to rest on, and the rim is low
        /// enough that an apple held over the button from 1.8 away does not touch it. The throat passes an
        /// apple of up to 0.64: one aimed at the middle of the cup from farther away stops against the near
        /// rim at 0.56 .. 0.63 whatever the distance, and that has to fit.
        /// </summary>
        public const float RimY = 0.45f, MouthRadius = 0.7f, ThroatRadius = 0.32f, ThroatY = 0.3f, ButtonY = 0.12f, CupOuterRadius = 0.7f;
        /// <summary>
        /// The cup is the gate, not the scales: whatever passes the throat presses the button, down to the
        /// apple's smallest size (no prop weighs less than 0.01). A marble that lay ON the button and was
        /// "too light" for it failed for a reason nobody could see, and the blurb asks for something small.
        /// </summary>
        public const float ButtonMass = 0.005f;
        /// <summary>
        /// The apple's clamps: it can never be bigger than it starts, and never smaller than a pea. A toy at
        /// its smallest cannot be put down nearer than it was picked up from, so the pea is small enough
        /// (0.1, spec 0.15) that a marble made at the feet can be taken from 5.8 away - about anywhere in the
        /// box - and still be let go over the cup.
        /// </summary>
        public const float SmallestApple = 0.1f, BiggestApple = 12f;
        const float ButtonTravel = 0.04f;
        const string FitsTag = "fits-the-cup";

        /// <summary>Said when an apple too big for the throat has come to rest in the cup's mouth.</summary>
        public const string TooBigLine = "Too big for the cup. Pick it up from farther off and let go up close.";
        /// <summary>
        /// Said when a pea was let go where the view could not put it. A toy cannot be put down nearer than
        /// where it would be at its smallest size: a marble picked up from across the box and held over the
        /// cup from beside it would have to be smaller than a pea there, so the click leaves it where it
        /// could last lie - on the floor beyond the cup - and nothing on screen says why.
        /// </summary>
        public const string PeaLine = "It cannot get small enough to come that close. Walk up to it and pick it up from there.";
        /// <summary>Below this the apple is a pea, and a let-go that the view could not place gets <see cref="PeaLine"/>.</summary>
        public const float PeaScale = 0.2f;
        /// <summary>Said to whoever got out of the box any other way than through the flap.</summary>
        public const string OutsideLine = "The flap is the only way out of the box.";

        // ---- The shelf and the apple --------------------------------------------------------------------
        public const float ShelfTop = 18f, AppleScale = 11.4f;
        public static readonly Vector3 AppleOrigin = new Vector3(0f, ShelfTop + AppleScale * 0.5f, 30f);

        // ---- The solution -------------------------------------------------------------------------------
        /// <summary>Where the level starts: at the back wall, looking down the box (feet).</summary>
        public static readonly Vector3 SpawnPoint = new Vector3(0f, 0f, -3.6f);
        public const float SpawnPitch = 8.5f;
        /// <summary>Beside the cup, on the way to it from the start: 1.15 from its axis (cup 0.7 + capsule 0.3 is the nearest).</summary>
        public static readonly Vector3 StandPoint = new Vector3(0.82f, 0f, -1.28f);
        /// <summary>The middle of the throat: looking here from beside the cup carries the apple into the cone.</summary>
        public static readonly Vector3 CupAim = new Vector3(CupX, ThroatY, CupZ);

        Prop apple;
        PressurePlate button;
        Door flap;
        PropLeash leash;
        Exit exit;
        SignalLamp flapLamp;
        int mouthTicks, rimTicks, peaTicks;
        bool mouthTold, peaPending;

        public Prop Apple => apple;
        public PressurePlate Button => button;
        public Door Flap => flap;
        public PropLeash Leash => leash;
        public Exit Exit => exit;

        public override string Blurb => "The button wants something small. Look up.";

        public override string[] Hints => new[]
        {
            "That apple is very far away. It only looks small enough to hold.",
            // (LEVELS.md says "grab"; the other three levels, and this level's own lines, say "pick up".)
            "Pick up the apple, then look at something close. Your own feet are the closest thing there is.",
            "Stand by the cup, pick the apple up from the shelf, look straight down into the cup, and let go.",
        };

        public override string Environment => "cardboard-box";

        // The preset's floor is the den's floor; the box stands on it on its own cardboard bottom.
        public override float GroundY => -FloorThickness;
        public override float KillY => -30f;

        public override void Build(LevelContext ctx)
        {
            mouthTicks = 0;
            rimTicks = 0;
            peaTicks = 0;
            mouthTold = false;
            peaPending = false;

            Dip dip = ctx.Dip;
            Material cardboard = Materials.Room(RoomSurface.LevelStatic, pattern: PatternSpec.Corrugated);
            Material boards = Materials.Room(RoomSurface.LevelStatic, pattern: PatternSpec.Planks.WithPitch(3f));
            Material wallpaper = Materials.Room(RoomSurface.LevelStatic, pattern: PatternSpec.Stripes.WithPitch(4f));
            // Printing and tape on the cardboard: the dip's other tones, never a second hue.
            Material print = Materials.Room(new RoomRecipe { Name = dip.Name + " Print", Top = dip.Light, Side = dip.Light, Dado = dip.Light });
            Material tape = Materials.Room(new RoomRecipe { Name = dip.Name + " Tape", Top = dip.Mid, Side = dip.Mid, Dado = dip.Mid });

            BuildBox(ctx, cardboard);
            BuildShelf(ctx, boards, wallpaper);

            // The apple: a boulder on the shelf, 11.4 across. It can never be bigger than it starts, and it
            // waits where it is until the first grab.
            apple = ToyCatalog.Add(ctx, ToyId.Apple, AppleOrigin, AppleScale, null, o =>
            {
                o.MinScale = SmallestApple;
                o.MaxScale = BiggestApple;
                o.FrozenUntilGrabbed = true;
            });

            // The cup: a free-standing funnel whose tube ends on the button.
            float capDown = Mathf.Max(0.02f, ThroatRadius * 0.08f) + ButtonTravel;
            Funnel.Build(ctx, new FunnelOptions
            {
                Name = "Cup",
                Axis = new Vector2(CupX, CupZ),
                RimY = RimY,
                MouthRadius = MouthRadius,
                ThroatRadius = ThroatRadius,
                ConeDepth = RimY - ThroatY,
                // The lower half of the tube is a "chamber" of the tube's own radius: that gives it a floor.
                TubeLength = (ThroatY - ButtonY) * 0.5f,
                ChamberHeight = (ThroatY - ButtonY) * 0.5f,
                ChamberRadius = ThroatRadius,
                OuterRadius = CupOuterRadius,
                OuterBaseY = 0f,
                Friction = 0.05f,
                Material = GadgetKit.Body,
            });

            // What passes the throat is what the button counts: an apple stuck in the mouth has its centre
            // over the button too, but it is not on it.
            ctx.OnUpdate(dt =>
            {
                bool fits = Funnel.Passes(apple.Radius, ThroatRadius);
                if (fits != apple.HasTag(FitsTag))
                {
                    if (fits) apple.AddTag(FitsTag);
                    else apple.RemoveTag(FitsTag);
                }
            });

            // The cap stands a travel proud of the tube's floor and sinks all but flush with it when pressed
            // (a hair above, or the two faces would fight for the same depth).
            button = new PressurePlate(ctx, new PressurePlateOptions
            {
                Name = "Button",
                Sensor = Zone.Cylinder(CupX, CupZ, ThroatRadius, ButtonY - capDown + 0.005f, ThroatY + 0.15f),
                Mode = PlateMode.Sum,
                MinMass = ButtonMass,
                AcceptTag = FitsTag,
                Latch = true,
                Travel = ButtonTravel,
            });

            flap = BuildFlap(ctx, cardboard);

            // The flap is the only way out. An apple can be grown under one's own feet (jump, take it in the
            // air, let go at the top of the jump: measured, 1.85 -> 4.57 in six jumps) and ridden over a
            // wall, so until the button is pressed the exit is locked and whoever is outside the box is put
            // back at the start.
            exit = ctx.AddExit(new Vector3(6f, 1.5f, 0f), new Vector3(3f, 3f, 3f)).Lock();
            var keepers = new List<HazardZone>();
            foreach (Zone outside in OutsideTheBox())
                keepers.Add(new HazardZone(ctx, new HazardZoneOptions
                {
                    Name = "Outside The Box",
                    Shape = outside,
                    Delay = 0.5f,
                    OnCaught = () =>
                    {
                        ctx.Game.Player.Respawn();
                        ctx.Say(OutsideLine, 4f);
                    },
                }));

            // Let go while the view had no place for it: see PeaLine. (The event is raised by the grabber's
            // Drop, right after its last try at a placement.)
            ctx.Game.Events.PropDropped += e =>
            {
                if (e.Prop != apple) return;
                peaPending = !apple.Held && !apple.Frozen && apple.Scale < PeaScale && !ctx.Game.Grabber.PlacementValid;
                peaTicks = 0;
            };

            button.OnPressed += prop =>
            {
                flap.Open();
                exit.Unlock();
                foreach (HazardZone keeper in keepers) keeper.Enabled = false;
            };
            ctx.OnUpdate(dt =>
            {
                flapLamp.Set(flap.Signal);
                flapLamp.Pulse(flap.Seconds);
                WatchTheMouth(ctx);
                WatchThePea(ctx);
                TipOffTheRim();
            });

            // No strand, no restart: an apple that leaves the box (thrown over the low wall, let go near the
            // shelf, left on top of a wall) is back on the shelf at full size two seconds later. That goes
            // for the shelf itself too: a small apple up there lies behind the shelf's front edge, out of
            // sight from the box. (Let go again straight after the grab, the apple hangs in front of the
            // shelf - the hold stops where it first touches the shelf's edge - and falls to the den floor.)
            leash = new PropLeash(ctx, new PropLeashOptions
            {
                Name = "Apple Leash",
                Props = new List<Prop> { apple },
                Allowed = new List<Zone>
                {
                    // The inside of the box, and the flap's recess in the +X wall.
                    Zone.MinMax(new Vector3(-HalfX, -FloorThickness, -HalfZ), new Vector3(HalfX, HighWall + 0.5f, HalfZ)),
                    Zone.MinMax(new Vector3(HalfX, -FloorThickness, -DoorHalfWidth), new Vector3(HalfX + WallThickness, DoorHeight, DoorHalfWidth)),
                },
                Grace = 2f,
                Action = LeashAction.Respawn,
            });

            BuildDressing(ctx, print, tape);

            ctx.SetSpawn(SpawnPoint, 0f, SpawnPitch);
        }

        // Everything beside the box, as four slabs of space round its footprint (walls and a capsule's
        // radius included, so standing on a wall is not being outside).
        static IEnumerable<Zone> OutsideTheBox()
        {
            float x = HalfX + WallThickness + 0.4f, z = HalfZ + WallThickness + 0.4f;
            const float far = 150f, low = -5f, high = 150f;
            yield return Zone.MinMax(new Vector3(-far, low, -far), new Vector3(-x, high, far));
            yield return Zone.MinMax(new Vector3(x, low, -far), new Vector3(far, high, far));
            yield return Zone.MinMax(new Vector3(-x, low, -far), new Vector3(x, high, -z));
            yield return Zone.MinMax(new Vector3(-x, low, z), new Vector3(x, high, far));
        }

        // An apple that is too big sits in the cup's mouth. Said once per drop.
        void WatchTheMouth(LevelContext ctx)
        {
            if (apple.Held) mouthTold = false;
            if (button.Pressed || apple.Held || apple.Frozen || apple.Removed)
            {
                mouthTicks = 0;
                return;
            }
            Vector3 centre = apple.Center;
            float dx = centre.x - CupX, dz = centre.z - CupZ;
            // (Slow, not still: the funnel's hard gate carries a ball on the throat's rim by cancelling gravity
            // tick by tick, and a big one trembles at 0.4 and more.)
            bool stuck = !Funnel.Passes(apple.Radius, ThroatRadius) && dx * dx + dz * dz < MouthRadius * MouthRadius &&
                         centre.y > ThroatY && centre.y < RimY + apple.Radius + 0.2f && apple.Velocity.sqrMagnitude < 1f;
            mouthTicks = stuck ? mouthTicks + 1 : 0;
            if (mouthTicks != 30 || mouthTold) return;
            mouthTold = true;
            ctx.Say(TooBigLine, 5f);
        }

        // A pea let go where the view had no place for it (the grabber left it at the last pose that was
        // free), lying still in the box and not on the button. Said once per such drop.
        void WatchThePea(LevelContext ctx)
        {
            if (!peaPending || button.Pressed || apple.Held || apple.Frozen || apple.Removed)
            {
                if (button.Pressed || apple.Held || apple.Frozen || apple.Removed) peaPending = false;
                peaTicks = 0;
                return;
            }
            bool still = apple.Velocity.sqrMagnitude < 0.01f && leash.TimeOut(apple) <= 0f;
            peaTicks = still ? peaTicks + 1 : 0;
            if (peaTicks < 30) return;
            peaPending = false;
            ctx.Say(PeaLine, 6f);
        }

        // A held apple that meets the cup's rim on its way is let go right over it, and now and then it comes
        // to rest there, balanced on the edge (measured: once in sixty drops). The rim tips it into the cup.
        void TipOffTheRim()
        {
            if (apple.Held || apple.Frozen || apple.Removed)
            {
                rimTicks = 0;
                return;
            }
            Vector3 centre = apple.Center;
            float dx = centre.x - CupX, dz = centre.z - CupZ;
            float out2 = Mathf.Sqrt(dx * dx + dz * dz);
            bool balanced = Mathf.Abs(out2 - MouthRadius) < 0.1f && Mathf.Abs(centre.y - RimY - apple.Radius) < 0.05f &&
                            apple.Velocity.sqrMagnitude < 0.01f;
            rimTicks = balanced ? rimTicks + 1 : 0;
            if (rimTicks < 6) return;
            rimTicks = 0;
            apple.Body.WakeUp();
            apple.Body.AddForce(new Vector3(-dx, 0f, -dz) * (0.5f / out2), ForceMode.VelocityChange);
        }

        // ---- Geometry -----------------------------------------------------------------------------------

        static void BuildBox(LevelContext ctx, Material cardboard)
        {
            const float t = WallThickness;
            float outerX = HalfX + t, outerZ = HalfZ + t;

            // The bottom of the box, and the sheet of cardboard outside the flap that the exit stands on.
            ctx.AddStatic(BasicToys.Slab(new Vector3(outerX * 2f, FloorThickness, outerZ * 2f), cardboard), new Vector3(0f, -FloorThickness * 0.5f, 0f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(9f - outerX, FloorThickness, 6f), cardboard), new Vector3((9f + outerX) * 0.5f, -FloorThickness * 0.5f, 0f));

            // Walls: -X and the two Z walls whole, +X in three pieces round the door.
            ctx.AddStatic(BasicToys.Slab(new Vector3(t, HighWall, outerZ * 2f), cardboard), new Vector3(-HalfX - t * 0.5f, HighWall * 0.5f, 0f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(HalfX * 2f, HighWall, t), cardboard), new Vector3(0f, HighWall * 0.5f, -HalfZ - t * 0.5f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(HalfX * 2f, LowWall, t), cardboard), new Vector3(0f, LowWall * 0.5f, HalfZ + t * 0.5f));
            float side = outerZ - DoorHalfWidth;
            ctx.AddStatic(BasicToys.Slab(new Vector3(t, HighWall, side), cardboard), new Vector3(HalfX + t * 0.5f, HighWall * 0.5f, -DoorHalfWidth - side * 0.5f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(t, HighWall, side), cardboard), new Vector3(HalfX + t * 0.5f, HighWall * 0.5f, DoorHalfWidth + side * 0.5f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(t, HighWall - DoorHeight, DoorHalfWidth * 2f), cardboard), new Vector3(HalfX + t * 0.5f, (HighWall + DoorHeight) * 0.5f, 0f));

            // The box's top flaps, folded open: they splay outward from the three tall walls.
            const float flapLength = 2.2f, flapThickness = 0.1f, splay = 35f;
            float reach = flapLength * 0.5f * Mathf.Cos(splay * Mathf.Deg2Rad), rise = flapLength * 0.5f * Mathf.Sin(splay * Mathf.Deg2Rad);
            ctx.AddStatic(BasicToys.Slab(new Vector3(flapLength, flapThickness, outerZ * 2f - 0.2f), cardboard),
                new Vector3(-outerX - reach, HighWall + rise, 0f), Quaternion.Euler(0f, 0f, -splay));
            ctx.AddStatic(BasicToys.Slab(new Vector3(flapLength, flapThickness, outerZ * 2f - 0.2f), cardboard),
                new Vector3(outerX + reach, HighWall + rise, 0f), Quaternion.Euler(0f, 0f, splay));
            ctx.AddStatic(BasicToys.Slab(new Vector3(outerX * 2f - 0.2f, flapThickness, flapLength), cardboard),
                new Vector3(0f, HighWall + rise, -outerZ - reach), Quaternion.Euler(splay, 0f, 0f));
        }

        static void BuildShelf(LevelContext ctx, Material boards, Material wallpaper)
        {
            // A slab of wall standing in the den, and the shelf on it: x -8..8, z 24..36, top at 18.
            const float wallHeight = 44f;
            ctx.AddStatic(BasicToys.Slab(new Vector3(40f, wallHeight + FloorThickness, 1f), wallpaper), new Vector3(0f, (wallHeight - FloorThickness) * 0.5f, 36.5f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(16f, 1f, 12f), boards), new Vector3(0f, ShelfTop - 0.5f, 30f));
            // Two brackets under it: wedges hanging from the board, deepest at the wall.
            for (int sign = -1; sign <= 1; sign += 2)
                ctx.AddStatic(BasicToys.Ramp(9f, 4f, 0.8f, boards), new Vector3(5.5f * sign, ShelfTop - 1f - 2f, 31.5f), Quaternion.Euler(0f, 0f, 180f));
        }

        Door BuildFlap(LevelContext ctx, Material cardboard)
        {
            // The panel in its own space: x is its thickness (inside at -x), y its height, z its width. The
            // two edges that become its near and far end when it lies open are chamfered on the inner side,
            // which is the top then: a 0.02 lip and a 45 degree slope are walked over.
            // It fills its opening exactly: the sun finds any gap and draws it on the floor.
            float hx = FlapThickness * 0.5f, hy = DoorHeight * 0.5f, hz = DoorHalfWidth;
            const float chamfer = 0.06f;
            Mesh mesh = MeshKit.Cached(MeshKit.Key("Level03 Flap", hx, hy, hz, chamfer), () =>
            {
                var section = new[]
                {
                    new Vector2(hx, -hy), new Vector2(hx, hy), new Vector2(-hx + chamfer, hy),
                    new Vector2(-hx, hy - chamfer), new Vector2(-hx, -hy + chamfer), new Vector2(-hx + chamfer, -hy),
                };
                int n = section.Length;
                var corners = new Vector3[n * 2];
                for (int i = 0; i < n; i++)
                {
                    corners[i] = new Vector3(section[i].x, section[i].y, -hz);
                    corners[i + n] = new Vector3(section[i].x, section[i].y, hz);
                }
                var faces = new int[n + 2][];
                faces[0] = new[] { 0, 1, 2, 3, 4, 5 };
                faces[1] = new[] { 6, 7, 8, 9, 10, 11 };
                for (int i = 0; i < n; i++) faces[i + 2] = new[] { i, (i + 1) % n, (i + 1) % n + n, i + n };
                return GadgetKit.ConvexMesh("Level03 Flap", corners, faces);
            });

            var panel = new GameObject("Flap") { layer = Layers.Default };
            MeshCollider hull = panel.AddComponent<MeshCollider>();
            hull.convex = true;
            hull.sharedMesh = mesh;
            GadgetKit.Visual(panel.transform, "Visual", mesh, cardboard);
            // The one signal element: a bar across the inside of the flap, amber while it is shut.
            flapLamp = GadgetKit.Lamp(panel.transform, GadgetKit.BoxMesh(new Vector3(0.03f, 0.12f, 1.6f)), Palette.Amber, new Vector3(-hx - 0.005f, 1.9f - hy, 0f));

            return new Door(ctx, new DoorOptions
            {
                Name = "Flap",
                Panel = panel,
                ClosedPosition = new Vector3(HalfX + WallThickness - hx, hy, 0f),
                ClosedRotation = Quaternion.identity,
                Motion = DoorMotion.Hinge,
                // Hinged on its bottom outer edge; it falls outward and lies flat.
                HingePivot = new Vector3(HalfX + WallThickness, 0f, 0f),
                HingeAxis = Vector3.back,
                HingeAngle = 90f,
                Seconds = 0.8f,
                Ease = DoorEase.EaseOut,
            });
        }

        // Things to look at that nothing lands on: printing, tape and the button's lead. No colliders, and
        // nothing stands more than a few hundredths proud of the surface it is on.
        static void BuildDressing(LevelContext ctx, Material print, Material tape)
        {
            Transform root = ctx.Root;
            const float proud = 0.012f;

            // "This way up", printed on the low wall: two arrows over a bar. They point at the apple.
            Mesh arrow = MeshKit.Cached("Level03 Arrow", () => MeshKit.Extrude(new List<Vector2>
            {
                new Vector2(-0.12f, 0f), new Vector2(0.12f, 0f), new Vector2(0.12f, 1f), new Vector2(0.4f, 1f),
                new Vector2(0f, 1.6f), new Vector2(-0.4f, 1f), new Vector2(-0.12f, 1f),
            }, proud * 2f));
            float wallZ = HalfZ;
            GadgetKit.Visual(root, "Arrow", arrow, print, new Vector3(-2.05f, 1.35f, wallZ));
            GadgetKit.Visual(root, "Arrow", arrow, print, new Vector3(-1.05f, 1.35f, wallZ));
            GadgetKit.Visual(root, "Arrow Bar", GadgetKit.BoxMesh(new Vector3(1.9f, 0.16f, proud * 2f)), print, new Vector3(-1.55f, 1.05f, wallZ));

            // A shipping label on the same wall: a sheet with three printed lines.
            GadgetKit.Visual(root, "Label", GadgetKit.BoxMesh(new Vector3(1.7f, 1.1f, proud * 2f)), print, new Vector3(1.55f, 2.1f, wallZ));
            for (int i = 0; i < 3; i++)
                GadgetKit.Visual(root, "Label Line", GadgetKit.BoxMesh(new Vector3(i == 2 ? 0.8f : 1.3f, 0.1f, proud * 4f)), tape,
                    new Vector3(i == 2 ? 1.3f : 1.55f, 2.4f - i * 0.28f, wallZ));

            // A bar code on the long wall opposite the flap.
            float[] bars = { 0.1f, 0.05f, 0.14f, 0.05f, 0.08f, 0.16f, 0.05f, 0.1f, 0.05f, 0.14f, 0.08f };
            float barZ = -0.2f;
            for (int i = 0; i < bars.Length; i++)
            {
                GadgetKit.Visual(root, "Bar Code", GadgetKit.BoxMesh(new Vector3(proud * 2f, 0.9f, bars[i])), print, new Vector3(-HalfX, 2.2f, barZ - bars[i] * 0.5f));
                barZ -= bars[i] + 0.07f;
            }

            // The taped seam of the box's bottom, and tape round the flap that was cut into the +X wall.
            GadgetKit.Visual(root, "Floor Tape", GadgetKit.BoxMesh(new Vector3(0.7f, proud * 2f, HalfZ * 2f)), tape, Vector3.zero);
            float frame = 0.14f;
            for (int sign = -1; sign <= 1; sign += 2)
                GadgetKit.Visual(root, "Door Tape", GadgetKit.BoxMesh(new Vector3(proud * 2f, DoorHeight + frame, frame)), print,
                    new Vector3(HalfX, (DoorHeight + frame) * 0.5f, sign * (DoorHalfWidth + frame * 0.5f)));
            GadgetKit.Visual(root, "Door Tape", GadgetKit.BoxMesh(new Vector3(proud * 2f, frame, DoorHalfWidth * 2f)), print,
                new Vector3(HalfX, DoorHeight + frame * 0.5f, 0f));

            // The button's lead: out of the cup toward the back of the box, along the floor to the wall beside
            // the flap, up the wall and into the flap's signal bar.
            Material lead = GadgetKit.Body;
            const float w = 0.07f, h = 0.05f, leadZ = -DoorHalfWidth - 0.32f;
            float cupEdge = CupZ - CupOuterRadius + 0.05f;
            GadgetKit.Visual(root, "Lead", GadgetKit.BoxMesh(new Vector3(w, h, cupEdge - leadZ + w * 0.5f)), lead, new Vector3(CupX, h * 0.5f, (cupEdge + leadZ - w * 0.5f) * 0.5f));
            GadgetKit.Visual(root, "Lead", GadgetKit.BoxMesh(new Vector3(HalfX - CupX + w * 0.5f, h, w)), lead, new Vector3((HalfX + CupX - w * 0.5f) * 0.5f, h * 0.5f, leadZ));
            GadgetKit.Visual(root, "Lead", GadgetKit.BoxMesh(new Vector3(h, 1.9f, w)), lead, new Vector3(HalfX - h * 0.5f, 0.95f, leadZ));
            GadgetKit.Visual(root, "Lead", GadgetKit.BoxMesh(new Vector3(h, w, -DoorHalfWidth - leadZ + w)), lead, new Vector3(HalfX - h * 0.5f, 1.9f, (leadZ - DoorHalfWidth) * 0.5f));
        }

        // ---- The solution -------------------------------------------------------------------------------

        public override IEnumerator Solve(Bot bot)
        {
            Game game = bot.Game;
            Player player = bot.Player;

            // One round, normally. Whatever state the apple is in when the bot takes over - a first drop
            // that missed, an apple somebody left as big as the box - each round leaves it nearer the button.
            for (int attempt = 0; attempt < 8 && !button.Pressed; attempt++)
            {
                // Whatever is in hand is let go at the bot's feet first: it is a marble there.
                if (game.Grabber.Held == apple) yield return bot.DropAt(player.Position);
                yield return Settled(bot);
                if (button.Pressed) break;

                if (apple.Frozen)
                {
                    // Beside the cup. From here the apple on the shelf is 38 away and about 17 degrees across.
                    yield return WalkRoundTheCup(bot, StandPoint, 0.15f);
                    yield return bot.Grab(apple);
                    yield return bot.Wait(0.4f);
                    // Down into the cup: the apple arrives at the cone as a marble and rolls onto the button.
                    yield return bot.LookAt(CupAim);
                    yield return bot.Wait(0.4f);
                    yield return bot.DropAt(CupAim);
                }
                else if (apple.Scale > 0.5f)
                {
                    // Too big for the cup, or all but. From far away it looks small, and let go over the
                    // bot's own feet it is small. (An apple that fills the box cannot be walked away from;
                    // taken from where the bot stands it still comes out smaller.)
                    if (apple.Scale < 2.5f || Vector3.Distance(player.Eye, apple.Center) < apple.Radius + 0.2f)
                        yield return AtMost(3f, WalkRoundTheCup(bot, FarthestFrom(apple.Center)));
                    yield return bot.Grab(apple);
                    yield return bot.DropAt(player.Position);
                }
                else
                {
                    // A marble lying about: walk up to it, take it, carry it to the cup.
                    yield return AtMost(4f, WalkRoundTheCup(bot, Beside(apple.Center, player.Position), 0.15f));
                    yield return bot.Grab(apple);
                    yield return WalkRoundTheCup(bot, StandPoint, 0.15f);
                    yield return bot.DropAt(CupAim);
                }
            }
            yield return Settled(bot);

            yield return bot.Until(() => button.Pressed, 3f);
            // The side of the box falls open to daylight.
            yield return bot.LookAt(new Vector3(HalfX, 1.4f, 0f));
            yield return bot.Until(() => flap.IsOpen, 3f);

            // Round the cup and out over the fallen flap.
            yield return WalkRoundTheCup(bot, new Vector3(2.5f, 0f, 0f));
            yield return bot.LookAt(new Vector3(6f, 1.3f, 0f));
            yield return bot.WalkTo(new Vector3(6f, 0f, 0f), 0.5f);
            yield return bot.Until(() => game.LevelCompleted, 3f);
        }

        // The bot walks in straight lines, and the cup stands in the middle of the floor: when the line to
        // the target crosses it, the bot first walks to a point beside the cup, on the side the line already
        // leans to.
        public static IEnumerator WalkRoundTheCup(Bot bot, Vector3 target, float tolerance = 0.3f)
        {
            const float clear = CupOuterRadius + Player.BaseRadius + 0.1f, wide = clear + 0.45f;
            var cup = new Vector2(CupX, CupZ);
            Player player = bot.Player;

            // Standing in the cup: its tube is deeper than a step, so walking does not get out. A jump does.
            for (int hop = 0; hop < 3 && InTheCup(player); hop++)
            {
                var toward = new Vector2(target.x - CupX, target.z - CupZ);
                toward = toward.magnitude > clear ? toward.normalized : Vector2.left;
                Vector3 landing = InReach(new Vector3(CupX + toward.x * (clear + 0.3f), 0f, CupZ + toward.y * (clear + 0.3f)));
                yield return bot.Until(() => player.Grounded, 3f);
                yield return bot.Jump(false);
                for (int tick = 0; tick < 90 && (tick < 5 || !player.Grounded); tick++)
                {
                    bot.WalkTo(landing, 0.05f, 2f).MoveNext();
                    yield return null;
                }
            }

            for (int leg = 0; leg < 3; leg++)
            {
                var from = new Vector2(bot.Player.Position.x, bot.Player.Position.z);
                var to = new Vector2(target.x, target.z);
                Vector2 along = to - from;
                float length = along.magnitude;
                if (length < 1e-3f) break;
                along /= length;
                float ahead = Vector2.Dot(cup - from, along);
                float side = along.x * (cup.y - from.y) - along.y * (cup.x - from.x);
                if (ahead <= 0f || ahead >= length || Mathf.Abs(side) >= clear) break;
                // The cup lies to the left of the line (side > 0): pass it on the right, and the other way round.
                var normal = new Vector2(along.y, -along.x) * (side > 0f ? 1f : -1f);
                Vector3 via = InReach(new Vector3(cup.x + normal.x * wide, 0f, cup.y + normal.y * wide));
                yield return bot.WalkTo(via, 0.25f);
            }
            yield return bot.WalkTo(target, tolerance);
        }

        static bool InTheCup(Player player)
        {
            Vector3 feet = player.Position;
            float dx = feet.x - CupX, dz = feet.z - CupZ;
            return feet.y > 0.03f && feet.y < RimY + 0.2f && dx * dx + dz * dz < (CupOuterRadius + 0.05f) * (CupOuterRadius + 0.05f);
        }

        // The nearest place to a point where the capsule can stand: inside the walls, outside the cup.
        static Vector3 InReach(Vector3 point)
        {
            const float margin = Player.BaseRadius + 0.1f, clear = CupOuterRadius + Player.BaseRadius + 0.1f;
            point.x = Mathf.Clamp(point.x, -HalfX + margin, HalfX - margin);
            point.z = Mathf.Clamp(point.z, -HalfZ + margin, HalfZ - margin);
            point.y = 0f;
            var out2 = new Vector2(point.x - CupX, point.z - CupZ);
            if (out2.magnitude >= clear) return point;
            out2 = out2.sqrMagnitude > 1e-6f ? out2.normalized : Vector2.left;
            return new Vector3(CupX + out2.x * clear, 0f, CupZ + out2.y * clear);
        }

        // Half a step from something lying on the floor, on the side the bot comes from.
        static Vector3 Beside(Vector3 thing, Vector3 comingFrom)
        {
            Vector3 toward = comingFrom - thing;
            toward.y = 0f;
            toward = toward.sqrMagnitude > 1e-4f ? toward.normalized : Vector3.back;
            return InReach(new Vector3(thing.x, 0f, thing.z) + toward * 0.5f);
        }

        // The corner of the box that is farthest from something.
        static Vector3 FarthestFrom(Vector3 thing)
        {
            Vector3 best = SpawnPoint;
            float bestDistance = -1f;
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -HalfX + 0.7f : HalfX - 0.7f, 0f, (i & 2) == 0 ? -HalfZ + 0.7f : HalfZ - 0.7f);
                float distance = new Vector2(corner.x - thing.x, corner.z - thing.z).sqrMagnitude;
                if (distance <= bestDistance) continue;
                bestDistance = distance;
                best = corner;
            }
            return best;
        }

        // Runs a script for so long at most and then lets it be: for a walk that may end against the apple.
        static IEnumerator AtMost(float seconds, IEnumerator script)
        {
            var running = new Stack<IEnumerator>();
            running.Push(script);
            int ticks = Mathf.CeilToInt(seconds / Sim.Dt);
            while (running.Count > 0 && ticks > 0)
            {
                IEnumerator top = running.Peek();
                if (!top.MoveNext())
                {
                    running.Pop();
                    continue;
                }
                if (top.Current is IEnumerator inner)
                {
                    running.Push(inner);
                    continue;
                }
                ticks--;
                yield return null;
            }
        }

        // Waits until the button is pressed, or the apple waits on the shelf, or it has been lying still
        // inside its bounds for half a second. (An apple the funnel's hard gate carries on the throat's rim
        // trembles, and one that has just been let go is no faster than that either.)
        IEnumerator Settled(Bot bot)
        {
            int still = 0;
            return bot.Until(() =>
            {
                if (button.Pressed || (apple.Frozen && !apple.Held)) return true;
                bool slow = !apple.Held && apple.Velocity.sqrMagnitude < 1f && leash.TimeOut(apple) <= 0f;
                still = slow ? still + 1 : 0;
                return still >= 30;
            }, 10f);
        }
    }
}
