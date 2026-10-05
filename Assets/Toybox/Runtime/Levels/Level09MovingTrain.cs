using System.Collections;
using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Levels
{
    /// <summary>
    /// Level 9, "The Moving Train" (LEVELS.md): timing - let go onto a moving target.
    ///
    /// A toy train runs a loop on trestles round a lighthouse lamp; the station is the top of a stack of
    /// books, and there is nothing under the track but a fourteen-unit drop. The one toy is a plank on a
    /// spool. Held end-on from the platform edge against the lighthouse it stretches all the way back to
    /// the track (the lighthouse wall sets its size: 4.0, twelve units long); let go while the wagons pass
    /// underneath, its near end lands on a wagon whose velcro bed grips it, and it becomes a spoke that
    /// sweeps round the lighthouse with the train. Ride the train out to it and walk in.
    ///
    /// The clamp (4.4, 13.2 long) is exactly the distance from the platform to the doorstep, so the plank
    /// can never rest on both. Let go with nothing under its near end it tips off the doorstep, falls and
    /// is back on its spool. Every try is decided within two and a half seconds - gripped, fallen, or sent
    /// back - and each way of getting it wrong has its own line.
    ///
    /// A second way in, which LEVELS accepts: carried onto a wagon and put down there pointing at the
    /// lighthouse, the plank is a pier that rides the train, and a sprint jump off its end reaches the
    /// doorstep.
    ///
    /// (Where this differs from LEVELS.md, and what was measured: tools/out/notes/level09-build.md and
    /// level09-review.md.)
    /// </summary>
    [Level(9, "moving-train", "The Moving Train", Phase = 2)]
    public sealed class Level09MovingTrain : LevelDefinition
    {
        // ---- The layout (LEVELS.md, Level 9). Platform top y = 0, travel +Z. ---------------------------------
        public const float StationHalfWidth = 9f, StationBack = -10f, StationEdge = 3.3f;
        /// <summary>The shelf the whole set stands on, and what closes the level.</summary>
        public const float ShelfY = -14f, WallTop = 16f, WallX = 20f, WallFar = 40f, WallThickness = 1f;
        /// <summary>The lighthouse stands on the axis x = 0, z = AxisZ; radii are measured from it.</summary>
        public const float AxisZ = 20f;
        public static readonly Vector3 Axis = new Vector3(0f, 0f, AxisZ);
        public const float TowerRadius = 2f, RingRadius = 3.5f, RingTop = -1f, RingThickness = 0.5f;
        /// <summary>
        /// The track. (LEVELS says radius 14, which leaves 1.2 between the platform and the decks. Measured:
        /// the feet are pulled down as they roll off an edge, so a step off the platform at a walk comes down
        /// 0.97 out - in that gap - and only a sprint reaches the deck, by 0.2. At 14.6 the gap is 0.6 and a
        /// step off lands on a wagon either way; the platform, the doorstep and the plank's clamp stay as
        /// they are, and the decks are 9.6 from the doorstep instead of 9.)
        /// </summary>
        public const float TrackRadius = 14.6f, DeckY = -1f;
        public const float DeckInner = TrackRadius - ToyFactory.TrainWidth * 0.5f, DeckOuter = TrackRadius + ToyFactory.TrainWidth * 0.5f;
        /// <summary>Degrees of the circle a wagon takes: 3.4 units of track for a deck of 3.2, as on LEVELS' smaller circle.</summary>
        public const float TrainSpeed = 24f, TrainStart = -150f, EngineArc = 16f, CarArc = 13.4f;
        /// <summary>What the velcro grips: nothing that would cut into the lighthouse or sweep the platform.</summary>
        public const float GripMinRadius = 1.95f, GripMaxRadius = 16.5f;

        // ---- The plank ---------------------------------------------------------------------------------------
        public const float PlankStart = 0.65f, PlankMin = 0.3f, PlankMax = 4.4f;
        public const string PlankTag = "plank";
        /// <summary>The size the painted outline on the lighthouse is drawn for (the solver's plank).</summary>
        public const float IntendedScale = 4.03f;
        public static readonly Vector3 SpoolBase = new Vector3(2.5f, 0f, -1f);
        public const float SpoolRadius = 0.4f, SpoolHeight = 1.1f;
        /// <summary>The plank lies on the spool pointing at the lighthouse.</summary>
        public static readonly float PlankYaw = Mathf.Atan2(Axis.x - SpoolBase.x, Axis.z - SpoolBase.z) * Mathf.Rad2Deg;
        /// <summary>How far behind the plank's middle the player starts: a step, looking along it.</summary>
        public const float PickupDistance = 1.4f;
        public static readonly Vector3 Spawn = SpoolBase - Quaternion.Euler(0f, PlankYaw, 0f) * Vector3.forward * PickupDistance;
        public const float SpawnPitch = -11f;

        /// <summary>
        /// Where the solver lets go from, and what it looks at: the foot of the lighthouse door, where the
        /// painted outline is. (LEVELS aims above the door, at y 0.9. That works as well - anything from the
        /// doorstep to halfway up the lighthouse does - but it puts the plank at the height of the eye, and
        /// end-on at eye height it is a yellow rectangle that hides the door, the doorstep and the track.
        /// Aimed at the foot of the door the plank lies below the eye and is seen from above, from the
        /// player's feet all the way to the lighthouse.)
        /// </summary>
        public static readonly Vector3 EdgeSpot = new Vector3(0f, 0f, 2.9f);
        public static readonly Vector3 AimPoint = new Vector3(0f, -0.75f, AxisZ - TowerRadius);
        /// <summary>The solver's way round the spool.</summary>
        public static readonly Vector3 AroundSpool = new Vector3(1.3f, 0f, -1.6f);
        /// <summary>
        /// The exit, as LEVELS has it. Its footprint lies inside the doorstep's (but for 0.04 at its four
        /// corners), so nobody below the doorstep's height can touch it: the doorstep's rim is in the way.
        /// (Measured: sprint jumps that end short slide down that rim 3.7 from the axis and fall.)
        /// </summary>
        public static readonly Vector3 ExitCentre = new Vector3(0f, 0.25f, AxisZ);
        public static readonly Vector3 ExitSize = new Vector3(5f, 3.5f, 5f);

        /// <summary>
        /// Apparent size (scale / pick-up distance) outside which the plank cannot be made to reach from the
        /// lighthouse back to the track from anywhere on the platform. From the start it is 0.45. Smaller
        /// than FarRatio it stays too short even from the back wall; bigger than NearRatio it is at its
        /// full size before it gets far enough.
        /// </summary>
        public const float FarRatio = 0.2f, NearRatio = 0.6f;
        /// <summary>A plank let go smaller than this goes back to its spool: nobody can pick it up from close enough.</summary>
        public const float CrumbScale = 0.5f;
        /// <summary>
        /// A plank on a wagon whose inner end is no farther from the lighthouse than this can be jumped from
        /// with room to spare. (Measured: a sprint jump off an inner end at r 7.4 lands on the doorstep, one
        /// off an inner end at r 9.0 ends 3.7 from the axis and falls; the plank's end travels sideways at
        /// 0.42 units a second per unit of radius, and the jumper keeps that.)
        /// </summary>
        public const float DivingReach = 7.5f;
        /// <summary>A plank whose middle is below this is on its way to the shelf.</summary>
        public const float FallY = -4f;
        /// <summary>Ticks after which a plank let go over the drop that no wagon has gripped goes back to its spool.</summary>
        public const int LimboTicks = 150;

        /// <summary>Said on a pick-up from far off. The words are Level 1's, on purpose.</summary>
        public const string FarLine = "It only ever gets as big as it looks. Pick it up from closer.";
        public const string NearLine = "From this close it looks too big: it stops growing before it gets there. Pick it up from a step back.";
        public const string ShortLine = "Too short to reach back to the track. Hold it and step back: it grows.";
        /// <summary>Too short, and let go from a wagon: there is no stepping back on a wagon.</summary>
        public const string TrainShortLine = "Too short from here: the train is nearer the lighthouse than the platform is. Hold it from the platform edge.";
        public const string MissedLine = "Nothing was under its near end. Let go while the wagons are passing underneath.";
        public const string ReachLine = "It has to reach from the track to the doorstep. Turn it end-on and hold it against the lighthouse.";
        /// <summary>End-on and at its full size, but let go from so far back that it hung short of the lighthouse.</summary>
        public const string FullSizeLine = "It was at its full size before it touched the lighthouse. Let go from the platform edge.";
        /// <summary>Let go below the wagons' decks: against the lighthouse under the doorstep.</summary>
        public const string UnderLine = "That was below the train. Hold it against the lighthouse above the doorstep.";
        public const string CrumbLine = "That plank was too small to use. It is back on its spool.";
        public const string StrandedLine = "Out of reach there. The plank is back on its spool.";
        /// <summary>
        /// Said after a hop from a standstill against the back of the plank that rides ahead, when it came
        /// down beside the plank again. In the air nobody gains on a train that goes faster than they walk,
        /// so that hop comes down where it went up.
        /// </summary>
        public const string HopLine = "A hop from a standstill will not get you up while the train is moving. Take a run at it.";
        /// <summary>
        /// Said to somebody who jumped off the platform at the train and went over it. (Measured: a jump at
        /// a walk from the last 0.3 of the edge, and any jump at a run from within 2.5 of it, clears the
        /// three-unit deck; a step off the edge lands on it.)
        /// </summary>
        public const string LeapLine = "A jump from the platform carries you clean over the train. Step off the edge instead.";

        Prop plank;
        Train train;
        PropCarrier carrier;
        PropLeash crumbs, stranded;
        Exit exit;
        MeshRenderer outline;
        string lastLine;
        int lastSaid, crumbTicks;
        /// <summary>The plank has been let go over the drop and nothing has been said about it yet.</summary>
        bool pending;
        // The plank as it was let go: how near its centre line came to the axis, how far its farthest corner
        // was from it, the height of its top, whether it pointed at the lighthouse, and whether the player
        // stood on the train.
        float dropInner, dropOuter, dropTop;
        bool dropAimed, dropFromTrain;
        int dropTick;
        // The tick of a hop at the back of the riding plank (-1: none), and of a jump off the platform toward
        // the track (-1: none) with whether a wagon was under it when it came down past the decks.
        int hopTick, leapTick;
        bool leapOverTrain;

        public Prop Plank => plank;
        public Train Train => train;
        public PropCarrier Carrier => carrier;
        /// <summary>Brings a plank that is too small to use back to the spool.</summary>
        public PropLeash Crumbs => crumbs;
        /// <summary>Brings a plank that lies where nobody can get at it back to the spool.</summary>
        public PropLeash Stranded => stranded;
        public Exit Exit => exit;
        /// <summary>The dashed outline of the plank's end on the lighthouse wall.</summary>
        public Renderer Outline => outline;

        /// <summary>True while the plank rides a wagon with its inner end over the doorstep: the bridge.</summary>
        public bool Bridged
        {
            get
            {
                if (plank == null || carrier == null || !carrier.IsCarrying(plank)) return false;
                Reach(out float inner, out _);
                return inner < RingRadius;
            }
        }

        /// <summary>Where on the track the plank's outer end is: its bearing (0 at the station, growing toward +X).</summary>
        public float PlankBearing
        {
            get
            {
                Ends(out _, out Vector3 outer);
                return BearingOf(outer);
            }
        }

        public override string Blurb => "The train never stops. Neither should your bridge.";

        public override string[] Hints => new[]
        {
            "You can ride the train, but it never gets closer to the lighthouse. Something has to reach from the train to the doorstep, and travel with it.",
            "Hold the plank end-on against the lighthouse wall. From the platform edge it stretches all the way back to the track. It needs something under its near end when you let go.",
            // Five lines of the pause card's hint panel at full size (Phase2CampaignTests.EveryHint_FitsThePauseCardsPanel_AtFullSize).
            "Pick the plank up from a step behind it. At the platform edge, press it to the foot of the lighthouse door and let go as the wagons pass. Step down onto one, take a running jump onto the plank, walk in.",
        };

        public override string Environment => "high-shelf";

        // The platform stands fourteen units above the shelf, and the kill plane two above that: nothing is
        // seen to hit the shelf.
        public override float GroundY => ShelfY;
        public override float KillY => -12f;

        /// <summary>Distance from the lighthouse's axis, in plan.</summary>
        public static float RadiusOf(Vector3 point) => new Vector2(point.x - Axis.x, point.z - Axis.z).magnitude;

        /// <summary>The train's bearing of a point: 0 toward the station (-Z), growing toward +X.</summary>
        public static float BearingOf(Vector3 point) => Mathf.Atan2(point.x - Axis.x, Axis.z - point.z) * Mathf.Rad2Deg;

        /// <summary>The middles of the plank's two ends: the one nearer the lighthouse, and the other.</summary>
        public void Ends(out Vector3 inner, out Vector3 outer)
        {
            Vector3 centre = plank.Center;
            Vector3 half = plank.Rotation * Vector3.forward * (ToyFactory.PlankSize.z * 0.5f * plank.Scale);
            Vector3 a = centre + half, b = centre - half;
            bool first = RadiusOf(a) <= RadiusOf(b);
            inner = first ? a : b;
            outer = first ? b : a;
        }

        /// <summary>
        /// How near the plank's centre line comes to the lighthouse's axis, and how far its far end is from
        /// it (both in plan). A plank that spans from the doorstep to the track has inner below 3.5 and
        /// outer above 12.5.
        /// </summary>
        public void Reach(out float inner, out float outer)
        {
            Ends(out Vector3 a, out Vector3 b);
            var p = new Vector2(a.x - Axis.x, a.z - Axis.z);
            var q = new Vector2(b.x - Axis.x, b.z - Axis.z);
            Vector2 along = q - p;
            float t = along.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector2.Dot(-p, along) / along.sqrMagnitude) : 0f;
            inner = (p + along * t).magnitude;
            outer = Mathf.Max(p.magnitude, q.magnitude);
        }

        /// <summary>
        /// The wagon (1 is the one behind the engine) whose middle will be within <paramref name="margin"/>
        /// degrees of a bearing in so many seconds, or 0 if none will.
        /// </summary>
        public int WagonAt(float bearing, float seconds, float margin)
        {
            for (int car = 1; car <= train.Cars; car++)
                if (Mathf.Abs(Mathf.DeltaAngle(train.CarBearing(car) + TrainSpeed * seconds, bearing)) <= margin) return car;
            return 0;
        }

        public override void Build(LevelContext ctx)
        {
            plank = null;
            train = null;
            carrier = null;
            crumbs = null;
            stranded = null;
            exit = null;
            outline = null;
            lastLine = null;
            lastSaid = -100000;
            crumbTicks = 0;
            pending = false;
            dropInner = dropOuter = dropTop = 0f;
            dropAimed = dropFromTrain = false;
            dropTick = 0;
            hopTick = leapTick = -1;
            leapOverTrain = false;

            Dip dip = ctx.Dip;
            BuildStation(ctx, dip);
            BuildLighthouse(ctx, dip);
            BuildTrestles(ctx);
            BuildWalls(ctx, dip);
            BuildPaint(ctx, dip);
            BuildDressing(ctx);
            SkyCap.Add(ctx, -WallX - WallThickness, WallX + WallThickness, StationBack - WallThickness, WallFar + WallThickness, WallTop);

            // The plank on its spool, pointing at the lighthouse. It stays put until it is picked up: nobody
            // knocks it off the spool by walking into it.
            ctx.AddStatic(ToyFactory.ThreadSpool(SpoolRadius, SpoolHeight, grabbable: false), SpoolBase + Vector3.up * (SpoolHeight * 0.5f));
            ToyDef def = ToyCatalog.Get(ToyId.Plank);
            plank = ToyCatalog.Add(ctx, ToyId.Plank, SpoolBase + Vector3.up * (SpoolHeight + def.RestHeight * PlankStart + 0.002f),
                Quaternion.Euler(0f, PlankYaw, 0f), PlankStart, null, o =>
                {
                    o.MinScale = PlankMin;
                    o.MaxScale = PlankMax;
                    o.Tags = new[] { PlankTag };
                    o.FrozenUntilGrabbed = true;
                });

            // The engine is what arrives first, and it has no velcro. With ordinary friction a plank let go
            // onto its roof rode along on it for five to nine seconds, looking like a bridge, before it slid
            // off and fell (measured). This level's engine is slick instead: the plank stays where it was let
            // go (its other end is on the doorstep), the engine runs out from under it, and its near end
            // comes down on the first wagon, whose bed grips it. (The player's feet have no friction of their
            // own: nothing changes for somebody who stands on the engine.)
            // Its funnel is looks only. A plank held against the foot of the lighthouse door passes 0.9 above
            // the wagons but only just over the engine's roof; the funnel, 0.4 higher, stopped it eight units
            // long for the half second it took to pass (measured), and let go then it fell.
            var slick = new PhysicsMaterial("Level09 Engine")
            {
                dynamicFriction = 0f, staticFriction = 0f, bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            ctx.OnDispose(() => Sim.Destroy(slick));
            train = new Train(ctx, new TrainOptions
            {
                Name = "Train", Center = new Vector2(Axis.x, Axis.z), Radius = TrackRadius, DeckY = DeckY, AngularSpeed = TrainSpeed,
                StartBearing = TrainStart, EngineArc = EngineArc, CarArc = CarArc, Cars = 8, StationBearing = 0f,
                EngineFactory = () =>
                {
                    GameObject engine = ToyFactory.TrainEngine();
                    foreach (BoxCollider box in engine.GetComponentsInChildren<BoxCollider>())
                    {
                        box.sharedMaterial = slick;
                        if (box.center.y + box.size.y * 0.5f > ToyFactory.TrainEngineHeight + 0.05f) box.enabled = false;
                    }
                    return engine;
                },
            });
            carrier = new PropCarrier(ctx, new PropCarrierOptions
            {
                Name = "Velcro", Beds = train, AcceptTag = PlankTag, FlatDot = 0.94f,
                MinRadius = GripMinRadius, MaxRadius = GripMaxRadius, RadiusCenter = new Vector2(Axis.x, Axis.z),
            });

            // "Closer" has to be possible: a plank let go at a crumb's size looks too small from anywhere it
            // can lie, so it goes back to the spool.
            crumbs = new PropLeash(ctx, new PropLeashOptions
            {
                Name = "crumb", Props = new[] { plank },
                Forbidden = new[] { Zone.MinMax(new Vector3(-100f, -100f, -100f), new Vector3(100f, 100f, 100f)) },
                Unless = () => plank.Scale >= CrumbScale,
                Grace = 1.5f,
            });
            crumbs.PropReturned += prop => Say(ctx, CrumbLine);
            // And one that has come to rest off the platform without a wagon under it - on the doorstep,
            // behind the lighthouse - is out of reach: back to the spool as well.
            stranded = new PropLeash(ctx, new PropLeashOptions
            {
                Name = "stranded", Props = new[] { plank },
                Allowed = new[]
                {
                    Zone.MinMax(new Vector3(-StationHalfWidth - 0.6f, -1f, StationBack - 0.6f), new Vector3(StationHalfWidth + 0.6f, WallTop + 1f, StationEdge + 0.4f)),
                },
                RestSpeed = 0.4f, Grace = 2f,
            });
            stranded.PropReturned += prop => Say(ctx, StrandedLine);

            // What the player is told, each at the moment it happens. The plank only ever gets as big as it
            // looks: taken from far off it cannot be made long enough, taken from touching distance it is at
            // its full size before it reaches. Both are said on the pick-up.
            ctx.Game.Events.PropGrabbed += e =>
            {
                if (e.Prop != plank) return;
                pending = false;
                float ratio = e.OldScale / Mathf.Max(0.01f, e.GrabDistance);
                if (ratio < FarRatio) Say(ctx, FarLine, 6f);
                else if (ratio > NearRatio) Say(ctx, NearLine, 6f);
            };
            ctx.Game.Events.PropDropped += e =>
            {
                if (e.Prop != plank || plank.Removed) return;
                Reach(out dropInner, out _);
                dropOuter = FarthestCorner();
                dropAimed = PointsAtTheLighthouse();
                GadgetKit.VerticalExtent(plank, out _, out dropTop);
                Vector3 feet = ctx.Game.Player.Position;
                dropFromTrain = feet.y < DeckY + 0.6f && RadiusOf(feet) > DeckInner - 0.5f && RadiusOf(feet) < DeckOuter + 0.5f;
                dropTick = ctx.Ticks;
                // Only a plank let go out over the drop is a try at the bridge.
                pending = plank.Center.z > StationEdge + 0.2f || Mathf.Abs(plank.Center.x) > StationHalfWidth + 0.2f;
            };
            ctx.Game.Events.PlayerJumped += e =>
            {
                Player player = ctx.Game.Player;
                // Off the platform toward the track: a jump goes over the train (see LeapLine). Watch says so
                // once it has.
                if (e.Position.y > -0.3f && e.Position.z > StationEdge - 3f && e.Position.z < StationEdge + 0.5f && Mathf.Abs(e.Position.x) < StationHalfWidth)
                {
                    leapTick = ctx.Ticks;
                    leapOverTrain = false;
                }
                // On the train, a hop at the plank from the wagon behind it, from a standstill: it cannot work
                // (see HopLine), and nothing about it looks as if it could not. Said when it has failed.
                if (plank == null || plank.Removed || !carrier.IsCarrying(plank) || e.Position.y > DeckY + 0.2f) return;
                float gap = GapToPlank(player, out Vector3 away, out float closing);
                if (gap > player.Radius + 0.25f || closing > 2f) return;
                // Only from behind: from the wagon in front a hop works as on standing ground.
                if (Vector3.Dot(away, Level09Shapes.Tangent(BearingOf(player.Position))) > -0.5f) return;
                hopTick = ctx.Ticks;
            };
            ctx.Game.Events.PlayerLanded += e =>
            {
                leapTick = -1;
                if (hopTick < 0) return;
                bool late = ctx.Ticks - hopTick > 120;
                hopTick = -1;
                if (late || plank == null || plank.Removed || e.Prop == plank || !carrier.IsCarrying(plank)) return;
                Say(ctx, HopLine, 6f);
            };
            ctx.Game.Events.PlayerRespawned += e => hopTick = leapTick = -1;
            // Back on its spool, whatever brought it there: that try is over.
            ctx.Game.Events.PropRespawned += e =>
            {
                if (e.Prop == plank) pending = false;
            };
            ctx.OnUpdate(dt => Watch(ctx));

            exit = ctx.AddExit(ExitCentre, ExitSize);
            ctx.SetSpawn(Spawn, PlankYaw, SpawnPitch);
        }

        // ---- What is said --------------------------------------------------------------------------------------

        // Every tick: one line about a try that has just been decided, and the crumb that rides a wagon.
        void Watch(LevelContext ctx)
        {
            WatchLeap(ctx);
            if (plank == null || plank.Removed) return;
            bool carried = carrier.IsCarrying(plank);

            // The leash does not see a carried prop: a crumb on a wagon is sent back from here.
            crumbTicks = carried && plank.Scale < CrumbScale ? crumbTicks + 1 : 0;
            if (crumbTicks >= 90)
            {
                crumbTicks = 0;
                pending = false;
                plank.Respawn();
                Say(ctx, CrumbLine);
                return;
            }

            // The kill plane takes a toy whose middle is below it. This one is up to thirteen units long: end
            // down it would stand on the shelf with its middle well above the plane, in full view. It is gone
            // as soon as its lowest point is.
            if (!plank.Held && !plank.Driven && !plank.Frozen && plank.Center.y < FallY)
            {
                GadgetKit.VerticalExtent(plank, out float bottom, out _);
                if (bottom < ctx.Game.KillY - 0.5f)
                {
                    Judge(ctx);
                    plank.Respawn();
                    return;
                }
            }

            if (!pending || plank.Held) return;
            if (carried)
            {
                // On a wagon. If it neither reaches the doorstep nor comes near enough to jump from, say so.
                pending = false;
                Reach(out float inner, out _);
                if (inner > DivingReach && plank.Scale >= CrumbScale) Say(ctx, ReachLine);
                return;
            }
            // Every try is decided within two and a half seconds: gripped by a wagon, fallen, or - shoved
            // along in front of the engine, lying loose across a wagon because it sticks out too far for the
            // velcro - neither. A plank in between looks like a bridge and is none; it goes back to the spool
            // with the line for what was wrong. (Off the engine's roof it is on the first wagon in 1.5 s.)
            bool limbo = ctx.Ticks - dropTick >= LimboTicks && !plank.Driven && !plank.Frozen;
            if (plank.Center.y > FallY && !limbo) return;
            Judge(ctx);
            if (limbo && plank.Center.y > FallY) plank.Respawn();
        }

        // The plank is falling to the shelf. Why: it was let go below the train; it was not over the doorstep
        // at all, or stuck out too far to be gripped (end-on and at its full size: let go from too far back);
        // it did not reach back to the track; or no wagon was there. Said once per try.
        void Judge(LevelContext ctx)
        {
            if (!pending) return;
            pending = false;
            if (plank.Scale < CrumbScale) return;
            if (dropTop < DeckY) Say(ctx, UnderLine);
            else if (dropInner > RingRadius - 0.2f || dropOuter > GripMaxRadius)
                Say(ctx, dropAimed && plank.Scale > PlankMax - 0.01f ? FullSizeLine : ReachLine);
            else if (dropOuter < DeckInner + 0.2f) Say(ctx, dropFromTrain ? TrainShortLine : ShortLine);
            else Say(ctx, MissedLine);
        }

        // Somebody who jumped off the platform toward the track: once they are below the decks it is known
        // whether they went over a train that was there.
        void WatchLeap(LevelContext ctx)
        {
            if (leapTick < 0) return;
            Player player = ctx.Game.Player;
            Vector3 feet = player.Position;
            if (player.Grounded || ctx.Ticks - leapTick > 180)
            {
                leapTick = -1;
                return;
            }
            // Coming down past the decks' height: was there a train to land on?
            if (feet.y < DeckY + 0.5f && feet.y > DeckY - 0.3f && !leapOverTrain)
            {
                float bearing = BearingOf(feet);
                leapOverTrain = WagonAt(bearing, 0f, CarArc * 0.5f + 1f) != 0 || Mathf.Abs(Mathf.DeltaAngle(train.EngineBearing, bearing)) < EngineArc * 0.5f;
            }
            if (feet.y > DeckY - 0.6f) return;
            leapTick = -1;
            // Inside the track's circle with a train there: over it.
            if (leapOverTrain && RadiusOf(feet) < DeckInner + 0.15f) Say(ctx, LeapLine, 6f);
        }

        /// <summary>How far the plank's farthest corner is from the lighthouse's axis, in plan (what the velcro's limit is measured on).</summary>
        public float FarthestCorner()
        {
            Vector3 half = plank.LocalHalfExtents * plank.Scale;
            Vector3 centre = plank.Center;
            Quaternion rotation = plank.Rotation;
            float farthest = 0f;
            for (int corner = 0; corner < 8; corner++)
            {
                var offset = new Vector3((corner & 1) == 0 ? -half.x : half.x, (corner & 2) == 0 ? -half.y : half.y, (corner & 4) == 0 ? -half.z : half.z);
                farthest = Mathf.Max(farthest, RadiusOf(centre + rotation * offset));
            }
            return farthest;
        }

        // Does the plank's long axis, carried on, go through the lighthouse?
        bool PointsAtTheLighthouse()
        {
            Ends(out Vector3 a, out Vector3 b);
            var p = new Vector2(a.x - Axis.x, a.z - Axis.z);
            Vector2 along = new Vector2(b.x - a.x, b.z - a.z);
            if (along.sqrMagnitude < 1e-6f) return false;
            return Mathf.Abs(p.x * along.y - p.y * along.x) / along.magnitude < TowerRadius;
        }

        // One line, and not the same one over and over.
        void Say(LevelContext ctx, string text, float seconds = 5f)
        {
            if (text == lastLine && ctx.Ticks - lastSaid < 360) return;
            lastLine = text;
            lastSaid = ctx.Ticks;
            ctx.Say(text, seconds);
        }

        // ---- Geometry ----------------------------------------------------------------------------------------

        // A collider and nothing to see, like the sky cap.
        static void Solid(GameObject root, string name, Vector3 min, Vector3 max)
        {
            var holder = new GameObject(name) { layer = Layers.Default };
            holder.transform.SetParent(root.transform, false);
            holder.transform.localPosition = (min + max) * 0.5f;
            holder.AddComponent<BoxCollider>().size = max - min;
        }

        static Material Covers(Dip dip, PatternSpec pattern = default) => Materials.Room(RoomSurface.LevelStatic, dip, pattern);

        // The cut edges of a book: paper, a shade off the room's light tone on the sides.
        static Material Pages(Dip dip) => Materials.Room(new RoomRecipe
        {
            Name = dip.Name + " Pages", Top = Palette.Mix(dip.Light, Palette.Paper, 0.6f), Side = Palette.Mix(dip.Light, Palette.Paper, 0.35f),
            Dado = Palette.Mix(dip.Light, Palette.Paper, 0.35f),
        });

        // The station: the top of a stack of five big books, x -9..9, z -10..3.3, from the shelf up to y = 0.
        // One box holds everybody up; the books are what is seen (none of them sticks out of the box).
        void BuildStation(LevelContext ctx, Dip dip)
        {
            var root = new GameObject("Station") { layer = Layers.Default };
            Solid(root, "Stack", new Vector3(-StationHalfWidth, ShelfY, StationBack), new Vector3(StationHalfWidth, 0f, StationEdge));

            const float topBook = 2.4f;
            // The platform is the top book's cover: its own material, so that it can carry a pattern to pace on.
            Mesh platform = MeshKit.Cached("Level09/Platform", () =>
                MeshKit.Box(new Vector3(StationHalfWidth * 2f, 0.24f, StationEdge - StationBack)));
            GadgetKit.Visual(root.transform, "Platform", platform, Covers(dip, new PatternSpec(RoomPattern.Quilt, 2.5f, 0.06f, 0f, 0.05f)),
                new Vector3(0f, -0.12f, (StationBack + StationEdge) * 0.5f));

            Mesh covers = null, pages = null;
            covers = MeshKit.Cached("Level09/Stack Covers", () => BuildStack(topBook, true));
            pages = MeshKit.Cached("Level09/Stack Pages", () => BuildStack(topBook, false));
            GadgetKit.Visual(root.transform, "Covers", covers, Covers(dip));
            GadgetKit.Visual(root.transform, "Pages", pages, Pages(dip));
            ctx.AddStatic(root, Vector3.zero);
        }

        static Mesh BuildStack(float topBook, bool wantCovers)
        {
            var covers = new List<MeshPart>();
            var pages = new List<MeshPart>();
            float w = StationHalfWidth;
            // The top book is exactly the platform (its upper board is the Platform mesh).
            Level09Shapes.LyingBook(covers, pages, new Vector3(-w, -topBook, StationBack), new Vector3(w, -0.24f, StationEdge), -1, false);
            // Four more underneath, each a little smaller than the box and a little askew of the one above.
            float[] heights = { 3.2f, 2.6f, 3.1f, 2.7f };
            float[] left = { 0.5f, 0.15f, 0.7f, 0.1f }, right = { 0.2f, 0.6f, 0.15f, 0.1f }, front = { 0.45f, 0.15f, 0.6f, 0.1f }, back = { 0.1f, 0.25f, 0.1f, 0.1f };
            float y = -topBook;
            for (int i = 0; i < heights.Length; i++)
            {
                Level09Shapes.LyingBook(covers, pages, new Vector3(-w + left[i], y - heights[i], StationBack + back[i]),
                    new Vector3(w - right[i], y, StationEdge - front[i]), i % 2 == 0 ? 1 : -1);
                y -= heights[i];
            }
            Mesh mesh = Level09Shapes.Merge(wantCovers ? "Level09 Stack Covers" : "Level09 Stack Pages", wantCovers ? covers : pages);
            foreach (MeshPart part in wantCovers ? pages : covers) MeshKit.Release(part.Mesh);
            return mesh;
        }

        // The lighthouse: a sheer round tower of radius 2 from the shelf to the sky cap (that is all the
        // simulation knows of it), banded like a lighthouse, with the doorstep ring round it one unit below
        // the platform and its lamp above the cap, where nothing ever gets.
        void BuildLighthouse(LevelContext ctx, Dip dip)
        {
            var root = new GameObject("Lighthouse") { layer = Layers.Default };
            var hull = new GameObject("Tower") { layer = Layers.Default };
            hull.transform.SetParent(root.transform, false);
            hull.transform.localPosition = new Vector3(0f, (ShelfY + WallTop) * 0.5f, 0f);
            hull.transform.localScale = new Vector3(TowerRadius * 2f, WallTop - ShelfY, TowerRadius * 2f);
            MeshCollider tower = hull.AddComponent<MeshCollider>();
            tower.convex = true;
            tower.sharedMesh = MeshKit.UnitCylinderHull;

            // Bands: the room's deep tone up past the doors, then light and deep in turn.
            float[] cuts = { ShelfY, 2.8f, 7.2f, 11.6f, WallTop };
            Material deep = Covers(dip);
            Material light = Materials.Room(new RoomRecipe { Name = dip.Name + " Lighthouse Band", Top = dip.Light, Side = dip.Light, Dado = dip.Light });
            for (int band = 0; band + 1 < cuts.Length; band++)
            {
                float y0 = cuts[band], y1 = cuts[band + 1];
                Mesh wall = MeshKit.Cached(MeshKit.Key("Level09/Shaft", y0, y1), () =>
                    MeshKit.Lathe(new List<Vector2> { new Vector2(TowerRadius, y0), new Vector2(TowerRadius, y1) }, 48));
                GadgetKit.Visual(root.transform, "Shaft", wall, band % 2 == 0 ? deep : light);
            }

            // The doorstep: an annulus from the wall out to 3.5, its top one unit below the platform.
            var ring = new GameObject("Doorstep") { layer = Layers.Default };
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0f, RingTop - RingThickness * 0.5f, 0f);
            var ringHull = new GameObject("Collider") { layer = Layers.Default };
            ringHull.transform.SetParent(ring.transform, false);
            ringHull.transform.localScale = new Vector3(RingRadius * 2f, RingThickness, RingRadius * 2f);
            MeshCollider step = ringHull.AddComponent<MeshCollider>();
            step.convex = true;
            step.sharedMesh = MeshKit.UnitCylinderHull;
            Mesh disc = MeshKit.Cached("Level09/Doorstep", () => MeshKit.Cylinder(RingRadius, RingThickness, 48));
            GadgetKit.Visual(ring.transform, "Visual", disc, Covers(dip));
            // What it hangs on: a collar under it (looks only; nothing gets down there).
            Mesh collar = MeshKit.Cached("Level09/Collar", () => MeshKit.Lathe(new List<Vector2>
            {
                new Vector2(TowerRadius, -2.4f), new Vector2(RingRadius - 0.35f, -RingThickness * 0.5f),
            }, 48));
            GadgetKit.Visual(ring.transform, "Collar", collar, deep);

            // Above the sky cap: the gallery, the lamp and its roof. No colliders - no toy and no player
            // ever comes up here.
            Mesh gallery = MeshKit.Cached("Level09/Gallery", () => MeshKit.Lathe(new List<Vector2>
            {
                new Vector2(TowerRadius, 0f), new Vector2(2.9f, 0.25f), new Vector2(2.9f, 0.6f), new Vector2(1.7f, 0.6f),
            }, 48));
            GadgetKit.Visual(root.transform, "Gallery", gallery, deep, new Vector3(0f, WallTop, 0f));
            Mesh lamp = MeshKit.Cached("Level09/Lamp", () => MeshKit.Cylinder(1.5f, 2.6f, 32, 0.12f));
            Color glow = Palette.Lin(Palette.WarmWhite) * 2.2f;
            MeshRenderer lampRenderer = GadgetKit.Visual(root.transform, "Lamp", lamp, Materials.Emissive(ToyRecipe.Lamp, Palette.WarmWhite, glow),
                new Vector3(0f, WallTop + 0.6f + 1.3f, 0f));
            lampRenderer.shadowCastingMode = ShadowCastingMode.Off;
            Mesh roof = MeshKit.Cached("Level09/Roof", () => MeshKit.Lathe(new List<Vector2>
            {
                new Vector2(0f, 0f), new Vector2(2.4f, 0f), new Vector2(2.4f, 0.18f), new Vector2(0.2f, 1.9f), new Vector2(0f, 1.9f),
            }, 48));
            GadgetKit.Visual(root.transform, "Roof", roof, deep, new Vector3(0f, WallTop + 3.2f, 0f));

            ctx.AddStatic(root, new Vector3(Axis.x, 0f, Axis.z));
        }

        // Rails and trestles are looks only (LEVELS): the train's own presenter lays the rails and the
        // sleepers; this is what they stand on, down to the shelf, and the little bracket on the station's
        // face that carries the signal post.
        void BuildTrestles(LevelContext ctx)
        {
            Material wood = Materials.Toy(ToyRecipe.PlainProp, Palette.Birch);
            // The underside of the sleepers: the wheels end 0.84 below the deck, rail 0.1, sleeper 0.08.
            float top = DeckY - (ToyFactory.TrainDeckThickness + 0.1f + 0.34f) - 0.1f - 0.08f - 0.01f;
            float gauge = ToyFactory.TrainWidth * 0.5f - 0.3f;
            var trestles = new GameObject("Trestles");
            Mesh mesh = MeshKit.Cached(MeshKit.Key("Level09/Trestles", top), () => Level09Shapes.Trestles(TrackRadius, gauge, top, ShelfY, 30f, 15f));
            GadgetKit.Visual(trestles.transform, "Visual", mesh, wood);
            ctx.AddStatic(trestles, new Vector3(Axis.x, 0f, Axis.z));

            var bracket = new GameObject("Signal Bracket");
            Mesh ledge = MeshKit.Cached("Level09/Bracket", () => MeshKit.Box(new Vector3(0.8f, 0.14f, 0.6f)));
            GadgetKit.Visual(bracket.transform, "Visual", ledge, wood);
            ctx.AddStatic(bracket, new Vector3(0f, top - 0.06f, StationEdge + 0.3f));
        }

        // What closes the level: walls at x = +-20, z = -10 and z = 40, from the shelf to the sky cap. They
        // stop a held toy all the way up; what is drawn are rows of books standing on the shelf, low enough
        // on the sun's side (-X) that the light reaches the track.
        void BuildWalls(LevelContext ctx, Dip dip)
        {
            var root = new GameObject("Walls") { layer = Layers.Default };
            float x = WallX, t = WallThickness, back = StationBack, far = WallFar;
            Solid(root, "Wall -X", new Vector3(-x - t, ShelfY, back - t), new Vector3(-x, WallTop, far + t));
            Solid(root, "Wall +X", new Vector3(x, ShelfY, back - t), new Vector3(x + t, WallTop, far + t));
            Solid(root, "Wall Back", new Vector3(-x, ShelfY, back - t), new Vector3(x, WallTop, back));
            Solid(root, "Wall Far", new Vector3(-x, ShelfY, far), new Vector3(x, WallTop, far + t));

            Mesh covers = MeshKit.Cached("Level09/Wall Covers", () => BuildRows(true));
            Mesh pages = MeshKit.Cached("Level09/Wall Pages", () => BuildRows(false));
            GadgetKit.Visual(root.transform, "Covers", covers, Covers(dip));
            GadgetKit.Visual(root.transform, "Pages", pages, Pages(dip));
            ctx.AddStatic(root, Vector3.zero);
        }

        static Mesh BuildRows(bool wantCovers)
        {
            var covers = new List<MeshPart>();
            var pages = new List<MeshPart>();
            float x = WallX, t = WallThickness, back = StationBack, far = WallFar;
            // Depth of the rows: books are deeper than the wall is thick; they reach outward, away from the level.
            const float deep = 6f;
            Level09Shapes.StandingBooks(covers, pages, new Vector3(-x - deep, ShelfY, back - t), new Vector3(-x, 0f, far + t), false, 1, -1.5f, 2.2f, 0);
            Level09Shapes.StandingBooks(covers, pages, new Vector3(x, ShelfY, back - t), new Vector3(x + deep, 0f, far + t), false, -1, 2.5f, 2.6f, 3);
            Level09Shapes.StandingBooks(covers, pages, new Vector3(-x, ShelfY, far), new Vector3(x, 0f, far + deep), true, -1, 1.5f, 2.6f, 5);
            Level09Shapes.StandingBooks(covers, pages, new Vector3(-x, ShelfY, back - deep), new Vector3(x, 0f, back), true, 1, 5.5f, 2.6f, 8);
            Mesh mesh = Level09Shapes.Merge(wantCovers ? "Level09 Wall Covers" : "Level09 Wall Pages", wantCovers ? covers : pages);
            foreach (MeshPart part in wantCovers ? pages : covers) MeshKit.Release(part.Mesh);
            return mesh;
        }

        // Paint (no colliders). The campaign's size language: the plank's end at the size that works, dashed
        // on the wall it stops against, at the foot of the door: where its end comes to rest when it lies on
        // the doorstep. (The builder's outline was above the door, where LEVELS aims; aimed there the held
        // plank is end-on at eye height and hides it.) Where to stand is in the room's own tones, so that
        // the toy's colour is kept for where the toy goes.
        void BuildPaint(LevelContext ctx, Dip dip)
        {
            Color hero = ToyCatalog.Get(ToyId.Plank).ColorIn(dip);
            Material fill = Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Deep, hero, 0.3f)));
            Material line = Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Light, hero, 0.9f)));
            Material paper = Materials.Room(RoomRecipe.Solid(Palette.Paper));

            var paint = new GameObject("Lighthouse Paint");
            Vector3 end = ToyFactory.PlankSize * IntendedScale;
            float y0 = OutlineY - end.y * 0.5f, y1 = OutlineY + end.y * 0.5f, half = end.x * 0.5f;
            Mesh patch = MeshKit.Cached(MeshKit.Key("Level09/Outline Fill", y0, y1, half), () => Level09Shapes.WallPatch(TowerRadius + 0.02f, 0f, half * 2f, y0, y1));
            Flat(paint.transform, "Outline Fill", patch, fill);
            Mesh dashes = MeshKit.Cached(MeshKit.Key("Level09/Outline", y0, y1, half), () =>
            {
                var sides = new List<MeshPart>();
                void Side(float ax, float ay, float bx, float by) => sides.Add(new MeshPart(
                    Level09Shapes.WallDashes(TowerRadius + 0.028f, 0f, new List<Vector2> { new Vector2(ax, ay), new Vector2(bx, by) }, 0.09f, 0.3f, 0.17f), Matrix4x4.identity));
                Side(-half, y0, half, y0);
                Side(-half, y1, half, y1);
                Side(-half, y0, -half, y1);
                Side(half, y0, half, y1);
                return Level09Shapes.Merge("Level09 Outline", sides);
            });
            outline = Flat(paint.transform, "Outline", dashes, line);

            // A door on each of the four sides, with the exit's four panes for a window: whichever way the
            // plank points when the player walks in, the lighthouse says "this is the way out".
            Color exitGlow = Color.white * 3f;
            exitGlow.a = 1f;
            Material panes = Materials.Flat(new FlatRecipe { Name = "Level09 Door Light", Color = exitGlow, Shape = FlatShape.FourPane, Blend = FlatBlend.Alpha, Soft = 0.06f });
            for (int side = 0; side < 4; side++)
            {
                float bearing = side * 90f;
                Mesh door = MeshKit.Cached(MeshKit.Key("Level09/Door", bearing), () => Level09Shapes.WallPatch(TowerRadius + 0.012f, bearing, 1.3f, RingTop, RingTop + 1.8f));
                Flat(paint.transform, "Door", door, paper);
                Mesh window = MeshKit.Cached(MeshKit.Key("Level09/Door Light", bearing), () => Level09Shapes.WallPatch(TowerRadius + 0.02f, bearing, 0.8f, RingTop + 0.82f, RingTop + 1.62f));
                Flat(paint.transform, "Door Light", window, panes);
            }
            ctx.AddStatic(paint, new Vector3(Axis.x, 0f, Axis.z));

            // The platform's edge: a dashed line, as on any station.
            var edge = new GameObject("Platform Edge");
            Mesh edgeLine = MeshKit.Cached("Level09/Edge Line", () =>
            {
                var parts = new List<MeshPart>();
                const float dash = 0.9f, gap = 0.5f;
                int count = Mathf.FloorToInt((StationHalfWidth * 2f - 0.6f + gap) / (dash + gap));
                float start = -(count * (dash + gap) - gap) * 0.5f;
                for (int i = 0; i < count; i++)
                    parts.Add(new MeshPart(MeshKit.Grid(new Vector2(dash, 0.14f), 1, 1), new Vector3(start + dash * 0.5f + i * (dash + gap), 0f, 0f)));
                return Level09Shapes.Merge("Level09 Edge Line", parts);
            });
            Flat(edge.transform, "Paint", edgeLine, paper);
            ctx.AddStatic(edge, new Vector3(0f, 0.012f, StationEdge - 0.45f));

            // Where to stand: a pad with a pair of shoe prints behind the plank (take it from here and it
            // looks the right size) and at the edge in front of the lighthouse.
            Material pad = Materials.Room(RoomRecipe.Solid(dip.Deep));
            StandMark(ctx, "Stand Mark Spool", Spawn, PlankYaw, pad, paper);
            StandMark(ctx, "Stand Mark Edge", new Vector3(EdgeSpot.x, 0f, StationEdge - 1.25f), 0f, pad, paper);
        }

        /// <summary>
        /// The height of the middle of the painted outline: the end of a plank of the intended size that lies
        /// on the doorstep (a hair above it, so that the lowest dashes are not sunk in the doorstep).
        /// </summary>
        public static readonly float OutlineY = RingTop + ToyFactory.PlankSize.y * IntendedScale * 0.5f + 0.04f;
        const float PadRadius = 0.62f;

        static MeshRenderer Flat(Transform parent, string name, Mesh mesh, Material material)
        {
            MeshRenderer renderer = GadgetKit.Visual(parent, name, mesh, material);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return renderer;
        }

        // Looks only: a renderer for the pad and one for each shoe, toes a little apart.
        static void StandMark(LevelContext ctx, string name, Vector3 at, float yaw, Material pad, Material print)
        {
            var mark = new GameObject(name);
            ctx.AddStatic(mark, at + Vector3.up * 0.012f, Quaternion.Euler(0f, yaw, 0f));
            Mesh disc = MeshKit.Cached(MeshKit.Key("Level09/Pad", PadRadius), () => MeshKit.Cylinder(PadRadius, 0.004f, 40));
            GadgetKit.Visual(mark.transform, "Pad", disc, pad).shadowCastingMode = ShadowCastingMode.Off;
            Mesh sole = MeshKit.Cached(MeshKit.Key("Level09/Sole"), () =>
            {
                // A sole in the XY plane, toe up: half a circle for the toe, a smaller half for the heel.
                var shape = new List<Vector2>();
                for (int i = 0; i <= 12; i++)
                {
                    float a = Mathf.PI * i / 12f;
                    shape.Add(new Vector2(Mathf.Cos(a) * 0.16f, 0.17f + Mathf.Sin(a) * 0.16f));
                }
                for (int i = 0; i <= 12; i++)
                {
                    float a = Mathf.PI + Mathf.PI * i / 12f;
                    shape.Add(new Vector2(Mathf.Cos(a) * 0.115f, -0.2f + Mathf.Sin(a) * 0.115f));
                }
                return MeshKit.Extrude(shape, 0.004f);
            });
            for (int side = -1; side <= 1; side += 2)
            {
                Quaternion flat = Quaternion.Euler(0f, side * 9f, 0f) * Quaternion.Euler(90f, 0f, 0f);
                GadgetKit.Visual(mark.transform, "Shoe", sole, print, new Vector3(side * 0.24f, 0.006f, 0f), flat).shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // Things that make it a station: luggage left on the platform, well away from the way to the edge
        // and from anything a held plank passes over.
        void BuildDressing(LevelContext ctx)
        {
            ctx.AddStatic(ToyFactory.WoodenBlock(new Vector3(1.7f, 1.1f, 1.1f), grabbable: false), new Vector3(-6.4f, 0.55f, -8.2f), Quaternion.Euler(0f, 8f, 0f));
            ctx.AddStatic(ToyFactory.WoodenBlock(new Vector3(1.1f, 0.8f, 0.9f), grabbable: false), new Vector3(-6.5f, 1.5f, -8.25f), Quaternion.Euler(0f, -14f, 0f));
            ctx.AddStatic(ToyFactory.WoodenBlock(new Vector3(1f, 1f, 1f), grabbable: false), new Vector3(-4.6f, 0.5f, -8.6f), Quaternion.Euler(0f, 31f, 0f));
            ctx.AddStatic(ToyFactory.WoodenBlock(new Vector3(1.3f, 0.9f, 0.9f), grabbable: false), new Vector3(7.3f, 0.45f, -6.5f), Quaternion.Euler(0f, -20f, 0f));
            ctx.AddStatic(ToyFactory.Marker(0.4f, 3.4f), new Vector3(6.2f, 0.4f, -8.9f), Quaternion.Euler(0f, 97f, 0f));
        }

        // ---- The intended solution -------------------------------------------------------------------------

        /// <summary>Seconds a plank let go against the lighthouse takes to come down on a wagon's bed.</summary>
        public const float FallSeconds = 0.25f;
        /// <summary>Seconds from setting off at the platform's edge to standing on a wagon's deck.</summary>
        public const float StepSeconds = 0.3f;

        public override IEnumerator Solve(Bot bot)
        {
            // The plank is a step away, end-on: picked up from here it looks the right size.
            yield return bot.Grab(plank);
            // Round the spool to the platform edge, and press the plank against the lighthouse just above
            // the doorstep: it stretches all the way back to the track.
            yield return bot.WalkTo(AroundSpool, 0.3f);
            yield return bot.WalkTo(EdgeSpot, 0.15f);
            yield return bot.LookAt(AimPoint);
            // Let go when a wagon in the middle of the train will be under the near end as it comes down.
            yield return bot.Until(() => MiddleWagonComing(), 20f);
            yield return bot.DropAt(AimPoint);
            yield return bot.Until(() => carrier.IsCarrying(plank), 3f);
            yield return Board(bot);
            yield return WalkIn(bot);
        }

        /// <summary>Will a wagon in the middle of the train be under the plank's outer end when it has fallen?</summary>
        public bool MiddleWagonComing()
        {
            Ends(out _, out Vector3 outer);
            int car = WagonAt(BearingOf(outer), FallSeconds, 1.5f);
            return car >= 3 && car <= 6;
        }

        /// <summary>
        /// From the platform's edge onto the plank that rides the train: step down onto the wagon next to
        /// the plank's as it passes, walk along the train to the plank and hop onto it. (On the train
        /// nothing moves any more: the plank is simply a few steps along.)
        /// </summary>
        public IEnumerator Board(Bot bot)
        {
            yield return OntoTheTrain(bot);
            yield return OntoThePlank(bot);
        }

        /// <summary>
        /// From where the bot stands at the platform's edge down onto a wagon's deck, two to three units
        /// clear of the plank's side and behind it: from behind, a running jump cannot overshoot (in the air
        /// nobody gains on the train). Only if the train ends behind the plank does the bot come down in
        /// front of it. Then it stands still for a moment: somebody who has just come down on a moving wagon
        /// does not have its speed yet, and a jump taken at once is left behind by the train.
        /// </summary>
        public IEnumerator OntoTheTrain(Bot bot)
        {
            Player player = bot.Player;
            Vector3 edge = player.Position;
            float here = BearingOf(edge);
            // Degrees between the plank's middle and where the bot comes down, at the radius it comes down at.
            float half = ToyFactory.PlankSize.x * 0.5f * plank.Scale;
            float least = (half + 1.7f) / (DeckOuter - 0.5f) * Mathf.Rad2Deg, most = (half + 3.3f) / (DeckOuter - 0.5f) * Mathf.Rad2Deg;
            yield return bot.Until(() =>
            {
                // A deck under the bot when it is down?
                if (WagonAt(here, StepSeconds, CarArc * 0.5f - 2f) == 0) return false;
                // Where the plank will be then: so far ahead of the bot (behind it, if negative).
                float ahead = Mathf.DeltaAngle(here, PlankBearing + TrainSpeed * StepSeconds);
                if (ahead >= least && ahead <= most) return true;
                // In front of the plank only if there is no wagon to stand on behind it.
                bool room = WagonAt(PlankBearing - (least + most) * 0.5f, 0f, CarArc * 0.5f + (most - least) * 0.5f) != 0;
                return !room && -ahead >= least && -ahead <= most;
            }, 20f);
            // Off the edge at a run: a step off lands on the deck.
            Vector3 onto = edge + (Axis - edge).normalized * 2.6f;
            yield return Chase(bot, () => onto, () => player.Grounded && player.Position.y < -0.3f, 2f, true);
            yield return bot.Wait(SettleSeconds);
        }

        /// <summary>Seconds a rider who has just come down on a wagon needs to pick up its speed.</summary>
        public const float SettleSeconds = 0.3f;

        /// <summary>From a wagon's deck onto the plank that lies on the next wagon.</summary>
        public IEnumerator OntoThePlank(Bot bot)
        {
            Player player = bot.Player;
            // Along the train to the plank, and up onto it where it lies on its wagon: a running jump, taken
            // a good step before its side. (From a standstill against a toy that rides ahead there is no
            // getting up: in the air nobody gains on a train that goes faster than they walk. So a hop that
            // comes too late is followed by a few steps back and another run.)
            bool back = false;
            yield return Chase(bot, () =>
            {
                float gap = GapToPlank(player, out Vector3 away, out _);
                if (player.Grounded && player.GroundProp != plank)
                {
                    if (gap < 0.5f) back = true;
                    else if (gap > 1.6f) back = false;
                }
                if (!back) return OnPlank(RadiusOf(player.Position) - 0.6f);
                // Back along the train, away from the plank, and never off the deck.
                Vector3 along = Level09Shapes.Tangent(BearingOf(player.Position));
                return OnDeck(player.Position + along * (Vector3.Dot(away, along) > 0f ? 2f : -2f));
            }, () => player.GroundProp == plank, 8f, false, () =>
            {
                if (back || !player.Grounded || player.GroundProp == plank) return false;
                float gap = GapToPlank(player, out _, out float closing);
                return gap > 0.75f && gap < 1.15f && closing > 3.5f;
            });
        }

        // The point of a wagon's deck nearest to a point: its radius brought onto the decks, well clear of both edges.
        static Vector3 OnDeck(Vector3 point)
        {
            Vector3 outward = point - Axis;
            outward.y = 0f;
            float radius = Mathf.Clamp(outward.magnitude, DeckInner + 0.6f, DeckOuter - 0.6f);
            Vector3 on = Axis + outward.normalized * radius;
            on.y = DeckY;
            return on;
        }

        // How far the player's axis is from the plank's side (in plan, at the height of their shins), the
        // way from the plank to them, and how fast the two are closing.
        public float GapToPlank(Player player, out Vector3 away, out float closing)
        {
            Vector3 shin = player.Position + Vector3.up * 0.2f;
            Vector3 nearest = GadgetKit.ClosestPoint(plank, shin);
            away = shin - nearest;
            away.y = 0f;
            float gap = away.magnitude;
            away = gap > 1e-4f ? away / gap : -(Quaternion.Euler(0f, player.Yaw, 0f) * Vector3.forward);
            closing = -Vector3.Dot(player.Velocity - GadgetKit.PointVelocity(plank, nearest), away);
            return gap;
        }

        /// <summary>Inward along the turning plank: it always points at the lighthouse.</summary>
        public IEnumerator WalkIn(Bot bot)
        {
            Game game = bot.Game;
            Player player = bot.Player;
            // Along its middle; from its inner end straight at the lighthouse (down onto the doorstep, if it ends short).
            yield return Chase(bot, () =>
            {
                Ends(out Vector3 inner, out _);
                float at = RadiusOf(player.Position);
                return at > RadiusOf(inner) + 1.2f ? OnPlank(at - 1.5f) : Axis;
            }, () => game.LevelCompleted || RadiusOf(player.Position) < TowerRadius + 0.6f, 12f);
            yield return bot.Until(() => game.LevelCompleted, 3f);
        }

        /// <summary>
        /// The point on top of the plank's centre line that is so far from the lighthouse's axis (never
        /// beyond either end, and near the outer end never off the part that lies on the wagon).
        /// </summary>
        public Vector3 OnPlank(float radius)
        {
            Ends(out Vector3 inner, out Vector3 outer);
            float from = RadiusOf(inner), to = RadiusOf(outer);
            float most = Mathf.Max(from, to - 0.8f);
            float t = Mathf.Approximately(from, to) ? 0f : Mathf.InverseLerp(from, to, Mathf.Clamp(radius, from + 0.2f, most));
            return Vector3.Lerp(inner, outer, t) + Vector3.up * (ToyFactory.PlankSize.y * 0.5f * plank.Scale);
        }

        /// <summary>
        /// Walks toward a point that moves (a spot on the train, seen from the platform or from the train):
        /// every tick a fresh step toward where it is now, until <paramref name="done"/>. With
        /// <paramref name="jump"/>, a tap on the jump key whenever that says so. All of it is input.
        /// </summary>
        public static IEnumerator Chase(Bot bot, System.Func<Vector3> target, System.Func<bool> done, float seconds, bool sprint = false, System.Func<bool> jump = null)
        {
            for (int tick = Mathf.CeilToInt(seconds / Sim.Dt); tick > 0; tick--)
            {
                if (done()) yield break;
                if (jump != null && jump())
                {
                    yield return bot.Jump();
                    continue;
                }
                IEnumerator step = bot.WalkTo(target(), 0.05f, 1f, sprint);
                yield return step.MoveNext() ? step.Current : null;
            }
            if (done()) yield break;
            Vector3 at = bot.Player.Position;
            throw new BotException("Chase gave up after " + seconds.ToString("0.#") + " s [bot at (" + at.x.ToString("0.00") + ", " + at.y.ToString("0.00") + ", " +
                                   at.z.ToString("0.00") + "), t=" + bot.Game.Time.ToString("0.00") + " s]");
        }
    }
}
