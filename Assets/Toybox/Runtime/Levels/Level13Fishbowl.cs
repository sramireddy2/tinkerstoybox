using System.Collections;
using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;
using UnityEngine.Rendering;
using SpongeGadget = Toybox.Gadgets.Sponge;

namespace Toybox.Levels
{
    /// <summary>
    /// Level 13, "Escaping the Fishbowl" (LEVELS.md): size is capacity - a toy carries a quantity through
    /// a resize.
    ///
    /// The player starts on the plastic castle in a goldfish bowl: water all round, too deep to wade, glass
    /// all round, and on the far side a glass filter tower that reaches the rim, with a stack of books
    /// behind it. The one toy is a bath sponge. A sponge holds 0.48 of water per unit of its scale cubed:
    /// held out over the bowl from the castle's edge it is the size of a house and drinks the whole of the
    /// bowl in two seconds. (What a smaller one leaves can be waded, up to 1.1 deep.) Carried into the tower and let go at the player's
    /// feet it is a brick again and has to put 400 units of water somewhere: the tower's flap shuts, the
    /// tower fills like a syringe, its cork floor rides the surface with the player on it, and at the top
    /// the way out is a step over the rim.
    ///
    /// Water is bookkeeping (WaterVolume): a footprint, a floor and a number, 405 units in all, conserved.
    /// Whatever leaves the player out of their depth carries them back to the castle and puts everything
    /// back as it was at the start (unless the sponge is in their hand): a clean retry, never a strand.
    ///
    /// (Where this differs from LEVELS.md, and what was measured: tools/out/notes/level13-build.md. What the
    /// review found and changed - the water's colour, the first picture, the doorway's guard, the short way
    /// down, what is said about a sponge that was turned; and, on a second look, how deep the bowl may be
    /// waded (1.1) and how much a sponge holds (0.48 per unit cubed: the sponge of the solution drinks the
    /// bowl dry, and one held at the middle of the dashed footprint, 6.6, is big enough - it takes 6.09),
    /// what the line about a sponge that is too small points at, and what is said and done when the cork
    /// goes up alone or stops short: tools/out/notes/level13-review.md.)
    /// </summary>
    [Level(13, "fishbowl", "Escaping the Fishbowl", Phase = 3)]
    public sealed class Level13Fishbowl : LevelDefinition
    {
        // ---- The layout (LEVELS.md, Level 13). Gravel y = 0, the bowl's axis is the Y axis, travel +Z. -------
        public const float BowlRadius = 10f, RimY = 8f, GlassThickness = 0.3f, SkyY = 12f;
        /// <summary>
        /// The den's floor, and the top of the bowl's foot, which is the tower's floor under the cork. (LEVELS
        /// puts the play plane at 0 and recesses the tower's floor to -0.4; the room's floor is a collider at
        /// the play plane, so the play plane has to lie below the recess. The gravel is still y = 0.)
        /// </summary>
        public const float DenFloorY = -0.6f, BedY = -0.4f;
        public const float IslandHalfWidth = 2f, IslandBack = -9.2f, IslandFront = -6f, IslandTop = 3f;
        public const float StairMinX = 2f, StairMaxX = 3.5f, StairBack = -9f, StairMiddle = -7.8f, StairFront = -6.6f;
        /// <summary>The tower's inside: x within the half width, z from its front wall's inner face to the bowl's glass.</summary>
        public const float TowerHalfWidth = 1.5f, TowerFront = 6.7f, TowerTop = 10.5f;
        public const float DoorHalfWidth = 1f, DoorHeight = 2.4f;
        /// <summary>
        /// Above the doorway the tower's front stands this far forward of it, from this height up: a bay. A
        /// sponge held toward the tower stops on the bay, and what comes down in front of it leaves the
        /// doorway free, with room under the bay to walk in from the side.
        /// </summary>
        public const float BayDepth = 1.3f, BayBottom = 2.6f;
        public static readonly Vector3 CorkSize = new Vector3(2.8f, 0.4f, 3f);
        public const float CorkZ = 8.35f;
        public const float LedgeHalfWidth = 3f, LedgeNear = 10.3f, LedgeFar = 15f;
        public static readonly Vector3 ExitCentre = new Vector3(0f, 9.25f, 13f);
        public static readonly Vector3 ExitSize = new Vector3(3f, 2.5f, 3f);

        // ---- The water -----------------------------------------------------------------------------------------
        public const float BowlArea = 270f, BowlWater = 405f;
        public const float TowerArea = 9f, TowerLip = 7.9f, LeakRate = 3f;
        /// <summary>
        /// How deep the player wades, and for how long they may be deeper before they are carried back.
        /// (LEVELS has 0.8, which makes the least sponge 8.44: from the castle's edge that is the tower and
        /// nothing else - the crosshair on the far row of the dashes gives 7.9 to 8.2, on the water in the
        /// middle of the bowl 7.4 to 7.8, a sponge turned 45 degrees or lying on its side stops at 7.8 to 8.2
        /// on the tower's bay. All of them a hair short. At 1.1 - and with a sponge that holds 0.48 per unit
        /// cubed, see CapacityPerScale3 - the least sponge is 6.09 and all of them work, and so does the
        /// crosshair on the middle of the dashed footprint, 6.6 to 6.8; held over the near third of the bowl it
        /// is still too small, under 6, and that is said.)
        /// </summary>
        public const float WadeDepth = 1.1f, SweepDelay = 0.3f;
        public const float CorkTop = 8f, CorkSpeed = 3f, Freeboard = 0.1f;
        public const float FlapSeconds = 0.4f;
        /// <summary>
        /// A cork whose top is this far below the rim can no longer be stepped off (a hop is 1.25), and what
        /// the tower then loses on top of its leak, in units a second, so that the way down is short.
        /// </summary>
        public const float StepOff = 1.3f, DrainRate = 21f;
        /// <summary>Seconds the tower holds its water after the sponge stops wringing itself out into it.</summary>
        public const float HoldSeconds = 3f;
        /// <summary>The dip the water is drawn in: pool blue, in a den that is peach all over.</summary>
        public static Dip WaterDip => Palette.Pool;

        // ---- The sponge ----------------------------------------------------------------------------------------
        public const float StartScale = 2f, MinScale = 0.5f, MaxScale = 12f;
        public const string SpongeTag = "sponge";
        /// <summary>
        /// What a sponge holds per unit of its scale cubed, and how fast it drinks and lets go. (LEVELS has
        /// 0.315 and 150 a second: the sponge the solution makes, 9.5, then holds 268 of the bowl's 405 and
        /// leaves it a third full - where LEVELS' own fantasy, and the brief, is a sponge that drinks the whole
        /// bowl. At 0.48 a sponge of 9.5 holds 411: the bowl is drunk dry, in the same two seconds. And the
        /// least sponge that will do is 6.09 instead of 7.0, so that the crosshair on the middle of the dashed
        /// footprint - 6.6 to 6.8, which was a hair short - is enough.)
        /// </summary>
        public const float CapacityPerScale3 = 0.48f, AbsorbRate = 200f, SqueezeRate = 24f;
        /// <summary>
        /// How fast the sponge lets go once the tower has filled and run over with nobody on the cork: what
        /// comes of that (the bowl too deep again, everything put back) is four seconds away, not fifteen.
        /// </summary>
        public const float LoneSqueezeRate = 96f;
        /// <summary>
        /// The size the dashed footprint on the gravel is painted for (the solver's sponge), and the least that
        /// leaves the bowl wadeable: 405 - 270 x 1.1 = 108 units have to go, and 0.48 x 6.09^3 is 108.4.
        /// </summary>
        public const float IntendedScale = 9.5f, LeastScale = 6.09f;
        /// <summary>
        /// The paint of the dashes: the sponge's colour, this much of it in Paper. (Nine tenths of it in the
        /// den's light peach, as it was, is the colour of the water it lies under.)
        /// </summary>
        public const float PaintTint = 0.55f;
        /// <summary>Where the sponge lies at the start: on the castle's front edge.</summary>
        public static readonly Vector3 SpongeHome = new Vector3(0f, IslandTop + 0.25f * StartScale, -6.9f);

