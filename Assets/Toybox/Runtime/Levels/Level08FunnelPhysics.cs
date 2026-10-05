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
    /// Level 8 (LEVELS.md): the lid of a marble-run toy standing on the workbench. Three funnels of three
    /// sizes are set into it, each over a weight plate; a gate in the far wall opens when all three plates
    /// are down. Three glass marbles, none the right size: a tiny one on a spool beside the start, a middling
    /// one on the floor - too big for the small hole, too light for the middle one - and a boulder in a
    /// niche above the gate.
    ///
    /// The rule: a marble passes a funnel's throat when it is smaller than the throat, and the plate under
    /// it wants a marble of at least 5/8 of the throat's width - so each hole takes a marble that nearly
    /// fills it. What a marble is when it is let go is how big it looked when it was picked up, times how
    /// far away it is let go: aimed across a funnel it stops against the far side of the cone, and walking
    /// toward the funnel makes it smaller there, walking away bigger. The studs round a funnel's mouth and
    /// the lamp behind it say whether the marble in the hand would do, as big as it is right now - once it is
    /// over the mouth. Until then the dashes on the funnel's far side blink: hold it over here.
    ///
    /// Lid top y = 0, x -14..14, z -12..16; the funnels on the line z = 9; travel +Z. The tray's walls are
    /// drawn 5 high and stand, unseen, up to the sky cap at 14; the gate and the niche are in a tower in the
    /// middle of the far wall, the return port in the left one. The lid and the funnels are sheets, so
    /// there is solid ground under all of them, and a net (the port) under that.
    ///
    /// What the level says, each when it happens: too big (it sits in the mouth), too small (through the
    /// hole, out of the port), stopped short of the hole (held too low, or from too far off), went over the
    /// hole, the hole is taken, picked up from too far off.
    /// (Where this differs from LEVELS.md, and what was measured: tools/out/notes/level08-build.md.)
    /// </summary>
    [Level(8, "funnel-physics", "Funnel Physics", Phase = 2)]
    public sealed class Level08FunnelPhysics : LevelDefinition
    {
        /// <summary>One funnel of the lid, with the plate under it (LEVELS.md, Level 8: the two tables).</summary>
        public sealed class Hole
        {
            public readonly string Name;
            /// <summary>Its axis is at (X, FunnelZ).</summary>
            public readonly float X;
            public readonly float Throat, Mouth, ConeDepth, Tube, Chamber;
            /// <summary>The smallest marble whose weight presses the plate.</summary>
            public readonly float MinScale;
            /// <summary>The gauge's lamp: how high its post stands, and how big it is.</summary>
            public readonly float PostHeight, LampRadius;

            public Hole(string name, float x, float throat, float mouth, float coneDepth, float tube, float chamber, float minScale, float postHeight, float lampRadius)
            {
                Name = name;
                X = x;
                Throat = throat;
                Mouth = mouth;
                ConeDepth = coneDepth;
                Tube = tube;
                Chamber = chamber;
                MinScale = minScale;
                PostHeight = postHeight;
                LampRadius = lampRadius;
            }

            public Vector3 Axis => new Vector3(X, 0f, FunnelZ);
            public float ThroatY => -ConeDepth;
            public float TubeY => -ConeDepth - Tube;
            public float FloorY => -ConeDepth - Tube - Chamber;
            /// <summary>A marble passes the throat when its scale is below this (its radius below the throat's).</summary>
            public float PassBelow => Throat * 2f;
            /// <summary>What the plate wants: the weight of a marble of <see cref="MinScale"/>, less a hair.</summary>
            public float MinMass => MarbleMass(MinScale) * 0.999f;
            /// <summary>Would a marble of this scale pass the throat and press the plate?</summary>
            public bool Takes(float scale) => scale < PassBelow && MarbleMass(scale) >= MinMass;
            /// <summary>Height of the cone's surface at a distance from the axis (between throat and mouth).</summary>
            public float ConeY(float radius) => -ConeDepth * Mathf.Clamp01((Mouth - radius) / (Mouth - Throat));
            public Vector3 LampPosition => new Vector3(X, PostHeight + LampRadius * 0.55f, FunnelZ + Mouth + PostGap);
        }

        // ---- The layout (LEVELS.md, Level 8). Lid top y = 0, travel +Z. -------------------------------------
        public const float HalfX = 14f, NearZ = -12f, FarZ = 16f, WallTop = 14f, WallThickness = 1f;
        /// <summary>How high the tray's walls are drawn (they stand up to WallTop), and the top of the tower in the far wall.</summary>
        public const float DrawnWall = 5f, TowerTop = 13f;
        /// <summary>The bench top the toy stands on, under the deepest chamber.</summary>
        public const float BenchY = -6.6f;
        public const float FunnelZ = 9f;
        public const int FunnelSegments = 32;
        const float PostGap = 0.6f, PostWidth = 0.3f;

        public static readonly Hole[] Holes =
        {
            new Hole("S", -8f, 0.40f, 1.0f, 0.42f, 1f, 1.2f, 0.50f, 0.7f, 0.13f),
            new Hole("M", -2f, 0.80f, 1.9f, 0.77f, 1f, 2.0f, 1.00f, 1.5f, 0.2f),
            new Hole("L", 6.5f, 1.60f, 3.6f, 1.40f, 1f, 3.6f, 2.00f, 3.4f, 0.32f),
        };

        /// <summary>The gate in the far wall, the tunnel behind it and the exit in the tunnel.</summary>
        public const float GateX0 = 0f, GateX1 = 3f, GateHeight = 3f, GateZ = FarZ + 0.5f, GateThickness = 0.3f, TunnelEnd = 22f;
        public static readonly Vector3 ExitCentre = new Vector3(1.5f, 1.5f, 19.5f);
        /// <summary>The niche above the gate.</summary>
        public const float NicheX0 = -1.5f, NicheX1 = 4.5f, NicheY0 = 6f, NicheY1 = 12f, NicheBack = 21.5f;
        /// <summary>The return port: an alcove in the -X wall.</summary>
        public const float PortZ = 0f, PortHalfWidth = 1.1f, PortHeight = 2.3f, PortBack = -HalfX - 2.5f;
        public static readonly Vector3 PortMouth = new Vector3(-HalfX - 1.3f, 1.1f, PortZ);
        /// <summary>(LEVELS.md says 3; with the marbles' spin damped that left them lying in the port's mouth.)</summary>
        public static readonly Vector3 PortVelocity = new Vector3(5f, 0f, 0f);
        public static readonly Vector3 PortPlayer = new Vector3(-12.5f, 0f, PortZ);
        /// <summary>A marble too big to come out of the alcove appears in front of it instead.</summary>
        public const float PortBiggest = 0.95f;

        // ---- The marbles ------------------------------------------------------------------------------------
        public const string MarbleTag = "marble";
        /// <summary>
        /// The clamps. The catalog's marble stops at 0.3; the little one starts at 0.25, so the floor is
        /// lower here. Under the open sky cap a marble stops growing at 6.
        /// </summary>
        public const float SmallestMarble = 0.2f, BiggestMarble = 6f;
        /// <summary>
        /// (LEVELS.md has the middling one at 1.2. That fits the middle hole as it lies, and a marble that
        /// light is rolled along by walking into it: eleven seconds of shoving put it on its plate with the
        /// perspective never used. At 0.9 no marble fits any hole as it lies, as the blurb says.)
        /// </summary>
        public const float PeeweeScale = 0.25f, AggieScale = 0.9f, ShooterScale = 5f;
        /// <summary>The spool the little one stands on: a step ahead and to the left of the start.</summary>
        public static readonly Vector3 SpoolBase = new Vector3(-0.95f, 0f, -8.2f);
        public const float SpoolRadius = 0.4f, SpoolHeight = 1.1f;
        public static readonly Vector3 PeeweeOrigin = SpoolBase + Vector3.up * (SpoolHeight + PeeweeScale * 0.5f + 0.005f);
        /// <summary>Four steps ahead and to the right of the start: picked up from there it looks a quarter of its distance across.</summary>
        public static readonly Vector3 AggieOrigin = new Vector3(2f, AggieScale * 0.5f, -6.4f);
        public static readonly Vector3 ShooterOrigin = new Vector3(1.5f, NicheY0 + ShooterScale * 0.5f, 18.6f);

        public static readonly Vector3 SpawnPoint = new Vector3(0f, 0f, -9.5f);
        public const float SpawnYaw = 0f, SpawnPitch = 3f;

        // ---- What the level says ------------------------------------------------------------------------------
        /// <summary>A marble that does not pass the throat has come to rest in a funnel's mouth.</summary>
        public const string TooBigLine = "Too big for this hole. Pick it up from farther off, or let go from closer.";
        /// <summary>A marble fell through and the plate under it did not go down.</summary>
        public const string TooLightLine = "Too small to press the plate. It rolls out of the port. Pick it up from close, or let go from farther off.";
        /// <summary>A marble fell into a funnel that already has one.</summary>
        public const string BusyLine = "That hole has its marble already. This one rolls back out of the port in the wall.";
        /// <summary>A marble came to rest in the mouth of a funnel that already has one (too big for it, or lying on the one it has).</summary>
        public const string TakenLine = "That hole has its marble already. Pick this one up and try another hole.";
        /// <summary>A marble held over the near half of a funnel came to rest on the lid in front of it.</summary>
        public const string ShortLine = "It stopped short, on the lid in front of the hole. Hold it over the far side of the funnel and let go.";
        /// <summary>A marble held over the far half of a funnel still came to rest on the lid in front of it: from that far off its underside meets the lid first.</summary>
        public const string CloserLine = "It met the lid before it got to the hole. Go closer and hold it over the far side of the funnel.";
        /// <summary>A marble held too high passed low over a funnel and came to rest on the lid behind it.</summary>
        public const string OverLine = "It went over the hole and came down behind it. Look lower: hold it over the far side of the funnel.";
        /// <summary>Said on a pick-up from far off. The words are Level 1's, on purpose.</summary>
        public const string FarLine = "It only ever gets as big as it looks. Pick it up from closer.";
        /// <summary>Apparent size (scale / pick-up distance) under which a marble fits no hole from anywhere on the lid.</summary>
        public const float FarRatio = 0.06f;

        /// <summary>"This does not fit": the red that blinks three times a second (the gadget presenters' own).</summary>
        public static readonly Signal Wrong = new Signal("Wrong", Palette.Hex("#FF4A32"), 2.4f, 0.35f, 3f);
        /// <summary>"Hold it over here": the studs' amber, twice as quick, on the dashes of a funnel's far side.</summary>
        public static readonly Signal Here = new Signal("Here", Palette.Amber.Color, 2.4f, 0.5f, 2f);

        // ---- The intended solution ----------------------------------------------------------------------------
        /// <summary>Straight ahead of the start, past the spool: from here every walk is a straight line.</summary>
        public static readonly Vector3 PastTheSpool = new Vector3(0f, 0f, -7f);
        // Measured (tools/out/notes/level08-probe-*.txt): each stand is the middle of the distances that work
        // for that pick-up, each aim the middle of the pitches that work from there - a point over the far
        // side of the funnel. A marble held over the middle of a hole stops on the lid in front of it.
        /// <summary>3.4 back from the small funnel: the little one, picked up from the start, fits from 2.4 to 4.4.</summary>
        public static readonly Vector3 StandS = new Vector3(-8f, 0f, 5.6f);
        public static readonly Vector3 AimS = new Vector3(-8f, 0f, 9.6f);
        /// <summary>3.7 from the middling one, on its way to the middle funnel: it looks a quarter of its distance across.</summary>
        public static readonly Vector3 PickAggie = new Vector3(0.9f, 0f, -3.15f);
        /// <summary>3.8 back from the middle funnel (works from 2.3 to 5.3).</summary>
        public static readonly Vector3 StandM = new Vector3(-2f, 0f, 5.2f);
        public static readonly Vector3 AimM = new Vector3(-2f, 0f, 10.1f);
        /// <summary>23 from the boulder in the niche.</summary>
        public static readonly Vector3 PickShooter = new Vector3(6.5f, 0f, -3f);
        /// <summary>8.8 back from the big funnel (works from 7.5 to 10.5).</summary>
        public static readonly Vector3 StandL = new Vector3(6.5f, 0f, 0.2f);
        public static readonly Vector3 AimL = new Vector3(6.5f, 0.4f, 12.2f);
        /// <summary>Between the middle and the big funnel, on the way to the gate.</summary>
        public static readonly Vector3 BetweenMAndL = new Vector3(1.4f, 0f, FunnelZ);

        static readonly Color[] MarbleColors = { Palette.Cherry, Palette.Lemon, Palette.Grape };
        static readonly string[] MarbleNames = { "Peewee", "Aggie", "Shooter" };

        Prop[] marbles;
        FunnelResult[] funnels;
        PressurePlate[] plates;
        FitGauge[] gauges;
        HazardZone[] hazards;
        SignalLamp[] studs, landings;
        GameObject[] latchedLamps, landingPaint;
        bool[] beckons;
        MeshRenderer[] shafts;
        ReturnPort port;
        PropLeash jamLeash, nicheLeash;
        Door gate;
        SignalLamp gateLamp;
        Exit exit;
        // Per marble: told off since it was last picked up; the funnel over whose mouth it was seen when it
        // was let go (and whether the view pointed past that funnel's middle), or that it passed low over;
        // which way the view pointed then; ticks at rest where it should not be.
        bool[] told, aimedBeyond;
        int[] aimed, passedOver, restTicks, lostTicks, parkedTicks;
        Vector2[] letGoAlong;

        public Prop Peewee => marbles[0];
        public Prop Aggie => marbles[1];
        public Prop Shooter => marbles[2];
        public IReadOnlyList<Prop> Marbles => marbles;
        public IReadOnlyList<FunnelResult> Funnels => funnels;
        public IReadOnlyList<PressurePlate> Plates => plates;
        public IReadOnlyList<FitGauge> Gauges => gauges;
        public IReadOnlyList<HazardZone> Hazards => hazards;
        /// <summary>The ring of studs round each mouth: amber while it waits, red or green for the marble in the hand, green for good once latched.</summary>
        public IReadOnlyList<SignalLamp> Studs => studs;
        public ReturnPort Port => port;
        public PropLeash JamLeash => jamLeash;
        public PropLeash NicheLeash => nicheLeash;
        public Door Gate => gate;
        public Exit Exit => exit;
        /// <summary>
        /// Do the dashes on a funnel's far side light up right now? They do while a marble is in the hand, its
        /// picture lies over that funnel and its middle is not over the mouth: let go there it would lie on the
        /// lid, and the lamps (which read a marble over the mouth) have nothing to say about it.
        /// </summary>
        public bool Beckons(int hole) => beckons[hole];
        /// <summary>How many times a marble that got under the lid outside a funnel was given back through the port.</summary>
        public int Rescues { get; private set; }
        /// <summary>How many plates are down.</summary>
        public int Latched
        {
            get
            {
                int count = 0;
                for (int i = 0; i < plates.Length; i++)
                    if (plates[i].Pressed) count++;
                return count;
            }
        }

        public override string Blurb => "Three holes. Three marbles. None of them the right size.";

        public override string[] Hints => new[]
        {
            "A marble has to nearly fill its hole: small enough to fall through, heavy enough to press the plate underneath. The lamps round a funnel turn green when the marble you hold over it would do.",
            "The marble never changes size on your screen, but the hole does. Walk toward a funnel and the marble shrinks against it; back away and it grows. Hold it over the far side of the hole.",
            // (LEVELS.md counts steps from the funnels' axes. Nobody can pace a step, and the hint panel holds
            // about 210 letters at full size: the lamps do the measuring. They only turn green for a marble
            // that goes in and presses the plate when it is let go; a test solves the level by them alone.)
            "Pick up red and yellow where you start, purple from the middle of the room. Hold each over the far side of its hole - small, middle, big - and step back or forward until the lamps turn green. Then let go.",
        };

        public override string Environment => "pegboard-workbench";

        // The toy stands on the bench; the chamber under the big funnel reaches down to 0.6 above it.
        public override float GroundY => BenchY;
        public override float KillY => -30f;

        /// <summary>Mass of a marble of a scale (the catalog's: 1.309 at scale 1).</summary>
        public static float MarbleMass(float scale) => ToyCatalog.Get(ToyId.Marble).Mass * scale * scale * scale;

        public override void Build(LevelContext ctx)
        {
            int count = Holes.Length;
            marbles = new Prop[3];
            funnels = new FunnelResult[count];
            plates = new PressurePlate[count];
            gauges = new FitGauge[count];
            hazards = new HazardZone[count];
            studs = new SignalLamp[count];
            landings = new SignalLamp[count];
            landingPaint = new GameObject[count];
            beckons = new bool[count];
            latchedLamps = new GameObject[count];
            shafts = new MeshRenderer[count];
            port = null;
            jamLeash = null;
            nicheLeash = null;
            gate = null;
            gateLamp = null;
            exit = null;
            told = new bool[3];
            aimedBeyond = new bool[3];
            letGoAlong = new Vector2[3];
            aimed = new[] { -1, -1, -1 };
            passedOver = new[] { -1, -1, -1 };
            restTicks = new int[3];
            lostTicks = new int[3];
            parkedTicks = new int[3];
            Rescues = 0;

            Dip dip = ctx.Dip;
            // The lid is a board of big squares in the dip's two lighter tones (the checker's other tone is the
            // recipe's side colour; the lid has no sides to show).
            Material lid = Materials.Room(RoomRecipe.For(RoomSurface.LevelStatic, dip, PatternSpec.Tiles.WithPitch(4f)).With(r =>
            {
                r.Name = dip.Name + " Lid";
                r.Side = Palette.Mix(dip.Light, dip.Mid, 0.8f);
            }));
            Material wall = Materials.Room(RoomSurface.LevelStatic, dip, PatternSpec.Stripes.WithPitch(2f));
            Material paint = Materials.Room(RoomRecipe.Solid(Palette.Paper));

            BuildLid(ctx, lid);
            BuildWalls(ctx, wall, lid);
            SkyCap.Add(ctx, -HalfX - WallThickness, HalfX + WallThickness, NearZ - WallThickness, FarZ + WallThickness, WallTop);

            // The marbles, in the order the level means them to be met: Prop.Id 0, 1, 2.
            SetPiece(ctx, ToyFactory.ThreadSpool(SpoolRadius, SpoolHeight, grabbable: false), SpoolBase + Vector3.up * (SpoolHeight * 0.5f), Quaternion.identity);
            marbles[0] = AddMarble(ctx, 0, PeeweeOrigin, PeeweeScale, true);
            marbles[1] = AddMarble(ctx, 1, AggieOrigin, AggieScale, false);
            marbles[2] = AddMarble(ctx, 2, ShooterOrigin, ShooterScale, true);

            // What gives things back: marbles that fell through the wrong hole, and whoever fell in after them.
            port = new ReturnPort(ctx, new ReturnPortOptions
            {
                Name = "Port", Mouth = PortMouth, EjectVelocity = PortVelocity, PlayerPosition = PortPlayer, PlayerYaw = 90f,
            });
            port.Ejected += prop =>
            {
                // Too big for the alcove: it comes out in front of it.
                if (prop.Radius <= PortBiggest) return;
                float radius = prop.Radius;
                prop.SetPose(new Vector3(-HalfX + radius + 0.15f, radius + 0.1f, PortZ) - prop.Rotation * (prop.LocalCenter * prop.Scale), prop.Rotation);
                if (!prop.Body.isKinematic) prop.Body.linearVelocity = PortVelocity;
            };

            for (int i = 0; i < count; i++) BuildFunnel(ctx, i, paint);

            // The jam rule: a marble at rest below a throat that no plate holds is given back.
            var below = new List<Zone>();
            for (int i = 0; i < count; i++)
                below.Add(Zone.Cylinder(Holes[i].X, FunnelZ, Holes[i].Throat + 0.3f, Holes[i].FloorY - 1f, Holes[i].ThroatY - 0.05f));
            jamLeash = new PropLeash(ctx, new PropLeashOptions
            {
                Name = "Jam", Props = marbles, Forbidden = below, RestSpeed = 0.3f, Grace = 1.5f, Action = LeashAction.Eject, Port = port,
            });
            // No strand: a marble left in the niche (thrown up there, or let go again the moment it was taken)
            // is out of reach and may be out of sight. It goes back to where it started, as it started.
            nicheLeash = new PropLeash(ctx, new PropLeashOptions
            {
                Name = "Niche", Props = marbles,
                Forbidden = new[] { Zone.MinMax(new Vector3(NicheX0 - 0.5f, NicheY0 - 0.5f, FarZ), new Vector3(NicheX1 + 0.5f, NicheY1 + 1f, NicheBack + 1f)) },
                Grace = 2f, Action = LeashAction.Respawn,
            });

            gate = BuildGate(ctx);
            exit = ctx.AddExit(ExitCentre, new Vector3(GateX1 - GateX0, GateHeight, 3f)).Lock();

            ctx.Game.Events.PropGrabbed += e =>
            {
                int m = IndexOf(e.Prop);
                if (m < 0) return;
                told[m] = false;
                aimed[m] = -1;
                passedOver[m] = -1;
                restTicks[m] = 0;
                if (e.OldScale / Mathf.Max(0.01f, e.GrabDistance) < FarRatio) ctx.Say(FarLine, 6f);
            };
            ctx.Game.Events.PropDropped += e =>
            {
                int m = IndexOf(e.Prop);
                if (m < 0 || e.Prop.Removed) return;
                Player player = ctx.Game.Player;
                float ratio = e.OldScale / Mathf.Max(0.01f, e.GrabDistance);
                Vector3 forward = player.Forward;
                var flat = new Vector2(forward.x, forward.z);
                letGoAlong[m] = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector2.zero;
                aimed[m] = SeenOver(player.Eye, forward, ratio, out aimedBeyond[m]);
                passedOver[m] = aimed[m] >= 0 ? -1 : PassedOver(ctx.Game, ratio, e.Prop.Center);
                restTicks[m] = 0;
            };
            ctx.OnUpdate(dt => Watch(ctx));

            BuildDressing(ctx, paint);

            ctx.SetSpawn(SpawnPoint, SpawnYaw, SpawnPitch);
            ctx.AddCheckpoint(Volume.Box(6f, 4f, 4f), SpawnPoint + Vector3.up * 2f, SpawnPoint, SpawnYaw, "Spawn");
        }

        Prop AddMarble(LevelContext ctx, int index, Vector3 origin, float scale, bool frozen) =>
            ToyCatalog.Add(ctx, ToyId.Marble, origin, scale, MarbleColors[index], o =>
            {
                o.Name = MarbleNames[index];
                o.Tags = new[] { MarbleTag };
                o.MinScale = SmallestMarble;
                o.MaxScale = BiggestMarble;
                o.FrozenUntilGrabbed = frozen;
                // Glass on a hard lid rolls a long way; this much brings a marble to rest within a few steps.
                o.AngularDamping = MarbleSpin;
            });

        /// <summary>
        /// Angular damping of the marbles (the catalog's toys have 0.05). Measured: with 0.6 a marble that
        /// left the port at 3 was still creeping across the lid, eleven units on, fourteen seconds later.
        /// </summary>
        public const float MarbleSpin = 2.5f;

        int IndexOf(Prop prop)
        {
            for (int m = 0; m < marbles.Length; m++)
                if (marbles[m] == prop) return m;
            return -1;
        }

        // ---- Geometry --------------------------------------------------------------------------------------

        // A box of the level. Where its shadow would fall on nothing anybody sees (a floor over the bench, a
        // piece of wall under the lid or in front of another one) it casts none: shadow draws are budgeted.
        static void Slab(LevelContext ctx, Material material, float x0, float x1, float y0, float y1, float z0, float z1, bool shadow = true)
        {
            GameObject slab = ctx.AddStatic(BasicToys.Slab(new Vector3(x1 - x0, y1 - y0, z1 - z0), material), new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f));
            if (shadow) return;
            foreach (MeshRenderer renderer in slab.GetComponentsInChildren<MeshRenderer>()) NoShadow(renderer);
        }

        // The lid: one sheet with the three mouths cut out of it, corner for corner the funnels' rims.
        static void BuildLid(LevelContext ctx, Material material)
        {
            Mesh mesh = MeshKit.Cached("Level08|Lid", () =>
            {
                var holes = new List<Vector2>();
                foreach (Hole hole in Holes) holes.Add(new Vector2(hole.X, hole.Mouth));
                return Level08Shapes.Lid(-HalfX, HalfX, NearZ, FarZ, FunnelZ, holes, FunnelSegments);
            });
            var root = new GameObject("Lid") { layer = Layers.Default };
            MeshCollider collider = root.AddComponent<MeshCollider>();
            collider.convex = false;
            collider.sharedMesh = mesh;
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterial = material;
            ctx.AddStatic(root, Vector3.zero);

            // The sheet has no thickness, and a small marble that comes down fast goes through a sheet
            // (measured: marbles under 0.35 through a funnel's floor at 11 units a second and more, continuous
            // collision detection or not). So there is solid ground right under it wherever there is no funnel:
            // unseen boxes, a hair below the sheet so that nothing ever rests on two floors at once.
            const float top = -0.002f, bottom = -0.6f;
            float widest = 0f;
            foreach (Hole hole in Holes) widest = Mathf.Max(widest, MouthSquare(hole));
            float z0 = FunnelZ - widest, z1 = FunnelZ + widest;
            Unseen(ctx, -HalfX, HalfX, bottom, top, NearZ, z0);
            Unseen(ctx, -HalfX, HalfX, bottom, top, z1, FarZ);
            float x = -HalfX;
            foreach (Hole hole in Holes)
            {
                float half = MouthSquare(hole);
                Unseen(ctx, x, hole.X - half, bottom, top, z0, z1);
                if (half < widest - 0.01f)
                {
                    Unseen(ctx, hole.X - half, hole.X + half, bottom, top, z0, FunnelZ - half);
                    Unseen(ctx, hole.X - half, hole.X + half, bottom, top, FunnelZ + half, z1);
                }
                x = hole.X + half;
            }
            Unseen(ctx, x, HalfX, bottom, top, z0, z1);
        }

        // Half the side of the square round a funnel's mouth that the ground under the lid leaves free.
        static float MouthSquare(Hole hole) => hole.Mouth / Mathf.Cos(Mathf.PI / FunnelSegments) + 0.02f;

        // A wall nobody sees: it stops a held marble and the player like any other.
        static void Unseen(LevelContext ctx, float x0, float x1, float y0, float y1, float z0, float z1)
        {
            var unseen = new GameObject("Unseen Wall") { layer = Layers.Default };
            unseen.AddComponent<BoxCollider>().size = new Vector3(x1 - x0, y1 - y0, z1 - z0);
            ctx.AddStatic(unseen, new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f));
        }

        // The tray's walls, the port's alcove in the -X wall, and in the middle of the +Z wall the tower with
        // the tunnel (behind the gate) and the niche above it. The walls are drawn five high - the sun
        // reaches the whole lid and the workbench shows over them - and stand, unseen, up to the sky cap.
        static void BuildWalls(LevelContext ctx, Material wall, Material floor)
        {
            const float t = WallThickness, low = BenchY, drawn = DrawnWall, top = WallTop;
            float outX = HalfX + t;
            Material ink = GadgetKit.Body;

            // -Z and +X: whole.
            Slab(ctx, wall, -outX, outX, low, drawn, NearZ - t, NearZ);
            Unseen(ctx, -outX, outX, drawn, top, NearZ - t, NearZ);
            Slab(ctx, wall, HalfX, outX, low, drawn, NearZ, FarZ);
            Unseen(ctx, HalfX, outX, drawn, top, NearZ, FarZ);

            // -X, round the port.
            Slab(ctx, wall, -outX, -HalfX, low, drawn, NearZ, PortZ - PortHalfWidth);
            Slab(ctx, wall, -outX, -HalfX, low, drawn, PortZ + PortHalfWidth, FarZ);
            Slab(ctx, wall, -outX, -HalfX, PortHeight, drawn, PortZ - PortHalfWidth, PortZ + PortHalfWidth);
            Slab(ctx, wall, -outX, -HalfX, low, -0.5f, PortZ - PortHalfWidth, PortZ + PortHalfWidth, false);
            Unseen(ctx, -outX, -HalfX, drawn, top, NearZ, FarZ);
            // The alcove: an Ink box behind the opening, its floor level with the lid.
            Slab(ctx, ink, PortBack, -HalfX, -0.5f, 0f, PortZ - PortHalfWidth, PortZ + PortHalfWidth, false);
            Slab(ctx, ink, PortBack, -outX, -0.5f, PortHeight + 0.5f, PortZ - PortHalfWidth - 0.5f, PortZ - PortHalfWidth);
            Slab(ctx, ink, PortBack, -outX, -0.5f, PortHeight + 0.5f, PortZ + PortHalfWidth, PortZ + PortHalfWidth + 0.5f);
            Slab(ctx, ink, PortBack, -outX, PortHeight, PortHeight + 0.5f, PortZ - PortHalfWidth, PortZ + PortHalfWidth);
            Slab(ctx, ink, PortBack - 0.5f, PortBack, -0.5f, PortHeight + 0.5f, PortZ - PortHalfWidth - 0.5f, PortZ + PortHalfWidth + 0.5f);

            // +Z: low on both sides of the tower.
            float z0 = FarZ, z1 = FarZ + t, towerX0 = NicheX0 - 1f, towerX1 = NicheX1 + 1f;
            Slab(ctx, wall, -outX, towerX0, low, drawn, z0, z1);
            Slab(ctx, wall, towerX1, outX, low, drawn, z0, z1);
            Unseen(ctx, -outX, towerX0, drawn, top, z0, z1);
            Unseen(ctx, towerX1, outX, drawn, top, z0, z1);
            Unseen(ctx, towerX0, towerX1, TowerTop, top, z0, z1);
            // The tower's front, round the gate and the niche.
            Slab(ctx, wall, towerX0, NicheX0, low, TowerTop, z0, z1, false);
            Slab(ctx, wall, NicheX1, towerX1, low, TowerTop, z0, z1, false);
            Slab(ctx, wall, NicheX0, GateX0, low, NicheY0, z0, z1);
            Slab(ctx, wall, GateX1, NicheX1, low, NicheY0, z0, z1);
            Slab(ctx, wall, GateX0, GateX1, GateHeight, NicheY0, z0, z1);
            Slab(ctx, wall, GateX0, GateX1, low, -0.5f, z0, z1, false);
            Slab(ctx, wall, NicheX0, NicheX1, NicheY1, TowerTop, z0, z1);

            // Behind it: the tunnel's floor, walls and end; the block between tunnel and niche; the niche.
            float back = TunnelEnd + 1f;
            Slab(ctx, floor, GateX0, GateX1, -0.5f, 0f, z0, TunnelEnd, false);
            Slab(ctx, wall, GateX0 - 1f, GateX0, -0.5f, GateHeight, z1, TunnelEnd);
            Slab(ctx, wall, GateX1, GateX1 + 1f, -0.5f, GateHeight, z1, TunnelEnd);
            Slab(ctx, wall, GateX0 - 1f, GateX1 + 1f, -0.5f, GateHeight, TunnelEnd, back);
            Slab(ctx, wall, towerX0, towerX1, GateHeight, NicheY0, z1, back);
            Slab(ctx, wall, towerX0, NicheX0, NicheY0, NicheY1, z1, back);
            Slab(ctx, wall, NicheX1, towerX1, NicheY0, NicheY1, z1, back);
            Slab(ctx, wall, NicheX0, NicheX1, NicheY0, NicheY1, NicheBack, back);
            Slab(ctx, wall, towerX0, towerX1, NicheY1, TowerTop, z1, back);
        }

        // A toy that is part of the set (a pedestal, a block in a corner): its body casts a shadow, the paper
        // labels, pips and thread on it do not need one of their own.
        static GameObject SetPiece(LevelContext ctx, GameObject toy, Vector3 position, Quaternion rotation)
        {
            foreach (MeshRenderer renderer in toy.GetComponentsInChildren<MeshRenderer>())
                if (renderer.gameObject.name != "Visual") NoShadow(renderer);
            return ctx.AddStatic(toy, position, rotation);
        }

        // Paint, studs, trim and light lie on what they are drawn on: they cast no shadow of their own.
        static MeshRenderer NoShadow(MeshRenderer renderer)
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return renderer;
        }

        // One funnel: the Ink cone with its tube and chamber, the plate on the chamber's floor, the hazard
        // for whoever falls in, the gauge with its lamp on a post behind the mouth, the studs and the paint.
        void BuildFunnel(LevelContext ctx, int i, Material paint)
        {
            Hole hole = Holes[i];
            funnels[i] = Funnel.Build(ctx, new FunnelOptions
            {
                Name = hole.Name,
                Axis = new Vector2(hole.X, FunnelZ),
                RimY = 0f,
                MouthRadius = hole.Mouth,
                ThroatRadius = hole.Throat,
                ConeDepth = hole.ConeDepth,
                TubeLength = hole.Tube,
                ChamberHeight = hole.Chamber,
                // The chamber is the tube going on down to the plate: a marble that has passed the throat
                // lands on the plate and stays there (in a wide chamber the big one rolled about for four seconds).
                ChamberRadius = hole.Throat,
                Segments = FunnelSegments,
                Friction = 0.05f,
                Material = GadgetKit.Body,
            });

            // The chamber's floor is a sheet too: a block under it holds what the sheet lets through.
            float under = hole.Throat / Mathf.Cos(Mathf.PI / FunnelSegments) + 0.1f;
            Unseen(ctx, hole.X - under, hole.X + under, Mathf.Max(BenchY, hole.FloorY - 1f), hole.FloorY, FunnelZ - under, FunnelZ + under);

            // The plate: its cap stands a travel proud of the chamber's floor and sinks flush with it. It
            // weighs whatever lies below the throat: a marble held up by the throat is over it, not on it.
            const float travel = 0.06f;
            float plateRadius = hole.Throat * 0.85f;
            float capDown = Mathf.Max(0.02f, plateRadius * 0.08f) + travel;
            plates[i] = new PressurePlate(ctx, new PressurePlateOptions
            {
                Name = "Plate " + hole.Name,
                Sensor = Zone.Cylinder(hole.X, FunnelZ, hole.Throat + 0.05f, hole.FloorY - capDown + 0.005f, hole.ThroatY - 0.05f),
                Mode = PlateMode.Heaviest,
                MinMass = hole.MinMass,
                AcceptTag = MarbleTag,
                Settle = 0.3f,
                Latch = true,
                LockProp = true,
                VisualRadius = plateRadius,
                Travel = travel,
            });
            int index = i;
            plates[i].OnPressed += prop => OnLatched(index, prop);
            plates[i].OnRejected += (prop, load) =>
            {
                port.Eject(prop, 0.6f);
                ctx.Say(plates[index].Pressed ? BusyLine : TooLightLine, 6f);
                int m = IndexOf(prop);
                if (m >= 0) told[m] = true;
            };

            hazards[i] = new HazardZone(ctx, new HazardZoneOptions
            {
                Name = "Chamber " + hole.Name,
                Shape = Zone.Cylinder(hole.X, FunnelZ, hole.Throat + 0.3f, hole.FloorY - 1f, hole.ThroatY - 0.25f),
                Delay = 0.1f,
                OnCaught = () => port.Eject(ctx.Game.Player),
            });

            // The gauge reads the marble in the hand while its middle is over the mouth - where, let go, it
            // comes down into the cone. Not a hand's breadth outside it: a marble that the lid stopped in
            // front of the hole, or that went over it and stopped behind, lies on the lid when it is let go,
            // whatever its size, and lamps that had turned green for it would have lied (measured: 11 of 296
            // let-gos, tools/out/notes/level08-review-probe-truth.txt). A hair inside the throat's size at
            // the top: what it calls good does go through.
            Vector3 lamp = hole.LampPosition;
            gauges[i] = new FitGauge(ctx, new FitGaugeOptions
            {
                Name = "Gauge " + hole.Name,
                MinScale = hole.MinScale,
                MaxScale = hole.PassBelow * 0.99f,
                Near = Zone.Cylinder(hole.X, FunnelZ, hole.Mouth, hole.FloorY, BiggestMarble * 0.5f + 1f),
                Tag = MarbleTag,
                Lamp = lamp,
                LampRadius = hole.LampRadius,
            });

            // The lamp's post (it stops a held marble, as anything one cannot see through must), and the lamp
            // that takes the gauge's place once the plate is down: green for good.
            var post = new GameObject("Gauge Post " + hole.Name) { layer = Layers.Default };
            float postTop = hole.PostHeight;
            var box = post.AddComponent<BoxCollider>();
            box.size = new Vector3(PostWidth, postTop + hole.LampRadius * 1.6f, PostWidth);
            box.center = new Vector3(0f, box.size.y * 0.5f, 0f);
            GadgetKit.Visual(post.transform, "Post", GadgetKit.RoundedBoxMesh(new Vector3(PostWidth * 0.5f, postTop, PostWidth * 0.5f), 0.03f), GadgetKit.Body, new Vector3(0f, postTop * 0.5f, 0f));
            NoShadow(GadgetKit.Visual(post.transform, "Foot", GadgetKit.DiscMesh(PostWidth * 0.9f, 0.06f, 16), GadgetKit.Body, new Vector3(0f, 0.03f, 0f)));
            NoShadow(GadgetKit.Visual(post.transform, "Cup", GadgetKit.DiscMesh(hole.LampRadius * 0.9f, hole.LampRadius * 0.5f, 16), GadgetKit.Body, new Vector3(0f, postTop - hole.LampRadius * 0.05f, 0f)));
            if (gauges[i].Lamp != null && gauges[i].Lamp.Renderer is MeshRenderer gaugeLamp) NoShadow(gaugeLamp);
            ctx.AddStatic(post, new Vector3(lamp.x, 0f, lamp.z));

            latchedLamps[i] = new GameObject("Latched Lamp " + hole.Name);
            float lampRadius = hole.LampRadius;
            Mesh sphere = MeshKit.Cached(MeshKit.Key("GadgetLamp", lampRadius), () => MeshKit.Sphere(lampRadius, 12, 8));
            GadgetKit.Lamp(latchedLamps[i].transform, sphere, Palette.Go, Vector3.zero);
            ctx.AddStatic(latchedLamps[i], lamp);
            latchedLamps[i].SetActive(false);

            // Round the mouth: a band of paint, and a ring of studs that says what the gauge says.
            var marks = new GameObject("Mouth " + hole.Name);
            float band = 0.1f + hole.Mouth * 0.03f;
            NoShadow(GadgetKit.Visual(marks.transform, "Paint", MeshKit.Cached(MeshKit.Key("Level08|MouthBand", hole.Mouth, band),
                () => Level08Shapes.Band(hole.Mouth + 0.01f, 0.004f, hole.Mouth + band, 0.004f, 0.006f, 64)), paint));
            // The throat is what the marble is measured against: a ring of paint on the cone, right above it.
            float ring = hole.Throat * 0.12f, lift = 0.012f;
            NoShadow(GadgetKit.Visual(marks.transform, "Throat", MeshKit.Cached(MeshKit.Key("Level08|ThroatBand", hole.Throat, hole.Mouth, hole.ConeDepth),
                () => Level08Shapes.Band(hole.Throat + 0.01f, hole.ConeY(hole.Throat + 0.01f) + lift, hole.Throat + ring, hole.ConeY(hole.Throat + ring) + lift, 0.008f, 64)), paint));
            // And where a marble held across the funnel comes to lie: dashes on the far side of the cone, as seen
            // from the start (the lamp's post stands behind them). The size language of the campaign, as far as
            // a cone allows it: the paint is on the surface the toy stops against.
            float wallIn = Mathf.Lerp(hole.Throat, hole.Mouth, 0.42f), wallOut = Mathf.Lerp(hole.Throat, hole.Mouth, 0.62f);
            Mesh dashes = MeshKit.Cached(MeshKit.Key("Level08|Landing", hole.Throat, hole.Mouth, hole.ConeDepth),
                () => Level08Shapes.ConeDashes(wallIn, hole.ConeY(wallIn) + lift, wallOut, hole.ConeY(wallOut) + lift, 30f, 150f, 5, 0.62f));
            landingPaint[i] = NoShadow(GadgetKit.Visual(marks.transform, "Landing", dashes, paint)).gameObject;
            // The same dashes as a light, in the paint's place while they beckon (see Beckons).
            MeshRenderer landingLight = NoShadow(GadgetKit.Visual(marks.transform, "Landing Light", dashes, Materials.Gadget(Here)));
            landings[i] = new SignalLamp(landingLight, Here);
            landingLight.gameObject.SetActive(false);
            float studRing = hole.Mouth + band + 0.12f + hole.Mouth * 0.03f, studRadius = 0.06f + hole.Mouth * 0.018f;
            int studCount = Mathf.Max(12, Mathf.RoundToInt(Mathf.PI * 2f * studRing / (studRadius * 7f)));
            MeshRenderer studRenderer = GadgetKit.Visual(marks.transform, "Studs", MeshKit.Cached(MeshKit.Key("Level08|Studs", studRing, studCount, studRadius),
                () => Level08Shapes.Studs(studRing, studCount, studRadius, studRadius * 0.7f)), Materials.Gadget(Palette.Amber));
            NoShadow(studRenderer);
            studs[i] = new SignalLamp(studRenderer, Palette.Amber);
            // The light that comes up through the funnel once its marble lies on the plate (in the marble's colour).
            float shaftTop = 5f + hole.Mouth * 1.5f;
            shafts[i] = GadgetKit.Visual(marks.transform, "Shaft", MeshKit.Cached(MeshKit.Key("Level08|Shaft", hole.Throat, hole.FloorY, shaftTop),
                () => Level08Shapes.Shaft(hole.Throat * 0.9f, hole.FloorY + 0.05f, hole.Throat * 1.7f, shaftTop)), ShaftMaterial(Palette.Paper));
            NoShadow(shafts[i]).gameObject.SetActive(false);
            ctx.AddStatic(marks, hole.Axis);
        }

        const float ShaftGain = 0.32f;

        static Material ShaftMaterial(Color candy)
        {
            Color linear = Palette.Lin(candy) * ShaftGain;
            linear.a = 1f;
            return Materials.Flat(new FlatRecipe { Name = "Level08 Shaft", Color = linear, Blend = FlatBlend.Additive });
        }

        Door BuildGate(LevelContext ctx)
        {
            float width = GateX1 - GateX0;
            var panel = new GameObject("Gate") { layer = Layers.Default };
            panel.AddComponent<BoxCollider>().size = new Vector3(width, GateHeight, GateThickness);
            GadgetKit.Visual(panel.transform, "Bars", MeshKit.Cached(MeshKit.Key("Level08|Gate", width, GateHeight),
                () => Level08Shapes.Grille(width, GateHeight, 7, 0.07f, 0.16f, GateThickness * 0.7f)), GadgetKit.Metal);
            // The one signal element: a bar across the bars, amber while the gate is shut.
            gateLamp = GadgetKit.Lamp(panel.transform, GadgetKit.RoundedBoxMesh(new Vector3(width * 0.62f, 0.16f, GateThickness + 0.06f), 0.04f), Palette.Amber, new Vector3(0f, 0.35f, 0f));
            if (gateLamp.Renderer is MeshRenderer bar) NoShadow(bar);
            var middle = new Vector3((GateX0 + GateX1) * 0.5f, GateHeight * 0.5f, GateZ);
            return new Door(ctx, new DoorOptions
            {
                Name = "Gate",
                Panel = panel,
                ClosedPosition = middle,
                // Up into the wall above the opening.
                OpenPosition = middle + Vector3.up * GateHeight,
                Motion = DoorMotion.Slide,
                Seconds = 0.8f,
                Ease = DoorEase.Smooth,
            });
        }

        // Things that make it a place on a workbench. All of it stands against a wall behind or beside the
        // start, out of the way of a marble held toward a funnel, or is paint.
        static void BuildDressing(LevelContext ctx, Material paint)
        {
            Transform root = ctx.Root;
            // Out of the port: an arrow of paint on the lid, and an Ink frame round the opening.
            Mesh arrow = MeshKit.Cached("Level08|Arrow", () => MeshKit.Extrude(new List<Vector2>
            {
                new Vector2(0f, -0.22f), new Vector2(1.1f, -0.22f), new Vector2(1.1f, -0.55f), new Vector2(1.9f, 0f),
                new Vector2(1.1f, 0.55f), new Vector2(1.1f, 0.22f), new Vector2(0f, 0.22f),
            }, 0.008f));
            NoShadow(GadgetKit.Visual(root, "Port Arrow", arrow, paint, new Vector3(-HalfX + 0.6f, 0.006f, PortZ), Quaternion.Euler(90f, 0f, 0f)));
            const float frame = 0.22f, proud = 0.1f;
            Material ink = GadgetKit.Body;
            for (int side = -1; side <= 1; side += 2)
                NoShadow(GadgetKit.Visual(root, "Port Frame", GadgetKit.RoundedBoxMesh(new Vector3(proud * 2f, PortHeight + frame, frame), 0.04f), ink,
                    new Vector3(-HalfX, (PortHeight + frame) * 0.5f, PortZ + side * (PortHalfWidth + frame * 0.5f))));
            NoShadow(GadgetKit.Visual(root, "Port Frame", GadgetKit.RoundedBoxMesh(new Vector3(proud * 2f, frame, PortHalfWidth * 2f + frame * 2f), 0.04f), ink,
                new Vector3(-HalfX, PortHeight + frame * 0.5f, PortZ)));

            // Paper trim round the niche and round the gate, a finger proud of the tower's front.
            const float trim = 0.3f, out2 = 0.07f;
            Mesh Bar(float x, float y) => GadgetKit.RoundedBoxMesh(new Vector3(x, y, out2 * 2f), 0.03f);
            float nicheMid = (NicheX0 + NicheX1) * 0.5f, nicheWide = NicheX1 - NicheX0, nicheTall = NicheY1 - NicheY0;
            NoShadow(GadgetKit.Visual(root, "Niche Trim", Bar(nicheWide + trim * 2f, trim), paint, new Vector3(nicheMid, NicheY0 - trim * 0.5f, FarZ)));
            NoShadow(GadgetKit.Visual(root, "Niche Trim", Bar(nicheWide + trim * 2f, trim), paint, new Vector3(nicheMid, NicheY1 + trim * 0.5f, FarZ)));
            NoShadow(GadgetKit.Visual(root, "Niche Trim", Bar(trim, nicheTall), paint, new Vector3(NicheX0 - trim * 0.5f, (NicheY0 + NicheY1) * 0.5f, FarZ)));
            NoShadow(GadgetKit.Visual(root, "Niche Trim", Bar(trim, nicheTall), paint, new Vector3(NicheX1 + trim * 0.5f, (NicheY0 + NicheY1) * 0.5f, FarZ)));
            float gateMid = (GateX0 + GateX1) * 0.5f, gateWide = GateX1 - GateX0;
            NoShadow(GadgetKit.Visual(root, "Gate Trim", Bar(gateWide + trim * 2f, trim), ink, new Vector3(gateMid, GateHeight + trim * 0.5f, FarZ)));
            NoShadow(GadgetKit.Visual(root, "Gate Trim", Bar(trim, GateHeight), ink, new Vector3(GateX0 - trim * 0.5f, GateHeight * 0.5f, FarZ)));
            NoShadow(GadgetKit.Visual(root, "Gate Trim", Bar(trim, GateHeight), ink, new Vector3(GateX1 + trim * 0.5f, GateHeight * 0.5f, FarZ)));

            // Building blocks and a marker left in the corners behind the start.
            SetPiece(ctx, ToyFactory.WoodenBlock(new Vector3(2.2f, 1.1f, 1.1f), grabbable: false), new Vector3(-11.8f, 0.55f, -11.1f), Quaternion.Euler(0f, 8f, 0f));
            SetPiece(ctx, ToyFactory.WoodenBlock(new Vector3(1.1f, 1.1f, 1.1f), grabbable: false), new Vector3(-12.1f, 1.65f, -11.2f), Quaternion.Euler(0f, -12f, 0f));
            SetPiece(ctx, ToyFactory.WoodenBlock(new Vector3(1.3f, 1.3f, 1.3f), grabbable: false), new Vector3(-9.6f, 0.65f, -10.9f), Quaternion.Euler(0f, 27f, 0f));
            SetPiece(ctx, ToyFactory.Marker(0.45f, 4.2f), new Vector3(10.6f, 0.45f, -11.1f), Quaternion.Euler(0f, 94f, 0f));
            // A ruler along the right wall: something to pace the steps against.
            SetPiece(ctx, ToyFactory.Ruler(new Vector3(1.2f, 0.08f, 16f), grabbable: false), new Vector3(13.1f, 0.04f, -3f), Quaternion.identity);
            // And in the two far corners, behind the funnels: a domino leaning on its neighbour, a spool on its side.
            // (The second one rests on its near bottom edge, 25 degrees over, its top a hair from the first.)
            Quaternion along = Quaternion.Euler(0f, 80f, 0f);
            var first = new Vector3(-12.9f, 0f, 14.6f);
            SetPiece(ctx, ToyFactory.Domino(grabbable: false), first + Vector3.up, along);
            SetPiece(ctx, ToyFactory.Domino(grabbable: false), first + along * new Vector3(0f, 0.972f, 0.73f), along * Quaternion.Euler(-25f, 0f, 0f));
            SetPiece(ctx, ToyFactory.ThreadSpool(0.7f, 1.3f, grabbable: false), new Vector3(12.6f, 0.7f, 14.9f), Quaternion.Euler(0f, 35f, 90f));
        }

        // ---- What the gadgets do to the place ---------------------------------------------------------------

        void OnLatched(int i, Prop prop)
        {
            // Nothing is left to judge here: the gauge's lamp makes way for one that is green for good. (Reset
            // first: a gauge switched off while it reads another marble held over the hole would go on saying so.)
            gauges[i].Reset();
            gauges[i].Enabled = false;
            if (gauges[i].Lamp != null && gauges[i].Lamp.Renderer != null) gauges[i].Lamp.Renderer.gameObject.SetActive(false);
            latchedLamps[i].SetActive(true);
            // The marble's own colour comes up through the funnel.
            int m = IndexOf(prop);
            shafts[i].sharedMaterial = ShaftMaterial(m >= 0 ? MarbleColors[m] : Palette.Paper);
            shafts[i].gameObject.SetActive(true);

            if (Latched < plates.Length) return;
            gate.Open();
            exit.Unlock();
        }

        // Every tick: the studs say what the gauges say, and a marble that has come to rest where it does no
        // good is told why, once per let-go.
        void Watch(LevelContext ctx)
        {
            float seconds = ctx.Time;
            for (int i = 0; i < Holes.Length; i++)
            {
                FitState state = plates[i].Pressed ? FitState.Good : gauges[i].State;
                studs[i].Set(state == FitState.Good ? Palette.Go : state == FitState.Idle ? Palette.Amber : Wrong);
                studs[i].Pulse(seconds);
            }
            gateLamp.Set(gate.Signal);
            gateLamp.Pulse(seconds);

            // The far side of a funnel beckons while the marble in the hand is seen over it and is not over its mouth.
            Prop held = ctx.Game.Grabber.Held;
            int over = -1;
            if (held != null && IndexOf(held) >= 0)
            {
                Player player = ctx.Game.Player;
                over = SeenOver(player.Eye, player.Forward, ctx.Game.Grabber.Ratio, out _);
            }
            for (int i = 0; i < Holes.Length; i++)
            {
                bool beckon = over == i && !plates[i].Pressed && gauges[i].State == FitState.Idle;
                beckons[i] = beckon;
                GameObject light = landings[i].Renderer.gameObject;
                if (light.activeSelf != beckon)
                {
                    light.SetActive(beckon);
                    landingPaint[i].SetActive(!beckon);
                }
                if (beckon) landings[i].Pulse(seconds);
            }

            for (int m = 0; m < marbles.Length; m++)
            {
                Prop marble = marbles[m];
                // The net under everything: a marble that is under the lid and not in a funnel (it went through
                // a sheet after all, or a cone) would be lost for good. The port gives it back.
                bool lost = !marble.Removed && !marble.Held && !marble.Driven && !marble.Frozen && !port.IsPending(marble) && UnderTheLid(marble.Center);
                lostTicks[m] = lost ? lostTicks[m] + 1 : 0;
                if (lostTicks[m] >= 15)
                {
                    lostTicks[m] = 0;
                    Rescues++;
                    port.Eject(marble);
                }
                // The port keeps nothing: a marble left lying in its alcove (put there, or rolled back in) would
                // be in the way of whatever the port has to give back next. It is rolled out again.
                bool parked = !marble.Removed && !marble.Held && !marble.Driven && !marble.Frozen && InThePort(marble.Center) && marble.Velocity.sqrMagnitude < 0.09f;
                parkedTicks[m] = parked ? parkedTicks[m] + 1 : 0;
                if (parkedTicks[m] >= 20)
                {
                    parkedTicks[m] = 0;
                    marble.Body.WakeUp();
                    marble.Body.linearVelocity = PortVelocity;
                }

                if (told[m] || marble.Removed || marble.Held || marble.Driven || marble.Frozen || port.IsPending(marble))
                {
                    restTicks[m] = 0;
                    continue;
                }
                Vector3 centre = marble.Center;
                float radius = marble.Radius, speed2 = marble.Velocity.sqrMagnitude;
                int mouth = MouthOver(centre, radius);
                // (Slow, not still: the funnel's hard gate carries a ball on the throat's rim by cancelling
                // gravity tick by tick, and a big one trembles.)
                bool stuck = mouth >= 0 && !Funnel.Passes(radius, Holes[mouth].Throat) && speed2 < 1f;
                bool onTheLid = mouth < 0 && speed2 < 0.04f && Mathf.Abs(centre.y - radius) < 0.1f;
                string line = null;
                if (mouth >= 0 && plates[mouth].Pressed)
                {
                    // In the mouth of a hole that has its marble: held up by the throat, or lying on the one below.
                    if (stuck || speed2 < 0.04f) line = TakenLine;
                }
                else if (stuck) line = TooBigLine;
                else if (onTheLid) line = MissLine(m, centre);
                restTicks[m] = line != null ? restTicks[m] + 1 : 0;
                if (restTicks[m] < 30) continue;
                told[m] = true;
                ctx.Say(line, 5f);
            }
        }

        // What to say about a marble that lies on the lid, if it was let go at a funnel: nothing when it was
        // not, or when it lies beside the funnel rather than before or behind it.
        string MissLine(int m, Vector3 centre)
        {
            int funnel = aimed[m] >= 0 ? aimed[m] : passedOver[m];
            if (funnel < 0 || plates[funnel].Pressed) return null;
            Hole hole = Holes[funnel];
            var from = new Vector2(centre.x - hole.X, centre.z - FunnelZ);
            Vector2 along = letGoAlong[m];
            float ahead = Vector2.Dot(from, along), beside = Mathf.Abs(from.x * along.y - from.y * along.x);
            if (beside >= hole.Mouth) return null;
            // Behind the hole: it went over. In front of it: held too low (over the near half of the mouth,
            // where its underside meets the lid before the rim) or, held over the far half, from too far off.
            if (ahead > 0f) return OverLine;
            return aimed[m] < 0 || aimedBeyond[m] ? CloserLine : ShortLine;
        }

        // The funnel in whose mouth a ball lies (its middle over the cone, above the throat), or -1.
        static int MouthOver(Vector3 centre, float radius)
        {
            for (int i = 0; i < Holes.Length; i++)
            {
                Hole hole = Holes[i];
                float dx = centre.x - hole.X, dz = centre.z - FunnelZ;
                if (dx * dx + dz * dz < hole.Mouth * hole.Mouth && centre.y > hole.ThroatY - 0.05f && centre.y < radius + 0.3f) return i;
            }
            return -1;
        }

        /// <summary>Is a point inside the port's alcove, behind the wall's inner face?</summary>
        public static bool InThePort(Vector3 point) =>
            point.x < -HalfX - 0.05f && point.x > PortBack - 0.5f && Mathf.Abs(point.z - PortZ) < PortHalfWidth + 0.5f && point.y > -0.3f && point.y < PortHeight + 0.5f;

        /// <summary>Is a point under the lid, and not inside a funnel (on its cone, in its tube or its chamber)?</summary>
        public static bool UnderTheLid(Vector3 point)
        {
            if (point.y > -0.3f) return false;
            for (int i = 0; i < Holes.Length; i++)
            {
                Hole hole = Holes[i];
                float dx = point.x - hole.X, dz = point.z - FunnelZ;
                float off = Mathf.Sqrt(dx * dx + dz * dz);
                if (off <= hole.Throat + 0.05f && point.y >= hole.FloorY - 0.2f) return false;
                if (off <= hole.Mouth && point.y >= hole.ConeY(Mathf.Max(off, hole.Throat)) - 0.02f) return false;
            }
            return true;
        }

        /// <summary>
        /// The funnel over whose mouth a marble in the hand is seen (-1: none), for an eye, a view direction
        /// and the marble's apparent size (scale over distance): the middle of its picture, or the point
        /// halfway to its upper or to its lower edge, lies over the mouth. It is what "let go at this
        /// funnel" looks like from behind the eye - wherever the marble really is. <paramref name="beyond"/>:
        /// the middle of the picture is past the funnel's axis (over the far half of the mouth, or farther).
        /// </summary>
        public static int SeenOver(Vector3 eye, Vector3 forward, float ratio, out bool beyond)
        {
            beyond = false;
            var flat = new Vector2(forward.x, forward.z);
            float level = flat.magnitude;
            if (level < 1e-3f || eye.y <= 0f) return -1;
            flat /= level;
            float pitch = Mathf.Atan2(forward.y, level), half = Mathf.Atan(Mathf.Max(0f, ratio) * 0.5f) * 0.5f;
            var from = new Vector2(eye.x, eye.z);
            for (int ray = 0; ray < 3; ray++)
            {
                float down = -(pitch + (ray == 0 ? 0f : ray == 1 ? half : -half));
                // Level or upward: this line of sight never comes down to the lid.
                if (down < 0.01f) continue;
                Vector2 ground = from + flat * (eye.y / Mathf.Tan(down));
                for (int i = 0; i < Holes.Length; i++)
                {
                    Hole hole = Holes[i];
                    var axis = new Vector2(hole.X, FunnelZ);
                    if ((ground - axis).sqrMagnitude >= hole.Mouth * hole.Mouth) continue;
                    // Where the middle of the picture meets the lid: before the axis, or past it (or nowhere).
                    beyond = -pitch < 0.01f || Vector2.Dot(from + flat * (eye.y / Mathf.Tan(-pitch)) - axis, flat) > 0f;
                    return i;
                }
            }
            return -1;
        }

        // The funnel a marble has just been let go behind, having passed low over its mouth - within its own
        // height of the lid - on the way there, or -1. (The view pointed past the funnel, not into it.)
        static int PassedOver(Game game, float ratio, Vector3 letGoAt)
        {
            Player player = game.Player;
            Vector3 eye = player.Eye, forward = player.Forward;
            var flat = new Vector2(forward.x, forward.z);
            float level = flat.magnitude;
            if (level < 1e-3f) return -1;
            for (int i = 0; i < Holes.Length; i++)
            {
                Hole hole = Holes[i];
                var to = new Vector2(hole.X - eye.x, FunnelZ - eye.z);
                // How far along the view the axis is, and how far beside it.
                float along = Vector2.Dot(to, flat) / level, beside = Mathf.Abs(to.x * flat.y - to.y * flat.x) / level;
                if (along <= 0f || beside >= hole.Mouth) continue;
                float distance = along / level;
                float height = eye.y + forward.y * distance, size = ratio * distance;
                if (height < 0f || height > size + 0.3f) continue;
                // It was let go beyond the axis, outside the mouth.
                var from = new Vector2(letGoAt.x - hole.X, letGoAt.z - FunnelZ);
                if (Vector2.Dot(from, flat) > 0f && from.magnitude > hole.Mouth) return i;
            }
            return -1;
        }

        // ---- The intended solution -------------------------------------------------------------------------

        public override IEnumerator Solve(Bot bot)
        {
            Game game = bot.Game;

            // The little one is a step away: picked up from here it looks big enough to grow into the small hole.
            yield return bot.Grab(Peewee);
            yield return bot.WalkTo(PastTheSpool);
            yield return bot.WalkTo(StandS, 0.15f);
            yield return bot.DropAt(AimS);
            yield return bot.Until(() => plates[0].Pressed, 8f);

            // The middling one from four steps off: let go five steps from where it comes to lie, it is a third bigger.
            yield return bot.WalkTo(PickAggie, 0.15f);
            yield return bot.Grab(Aggie);
            yield return bot.WalkTo(StandM, 0.15f);
            yield return bot.DropAt(AimM);
            yield return bot.Until(() => plates[1].Pressed, 8f);

            // The boulder in the niche, from the back of the room: it only looks small enough to carry.
            yield return bot.WalkTo(PickShooter, 0.15f);
            yield return bot.Grab(Shooter);
            yield return bot.WalkTo(StandL, 0.15f);
            yield return bot.DropAt(AimL);
            yield return bot.Until(() => plates[2].Pressed, 10f);

            // The gate rolls up. Between the middle and the big funnel, and out.
            yield return bot.Until(() => gate.IsOpen, 3f);
            yield return bot.WalkTo(BetweenMAndL);
            yield return bot.WalkTo(new Vector3(ExitCentre.x, 0f, ExitCentre.z));
            yield return bot.Until(() => game.LevelCompleted, 3f);
        }
    }
}
