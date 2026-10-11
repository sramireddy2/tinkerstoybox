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
    /// Level 11, "Blocking Lasers" (LEVELS.md): a held toy is not in the world - the roof only works once
    /// you let go.
    ///
    /// A shoebox diorama on the workbench, its lid pushed back over the far half: under the lid a rig of
    /// laser pointers rains beams straight down across eight units of corridor, and the little door out is
    /// on the far side of them. The one toy is a playing card on a spool. Two low walls of building blocks
    /// run through the rain; a card wide enough to lie across both is a roof, and the patch of floor with no
    /// red dots on it is its shadow. Held, the card stops nothing - the beams go straight through what is in
    /// the hand - so it has to be made big where it is let go: laid flat against the far wall, above the
    /// walls, it is as big as the far wall is far, and it drops onto both of them.
    ///
    /// The card only ever gets as big as it looks. Picked up from the start it is 0.377 of its distance; held
    /// against the far wall from there it is 5.9, a roof over all of the rain. It needs 4.7 to lie on both
    /// walls with something to spare and stops growing at 7.
    ///
    /// Where a held card has got to cannot be seen - in the hand it looks the same against the far wall and
    /// caught on a wall half way - so the dashed outline on the floor is the gauge, as in Level 2: red and
    /// blinking while the card is held out over the walls and would be no roof, green and steady while it
    /// would be one if it were let go where it is, and for as long as it lies there as one.
    /// (Where this differs from LEVELS.md, and what was measured: tools/out/notes/level11-build.md; what
    /// the reviews found and changed - the wider seat, the lines for a pick-up from too near and for a card
    /// on end, a light card that does not touch the player; then the outline as the gauge, the card that
    /// goes back to its table when its holder is zapped, the cushion under a card that only just spans
    /// the walls: tools/out/notes/level11-review.md.)
    /// </summary>
    [Level(11, "blocking-lasers", "Blocking Lasers", Phase = 3)]
    public sealed class Level11BlockingLasers : LevelDefinition
    {
        // ---- Layout (LEVELS.md, Level 11; floor y = 0, travel +Z) ------------------------------------------
        public const float HalfWidth = 6f, NearZ = -2f, FarZ = 23f, WallThickness = 1f;
        /// <summary>The underside of the lid, and of the unseen cap over the open end of the box.</summary>
        public const float LidY = 8f;
        /// <summary>The lid is pushed back: it is drawn from here to the far wall. In front of it the box is open to the room.</summary>
        public const float LidNear = 9f;
        /// <summary>How high the box's sides are drawn where the lid does not cover them (they stand, unseen, up to the cap).</summary>
        public const float OpenWallTop = 4.5f;
        /// <summary>The box's cardboard bottom: the bench lies this far under the corridor's floor (y = 0).</summary>
        public const float FloorThickness = 0.1f;
        public const float DoorHalfWidth = 1f, DoorHeight = 2.6f, TunnelEnd = 27f;
        /// <summary>The two low walls: their faces toward the lane, their thickness, where they begin, their tops.</summary>
        public const float LaneHalfWidth = 3f, LowWallThickness = 1f, LowWallNear = 13f, LowWallTop = 3f;
        /// <summary>
        /// The rain falls on the whole width of the corridor between these two lines. (LEVELS starts it at
        /// 14. The narrowest card that lies on both walls is 8.6 long and ends at 14.4 when it is against the
        /// far wall: starting the rain at 14.5 makes "on both walls, against the far wall" a roof over all of
        /// it, with no strip at the near end for the smallest roofs to miss.)
        /// </summary>
        public const float RainNear = 14.5f, RainFar = 22f;
        public const float LatticePitch = 0.45f, ZapDelay = 0.1f;

        /// <summary>The card's table: a spool. (LEVELS: 0.8 high. At 1.1 the first picture looks less steeply down and shows the rain.)</summary>
        public static readonly Vector3 TableBase = new Vector3(0f, 0f, 3f);
        public const float TableRadius = 0.6f, TableHeight = 1.1f;

        // ---- The card ----------------------------------------------------------------------------------------
        public const float StartScale = 0.5f, MinScale = 0.2f, MaxScale = 7f;
        public const string CardTag = "card";
        /// <summary>The size the dashed line on the far wall is painted for (the solver's card), and the least that lies on both walls with something to spare.</summary>
        public const float IntendedScale = 5.9f, LeastScale = 4.7f;
        /// <summary>Narrower than the lane below this: it cannot lie on both walls at all.</summary>
        public static readonly float SpanScale = LaneHalfWidth * 2f / ToyFactory.PlayingCardSize.x;

        /// <summary>Where the player starts, and where the solver picks the card up and lets it go from: a step in front of the table.</summary>
        public static readonly Vector3 Spawn = new Vector3(0f, 0f, 1.75f);
        /// <summary>The view at the start: the crosshair on the card, the rain and the door behind it.</summary>
        public static readonly float SpawnPitch = -Mathf.Atan2(Player.BaseEyeHeight - CardHome.y, TableBase.z - Spawn.z) * Mathf.Rad2Deg;
        /// <summary>The middle of the card where it lies on its table.</summary>
        public static Vector3 CardHome => TableBase + Vector3.up * (TableHeight + ToyFactory.PlayingCardSize.y * 0.5f * StartScale + 0.002f);
        /// <summary>What the solver looks at when it lets go: the far wall, well above the little door.</summary>
        public static readonly Vector3 AimPoint = new Vector3(0f, 7f, FarZ);
        public static readonly Vector3 ExitCentre = new Vector3(0f, 1.3f, 25.5f);
        public static readonly Vector3 ExitSize = new Vector3(2f, 2.6f, 2f);

        /// <summary>
        /// Apparent size (scale / pick-up distance) below which the card cannot be made into a roof from
        /// anywhere in the box: held against the far wall from the back wall it is still too narrow. From
        /// the start it is 0.377.
        /// </summary>
        public const float LooksTooSmall = 0.26f;
        /// <summary>
        /// Apparent size above which the card is at its full size long before the far wall, wherever in the
        /// dry half it is held from: the roof picked up again from underneath looks like this (2 to 4). It
        /// can still be made a roof from the very edge of the rain, looking steeply up; nobody finds that.
        /// Picked up from in front of where it lies, a roof or a doormat looks 0.8 to 1.3.
        /// </summary>
        public const float LooksTooBig = 1.1f;
        /// <summary>A card that went back to its table smaller than this is called too small; a bigger one was out of reach.</summary>
        public const float CrumbScale = 0.6f;
        /// <summary>How near the rain somebody can stand without a beam on them.</summary>
        public const float DryZ = RainNear - 0.4f;

        /// <summary>Said on a pick-up from too far away. The words are Level 1's, on purpose.</summary>
        public const string FarLine = "It only ever gets as big as it looks. Pick it up from closer.";
        /// <summary>Said on a pick-up from too near (the roof, from underneath). The words are Level 7's, on purpose.</summary>
        public const string NearLine = "From this close it looks too big to reach the far wall. Pick it up from further back.";
        /// <summary>
        /// Said to whoever walks into the rain with the card in their hand (the card goes back to its
        /// table: see OnZapped).
        /// </summary>
        public const string HeldLine = "A card in your hand stops nothing. Let go of it and it is in the beams' way. It is back on its table.";
        /// <summary>Said to whoever walks into the rain with nothing over them at all.</summary>
        public const string BareLine = "The beams come straight down. Something solid has to be over your head.";
        /// <summary>Said to whoever is zapped beside the roof: in a strip outside the two walls.</summary>
        public const string BesideLine = "The roof only keeps the lane between the two walls dry. Walk under it, down the middle.";
        /// <summary>
        /// The card was let go lower than the walls' tops. ("Well above": the card has to be over the walls'
        /// tops when it is still half way out and as wide as the lane - a crosshair two and a half units
        /// over them on the far wall, 10 degrees up from the start, more when the view is a little turned.
        /// Lower than that a wall catches it, and held it looks no different.)
        /// </summary>
        public const string LowLine = "Held that low it catches on the walls. Look higher, over them, until the outline turns green.";
        /// <summary>The card stopped on the lid.</summary>
        public const string HighLine = "It stopped against the lid, short of the far wall. Hold it a little lower.";
        /// <summary>The card was at its largest before it got anywhere.</summary>
        public const string FullSizeLine = "It was at its full size before it reached the far wall. Let go from nearer the far wall.";
        /// <summary>Something else stopped it: a side wall (the view was turned), the walls' ends.</summary>
        public const string ShortLine = "Something stopped it short of the far wall. Look straight down the middle, over the two walls.";
        /// <summary>Against the far wall, above the walls, wide enough - and off to one side.</summary>
        public const string SlippedLine = "It missed one of the walls. Stand in the middle and look straight at the far wall.";
        /// <summary>Let go with its long side across the corridor (turned in the hand, or picked up from the side).</summary>
        public const string TurnedLine = "Its long side has to point at the door. Turn it in your hand and hold it up again.";
        /// <summary>Let go standing on end (flipped in the hand): it falls over where it will.</summary>
        public const string OnEndLine = "On its end the card is no roof. Flip it in your hand until it lies flat.";
        /// <summary>Against the far wall, above the walls, and not wide enough.</summary>
        public const string NarrowLine = "Too narrow to lie across both walls. Hold it against the far wall and step back: it grows.";
        /// <summary>The card lies on both walls and beams still reach the lane.</summary>
        public const string GapLine = "The roof does not cover the whole way. Pick it up again and hold it straight against the far wall.";
        /// <summary>Said to whoever clicks on the card they are standing on (the engine lifts nothing from under its player's feet).</summary>
        public const string StandingLine = "You are standing on it. Step off the card to pick it up.";
        public const string CrumbLine = "That card was too small to use. It is back on its table.";
        public const string LostLine = "Out of reach there. The card is back on its table.";

        Prop card;
        LaserRain rain;
        PropLeash useless, room;
        Exit exit;
        GameObject table;
        string lastLine;
        int lastSaid;
        /// <summary>Apparent size of the card at its last pick-up.</summary>
        float ratio;
        /// <summary>The card has been looked at since it was last let go (or has not been let go yet).</summary>
        bool judged;
        int restTicks, dropTick;
        // The card as it was let go: its underside, its top, how far toward the far wall it reached, how far
        // to either side, its size, the pitch of the view, and whether its long side lay across the corridor.
        float dropBottom, dropTop, dropFar, dropMinX, dropMaxX, dropScale, dropPitch;
        bool dropAcross, dropOnEnd;
        // The seat a card that was let go nearly right is on its way to (see FindSeat), and for how long yet.
        int seatTicks;
        Vector3 seatCentre;
        // A card that only just spans the walls is on its way down to them (see Cushion).
        bool cushion;

        // The outline is the gauge (Level 2's language): its renderer, the paint it wears while nothing is
        // judged, what it shows now, and what the card that was let go last was read as in that moment.
        MeshRenderer outline;
        Material paint;
        bool wasHeld, fitAtLetGo;
        int stillTicks;

        /// <summary>How many times a card that was let go nearly right has been helped into its seat.</summary>
        public int Seatings { get; private set; }
        /// <summary>The dashed outline on the floor and the marks on the walls' ends: one renderer.</summary>
        public Renderer Outline => outline;
        /// <summary>
        /// What the outline says about the card: Idle (paint) while nothing is judged, Good (green, steady)
        /// while the card in the hand would be a roof if it were let go where it is - and for as long as it
        /// is one -, TooSmall (red, blinking) while it is held out over the outline and would be none.
        /// </summary>
        public FitState Gauge { get; private set; }
        // Recoverable is asked every tick; the answer only changes when the card moves.
        bool cacheValid, cachedRecoverable;
        Vector3 cachedCentre;
        Quaternion cachedRotation;
        float cachedScale;

        public Prop Card => card;
        public LaserRain Rain => rain;
        /// <summary>Brings a card that is of no use where it lies back to its table.</summary>
        public PropLeash Useless => useless;
        public Exit Exit => exit;
        /// <summary>The spool the card starts on (a set piece: it cannot be picked up).</summary>
        public GameObject Table => table;

        public override string Blurb => "It's raining lasers. All you have is a playing card.";

        public override string[] Hints => new[]
        {
            "The beams come straight down. Anything solid above your head stops them - but not while it is in your hand.",
            "Those two low walls could hold up a roof. The card would have to be wider than the gap between them.",
            // Five lines of the pause card's hint panel at full size (Level11UiTests.EveryHint_FitsThePauseCardsPanel_AtFullSize).
            "Pick the card up from the shoe prints: from there it looks big. Stay there and look up, over the two low walls, at the far wall: the dashed outline turns green. Let go, and walk under the card.",
        };

        public override string Environment => "pegboard-workbench";
        // The bench is the room's; the box stands on it on its own cardboard bottom.
        public override float GroundY => -FloorThickness;
        public override float KillY => -30f;

        /// <summary>
        /// True while the card lies across the two low walls: all of it up at their tops (on them, or wedged
        /// between them a hand below), reaching from one to the other.
        /// </summary>
        public bool OnBothWalls
        {
            get
            {
                if (card == null || card.Removed || card.Held || card.Frozen) return false;
                GadgetKit.VerticalExtent(card, out float bottom, out _);
                if (bottom < LowWallTop - 0.4f || bottom > LowWallTop + 0.06f) return false;
                Bounds bounds = card.Colliders[0].bounds;
                return bounds.max.z > LowWallNear && bounds.min.x < -LaneHalfWidth + 0.05f && bounds.max.x > LaneHalfWidth - 0.05f;
            }
        }

        /// <summary>True while the card lies still and is a roof over the whole lane: no beam reaches the floor between the walls.</summary>
        public bool Roofed => card != null && !card.Removed && !card.Frozen && AtRest(card) && rain.Coverage01 > 1f - 1e-4f;

        /// <summary>
        /// A strip of the lane that beams still reach is harmless while it is narrower than this: the rain
        /// has to rest on somebody for a tenth of a second, and nobody takes that long to cross it. (Measured:
        /// a walker went dry under a roof that ended 0.2 short of the rain's far edge.)
        /// </summary>
        public const float HarmlessGap = 0.24f;

        /// <summary>
        /// The longest stretch along the lane that is not roofed over, in units: 0 under a whole roof, the
        /// rain's whole length with none. Looked at down five lines of the lane, a beam every 0.06.
        /// </summary>
        public float LongestGap(Game game)
        {
            const float step = 0.06f, from = LidY - 0.02f;
            float longest = 0f;
            for (int column = -2; column <= 2; column++)
            {
                float x = column * (LaneHalfWidth - 0.3f) * 0.5f, run = 0f;
                for (float z = RainNear + step * 0.5f; z < RainFar; z += step)
                {
                    // Roofed: something stops the beam above the head of whoever stands there.
                    bool roofed = game.PhysicsScene.Raycast(new Vector3(x, from, z), Vector3.down, from - Player.BaseHeight, Layers.SolidMask, QueryTriggerInteraction.Ignore);
                    run = roofed ? 0f : run + step;
                    if (run > longest) longest = run;
                }
            }
            return longest;
        }

        /// <summary>
        /// The size of a card that was picked up looking this big (scale / distance) and is held flat against
        /// the far wall from a place z, the view pitched up by so many degrees: the backstop rule of LEVELS
        /// 0.2 with the card's half length for its reach.
        /// </summary>
        public static float ScaleAt(float ratio, float standZ, float pitchDegrees = 14f) =>
            ratio * (FarZ - standZ) / (Mathf.Cos(pitchDegrees * Mathf.Deg2Rad) + ratio * ToyFactory.PlayingCardSize.z * 0.5f);

        public override void Build(LevelContext ctx)
        {
            card = null;
            rain = null;
            useless = null;
            room = null;
            exit = null;
            table = null;
            lastLine = null;
            lastSaid = -100000;
            ratio = 0f;
            judged = true;
            restTicks = 0;
            dropTick = 0;
            dropBottom = dropTop = dropFar = dropMinX = dropMaxX = dropScale = dropPitch = 0f;
            dropAcross = dropOnEnd = false;
            seatTicks = 0;
            seatCentre = Vector3.zero;
            cushion = false;
            Seatings = 0;
            outline = null;
            paint = null;
            wasHeld = fitAtLetGo = false;
            stillTicks = 0;
            Gauge = FitState.Idle;
            cacheValid = false;
            cachedRecoverable = true;
            cachedCentre = Vector3.zero;
            cachedRotation = Quaternion.identity;
            cachedScale = 0f;

            Dip dip = ctx.Dip;
            BuildBox(ctx, dip);
            BuildLowWalls(ctx);
            outline = BuildPaint(ctx, dip, out paint);
            BuildDressing(ctx);

            // The card on its spool, face up, its long side along the corridor. It stays put until it is
            // picked up: nobody knocks it off by walking into the spool.
            table = ctx.AddStatic(ToyFactory.ThreadSpool(TableRadius, TableHeight, grabbable: false), TableBase + Vector3.up * (TableHeight * 0.5f));
            card = ToyCatalog.Add(ctx, ToyId.PlayingCard, CardHome, StartScale, options: o =>
            {
                o.MinScale = MinScale;
                // 9.8 wide at most: it cannot wedge between the box's sides, twelve apart.
                o.MaxScale = MaxScale;
                o.Tags = new[] { CardTag };
                o.FrozenUntilGrabbed = true;
                // Its heading decides whether it fits between the box's sides and lies on both walls (four
                // degrees askew is too much): in the hand it is square to the view, wherever on it the click
                // landed and however it lay.
                o.SquareOnGrab = true;
            });

            // The rig under the lid: beams straight down over the whole width, from the lid to the floor.
            rain = new LaserRain(ctx, new LaserRainOptions
            {
                Name = "Rain", Center = new Vector3(0f, LidY, (RainNear + RainFar) * 0.5f), Size = new Vector2(HalfWidth * 2f, RainFar - RainNear),
                Direction = Vector3.down, Range = LidY, LatticePitch = LatticePitch, ZapDelay = ZapDelay,
                Lane = Zone.MinMax(new Vector3(-LaneHalfWidth, 0f, RainNear), new Vector3(LaneHalfWidth, LidY, RainFar)),
            });
            rain.Zapped += () => OnZapped(ctx);

            // "Closer" has to be possible, and nothing is ever out of reach for good: a card that cannot be
            // seen looking big enough from anywhere dry goes back to its table.
            float outer = HalfWidth + WallThickness + 2f;
            Zone everywhere = Zone.MinMax(new Vector3(-outer, -4f, NearZ - 4f), new Vector3(outer, LidY + 4f, TunnelEnd + 4f));
            float returned = StartScale;
            useless = new PropLeash(ctx, new PropLeashOptions
            {
                Name = "Useless", Props = new[] { card }, Forbidden = new[] { everywhere },
                Unless = () => Recoverable(ctx.Game),
                RestSpeed = 1f, Grace = 1.5f, Action = LeashAction.Respawn, OnReturn = prop => returned = prop.Scale,
            });
            useless.PropReturned += prop =>
            {
                judged = true;
                Say(ctx, returned < CrumbScale ? CrumbLine : LostLine);
            };
            // Should the card ever leave the box (it cannot by any means the player has), it comes back.
            room = new PropLeash(ctx, new PropLeashOptions { Name = "Room", Props = new[] { card }, Allowed = new[] { everywhere }, Grace = 2f });

            // What the player is told, each at the moment it happens.
            ctx.Game.Events.PropGrabbed += e =>
            {
                if (e.Prop != card) return;
                ratio = e.OldScale / Mathf.Max(0.01f, e.GrabDistance);
                judged = true;
                seatTicks = 0;
                cushion = false;
                // Picked up from far off it looks small, and it only ever gets as big as it looks.
                if (ratio < LooksTooSmall) Say(ctx, FarLine, 6f);
                // And from right underneath it looks bigger than anything the far wall can make of it. (Not
                // to somebody who takes the roof away from over their own head, in the rain: a tenth of a
                // second later they are told what matters, and the card is back on its table.)
                else if (ratio > LooksTooBig && ctx.Game.Player.Position.z < RainNear - 0.24f) Say(ctx, NearLine, 6f);
            };
            ctx.Game.Events.PropDropped += e =>
            {
                if (e.Prop != card || card.Held || card.Removed) return;
                GadgetKit.VerticalExtent(card, out dropBottom, out dropTop);
                Bounds bounds = card.Colliders[0].bounds;
                dropFar = bounds.max.z;
                dropMinX = bounds.min.x;
                dropMaxX = bounds.max.x;
                dropScale = card.Scale;
                dropPitch = ctx.Game.Player.Pitch;
                Vector3 along = card.Transform.forward;
                dropAcross = Mathf.Abs(along.x) > Mathf.Abs(along.z) && Mathf.Abs(along.y) < 0.5f;
                dropOnEnd = Mathf.Abs(card.Transform.up.y) < 0.7f;
                dropTick = ctx.Ticks;
                judged = false;
                restTicks = 0;
                seatTicks = FindSeat(out seatCentre, out int ticks) ? ticks : 0;
                if (seatTicks > 0) Seatings++;
                cushion = seatTicks > 0 && card.Scale < SnugScale;
            };
            ctx.OnUpdate(dt => Watch(ctx));

            exit = ctx.AddExit(ExitCentre, ExitSize);
            ctx.SetSpawn(Spawn, 0f, SpawnPitch);
            ctx.AddCheckpoint(Volume.Box(4f, 4f, 3f), Spawn + Vector3.up * 2f, Spawn, 0f, "Spawn");
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

        // A beam has rested on the player: back to the start. Why, in a line.
        void OnZapped(LevelContext ctx)
        {
            // Back on the shoe prints they look at what matters next: down at the card on its table if it
            // is there (the level's first picture, the crosshair on the card), else level down the
            // corridor, where the card lies. (The checkpoint's own trigger knows no pitch.)
            bool onItsTable = card.Held || card.Frozen;
            ctx.Game.Player.SetCheckpoint(Spawn, 0f, onItsTable ? SpawnPitch : 0f);
            if (card.Held)
            {
                Say(ctx, HeldLine, 6f);
                // The card starts over with them: back on its table, as it was found. (Kept in the hand - as
                // a respawn keeps a held toy everywhere else - a roof that was taken away from underneath
                // came back with the player 5 big and a hand from their eyes: the whole view was the card's
                // face, and let go there it lay on the spool like a table top round their chest. Whatever
                // size it looked, picked up from the shoe prints it looks right again.)
                card.Respawn();
            }
            // Under a roof that is on both walls: either it has a gap, or they were not under it at all.
            else if (OnBothWalls) Say(ctx, Mathf.Abs(ctx.Game.Player.Position.x) < LaneHalfWidth ? GapLine : BesideLine, 6f);
            else Say(ctx, BareLine);
        }

        // Every tick: the outline says what the card would be; a card on its way to its seat is helped
        // there; and the card that was let go is looked at once, when it lies still (or after two and a
        // half seconds, should it go on rocking).
        void Watch(LevelContext ctx)
        {
            if (card.Removed) return;
            Ghost(!card.Held && card.Mass <= Player.Mass);
            bool held = card.Held;
            // Let go in this tick: what it was in that moment holds while it comes down.
            if (wasHeld && !held)
            {
                fitAtLetGo = !card.Frozen && Seatable(out _, out _);
                stillTicks = 0;
            }
            wasHeld = held;
            if (!held) stillTicks = AtRest(card) ? stillTicks + 1 : 0;
            Tint(ctx, Read());
            if (held) return;
            if (ctx.Game.LastInput.GrabPressed) Refused(ctx);
            if (seatTicks != 0) Seat();
            if (cushion) Cushion();
            if (judged) return;
            restTicks = AtRest(card) ? restTicks + 1 : 0;
            if (restTicks < 30 && ctx.Ticks - dropTick < JudgeTicks) return;
            judged = true;
            Judge(ctx);
        }

        const int JudgeTicks = 150;

        // A click with nothing in the hand, by somebody who stands on the card and looks at it: nothing
        // happens, and nothing shows why (the crosshair does not even light up). One line.
        void Refused(LevelContext ctx)
        {
            Player player = ctx.Game.Player;
            if (!player.Grounded || player.GroundProp != card || ctx.Game.Grabber.IsHolding) return;
            if (ctx.Game.PhysicsScene.Raycast(player.Eye, player.Forward, out RaycastHit hit, 60f, Layers.SolidMask, QueryTriggerInteraction.Ignore) && PropRef.Of(hit.collider) == card)
                Say(ctx, StandingLine);
        }

        // ---- The outline is the gauge ------------------------------------------------------------------------

        // Where a held card ends up cannot be seen: in the hand it looks the same whether it has reached
        // the far wall or caught on a wall half way (the first review's open point: from the shoe prints
        // the lower third of the far wall over the walls fails, and nothing showed it). So the outline says
        // it, in the words Level 2 taught: paint while nothing is judged, green and steady while the card
        // in the hand would be a roof if it were let go where it is, red and blinking while it is held out
        // over the outline and would be none. Green stays while the card comes down, and for as long as it
        // lies there as a roof. (LEVELS, appendix C: outline tints are the sanctioned cue for a held toy.)
        FitState Read()
        {
            if (card.Frozen) return FitState.Idle;
            if (card.Held)
            {
                if (Seatable(out _, out _)) return FitState.Good;
                // Held out as far as the two walls: a try at a roof. Anywhere nearer the card is just being carried.
                return card.Colliders[0].bounds.max.z > LowWallNear - 0.1f ? FitState.TooSmall : FitState.Idle;
            }
            // On its way down (and to its seat): what it was when it was let go, until it lies still.
            if (fitAtLetGo && stillTicks < 30) return FitState.Good;
            // Lying still: green for as long as it is a roof. (Still for a moment first: a card too narrow
            // for the walls is "on both" for a few ticks on its way down between them.)
            return stillTicks >= 10 && OnBothWalls && rain.Coverage01 > 1f - 1e-4f ? FitState.Good : FitState.Idle;
        }

        void Tint(LevelContext ctx, FitState state)
        {
            if (outline == null) return;
            Signal wrong = Level02ThimbleChasm.Wrong;
            if (state != Gauge)
            {
                Gauge = state;
                if (state == FitState.Idle) outline.sharedMaterial = paint;
                else
                {
                    Signal signal = state == FitState.Good ? Palette.Go : wrong;
                    outline.sharedMaterial = Materials.Gadget(signal);
                    Materials.SetEmission(outline, signal.Emission);
                }
            }
            // "This would not be one" blinks; "this would" is steady (state never depends on hue alone).
            if (Gauge != FitState.Idle && Gauge != FitState.Good) Materials.SetEmission(outline, wrong.EmissionAt(wrong.GainAt(ctx.Time)));
        }

        // A card is paper. One heavier than the player goes through them when it comes at them and carries
        // them when they stand on it: the engine sees to that. One lighter than they are would be pushed
        // about and lean on them - and a card that leans on somebody who walks into the rain is over their
        // head for a step or three (measured: let go overhead at the edge of the rain, 1.2 big, and sprinted
        // under, it kept the beams off for two units). So a light card does not touch the player at all: it
        // falls past them and lies where it lands. Beams, the hand and everything else meet it as before.
        void Ghost(bool on)
        {
            foreach (Collider collider in card.Colliders)
            {
                if (collider == null || ((collider.excludeLayers.value & Layers.PlayerMask) != 0) == on) continue;
                collider.excludeLayers = on ? Layers.PlayerMask : 0;
            }
        }

        // ---- The seat ----------------------------------------------------------------------------------------

        /// <summary>
        /// How far a card that was let go nearly right is helped: this far sideways and toward the far wall,
        /// this many degrees, over this many ticks at least and at most (the farther, the longer; it is
        /// still falling for most of them).
        /// </summary>
        public const float SeatSlide = 2f, SeatPush = 2.4f, SeatTurn = 9f;
        /// <summary>
        /// A card narrower than this has so little of itself on either wall that it is always put exactly in
        /// its seat, and set down on the walls (see Cushion).
        /// </summary>
        public const float SnugScale = 5f;
        /// <summary>A snug card comes down on the walls no faster than this, slowing over this much height above their tops.</summary>
        public const float CushionSpeed = 1.5f, CushionHeight = 0.9f;
        public const int SeatTicks = 15, SeatTicksMost = 24;
        /// <summary>A card on its way to its seat comes no nearer the walls' tops than this until it is square over them.</summary>
        public const float SeatHover = 0.15f;

        // The two walls and the far wall are a cradle, and a card let go over it nearly right finds its seat
        // as it comes down: it slides to the middle, turns square and ends against the far wall. (Measured
        // without this: held square but looking four degrees to one side - a unit off, fifteen units out -
        // the card missed a wall; from the near end of the window two degrees did it. LEVELS reckons with
        // six.) Only a card that is flat, above the walls, pointing down the corridor, wide enough to lie on
        // both walls and long enough to cover the rain from where it will lie; and only this far. Anything
        // else is left to fall as it will, and is told why it did not work.
        // (The reviewer's sweep - a step of 0.6 off the shoe prints any way, the crosshair up to two units
        // either side of the middle of the far wall - asked for two units sideways and 2.4 toward the far
        // wall: looking four degrees to the side the card one stands beside is 1.7 off, and seven degrees
        // turn a corner into the box's side two units short. Before, that was 1.6 and 1.5.)
        bool FindSeat(out Vector3 centre, out int ticks)
        {
            ticks = 0;
            if (!Seatable(out centre, out float share)) return false;
            // Let go right, there is nothing to help - unless it only just spans the walls: a card of 4.5
            // has a seventh of a unit on either wall, and "right" (a fiftieth off the middle, a fifth of a
            // degree askew) was not right enough for that. (The second review's sweep: 4.49, let go dead
            // straight from high up, slipped off one wall; every narrow card that had been put in its seat,
            // down to 4.41, lay.) Those are put exactly in the middle whatever they need.
            Vector3 from = card.Center;
            bool right = Mathf.Abs(from.x) <= 0.02f && centre.z - from.z <= 0.05f && Mathf.Abs(Heading(card.Transform)) <= 0.2f;
            if (right && card.Scale >= SnugScale) return false;
            // The farther it has to go, the longer it takes: never faster than the nearest ones were.
            ticks = Mathf.Clamp(Mathf.CeilToInt(SeatTicks * share), SeatTicks, SeatTicksMost);
            return true;
        }

        /// <summary>
        /// Would the card, as it is placed right now (in the hand, or just let go), come to lie as a roof:
        /// flat, above the walls, pointing down the corridor, wide and long enough, and no farther from its
        /// seat than a card is helped? Where that seat is, and how much of the help it needs (0: none).
        /// The outline's green and the seat itself ask the same question.
        /// </summary>
        bool Seatable(out Vector3 centre, out float share)
        {
            centre = Vector3.zero;
            share = 0f;
            Transform t = card.Transform;
            float scale = card.Scale;
            if (Mathf.Abs(t.up.y) < 0.999f || scale < SpanScale + 0.1f) return false;
            GadgetKit.VerticalExtent(card, out float bottom, out _);
            if (bottom < LowWallTop + 0.03f) return false;
            float askew = Mathf.Abs(Heading(t));
            if (askew > SeatTurn) return false;
            Vector3 from = card.Center;
            centre = new Vector3(0f, from.y, Mathf.Max(from.z, FarZ - 0.04f - ToyFactory.PlayingCardSize.z * 0.5f * scale));
            if (Mathf.Abs(from.x) > SeatSlide || centre.z - from.z > SeatPush) return false;
            // From there it has to reach back over the near edge of the rain.
            if (centre.z - ToyFactory.PlayingCardSize.z * 0.5f * scale > RainNear - 0.05f) return false;
            share = Mathf.Max(Mathf.Abs(from.x) / 1.6f, (centre.z - from.z) / 1.5f, askew / 8f);
            return true;
        }

        // How far the card's long side is from pointing down the corridor, in degrees (it has no front or back).
        static float Heading(Transform t)
        {
            Vector3 along = t.forward;
            float heading = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg;
            return Mathf.DeltaAngle(0f, Mathf.Repeat(heading + 90f, 180f) - 90f);
        }

        // One tick of the way to the seat: the speed that gets it there in the ticks that are left, and in
        // the tick after the last of them, a stop. Its fall is its own - down to a hand above the walls:
        // there it waits until it is square over both (let go low, it would otherwise come down on one wall
        // with its other side in the lane, and jam).
        void Seat()
        {
            Rigidbody body = card.Body;
            if (body.isKinematic)
            {
                seatTicks = 0;
                return;
            }
            Vector3 velocity = body.linearVelocity;
            if (seatTicks < 0)
            {
                seatTicks = 0;
                body.linearVelocity = new Vector3(0f, velocity.y, 0f);
                body.angularVelocity = Vector3.zero;
                return;
            }
            float left = seatTicks * Sim.Dt;
            Vector3 centre = card.Center;
            velocity.x = (seatCentre.x - centre.x) / left;
            velocity.z = (seatCentre.z - centre.z) / left;
            GadgetKit.VerticalExtent(card, out float bottom, out _);
            // (The step that follows takes a tick of gravity off again: it hangs.)
            if (bottom < LowWallTop + SeatHover) velocity.y = Mathf.Max(velocity.y, Game.Gravity * Sim.Dt);
            body.linearVelocity = velocity;
            body.angularVelocity = new Vector3(0f, -Heading(card.Transform) * Mathf.Deg2Rad / left, 0f);
            seatTicks--;
            if (seatTicks == 0) seatTicks = -1;
        }

        // A card that only just spans the walls is set down on them: over the last stretch above their tops it
        // falls slower and slower - paper, on the air that has to get out from under it - and touches down
        // at walking pace. (At full tilt, eleven units a second from under the lid, a card with a seventh of
        // a unit on either wall bounced one side off, however exactly it was in the middle: 4.49 fell in
        // where 4.41 and 4.55 lay. A wider card comes down with the slap it always had.)
        void Cushion()
        {
            Rigidbody body = card.Body;
            GadgetKit.VerticalExtent(card, out float bottom, out _);
            float above = bottom - LowWallTop;
            // Down, lying still, tipped over or gone between the walls after all: nothing more to do.
            if (body.isKinematic || above < 0.01f || stillTicks > 10 || Mathf.Abs(card.Transform.up.y) < 0.99f)
            {
                cushion = false;
                return;
            }
            if (above >= CushionHeight) return;
            Vector3 velocity = body.linearVelocity;
            float most = Mathf.Lerp(CushionSpeed, 14f, above / CushionHeight);
            if (velocity.y >= -most) return;
            velocity.y = -most;
            body.linearVelocity = velocity;
        }

        // The card has come to rest. A roof needs no words (nor does one with a gap too thin to matter);
        // anything else that was a try at one gets the reason it did not work.
        void Judge(LevelContext ctx)
        {
            if (card.Frozen || LongestGap(ctx.Game) < HarmlessGap) return;
            bool onWalls = OnBothWalls;
            // Put down in the dry half: no try at a roof. (On end it is none wherever it stands, and that is worth a line.)
            if (!onWalls && dropFar < LowWallNear - 1f && !dropOnEnd) return;
            // One that is of no use where it lies is on its way back to the table, with a line of its own.
            if (!onWalls && !Recoverable(ctx.Game)) return;
            Say(ctx, Why(onWalls), 6f);
        }

        // Why the card that was let go is not a roof, from how it was let go.
        string Why(bool onWalls)
        {
            // Whatever it met first: it looked too big in the hand to get anywhere.
            if (ratio > LooksTooBig) return NearLine;
            if (dropOnEnd) return OnEndLine;
            if (dropAcross) return TurnedLine;
            if (dropBottom < LowWallTop - 0.02f) return LowLine;
            if (dropFar < FarZ - 0.5f)
            {
                if (dropTop > LidY - 0.2f) return HighLine;
                if (dropScale > MaxScale - 0.02f) return FullSizeLine;
                return ShortLine;
            }
            // Against the far wall, above the walls. On both of them and beams still get past: it lies askew.
            if (onWalls) return GapLine;
            bool centred = Mathf.Abs(dropMinX + dropMaxX) < 0.6f;
            if (dropScale < SpanScale + 0.05f || (dropScale < LeastScale && centred))
            {
                // Would a step back have done it? Not if it looked too small in the hand.
                return ScaleAt(ratio, NearZ + 0.3f, dropPitch) < LeastScale ? FarLine : NarrowLine;
            }
            return SlippedLine;
        }

        // ---- Nothing is lost ----------------------------------------------------------------------------------

        /// <summary>
        /// Can the card, where it lies, still be made into a roof: is there somewhere dry to stand from which
        /// it is in plain view and looks big enough? (A held card, or one on its table, always can.)
        /// </summary>
        public bool Recoverable(Game game)
        {
            if (card == null || card.Removed || card.Held || card.Frozen) return true;
            Vector3 centre = card.Center;
            Quaternion rotation = card.Rotation;
            float scale = card.Scale;
            if (cacheValid && (centre - cachedCentre).sqrMagnitude < 1e-6f && Quaternion.Angle(rotation, cachedRotation) < 0.05f && Mathf.Approximately(scale, cachedScale))
                return cachedRecoverable;
            cacheValid = true;
            cachedCentre = centre;
            cachedRotation = rotation;
            cachedScale = scale;
            cachedRecoverable = BestRatio(game) >= LooksTooSmall;
            return cachedRecoverable;
        }

        /// <summary>
        /// The biggest the card can look (scale / distance) from anywhere dry it can be seen from: a ring of
        /// places round it, the place nearest to it, and a row along the edge of the rain.
        /// </summary>
        public float BestRatio(Game game)
        {
            Vector3 centre = card.Center;
            Vector3 half = ToyFactory.PlayingCardSize * (0.5f * card.Scale);
            Vector3 right = card.Transform.right, forward = card.Transform.forward;
            GadgetKit.VerticalExtent(card, out _, out float top);
            float best = 0f;
            for (int i = 0; i < 14; i++)
            {
                Vector3 feet;
                if (i == 0) feet = centre;
                else if (i <= 8)
                {
                    float angle = (i - 1) * Mathf.PI * 0.25f;
                    var way = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                    float reach = half.x * Mathf.Abs(Vector3.Dot(way, right)) + half.z * Mathf.Abs(Vector3.Dot(way, forward)) + half.y * Mathf.Abs(Vector3.Dot(way, card.Transform.up));
                    feet = centre + way * (reach + 0.45f);
                }
                else feet = new Vector3(-5f + (i - 9) * 2.5f, 0f, DryZ);
                feet = DryPlace(feet);

                // Standing on the card it cannot be picked up.
                Vector3 off = feet - centre;
                bool over = Mathf.Abs(Vector3.Dot(off, right)) < half.x + 0.3f && Mathf.Abs(Vector3.Dot(off, forward)) < half.z + 0.3f;
                if (over && top < Player.BaseHeight) continue;

                Vector3 eye = feet + Vector3.up * Player.BaseEyeHeight;
                float distance = Vector3.Distance(eye, centre);
                float looks = card.Scale / Mathf.Max(0.01f, distance);
                if (looks <= best || !Sees(game, eye, centre, half, right, forward)) continue;
                best = looks;
            }
            return best;
        }

        // The nearest place to stand that is in the box, out of the rain, and not inside the table or a wall.
        static Vector3 DryPlace(Vector3 feet)
        {
            const float body = 0.35f;
            feet.y = 0f;
            feet.x = Mathf.Clamp(feet.x, -HalfWidth + body, HalfWidth - body);
            feet.z = Mathf.Clamp(feet.z, NearZ + body, DryZ);
            float beside = Mathf.Abs(feet.x);
            if (feet.z > LowWallNear - body && beside > LaneHalfWidth - body && beside < LaneHalfWidth + LowWallThickness + body) feet.z = LowWallNear - body;
            Vector3 fromTable = feet - TableBase;
            float clear = TableRadius + body;
            if (fromTable.sqrMagnitude < clear * clear)
                feet = TableBase + (fromTable.sqrMagnitude > 1e-6f ? fromTable.normalized : Vector3.back) * clear;
            return feet;
        }

        // Is any of the card in plain view from an eye: its middle, or a spot toward one of its corners?
        bool Sees(Game game, Vector3 eye, Vector3 centre, Vector3 half, Vector3 right, Vector3 forward)
        {
            for (int i = 0; i < 5; i++)
            {
                Vector3 point = centre;
                if (i > 0) point += right * (half.x * 0.6f * (i % 2 == 0 ? 1f : -1f)) + forward * (half.z * 0.6f * (i <= 2 ? 1f : -1f));
                Vector3 to = point - eye;
                float distance = to.magnitude;
                if (distance < 1e-3f) continue;
                if (game.PhysicsScene.Raycast(eye, to / distance, out RaycastHit hit, distance + 0.05f, Layers.SolidMask, QueryTriggerInteraction.Ignore) && PropRef.Of(hit.collider) == card)
                    return true;
            }
            return false;
        }

        // ---- Geometry ---------------------------------------------------------------------------------------

        // A box of the level that is drawn.
        static GameObject Slab(LevelContext ctx, Material material, float x0, float x1, float y0, float y1, float z0, float z1, bool shadow = true)
        {
            GameObject slab = ctx.AddStatic(BasicToys.Slab(new Vector3(x1 - x0, y1 - y0, z1 - z0), material), new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f));
            if (!shadow)
                foreach (MeshRenderer renderer in slab.GetComponentsInChildren<MeshRenderer>()) renderer.shadowCastingMode = ShadowCastingMode.Off;
            return slab;
        }

        // A box nobody sees: it stops a held card and the player like any other.
        static void Unseen(LevelContext ctx, string name, float x0, float x1, float y0, float y1, float z0, float z1)
        {
            var unseen = new GameObject(name) { layer = Layers.Default };
            unseen.AddComponent<BoxCollider>().size = new Vector3(x1 - x0, y1 - y0, z1 - z0);
            ctx.AddStatic(unseen, new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f));
        }

        // The shoebox: bottom, sides, the far wall with the little door and the tunnel behind it, and the lid
        // over the far half. Where the lid does not reach, the sides are drawn low - the sun gets in and the
        // workbench shows over them - and stand, unseen, up to an unseen cap at the lid's height.
        static void BuildBox(LevelContext ctx, Dip dip)
        {
            const float t = WallThickness;
            float outX = HalfWidth + t, back = NearZ - t, far = FarZ + t, tunnelOut = TunnelEnd + t, tunnelX = DoorHalfWidth + t;
            Material bottom = Materials.Room(RoomRecipe.For(RoomSurface.LevelStatic, dip, new PatternSpec(RoomPattern.Planks, 2f, 13f, 0.06f)).With(r => r.Name = dip.Name + " Box Bottom"));
            Material cardboard = Materials.Room(RoomSurface.LevelStatic, dip, PatternSpec.Corrugated);
            Material ink = GadgetKit.Body;

            Slab(ctx, bottom, -outX, outX, -FloorThickness, 0f, back, far, false);
            Slab(ctx, bottom, -tunnelX, tunnelX, -FloorThickness, 0f, far, tunnelOut, false);

            // The sides and the back: low where the box is open, whole under the lid.
            Slab(ctx, cardboard, -outX, -HalfWidth, 0f, OpenWallTop, back, LidNear);
            Slab(ctx, cardboard, HalfWidth, outX, 0f, OpenWallTop, back, LidNear);
            Slab(ctx, cardboard, -HalfWidth, HalfWidth, 0f, OpenWallTop, back, NearZ);
            Unseen(ctx, "Unseen Wall -X", -outX, -HalfWidth, OpenWallTop, LidY, back, LidNear);
            Unseen(ctx, "Unseen Wall +X", HalfWidth, outX, OpenWallTop, LidY, back, LidNear);
            Unseen(ctx, "Unseen Wall Back", -HalfWidth, HalfWidth, OpenWallTop, LidY, back, NearZ);
            Slab(ctx, cardboard, -outX, -HalfWidth, 0f, LidY, LidNear, far);
            Slab(ctx, cardboard, HalfWidth, outX, 0f, LidY, LidNear, far);

            // The far wall, round the little door.
            Slab(ctx, cardboard, -HalfWidth, -DoorHalfWidth, 0f, LidY, FarZ, far);
            Slab(ctx, cardboard, DoorHalfWidth, HalfWidth, 0f, LidY, FarZ, far);
            Slab(ctx, cardboard, -DoorHalfWidth, DoorHalfWidth, DoorHeight, LidY, FarZ, far);
            // The tunnel behind it: dark, so that the way out's own light is what shows in the door.
            Slab(ctx, ink, -tunnelX, -DoorHalfWidth, 0f, DoorHeight + t, far, tunnelOut, false);
            Slab(ctx, ink, DoorHalfWidth, tunnelX, 0f, DoorHeight + t, far, tunnelOut, false);
            Slab(ctx, ink, -DoorHalfWidth, DoorHalfWidth, 0f, DoorHeight + t, TunnelEnd, tunnelOut, false);
            Slab(ctx, ink, -DoorHalfWidth, DoorHalfWidth, DoorHeight, DoorHeight + t, far, TunnelEnd, false);

            // The lid, and the cap nobody sees over the open end: together the level's ceiling.
            Slab(ctx, cardboard, -outX, outX, LidY, LidY + 0.6f, LidNear, far);
            SkyCap.Add(ctx, -outX, outX, back, LidNear, LidY);
        }

        // Two rows of building blocks, two courses each, the upper one staggered. What stops things is one
        // box per wall (a card lies on a flat top, not on a row of seams); the blocks are looks.
        static void BuildLowWalls(LevelContext ctx)
        {
            var set = new Level01Set();
            float course = LowWallTop * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                float x0 = side < 0 ? -LaneHalfWidth - LowWallThickness : LaneHalfWidth, x1 = x0 + LowWallThickness;
                Unseen(ctx, side < 0 ? "Low Wall -X" : "Low Wall +X", x0, x1, 0f, LowWallTop, LowWallNear, FarZ);
                float x = (x0 + x1) * 0.5f;
                for (float z = LowWallNear; z < FarZ - 0.01f; z += 2f)
                    WallBlock(ctx, set, new Vector3(x, course * 0.5f, z + 1f), new Vector3(LowWallThickness, course, 2f));
                for (float from = LowWallNear; from < FarZ - 0.01f;)
                {
                    float length = Mathf.Min(from < LowWallNear + 0.01f ? 1f : 2f, FarZ - from);
                    WallBlock(ctx, set, new Vector3(x, course * 1.5f, from + length * 0.5f), new Vector3(LowWallThickness, course, length));
                    from += length;
                }
            }
            set.Finish(ctx, "Low Walls");
        }

        // One building block of a low wall: its looks join the set, and it has no collider of its own.
        static void WallBlock(LevelContext ctx, Level01Set set, Vector3 centre, Vector3 size)
        {
            GameObject block = ToyFactory.WoodenBlock(size, grabbable: false);
            foreach (Collider collider in block.GetComponentsInChildren<Collider>()) Sim.Destroy(collider);
            ctx.AddStatic(block, centre);
            set.Absorb(block);
        }

        /// <summary>
        /// The size of the solver's card: picked up from the start and held flat against the far wall from
        /// there, the view on <see cref="AimPoint"/>. The outline on the floor is painted for it.
        /// </summary>
        public static float SolverScale
        {
            get
            {
                Vector3 eye = Spawn + Vector3.up * Player.BaseEyeHeight;
                float pitch = Mathf.Atan2(AimPoint.y - eye.y, AimPoint.z - eye.z) * Mathf.Rad2Deg;
                return ScaleAt(StartScale / Vector3.Distance(eye, CardHome), eye.z, pitch);
            }
        }

        /// <summary>Where the near edge of the solver's card comes down: the near side of the outline on the floor.</summary>
        public static float OutlineNear => FarZ - ToyFactory.PlayingCardSize.z * SolverScale;
        public static float OutlineHalfWidth => ToyFactory.PlayingCardSize.x * SolverScale * 0.5f;

        // The size language of the campaign, and this level's shadow: the outline of the roof, dashed in the
        // card's colour, on the floor it will keep dry - wider than the two walls together, from in front of
        // them to the far wall. (The far wall, which the card stops against, is behind the rain: nothing
        // painted on it can be seen.) A mark on the end of each wall, at the top: that is where it lies. A
        // frame round the little door. And where to stand: shoe prints at the start.
        // The outline and the marks are one renderer of their own: they are the gauge (see Read), and
        // change their paint for a signal while the card is judged. Returns that renderer and its paint.
        static MeshRenderer BuildPaint(LevelContext ctx, Dip dip, out Material line)
        {
            Color hero = ToyCatalog.Get(ToyId.PlayingCard).ColorIn(dip);
            line = Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Light, hero, 0.9f)));
            Material paper = Materials.Room(RoomRecipe.Solid(Palette.Paper));
            var marks = new Level01Set();
            Vector3 face = Vector3.back;

            float halfWide = OutlineHalfWidth, near = OutlineNear;
            const float stroke = 0.2f, dash = 0.62f, gap = 0.36f;
            marks.Dashes(line, new Vector3(-halfWide, 0f, near), new Vector3(halfWide, 0f, near), Vector3.up, stroke, dash, gap);
            marks.Dashes(line, new Vector3(-halfWide, 0f, near), new Vector3(-halfWide, 0f, FarZ - 0.1f), Vector3.up, stroke, dash, gap);
            marks.Dashes(line, new Vector3(halfWide, 0f, near), new Vector3(halfWide, 0f, FarZ - 0.1f), Vector3.up, stroke, dash, gap);
            for (int side = -1; side <= 1; side += 2)
            {
                float inner = side * LaneHalfWidth, outer = side * (LaneHalfWidth + LowWallThickness);
                marks.Dashes(line, new Vector3(inner, LowWallTop - 0.2f, LowWallNear), new Vector3(outer, LowWallTop - 0.2f, LowWallNear), face, stroke, 0.38f, 0.24f);
            }
            MeshRenderer outline = marks.Finish(ctx, "Outline", castShadows: false).GetComponentInChildren<MeshRenderer>();

            // The little door's frame.
            var set = new Level01Set();
            const float frame = 0.22f, proud = 0.03f;
            float z = FarZ - proud * 0.5f;
            set.Box(paper, new Vector3(-DoorHalfWidth - frame * 0.5f, (DoorHeight + frame) * 0.5f, z), new Vector3(frame, DoorHeight + frame, proud));
            set.Box(paper, new Vector3(DoorHalfWidth + frame * 0.5f, (DoorHeight + frame) * 0.5f, z), new Vector3(frame, DoorHeight + frame, proud));
            set.Box(paper, new Vector3(0f, DoorHeight + frame * 0.5f, z), new Vector3(DoorHalfWidth * 2f, frame, proud));
            set.Finish(ctx, "Paint", castShadows: false);

            StandMark(ctx, "Stand Mark", Spawn, Materials.Room(RoomRecipe.Solid(dip.Deep)), paper);
            return outline;
        }

        const float PadRadius = 0.62f;

        // Looks only: a renderer for the pad and one for each shoe, facing +Z, toes a little apart.
        static void StandMark(LevelContext ctx, string name, Vector3 at, Material pad, Material print)
        {
            var mark = new GameObject(name);
            ctx.AddStatic(mark, at + Vector3.up * 0.012f);
            Mesh disc = MeshKit.Cached(MeshKit.Key("Level11/Pad", PadRadius), () => MeshKit.Cylinder(PadRadius, 0.004f, 40));
            GadgetKit.Visual(mark.transform, "Pad", disc, pad).shadowCastingMode = ShadowCastingMode.Off;
            Mesh sole = MeshKit.Cached(MeshKit.Key("Level11/Sole"), () =>
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

        // A corner of a playroom: spare blocks and a marker in the back corners of the box, behind the start.
        // Nothing stands where a card held toward the far wall passes, or where it comes down.
        static void BuildDressing(LevelContext ctx)
        {
            float corner = HalfWidth - 0.9f, backRow = NearZ + 0.9f;
            Block(ctx, new Vector3(1.3f, 1.3f, 1.3f), new Vector3(-corner, 0f, backRow), 8f);
            Block(ctx, new Vector3(0.9f, 0.9f, 0.9f), new Vector3(-corner + 0.05f, 1.3f, backRow), -16f);
            Block(ctx, new Vector3(1.5f, 0.8f, 0.8f), new Vector3(-corner + 1.7f, 0f, backRow - 0.2f), -7f);
            Block(ctx, new Vector3(1.1f, 1.1f, 1.1f), new Vector3(corner, 0f, backRow), -11f);
            ctx.AddStatic(ToyFactory.Marker(0.35f, 2.8f), new Vector3(corner - 2.4f, 0.35f, backRow - 0.25f), Quaternion.Euler(0f, 86f, 0f));
        }

        static void Block(LevelContext ctx, Vector3 size, Vector3 foot, float yaw) =>
            ctx.AddStatic(ToyFactory.WoodenBlock(size, grabbable: false), foot + Vector3.up * (size.y * 0.5f), Quaternion.Euler(0f, yaw, 0f));

        // ---- The solution -----------------------------------------------------------------------------------

        public override IEnumerator Solve(Bot bot)
        {
            // The card is a step away, under the crosshair: picked up from here it looks as big as it needs to.
            yield return bot.Grab(card);
            // Without moving: look up at the far wall, well above the door. The card slides out flat along
            // the view, over the two walls, until its far edge meets the far wall. A beat, and let go.
            yield return bot.LookAt(AimPoint);
            yield return bot.Wait(0.4f);
            yield return bot.DropAt(AimPoint);
            yield return bot.Until(() => Roofed, 6f);
            yield return bot.Wait(0.3f);
            yield return WalkOut(bot);
        }

        /// <summary>The second half of the solution, with the roof in place: round the table, down the lane under the card, through the door.</summary>
        public IEnumerator WalkOut(Bot bot)
        {
            Game game = bot.Game;
            if (bot.Player.Position.z < TableBase.z + 0.5f) yield return bot.WalkTo(new Vector3(1.8f, 0f, TableBase.z), 0.3f, 10f);
            yield return bot.WalkTo(new Vector3(0f, 0f, LowWallNear - 1f), 0.3f, 15f);
            yield return bot.WalkTo(new Vector3(0f, 0f, FarZ - 0.5f), 0.3f, 15f);
            yield return bot.WalkTo(ExitCentre, 0.5f, 10f);
            yield return bot.Until(() => game.LevelCompleted, 3f);
        }

        /// <summary>True once a prop that was let go has stopped moving.</summary>
        public static bool AtRest(Prop prop) =>
            !prop.Held && prop.Velocity.sqrMagnitude < 0.05f * 0.05f && prop.Body.angularVelocity.sqrMagnitude < 0.05f * 0.05f;
    }
}
