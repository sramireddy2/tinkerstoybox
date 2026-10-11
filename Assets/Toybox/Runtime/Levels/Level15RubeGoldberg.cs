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
    /// Level 15, "The Rube Goldberg Machine" (LEVELS.md): everything at once, judged by running it.
    ///
    /// The strip of floorboards along the skirting board, at night, seen from the edge of a quilt.
    /// Somebody built a chain-reaction machine down there and left it unfinished: four painted outlines
    /// are empty. Four toys lie about, every one the wrong size - a domino set and a rubber ball far too
    /// small, a ruler about right and lying the wrong way round, a desk fan the size of a house. Each
    /// station takes its toy (a <see cref="Socket"/>), and a pedal on the quilt runs the machine:
    ///
    ///   pedal - gate - the BALL rolls down the slide, bounces off a drum over a wall of books and lands
    ///   on the RULER - the ruler flips a marble up through a hoop - the marble closes a switch - the FAN
    ///   blows down a duct - the puff turns the sail on the first DOMINO - seven dominoes fall, each
    ///   bigger than the last - the last swats a baseball off a shelf - the baseball drops through the
    ///   neck of a bell jar onto an alarm clock.
    ///
    /// The pedal can be pressed at any time. The motion runs as far as it can, stops visibly at the first
    /// wrong link, says which, and the machine sets itself up again with every toy where the player put
    /// it: running the machine is the diagnostic, never a softlock. Every motion of a run is a closed
    /// form (<see cref="PathDrive"/>, <see cref="Seesaw"/>, <see cref="HingeChain"/>); what decides pass
    /// or fail is a rule of scale:
    /// - the ball has to weigh enough to flip the marble through the hoop (the seesaw's f, mass = s^3);
    /// - the ruler has to reach under the ball and point its cap at the hoop;
    /// - the fan has to fit its garage and still blow hard enough (strength = (s / 0.6)^2);
    /// - the smallest domino has to stand in the wind while the biggest reaches the baseball.
    ///
    /// What differs from LEVELS.md is listed in tools/out/notes/level15-build.md; what the review found and
    /// changed after that (marked "the review" in the comments here), in tools/out/notes/level15-review.md.
    /// </summary>
    [Level(15, "rube-goldberg", "The Rube Goldberg Machine", Phase = 3)]
    public sealed partial class Level15RubeGoldberg : LevelDefinition
    {
        // ---- Layout (LEVELS.md, Level 15; the machine runs along +X at the foot of the back wall) ------------
        public const float HalfWidth = 28f, NearZ = -6f, EdgeZ = 11.5f, BandZ = 16.5f, BackZ = 20f;
        public const float GalleryTop = 3f, WallTop = 16f, WallThickness = 1f;
        /// <summary>The machine's centre line.</summary>
        public const float AxisZ = 18.25f;
        /// <summary>The ramp notch in the gallery: x 6..9, from the top at z 5.5 down to the trench at z 11.5.</summary>
        public const float RampMinX = 6f, RampMaxX = 9f, RampTopZ = 5.5f;

        public static readonly Vector3 PedalPoint = new Vector3(2f, GalleryTop, 9.5f);
        public const float PedalRadius = 0.7f;

        // ---- The slide: hopper, gate, chute --------------------------------------------------------------------
        public const float TowerMinX = -21f, TowerMaxX = -15f, TowerTop = 6f, HopperX = -18f;
        /// <summary>Where the lowest point of a seated ball is: the brink of the chute.</summary>
        public static readonly Vector3 HopperFoot = new Vector3(HopperX, TowerTop, AxisZ);
        /// <summary>The lip of the chute (the ball's lowest point leaves it here).</summary>
        public static readonly Vector3 ChuteLip = new Vector3(-14.5f, 3.55f, AxisZ);
        /// <summary>A rolling ball: a = (5 / 7) g sin(slope).</summary>
        public static readonly float ChuteAcceleration = 5f / 7f * Game.Gravity * (HopperFoot.y - ChuteLip.y) / Vector3.Distance(HopperFoot, ChuteLip);
        public const float GateX = -16.35f, GateSeconds = 0.3f;
        public const float HopperInner = 1.6f, HopperOuter = 1.75f, HopperRim = 6.4f;

        // ---- Drum, books, fulcrum --------------------------------------------------------------------------------
        public const float DrumX = -12.45f, DrumRadius = 1.75f, DrumTop = 1.2f, DrumRestitution = 0.88f;
        public const float BooksMinX = -9.7f, BooksMaxX = -8.7f, BooksTop = 2.4f;
        /// <summary>The pivot line: the top of the marker (it lies along Z).</summary>
        public static readonly Vector3 Pivot = new Vector3(-5f, 0.8f, AxisZ);
        public const float MarkerRadius = 0.4f, MarkerLength = 2f;
        /// <summary>The ruler lies with a third of its length on the short side: arms per unit of its scale.</summary>
        public const float LongArmPerScale = 8f / 3f, ShortArmPerScale = 4f / 3f, CentreArmPerScale = 2f / 3f;
        /// <summary>The height the ball's lowest point is handed back to physics at, over the short arm (its tip is at 1.2).</summary>
        public const float BallLandY = 1.25f;

        // ---- Hoop, control tower, garage, duct ------------------------------------------------------------------
        /// <summary>
        /// The hoop. LEVELS hangs it at x = -2.9; measured, the cap of a tipped ruler is at x = -3.93 (a ruler
        /// of 0.8) to -2.75 (1.2), so it hangs over the middle of that and every ruler the fulcrum takes
        /// shoots its marble up inside the ring.
        /// </summary>
        public static readonly Vector3 HoopCentre = new Vector3(-3.3f, 6f, AxisZ);
        public const float HoopRadius = 0.9f;
        /// <summary>
        /// The marble has to top out at least this high to drop through the hoop (LEVELS: 6.3; the builder: 6.2,
        /// with the gauge green from a ball of 2.15). Measured in the review, on rulers of 0.8 / 1.0 / 1.2:
        /// a ball of 2.15 tops out at 6.195 / 6.24 / 6.26 (at 6.2 the first fell short by 0.005 with the gauge
        /// green), one of 2.25 at 6.30 / 6.35 / 6.37, one of 2.0 at 6.00 / 6.04 / 6.06; and the dominoes at
        /// their largest, let go on the short arm instead, reach 6.15: LEVELS has no other toy heavy enough.
        /// So 6.25, and the gauge is green from LEVELS' own 2.25: clear of both by 0.05.
        /// </summary>
        public const float HoopNeed = 6.25f;
        public const float ControlMinX = -1.5f, ControlMaxX = 3.5f, ControlTop = 7f;
        public const float GarageMinX = 0.5f, GarageMaxX = 2f, GarageTop = 1.6f, GarageBackZ = 17.9f;
        public static readonly Vector3 FanSeat = new Vector3(1.25f, 0f, 17.2f);
        public static readonly Vector3 SwitchPoint = new Vector3(-1.66f, 4.4f, 16.34f);
        public static readonly Vector3 NozzleTip = new Vector3(8.5f, 5.6f, AxisZ);

        // ---- Plinth, lane, shelf, bell jar ------------------------------------------------------------------------
        public const float PlinthMinX = 8f, PlinthMaxX = 21f, LaneTop = 5f, LaneMinX = 8.5f, LaneEndX = 20f, WindMaxX = 13.5f;
        public const float ShelfMinX = 21.2f, ShelfMaxX = 24.5f, ShelfTop = 6.8f;
        public static readonly Vector3 BaseballCentre = new Vector3(22f, ShelfTop + ToyFactory.BaseballRadius, AxisZ);
        public const float JarX = 26.2f, JarRadius = 1.7f, JarTop = 5.2f, JarMouthY = 6f, JarMouthRadius = 1.3f, JarThroatRadius = 0.7f;
        public static readonly Vector3 ClockCentre = new Vector3(JarX, 1.4f, AxisZ);
        public static readonly Vector3 ClockSize = new Vector3(2f, 2.8f, 1f);

        public static readonly Vector3 ExitCentre = new Vector3(30f, 1.5f, 14f);
        public static readonly Vector3 ExitSize = new Vector3(3f, 3f, 3f);

        // ---- The toys ----------------------------------------------------------------------------------------------
        public const string BallTag = "ball", FanTag = "fan", RulerTag = "ruler", DominoTag = "dominoes", BaseballTag = "baseball";
        public static readonly Vector3 BookPedestal = new Vector3(14f, GalleryTop, 6f);
        public static readonly Vector3 SpoolPedestal = new Vector3(-18f, GalleryTop, 6f);
        public const float PedestalHeight = 1.1f;
        public const float DominoStart = 0.6f, DominoMin = 0.5f, DominoMax = 3.5f;
        public const float BallStart = 0.6f, BallMin = 0.6f, BallMax = 3f;
        public const float RulerStart = 1f, RulerMin = 0.6f, RulerMax = 1.2f;
        public const float FanStart = 5f, FanMin = 0.5f, FanMax = 5f;
        /// <summary>
        /// LEVELS stands the fan at (-22.6, 0, 13.6) with its guard toward the quilt. From the mark in front of
        /// the garage it was then picked up edge-on, and stayed edge-on in the hand in front of an outline that
        /// is a circle (the builder's own open issue). It faces down the trench now, toward that mark: a fan
        /// seen face-on at the far end of the floorboards, and face-on in the hand at the garage's door. Turned
        /// like this its guard reaches 0.3 into the band west of the slide tower, where nothing stands.
        /// </summary>
        public static readonly Vector3 FanStartPoint = new Vector3(-23f, 0f, 14.3f);
        public static readonly Quaternion FanStartRotation = Quaternion.Euler(0f, 90f, 0f);
        /// <summary>
        /// LEVELS lays the ruler at (-9, 0.04, 14). From the stand mark it then looks 0.23 big, and held over the
        /// near half of the marker it comes out under 0.8 (measured: 0.85 over the middle). A unit nearer it
        /// looks 0.29: 0.84 over the marker's near end, its largest size over the far end.
        /// </summary>
        public static readonly Vector3 RulerStartPoint = new Vector3(-8.1f, 0.04f, 14.2f);

        // ---- What each station takes, and what the chain needs ---------------------------------------------------
        public const float HopperMinScale = 1.2f, HopperMaxScale = 3f;
        public const float FulcrumMinScale = 0.8f, FulcrumMaxScale = 1.2f, FulcrumYaw = 40f;
        public const float CollarMinScale = 0.5f, CollarMaxScale = 1.2f;
        public const float LaneMinScale = 1f, LaneMaxScale = 3.5f, LaneYaw = 45f;
        /// <summary>
        /// The air in front of two stations. Measured: a ball or a set that is as big as it gets hangs short of
        /// the wall when it was picked up from very near or is let go from behind the edge (the ball at z 13.8
        /// from two steps back, the set at z 15 picked up from beside its book). So a second socket in front of
        /// each takes a toy that is big enough to work from this far out and hands it on to the station. One
        /// that is too small there caught on the tower or on the plinth's edge at half its size: it is better
        /// off in the trench, where it can be picked up again, than seated where it is of no use.
        /// (The review: the builder's two reached only part of the way back to the quilt - the lane's to z 14.4,
        /// the hopper's 4.6 from its axis and from y 6 up. Measured: the set, let go at its outline from the
        /// start without a step, stops growing at z 13.8 and fell; at its largest in the trench under the
        /// edge and picked up from the quilt (3.1 to 4.6 away) it hung at z 13.7 to 14.1, again and again,
        /// with no getting it bigger and no getting it nearer; the ball picked up from beside it hung at
        /// z 12.9, y 5.9. Now the whole trench in front of a station is its approach: from half a unit
        /// beyond the quilt's edge, for a toy let go at y 5 or higher.)
        /// </summary>
        public const float HopperReach = 4.6f, ApproachZ = 12f, ApproachMinY = 5f;
        public const float LaneReachZ = ApproachZ;
        /// <summary>What the chain needs of the two toys that grow (the sockets take less: see the build notes).</summary>
        public const float DominoWorks = 2.2f, BallWorks = 2.25f;
        /// <summary>The fan blows with (scale / 0.6)^2; the sail needs 1.</summary>
        public const float FanReference = 0.6f;

        // ---- Where to stand -------------------------------------------------------------------------------------------
        /// <summary>The start: a step from the dominoes on their book, the lane and its outline behind them.</summary>
        public static readonly Vector3 SpawnPoint = new Vector3(14f, GalleryTop, 4.3f);
        public const float SpawnYaw = 0f, SpawnPitch = 0f;
        public static readonly Vector3 DominoStand = new Vector3(14.25f, GalleryTop, 11.1f);
        /// <summary>(LEVELS aims at 8.9; the outline's middle is at 9.5 now, and the bot covers it like anybody else.)</summary>
        public static readonly Vector3 DominoAim = new Vector3(14.25f, 9.4f, BackZ);
        public static readonly Vector3 BallPickup = new Vector3(-18f, GalleryTop, 4.5f);
        public static readonly Vector3 BallStand = new Vector3(-18f, GalleryTop, 11.1f);
        public static readonly Vector3 BallAim = new Vector3(-18f, 10.8f, BackZ);
        public static readonly Vector3 RulerStand = new Vector3(-5f, 0f, 14.4f);
        public static readonly Vector3 RulerAim = new Vector3(-5f, 0.88f, AxisZ);
        public static readonly Vector3 FanStand = new Vector3(1.25f, 0f, 13f);
        public static readonly Vector3 FanAim = new Vector3(1.25f, 0.6f, 17.2f);

        // ---- The links of a run, in chain order ----------------------------------------------------------------------
        public const string LinkGate = "gate", LinkChute = "chute", LinkDrum = "drum", LinkBall = "ball", LinkStrike = "strike",
            LinkMarble = "marble", LinkSwitch = "switch", LinkFan = "fan", LinkWind = "wind", LinkDomino = "domino",
            LinkTarget = "target", LinkBaseball = "baseball", LinkClock = "clock";
        static readonly string[] Links =
        {
            LinkGate, LinkChute, LinkDrum, LinkBall, LinkStrike, LinkMarble, LinkSwitch, LinkFan, LinkWind, LinkDomino, LinkTarget, LinkBaseball, LinkClock,
        };

        // ---- What is said -----------------------------------------------------------------------------------------------
        /// <summary>Said when the dominoes or the ball are picked up looking too small (the campaign's sentence).</summary>
        public const string FarLine = "It only ever gets as big as it looks. Pick it up from closer.";
        /// <summary>Said when the fan is picked up looking too big for its garage.</summary>
        public const string NearLine = "From this close it looks too big for the garage. Pick it up from farther off.";
        public const string BallSmallLine = "The ball is too small for the hopper: out it rolls. Pick it up from closer.";
        /// <summary>(Measured: it comes out short both ways - picked up from far off, or let go from right in front of the marker.)</summary>
        public const string RulerSmallLine = "Too short to reach under the ball. Pick the ruler up from closer, or let go from farther back.";
        public const string RulerTurnLine = "The cap has to point at the hoop. Turn the ruler with Q.";
        public const string FanBigLine = "Too big for the garage. Pick the fan up from farther off and let go up close.";
        public const string DominoSmallLine = "Too small to reach the shelf. The dominoes are back on their book: pick them up from closer.";
        public const string DominoTurnLine = "The small end goes under the nozzle. The dominoes are back on their book: turn them with Q.";
        /// <summary>The same for a set that was refused in the air and fell into the trench.</summary>
        public const string DominoTurnShortLine = "The small end goes under the nozzle. Turn the dominoes with Q.";
        public const string DominoEdgeLine = "The dominoes caught on the edge of the lane. Hold them higher, against the wall above it.";
        public const string BallTowerLine = "The ball stopped short of the funnel. Hold it higher, against the wall above it.";
        /// <summary>
        /// Said when a toy that has stopped growing (it is as big as it gets) was let go over the quilt, short of
        /// the trench: nothing stopped it but its own limit, and "hold it higher" would be wrong.
        /// </summary>
        public const string DominoFullLine = "The dominoes are as big as they get, and still short of the lane. Let go from the edge of the quilt.";
        public const string BallFullLine = "The ball is as big as it gets, and still short of the funnel. Let go from the edge of the quilt.";
        /// <summary>Said when the marble fell short because the player stood on the ruler's long end (Level 7 taught them to).</summary>
        public const string RiderLine = "Your weight on the long end holds the marble down. Step off the ruler.";
        /// <summary>Said when something else than the ball tipped the ruler by hand and the marble fell short.</summary>
        public const string OtherWeightLine = "That is not heavy enough to flip the marble through the hoop. The ball will be, at its biggest.";
        /// <summary>Said when the ball comes down on a ruler that something still holds tipped.</summary>
        public const string ShortArmLine = "Something lies on the short end of the ruler and holds it down. Pick it up.";
        public const string RulerMissLine = "The ruler goes across the marker, on its outline. Hold it over the marker and let go.";
        public const string FanMissLine = "The fan goes inside the garage. Hold it against the back of the garage and let go.";
        public const string ParkingLine = "It cannot stay up there. It is back where it started.";
        public const string GateLine = "The gate opens. Nothing rolls.";
        public const string PivotLine = "The ball lands on a bare pivot.";
        public const string MarbleLine = "The marble falls short. The ball is not heavy enough.";
        public const string MarbleWideLine = "The marble goes up beside the hoop. The ruler is too short.";
        public const string PlayerLine = "You are not heavy enough to flip the marble through the hoop.";
        public const string FanMissingLine = "Click. Nothing is plugged in.";
        public const string FanSmallLine = "The little fan wheezes. The sail does not turn.";
        public const string WindLine = "The wind has nothing to push.";
        public const string WindShortLine = "The wind blows short of the smallest domino. The dominoes have to be bigger.";
        public const string ShortLine = "The last domino falls short of the shelf.";
        public const string DoneLine = "The alarm rings. The way out is open, at the far end of the floorboards.";

        /// <summary>
        /// scale / distance below which the dominoes look too small to come out big enough at the wall.
        /// (Measured from the edge: looking 0.257 they come out at 2.31, looking 0.227 at 2.06; they need 2.2.)
        /// </summary>
        public const float DominoLooksSmall = 0.245f;
        /// <summary>The same for the ball (measured: 0.249 gives 2.37, 0.214 gives 2.08; it needs 2.25).</summary>
        public const float BallLooksSmall = 0.238f;
        /// <summary>
        /// scale / distance above which the fan looks too big to end up in its garage. (Measured: what decides
        /// is how near the garage it is let go; looking 0.6 big it still comes out at 0.9 from the garage's
        /// door. Only a fan picked up from beside it, looking 0.9 and more, cannot be made to fit.)
        /// </summary>
        public const float FanLooksBig = 0.9f;

        const float LampSize = 0.11f, LaneLampY = 4.8f;
        const int SpinUpTicks = 30, ChainDelayTicks = 24, WheezeTicks = 54, LiftTicks = 66, BallWaitTicks = 60;

        // ---- Props and gadgets ---------------------------------------------------------------------------------------------
        public Prop Dominoes { get; private set; }
        public Prop Ball { get; private set; }
        public Prop Ruler { get; private set; }
        public Prop Fan { get; private set; }
        public Prop Baseball { get; private set; }

        public PressurePlate Pedal { get; private set; }
        public Socket Hopper { get; private set; }
        public Socket Fulcrum { get; private set; }
        public Socket Collar { get; private set; }
        public Socket Lane { get; private set; }
        /// <summary>The air in front of the hopper and of the lane: a toy that works is passed on to its station.</summary>
        public Socket HopperApproach { get; private set; }
        public Socket LaneApproach { get; private set; }
        public Door Gate { get; private set; }
        public MachineDirector Director { get; private set; }
        public PathDrive BallRun { get; private set; }
        /// <summary>The lever: built when the fulcrum seats a ruler, null while there is none.</summary>
        public Seesaw Seesaw { get; private set; }
        public PathDrive MarblePath { get; private set; }
        public WindStream Wind { get; private set; }
        public HingeChain Chain { get; private set; }
        public PathDrive BaseballRun { get; private set; }
        public Breakable Clock { get; private set; }
        public Door ExitDoor { get; private set; }
        public Exit Exit { get; private set; }
        public ReturnPort RejectPort { get; private set; }
        public HazardZone JarHazard { get; private set; }
        public PropLeash Parking { get; private set; }
        public PropLeash LaneLeash { get; private set; }
        public FitGauge[] Gauges { get; private set; }

        /// <summary>How high the marble of the last swing topped out, and the f of that swing.</summary>
        public float MarbleApex { get; private set; }
        public float LastF { get; private set; }
        /// <summary>The marble of the last swing went through the hoop.</summary>
        public bool MarbleThrough { get; private set; }
        /// <summary>True while the marble is out of its cap.</summary>
        public bool MarbleFlying { get; private set; }
        /// <summary>The switch at the foot of the tube is closed (until the machine sets itself up again).</summary>
        public bool SwitchClosed { get; private set; }

        LevelContext context;
        Mover marbleMover;
        MeshRenderer marbleLight;
        Transform marbleChild;
        // The lever has thrown: it stays as it lies until what tipped it is gone and the machine is idle again.
        bool leverSpent;
        SignalLamp switchLamp;
        Transform fanBlades;
        float bladeAngle;
        // A run that began with something landing on the ruler by hand, not with the pedal.
        bool handStart, runFromPedal, ballWasSeated;
        int fanTicks, chainTicks, wheezeTicks;
        // The ball on its way back to the hopper after a run that stopped.
        Mover liftMover;
        Vector3 liftFrom, liftTo;
        int liftTick;
        FitState laneRefusal;
        string lastLine;
        int lastSaid;
        // A toy that was let go: a little later, was it taken by its station? (And a set that was refused: is it still lying about?)
        Prop missToy;
        Vector3 missFrom;
        int missTick, missSaid, turnTick;
        // A toy has arrived over its station from the air in front of it: let go of it there.
        bool handOn;

        public override string Blurb => "Four toys, four outlines, one pedal. Nothing here is the right size.";

        public override string[] Hints => new[]
        {
            "Put a toy on every painted outline, then stomp the pedal and watch where the motion stops. That link is the one to fix; the machine sets itself up again.",
            "The ball must be heavy enough to flip the marble through the hoop. The fan must fit its garage. The smallest domino stands in the wind stripes. The cap of the ruler points at the hoop.",
            // (The review: it did not say where from, and "pick up the fan from the garage" read as if the fan were in it.)
            "Pick up dominoes and ball from a step away. At the quilt's edge, cover the outlines on the wall, let go. Ruler: on the marker, cap to the hoop (Q). Fan: pick up from the garage door, let go inside. Pedal.",
        };

        public override string Environment => "night-light";
        public override float GroundY => 0f;
        public override float KillY => -30f;

        public override void Build(LevelContext ctx)
        {
            context = ctx;
            Seesaw = null;
            marbleMover = null;
            marbleLight = null;
            marbleChild = null;
            leverSpent = false;
            switchLamp = null;
            fanBlades = null;
            bladeAngle = 0f;
            handStart = false;
            runFromPedal = false;
            ballWasSeated = false;
            fanTicks = -1;
            chainTicks = -1;
            wheezeTicks = -1;
            liftMover = null;
            liftTick = 0;
            laneRefusal = FitState.Idle;
            lastLine = null;
            lastSaid = -100000;
            handOn = false;
            missToy = null;
            missTick = 0;
            missSaid = 0;
            turnTick = -1;
            MarbleApex = 0f;
            LastF = 0f;
            MarbleThrough = false;
            MarbleFlying = false;
            SwitchClosed = false;
            marbleWide = false;
            riderOnCap = false;
            struckByPlayer = false;
            struckByBall = false;
            ballTicks = -1;

            BuildRoom(ctx);
            BuildSlide(ctx);
            BuildDrumAndBooks(ctx);
            BuildControlTower(ctx);
            BuildPlinth(ctx);
            BuildJar(ctx);
            BuildPedestals(ctx);
            AddToys(ctx);
            AddGadgets(ctx);
            BuildPaint(ctx);
            BuildDressing(ctx);

            ctx.SetSpawn(SpawnPoint, SpawnYaw, SpawnPitch);
            // Whoever drops into the bell jar from the shelf is put back at the foot of the ramp, not at the start.
            ctx.AddCheckpoint(Volume.Box(3f, 3f, 3f), new Vector3(7.5f, 1.5f, 13f), new Vector3(7.5f, 0f, 13f), -90f, "Trench");
        }

        // ---- Toys ------------------------------------------------------------------------------------------------------------

        void AddToys(LevelContext ctx)
        {
            ToyDef set = ToyCatalog.Get(ToyId.DominoSet);
            Dominoes = ToyCatalog.Add(ctx, ToyId.DominoSet, BookPedestal + Vector3.up * (PedestalHeight + set.RestHeight * DominoStart), DominoStart, null, o =>
            {
                o.MinScale = DominoMin;
                o.MaxScale = DominoMax;
                o.Tags = new[] { DominoTag };
            });

            ToyDef ball = ToyCatalog.Get(ToyId.BouncyBall);
            Ball = ToyCatalog.Add(ctx, ToyId.BouncyBall, SpoolPedestal + Vector3.up * (PedestalHeight + ball.RestHeight * BallStart), BallStart, null, o =>
            {
                o.MinScale = BallMin;
                o.MaxScale = BallMax;
                o.Tags = new[] { BallTag };
                // A sphere on a flat top is only metastable: it stays put until it is picked up.
                o.FrozenUntilGrabbed = true;
            });

            // Flat on the trench floor, long axis along X, the cap toward -X: the wrong way round.
            Ruler = ToyCatalog.Add(ctx, ToyId.CatapultRuler, RulerStartPoint, Quaternion.Euler(0f, -90f, 0f), RulerStart, null, o =>
            {
                o.MinScale = RulerMin;
                o.MaxScale = RulerMax;
                o.Tags = new[] { RulerTag };
            });
            marbleChild = Ruler.Transform.Find(ToyFactory.CatapultMarble);

            // A turbine the size of a house at the west end of the trench, blowing down it.
            Fan = ToyCatalog.Add(ctx, ToyId.DeskFan, FanStartPoint, FanStartRotation, FanStart, null, o =>
            {
                o.MinScale = FanMin;
                // Never bigger than it starts.
                o.MaxScale = FanMax;
                o.Tags = new[] { FanTag };
            });
            fanBlades = Fan.Transform.Find(ToyFactory.DeskFanBlades);

            // Not the player's: it sits in its ring of putty until the last domino swats it.
            Baseball = ToyCatalog.Add(ctx, ToyId.Baseball, BaseballCentre, 1f, null, o =>
            {
                o.Tags = new[] { BaseballTag };
                o.FrozenUntilGrabbed = true;
            });
        }

        // ---- Gadgets, in feed order ----------------------------------------------------------------------------------------

        void AddGadgets(LevelContext ctx)
        {
            Game game = ctx.Game;

            // Only the player presses it: no toy carries this tag.
            Pedal = new PressurePlate(ctx, new PressurePlateOptions
            {
                Name = "pedal", Sensor = Zone.Cylinder(PedalPoint.x, PedalPoint.z, PedalRadius, GalleryTop, GalleryTop + 0.8f),
                MinMass = 2f, AcceptPlayer = true, AcceptTag = "nobody",
            });

            Hopper = new Socket(ctx, new SocketOptions
            {
                Name = "hopper", AcceptTag = BallTag, Capture = Zone.Cylinder(HopperX, AxisZ, 2.5f, TowerTop, 12.5f),
                MinScale = HopperMinScale, MaxScale = HopperMaxScale,
                SeatPose = s => new Pose(HopperFoot + Vector3.up * (0.5f * s), Quaternion.identity),
                Lamp = new Vector3(-19.4f, 5.4f, BandZ - 0.12f), LampRadius = LampSize,
            });
            Fulcrum = new Socket(ctx, new SocketOptions
            {
                Name = "fulcrum", AcceptTag = RulerTag, Capture = Zone.Box(new Vector3(-4.5f, 1.25f, AxisZ), new Vector3(7f, 2.5f, 2.5f)),
                MinScale = FulcrumMinScale, MaxScale = FulcrumMaxScale,
                YawAxis = Vector3.forward, YawReference = Vector3.right, YawTolerance = FulcrumYaw,
                SeatPose = RulerSeat,
                Lamp = new Vector3(-5.35f, 0.3f, 16.8f), LampRadius = LampSize,
            });
            Collar = new Socket(ctx, new SocketOptions
            {
                // The garage and the floor in front of its door: a fan that stopped on the sill is rolled in.
                Name = "collar", AcceptTag = FanTag, Capture = Zone.MinMax(new Vector3(GarageMinX - 0.2f, 0f, 15.7f), new Vector3(GarageMaxX + 0.2f, 2f, GarageBackZ)),
                MinScale = CollarMinScale, MaxScale = CollarMaxScale,
                SeatPose = s => new Pose(FanSeat, Quaternion.identity),
                Lamp = new Vector3(0.9f, 2.05f, BandZ - 0.12f), LampRadius = LampSize,
            });
            Lane = new Socket(ctx, new SocketOptions
            {
                Name = "lane", AcceptTag = DominoTag, Capture = Zone.MinMax(new Vector3(LaneMinX, LaneTop, BandZ), new Vector3(LaneEndX + 0.4f, 13.5f, BackZ)),
                MinScale = LaneMinScale, MaxScale = LaneMaxScale,
                YawAxis = Vector3.right, YawReference = Vector3.right, YawTolerance = LaneYaw,
                SeatPose = DominoSeat, EaseSeconds = 0.4f,
                // (Just under the lane's edge: with the outline's middle under the crosshair the HUD's size readout covered them at 4.45.)
                Lamp = new Vector3(14.6f, LaneLampY, BandZ - 0.12f), LampRadius = LampSize,
            });

            // After the stations (a toy inside a station is the station's): the air in front of them.
            HopperApproach = new Socket(ctx, new SocketOptions
            {
                Name = "hopper approach", AcceptTag = BallTag,
                Capture = Zone.MinMax(new Vector3(HopperX - HopperReach, ApproachMinY, ApproachZ), new Vector3(HopperX + HopperReach, 12.5f, BackZ)),
                MinScale = BallWorks, MaxScale = HopperMaxScale, EaseSeconds = 0.3f,
                SeatPose = s => new Pose(HopperFoot + Vector3.up * (0.5f * s + 0.3f), Quaternion.identity),
            });
            LaneApproach = new Socket(ctx, new SocketOptions
            {
                Name = "lane approach", AcceptTag = DominoTag, Capture = Zone.MinMax(new Vector3(LaneMinX, ApproachMinY, LaneReachZ), new Vector3(LaneEndX + 0.4f, 13.5f, BandZ)),
                MinScale = DominoWorks, MaxScale = LaneMaxScale, EaseSeconds = 0.3f,
                YawAxis = Vector3.right, YawReference = Vector3.right, YawTolerance = LaneYaw,
                SeatPose = s =>
                {
                    Pose seat = DominoSeat(s);
                    return new Pose(seat.position + Vector3.up * 0.3f, seat.rotation);
                },
            });
            HopperApproach.OnSeated += prop => handOn = true;
            LaneApproach.OnSeated += prop => handOn = true;
            // (The review, second pass: a socket hands a toy back as grabbable as it found it. Found in the tick
            // it was let go in a run, that is "the player's"; found a little later, "the machine's" - and the
            // approach then passed that on to the station after the machine was idle again. An approach only
            // hands on: in a run the toy is the machine's (see Tick), and once the machine is idle it is the player's.)
            HopperApproach.OnUnseated += HandedOn;
            LaneApproach.OnUnseated += HandedOn;
            LaneApproach.OnRejected += (prop, fit) =>
            {
                // (Too small and the wrong way round is told as the wrong way round: that is what stopped it short.)
                if (fit != FitState.Backwards && LaneApproach.HeadingFits(prop.Rotation)) return;
                laneRefusal = FitState.Backwards;
                turnTick = ctx.Ticks + 110;
            };

            Gate = new Door(ctx, new DoorOptions
            {
                Name = "gate", Panel = BuildGate(), ClosedPosition = GateClosed, OpenPosition = GateClosed + Vector3.down * GateHeight,
                Motion = DoorMotion.Slide, Seconds = GateSeconds, Ease = DoorEase.Linear,
            });

            Director = new MachineDirector(ctx, new MachineDirectorOptions
            {
                Name = "machine", Start = Pedal, Toys = new[] { Ball, Fan, Ruler, Dominoes }, Links = Links,
                FizzleTimeout = 2.5f, ResetSeconds = 1.5f,
                Messages = new Dictionary<string, string>
                {
                    { "", GateLine }, { LinkGate, GateLine }, { LinkChute, PivotLine }, { LinkDrum, PivotLine }, { LinkBall, PivotLine },
                    { LinkStrike, MarbleLine }, { LinkMarble, MarbleLine }, { LinkSwitch, FanMissingLine }, { LinkFan, WindLine }, { LinkWind, WindLine },
                    { LinkDomino, ShortLine }, { LinkTarget, ShortLine }, { LinkBaseball, ShortLine },
                },
            });
            Director.MachineStarted += OnRunStarted;
            Director.MachineFizzled += link => OnRunStopped();
            Director.MachineReset += OnMachineReady;

            Gate.Opened += () =>
            {
                if (Director.State != MachineState.Running) return;
                Director.Report(LinkGate);
                if (Hopper.Seated && Hopper.SeatedProp == Ball) StartBallRun();
            };

            // The ball's lowest point follows the chute, the fall to the drum and the bounce over the books.
            BallRun = new PathDrive(ctx, new PathDriveOptions
            {
                Name = "ball run", Body = Ball, Segments = new List<PathSegment> { PathSegment.Hold(Sim.Dt) },
                EndVelocity = new Vector3(0f, -4f, 0f), GhostToPlayer = true,
            });
            BallRun.SegmentEnded += index =>
            {
                Director.Report(index == 0 ? LinkChute : index == 1 ? LinkDrum : LinkBall);
                if (index == 2) ballTicks = 0;
            };

            // The marble that leaves the ruler's cap: a light of its own, nobody collides with it.
            var marble = new GameObject("Marble");
            Mesh sphere = MeshKit.Cached(MeshKit.Key("Level15/Marble", ToyFactory.CatapultMarbleRadius), () => MeshKit.Sphere(ToyFactory.CatapultMarbleRadius * 1.25f, 16, 10));
            marbleLight = GadgetKit.Visual(marble.transform, "Visual", sphere, Materials.Emissive(ToyRecipe.Lamp, Palette.Paper, Palette.Lin(Palette.WarmWhite) * 5f));
            marbleLight.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            marbleLight.enabled = false;
            marbleMover = ctx.AddKinematic(marble, HoopCentre);
            MarblePath = new PathDrive(ctx, new PathDriveOptions { Name = "marble", Mover = marbleMover, Segments = new List<PathSegment> { PathSegment.Hold(Sim.Dt) } });
            MarblePath.Finished += OnMarbleDone;

            // The nozzle's jet over the first stretch of the lane. It pushes nothing: the sail reads it.
            Wind = new WindStream(ctx, new WindStreamOptions
            {
                Name = "jet", Center = new Vector3((LaneMinX + WindMaxX) * 0.5f, LaneTop + 1f, AxisZ), Size = new Vector3(WindMaxX - LaneMinX, 2f, 2f),
                Direction = Vector3.right, Strength = 0f, PlayerAirPush = 0f, PlayerGroundPush = 0f, PropDrag = 0f,
            });

            HingeChainOptions chain = HingeChain.ForDominoSet(Dominoes);
            chain.Name = "dominoes";
            chain.Target = BaseballCentre;
            chain.TargetRadius = 0.5f;
            chain.ResetSeconds = 1f;
            // (The gadget's 3 degrees make the seven beats 5.5 s long at this size; LEVELS' run has 3.6 s for them.)
            chain.StartAngle = 5f;
            Chain = new HingeChain(ctx, chain);
            Chain.PieceFell += index =>
            {
                if (Director.State == MachineState.Running) Director.Report(LinkDomino);
            };
            Chain.TargetStruck += OnBaseballStruck;
            Chain.FellShort += () => Director.Fail(ShortLine);

            // The baseball rolls along the shelf, falls into the jar's neck and straight down onto the clock.
            // (Off the shelf's end at the speed it rolls with, it is over the jar's axis after `time`, in the neck.)
            float time = (JarX - ShelfMaxX) / BaseballSpeed;
            float neckY = BaseballCentre.y - 0.5f * Game.Gravity * time * time;
            BaseballRun = new PathDrive(ctx, new PathDriveOptions
            {
                Name = "baseball", Body = Baseball, GhostToPlayer = true,
                Segments = new List<PathSegment>
                {
                    PathSegment.Roll(new Vector3(ShelfMaxX, BaseballCentre.y, AxisZ), 0f, BaseballSpeed),
                    PathSegment.Ballistic(new Vector3(BaseballSpeed, 0f, 0f), neckY),
                    PathSegment.Ballistic(new Vector3(0f, -Game.Gravity * time, 0f), ClockCentre.y + ClockSize.y * 0.5f + ToyFactory.BaseballRadius + 0.25f),
                },
            });
            BaseballRun.SegmentEnded += index =>
            {
                if (Director.State == MachineState.Running) Director.Report(LinkBaseball);
            };

            // Only the baseball crushes it (LEVELS blocks everything else by weight; a ball held down the neck would do).
            Clock = new Breakable(ctx, new BreakableOptions
            {
                Name = "alarm clock", Center = ClockCentre, Size = ClockSize, AcceptTag = BaseballTag, MinMass = 8f, MinSpeed = 6f,
                Direction = Vector3.down, OnBreak = BreakMode.SwapCollider, FlattenedHeight = 1f, Body = BuildClock(),
            });
            Clock.Watch(Baseball);
            Clock.Broke += (prop, speed) => OnClockCrushed();

            ExitDoor = new Door(ctx, new DoorOptions
            {
                Name = "exit door", Size = new Vector3(3f, 3f, 0.3f),
                ClosedPosition = new Vector3(HalfWidth + 0.15f, 1.5f, ExitCentre.z), ClosedRotation = Quaternion.Euler(0f, 90f, 0f),
                OpenPosition = new Vector3(HalfWidth + 0.15f, 4.6f, ExitCentre.z), OpenRotation = Quaternion.Euler(0f, 90f, 0f), Seconds = 0.8f,
            });
            Exit = ctx.AddExit(ExitCentre, ExitSize).Lock();

            // A ball that is too small for the hopper drops between the bars and rolls out at the tower's foot.
            RejectPort = new ReturnPort(ctx, new ReturnPortOptions
            {
                Name = "reject door", Mouth = new Vector3(-16.5f, 0.6f, 15.6f), EjectVelocity = new Vector3(0f, 0f, -1.5f),
                PlayerPosition = new Vector3(-16.5f, 0f, 14.5f), PlayerYaw = 0f,
            });
            JarHazard = new HazardZone(ctx, new HazardZoneOptions { Name = "bell jar", Shape = Zone.Cylinder(JarX, AxisZ, JarRadius, -0.5f, JarTop) });

            // The lamp beside each station, while its toy is held near: green if a toy of that size would make
            // its link work (which is more than the station taking it: the lane seats a set of 1.0, the wind
            // reaches one of 2.2), amber if not. The ruler's also minds which way the cap points.
            Gauges = new[]
            {
                new FitGauge(ctx, new FitGaugeOptions
                {
                    Name = "hopper gauge", MinScale = BallWorks, MaxScale = HopperMaxScale, Tag = BallTag, Near = Zone.MinMax(new Vector3(-24f, 4f, 13.5f), new Vector3(-12f, 14f, BackZ)),
                    Lamp = new Vector3(-19.9f, 5.4f, BandZ - 0.12f), LampRadius = LampSize,
                }),
                new FitGauge(ctx, new FitGaugeOptions
                {
                    Name = "fulcrum gauge", Target = Fulcrum, Tag = RulerTag, Near = Zone.MinMax(new Vector3(-8.5f, 0f, 15.5f), new Vector3(-1.5f, 3f, BackZ)),
                    Lamp = new Vector3(-4.65f, 0.3f, 16.8f), LampRadius = LampSize,
                }),
                new FitGauge(ctx, new FitGaugeOptions
                {
                    Name = "collar gauge", MinScale = FanReference, MaxScale = CollarMaxScale, Tag = FanTag, Near = Zone.MinMax(new Vector3(-0.5f, 0f, 14.5f), new Vector3(3f, 3f, GarageBackZ)),
                    Lamp = new Vector3(1.6f, 2.05f, BandZ - 0.12f), LampRadius = LampSize,
                }),
                new FitGauge(ctx, new FitGaugeOptions
                {
                    Name = "lane gauge", MinScale = DominoWorks, MaxScale = LaneMaxScale, Tag = DominoTag, Near = Zone.MinMax(new Vector3(PlinthMinX - 3f, 4f, 13.5f), new Vector3(PlinthMaxX + 3f, 14f, BackZ)),
                    Lamp = new Vector3(13.9f, LaneLampY, BandZ - 0.12f), LampRadius = LampSize,
                }),
            };

            // No parking: a toy that comes to rest where nobody can get at it goes back to where it started.
            var toys = new[] { Dominoes, Ball, Ruler, Fan };
            var forbidden = new List<Zone>
            {
                // The shelf, the top of the slide tower, the roof of the control tower, the bell jar, the tunnel.
                Zone.MinMax(new Vector3(ShelfMinX - 0.2f, ShelfTop, BandZ), new Vector3(ShelfMaxX, ShelfTop + 6f, BackZ)),
                // (The tower's top as far as the gate: a ruler that lies behind the bars is as far out of reach.
                // A ball there is the hopper's, or rolls out of the reject door.)
                Zone.MinMax(new Vector3(TowerMinX, TowerTop - 0.9f, BandZ), new Vector3(GateX + 0.3f, TowerTop + 6f, BackZ)),
                Zone.MinMax(new Vector3(ControlMinX, ControlTop, BandZ), new Vector3(ControlMaxX, ControlTop + 6f, BackZ)),
                Zone.Cylinder(JarX, AxisZ, JarRadius + 0.3f, -0.5f, JarMouthY + 0.4f),
                Zone.MinMax(new Vector3(HalfWidth, -0.5f, 12f), new Vector3(HalfWidth + 5f, 4f, 16f)),
                // (The review: and the slide below the gate, between its rails. A ruler that lay along it stayed
                // there, behind the front rail, where no click from the quilt or the trench found it. Nothing
                // else is this high in the band here: on the floorboards a toy's middle is lower.)
                Zone.MinMax(new Vector3(TowerMinX, 3f, BandZ), new Vector3(ChuteLip.x + 0.2f, TowerTop + 6f, BackZ)),
                // The hoop and its arm, and the top of the duct from the tower to the nozzle: a flat toy up there
                // shows the quilt nothing but an edge.
                Zone.MinMax(new Vector3(HoopCentre.x - HoopRadius - 0.3f, HoopCentre.y - 0.1f, AxisZ - 1.2f), new Vector3(ControlMinX, HoopCentre.y + 6f, AxisZ + 1.2f)),
                Zone.MinMax(new Vector3(ControlMaxX, NozzleTip.y + 0.3f, AxisZ - 1f), new Vector3(NozzleTip.x + 0.2f, NozzleTip.y + 6.4f, AxisZ + 1f)),
            };
            Parking = new PropLeash(ctx, new PropLeashOptions
            {
                Name = "no parking", Props = toys, RestSpeed = 0.2f, Grace = 2f, Action = LeashAction.Respawn, Forbidden = forbidden,
            });
            Parking.PropReturned += prop => Say(ParkingLine, 5f);
            // The lane is for the dominoes, and only for dominoes it can use: a set that lies up there unseated
            // (too small, or the wrong way round) is out of reach, so it goes back to its book.
            LaneLeash = new PropLeash(ctx, new PropLeashOptions
            {
                Name = "lane", Props = toys, RestSpeed = 0.5f, Grace = 1.5f, Action = LeashAction.Respawn,
                Forbidden = new[] { Zone.MinMax(new Vector3(PlinthMinX, LaneTop, BandZ), new Vector3(PlinthMaxX, LaneTop + 6f, BackZ)) },
            });
            LaneLeash.PropReturned += prop =>
            {
                if (prop != Dominoes) Say(ParkingLine, 5f);
                else Say(laneRefusal == FitState.Backwards ? DominoTurnLine : DominoSmallLine, 7f);
                if (prop == Dominoes) turnTick = -1;
            };

            Hopper.OnRejected += (prop, fit) =>
            {
                // (In the hopper itself it also drops between the bars: see Tick.)
                if (fit == FitState.TooSmall) Say(BallSmallLine, 6f);
            };
            Hopper.OnUnseated += prop =>
            {
                // (The socket hands a prop it lets go of back as grabbable; in a run the toys are not.)
                if (Director.State == MachineState.Running || Director.State == MachineState.Resetting) prop.Grabbable = prop.Held;
            };
            Fulcrum.OnRejected += (prop, fit) => Say(fit == FitState.Backwards ? RulerTurnLine : RulerSmallLine, 6f);
            Fulcrum.OnSeated += prop => BuildSeesaw(ctx, prop);
            Fulcrum.OnUnseated += prop =>
            {
                // The lever goes with the ruler. (The review: it was only forgotten. A prop has one mover for all
                // who drive it, so a ruler that was seated a second time woke every earlier lever as well: each
                // struck with the ball, none was held down after the throw, and they swung the ruler back up
                // and down again with the arms of the size it had before.)
                if (Seesaw != null)
                {
                    Seesaw.Enabled = false;
                    if (!prop.Removed) GadgetKit.IgnorePlayer(ctx.Game, prop.Colliders, false);
                }
                Seesaw = null;
                leverSpent = false;
                riderOnCap = false;
                struckByPlayer = false;
            };
            Collar.OnRejected += (prop, fit) =>
            {
                if (fit == FitState.TooBig) Say(FanBigLine, 6f);
            };
            Lane.OnRejected += (prop, fit) =>
            {
                laneRefusal = fit;
                // Refused on the lane it goes back to its book and is told then; refused in the air it may
                // fall into the trench, and is told there.
                if (fit == FitState.Backwards) turnTick = ctx.Ticks + 110;
            };

            // The one way a try goes quietly wrong: a toy only ever gets as big as it looks in the hand.
            game.Events.PropGrabbed += e =>
            {
                if (e.Prop == missToy) missToy = null;
                if (e.Prop == Dominoes)
                {
                    laneRefusal = FitState.Idle;
                    turnTick = -1;
                }
                float ratio = e.OldScale / Mathf.Max(0.01f, e.GrabDistance);
                if (e.Prop == Dominoes && ratio < DominoLooksSmall) Say(FarLine, 6f);
                else if (e.Prop == Ball && ratio < BallLooksSmall) Say(FarLine, 6f);
                else if (e.Prop == Fan && ratio > FanLooksBig) Say(NearLine, 6f);
            };

            // A toy that was let go at its station and not taken is told why, a moment later.
            game.Events.PropDropped += e =>
            {
                if (e.Prop != Dominoes && e.Prop != Ball && e.Prop != Ruler && e.Prop != Fan) return;
                // A toy that was in the hand when the pedal went down stayed the player's (the director leaves it
                // alone). Let go while the machine runs or sets itself up it is the machine's like the others,
                // until the machine is idle again: nobody takes the dominoes out from under their own fall.
                if (!e.Prop.Removed && (Director.State == MachineState.Running || Director.State == MachineState.Resetting)) e.Prop.Grabbable = false;
                if (e.Prop.Removed || e.Prop.Frozen) return;
                missToy = e.Prop;
                missFrom = e.Prop.Center;
                missTick = ctx.Ticks + 12;
                // (A station that refuses the toy says so in the very tick it is let go, before this is heard.)
                missSaid = ctx.Ticks - 2;
            };

            ctx.OnUpdate(Tick);
        }

        bool MachineBusy => Director.State == MachineState.Running || Director.State == MachineState.Resetting;

        static void Keep(Prop toy)
        {
            if (!toy.Removed && !toy.Held && toy.Grabbable) toy.Grabbable = false;
        }

        // An approach lets go of a toy over its station: whose is it now?
        void HandedOn(Prop prop)
        {
            if (prop.Removed) return;
            if (MachineBusy) prop.Grabbable = prop.Held;
            else if (Director.State == MachineState.Idle) prop.Grabbable = true;
        }

        // Was the toy that was let go a moment ago meant for its station, and left out? One line says why.
        void CheckMiss()
        {
            Prop toy = missToy;
            missToy = null;
            if (toy == null || toy.Removed || toy.Held || toy.Frozen || toy.Driven) return;
            if (HopperApproach.SeatedProp == toy || LaneApproach.SeatedProp == toy) return;
            // (Something was said about it since: a refusal has its own line.)
            if (lastSaid >= missSaid) return;
            Vector3 at = missFrom;
            if (toy == Dominoes)
            {
                if (laneRefusal == FitState.Backwards) return;
                bool toward = at.x > PlinthMinX - 3f && at.x < PlinthMaxX + 3f;
                // As big as they get and held up (half a unit clear of the quilt, not put down on it), but over the
                // quilt: only their own limit stopped them.
                float clear = GalleryTop + ToyFactory.DominoSetSize.y * 0.5f * DominoMax + 0.5f;
                if (toward && toy.Scale >= DominoMax - 0.01f && at.z < LaneReachZ && at.y > clear) Say(DominoFullLine, 7f);
                // Smaller than that, in front of the plinth: something stopped them there.
                else if (at.x > PlinthMinX - 1f && at.x < PlinthMaxX + 1f && at.z > EdgeZ + 1.9f && at.y > LaneTop - 0.8f) Say(DominoEdgeLine, 6f);
            }
            else if (toy == Ball)
            {
                bool toward = Mathf.Abs(at.x - HopperX) < HopperReach + 2f;
                if (toward && toy.Scale >= BallMax - 0.01f && at.z < ApproachZ && at.y > GalleryTop + 0.5f * BallMax + 0.5f) Say(BallFullLine, 7f);
                else if (Mathf.Abs(at.x - HopperX) < HopperReach && at.z > AxisZ - HopperReach && at.y > GalleryTop + 0.4f) Say(BallTowerLine, 6f);
            }
            else if (toy == Ruler)
            {
                if (at.x > Pivot.x - 4f && at.x < Pivot.x + 4f && at.z > 13f && at.y < 4f) Say(RulerMissLine, 6f);
            }
            else if (toy == Fan)
            {
                if (at.x > GarageMinX - 3f && at.x < GarageMaxX + 3f && at.z > 13f && at.y < 6f) Say(toy.Scale > CollarMaxScale ? FanBigLine : FanMissLine, 6f);
            }
        }

        // The inside of the hopper: the platform behind the gate.
        static bool InHopper(Vector3 point) =>
            point.x > TowerMinX && point.x < GateX + 0.3f && point.z > BandZ && point.z < BackZ && point.y > TowerTop - 0.6f && point.y < TowerTop + 2.5f;

        // ---- Seats ---------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// The ruler on the marker: a third of its length on the short side (toward the books), the cap end
        /// on the floor toward the hoop. The seesaw's own rest pose.
        /// </summary>
        public static Pose RulerSeat(float scale)
        {
            float arm = LongArmPerScale * scale;
            float tilt = Mathf.Asin(Mathf.Clamp01(Pivot.y / arm));
            var along = new Vector3(Mathf.Cos(tilt), -Mathf.Sin(tilt), 0f);
            var up = new Vector3(Mathf.Sin(tilt), Mathf.Cos(tilt), 0f);
            Quaternion rotation = Quaternion.LookRotation(along, up);
            float thickness = ToyFactory.CatapultRulerPlank.y * scale;
            return new Pose(Pivot + rotation * new Vector3(0f, thickness * 0.5f, CentreArmPerScale * scale), rotation);
        }

        /// <summary>The domino set on the lane: the big end of its strip against the end stop.</summary>
        public static Pose DominoSeat(float scale) =>
            new Pose(new Vector3(LaneEndX - ToyFactory.DominoSetSize.x * 0.5f * scale, LaneTop + ToyFactory.DominoSetSize.y * 0.5f * scale, AxisZ), Quaternion.identity);

        /// <summary>The load of the marble in the ruler's cap (LEVELS: 0.75 + 0.0565 s^3).</summary>
        public static float MarbleLoad(float rulerScale) => 0.75f + 0.0565f * rulerScale * rulerScale * rulerScale;

        /// <summary>The seesaw's f for a ball of this scale on a ruler of that scale (the gadget's rule, with r = 2).</summary>
        public static float FOf(float ballScale, float rulerScale)
        {
            float mass = ToyCatalog.Get(ToyId.BouncyBall).Mass * ballScale * ballScale * ballScale, m = MarbleLoad(rulerScale);
            return Mathf.Clamp((mass - 2f * m) / (mass + 4f * m), 0.05f, 1f);
        }

        /// <summary>Where domino 1 stands (its centre's x) when a set of this scale is seated.</summary>
        public static float FirstDominoX(float scale) => DominoSeat(scale).position.x + ToyFactory.DominoSetPieceCenter(0).x * scale;

        // The lever is the seated ruler itself. One per seating: its arms go with the ruler's scale.
        void BuildSeesaw(LevelContext ctx, Prop ruler)
        {
            float s = ruler.Scale;
            Seesaw = new Seesaw(ctx, new SeesawOptions
            {
                Name = "ruler", Pivot = Pivot, ArmADirection = Vector3.right, ArmA = LongArmPerScale * s, ArmB = ShortArmPerScale * s,
                Width = ToyFactory.CatapultRulerPlank.x * s, Thickness = ToyFactory.CatapultRulerPlank.y * s,
                Plank = ruler, PlankBias = 0.06f, RiderBias = MarbleLoad(s), ProjectileArm = LongArmPerScale * s, TipMargin = 1.5f,
            });
            Seesaw mine = Seesaw;
            mine.SeesawStruck += (load, f) =>
            {
                if (mine != Seesaw) return;
                LastF = f;
                // Who is on the ruler as it is struck (afterwards the ball has rolled off and the player been thrown):
                // on the short arm with nothing else there, the player is what struck it; on the long end
                // (Level 7 taught them to stand there) the marble has their weight to lift as well.
                bool playerOn = GadgetKit.PlayerStandsOn(ctx.Game, mine.Mover.Body);
                struckByPlayer = playerOn && load < Player.Mass + 0.5f;
                riderOnCap = playerOn && !struckByPlayer && mine.RiderLoad > MarbleLoad(s) + 0.5f * Player.Mass;
                struckByBall = OnShortArm(Ball);
                ballTicks = -1;
                // Something came down on the short arm without the pedal (the ball let go over it, the player
                // stepping on it): that is a run too, from this link on.
                if (Director.State == MachineState.Idle)
                {
                    handStart = true;
                    Director.Begin();
                    handStart = false;
                }
                Director.Report(LinkStrike);
            };
            mine.ProjectileLaunched += (from, velocity) =>
            {
                if (mine != Seesaw) return;
                LaunchMarble(velocity.y);
                // One throw a run. What tipped the ruler holds it down (measured: left to itself the ruler came
                // back up under the ball that lay beside its tip, and struck again, once a second, for ever).
                leverSpent = true;
                mine.Enabled = false;
            };
        }

        // Is anything where it would tip the ruler again the moment it came back to rest?
        bool ShortArmLoaded()
        {
            if (Seesaw == null) return false;
            if (GadgetKit.PlayerStandsOn(context.Game, Seesaw.Mover.Body)) return true;
            foreach (Prop toy in new[] { Ball, Fan, Dominoes })
                if (OnShortArm(toy)) return true;
            return false;
        }

        // Does this toy lie on the ruler's short arm, or right over it?
        bool OnShortArm(Prop toy)
        {
            if (toy.Removed || toy.Held || toy == liftedToy) return false;
            float reach = ShortArmPerScale * Ruler.Scale;
            Vector3 centre = toy.Center;
            float radius = toy.Radius;
            return centre.x > Pivot.x - reach - radius - 0.4f && centre.x < Pivot.x + 0.3f && Mathf.Abs(centre.z - AxisZ) < radius + 1f && centre.y < Pivot.y + radius + 1.5f;
        }

        Prop liftedToy => liftMover != null ? Ball : null;

        // ---- A run -------------------------------------------------------------------------------------------------------------------

        void OnRunStarted()
        {
            runFromPedal = !handStart;
            ballWasSeated = Hopper.Seated && Hopper.SeatedProp == Ball;
            if (runFromPedal) Gate.Open();
        }

        void StartBallRun()
        {
            float radius = Ball.Radius;
            BallRun.Offset = Vector3.up * radius;
            Vector3 down = (ChuteLip - HopperFoot).normalized;
            float length = Vector3.Distance(HopperFoot, ChuteLip);
            Vector3 atLip = down * Mathf.Sqrt(2f * ChuteAcceleration * length);
            float impact = Mathf.Sqrt(atLip.y * atLip.y + 2f * Game.Gravity * (ChuteLip.y - DrumTop));
            List<PathSegment> legs = BallRun.Segments;
            legs.Clear();
            legs.Add(PathSegment.Roll(ChuteLip, ChuteAcceleration, 0f, HopperFoot));
            legs.Add(PathSegment.Ballistic(atLip, DrumTop));
            legs.Add(PathSegment.Ballistic(new Vector3(atLip.x, impact * DrumRestitution, 0f), BallLandY));
            BallRun.Start();
        }

        // The swing is over: the marble leaves the cap straight up with the speed of the ruler's tip.
        void LaunchMarble(float speed)
        {
            if (MarbleFlying || marbleChild == null || Ruler.Removed) return;
            Vector3 from = Ruler.Transform.TransformPoint(ToyFactory.CatapultMarbleCenter);
            MarbleApex = from.y + speed * speed / (2f * Game.Gravity);
            bool under = Mathf.Abs(from.x - HoopCentre.x) <= HoopRadius && Mathf.Abs(from.z - HoopCentre.z) <= HoopRadius;
            // (Only a run closes the switch: a marble that hops while the machine sets itself up falls back.)
            MarbleThrough = under && MarbleApex >= HoopNeed && Director.State == MachineState.Running;
            marbleWide = !under;

            List<PathSegment> legs = MarblePath.Segments;
            legs.Clear();
            if (MarbleThrough)
            {
                // Up past the hoop and down through it, eased onto its axis on the way.
                float time = (speed + Mathf.Sqrt(Mathf.Max(0f, speed * speed - 2f * Game.Gravity * (HoopCentre.y - from.y)))) / Game.Gravity;
                legs.Add(PathSegment.Ballistic(new Vector3((HoopCentre.x - from.x) / time, speed, (HoopCentre.z - from.z) / time), HoopCentre.y));
                legs.Add(PathSegment.Spline(TubePath, 0.6f));
            }
            else
            {
                // Up, and back down into the cap.
                legs.Add(PathSegment.Ballistic(new Vector3(0f, speed, 0f), from.y));
            }
            marbleChild.gameObject.SetActive(false);
            marbleMover.Transform.localScale = Vector3.one * Ruler.Scale;
            marbleMover.Teleport(from, Quaternion.identity);
            marbleLight.enabled = true;
            MarbleFlying = true;
            if (!MarblePath.Start())
            {
                RestoreMarble();
                return;
            }
            Director.Report(LinkMarble);
        }

        bool marbleWide, riderOnCap, struckByPlayer, struckByBall;
        // Ticks since the ball came down over the ruler without tipping it (-1: not waiting).
        int ballTicks;

        void OnMarbleDone()
        {
            if (MarbleThrough && Director.State == MachineState.Running)
            {
                // It is in the tube: the switch at its foot closes.
                marbleLight.enabled = false;
                SwitchClosed = true;
                switchLamp?.Set(Palette.Go);
                Director.Report(LinkSwitch);
                if (!Collar.Seated)
                {
                    Director.Fail(FanMissingLine);
                    return;
                }
                fanTicks = 0;
                return;
            }
            RestoreMarble();
            if (Director.State != MachineState.Running) return;
            Director.Fail(struckByPlayer ? PlayerLine : marbleWide ? MarbleWideLine : riderOnCap ? RiderLine : !struckByBall ? OtherWeightLine : MarbleLine);
        }

        void RestoreMarble()
        {
            MarbleFlying = false;
            if (marbleLight != null) marbleLight.enabled = false;
            if (marbleChild != null) marbleChild.gameObject.SetActive(true);
        }

        void OnBaseballStruck()
        {
            if (Director.State == MachineState.Running) Director.Report(LinkTarget);
            BaseballRun.Start();
        }

        void OnClockCrushed()
        {
            Exit.Unlock();
            ExitDoor.Open();
            Director.Report(LinkClock);
            Say(DoneLine, 6f);
        }

        // The run stopped at a link: everything goes back to where a press of the pedal finds it.
        void OnRunStopped()
        {
            // (The review, second pass: asked before the ball's run is called off. The run drives the ball
            // through the hopper's own drive, so until the ball has landed the hopper still counts it as
            // seated - and a run that was stopped while the ball was on its way (something else struck the
            // ruler first) left it lying where it came down, beside the ruler, with "Nothing rolls" from then on.)
            bool onItsWay = BallRun.Running;
            Gate.Close();
            BallRun.Reset();
            MarblePath.Reset();
            RestoreMarble();
            SwitchClosed = false;
            switchLamp?.Set(Palette.Amber);
            fanTicks = -1;
            chainTicks = -1;
            wheezeTicks = -1;
            ballTicks = -1;
            Wind.Strength = 0f;
            Chain.Reset();
            // A ball that left the hopper is lifted back, at the size the player gave it.
            if (runFromPedal && ballWasSeated && !Ball.Removed && !Ball.Held && (onItsWay || Hopper.SeatedProp != Ball))
            {
                liftMover = Ball.BeginDrive();
                if (liftMover != null)
                {
                    liftFrom = Ball.Position;
                    liftTo = HopperFoot + Vector3.up * (0.5f * Ball.Scale);
                    liftTick = 0;
                    Ball.Grabbable = false;
                }
            }
        }

        // The machine is idle again and the toys are the player's.
        void OnMachineReady()
        {
            if (!Ball.Removed) Ball.Grabbable = true;
            if (!Ruler.Removed) Ruler.Grabbable = true;
            if (!Fan.Removed) Fan.Grabbable = true;
            if (!Dominoes.Removed) Dominoes.Grabbable = true;
        }

        void Tick(float dt)
        {
            switchLamp?.Pulse(context.Ticks * Sim.Dt);

            if (handOn)
            {
                handOn = false;
                if (HopperApproach.Seated) HopperApproach.Release();
                if (LaneApproach.Seated) LaneApproach.Release();
            }
            // While the machine runs or sets itself up, a toy that is not in the hand is the machine's, however
            // it came to lie where it lies. (The review, second pass: the dominoes, let go from the pedal in a
            // run and taken by the lane's approach in the same tick, came out of it grabbable again - the
            // socket had found them so, a tick before the let-go was heard - and could be picked up out of
            // their own fall.)
            if (MachineBusy)
            {
                Keep(Ball);
                Keep(Ruler);
                Keep(Fan);
                Keep(Dominoes);
            }
            if (missToy != null && context.Ticks >= missTick) CheckMiss();
            if (turnTick >= 0 && context.Ticks >= turnTick)
            {
                // (A set that still lies on the lane is the lane's: it goes back to its book, and is told then.
                // The review: with a higher aim it took longer to come to rest up there, and both were said.)
                Vector3 at = Dominoes.Center;
                bool onTheLane = at.x > PlinthMinX && at.x < PlinthMaxX && at.z > BandZ && at.y > LaneTop;
                if (onTheLane && !Dominoes.Removed && !Dominoes.Held)
                {
                    turnTick = context.Ticks + 30;
                }
                else
                {
                    turnTick = -1;
                    if (!Dominoes.Removed && !Dominoes.Held && !Dominoes.Driven && laneRefusal == FitState.Backwards) Say(DominoTurnShortLine, 6f);
                }
            }
            // A ball too small for the hopper that lies in it drops between the bars and rolls out at the tower's foot.
            if (!Ball.Removed && !Ball.Held && !Ball.Driven && !Ball.Frozen && Ball.Scale < HopperMinScale && InHopper(Ball.Center)
                && Ball.Velocity.sqrMagnitude < 4f && !RejectPort.IsPending(Ball))
            {
                RejectPort.Eject(Ball, 0.35f);
                Say(BallSmallLine, 6f);
            }

            // The ball's way back up to the hopper.
            if (liftMover != null)
            {
                if (Ball.Removed || !Ball.Driven)
                {
                    liftMover = null;
                }
                else
                {
                    liftTick++;
                    float t = GadgetKit.Smooth((float)liftTick / LiftTicks);
                    Vector3 at = Vector3.Lerp(liftFrom, liftTo, t) + Vector3.up * (Mathf.Sin(Mathf.PI * t) * 3.5f);
                    liftMover.MoveTo(at, Ball.Rotation);
                    if (liftTick >= LiftTicks)
                    {
                        liftMover = null;
                        // Let go inside the hopper: its socket takes the ball again.
                        Ball.EndDrive(Vector3.zero);
                    }
                }
            }

            // The ruler comes back to rest once the machine is idle and nothing is left on its short arm.
            if (leverSpent && Seesaw != null && Director.State == MachineState.Idle && !ShortArmLoaded())
            {
                leverSpent = false;
                Seesaw.Enabled = true;
            }

            if (Director.State != MachineState.Running) return;

            // The ball has come down on a ruler and nothing tips: not a bare pivot. Somebody stands on the long
            // end (and the ball is too light to lift them), or something still holds the short end down.
            if (ballTicks >= 0 && ++ballTicks >= BallWaitTicks)
            {
                ballTicks = -1;
                if (Seesaw != null && Fulcrum.Seated && Director.LastLink == LinkBall)
                {
                    bool playerOn = GadgetKit.PlayerStandsOn(context.Game, Seesaw.Mover.Body);
                    if (playerOn || leverSpent)
                    {
                        Director.Fail(playerOn ? RiderLine : ShortArmLine);
                        return;
                    }
                }
            }

            // The switch is closed: the fan spins up, and after half a second it blows as hard as its size lets it.
            if (fanTicks >= 0)
            {
                fanTicks++;
                if (!Collar.Seated)
                {
                    fanTicks = -1;
                    Director.Fail(FanMissingLine);
                    return;
                }
                if (fanTicks == SpinUpTicks)
                {
                    float strength = FanStrength(Fan.Scale);
                    Wind.Strength = strength;
                    Director.Report(LinkFan);
                    fanTicks = -1;
                    if (strength < 1f) wheezeTicks = 0;
                    else chainTicks = 0;
                }
            }
            if (wheezeTicks >= 0 && ++wheezeTicks >= WheezeTicks)
            {
                wheezeTicks = -1;
                Director.Fail(FanSmallLine);
                return;
            }
            if (chainTicks >= 0 && ++chainTicks >= ChainDelayTicks)
            {
                chainTicks = -1;
                bool seated = Lane.Seated && Lane.SeatedProp == Dominoes;
                bool inTheWind = seated && Wind.Contains(Chain.PieceCenter(0));
                if (!inTheWind || !Chain.Start())
                {
                    Director.Fail(seated && !inTheWind ? WindShortLine : WindLine);
                    return;
                }
                Director.Report(LinkWind);
            }
            SpinBlades(Wind.Strength, dt);
        }

        /// <summary>How hard a fan of this scale blows: its area over that of a fan of 0.6.</summary>
        public static float FanStrength(float scale) => scale * scale / (FanReference * FanReference);

        // The blades of the seated fan turn while it blows (a look: the blades have no collider).
        void SpinBlades(float strength, float dt)
        {
            if (fanBlades == null || Fan.Removed) return;
            if (strength <= 0f || Fan.Held) return;
            bladeAngle = Mathf.Repeat(bladeAngle + Mathf.Min(1440f, 420f + 420f * strength) * dt, 360f);
            fanBlades.localRotation = Quaternion.Euler(0f, 0f, bladeAngle);
        }

        // One line, and not the same one over and over.
        void Say(string text, float seconds = 5f)
        {
            if (text == null) return;
            if (text == lastLine && context.Ticks - lastSaid < 360) return;
            lastLine = text;
            lastSaid = context.Ticks;
            context.Say(text, seconds);
        }

        // ---- The solution ------------------------------------------------------------------------------------------------------------

        public override IEnumerator Solve(Bot bot)
        {
            yield return PlaceDominoes(bot);
            yield return PlaceBall(bot);
            yield return ToTheTrench(bot);
            yield return PlaceRuler(bot);
            yield return PlaceFan(bot);
            yield return ToThePedal(bot);
            yield return Watch(bot);
            yield return bot.Until(() => Clock.Broken, 25f);
            yield return ToTheExit(bot);
        }

        /// <summary>On the pedal: follow the motion with the eyes, link by link, for as long as it runs.</summary>
        public IEnumerator Watch(Bot bot)
        {
            bool Running() => Director.State == MachineState.Running;
            yield return bot.Until(() => Director.State != MachineState.Idle, 2f);
            yield return Follow(bot, () => Ball.Center, () => MarbleFlying || !Running(), 6f);
            yield return Follow(bot, () => HoopCentre + Vector3.down * 0.8f, () => SwitchClosed || !Running(), 4f);
            yield return Follow(bot, () => new Vector3(Chain.PieceCenter(Mathf.Min(Chain.Fallen, Chain.PieceCount - 1)).x, LaneTop + 1.5f, AxisZ), () => Chain.Struck || !Running(), 14f);
            yield return Follow(bot, () => Baseball.Center, () => Clock.Broken || !Running(), 6f);
        }

        // Keeps the view on a point that moves, until a condition holds or the time is up. It never fails.
        static IEnumerator Follow(Bot bot, System.Func<Vector3> target, System.Func<bool> until, float seconds)
        {
            Game game = bot.Game;
            float end = game.Time + seconds;
            while (!until() && game.Time < end)
            {
                yield return bot.LookAt(target(), 2f);
                yield return bot.Wait(Sim.Dt);
            }
        }

        /// <summary>From the start: the dominoes are under the crosshair. Round the book to the edge, and against the wall above the lane.</summary>
        public IEnumerator PlaceDominoes(Bot bot)
        {
            yield return bot.Grab(Dominoes);
            yield return bot.WalkTo(new Vector3(16f, GalleryTop, 6f));
            yield return bot.WalkTo(DominoStand, 0.15f);
            yield return bot.DropAt(DominoAim);
            yield return bot.Until(() => Lane.Seated, 5f);
        }

        /// <summary>Along the quilt, south of the ramp notch, to the spool: the ball, up to its limit above the funnel.</summary>
        public IEnumerator PlaceBall(Bot bot)
        {
            yield return bot.WalkTo(new Vector3(10f, GalleryTop, 4.5f), 0.4f, 10f, true);
            yield return bot.WalkTo(new Vector3(-16f, GalleryTop, 4.5f), 0.4f, 12f, true);
            yield return bot.WalkTo(BallPickup, 0.12f);
            yield return bot.Grab(Ball);
            yield return bot.WalkTo(new Vector3(-16.8f, GalleryTop, 6f));
            yield return bot.WalkTo(BallStand, 0.15f);
            yield return bot.DropAt(BallAim);
            yield return bot.Until(() => Hopper.Seated, 5f);
        }

        /// <summary>From anywhere on the quilt: south of the notch, then down the ramp.</summary>
        public IEnumerator ToTheTrench(Bot bot)
        {
            yield return bot.WalkTo(new Vector3(4f, GalleryTop, 4.5f), 0.4f, 14f, true);
            yield return bot.WalkTo(new Vector3(7.5f, GalleryTop, 5f), 0.3f, 6f);
            yield return bot.WalkTo(new Vector3(7.5f, 0f, 12.6f), 0.3f, 8f);
        }

        /// <summary>The ruler: the same size, the right way round.</summary>
        public IEnumerator PlaceRuler(Bot bot)
        {
            yield return bot.WalkTo(new Vector3(-3.5f, 0f, 14.2f), 0.4f, 10f, true);
            yield return bot.WalkTo(RulerStand, 0.12f);
            yield return bot.Grab(Ruler);
            yield return bot.LookAt(RulerAim);
            // It points at the wall now, cap first: six clicks turn the cap toward the hoop.
            yield return bot.RotateHeld(6);
            yield return bot.DropAt(RulerAim);
            yield return bot.Until(() => Fulcrum.Seated, 4f);
        }

        /// <summary>The fan: picked up from in front of its garage, 24 units away, and let go inside.</summary>
        public IEnumerator PlaceFan(Bot bot)
        {
            yield return bot.WalkTo(FanStand, 0.12f);
            yield return bot.Grab(Fan);
            yield return bot.DropAt(FanAim);
            yield return bot.Until(() => Collar.Seated, 4f);
        }

        /// <summary>From the trench: up the ramp and onto the pedal.</summary>
        public IEnumerator ToThePedal(Bot bot)
        {
            yield return bot.WalkTo(new Vector3(7.5f, 0f, 12.6f), 0.3f, 10f, true);
            yield return bot.WalkTo(new Vector3(7.5f, GalleryTop, 5f), 0.3f, 8f);
            yield return bot.WalkTo(new Vector3(4f, GalleryTop, 4.5f), 0.3f, 6f);
            yield return bot.WalkTo(PedalPoint, 0.2f, 8f);
        }

        /// <summary>From the pedal: down the ramp, along the trench and out through the door.</summary>
        public IEnumerator ToTheExit(Bot bot)
        {
            yield return bot.WalkTo(new Vector3(4f, GalleryTop, 4.5f), 0.3f, 8f);
            yield return bot.WalkTo(new Vector3(7.5f, GalleryTop, 5f), 0.3f, 6f);
            yield return bot.WalkTo(new Vector3(7.5f, 0f, 12.6f), 0.3f, 8f);
            yield return bot.WalkTo(new Vector3(26.5f, 0f, ExitCentre.z), 0.4f, 12f, true);
            yield return bot.WalkTo(new Vector3(ExitCentre.x, 0f, ExitCentre.z), 0.5f, 6f);
            yield return bot.Until(() => bot.Game.LevelCompleted, 3f);
        }
    }
}