        /// <summary>Where the player starts, and where the solver picks the sponge up from: a step behind it.</summary>
        public static readonly Vector3 Spawn = new Vector3(0f, IslandTop, -8.2f);
        /// <summary>
        /// The view at the start: the crosshair on the sponge's top (a click picks it up), and above it the
        /// water, the tower with its doorway, and the way out behind the tower's glass. (At -28 the way out
        /// was above the picture: it is 12.5 degrees up from here and the picture ends 35 above its middle.)
        /// </summary>
        public const float SpawnPitch = -19f;
        /// <summary>Where the solver lets go from: the shoe prints at the castle's front edge.</summary>
        public static readonly Vector3 EdgeSpot = new Vector3(0f, IslandTop, -6.4f);
        /// <summary>The middle of the sponge when it lies on the dashed footprint: what the solver holds it out at.</summary>
        public static readonly Vector3 AimPoint = new Vector3(0f, 2.4f, 1.2f);
        /// <summary>
        /// Where to stand on the cork - the shoe prints at its back, facing the doorway - and where the
        /// sponge goes: at the feet, between the prints and the doorway.
        /// </summary>
        public static readonly Vector3 CorkSpot = new Vector3(0f, 0f, 9.1f);
        public static readonly Vector3 CorkAim = new Vector3(0f, 0.4f, 7.8f);

        /// <summary>
        /// Apparent size (scale / pick-up distance) below which the sponge cannot be made big enough from the
        /// castle. From the start it is 1.2, from the castle's back corner 1.0. (Held toward the tower from the
        /// castle's edge it stops on the bay at 11.5 k / (0.97 + 0.35 k): 6.1 takes 0.63, and at 0.72 it is 6.8.)
        /// </summary>
        public const float FarRatio = 0.72f;
        /// <summary>
        /// A sponge that lies where it looks smaller than this from anywhere on the castle, while the bowl is
        /// too deep to wade, goes back to where it started: nobody can get nearer to it.
        /// </summary>
        public const float UselessRatio = 0.68f;

        /// <summary>Said on a pick-up from too far away. The words are Level 1's, on purpose.</summary>
        public const string FarLine = "It only ever gets as big as it looks. Pick it up from closer.";
        /// <summary>Said to somebody who went into the water before it was shallow enough.</summary>
        public const string DeepLine = "Too deep to wade. The water has to go somewhere first.";
        /// <summary>Said when the castle itself stopped the held sponge.</summary>
        public const string CastleLine = "It stopped on the castle. Step to the edge and hold it out over the water.";
        /// <summary>
        /// Said when the sponge has drunk all it can and the bowl is still too deep. (It names the tower,
        /// which is in plain view over the held sponge and where every aim works. It used to say "until it
        /// fills the dashes": held low the sponge hides them, and the crosshair on their far row was a hair
        /// short.)
        /// </summary>
        public const string ShortLine = "Not enough sponge for this much water. Pick it up again and hold it out farther, toward the glass tower.";
        /// <summary>
        /// Said instead when the sponge fell short because of how it was turned or flipped in the hand (Q or
        /// the wheel, F): on its side or on end it meets the gravel sooner, and turned 45 degrees a corner of
        /// it meets the tower's bay sooner. (With the bowl wadeable at 1.1 both still work when it is held
        /// toward the tower - 7.8 to 8.2; this is for the ones that were held low, or picked up from far off,
        /// as well.)
        /// </summary>
        public const string TurnedLine = "Turned like that it stops too soon. Lay it flat and square to the dashes, and hold it out again.";
        public const string CrumbLine = "From the castle that sponge looks too small to be of use. It is back where it started.";
        /// <summary>Said the moment a full sponge is let go smaller, anywhere but in the tower.</summary>
        public const string WringLine = "Made small, a full sponge lets its water go right where it lies. Pick it up before the bowl fills!";
        public const string RefillLine = "The sponge let its water go where it lay and the bowl filled up again. Everything is back at the start.";
        public const string TowerLine = "The tower filled and ran over without you. Be on the cork when you let go of the sponge.";
        public const string DoorLine = "The flap cannot shut with you in the doorway, so the water runs out. Step onto the cork.";
        /// <summary>Said when that went on until the bowl was too deep again.</summary>
        public const string SpillLine = "The flap could not shut with you in the doorway, and the bowl filled again. Back to the start.";
        /// <summary>Said to somebody who rode the cork up, stayed on it, and came down with it again.</summary>
        public const string LateLine = "The tower ran empty again before you stepped off at the top. Everything is back at the start.";
        /// <summary>
        /// Said instead to somebody whose cork never got to the top: the sponge had too little left in it (a
        /// sponge from the low end of the window that lost some on the way - under a flap that could not shut,
        /// or let go small out in the bowl first). The tower needs 71 units and what runs out while it fills.
        /// </summary>
        public const string LowLine = "The sponge ran dry before the cork got to the rim: it has to go in fuller. Back to the start.";
        public const string BackLine = "The sponge is back on the castle.";

        Prop sponge;
        SpongeGadget soak;
        /// <summary>The sponge's options: its squeeze rate is the one thing the level changes while it runs (see LoneSqueezeRate).</summary>
        SpongeOptions soakOptions;
        WaterVolume bowl, tower;
        FloatPlatform cork;
        Door flap;
        PropLeash leash;
        Exit exit;
        /// <summary>
        /// The doorway, shut to a sponge in the hand of somebody who stands in the tower: from in there it can
        /// only be let go in there. (Without it a sponge let go from the middle of the cork while looking out
        /// through the doorway came down in the doorway or in front of it, wrung itself out into the bowl with
        /// its holder standing on the cork, and lay under the flap.) The player walks through it; it is there
        /// only while the sponge is in their hand and they are inside.
        /// </summary>
        BoxCollider guard;
        /// <summary>The fish food on the water: it lies on the surface and goes down and up with it.</summary>
        Transform flakes;
        string lastLine;
        int lastSaid;
        /// <summary>Ticks the player has been out of their depth since they last stood somewhere dry.</summary>
        int wetTicks;
        /// <summary>Ticks the sponge has lain where it is of no use.</summary>
        int crumbTicks;
        /// <summary>Ticks the flap has waited for the player to leave the doorway.</summary>
        int doorTicks;
        /// <summary>The sponge has been let go and nothing has been said about where it lies yet.</summary>
        bool pending;
        int restTicks;
        /// <summary>The castle stopped the sponge when it was last let go.</summary>
        bool castleStop;
        /// <summary>The bowl has been shallow enough to wade since everything was last put back.</summary>
        bool drained;
        /// <summary>The sponge last wrung itself out into the tower (not into the bowl).</summary>
        bool wrungInTower;
        /// <summary>Since it was last picked up the sponge has wrung itself out into the bowl, and that has been said.</summary>
        bool wrungOut;
        bool doorSaid;
        /// <summary>The tick the sponge last wrung itself out into the tower while the flap could not shut.</summary>
        int spillTick;
        /// <summary>The tick the sponge last wrung itself out into the tower at all.</summary>
        int fedTick;
        /// <summary>The cork has been at the top with the player in the tower since it last lay at rest.</summary>
        bool rodeToTop;
        /// <summary>The cork went up without the player, and that has been said (while they watched it).</summary>
        bool loneSaid;

        public Prop Sponge => sponge;
        /// <summary>The sponge's behaviour: what it holds and what it does.</summary>
        public SpongeGadget Soak => soak;
        public WaterVolume Bowl => bowl;
        public WaterVolume Tower => tower;
        public FloatPlatform Cork => cork;
        public Door Flap => flap;
        /// <summary>Returns a sponge that has left the bowl.</summary>
        public PropLeash Leash => leash;
        public Exit Exit => exit;
        /// <summary>How many times the water has been put back as it was at the start.</summary>
        public int Resets { get; private set; }
        /// <summary>How many times a sponge of no use where it lay has been brought back.</summary>
        public int Returns { get; private set; }
        /// <summary>How many times the player has been carried back to the castle.</summary>
        public int Sweeps { get; private set; }

        /// <summary>All the water there is: in the bowl, in the tower and in the sponge.</summary>
        public float TotalWater => bowl.Volume + tower.Volume + soak.Stored;

