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
    /// Visual QA for the level machinery: every gadget that has something to show stands in a bay of its
    /// own, in the states a player has to be able to tell apart. Not part of the campaign: ask for it
    /// with ?level=98 (or gadget-gallery).
    ///
    /// Front row, left to right: laser rain over a card roof; a desk fan and its stream; a size gauge
    /// with three backstops and three sockets (waiting, refusing, seated); a basin that drains into a
    /// tower, with a dry, a half-soaked and a soaked sponge; two portal doorways and a recall pad.
    /// Back row: a seesaw whose striker drops once somebody stands on the seat; a bounce pad; a
    /// barricade that a marble breaks once somebody comes to watch; behind them a train and its station.
    ///
    /// The solve script is a tour of the bays that need a player; <see cref="Beats"/> says when each
    /// thing happened, for taking pictures:
    ///
    ///   unity.ps1 exec -Method Toybox.EditorTools.Shots.Capture -UnityArgs '-toyboxLevel','98','-toyboxTimes','1.5,6,8','-toyboxOut','tools/out/shots/level98'
    ///   unity.ps1 exec -Method Toybox.EditorTools.RoomShots.Capture -UnityArgs '-toyboxLevel','98','-toyboxEnv','night-light','-toyboxSpawn','-30,0,-1,0,5','-toyboxTimes','1.5'
    /// </summary>
    [Level(98, "gadget-gallery", "Gadget Gallery", Phase = 0)]
    public sealed class Level98GadgetGallery : LevelDefinition
    {
        const float FloorThickness = 1f;
        /// <summary>The marble starts down its ramp when somebody comes this close to the spot the barricade is watched from.</summary>
        public const float BreakerStartsAt = 8f;

        // ---- Where things stand (also the spawns to look at them from) ------------------------------------
        public static readonly Vector3 LaserBay = new Vector3(-30f, 0f, 8f);
        public static readonly Vector3 WindBay = new Vector3(-17.2f, 2.45f, 8f);
        public static readonly Vector3 GaugeStand = new Vector3(0f, 0f, -1.8f);
        public static readonly Vector3 WaterBay = new Vector3(15f, 0f, 8f);
        public static readonly Vector3 PortalBay = new Vector3(27f, 0f, 6f);
        public static readonly Vector3 BounceBay = new Vector3(-4f, 0f, 22f);
        public static readonly Vector3 SeesawBay = new Vector3(-16f, 1f, 28f);
        public static readonly Vector2 TrainCentre = new Vector2(2f, 38f);
        public static readonly Vector3 BreakBay = new Vector3(18f, 2f, 32f);
        public static readonly Vector3 BreakWatch = new Vector3(11f, 0f, 26f);
        public static readonly Vector3 ExitPoint = new Vector3(31f, 0f, -5f);

        static readonly Vector3 NearBackstop = new Vector3(-1.9f, 1.55f, 0.6f);
        static readonly Vector3 MidBackstop = new Vector3(0f, 1.55f, 4.4f);
        static readonly Vector3 FarBackstop = new Vector3(4.5f, 1.55f, 11.7f);

        readonly List<KeyValuePair<string, float>> beats = new List<KeyValuePair<string, float>>();
        int seatedTicks;
        bool strikerDropped, breakerRolled;

        public override string Blurb => "Every gadget, switched on.";
        public override string Environment => "sunny-rug";
        // The floor is a mat on the room's own.
        public override float GroundY => -FloorThickness;

        // ---- What stands in the bays (for tests and tools) ------------------------------------------------
        public LaserRain Lasers { get; private set; }
        public Prop Card { get; private set; }
        public WindStream Wind { get; private set; }
        public GameObject Fan { get; private set; }
        public FitGauge Gauge { get; private set; }
        public Prop Plug { get; private set; }
        public Socket WaitingSocket { get; private set; }
        public Socket RefusingSocket { get; private set; }
        public Socket SeatedSocket { get; private set; }
        public WaterVolume Basin { get; private set; }
        public WaterVolume Tower { get; private set; }
        public WaterVolume Puddle { get; private set; }
        public WaterVolume Pool { get; private set; }
        public Sponge DrySponge { get; private set; }
        public Sponge HalfSponge { get; private set; }
        public Sponge SoakedSponge { get; private set; }
        public PortalDoorway SmallDoor { get; private set; }
        public PortalDoorway BigDoor { get; private set; }
        public RecallPad Recall { get; private set; }
        public BouncePad Bounce { get; private set; }
        public Seesaw Seesaw { get; private set; }
        public Prop Striker { get; private set; }
        public Train Train { get; private set; }
        public Breakable Wall { get; private set; }
        public Prop Breaker { get; private set; }

        /// <summary>What the tour did and when (seconds of the level), in order: "gauge-small", "gauge-good", "gauge-big", "launch", "bounce", "break", "portal", "recall", "door-held".</summary>
        public IReadOnlyList<KeyValuePair<string, float>> Beats => beats;

        /// <summary>When a beat of the tour happened, or -1 if it has not (yet).</summary>
        public float BeatTime(string name)
        {
            for (int i = 0; i < beats.Count; i++)
                if (beats[i].Key == name) return beats[i].Value;
            return -1f;
        }

        public override void Build(LevelContext ctx)
        {
            beats.Clear();
            seatedTicks = 0;
            strikerDropped = false;
            breakerRolled = false;
            Striker = null;
            Breaker = null;

            ctx.AddStatic(BasicToys.Slab(new Vector3(90f, FloorThickness, 70f)), new Vector3(0f, -FloorThickness * 0.5f, 16f));

            BuildLasers(ctx);
            BuildWind(ctx);
            BuildGauge(ctx);
            BuildWater(ctx);
            BuildPortals(ctx);
            BuildBounce(ctx);
            BuildSeesaw(ctx);
            BuildTrain(ctx);
            BuildBarricade(ctx);

            ctx.OnUpdate(dt => Direct(ctx, dt));
            ctx.AddExit(ExitPoint + Vector3.up * 1.5f, new Vector3(3f, 3f, 2f));
            ctx.SetSpawn(new Vector3(0f, 0f, -10f), 0f);
        }

        // A lid with the emitter under it, two low walls and a card across them: the lane between the
        // walls is roofed, the floor either side of them is not.
        void BuildLasers(LevelContext ctx)
        {
            Vector3 bay = LaserBay;
            ctx.AddStatic(BasicToys.Slab(new Vector3(6.8f, 0.3f, 5.8f)), bay + new Vector3(0f, 5.25f, 0f));
            for (int i = 0; i < 4; i++)
                ctx.AddStatic(BasicToys.Slab(new Vector3(0.3f, 5.1f, 0.3f)), bay + new Vector3(i % 2 == 0 ? -3.25f : 3.25f, 2.55f, i < 2 ? -2.75f : 2.75f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(0.4f, 2f, 4.4f)), bay + new Vector3(-1.6f, 1f, 0f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(0.4f, 2f, 4.4f)), bay + new Vector3(1.6f, 1f, 0f));
            Card = ToyCatalog.Add(ctx, ToyId.PlayingCard, bay + new Vector3(0f, 2.1f, 0f), 2.6f);
            Lasers = new LaserRain(ctx, new LaserRainOptions { Name = "Gallery", Center = bay + new Vector3(0f, 5f, 0f), Size = new Vector2(6f, 5f), Range = 5f });
        }

        // The machine of Level 5: a fan that cannot be picked up, blowing along +X.
        void BuildWind(LevelContext ctx)
        {
            Fan = ctx.AddStatic(ToyFactory.DeskFan(null, false), new Vector3(-22.5f, 0f, 8f), Quaternion.Euler(0f, 90f, 0f));
            Fan.transform.localScale = Vector3.one * 3.5f;
            Wind = new WindStream(ctx, new WindStreamOptions
            {
                Name = "Gallery", Center = WindBay, Size = new Vector3(3.4f, 3.4f, 9.6f), Rotation = Quaternion.Euler(0f, 90f, 0f), Direction = Vector3.right,
            });
        }

        // A plug on a pedestal and three things to hold it against: close (too small), middling (fits), far
        // (too big). Beside it three sockets in the three states a socket's lamp has.
        void BuildGauge(LevelContext ctx)
        {
            ctx.AddStatic(BasicToys.Slab(new Vector3(0.7f, 1.3f, 0.7f)), new Vector3(2.2f, 0.65f, 0.4f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(0.8f, 3.2f, 0.8f)), new Vector3(-1.9f, 1.6f, 1f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(1.2f, 3f, 1.2f)), new Vector3(0f, 1.5f, 5f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(16f, 6f, 0.6f)), new Vector3(1f, 3f, 12f));
            Plug = ToyCatalog.Add(ctx, ToyId.WoodenBlock, new Vector3(2.2f, 1.55f, 0.4f), 0.5f, null, o =>
            {
                o.Name = "Plug";
                o.Tags = new[] { "plug" };
            });
            Gauge = new FitGauge(ctx, new FitGaugeOptions
            {
                Name = "Gallery", MinScale = 0.75f, MaxScale = 1.25f, Tag = "plug",
                Near = Zone.MinMax(new Vector3(-7f, 0f, -8f), new Vector3(9f, 8f, 13f)), Lamp = new Vector3(0f, 4.7f, 5f), LampRadius = 0.25f,
            });

            WaitingSocket = BuildSocket(ctx, "Waiting", 4f, 0f);
            RefusingSocket = BuildSocket(ctx, "Refusing", 6.2f, 0.5f);
            SeatedSocket = BuildSocket(ctx, "Seated", 8.4f, 1f);
        }

        Socket BuildSocket(LevelContext ctx, string name, float x, float pegScale)
        {
            const float z = 4f;
            ctx.AddStatic(BasicToys.Slab(new Vector3(1.3f, 0.2f, 1.3f)), new Vector3(x, 0.1f, z));
            ctx.AddStatic(BasicToys.Slab(new Vector3(0.12f, 1.7f, 0.12f)), new Vector3(x, 0.85f, z + 0.9f));
            var socket = new Socket(ctx, new SocketOptions
            {
                Name = name, AcceptTag = "peg", Capture = Zone.Box(new Vector3(x, 0.9f, z), new Vector3(1.3f, 1.4f, 1.3f)), MinScale = 0.8f, MaxScale = 1.2f,
                SeatPose = s => new Pose(new Vector3(x, 0.2f + 0.5f * s, z), Quaternion.identity), Lamp = new Vector3(x, 1.9f, z + 0.9f), LampRadius = 0.2f,
            });
            if (pegScale > 0f)
            {
                ToyCatalog.Add(ctx, ToyId.WoodenBlock, new Vector3(x, 0.2f + 0.5f * pegScale + 0.02f, z), Quaternion.Euler(0f, 20f, 0f), pegScale, null, o =>
                {
                    o.Name = "Peg " + name;
                    o.Tags = new[] { "peg" };
                });
            }
            return socket;
        }

        // A walled basin low enough to look into and a free-standing column of water beside it; the director
        // moves water from the one to the other for a while, so both levels are seen to move. In front, three
        // sponges side by side: one on the dry floor, one in a puddle too small to fill it, one in plenty.
        void BuildWater(LevelContext ctx)
        {
            Vector3 bay = WaterBay;
            ctx.AddStatic(BasicToys.Slab(new Vector3(5.6f, 1.2f, 0.3f)), bay + new Vector3(0f, 0.6f, -2.65f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(5.6f, 1.2f, 0.3f)), bay + new Vector3(0f, 0.6f, 2.65f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(0.3f, 1.2f, 5f)), bay + new Vector3(-2.65f, 0.6f, 0f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(0.3f, 1.2f, 5f)), bay + new Vector3(2.65f, 0.6f, 0f));
            Basin = new WaterVolume(ctx, new WaterVolumeOptions
            {
                Name = "Basin", Footprint = Zone.Box(bay + new Vector3(0f, 0.6f, 0f), new Vector3(5f, 1.2f, 5f)), FloorY = 0f, Area = 25f, Volume = 25f,
            });
            Tower = new WaterVolume(ctx, new WaterVolumeOptions
            {
                Name = "Tower", Footprint = Zone.Cylinder(bay.x + 5.5f, bay.z, 1.3f, 0f, 5f), FloorY = 0f, Area = Mathf.PI * 1.69f, Volume = 2f, MaxSurfaceY = 4.5f,
            });
            Puddle = new WaterVolume(ctx, new WaterVolumeOptions
            {
                Name = "Puddle", Footprint = Zone.Box(new Vector3(bay.x, 0.2f, 3.2f), new Vector3(2.3f, 0.4f, 2.3f)), FloorY = 0f, Area = 5.29f, Volume = 0.9f,
            });
            Pool = new WaterVolume(ctx, new WaterVolumeOptions
            {
                Name = "Pool", Footprint = Zone.Box(new Vector3(bay.x + 2.6f, 0.2f, 3.2f), new Vector3(2.3f, 0.4f, 2.3f)), FloorY = 0f, Area = 5.29f, Volume = 3.4f,
            });
            var volumes = new[] { Basin, Puddle, Pool };
            float rest = ToyCatalog.Get(ToyId.Sponge).RestHeight;
            Prop dry = ToyCatalog.Add(ctx, ToyId.Sponge, new Vector3(bay.x - 2.6f, rest * 1.8f, 3.2f), Quaternion.Euler(0f, 12f, 0f), 1.8f, Palette.Lemon, o => o.Name = "Dry Sponge");
            Prop half = ToyCatalog.Add(ctx, ToyId.Sponge, new Vector3(bay.x, rest * 1.8f, 3.2f), Quaternion.Euler(0f, 12f, 0f), 1.8f, Palette.Lemon, o => o.Name = "Half Sponge");
            Prop soaked = ToyCatalog.Add(ctx, ToyId.Sponge, new Vector3(bay.x + 2.6f, rest * 1.8f, 3.2f), Quaternion.Euler(0f, 12f, 0f), 1.8f, Palette.Lemon, o => o.Name = "Soaked Sponge");
            DrySponge = new Sponge(ctx, new SpongeOptions { Name = "Dry", Prop = dry, Volumes = volumes });
            HalfSponge = new Sponge(ctx, new SpongeOptions { Name = "Half", Prop = half, Volumes = volumes });
            SoakedSponge = new Sponge(ctx, new SpongeOptions { Name = "Soaked", Prop = soaked, Volumes = volumes });
        }

        // A door smaller than the player near the front and one bigger further back; a pad that fetches the
        // small one.
        void BuildPortals(LevelContext ctx)
        {
            Prop small = ToyCatalog.Add(ctx, ToyId.Doorway, PortalBay, 0.6f, null, o => o.Name = "Small Door");
            Prop big = ToyCatalog.Add(ctx, ToyId.Doorway, new Vector3(33f, 0f, 12f), 2.2f, null, o => o.Name = "Big Door");
            // Something to see through them: beyond a small door these loom, beyond a big one they are trinkets.
            ctx.AddProp(BasicToys.Block(1f, Palette.Grape), new Vector3(26.6f, 0.5f, 9.6f), Quaternion.Euler(0f, 25f, 0f), new PropOptions { Name = "Beyond A" });
            ctx.AddProp(BasicToys.Ball(0.5f, Palette.Lagoon), new Vector3(27.9f, 0.5f, 10.4f), new PropOptions { Name = "Beyond B" });
            ctx.AddProp(BasicToys.Block(new Vector3(1.2f, 2.4f, 1.2f), Palette.Tangerine), new Vector3(27.2f, 1.2f, 12.6f), new PropOptions { Name = "Beyond C" });
            ctx.AddProp(BasicToys.Block(2f, Palette.Lagoon), new Vector3(33f, 1f, 14.2f), Quaternion.Euler(0f, 30f, 0f), new PropOptions { Name = "Beyond D" });
            SmallDoor = new PortalDoorway(ctx, new PortalDoorwayOptions { Name = "Small", Prop = small });
            BigDoor = new PortalDoorway(ctx, new PortalDoorwayOptions { Name = "Big", Prop = big });
            Recall = new RecallPad(ctx, new RecallPadOptions { Name = "Gallery", Position = new Vector3(31f, 0f, 3f), Prop = small });
        }

        void BuildBounce(LevelContext ctx)
        {
            GameObject pad = ctx.AddStatic(BasicToys.Slab(new Vector3(4f, 0.4f, 4f), Materials.Gadget(GadgetPart.Metal)), BounceBay + new Vector3(0f, 0.2f, 0f));
            Bounce = new BouncePad(ctx, new BouncePadOptions { Name = "Gallery", Surface = pad.GetComponent<Collider>(), Scale = 2.4f });
        }

        void BuildSeesaw(LevelContext ctx)
        {
            ctx.AddStatic(BasicToys.Cylinder(0.5f, 3f), SeesawBay + new Vector3(0f, -0.5f, 0f), Quaternion.Euler(90f, 0f, 0f));
            Seesaw = new Seesaw(ctx, new SeesawOptions { Name = "Gallery", Pivot = SeesawBay, ArmADirection = Vector3.left, ArmA = 5f, ArmB = 2.5f, Width = 2f, SeatArm = 4.4f });
        }

        void BuildTrain(LevelContext ctx)
        {
            // The sleepers lie on the floor: the deck is as high above it as the wheels, rails and sleepers are.
            Train = new Train(ctx, new TrainOptions
            {
                Name = "Gallery", Center = TrainCentre, Radius = 6.5f, DeckY = 1.02f, Cars = 3, EngineArc = 40f, CarArc = 32f, StartBearing = 200f, StationBearing = 0f,
            });
        }

        void BuildBarricade(LevelContext ctx)
        {
            Wall = new Breakable(ctx, new BreakableOptions { Name = "Gallery", Center = BreakBay, Size = new Vector3(5f, 4f, 0.8f), MinMass = 5f, MinSpeed = 2.5f });
            // A ramp that comes down toward the wall.
            ctx.AddStatic(BasicToys.Ramp(6f, 3f, 3f), new Vector3(BreakBay.x, 1.5f, 22f), Quaternion.Euler(0f, 180f, 0f));
        }

        // The gallery's own stage direction: nothing here is a puzzle, things happen when somebody is there to see them.
        void Direct(LevelContext ctx, float dt)
        {
            Game game = ctx.Game;
            Player player = game.Player;

            // Water runs from the basin into the tower between the fourth and the eleventh second.
            if (ctx.Time >= 4f && ctx.Time < 11f) Tower.Add(Basin.Take(2.2f * dt));

            // Somebody has been on the seesaw for a moment: the striker comes down on the short arm.
            bool seated = GadgetKit.PlayerStandsOn(game, Seesaw.Mover.Body);
            seatedTicks = seated ? seatedTicks + 1 : 0;
            if (!strikerDropped && seatedTicks >= 24)
            {
                strikerDropped = true;
                Striker = ctx.AddProp(BasicToys.Block(2.6f, Palette.Birch), SeesawBay + new Vector3(1.7f, 4.2f, 0f),
                    new PropOptions { Name = "Striker", Density = 1.5f, Grabbable = false, Friction = 0.9f });
            }

            // Somebody stands where the barricade can be watched: the marble starts down the ramp.
            Vector3 toWatch = player.Position - BreakWatch;
            toWatch.y = 0f;
            if (!breakerRolled && toWatch.sqrMagnitude < BreakerStartsAt * BreakerStartsAt)
            {
                breakerRolled = true;
                Breaker = ToyCatalog.Add(ctx, ToyId.Marble, new Vector3(BreakBay.x, 4.2f, 19.6f), 2.4f, null, o =>
                {
                    o.Name = "Breaker";
                    o.Grabbable = false;
                });
            }
        }

        public override IEnumerator Solve(Bot bot)
        {
            Game game = bot.Game;

            yield return bot.Wait(0.5f);

            // The gauge: one plug, held against three backstops.
            yield return bot.WalkTo(GaugeStand, 0.15f);
            yield return bot.Grab(Plug);
            yield return bot.LookAt(NearBackstop);
            yield return bot.Wait(0.6f);
            Beat(game, "gauge-small");
            yield return bot.LookAt(MidBackstop);
            yield return bot.Wait(0.6f);
            Beat(game, "gauge-good");
            yield return bot.LookAt(FarBackstop);
            yield return bot.Wait(0.6f);
            Beat(game, "gauge-big");
            yield return bot.LookAt(MidBackstop);
            yield return bot.Drop();
            yield return bot.Wait(0.3f);

            // The seesaw: onto the seat at the long arm's tip; the striker does the rest.
            yield return bot.WalkTo(new Vector3(-23f, 0f, 24f), 0.5f, 20f, true);
            yield return bot.WalkTo(new Vector3(-22.6f, 0f, 28f), 0.3f);
            yield return bot.WalkTo(new Vector3(SeesawBay.x - 4.4f, 0f, 28f), 0.25f);
            yield return bot.LookAt(SeesawBay + new Vector3(2.5f, 1f, 0f));
            yield return bot.Until(() => Seesaw.Launched, 8f);
            Beat(game, "launch");
            yield return bot.Wait(1.3f);

            // The bounce pad: off the seesaw, a running jump onto the pad, and on over its far side.
            yield return bot.WalkTo(new Vector3(SeesawBay.x - 4.4f, 0f, 25f), 0.4f, 10f);
            yield return bot.WalkTo(BounceBay + new Vector3(-5.6f, 0f, 0f), 0.4f, 15f, true);
            yield return bot.WalkTo(BounceBay + new Vector3(-3.4f, 0f, 0f), 0.3f);
            yield return bot.Jump();
            yield return bot.Until(() => Bounce.BounceCount > 0, 3f);
            Beat(game, "bounce");

            // On to where the barricade can be watched: the marble is already rolling.
            yield return bot.WalkTo(BreakWatch, 0.5f, 20f, true);
            yield return bot.LookAt(BreakBay);
            yield return bot.Until(() => Wall.Broken, 10f);
            Beat(game, "break");
            yield return bot.Wait(0.6f);

            // The portals: a look at the small door from where it can be seen through, then the recall pad
            // fetches it to where the player stands.
            yield return bot.WalkTo(new Vector3(15.5f, 0f, 17.5f), 0.6f, 20f, true);
            yield return bot.WalkTo(new Vector3(24f, 0f, 14f), 0.6f, 20f, true);
            yield return bot.WalkTo(new Vector3(24.5f, 0f, 4f), 0.5f, 15f, true);
            yield return bot.WalkTo(new Vector3(27.3f, 0f, 2.2f), 0.3f);
            yield return bot.LookAt(SmallDoor.OpeningCenter);
            yield return bot.Wait(0.6f);
            Beat(game, "portal");
            yield return bot.WalkTo(Recall.Position, 0.2f, 15f);
            yield return bot.Until(() => Recall.Recalls > 0, 4f);
            Beat(game, "recall");
            yield return bot.LookAt(SmallDoor.OpeningCenter);
            yield return bot.Wait(0.6f);

            // The door in the hand (taken by its lintel: the opening has nothing to take hold of). It is no
            // portal there, and its opening is closed: nothing of the room shows through a frame that is carried.
            Prop door = SmallDoor.Prop;
            float lintel = ToyFactory.DoorwayOpening.y + (ToyFactory.DoorwaySize.y - ToyFactory.DoorwayOpening.y) * 0.5f;
            yield return bot.GrabAt(door, door.Position + door.Rotation * new Vector3(0f, lintel * door.Scale, 0f));
            yield return bot.LookAt(Recall.Position + new Vector3(6f, 0f, 1.2f));
            yield return bot.Wait(0.5f);
            Beat(game, "door-held");
            yield return bot.Drop();
            yield return bot.Wait(0.3f);

            // Out: the exit is a few steps behind the pad, away from the door that has just arrived.
            yield return bot.WalkTo(ExitPoint, 0.5f, 15f, true);
            yield return bot.Until(() => game.LevelCompleted, 3f);
        }

        void Beat(Game game, string name) => beats.Add(new KeyValuePair<string, float>(name, game.Time));
    }
}
