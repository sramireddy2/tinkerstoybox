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
    /// Level 7, "The Teeter-Totter" (LEVELS.md): a lever - you are one of the weights, and you have to be
    /// in place when the other one lands.
    ///
    /// The corner of a bookcase shelf. A ruler lies across a fat marker at the foot of a tall stack of
    /// books; its long end is down, with a bullseye painted on it. A pebble sits on a bobbin beside that
    /// end. Picked up, carried to the bullseye and held against the dashed outline on the far wall it is a
    /// boulder four and a half units across - a hundred and fifty times the player's weight. Let go, it
    /// drops onto the short end, and the long end throws whoever stands on it up past the spines of the
    /// books and onto the top of the stack, where the exit is.
    ///
    /// The rule is the seesaw gadget's and is closed-form: f = (M - 2 m) / (M + 4 m) with M the boulder
    /// and m the player (3), and a rider x from the pivot leaves with 2 sqrt(2 g f h) x / 10 (h = 2.7).
    /// From the bullseye (x = 9.3) the top of the books (11) wants a boulder of about 3 across; see
    /// <see cref="LeastMass"/>.
    ///
    /// What the level does about tries that go wrong (tools/out/notes/level07-build.md, level07-review.md):
    /// - The player starts on a book beside the bobbin with the pebble at eye height under the crosshair,
    ///   so the first click is a close one (LEVELS starts them five units away, from where the pebble can
    ///   never be made heavy enough).
    /// - A pebble that looks too small in the hand is told so at the pick-up, in the campaign's words; a
    ///   boulder picked up again from so near that it looks too big to get to the far wall is told that.
    /// - A pebble that has been let go and is of no use where it is - anywhere but on the high end of the
    ///   ruler, or there but too light - goes back to its bobbin after a second and a half, with one line
    ///   saying why. So no try has to be undone by hand, and nothing can be lost or soft-locked.
    /// - A boulder that came down with nobody on the low end, or with the player too far in from the tip,
    ///   or after they walked off the ruler while it swung, or that threw them high enough only for them
    ///   to come straight back down, gets its own line; and as the ruler begins to swing under a boulder
    ///   that is heavy enough the level says what the flight wants. (A jump while the ruler swings does
    ///   not lose the throw, and a boulder picked up again in mid-swing calls the throw off: both are the
    ///   seesaw's own rules.)
    /// - The spines of the books stand against the ruler's edge, so the push toward the books may begin
    ///   at any time from the click to about 0.9 s after the throw: leaning on the spines is standing on
    ///   the ruler, and the throw takes the player up along them and over the top.
    /// </summary>
    [Level(7, "teeter-totter", "The Teeter-Totter", Phase = 2)]
    public sealed class Level07TeeterTotter : LevelDefinition
    {
        // ---- Layout (LEVELS.md, Level 7; the shelf is y = 0, the ruler runs along Z) ------------------------
        public const float HalfWidth = 10f, NearZ = -6f, BackstopZ = 15.5f, WallTop = 20f;
        const float WallThickness = 1f;
        /// <summary>
        /// The stack of books the exit is on: x 1.7..10, z -6..5, sheer faces. The spines stand a fifth of a
        /// unit from the ruler's edge (LEVELS: half a unit). Measured with the half-unit strip: a player who
        /// pushed toward the books while the ruler was still swinging walked off its edge into the strip and
        /// was not thrown, or was flung six units forward past the books, and one who came down in the strip
        /// stood tucked under the ruler's edge. With the spines this close a player who leans on them still
        /// has both feet on the ruler (the capsule's axis stops 0.1 inside its edge), is thrown, and slides
        /// up the spines and over the top.
        /// </summary>
        public const float TowerMinX = 1.7f, TowerMaxZ = 5f, TowerTop = 11f;

        /// <summary>The pivot line: the top of the marker.</summary>
        public static readonly Vector3 Pivot = new Vector3(0f, 1.8f, 10f);
        public const float MarkerRadius = 0.9f, MarkerLength = 4f;
        /// <summary>The ruler: 15 long, 3 wide, 0.2 thick; the long arm points at -Z and lies on the shelf.</summary>
        public const float LongArm = 10f, ShortArm = 5f, RulerWidth = 3f, RulerThickness = 0.2f;
        /// <summary>The bullseye's distance from the pivot, along the long arm.</summary>
        public const float SeatArm = 9.3f;

        /// <summary>The bobbin the pebble sits on, an arm's length from the bullseye.</summary>
        public static readonly Vector3 BobbinBase = new Vector3(-2.1f, 0f, 0.7f);
        public const float BobbinRadius = 0.25f, BobbinHeight = 2.3f;
        public const float StartScale = 0.6f, MinScale = 0.2f, MaxScale = 7f;
        public const string WeightTag = "weight";

        /// <summary>
        /// The middle of the outline painted on the far wall: what to hold the pebble against. LEVELS paints it
        /// at a height of 6.6 (a view pitch of 18 degrees from the bullseye) and has its own solver aim at 7.5.
        /// Measured: the pebble that looks biggest (picked up from the bobbin's foot) lands on the player's own
        /// end of the ruler below about 16 degrees, so an aim half a unit under the old centre failed with it.
        /// At 8 (23 degrees) every sensible pick-up works anywhere in the middle of the outline.
        /// </summary>
        public static readonly Vector3 OutlineCentre = new Vector3(0f, 8f, BackstopZ);
        /// <summary>The outline is the boulder at this size.</summary>
        public const float OutlineScale = 4.6f;

        public static readonly Vector3 ExitCentre = new Vector3(7f, 12.25f, -2f);
        public static readonly Vector3 ExitSize = new Vector3(3f, 2.5f, 3f);

        // ---- The book the player starts on -------------------------------------------------------------------
        /// <summary>
        /// A book lying flat beside the bobbin. Standing on it the pebble is at eye height; its edge keeps
        /// the player <see cref="StepGap"/> from the bobbin's axis, so the pebble never looks bigger than 0.48
        /// from up here (from the shelf at the bobbin's foot it is 1.05 above the eye: 0.5 at most).
        /// </summary>
        public const float StepTop = 0.9f, StepDepth = 4f, StepWidth = 3f, StepYaw = 38f, StepGap = 1.25f;
        /// <summary>From the bobbin's axis to the start: eye to pebble 1.75, scale / distance 0.343 (LEVELS' own pick-up).</summary>
        public const float PickupDistance = 1.74f;
        /// <summary>The view at the start: the crosshair on the pebble.</summary>
        public const float SpawnPitch = 0f;
        static readonly Vector3 StepForward = Quaternion.Euler(0f, StepYaw, 0f) * Vector3.forward;
        public static readonly Vector3 SpawnPoint = BobbinBase - StepForward * PickupDistance + Vector3.up * StepTop;
        /// <summary>The foot of the ruler's long end: where to walk on from.</summary>
        public static readonly Vector3 TipApproach = new Vector3(0f, 0f, -0.6f);

        // ---- The judge ---------------------------------------------------------------------------------------
        /// <summary>
        /// The rule's margin over the top of the books (LEVELS: an apex of 11.6 for a top at 11). A throw that
        /// high is "heavy enough" to the level: such a boulder stays on the ruler, a lighter one goes home.
        /// Measured, the least that can work is an apex of 11.05 (a boulder of 2.7 against the rule's 3.0),
        /// with the player pushing toward the books from the moment they leave the ruler.
        /// </summary>
        public const float Clearance = 0.6f;
        /// <summary>
        /// scale / distance below which the pebble looks too small in the hand. Measured, held against the
        /// outline from the bullseye: 0.217 comes out as the rule's 3.0, 0.19 as 2.7 (the least that can
        /// work), from 0.165 down nothing works. Picked up from the bullseye itself the pebble is 0.26, from
        /// the start 0.34, from the bobbin's foot 0.5.
        /// </summary>
        public const float LooksTooSmall = 0.23f;
        /// <summary>
        /// scale / distance above which the pebble looks too big in the hand: held toward the far wall from
        /// the bullseye it meets the player's own end of the ruler, or reaches its largest size, before it
        /// is over the high end. Only a boulder picked up again from near by looks like that (measured, held
        /// against the outline: 0.564 still works, 0.586 comes down on the player's own end); on its bobbin
        /// the pebble never looks bigger than 0.51.
        /// </summary>
        public const float LooksTooBig = 0.575f;

        /// <summary>Said whenever the pebble is picked up looking too small (the campaign's sentence).</summary>
        public const string FarLine = "It only ever gets as big as it looks. Pick it up from closer.";
        /// <summary>Said when the boulder is picked up again looking too big (from close by).</summary>
        public const string NearLine = "From this close it looks too big to reach the far wall. Pick it up from further back.";
        /// <summary>The boulder landed on the high end and was too light, after a pick-up from too far off.</summary>
        public const string TooLightLine = "Too light to throw you up there. It is back on its bobbin: pick it up from closer.";
        /// <summary>The boulder landed on the high end and was too light because it stopped before the wall.</summary>
        public const string ShortLine = "Too light: it grows until it touches. Hold it against the outline on the far wall.";
        public const string NobodyLine = "Nobody was standing on the low end. Stand on the bullseye, then let go.";
        public const string OffSeatLine = "The very end of the ruler is thrown highest. Stand in the middle of the bullseye.";
        /// <summary>The boulder was let go again while the ruler was still swinging back.</summary>
        public const string EarlyLine = "The ruler was not back down yet. Wait for it, then let go.";
        public const string OwnEndLine = "That was your own end of the ruler. Hold the pebble against the far wall.";
        /// <summary>It came down on the player's own end because it looked too big to get any further.</summary>
        public const string TooBigLine = "It looked too big to get past your end of the ruler. It is back on its bobbin.";
        /// <summary>The pebble was let go where it had stopped against the books (held from right beside them, or at them).</summary>
        public const string BooksLine = "It touched the tall books first. Hold it clear of them, against the far wall.";
        /// <summary>The boulder was heavy enough and the player was on the ruler when it landed, but not when the ruler had swung.</summary>
        public const string LeftLine = "The ruler came up without you. Stay on it until it throws you.";
        /// <summary>The player stood on the short end and put the pebble on the long one.</summary>
        public const string WrongEndLine = "The other way round. You stand on the long end; the pebble lands on the short one.";
        public const string ClampLine = "That is as big as it gets, and it stopped short. It is back on its bobbin.";
        public const string MissedLine = "It has to land on the high end of the ruler. The pebble is back on its bobbin.";
        public const string BackLine = "The pebble is back on its bobbin.";
        /// <summary>Said as the ruler begins to swing with the player on it and a boulder that is heavy enough.</summary>
        public const string FlyLine = "Here it comes. Push toward the tall books while you are in the air.";
        /// <summary>Said when the player was thrown high enough and came down beside the books all the same.</summary>
        public const string SteerLine = "High enough. While you are in the air, push toward the tall books.";

        public Prop Pebble { get; private set; }
        public Seesaw Seesaw { get; private set; }
        public Exit Exit { get; private set; }
        /// <summary>Brings a pebble that is of no use where it lies back to the bobbin.</summary>
        public PropLeash Leash { get; private set; }
        /// <summary>Where the pebble sits on its bobbin (its centre).</summary>
        public Vector3 Perch { get; private set; }
        /// <summary>The middle of the bullseye, on the ruler's top, while the ruler is at rest.</summary>
        public Vector3 SeatPoint { get; private set; }
        /// <summary>The lightest boulder that throws a player on the bullseye over the top of the books.</summary>
        public float LeastMass { get; private set; }
        /// <summary>The scale of a pebble of <see cref="LeastMass"/>.</summary>
        public float LeastScale { get; private set; }

        float pebbleDensity;
        /// <summary>scale / distance of the last pick-up.</summary>
        float ratio;
        /// <summary>The last drop was at the pebble's largest size.</summary>
        bool clamped;
        /// <summary>The player is in the air after a launch.</summary>
        bool flying;
        /// <summary>
        /// Of the last full swing: the rule threw the player high enough from where they stood; it would have
        /// from the bullseye; and they did come down on the books.
        /// </summary>
        bool highEnough, sure, madeIt;
        /// <summary>The ruler has been struck since the pebble was last let go.</summary>
        bool struck;
        /// <summary>A swing that should throw the player is under way; and it did throw them.</summary>
        bool awaiting, thrown;
        /// <summary>The swing is over and threw nobody: the player left the ruler during it. Said when they are back on their feet.</summary>
        bool leftBehind;
        int leftTicks;
        /// <summary>Thrown, but from nearer the pivot than they stood when the boulder landed: not high enough after all.</summary>
        bool cameIn;
        /// <summary>The pebble was let go touching the books.</summary>
        bool caught;
        int flightTicks;
        string lastLine;
        int lastSaid;
        // What the pebble was doing when the leash took it home.
        bool returnedFromStrike, returnedFromOwnEnd, returnedWrongWayRound;
        float returnedScale;

        public override string Blurb => "A lever only argues with something heavier than you.";

        public override string[] Hints => new[]
        {
            "Stand on the low end. Something has to land on the high end, and it has to weigh far more than you do.",
            "You have to be standing on the bullseye when it lands. Hold the pebble and look at the far wall: weight grows much faster than size.",
            "Pick the pebble up from the book beside it, stand on the bullseye, cover the dashed outline on the far wall with it, and let go. Then push toward the tall books until you stand on them.",
        };

        public override string Environment => "high-shelf";
        public override float GroundY => 0f;
        public override float KillY => -30f;

        public override void Build(LevelContext ctx)
        {
            Pebble = null;
            Seesaw = null;
            Exit = null;
            Leash = null;
            ratio = 0f;
            clamped = false;
            flying = false;
            highEnough = false;
            sure = false;
            madeIt = false;
            struck = false;
            awaiting = false;
            thrown = false;
            leftBehind = false;
            leftTicks = 0;
            cameIn = false;
            caught = false;
            flightTicks = 0;
            lastLine = null;
            lastSaid = -100000;
            returnedFromStrike = false;
            returnedFromOwnEnd = false;
            returnedWrongWayRound = false;
            returnedScale = 0f;

            BuildShelf(ctx);
            BuildTower(ctx);
            BuildStep(ctx);
            BuildKerbs(ctx);
            BuildDressing(ctx);

            // The marker the ruler lies across, and the bobbin with the pebble on top.
            ctx.AddStatic(ToyFactory.Marker(MarkerRadius, MarkerLength, Palette.Steel), new Vector3(Pivot.x, MarkerRadius, Pivot.z), Quaternion.Euler(0f, 90f, 0f));
            ctx.AddStatic(ToyFactory.ThreadSpool(BobbinRadius, BobbinHeight, grabbable: false), BobbinBase + Vector3.up * (BobbinHeight * 0.5f));

            ToyDef def = ToyCatalog.Get(ToyId.Pebble);
            Perch = BobbinBase + Vector3.up * (BobbinHeight + def.RestHeight * StartScale);
            Pebble = ToyCatalog.Add(ctx, ToyId.Pebble, Perch, StartScale, null, o =>
            {
                o.MinScale = MinScale;
                o.MaxScale = MaxScale;
                o.Tags = new[] { WeightTag };
                // A sphere on a flat top is only metastable: it stays put until it is picked up.
                o.FrozenUntilGrabbed = true;
            });
            pebbleDensity = Pebble.Mass / (StartScale * StartScale * StartScale);

            // The lever. Its plank is the gadget's own (a kinematic mover with tapered tips to walk on by);
            // the level only dresses it as a ruler.
            Seesaw = new Seesaw(ctx, new SeesawOptions
            {
                Name = "Ruler", Pivot = Pivot, ArmADirection = Vector3.back, ArmA = LongArm, ArmB = ShortArm,
                Width = RulerWidth, Thickness = RulerThickness, PlankBias = 1f, TipMargin = 1f, ReturnRate = 60f, SeatArm = SeatArm,
            });
            SeatPoint = Seesaw.PointAt(SeatArm, RulerThickness, Seesaw.RestAngle);
            DressRuler(ctx);

            // The lightest boulder that does it, from the gadget's own rule.
            float low = 0f, high = 4000f;
            for (int i = 0; i < 40; i++)
            {
                float middle = (low + high) * 0.5f;
                if (ApexFor(middle, SeatArm) >= TowerTop + Clearance) high = middle;
                else low = middle;
            }
            LeastMass = high;
            LeastScale = Mathf.Pow(LeastMass / pebbleDensity, 1f / 3f);

            BuildPaint(ctx);

            Seesaw.SeesawStruck += (load, f) => OnStruck(ctx, f);
            Seesaw.SeesawLaunched += (rider, speed) =>
            {
                if (rider != null) return;
                flying = true;
                thrown = true;
                flightTicks = 0;
                // Where the player stood when the boulder landed is not always where they are thrown from:
                // half a second of walking toward the marker costs a third of the height.
                float reach = ctx.Game.Player.Position.y + speed * speed / (2f * Game.Gravity);
                cameIn = highEnough && reach < TowerTop + 0.3f;
                if (cameIn) highEnough = false;
            };

            // No strand, no restart: the pebble goes home whenever it has been let go and is of no use where
            // it is. The only place it is of use is the high end of the ruler, heavy enough.
            Leash = new PropLeash(ctx, new PropLeashOptions
            {
                Name = "Bobbin", Props = new[] { Pebble },
                // Everywhere: at home the pebble is frozen and the leash leaves it alone. Let go on the bobbin
                // at another size it is put back at its own (a crumb left there could never be made heavy).
                Forbidden = new[] { Zone.Sphere(Perch, 500f) },
                Unless = Useful,
                Grace = 1.5f, Action = LeashAction.Respawn,
                OnReturn = prop =>
                {
                    returnedScale = prop.Scale;
                    returnedFromStrike = OnStrikeArm(prop);
                    returnedFromOwnEnd = !returnedFromStrike && OverLongArm(prop.Center);
                    returnedWrongWayRound = returnedFromOwnEnd && GadgetKit.PlayerStandsOn(ctx.Game, Seesaw.Mover.Body) && ArmOf(ctx.Game.Player) < 0f;
                },
            });
            // (To somebody who is standing on the books already there is nothing to explain.)
            Leash.PropReturned += prop => Say(ctx, OnTheBooks(ctx.Game.Player) ? BackLine : ReturnLine(), 6f);

            // The one way a try goes quietly wrong: taken from across the shelf the pebble looks tiny, and
            // it only ever gets as big as it looks.
            ctx.Game.Events.PropGrabbed += e =>
            {
                if (e.Prop != Pebble) return;
                ratio = e.OldScale / Mathf.Max(0.01f, e.GrabDistance);
                clamped = false;
                struck = false;
                madeIt = false;
                highEnough = false;
                sure = false;
                // Picked up again while the ruler swung under it: the seesaw calls that swing off (its rider
                // gets the little hop it had in it). That was no try, and there is nothing to say about it.
                awaiting = false;
                leftBehind = false;
                cameIn = false;
                if (ratio < LooksTooSmall) Say(ctx, FarLine, 6f);
                else if (ratio > LooksTooBig) Say(ctx, NearLine, 6f);
            };
            ctx.Game.Events.PropDropped += e =>
            {
                if (e.Prop != Pebble) return;
                clamped = e.NewScale >= MaxScale - 0.01f;
                struck = false;
                caught = TouchesTheBooks(e.Prop.Center, e.NewScale * 0.5f);
                // Let go over the high end, heavy enough, with the player in place: it is on its way down
                // (half a second of falling, half a second of swing). What the flight wants is said now, while
                // there is time to read it; the strike says the same and is not heard twice.
                if (!caught && Seesaw.State == SeesawState.Rest && OverShortArm(e.Prop.Center) && GadgetKit.PlayerStandsOn(ctx.Game, Seesaw.Mover.Body))
                {
                    float arm = ArmOf(ctx.Game.Player);
                    if (arm > 0f && ApexFor(MassAt(e.NewScale), arm) >= TowerTop + Clearance) Say(ctx, FlyLine, 5f);
                }
            };
            ctx.Game.Events.PlayerLanded += e =>
            {
                if (flying || leftBehind) OnLanded(ctx, e.Position);
            };
            ctx.OnUpdate(dt =>
            {
                // A flight that never ends with a landing (a respawn) is over all the same.
                if (flying && ++flightTicks > 360) flying = false;
                // The swing is over. If it threw nobody although the player was on the ruler when the boulder
                // landed, they walked off it in the half second between: say so. (A jump during the swing is
                // still a ride: the seesaw takes its rider along from under their feet and throws them.)
                if (awaiting && Seesaw.State != SeesawState.Swing)
                {
                    awaiting = false;
                    leftBehind = !thrown;
                    leftTicks = 0;
                }
                // (A jump late in the swing takes the ruler's speed along and can still end on the books: the
                // line waits until the player is back on their feet somewhere else.)
                if (leftBehind)
                {
                    Player player = ctx.Game.Player;
                    if (!player.Grounded) leftTicks = 0;
                    else if (++leftTicks >= 12) OnLanded(ctx, player.Position);
                }
            });

            Exit = ctx.AddExit(ExitCentre, ExitSize);
            ctx.SetSpawn(SpawnPoint, StepYaw, SpawnPitch);
        }

        // ---- The rule ----------------------------------------------------------------------------------------

        /// <summary>
        /// How high the feet of a rider get who stands <paramref name="arm"/> from the pivot on the long end
        /// when a boulder of this mass comes down on the short end (the gadget's closed form).
        /// </summary>
        public float ApexFor(float mass, float arm) => ApexOf(Seesaw.F(mass, Player.Mass), arm);

        // share: the part of the full swing's speed this swing has (1 from rest; less if the ruler was struck
        // on its way back, when the high end has less far to fall).
        float ApexOf(float f, float arm, float share = 1f)
        {
            arm = Mathf.Clamp(arm, 0f, LongArm);
            float speed = Seesaw.LaunchSpeed(f, arm) * share;
            return Seesaw.PointAt(arm, RulerThickness, Seesaw.TippedAngle).y + speed * speed / (2f * Game.Gravity);
        }

        /// <summary>The mass of the pebble at a scale.</summary>
        public float MassAt(float scale) => pebbleDensity * scale * scale * scale;

        /// <summary>Is the pebble where it does its job: lying on the high end of the ruler, heavy enough?</summary>
        public bool Useful()
        {
            // Not while the ruler swings or the player is in the air: the try is not over.
            if (Seesaw.State == SeesawState.Swing || flying) return true;
            // On the high end and heavy enough - by the rule, or because it has just shown that it is.
            return (madeIt || highEnough || Pebble.Mass >= LeastMass) && OnStrikeArm(Pebble);
        }

        // The seesaw counts the prop as load on its short arm (it touches the plank there).
        bool OnStrikeArm(Prop prop)
        {
            Vector3 centre = prop.Center;
            return centre.z > Pivot.z + ShortArm * 0.1f && Seesaw.StrikeLoad >= prop.Mass - 0.01f;
        }

        // How far out along the long arm the player's feet are, measured from the pivot (negative: on the short arm).
        float ArmOf(Player player) => Vector3.Dot(player.Position - Pivot, Seesaw.PointAt(1f, 0f, Seesaw.Angle) - Pivot);

        // Over the high end of the ruler: where a boulder that is let go comes down on it (the gadget's footprint).
        static bool OverShortArm(Vector3 point) =>
            Mathf.Abs(point.x - Pivot.x) <= RulerWidth * 0.5f + 0.9f && point.z > Pivot.z + ShortArm * 0.1f && point.z <= Pivot.z + ShortArm + 1f;

        // Over the player's own arm of the ruler, or just off its edge (the bobbin stands 0.6 from that edge and
        // is not "the ruler": a pebble that lies on it or at its foot has simply been put down).
        static bool OverLongArm(Vector3 point) =>
            Mathf.Abs(point.x - Pivot.x) <= RulerWidth * 0.5f + 0.3f && point.z <= Pivot.z + ShortArm * 0.1f && point.z >= Pivot.z - LongArm - 1f;

        // The boulder has come down on the high end and the ruler goes over.
        void OnStruck(LevelContext ctx, float f)
        {
            // The player walking out along the short arm tips it too: that is not a try.
            if (Pebble.Held || !OnStrikeArm(Pebble)) return;
            // Only the first strike after the pebble was let go is the try. Once the ruler is down, a boulder
            // that shifts on it "strikes" again (it settles for a moment after the swing and the gadget counts
            // that as a second strike): that is nothing.
            if (struck) return;
            struck = true;
            bool riding = GadgetKit.PlayerStandsOn(ctx.Game, Seesaw.Mover.Body);
            float arm = ArmOf(ctx.Game.Player);
            // How far the high end still has to come down: all the way from rest, less if the ruler was on its
            // way back up - and the throw is as much weaker as the square root of that.
            float travel = Pivot.y + ShortArm * Mathf.Sin(Seesaw.Angle * Mathf.Deg2Rad);
            float share = Mathf.Sqrt(Mathf.Clamp01(travel / Seesaw.StrikeTravel));
            highEnough = false;
            sure = false;
            awaiting = false;
            thrown = false;
            leftBehind = false;
            cameIn = false;
            if (!riding || arm <= 0f)
            {
                Say(ctx, NobodyLine, 6f);
                return;
            }
            float needed = TowerTop + Clearance;
            highEnough = ApexOf(f, arm, share) >= needed;
            sure = ApexOf(f, SeatArm) >= needed;
            // What the flight wants (already said if the boulder was let go from here and fell onto the ruler).
            if (highEnough)
            {
                Say(ctx, FlyLine, 4f);
                awaiting = true;
            }
            // It would have done from rest: the pebble was let go again while the ruler was still coming back.
            else if (share < 0.97f && ApexOf(f, arm) >= needed) Say(ctx, EarlyLine, 6f);
            // Heavy enough for somebody on the bullseye, and the player was not on it.
            else if (sure) Say(ctx, OffSeatLine, 6f);
            // Too light is told when the pebble goes home.
        }

        // The player has come down after a swing that should have thrown them onto the books.
        void OnLanded(LevelContext ctx, Vector3 at)
        {
            bool left = leftBehind;
            flying = false;
            leftBehind = false;
            if (at.y > LandedHeight)
            {
                // On the books: whatever the rule made of it, that boulder did it.
                madeIt = true;
                return;
            }
            // Not on the ruler when it had swung: it threw nobody.
            if (left) Say(ctx, LeftLine, 6f);
            // Thrown high enough, and back down beside the books: the one thing left to say.
            else if (highEnough) Say(ctx, SteerLine, 6f);
            // Thrown from nearer the marker than they stood at first.
            else if (cameIn) Say(ctx, OffSeatLine, 6f);
        }

        /// <summary>Feet above this are on top of the books.</summary>
        const float LandedHeight = TowerTop - 0.5f;

        static bool OnTheBooks(Player player) => player.Grounded && player.Position.y > LandedHeight;

        // Is a ball of this size at this place against the stack of books from outside (its spines, its end, its
        // top edge)? One that lies on top of them is not.
        static bool TouchesTheBooks(Vector3 centre, float radius)
        {
            if (centre.x >= TowerMinX && centre.z <= TowerMaxZ) return false;
            float dx = Mathf.Max(0f, TowerMinX - centre.x), dy = Mathf.Max(0f, centre.y - TowerTop), dz = Mathf.Max(0f, centre.z - TowerMaxZ);
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz) < radius + 0.12f;
        }

        // Why the pebble went back to the bobbin (null: nothing to say).
        string ReturnLine()
        {
            if (madeIt) return null;
            if (returnedFromStrike) return ratio > 0f && ratio < LooksTooSmall ? TooLightLine : ShortLine;
            if (returnedWrongWayRound) return WrongEndLine;
            // (A pebble that looked too big is told that, whatever it met first: the ruler, the books or its own limit.)
            if (returnedFromOwnEnd) return ratio > LooksTooBig ? TooBigLine : clamped ? ClampLine : caught ? BooksLine : OwnEndLine;
            return caught ? BooksLine : returnedScale >= 1f ? MissedLine : BackLine;
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

        // ---- Geometry ----------------------------------------------------------------------------------------

        static GameObject Solid(string name, Vector3 size)
        {
            var solid = new GameObject(name) { layer = Layers.Default };
            solid.AddComponent<BoxCollider>().size = size;
            return solid;
        }

        // The corner of a bookcase shelf: the shelf's own top for a floor, the back of the bookcase for the
        // wall the pebble stops against, a side panel behind the books. Toward the room the shelf is open
        // (what stops the player and a held pebble there is not drawn; a kerb of things lies along it).
        void BuildShelf(LevelContext ctx)
        {
            float t = WallThickness, depth = BackstopZ - NearZ, midZ = (BackstopZ + NearZ) * 0.5f;
            ctx.AddStatic(Solid("Shelf Floor", new Vector3(2f * (HalfWidth + t), 1f, depth + 2f * t)), new Vector3(0f, -0.5f, midZ));

            Dip dip = ctx.Dip;
            // Boards, a tone lighter than the books: the outline has to be found on them from across the shelf.
            Material boards = Materials.Room(RoomRecipe.For(RoomSurface.LevelStatic, dip, new PatternSpec(RoomPattern.Planks, 2.5f, 60f, 0.08f)).With(r =>
            {
                r.Name = dip.Name + " Bookcase";
                r.Side = dip.Mid;
                r.Dado = dip.Mid;
            }));
            // The sun stands over the room's window wall (-X) and a little beyond the back of the bookcase:
            // the back panel would lay its shadow over the high end of the ruler, where the boulder's own
            // shadow is the cue (ART_BIBLE 5). It is drawn without casting one.
            GameObject back = ctx.AddStatic(BasicToys.Slab(new Vector3(2f * (HalfWidth + t), WallTop, t), boards), new Vector3(0f, WallTop * 0.5f, BackstopZ + t * 0.5f));
            foreach (MeshRenderer renderer in back.GetComponentsInChildren<MeshRenderer>()) renderer.shadowCastingMode = ShadowCastingMode.Off;
            ctx.AddStatic(BasicToys.Slab(new Vector3(t, WallTop, depth), boards), new Vector3(HalfWidth + t * 0.5f, WallTop * 0.5f, midZ));

            ctx.AddStatic(Solid("Open Side", new Vector3(t, WallTop, depth + 2f * t)), new Vector3(-HalfWidth - t * 0.5f, WallTop * 0.5f, midZ));
            ctx.AddStatic(Solid("Open Front", new Vector3(2f * HalfWidth, WallTop, t)), new Vector3(0f, WallTop * 0.5f, NearZ - t * 0.5f));
            SkyCap.Add(ctx, -HalfWidth - t, HalfWidth + t, NearZ - t, BackstopZ + t, WallTop);
        }

        static readonly float[] TowerBooks = { 2.1f, 1.5f, 2.3f, 1.4f, 2f, 1.7f };
        static readonly float[] TowerTones = { 0.9f, 1f, 0.86f, 0.96f, 0.9f, 1f };

        // The goal: six fat books lying on one another, spines toward the ruler. One box to the simulation,
        // with sheer faces and a square top edge.
        void BuildTower(LevelContext ctx)
        {
            Dip dip = ctx.Dip;
            float width = HalfWidth - TowerMinX, depth = TowerMaxZ - NearZ;
            var centre = new Vector3((TowerMinX + HalfWidth) * 0.5f, TowerTop * 0.5f, (NearZ + TowerMaxZ) * 0.5f);
            ctx.AddStatic(Solid("Book Tower", new Vector3(width, TowerTop, depth)), centre);

            Material cover = Materials.Room(RoomSurface.LevelStatic, dip);
            Material pages = Materials.Room(RoomSurface.Trim, dip);
            Material dotted = Materials.Room(RoomRecipe.For(RoomSurface.LevelStatic, dip, new PatternSpec(RoomPattern.Dots, 2f, 0.45f, 0f, 0.05f)));
            Material label = Paint(dip);
            var set = new Level07Set();
            float y = 0f;
            for (int i = 0; i < TowerBooks.Length; i++)
            {
                float h = TowerBooks[i];
                set.Book(cover, pages, new Vector3(centre.x, y + h * 0.5f, centre.z), new Vector3(width, h, depth), Level07Set.Side.NegX, Quaternion.identity,
                    TowerTones[i], i == TowerBooks.Length - 1 ? dotted : null);
                y += h;
            }
            // What makes a spine a spine: a band near its head and its foot, and a title label - on alternate
            // ends of the books, clear of the way up beside the bullseye.
            const float proud = 0.03f;
            float face = TowerMinX - proud * 0.5f;
            y = 0f;
            for (int i = 0; i < TowerBooks.Length; i++)
            {
                float h = TowerBooks[i];
                set.Box(label, new Vector3(face, y + 0.24f, centre.z), new Vector3(proud, 0.07f, depth - 0.3f));
                set.Box(label, new Vector3(face, y + h - 0.24f, centre.z), new Vector3(proud, 0.07f, depth - 0.3f));
                float tall = Mathf.Min(0.62f, h * 0.34f), wide = 1.5f + 0.25f * (i % 3);
                set.Box(label, new Vector3(face, y + h * 0.5f, i % 2 == 0 ? 3.5f : -3.7f), new Vector3(proud, tall, wide));
                y += h;
            }
            set.Finish(ctx, "Books");
        }

        // The dip's light tone on every face: what is painted on spines.
        static Material Paint(Dip dip) =>
            Materials.Room(new RoomRecipe { Name = dip.Name + " Paint", Top = dip.Light, Side = dip.Light, Dado = dip.Light });

        /// <summary>The middle of the step book's top.</summary>
        public static Vector3 StepCentre => BobbinBase - StepForward * (StepGap + StepDepth * 0.5f) + Vector3.up * StepTop;

        // The book the player starts on, askew beside the bobbin.
        void BuildStep(LevelContext ctx)
        {
            Dip dip = ctx.Dip;
            Quaternion turn = Quaternion.Euler(0f, StepYaw, 0f);
            var size = new Vector3(StepWidth, StepTop, StepDepth);
            Vector3 centre = StepCentre - Vector3.up * (StepTop * 0.5f);
            ctx.AddStatic(Solid("Step Book", size), centre, turn);

            Material cover = Materials.Room(RoomSurface.LevelStatic, dip);
            Material pages = Materials.Room(RoomSurface.Trim, dip);
            Material striped = Materials.Room(RoomRecipe.For(RoomSurface.LevelStatic, dip, new PatternSpec(RoomPattern.Stripes, 1.5f, 0f, 0f, 0.05f)));
            var set = new Level07Set();
            set.Book(cover, pages, centre, size, Level07Set.Side.NegX, turn, 0.95f, striped);
            set.Finish(ctx, "Step Book Looks");
        }

        // Along the two open sides of the shelf corner: two atlases lying flat, and a pencil. They lie
        // outside the level, behind what stops the player; nobody gets to them and no toy does.
        void BuildKerbs(LevelContext ctx)
        {
            Dip dip = ctx.Dip;
            Material cover = Materials.Room(RoomSurface.LevelStatic, dip);
            Material pages = Materials.Room(RoomSurface.Trim, dip);
            Material quilted = Materials.Room(RoomRecipe.For(RoomSurface.LevelStatic, dip, new PatternSpec(RoomPattern.Quilt, 1.6f, 0.06f, 0f, 0.05f)));
            Material striped = Materials.Room(RoomRecipe.For(RoomSurface.LevelStatic, dip, new PatternSpec(RoomPattern.Stripes, 1.5f, 0f, 0f, 0.05f)));
            var set = new Level07Set();
            const float split = 4.6f;
            float x = -HalfWidth;
            set.Book(cover, pages, new Vector3(x - 1.6f, 0.8f, (NearZ + split) * 0.5f), new Vector3(3.2f, 1.6f, split - NearZ), Level07Set.Side.PosX, Quaternion.identity, 0.92f, quilted);
            set.Book(cover, pages, new Vector3(x - 1.4f, 0.95f, (split + BackstopZ) * 0.5f), new Vector3(2.8f, 1.9f, BackstopZ - split), Level07Set.Side.PosX, Quaternion.identity, 1f, striped);

            // The pencil: a six-sided shaft lying on one of its flats, point toward the books.
            const float radius = 0.85f, shaft = 14.5f, cone = 2.4f, ferrule = 1.1f, rubber = 0.9f;
            float flat = radius * Mathf.Cos(Mathf.PI / 6f);
            var along = Quaternion.Euler(0f, 90f, 0f);
            var onSide = Quaternion.Euler(0f, 0f, -90f);
            float z = NearZ - radius - 0.02f, from = -HalfWidth - 0.4f;
            Material wood = Materials.Toy(ToyRecipe.PlainProp, Palette.Birch);
            Material paint = Materials.Toy(ToyRecipe.PlainProp, Palette.Kraft);
            Material steel = Materials.Toy(ToyRecipe.PlainProp, Palette.Steel);
            Material lead = Materials.Toy(ToyRecipe.PlainProp, Palette.Ink);
            Material gum = Materials.Toy(ToyRecipe.PlainProp, dip.Deep);
            Mesh hex = MeshKit.Cached(MeshKit.Key("Level07/PencilShaft", radius, shaft), () =>
            {
                var outline = new List<Vector2>();
                for (int i = 0; i < 6; i++)
                {
                    float a = Mathf.PI / 3f * i;
                    outline.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
                }
                return MeshKit.Extrude(outline, shaft, 0.03f);
            });
            Mesh tip = MeshKit.Cached(MeshKit.Key("Level07/PencilCone", flat, cone), () =>
                MeshKit.Lathe(new List<Vector2> { new Vector2(0f, 0f), new Vector2(flat, 0f), new Vector2(0.24f, cone * 0.8f) }, 24));
            Mesh point = MeshKit.Cached(MeshKit.Key("Level07/PencilLead", cone), () =>
                MeshKit.Lathe(new List<Vector2> { new Vector2(0f, cone * 0.8f), new Vector2(0.24f, cone * 0.8f), new Vector2(0f, cone) }, 24));
            set.Disc(gum, new Vector3(from + rubber * 0.5f, flat, z), radius * 0.92f, rubber, onSide);
            set.Disc(steel, new Vector3(from + rubber + ferrule * 0.5f, flat, z), radius * 0.98f, ferrule, onSide);
            float start = from + rubber + ferrule;
            set.Shape(paint, hex, new Vector3(start + shaft * 0.5f, flat, z), along);
            set.Shape(wood, tip, new Vector3(start + shaft, flat, z), onSide);
            set.Shape(lead, point, new Vector3(start + shaft, flat, z), onSide);
            set.Finish(ctx, "Kerbs");
        }

        // Nobody's toys in the corners: Birch blocks, well clear of the ruler, of the way to it and of the
        // line from the bullseye to the outline.
        void BuildDressing(LevelContext ctx)
        {
            var set = new Level07Set();
            // In the corner where the back of the bookcase meets the open side.
            Block(ctx, set, new Vector3(1.8f, 1.8f, 1.8f), new Vector3(-8.7f, 0f, 14.2f), 6f);
            Block(ctx, set, new Vector3(1.1f, 1.1f, 1.1f), new Vector3(-8.6f, 1.8f, 14.3f), -22f);
            Block(ctx, set, new Vector3(2.2f, 1.1f, 1.1f), new Vector3(-6.4f, 0f, 14.6f), -8f);
            // Behind the start, in the open corner.
            Block(ctx, set, new Vector3(1.4f, 1.4f, 1.4f), new Vector3(-8.8f, 0f, -4.9f), 14f);
            Block(ctx, set, new Vector3(0.9f, 0.9f, 1.8f), new Vector3(-7f, 0f, -5.1f), 71f);
            // Between the books and the back of the bookcase, against the side panel.
            Block(ctx, set, new Vector3(1.6f, 1.6f, 1.6f), new Vector3(9f, 0f, 14.4f), -9f);
            Block(ctx, set, new Vector3(1f, 1f, 1f), new Vector3(9.3f, 0f, 12.6f), 24f);
            set.Finish(ctx, "Clutter");
        }

        static void Block(LevelContext ctx, Level07Set set, Vector3 size, Vector3 foot, float yaw)
        {
            GameObject piece = ToyFactory.WoodenBlock(size, grabbable: false);
            ctx.AddStatic(piece, foot + Vector3.up * (size.y * 0.5f), Quaternion.Euler(0f, yaw, 0f));
            set.Absorb(piece);
        }

        // The gadget's plank is steel; this one is a wooden ruler with its marks along one edge, and the
        // bullseye round the gadget's lamp. Looks only, and they ride with the plank.
        void DressRuler(LevelContext ctx)
        {
            Transform plank = Seesaw.Mover.Transform;
            MeshRenderer body = plank.GetComponent<MeshRenderer>();
            if (body != null) body.sharedMaterial = Materials.Toy(ToyRecipe.PlainProp, Palette.Birch);

            // In the plank's own space z runs along the long arm from the pivot, y out of its top, and +x is
            // the side the bobbin is on.
            const float lift = 0.004f, taper = RulerThickness * 3f, spacing = 1f / 3f;
            float from = -ShortArm + taper + 0.1f, to = LongArm - taper - 0.1f;
            Mesh ticks = MeshKit.Cached(MeshKit.Key("Level07/RulerTicks", from, to, spacing), () =>
            {
                var parts = new List<MeshPart>();
                int count = Mathf.FloorToInt((to - from) / spacing);
                for (int i = 0; i <= count; i++)
                {
                    float length = i % 5 == 0 ? 0.36f : 0.2f;
                    var size = new Vector3(length, 0.008f, 0.05f);
                    parts.Add(new MeshPart(MeshKit.Box(size), new Vector3(RulerWidth * 0.5f - 0.07f - length * 0.5f, 0f, from + i * spacing)));
                }
                Mesh merged = MeshKit.Merge("Level07 Ruler Ticks", parts);
                foreach (MeshPart part in parts) MeshKit.Release(part.Mesh);
                return merged;
            });
            Material ink = Materials.Toy(ToyRecipe.PlainProp, Palette.Ink);
            GadgetKit.Visual(plank, "Ticks", ticks, ink, new Vector3(0f, RulerThickness + lift, 0f)).shadowCastingMode = ShadowCastingMode.Off;

            // The bullseye: the gadget's lamp is its middle (radius 0.6, 0.02 thick, 0.012 above the plank).
            var seat = new Vector3(0f, RulerThickness + lift, SeatArm);
            GadgetKit.Visual(plank, "Bullseye Inner", Level07Set.RingMesh(0.66f, 0.84f, 0.008f), Materials.Toy(ToyRecipe.PlainProp, Palette.Kraft), seat).shadowCastingMode = ShadowCastingMode.Off;
            GadgetKit.Visual(plank, "Bullseye Outer", Level07Set.RingMesh(0.9f, 1.08f, 0.008f), ink, seat).shadowCastingMode = ShadowCastingMode.Off;
            // A lamp on a gadget usually marks where a toy goes; this one is where the player goes. The shoe
            // prints say so: toes toward the far wall (the plank's -z).
            ShoePrints(plank, new Vector3(0f, RulerThickness + 0.026f, SeatArm), 180f, ink);
        }

        const float PadRadius = 0.62f;
        /// <summary>How much bigger than the least boulder its ring on the wall is painted (see BuildPaint).</summary>
        public const float LeastRingGain = 1.16f;
        static readonly float[] ChevronHeights = { 2.9f, 4.5f, 6.1f };

        // The size language of the campaign: the boulder at the size that works, dashed, on the wall it
        // stops against. And where to stand when picking the pebble up: a round pad with a pair of shoe
        // prints, in the room's own tones (the toy's colour is kept for where the toy goes).
        void BuildPaint(LevelContext ctx)
        {
            Dip dip = ctx.Dip;
            Color hero = ToyCatalog.Get(ToyId.Pebble).ColorIn(dip);
            Material fill = Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Light, hero, 0.38f)));
            Material line = Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Light, hero, 0.9f)));
            var set = new Level07Set();
            var facing = Quaternion.Euler(90f, 0f, 0f);
            float radius = OutlineScale * 0.5f;
            set.Disc(fill, OutlineCentre + Vector3.back * 0.012f, radius, 0.008f, facing);
            set.DashedCircle(line, OutlineCentre, Vector3.right, Vector3.up, radius, 0.2f, 20, 0.62f, 0.02f);
            // The least boulder that is sure to do it, dashed finer inside. A boulder hangs in front of the wall
            // by half its size, so from the bullseye it looks a sixth bigger than a ring of its size painted
            // on the wall: the ring is drawn that much bigger, and a pebble that covers it is heavy enough.
            float least = LeastScale * 0.5f * LeastRingGain;
            set.DashedCircle(line, OutlineCentre, Vector3.right, Vector3.up, least, 0.09f, 26, 0.5f, 0.02f);

            // Three chevrons up the spines beside the bullseye: that way. Low enough to be in the first picture.
            Material paper = Materials.Room(RoomSurface.Trim, dip);
            Vector3 normal = Vector3.left;
            float z = SeatPoint.z + 0.3f;
            foreach (float y in ChevronHeights)
            {
                var peak = new Vector3(TowerMinX, y + 0.75f, z);
                set.Stroke(paper, new Vector3(TowerMinX, y, z - 0.9f), peak, normal, 0.2f, 0.035f);
                set.Stroke(paper, new Vector3(TowerMinX, y, z + 0.9f), peak, normal, 0.2f, 0.035f);
            }
            set.Finish(ctx, "Paint", castShadows: false);

            Material pad = Materials.Room(RoomRecipe.Solid(dip.Deep));
            Material print = Materials.Room(RoomRecipe.Solid(Palette.Paper));
            StandMark(ctx, "Stand Mark", SpawnPoint, StepYaw, pad, print);
        }

        // Looks only: a renderer for the pad and one for each shoe, toes a little apart.
        static void StandMark(LevelContext ctx, string name, Vector3 at, float yaw, Material pad, Material print)
        {
            var mark = new GameObject(name);
            ctx.AddStatic(mark, at + Vector3.up * 0.012f, Quaternion.Euler(0f, yaw, 0f));
            Mesh disc = MeshKit.Cached(MeshKit.Key("Level07/Pad", PadRadius), () => MeshKit.Cylinder(PadRadius, 0.004f, 40));
            GadgetKit.Visual(mark.transform, "Pad", disc, pad).shadowCastingMode = ShadowCastingMode.Off;
            ShoePrints(mark.transform, new Vector3(0f, 0.006f, 0f), 0f, print);
        }

        // A pair of shoe prints lying flat on the parent's XZ plane, toes toward its +Z turned by yaw and a
        // little apart: the campaign's mark for "stand here".
        static void ShoePrints(Transform parent, Vector3 at, float yaw, Material print)
        {
            Mesh sole = MeshKit.Cached(MeshKit.Key("Level07/Sole"), () =>
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
            Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
            for (int side = -1; side <= 1; side += 2)
            {
                Quaternion flat = turn * Quaternion.Euler(0f, side * 9f, 0f) * Quaternion.Euler(90f, 0f, 0f);
                GadgetKit.Visual(parent, "Shoe", sole, print, at + turn * new Vector3(side * 0.24f, 0f, 0f), flat).shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // ---- The solution ------------------------------------------------------------------------------------

        public override IEnumerator Solve(Bot bot)
        {
            // The pebble is under the crosshair at the start, an arm's length away: a click.
            yield return bot.Grab(Pebble);
            yield return Mount(bot);
            // Cover the outline on the far wall with it and let go: it stops on that wall, a boulder.
            yield return bot.DropAt(OutlineCentre);
            yield return Fly(bot);
        }

        /// <summary>From anywhere on the shelf: round to the foot of the ruler and up it to the bullseye.</summary>
        public IEnumerator Mount(Bot bot)
        {
            yield return bot.WalkTo(TipApproach);
            yield return bot.WalkTo(SeatPoint, 0.12f);
            yield return bot.Until(() => GadgetKit.PlayerStandsOn(bot.Game, Seesaw.Mover.Body), 2f);
        }

        /// <summary>The second half: thrown, steer over the books, land, and walk into the exit.</summary>
        public IEnumerator Fly(Bot bot)
        {
            Game game = bot.Game;
            yield return bot.Until(() => Seesaw.Launched, 4f);
            // Push toward the books while in the air; the capsule slides up their spines and over the edge.
            yield return bot.WalkTo(new Vector3(TowerMinX + 3f, TowerTop, SeatPoint.z), 0.5f, 5f);
            yield return bot.Until(() => bot.Player.Grounded && bot.Player.Position.y > TowerTop - 0.5f, 4f);
            yield return bot.WalkTo(new Vector3(ExitCentre.x, TowerTop, ExitCentre.z), 0.5f, 8f);
            yield return bot.Until(() => game.LevelCompleted, 3f);
        }
    }
}
