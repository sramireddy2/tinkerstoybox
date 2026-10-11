using System.Collections;
using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;
using UnityEngine.Rendering;
using Volume = Toybox.Engine.Volume;

namespace Toybox.Levels
{
    /// <summary>
    /// Level 14, "The Infinite Hallway" (LEVELS.md): the toy resizes you.
    ///
    /// A dollhouse hallway papered with little doors, at night, and one loose door frame standing in the
    /// middle of it. A door is always the right size for whoever walks through it - so walking through
    /// makes the player the size of the door. There is one room; being a third of your size in it is being
    /// in a hall three times as big.
    ///
    /// Three gates, and no size passes two of them in a row: the mouse-hole in the dividing wall (0.8 high:
    /// a player of 0.45 or less), the ledge at the end of the second hall (its face 3.4 high: a jump and the
    /// roll of the capsule's foot over the edge get a player of 2.5 or more up it) and the way out at the
    /// back of the ledge (3.4 high: less than 2). Small, big, medium: the door is put down three times and walked
    /// through three times. Any pass that does not go far enough can be made again from the size it gave -
    /// that is the recursion.
    ///
    /// In the hand the door is as big as it looks, like every toy: from across the hall and let go at the
    /// feet it is a door for a mouse; a mouse who picks it up from a step away and lets go of it looking up
    /// gets a door for a giant; from up on the ledge it looks small again. The level says so wherever a try
    /// shows otherwise (the lines below), and a door the player cannot get to is either in sight - it can be
    /// picked up from anywhere it is seen - or called back from a floor plate.
    /// (Where this differs from LEVELS.md, and why: tools/out/notes/level14-build.md.)
    /// </summary>
    [Level(14, "infinite-hallway", "The Infinite Hallway", Phase = 3)]
    public sealed class Level14InfiniteHallway : LevelDefinition
    {
        // ---- Layout (LEVELS.md, Level 14; floor y = 0, travel +Z) --------------------------------------------
        public const float HalfWidth = 5f, WallThickness = 1f, WallTop = 11f;
        /// <summary>The dollhouse stands on its own base board: the room's floor lies this far under the hall's.</summary>
        public const float FloorThickness = 0.2f;
        /// <summary>Hall A is z 0..WallZ; the dividing wall is one unit thick; hall B ends at the ledge's sheer face.</summary>
        public const float WallZ = 14f, HallBZ = WallZ + WallThickness, TerraceZ = 25f, EndZ = 31f, TunnelZ = EndZ + WallThickness, TunnelEnd = 35f;
        /// <summary>
        /// The height of the ledge's face: what a jump has to clear. LEVELS.md has 3.0 for a gate at 2.5 (a
        /// jump reaches 1.25 times the player's size); measured, the round foot of the capsule also rolls over
        /// the edge and a 3.0 face is climbed from 2.2 up - 1.36 times the size. 3.4 puts the gate back at 2.5.
        /// </summary>
        public const float LedgeFace = 3.4f;
        /// <summary>
        /// A kerb runs along the ledge's edge. Whoever is small enough for the way out (2 or less) does not
        /// walk over it - a kerb is walked over from a tenth of the player's size down - so nobody who has
        /// just come out of the last door, still walking, goes over the edge with the door left up there
        /// (review: that was the likeliest way to lose two minutes). A giant steps over it without noticing.
        /// </summary>
        public const float KerbHeight = 0.25f, KerbDepth = 0.25f;
        /// <summary>The floor of the ledge, behind its kerb.</summary>
        public const float TerraceTop = LedgeFace - KerbHeight;
        public const float HoleHalfWidth = 0.35f, HoleHeight = 0.8f;
        public const float ExitHalfWidth = 0.8f, ExitHeight = 3.4f;

        // ---- The door ---------------------------------------------------------------------------------------
        public static readonly Vector3 DoorStart = new Vector3(0f, 0f, 8f);
        public const float DoorStartScale = 1f, DoorMinScale = 0.1f, DoorMaxScale = 3.2f;
        /// <summary>The player is never smaller or bigger than this, whatever the door.</summary>
        public const float MinPlayerScale = 0.3f, MaxPlayerScale = 3.2f;

        // ---- The gates ----------------------------------------------------------------------------------------
        /// <summary>The mouse-hole lets a player of this size or less through (1.7 x 0.45 = 0.77 under a lintel at 0.8).</summary>
        public const float MouseScale = 0.45f;
        /// <summary>The ledge is jumped from this size up (a jump reaches 1.25 x the player's size).</summary>
        public const float LedgeScale = 2.5f;
        /// <summary>The way out lets a player of this size or less through (1.7 x 2 = 3.4).</summary>
        public const float ExitScale = 2f;
        /// <summary>
        /// Measured: the mouse-hole stops a player of 0.48; the ledge is climbed from 2.48 up; the way out stops
        /// a player of 2.02 and lets one of 2.01 through at an angle only - whoever is over 2 is told.
        /// (The ledge with one jump. Sprinting at it and mashing jump, now and then somebody from 2.2 up
        /// scrambles over the kerb - still too tall for the way out. Under 2.16 the round of the foot does not
        /// reach the kerb's edge, so nobody who fits the way out gets up: tested. Nor does a jump INTO a door
        /// that makes them bigger help: the gadget carries a jump over as the new size's own jump
        /// (PortalDoorway.Carried) - it used to carry the hop of a mouse into a door of 1.5 over as a leap of
        /// seven units, onto the ledge at a size that fits the way out: the level in two trips.)
        /// </summary>
        public const float MouseLimit = 0.47f, ExitLimit = 2f, LedgeLimit = 2.48f;

        // ---- Where things happen ------------------------------------------------------------------------------
        public static readonly Vector3 Spawn = new Vector3(0f, 0f, 2f);
        /// <summary>The view at the start: the crosshair on the middle of the door, the mouse-hole seen through it.</summary>
        public const float SpawnPitch = -2.9f;
        /// <summary>
        /// Shoe prints: where the solver stands for each of the three passes. The first are a few steps from
        /// the mouse-hole (LEVELS has them a step from the start, ten units from it): in the hand the door's
        /// opening is closed, and a mouse who has to carry it ten units to a hole it hides walks blind. From
        /// here it is three, and the hole was in view all the way to the prints.
        /// The second are 2.8 from the dividing wall, not a step from it (review): carried edge-on, as the
        /// level's own tip has it, the door is 0.8 of its size long either side of its middle, and let go
        /// looking straight up from nearer the wall than 2.6 it met the wall behind the player at 1.3 to 1.8.
        /// The third stand clear of the kerb.
        /// </summary>
        public static readonly Vector3 StandA = new Vector3(0f, 0f, 11.2f), StandB = new Vector3(0f, 0f, 17.8f), StandT = new Vector3(0f, TerraceTop, 26.75f);
        /// <summary>Dashes: the door's foot at the size each pass is after, where the solver's door comes to stand.</summary>
        public static readonly Vector3 FootA = new Vector3(0f, 0f, 12.31f), FootB = new Vector3(0f, 0f, 18.6f), FootT = new Vector3(0f, TerraceTop, 28.65f);
        public const float FootScaleA = 0.28f, FootScaleB = 3.2f, FootScaleT = 1.35f;
        /// <summary>
        /// Where the solver looks when it lets go: down at the floor ahead for the small door and for the
        /// medium one (LEVELS has 60 degrees down for both; picked up from the start, six units off instead
        /// of five, 45 gives the 0.28 the text is after), almost straight up for the big one (80: the third
        /// hint says "straight up", and the big door comes to stand where LEVELS has it, at z 18.5).
        /// </summary>
        public const float SmallPitch = -45f, BigPitch = 80f, MediumPitch = -60f;
        public static readonly Vector3 PadA = new Vector3(-3.5f, 0f, 1.5f), PadB = new Vector3(-3.5f, 0f, 16.5f);
        public static readonly Vector3 ExitCentre = new Vector3(0f, TerraceTop + 1.5f, 33.8f);
        public static readonly Vector3 ExitSize = new Vector3(1.6f, 3f, 2f);