        public override string Blurb => "Wind-up toys don't swim. Sponges do the opposite.";

        public override string[] Hints => new[]
        {
            "A sponge holds as much as its size. Make it big where the water is.",
            "A full sponge that suddenly gets small has to put its water somewhere: right where it lies. The glass tower is narrow, and its floor floats.",
            // Five lines of the pause card's hint panel at full size (Level13Tests.EveryHint_FitsThePauseCardsPanel_AtFullSize).
            "Pick the sponge up from right beside it. At the castle's edge, hold it out toward the glass tower and let go. Then stand on the cork in the tower, pick it up from there and let go at your feet.",
        };

        public override string Environment => "cardboard-box";
        public override float GroundY => DenFloorY;
        public override float KillY => -30f;

        /// <summary>What a sponge of this scale holds.</summary>
        public static float Capacity(float scale) => CapacityPerScale3 * scale * scale * scale;

        /// <summary>Is the point inside the tower (in plan)?</summary>
        public static bool InTower(Vector3 point) =>
            Mathf.Abs(point.x) <= TowerHalfWidth && point.z >= TowerFront && point.z <= BowlRadius + GlassThickness;

        /// <summary>Is somebody with their feet here up at the rim or out on the books (where only the cork takes them)?</summary>
        public static bool Above(Vector3 feet) => feet.y > RimY - 0.5f;

        public override void Build(LevelContext ctx)
        {
            sponge = null;
            soak = null;
            soakOptions = null;
            bowl = null;
            tower = null;
            cork = null;
            flap = null;
            leash = null;
            exit = null;
            guard = null;
            flakes = null;
            lastLine = null;
            lastSaid = -100000;
            wetTicks = 0;
            crumbTicks = 0;
            doorTicks = 0;
            pending = false;
            restTicks = 0;
            castleStop = false;
            drained = false;
            wrungInTower = false;
            wrungOut = false;
            doorSaid = false;
            spillTick = -100000;
            fedTick = -100000;
            rodeToTop = false;
            loneSaid = false;
            Resets = 0;
            Returns = 0;
            Sweeps = 0;

            Dip dip = ctx.Dip;
            BuildSolids(ctx);
            BuildBowl(ctx, dip);
            BuildCastle(ctx, dip);
            BuildTower(ctx, dip);
            BuildLedge(ctx, dip);
            BuildPaint(ctx, dip);
            BuildDressing(ctx, dip);

            // Gadgets in feed order: the bowl takes the tower's overflow; the cork reads the tower before the
            // sponge pours into it (so water that runs straight out under an open flap never lifts it).
            // (The bowl's footprint takes in the tower, and its volume knows nothing of flaps or hops: how deep
            // the player stands in the bowl is judged by the level itself, in Watch.)
            // (The water is drawn in the pool dip: in the den's own peach it was the colour of the room, the
            // gravel and the castle, and read as a sheet of glass. This level is about the water.)
            bowl = new WaterVolume(ctx, new WaterVolumeOptions
            {
                Name = "bowl", Footprint = Zone.Cylinder(0f, 0f, BowlRadius, 0f, RimY), FloorY = 0f, Area = BowlArea, Volume = BowlWater,
                WadeDepth = float.MaxValue, SweepDelay = SweepDelay, Dip = WaterDip,
            });
            tower = new WaterVolume(ctx, new WaterVolumeOptions
            {
                Name = "tower", Footprint = Zone.MinMax(new Vector3(-TowerHalfWidth, 0f, TowerFront), new Vector3(TowerHalfWidth, TowerTop, BowlRadius)),
                FloorY = 0f, Area = TowerArea, Volume = 0f, MaxSurfaceY = 0f, OverflowTo = bowl, LeakTo = bowl, LeakRate = LeakRate,
                WadeDepth = WadeDepth, SweepDelay = SweepDelay, OnSwept = () => Swept(ctx), Dip = WaterDip,
            });
            cork = new FloatPlatform(ctx, new FloatPlatformOptions
            {
                Name = "cork", Platform = BuildCork(dip), Position = new Vector2(0f, CorkZ), Volume = tower,
                RestY = 0f, MaxY = CorkTop, MaxSpeed = CorkSpeed, Freeboard = Freeboard, TopOffset = CorkSize.y * 0.5f,
            });
            // The flap: a sluice in the tower's front wall. Open it stands in the wall above the doorway.
            flap = new Door(ctx, new DoorOptions
            {
                Name = "flap", Size = new Vector3(DoorHalfWidth * 2f - 0.04f, DoorHeight, 0.2f),
                ClosedPosition = new Vector3(0f, DoorHeight * 0.5f, TowerFront - GlassThickness * 0.5f),
                OpenPosition = new Vector3(0f, DoorHeight * 1.5f + 0.02f, TowerFront - GlassThickness * 0.5f),
                Motion = DoorMotion.Slide, Seconds = FlapSeconds, StartsOpen = true,
            });

            // The sponge on the castle's front edge, long side toward the start. It stays put until it is
            // picked up: nobody shoves it into the water by walking into it.
            sponge = ToyCatalog.Add(ctx, ToyId.Sponge, SpongeHome + Vector3.up * 0.005f, StartScale, options: o =>
            {
                o.MinScale = MinScale;
                o.MaxScale = MaxScale;
                o.Tags = new[] { SpongeTag };
                o.FrozenUntilGrabbed = true;
            });
            soakOptions = new SpongeOptions
            {
                Name = "sponge", Prop = sponge, Volumes = new[] { tower, bowl },
                CapacityPerScale3 = CapacityPerScale3, AbsorbRate = AbsorbRate, SqueezeRate = SqueezeRate,
            };
            soak = new SpongeGadget(ctx, soakOptions);
            soak.Wringing += () => OnWringing(ctx);

            // The grab ray stops at glass: a sponge that has left the bowl (over the rim behind the tower,
            // onto the books) would be lost. It comes back, and its water with it.
            leash = new PropLeash(ctx, new PropLeashOptions
            {
                Name = "bowl", Props = new[] { sponge },
                Allowed = new[] { Zone.Cylinder(0f, 0f, BowlRadius, DenFloorY - 1f, SkyY + 1f) },
                Unless = () => Above(ctx.Game.Player.Position),
                Grace = 3f, Action = LeashAction.Respawn,
            });
            leash.PropReturned += prop => Say(ctx, BackLine);

            // Wherever the sponge is brought back from - by the leash, by the kill plane, as a crumb - the
            // water it held goes back into the bowl. The total never changes.
            ctx.Game.Events.PropRespawned += e =>
            {
                if (e.Prop != sponge) return;
                soak.EmptyInto(bowl);
                pending = false;
                crumbTicks = 0;
            };
            ctx.Game.Events.PropGrabbed += e =>
            {
                if (e.Prop != sponge) return;
                pending = false;
                wrungOut = false;
                loneSaid = false;
                crumbTicks = 0;
                // Picked up from far off it looks small, and it only ever gets as big as it looks. That only
                // matters while the bowl is still to be drunk.
                float ratio = e.OldScale / Mathf.Max(0.01f, e.GrabDistance);
                if (ratio < FarRatio && bowl.Depth > WadeDepth) Say(ctx, FarLine, 6f);
            };
            ctx.Game.Events.PropDropped += e =>
            {
                if (e.Prop != sponge || sponge.Held || sponge.Removed || sponge.Frozen) return;
                pending = true;
                restTicks = 0;
                castleStop = OnTheCastle();
            };
            ctx.OnUpdate(dt => Watch(ctx));

            exit = ctx.AddExit(ExitCentre, ExitSize);
            ctx.SetSpawn(Spawn, 0f, SpawnPitch);
        }

        // ---- What happens, and what is said ----------------------------------------------------------------------

        // One line, and not the same one over and over.
        void Say(LevelContext ctx, string text, float seconds = 5f)
        {
            if (text == lastLine && ctx.Ticks - lastSaid < 360) return;
            lastLine = text;
            lastSaid = ctx.Ticks;
            ctx.Say(text, seconds);
        }

