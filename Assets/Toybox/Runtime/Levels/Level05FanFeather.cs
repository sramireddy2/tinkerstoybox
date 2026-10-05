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
    /// Level 5, "The Fan and the Feather" (LEVELS.md): size is area - wide catches wind.
    ///
    /// Two board-game boxes stand on the rug with a canyon between them, and a desk fan the size of a
    /// ferris wheel blows across the first one, over the gap and onto the second. The one toy is a craft
    /// feather on a spool, up on a block of notes beside the stream. Laid on the blotter in the gale it is
    /// a sail: big enough (6.5 and up) it carries whoever stands on its quill across the canyon; smaller
    /// it cups, lifts a hand's breadth and flops back; below 2.5 the wind simply takes it.
    ///
    /// The rule is the gadget's (SailRaft): capacity(s) = 7.78 x 0.30 x s^2 / 22 - 0.0054 s^3 against the
    /// player's 3. The level's part is to make the size readable and every miss recoverable:
    /// the feather's outline is painted on the blotter at the size the intended drop gives, with the least
    /// size that flies dashed inside it; the player starts one step from the spool, so the first pick-up
    /// is a close one; a pick-up that looks too small is told so at once; a feather too small to moor
    /// goes back to its spool wherever it lies; and each way a try can fail has one line.
    /// (What differs from LEVELS.md, and the measurements: tools/out/notes/level05-build.md.)
    /// </summary>
    [Level(5, "fan-feather", "The Fan and the Feather", Phase = 2)]
    public sealed class Level05FanFeather : LevelDefinition
    {
        // ---- Layout (LEVELS.md, Level 5; deck tops y = 0, the wind and the crossing run along +Z) -----------
        public const float DeckMinX = -9f, DeckMaxX = 7f, RightX = 15f;
        public const float BackZ = -6f, CanyonNear = 18f, CanyonFar = 42f, EndZ = 58f;
        public const float BalconyTop = 3f, BalconyFront = 10f;
        public const float RampMinX = 12f, RampEnd = 16f;
        public const float RugY = -15f, WallTop = 12f;
        /// <summary>A feather last seen below this was on its way down the canyon (the kill plane is at -13).</summary>
        public const float CanyonDepth = -6f;
        /// <summary>Thickness of the walls; the boxes reach this far out under them.</summary>
        const float Rim = 1f;

        // ---- The wind (LEVELS: box X -6..4, Y 0..9, Z -6..44, blowing +Z) -----------------------------------
        public const float StreamMinX = -6f, StreamMaxX = 4f, StreamTop = 9f, StreamEnd = 44f;
        /// <summary>What the gale does to a player in the air inside it, in units per second squared (LEVELS: 3).</summary>
        public const float WindPush = 3f;
        /// <summary>
        /// The stream begins a hair above the deck: its underside is drawn (a faint tint), and in the plane
        /// of the deck it would flicker. A feather lying on the deck is in it all the same (WindStream
        /// counts a prop whose middle is within a quarter unit under the box).
        /// </summary>
        public const float StreamFloor = 0.05f;
        /// <summary>
        /// The launch volume: the blotter - the part of deck A that is in the stream, from the back wall to
        /// the canyon's edge. (LEVELS: Z -5..17.5. A feather that lies in front of the fan, or with its
        /// middle on the last half unit before the edge, lies in the wind like any other: it is moored.)
        /// </summary>
        public const float LaunchBack = BackZ, LaunchFront = CanyonNear, LaunchTop = 1.5f;
        /// <summary>
        /// A feather whose middle lies this far beside the blotter is moored all the same. (Measured: held
        /// toward the left-hand blocks at the clamp, the blocks stop it with its middle at -5.99 - a
        /// hundredth inside the stream's edge. Nothing should hang on a hundredth.)
        /// </summary>
        public const float LaunchMargin = 0.5f;

        public const float FanScale = 11f;
        /// <summary>The middle of the fan's guard. It stands behind the first box, its guard just clear of the box's rim.</summary>
        public static readonly Vector3 FanHub = new Vector3(-1f, 5f, BackZ - Rim - 0.05f - ToyFactory.DeskFanGuardDepth * 0.5f * FanScale);

        // ---- The feather ------------------------------------------------------------------------------------
        public const float StartScale = 1f, MinScale = 0.5f, MaxScale = 12f;
        public const string SailTag = "sail";
        /// <summary>From this size the feather lies still in the wind and is moored; below it the wind takes it.</summary>
        public const float MoorAbove = 2.5f;
        /// <summary>The size the intended drop gives (the painted outline), and the least size that carries the player.</summary>
        public const float IntendedScale = 9.2f, LeastScale = 6.5f;
        /// <summary>
        /// How far the gale presses the moored feather into the blotter, per unit of scale. It comes to rest
        /// balanced on its quill (0.015 s under its middle) with its outline (0.002 s) in the air - a lip
        /// of 0.16 at the intended size, which no player walks over. Pressed down by the difference the
        /// outline lies on the blotter and the feather is walked onto, as LEVELS.md means it to be.
        /// </summary>
        public const float PressDepth = 0.013f;

        /// <summary>
        /// The spool stands two steps from the balcony's edge. (LEVELS puts it at (11, 2), four units from
        /// the edge; from beside it there the balcony's own floor hides the whole blotter - seen in the
        /// first pictures - and the player would start without a view of where the feather goes.)
        /// </summary>
        public static readonly Vector3 SpoolBase = new Vector3(8.95f, BalconyTop, 3.2f);
        /// <summary>
        /// The spool is 0.9 high (LEVELS and the first build: 1.1). What a player does first is click on the
        /// feather, turn to the painted outline and click again - from the start mark, without a step to
        /// the edge. On that way out the held feather passes over the spool's rim, between 0.8 and 1.5 from
        /// the eye; over a spool of 1.1 it grazed the rim by two hundredths, stopped there at 1.1 and was
        /// "too small". Over this one it clears by 0.18, goes on to the outline and lies there at 10.7.
        /// </summary>
        public const float SpoolRadius = 0.4f, SpoolHeight = 0.9f;
        /// <summary>
        /// Where the player starts: one step from the spool (1.37 from the feather, as in LEVELS' solution:
        /// it looks 0.73 big), the feather under the crosshair, the painted outline on the blotter to its
        /// left, the canyon and the way out beyond it. (LEVELS starts 4.5 away; a click from there takes
        /// the feather looking a third as big, and nothing the balcony overlooks is far enough to make
        /// that fly.)
        /// </summary>
        public static readonly Vector3 Spawn = new Vector3(9.37f, BalconyTop, 2.06f);
        /// <summary>
        /// Where the solver lets go: the mark at the balcony's edge over the blotter. (LEVELS: (7.6, 4). Half
        /// a unit nearer the start along the edge the mark is in plain view beside the spool from the start.)
        /// </summary>
        public static readonly Vector3 EdgeSpot = new Vector3(DeckMaxX + MarkRadius + 0.08f, BalconyTop, 3.5f);
        /// <summary>Between the two: round the spool, not through it.</summary>
        public static readonly Vector3 RoundTheSpool = new Vector3(8.1f, BalconyTop, 2.6f);
        /// <summary>
        /// What it aims at: the middle of the painted outline, at the height of a feather lying there.
        /// (LEVELS: z = 12. From the edge mark as it stands that gives 9.5; here it gives the 9.2 the
        /// outline is painted at.)
        /// </summary>
        public static readonly Vector3 Aim = new Vector3(-1f, 0.15f, 11.6f);
        public static readonly Vector3 Landing = new Vector3(-1f, 0f, 49f);
        public static readonly Vector3 ExitCentre = new Vector3(-1f, 1.5f, 55f);
        public static readonly Vector3 ExitSize = new Vector3(4f, 3f, 3f);
        /// <summary>Where a fall sends the player once they have come down from the balcony: the apron, facing the blotter.</summary>
        public static readonly Vector3 DeckRespawn = new Vector3(9.5f, 0f, 14f);
        /// <summary>And once they are across.</summary>
        public static readonly Vector3 FarRespawn = new Vector3(4.5f, 0f, 46f);

        /// <summary>The view at the start: toward the spool, a step away.</summary>
        public static readonly float SpawnYaw = Mathf.Atan2(SpoolBase.x - Spawn.x, SpoolBase.z - Spawn.z) * Mathf.Rad2Deg;
        /// <summary>The view the solver lets go with: from the edge toward the outline.</summary>
        public static readonly float AimYaw = Mathf.Atan2(Aim.x - EdgeSpot.x, Aim.z - EdgeSpot.z) * Mathf.Rad2Deg;

        /// <summary>
        /// Apparent size (scale / distance) below which the feather, held over the painted outline from the
        /// balcony's edge, comes out too small to fly. Measured: the outline is 12.8 from the eye there, so
        /// 6.5 takes 0.506; a pick-up from 1.9 away (0.524) lands at 6.7 and flies, one from 2.2 (0.453) at
        /// 5.8 and does not. From the start it is 0.73.
        /// </summary>
        public const float FarRatio = 0.52f;

        /// <summary>Said whenever the feather is picked up looking too small (the words of Levels 1, 2 and 4).</summary>
        public const string FarLine = "It only ever gets as big as it looks. Pick it up from closer.";
        /// <summary>Said when the feather cupped, lifted and flopped back under its rider.</summary>
        public const string StallLine = "Too small to carry you. Pick it up and let go of it farther off: far means big.";
        /// <summary>Said when a feather too small to lie still was blown off the deck and has come back.</summary>
        public const string BlownLine = "Too small: the wind took it. It is back on its spool.";
        /// <summary>
        /// Said when a feather too small to moor was left lying anywhere but in the wind and has been put
        /// back (let go at one's feet, or onto the deck right under the balcony's edge).
        /// </summary>
        public const string SmallLine = "Too small: it is back on its spool. Let go of it farther off: far means big.";
        /// <summary>
        /// Said when the feather went down the canyon: let go over it (held out toward the far box it stops
        /// growing at its biggest half way there), or left by a rider who went over its side.
        /// </summary>
        public const string CanyonLine = "It fell into the canyon. It is back on its spool.";
        /// <summary>Said when it came to lie on the far box with the player still on this side.</summary>
        public const string FarBoxLine = "It lay out of reach on the far box. It is back on its spool.";
        /// <summary>Said when the feather has gone back to its spool for any other reason.</summary>
        public const string SpoolLine = "The feather is back on its spool.";
        /// <summary>
        /// Said when the held feather stopped on its own spool and was let go there (from behind the spool, or
        /// from the start mark toward the part of the blotter that lies behind it): it is put back as it was,
        /// which looks as if nothing had happened.
        /// </summary>
        public const string SpoolWayLine = "Its spool was in the way. Let go of it from the mark at the balcony edge.";
        /// <summary>Said when a feather big enough to moor has come to rest outside the stream.</summary>
        public const string CalmLine = "It lies out of the wind. Lay it on the blotter, in the stream.";
        /// <summary>
        /// Said when it has come to lie on the balcony: let go from too far back, or held toward something
        /// across the balcony (the apron, the ruler, the blocks) from the edge mark itself - so the line
        /// cannot be "stand at the edge": it says where the feather has to be let go, too.
        /// </summary>
        public const string BalconyLine = "It landed on the balcony. Let go of it from the edge, out over the blotter.";
        /// <summary>Said when it rests in the stream but leans on something.</summary>
        public const string FlatLine = "It has to lie flat. Pick it up and lay it on the blotter again.";

        Prop feather;
        WindStream wind;
        SailRaft raft;
        PropLeash leash;
        Exit exit;
        GameObject fan;
        Vector3 home;
        string lastLine;
        int lastSaid, restTicks, blownTick;
        bool judged;
        Vector3 lastSeen;
        float lastScale;

        public Prop Feather => feather;
        public WindStream Wind => wind;
        public SailRaft Raft => raft;
        /// <summary>Brings a feather that is too small to moor, or out of reach, back to the spool.</summary>
        public PropLeash Leash => leash;
        public Exit Exit => exit;
        public GameObject Fan => fan;
        /// <summary>Where the feather lies on its spool.</summary>
        public Vector3 FeatherHome => home;

        public override string Blurb => "The wind only carries what it can catch.";

        public override string[] Hints => new[]
        {
            "Let go of the little feather in the wind and watch where it goes. Now imagine standing on it.",
            "A feather has to be much wider than you before the wind can lift you both. From up here the blotter is a long way down, and far means big.",
            // LEVELS.md: "From the balcony edge, aim the feather at the middle of the blotter and let go."
            // As built the outline is the target (the middle of the blotter is nearer and gives 7), and the
            // pick-up matters, so the hint says where to take it from. It fills the pause card's hint panel
            // to its last line (five of five: Level05Tests.TheHints_FitThePauseCardsPanel) - not a word more.
            "Pick the feather up from the mark beside its spool. From the mark at the balcony edge, cover the painted feather on the blotter with it and let go. Walk down the ruler, stand on the quill, and hold on.",
        };

        public override string Environment => "sunny-rug";
        // The boxes stand on the rug; the canyon between them goes all the way down to it, and the kill
        // plane lies 2 above, so nothing is seen to hit the ground.
        public override float GroundY => RugY;
        public override float KillY => -13f;

        public override void Build(LevelContext ctx)
        {
            feather = null;
            wind = null;
            raft = null;
            leash = null;
            exit = null;
            fan = null;
            lastLine = null;
            lastSaid = -100000;
            restTicks = 0;
            blownTick = -100000;
            judged = true;

            // What is only drawn is collected in three sets and merged per material when the level is built
            // (level statics are a draw per material): the boxes in the room's tones, nobody's toys in wood,
            // and paint, which casts no shadow.
            Dip dip = ctx.Dip;
            var boxes = new Level01Set();
            var blocks = new Level01Set();
            var paint = new Level01Set();
            BuildBoxes(ctx, dip, boxes, blocks);
            BuildFan(ctx, dip, boxes);
            BuildPaint(ctx, dip, paint);
            BuildDressing(ctx, blocks);
            // The pedestal: a spool standing on the balcony.
            Piece(ctx, blocks, ToyFactory.ThreadSpool(SpoolRadius, SpoolHeight, grabbable: false), SpoolBase + Vector3.up * (SpoolHeight * 0.5f), Quaternion.identity);
            boxes.Finish(ctx, "Boxes");
            blocks.Finish(ctx, "Blocks");
            paint.Finish(ctx, "Paint", castShadows: false);

            // The feather on its spool, the quill turned so that, taken from the start and let go from the
            // edge mark, it lies along the wind.
            ToyDef def = ToyCatalog.Get(ToyId.Feather);
            home = SpoolBase + Vector3.up * (SpoolHeight + def.RestHeight * StartScale + 0.005f);
            lastSeen = home;
            lastScale = StartScale;
            feather = ToyCatalog.Add(ctx, ToyId.Feather, home, Quaternion.Euler(0f, SpawnYaw - AimYaw, 0f), StartScale, null, o =>
            {
                o.MinScale = MinScale;
                o.MaxScale = MaxScale;
                o.Tags = new[] { SailTag };
                o.GrabPose = GrabPose.Upright;
                // A sail lies flat: stood on its edge the feather would be neither a raft nor anything else.
                o.AllowPitch = false;
            });

            // Every tick, before the gadgets: where the feather is, and a word about a try that failed.
            ctx.OnUpdate(dt => Watch(ctx));
            ctx.OnUpdate(dt => Lee(ctx.Game.Player));

            // The gale. A feather big enough to be moored is too heavy for it; a smaller one is slid along
            // the blotter and over the edge. (The gadget's default limit of 1 would also push a feather of
            // 2.5 to 5.7 about while the raft waits for it to lie still.)
            float moorMass = def.Mass * MoorAbove * MoorAbove * MoorAbove;
            wind = new WindStream(ctx, new WindStreamOptions
            {
                Name = "Gale",
                Center = new Vector3((StreamMinX + StreamMaxX) * 0.5f, (StreamFloor + StreamTop) * 0.5f, (BackZ + StreamEnd) * 0.5f),
                Size = new Vector3(StreamMaxX - StreamMinX, StreamTop - StreamFloor, StreamEnd - BackZ),
                Direction = Vector3.forward, PlayerAirPush = WindPush, PropDrag = 12f, MaxPropMass = moorMass * 0.999f,
            });
            raft = new SailRaft(ctx, new SailRaftOptions
            {
                Name = "Feather", Prop = feather, Stream = wind,
                Launch = Zone.MinMax(new Vector3(StreamMinX - LaunchMargin, 0f, LaunchBack - LaunchMargin), new Vector3(StreamMaxX + LaunchMargin, LaunchTop, LaunchFront)),
                Area = ToyFactory.FeatherArea, LiftPressure = 7.78f, MoorAbove = MoorAbove, BoardDelay = 0.6f,
                CruiseSpeed = 6f, CruiseHeight = 1.2f, Landing = Landing, Settle = PressDepth,
            });
            raft.SailStalled += share => Say(ctx, StallLine, 6f);
            raft.SailMoored += () => judged = true;

            // No strand: a feather too small to moor goes back to its spool wherever it comes to rest (on a
            // floor it could only be picked up from above, looking a crumb), and so does one that lies
            // across the canyon while the player does not.
            leash = new PropLeash(ctx, new PropLeashOptions
            {
                Name = "Spool", Props = new[] { feather },
                Forbidden = new[] { Zone.MinMax(new Vector3(-60f, -60f, -60f), new Vector3(60f, 60f, 120f)) },
                Unless = () => !OutOfPlace(ctx.Game.Player), Grace = 1.5f, RestSpeed = 0.5f, Action = LeashAction.Respawn,
            });

            // Taken from far off the feather looks small, and it only ever gets as big as it looks.
            ctx.Game.Events.PropGrabbed += e =>
            {
                if (e.Prop != feather) return;
                judged = true;
                restTicks = 0;
                if (!Across(ctx.Game.Player) && e.OldScale < FarRatio * e.GrabDistance) Say(ctx, FarLine, 6f);
            };
            ctx.Game.Events.PropDropped += e =>
            {
                if (e.Prop != feather) return;
                judged = false;
                restTicks = 0;
            };
            // Back on the spool: say so, and why - the wind took it, it went down the canyon, it was too
            // small to be left lying, or it lay across the canyon without the player.
            ctx.Game.Events.PropRespawned += e =>
            {
                if (e.Prop != feather) return;
                judged = true;
                bool blown = ctx.Ticks - blownTick < 300;
                blownTick = -100000;
                Vector3 from = lastSeen;
                float size = lastScale;
                lastSeen = home;
                lastScale = StartScale;
                if ((from - home).sqrMagnitude < 1f)
                {
                    // It lay on its own spool at another size: the spool had stopped it there.
                    if (Mathf.Abs(size - StartScale) > 0.005f && !Across(ctx.Game.Player)) Say(ctx, SpoolWayLine, 6f);
                    return;
                }
                string line = blown ? BlownLine
                    : from.y < CanyonDepth ? CanyonLine
                    : size < MoorAbove ? SmallLine
                    : from.z > CanyonFar - 1f ? FarBoxLine
                    : SpoolLine;
                Say(ctx, line, 6f);
            };

            ctx.SetSpawn(Spawn, SpawnYaw, SpawnPitch(def));
            // A fall sends the player back to the floor they were last on.
            float spawnPitch = SpawnPitch(def);
            Checkpoint(ctx, "Balcony", new Vector3(11.5f, BalconyTop + 1.35f, 2f), new Vector3(9f, 2.5f, 16f), Spawn, SpawnYaw, spawnPitch);
            Checkpoint(ctx, "Deck", new Vector3(3f, 0.8f, 6f), new Vector3(24f, 1.6f, 24f), DeckRespawn, -90f, -8f);
            Checkpoint(ctx, "Far Deck", new Vector3(-1f, 1.5f, 50.5f), new Vector3(16f, 3f, 15f), FarRespawn, 0f, 0f);
            exit = ctx.AddExit(ExitCentre, ExitSize);
        }

        /// <summary>The pitch that puts the crosshair on the feather from the start.</summary>
        static float SpawnPitch(ToyDef def)
        {
            Vector3 to = SpoolBase + Vector3.up * (SpoolHeight + def.RestHeight * StartScale) - (Spawn + Vector3.up * Player.BaseEyeHeight);
            return Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
        }

        static void Checkpoint(LevelContext ctx, string name, Vector3 centre, Vector3 size, Vector3 respawn, float yaw, float pitch)
        {
            Trigger trigger = ctx.AddTrigger(name, Volume.Box(size), centre);
            trigger.SensesProps = false;
            trigger.OnEnter += e => ctx.Game.Player.SetCheckpoint(respawn, yaw, pitch);
        }

        // ---- What the level watches ------------------------------------------------------------------------

        /// <summary>The player has crossed: nothing is brought back or told off any more.</summary>
        static bool Across(Player player) => player.Position.z > CanyonFar - 1f && player.Position.y > -1f;

        // A feather the player cannot use where it lies: too small to moor (and not on its spool as it
        // started), or on the far box without them. A little feather put down on the spool itself at
        // another size (let go from behind the spool, the spool catches it at 0.7 to 1.2) counts too: put
        // back, it is the size the start mark was measured for - at 0.7 a pick-up from that mark looks too
        // small and is told "from closer", by a player who stands as close as the level asks.
        bool OutOfPlace(Player player)
        {
            if (feather == null || feather.Removed) return false;
            Vector3 centre = feather.Center;
            bool onTheSpool = (centre - home).sqrMagnitude < 0.3f * 0.3f;
            if (onTheSpool && Mathf.Abs(feather.Scale - StartScale) < 0.005f) return false;
            if (Across(player)) return false;
            return feather.Scale < MoorAbove || (!onTheSpool && centre.z > CanyonFar - 1f);
        }

        // The feather under way travels with the wind, so whoever rides it is in still air: a rider who hops
        // comes down where they took off. Without this the gale pushes them forward along the feather while
        // they are in the air - 0.7 with one hop, two units with two in a row, off its tip with four.
        // (It takes back exactly what the stream is about to give: the same test, the same tick.)
        void Lee(Player player)
        {
            if (raft == null || raft.State != SailState.Glide || player.Grounded || !raft.RiderOverhead) return;
            if (!wind.Contains(player.Position + Vector3.up * (player.Height * 0.5f))) return;
            player.AddPush(-wind.Direction * (WindPush * wind.Strength));
        }

        void Watch(LevelContext ctx)
        {
            if (feather == null || feather.Removed) return;
            if (feather.Held)
            {
                restTicks = 0;
                return;
            }
            Vector3 centre = feather.Center;
            lastSeen = centre;
            lastScale = feather.Scale;
            if (feather.Driven)
            {
                // Moored or under way: the raft has it.
                restTicks = 0;
                judged = true;
                return;
            }
            Vector3 velocity = feather.Velocity;
            // Sliding along the blotter before the wind, too small to lie still.
            if (feather.Scale < MoorAbove && velocity.sqrMagnitude > 1f && wind.Contains(centre + Vector3.up * 0.2f)) blownTick = ctx.Ticks;

            if (judged) return;
            bool still = velocity.sqrMagnitude < 0.04f && feather.Body.angularVelocity.sqrMagnitude < 0.04f;
            restTicks = still ? restTicks + 1 : 0;
            if (restTicks < 60) return;
            judged = true;
            string line = Judge(ctx.Game.Player, centre);
            if (line != null) Say(ctx, line, 6f);
        }

        // Why the feather that lies there is not a raft (null: nothing to say - it is on its way back to
        // the spool, or the player has crossed).
        string Judge(Player player, Vector3 centre)
        {
            if (feather.Scale < MoorAbove || Across(player) || centre.z > CanyonNear) return null;
            bool inStream = centre.x > StreamMinX - LaunchMargin && centre.x < StreamMaxX + LaunchMargin && centre.z < LaunchFront && centre.y < LaunchTop;
            if (inStream) return FlatLine;
            if (centre.x > DeckMaxX - 0.5f && centre.z < BalconyFront + 0.5f && centre.y > BalconyTop - 1.5f) return BalconyLine;
            return CalmLine;
        }

        // One line, and not the same one over and over.
        void Say(LevelContext ctx, string text, float seconds)
        {
            if (text == lastLine && ctx.Ticks - lastSaid < 360) return;
            lastLine = text;
            lastSaid = ctx.Ticks;
            ctx.Say(text, seconds);
        }

        // ---- Geometry --------------------------------------------------------------------------------------

        // A collider and nothing to see.
        static void Solid(LevelContext ctx, string name, Vector3 min, Vector3 max)
        {
            var solid = new GameObject(name) { layer = Layers.Default };
            solid.AddComponent<BoxCollider>().size = max - min;
            ctx.AddStatic(solid, (min + max) * 0.5f);
        }

        static void Tile(Level01Set set, Material material, float x0, float x1, float y0, float y1, float z0, float z1, float tone = 1f) =>
            set.Box(material, new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, y1 - y0, z1 - z0), tone);

        const float LidDepth = 2.6f, LidLip = 0.18f, TileDepth = 0.06f;

        // The two board-game boxes, the block of notes that is the balcony, the ruler down from it, and
        // the walls: colliders up to the sky cap all round, of which only a low row of building blocks on
        // each rim is drawn - the sun comes from the left, and a wall twelve high there would lay its
        // shadow over the whole blotter, the feather's own shadow with it.
        static void BuildBoxes(LevelContext ctx, Dip dip, Level01Set set, Level01Set blocks)
        {
            float ax0 = DeckMinX - Rim, ax1 = RightX + Rim, az0 = BackZ - Rim, az1 = CanyonNear;
            float cx0 = DeckMinX - Rim, cx1 = DeckMaxX + Rim, cz0 = CanyonFar, cz1 = EndZ + Rim;

            Solid(ctx, "Box A", new Vector3(ax0, RugY, az0), new Vector3(ax1, 0f, az1));
            Solid(ctx, "Box C", new Vector3(cx0, RugY, cz0), new Vector3(cx1, 0f, cz1));
            Solid(ctx, "Balcony", new Vector3(DeckMaxX, 0f, az0), new Vector3(ax1, BalconyTop, BalconyFront));

            // Walls (LEVELS: X = -9 and X = 15, X = 7 beside deck C, Z = 58; here also the back, Z = -6).
            Solid(ctx, "Wall -X", new Vector3(ax0, RugY, az0), new Vector3(DeckMinX, WallTop, cz1));
            Solid(ctx, "Wall Back", new Vector3(DeckMinX, 0f, az0), new Vector3(RightX, WallTop, BackZ));
            Solid(ctx, "Wall +X", new Vector3(RightX, 0f, az0), new Vector3(ax1, WallTop, CanyonNear));
            Solid(ctx, "Wall +X Canyon", new Vector3(RightX, RugY, CanyonNear), new Vector3(ax1, WallTop, CanyonFar + Rim));
            Solid(ctx, "Wall Canyon End", new Vector3(cx1, RugY, CanyonFar), new Vector3(RightX, WallTop, CanyonFar + Rim));
            Solid(ctx, "Wall +X Far", new Vector3(DeckMaxX, 0f, CanyonFar), new Vector3(cx1, WallTop, cz1));
            Solid(ctx, "Wall End", new Vector3(DeckMinX, 0f, EndZ), new Vector3(DeckMaxX, WallTop, cz1));
            SkyCap.Add(ctx, ax0, ax1, az0, cz1, WallTop);

            Material body = Materials.Room(RoomRecipe.For(RoomSurface.LevelStatic, dip, new PatternSpec(RoomPattern.Stripes, 3f, 0f, 0f, 0.05f)));
            Material lid = Materials.Room(RoomRecipe.For(RoomSurface.LevelStatic, dip, new PatternSpec(RoomPattern.Dots, 2.5f, 0.5f, 0f, 0.035f)));
            Material blotter = Materials.Room(RoomSurface.Furniture, dip);
            Material paper = Materials.Room(RoomSurface.Trim, dip);
            // Ruled paper.
            Material notes = Materials.Room(RoomRecipe.For(RoomSurface.LevelStatic, dip, new PatternSpec(RoomPattern.Planks, 1.2f, 80f, 0.05f, 0.05f)));

            // Box A: a body, a lid a little wider, and the lid's top in tiles - the blotter is the tile that
            // lies in the stream, flush with the rest, from the back wall to the canyon's edge.
            GameBox(set, body, lid, ax0, ax1, az0, az1);
            Tile(set, blotter, StreamMinX, StreamMaxX, -TileDepth, 0f, LaunchBack, LaunchFront);
            Tile(set, lid, ax0, StreamMinX, -TileDepth, 0f, az0, az1);
            Tile(set, lid, StreamMaxX, ax1, -TileDepth, 0f, az0, az1);
            Tile(set, lid, StreamMinX, StreamMaxX, -TileDepth, 0f, az0, LaunchBack);
            // Its label, on the side that faces the canyon.
            Tile(set, paper, -4.5f, 2.5f, -1.9f, -0.7f, az1, az1 + LidLip + 0.03f);

            // Box C.
            GameBox(set, body, lid, cx0, cx1, cz0, cz1);
            Tile(set, lid, cx0, cx1, -TileDepth, 0f, cz0, cz1);
            Tile(set, paper, -4.5f, 2.5f, -1.9f, -0.7f, cz0 - LidLip - 0.03f, cz0);

            // The balcony: a block of notes lying on the first box, its glued back toward the wall.
            var balconyCentre = new Vector3((DeckMaxX + ax1) * 0.5f, BalconyTop * 0.5f, (az0 + BalconyFront) * 0.5f);
            var balconySize = new Vector3(ax1 - DeckMaxX, BalconyTop, BalconyFront - az0);
            set.Book(notes, paper, balconyCentre, balconySize, Level01Set.Side.PosX, Quaternion.identity, 1f, notes);

            // The way down: a ruler leaning from the balcony's front edge to the apron, 26.6 degrees, on a
            // wedge that fills the space under it.
            const float thickness = 0.2f;
            float run = RampEnd - BalconyFront, slope = Mathf.Atan2(BalconyTop, run) * Mathf.Rad2Deg;
            float under = thickness / Mathf.Cos(slope * Mathf.Deg2Rad);
            float wedgeHeight = BalconyTop - under, wedgeLength = wedgeHeight * run / BalconyTop;
            float rampX = (RampMinX + RightX) * 0.5f;
            Piece(ctx, set, BasicToys.Ramp(wedgeLength, wedgeHeight, RightX - RampMinX - 0.3f, blotter),
                new Vector3(rampX + 0.05f, wedgeHeight * 0.5f, BalconyFront + wedgeLength * 0.5f), Quaternion.Euler(0f, 180f, 0f));
            Quaternion lean = Quaternion.Euler(slope, 0f, 0f);
            float length = Mathf.Sqrt(run * run + BalconyTop * BalconyTop) + 0.6f;
            var topEdge = new Vector3(rampX, BalconyTop, BalconyFront);
            // Turned end for end, so that its tick marks are on the open side.
            Piece(ctx, blocks, ToyFactory.Ruler(new Vector3(RightX - RampMinX, thickness, length), grabbable: false),
                topEdge + lean * new Vector3(0f, -thickness * 0.5f, length * 0.5f), lean * Quaternion.Euler(0f, 180f, 0f));

            BuildFences(ctx, blocks);
        }

        // A board-game box standing on the rug: the body, and a lid that reaches a lip's width further out.
        // The lid's top is laid by the caller, in tiles.
        static void GameBox(Level01Set set, Material body, Material lid, float x0, float x1, float z0, float z1)
        {
            Tile(set, body, x0, x1, RugY, -LidDepth, z0, z1);
            Tile(set, lid, x0 - LidLip, x1 + LidLip, -LidDepth, -TileDepth, z0 - LidLip, z1 + LidLip);
        }

        // Rows of building blocks on the rims of the boxes and of the balcony: what is drawn of the walls.
        // They stand inside the walls' colliders, their inner faces in the walls' planes.
        static void BuildFences(LevelContext ctx, Level01Set set)
        {
            float ax0 = DeckMinX - Rim, az0 = BackZ - Rim, ax1 = RightX + Rim, cz1 = EndZ + Rim, cx1 = DeckMaxX + Rim;
            // Every block is taller than a jump is high (1.25) and than the player's eye: a row reads as a
            // wall, not as a step. Box A: the left rim (no taller than 2.45: the sun comes over it, and its
            // shadow stops short of the blotter), the back corners either side of the fan.
            BlockRow(ctx, set, ax0, DeckMinX, az0, CanyonNear, 0f, 1.9f, 0.55f, 0);
            BlockRow(ctx, set, DeckMinX, DeckMinX + 2.2f, az0, BackZ, 0f, 1.9f, 0.4f, 3);
            BlockRow(ctx, set, DeckMaxX - 1.8f, DeckMaxX, az0, BackZ, 0f, 1.9f, 0.4f, 1);
            // The balcony: its back and its right side.
            BlockRow(ctx, set, DeckMaxX, RightX, az0, BackZ, BalconyTop, 1.9f, 0.5f, 2);
            BlockRow(ctx, set, RightX, ax1, az0, BalconyFront, BalconyTop, 1.9f, 0.5f, 5);
            // Beside the ruler, stepping down with it.
            Block(ctx, set, RightX, ax1, 0f, 3.4f, BalconyFront, BalconyFront + 2f);
            Block(ctx, set, RightX, ax1, 0f, 2.8f, BalconyFront + 2.04f, BalconyFront + 4f, true);
            Block(ctx, set, RightX, ax1, 0f, 2.3f, BalconyFront + 4.04f, BalconyFront + 6f);
            Block(ctx, set, RightX, ax1, 0f, 2f, BalconyFront + 6.04f, CanyonNear, true);
            // Box C: left, right and the far end.
            BlockRow(ctx, set, ax0, DeckMinX, CanyonFar, cz1, 0f, 1.9f, 0.55f, 4);
            BlockRow(ctx, set, DeckMaxX, cx1, CanyonFar, EndZ, 0f, 1.9f, 0.55f, 1);
            BlockRow(ctx, set, DeckMinX, cx1, EndZ, cz1, 0f, 2f, 0.6f, 2);
        }

        static readonly float[] BlockLengths = { 2f, 1.5f, 2.5f, 1.5f, 2f, 1f, 2.5f };
        static readonly float[] BlockSteps = { 0f, 1f, 0.35f, 0.7f, 0f, 1f, 0.5f, 0.2f };

        // Blocks side by side along the longer side of a strip, standing on y, of mixed lengths and heights.
        static void BlockRow(LevelContext ctx, Level01Set set, float x0, float x1, float z0, float z1, float y, float height, float variation, int phase)
        {
            bool alongZ = z1 - z0 >= x1 - x0;
            float from = alongZ ? z0 : x0, to = alongZ ? z1 : x1;
            const float gap = 0.04f;
            for (int i = 0; from < to - 0.05f; i++)
            {
                float length = BlockLengths[(i + phase) % BlockLengths.Length];
                float end = Mathf.Min(to, from + length);
                // No sliver at the end of a row: the last block takes what is left.
                if (to - end < 0.9f) end = to;
                float tall = height + variation * BlockSteps[(i * 3 + phase) % BlockSteps.Length];
                // Every other one a quarter turn round, so that the row does not show the same face all along.
                bool turned = (i + phase) % 2 == 1;
                if (alongZ) Block(ctx, set, x0, x1, y, y + tall, from, end - gap, turned);
                else Block(ctx, set, from, end - gap, y, y + tall, z0, z1, turned);
                from = end;
            }
        }

        static void Block(LevelContext ctx, Level01Set set, float x0, float x1, float y0, float y1, float z0, float z1, bool turned = false)
        {
            var size = new Vector3(Round(x1 - x0), Round(y1 - y0), Round(z1 - z0));
            var centre = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
            if (turned) Piece(ctx, set, ToyFactory.WoodenBlock(new Vector3(size.z, size.y, size.x), grabbable: false), centre, Quaternion.Euler(0f, 90f, 0f));
            else Piece(ctx, set, ToyFactory.WoodenBlock(size, grabbable: false), centre, Quaternion.identity);
        }

        // Sizes to the hundredth, so that blocks of one size share one mesh.
        static float Round(float value) => Mathf.Round(value * 100f) / 100f;

        // A set piece: its colliders stand in the level, its looks join the set.
        static void Piece(LevelContext ctx, Level01Set set, GameObject piece, Vector3 centre, Quaternion rotation)
        {
            ctx.AddStatic(piece, centre, rotation);
            set.Absorb(piece);
        }

        // The machine: a desk fan (the toy catalog's own, as a static at scale 11) behind the first box,
        // on a stack of books that brings its guard level with the stream.
        void BuildFan(LevelContext ctx, Dip dip, Level01Set set)
        {
            Vector3 foot = FanHub - ToyFactory.DeskFanHub * FanScale;
            fan = ctx.AddStatic(ToyFactory.DeskFan(null, false), foot);
            fan.transform.localScale = Vector3.one * FanScale;

            // The books under it: looks only (nothing gets behind the back wall).
            Material cover = Materials.Room(RoomSurface.Furniture, dip);
            Material pages = Materials.Room(RoomSurface.Trim, dip);
            float half = ToyFactory.DeskFanSize.z * 0.5f * FanScale;
            float back = foot.z - half - 0.4f, front = BackZ - Rim - LidLip - 0.04f;
            float[] heights = { 4.4f, 3.7f, 4.2f };
            float[] wider = { 0.9f, 0.3f, 0.6f };
            float y = RugY;
            float top = foot.y;
            for (int i = 0; i < heights.Length; i++)
            {
                float height = i == heights.Length - 1 ? top - y : heights[i];
                var size = new Vector3(2f * (half + wider[i]), height, front - back);
                set.Book(cover, pages, new Vector3(foot.x + (i - 1) * 0.25f, y + height * 0.5f, (front + back) * 0.5f), size,
                    i % 2 == 0 ? Level01Set.Side.NegX : Level01Set.Side.PosX, Quaternion.identity, 1f - 0.05f * i);
                y += height;
            }
        }

        // ---- Paint (no colliders) ----------------------------------------------------------------------------

        /// <summary>Radius of the round mats that say where to stand.</summary>
        public const float MarkRadius = 0.62f;

        // The size language of the campaign: the feather's outline on the surface it comes to lie on, at the
        // size the intended drop gives, with the least size that flies dashed inside it. And where to
        // stand: a mat with a pair of shoe prints beside the spool and at the balcony's edge, in the room's
        // own tones - the toy's colour is kept for where the toy goes.
        static void BuildPaint(LevelContext ctx, Dip dip, Level01Set set)
        {
            Color hero = ToyCatalog.Get(ToyId.Feather).ColorIn(dip);
            Material fill = Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Light, hero, 0.2f)));
            Material line = Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Light, hero, 0.9f)));
            Material deep = Materials.Room(RoomRecipe.Solid(dip.Deep));
            Material print = Materials.Room(RoomRecipe.Solid(Palette.Paper));

            var paint = new GameObject("Painted Feather");
            ctx.AddStatic(paint, new Vector3(Aim.x, 0f, Aim.z));
            Quaternion flat = Quaternion.Euler(90f, 0f, 0f);
            Mesh patch = MeshKit.Cached(MeshKit.Key("Level05/Fill", IntendedScale), () => MeshKit.Extrude(Level05Shapes.FeatherOutline(IntendedScale), 0.004f));
            Paint(paint.transform, "Fill", patch, fill, new Vector3(0f, 0.008f, 0f), flat);
            Paint(paint.transform, "Outline", MeshKit.Cached(MeshKit.Key("Level05/Outline", IntendedScale),
                () => Level05Shapes.Dashed("Feather Outline", Level05Shapes.FeatherOutline(IntendedScale), true, 0.16f, 0.55f, 0.3f)), line, new Vector3(0f, 0.014f, 0f), Quaternion.identity);
            Paint(paint.transform, "Shaft", MeshKit.Cached(MeshKit.Key("Level05/Shaft", IntendedScale),
                () => Level05Shapes.Dashed("Feather Shaft", Level05Shapes.FeatherShaft(IntendedScale), false, 0.12f, 0.55f, 0.3f)), line, new Vector3(0f, 0.014f, 0f), Quaternion.identity);
            Paint(paint.transform, "Least", MeshKit.Cached(MeshKit.Key("Level05/Least", LeastScale),
                () => Level05Shapes.Dashed("Feather Least", Level05Shapes.FeatherOutline(LeastScale), true, 0.08f, 0.24f, 0.22f)), line, new Vector3(0f, 0.014f, 0f), Quaternion.identity);
            set.Absorb(paint);

            // The blotter's four corner holders.
            var corners = new GameObject("Blotter Corners");
            ctx.AddStatic(corners, new Vector3(0f, 0.008f, 0f));
            const float leg = 1.7f;
            Mesh corner = MeshKit.Cached(MeshKit.Key("Level05/Corner", leg), () => MeshKit.Extrude(
                new List<Vector2> { new Vector2(0f, 0f), new Vector2(leg, 0f), new Vector2(0f, leg) }, 0.004f));
            Paint(corners.transform, "Corner", corner, deep, new Vector3(StreamMinX, 0f, LaunchBack), flat);
            Paint(corners.transform, "Corner", corner, deep, new Vector3(StreamMaxX, 0f, LaunchBack), Quaternion.Euler(0f, -90f, 0f) * flat);
            Paint(corners.transform, "Corner", corner, deep, new Vector3(StreamMaxX, 0f, LaunchFront), Quaternion.Euler(0f, 180f, 0f) * flat);
            Paint(corners.transform, "Corner", corner, deep, new Vector3(StreamMinX, 0f, LaunchFront), Quaternion.Euler(0f, 90f, 0f) * flat);
            set.Absorb(corners);

            StandMark(ctx, set, "Stand Mark Spool", Spawn, SpawnYaw, deep, print);
            StandMark(ctx, set, "Stand Mark Edge", EdgeSpot, AimYaw, deep, print);
        }

        // A coat of paint where its parent stands; the set takes it over.
        static void Paint(Transform parent, string name, Mesh mesh, Material material, Vector3 localPosition, Quaternion localRotation) =>
            GadgetKit.Visual(parent, name, mesh, material, localPosition, localRotation);

        // A round mat with a pair of shoe prints, toes toward the given yaw.
        static void StandMark(LevelContext ctx, Level01Set set, string name, Vector3 at, float yaw, Material pad, Material print)
        {
            var mark = new GameObject(name);
            ctx.AddStatic(mark, at + Vector3.up * 0.012f, Quaternion.Euler(0f, yaw, 0f));
            Mesh disc = MeshKit.Cached(MeshKit.Key("Level05/Pad", MarkRadius), () => MeshKit.Cylinder(MarkRadius, 0.004f, 40));
            Paint(mark.transform, "Pad", disc, pad, Vector3.zero, Quaternion.identity);
            Mesh sole = MeshKit.Cached(MeshKit.Key("Level05/Sole"), () =>
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
                Paint(mark.transform, "Shoe", sole, print, new Vector3(side * 0.24f, 0.006f, 0f), Quaternion.Euler(0f, side * 9f, 0f) * Quaternion.Euler(90f, 0f, 0f));
            set.Absorb(mark);
        }

        // ---- Set dressing ------------------------------------------------------------------------------------

        // Nobody's toys (plain props: no candy, no rim, no pool). On the balcony they stand behind the
        // start and along the right-hand blocks, clear of the ways from the spool to the edge and to the
        // ruler. Deck A is left bare: anything on it could end up under the feather. On the far box an
        // arch of blocks stands over the way out, beyond where the feather comes down.
        static void BuildDressing(LevelContext ctx, Level01Set set)
        {
            float top = BalconyTop;
            Piece(ctx, set, ToyFactory.WoodenBlock(1.5f, grabbable: false), new Vector3(13.9f, top + 0.75f, -4.6f), Quaternion.Euler(0f, 8f, 0f));
            Piece(ctx, set, ToyFactory.WoodenBlock(1f, grabbable: false), new Vector3(13.95f, top + 2f, -4.55f), Quaternion.Euler(0f, 31f, 0f));
            Piece(ctx, set, ToyFactory.WoodenBlock(new Vector3(1.6f, 0.8f, 0.8f), grabbable: false), new Vector3(12f, top + 0.4f, -5.2f), Quaternion.Euler(0f, -14f, 0f));
            Piece(ctx, set, ToyFactory.Marker(0.4f, 3.4f), new Vector3(9.3f, top + 0.4f, -5f), Quaternion.Euler(0f, 97f, 0f));
            Piece(ctx, set, ToyFactory.Domino(grabbable: false), new Vector3(14.3f, top + 1f, 6.4f), Quaternion.Euler(0f, -24f, 0f));
            Piece(ctx, set, ToyFactory.Domino(grabbable: false), new Vector3(13.8f, top + 0.15f, 4.6f), Quaternion.Euler(90f, 62f, 0f));
            Piece(ctx, set, ToyFactory.WoodenBlock(1.2f, grabbable: false), new Vector3(14.2f, top + 0.6f, 1.2f), Quaternion.Euler(0f, 14f, 0f));
            Piece(ctx, set, ToyFactory.WoodenBlock(0.8f, grabbable: false), new Vector3(14.25f, top + 1.6f, 1.15f), Quaternion.Euler(0f, -22f, 0f));

            // The arch over the way out.
            float archZ = EndZ - 0.55f;
            Piece(ctx, set, ToyFactory.WoodenBlock(new Vector3(1f, 3.4f, 1f), grabbable: false), new Vector3(ExitCentre.x - 2.7f, 1.7f, archZ), Quaternion.identity);
            Piece(ctx, set, ToyFactory.WoodenBlock(new Vector3(1f, 3.4f, 1f), grabbable: false), new Vector3(ExitCentre.x + 2.7f, 1.7f, archZ), Quaternion.identity);
            Piece(ctx, set, ToyFactory.WoodenBlock(new Vector3(6.8f, 0.9f, 1f), grabbable: false), new Vector3(ExitCentre.x, 3.85f, archZ), Quaternion.identity);
            // And a few left about in its corners.
            Piece(ctx, set, ToyFactory.WoodenBlock(1.4f, grabbable: false), new Vector3(5.6f, 0.7f, 56.6f), Quaternion.Euler(0f, 12f, 0f));
            Piece(ctx, set, ToyFactory.WoodenBlock(0.9f, grabbable: false), new Vector3(5.65f, 1.85f, 56.55f), Quaternion.Euler(0f, -20f, 0f));
            Piece(ctx, set, ToyFactory.Domino(grabbable: false), new Vector3(-7.6f, 1f, 56.9f), Quaternion.Euler(0f, 18f, 0f));
        }

        // ---- The intended solution -------------------------------------------------------------------------

        public override IEnumerator Solve(Bot bot)
        {
            // The feather is a step away, under the crosshair: a look and a click.
            yield return Take(bot);
            // To the edge mark, cover the painted feather on the blotter for a beat, and let go.
            yield return Place(bot, EdgeSpot, Aim, 0.5f);
            yield return bot.Until(() => raft.State == SailState.Moored, 6f);
            yield return bot.Wait(0.4f);
            yield return Descend(bot);
            yield return Ride(bot);
        }

        /// <summary>Picks the feather up from where the bot stands, square on.</summary>
        public IEnumerator Take(Bot bot)
        {
            yield return bot.LookAt(feather);
            yield return bot.Grab(feather);
        }

        /// <summary>
        /// Carries the feather round the spool to a spot on the balcony, turns the view so that its ray
        /// passes through <paramref name="aim"/>, keeps it there for <paramref name="holdSeconds"/> and lets go.
        /// </summary>
        public IEnumerator Place(Bot bot, Vector3 from, Vector3 aim, float holdSeconds = 0f)
        {
            // From the start: round the spool, not through it.
            if ((bot.Player.Position - Spawn).sqrMagnitude < 0.5f * 0.5f) yield return bot.WalkTo(RoundTheSpool, 0.3f);
            yield return bot.WalkTo(from, 0.15f);
            if (holdSeconds > 0f)
            {
                yield return bot.LookAt(aim);
                yield return bot.Wait(holdSeconds);
            }
            yield return bot.DropAt(aim);
        }

        /// <summary>From the balcony: down the ruler and across the apron to the edge of the blotter.</summary>
        public IEnumerator Descend(Bot bot)
        {
            float rampX = (RampMinX + RightX) * 0.5f;
            yield return bot.WalkTo(new Vector3(rampX, BalconyTop, BalconyFront - 0.5f));
            yield return bot.WalkTo(new Vector3(rampX, 0f, RampEnd + 0.6f));
            yield return bot.WalkTo(new Vector3(6f, 0f, 15f));
        }

        /// <summary>
        /// Onto the feather wherever it lies, toward its quill, and no farther than it takes to stand well
        /// on it (it leaves 0.6 s after the first step); then stand still until it has come down on the far
        /// box, and walk out.
        /// </summary>
        public IEnumerator Ride(Bot bot)
        {
            Game game = bot.Game;
            yield return Board(bot);
            yield return bot.LookAt(ExitCentre, 3f);
            yield return bot.Until(() => raft.State == SailState.Docked || game.LevelCompleted, 20f);
            if (!game.LevelCompleted) yield return bot.WalkTo(new Vector3(ExitCentre.x, 0f, ExitCentre.z), 0.5f, 10f);
            yield return bot.Until(() => game.LevelCompleted, 3f);
        }

        /// <summary>Walks onto the feather and stops once it stands well on it, or it has left the deck.</summary>
        public IEnumerator Board(Bot bot)
        {
            int aboard = 0;
            IEnumerator walk = bot.WalkTo(feather.Center, 0.4f, 12f);
            while (walk.MoveNext())
            {
                aboard = raft.RiderAboard ? aboard + 1 : 0;
                if (aboard >= 15 || raft.State == SailState.Glide || raft.State == SailState.Stall) break;
                yield return walk.Current;
            }
        }
    }
}
