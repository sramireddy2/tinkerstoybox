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
    /// Level 6, "Bouncing Eraser" (LEVELS.md): size is stored bounce.
    ///
    /// A cubby fenced off with building blocks at the foot of a storage cabinet fourteen units tall; the
    /// way out is on top of it. The one toy is a pink eraser on a spool. Rubber throws whoever jumps on it
    /// 1.75 units up per unit of its scale, so the eraser has to be made big: held against the dashed
    /// outline on the cabinet it stops on the cabinet's face, and how far back the player stands decides
    /// its size there. Let go it falls to the floor, its slanted ends are the way onto it, and a jump on
    /// its back is a launch.
    ///
    /// The rule: the top of a bounce is twice the eraser's scale above the floor (a quarter of that is the
    /// slab's own thickness). Measured: 7.1 just makes the cabinet, 7.5 and up clears it with time to
    /// spare; the eraser's largest size is 10.5. Picked up from the start and held against the outline it
    /// is 0.59 times the distance to the cabinet: 6 from beside the spool (the first try, two units short),
    /// 9.4 from the shoe prints in the middle of the room, and at its largest from 17.8 back.
    /// (Where this differs from LEVELS.md, and why: tools/out/notes/level06-build.md.)
    ///
    /// The spool the eraser starts on is furniture, not part of the puzzle: a big eraser let go above it
    /// comes down flat and the spool is out of sight under it until the eraser is picked up again
    /// (see WatchSpool; measured before and after in tools/out/notes/level06-review.md).
    /// </summary>
    [Level(6, "bouncing-eraser", "Bouncing Eraser", Phase = 2)]
    public sealed class Level06BouncingEraser : LevelDefinition
    {
        // ---- Layout (LEVELS.md, Level 6; floor y = 0, travel +Z, the cabinet is at +Z) -----------------------
        public const float HalfWidth = 16f, NearZ = -2.5f, FarZ = 30f;
        /// <summary>The cabinet's sheer face is the plane z = CabinetZ; its top is the goal.</summary>
        public const float CabinetZ = 18f, CabinetTop = 14f;
        public const float WallTop = 26f;
        const float WallThickness = 1f;

        /// <summary>
        /// The spool stands well to the left: an eraser held from anywhere near the middle of the room
        /// passes it at any size, and one let go from beside it has room before the wall.
        /// </summary>
        public static readonly Vector3 SpoolBase = new Vector3(-8.4f, 0f, 9f);
        public const float SpoolRadius = 0.4f, SpoolHeight = 1.1f;

        // ---- The eraser -------------------------------------------------------------------------------------
        public const float StartScale = 0.8f, MinScale = 0.4f, MaxScale = 10.5f;
        public const string BouncyTag = "bouncy";
        public const float GainPerScale = 1.75f;
        /// <summary>The size the outline on the cabinet is painted for, and the least size whose bounce clears the cabinet with time to spare.</summary>
        public const float IntendedScale = 9.5f, LeastScale = 7.5f;

        /// <summary>Where the player starts, and where the solver picks the eraser up from: a step in front of the spool.</summary>
        public static readonly Vector3 GrabSpot = new Vector3(-8.4f, 0f, 7.9f);
        /// <summary>The view at the start: the crosshair on the eraser, the cabinet behind it.</summary>
        public const float SpawnPitch = -15f;
        /// <summary>Where the solver lets go from: the shoe prints in the middle of the room.</summary>
        public static readonly Vector3 StandPoint = new Vector3(0f, 0f, 2f);
        /// <summary>The middle of the painted outline: what to hold the eraser against.</summary>
        public static readonly Vector3 AimPoint = new Vector3(0f, 3.5f, CabinetZ);
        public static readonly Vector3 ExitCentre = new Vector3(0f, CabinetTop + 1.25f, 27f);
        public static readonly Vector3 ExitSize = new Vector3(3f, 2.5f, 3f);

        /// <summary>
        /// Apparent size (scale / distance) below which the eraser cannot be made big enough from anywhere
        /// but the last steps before the back wall. Picked up from the start it is 0.69.
        /// </summary>
        public const float LooksTooSmall = 0.45f;
        /// <summary>
        /// An eraser lying anywhere but on its spool at less than this goes back to the spool. On the floor
        /// it cannot be picked up from nearer than the eye is high: below this it looks too small from
        /// wherever it lies.
        /// </summary>
        public const float CrumbScale = 1f;
        /// <summary>From this size up the eraser is something to climb, and which way up it lies matters.</summary>
        public const float ClimbScale = 2f;
        /// <summary>
        /// From this size up a level eraser is thicker than the spool is tall (1.2 against 1.1): let go
        /// above the spool it comes down flat on the floor and the spool is gone under it, instead of
        /// propping it up.
        /// </summary>
        public const float CoverScale = 4.8f;

        /// <summary>Said on a pick-up from too far away. The words are Level 1's, on purpose.</summary>
        public const string FarLine = "It only ever gets as big as it looks. Pick it up from closer.";
        /// <summary>Said when something other than the cabinet stopped the held eraser and it is too small: the floor, a wall, the spool.</summary>
        public const string ShortLine = "It stopped short of the cabinet. Hold it against the dashed outline.";
        /// <summary>Said at the top of a bounce that cannot reach.</summary>
        public const string LowLine = "Not high enough. More rubber, more bounce: hold it against the cabinet and step back.";
        /// <summary>Said after a bounce that was high enough and came down beside the cabinet all the same.</summary>
        public const string MissedLine = "High enough. Keep pushing toward the cabinet while you are in the air.";
        /// <summary>Said after a bounce that was high enough from an eraser too far from the cabinet to get across.</summary>
        public const string AwayLine = "High enough, but too far from the cabinet. Let the eraser go right against it.";
        public const string CrumbLine = "That eraser was too small to use. It is back on its spool.";
        /// <summary>Said when the eraser went back to the spool at about the size it started with (put down beside it).</summary>
        public const string SpoolLine = "The eraser is back on its spool.";
        public const string ShelfLine = "The eraser landed out of sight on the cabinet. It is back on its spool.";
        /// <summary>Said once to a player who stands on the eraser's back and does not jump.</summary>
        public const string StandLine = "Rubber. Jump on it.";
        /// <summary>
        /// Said when a big eraser has come to rest on its long edge, on end or on its back (the flip key).
        /// The two white stripes are on its top face only: on its back it lies on a wide side too.
        /// </summary>
        public const string FlipLine = "Its slanted ends are the way up. Pick it up and flip it so the white stripes are on top.";
        /// <summary>Said when a big eraser has come to rest tilted or off the floor: on the rim of the spool, on a block in a corner, against the cabinet.</summary>
        public const string CrookedLine = "It came down crooked. Pick it up and let it go flat on the floor, against the cabinet.";

        Prop eraser;
        BouncePad pad;
        PropLeash shelfLeash, crumbLeash;
        Exit exit;
        GameObject spool;
        /// <summary>The spool is under the eraser: neither drawn nor in the way, until the eraser is picked up again.</summary>
        bool spoolCovered;
        /// <summary>The eraser was let go above the spool and is on its way down onto it.</summary>
        bool spoolDoomed;
        bool wasHeld;
        string lastLine;
        int lastSaid;
        /// <summary>Apparent size of the eraser at its last pick-up.</summary>
        float ratio;
        // The bounce that is in the air: where it started and how fast.
        bool inFlight, lowSaid, missSaid, nudged;
        /// <summary>The eraser has been looked at since it was last let go (or has not been let go yet).</summary>
        bool judged;
        int flightTicks, landedTicks, standTicks, restTicks, awayTicks;
        float flightLaunch, flightFromY, flightFromZ;

        public Prop Eraser => eraser;
        public BouncePad Pad => pad;
        /// <summary>Returns an eraser that was thrown onto the cabinet, where it cannot be seen from the floor.</summary>
        public PropLeash ShelfLeash => shelfLeash;
        /// <summary>Returns an eraser that is too small to use to its spool.</summary>
        public PropLeash CrumbLeash => crumbLeash;
        public Exit Exit => exit;
        /// <summary>The spool the eraser starts on (a set piece: it cannot be picked up).</summary>
        public GameObject Spool => spool;
        /// <summary>True while a big eraser lies where the spool stands, with the spool out of sight under it.</summary>
        public bool SpoolCovered => spoolCovered;

        // The way out cannot be seen from the floor, and the first picture ends below the signs that point
        // at it: the card says where it is.
        public override string Blurb => "Rubber remembers how to jump. The way out is up top.";

        public override string[] Hints => new[]
        {
            "Climb the eraser by its slanted end and jump on it. How high you go depends on how much rubber is under you.",
            "Hold the eraser against the cabinet and walk backward. The farther back you stand, the bigger it gets, and the higher it throws you.",
            // Five lines of the pause card's hint panel at full size (Phase2CampaignTests.EveryHint_FitsThePauseCardsPanel_AtFullSize);
            // a sixth line would be set in smaller type than the same panel in the level before.
            "Pick the eraser up from right beside it. From the shoe prints, cover the dashed outline on the cabinet with it and let go. Climb on, jump close to the cabinet, and keep pushing toward it in the air.",
        };

        public override string Environment => "block-hall";
        public override float GroundY => 0f;
        public override float KillY => -30f;

        /// <summary>How high above the floor the feet get in a bounce off an eraser of this scale that lies on the floor.</summary>
        public static float BounceTop(float scale) => (ToyFactory.EraserSize.y + GainPerScale) * scale;

        public override void Build(LevelContext ctx)
        {
            eraser = null;
            pad = null;
            shelfLeash = null;
            crumbLeash = null;
            exit = null;
            spool = null;
            spoolCovered = false;
            spoolDoomed = false;
            wasHeld = false;
            lastLine = null;
            lastSaid = -100000;
            ratio = 0f;
            inFlight = false;
            lowSaid = false;
            missSaid = false;
            nudged = false;
            judged = true;
            flightTicks = 0;
            landedTicks = 0;
            awayTicks = 0;
            standTicks = 0;
            restTicks = 0;
            flightLaunch = 0f;
            flightFromY = 0f;
            flightFromZ = 0f;

            BuildRoom(ctx);
            BuildPaint(ctx);
            BuildDressing(ctx);

            // The eraser on its spool, long side toward the start. It stays put until it is picked up.
            spool = ctx.AddStatic(ToyFactory.ThreadSpool(SpoolRadius, SpoolHeight, grabbable: false), SpoolBase + Vector3.up * (SpoolHeight * 0.5f));
            ToyDef def = ToyCatalog.Get(ToyId.Eraser);
            eraser = ToyCatalog.Add(ctx, ToyId.Eraser, SpoolBase + Vector3.up * (SpoolHeight + def.RestHeight * StartScale + 0.005f), StartScale, options: o =>
            {
                o.MinScale = MinScale;
                // 15.75 long at most: leaned on the cabinet it would have to stand at 63 degrees to reach the top.
                o.MaxScale = MaxScale;
                o.Tags = new[] { BouncyTag };
                o.FrozenUntilGrabbed = true;
            });
            Vector3 home = eraser.Center;

            pad = new BouncePad(ctx, new BouncePadOptions
            {
                Name = "Eraser", Prop = eraser, GainPerScale = GainPerScale, MinImpact = 3f, MinNormalY = 0.9f, Cooldown = 0.1f,
            });
            pad.Bounced += (launch, impact) => OnBounced(ctx, launch);

            // An eraser thrown onto the cabinet is out of sight from the floor: it goes back to the spool,
            // unless the player is up there with it.
            float outer = HalfWidth + WallThickness + 1f;
            shelfLeash = new PropLeash(ctx, new PropLeashOptions
            {
                Name = "Cabinet Top", Props = new[] { eraser },
                Forbidden = new[] { Zone.MinMax(new Vector3(-outer, CabinetTop, NearZ - 2f), new Vector3(outer, WallTop + 4f, FarZ + 2f)) },
                Unless = () => ctx.Game.Player.Grounded && ctx.Game.Player.Position.y > CabinetTop - 0.5f,
                Grace = 2f, Action = LeashAction.Respawn,
            });
            shelfLeash.PropReturned += prop => Say(ctx, ShelfLine);

            // "Closer" has to be possible. An eraser too small to use goes back to the spool, where it is at
            // eye height and a step away is close.
            Zone everywhere = Zone.MinMax(new Vector3(-outer - 2f, -4f, NearZ - 4f), new Vector3(outer + 2f, WallTop + 4f, FarZ + 4f));
            float returned = StartScale;
            crumbLeash = new PropLeash(ctx, new PropLeashOptions
            {
                Name = "Crumb", Props = new[] { eraser }, Forbidden = new[] { everywhere },
                Unless = () => eraser.Scale >= CrumbScale || (eraser.Center - home).sqrMagnitude < 0.3f * 0.3f,
                Grace = 1.5f, Action = LeashAction.Respawn, OnReturn = prop => returned = prop.Scale,
            });
            crumbLeash.PropReturned += prop => Say(ctx, returned < StartScale - 0.01f ? CrumbLine : SpoolLine);

            // Should the eraser ever leave the room (it cannot by any means the player has), it comes back.
            new PropLeash(ctx, new PropLeashOptions { Name = "Room", Props = new[] { eraser }, Allowed = new[] { everywhere }, Grace = 2f });

            // What the player is told, each at the moment it happens.
            ctx.Game.Events.PropGrabbed += e =>
            {
                if (e.Prop != eraser) return;
                ratio = e.OldScale / Mathf.Max(0.01f, e.GrabDistance);
                lowSaid = false;
                missSaid = false;
                judged = true;
                // Picked up from far off it looks small, and it only ever gets as big as it looks.
                if (ratio < LooksTooSmall) Say(ctx, FarLine, 6f);
            };
            ctx.Game.Events.PropDropped += e =>
            {
                if (e.Prop != eraser || eraser.Held || eraser.Removed) return;
                OnLetGo(ctx);
            };
            ctx.OnUpdate(dt => Watch(ctx));

            exit = ctx.AddExit(ExitCentre, ExitSize);
            ctx.SetSpawn(GrabSpot, 0f, SpawnPitch);
            ctx.AddCheckpoint(Volume.Box(4f, 4f, 4f), GrabSpot + Vector3.up * 2f, GrabSpot, 0f, "Spawn");
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

        // The eraser has been let go. If it is too small and something other than the cabinet stopped it -
        // the floor (the view too low), a wall, the spool - say so: stepping back would not have helped.
        void OnLetGo(LevelContext ctx)
        {
            judged = false;
            restTicks = 0;
            float scale = eraser.Scale;
            if (scale < CrumbScale || scale >= LeastScale || scale >= MaxScale - 0.01f) return;
            // One that looked too small in the hand has had its own line.
            if (ratio < LooksTooSmall) return;
            float reach = float.MinValue;
            foreach (Collider collider in eraser.Colliders) reach = Mathf.Max(reach, collider.bounds.max.z);
            if (reach < CabinetZ - 0.6f) Say(ctx, ShortLine);
        }

        void OnBounced(LevelContext ctx, float launch)
        {
            // Landing on the eraser again ends the bounce before.
            if (inFlight && flightTicks > 6) Judge(ctx);
            Player player = ctx.Game.Player;
            inFlight = true;
            flightTicks = 0;
            landedTicks = 0;
            flightLaunch = launch;
            flightFromY = player.Position.y;
            flightFromZ = player.Position.z;
        }

        /// <summary>How high the feet get in the bounce that is in the air (0.2 is what the fixed step takes off the top).</summary>
        float FlightTop => flightFromY + flightLaunch * flightLaunch / (2f * Game.Gravity) - 0.2f;

        // ---- The spool ---------------------------------------------------------------------------------------

        // The spool is where the eraser starts, not part of the puzzle - but it stands where big erasers
        // come down: after the first try that falls short, whoever picks the eraser up again and steps back
        // through the left half of the room lets it go right above the spool (measured: one such try in
        // four). Propped up on the spool the eraser cannot be climbed, and none of that has to do with size
        // and bounce. So the spool gives way: an eraser thicker than the spool is tall, let go level above
        // it, comes down flat on the floor and the spool is gone under it - not drawn, not in the way - for
        // as long as the eraser lies there. Picked up again, the spool stands where it stood.
        void WatchSpool(LevelContext ctx, Player player)
        {
            bool held = eraser.Held;
            bool loose = !held && !eraser.Frozen && !eraser.Removed && eraser.Scale >= CoverScale;
            // (In the tick of the let-go this runs before the physics step.)
            if (!loose) spoolDoomed = false;
            else if (wasHeld) spoolDoomed = !spoolCovered && OverTheSpool(true);
            wasHeld = held;

            if (spoolDoomed)
            {
                // It goes as the eraser's underside reaches it, not before - and not at all if the eraser was
                // knocked off its way down (by the cabinet's edge, say).
                float underside = eraser.Center.y - ToyFactory.EraserSize.y * 0.5f * eraser.Scale;
                if (underside + Mathf.Min(0f, eraser.Velocity.y) * Sim.Dt * 2f <= SpoolHeight + 0.02f)
                {
                    spoolDoomed = false;
                    if (OverTheSpool(false)) CoverSpool(true);
                }
            }
            else if (spoolCovered)
            {
                // Back in its place once the eraser is picked up - or has come to rest somewhere else after
                // all - and nobody stands in that place. (Rest means half a second of it: at the top of its
                // hop after a landing from high up the eraser is still for a tick, a hand above the spool.)
                awayTicks = loose && AtRest(eraser) && !LiesWhereTheSpoolStands(ctx) ? awayTicks + 1 : 0;
                bool free = !loose || awayTicks >= 30;
                if (free && !InTheSpoolsPlace(player)) CoverSpool(false);
            }
        }

        void CoverSpool(bool covered)
        {
            if (spoolCovered == covered || spool == null) return;
            spoolCovered = covered;
            awayTicks = 0;
            spool.SetActive(!covered);
        }

        // A level eraser whose outline on the floor reaches over any of the spool - and, for one that is
        // being let go, whose underside is above the spool's top: it comes down on it.
        bool OverTheSpool(bool above)
        {
            Transform t = eraser.Transform;
            if (Mathf.Abs(t.up.y) < (above ? LevelDot : 0.99f)) return false;
            Vector3 half = ToyFactory.EraserSize * (0.5f * eraser.Scale);
            Vector3 to = SpoolBase - eraser.Center;
            if (above && -to.y - half.y < SpoolHeight - 0.05f) return false;
            return Mathf.Abs(Vector3.Dot(to, t.right)) < half.x + SpoolRadius && Mathf.Abs(Vector3.Dot(to, t.forward)) < half.z + SpoolRadius;
        }

        readonly Collider[] spoolHits = new Collider[8];

        // Is any of the eraser in the space the spool takes up?
        bool LiesWhereTheSpoolStands(LevelContext ctx)
        {
            var half = new Vector3(SpoolRadius, SpoolHeight * 0.5f, SpoolRadius);
            int count = ctx.Game.PhysicsScene.OverlapBox(SpoolBase + Vector3.up * half.y, half, spoolHits, Quaternion.identity, Layers.PropMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (PropRef.Of(spoolHits[i]) == eraser) return true;
            return false;
        }

        // The spool does not come back into anybody's legs.
        static bool InTheSpoolsPlace(Player player)
        {
            Vector3 to = player.Position - SpoolBase;
            float reach = SpoolRadius + player.Radius + 0.05f;
            return to.y < SpoolHeight + 0.05f && to.x * to.x + to.z * to.z < reach * reach;
        }

        // Every tick: the spool, the bounce in the air, and the player who stands on the eraser and does not jump.
        void Watch(LevelContext ctx)
        {
            Player player = ctx.Game.Player;
            WatchSpool(ctx, player);

            if (!nudged && pad.BounceCount == 0)
            {
                bool onItsBack = player.Grounded && player.GroundProp == eraser && player.GroundNormal.y > 0.9f && eraser.Scale >= ClimbScale;
                standTicks = onItsBack ? standTicks + 1 : 0;
                if (standTicks >= 150)
                {
                    nudged = true;
                    Say(ctx, StandLine);
                }
            }

            // An eraser big enough to climb that has come to rest any way but flat on its wide side: on its
            // edge or its back its slanted ends lead nowhere, and propped up on the spool they are too steep
            // or hang in the air. Looked at once per let-go, when it lies still.
            if (!judged && !eraser.Held && !eraser.Removed)
            {
                restTicks = AtRest(eraser) ? restTicks + 1 : 0;
                if (restTicks >= 30)
                {
                    judged = true;
                    float up = eraser.Transform.up.y;
                    // Level, but perched on something (balanced on the spool): its ends hang in the air.
                    bool perched = up >= LevelDot && eraser.Center.y - ToyFactory.EraserSize.y * 0.5f * eraser.Scale > PerchedHeight;
                    if (eraser.Scale >= ClimbScale && (up < LevelDot || perched)) Say(ctx, up < 0.5f ? FlipLine : CrookedLine, 6f);
                }
            }

            if (!inFlight) return;
            flightTicks++;
            // A bounce that cannot reach is told as soon as it is on its way down again, well under the top
            // (one that only just reaches may still end on the cabinet).
            if (!lowSaid && FlightTop < CabinetTop + ReachMargin && player.Velocity.y < 0f && player.Position.y < CabinetTop - 1.5f)
            {
                lowSaid = true;
                Say(ctx, LowLine, 6f);
            }
            // The bounce is over once the feet have stayed down for a moment. (One tick is not enough: a
            // capsule that meets the cabinet's top edge a hand's breadth under the top counts as standing
            // for that tick, and then slides down the face. Measured: 13.80 at the edge, from 17.27.)
            landedTicks = player.Grounded ? landedTicks + 1 : 0;
            if (flightTicks > 6 && landedTicks >= LandedTicks) Judge(ctx);
        }

        /// <summary>A tenth of a second on the ground: the bounce is over.</summary>
        const int LandedTicks = 6;
        /// <summary>Seconds of a bounce that are lost to getting up to speed toward the cabinet.</summary>
        const float AirStart = 0.35f;

        /// <summary>A bounce whose top is less than this above the cabinet's is too low to count on.</summary>
        const float ReachMargin = 0.3f;
        /// <summary>The eraser lies flat while its up axis is within two and a half degrees of the world's.</summary>
        const float LevelDot = 0.999f;
        /// <summary>A level eraser whose underside is this far off the floor lies on something, not on the floor.</summary>
        const float PerchedHeight = 0.3f;

        // The bounce is over: on the cabinet, or not - and then why not. Each reason is given once per
        // let-go: whoever goes on bouncing for the fun of it is left alone.
        void Judge(LevelContext ctx)
        {
            inFlight = false;
            Player player = ctx.Game.Player;
            // On the cabinet: nothing to say. (Landing on the eraser again also ends up here, from OnBounced.)
            if (player.Grounded && player.GroundProp != eraser && player.Position.y > CabinetTop - 0.1f) return;
            if (FlightTop < CabinetTop + ReachMargin)
            {
                if (!lowSaid) Say(ctx, LowLine, 6f);
                lowSaid = true;
                return;
            }
            if (missSaid) return;
            missSaid = true;
            // High enough. How far can a walk in the air carry before the feet are below the top again?
            float rise = CabinetTop - flightFromY;
            float root = Mathf.Sqrt(Mathf.Max(0f, flightLaunch * flightLaunch - 2f * Game.Gravity * rise));
            float airTime = (flightLaunch + root) / Game.Gravity;
            // (Getting up to walking speed in the air costs a good unit: measured 2.1 / 4.2 / 5.0 units a
            // second after 10 / 19 / 28 ticks.)
            bool tooFar = CabinetZ - flightFromZ > Player.WalkSpeed * (airTime - AirStart);
            Say(ctx, tooFar ? AwayLine : MissedLine, 6f);
        }

        // ---- Geometry ---------------------------------------------------------------------------------------

        static void Solid(LevelContext ctx, string name, Vector3 min, Vector3 max)
        {
            var solid = new GameObject(name) { layer = Layers.Default };
            solid.AddComponent<BoxCollider>().size = max - min;
            ctx.AddStatic(solid, (min + max) * 0.5f);
        }

        // Heights of the fence of building blocks.
        const float FenceLeft = 7f, FenceBack = 4.4f, FenceRight = 3.6f;

        void BuildRoom(LevelContext ctx)
        {
            float t = WallThickness, outer = HalfWidth + t;
            float back = NearZ - t, far = FarZ + t;

            // The floor is the room's own boards (the play plane is y = 0); this collider makes the level
            // stand on its own feet.
            Solid(ctx, "Floor", new Vector3(-outer, -1f, back), new Vector3(outer, 0f, far));
            // The cabinet: sheer face, square top edge, no lip.
            Solid(ctx, "Cabinet", new Vector3(-outer, 0f, CabinetZ), new Vector3(outer, CabinetTop, far));
            // What stops a held toy reaches the sky cap everywhere; what is drawn is a fence of building
            // blocks low enough for the sun to get in, and the cabinet itself.
            Solid(ctx, "Wall -X", new Vector3(-outer, 0f, back), new Vector3(-HalfWidth, WallTop, CabinetZ));
            Solid(ctx, "Wall +X", new Vector3(HalfWidth, 0f, back), new Vector3(outer, WallTop, CabinetZ));
            Solid(ctx, "Wall Back", new Vector3(-HalfWidth, 0f, back), new Vector3(HalfWidth, WallTop, NearZ));
            Solid(ctx, "Top -X", new Vector3(-outer, CabinetTop, CabinetZ), new Vector3(-HalfWidth, WallTop, far));
            Solid(ctx, "Top +X", new Vector3(HalfWidth, CabinetTop, CabinetZ), new Vector3(outer, WallTop, far));
            Solid(ctx, "Top Far", new Vector3(-HalfWidth, CabinetTop, FarZ), new Vector3(HalfWidth, WallTop, far));
            SkyCap.Add(ctx, -outer, outer, back, far, WallTop);

            Dip dip = ctx.Dip;
            var set = new Level01Set();
            Material wood = Materials.Room(RoomSurface.Furniture, dip);
            Material blocks = Materials.Room(RoomSurface.LevelStatic, dip, new PatternSpec(RoomPattern.Planks, 2f, 2.5f, 0.1f, 0.07f));
            set.Box(wood, new Vector3(0f, CabinetTop * 0.5f, (CabinetZ + far) * 0.5f), new Vector3(outer * 2f, CabinetTop, far - CabinetZ));

            // The sun comes from +X: that side is the lowest.
            BlockRow(set, blocks, -outer, -HalfWidth, back, CabinetZ, FenceLeft, 0.8f);
            BlockRow(set, blocks, HalfWidth, outer, back, CabinetZ, FenceRight, 0.6f);
            BlockRow(set, blocks, -HalfWidth, HalfWidth, back, NearZ, FenceBack, 0.7f);
            set.Finish(ctx, "Room");
        }

        // Building blocks standing side by side along a wall line, every other one a notch lower: looks
        // only (the wall's collider is a Solid). They stand on the floor.
        static void BlockRow(Level01Set set, Material material, float xMin, float xMax, float zMin, float zMax, float top, float notch)
        {
            bool alongZ = zMax - zMin >= xMax - xMin;
            float length = alongZ ? zMax - zMin : xMax - xMin;
            int count = Mathf.Max(1, Mathf.RoundToInt(length / 2f));
            float each = length / count;
            for (int i = 0; i < count; i++)
            {
                float height = top - (i % 2 == 1 ? notch : 0f);
                float along = (alongZ ? zMin : xMin) + (i + 0.5f) * each;
                Vector3 size = alongZ ? new Vector3(xMax - xMin, height, each) : new Vector3(each, height, zMax - zMin);
                Vector3 centre = alongZ ? new Vector3((xMin + xMax) * 0.5f, height * 0.5f, along) : new Vector3(along, height * 0.5f, (zMin + zMax) * 0.5f);
                set.Box(material, centre, size, i % 3 == 1 ? 0.95f : 1f);
            }
        }

        // The size language of the campaign: the eraser's long side at the size that works, dashed, on the
        // face it stops against. Above it the way is signed: up, and out.
        void BuildPaint(LevelContext ctx)
        {
            Dip dip = ctx.Dip;
            Color hero = ToyCatalog.Get(ToyId.Eraser).ColorIn(dip);
            Material line = Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Light, hero, 0.9f)));
            Material sign = Materials.Room(RoomRecipe.Solid(Palette.Paper));
            Material groove = Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Deep, Palette.Ink, 0.28f)));
            var set = new Level01Set();
            Vector3 face = Vector3.back;

            // The outline: 14.25 long at the foot, 8.55 on top, 2.4 tall.
            Vector3 size = ToyFactory.EraserSize * IntendedScale;
            float foot = size.x * 0.5f, top = ToyFactory.EraserTopLength * IntendedScale * 0.5f;
            float y0 = AimPoint.y - size.y * 0.5f, y1 = AimPoint.y + size.y * 0.5f;
            const float stroke = 0.2f, dash = 0.7f, gap = 0.42f;
            set.Dashes(line, new Vector3(-foot, y0, CabinetZ), new Vector3(foot, y0, CabinetZ), face, stroke, dash, gap);
            set.Dashes(line, new Vector3(-top, y1, CabinetZ), new Vector3(top, y1, CabinetZ), face, stroke, dash, gap);
            set.Dashes(line, new Vector3(-foot, y0, CabinetZ), new Vector3(-top, y1, CabinetZ), face, stroke, dash, gap);
            set.Dashes(line, new Vector3(foot, y0, CabinetZ), new Vector3(top, y1, CabinetZ), face, stroke, dash, gap);

            // The cabinet's fronts: a wide drawer at the foot, two rows of three doors above it.
            const float proud = 0.012f, thin = 0.02f, width = 0.14f;
            float z = CabinetZ - proud - thin * 0.5f, across = (HalfWidth + WallThickness) * 2f;
            foreach (float y in new[] { DrawerFoot, DrawerTop, DoorTop, CorniceY })
                set.Box(groove, new Vector3(0f, y, z), new Vector3(across, width, thin));
            foreach (float x in new[] { -DoorHalfWidth, DoorHalfWidth })
                set.Box(groove, new Vector3(x, (DrawerTop + CorniceY) * 0.5f, z), new Vector3(width, CorniceY - DrawerTop, thin));
            Quaternion flat = Quaternion.Euler(90f, 0f, 0f);
            float lower = (DrawerTop + DoorTop) * 0.5f, upper = (DoorTop + CorniceY) * 0.5f, pull = DoorHalfWidth + 1.3f;
            foreach (Vector2 knob in new[] { new Vector2(-11.5f, 2.9f), new Vector2(11.5f, 2.9f), new Vector2(-pull, lower), new Vector2(pull, lower), new Vector2(-pull, upper), new Vector2(pull, upper) })
                set.Disc(groove, new Vector3(knob.x, knob.y, z), 0.42f, thin, flat);

            // Three chevrons up the middle door, and the way out's four panes on the one above.
            Quaternion left = Quaternion.Euler(0f, 0f, 38f), right = Quaternion.Euler(0f, 0f, -38f);
            foreach (float y in new[] { lower - 1f, lower, lower + 1f })
            {
                set.Box(sign, new Vector3(-0.55f, y, z), new Vector3(1.5f, 0.24f, thin), left);
                set.Box(sign, new Vector3(0.55f, y, z), new Vector3(1.5f, 0.24f, thin), right);
            }
            const float pane = 0.7f, mullion = 0.18f;
            for (int i = -1; i <= 1; i += 2)
                for (int j = -1; j <= 1; j += 2)
                    set.Box(sign, new Vector3(i * (pane + mullion) * 0.5f, upper + j * (pane + mullion) * 0.5f, z), new Vector3(pane, pane, thin));

            set.Finish(ctx, "Paint", castShadows: false);

            // Where to stand: a round pad with a pair of shoe prints in the middle of the room, in the room's
            // own tones (the toy's colour is kept for where the toy goes).
            StandMark(ctx, "Stand Mark", StandPoint, Materials.Room(RoomRecipe.Solid(dip.Deep)), sign);
        }

        // Big enough to be found from the spool, ten units away.
        const float PadRadius = 0.9f, SoleScale = 1.4f;
        // The cabinet's front: where the drawer ends, where the lower doors end, where the cornice begins.
        const float DrawerFoot = 0.7f, DrawerTop = 5.1f, DoorTop = 9.5f, CorniceY = 13.4f, DoorHalfWidth = 5.6f;

        // Looks only: a renderer for the pad and one for each shoe, facing +Z, toes a little apart.
        static void StandMark(LevelContext ctx, string name, Vector3 at, Material pad, Material print)
        {
            var mark = new GameObject(name);
            ctx.AddStatic(mark, at + Vector3.up * 0.012f);
            Mesh disc = MeshKit.Cached(MeshKit.Key("Level06/Pad", PadRadius), () => MeshKit.Cylinder(PadRadius, 0.004f, 40));
            GadgetKit.Visual(mark.transform, "Pad", disc, pad).shadowCastingMode = ShadowCastingMode.Off;
            Mesh sole = MeshKit.Cached(MeshKit.Key("Level06/Sole"), () =>
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
                MeshRenderer shoe = GadgetKit.Visual(mark.transform, "Shoe", sole, print, new Vector3(side * 0.24f * SoleScale, 0.006f, 0f), flat);
                shoe.transform.localScale = Vector3.one * SoleScale;
                shoe.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // A corner of a playroom: spare blocks and a marker in the back corners, a ruler along the fence to
        // pace the steps back against, books and blocks on top of the cabinet with an arch of blocks round
        // the way out. Nothing stands where an eraser held toward the cabinet passes, or where it lands.
        void BuildDressing(LevelContext ctx)
        {
            Dip dip = ctx.Dip;
            var set = new Level01Set();

            // On the floor, in the back corners and flat along the left fence.
            float corner = HalfWidth - 1.1f, backRow = NearZ + 1f;
            Block(ctx, set, new Vector3(1.4f, 1.4f, 1.4f), new Vector3(-corner, 0f, backRow), 9f);
            Block(ctx, set, new Vector3(0.9f, 0.9f, 0.9f), new Vector3(-corner - 0.05f, 1.4f, backRow - 0.05f), -17f);
            Block(ctx, set, new Vector3(1.6f, 0.8f, 0.8f), new Vector3(-corner + 1.9f, 0f, backRow - 0.4f), -8f);
            Block(ctx, set, new Vector3(1.2f, 1.2f, 1.2f), new Vector3(corner, 0f, backRow), -12f);
            Piece(ctx, set, ToyFactory.Marker(0.4f, 3.2f), new Vector3(corner - 2.9f, 0.4f, backRow - 0.3f), Quaternion.Euler(0f, 84f, 0f));
            // (The ruler ends short of where the end of an eraser that lies against the cabinet comes down.)
            Piece(ctx, set, ToyFactory.Ruler(new Vector3(1.2f, 0.08f, 11f), grabbable: false), new Vector3(-HalfWidth + 0.7f, 0.04f, 5.4f), Quaternion.identity);

            // On top of the cabinet: an arch of blocks round the way out.
            float archZ = ExitCentre.z + 0.4f;
            Block(ctx, set, new Vector3(1.1f, 3.1f, 1.1f), new Vector3(-2.2f, CabinetTop, archZ), 0f);
            Block(ctx, set, new Vector3(1.1f, 3.1f, 1.1f), new Vector3(2.2f, CabinetTop, archZ), 0f);
            Block(ctx, set, new Vector3(5.9f, 1f, 1.1f), new Vector3(0f, CabinetTop + 3.1f, archZ), 0f);
            Block(ctx, set, new Vector3(1.5f, 1.5f, 1.5f), new Vector3(-9.5f, CabinetTop, 27.8f), 14f);
            Block(ctx, set, new Vector3(1f, 1f, 1f), new Vector3(-7.9f, CabinetTop, 28.6f), -21f);
            Block(ctx, set, new Vector3(1.3f, 1.3f, 1.3f), new Vector3(10.2f, CabinetTop, 28.2f), -9f);
            set.Finish(ctx, "Dressing");

            // A row of picture books standing along the back of the cabinet's top, spines to the room: what
            // closes the picture up there. They stand inside the wall that is not drawn.
            var books = new Level01Set();
            Material cover = Materials.Room(RoomSurface.LevelStatic, dip);
            Material pages = Materials.Room(RoomSurface.Trim, dip);
            Quaternion standing = Quaternion.Euler(0f, 0f, 90f);
            float[] thick = { 1.3f, 0.9f, 1.6f, 1.1f, 0.8f, 1.5f, 1.2f, 0.9f, 1.7f, 1f, 1.4f };
            float[] tall = { 4.4f, 3.6f, 5f, 4.1f, 3.3f, 4.7f, 3.9f, 4.4f, 5.2f, 3.5f, 4.6f, 3.8f, 4.9f };
            float edge = HalfWidth + WallThickness;
            float x = -edge;
            for (int i = 0; x < edge - 0.01f; i++)
            {
                float across = Mathf.Min(thick[i % thick.Length], edge - x), height = tall[i % tall.Length];
                var centre = new Vector3(x + across * 0.5f, CabinetTop + height * 0.5f, FarZ + WallThickness * 0.5f);
                books.Book(cover, pages, centre, new Vector3(height, across, WallThickness), Level01Set.Side.NegZ, standing, i % 3 == 0 ? 1f : i % 3 == 1 ? 0.9f : 0.96f);
                // A title label on the spine - but nothing pale behind the way out's own mark.
                if (across > 0.7f && Mathf.Abs(centre.x - ExitCentre.x) > 3f)
                    books.Box(pages, new Vector3(centre.x, CabinetTop + height * (0.55f + 0.08f * (i % 3)), FarZ - 0.012f), new Vector3(across * 0.55f, 0.55f, 0.02f));
                x += across;
            }
            books.Finish(ctx, "Books");
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

        public override IEnumerator Solve(Bot bot)
        {
            // The eraser is a step away: picked up from here it looks as big as it will ever need to.
            yield return bot.Grab(eraser);
            // From the shoe prints in the middle of the room, hold it against the outline for a beat - it
            // covers the dashes - and let go.
            yield return bot.WalkTo(StandPoint);
            yield return bot.LookAt(AimPoint);
            yield return bot.Wait(0.4f);
            yield return bot.DropAt(AimPoint);
            yield return bot.Until(() => AtRest(eraser), 6f);
            yield return bot.Wait(0.3f);

            yield return Climb(bot);
        }

        /// <summary>
        /// The second half of the solution, with the eraser lying on the floor: round its nearer end, up the
        /// slanted end onto its back, to the edge nearest the cabinet, jump, steer onto the cabinet in the
        /// air and walk into the exit.
        /// </summary>
        public IEnumerator Climb(Bot bot, bool sprint = false)
        {
            Game game = bot.Game;
            Vector3 half = ToyFactory.EraserSize * 0.5f;
            float top = ToyFactory.EraserTopLength * 0.5f;

            // The end of the eraser nearer to the bot - or the other one, if there is no room to walk round
            // that end (it lies against a fence).
            Vector3 forward = eraser.Transform.forward;
            float side = eraser.Transform.InverseTransformPoint(bot.Player.Position).x < 0f ? -1f : 1f;
            Vector3 foot = Vector3.zero, crest = Vector3.zero, along = Vector3.zero, lineUp = Vector3.zero;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                foot = eraser.Transform.TransformPoint(new Vector3(side * half.x, -half.y, 0f));
                crest = eraser.Transform.TransformPoint(new Vector3(side * top, half.y, 0f));
                along = crest - foot;
                along.y = 0f;
                along.Normalize();
                lineUp = foot - along * 1.6f;
                if (Mathf.Abs(lineUp.x) < HalfWidth - 0.5f && lineUp.z > NearZ + 0.5f && lineUp.z < CabinetZ - 0.5f) break;
                side = -side;
            }

            // Round the corner of the slab, to a spot in line with the slanted end, and up it.
            Vector3 corner = lineUp - forward * (half.z * eraser.Scale + 1.2f);
            if (Vector3.Dot(bot.Player.Position - lineUp, forward) < -1f) yield return bot.WalkTo(corner, 0.4f, 15f);
            yield return bot.WalkTo(lineUp, 0.3f, 15f);
            yield return bot.WalkTo(crest + along * 1f, 0.3f, 15f);

            // On its back: to the edge nearest the cabinet, in the middle.
            Vector3 jumpSpot = eraser.Transform.TransformPoint(new Vector3(0f, half.y, 0f));
            jumpSpot.z = Mathf.Min(CabinetZ - 1.2f, jumpSpot.z + half.z * eraser.Scale - 0.6f);
            yield return bot.WalkTo(jumpSpot, 0.25f, 15f);
            yield return bot.Until(() => bot.Player.Grounded, 2f);
            yield return bot.LookAt(new Vector3(jumpSpot.x, bot.Player.Eye.y, CabinetZ + 10f));

            int before = pad.BounceCount;
            yield return bot.Jump(false);
            yield return bot.Until(() => pad.BounceCount > before, 3f);
            // Steer in the air: against the cabinet's face on the way up, over its edge at the top.
            yield return bot.WalkTo(new Vector3(jumpSpot.x, CabinetTop, CabinetZ + 3f), 0.5f, 8f, sprint);
            yield return bot.Until(() => bot.Player.Grounded && bot.Player.Position.y > CabinetTop - 0.5f, 6f);
            yield return bot.WalkTo(ExitCentre, 0.5f, 15f);
            yield return bot.Until(() => game.LevelCompleted, 3f);
        }

        /// <summary>True once a prop that was let go has stopped moving.</summary>
        public static bool AtRest(Prop prop) =>
            !prop.Held && prop.Velocity.sqrMagnitude < 0.05f * 0.05f && prop.Body.angularVelocity.sqrMagnitude < 0.05f * 0.05f;
    }
}