        // Does the sponge lie on or against the castle, above its top? (Read at the moment it is let go.)
        bool OnTheCastle()
        {
            float back = float.MaxValue, low = float.MaxValue;
            foreach (Collider collider in sponge.Colliders)
            {
                Bounds bounds = collider.bounds;
                back = Mathf.Min(back, bounds.min.z);
                low = Mathf.Min(low, bounds.min.y);
            }
            return back < IslandFront - 0.02f && low < IslandTop + 0.3f && low > IslandTop - 0.3f && Mathf.Abs(sponge.Center.x) < IslandHalfWidth + sponge.Scale * 0.5f;
        }

        /// <summary>
        /// The biggest the sponge looks, where it lies, from anywhere the player can stand while the bowl is
        /// too deep to wade: the castle, and wherever they stand dry right now.
        /// </summary>
        public float BestRatio(Player player)
        {
            Vector3 centre = sponge.Center;
            var eye = new Vector3(Mathf.Clamp(centre.x, -IslandHalfWidth + 0.35f, IslandHalfWidth - 0.35f), IslandTop + Player.BaseEyeHeight,
                Mathf.Clamp(centre.z, IslandBack + 0.65f, IslandFront - 0.35f));
            float best = sponge.Scale / Mathf.Max(0.5f, Vector3.Distance(eye, centre));
            if (player.Grounded && player.Position.y >= bowl.SurfaceY - WadeDepth)
                best = Mathf.Max(best, sponge.Scale / Mathf.Max(0.5f, Vector3.Distance(player.Eye, centre)));
            return best;
        }

        // A full sponge has been made small: it starts to let its water go, into whatever it lies over.
        void OnWringing(LevelContext ctx)
        {
            wrungInTower = soak.Over == tower;
            if (wrungInTower)
            {
                if (cork.AtRest) rodeToTop = false;
                return;
            }
            // (This is what is said about this let-go: nothing more about its size when it has come to rest.)
            wrungOut = true;
            Say(ctx, WringLine, 6f);
        }

        // The player is out of their depth: back to the castle, and - unless the sponge is in their hand -
        // everything as it was at the start.
        void Swept(LevelContext ctx)
        {
            Player player = ctx.Game.Player;
            bool inTower = InTower(player.Position);
            wetTicks = 0;
            Sweeps++;
            player.Respawn();
            if (sponge.Held)
            {
                Say(ctx, DeepLine);
                return;
            }
            string line = !drained ? DeepLine : !wrungInTower ? RefillLine : ctx.Ticks - spillTick < 120 ? SpillLine : !inTower ? (loneSaid ? RefillLine : TowerLine) : rodeToTop ? LateLine : LowLine;
            PutBack(ctx);
            Say(ctx, line, 6f);
        }

        // The sponge dry on the castle at the size it started with, the bowl full, the tower empty.
        void PutBack(LevelContext ctx)
        {
            if (!sponge.Frozen || soak.Stored > 0f || Mathf.Abs(bowl.Volume - BowlWater) > 0.01f) Resets++;
            sponge.Respawn();
            soak.Reset();
            bowl.Set(BowlWater);
            tower.Set(0f);
            drained = false;
            wrungInTower = false;
            wrungOut = false;
            pending = false;
            doorSaid = false;
            rodeToTop = false;
            loneSaid = false;
            crumbTicks = 0;
        }

        // Every tick, after the gadgets: the flap, the player's depth, and the sponge where it lies.
        void Watch(LevelContext ctx)
        {
            Player player = ctx.Game.Player;
            if (flakes != null) flakes.localPosition = new Vector3(0f, Mathf.Max(0f, bowl.SurfaceY) + FlakeLift, 0f);

            // From inside the tower the sponge can only be let go inside the tower.
            bool guarded = sponge.Held && InTower(player.Position);
            if (guard.enabled != guarded) guard.enabled = guarded;

            // The flap shuts when the sponge starts wringing itself out in the tower and opens when the tower
            // is empty again. Until it has shut, what is wrung into the tower runs straight out into the bowl.
            bool wringing = soak.Activity == SpongeActivity.Wringing && soak.Over == tower;
            if (wringing) flap.Close();
            else if (tower.Volume <= 0.05f) flap.Open();
            tower.MaxSurfaceY = flap.IsClosed ? TowerLip : 0f;
            if (wringing && flap.Waiting)
            {
                spillTick = ctx.Ticks;
                doorTicks++;
                if (doorTicks >= 45 && !doorSaid)
                {
                    doorSaid = true;
                    Say(ctx, DoorLine, 6f);
                }
            }
            else
            {
                doorTicks = 0;
            }

            // The tower holds what it has for three seconds after the sponge stops feeding it: its leak is made
            // good from the bowl. (A cork that has sunk a tenth of a unit below the rim is a kerb to the foot.
            // Without this, somebody who picked the sponge up at the top to take it along - or to get it out of
            // their way - had a third of a second to walk off the cork, and after that had to hop.) One tick's
            // leak ahead, so that the cork lies still meanwhile.
            if (wringing) fedTick = ctx.Ticks;
            else if (flap.IsClosed && tower.Volume > 0f && ctx.Ticks - fedTick < Mathf.RoundToInt(HoldSeconds / Sim.Dt))
                tower.Add(bowl.Take(LeakRate * Sim.Dt));

            // Nobody waits out a slow leak behind a shut flap (71 units at 3 a second: 24 s in a glass tube
            // with nothing to do). Once the sponge has no more to give and the cork has sunk too far below the
            // rim to step off it, the tower runs empty as fast as it filled.
            bool spent = !sponge.Held && !wringing;
            if (spent && !flap.IsOpen && tower.Volume > 0f && cork.TopY < RimY - StepOff)
                bowl.Add(tower.Take(DrainRate * Sim.Dt));

            if (bowl.Depth <= WadeDepth) drained = true;
            if (cork.AtTop && InTower(player.Position)) rodeToTop = true;

            // The cork is at the top and they are not on it: that is said now, while they stand and watch it,
            // and not when the surplus has filled the bowl again behind it - which is eight seconds later, with
            // nothing for them to do meanwhile (the sponge lies behind glass).
            if (cork.AtTop && wrungInTower && !rodeToTop && !loneSaid && !InTower(player.Position) && !Above(player.Position))
            {
                loneSaid = true;
                Say(ctx, TowerLine, 6f);
            }
            // And from then on the sponge lets the rest go in a rush: the tower runs over like a tap left on,
            // and what comes of it - the bowl too deep again, everything put back - comes while the line is
            // still being read. (At its own pace a sponge that drank the whole bowl takes a quarter of a minute.)
            soakOptions.SqueezeRate = loneSaid ? LoneSqueezeRate : SqueezeRate;

            // How deep the player stands in the bowl. Behind the tower's flap, once it has begun to shut,
            // nobody stands in the bowl's water (what ran out under it may have made the bowl too deep again).
            // And nobody hops across deep water: once under, the time counts on, in the air as well, until the
            // feet stand somewhere dry.
            // (Nor is anybody swept out of the doorway while the flap is waiting for them to get out of its way:
            // they are told to step onto the cork - after three quarters of a second - and with a sponge from
            // the low end of the window the bowl is too deep again in a quarter of one.)
            Vector3 feet = player.Position;
            float limit = bowl.SurfaceY - WadeDepth * player.Scale;
            bool inDoorway = wringing && flap.Waiting;
            bool sheltered = (InTower(feet) && !flap.IsOpen) || inDoorway;
            if (!bowl.Over(feet) || sheltered || bowl.Depth <= WadeDepth * player.Scale) wetTicks = 0;
            else if (feet.y < limit || (wetTicks > 0 && !player.Grounded)) wetTicks++;
            else if (player.Grounded) wetTicks = 0;
            if (wetTicks >= Mathf.RoundToInt(SweepDelay / Sim.Dt))
            {
                ctx.Game.Events.RaiseWaterSwept(new GadgetEvent { Gadget = bowl, Position = feet });
                Swept(ctx);
                return;
            }

            bool loose = !sponge.Held && !sponge.Removed && !sponge.Frozen;
            bool deep = bowl.Depth > WadeDepth;

            // "Closer" has to be possible. While the bowl is too deep to wade the castle is all there is to
            // stand on: a sponge that looks too small from there, wherever it lies, goes back to the start.
            // (Not while the player is in the tower: riding the cork up with the sponge at their feet, the bowl
            // may well be too deep again behind the flap, and that is nobody's mistake. And not once they are
            // up at the rim or out on the books: the sponge did its work, and nothing is put back behind them -
            // with a sponge from the low end of the window the bowl is too deep again before they step off. Nor
            // while they stand in the tower's doorway with the flap waiting for them: that has its own line.)
            bool away = InTower(feet) || Above(feet) || inDoorway;
            bool useless = loose && deep && !away && soak.Activity != SpongeActivity.Soaking && BestRatio(player) < UselessRatio;
            crumbTicks = useless ? crumbTicks + 1 : 0;
            if (crumbTicks >= 90)
            {
                crumbTicks = 0;
                Returns++;
                bool stopped = castleStop;
                if (tower.Volume > 0.05f)
                {
                    // It wrung itself out in the tower with the player high and dry: no waiting for the
                    // tower to run empty, everything goes back.
                    bool told = loneSaid;
                    PutBack(ctx);
                    Say(ctx, told ? BackLine : TowerLine, 6f);
                    return;
                }
                sponge.Respawn();
                Say(ctx, stopped ? CastleLine : CrumbLine, 6f);
                return;
            }

            // The sponge has been let go and has come to rest (or has drunk all it can): is the bowl
            // shallow enough now? If not, say why - once per let-go.
            if (!pending || !loose) return;
            bool still = soak.Activity == SpongeActivity.Idle && sponge.Velocity.sqrMagnitude < 0.04f;
            restTicks = still ? restTicks + 1 : 0;
            if (restTicks < 30) return;
            pending = false;
            if (!deep || useless || away || wrungOut || InTower(sponge.Center)) return;
            GadgetKit.VerticalExtent(sponge, out float lowest, out _);
            bool dry = lowest >= bowl.SurfaceY && soak.Stored < soak.Capacity - 0.5f;
            Say(ctx, castleStop || dry ? CastleLine : TurnedShort() ? TurnedLine : ShortLine, 6f);
        }

