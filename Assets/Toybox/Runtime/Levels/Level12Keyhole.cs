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
    /// Level 12, "The Keyhole" (LEVELS.md): one toy, two jobs - the bridge has to be taken away to be used.
    ///
    /// The top of a toy chest of drawers. Its drawer stands open, twelve units of socks between the chest
    /// and the front door of a dollhouse, and a brass key the length of a bus lies across it as the bridge.
    /// The door is flush in a sheer wall: over there the player stands on the key and on nothing else, and
    /// nobody can pick up what they stand on. So the key has to stop being the bridge (picked up from the
    /// chest and put down close by it is a trinket), something else has to fill the drawer (the spool,
    /// held up against the outline over the door, comes down ten units across), and then the key is
    /// carried over the spool and held up to the lock.
    ///
    /// How the level says what will work, before anything is let go:
    /// - the paint is the spool's gauge, as in Level 2: the dashes of its outline on the door wall, of the
    ///   line along the atlas and of the line along the chest's edge are green while the spool in the
    ///   hand would fill the drawer from where it is held, and red (blinking) while it would not. (The
    ///   outline itself cannot be the measure: a spool that fills the drawer is ten units deep, its near
    ///   side is ten units nearer than the wall, and on screen it is far bigger than its own outline.)
    /// - a spool that is big enough, let go with its middle over the drawer, is drawn to the middle of
    ///   the drawer against the door wall before it falls (LEVELS' own fallback). It never comes down
    ///   to one side, a step short of the door, or balanced on the chest's edge.
    ///
    /// What the level does about tries that go wrong:
    /// - a key that is too big for the lock, or let go beside the door, is told so - and whether it only
    ///   has to be carried closer or has to be picked up from farther off. It is never too small: it
    ///   shrinks no further than its lock takes;
    /// - a key that is lost (among the socks under the spool, in the slot under the door) or has become a
    ///   crumb is put back on the little shelf at pocket size (or in front of it, if the shelf is taken);
    /// - a spool that looks too small in the hand is told so at the pick-up, in the campaign's words; one
    ///   that came down too small to stand on goes back to where it stood, with the reason; one that
    ///   stopped on the floor, on the key, or at its full size with its middle still over the chest is
    ///   told why.
    ///
    /// The two ways round that were found and closed. The second review: nobody gets to the open door
    /// from above. A spool picked up in mid-air - from under one's own feet, at the top of a jump off it -
    /// gets no bigger until the player has landed, so it cannot be ridden up into a tower; and whoever is
    /// high over the empty drawer all the same (above <see cref="HighY"/>) is sent back to the start.
    /// The third review: nothing but the key holds a spool up over the drawer. The spool cannot tip, and
    /// it hung by its rim wherever the rim found a hold - on the sill under the shut door, 0.015 deep,
    /// where it was something else to stand on and the key could be picked up from it and let go at the
    /// keyhole. The door stops things at the wall's own face now, and a spool whose middle is over the
    /// drawer slips off whatever its rim rests on (LetHangingSpoolFall).
    ///
    /// Also since the third review: the two shoe-print marks are where a spool picked up and let go fills
    /// the drawer from anywhere on them (not only from the solver's own spots), and a spool that goes back
    /// to where it stood puts a key that lies there by then on top of itself, not inside.
    /// (Where this differs from LEVELS.md, and what was measured: tools/out/notes/level12-build.md and
    /// level12-review.md.)
    /// </summary>
    [Level(12, "the-keyhole", "The Keyhole", Phase = 3)]
    public sealed class Level12Keyhole : LevelDefinition
    {
        // ---- The layout (LEVELS.md, Level 12). The chest's top is y = 0, travel +Z. --------------------------
        public const float HalfWidth = 6f, BackZ = -6f, EdgeZ = 8f, DoorZ = 20f;
        /// <summary>The tops of the socks: what a toy in the drawer rests on.</summary>
        public const float PitFloor = -9f;
        /// <summary>The shelf the chest stands on (the drawer's bottom board and the socks lie between it and <see cref="PitFloor"/>).</summary>
        public const float BaseY = -9.5f;
        /// <summary>
        /// The top of the walls as they are drawn. (LEVELS: 12. One more, so that the spool's outline fits
        /// between the door and the eaves.)
        /// </summary>
        public const float WallTop = 13f;
        /// <summary>
        /// The sky cap, and how high the walls stop a held toy (above <see cref="WallTop"/> nothing of that
        /// is drawn). Two above the eaves: a spool held against the top of its outline still grows until it
        /// meets the door wall, instead of being cut short by the sky and coming down short of the door.
        /// </summary>
        public const float CapY = 15f;
        const float WallThickness = 1f, FacadeDepth = 2f, TunnelEnd = 24f, ChestLeft = -9.5f;

        /// <summary>The door: an opening 2 x 2.6 whose sill is 0.45 above the chest's top, closed by a slab.</summary>
        public const float SillY = 0.45f, DoorTop = 3.05f, DoorHalfWidth = 1f, DoorThickness = 0.3f, DoorTravel = 2.7f;
        /// <summary>
        /// How far the door's face stands behind the wall's. Nothing: a recess would be a ledge to stand on
        /// beside the key, and the far side has no floor. A hair, so that the raised door is behind the wall -
        /// and that hair is filled by the door's collider (BuildDoor): a sill 0.015 deep held the spool up.
        /// </summary>
        public const float DoorRecess = 0.015f;
        /// <summary>The lock takes a key whose middle is in front of the door's face, this far inside its edges.</summary>
        public const float CaptureMargin = 0.12f;
        /// <summary>The slot under the sill that the key's tip lies in: too low to get into.</summary>
        public const float SlotHalfWidth = 1.1f, SlotTop = 0.4f, SlotDepth = 1.2f;
        /// <summary>
        /// The keyhole. (LEVELS: at 1.65, the height of the eye. A key is held level, pointing away: at the
        /// height of the eye it is seen end-on, a sliver. At chest height its flat is in view all the way in.)
        /// </summary>
        public static readonly Vector3 Keyhole = new Vector3(0f, 1.25f, DoorZ);
        public static readonly Vector3 DoorClosed = new Vector3(0f, (SillY + DoorTop) * 0.5f, DoorZ + DoorRecess + DoorThickness * 0.5f);

        // ---- The key -----------------------------------------------------------------------------------------
        public const string KeyTag = "key";
        /// <summary>
        /// (LEVELS: clamps 0.05 to 8. The lower one is the lock's own: a key that is carried right up to the
        /// keyhole - which is what one does with a key - is never too small for it. With 0.05 a key that looked
        /// under 0.17 in the hand came out under 0.06 from touching distance and was sent back to the shelf.)
        /// </summary>
        public const float KeyStart = 8f, KeyMin = LockMin, KeyMax = 8f;
        /// <summary>The key as the bridge: level, its bow on the chest, its tip one unit into the slot.</summary>
        public static readonly Vector3 KeyCentre = new Vector3(0f, 0.16f, 13f);
        /// <summary>What the keyhole takes (LEVELS Appendix B).</summary>
        public const float LockMin = 0.06f, LockMax = 0.5f;
        /// <summary>The size the lock draws the key down to as it goes in.</summary>
        public const float SeatScale = 0.15f;
        /// <summary>A key that lies among the socks and is smaller than this is out of sight from the chest.</summary>
        public const float KeyHandy = 1f;
        /// <summary>A key let go smaller than this (a fifth of a unit long) is too small to find again where it lies.</summary>
        public const float KeyCrumb = 0.1f;
        /// <summary>
        /// The size a key has that the level puts on the shelf, and of the key painted there. (LEVELS: 0.31,
        /// what the intended let-go leaves; the solver's own is 0.58. Half a unit, a key one unit long: picked
        /// up from a few steps away it still looks big enough to be held up to the lock from a step away.)
        /// </summary>
        public const float ShelfScale = 0.5f;
        /// <summary>
        /// Apparent size (scale / pick-up distance) above which the key cannot be made small enough for the
        /// lock (it comes no nearer the eye than 0.35).
        /// </summary>
        public const float KeyNearRatio = 1.35f;
        /// <summary>
        /// Apparent size up to which the key goes into the lock from a step in front of the door (0.5 at the
        /// door from 1.05 away; the solver's key looks 0.52). A key that was too big and looks no bigger than
        /// this only has to be carried closer.
        /// </summary>
        public const float KeyEasyRatio = 0.9f;

        // ---- The little shelf on the back wall ------------------------------------------------------------------
        public const float ShelfMinX = -5.2f, ShelfMaxX = -2.8f, ShelfFront = -4.6f, ShelfTop = 1.3f, ShelfThickness = 0.2f;
        /// <summary>The middle of the shelf's top: where the key is put down, and where a lost one comes back.</summary>
        public static readonly Vector3 ShelfSpot = new Vector3(-4f, ShelfTop, -5.3f);
        public static readonly Vector3 ShelfStand = new Vector3(-4f, 0f, -4.25f);

        // ---- The spool -------------------------------------------------------------------------------------------
        public const string SpoolTag = "spool";
        public const float SpoolStart = 1f, SpoolMin = 0.5f, SpoolMax = 9.9f;
        public static readonly Vector3 SpoolCentre = new Vector3(3f, 0.5f, 3f);
        /// <summary>
        /// The smallest spool that will do: its top 0.65 below the chest and 1.1 below the sill, which is as
        /// high as a hop goes. (LEVELS Appendix B: 8.35 to the clamp at 9.9.)
        /// </summary>
        public const float FillMin = 8.35f;
        /// <summary>The outline on the door wall is the spool's side at this size.</summary>
        public const float OutlineScale = 9.1f;
        /// <summary>A spool let go smaller than this cannot be picked up from close enough to grow back.</summary>
        public const float SpoolCrumb = 0.75f;
        /// <summary>
        /// A spool that stands further than this from the door wall is told so: from its rim the key held
        /// toward the keyhole is too big for it, and the sill is a long jump away. (What is left of that
        /// since the drawer draws the spool in: a spool it could not draw - the player stood in its way.)
        /// </summary>
        public const float GapMax = 1f;
        /// <summary>
        /// Apparent size (scale / pick-up distance) under which the spool cannot be made to fill the drawer
        /// from anywhere on the chest: picked up from two units behind it or more. (Measured: against the
        /// door wall it is 14.7 times that from the mark and 19 times from the back board.)
        /// </summary>
        public const float SpoolFarRatio = 0.45f;
        /// <summary>
        /// Where the solver stands to pick the spool up, and where it lets go of it from. From these two the
        /// spool against the door wall is the outline's size. The paint is what says when, from wherever
        /// anybody else stands.
        /// </summary>
        public static readonly Vector3 SpoolStand = new Vector3(3f, 0f, 1.85f);
        public static readonly Vector3 ThrowStand = new Vector3(0f, 0f, 1f);
        /// <summary>
        /// The middles of the two shoe-print marks: where to stand to pick the spool up, and where to let go
        /// of it from. (They were at the solver's two stands. Measured there: a spool picked up from more
        /// than 1.4 behind its middle is too small from the second mark, and the first mark reached to 1.77 -
        /// whoever stopped a quarter of a unit short of its prints stood on the mark and got red paint. So
        /// the first prints are right behind the spool, 0.9 from its middle, where nobody gets nearer than
        /// 0.85, and the second mark lies a third of a unit further from the edge: from within a step of the
        /// first prints and from anywhere on the second mark the paint is green. The solver's own stands are
        /// a step short of both prints, as somebody stops who walks up to them.)
        /// </summary>
        public static readonly Vector3 SpoolMark = new Vector3(3f, 0f, 2.1f);
        public static readonly Vector3 ThrowMark = new Vector3(0f, 0f, 0.7f);
        /// <summary>The middle of the painted outline: what to hold the spool against.</summary>
        public static readonly Vector3 OutlineCentre = new Vector3(0f, 7.95f, DoorZ);
        /// <summary>The dashed line along the atlas: its height, and where it begins (it ends at the door wall).</summary>
        public const float TapeY = 2.5f, TapeFrom = -1f;
        /// <summary>
        /// A spool that is big enough, let go with its middle over the drawer, is drawn to the middle of the
        /// drawer and against the door wall before it falls (LEVELS' fallback for this level: "eased onto the
        /// pit's centre line"). This far from the wall, and this fast.
        /// </summary>
        public const float DoorClearance = 0.02f, GuideSpeed = 12f;

        public static readonly Vector3 Spawn = new Vector3(0f, 0f, -2f);
        public const float SpawnYaw = 0f, SpawnPitch = -5.4f;
        public static readonly Vector3 ExitCentre = new Vector3(0f, 1.75f, 22.5f);
        public static readonly Vector3 ExitSize = new Vector3(2f, 2.6f, 2f);
        /// <summary>Feet below this over the drawer are sent back to the start (also from a spool that is too short).</summary>
        public const float HazardTop = -1.5f;
        /// <summary>
        /// Feet higher than this over the drawer are sent back to the start as well. The highest anybody gets
        /// there by walking and jumping is a jump from the biggest spool (0.9 + 1.25 = 2.15; 2.5 from the key
        /// where it lies on that spool). Whoever is higher has climbed something that stands on the chest,
        /// and from a tower by the chest's edge a running jump comes down through the open doorway (measured:
        /// a spool 9.9 high, a sprint and a jump from 3.3 before its rim - out, and the drawer was never
        /// filled; the same from spools 6 and 4 high that hung out from the edge by nearly their whole width -
        /// they could, as they cannot tip, until the third review: now a spool stands out by half its width
        /// at most, see LetHangingSpoolFall). By the arithmetic a spool has to be about 3.5 high before a
        /// jump from it is long enough, and the top of that jump is 4.7; from anything that is lower than this
        /// a jump that stays under it is more than two units short. Nobody arrives at the door from above.
        /// (The rule is off while a spool that fills the drawer stands in it: then the way across is there.)
        /// </summary>
        public const float HighY = 3.3f;

        // ---- What is said ----------------------------------------------------------------------------------------
        /// <summary>Said on a pick-up from far off. The words are Level 1's, on purpose.</summary>
        public const string FarLine = "It only ever gets as big as it looks. Pick it up from closer.";
        public const string StandingLine = "You cannot pick up what you are standing on.";
        public const string LockedLine = "Locked. The keyhole is tiny, and the only key is under your feet.";
        public const string KeyNearLine = "From this close it looks too big for the keyhole. Pick it up from farther off.";
        public const string TooBigLine = "Too big for the keyhole. Pick it up from farther off and let go up close.";
        /// <summary>Too big where it was let go, but it looks small enough: nearer the door it fits.</summary>
        public const string KeyCloserLine = "Too big for the keyhole from there. Carry it right up to the door: it shrinks as it gets close.";
        public const string KeyholeLine = "Hold the key right up to the keyhole, then let go.";
        public const string KeyLostLine = "The key fell in among the socks. It is back on the little shelf.";
        public const string KeyCrumbLine = "That key was too small to find again. It is back on the little shelf.";
        public const string SpoolSmallLine = "Too small to fill the drawer. It is back where it stood: step back with it until the paint is green.";
        public const string SpoolFarSmallLine = "Too small to fill the drawer. It is back where it stood: pick it up from closer.";
        public const string SpoolSideLine = "It caught on the side wall and stayed too small. Hold it in the middle, over the door.";
        public const string SpoolFloorLine = "It touched the floor before it was over the drawer. Hold it higher, against the outline.";
        /// <summary>Too small, let go in the air over the chest's edge, and it came down with its rim on the chest.</summary>
        public const string SpoolEdgeLine = "It came down on the edge of the chest, not in the drawer. Hold it against the outline.";
        public const string SpoolKeyLine = "The key is in the way. Nothing fills the drawer while the key lies across it.";
        public const string SpoolClampLine = "That is as big as it gets, and it is not over the drawer. Let go of it from nearer the edge.";
        /// <summary>Big enough and in the drawer, but more than a step short of the door (see <see cref="GapMax"/>).</summary>
        public const string SpoolGapLine = "It stands too far from the door. Pick it up again and hold it against the outline.";
        public const string SpoolCrumbLine = "That spool was too small to be of use. It is back where it stood.";
        /// <summary>Picked up in mid-air, let go in mid-air, and it would have grown (see HoldInTheAir).</summary>
        public const string AirLine = "Picked up in the air it does not grow. Pick it up with both feet on the ground.";
        /// <summary>Said to whoever is sent back from high over the drawer (see <see cref="HighY"/>).</summary>
        public const string HighLine = "Nobody gets to the door from up there. Fill the drawer and walk across.";

        Prop key, spool;
        Socket keyhole;
        Door door;
        HazardZone hazard, high;
        // The spool was picked up in mid-air and the player has not landed since: it gets no bigger.
        bool airHold, spoolWasHeld;
        ReturnPort shelfPort;
        ReturnPortOptions shelfMouth;
        PropLeash keyLeash, spoolLeash;
        Exit exit;
        SignalLamp[] tumblers;
        string lastLine, spoolReturnLine, keyReturnLine;
        int lastSaid;
        /// <summary>scale / distance of the last pick-up of each toy.</summary>
        float keyRatio, spoolRatio;
        /// <summary>The tick the lock took the key (-1: it has not), and the tick the key was home in it.</summary>
        int captureTick, seatedTick;
        bool lockedTold, wasHolding;
        /// <summary>The tick the key was last let go (-1: nothing left to say about it), and whether the view was on the lock.</summary>
        int keyDropTick;
        bool keyAtLock;
        /// <summary>The spool was let go bigger than it started and nothing has been said about it yet.</summary>
        bool spoolPending;
        // How the spool was let go: touching the chest's top, against a side wall, at its full size.
        bool dropOnFloor, dropAtSide, dropClamped;
        int spoolRestTicks;
        // The paint that is the spool's gauge: the outline's dashes, the line along the atlas and the line
        // along the chest's edge; the paint they wear while nothing is judged; and what they say now.
        MeshRenderer outlinePaint, tapePaint, edgePaint;
        Material linePaint, edgeIdle;
        FitState spoolTint;
        // The drawer draws a spool that is big enough to its middle, against the door wall.
        bool guiding, guided;
        int guideTick, guideTicks;
        Vector3 guideFrom, guideTo;
        // A spool that hangs by its rim over the drawer slips off what holds it.
        bool slipping;
        int hangTicks, slipTick, slipTicks;
        Vector3 slipFrom, slipTo;

        public Prop Key => key;
        public Prop Spool => spool;
        public Socket Lock => keyhole;
        public Door Door => door;
        public HazardZone Hazard => hazard;
        /// <summary>Sends back whoever is high over the drawer (see <see cref="HighY"/>).</summary>
        public HazardZone HighHazard => high;
        /// <summary>Puts a key that is lost, or a crumb, back on the little shelf.</summary>
        public PropLeash KeyLeash => keyLeash;
        /// <summary>Puts a spool that is of no use where it lies back where it stood.</summary>
        public PropLeash SpoolLeash => spoolLeash;
        public Exit Exit => exit;

        /// <summary>The spool stands in the drawer, big enough to walk across, and has come to rest.</summary>
        public bool SpoolFills => DrawerFilled && spool.Velocity.sqrMagnitude < 0.01f;

        /// <summary>The spool is in the drawer (at rest or on its way down) and big enough to walk across.</summary>
        public bool DrawerFilled => spool != null && !spool.Removed && !spool.Held && SpoolInDrawer() && spool.Scale >= FillMin;

        // (LEVELS: "The door is locked. You are standing on the key." At the start nobody is: the bow of the
        // key begins seven units ahead. The level says it at the door, where it is true - LockedLine.)
        public override string Blurb => "The door is locked. The only key is the bridge.";

        public override string[] Hints => new[]
        {
            "You cannot pick up what is under your feet, and there is nowhere else to stand over there. The key has to stop being the bridge.",
            "Pick the key up from far away and put it down somewhere close: now it is small. Then find something else that could fill the drawer.",
            // (LEVELS: "... Pick the spool up from close, stand a few steps back from the edge, fill the outline
            // above the door and let go." The paint says when: it is the campaign's gauge, as in Level 2.)
            "Pick the key up from where you start and put it on the little shelf. Hold the spool over the outline, step back until the paint is green, let go. Carry the key over the spool to the lock.",
        };

        public override string Environment => "high-shelf";

        // The chest stands nine and a half units high on the shelf; its open drawer goes down to the socks.
        public override float GroundY => BaseY;
        public override float KillY => -30f;

        public override void Build(LevelContext ctx)
        {
            key = null;
            spool = null;
            keyhole = null;
            door = null;
            hazard = null;
            high = null;
            airHold = spoolWasHeld = false;
            shelfPort = null;
            shelfMouth = null;
            keyLeash = null;
            spoolLeash = null;
            exit = null;
            tumblers = null;
            lastLine = null;
            spoolReturnLine = null;
            keyReturnLine = null;
            lastSaid = -100000;
            keyRatio = 0f;
            spoolRatio = 0f;
            captureTick = -1;
            seatedTick = -1;
            lockedTold = false;
            wasHolding = false;
            keyDropTick = -1;
            keyAtLock = false;
            spoolPending = false;
            dropOnFloor = dropAtSide = dropClamped = false;
            spoolRestTicks = 0;
            outlinePaint = tapePaint = edgePaint = null;
            linePaint = edgeIdle = null;
            spoolTint = FitState.Idle;
            guiding = guided = false;
            guideTick = guideTicks = 0;
            guideFrom = guideTo = Vector3.zero;
            slipping = false;
            hangTicks = slipTick = slipTicks = 0;
            slipFrom = slipTo = Vector3.zero;

            Dip dip = ctx.Dip;
            BuildSolids(ctx);
            BuildLooks(ctx, dip);
            BuildShelf(ctx, dip);
            BuildPaint(ctx, dip);
            BuildDressing(ctx);
            SkyCap.Add(ctx, -HalfWidth - WallThickness, HalfWidth + WallThickness, BackZ - WallThickness, TunnelEnd + WallThickness, CapY);

            // The key, as the bridge. It lies where it lies until it is picked up for the first time.
            key = ToyCatalog.Add(ctx, ToyId.Key, KeyCentre, KeyStart, null, o =>
            {
                o.MinScale = KeyMin;
                o.MaxScale = KeyMax;
                o.Tags = new[] { KeyTag };
                o.FrozenUntilGrabbed = true;
            });
            // The spool stands on the chest. Nobody kicks it away from its mark before they have picked it up.
            spool = ToyCatalog.Add(ctx, ToyId.ThreadSpool, SpoolCentre, SpoolStart, null, o =>
            {
                o.MinScale = SpoolMin;
                o.MaxScale = SpoolMax;
                o.Tags = new[] { SpoolTag };
                o.FrozenUntilGrabbed = true;
                // It is a pillar and a platform: it always stands on its rim (as Level 2's thimble does).
                // Flipped in the hand it came down on its side - a drawer "filled" by a round wall too high to
                // get onto - or, flipped and turned, tilted on its edge.
                o.AllowPitch = false;
            });

            // The lock: a key of the right size let go at the door is drawn into the keyhole, bit down, and
            // turned. (LEVELS: a box 1.2 x 1.2 round the keyhole. Here it is the door's face, less a margin:
            // the key is held level and stops against the door wherever the view meets it - above the keyhole
            // from a tall spool - and getting it there small enough is the whole of the task.)
            keyhole = new Socket(ctx, new SocketOptions
            {
                Name = "Keyhole", AcceptTag = KeyTag,
                Capture = Zone.MinMax(new Vector3(-DoorHalfWidth + CaptureMargin, SillY + CaptureMargin, DoorZ - 0.85f), new Vector3(DoorHalfWidth - CaptureMargin, DoorTop - CaptureMargin, DoorZ + 0.05f)),
                MinScale = LockMin, MaxScale = LockMax,
                SeatPose = s => new Pose(SeatPosition(s), Quaternion.Euler(0f, 0f, -90f)),
                SeatScale = s => SeatScale,
                EaseSeconds = 0.25f, ThenTurnDegrees = -90f, ThenTurnAxis = Vector3.forward, ThenTurnSeconds = 0.4f,
                LockOnSeat = true,
            });
            keyhole.OnSeated += prop => seatedTick = ctx.Ticks;
            keyhole.OnRejected += (prop, fit) =>
            {
                keyDropTick = -1;
                if (fit == FitState.TooBig) Say(ctx, TooBig(), 6f);
            };

            door = new Door(ctx, new DoorOptions
            {
                Name = "Front Door", Panel = BuildDoor(dip), ClosedPosition = DoorClosed, OpenPosition = DoorClosed + Vector3.up * DoorTravel,
                Motion = DoorMotion.Slide, Seconds = 0.8f, Ease = DoorEase.EaseOut,
            });

            // Whoever falls among the socks is back at the start, with whatever they hold.
            hazard = new HazardZone(ctx, new HazardZoneOptions
            {
                Name = "Drawer",
                Shape = Zone.MinMax(new Vector3(-HalfWidth, BaseY - 1f, EdgeZ), new Vector3(HalfWidth, HazardTop, DoorZ)),
            });
            // And whoever is high over the drawer - on a tower that hangs out from the chest, or in the air
            // on the way down from one. (A step past the chest's edge: standing on something by the edge is
            // nobody's business.)
            high = new HazardZone(ctx, new HazardZoneOptions
            {
                Name = "High Over The Drawer",
                Shape = Zone.MinMax(new Vector3(-HalfWidth, HighY, EdgeZ + 0.4f), new Vector3(HalfWidth, CapY + 1f, DoorZ)),
            });
            high.PlayerCaught += () => Say(ctx, HighLine, 6f);

            // No strand, no restart. The key that is out of sight or too small to find comes back on the
            // little shelf (not as the bridge: that would lie through a spool in the drawer).
            var everywhere = new[] { Zone.Sphere(Vector3.zero, 500f) };
            shelfMouth = new ReturnPortOptions { Name = "Little Shelf", Mouth = ShelfSpot + Vector3.up * 0.05f };
            shelfPort = new ReturnPort(ctx, shelfMouth);
            keyLeash = new PropLeash(ctx, new PropLeashOptions
            {
                Name = "Key", Props = new[] { key }, Forbidden = everywhere, Unless = () => !KeyLost(),
                Grace = 1.5f, Action = LeashAction.Eject, Port = shelfPort,
                OnReturn = prop =>
                {
                    keyReturnLine = prop.Scale < KeyCrumb ? KeyCrumbLine : KeyLostLine;
                    Pocket(prop);
                    shelfMouth.Mouth = KeySpot(ctx.Game);
                },
            });
            keyLeash.PropReturned += prop => Say(ctx, keyReturnLine, 6f);
            // And the spool that is of no use where it is goes back to where it stood.
            spoolLeash = new PropLeash(ctx, new PropLeashOptions
            {
                Name = "Spool", Props = new[] { spool }, Forbidden = everywhere, Unless = () => !SpoolUseless(),
                Grace = 1.5f, Action = LeashAction.Respawn,
                OnReturn = prop => spoolReturnLine = prop.Scale < SpoolCrumb ? SpoolCrumbLine
                    : spoolRatio > 0f && spoolRatio < SpoolFarRatio ? SpoolFarSmallLine
                    : dropAtSide ? SpoolSideLine
                    // (Grown, and stopped by the chest's top with its middle past the edge: it slipped in from there.)
                    : dropOnFloor && prop.Scale >= 2.5f ? SpoolFloorLine : SpoolSmallLine,
            });
            spoolLeash.PropReturned += prop =>
            {
                spoolPending = false;
                Say(ctx, spoolReturnLine, 6f);
                KeyOutOfTheSpool();
            };

            ctx.Game.Events.PropGrabbed += e =>
            {
                if (e.Prop == key)
                {
                    keyDropTick = -1;
                    keyRatio = e.OldScale / Mathf.Max(0.01f, e.GrabDistance);
                    if (keyRatio > KeyNearRatio) Say(ctx, KeyNearLine, 6f);
                }
                else if (e.Prop == spool)
                {
                    spoolPending = false;
                    guiding = guided = slipping = false;
                    hangTicks = 0;
                    spoolRatio = e.OldScale / Mathf.Max(0.01f, e.GrabDistance);
                    // (Not in mid-air: there it does not grow at all, and the level says that instead.)
                    if (spoolRatio < SpoolFarRatio && ctx.Game.Player.Grounded) Say(ctx, FarLine, 6f);
                }
            };
            ctx.Game.Events.PropDropped += e =>
            {
                if (e.Prop == key && !key.Removed)
                {
                    keyDropTick = ctx.Ticks;
                    keyAtLock = LooksAtTheLock(ctx.Game.Player);
                }
                else if (e.Prop == spool && !spool.Removed) NoteSpoolDrop(e.NewScale);
            };
            // The kill plane puts a toy back where the level began with it. For the key that is across the
            // drawer - where the spool may stand by now.
            ctx.Game.Events.PropRespawned += e =>
            {
                if (e.Prop != key || !key.Frozen || !BridgeBlocked()) return;
                Pocket(key);
                shelfMouth.Mouth = KeySpot(ctx.Game);
                shelfPort.Eject(key);
            };
            ctx.OnUpdate(dt => Watch(ctx));

            exit = ctx.AddExit(ExitCentre, ExitSize).Lock();
            door.Opened += () => exit.Unlock();
            ctx.SetSpawn(Spawn, SpawnYaw, SpawnPitch);
        }

        // ---- The rules ---------------------------------------------------------------------------------------

        /// <summary>Where the key's middle is when it is home in the lock at a scale: its bow against the door, its blade inside.</summary>
        public static Vector3 SeatPosition(float scale) => new Vector3(Keyhole.x, Keyhole.y, DoorZ + DoorRecess + 0.4f * scale);

        /// <summary>Does the spool stand in the drawer (not on the chest, not in the hand)?</summary>
        public bool SpoolInDrawer() => !spool.Held && spool.Center.y - 0.5f * spool.Scale < -1f;

        /// <summary>How far the spool's side is from the door wall.</summary>
        public float DoorGap() => DoorZ - (spool.Center.z + ToyFactory.ThreadSpoolRadius * spool.Scale);

        /// <summary>Is the spool of no use where it is: a crumb, or in the drawer and too short to stand on?</summary>
        public bool SpoolUseless() => spool.Scale < SpoolCrumb || (SpoolInDrawer() && spool.Scale < FillMin);

        /// <summary>Where the middle of a spool of this size is (z) when it stands in the drawer against the door wall.</summary>
        public static float SeatZ(float scale) => DoorZ - ToyFactory.ThreadSpoolRadius * scale - DoorClearance;

        /// <summary>Is the drawer drawing the spool to its middle right now?</summary>
        public bool Guiding => guiding;

        /// <summary>What the paint says about the spool: nothing, too small (or not like this), or that it fills the drawer.</summary>
        public FitState SpoolTint => spoolTint;
        public Renderer OutlinePaint => outlinePaint;
        public Renderer TapePaint => tapePaint;
        public Renderer EdgePaint => edgePaint;

        /// <summary>
        /// Would the spool fill the drawer if it were let go where it is held (or from where it is now): big
        /// enough, upright, its middle over the drawer, and no key lying across under it?
        /// </summary>
        public bool SpoolWouldFill()
        {
            if (spool.Removed || spool.Frozen || spool.Scale < FillMin) return false;
            Vector3 centre = spool.Center;
            if (centre.z <= EdgeZ || centre.z >= DoorZ || Mathf.Abs(centre.x) >= HalfWidth) return false;
            if (Vector3.Angle(spool.Rotation * Vector3.up, Vector3.up) > 5f) return false;
            return !KeyInTheWay();
        }

        // Does the key lie where the spool would come down: across the drawer, or standing in it? (A key that
        // lies flat on the socks is not in the way: the spool stands on it until the level takes it out.)
        bool KeyInTheWay()
        {
            if (key.Removed || key.Held || keyhole.Seated || keyhole.Seating) return false;
            float radius = ToyFactory.ThreadSpoolRadius * spool.Scale, seatZ = SeatZ(spool.Scale);
            float bottom = spool.Center.y - 0.5f * spool.Scale;
            Collider[] colliders = key.Colliders;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled) continue;
                Bounds bounds = collider.bounds;
                if (bounds.max.y < PitFloor + 0.6f || bounds.min.y > bottom + 0.05f) continue;
                float dx = Mathf.Max(0f, Mathf.Max(bounds.min.x, -bounds.max.x));
                float dz = Mathf.Max(0f, Mathf.Max(bounds.min.z - seatZ, seatZ - bounds.max.z));
                if (dx * dx + dz * dz < radius * radius) return true;
            }
            return false;
        }

        // The drawer takes a spool that is big enough: from where it was let go (or from where it stands, once
        // the key is out from under it) to the middle of the drawer, against the door wall, and then it falls.
        // So it never comes down a long step short of the door, to one side, or with its rim on the chest.
        void GuideSpool(LevelContext ctx)
        {
            if (guiding)
            {
                // Picked up again on the way (a pick-up ends the drive), or gone.
                if (spool.Removed || spool.Held || !spool.Driven || spool.Mover == null)
                {
                    guiding = false;
                    return;
                }
                guideTick++;
                Vector3 next = Vector3.Lerp(guideFrom, guideTo, GadgetKit.Smooth((float)guideTick / guideTicks));
                // Nobody is pushed about by it: with the player in its way it falls from where it is.
                bool blocked = GadgetKit.WouldCrush(ctx.Game, spool.Transform, spool.Colliders, next, spool.Rotation);
                if (!blocked) spool.Mover.MoveTo(next, spool.Rotation);
                if (blocked || guideTick >= guideTicks)
                {
                    guiding = false;
                    spool.EndDrive(Vector3.zero);
                }
                return;
            }
            if (guided || spool.Held || spool.Driven || !SpoolWouldFill()) return;
            guided = true;
            Vector3 centre = spool.Center;
            Vector3 to = spool.Position + new Vector3(-centre.x, 0f, SeatZ(spool.Scale) - centre.z);
            float distance = Vector3.Distance(spool.Position, to);
            if (distance < 0.05f) return;
            if (spool.BeginDrive() == null) return;
            guiding = true;
            guideTick = 0;
            guideTicks = Mathf.Clamp(Mathf.RoundToInt(distance / GuideSpeed / Sim.Dt), 8, 30);
            guideFrom = spool.Position;
            guideTo = to;
        }

        /// <summary>How long a spool hangs by its rim before it slips: a tenth of a second.</summary>
        public const int HangTicks = 6;

        /// <summary>Is the spool slipping off what its rim rested on right now?</summary>
        public bool Slipping => slipping;

        // Nothing but the key holds a spool up whose middle is over the drawer. The spool cannot tip (it is a
        // pillar and a platform), so wherever its rim found a hold it hung there, level, with nothing under its
        // middle: on the chest's edge - and on the sill under the shut door, 0.015 deep, a hair over the key.
        // There it was something else to stand on at the door: hopped onto from the key, the key picked up
        // from it (the spool stayed where it hung) and let go at the keyhole, and the way in was a step down
        // from the spool - out, and the drawer was never filled (the third review; 42 of 100 tries hung).
        // Now it slips off what its rim rests on, the way it would have tipped, and falls: onto the key, if
        // the key lies under it (and then it goes when the key goes), or into the drawer.
        void LetHangingSpoolFall(LevelContext ctx)
        {
            if (slipping)
            {
                // Picked up on the way (a pick-up ends the drive), or gone.
                if (spool.Removed || spool.Held || !spool.Driven || spool.Mover == null)
                {
                    slipping = false;
                    return;
                }
                slipTick++;
                Vector3 next = Vector3.Lerp(slipFrom, slipTo, GadgetKit.Smooth((float)slipTick / slipTicks));
                // Nobody is pushed about by it: with the player in its way it stays, and tries again.
                bool blocked = GadgetKit.WouldCrush(ctx.Game, spool.Transform, spool.Colliders, next, spool.Rotation);
                if (!blocked) spool.Mover.MoveTo(next, spool.Rotation);
                if (blocked || slipTick >= slipTicks)
                {
                    slipping = false;
                    spool.EndDrive(Vector3.zero);
                }
                return;
            }
            if (guiding || spool.Removed || spool.Held || spool.Frozen || spool.Driven)
            {
                hangTicks = 0;
                return;
            }
            Vector3 centre = spool.Center;
            float radius = ToyFactory.ThreadSpoolRadius * spool.Scale, foot = centre.y - 0.5f * spool.Scale;
            // How far its rim reaches back over the chest, and how far into the line of the door wall.
            float onChest = EdgeZ - (centre.z - radius), inWall = centre.z + radius - DoorZ;
            bool hangs = centre.z > EdgeZ && centre.z < DoorZ && foot > PitFloor + 0.2f && (onChest > 0f || inWall > -0.03f) &&
                         spool.Velocity.sqrMagnitude < 0.04f && KeyDistance() > 0.04f;
            hangTicks = hangs ? hangTicks + 1 : 0;
            if (hangTicks < HangTicks) return;
            hangTicks = 0;
            // Big enough to fill the drawer, and nothing in its way: the drawer takes it (see GuideSpool).
            if (guided && SpoolWouldFill())
            {
                guided = false;
                return;
            }
            Vector3 step = onChest > 0f ? Vector3.forward * (onChest + 0.06f) : Vector3.back * (Mathf.Max(0f, inWall) + 0.08f);
            if (spool.BeginDrive() == null) return;
            slipping = true;
            slipTick = 0;
            slipTicks = Mathf.Clamp(Mathf.RoundToInt(step.magnitude / GuideSpeed / Sim.Dt), 3, 40);
            slipFrom = spool.Position;
            slipTo = slipFrom + step;
        }

        // Nobody lifts themselves by the spool they stand on. The engine refuses a pick-up of the toy underfoot,
        // but not in mid-air: jump, pick the spool up from under one's own feet, let go of it at the top of the
        // jump - from higher up, so it comes out bigger - and land on it. Measured: 0.85 to 9.9 in thirteen
        // jumps, a tower on the chest. So a spool that is picked up in mid-air gets no bigger than it was (or
        // than it started) until the player's feet are on something again; an ordinary pick-up is not touched.
        void HoldInTheAir(LevelContext ctx)
        {
            Player player = ctx.Game.Player;
            bool held = spool.Held;
            if (held && !spoolWasHeld && !player.Grounded)
            {
                airHold = true;
                spool.MaxScale = Mathf.Clamp(spool.Scale, SpoolStart, SpoolMax);
            }
            else if (airHold && (!held || player.Grounded))
            {
                // Let go in the air at the size it was held down to: said, once.
                if (!held && !spool.Removed && !player.Grounded && spool.Scale >= spool.MaxScale - 1e-3f && spool.MaxScale < SpoolMax) Say(ctx, AirLine, 6f);
                airHold = false;
                spool.MaxScale = SpoolMax;
            }
            spoolWasHeld = held;
        }

        // The paint is the gauge (the campaign's language since Level 2): red and blinking while the spool in
        // the hand would not fill the drawer from where it is held, green and steady while it would - and for
        // as long as it stands there.
        void TintPaint(LevelContext ctx)
        {
            FitState next = FitState.Idle;
            if (spool.Held)
            {
                // Judged while it is held out over the drawer, or is big enough to be meant for it.
                if (spool.Center.z > EdgeZ - 1f || spool.Scale >= FillMin) next = SpoolWouldFill() ? FitState.Good : FitState.TooSmall;
            }
            else if (guiding || SpoolWouldFill()) next = FitState.Good;
            if (next != spoolTint)
            {
                spoolTint = next;
                ShowPaint(next);
            }
            if (spoolTint != FitState.TooSmall || outlinePaint == null) return;
            Signal wrong = Level02ThimbleChasm.Wrong;
            Color emission = wrong.EmissionAt(wrong.GainAt(ctx.Time));
            Materials.SetEmission(outlinePaint, emission);
            Materials.SetEmission(tapePaint, emission);
            Materials.SetEmission(edgePaint, emission);
        }

        void ShowPaint(FitState state)
        {
            if (outlinePaint == null || tapePaint == null || edgePaint == null) return;
            if (state == FitState.Idle)
            {
                outlinePaint.sharedMaterial = linePaint;
                tapePaint.sharedMaterial = linePaint;
                edgePaint.sharedMaterial = edgeIdle;
                return;
            }
            Signal signal = state == FitState.Good ? Palette.Go : Level02ThimbleChasm.Wrong;
            Material lit = Materials.Gadget(signal);
            outlinePaint.sharedMaterial = lit;
            tapePaint.sharedMaterial = lit;
            edgePaint.sharedMaterial = lit;
            Materials.SetEmission(outlinePaint, signal.Emission);
            Materials.SetEmission(tapePaint, signal.Emission);
            Materials.SetEmission(edgePaint, signal.Emission);
        }

        /// <summary>
        /// Is the key out of the player's reach or sight: a crumb wherever it lies, in the slot under the
        /// door, or among the socks where it is small or the spool stands over it?
        /// </summary>
        public bool KeyLost()
        {
            if (key.Held || key.Frozen || keyhole.Seated || keyhole.Seating) return false;
            if (key.Scale < KeyCrumb) return true;
            Vector3 centre = key.Center;
            if (centre.z > DoorZ) return true;
            bool amongSocks = centre.y < -1f && centre.z > EdgeZ - 0.5f;
            return amongSocks && (key.Scale < KeyHandy || SpoolInDrawer());
        }

        // The key at pocket size, flat, its blade toward the board (as it is painted on the shelf): how the
        // port puts it there.
        static void Pocket(Prop prop)
        {
            prop.SetScale(ShelfScale);
            prop.SetPose(prop.Position, Quaternion.Euler(0f, 180f, 0f));
        }

        static readonly Collider[] SpotHits = new Collider[8];

        // Where a key that comes back is put: the middle of the little shelf - or, with something standing
        // there (the spool; the shelf is a place to put things), on the chest in front of it. A port whose
        // mouth stayed blocked would keep the key out of sight for as long as that thing stood there.
        Vector3 KeySpot(Game game)
        {
            Vector3 middle = ShelfSpot + Vector3.up * 0.05f;
            // (The port's own measure of "in the way".)
            float radius = key.BaseRadius * ShelfScale * 0.9f;
            for (int i = 0; i < KeySpots.Length; i++)
            {
                Vector3 spot = middle + KeySpots[i];
                // Where the port will put it: its own height above whatever floor is under the spot.
                Vector3 centre = spot;
                if (game.PhysicsScene.Raycast(spot, Vector3.down, out RaycastHit hit, 5f, Layers.DefaultMask, QueryTriggerInteraction.Ignore))
                    centre.y = Mathf.Max(centre.y, hit.point.y + radius + 0.1f);
                int count = game.PhysicsScene.OverlapSphere(centre, radius, SpotHits, Layers.PropMask | Layers.PlayerMask, QueryTriggerInteraction.Ignore);
                bool free = true;
                for (int h = 0; h < count && free; h++)
                    if (PropRef.Of(SpotHits[h]) != key) free = false;
                if (free) return spot;
            }
            return middle;
        }

        // Relative to the middle of the shelf: there, and on the chest in front of it (a step and more apart,
        // so that whoever or whatever stands by the shelf is in the way of one only).
        static readonly Vector3[] KeySpots =
        {
            Vector3.zero, new Vector3(0f, 0f, 2.2f), new Vector3(1.8f, 0f, 2.2f), new Vector3(-1.2f, 0f, 3f), new Vector3(3.2f, 0f, 1.6f),
        };

        // Would the key, back as the bridge, lie through the spool?
        bool BridgeBlocked()
        {
            if (spool.Held) return false;
            Vector3 centre = spool.Center;
            float radius = ToyFactory.ThreadSpoolRadius * spool.Scale, half = 0.5f * spool.Scale;
            if (centre.y - half > 0.4f || centre.y + half < -0.05f) return false;
            float reachX = Mathf.Abs(centre.x) - radius, nearZ = centre.z - radius, farZ = centre.z + radius;
            return reachX < 3f && farZ > 4.8f && nearZ < DoorZ + 1.1f;
        }

        // The spool is back where it stood - and if the key lay there by then, it stands in the key. (Measured:
        // a pocket key stayed where it lay, inside the spool and out of sight until the spool was picked up
        // again; a key of eight was pressed half a unit down into the chest's top.) The key goes on top of
        // the spool instead, in plain view.
        void KeyOutOfTheSpool()
        {
            if (key.Removed || key.Held || key.Driven || key.Frozen || keyhole.Seating || keyhole.Seated) return;
            Vector3 middle = spool.Center;
            float radius = ToyFactory.ThreadSpoolRadius * spool.Scale, top = middle.y + 0.5f * spool.Scale;
            // The point of the key that is deepest in the spool: the nearest to its middle.
            Vector3 nearest = GadgetKit.ClosestPoint(key, middle);
            float dx = nearest.x - middle.x, dz = nearest.z - middle.z, inside = radius - 0.03f;
            if (dx * dx + dz * dz > inside * inside || nearest.y > top - 0.01f) return;
            GadgetKit.VerticalExtent(key, out float lowest, out _);
            key.SetPose(key.Position + Vector3.up * (top + 0.05f - lowest), key.Rotation);
        }

        // The spool has been let go. What stopped it?
        void NoteSpoolDrop(float scale)
        {
            spoolRestTicks = 0;
            dropClamped = scale >= SpoolMax - 0.01f;
            Vector3 centre = spool.Center;
            float radius = ToyFactory.ThreadSpoolRadius * scale, bottom = centre.y - 0.5f * scale;
            dropAtSide = Mathf.Abs(centre.x) + radius > HalfWidth - 0.15f;
            dropOnFloor = bottom < 0.12f && bottom > -0.2f && centre.z - radius < EdgeZ;
            // Only a spool that has grown is a try at the drawer; one that was just put down is not.
            spoolPending = scale >= 2.5f;
        }

        // Does the spool stand on the key, or against it?
        bool SpoolTouchesKey() => KeyDistance() < 0.15f;

        // How far the spool is from the key where they are nearest (far, if the key is in the hand or in the lock).
        float KeyDistance()
        {
            if (key.Held || key.Removed || keyhole.Seated) return 1000f;
            // Nearest pair of points of the two toys, by going back and forth a few times.
            Vector3 onSpool = spool.Center, onKey = GadgetKit.ClosestPoint(key, onSpool);
            for (int i = 0; i < 3; i++)
            {
                onSpool = GadgetKit.ClosestPoint(spool, onKey);
                onKey = GadgetKit.ClosestPoint(key, onSpool);
            }
            return Vector3.Distance(onKey, onSpool);
        }

        // The key was too big where it was let go. Looking as it does in the hand, does it fit from a comfortable
        // step nearer the door (then that is all there is to say), or only picked up from farther off?
        string TooBig() => keyRatio > 0f && keyRatio <= KeyEasyRatio ? KeyCloserLine : TooBigLine;

        // Does the view pass the keyhole, near enough to be meant for it?
        static bool LooksAtTheLock(Player player)
        {
            Vector3 to = Keyhole - player.Eye, forward = player.Forward;
            float along = Vector3.Dot(to, forward);
            return along > 0f && (to - forward * along).magnitude < 1.3f;
        }

        // Every tick, after the gadgets: the lock's lamps and the key in the rising door, and the one line
        // about what has just been tried.
        void Watch(LevelContext ctx)
        {
            Game game = ctx.Game;
            Player player = game.Player;
            PerspectiveGrabber grabber = game.Grabber;

            WatchLock(ctx);
            HoldInTheAir(ctx);
            GuideSpool(ctx);
            LetHangingSpoolFall(ctx);
            TintPaint(ctx);
            // Nobody is sent back from above a drawer that is filled: there the way across is the spool itself,
            // whatever anybody has stood on it to jump from (and "fill the drawer" would be the wrong thing to say).
            high.Enabled = !DrawerFilled;
            // A key on its way back waits while something stands where it is to appear: then it appears in front of it.
            if (shelfPort.IsPending(key)) shelfMouth.Mouth = KeySpot(game);

            // Standing on the key at the door: this is the picture the level is about.
            if (!lockedTold && !keyhole.Seated && player.Grounded && player.GroundProp == key && player.Position.z > DoorZ - 1.6f)
            {
                lockedTold = true;
                Say(ctx, LockedLine, 6f);
            }

            // A click at the toy under the player's own feet takes nothing.
            if (game.LastInput.GrabPressed && !wasHolding && !grabber.IsHolding && player.Grounded && player.GroundProp != null &&
                game.PhysicsScene.Raycast(player.Eye, player.Forward, out RaycastHit hit, PerspectiveGrabber.GrabRange, Layers.SolidMask, QueryTriggerInteraction.Ignore) &&
                PropRef.Of(hit.collider) == player.GroundProp)
                Say(ctx, StandingLine, 5f);
            wasHolding = grabber.IsHolding;

            // The key was let go and the lock has not taken it. Held toward the keyhole: its size was wrong (and
            // it was too far from the door for the lock to say so itself). Let go at the door, but beside the
            // keyhole: said too.
            if (keyDropTick >= 0 && ctx.Ticks - keyDropTick >= 2)
            {
                keyDropTick = -1;
                if (!key.Held && !key.Driven && !key.Frozen && !keyhole.Seating && !keyhole.Seated)
                {
                    FitState fit = keyhole.Fit(key.Scale);
                    if (keyAtLock && fit == FitState.TooBig) Say(ctx, TooBig(), 6f);
                    else if (fit == FitState.Good && Vector3.Distance(key.Center, Keyhole) < 2.2f && player.Position.z > DoorZ - 3f) Say(ctx, KeyholeLine, 5f);
                }
            }

            // A spool that has come to rest somewhere it does not fill the drawer, and stays there.
            if (!spoolPending || spool.Held || spool.Frozen) return;
            // In the drawer and too small: the leash says why when it takes it back.
            if (SpoolInDrawer() && spool.Scale < FillMin) return;
            spoolRestTicks = spool.Velocity.sqrMagnitude < 0.04f ? spoolRestTicks + 1 : 0;
            if (spoolRestTicks < 30) return;
            spoolPending = false;
            if (SpoolInDrawer())
            {
                // Big enough. Against the door wall, or a short step from it: nothing to say.
                if (DoorGap() > GapMax) Say(ctx, SpoolGapLine, 6f);
                return;
            }
            float radius = ToyFactory.ThreadSpoolRadius * spool.Scale, z = spool.Center.z;
            if (SpoolTouchesKey()) Say(ctx, SpoolKeyLine, 6f);
            else if (dropAtSide) Say(ctx, SpoolSideLine, 6f);
            else if (dropClamped) Say(ctx, SpoolClampLine, 6f);
            else if (dropOnFloor) Say(ctx, SpoolFloorLine, 6f);
            // Let go in the air, and it came down with its rim on the chest and the rest of it over the drawer.
            else if (z - radius < EdgeZ && z + radius > EdgeZ + 0.5f) Say(ctx, SpoolEdgeLine, 6f);
        }

        const float SinkSeconds = 0.2f, SinkDepth = 0.1f;

        void WatchLock(LevelContext ctx)
        {
            if (keyhole.Seating && captureTick < 0) captureTick = ctx.Ticks;
            if (!keyhole.Seating && !keyhole.Seated) captureTick = -1;

            // Three tumblers behind the plate: amber while the door is locked, green one by one as the key turns.
            if (tumblers != null)
            {
                float since = captureTick < 0 ? -1f : (ctx.Ticks - captureTick) * Sim.Dt;
                for (int i = 0; i < tumblers.Length; i++)
                {
                    bool open = since >= 0.25f + 0.4f * (i + 1) / tumblers.Length - 1e-4f;
                    tumblers[i].Set(open ? Palette.Go : Palette.Amber);
                    tumblers[i].Pulse(ctx.Time);
                }
            }

            if (!keyhole.Seated || seatedTick < 0) return;
            // The lock swallows the key, the door goes up, and the key goes with it inside the door.
            float t = (ctx.Ticks - seatedTick) * Sim.Dt;
            if (t >= SinkSeconds) door.Open();
            door.PoseAt(door.Openness, out Vector3 at, out _);
            if (key.Driven && key.Mover != null)
                key.Mover.MoveTo(SeatPosition(SeatScale) + Vector3.forward * (SinkDepth * GadgetKit.Smooth(t / SinkSeconds)) + (at - DoorClosed),
                    Quaternion.Euler(0f, 0f, -180f));
        }

        // One line, and not the same one over and over.
        void Say(LevelContext ctx, string text, float seconds = 5f)
        {
            if (text == null) return;
            if (text == lastLine && ctx.Ticks - lastSaid < 360) return;
            lastLine = text;
            lastSaid = ctx.Ticks;
            ctx.Say(text, seconds);
        }

        // ---- What stops things -------------------------------------------------------------------------------

        static void Solid(GameObject root, string name, Vector3 min, Vector3 max)
        {
            var holder = new GameObject(name) { layer = Layers.Default };
            holder.transform.SetParent(root.transform, false);
            holder.transform.localPosition = (min + max) * 0.5f;
            holder.AddComponent<BoxCollider>().size = max - min;
        }

        // Everything that stops the player or a held toy, as boxes. What is drawn stays inside them (BuildLooks).
        void BuildSolids(LevelContext ctx)
        {
            var root = new GameObject("Level 12 Solids") { layer = Layers.Default };
            float w = HalfWidth, t = WallThickness, far = DoorZ + FacadeDepth;
            // The chest of drawers (its top is the landing) and the open drawer (the socks' tops are its floor).
            Solid(root, "Chest", new Vector3(ChestLeft, BaseY, BackZ - t), new Vector3(w, 0f, EdgeZ));
            Solid(root, "Socks", new Vector3(-w - t, BaseY, EdgeZ), new Vector3(w + t, PitFloor, DoorZ));
            // The big book on the right, the board behind, and the side that is open to the room (nothing is
            // drawn of what stops a toy there above the chest).
            Solid(root, "Atlas", new Vector3(w, BaseY, BackZ - t), new Vector3(w + t, CapY, far));
            Solid(root, "Open Side", new Vector3(-w - t, BaseY, BackZ - t), new Vector3(-w, CapY, far));
            Solid(root, "Back Board", new Vector3(-w - t, 0f, BackZ - t), new Vector3(w + t, CapY, BackZ));

            // The front of the dollhouse: sheer from the socks to the top (and on up to the sky cap, in front
            // of the roof), with the slot the key's tip lies in and the door's opening above it.
            float x = w + t, slot = SlotHalfWidth, half = DoorHalfWidth;
            Solid(root, "Front Left", new Vector3(-x, SlotTop, DoorZ), new Vector3(-half, CapY, far));
            Solid(root, "Front Right", new Vector3(half, SlotTop, DoorZ), new Vector3(x, CapY, far));
            Solid(root, "Front Left Foot", new Vector3(-x, BaseY, DoorZ), new Vector3(-slot, SlotTop, far));
            Solid(root, "Front Right Foot", new Vector3(slot, BaseY, DoorZ), new Vector3(x, SlotTop, far));
            Solid(root, "Under The Slot", new Vector3(-slot, BaseY, DoorZ), new Vector3(slot, 0f, far));
            Solid(root, "Slot Back", new Vector3(-slot, 0f, DoorZ + SlotDepth), new Vector3(slot, SlotTop, far));
            Solid(root, "Sill", new Vector3(-half, SlotTop, DoorZ), new Vector3(half, SillY, far));
            Solid(root, "Lintel", new Vector3(-half, DoorTop, DoorZ), new Vector3(half, CapY, far));
            // The hall behind the door.
            float end = TunnelEnd + t;
            Solid(root, "Hall Floor", new Vector3(-half - t, SillY - 1f, far), new Vector3(half + t, SillY, end));
            Solid(root, "Hall Left", new Vector3(-half - t, SillY, far), new Vector3(-half, DoorTop, end));
            Solid(root, "Hall Right", new Vector3(half, SillY, far), new Vector3(half + t, DoorTop, end));
            Solid(root, "Hall Ceiling", new Vector3(-half - t, DoorTop, far), new Vector3(half + t, DoorTop + 1f, end));
            Solid(root, "Hall End", new Vector3(-half, SillY, TunnelEnd), new Vector3(half, DoorTop, end));
            ctx.AddStatic(root, Vector3.zero);
        }

        // ---- What is seen ------------------------------------------------------------------------------------

        static Material Tones(Dip dip, string name, Color top, Color side, PatternSpec pattern = default, bool pit = false) =>
            Materials.Room(new RoomRecipe
            {
                Name = dip.Name + " " + name, Top = top, Side = side, Dado = pit ? dip.Deep : side, DadoY = pit ? -0.02f : RoomRecipe.NoDado,
                Pattern = pattern.Pattern == RoomPattern.None ? PatternSpec.None : pattern,
            });

        // Pages, cuffs, the fields of the door: paper with a breath of the room in it.
        static Material Paper(Dip dip) => Tones(dip, "Paper", Palette.Mix(dip.Light, Palette.Paper, 0.6f), Palette.Mix(dip.Light, Palette.Paper, 0.35f));

        static Material Boards(Dip dip) => Tones(dip, "Boards", dip.Light, dip.Mid, new PatternSpec(RoomPattern.Planks, 2.5f, 60f, 0.08f));

        void BuildLooks(LevelContext ctx, Dip dip)
        {
            float w = HalfWidth, t = WallThickness, far = DoorZ + FacadeDepth;
            Material chestTop = Tones(dip, "Chest", dip.Light, dip.Deep, new PatternSpec(RoomPattern.Planks, 2.4f, 14f, 0.06f));
            Material chestBody = Tones(dip, "Chest Body", dip.Mid, dip.Deep);
            Material boards = Boards(dip);
            Material cover = Tones(dip, "Atlas", dip.Mid, dip.Mid, new PatternSpec(RoomPattern.Quilt, 3f, 0.08f, 0f, 0.05f), true);
            Material pages = Paper(dip);

            var set = new Level12Set();
            // The chest of drawers: a top board with a lip over a plain body. Its front is flush (it is the
            // near wall of the drawer); it goes on to the left of what the player can walk on.
            set.Span(chestTop, new Vector3(ChestLeft, -0.3f, BackZ - t - 0.6f), new Vector3(w, 0f, EdgeZ));
            set.Span(chestBody, new Vector3(ChestLeft + 0.2f, BaseY, BackZ - t - 0.4f), new Vector3(w, -0.3f, EdgeZ));
            // The drawer: bottom board and left side (the right side is against the atlas).
            set.Span(chestBody, new Vector3(-w - 0.4f, BaseY, EdgeZ), new Vector3(w, BaseY + 0.1f, DoorZ));
            set.Span(chestBody, new Vector3(-w - 0.4f, BaseY + 0.1f, EdgeZ), new Vector3(-w, 0f, DoorZ));
            // The board behind the start.
            set.Span(boards, new Vector3(ChestLeft, 0f, BackZ - t), new Vector3(w, WallTop + 1f, BackZ));
            // The atlas on the right: a picture book standing on the shelf, its cover toward the chest.
            set.Span(cover, new Vector3(w, BaseY, BackZ - t - 0.6f), new Vector3(w + 0.14f, WallTop + 1.3f, DoorZ));
            set.Span(pages, new Vector3(w + 0.14f, BaseY + 0.1f, BackZ - t - 0.5f), new Vector3(w + t - 0.14f, WallTop + 1.2f, DoorZ));
            set.Span(cover, new Vector3(w + t - 0.14f, BaseY, BackZ - t - 0.6f), new Vector3(w + t, WallTop + 1.3f, DoorZ));
            set.Finish(ctx, "Chest And Walls");

            BuildSocks(ctx, dip);
            BuildDollhouse(ctx, dip);
        }

        static readonly float[] SockYaw = { 4f, -7f, 2f, 9f, -3f, 6f, -8f, 1f, 5f, -5f, 8f, -2f };
        static readonly float[] SockShiftX = { 0.1f, -0.15f, 0.2f, -0.05f, -0.2f, 0.12f, 0.05f, -0.1f, 0.18f, -0.18f, 0f, 0.14f };
        static readonly float[] SockShiftZ = { -0.2f, 0.15f, 0.1f, -0.1f, 0.2f, -0.15f, 0.05f, 0.18f, -0.05f, 0.12f, -0.18f, 0f };

        // Folded pairs of socks, laid flat in the drawer: their tops are the floor a toy lands on.
        void BuildSocks(LevelContext ctx, Dip dip)
        {
            Material striped = Tones(dip, "Sock Stripes", dip.Light, dip.Mid, new PatternSpec(RoomPattern.Stripes, 0.5f, 0f, 0f, 0.14f));
            Material dotted = Tones(dip, "Sock Dots", Palette.Mix(dip.Light, Palette.Paper, 0.5f), dip.Mid, new PatternSpec(RoomPattern.Dots, 0.55f, 0.13f, 0f, 0.1f));
            Material plain = Tones(dip, "Chest Body", dip.Mid, dip.Deep);
            Material cuff = Paper(dip);
            Material[] kinds = { striped, plain, dotted };

            var set = new Level12Set();
            const float thick = 0.4f, wide = 1.02f, body = 2.5f, band = 0.62f, bevel = 0.15f;
            float y = PitFloor - thick * 0.5f;
            int index = 0;
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 4; column++, index++)
                {
                    var middle = new Vector3(-4.5f + 3f * column + SockShiftX[index], y, 10f + 4f * row + SockShiftZ[index]);
                    Quaternion turn = Quaternion.Euler(0f, SockYaw[index] + (index % 2 == 0 ? 0f : 180f), 0f);
                    Material kind = kinds[(row + column * 2) % kinds.Length];
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 across = turn * Vector3.right * (side * (wide * 0.5f + 0.03f));
                        Vector3 along = turn * Vector3.forward;
                        set.Soft(kind, middle + across - along * (band * 0.5f), new Vector3(wide, thick, body), bevel, turn);
                        set.Soft(cuff, middle + across + along * (body * 0.5f), new Vector3(wide, thick, band), bevel, turn);
                    }
                }
            }
            set.Finish(ctx, "Socks");
        }

        // The dollhouse: its front wall is the door wall, drawn piece for piece as the boxes that stop
        // things (so the slot and the doorway are real), with the hall behind the door, a body round the
        // hall and a roof above the sky cap that nothing ever reaches. The sun stands a little beyond it:
        // it is drawn without a shadow, or the far end of the drawer would lie in it.
        void BuildDollhouse(LevelContext ctx, Dip dip)
        {
            float w = HalfWidth, t = WallThickness, far = DoorZ + FacadeDepth, end = TunnelEnd + t;
            float slot = SlotHalfWidth, half = DoorHalfWidth;
            const float left = -9f, right = 8f, back = 27f;
            Material front = Tones(dip, "Dollhouse", dip.Light, dip.Mid, new PatternSpec(RoomPattern.Planks, 1.25f, 60f, 0.05f, 0.03f), true);
            Material hall = Boards(dip);
            // Shingles: a quiet checker of two tones that are close together.
            Material roof = Tones(dip, "Roof", dip.Mid, Palette.Mix(dip.Mid, dip.Deep, 0.4f), new PatternSpec(RoomPattern.Tiles, 1.1f, 0.07f));
            Material trim = Materials.Room(RoomSurface.Trim, dip);

            var set = new Level12Set();
            set.Span(front, new Vector3(left, SlotTop, DoorZ), new Vector3(-half, WallTop, far));
            set.Span(front, new Vector3(half, SlotTop, DoorZ), new Vector3(right, WallTop, far));
            set.Span(front, new Vector3(left, BaseY, DoorZ), new Vector3(-slot, SlotTop, far));
            set.Span(front, new Vector3(slot, BaseY, DoorZ), new Vector3(right, SlotTop, far));
            set.Span(front, new Vector3(-slot, BaseY, DoorZ), new Vector3(slot, 0f, far));
            set.Span(front, new Vector3(-slot, 0f, DoorZ + SlotDepth), new Vector3(slot, SlotTop, far));
            set.Span(front, new Vector3(-half, SlotTop, DoorZ), new Vector3(half, SillY, far));
            set.Span(front, new Vector3(-half, DoorTop, DoorZ), new Vector3(half, WallTop, far));
            // The hall.
            set.Span(hall, new Vector3(-half - t, SillY - 1f, far), new Vector3(half + t, SillY, end));
            set.Span(hall, new Vector3(-half - t, SillY, far), new Vector3(-half, DoorTop, end));
            set.Span(hall, new Vector3(half, SillY, far), new Vector3(half + t, DoorTop, end));
            set.Span(hall, new Vector3(-half - t, DoorTop, far), new Vector3(half + t, DoorTop + 1f, end));
            set.Span(hall, new Vector3(-half, SillY, TunnelEnd), new Vector3(half, DoorTop, end));
            // The rest of the house, round the hall.
            set.Span(front, new Vector3(left, BaseY, far), new Vector3(-half - t, WallTop, back));
            set.Span(front, new Vector3(half + t, BaseY, far), new Vector3(right, WallTop, back));
            set.Span(front, new Vector3(-half - t, DoorTop + 1f, far), new Vector3(half + t, WallTop, back));
            set.Span(front, new Vector3(-half - t, BaseY, far), new Vector3(half + t, SillY - 1f, back));
            set.Span(front, new Vector3(-half - t, SillY - 1f, end), new Vector3(half + t, DoorTop + 1f, back));
            // The frame round the door: paint, flat on the wall (a frame that stood out would be a ledge).
            const float frame = 0.22f, proud = 0.012f;
            set.Span(trim, new Vector3(-half - frame, SillY, DoorZ - proud), new Vector3(-half, DoorTop + frame, DoorZ));
            set.Span(trim, new Vector3(half, SillY, DoorZ - proud), new Vector3(half + frame, DoorTop + frame, DoorZ));
            set.Span(trim, new Vector3(-half, DoorTop, DoorZ - proud), new Vector3(half, DoorTop + frame, DoorZ));
            // The roof: two slopes from the eaves to a ridge along the house.
            const float rise = 3.4f, eave = 0.7f;
            float ridge = (DoorZ + back) * 0.5f, run = ridge - (DoorZ - eave), span = right - left + 2f * eave;
            Mesh slope = MeshKit.Cached(MeshKit.Key("Level12/Roof", span, rise, run), () => MeshKit.Wedge(span, rise, run));
            float middle = (left + right) * 0.5f;
            set.Shape(roof, slope, new Vector3(middle, WallTop + rise * 0.5f, ridge - run * 0.5f), Quaternion.identity);
            set.Shape(roof, slope, new Vector3(middle, WallTop + rise * 0.5f, ridge + run * 0.5f), Quaternion.Euler(0f, 180f, 0f));
            set.Finish(ctx, "Dollhouse", castShadows: false);
        }

        // The front door: a white slab with two fields and a steel lock plate - keyhole, and three tumbler
        // lamps over it. Nothing on it stands out by more than the door stands back, so that the raised
        // door is out of sight in the wall.
        GameObject BuildDoor(Dip dip)
        {
            var panel = new GameObject("Front Door");
            var size = new Vector3(DoorHalfWidth * 2f, DoorTop - SillY, DoorThickness);
            // What stops things is the slab and the hair in front of it, out to the wall's own face: the sill
            // under the door is not a ledge, not even one 0.015 deep (see LetHangingSpoolFall).
            var solid = panel.AddComponent<BoxCollider>();
            solid.size = size + Vector3.forward * DoorRecess;
            solid.center = Vector3.back * (DoorRecess * 0.5f);
            Transform at = panel.transform;
            float face = -DoorThickness * 0.5f;
            // Wood, like the little shelf and nobody's blocks: the wall it stands in is in the shade, where paper
            // on lilac is grey on grey, and the door is what the whole level is about.
            GadgetKit.Visual(at, "Slab", GadgetKit.BoxMesh(size), Materials.Toy(ToyRecipe.PlainProp, Palette.Birch));
            // The keyhole is at Keyhole.y; the plate reaches from under it to over the lamps, and the door's two
            // fields lie over and under the plate.
            float hole = Keyhole.y - DoorClosed.y, half = size.y * 0.5f;
            float plateTop = hole + 0.43f, plateBottom = hole - 0.31f;
            Material field = Materials.Toy(ToyRecipe.PlainProp, Palette.Kraft);
            float upper = half - 0.15f - (plateTop + 0.1f), lower = (plateBottom - 0.1f) - (-half + 0.13f);
            GadgetKit.Visual(at, "Upper Field", GadgetKit.BoxMesh(new Vector3(1.4f, upper, 0.004f)), field, new Vector3(0f, half - 0.15f - upper * 0.5f, face - 0.002f))
                .shadowCastingMode = ShadowCastingMode.Off;
            GadgetKit.Visual(at, "Lower Field", GadgetKit.BoxMesh(new Vector3(1.4f, lower, 0.004f)), field, new Vector3(0f, -half + 0.13f + lower * 0.5f, face - 0.002f))
                .shadowCastingMode = ShadowCastingMode.Off;

            GadgetKit.Visual(at, "Lock Plate", GadgetKit.BoxMesh(new Vector3(0.5f, 0.74f, 0.005f)), GadgetKit.Metal, new Vector3(0f, hole + 0.06f, face - 0.0025f))
                .shadowCastingMode = ShadowCastingMode.Off;
            var flat = Quaternion.Euler(90f, 0f, 0f);
            Material ink = GadgetKit.Body;
            GadgetKit.Visual(at, "Keyhole", GadgetKit.DiscMesh(0.075f, 0.003f), ink, new Vector3(0f, hole + 0.03f, face - 0.0065f), flat).shadowCastingMode = ShadowCastingMode.Off;
            GadgetKit.Visual(at, "Keyhole Slot", GadgetKit.BoxMesh(new Vector3(0.07f, 0.16f, 0.003f)), ink, new Vector3(0f, hole - 0.08f, face - 0.0065f))
                .shadowCastingMode = ShadowCastingMode.Off;
            tumblers = new SignalLamp[3];
            Mesh lamp = GadgetKit.DiscMesh(0.05f, 0.003f);
            for (int i = 0; i < tumblers.Length; i++)
            {
                tumblers[i] = GadgetKit.Lamp(at, lamp, Palette.Amber, new Vector3(-0.14f + 0.14f * i, hole + 0.29f, face - 0.0065f), flat);
                tumblers[i].Renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            return panel;
        }

        // The little shelf on the board behind the start: a board on two brackets, with a rack of pegs above.
        void BuildShelf(LevelContext ctx, Dip dip)
        {
            var root = new GameObject("Little Shelf") { layer = Layers.Default };
            Solid(root, "Board", new Vector3(ShelfMinX, ShelfTop - ShelfThickness, BackZ), new Vector3(ShelfMaxX, ShelfTop, ShelfFront));
            Solid(root, "Bracket Left", new Vector3(ShelfMinX + 0.25f, ShelfTop - 0.75f, BackZ), new Vector3(ShelfMinX + 0.43f, ShelfTop - ShelfThickness, ShelfFront - 0.35f));
            Solid(root, "Bracket Right", new Vector3(ShelfMaxX - 0.43f, ShelfTop - 0.75f, BackZ), new Vector3(ShelfMaxX - 0.25f, ShelfTop - ShelfThickness, ShelfFront - 0.35f));
            ctx.AddStatic(root, Vector3.zero);

            Material wood = Materials.Toy(ToyRecipe.PlainProp, Palette.Birch);
            Material kraft = Materials.Toy(ToyRecipe.PlainProp, Palette.Kraft);
            var set = new Level12Set();
            set.Span(wood, new Vector3(ShelfMinX, ShelfTop - ShelfThickness, BackZ), new Vector3(ShelfMaxX, ShelfTop, ShelfFront));
            set.Span(kraft, new Vector3(ShelfMinX + 0.25f, ShelfTop - 0.75f, BackZ), new Vector3(ShelfMinX + 0.43f, ShelfTop - ShelfThickness, ShelfFront - 0.35f));
            set.Span(kraft, new Vector3(ShelfMaxX - 0.43f, ShelfTop - 0.75f, BackZ), new Vector3(ShelfMaxX - 0.25f, ShelfTop - ShelfThickness, ShelfFront - 0.35f));
            // The rack: a strip on the board with three pegs, at the height of the eye.
            set.Span(kraft, new Vector3(ShelfMinX + 0.2f, 2.15f, BackZ), new Vector3(ShelfMaxX - 0.2f, 2.45f, BackZ + 0.04f));
            var peg = Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < 3; i++)
                set.Disc(wood, new Vector3(ShelfMinX + 0.6f + 0.6f * i, 2.3f, BackZ + 0.1f), 0.045f, 0.14f, peg);
            set.Finish(ctx, "Little Shelf Looks");
        }

        const float PadRadius = 0.62f;

        // Paint (no colliders). The campaign's size language: the spool's side at the size that fills the
        // drawer, dashed in its colour on the wall it stops against; the key at pocket size on the little
        // shelf. Where to stand is a pad with shoe prints in the room's own tones.
        void BuildPaint(LevelContext ctx, Dip dip)
        {
            var set = new Level12Set();
            Vector3 wall = Vector3.back;

            // The spool, seen from the side: 1.1 wide for 1 tall, with its two flanges.
            Color orange = ToyCatalog.Get(ToyId.ThreadSpool).ColorIn(dip);
            Material fill = Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Mid, orange, 0.22f)));
            Material line = Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Light, orange, 0.9f)));
            float halfWide = ToyFactory.ThreadSpoolRadius * OutlineScale, halfTall = 0.5f * OutlineScale, flange = 0.09f * OutlineScale;
            float x0 = -halfWide, x1 = halfWide, y0 = OutlineCentre.y - halfTall, y1 = OutlineCentre.y + halfTall, z = DoorZ;
            set.Box(fill, new Vector3(0f, OutlineCentre.y, z - 0.004f), new Vector3(halfWide * 2f, halfTall * 2f, 0.004f));
            const float stroke = 0.22f, dash = 0.95f, gap = 0.55f, proud = 0.008f;
            // The dashes are the gauge (as in Level 2): a renderer of their own, whose paint the level changes.
            var dashes = new Level12Set();
            dashes.Dashes(line, new Vector3(x0, y0, z), new Vector3(x1, y0, z), wall, stroke, dash, gap, proud);
            dashes.Dashes(line, new Vector3(x0, y1, z), new Vector3(x1, y1, z), wall, stroke, dash, gap, proud);
            dashes.Dashes(line, new Vector3(x0, y0, z), new Vector3(x0, y1, z), wall, stroke, dash, gap, proud);
            dashes.Dashes(line, new Vector3(x1, y0, z), new Vector3(x1, y1, z), wall, stroke, dash, gap, proud);
            dashes.Dashes(line, new Vector3(x0, y0 + flange, z), new Vector3(x1, y0 + flange, z), wall, 0.1f, 0.5f, 0.5f, proud);
            dashes.Dashes(line, new Vector3(x0, y1 - flange, z), new Vector3(x1, y1 - flange, z), wall, 0.1f, 0.5f, 0.5f, proud);
            outlinePaint = dashes.Finish(ctx, "Outline", castShadows: false).GetComponentInChildren<MeshRenderer>();
            // A dashed line in the same paint along the atlas, a little over head height: it leads the eye to
            // the outline, and it is the part of the gauge that a spool held big does not cover.
            var tape = new Level12Set();
            // (A little finer than the outline's own strokes: its near end is a few steps from the eye.)
            tape.Dashes(line, new Vector3(HalfWidth, TapeY, TapeFrom), new Vector3(HalfWidth, TapeY, DoorZ - 0.4f), Vector3.left, 0.16f, dash, gap, proud);
            tapePaint = tape.Finish(ctx, "Tape", castShadows: false).GetComponentInChildren<MeshRenderer>();
            linePaint = line;

            // The key at pocket size on the shelf, its blade toward the wall.
            Color brass = ToyCatalog.Get(ToyId.Key).ColorIn(dip);
            Material keyFill = Materials.Toy(ToyRecipe.PlainProp, Palette.Kraft);
            Material keyLine = Materials.Room(RoomRecipe.Solid(Palette.Mix(Palette.Birch, brass, 0.95f)));
            float s = ShelfScale;
            Vector3 at = ShelfSpot + Vector3.up * 0.003f;
            // (The key's own axes: its blade points along +Z; here that is toward the board, -Z.)
            Vector3 P(float kx, float kz) => at + new Vector3(-kx * s, 0f, -kz * s);
            set.Span(keyFill, P(0.35f, -0.4f) - Vector3.up * 0.002f, P(-0.35f, -1f) + Vector3.up * 0.002f);
            set.Span(keyFill, P(0.1f, 1f) - Vector3.up * 0.002f, P(-0.1f, -0.4f) + Vector3.up * 0.002f);
            const float fine = 0.02f, bit = 0.045f, space = 0.035f;
            Vector3 up = Vector3.up;
            set.Dashes(keyLine, P(-0.35f, -1f), P(0.35f, -1f), up, fine, bit, space, 0.004f, 0f, 0f, 0.003f);
            set.Dashes(keyLine, P(-0.35f, -1f), P(-0.35f, -0.4f), up, fine, bit, space, 0.004f, 0f, 0f, 0.003f);
            set.Dashes(keyLine, P(0.35f, -1f), P(0.35f, -0.4f), up, fine, bit, space, 0.004f, 0f, 0f, 0.003f);
            set.Dashes(keyLine, P(-0.1f, -0.4f), P(-0.1f, 1f), up, fine, bit, space, 0.004f, 0f, 0f, 0.003f);
            set.Dashes(keyLine, P(0.1f, -0.4f), P(0.1f, 1f), up, fine, bit, space, 0.004f, 0f, 0f, 0.003f);
            set.Finish(ctx, "Paint", castShadows: false);
            // The edge of the chest over the drawer: a dashed line across, as on a platform. It shows what the
            // outline shows, at the foot of the view.
            Material paper = Materials.Room(RoomRecipe.Solid(Palette.Paper));
            var edge = new Level12Set();
            edge.Dashes(paper, new Vector3(-HalfWidth + 0.3f, 0f, EdgeZ - 0.4f), new Vector3(HalfWidth - 0.3f, 0f, EdgeZ - 0.4f), Vector3.up, 0.14f, 0.8f, 0.5f, 0.006f, 0f, 0f, 0.006f);
            edgePaint = edge.Finish(ctx, "Edge Line", castShadows: false).GetComponentInChildren<MeshRenderer>();
            edgeIdle = paper;

            Material pad = Materials.Room(RoomRecipe.Solid(dip.Deep));
            Material print = paper;
            StandMark(ctx, "Stand Mark Shelf", ShelfStand, 180f, pad, print);
            StandMark(ctx, "Stand Mark Spool", SpoolMark, 0f, pad, print);
            StandMark(ctx, "Stand Mark Door", ThrowMark, 0f, pad, print);
        }

        // Looks only: a renderer for the pad and one for each shoe, toes a little apart.
        static void StandMark(LevelContext ctx, string name, Vector3 at, float yaw, Material pad, Material print)
        {
            var mark = new GameObject(name);
            ctx.AddStatic(mark, at + Vector3.up * 0.012f, Quaternion.Euler(0f, yaw, 0f));
            Mesh disc = MeshKit.Cached(MeshKit.Key("Level12/Pad", PadRadius), () => MeshKit.Cylinder(PadRadius, 0.004f, 40));
            GadgetKit.Visual(mark.transform, "Pad", disc, pad).shadowCastingMode = ShadowCastingMode.Off;
            Mesh sole = MeshKit.Cached(MeshKit.Key("Level12/Sole"), () =>
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

        // Nobody's toys: two blocks in the corner behind the start, and a few more on the part of the chest that
        // is out of reach on the open side. Nothing stands between the start and the door.
        // The two in the corner stand square against the walls and each other. (They stood askew, with a third
        // on top: behind them, beside them and behind the one on top were three pockets that nobody could see
        // into from anywhere on the chest - and a small key can slip into a pocket.)
        void BuildDressing(LevelContext ctx)
        {
            var set = new Level12Set();
            Block(ctx, set, new Vector3(1.3f, 1.3f, 1.3f), new Vector3(HalfWidth - 0.65f, 0f, BackZ + 0.65f), 0f);
            Block(ctx, set, new Vector3(1.8f, 0.9f, 0.9f), new Vector3(HalfWidth - 1.3f - 0.9f, 0f, BackZ + 0.45f), 0f);
            Block(ctx, set, new Vector3(0.8f, 0.8f, 0.8f), new Vector3(-8.3f, 0f, -2.1f), -17f);
            Block(ctx, set, new Vector3(1.5f, 1.5f, 1.5f), new Vector3(-8.4f, 0f, -4.6f), 12f);
            Block(ctx, set, new Vector3(1f, 1f, 2f), new Vector3(-8.2f, 0f, 0.4f), -9f);
            Block(ctx, set, new Vector3(1.2f, 1.2f, 1.2f), new Vector3(-8.5f, 0f, 5.6f), 28f);
            Block(ctx, set, new Vector3(0.8f, 0.8f, 0.8f), new Vector3(-8.4f, 1.2f, 5.5f), -11f);
            Block(ctx, set, new Vector3(0.9f, 0.9f, 1.8f), new Vector3(-8.1f, 0f, 3f), 6f);
            set.Finish(ctx, "Clutter");
        }

        static void Block(LevelContext ctx, Level12Set set, Vector3 size, Vector3 foot, float yaw)
        {
            GameObject piece = ToyFactory.WoodenBlock(size, grabbable: false);
            ctx.AddStatic(piece, foot + Vector3.up * (size.y * 0.5f), Quaternion.Euler(0f, yaw, 0f));
            set.Absorb(piece);
        }

        // ---- The intended solution -------------------------------------------------------------------------

        public override IEnumerator Solve(Bot bot)
        {
            // The key is under the crosshair at the start. Picked up from here it is no longer the bridge.
            yield return bot.Grab(key);
            // Put down on the little shelf, at arm's length, it is a trinket.
            yield return PutKeyOnShelf(bot);
            // The spool, from a step away; held up against the outline over the door it fills the drawer.
            yield return FillDrawer(bot, SpoolStand, ThrowStand, OutlineCentre);
            // The key again, across the spool, and up to the lock.
            yield return bot.WalkTo(ShelfStand, 0.15f);
            yield return bot.Grab(key);
            yield return Cross(bot);
            yield return bot.DropAt(Keyhole);
            yield return Leave(bot);
        }

        /// <summary>With the key in hand: to the little shelf, and let go of it there.</summary>
        public IEnumerator PutKeyOnShelf(Bot bot)
        {
            yield return bot.WalkTo(ShelfStand, 0.15f);
            yield return bot.DropAt(ShelfSpot);
            yield return bot.Until(() => !key.Held && key.Velocity.sqrMagnitude < 0.01f, 3f);
        }

        /// <summary>Picks the spool up from one place, holds it toward a point from another and lets go; waits until it stands in the drawer.</summary>
        public IEnumerator FillDrawer(Bot bot, Vector3 pickUpFrom, Vector3 letGoFrom, Vector3 aim)
        {
            yield return bot.WalkTo(pickUpFrom, 0.1f);
            yield return bot.Grab(spool);
            yield return bot.WalkTo(letGoFrom, 0.15f);
            yield return bot.DropAt(aim);
            yield return bot.Until(() => SpoolFills, 6f);
        }

        /// <summary>
        /// From the chest over the gap onto the spool and up to the door, a short step from it (or as near
        /// it as the spool goes): from a tall spool the keyhole is well under the eye, and the key held
        /// toward it from further back is too big.
        /// </summary>
        public IEnumerator Cross(Bot bot)
        {
            yield return bot.WalkTo(new Vector3(0f, 0f, EdgeZ - 2f), 0.3f);
            yield return bot.WalkTo(new Vector3(0f, 0f, EdgeZ - 0.35f), 0.2f);
            yield return bot.Jump();
            float reach = spool.Center.z + ToyFactory.ThreadSpoolRadius * spool.Scale * 0.98f - 0.35f;
            yield return bot.WalkTo(new Vector3(0f, 0f, Mathf.Min(DoorZ - 0.8f, reach)), 0.15f, 8f);
            yield return bot.Until(() => bot.Player.Grounded, 2f);
        }

        /// <summary>Once the key is in the lock: wait for the door, hop over the sill and walk out.</summary>
        public IEnumerator Leave(Bot bot)
        {
            Game game = bot.Game;
            yield return bot.Until(() => door.IsOpen, 4f);
            yield return bot.WalkTo(new Vector3(0f, 0f, DoorZ - 0.6f), 0.2f);
            yield return bot.Jump();
            yield return bot.WalkTo(new Vector3(ExitCentre.x, SillY, ExitCentre.z), 0.4f, 6f);
            yield return bot.Until(() => game.LevelCompleted, 3f);
        }
    }
}
