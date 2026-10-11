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
    /// Level 10, "Matryoshka Boxes" (LEVELS.md): several toys from one, a clamp is the puzzle, stack them.
    ///
    /// A corner of the den under a low shelf with a child's height chart taped to its face; the way out is
    /// on the shelf, 3.9 up. The one toy is a red gift box - which turns out to be three: picked up, a
    /// yellow one is simply there where it stood, and under the yellow one a blue one. No box grows taller
    /// than the "2" of the chart (the two inner ones no taller than a hop, a little past its "1") and a
    /// hop is good for about one mark, so the boxes have to become a staircase: the red one as big as it
    /// gets against the foot of the shelf, a one-mark box on the rug in front of it, and a one-mark box
    /// on top of it at the back.
    /// </summary>
    [Level(10, "matryoshka-boxes", "Matryoshka Boxes", Phase = 3)]
    public sealed class Level10MatryoshkaBoxes : LevelDefinition
    {
        // ---- Layout (LEVELS.md, Level 10; rug y = 0, travel +Z, the shelf is at +Z) -------------------------
        public const float HalfWidth = 8f, NearZ = -6f, FarZ = 16f;
        /// <summary>The shelf's sheer face is the plane z = ShelfZ; its top is the goal.</summary>
        public const float ShelfZ = 12f, ShelfTop = 3.9f;
        public const float WallTop = 12f;
        const float WallThickness = 1f;
        /// <summary>The rug lies on the den's floor: the room's own floor is this far under the play plane.</summary>
        public const float RugThickness = 0.1f;

        // ---- The boxes --------------------------------------------------------------------------------------
        public const float RedStart = 1.2f, YellowStart = 0.9f, TealStart = 0.6f;
        /// <summary>The clamp is the puzzle: no box is taller than two marks of the chart.</summary>
        public const float MinScale = 0.3f, MaxScale = 2f;
        /// <summary>
        /// ... and the two that come out of the red one grow no taller than a hop (LEVELS.md's "largest
        /// rise", and the top end of its windows for the step and the top box). A box inside another cannot
        /// be as big as the one around it; and it is what makes two boxes no way up. LEVELS counted on a
        /// hop of 1.15 ("two boxes: 1.0 + 2.0 + 1.25 = 3.25"); the hop measures 1.30 to 1.35, and with every
        /// box free to be 2.0 two boxes of about 1.3 each, one on the other, were three hops to the shelf
        /// (tools/out/notes/level10-review3-probe-twoboxes-before.txt and -carryup-before.txt). Now the
        /// best two boxes do is 1.35 + 1.15 = 2.50: the shelf is 1.40 above that, and no hop gets up 1.40.
        /// It is also kinder: an inner box picked up from too near used to come out 1.4 to 1.8 and was told
        /// nothing until a hop at it failed; now it stops at a size that works as a step, or stops short
        /// of the shelf and is told to step closer.
        /// </summary>
        public const float InnerMaxScale = 1.15f;
        /// <summary>Where the three boxes stand at the start, one inside the next (the middle of their floor).</summary>
        public static readonly Vector3 Nest = new Vector3(0f, 0f, 0f);
        /// <summary>
        /// What a hop gets the player up, measured: every step of 1.30 was climbed, 1.35 from the rug and
        /// from box to box but not onto the shelf, 1.40 never. (LEVELS.md designs with 1.15, from a jump
        /// whose top is 1.25; the capsule's round foot rolls over the last tenth.)
        /// </summary>
        public const float HopRise = 1.3f;

        // ---- The solution -----------------------------------------------------------------------------------
        /// <summary>Where the player starts, and where the solver picks every box up from: a step in front of the nest.</summary>
        public static readonly Vector3 PickSpot = new Vector3(0f, 0f, -2.6f);
        public const float SpawnPitch = -13f;
        /// <summary>Where the solver lets every box go from: the shoe prints in front of the chart.</summary>
        public static readonly Vector3 StandSpot = new Vector3(-2f, 0f, 6.7f);
        /// <summary>The staircase stands on this line.</summary>
        public const float StairX = -2f;
        /// <summary>
        /// What the solver holds the red box at: a point of its outline on the shelf's face, a little above
        /// the middle. (From the shoe prints any point of the face from 0.95 to 2.8 up gives the same box,
        /// 1.91 to 1.95 against the shelf. Lower than that the rug stops it before the shelf does: held at
        /// its own middle where it will stand, (x, 0.95, 11.05), it is already 0.05 short, at 0.6 up it is
        /// 1.72 and 0.6 short, at 0.3 up 1.51 and 1.2 short.)
        /// </summary>
        public static readonly Vector3 BigAim = new Vector3(StairX, 1.3f, ShelfZ);
        /// <summary>... the yellow one: its middle when it stands on the rug in front of the red one.</summary>
        public static readonly Vector3 StepAim = new Vector3(StairX, 0.49f, 9.43f);
        /// <summary>
        /// ... the blue one: its middle when it is held against the shelf's face with its top level with the
        /// shelf's. (Let go, it comes down on the red box. It cannot be held any lower: the eye is below the
        /// red box's top, and a box held toward that top catches its front edge.)
        /// </summary>
        public static readonly Vector3 TopAim = new Vector3(StairX, 3.3f, 11.47f);
        public static readonly Vector3 ExitCentre = new Vector3(4f, ShelfTop + 1.25f, 14f);
        public static readonly Vector3 ExitSize = new Vector3(3f, 2.5f, 3f);

        // ---- What the level says ----------------------------------------------------------------------------
        /// <summary>
        /// Apparent size (scale / distance) below which a box cannot be made into any step of the stairs
        /// from the shoe prints (measured: the blue one at 0.145 came out 0.76 on the red one, at 0.118 it
        /// was 0.63; 0.70 is the least that still gets the player onto the shelf).
        /// </summary>
        public const float LooksTooSmall = 0.13f;
        /// <summary>A box lying about at less than this goes back to where it started, at the size it started with.</summary>
        public const float CrumbScale = 0.45f;

        /// <summary>Said on a pick-up from too far away. The words are Level 1's, on purpose.</summary>
        public const string FarLine = "It only ever gets as big as it looks. Pick it up from closer.";
        /// <summary>Said after a hop at something more than a hop high.</summary>
        public const string TallLine = "Too tall to hop onto. A hop gets you up about one mark of the chart.";
        /// <summary>Said when a box held toward the top of another one caught that one's edge and fell beside it.</summary>
        public const string EdgeLine = "It caught on the edge of the box below. Hold it higher, against the shelf above that box.";
        /// <summary>Said when the red box was let go at its largest size, short of the shelf: whoever stands too far back gets this.</summary>
        public const string ClampLine = "This box gets no bigger than that. Step closer and let it go right against the shelf.";
        /// <summary>
        /// Said when one of the two inner boxes was held toward the shelf and let go at its largest size
        /// short of it - picked up from too near, it looks bigger than it can get - unless it comes down
        /// as a last step all the same. ("From further back", not "step closer": the shoe prints are where
        /// to stand, and measured, tools/out/notes/level10-review3-probe-neartop.txt, picked up again from
        /// further back it was right in 8 of 8, let go from closer in 5 of 7.)
        /// </summary>
        public const string InnerClampLine = "This box gets no bigger than that, and it stopped short of the shelf. Pick it up from further back.";
        public const string ShelfLine = "That box went over the edge of the shelf and out of sight. It is back at the start.";
        /// <summary>Said when a box that was picked up from too far away (it was told so then) is sent back as a crumb.</summary>
        public const string CrumbLine = "That box was too small to use. It is back at the start, as big as it was.";
        /// <summary>Said when any other box is sent back as a crumb: it was let go a good deal nearer than it was picked up.</summary>
        public const string CloseCrumbLine = "Let go that close, it came out too small to use. It is back at the start, as big as it was.";
        /// <summary>
        /// Said when a box carried up the stairs and put on another one by hand came out too small for the
        /// last hop (and too big to be sent back). What everybody tries first with the third box, and it
        /// never works without a jump: measured, tools/out/notes/level10-review3-probe-byhand.txt - picked
        /// up from 0.9 to 2.6 before the nest and let go from the big box's front edge it is 0.30 to 0.57.
        /// </summary>
        public const string CloseLine = "Let go this close, it comes out small. From back on the rug, hold it against the shelf above the big box.";
        /// <summary>A box put on another one by hand at less than this is no last step (0.70 is the least that gets the player onto the shelf from a big box of 1.9).</summary>
        public const float SmallTop = 0.7f;
        /// <summary>Said when a big box held toward the shelf too low came down on the rug short of it.</summary>
        public const string ShortLine = "It came down on the rug before it reached the shelf. Hold it a little higher and let go.";
        /// <summary>Said on a click at a box while the feet are on one, or in the air off one (see <see cref="StandOn"/>).</summary>
        public const string FeetLine = "You cannot pick up a box while you are up on one. Step down onto the rug first.";
        /// <summary>A box that stands this far short of the shelf, or further, did not reach it.</summary>
        public const float ShortGap = 0.3f;

        Prop red, yellow, teal;
        Prop[] boxes;
        NestedSet nested;
        PropLeash shelfLeash;
        PropLeash[] crumbLeashes;
        Exit exit;
        bool[] wasHeld;
        float[] lastRatio;
        string lastLine;
        int lastSaid;
        Vector3 jumpedFrom;
        bool jumped;
        GameObject topOutline;
        Prop stoodOn;

        public Prop Red => red;
        public Prop Yellow => yellow;
        public Prop Teal => teal;
        /// <summary>The three boxes, outermost first.</summary>
        public IReadOnlyList<Prop> Boxes => boxes;
        public NestedSet Nested => nested;
        /// <summary>Returns a box that came to rest on the shelf, where it cannot be seen from the rug.</summary>
        public PropLeash ShelfLeash => shelfLeash;
        /// <summary>One per box: returns it when it lies about too small to use.</summary>
        public IReadOnlyList<PropLeash> CrumbLeashes => crumbLeashes;
        public Exit Exit => exit;
        /// <summary>The blue dashes on the chart: shown until a box stands on top of another one under them.</summary>
        public GameObject TopOutline => topOutline;
        /// <summary>The box under the player's feet, or the one they last left the ground from: while there is one, no box can be picked up. Null on the rug.</summary>
        public Prop StoodOn => stoodOn;

        public override string Blurb => "One box. Or is it? The shelf is four marks up.";

        public override string[] Hints => new[]
        {
            "Pick the box up. There is another one inside. And another.",
            "The red box grows no taller than the '2' on the height chart, the two inside it only a little past the '1'. You can only hop up about one mark at a time.",
            // Five lines of the pause card's hint panel at full size (about 200 characters). ("In the outline",
            // not "at the foot of the shelf": held at the very foot the red box comes down on the rug 1.7
            // short of the shelf, at 1.36.)
            "Pick each box up from the rim of the mat. From the shoe prints, hold it in the outline of its colour and let go: red against the shelf, yellow on the rug, blue at the top of the chart. Then hop up.",
        };

        public override string Environment => "cardboard-box";
        // The preset's floor is the den's floor; the rug lies on it.
        public override float GroundY => -RugThickness;
        public override float KillY => -30f;

        public override void Build(LevelContext ctx)
        {
            red = null;
            yellow = null;
            teal = null;
            boxes = null;
            nested = null;
            shelfLeash = null;
            crumbLeashes = null;
            exit = null;
            wasHeld = new bool[3];
            lastRatio = new[] { 1f, 1f, 1f };
            lastLine = null;
            lastSaid = -100000;
            jumpedFrom = Vector3.zero;
            jumped = false;
            topOutline = null;
            stoodOn = null;

            BuildRoom(ctx);
            BuildPaint(ctx);
            BuildDressing(ctx);

            // Three gift boxes, one inside the next: solid cubes, the inner two hidden until the one around
            // them is picked up for the first time. Each stays put until it is picked up itself.
            ToyDef def = ToyCatalog.Get(ToyId.GiftBox);
            red = AddBox(ctx, def, RedStart, MaxScale, null, null);
            yellow = AddBox(ctx, def, YellowStart, InnerMaxScale, Palette.Lemon, "Yellow Gift Box");
            teal = AddBox(ctx, def, TealStart, InnerMaxScale, Palette.Lagoon, "Blue Gift Box");
            boxes = new[] { red, yellow, teal };
            nested = new NestedSet(ctx, new NestedSetOptions { Name = "Matryoshka", Props = boxes });

            // A box on the shelf may lie out of sight from the rug: back to the start with it, unless the
            // player is up there too.
            float outer = HalfWidth + WallThickness;
            shelfLeash = new PropLeash(ctx, new PropLeashOptions
            {
                Name = "Shelf", Props = boxes,
                Forbidden = new[] { Zone.MinMax(new Vector3(-outer, ShelfTop, ShelfZ), new Vector3(outer, WallTop + 2f, FarZ + WallThickness)) },
                Unless = () => ctx.Game.Player.Grounded && ctx.Game.Player.Position.y > ShelfTop - 0.4f,
                RestSpeed = 0.3f, Grace = 2f, Action = LeashAction.Respawn,
            });
            shelfLeash.PropReturned += prop =>
            {
                Rehome(ctx, prop);
                Say(ctx, ShelfLine);
            };

            // A box that has become too small to be of any use goes back too.
            Zone everywhere = Zone.MinMax(new Vector3(-outer - 2f, -4f, NearZ - 4f), new Vector3(outer + 2f, WallTop + 4f, FarZ + 4f));
            crumbLeashes = new PropLeash[boxes.Length];
            for (int i = 0; i < boxes.Length; i++)
            {
                Prop box = boxes[i];
                int index = i;
                crumbLeashes[i] = new PropLeash(ctx, new PropLeashOptions
                {
                    Name = "Crumb " + (i + 1), Props = new[] { box }, Forbidden = new[] { everywhere },
                    Unless = () => box.Scale >= CrumbScale,
                    RestSpeed = 0.5f, Grace = 1.5f, Action = LeashAction.Respawn,
                });
                crumbLeashes[i].PropReturned += prop =>
                {
                    Rehome(ctx, prop);
                    // Why it was a crumb: picked up from too far away (said at the pick-up), or - every other
                    // time - let go a good deal nearer than it was picked up (at one's feet, or carried up
                    // the stairs and put on a box by hand).
                    Say(ctx, lastRatio[index] < LooksTooSmall ? CrumbLine : CloseCrumbLine);
                };
            }

            // Whatever sends a box back to the start (the kill plane too): never into another box.
            ctx.Game.Events.PropRespawned += e => Rehome(ctx, e.Prop);

            ctx.Game.Events.PropGrabbed += e =>
            {
                int index = IndexOf(e.Prop);
                if (index < 0) return;
                lastRatio[index] = e.OldScale / Mathf.Max(0.01f, e.GrabDistance);
                // Picked up from far off it looks small, and it only ever gets as big as it looks.
                if (lastRatio[index] < LooksTooSmall) Say(ctx, FarLine, 6f);
            };
            ctx.Game.Events.PlayerJumped += e =>
            {
                jumped = true;
                jumpedFrom = e.Position;
            };
            ctx.Game.Events.PlayerLanded += e =>
            {
                // (Delivered when the tick of the landing ends: before the next one's click.)
                StandOn(e.Prop);
                OnLanded(ctx, e.Position);
            };
            ctx.OnUpdate(dt => Watch(ctx));

            exit = ctx.AddExit(ExitCentre, ExitSize);
            ctx.SetSpawn(PickSpot, 0f, SpawnPitch);
        }

        static Prop AddBox(LevelContext ctx, ToyDef def, float scale, float largest, Color? color, string name) =>
            ToyCatalog.Add(ctx, ToyId.GiftBox, Nest + Vector3.up * (def.RestHeight * scale), scale, color, o =>
            {
                if (name != null) o.Name = name;
                o.MinScale = MinScale;
                o.MaxScale = largest;
                o.FrozenUntilGrabbed = true;
                // In the hand a box is square to the view, whichever side of the mat it is picked up from:
                // held toward the shelf it stands square against it, like its outline. (Without this a box
                // picked up from a corner of the mat stands 45 degrees askew: the stairs still work, at
                // 1.79 / 0.79 / 1.03, but nothing lines up with what is painted. Q still turns it.)
                o.SquareOnGrab = true;
            });

        int IndexOf(Prop prop)
        {
            if (boxes == null) return -1;
            for (int i = 0; i < boxes.Length; i++)
                if (boxes[i] == prop) return i;
            return -1;
        }

        /// <summary>True for a box that is out of its wrapping and not in the player's hand.</summary>
        public static bool Out(Prop box) => box != null && !box.Removed && !box.Held && box.GameObject.activeInHierarchy;

        /// <summary>The box around a box, in the world's axes.</summary>
        public static Bounds BoundsOf(Prop box)
        {
            Bounds bounds = box.Colliders[0].bounds;
            for (int i = 1; i < box.Colliders.Length; i++) bounds.Encapsulate(box.Colliders[i].bounds);
            return bounds;
        }

        // ---- Back to the start ------------------------------------------------------------------------------

        // Where a box that is sent back stands if the nest is taken (by a box that has not been picked up
        // yet, or one that was put down there): beside it, on the side away from the way to the shelf.
        static readonly Vector3[] Spots =
        {
            Nest, Nest + new Vector3(2.4f, 0f, 0f), Nest + new Vector3(2.4f, 0f, 2.5f), Nest + new Vector3(4.9f, 0f, 0f), Nest + new Vector3(4.9f, 0f, 2.5f),
        };

        readonly Collider[] spotHits = new Collider[16];

        // A box that was sent back stands at the nest, at the size it started with. Two solid boxes cannot
        // stand in one place: if something is there, it goes to the first free spot beside the nest.
        void Rehome(LevelContext ctx, Prop box)
        {
            if (IndexOf(box) < 0 || box.Removed || box.Held) return;
            float half = box.Scale * 0.5f;
            foreach (Vector3 spot in Spots)
            {
                Vector3 centre = spot + Vector3.up * half;
                if (!SpotFree(ctx, box, centre, half)) continue;
                if ((box.Center - centre).sqrMagnitude > 1e-6f) box.SetPose(centre, Quaternion.identity);
                return;
            }
        }

        bool SpotFree(LevelContext ctx, Prop box, Vector3 centre, float half)
        {
            int count = ctx.Game.PhysicsScene.OverlapBox(centre, Vector3.one * (half - 0.01f), spotHits, Quaternion.identity, Layers.PropMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (PropRef.Of(spotHits[i]) != box) return false;
            // Not into anybody's legs either.
            Player player = ctx.Game.Player;
            Vector3 to = player.Position - centre;
            float reach = half + player.Radius + 0.05f;
            return Mathf.Abs(to.x) > reach || Mathf.Abs(to.z) > reach || player.Position.y > centre.y + half - 0.05f;
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

        // Every tick: a box that has just been let go is looked at where it was let go.
        void Watch(LevelContext ctx)
        {
            bool hadBox = false;
            for (int i = 0; i < boxes.Length; i++)
            {
                Prop box = boxes[i];
                bool held = box.Held;
                hadBox |= wasHeld[i];
                if (wasHeld[i] && !held && !box.Removed) OnLetGo(ctx, box);
                wasHeld[i] = held;
            }

            // Up on a box nothing is picked up (see StandOn; a box let go up there in this tick is out of
            // reach from the next). A click at a box with empty hands - this tick's click picked nothing
            // up - is told why.
            Game game = ctx.Game;
            Player player = game.Player;
            if (player.Grounded) StandOn(player.GroundProp);
            KeepBoxesDown();
            if (stoodOn != null && !hadBox && game.LastInput.GrabPressed && game.Grabber.Held == null && LooksAtABox(game)) Say(ctx, FeetLine, 6f);

            // The blue outline is where a box is held, not where it ends up: once a box stands under it, on
            // top of another one, it has done its work (and would look like a place for a fourth box). A
            // box up there that is too small to be the last step - the shelf more than a hop above it - or
            // that stands on the front of the big box, well short of the shelf, has not done that work:
            // the outline stays, and still says where a box is to be held.
            if (topOutline != null)
            {
                bool taken = false;
                foreach (Prop box in boxes)
                {
                    if (!Out(box) || !AtRest(box)) continue;
                    Vector3 centre = box.Center;
                    float half = box.Scale * 0.5f;
                    if (Mathf.Abs(centre.x - StairX) < 1.2f && centre.z + half > ShelfZ - 0.6f && centre.y - half > 1.2f && centre.y < ShelfTop &&
                        ShelfTop - (centre.y + half) < HopRise + 0.05f) taken = true;
                }
                if (topOutline.activeSelf == taken) topOutline.SetActive(!taken);
            }
        }

        // "Lifting the thing you stand on would let you fly", says the engine, and refuses a click at the prop
        // under the feet. In the air it does not: jump, click, click - and the box one jumped off is back
        // under the feet, bigger, because the eye was higher when it was let go than when it was picked up
        // (measured: 0.32 -> 0.51 -> 0.67 in two jumps with the shelf's face in the way, about 1.7 times a
        // jump in the open). Any box is 2.0 after four jumps with its owner on top of it, and a second one
        // grown the same way on top of the first is the shelf: nothing sized, no stairs.
        //
        // And from the top of the big box the step can be pulled up after oneself: picked up from up there
        // and let go again on the big box. In a jump, eyes on one's feet, it comes out 0.72 to 0.76 with the
        // stairs exactly as the outlines show them, and the shelf is a hop away: two boxes, the blue one
        // never touched (measured, tools/out/notes/level10-review2-probe-jumppull.txt: 4 of 7 ways of doing
        // it; 12 of 14 with the red box at 2.0 and a step of 1.2 or 1.3, with or without the jump).
        //
        // So here nothing is picked up from up on a box: while the feet are on one, or in the air off one,
        // every box is out of reach until the feet are on something else. A box in the hand may be carried
        // up and let go up there: under one's feet at the top of a jump it is big enough to be a top box
        // (the third of three - with two it is no way up, see InnerMaxScale); put down by hand it is too
        // small, and is told so (CloseLine). (Set when the feet come down, so it holds in the very tick
        // they leave again.)
        void StandOn(Prop under)
        {
            Prop box = IndexOf(under) >= 0 ? under : null;
            if (box == stoodOn) return;
            stoodOn = box;
            KeepBoxesDown();
        }

        // Every box that is out and not in the hand can be picked up from the rug, and from nowhere else.
        // (The ones still inside another are the nested set's business.)
        void KeepBoxesDown()
        {
            if (boxes == null) return;
            bool free = stoodOn == null;
            foreach (Prop box in boxes)
                if (Out(box) && box.Grabbable != free) box.Grabbable = free;
        }

        // Is the crosshair on one of the boxes, with nothing in front of it?
        bool LooksAtABox(Game game)
        {
            Player player = game.Player;
            return game.PhysicsScene.Raycast(player.Eye, player.Forward, out RaycastHit hit, PerspectiveGrabber.GrabRange, Layers.SolidMask, QueryTriggerInteraction.Ignore) &&
                   IndexOf(PropRef.Of(hit.collider)) >= 0;
        }

        void OnLetGo(LevelContext ctx, Prop box)
        {
            Bounds own = BoundsOf(box);
            // As big as it gets, in mid-air short of the shelf. The red box: whoever stands too far back gets
            // this, whatever it was held at. One of the inner two: only when it was held toward the shelf or
            // over it (held toward the rug it is a step, and a step stands short of the shelf on purpose),
            // and not when it comes down on another box as a last step all the same.
            if (box.Scale >= box.MaxScale - 0.005f && own.max.z < ShelfZ - 0.25f && own.max.z > ShelfZ - 4f)
            {
                if (box == red)
                {
                    Say(ctx, ClampLine, 6f);
                    return;
                }
                if (HeldTowardTheShelf(ctx.Game.Player) && !LastStepWhereItFalls(box, own))
                {
                    Say(ctx, InnerClampLine, 6f);
                    return;
                }
            }
            // On the rug, short of the shelf, with the crosshair on the shelf's face and no box in between:
            // a box meant for the foot of the shelf was held too low, and the rug stopped it first. (Only a
            // box big enough to be meant as the big one: a step stands short of the shelf on purpose.)
            if (own.min.y < 0.05f)
            {
                float gap = ShelfZ - own.max.z;
                if (box.Scale >= 1.2f && gap > ShortGap && gap < 4f && AimsAtTheShelf(ctx.Game.Player) && NothingBehind(box, own)) Say(ctx, ShortLine, 6f);
                return;
            }
            // Let go in the air with its middle above another box's top and that box's top edge in its side:
            // it was held toward the top of that box from below (the eye is lower than a big box is tall),
            // caught the edge at a fraction of the size and now falls beside it.
            if (own.min.y < 0.25f) return;
            foreach (Prop other in boxes)
            {
                if (other == box || !Out(other)) continue;
                Bounds its = BoundsOf(other);
                if (its.max.y < own.min.y + 0.03f || its.max.y > own.center.y) continue;
                const float touch = 0.06f;
                if (own.min.x > its.max.x + touch || own.max.x < its.min.x - touch || own.min.z > its.max.z + touch || own.max.z < its.min.z - touch) continue;
                Say(ctx, EdgeLine, 6f);
                return;
            }
            // Carried up the stairs and put on a box by hand: let go this close it is small, and the shelf
            // is more than a hop above its top. (A crumb is told when it is sent back; a box that was
            // picked up from too far away was told at the pick-up.)
            int index = IndexOf(box);
            if (stoodOn != null && box.Scale >= CrumbScale && box.Scale < SmallTop && lastRatio[index] >= LooksTooSmall)
            {
                Prop under = Under(box, own);
                if (under != null && BoundsOf(under).max.y + box.Scale + HopRise + 0.02f < ShelfTop) Say(ctx, CloseLine, 6f);
            }
        }

        // The highest box right under the middle of a box that has just been let go: what it stands on, or
        // comes down on. Null: the rug.
        Prop Under(Prop box, Bounds own)
        {
            Prop under = null;
            float top = 0.25f;
            foreach (Prop other in boxes)
            {
                if (other == box || !Out(other)) continue;
                Bounds its = BoundsOf(other);
                if (its.max.y > own.min.y + 0.05f || its.max.y <= top) continue;
                if (own.center.x < its.min.x || own.center.x > its.max.x || own.center.z < its.min.z || own.center.z > its.max.z) continue;
                top = its.max.y;
                under = other;
            }
            return under;
        }

        // Let go in the air, will the box come down as the last step of a staircase: on another box, with
        // room to stand before it, its top within a hop of the shelf?
        bool LastStepWhereItFalls(Prop box, Bounds own)
        {
            Prop under = Under(box, own);
            if (under == null) return false;
            Bounds its = BoundsOf(under);
            return own.min.z - its.min.z >= 0.3f && ShelfTop - (its.max.y + box.Scale) <= HopRise + 0.02f;
        }

        // Does the view meet the shelf's face, or the rug right at its foot (not the rug a step or more
        // before it, where the step's footprint is; not the air above it)?
        static bool AimsAtTheShelf(Player player)
        {
            Vector3 eye = player.Eye, forward = player.Forward;
            if (forward.z < 0.1f || eye.z > ShelfZ - 0.5f) return false;
            Vector3 at = eye + forward * ((ShelfZ - eye.z) / forward.z);
            if (Mathf.Abs(at.x) > HalfWidth || at.y > ShelfTop) return false;
            if (at.y >= 0f) return true;
            float rugZ = eye.z + forward.z * (eye.y / -forward.y);
            return rugZ > ShelfZ - 0.7f;
        }

        // Does the view meet the plane of the shelf's face above the rug - on the face or over the shelf's top?
        static bool HeldTowardTheShelf(Player player)
        {
            Vector3 eye = player.Eye, forward = player.Forward;
            if (forward.z < 0.1f || eye.z > ShelfZ - 0.5f) return false;
            Vector3 at = eye + forward * ((ShelfZ - eye.z) / forward.z);
            return Mathf.Abs(at.x) <= HalfWidth && at.y >= 0f;
        }

        // No other box stands between this one and the shelf.
        bool NothingBehind(Prop box, Bounds own)
        {
            foreach (Prop other in boxes)
            {
                if (other == box || !Out(other)) continue;
                Bounds its = BoundsOf(other);
                if (its.max.z > own.max.z && its.min.x < own.max.x && its.max.x > own.min.x) return false;
            }
            return true;
        }

        // A hop that got nowhere, next to something more than a hop high (and less than two): say why.
        void OnLanded(LevelContext ctx, Vector3 at)
        {
            if (!jumped) return;
            jumped = false;
            if (at.y > jumpedFrom.y + 0.3f) return;
            Player player = ctx.Game.Player;
            // What did they hop at? The nearest thing that stands higher than they stood, within reach and
            // not behind their back.
            float reach = player.Radius + 0.2f, nearest = float.MaxValue, rise = 0f;
            var ahead = new Vector3(Mathf.Sin(player.Yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(player.Yaw * Mathf.Deg2Rad));
            float toShelf = ShelfZ - at.z;
            if (toShelf < reach && ahead.z > -0.2f && ShelfTop - jumpedFrom.y > 0.3f)
            {
                nearest = toShelf;
                rise = ShelfTop - jumpedFrom.y;
            }
            foreach (Prop box in boxes)
            {
                if (!Out(box)) continue;
                Bounds bounds = BoundsOf(box);
                var to = new Vector3(Mathf.Clamp(at.x, bounds.min.x, bounds.max.x) - at.x, 0f, Mathf.Clamp(at.z, bounds.min.z, bounds.max.z) - at.z);
                float distance = to.magnitude;
                if (distance > reach || distance >= nearest) continue;
                if (distance > 0.05f && Vector3.Dot(to, ahead) < -0.2f * distance) continue;
                // (Its top as it stands, not the top of the box around it: a box that a hop has just bumped
                // into rocks for a moment - a 1.15 step by ten degrees - and a rocking box is taller on
                // paper. That made a step one can hop onto "too tall" when the hop at it began too early.)
                float up = Mathf.Min(bounds.max.y, box.Center.y + box.Scale * 0.5f + 0.02f) - jumpedFrom.y;
                // What the player stands on, or stood on, is not what they hopped at.
                if (up < 0.3f) continue;
                nearest = distance;
                rise = up;
            }
            // (Between 1.30 and 1.35 a hop gets up in some places and not in others: one that did not is told.)
            if (rise > HopRise + 0.02f && rise < 2f * HopRise + 0.3f) Say(ctx, TallLine, 6f);
        }

        // ---- Geometry ---------------------------------------------------------------------------------------

        static void Solid(LevelContext ctx, string name, Vector3 min, Vector3 max)
        {
            var solid = new GameObject(name) { layer = Layers.Default };
            solid.AddComponent<BoxCollider>().size = max - min;
            ctx.AddStatic(solid, (min + max) * 0.5f);
        }

        void BuildRoom(LevelContext ctx)
        {
            float t = WallThickness, outer = HalfWidth + t;
            float back = NearZ - t, far = FarZ + t;

            // The rug (its top is the play plane) and the shelf: a sheer face, a square top edge, no lip.
            Solid(ctx, "Rug", new Vector3(-outer, -1f, back), new Vector3(outer, 0f, far));
            Solid(ctx, "Shelf", new Vector3(-outer, 0f, ShelfZ), new Vector3(outer, ShelfTop, far));
            // What stops a held toy reaches the sky cap everywhere; what is drawn on the sun's side and
            // behind the start is cardboard low enough for the sun to get in.
            Solid(ctx, "Wall -X", new Vector3(-outer, 0f, back), new Vector3(-HalfWidth, WallTop, far));
            Solid(ctx, "Wall +X", new Vector3(HalfWidth, 0f, back), new Vector3(outer, WallTop, far));
            Solid(ctx, "Wall Near", new Vector3(-HalfWidth, 0f, back), new Vector3(HalfWidth, WallTop, NearZ));
            Solid(ctx, "Wall Far", new Vector3(-HalfWidth, ShelfTop, FarZ), new Vector3(HalfWidth, WallTop, far));
            SkyCap.Add(ctx, -outer, outer, back, far, WallTop);

            Dip dip = ctx.Dip;
            var set = new Level01Set();
            Material rug = Materials.Room(RoomSurface.LevelStatic, dip, new PatternSpec(RoomPattern.Dots, 2f, 0.3f));
            Material wood = Materials.Room(RoomSurface.Furniture, dip, PatternSpec.Planks.WithPitch(2f));
            // Wallpaper: the room's light tone with a stripe, its mid tone below the shelf's height.
            Material paper = Materials.Room(new RoomRecipe
            {
                Name = dip.Name + " Den Wall", Top = dip.Light, Side = dip.Light, Dado = dip.Mid, DadoY = ShelfTop,
                Pattern = PatternSpec.Stripes.WithPitch(2f),
            });
            Material card = Materials.Room(RoomSurface.LevelStatic, dip, PatternSpec.Corrugated);
            Material trim = Materials.Room(RoomSurface.Trim, dip);
            Material edge = Materials.Room(RoomRecipe.Solid(dip.Mid));

            set.Box(rug, new Vector3(0f, -RugThickness * 0.5f, (back + ShelfZ) * 0.5f), new Vector3(outer * 2f, RugThickness, ShelfZ - back));
            set.Box(wood, new Vector3(0f, ShelfTop * 0.5f, (ShelfZ + far) * 0.5f), new Vector3(outer * 2f, ShelfTop, far - ShelfZ));
            // The plank's own edge along the top of the face.
            const float lip = 0.3f;
            set.Box(edge, new Vector3(0f, ShelfTop - lip * 0.5f, ShelfZ - 0.006f), new Vector3(HalfWidth * 2f, lip, 0.012f));
            // The den's own corner: two papered walls, at the back and on the left, with a skirting board
            // along the rug.
            set.Box(paper, new Vector3(0f, WallTop * 0.5f, FarZ + t * 0.5f), new Vector3(outer * 2f, WallTop, t));
            set.Box(paper, new Vector3(-HalfWidth - t * 0.5f, WallTop * 0.5f, (back + FarZ) * 0.5f), new Vector3(t, WallTop, FarZ - back));
            set.Box(trim, new Vector3(-HalfWidth + 0.04f, 0.3f, (NearZ + ShelfZ) * 0.5f), new Vector3(0.08f, 0.6f, ShelfZ - NearZ));
            // Cardboard on the other two sides: a shipping box lying on its side, a flap standing open.
            set.Box(card, new Vector3(HalfWidth + t * 0.5f, SunSideTop * 0.5f, (back + FarZ) * 0.5f), new Vector3(t, SunSideTop, FarZ - back));
            set.Box(card, new Vector3(0f, NearSideTop * 0.5f, NearZ - t * 0.5f), new Vector3(HalfWidth * 2f, NearSideTop, t));
            set.Finish(ctx, "Room");
        }

        /// <summary>How high the cardboard is drawn on the sun's side and behind the start (the walls themselves reach the sky cap).</summary>
        const float SunSideTop = 3f, NearSideTop = 4.4f;
        /// <summary>The height chart on the shelf's face, from x to x.</summary>
        public const float ChartLeft = -4f, ChartRight = 0f;
        /// <summary>Where the chart's numerals are: this far along the chart, their middles this far above their marks.</summary>
        public const float NumeralX = ChartLeft + 0.72f, NumeralRise = 0.36f;
        /// <summary>The sizes the outlines are painted for: what the solver makes of the three boxes.</summary>
        public const float BigSize = 1.9f, StepSize = 0.97f, TopSize = 1.06f;

        // The chart and what is painted on it and before it. Looks only.
        void BuildPaint(LevelContext ctx)
        {
            Dip dip = ctx.Dip;
            var set = new Level01Set();
            Material sheet = Materials.Room(RoomRecipe.Solid(Palette.Paper));
            Material band = Materials.Room(RoomRecipe.Solid(dip.Light));
            Material ink = Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Deep, Palette.Ink, 0.45f)));
            Material rule = Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Light, dip.Deep, 0.6f)));
            Vector3 face = Vector3.back;

            // The height chart: a sheet of paper taped to the shelf's face. A band for every other mark, a
            // line across at every mark, a tick at every half, and the marks' numbers up its left side.
            const float proud = 0.01f, thin = 0.01f;
            float width = ChartRight - ChartLeft, middle = (ChartLeft + ChartRight) * 0.5f;
            float z0 = ShelfZ - proud - thin * 0.5f, z1 = z0 - thin, z2 = z1 - thin;
            set.Box(sheet, new Vector3(middle, ShelfTop * 0.5f, z0), new Vector3(width, ShelfTop, thin));
            set.Box(band, new Vector3(middle, 1.5f, z1), new Vector3(width, 1f, thin));
            set.Box(band, new Vector3(middle, (3f + ShelfTop) * 0.5f, z1), new Vector3(width, ShelfTop - 3f, thin));
            for (int half = 1; half < 8; half++)
            {
                float y = half * 0.5f;
                if (half % 2 == 0)
                {
                    set.Box(rule, new Vector3(middle, y, z2), new Vector3(width, 0.035f, thin));
                    set.Box(ink, new Vector3(ChartLeft + 0.5f, y, z2 - thin), new Vector3(1f, 0.07f, thin));
                }
                else
                {
                    set.Box(ink, new Vector3(ChartLeft + 0.2f, y, z2 - thin), new Vector3(0.4f, 0.04f, thin));
                }
            }
            // (Each numeral sits on its mark's line, to the right of the half-mark tick above it: a tick that
            // runs into a numeral reads as a stroke of it.)
            for (int mark = 1; mark <= 3; mark++) Digit(set, ink, mark, new Vector3(NumeralX, mark + NumeralRise, z2 - thin), 0.42f, thin);
            // Four strips of tape hold it.
            Material tape = Materials.Room(RoomRecipe.Solid(dip.Mid));
            foreach (float x in new[] { ChartLeft, ChartRight })
                foreach (float y in new[] { 0.55f, ShelfTop - 0.55f })
                    set.Box(tape, new Vector3(x, y, z2), new Vector3(0.5f, 0.2f, thin), Quaternion.Euler(0f, 0f, x < middle == y < 2f ? 35f : -35f));

            // The size language of the campaign: each box at the size that works, dashed in its own colour, on
            // the surface it stops against - the red one at the foot of the shelf, the yellow one on the rug.
            const float stroke = 0.09f, dash = 0.26f, gap = 0.17f;
            Square(set, Paint(dip, Palette.Cherry), new Vector3(StairX, BigSize * 0.5f + 0.02f, ShelfZ), Vector3.right, Vector3.up, face, BigSize, stroke, dash, gap);
            Square(set, Paint(dip, Palette.Lemon), new Vector3(StairX, 0f, StepAim.z), Vector3.right, Vector3.forward, Vector3.up, StepSize, stroke, dash, gap);
            set.Finish(ctx, "Paint", castShadows: false);

            // The blue one is held high against the chart and comes down on the red one: its outline is
            // where it is held, with arrows down to where it ends up. It goes when a box stands there.
            var top = new Level01Set();
            Material blue = Paint(dip, Palette.Lagoon);
            Square(top, blue, new Vector3(StairX, TopAim.y, ShelfZ), Vector3.right, Vector3.up, face, TopSize, stroke, dash, gap);
            float low = TopAim.y - TopSize * 0.5f;
            foreach (float y in new[] { low - 0.2f, low - 0.48f })
            {
                top.Box(blue, new Vector3(StairX - 0.11f, y, ShelfZ - 0.04f), new Vector3(0.36f, stroke, 0.02f), Quaternion.Euler(0f, 0f, -38f));
                top.Box(blue, new Vector3(StairX + 0.11f, y, ShelfZ - 0.04f), new Vector3(0.36f, stroke, 0.02f), Quaternion.Euler(0f, 0f, 38f));
            }
            topOutline = top.Finish(ctx, "Top Outline", castShadows: false);

            // Where to stand: shoe prints on a pad, in the room's own tones (a toy's colour is kept for where
            // the toy goes). One where the boxes are picked up, one where they are let go.
            Material pad = Materials.Room(RoomRecipe.Solid(dip.Deep));
            StandMark(ctx, "Pick-up Mark", PickSpot, pad, sheet);
            StandMark(ctx, "Stand Mark", StandSpot, pad, sheet);
        }

        static Material Paint(Dip dip, Color candy) => Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Light, candy, 0.9f)));

        // A dashed square of the given side about a centre, in the plane of two axes.
        static void Square(Level01Set set, Material paint, Vector3 centre, Vector3 right, Vector3 up, Vector3 normal, float side, float stroke, float dash, float gap)
        {
            Vector3 r = right * (side * 0.5f), u = up * (side * 0.5f);
            set.Dashes(paint, centre - r - u, centre + r - u, normal, stroke, dash, gap);
            set.Dashes(paint, centre - r + u, centre + r + u, normal, stroke, dash, gap);
            set.Dashes(paint, centre - r - u, centre - r + u, normal, stroke, dash, gap);
            set.Dashes(paint, centre + r - u, centre + r + u, normal, stroke, dash, gap);
        }

        // A numeral of seven strokes, upright on a face that looks toward -Z. The centre is its middle.
        static void Digit(Level01Set set, Material ink, int digit, Vector3 centre, float height, float thin)
        {
            // Strokes: top, upper right, lower right, bottom, lower left, upper left, middle.
            string strokes = digit == 1 ? "0110000" : digit == 2 ? "1101101" : digit == 3 ? "1111001" : "0110011";
            float width = height * 0.55f, w = height * 0.14f, halfH = height * 0.5f, quarter = height * 0.25f;
            var horizontal = new Vector3(width, w, thin);
            var vertical = new Vector3(w, halfH + w * 0.5f, thin);
            float side = (width - w) * 0.5f;
            // (Seen from -Z, the world's +X is to the right.)
            if (strokes[0] == '1') set.Box(ink, centre + new Vector3(0f, halfH - w * 0.5f, 0f), horizontal);
            if (strokes[1] == '1') set.Box(ink, centre + new Vector3(side, quarter, 0f), vertical);
            if (strokes[2] == '1') set.Box(ink, centre + new Vector3(side, -quarter, 0f), vertical);
            if (strokes[3] == '1') set.Box(ink, centre + new Vector3(0f, -halfH + w * 0.5f, 0f), horizontal);
            if (strokes[4] == '1') set.Box(ink, centre + new Vector3(-side, -quarter, 0f), vertical);
            if (strokes[5] == '1') set.Box(ink, centre + new Vector3(-side, quarter, 0f), vertical);
            if (strokes[6] == '1') set.Box(ink, centre, horizontal);
        }

        const float PadRadius = 0.62f, SoleScale = 1f;

        // Looks only: a renderer for the pad and one for each shoe, facing +Z, toes a little apart.
        static void StandMark(LevelContext ctx, string name, Vector3 at, Material pad, Material print)
        {
            var mark = new GameObject(name);
            ctx.AddStatic(mark, at + Vector3.up * 0.012f);
            Mesh disc = MeshKit.Cached(MeshKit.Key("Level10/Pad", PadRadius), () => MeshKit.Cylinder(PadRadius, 0.004f, 40));
            GadgetKit.Visual(mark.transform, "Pad", disc, pad).shadowCastingMode = ShadowCastingMode.Off;
            Mesh sole = MeshKit.Cached(MeshKit.Key("Level10/Sole"), () =>
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

        // A corner of a den: the mat the present stood on, spare blocks and a marker along the walls, and on
        // the shelf an arch of blocks round the way out. Nothing stands where a box held toward the chart
        // passes, where one comes back to, or on the way from the stairs to the exit.
        void BuildDressing(LevelContext ctx)
        {
            Dip dip = ctx.Dip;
            var set = new Level01Set();

            // On the rug, along the left wall and in the right-hand corners.
            float left = -HalfWidth + 1.1f, right = HalfWidth - 1.1f;
            Block(ctx, set, new Vector3(1.3f, 1.3f, 1.3f), new Vector3(left, 0f, NearZ + 1.2f), 8f);
            Block(ctx, set, new Vector3(0.8f, 0.8f, 0.8f), new Vector3(left + 0.05f, 1.3f, NearZ + 1.15f), -19f);
            Block(ctx, set, new Vector3(1.6f, 0.8f, 0.8f), new Vector3(left + 0.2f, 0f, NearZ + 3.1f), 81f);
            Piece(ctx, set, ToyFactory.Marker(0.36f, 3f), new Vector3(left - 0.35f, 0.36f, 2.2f), Quaternion.Euler(0f, 4f, 0f));
            // (Nothing with a top stands within a few steps of the shelf: it would be a step of the stairs.)
            Block(ctx, set, new Vector3(1.1f, 1.1f, 1.1f), new Vector3(right, 0f, NearZ + 1.1f), -11f);
            Block(ctx, set, new Vector3(1.4f, 0.7f, 0.7f), new Vector3(right - 0.1f, 0f, NearZ + 2.9f), 86f);
            Piece(ctx, set, ToyFactory.ThreadSpool(0.5f, 0.9f, grabbable: false), new Vector3(right + 0.1f, 0.45f, NearZ + 4.6f), Quaternion.identity);

            // On the shelf: an arch of blocks round the way out, and a block or two in the far corner.
            float archZ = ExitCentre.z + 1f;
            Block(ctx, set, new Vector3(1f, 2.9f, 1f), new Vector3(ExitCentre.x - 2.2f, ShelfTop, archZ), 0f);
            Block(ctx, set, new Vector3(1f, 2.9f, 1f), new Vector3(ExitCentre.x + 2.2f, ShelfTop, archZ), 0f);
            Block(ctx, set, new Vector3(5.8f, 0.9f, 1f), new Vector3(ExitCentre.x, ShelfTop + 2.9f, archZ), 0f);
            Block(ctx, set, new Vector3(1.2f, 1.2f, 1.2f), new Vector3(-6.6f, ShelfTop, 14.9f), 13f);
            Block(ctx, set, new Vector3(0.8f, 0.8f, 0.8f), new Vector3(-5.3f, ShelfTop, 15.2f), -24f);
            set.Finish(ctx, "Dressing");

            // The mat the present stands on: its rim is where the boxes are picked up from. Looks only.
            var mat = new Level01Set();
            Material rim = Materials.Room(RoomRecipe.Solid(Palette.Mix(dip.Mid, dip.Deep, 0.55f)));
            Material felt = Materials.Room(RoomRecipe.Solid(dip.Mid));
            mat.Disc(rim, Nest + Vector3.up * 0.003f, MatRadius, 0.004f, Quaternion.identity);
            mat.Disc(felt, Nest + Vector3.up * 0.007f, MatRadius - MatRim, 0.004f, Quaternion.identity);
            mat.Finish(ctx, "Mat", castShadows: false);
        }

        /// <summary>
        /// The round mat under the nest. Its rim is where the boxes are picked up from, whichever side one
        /// comes from: the start is on it, and from 2.4 to 3.0 away all three boxes come out right from the
        /// shoe prints (measured: the red one works from 2.0 to 3.5, the blue one from 2.1 to 4.3; nearer
        /// than that the blue one is as big as it gets before it reaches the shelf, and is told so).
        /// </summary>
        public const float MatRadius = 3f, MatRim = 0.6f;

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
            // The red box is a step away: picked up from here it looks as big as it needs to.
            yield return Carry(bot, red, PickSpot, StandSpot);
            yield return bot.DropAt(BigAim);
            yield return bot.Until(() => AtRest(red), 4f);

            yield return Carry(bot, yellow, PickSpot, StandSpot);
            yield return bot.DropAt(StepAim);
            yield return bot.Until(() => AtRest(yellow), 4f);

            yield return Carry(bot, teal, PickSpot, StandSpot);
            yield return bot.DropAt(TopAim);
            // (It is let go in the air, against the shelf's face, and comes down on the red box.)
            yield return bot.Until(() => AtRest(teal) && StandsOn(teal, red), 4f);

            yield return Climb(bot, yellow, red, teal);
        }

        /// <summary>
        /// LEVELS.md's own solver, which works as well (measured: 1.91 / 1.04 / 1.02): every box from its own
        /// distance (2.6, 2.3 and 1.9 in front of the nest), the blue one let go from a second place to
        /// stand, a step closer to the shelf.
        /// </summary>
        public IEnumerator SolveAsSpecified(Bot bot)
        {
            yield return Carry(bot, red, new Vector3(0f, 0f, -2.6f), StandSpot);
            yield return bot.DropAt(new Vector3(StairX, 0.95f, 11.05f));
            yield return bot.Until(() => AtRest(red), 4f);

            yield return Carry(bot, yellow, new Vector3(0f, 0f, -2.3f), StandSpot);
            yield return bot.DropAt(new Vector3(StairX, 0.5f, 9.4f));
            yield return bot.Until(() => AtRest(yellow), 4f);

            yield return Carry(bot, teal, new Vector3(0f, 0f, -1.9f), new Vector3(StairX, 0f, 8f));
            yield return bot.DropAt(new Vector3(StairX, 3f, 11.5f));
            // (It is let go in the air, against the shelf's face, and comes down on the red box.)
            yield return bot.Until(() => AtRest(teal) && StandsOn(teal, red), 4f);

            yield return Climb(bot, yellow, red, teal);
        }

        /// <summary>Walks to where a box is picked up from (round the nest), picks it up and carries it to a standing place.</summary>
        public IEnumerator Carry(Bot bot, Prop box, Vector3 pickFrom, Vector3 stand)
        {
            var lane = new Vector3(StairX, 0f, -1.4f);
            if (bot.Player.Position.z > 1f) yield return bot.WalkTo(lane, 0.4f, 12f);
            yield return bot.WalkTo(pickFrom, 0.08f, 12f);
            // Eyes on its middle first, as anybody's would be. (Grab clicks as soon as the crosshair touches
            // the box, some 10 degrees before the turn toward it is done; in the hand the box is square to
            // the view either way.)
            yield return bot.LookAt(box);
            yield return bot.Grab(box);
            // The box in the hand covers the one that was inside it exactly. A glance to the side, and
            // there it is. (The last one has nothing behind it.)
            int index = IndexOf(box);
            if (index >= 0 && index < boxes.Length - 1 && Out(boxes[index + 1]) && (box.Center - boxes[index + 1].Center).sqrMagnitude < 4f)
            {
                Vector3 inner = boxes[index + 1].Center;
                Vector3 side = Vector3.Cross(Vector3.up, inner - bot.Player.Eye).normalized;
                yield return bot.LookAt(inner - side * 1.25f);
                yield return bot.Wait(0.35f);
            }
            yield return bot.WalkTo(lane, 0.4f, 12f);
            yield return bot.WalkTo(stand, 0.08f, 12f);
        }

        /// <summary>
        /// The second half of the solution: up a staircase of a step on the rug, a big box behind it and a
        /// box on top of that one at the back, onto the shelf and into the exit.
        /// </summary>
        public IEnumerator Climb(Bot bot, Prop step, Prop big, Prop top)
        {
            Game game = bot.Game;
            Bounds a = BoundsOf(step), b = BoundsOf(big), c = BoundsOf(top);
            float tread = Mathf.Max(0.3f, c.min.z - b.min.z);

            yield return bot.WalkTo(new Vector3(a.center.x, 0f, a.min.z - 1.4f), 0.3f, 12f);
            // Eyes on the steps, as anybody's would be.
            yield return bot.LookAt(new Vector3(b.center.x, b.max.y, b.min.z));
            yield return Hop(bot, new Vector3(a.center.x, 0f, a.min.z - 0.55f), new Vector3(a.center.x, a.max.y, a.center.z), a.max.y);
            yield return Hop(bot, new Vector3(a.center.x, a.max.y, TakeOff(a.min.z, a.max.z, b.min.z)), new Vector3(b.center.x, b.max.y, b.min.z + Mathf.Min(0.45f, tread * 0.5f)), b.max.y);
            yield return Hop(bot, new Vector3(c.center.x, b.max.y, TakeOff(b.min.z, b.max.z, c.min.z)), new Vector3(c.center.x, c.max.y, c.center.z), c.max.y);
            yield return Hop(bot, new Vector3(c.center.x, c.max.y, TakeOff(c.min.z, c.max.z, ShelfZ)), new Vector3(c.center.x, ShelfTop, ShelfZ + 0.9f), ShelfTop);
            yield return bot.LookAt(ExitCentre);
            yield return bot.WalkTo(ExitCentre, 0.5f, 10f);
            yield return bot.Until(() => game.LevelCompleted, 3f);
        }

        // Where to hop from, standing on something that reaches from z to z, at a face further on: half a
        // step before the face where there is room; on a narrow tread as far back as still has the feet
        // on it; and never nearer than the body can get (touching the face), or the walk there never ends.
        static float TakeOff(float standMin, float standMax, float face)
        {
            float room = Player.BaseRadius + 0.2f, touching = Player.BaseRadius + 0.03f, footing = 0.17f;
            float z = Mathf.Max(face - room, standMin + footing);
            return Mathf.Min(z, Mathf.Min(face - touching, standMax - 0.05f));
        }

        // To the take-off spot, eyes on where the feet are going, a hop, and forward in the air onto the step.
        static IEnumerator Hop(Bot bot, Vector3 from, Vector3 onto, float height)
        {
            yield return bot.WalkTo(from, 0.1f, 6f);
            yield return bot.Until(() => bot.Player.Grounded, 2f);
            yield return bot.LookAt(new Vector3(onto.x, height, onto.z + 0.6f), 2f);
            yield return bot.Jump(false);
            yield return bot.WalkTo(onto, 0.2f, 3f);
            yield return bot.Until(() => bot.Player.Grounded && bot.Player.Position.y > height - 0.15f, 2f);
        }

        /// <summary>True for a box whose foot is on the top of another one.</summary>
        public static bool StandsOn(Prop box, Prop under)
        {
            Bounds own = BoundsOf(box), its = BoundsOf(under);
            return Mathf.Abs(own.min.y - its.max.y) < 0.05f && own.center.x > its.min.x && own.center.x < its.max.x && own.center.z > its.min.z && own.center.z < its.max.z;
        }

        /// <summary>True once a box that was let go has stopped moving.</summary>
        public static bool AtRest(Prop prop) =>
            !prop.Held && prop.Velocity.sqrMagnitude < 0.05f * 0.05f && prop.Body.angularVelocity.sqrMagnitude < 0.05f * 0.05f;
    }
}
