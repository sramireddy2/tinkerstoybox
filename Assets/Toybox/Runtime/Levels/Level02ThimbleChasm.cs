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
    /// Level 2 (LEVELS.md): a pegboard walkway, nine units above the bench, ends in a round silo whose floor
    /// is missing. The thimble on the spool beside the spawn is the plug - once it is big enough. Held
    /// against the painted outline on the silo's far wall it stops on that wall; how far back the player
    /// stands decides how big it is there (9 to 13 fits; 13 is the thimble's clamp, so "too far back" is
    /// harmless). The well takes a thimble of the right size, drops it flush with the floor and closes a
    /// rubber grommet round it; one that is too small falls to the pad, which gives it back to the spool.
    ///
    /// The paint is the gauge: the outline's dashes and the band round the hole blink red while the held
    /// thimble is too small and turn green when it would be taken; the dashed lines along the corridor walls
    /// turn green with them (a thimble held big covers everything else). White means "not over the hole".
    /// Four lines say what went wrong, each when it happens: taken from too far off, too small, caught on
    /// the walkway's edge, big enough but not over the hole.
    /// (Where this differs from LEVELS.md, and why: tools/out/notes/level02-build.md and level02-review.md.)
    /// </summary>
    [Level(2, "thimble-chasm", "The Thimble Chasm", Phase = 1)]
    public sealed class Level02ThimbleChasm : LevelDefinition
    {
        // ---- The layout (LEVELS.md, Level 2). Walkway top y = 0, travel +Z. ---------------------------------
        public const float HalfWidth = 7f, NearZ = -10f;
        /// <summary>The silo's axis is at (0, AxisZ); the hole is the whole disc of SiloRadius about it.</summary>
        public const float AxisZ = 23f, SiloRadius = 7f;
        public const float WallTop = 18f, WellFloor = -9f, FloorThickness = 0.6f, WallThickness = 1f;
        public const float DoorHalfWidth = 1f, DoorHeight = 2.6f, TunnelEnd = 36f;
        /// <summary>The pad's top: a tenth above the bench it lies on.</summary>
        public const float PadTop = WellFloor + 0.1f;

        /// <summary>The scale window of the well. The top of it is the thimble's clamp.</summary>
        public const float MinPlug = 9f, MaxPlug = 13f;
        public const float StartScale = 0.8f;
        public const string PlugTag = "plug";

        /// <summary>The spool stands beside the spawn, a step away: the first pickup is a close one.</summary>
        public static readonly Vector3 SpoolBase = new Vector3(0.9f, 0f, -4.8f);
        public const float SpoolRadius = 0.4f, SpoolHeight = 1.1f;
        public static readonly Vector3 Spawn = new Vector3(0f, 0f, -6f);
        public const float SpawnYaw = 12f;
        /// <summary>The middle of the painted outline on the far wall: what to hold the thimble against.</summary>
        public static readonly Vector3 OutlineCentre = new Vector3(0f, 7.5f, AxisZ + SiloRadius);
        /// <summary>The outline is the thimble's silhouette at this scale: 11 wide, 8.8 tall.</summary>
        public const float OutlineScale = 11f;
        /// <summary>Where the solver lets go from.</summary>
        public static readonly Vector3 StandPoint = new Vector3(0f, 0f, 3f);
        public static readonly Vector3 ExitCentre = new Vector3(0f, 1.3f, 34f);

        /// <summary>
        /// The well takes a thimble whose middle is over the hole: within this of the axis. (LEVELS says 6;
        /// measured, a thimble at the clamp let go from the start with the view a little high hangs 6.1 to
        /// 6.8 from the axis - half over the hole, and left standing on its edge. And a small one against
        /// the far wall is 7 - s / 2 from the axis: with 6 nothing under 2 across was ever judged.)
        /// </summary>
        public const float CaptureRadius = SiloRadius - 0.15f;
        /// <summary>Feet under the walkway's slab are in the well (LEVELS says -1.2: see the review notes).</summary>
        public const float HazardTop = -FloorThickness;
        /// <summary>A thimble whose middle is below this is in the well.</summary>
        public const float LeashTop = -1.5f;
        /// <summary>
        /// Apparent size (scale / grab distance) under which the thimble cannot be made big enough from
        /// anywhere but the last steps before the back wall. From the start it is 0.53.
        /// </summary>
        public const float FarRatio = 0.3f;

        public const string TooSmallLine = "Too small to plug the hole. Hold it and step back: it grows.";
        /// <summary>Too small because the walkway stopped it (the view too low), not because of where the player stood.</summary>
        public const string EdgeLine = "Too small: it caught on the edge of the floor. Hold it higher, against the painted outline.";
        /// <summary>Said on a grab from far off. The words are Level 1's, on purpose.</summary>
        public const string FarLine = "It only ever gets as big as it looks. Pick it up from closer.";
        public const string MissedLine = "Big enough, but it is not over the hole. Carry it closer and let go again.";

        const float LipBottom = -1.1f, LipTop = -0.08f;
        /// <summary>Height of the dashed lines along the corridor walls.</summary>
        public const float TapeY = 2.5f;
        /// <summary>
        /// The gauge's lamp: on a bracket high on the far wall, over the outline. It is in view over a held
        /// thimble that is too small or only just fits; one that has grown past about 10.5 covers the lamp
        /// as it covers the outline.
        /// </summary>
        public static readonly Vector3 GaugeLamp = new Vector3(0f, 15.7f, AxisZ + SiloRadius - 1f);
        const float GaugeLampRadius = 0.5f;
        /// <summary>
        /// "This does not fit": the red that blinks three times a second. The same signal the lamps of the
        /// gadget presenters show for a wrong size (GadgetFx.Wrong), so that the outline and the lamp agree.
        /// </summary>
        public static readonly Signal Wrong = new Signal("Wrong", Palette.Hex("#FF4A32"), 2.4f, 0.35f, 3f);
        const int RingSegments = 48;
        const float SegmentDegrees = 360f / RingSegments;

        Prop thimble;
        Socket well;
        HazardZone hazard;
        PropLeash leash;
        FitGauge gauge;
        Exit exit;
        GameObject plugFloor, pad, pluggedLamp;
        MeshRenderer outline, rim;
        MeshRenderer[] tapes;
        Material paint;
        FitState tint;
        /// <summary>Apparent size of the thimble at its last grab.</summary>
        float ratio;
        /// <summary>The thimble has been told off since it was last picked up.</summary>
        bool judged;
        /// <summary>Ticks a thimble that is big enough has lain still on the walkway.</summary>
        int atRest;

        public Prop Thimble => thimble;
        public Socket Well => well;
        public HazardZone Hazard => hazard;
        public PropLeash Leash => leash;
        public FitGauge Gauge => gauge;
        public Exit Exit => exit;
        /// <summary>The painted outline's renderer (its material says what the gauge reads).</summary>
        public Renderer Outline => outline;
        /// <summary>The band of paint round the hole. It shows what the outline shows, where a big thimble does not cover it.</summary>
        public Renderer HoleRim => rim;
        /// <summary>
        /// The dashed lines along the two corridor walls. They turn green with the outline (and only green):
        /// a thimble held big covers the outline, the lamp and the hole; the walls beside it stay in view.
        /// </summary>
        public IReadOnlyList<Renderer> Tapes => tapes;

        public override string Blurb => "The floor is missing. The thimble isn't.";

        public override string[] Hints => new[]
        {
            "The thimble has a flat top you could stand on, if it were big enough.",
            "Hold it up against the outline on the round wall. Step backward to make it bigger, forward to make it smaller.",
            // LEVELS.md: "Pick the thimble up from right beside it, stand a few steps past the spool, cover the
            // painted outline above the little door, and let go." Measured, that recipe leaves a thimble taken
            // from touching distance (k 0.8 to 1.1) on the walkway: it reaches its clamp short of the hole.
            // The paint is what tells, so the hint says what the paint says.
            "Pick the thimble up from a step away and hold it over the painted outline above the little door. When the paint turns green, let go. Red: step back. If the thimble stops on the floor, walk closer to the hole.",
        };

        public override string Environment => "pegboard-workbench";

        // The walkway stands nine units above the bench top; the well goes all the way down to it.
        public override float GroundY => WellFloor;
        public override float KillY => -30f;

        public override void Build(LevelContext ctx)
        {
            thimble = null;
            well = null;
            hazard = null;
            leash = null;
            gauge = null;
            exit = null;
            plugFloor = null;
            pad = null;
            pluggedLamp = null;
            outline = null;
            rim = null;
            tapes = null;
            tint = FitState.Idle;
            ratio = 0f;
            judged = false;
            atRest = 0;

            Dip dip = ctx.Dip;
            Material board = Materials.Room(RoomSurface.LevelStatic, dip, PatternSpec.Pegboard);
            // The silo is a barrel of staves; inside the well, below the walkway, it is in the dark.
            Material staves = Materials.Room(RoomRecipe.For(RoomSurface.LevelStatic, dip, new PatternSpec(RoomPattern.Planks, 1.4f, 40f, 0.07f)).With(r =>
            {
                r.Name = dip.Name + " Silo";
                r.Dado = Palette.Mix(dip.Deep, Palette.Ink, 0.5f);
                r.DadoY = -0.02f;
            }));
            paint = Materials.Room(RoomRecipe.Solid(Palette.Paper));

            BuildWalkway(ctx, board);
            BuildSilo(ctx, staves);
            BuildTunnel(ctx, board);
            BuildPad(ctx);
            BuildPaint(ctx);
            BuildDressing(ctx);
            SkyCap.Add(ctx, -HalfWidth - WallThickness, HalfWidth + WallThickness, NearZ - WallThickness, TunnelEnd + WallThickness, WallTop);

            // The thimble on its spool.
            ctx.AddStatic(ToyFactory.ThreadSpool(SpoolRadius, SpoolHeight, grabbable: false), SpoolBase + Vector3.up * (SpoolHeight * 0.5f));
            ToyDef def = ToyCatalog.Get(ToyId.Thimble);
            thimble = ToyCatalog.Add(ctx, ToyId.Thimble, SpoolBase + Vector3.up * (SpoolHeight + def.RestHeight * StartScale + 0.005f), StartScale, options: o =>
            {
                o.MinScale = 0.3f;
                o.MaxScale = MaxPlug;
                o.Tags = new[] { PlugTag };
                // It is a plug and a platform: it always lands on its rim.
                o.AllowPitch = false;
            });

            // The well takes a thimble of the right size whose middle is over the hole: onto the axis,
            // upright, then down until its top is flush with the walkway.
            Zone over = Zone.Cylinder(0f, AxisZ, CaptureRadius, LeashTop, WallTop);
            well = new Socket(ctx, new SocketOptions
            {
                Name = "Well", AcceptTag = PlugTag, Capture = over, MinScale = MinPlug, MaxScale = MaxPlug + 0.01f,
                SeatPose = s => new Pose(new Vector3(0f, 1.6f + def.RestHeight * s, AxisZ), Quaternion.identity),
                // (LEVELS says 0.35: with the wider capture a thimble at the clamp may have 6.8 to slide.)
                EaseSeconds = 0.45f, ThenFallTo = 0f, LockOnSeat = true,
            });
            well.OnSeated += prop => Plugged(ctx, prop);
            well.OnRejected += (prop, fit) =>
            {
                if (fit != FitState.TooSmall) return;
                // Let go with its underside at floor level on the near half of the hole: the walkway's edge
                // is what stopped it, not the far wall.
                GadgetKit.VerticalExtent(prop, out float bottom, out _);
                Refused(ctx, bottom < 0.75f && prop.Center.z < AxisZ);
            };

            // The outline is the gauge: its dashes say whether the thimble in the player's hands would fit
            // if it were let go where it is, and so does the band of paint round the hole (a thimble that has
            // grown past about 10.5 covers the outline and the lamp; the band shows under it).
            // The lamp, high on the wall, wears the read-out of that size (a ring against the band of sizes
            // that fit), which moves as the player steps back and forth.
            gauge = new FitGauge(ctx, new FitGaugeOptions
            {
                Name = "Outline", Target = well, Near = over, Tag = PlugTag,
                Lamp = GaugeLamp, LampRadius = GaugeLampRadius,
            });
            BuildLampMount(ctx);
            gauge.Changed += Tint;
            ctx.OnUpdate(dt => Pulse(ctx.Time));

            Zone inTheWell = Zone.Cylinder(0f, AxisZ, SiloRadius + 0.5f, WellFloor - 1f, LeashTop);
            hazard = new HazardZone(ctx, new HazardZoneOptions { Name = "Well", Shape = Zone.Cylinder(0f, AxisZ, SiloRadius + 0.5f, WellFloor - 1f, HazardTop) });
            // A thimble that is too small goes down the well, out of sight from the walkway: the pad springs
            // it back to the spool at the size it started with, a second and a half after it went in. Not
            // "once it lies still" (LEVELS): one that topples in from the near edge lies on its side and rolls
            // to and fro for six to ten seconds (measured), a hump whose top - at -9 + s - a player can stand on.
            // The same goes for the two other places where it would be out of reach: thrown small through
            // the door into the tunnel, or come to rest on the lamp's bracket.
            float far = AxisZ + SiloRadius;
            leash = new PropLeash(ctx, new PropLeashOptions
            {
                Name = "Pad", Props = new[] { thimble },
                Forbidden = new[]
                {
                    inTheWell,
                    Zone.MinMax(new Vector3(-DoorHalfWidth - 0.5f, -0.5f, far - 0.2f), new Vector3(DoorHalfWidth + 0.5f, DoorHeight + 0.5f, TunnelEnd + 0.5f)),
                    Zone.MinMax(new Vector3(-1.2f, GaugeLamp.y + GaugeLampRadius + 0.05f, far - 2.2f), new Vector3(1.2f, WallTop, far + 0.2f)),
                },
                Grace = 1.5f, Action = LeashAction.Respawn,
            });

            // What the player is told, each at the moment it happens.
            // Taken from far off the thimble looks small, and it only ever gets as big as it looks: said on
            // the grab (the thimble that came back to the spool is twenty units from the hole, and the
            // natural thing is to take it from there).
            ctx.Game.Events.PropGrabbed += e =>
            {
                if (e.Prop != thimble) return;
                judged = false;
                ratio = e.OldScale / Mathf.Max(0.01f, e.GrabDistance);
                if (ratio < FarRatio) ctx.Say(FarLine, 6f);
            };
            ctx.OnUpdate(dt =>
            {
                if (judged || thimble.Held || thimble.Driven || well.Seated)
                {
                    atRest = 0;
                    return;
                }
                if (thimble.Scale < MinPlug)
                {
                    // Too small, and it went down the well without the well having judged it: let go from
                    // the edge straight down, or toppled in.
                    if (inTheWell.Contains(thimble.Center)) Refused(ctx, false);
                    return;
                }
                // Big enough, and come to rest on the walkway: the size is not what is wrong. (Said once it
                // lies still: one that lands with its middle just over the rim is taken by the well after all.)
                if (thimble.Center.y > 0f && thimble.Velocity.sqrMagnitude < 0.04f) atRest++;
                else atRest = 0;
                if (atRest < 30) return;
                judged = true;
                ctx.Say(MissedLine, 5f);
            });

            ctx.SetSpawn(Spawn, SpawnYaw);
            ctx.AddCheckpoint(Volume.Box(HalfWidth * 2f, 4f, 4f), Spawn + Vector3.up * 2f, Spawn, SpawnYaw, "Spawn");
            exit = ctx.AddExit(ExitCentre, new Vector3(DoorHalfWidth * 2f, DoorHeight, 3f));
        }

        // ---- Geometry --------------------------------------------------------------------------------------

        // The walkway: x -7..7, z -10..23 without the disc of the hole, 0.6 thick, between walls 18 high.
        void BuildWalkway(LevelContext ctx, Material board)
        {
            var root = new GameObject("Walkway");
            Mesh mesh = MeshKit.Cached("Level02|Walkway", () =>
            {
                var shape = new List<Vector2> { new Vector2(-HalfWidth, NearZ), new Vector2(HalfWidth, NearZ) };
                // Round the hole from the right wall (0 degrees) through the near point to the left wall.
                const int steps = 48;
                for (int i = 0; i <= steps; i++)
                {
                    float a = -Mathf.PI * i / steps;
                    shape.Add(new Vector2(SiloRadius * Mathf.Cos(a), AxisZ + SiloRadius * Mathf.Sin(a)));
                }
                Mesh flat = MeshKit.Extrude(shape, FloorThickness);
                Mesh laid = MeshKit.Merge("Level02 Walkway", new[] { new MeshPart(flat, new Vector3(0f, -FloorThickness * 0.5f, 0f), Quaternion.Euler(90f, 0f, 0f)) });
                MeshKit.Release(flat);
                return laid;
            });
            Visual(root, "Visual", mesh, board);

            // Colliders: a box up to where the hole begins, then columns between the hole and the walls.
            float edge = AxisZ - SiloRadius;
            Box(root, "Floor", new Vector3(0f, -FloorThickness * 0.5f, (NearZ + edge) * 0.5f), new Vector3(HalfWidth * 2f, FloorThickness, edge - NearZ));
            int[] cuts = { 0, 2, 3, 4, 5, 6, 7, 8, 9, 10, 12 };
            for (int side = -1; side <= 1; side += 2)
            {
                for (int c = 0; c + 1 < cuts.Length; c++)
                {
                    int from = cuts[c], to = cuts[c + 1], sign = side;
                    Mesh hull = ToyHull.Cached("Level02|Cusp|" + from + "|" + sign, () =>
                    {
                        Vector2 a = Rim(from, sign), b = Rim(to, sign);
                        var footprint = new List<Vector2> { new Vector2(a.x, edge), new Vector2(b.x, edge), b };
                        if (a.y > edge + 1e-4f) footprint.Add(a);
                        return Level02Shapes.Prism(footprint, -FloorThickness, 0f);
                    });
                    var holder = new GameObject("Cusp");
                    holder.transform.SetParent(root.transform, false);
                    MeshCollider collider = holder.AddComponent<MeshCollider>();
                    collider.convex = true;
                    collider.sharedMesh = hull;
                }
            }
            ctx.AddStatic(root, Vector3.zero);

            float height = WallTop - WellFloor, middle = (WallTop + WellFloor) * 0.5f;
            float length = AxisZ - NearZ + WallThickness;
            for (int side = -1; side <= 1; side += 2)
                ctx.AddStatic(BasicToys.Slab(new Vector3(WallThickness, height, length), board),
                    new Vector3(side * (HalfWidth + WallThickness * 0.5f), middle, AxisZ - length * 0.5f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(HalfWidth * 2f, height, WallThickness), board), new Vector3(0f, middle, NearZ - WallThickness * 0.5f));
        }

        // A point of the hole's rim on the near side: step 0 is the near point (0, 16), step 12 where the rim meets a wall.
        static Vector2 Rim(int step, int side)
        {
            float a = step * SegmentDegrees * Mathf.Deg2Rad;
            return new Vector2(side * SiloRadius * Mathf.Sin(a), AxisZ - SiloRadius * Mathf.Cos(a));
        }

        // The silo: a round wall of radius 7 from the bench up to 18. Beyond the hole's axis it stands on
        // the walkway's level and above; on the near side it is only the wall of the well under the floor.
        // The door is in its far point.
        void BuildSilo(LevelContext ctx, Material staves)
        {
            var root = new GameObject("Silo");
            float outer = SiloRadius + WallThickness;
            float door = Mathf.Asin(DoorHalfWidth / SiloRadius) * Mathf.Rad2Deg;
            Visual(root, "Wall Right", MeshKit.Cached("Level02|Silo|Right", () => Level02Shapes.Ring(SiloRadius, outer, WellFloor, WallTop, 0f, 90f - door, 22)), staves);
            Visual(root, "Wall Left", MeshKit.Cached("Level02|Silo|Left", () => Level02Shapes.Ring(SiloRadius, outer, WellFloor, WallTop, 90f + door, 180f, 22)), staves);
            Visual(root, "Lintel", MeshKit.Cached("Level02|Silo|Lintel", () => Level02Shapes.Ring(SiloRadius, outer, DoorHeight, WallTop, 90f - door, 90f + door, 4)), staves);
            Visual(root, "Sill", MeshKit.Cached("Level02|Silo|Sill", () => Level02Shapes.Ring(SiloRadius, outer, WellFloor, -0.02f, 90f - door, 90f + door, 4)), staves);
            Visual(root, "Liner", MeshKit.Cached("Level02|Silo|Liner", () => Level02Shapes.Ring(SiloRadius, outer, WellFloor, -FloorThickness, 180f, 360f, 48)), staves);

            // Colliders: one box per 7.5 degrees, its inner face tangent to the round wall (so nothing is
            // nearer the axis than 7). The three over the door leave the opening free.
            float length = 2f * outer * Mathf.Tan(SegmentDegrees * 0.5f * Mathf.Deg2Rad) + 0.02f;
            for (int i = 0; i < RingSegments; i++)
            {
                float degrees = i * SegmentDegrees;
                float a = degrees * Mathf.Deg2Rad;
                Vector3 at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (SiloRadius + WallThickness * 0.5f);
                Quaternion turn = Quaternion.Euler(0f, -degrees, 0f);
                if (degrees > 180.01f)
                    Wall(root, at, turn, length, WellFloor, -FloorThickness * 0.5f);
                else if (Mathf.Abs(degrees - 90f) < SegmentDegrees + 0.01f)
                {
                    Wall(root, at, turn, length, WellFloor, 0f);
                    Wall(root, at, turn, length, DoorHeight, WallTop);
                }
                else
                    Wall(root, at, turn, length, WellFloor, WallTop);
            }
            ctx.AddStatic(root, new Vector3(0f, 0f, AxisZ));
        }

        static void Wall(GameObject root, Vector3 at, Quaternion turn, float length, float y0, float y1)
        {
            var holder = new GameObject("Stave") { layer = Layers.Default };
            holder.transform.SetParent(root.transform, false);
            holder.transform.localPosition = new Vector3(at.x, (y0 + y1) * 0.5f, at.z);
            holder.transform.localRotation = turn;
            holder.AddComponent<BoxCollider>().size = new Vector3(WallThickness, y1 - y0, length);
        }

        // The way out: a tunnel from the door in the silo's far point, x -1..1, y 0..2.6, z 30..36.
        void BuildTunnel(LevelContext ctx, Material board)
        {
            float far = AxisZ + SiloRadius;
            float mouth = AxisZ + Mathf.Sqrt(SiloRadius * SiloRadius - DoorHalfWidth * DoorHalfWidth);   // 29.93: the wall at the door's edges
            float outside = DoorHalfWidth + WallThickness, roof = DoorHeight + 1f;
            // Floor, two walls, a ceiling a hair under the lintel (no two faces in one plane), the end wall.
            // (The floor begins behind the round wall's inner face, so that the stripes of the well's lip run on under the door.)
            Slab(ctx, board, -outside, outside, -FloorThickness, 0f, far, TunnelEnd);
            for (int side = -1; side <= 1; side += 2)
                Slab(ctx, board, side > 0 ? DoorHalfWidth : -outside, side > 0 ? outside : -DoorHalfWidth, 0f, DoorHeight - 0.02f, mouth + 0.02f, TunnelEnd);
            Slab(ctx, board, -outside, outside, DoorHeight - 0.02f, roof, far + 0.5f, TunnelEnd);
            Slab(ctx, board, -outside, outside, -FloorThickness, roof, TunnelEnd, TunnelEnd + WallThickness);
            // What it stands on, outside the silo.
            Slab(ctx, board, -outside, outside, WellFloor, -FloorThickness, far + 0.8f, TunnelEnd + WallThickness);
        }

        static void Slab(LevelContext ctx, Material material, float x0, float x1, float y0, float y1, float z0, float z1) =>
            ctx.AddStatic(BasicToys.Slab(new Vector3(x1 - x0, y1 - y0, z1 - z0), material), new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f));

        // The floor of the well: a pad in hazard stripes. Falling onto it sends the player back to the
        // start, and a thimble that comes to rest on it back to the spool.
        void BuildPad(LevelContext ctx)
        {
            pad = new GameObject("Spring Pad") { layer = Layers.Default };
            var box = pad.AddComponent<BoxCollider>();
            box.size = new Vector3(SiloRadius * 2f + 0.4f, 0.1f, SiloRadius * 2f + 0.4f);
            box.center = new Vector3(0f, -0.05f, 0f);
            float radius = SiloRadius - 0.02f, stripe = Palette.HazardStripePitch * 0.5f;
            Visual(pad, "Ink", MeshKit.Cached(MeshKit.Key("Level02|Pad", radius, stripe, 0f), () => Level02Shapes.StripedDisc(radius, stripe, false)), GadgetKit.Body);
            Visual(pad, "Hazard", MeshKit.Cached(MeshKit.Key("Level02|Pad", radius, stripe, 1f), () => Level02Shapes.StripedDisc(radius, stripe, true)), Materials.Gadget(Palette.Hazard));
            ctx.AddStatic(pad, new Vector3(0f, PadTop, AxisZ));

            // The same stripes run round the lip of the well, just under the walkway: from the start the
            // far side of them is all that shows of the hole, and it says what the hole is.
            var lip = new GameObject("Well Lip");
            float inside = SiloRadius - 0.04f;
            Visual(lip, "Ink", MeshKit.Cached(MeshKit.Key("Level02|Lip", inside, stripe, 0f), () => Level02Shapes.StripedBand(inside, LipBottom, LipTop, stripe, false)), GadgetKit.Body);
            Visual(lip, "Hazard", MeshKit.Cached(MeshKit.Key("Level02|Lip", inside, stripe, 1f), () => Level02Shapes.StripedBand(inside, LipBottom, LipTop, stripe, true)), Materials.Gadget(Palette.Hazard));
            ctx.AddStatic(lip, new Vector3(0f, 0f, AxisZ));

            // What closes the hole once the thimble is seated: the floor under the grommet.
            plugFloor = new GameObject("Plug Floor") { layer = Layers.Default };
            plugFloor.AddComponent<BoxCollider>().size = new Vector3(SiloRadius * 2f, 0.3f, SiloRadius * 2f);
            ctx.AddStatic(plugFloor, new Vector3(0f, -0.15f, AxisZ));
            plugFloor.SetActive(false);
        }

        // Paint (no colliders): a white band round the hole, and the thimble's outline at the size that
        // fits on the wall it stops against.
        void BuildPaint(LevelContext ctx)
        {
            var band = new GameObject("Hole Rim");
            rim = Visual(band, "Paint", MeshKit.Cached("Level02|Rim", () => Level02Shapes.FloorBand(SiloRadius, SiloRadius + 0.45f, 180f, 360f, 48, HalfWidth)), paint);
            ctx.AddStatic(band, new Vector3(0f, 0.02f, AxisZ));

            var holder = new GameObject("Outline");
            Mesh mesh = MeshKit.Cached("Level02|Outline", () =>
            {
                float rim = ToyFactory.ThimbleRimRadius * OutlineScale, top = ToyFactory.ThimbleTopRadius * OutlineScale;
                float tall = ToyFactory.ThimbleHeight * OutlineScale;
                float y0 = OutlineCentre.y - tall * 0.5f, y1 = OutlineCentre.y + tall * 0.5f;
                const float shoulder = 0.7f;
                // Up the right side, round the shoulder, along the top, round the other shoulder and down.
                var crown = new List<Vector2> { new Vector2(rim, y0), new Vector2(top, y1 - shoulder) };
                for (int i = 1; i <= 6; i++)
                {
                    float a = Mathf.PI * 0.5f * i / 6f;
                    crown.Add(new Vector2(top - shoulder + shoulder * Mathf.Cos(a), y1 - shoulder + shoulder * Mathf.Sin(a)));
                }
                for (int i = 0; i <= 6; i++)
                {
                    float a = Mathf.PI * 0.5f * (1f + i / 6f);
                    crown.Add(new Vector2(-(top - shoulder) + shoulder * Mathf.Cos(a), y1 - shoulder + shoulder * Mathf.Sin(a)));
                }
                crown.Add(new Vector2(-rim, y0));
                var foot = new List<Vector2> { new Vector2(-rim, y0), new Vector2(rim, y0) };

                var parts = new List<MeshPart>();
                float wall = SiloRadius - 0.04f;
                Level02Shapes.Dashes(parts, crown, wall, 0.4f, 1.1f, 0.6f);
                Level02Shapes.Dashes(parts, foot, wall, 0.4f, 1.1f, 0.6f);
                Mesh merged = MeshKit.Merge("Level02 Outline", parts);
                foreach (MeshPart part in parts) MeshKit.Release(part.Mesh);
                return merged;
            });
            outline = Visual(holder, "Paint", mesh, paint);
            ctx.AddStatic(holder, new Vector3(0f, 0f, AxisZ));

            // A dashed line along each corridor wall, a little over head height, from the start to where the
            // round wall begins: they lead the eye to the outline, and they are the part of the paint that
            // a thimble held big does not cover.
            tapes = new MeshRenderer[2];
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                var normal = new Vector3(-sign, 0f, 0f);
                float from = NearZ + 0.6f, to = AxisZ - 0.4f;
                var tape = new GameObject(side == 0 ? "Tape Left" : "Tape Right");
                tapes[side] = Visual(tape, "Paint", MeshKit.Cached(MeshKit.Key("Level02|Tape", from, to, TapeY, sign),
                    () => Level02Shapes.WallDashes(from, to, TapeY, 0.3f, 1.1f, 0.7f, normal)), paint);
                ctx.AddStatic(tape, new Vector3(sign * (HalfWidth - 0.02f), 0f, 0f));
            }
        }

        // The bracket the gauge's lamp sits on (the gauge makes the lamp itself), and the lamp that takes
        // its place once the hole is plugged: green and steady. Lamp and bracket stop a held thimble.
        void BuildLampMount(LevelContext ctx)
        {
            var mount = new GameObject("Gauge Mount") { layer = Layers.Default };
            float wall = AxisZ + SiloRadius, reach = wall - GaugeLamp.z;
            var box = mount.AddComponent<BoxCollider>();
            box.size = new Vector3(GaugeLampRadius * 2f, GaugeLampRadius * 2f, reach + GaugeLampRadius);
            box.center = new Vector3(0f, 0f, (reach - GaugeLampRadius) * 0.5f);
            GadgetKit.Visual(mount.transform, "Arm", GadgetKit.BoxMesh(new Vector3(0.3f, 0.3f, reach)), GadgetKit.Body, new Vector3(0f, 0f, reach * 0.5f + 0.02f));
            GadgetKit.Visual(mount.transform, "Plate", GadgetKit.RoundedBoxMesh(new Vector3(1f, 1f, 0.12f), 0.04f), GadgetKit.Body, new Vector3(0f, 0f, reach - 0.03f));
            ctx.AddStatic(mount, GaugeLamp);

            pluggedLamp = new GameObject("Plugged Lamp");
            Mesh sphere = MeshKit.Cached(MeshKit.Key("GadgetLamp", GaugeLampRadius), () => MeshKit.Sphere(GaugeLampRadius, 12, 8));
            GadgetKit.Lamp(pluggedLamp.transform, sphere, Palette.Go, Vector3.zero);
            ctx.AddStatic(pluggedLamp, GaugeLamp);
            pluggedLamp.SetActive(false);
        }

        // Things that make it a place on a workbench. All of it is behind the spawn or flat against a
        // wall, out of the way of a thimble held toward the silo.
        void BuildDressing(LevelContext ctx)
        {
            // A ruler along the left wall: something to pace the steps back against.
            ctx.AddStatic(ToyFactory.Ruler(new Vector3(1.2f, 0.08f, 15f), grabbable: false), new Vector3(-6.1f, 0.04f, 6.5f));
            // Building blocks and a marker left in the corners behind the start.
            ctx.AddStatic(ToyFactory.WoodenBlock(new Vector3(1.8f, 0.9f, 0.9f), grabbable: false), new Vector3(-5.6f, 0.45f, -9.2f), Quaternion.Euler(0f, 6f, 0f));
            ctx.AddStatic(ToyFactory.WoodenBlock(new Vector3(0.9f, 0.9f, 0.9f), grabbable: false), new Vector3(-5.9f, 1.35f, -9.2f), Quaternion.Euler(0f, -14f, 0f));
            ctx.AddStatic(ToyFactory.WoodenBlock(new Vector3(1.1f, 1.1f, 1.1f), grabbable: false), new Vector3(-3.9f, 0.55f, -8.9f), Quaternion.Euler(0f, 31f, 0f));
            ctx.AddStatic(ToyFactory.Marker(0.4f, 3.4f), new Vector3(5.2f, 0.4f, -9.1f), Quaternion.Euler(0f, 97f, 0f));
        }

        // ---- What the gadgets do to the place ---------------------------------------------------------------

        // The thimble is in its seat: its top is the floor now. The grommet closes the ring round it.
        void Plugged(LevelContext ctx, Prop prop)
        {
            // The hazard stays armed under the new floor: nobody on top of it is that deep, and a player who
            // slipped in beside the falling thimble at the last moment is sent back instead of sealed in.
            plugFloor.SetActive(true);

            // From the rounded shoulder of the thimble's top out over the rim of the hole, proud of the floor by a hair.
            float inner = 0.44f * prop.Scale, outer = SiloRadius + 0.2f;
            // A closed loop that never touches the axis: under, up the outside, across the top (three low
            // ridges, so that it reads as rubber and not as a gap), down the inside.
            const float top = 0.03f, ridge = 0.07f;
            var profile = new List<Vector2> { new Vector2(inner, -0.25f), new Vector2(outer, -0.25f), new Vector2(outer, top) };
            for (int i = 2; i >= 0; i--)
            {
                float r = Mathf.Lerp(inner + 0.3f, SiloRadius - 0.3f, i * 0.5f);
                profile.Add(new Vector2(r + 0.13f, top));
                profile.Add(new Vector2(r + 0.06f, ridge));
                profile.Add(new Vector2(r - 0.06f, ridge));
                profile.Add(new Vector2(r - 0.13f, top));
            }
            profile.Add(new Vector2(inner, top));
            profile.Add(new Vector2(inner, -0.25f));
            Mesh mesh = MeshKit.Lathe(profile, 48);
            var grommet = new GameObject("Grommet");
            Visual(grommet, "Rubber", mesh, Materials.Toy(ToyRecipe.Rubber, Palette.Ink));
            ctx.AddStatic(grommet, new Vector3(0f, 0f, AxisZ));
            ctx.OnDispose(() => Sim.Destroy(mesh));

            // The pad comes up under it (nobody sees that; it is what holds the thimble up).
            GadgetKit.VerticalExtent(prop, out float bottom, out _);
            pad.transform.position = new Vector3(0f, Mathf.Max(PadTop, bottom), AxisZ);

            // Nothing is left to judge: the gauge's lamp makes way for one that is green for good, and so
            // are the outline's dashes.
            gauge.Enabled = false;
            if (gauge.Lamp != null && gauge.Lamp.Renderer != null) gauge.Lamp.Renderer.gameObject.SetActive(false);
            pluggedLamp.SetActive(true);
            Show(Palette.Go);
            tint = FitState.Good;
        }

        // A thimble that is too small has been let go over the hole, or has gone down it. Said once per
        // pick-up. One taken from too far off has had its own line on the grab ("step back" would be wrong:
        // from no place on the walkway does it get big enough).
        void Refused(LevelContext ctx, bool caughtOnTheEdge)
        {
            if (judged) return;
            judged = true;
            if (ratio > 0f && ratio < FarRatio) return;
            ctx.Say(caughtOnTheEdge ? EdgeLine : TooSmallLine, 5f);
        }

        // The outline's dashes and the band round the hole: paint while nothing is judged, red and blinking
        // while the held thimble is too small, green and steady while it would fit (and for good once it is
        // seated). The lines along the walls only ever say "it fits".
        void Tint(FitState state)
        {
            // (Once the well has the thimble the paint is green, whatever the gauge read last: see Pulse.)
            if (well.Seated || well.Seating || outline == null) return;
            tint = state;
            if (state == FitState.Idle)
            {
                outline.sharedMaterial = paint;
                rim.sharedMaterial = paint;
                foreach (MeshRenderer tape in tapes) tape.sharedMaterial = paint;
            }
            else Show(state == FitState.Good ? Palette.Go : Wrong);
        }

        void Show(Signal signal)
        {
            if (outline == null) return;
            Material lit = Materials.Gadget(signal);
            outline.sharedMaterial = lit;
            rim.sharedMaterial = lit;
            Materials.SetEmission(outline, signal.Emission);
            Materials.SetEmission(rim, signal.Emission);
            bool go = signal.Equals(Palette.Go);
            foreach (MeshRenderer tape in tapes)
            {
                tape.sharedMaterial = go ? lit : paint;
                if (go) Materials.SetEmission(tape, signal.Emission);
            }
        }

        void Pulse(float seconds)
        {
            if (outline == null || well.Seated) return;
            if (well.Seating)
            {
                // The well has taken it: green from that moment on, while it slides in and falls. (The gauge
                // reads the hold one tick late: after a quick aim its last word may have been "too small".)
                if (tint != FitState.Good)
                {
                    tint = FitState.Good;
                    Show(Palette.Go);
                }
                // And so is the lamp: the gauge's own goes dark the moment nothing is held.
                if (!pluggedLamp.activeSelf)
                {
                    if (gauge.Lamp != null && gauge.Lamp.Renderer != null) gauge.Lamp.Renderer.gameObject.SetActive(false);
                    pluggedLamp.SetActive(true);
                }
                return;
            }
            if (tint == FitState.Idle || tint == FitState.Good) return;
            Color emission = Wrong.EmissionAt(Wrong.GainAt(seconds));
            Materials.SetEmission(outline, emission);
            Materials.SetEmission(rim, emission);
        }

        static MeshRenderer Visual(GameObject root, string name, Mesh mesh, Material material)
        {
            var visual = new GameObject(name);
            visual.transform.SetParent(root.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        static void Box(GameObject root, string name, Vector3 centre, Vector3 size)
        {
            var holder = new GameObject(name) { layer = Layers.Default };
            holder.transform.SetParent(root.transform, false);
            holder.transform.localPosition = centre;
            holder.AddComponent<BoxCollider>().size = size;
        }

        // ---- The intended solution -------------------------------------------------------------------------

        public override IEnumerator Solve(Bot bot)
        {
            Game game = bot.Game;

            // The thimble is a step away: picked up from here it is as big on screen as it will ever need to be.
            yield return bot.Grab(thimble);
            // A few steps past the spool, cover the outline on the far wall and let go. The thimble stops on
            // that wall, eleven units wide; the well takes it.
            yield return bot.WalkTo(StandPoint);
            yield return bot.DropAt(OutlineCentre);
            yield return bot.Until(() => well.Seated, 5f);

            // Across its top and out through the little door.
            yield return bot.WalkTo(new Vector3(0f, 0f, AxisZ + SiloRadius - 1f), 0.3f, 12f);
            yield return bot.WalkTo(new Vector3(0f, 0f, ExitCentre.z));
            yield return bot.Until(() => game.LevelCompleted, 3f);
        }
    }
}