        // Did the sponge fall short because of how it was turned? It does not lie flat (it was flipped onto a
        // side or an end, and met the gravel sooner for it), or it lies askew to the dashes (its long side
        // more than 10 degrees off the bowl's X axis) and reaches as far as the tower's bay: "hold it out
        // farther" would be no help there, there is no farther.
        bool TurnedShort()
        {
            if (Mathf.Abs(sponge.Transform.up.y) < 0.9f) return true;
            Vector3 along = sponge.Transform.right;
            float turn = Mathf.Abs(Mathf.Atan2(along.z, along.x)) * Mathf.Rad2Deg;
            turn = Mathf.Min(turn, 180f - turn);
            if (turn < 10f) return false;
            float far = float.MinValue;
            foreach (Collider collider in sponge.Colliders) far = Mathf.Max(far, collider.bounds.max.z);
            return far > TowerFront - GlassThickness - BayDepth - 0.8f;
        }

        // ---- Geometry: what stops the player and a held toy ------------------------------------------------------

        static void Solid(Transform root, string name, Vector3 min, Vector3 max)
        {
            var solid = new GameObject(name) { layer = Layers.Default };
            solid.transform.SetParent(root, false);
            solid.transform.localPosition = (min + max) * 0.5f;
            solid.AddComponent<BoxCollider>().size = max - min;
        }

        /// <summary>Segments of the bowl's wall: one box per 15 degrees, its inner face tangent to the round glass.</summary>
        public const int WallSegments = 24;

        // A piece of the round wall as a box: the segment at this angle (0 along +X, 90 along +Z), from one
        // height to another, along its tangent from u0 to u1 (the whole segment is -half..half).
        static void WallBox(Transform root, float degrees, float y0, float y1, float u0, float u1)
        {
            float a = degrees * Mathf.Deg2Rad;
            var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
            var holder = new GameObject("Glass") { layer = Layers.Default };
            holder.transform.SetParent(root, false);
            Vector3 at = radial * (BowlRadius + GlassThickness * 0.5f) + tangent * ((u0 + u1) * 0.5f);
            holder.transform.localPosition = new Vector3(at.x, (y0 + y1) * 0.5f, at.z);
            holder.transform.localRotation = Quaternion.Euler(0f, -degrees, 0f);
            holder.AddComponent<BoxCollider>().size = new Vector3(GlassThickness, y1 - y0, u1 - u0);
        }

        void BuildSolids(LevelContext ctx)
        {
            var root = new GameObject("Solids");
            Transform t = root.transform;
            float outer = BowlRadius + GlassThickness;

            // The bowl's foot and the gravel on it. The gravel leaves the tower's floor free: the cork lies
            // in that recess, its top level with the gravel.
            Solid(t, "Foot", new Vector3(-outer, DenFloorY, -outer), new Vector3(outer, BedY, outer));
            Solid(t, "Gravel", new Vector3(-outer, BedY, -outer), new Vector3(outer, 0f, TowerFront));
            Solid(t, "Gravel -X", new Vector3(-outer, BedY, TowerFront), new Vector3(-TowerHalfWidth, 0f, outer));
            Solid(t, "Gravel +X", new Vector3(TowerHalfWidth, BedY, TowerFront), new Vector3(outer, 0f, outer));

            // The glass. Up to the rim it is drawn; above it, up to the sky cap, it is only there (nothing is
            // projected over the rim, and nobody leaves the bowl but over the rim behind the tower).
            float step = 360f / WallSegments;
            float half = outer * Mathf.Tan(step * 0.5f * Mathf.Deg2Rad) + 0.01f;
            // Where the two segments beside the tower's back are cut off above the rim: at the tower's inside.
            float cut = half - 0.26f;
            for (int i = 0; i < WallSegments; i++)
            {
                float degrees = i * step;
                WallBox(t, degrees, DenFloorY, RimY, -half, half);
                float off = Mathf.DeltaAngle(degrees, 90f);
                if (Mathf.Abs(off) < 0.5f) continue;                                   // the way out, over the rim
                if (Mathf.Abs(off) < step + 0.5f) WallBox(t, degrees, RimY, SkyY, off > 0f ? -half : -cut, off > 0f ? cut : half);
                else WallBox(t, degrees, RimY, SkyY, -half, half);
            }

            // The tower: side walls, a front wall with a doorway, and above the doorway a bay that stands
            // forward of it. Drawn up to 10.5, there up to the sky cap.
            float front = TowerFront - GlassThickness, side = TowerHalfWidth + GlassThickness;
            Solid(t, "Tower -X", new Vector3(-side, BedY, front), new Vector3(-TowerHalfWidth, SkyY, outer + 0.05f));
            Solid(t, "Tower +X", new Vector3(TowerHalfWidth, BedY, front), new Vector3(side, SkyY, outer + 0.05f));
            Solid(t, "Tower Jamb -X", new Vector3(-TowerHalfWidth, 0f, front), new Vector3(-DoorHalfWidth, DoorHeight, TowerFront));
            Solid(t, "Tower Jamb +X", new Vector3(DoorHalfWidth, 0f, front), new Vector3(TowerHalfWidth, DoorHeight, TowerFront));
            Solid(t, "Tower Front", new Vector3(-TowerHalfWidth, DoorHeight, front), new Vector3(TowerHalfWidth, SkyY, TowerFront));
            Solid(t, "Tower Bay", new Vector3(-side, BayBottom, front - BayDepth), new Vector3(side, SkyY, front));

            // The stack of books outside, and what keeps the player on it.
            Solid(t, "Books", new Vector3(-LedgeHalfWidth, DenFloorY, LedgeNear), new Vector3(LedgeHalfWidth, RimY, LedgeFar));
            Solid(t, "Ledge -X", new Vector3(-LedgeHalfWidth - 0.3f, RimY, LedgeNear - 0.5f), new Vector3(-LedgeHalfWidth, SkyY, LedgeFar + 0.3f));
            Solid(t, "Ledge +X", new Vector3(LedgeHalfWidth, RimY, LedgeNear - 0.5f), new Vector3(LedgeHalfWidth + 0.3f, SkyY, LedgeFar + 0.3f));
            Solid(t, "Ledge Far", new Vector3(-LedgeHalfWidth, RimY, LedgeFar), new Vector3(LedgeHalfWidth, SkyY, LedgeFar + 0.3f));

            ctx.AddStatic(root, Vector3.zero);
            SkyCap.Add(ctx, -outer - 0.3f, outer + 0.3f, -outer - 0.3f, LedgeFar + 0.3f, SkyY);

            // The doorway's guard (see the field): the doorway itself as a box that the player's capsule never
            // touches but the hold march sees, switched on in Watch.
            var door = new GameObject("Doorway Guard") { layer = Layers.Default };
            guard = door.AddComponent<BoxCollider>();
            guard.size = new Vector3(DoorHalfWidth * 2f, DoorHeight, GlassThickness);
            guard.excludeLayers = Layers.PlayerMask;
            guard.enabled = false;
            ctx.AddStatic(door, new Vector3(0f, DoorHeight * 0.5f, TowerFront - GlassThickness * 0.5f));
        }