        /// <summary>
        /// Apparent size (scale / distance) below which a door held straight up by a player of this size
        /// meets the sky over the hallway before it is big enough for the ledge: its top is at eye height
        /// plus (1 + 1.25 k) times the distance. 0.35 for a mouse-sized player (measured: 0.35 gives 2.56,
        /// 0.3 gives 2.29).
        /// </summary>
        public static float LooksTooSmall(float playerScale) =>
            1.03f * LedgeScale / (WallTop - 1.25f * LedgeScale - Player.BaseEyeHeight * playerScale);

        /// <summary>
        /// Apparent size above which the foot of a door held straight up by a player of this size is still
        /// on the floor when it is big enough for the ledge - it never gets there. 0.91 for a mouse-sized
        /// player (measured: 0.9 gives 3.2, 1.0 gives 1.85); no limit from a size of 2 up.
        /// </summary>
        public static float LooksTooBig(float playerScale)
        {
            float room = 1.25f * LedgeScale - Player.BaseEyeHeight * playerScale;
            return room > 0.05f ? 0.97f * LedgeScale / room : float.MaxValue;
        }

        // ---- What the level says ------------------------------------------------------------------------------
        /// <summary>Said on a pick-up from too far away when the door has to grow. The words are Level 1's, on purpose.</summary>
        public const string FarLine = "It only ever gets as big as it looks. Pick it up from closer.";
        /// <summary>Said on a pick-up from too near when the door has to grow: its foot will not leave the floor.</summary>
        public const string NearLine = "From this close it looks too big to rise off the floor. Pick it up from a step back.";
        /// <summary>Said after a trip through the door that made the player smaller, but not small enough for the mouse-hole.</summary>
        public const string StillBigLine = "Still too big for the mouse-hole. Pick the door up from farther off and go through once more.";
        /// <summary>
        /// Said after a trip through the door in the second hall that made the player bigger, but not big
        /// enough for the ledge. "Straight" up (review): a door that looks big keeps its foot on the floor at
        /// 60 degrees and leaves it at 80 - the one jittered solution in sixty that needed a fourth trip.
        /// </summary>
        public const string StillShortLine = "Not tall enough to jump the ledge yet. Pick the door up again and let go of it looking straight up.";
        /// <summary>Said after a trip through the door on the ledge that left the player too tall for the way out.</summary>
        public const string StillTallLine = "Too tall for the way out. Pick the door up and let go of it at your feet.";
        /// <summary>Said when the far side of the door had no room for the new size and the player came back out of the near side.</summary>
        public const string TurnedLine = "No room for you beyond the door, so you came back out the way you went in.";
        /// <summary>Said when neither side of the door has room for the new size and nothing happens.</summary>
        public const string BlockedLine = "No room to be that size here. Stand the door where there is more space.";
        /// <summary>Said when the door has come to stand where the player cannot get into its opening: against a wall, in the mouse-hole.</summary>
        public const string StuckLine = "You cannot walk through it there. Stand the door where there is room on both sides.";
        /// <summary>Said when the door stands where the player cannot follow but can still see it: beyond the mouse-hole, up on the ledge.</summary>
        public const string ReachLine = "You cannot get to the door there. Pick it up again and stand it where you can walk.";
        /// <summary>Said when the door stands where the player cannot follow and cannot see it.</summary>
        public const string LostLine = "The door is out of reach. Stand on a round floor plate to call it back.";
        /// <summary>
        /// Said when a door that looked the right size in the hand was let go in the second hall with its foot
        /// off the floor and still is not big enough: then a wall stopped it - the one behind the player if
        /// it is carried edge-on, a side wall if they stand in a corner (the floor plate is in one).
        /// </summary>
        public const string CrampedLine = "It met a wall and stopped growing. Let go of it out in the open, away from the walls.";
        /// <summary>Said when a giant up on the ledge lets go of the door and it comes to stand down in the hall: they were looking that way.</summary>
        public const string EdgeLine = "The door went over the edge. Turn to the way out, then let go of it at your feet.";
        /// <summary>Said when the door has left the hallway altogether (it cannot by any means the player has).</summary>
        public const string BackLine = "The door left the hallway. It is back where it started.";
        /// <summary>
        /// Said once, the first time a mouse-sized player picks the door up in the first hall from so near that
        /// it hides the way to the mouse-hole: in the hand its opening is closed (LEVELS, portal rendering).
        /// </summary>
        public const string TurnLine = "Cannot see past the door? Turn it edge-on while you carry it.";
        /// <summary>
        /// Said once, to whoever walks through the door while it is still their own size and has not made a
        /// trip yet (review: the first thing a new player does is walk through the door as it stands, and
        /// nothing happens). It says why, not what to do about it.
        /// </summary>
        public const string SameLine = "It is just a door while it is your size.";
        /// <summary>Said after a trip through the door in the first hall that left the player bigger than the mouse-hole lets through.</summary>
        public const string GrownLine = "Too big for the mouse-hole now. Make the door small again and go back through it.";

        /// <summary>
        /// Apparent size (scale / distance) from which a door in the hand hides the way ahead: it is
        /// 2 atan(1.25 k) tall on a screen that shows 70 degrees - half of it at 0.25.
        /// </summary>
        public const float HidesTheWay = 0.25f;

        /// <summary>The halls of the level, in the order they are walked.</summary>
        public enum Place
        {
            HallA,
            Hole,
            HallB,
            Terrace,
            Tunnel,
        }

        Prop door;
        PortalDoorway portal;
        RecallPad padA, padB;
        Exit exit;
        string lastLine;
        int lastSaid;
        int lostTicks, outTicks;
        bool lostSaid, reachSaid, turnSaid, sameSaid;
        /// <summary>Which side of the door the player was on a tick ago, while it stands and is their own size (0: not known).</summary>
        int sameSide;
        /// <summary>Apparent size of the door at its last pick-up, and the size of the player who picked it up.</summary>
        float ratio, pickedUpAt;
        /// <summary>The door has been in the hand since it last came to stand; something was said about how big it looked in it.</summary>
        bool carried, warned;
        /// <summary>Where the door's foot was when it was last let go, and where the player stood then.</summary>
        float letGoY;
        Place letGoFrom;
        int trips;

        public Prop Door => door;
        public PortalDoorway Portal => portal;
        public RecallPad RecallA => padA;
        public RecallPad RecallB => padB;
        public Exit Exit => exit;
        /// <summary>Apparent size (scale / distance) of the door at its last pick-up.</summary>
        public float Ratio => ratio;
        /// <summary>How often the player has walked into the door at another size than their own: through it, back out of it, or refused.</summary>
        public int Trips => trips;

        public override string Blurb => "This door fits everyone. That's the problem, and the answer.";

        public override string[] Hints => new[]
        {
            "Walk through the door and you come out the size the door is. Look through it first.",
            "A door the size of the mouse-hole makes you the size of a mouse. If one trip is not enough, pick the door up again and go through once more.",
            "Pick the door up from afar, let go at your feet, walk in. Pick it up from close, carry it through the mouse-hole, let go looking straight up, walk in. Jump the ledge, face the way out, redo step one.",
        };

        public override string Environment => "night-light";
        public override float GroundY => -FloorThickness;
        public override float KillY => -30f;

        /// <summary>Which hall a point is in (by its place along the hallway; the ledge and the tunnel begin where their floors do).</summary>
        public static Place PlaceOf(Vector3 point)
        {
            if (point.z < WallZ) return Place.HallA;
            if (point.z < HallBZ) return Place.Hole;
            if (point.z < TerraceZ) return Place.HallB;
            return point.z < EndZ + 0.5f * WallThickness ? Place.Terrace : Place.Tunnel;
        }

        public override void Build(LevelContext ctx)
        {
            door = null;
            portal = null;
            padA = null;
            padB = null;
            exit = null;
            lastLine = null;
            lastSaid = -100000;
            lostTicks = 0;
            outTicks = 0;
            lostSaid = false;
            reachSaid = false;
            turnSaid = false;
            sameSaid = false;
            sameSide = 0;
            ratio = 0f;
            pickedUpAt = 1f;
            carried = false;
            warned = false;
            letGoY = 0f;
            letGoFrom = Place.HallA;
            trips = 0;

            BuildRoom(ctx);
            BuildPaper(ctx);
            BuildMarks(ctx);
            BuildDressing(ctx);

            door = AddDoor(ctx);
            portal = new PortalDoorway(ctx, new PortalDoorwayOptions
            {
                Name = "Door", Prop = door, MinPlayerScale = MinPlayerScale, MaxPlayerScale = MaxPlayerScale, Cooldown = 0.5f, SettleSpeed = 30f,
            });
            portal.Crossed += (from, to) => OnCrossed(ctx, from, to);
            portal.Blocked += () =>
            {
                trips++;
                Say(ctx, BlockedLine, 6f);
            };
            portal.OnSettled += () => OnSettled(ctx);

            // The door can be put where the player cannot follow (through the mouse-hole while they are too
            // big for it, up on the ledge while they are too small): a plate in each hall calls it back.
            padA = new RecallPad(ctx, new RecallPadOptions { Name = "Hall A", Position = PadA, Radius = 0.6f, Prop = door, HoldSeconds = 0.5f });
            padB = new RecallPad(ctx, new RecallPadOptions { Name = "Hall B", Position = PadB, Radius = 0.6f, Prop = door, HoldSeconds = 0.5f });
            padA.Recalled += () => OnRecalled(ctx, new Vector3(0f, 0f, WallZ * 0.5f));
            padB.Recalled += () => OnRecalled(ctx, new Vector3(0f, 0f, (HallBZ + TerraceZ) * 0.5f));

            ctx.Game.Events.PropGrabbed += e =>
            {
                if (e.Prop != door) return;
                OnPickedUp(ctx, e.OldScale, e.GrabDistance);
            };
            ctx.Game.Events.PropDropped += e =>
            {
                if (e.Prop != door) return;
                letGoY = door.Position.y;
                letGoFrom = PlaceOf(ctx.Game.Player.Position);
            };
            ctx.OnUpdate(dt => Watch(ctx));

            exit = ctx.AddExit(ExitCentre, ExitSize);
            ctx.SetSpawn(Spawn, 0f, SpawnPitch);
            // Nothing in the hallway kills. Should the player ever leave it all the same, they come back to
            // the hall they had got to.
            ctx.AddCheckpoint(Volume.Box(8f, 2f, 6f), new Vector3(0f, 1f, 20f), new Vector3(0f, 0f, 17f), 0f, "Hall B");
            ctx.AddCheckpoint(Volume.Box(8f, 2f, 4f), new Vector3(0f, TerraceTop + 1f, 28f), new Vector3(0f, TerraceTop, 26.5f), 0f, "Ledge");
        }

        // The catalog's doorway, with its opening closed for the grab ray: the frame is two posts and a lintel,
        // and a click at the middle of a door would go straight through it. The pane is a collider only - like
        // the frame it never touches the player - and lies inside the frame, so the frame is what meets a wall.
        static Prop AddDoor(LevelContext ctx)
        {
            ToyDef def = ToyCatalog.Get(ToyId.Doorway);
            GameObject toy = def.Build(def.ColorIn(ctx.Dip));
            Vector2 opening = ToyFactory.DoorwayOpening;
            var pane = new GameObject("Pane");
            pane.transform.SetParent(toy.transform, false);
            pane.transform.localPosition = new Vector3(0f, opening.y * 0.5f, 0f);
            BoxCollider box = pane.AddComponent<BoxCollider>();
            box.size = new Vector3(opening.x, opening.y, ToyFactory.DoorwaySize.z * 0.9f);
            box.excludeLayers = Layers.PlayerMask;

            PropOptions options = def.Options(DoorStartScale);
            options.MinScale = DoorMinScale;
            options.MaxScale = DoorMaxScale;
            options.AllowPitch = false;
            // In the hand it is square to the view, wherever on it the click landed and from whatever side:
            // let go, it stands facing whoever let go of it, and "walk in" is straight ahead.
            options.SquareOnGrab = true;
            // Facing the start.
            return ctx.AddProp(toy, DoorStart, Quaternion.Euler(0f, 180f, 0f), options);
        }

        // ---- What the level says ----------------------------------------------------------------------------

        // One line, and not the same one over and over.
        void Say(LevelContext ctx, string text, float seconds = 5f)
        {
            if (text == lastLine && ctx.Ticks - lastSaid < 360) return;
            lastLine = text;
            lastSaid = ctx.Ticks;
            ctx.Say(text, seconds);
        }

        void OnPickedUp(LevelContext ctx, float scale, float distance)
        {
            Player player = ctx.Game.Player;
            float size = player.Scale;
            ratio = scale / Mathf.Max(0.01f, distance);
            pickedUpAt = size;
            carried = true;
            warned = false;
            lostTicks = 0;
            lostSaid = false;
            reachSaid = false;
            Place place = PlaceOf(player.Position);
            if (place >= Place.Terrace || size >= LedgeScale) return;
            if (place == Place.HallA)
            {
                // On its way to the mouse-hole. Whether it looks big enough for what comes after is said
                // where that shows (OnSettled); here, only that a door this near hides the hole.
                if (size <= MouseLimit && ratio >= HidesTheWay && !turnSaid)
                {
                    turnSaid = true;
                    Say(ctx, TurnLine, 4f);
                }
                return;
            }
            // In the second hall the next thing the door has to do is grow, and how big it looks in the hand
            // decides how big it can get: it only ever gets as big as it looks, and one that looks too big
            // keeps its foot on the floor.
            warned = SayHowItLooks(ctx);
        }

        // The line for a door that looks too small or too big in the hand to be made big enough for the ledge.
        bool SayHowItLooks(LevelContext ctx)
        {
            if (ratio < LooksTooSmall(pickedUpAt)) Say(ctx, FarLine, 6f);
            else if (ratio > LooksTooBig(pickedUpAt)) Say(ctx, NearLine, 6f);
            else return false;
            return true;
        }

        // The plate has put the door down in front of the player. A giant with a wall on every side of them
        // gets it where the plate is - in the wall (review). Then it stands in the middle of that hall instead,
        // facing them.
        void OnRecalled(LevelContext ctx, Vector3 middle)
        {
            lostTicks = 0;
            lostSaid = false;
            reachSaid = false;
            carried = false;
            Game game = ctx.Game;
            float scale = door.Scale;
            Vector3 half = door.LocalHalfExtents * (scale * 0.98f);
            Vector3 lift = Vector3.up * (0.05f * scale);
            if (game.PhysicsScene.OverlapBox(door.Center + lift, half, Overlaps, door.Rotation, Layers.DefaultMask, QueryTriggerInteraction.Ignore) == 0) return;
            Vector3 toPlayer = game.Player.Position - middle;
            toPlayer.y = 0f;
            // Square to the hall: across it there is room for the biggest door, along it the walls are nearer.
            Quaternion facing = Quaternion.LookRotation(toPlayer.z >= 0f ? Vector3.forward : Vector3.back, Vector3.up);
            door.SetPose(middle + Vector3.up * (0.02f * scale), facing);
        }

        static readonly Collider[] Overlaps = new Collider[4];