        // ---- Geometry: what is drawn -------------------------------------------------------------------------------

        /// <summary>The glass of the bowl and the tower: clear enough to see the room through, never a toy.</summary>
        static readonly ToyRecipe BowlGlass = ToyRecipe.Glass.With(r =>
        {
            r.Name = "Bowl Glass";
            r.AlphaFace = 0.1f;
            r.AlphaEdge = 0.5f;
            r.Glint = 0.5f;
            r.Rim = 0f;
            r.BackShell = false;
            r.ShadowDither = false;
            r.Pool = false;
            r.PoolTint = 1f;
        });

        static Material Glass => Materials.Toy(BowlGlass, Palette.Paper);

        void BuildBowl(LevelContext ctx, Dip dip)
        {
            var root = new GameObject("Bowl");
            float outer = BowlRadius + GlassThickness;
            // Gravel a good deal darker than the castle and the room: what is under water has to look it.
            Color pebbles = Palette.Mix(dip.Deep, Palette.Ink, 0.22f);
            Material gravel = Materials.Room(new RoomRecipe
            {
                Name = "Level13 Gravel", Top = pebbles, Side = Palette.Mix(dip.Deep, Palette.Ink, 0.3f), Dado = pebbles,
                Pattern = new PatternSpec(RoomPattern.Dots, 0.8f, 0.24f, 0f, 0.12f),
            });
            Material foot = Materials.Room(RoomRecipe.Solid(dip.Deep));
            Mesh bed = MeshKit.Cached("Level13|Gravel", () => Level13Shapes.GravelBed(BowlRadius, -BedY, TowerHalfWidth, TowerFront, 72));
            GadgetKit.Visual(root.transform, "Gravel", bed, gravel);
            Mesh plate = MeshKit.Cached("Level13|Foot", () => MeshKit.Cylinder(outer + 0.15f, BedY - DenFloorY, 64));
            GadgetKit.Visual(root.transform, "Foot", plate, foot, new Vector3(0f, (BedY + DenFloorY) * 0.5f, 0f));
            Mesh ring = MeshKit.Cached("Level13|Glass", () => Level13Shapes.Ring(BowlRadius, outer, BedY, RimY, 72));
            GadgetKit.Visual(root.transform, "Glass", ring, Glass).shadowCastingMode = ShadowCastingMode.Off;
            ctx.AddStatic(root, Vector3.zero);
        }

        // The plastic castle: a block of an island with two steps down its +X flank and battlements along
        // its back and its -X side. The front edge and the side with the steps are open.
        void BuildCastle(LevelContext ctx, Dip dip)
        {
            Material stone = Materials.Room(RoomSurface.LevelStatic, dip, new PatternSpec(RoomPattern.Planks, 0.75f, 1.5f, 0.08f, 0.06f));
            float depth = IslandFront - IslandBack;
            ctx.AddStatic(BasicToys.Slab(new Vector3(IslandHalfWidth * 2f, IslandTop, depth), stone), new Vector3(0f, IslandTop * 0.5f, (IslandBack + IslandFront) * 0.5f));
            float stairWidth = StairMaxX - StairMinX, stairX = (StairMinX + StairMaxX) * 0.5f;
            ctx.AddStatic(BasicToys.Slab(new Vector3(stairWidth, 2f, StairMiddle - StairBack), stone), new Vector3(stairX, 1f, (StairBack + StairMiddle) * 0.5f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(stairWidth, 1f, StairFront - StairMiddle), stone), new Vector3(stairX, 0.5f, (StairMiddle + StairFront) * 0.5f));

            const float merlon = 0.5f, thick = 0.3f;
            foreach (float x in new[] { -1.75f, -0.875f, 0f, 0.875f, 1.75f })
                ctx.AddStatic(BasicToys.Slab(new Vector3(merlon, merlon, thick), stone), new Vector3(x, IslandTop + merlon * 0.5f, IslandBack + thick * 0.5f));
            foreach (float z in new[] { -8.25f, -7.4f })
                ctx.AddStatic(BasicToys.Slab(new Vector3(thick, merlon, merlon), stone), new Vector3(-IslandHalfWidth + thick * 0.5f, IslandTop + merlon * 0.5f, z));
        }

        // The filter tower's glass and its frame. (Its colliders are in BuildSolids.)
        void BuildTower(LevelContext ctx, Dip dip)
        {
            var set = new Level01Set();
            Material glass = Glass;
            Material frame = Materials.Gadget(GadgetPart.Body);
            float front = TowerFront - GlassThickness, side = TowerHalfWidth + GlassThickness, wallZ = (front + TowerFront) * 0.5f;
            float postX = TowerHalfWidth + GlassThickness * 0.5f, bayFront = front - BayDepth;
            const float collar = 0.25f, hood = 0.2f;
            float glassTop = TowerTop - collar, back = BowlRadius + GlassThickness * 0.5f;

            // Glass: the two sides, the front beside and over the doorway, and the bay above it (one block
            // from its face back to the tower's inside, so that no pane shows inside the glass).
            for (int s = -1; s <= 1; s += 2)
            {
                set.Box(glass, new Vector3(s * postX, glassTop * 0.5f, (TowerFront + back) * 0.5f), new Vector3(GlassThickness, glassTop, back - TowerFront));
                set.Box(glass, new Vector3(s * (DoorHalfWidth + TowerHalfWidth) * 0.5f, DoorHeight * 0.5f, wallZ), new Vector3(TowerHalfWidth - DoorHalfWidth, DoorHeight, GlassThickness));
            }
            set.Box(glass, new Vector3(0f, (DoorHeight + BayBottom) * 0.5f, wallZ), new Vector3(TowerHalfWidth * 2f, BayBottom - DoorHeight, GlassThickness));
            float bayY0 = BayBottom + hood;
            set.Box(glass, new Vector3(0f, (bayY0 + glassTop) * 0.5f, (bayFront + TowerFront) * 0.5f), new Vector3(TowerHalfWidth * 2f, glassTop - bayY0, TowerFront - bayFront));
            set.Finish(ctx, "Tower Glass", castShadows: false);

            // The frame, in the gadgets' ink: posts beside the doorway, the hood under the bay, the bay's two
            // edges, and a collar round the top.
            var trim = new Level01Set();
            for (int s = -1; s <= 1; s += 2)
            {
                trim.Box(frame, new Vector3(s * postX, BayBottom * 0.5f, wallZ), new Vector3(GlassThickness, BayBottom, GlassThickness));
                trim.Box(frame, new Vector3(s * postX, (bayY0 + TowerTop) * 0.5f, bayFront + GlassThickness * 0.5f), new Vector3(GlassThickness, TowerTop - bayY0, GlassThickness));
                trim.Box(frame, new Vector3(s * postX, TowerTop - collar * 0.5f, (bayFront + GlassThickness + back) * 0.5f), new Vector3(GlassThickness, collar, back - bayFront - GlassThickness));
            }
            trim.Box(frame, new Vector3(0f, BayBottom + hood * 0.5f, (bayFront + front) * 0.5f), new Vector3(side * 2f, hood, BayDepth));
            trim.Box(frame, new Vector3(0f, TowerTop - collar * 0.5f, bayFront + GlassThickness * 0.5f), new Vector3(TowerHalfWidth * 2f, collar, GlassThickness));
            trim.Finish(ctx, "Tower Frame");
        }