        // The player has come out of the door at another size. If that size does not get them past the gate
        // of the hall they are in, say so: the next trip starts from here.
        void OnCrossed(LevelContext ctx, float from, float to)
        {
            trips++;
            if (portal.LastResult == PortalResult.TurnedBack)
            {
                Say(ctx, TurnedLine, 6f);
                return;
            }
            switch (PlaceOf(ctx.Game.Player.Position))
            {
                case Place.HallA:
                    if (to < from && to > MouseLimit) Say(ctx, StillBigLine, 6f);
                    else if (to > from && to > MouseLimit) Say(ctx, GrownLine, 6f);
                    break;
                case Place.HallB:
                    if (to > from && to < LedgeLimit) Say(ctx, StillShortLine, 6f);
                    break;
                case Place.Terrace:
                    if (to < from && to > ExitLimit) Say(ctx, StillTallLine, 6f);
                    break;
            }
        }

        // The door has come to stand. Can the player get into its opening at the size they are?
        void OnSettled(LevelContext ctx)
        {
            Player player = ctx.Game.Player;
            bool fromTheHand = carried;
            carried = false;
            // Carried in from the first hall and let go in the second, where it grew - but not enough for
            // the ledge. If that was because of how big it looked when it was picked up, this is where it
            // shows, and where it is said.
            float size = player.Scale, target = portal.TargetScale;
            bool below = door.Position.y < TerraceTop - 0.2f;
            if (fromTheHand && !warned && size < LedgeLimit && target > size * 1.03f && target < LedgeLimit
                && PlaceOf(door.Position) == Place.HallB && below && PlaceOf(player.Position) != Place.HallA)
            {
                warned = SayHowItLooks(ctx);
                // It looked right, and its foot had left the floor when it was let go (it came down to stand):
                // then it was neither the floor nor the sky that stopped it. A wall did.
                if (!warned && letGoY - door.Position.y > 0.05f * door.Scale)
                {
                    warned = true;
                    Say(ctx, CrampedLine, 6f);
                }
            }
            // Let go from up on the ledge by somebody who still needs it up there, and it stands down in the hall.
            if (fromTheHand && letGoFrom == Place.Terrace && size > ExitLimit && PlaceOf(door.Position) == Place.HallB && below
                && PlaceOf(player.Position) == Place.Terrace)
            {
                Say(ctx, EdgeLine, 6f);
                return;
            }
            // A door of the player's own size is just a door.
            if (Mathf.Abs(portal.TargetScale - player.Scale) <= 0.03f * player.Scale) return;
            // On another floor, or in another hall: that is for the plates (see Watch).
            if (Unreachable(player) || Mathf.Abs(player.Position.y - door.Position.y) > 0.6f * player.Scale + 0.3f) return;
            if (!CanCross(ctx.Game, player)) Say(ctx, StuckLine, 6f);
        }

        /// <summary>
        /// Is there a spot in the door's plane, within its opening, where the player fits as they are? A
        /// trip through the door begins with the player's axis crossing that plane.
        /// </summary>
        public bool CanCross(Game game, Player player)
        {
            // A door is walked into feet first (the gadget's rule): not one whose threshold is above them.
            if (door.Position.y - player.Position.y > PortalDoorway.FootSlack * player.Scale + 0.02f) return false;
            float radius = player.Radius;
            float reach = ToyFactory.DoorwayOpening.x * 0.5f * door.Scale + radius;
            for (int i = -3; i <= 3; i++)
            {
                Vector3 feet = door.Position + door.Rotation * new Vector3(reach * i / 3f, 0f, 0f) + Vector3.up * (0.02f * player.Scale);
                if (GadgetKit.CapsuleFree(game, feet, radius, player.Height, door)) return true;
            }
            return false;
        }

        // Is the door where the player cannot walk to at the size they are?
        bool Unreachable(Player player)
        {
            Place mine = PlaceOf(player.Position), its = PlaceOf(door.Position);
            // A door that came to stand on the very edge of the ledge is up on the ledge.
            if (its == Place.HallB && door.Position.y > TerraceTop - 0.2f) its = Place.Terrace;
            if (mine == its) return false;
            float size = player.Scale;
            // In the mouse-hole, or on the other side of it.
            if (its == Place.Hole) return size > MouseLimit;
            if (mine == Place.Hole) return false;
            if ((mine == Place.HallA) != (its == Place.HallA)) return size > MouseLimit;
            // Up on the ledge, or in the way out, from the floor of the second hall.
            if (mine == Place.HallB) return size < LedgeLimit;
            // In the way out, from the ledge.
            if (its == Place.Tunnel) return size > ExitLimit;
            return false;
        }

        // Does a ray from the eye reach any of the door? (Then it can be picked up from here.)
        bool InView(Game game, Player player)
        {
            Vector3 eye = player.Eye;
            Vector2 half = new Vector2(ToyFactory.DoorwaySize.x, ToyFactory.DoorwaySize.y) * (0.5f * door.Scale);
            Vector3 centre = door.Center;
            Quaternion rotation = door.Rotation;
            for (int i = 0; i < 9; i++)
            {
                var offset = new Vector3((i % 3 - 1) * half.x * 0.85f, (i / 3 - 1) * half.y * 0.85f, 0f);
                Vector3 to = centre + rotation * offset - eye;
                float distance = to.magnitude;
                if (distance < 1e-3f) return true;
                if (game.PhysicsScene.Raycast(eye, to / distance, out RaycastHit hit, distance + 0.05f, Layers.SolidMask, QueryTriggerInteraction.Ignore)
                    && PropRef.Of(hit.collider) == door)
                    return true;
            }
            return false;
        }

        /// <summary>A second and a half: how long the door is out of reach and out of sight before the level says so.</summary>
        const int LostTicks = 90;

        // Every tick: where is the door?
        void Watch(LevelContext ctx)
        {
            if (door == null || door.Removed) return;
            Game game = ctx.Game;
            Player player = game.Player;

            // Out of the hallway altogether (no way the player has puts it there): back to where it started.
            Vector3 at = door.Position;
            bool outside = !door.Held && (Mathf.Abs(at.x) > HalfWidth + 0.5f || at.z < -0.5f || at.z > TunnelEnd + 0.5f || at.y < -0.5f || at.y > WallTop + 1f);
            outTicks = outside ? outTicks + 1 : 0;
            if (outTicks >= LostTicks)
            {
                outTicks = 0;
                door.Respawn();
                Say(ctx, BackLine, 6f);
                return;
            }

            WatchSameSize(ctx, player);

            // Where the player cannot follow. In their sight it can be picked up again from where they are;
            // out of it, the plates bring it back.
            bool away = !door.Held && portal.Settled && !outside && Unreachable(player);
            lostTicks = away ? lostTicks + 1 : 0;
            if (!away) reachSaid = false;
            if (lostTicks < LostTicks || lostSaid) return;
            if (!InView(game, player))
            {
                lostSaid = true;
                Say(ctx, LostLine, 6f);
            }
            else if (!reachSaid)
            {
                reachSaid = true;
                Say(ctx, ReachLine, 6f);
            }
        }

        // Through the door while it is the player's own size: nothing happens (the gadget's rule: it is just
        // a door). Until they have made a trip they do not know that rule, so the first time it is said.
        void WatchSameSize(LevelContext ctx, Player player)
        {
            if (sameSaid || trips > 0) return;
            if (door.Held || !portal.Settled || Mathf.Abs(portal.TargetScale - player.Scale) > 1e-3f * player.Scale)
            {
                sameSide = 0;
                return;
            }
            Vector3 local = Quaternion.Inverse(door.Rotation) * (player.Position - door.Position);
            int now = local.z >= 0f ? 1 : -1;
            int before = sameSide;
            sameSide = now;
            if (before == 0 || now == before) return;
            Vector2 opening = ToyFactory.DoorwayOpening * door.Scale;
            if (Mathf.Abs(local.x) > opening.x * 0.5f || local.y < -PortalDoorway.FootSlack * player.Scale || local.y > opening.y) return;
            sameSaid = true;
            Say(ctx, SameLine, 5f);
        }

        // ---- Geometry ---------------------------------------------------------------------------------------

        static void Solid(LevelContext ctx, string name, Vector3 min, Vector3 max)
        {
            var solid = new GameObject(name) { layer = Layers.Default };
            solid.AddComponent<BoxCollider>().size = max - min;
            ctx.AddStatic(solid, (min + max) * 0.5f);
        }

        // A box of the hallway: what stops the player and a held door, and what is drawn, are the same box.
        static void Wall(LevelContext ctx, Level01Set set, Material material, string name, Vector3 min, Vector3 max)
        {
            Solid(ctx, name, min, max);
            set.Box(material, (min + max) * 0.5f, max - min);
        }

        /// <summary>The wainscot: the lower part of every wall is darker up to this height above its floor.</summary>
        const float DadoHeight = 1.25f;

        void BuildRoom(LevelContext ctx)
        {
            float t = WallThickness, w = HalfWidth, o = w + t;
            float exitTop = TerraceTop + ExitHeight, e = ExitHalfWidth;
            Dip dip = ctx.Dip;
            var set = new Level01Set();

            // Floors are tiled (a tile is a unit: the size of the hall can be counted); walls are papered
            // above a dark wainscot.
            Material floor = Materials.Room(RoomSurface.LevelStatic, dip, new PatternSpec(RoomPattern.Tiles, 1f, 0.06f, 0f, 0.05f));
            var paper = new RoomRecipe { Name = "Level14 Wall", Top = dip.Light, Side = dip.Mid, Dado = dip.Deep, DadoY = DadoHeight };
            Material wall = Materials.Room(paper);
            Material wallHigh = Materials.Room(paper.With(r =>
            {
                r.Name = "Level14 Wall High";
                r.DadoY = TerraceTop + DadoHeight;
            }));

            Wall(ctx, set, floor, "Floor", new Vector3(-o, -FloorThickness, -t), new Vector3(o, 0f, TerraceZ));
            // The ledge: a sheer face and a square edge with a kerb along it. It runs on under the end wall.
            Wall(ctx, set, floor, "Ledge", new Vector3(-w, -FloorThickness, TerraceZ), new Vector3(w, TerraceTop, TunnelZ));
            Material kerb = Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Light, Palette.Paper, 0.35f)));
            Wall(ctx, set, kerb, "Kerb", new Vector3(-w, TerraceTop, TerraceZ), new Vector3(w, LedgeFace, TerraceZ + KerbDepth));

            Wall(ctx, set, wall, "Wall Back", new Vector3(-w, 0f, -t), new Vector3(w, WallTop, 0f));
            Wall(ctx, set, wall, "Wall -X", new Vector3(-o, 0f, -t), new Vector3(-w, WallTop, TerraceZ));
            Wall(ctx, set, wall, "Wall +X", new Vector3(w, 0f, -t), new Vector3(o, WallTop, TerraceZ));
            Wall(ctx, set, wallHigh, "Wall -X High", new Vector3(-o, -FloorThickness, TerraceZ), new Vector3(-w, WallTop, TunnelZ));
            Wall(ctx, set, wallHigh, "Wall +X High", new Vector3(w, -FloorThickness, TerraceZ), new Vector3(o, WallTop, TunnelZ));

            // The dividing wall, full height, with the mouse-hole through its foot.
            Wall(ctx, set, wall, "Divider -X", new Vector3(-w, 0f, WallZ), new Vector3(-HoleHalfWidth, WallTop, HallBZ));
            Wall(ctx, set, wall, "Divider +X", new Vector3(HoleHalfWidth, 0f, WallZ), new Vector3(w, WallTop, HallBZ));
            Wall(ctx, set, wall, "Divider Top", new Vector3(-HoleHalfWidth, HoleHeight, WallZ), new Vector3(HoleHalfWidth, WallTop, HallBZ));

            // The end wall with the way out, and the tunnel behind it.
            Wall(ctx, set, wallHigh, "End -X", new Vector3(-w, TerraceTop, EndZ), new Vector3(-e, WallTop, TunnelZ));
            Wall(ctx, set, wallHigh, "End +X", new Vector3(e, TerraceTop, EndZ), new Vector3(w, WallTop, TunnelZ));
            Wall(ctx, set, wallHigh, "End Top", new Vector3(-e, exitTop, EndZ), new Vector3(e, WallTop, TunnelZ));
            Wall(ctx, set, floor, "Tunnel Floor", new Vector3(-e - t, TerraceTop - t, TunnelZ), new Vector3(e + t, TerraceTop, TunnelEnd + t));
            Wall(ctx, set, wallHigh, "Tunnel -X", new Vector3(-e - t, TerraceTop, TunnelZ), new Vector3(-e, exitTop + t, TunnelEnd + t));
            Wall(ctx, set, wallHigh, "Tunnel +X", new Vector3(e, TerraceTop, TunnelZ), new Vector3(e + t, exitTop + t, TunnelEnd + t));
            Wall(ctx, set, wallHigh, "Tunnel Top", new Vector3(-e, exitTop, TunnelZ), new Vector3(e, exitTop + t, TunnelEnd + t));
            Wall(ctx, set, wallHigh, "Tunnel End", new Vector3(-e, TerraceTop, TunnelEnd), new Vector3(e, exitTop, TunnelEnd + t));

            // The night comes in from above: nothing is drawn up there, but nothing gets out either.
            SkyCap.Add(ctx, -o, o, -t, TunnelZ, WallTop);
            set.Finish(ctx, "Hallway");
        }

        // The wallpaper: rows of little doors above the wainscot, every other row half a door along. Looks
        // only, a finger proud of the wall.
        void BuildPaper(LevelContext ctx)
        {
            Dip dip = ctx.Dip;
            var set = new Level01Set();
            Material frame = Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Mid, dip.Light, 0.75f)));
            Material leaf = Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Mid, dip.Deep, 0.35f)));
            Material knob = Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Light, Palette.Birch, 0.6f)));
            Material trim = Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Light, Palette.Paper, 0.35f)));
            float w = HalfWidth, hole = HoleHalfWidth, e = ExitHalfWidth;

            // Side walls.
            for (int side = -1; side <= 1; side += 2)
            {
                var normal = new Vector3(-side, 0f, 0f);
                Doors(set, frame, leaf, knob, new Vector3(side * w, 0f, 0f), Vector3.forward, normal, WallZ, 0f, null);
                Doors(set, frame, leaf, knob, new Vector3(side * w, 0f, HallBZ), Vector3.forward, normal, TerraceZ - HallBZ, 0f, null);
                Doors(set, frame, leaf, knob, new Vector3(side * w, 0f, TerraceZ), Vector3.forward, normal, EndZ - TerraceZ, TerraceTop, null);
                Rails(set, trim, new Vector3(side * w, 0f, 0f), Vector3.forward, normal, WallZ, 0f);
                Rails(set, trim, new Vector3(side * w, 0f, HallBZ), Vector3.forward, normal, TerraceZ - HallBZ, 0f);
                Rails(set, trim, new Vector3(side * w, 0f, TerraceZ), Vector3.forward, normal, EndZ - TerraceZ, TerraceTop);
            }
            // The back wall, both faces of the dividing wall (clear of the mouse-hole and its sign), the end wall
            // (clear of the way out).
            Doors(set, frame, leaf, knob, new Vector3(-w, 0f, 0f), Vector3.right, Vector3.forward, 2f * w, 0f, null);
            Rails(set, trim, new Vector3(-w, 0f, 0f), Vector3.right, Vector3.forward, 2f * w, 0f);
            Doors(set, frame, leaf, knob, new Vector3(-w, 0f, WallZ), Vector3.right, Vector3.back, 2f * w, 0f, (x, y0, y1) => Mathf.Abs(x - w) < 1.3f && y0 < 3.2f);
            Doors(set, frame, leaf, knob, new Vector3(-w, 0f, HallBZ), Vector3.right, Vector3.forward, 2f * w, 0f, null);
            Doors(set, frame, leaf, knob, new Vector3(-w, 0f, EndZ), Vector3.right, Vector3.back, 2f * w, TerraceTop, (x, y0, y1) => Mathf.Abs(x - w) < e + 1f && y0 < TerraceTop + ExitHeight + 0.5f);
            foreach (float z in new[] { WallZ, HallBZ })
            {
                Vector3 normal = z == WallZ ? Vector3.back : Vector3.forward;
                Rails(set, trim, new Vector3(-w, 0f, z), Vector3.right, normal, w - hole - 0.12f, 0f);
                Rails(set, trim, new Vector3(hole + 0.12f, 0f, z), Vector3.right, normal, w - hole - 0.12f, 0f);
                // The mouse-hole is one of the little doors, and this one is real: a frame round the hole.
                Plate(set, trim, new Vector3(-hole - 0.05f, HoleHeight * 0.5f + 0.05f, z), normal, 0.1f, HoleHeight + 0.1f, 0.03f);
                Plate(set, trim, new Vector3(hole + 0.05f, HoleHeight * 0.5f + 0.05f, z), normal, 0.1f, HoleHeight + 0.1f, 0.03f);
                Plate(set, trim, new Vector3(0f, HoleHeight + 0.05f, z), normal, 2f * hole, 0.1f, 0.03f);
            }
            Rails(set, trim, new Vector3(-w, 0f, EndZ), Vector3.right, Vector3.back, w - e - 0.2f, TerraceTop);
            Rails(set, trim, new Vector3(e + 0.2f, 0f, EndZ), Vector3.right, Vector3.back, w - e - 0.2f, TerraceTop);
            // The way out has a frame of its own.
            float exitTop = TerraceTop + ExitHeight;
            Plate(set, trim, new Vector3(-e - 0.1f, TerraceTop + ExitHeight * 0.5f + 0.1f, EndZ), Vector3.back, 0.2f, ExitHeight + 0.2f, 0.04f);
            Plate(set, trim, new Vector3(e + 0.1f, TerraceTop + ExitHeight * 0.5f + 0.1f, EndZ), Vector3.back, 0.2f, ExitHeight + 0.2f, 0.04f);
            Plate(set, trim, new Vector3(0f, exitTop + 0.1f, EndZ), Vector3.back, 2f * e, 0.2f, 0.04f);

            // The way is signed where it cannot be seen: the four panes of the way out and arrows down to the
            // mouse-hole on the first wall, arrows up the face of the ledge.
            Material sign = Materials.Room(RoomRecipe.Solid(Palette.Paper));
            const float pane = 0.2f, mullion = 0.05f;
            for (int i = -1; i <= 1; i += 2)
                for (int j = -1; j <= 1; j += 2)
                    Plate(set, sign, new Vector3(i * (pane + mullion) * 0.5f, 2.55f + j * (pane + mullion) * 0.5f, WallZ), Vector3.back, pane, pane, 0.02f);
            foreach (float y in new[] { 1.3f, 1.65f, 2f })
                Chevron(set, sign, new Vector3(0f, y, WallZ), 0.42f, 0.075f, false);
            foreach (float y in new[] { 0.9f, 1.7f, 2.5f })
                Chevron(set, sign, new Vector3(0f, y, TerraceZ), 1.1f, 0.18f, true);
            // The edge of the ledge, picked out so that it reads from below and from above.
            Plate(set, trim, new Vector3(0f, LedgeFace - 0.09f, TerraceZ), Vector3.back, 2f * w, 0.18f, 0.03f);

            set.Finish(ctx, "Paper", castShadows: false);
        }

        const float DoorPitch = 1.4f, RowPitch = 2.1f, PaperDoorWidth = 0.7f, PaperDoorHeight = 1.4f;

        delegate bool Skip(float along, float bottom, float top);

        // Little doors on one wall face. origin: the foot of the face where it begins; floor: the height of
        // the floor in front of it.
        static void Doors(Level01Set set, Material frame, Material leaf, Material knob, Vector3 origin, Vector3 along, Vector3 normal, float length, float floor, Skip skip)
        {
            int columns = Mathf.Max(1, Mathf.FloorToInt((length - 0.5f) / DoorPitch));
            float margin = (length - (columns - 1) * DoorPitch) * 0.5f;
            int row = 0;
            for (float bottom = floor + DadoHeight + 0.45f; bottom + PaperDoorHeight < WallTop - 0.3f; bottom += RowPitch, row++)
            {
                bool shifted = row % 2 == 1;
                int count = shifted ? columns - 1 : columns;
                for (int i = 0; i < count; i++)
                {
                    float at = margin + i * DoorPitch + (shifted ? DoorPitch * 0.5f : 0f);
                    if (skip != null && skip(at, bottom, bottom + PaperDoorHeight)) continue;
                    Vector3 centre = origin + along * at + Vector3.up * (bottom + PaperDoorHeight * 0.5f);
                    Plate(set, frame, centre, normal, PaperDoorWidth, PaperDoorHeight, 0.018f);
                    Plate(set, leaf, centre - Vector3.up * 0.04f, normal, PaperDoorWidth - 0.16f, PaperDoorHeight - 0.16f, 0.026f);
                    Plate(set, knob, centre + along * (PaperDoorWidth * 0.5f - 0.18f) * Handed(along, normal) - Vector3.up * 0.1f, normal, 0.07f, 0.07f, 0.04f);
                }
            }
        }

        // Which way is "right" along a wall for someone who faces it (so every knob is on the door's right).
        static float Handed(Vector3 along, Vector3 normal) => Vector3.Dot(Vector3.Cross(Vector3.up, -normal), along) >= 0f ? 1f : -1f;

        // The skirting board and the rail on top of the wainscot.
        static void Rails(Level01Set set, Material trim, Vector3 origin, Vector3 along, Vector3 normal, float length, float floor)
        {
            Vector3 middle = origin + along * (length * 0.5f);
            Plate(set, trim, middle + Vector3.up * (floor + 0.09f), normal, length, 0.18f, 0.03f);
            Plate(set, trim, middle + Vector3.up * (floor + DadoHeight), normal, length, 0.09f, 0.04f);
        }

        // A flat piece lying on a wall face: `proud` is how far its front stands off the wall.
        static void Plate(Level01Set set, Material material, Vector3 onWall, Vector3 normal, float width, float height, float proud)
        {
            Quaternion rotation = Quaternion.LookRotation(normal, Vector3.up);
            set.Box(material, onWall + normal * (proud * 0.5f), new Vector3(width, height, proud), rotation);
        }

        // An arrowhead painted on a wall that faces -Z: two strokes meeting at the point.
        static void Chevron(Level01Set set, Material material, Vector3 onWall, float arm, float stroke, bool up)
        {
            const float proud = 0.02f, lean = 38f;
            float reach = arm * 0.5f * Mathf.Cos(lean * Mathf.Deg2Rad);
            float sign = up ? 1f : -1f;
            Vector3 centre = onWall + Vector3.back * (proud * 0.5f);
            set.Box(material, centre + Vector3.left * reach, new Vector3(arm, stroke, proud), Quaternion.Euler(0f, 0f, sign * lean));
            set.Box(material, centre + Vector3.right * reach, new Vector3(arm, stroke, proud), Quaternion.Euler(0f, 0f, -sign * lean));
        }

        // The size language of the campaign, three times over: shoe prints are where to stand - and as big as
        // whoever gets there is - and dashes in the door's colour are the door's foot at the size it has to
        // be next.
        void BuildMarks(LevelContext ctx)
        {
            Dip dip = ctx.Dip;
            Color hero = ToyCatalog.Get(ToyId.Doorway).ColorIn(dip);
            Material line = Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Light, hero, 0.9f)));
            Material pad = Materials.Room(RoomRecipe.Solid(dip.Deep));
            Material print = Materials.Room(RoomRecipe.Solid(Palette.Paper));
            var set = new Level01Set();
            Footprint(set, line, FootA, FootScaleA);
            Footprint(set, line, FootB, FootScaleB);
            Footprint(set, line, FootT, FootScaleT);
            set.Finish(ctx, "Dashes", castShadows: false);

            StandMark(ctx, "Stand Mark A", StandA, 1f, pad, print);
            StandMark(ctx, "Stand Mark B", StandB, MinPlayerScale, pad, print);
            StandMark(ctx, "Stand Mark T", StandT, MaxPlayerScale * 0.75f, pad, print);
        }

        // The outline of the door's foot (1.6 x 0.3 times its scale), dashed, flat on the floor.
        static void Footprint(Level01Set set, Material paint, Vector3 centre, float scale)
        {
            float halfX = ToyFactory.DoorwaySize.x * 0.5f * scale, halfZ = ToyFactory.DoorwaySize.z * 0.5f * scale;
            float stroke = 0.045f * scale, dash = 0.2f * scale, gap = 0.12f * scale;
            float y = centre.y + 0.004f + 0.004f * scale;
            // The two short ends are the posts: filled in. The long sides are dashed between them.
            float post = (ToyFactory.DoorwaySize.x - ToyFactory.DoorwayOpening.x) * 0.5f * scale;
            float inner = halfX - post;
            set.Box(paint, new Vector3(centre.x - halfX + post * 0.5f, y, centre.z), new Vector3(post, 0.004f, 2f * halfZ));
            set.Box(paint, new Vector3(centre.x + halfX - post * 0.5f, y, centre.z), new Vector3(post, 0.004f, 2f * halfZ));
            float edge = halfZ - stroke * 0.5f;
            Dashes(set, paint, new Vector3(centre.x - inner, y, centre.z - edge), new Vector3(centre.x + inner, y, centre.z - edge), stroke, dash, gap);
            Dashes(set, paint, new Vector3(centre.x - inner, y, centre.z + edge), new Vector3(centre.x + inner, y, centre.z + edge), stroke, dash, gap);
        }

        // Flat strips from one point to another on a floor: as many whole dashes as fit, the first and the
        // last on the ends.
        static void Dashes(Level01Set set, Material paint, Vector3 from, Vector3 to, float width, float dash, float gap)
        {
            Vector3 along = to - from;
            float length = along.magnitude;
            if (length < 1e-4f) return;
            along /= length;
            int count = Mathf.Max(1, Mathf.FloorToInt((length + gap) / (dash + gap)));
            float each = Mathf.Min(dash, length);
            float step = count > 1 ? (length - each) / (count - 1) : 0f;
            Quaternion rotation = Quaternion.LookRotation(along, Vector3.up);
            for (int i = 0; i < count; i++)
                set.Box(paint, from + along * (each * 0.5f + step * i), new Vector3(width, 0.004f, each), rotation);
        }

        // Looks only: a round pad and a pair of shoe prints on it, facing +Z, toes a little apart. `size` is
        // the size of the player the prints are made for.
        static void StandMark(LevelContext ctx, string name, Vector3 at, float size, Material pad, Material print)
        {
            var mark = new GameObject(name);
            ctx.AddStatic(mark, at + Vector3.up * (0.004f + 0.003f * size));
            mark.transform.localScale = Vector3.one * size;
            Mesh disc = MeshKit.Cached(MeshKit.Key("Level14/Pad"), () => MeshKit.Cylinder(0.62f, 0.004f, 40));
            GadgetKit.Visual(mark.transform, "Pad", disc, pad).shadowCastingMode = ShadowCastingMode.Off;
            Mesh sole = MeshKit.Cached(MeshKit.Key("Level14/Sole"), () =>
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
                GadgetKit.Visual(mark.transform, "Shoe", sole, print, new Vector3(side * 0.22f, 0.005f, 0f), flat).shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // A hallway somebody lives in: spare blocks and a spool in the corners of the first hall (nothing to
        // climb stands in the second one, where a way up would be a way round the ledge), and lamps high on
        // the walls.
        void BuildDressing(LevelContext ctx)
        {
            Dip dip = ctx.Dip;
            var set = new Level01Set();
            float corner = HalfWidth - 0.9f;
            Block(ctx, set, new Vector3(1.2f, 1.2f, 1.2f), new Vector3(corner, 0f, 0.9f), 8f);
            Block(ctx, set, new Vector3(0.8f, 0.8f, 0.8f), new Vector3(corner - 0.05f, 1.2f, 0.95f), -19f);
            Block(ctx, set, new Vector3(1.4f, 0.7f, 0.7f), new Vector3(corner - 1.6f, 0f, 0.6f), -6f);
            Piece(ctx, set, ToyFactory.ThreadSpool(0.45f, 1f, grabbable: false), new Vector3(-corner, 0.5f, WallZ - 0.9f), Quaternion.identity);
            set.Finish(ctx, "Dressing");

            // Lamps. High on the side walls, out of everybody's reach: a bracket and a warm shade. And low on
            // the cross walls, where a plug would be: night-lights, one beside each face of the mouse-hole and
            // one at the foot of the ledge. None of them is a light (the room has one, the moon); each has a
            // pool of its own warm colour painted on the wall behind it, and the night-lights one on the floor.
            var lamps = new Level01Set();
            var glows = new List<MeshPart>();
            Material bracket = Materials.Room(RoomRecipe.Solid(dip.Deep));
            Color warm = Palette.Lin(LampColor) * 1.35f;
            warm.a = 1f;
            Material shade = Materials.Emissive(ToyRecipe.Lamp, LampColor, warm);
            foreach (Vector2 spot in new[] { new Vector2(3.5f, 6.4f), new Vector2(10.5f, 6.4f), new Vector2(20f, 6.4f), new Vector2(28f, TerraceTop + 5.8f) })
                for (int side = -1; side <= 1; side += 2)
                {
                    var onWall = new Vector3(side * HalfWidth, spot.y, spot.x);
                    var normal = new Vector3(-side, 0f, 0f);
                    Plate(lamps, bracket, onWall - Vector3.up * 0.22f, normal, 0.12f, 0.4f, 0.1f);
                    lamps.Box(shade, onWall + normal * 0.16f + Vector3.up * 0.04f, new Vector3(0.2f, 0.3f, 0.2f));
                    Glow(glows, onWall, normal, 2.6f);
                }
            NightLight(lamps, glows, bracket, shade, new Vector3(-1.7f, 0f, WallZ), Vector3.back);
            NightLight(lamps, glows, bracket, shade, new Vector3(1.7f, 0f, HallBZ), Vector3.forward);
            NightLight(lamps, glows, bracket, shade, new Vector3(2.6f, 0f, TerraceZ), Vector3.back);
            lamps.Finish(ctx, "Lamps", castShadows: false);

            Color pool = Palette.Lin(LampColor) * 0.2f;
            pool.a = 1f;
            Mesh merged = SetMeshes.Take(ctx, "Level14 Glows", glows);
            var glow = new GameObject("Glows");
            glow.AddComponent<MeshFilter>().sharedMesh = merged;
            MeshRenderer renderer = glow.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Materials.Flat(new FlatRecipe { Name = "Level14 Glow", Color = pool, Shape = FlatShape.SoftDisc, Soft = 1f, Blend = FlatBlend.Additive });
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ctx.AddStatic(glow, Vector3.zero);
        }

        /// <summary>The colour of the hallway's lamps: the warm white of the art bible's night-light, a little deeper.</summary>
        static readonly Color LampColor = Palette.Mix(Palette.WarmWhite, Palette.Tangerine, 0.18f);

        // A plug-in night-light at the foot of a wall: a plate, a small glowing shade, a pool of light on the
        // wall and one on the floor in front of it. Looks only.
        static void NightLight(Level01Set set, List<MeshPart> glows, Material plate, Material shade, Vector3 foot, Vector3 normal)
        {
            Vector3 onWall = foot + Vector3.up * 0.42f;
            Plate(set, plate, onWall - Vector3.up * 0.12f, normal, 0.2f, 0.2f, 0.05f);
            Plate(set, shade, onWall + Vector3.up * 0.08f, normal, 0.16f, 0.24f, 0.09f);
            Glow(glows, onWall + Vector3.up * 0.1f, normal, 1.5f);
            Glow(glows, foot + normal * 0.9f - Vector3.up * 0.03f, Vector3.up, 1.6f);
        }

        // A soft disc of light lying on a surface (a wall, a floor), just proud of whatever is painted there.
        static void Glow(List<MeshPart> glows, Vector3 onSurface, Vector3 normal, float radius)
        {
            Mesh quad = MeshKit.Cached(MeshKit.Key("Level14/Glow"), () =>
            {
                var mesh = new Mesh { name = "Level14 Glow Quad", hideFlags = HideFlags.DontSave };
                mesh.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
                mesh.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
                mesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
                mesh.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });
                mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                mesh.RecalculateBounds();
                return mesh;
            });
            Vector3 up = Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up;
            glows.Add(new MeshPart(quad, onSurface + normal * 0.055f, Quaternion.LookRotation(-normal, up), Vector3.one * (2f * radius)));
        }

        static void Block(LevelContext ctx, Level01Set set, Vector3 size, Vector3 foot, float yaw) =>
            Piece(ctx, set, ToyFactory.WoodenBlock(size, grabbable: false), foot + Vector3.up * (size.y * 0.5f), Quaternion.Euler(0f, yaw, 0f));

        // A set piece: its colliders stand in the level, its looks join the set.
        static void Piece(LevelContext ctx, Level01Set set, GameObject piece, Vector3 centre, Quaternion rotation)
        {
            ctx.AddStatic(piece, centre, rotation);
            set.Absorb(piece);
        }

        // ---- The solution -----------------------------------------------------------------------------------

        /// <summary>The direction of a view: yaw 0 looks down +Z, positive pitch looks up.</summary>
        public static Vector3 View(float yawDegrees, float pitchDegrees) => Quaternion.Euler(-pitchDegrees, yawDegrees, 0f) * Vector3.forward;

        public override IEnumerator Solve(Bot bot)
        {
            yield return Small(bot);
            yield return Big(bot);
            yield return Climb(bot);
            yield return Medium(bot);
        }

        /// <summary>
        /// Small. From the start the door is six units off and looks small; put down at the feet it is as
        /// small as it looks: a door for a mouse. (From wherever the bot stands when it has the door in view.)
        /// </summary>
        public IEnumerator Small(Bot bot)
        {
            Player player = bot.Player;
            yield return bot.Grab(door);
            yield return bot.WalkTo(StandA, 0.1f);
            yield return bot.DropAt(player.Eye + View(0f, SmallPitch) * 2f);
            yield return bot.Until(() => portal.Settled, 2f);
            yield return Through(bot);
        }

        /// <summary>
        /// Big. Mouse-sized: a step on from the little door, so that it does not look too big, then round,
        /// pick it up and carry it through the mouse-hole. Looking almost straight up nothing stops it but
        /// its own limit.
        /// </summary>
        public IEnumerator Big(Bot bot)
        {
            Player player = bot.Player;
            yield return bot.WalkTo(door.Position + Vector3.forward * 0.42f, 0.03f, 4f);
            yield return bot.Grab(door);
            yield return bot.LookAt(new Vector3(0f, HoleHeight * 0.5f, WallZ));
            yield return bot.WalkTo(new Vector3(0f, 0f, WallZ - 0.5f), 0.05f, 15f, true);
            yield return bot.WalkTo(StandB, 0.05f, 15f, true);
            yield return bot.DropAt(player.Eye + View(0f, BigPitch) * 5f);
            yield return bot.Until(() => portal.Settled, 3f);
            yield return Through(bot);
        }

        /// <summary>A giant: up the ledge in one jump, and on to the shoe prints up there.</summary>
        public IEnumerator Climb(Bot bot)
        {
            Player player = bot.Player;
            yield return bot.LookAt(new Vector3(0f, TerraceTop + 1.5f, EndZ));
            // A run-up, if the bot stands far enough back for one; from nearer the face it jumps where it stands.
            var runUp = new Vector3(0f, 0f, TerraceZ - 0.8f * player.Scale);
            if (player.Position.z < runUp.z) yield return bot.WalkTo(runUp, 0.15f * player.Scale, 6f);
            yield return bot.Jump();
            yield return bot.WalkTo(StandT, 0.15f * player.Scale, 4f);
            yield return bot.Until(() => player.Grounded && player.Position.y > TerraceTop - 0.1f, 3f);
            yield return bot.WalkTo(StandT, 0.15f, 6f);
        }

        /// <summary>
        /// Medium. From up on the ledge the big door looks small again; at the feet it is a door for the
        /// way out.
        /// </summary>
        public IEnumerator Medium(Bot bot)
        {
            Player player = bot.Player;
            yield return bot.Grab(door);
            yield return bot.DropAt(player.Eye + View(0f, MediumPitch) * 2f);
            yield return bot.Until(() => portal.Settled, 2f);
            yield return Through(bot);
            yield return GoOut(bot);
        }

        /// <summary>Into the way out at the back of the ledge.</summary>
        public IEnumerator GoOut(Bot bot)
        {
            Game game = bot.Game;
            yield return bot.LookAt(ExitCentre);
            yield return bot.WalkTo(new Vector3(0f, TerraceTop, ExitCentre.z), 0.3f, 10f);
            yield return bot.Until(() => game.LevelCompleted, 3f);
        }

        /// <summary>
        /// Looks through the door, then walks into it from the side the bot is on, through the middle of its
        /// opening, until the door has done what it does (<see cref="Trips"/>): the bot comes out beyond it
        /// at the door's size, or back out of the near side, or stays as it is.
        /// </summary>
        public IEnumerator Through(Bot bot)
        {
            Player player = bot.Player;
            float target = portal.TargetScale;
            Vector3 normal = portal.Normal;
            normal.y = 0f;
            normal.Normalize();
            if (Vector3.Dot(normal, door.Position - player.Position) < 0f) normal = -normal;
            float halfDepth = ToyFactory.DoorwaySize.z * door.Scale * 0.5f;
            float beyond = Player.BaseRadius * target + halfDepth + 0.05f;
            int before = trips;
            // Line up in front of the opening first if the bot stands off to one side.
            Vector3 front = door.Position - normal * (player.Radius + halfDepth + 0.3f * player.Scale);
            if (Vector3.Cross(normal, player.Position - door.Position).magnitude > 0.2f * door.Scale) yield return bot.WalkTo(front, 0.1f * player.Scale, 6f);
            // What is to be seen in the opening is where the door leads: a look at it, no higher than the eye.
            yield return bot.LookAt(door.Position + Vector3.up * Mathf.Min(player.EyeHeight, ToyFactory.DoorwayOpening.y * door.Scale * 0.5f));
            IEnumerator walk = bot.WalkTo(door.Position + normal * (beyond + 0.6f * target), 0.2f * target, 6f);
            while (trips == before && walk.MoveNext()) yield return walk.Current;
            if (trips == before) yield return bot.Until(() => trips > before, 2f);
        }
    }
}