        // The cork: the tower's floor. On it, where to stand and where the sponge goes.
        GameObject BuildCork(Dip dip)
        {
            var root = new GameObject("Cork") { layer = Layers.Default };
            root.AddComponent<BoxCollider>().size = CorkSize;
            float bevel = CorkSize.y * 0.08f, top = CorkSize.y * 0.5f;
            GadgetKit.Visual(root.transform, "Visual", GadgetKit.RoundedBoxMesh(CorkSize, bevel), Materials.Toy(ToyRecipe.PlainProp, Palette.Kraft));
            Material pad = Materials.Room(RoomRecipe.Solid(dip.Deep)), print = Materials.Room(RoomRecipe.Solid(Palette.Paper));
            StandMark(root.transform, new Vector3(0f, top, CorkSpot.z - CorkZ), 180f, 0.62f, pad, print);

            // The sponge's footprint as it fits in here, dashed, in its colour.
            Material line = Paint(dip);
            Vector3 size = ToyFactory.SpongeSize * StartScale;
            var centre = new Vector3(0f, top + 0.012f, CorkAim.z - CorkZ);
            Dashes(root.transform, line, centre + new Vector3(-size.x * 0.5f, 0f, -size.z * 0.5f), centre + new Vector3(size.x * 0.5f, 0f, -size.z * 0.5f), 0.09f, 0.3f, 0.2f);
            Dashes(root.transform, line, centre + new Vector3(-size.x * 0.5f, 0f, size.z * 0.5f), centre + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f), 0.09f, 0.3f, 0.2f);
            Dashes(root.transform, line, centre + new Vector3(-size.x * 0.5f, 0f, -size.z * 0.5f), centre + new Vector3(-size.x * 0.5f, 0f, size.z * 0.5f), 0.09f, 0.3f, 0.2f);
            Dashes(root.transform, line, centre + new Vector3(size.x * 0.5f, 0f, -size.z * 0.5f), centre + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f), 0.09f, 0.3f, 0.2f);
            return root;
        }

        static Material Paint(Dip dip) => Materials.Room(RoomRecipe.Solid(Palette.Mix(Palette.Paper, ToyCatalog.Get(ToyId.Sponge).ColorIn(dip), PaintTint)));

        // A dashed line of flat strips lying on a level surface, as children of a body that moves (the cork).
        static void Dashes(Transform parent, Material paint, Vector3 from, Vector3 to, float width, float dash, float gap)
        {
            Vector3 along = to - from;
            float length = along.magnitude;
            if (length < 1e-4f) return;
            along /= length;
            int count = Mathf.Max(1, Mathf.FloorToInt((length + gap) / (dash + gap)));
            float step = count > 1 ? (length - dash) / (count - 1) : 0f;
            Quaternion rotation = Quaternion.LookRotation(along, Vector3.up);
            Mesh strip = GadgetKit.BoxMesh(new Vector3(width, 0.006f, dash));
            for (int i = 0; i < count; i++)
                GadgetKit.Visual(parent, "Dash", strip, paint, from + along * (dash * 0.5f + step * i), rotation).shadowCastingMode = ShadowCastingMode.Off;
        }

        // Where to stand: a round pad with a pair of shoe prints, toes the way the yaw says (0 is +Z), in
        // the room's own tones (the toy's colour is kept for where the toy goes). Looks only.
        static void StandMark(Transform parent, Vector3 at, float yaw, float radius, Material pad, Material print)
        {
            Mesh disc = MeshKit.Cached(MeshKit.Key("Level13/Pad", radius), () => MeshKit.Cylinder(radius, 0.004f, 40));
            GadgetKit.Visual(parent, "Pad", disc, pad, at + Vector3.up * 0.008f).shadowCastingMode = ShadowCastingMode.Off;
            Mesh sole = MeshKit.Cached(MeshKit.Key("Level13/Sole"), Level13Shapes.Sole);
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
            for (int s = -1; s <= 1; s += 2)
            {
                Quaternion flat = turn * Quaternion.Euler(0f, s * 9f, 0f) * Quaternion.Euler(90f, 0f, 0f);
                GadgetKit.Visual(parent, "Shoe", sole, print, at + turn * new Vector3(s * 0.24f, 0.014f, 0f), flat).shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // The stack of books behind the bowl: the way out is on top of it.
        void BuildLedge(LevelContext ctx, Dip dip)
        {
            var set = new Level01Set();
            Material cover = Materials.Room(RoomSurface.LevelStatic, dip);
            Material pages = Materials.Room(RoomSurface.Trim, dip);
            float[] thick = { 1.9f, 1.6f, 1.8f, 1.5f, 1.8f };
            float[] inset = { 0.25f, 0.05f, 0.3f, 0.12f, 0f };
            float y = DenFloorY, width = LedgeHalfWidth * 2f, depth = LedgeFar - LedgeNear;
            for (int i = 0; i < thick.Length; i++)
            {
                // Every book lies inside the stack's collider; the top one fills it.
                var size = new Vector3(width - inset[i] * 2f, thick[i], depth - inset[i]);
                var centre = new Vector3(0f, y + thick[i] * 0.5f, LedgeNear + size.z * 0.5f);
                set.Book(cover, pages, centre, size, i % 2 == 0 ? Level01Set.Side.NegX : Level01Set.Side.PosX, Quaternion.identity, i % 3 == 1 ? 0.92f : 1f);
                y += thick[i];
            }
            set.Finish(ctx, "Books");
        }

        // The size language of the campaign: the sponge's footprint at the size that works, dashed, on the
        // gravel it comes to lie on. And where to stand to put it there: shoe prints at the castle's edge.
        void BuildPaint(LevelContext ctx, Dip dip)
        {
            var set = new Level01Set();
            Material line = Paint(dip);
            Vector3 size = ToyFactory.SpongeSize * IntendedScale;
            float x = size.x * 0.5f, z0 = AimPoint.z - size.z * 0.5f, z1 = AimPoint.z + size.z * 0.5f;
            const float stroke = 0.22f, dash = 0.7f, gap = 0.45f;
            set.Dashes(line, new Vector3(-x, 0f, z0), new Vector3(x, 0f, z0), Vector3.up, stroke, dash, gap);
            set.Dashes(line, new Vector3(-x, 0f, z1), new Vector3(x, 0f, z1), Vector3.up, stroke, dash, gap);
            set.Dashes(line, new Vector3(-x, 0f, z0), new Vector3(-x, 0f, z1), Vector3.up, stroke, dash, gap);
            set.Dashes(line, new Vector3(x, 0f, z0), new Vector3(x, 0f, z1), Vector3.up, stroke, dash, gap);
            set.Finish(ctx, "Paint", castShadows: false);

            var mark = new GameObject("Stand Mark");
            ctx.AddStatic(mark, new Vector3(EdgeSpot.x, IslandTop, EdgeSpot.z - 0.05f));
            StandMark(mark.transform, Vector3.zero, 0f, 0.5f, Materials.Room(RoomRecipe.Solid(dip.Deep)), Materials.Room(RoomRecipe.Solid(Palette.Paper)));
        }

        /// <summary>
        /// Where the wind-up goldfish lies: against the glass beside the tower, where no sponge held out from
        /// the castle comes down. (Out in the bowl it stopped one held 25 degrees to the left at 7.8.)
        /// </summary>
        public static readonly Vector3 FishAt = new Vector3(-4.2f, 0f, 7.9f);
        public const float FishYaw = 62f;
        /// <summary>The fish food: how many places are tried, how thick a flake is, and how far its middle lies above the surface.</summary>
        public const int FlakeCount = 30;
        public const float FlakeThickness = 0.016f, FlakeLift = 0.022f;
        /// <summary>The height the fish food lies at (for tests).</summary>
        public float FlakesY => flakes != null ? flakes.position.y : float.NaN;

        // A bowl somebody lives in: a wind-up goldfish that the water leaves ticking on the gravel.
        void BuildDressing(LevelContext ctx, Dip dip)
        {
            const float length = 1.5f, height = 0.85f, thickness = 0.5f;
            var fish = new GameObject("Goldfish") { layer = Layers.Default };
            var box = fish.AddComponent<BoxCollider>();
            box.size = new Vector3(thickness, height, length + 0.5f);
            box.center = new Vector3(0f, height * 0.5f, -0.25f);
            Material gold = Materials.Toy(ToyRecipe.PlainProp, Palette.Mix(Palette.Lemon, Palette.Kraft, 0.4f));
            Material steel = Materials.Gadget(GadgetPart.Metal);
            Material ink = Materials.Room(RoomRecipe.Solid(Palette.Ink));
            Mesh body = MeshKit.Cached(MeshKit.Key("Level13/Fish", length, height, thickness), () => Level13Shapes.FishBody(length, height, thickness));
            Mesh tail = MeshKit.Cached(MeshKit.Key("Level13/FishTail", height), () => Level13Shapes.FishTail(0.55f, height * 0.9f, thickness * 0.3f));
            GadgetKit.Visual(fish.transform, "Body", body, gold);
            GadgetKit.Visual(fish.transform, "Tail", tail, gold, new Vector3(0f, height * 0.5f, -length * 0.5f + 0.08f));
            // The key in its side, and an eye on each.
            GadgetKit.Visual(fish.transform, "Key Stem", GadgetKit.BoxMesh(new Vector3(0.34f, 0.07f, 0.07f)), steel, new Vector3(thickness * 0.5f + 0.1f, height * 0.5f, -0.2f));
            GadgetKit.Visual(fish.transform, "Key", GadgetKit.BoxMesh(new Vector3(0.05f, 0.36f, 0.24f)), steel, new Vector3(thickness * 0.5f + 0.27f, height * 0.5f, -0.2f));
            Mesh eye = GadgetKit.DiscMesh(0.07f, 0.02f, 16);
            for (int s = -1; s <= 1; s += 2)
                GadgetKit.Visual(fish.transform, "Eye", eye, ink, new Vector3(s * (thickness * 0.5f - 0.02f), height * 0.62f, length * 0.28f), Quaternion.Euler(0f, 0f, 90f));
            ctx.AddStatic(fish, FishAt, Quaternion.Euler(0f, FishYaw, 0f));

            // Fish food on the water: flat flakes that lie on the surface, so that it is seen to be one - and
            // seen to go down. Looks only, and thin as paint. Sown on a spiral, clear of the castle and the tower.
            var set = new Level01Set();
            Material pale = Materials.Room(RoomRecipe.Solid(Palette.Paper)), warm = Materials.Room(RoomRecipe.Solid(dip.Light));
            for (int i = 0; i < FlakeCount; i++)
            {
                float radius = (BowlRadius - 0.9f) * Mathf.Sqrt((i + 0.5f) / FlakeCount), angle = i * 137.508f * Mathf.Deg2Rad;
                var at = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                bool castle = at.z < IslandFront + 0.6f && at.x > -IslandHalfWidth - 0.6f && at.x < StairMaxX + 0.6f;
                bool towerSide = at.z > TowerFront - GlassThickness - BayDepth - 0.6f && Mathf.Abs(at.x) < TowerHalfWidth + GlassThickness + 0.6f;
                if (castle || towerSide || Vector3.Distance(at, FishAt) < 1.4f) continue;
                float size = 0.3f + 0.22f * ((i * 7) % 5) / 4f;
                set.Box(i % 3 == 0 ? warm : pale, at, new Vector3(size, FlakeThickness, size * (0.62f + 0.1f * (i % 4))), Quaternion.Euler(0f, i * 53f, 0f));
            }
            flakes = set.Finish(ctx, "Flakes", castShadows: false).transform;
            flakes.localPosition = new Vector3(0f, BowlWater / BowlArea + FlakeLift, 0f);
        }

        // ---- The solution ------------------------------------------------------------------------------------------

        public override IEnumerator Solve(Bot bot)
        {
            // The sponge is a step away: picked up from here it looks as big as it will ever need to.
            yield return bot.Wait(0.25f);
            yield return bot.Grab(sponge);
            // From the castle's edge, held out over the middle of the bowl it comes down on the dashes, the
            // size of a house. It drinks until the water can be waded.
            yield return bot.WalkTo(EdgeSpot, 0.15f);
            yield return bot.LookAt(AimPoint);
            yield return bot.Wait(0.3f);
            yield return bot.DropAt(AimPoint);
            yield return Drunk(bot);

            // From the shoe prints on the cork the sponge is in plain view through the doorway. It keeps its
            // water while it is held, and lets it go when it is let go smaller: at the feet it is a brick
            // again. The flap shuts, the tower fills, the cork rides the water up.
            yield return ToTheCork(bot);
            yield return bot.Grab(sponge);
            yield return bot.DropAt(CorkAim);
            yield return Ride(bot);
        }

        /// <summary>Waits until the sponge has drunk the bowl shallow enough to wade and lies still.</summary>
        public IEnumerator Drunk(Bot bot)
        {
            yield return bot.Until(() => bowl.Depth <= WadeDepth - 0.01f && soak.Activity != SpongeActivity.Soaking, 8f);
            yield return bot.Until(() => sponge.Velocity.sqrMagnitude < 0.01f, 4f);
        }

        /// <summary>
        /// The way to the tower, with the soaked sponge lying in the bowl: down the castle's steps, round
        /// the sponge on its +X side, under the tower's bay to the doorway, and onto the shoe prints at the
        /// back of the cork.
        /// </summary>
        public IEnumerator ToTheCork(Bot bot)
        {
            float stairX = (StairMinX + StairMaxX) * 0.5f, stairZ = (StairBack + StairMiddle) * 0.5f;
            if (bot.Player.Position.y > 2.5f)
            {
                yield return bot.WalkTo(new Vector3(IslandHalfWidth - 0.4f, IslandTop, stairZ), 0.25f);
                yield return bot.WalkTo(new Vector3(stairX, 2f, stairZ), 0.25f);
                yield return bot.WalkTo(new Vector3(stairX, 1f, (StairMiddle + StairFront) * 0.5f), 0.25f);
                yield return bot.WalkTo(new Vector3(stairX, 0f, StairFront + 1f), 0.25f);
                yield return bot.Until(() => bot.Player.Grounded, 2f);
            }

            // Round the sponge's near corner on the +X side, then along its flank to the tower.
            float alcoveZ = TowerFront - GlassThickness - BayDepth * 0.5f;
            if (!sponge.Held)
            {
                Bounds bounds = sponge.Colliders[0].bounds;
                foreach (Collider collider in sponge.Colliders) bounds.Encapsulate(collider.bounds);
                float flankX = Mathf.Min(bounds.max.x + 0.9f, Mathf.Sqrt(9.4f * 9.4f - alcoveZ * alcoveZ));
                if (bounds.max.x > stairX - 0.9f)
                    yield return bot.WalkTo(new Vector3(flankX, 0f, Mathf.Min(bounds.min.z - 0.9f, StairFront + 1f)), 0.3f, 15f);
                yield return bot.WalkTo(new Vector3(flankX, 0f, alcoveZ), 0.25f, 15f);
            }
            // Under the bay to the doorway, and in.
            yield return bot.WalkTo(new Vector3(0f, 0f, alcoveZ), 0.2f, 15f);
            yield return bot.WalkTo(CorkSpot, 0.2f, 10f);
        }

        /// <summary>The end of the solution: up with the cork, and out over the rim behind.</summary>
        public IEnumerator Ride(Bot bot)
        {
            Game game = bot.Game;
            // (Looking out over the bowl on the way up, as anybody would.)
            yield return bot.LookAt(new Vector3(0f, 3f, -6f), 3f);
            yield return bot.Until(() => cork.AtTop, 10f);
            yield return bot.WalkTo(ExitCentre, 0.5f, 10f);
            yield return bot.Until(() => game.LevelCompleted, 3f);
        }
    }
}
