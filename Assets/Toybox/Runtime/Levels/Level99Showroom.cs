using System.Collections;
using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Levels
{
    /// <summary>
    /// Visual QA for the toy catalog: every toy at scale 1 in its own colour, laid out on a mat in rows of
    /// six with room to read each silhouette, and a last row of variants (other colours, the set-piece
    /// versions, a toy on its pedestal). Not part of the campaign: ask for it with ?level=showroom.
    ///
    /// The solve script is a guided tour: the bot walks along the rows and stops in front of each toy for
    /// <see cref="StopSeconds"/> (twice that for the first toy of a new row). <see cref="LookTime"/> says
    /// when it is looking at which, so Shots can take a picture of every toy:
    ///
    ///   unity.ps1 exec -Method Toybox.EditorTools.Shots.Capture -UnityArgs '-toyboxLevel','99','-toyboxTimes','2.8,5.8,8.8','-toyboxOverview'
    /// </summary>
    [Level(99, "showroom", "Showroom", Phase = 0)]
    public sealed class Level99Showroom : LevelDefinition
    {
        public const int Columns = 6;
        public const float Spacing = 4.6f, RowSpacing = 6f;
        /// <summary>How long the tour gives each toy. The first toy of every row after the first gets twice that.</summary>
        public const float StopSeconds = 3f;
        const float MatThickness = 0.5f;

        // The display pose of each catalog toy: a turn about Y that shows its best side to the aisle.
        static readonly float[] Yaw =
        {
            30f, -55f, 0f, 0f, 20f, 60f,           // block, cheese, thimble, apple, domino, feather
            20f, 0f, 0f, 70f, 30f, 10f,            // eraser, pebble, marble, plank, gift box, card
            60f, 0f, 25f, 20f, 0f, 155f,           // key, spool, sponge, doorway, ball, fan
            75f, -25f, 0f, 75f, -90f, -90f,        // ruler, domino set, baseball, marker, engine, car (a train heading -X)
        };

        sealed class Stop
        {
            public string Name;
            public Prop Prop;
            public Vector3 Target;
            public Vector3 View;
            public List<Vector3> Path;
            public int EndTick;
        }

        readonly List<Stop> stops = new List<Stop>();
        readonly Dictionary<ToyId, Prop> props = new Dictionary<ToyId, Prop>();

        public override string Blurb => "Every toy in the box.";

        // The mat lies on the room's rug.
        public override float GroundY => -MatThickness;

        /// <summary>The prop of a catalog toy, once the level is built.</summary>
        public Prop PropOf(ToyId id) => props.TryGetValue(id, out Prop prop) ? prop : null;

        /// <summary>How many stops the tour has: one per catalog toy and one per variant.</summary>
        public int StopCount => stops.Count;

        /// <summary>Seconds into the tour at which the bot has been looking at stop <paramref name="index"/> for a good while.</summary>
        public float LookTime(int index) => stops[index].EndTick * Sim.Dt - 0.2f;

        /// <summary>Seconds into the tour at which the bot looks at a catalog toy.</summary>
        public float LookTime(ToyId id) => LookTime(StopOf((int)id));

        public string StopName(int index) => stops[index].Name;

        /// <summary>Where toy number <paramref name="index"/> stands (rows of six, the first row nearest the spawn).</summary>
        public static Vector3 Place(int index)
        {
            int row = index / Columns, column = index % Columns;
            return new Vector3((column - (Columns - 1) * 0.5f) * Spacing, 0f, row * RowSpacing);
        }

        // The tour snakes: left to right along even rows, right to left along odd ones.
        static int StopOf(int index)
        {
            int row = index / Columns, column = index % Columns;
            return row * Columns + (row % 2 == 0 ? column : Columns - 1 - column);
        }

        public override void Build(LevelContext ctx)
        {
            stops.Clear();
            props.Clear();
            IReadOnlyList<ToyDef> toys = ToyCatalog.All;
            int count = toys.Count + Columns;
            int rows = (count + Columns - 1) / Columns;

            // The mat: a low platform with a margin for the aisles round the rows.
            float halfWidth = (Columns - 1) * 0.5f * Spacing + 4.5f;
            float near = -8f, far = (rows - 1) * RowSpacing + 4.5f;
            ctx.AddStatic(BasicToys.Slab(new Vector3(halfWidth * 2f, MatThickness, far - near)), new Vector3(0f, -MatThickness * 0.5f, (near + far) * 0.5f));

            var placed = new Stop[count];
            for (int i = 0; i < toys.Count; i++)
            {
                ToyDef def = toys[i];
                Vector3 at = Place(i) + Vector3.up * (def.RestHeight + 0.005f);
                // Balls would roll off; they wait for the first grab.
                bool round = def.Id == ToyId.Apple || def.Id == ToyId.Pebble || def.Id == ToyId.Marble || def.Id == ToyId.BouncyBall || def.Id == ToyId.Baseball;
                // The train stands on its wheels, which hang below the deck the origin is on.
                if (def.Id == ToyId.TrainEngine || def.Id == ToyId.TrainCar) at.y = 0.84f;
                Prop prop = ToyCatalog.Add(ctx, def.Id, at, Quaternion.Euler(0f, Yaw[i], 0f), 1f, def.Color, o => o.FrozenUntilGrabbed = round && o.Body == PropBody.Dynamic);
                props[def.Id] = prop;
                // Flat toys are looked at from close by, which is from above.
                Vector3 view = def.Size.y < 0.2f ? Place(i) + Vector3.back * Mathf.Clamp(0.7f + def.Radius, 1.2f, 4.3f) : ViewPoint(Place(i), def.Radius);
                placed[i] = new Stop { Name = def.Name, Prop = prop, Target = Place(i) + Vector3.up * def.HalfExtents.y, View = view };
            }
            BuildVariants(ctx, placed, toys.Count);

            // The tour, in snake order, with a detour round the end of the row before each new one.
            float edge = (Columns - 1) * 0.5f * Spacing + 3.2f;
            int tick = 0;
            for (int order = 0; order < count; order++)
            {
                int row = order / Columns, k = order % Columns;
                Stop stop = placed[row * Columns + (row % 2 == 0 ? k : Columns - 1 - k)];
                stop.Path = new List<Vector3>();
                if (k == 0 && row > 0)
                {
                    float side = row % 2 == 1 ? edge : -edge;
                    stop.Path.Add(new Vector3(side, 0f, stops[stops.Count - 1].View.z));
                    stop.Path.Add(new Vector3(side, 0f, stop.View.z));
                }
                stop.Path.Add(stop.View);
                tick += Mathf.RoundToInt((k == 0 && row > 0 ? 2f : 1f) * StopSeconds / Sim.Dt);
                stop.EndTick = tick;
                stops.Add(stop);
            }

            ctx.SetSpawn(new Vector3(placed[0].View.x, 0f, placed[0].View.z - 2f), 0f);
            ctx.Say("The showroom: every toy in the box.", 5f);
        }

        // The aisle side of a toy: far enough back to see all of it.
        static Vector3 ViewPoint(Vector3 place, float radius) =>
            new Vector3(place.x, 0f, place.z - Mathf.Clamp(1.3f + 1.9f * radius, 2.3f, 4.3f));

        // The last row: other colours, the set-piece versions and the toys in use.
        void BuildVariants(LevelContext ctx, Stop[] placed, int first)
        {
            int index = first;

            // Marbles in the other two colours of Level 8 (blue would be Lagoon, which its room bans: Grape).
            Vector3 at = Place(index);
            ToyDef marble = ToyCatalog.Get(ToyId.Marble);
            Prop lemon = ToyCatalog.Add(ctx, ToyId.Marble, at + new Vector3(-0.7f, 0.505f, 0f), 1f, Palette.Lemon, o => o.FrozenUntilGrabbed = true);
            ToyCatalog.Add(ctx, ToyId.Marble, at + new Vector3(0.5f, 0.305f, 0.2f), 0.6f, Palette.Grape, o => o.FrozenUntilGrabbed = true);
            placed[index++] = new Stop { Name = "Marbles", Prop = lemon, Target = at + Vector3.up * 0.5f, View = ViewPoint(at, marble.Radius * 1.6f) };

            // The matryoshka boxes of Level 10 at their three sizes.
            at = Place(index);
            Prop box = ToyCatalog.Add(ctx, ToyId.GiftBox, at + new Vector3(-0.9f, 0.605f, 0f), Quaternion.Euler(0f, 25f, 0f), 1.2f, Palette.Cherry);
            ToyCatalog.Add(ctx, ToyId.GiftBox, at + new Vector3(0.45f, 0.455f, -0.2f), Quaternion.Euler(0f, -15f, 0f), 0.9f, Palette.Lemon);
            ToyCatalog.Add(ctx, ToyId.GiftBox, at + new Vector3(1.45f, 0.305f, 0.3f), Quaternion.Euler(0f, 40f, 0f), 0.6f, Palette.Lagoon);
            placed[index++] = new Stop { Name = "Gift Boxes", Prop = box, Target = at + Vector3.up * 0.6f, View = ViewPoint(at, 1.6f) };

            // A toy on its pedestal, as the levels present it: the cheese crumb of Level 1 on a spool.
            at = Place(index);
            ctx.AddStatic(ToyFactory.ThreadSpool(0.4f, 1.1f, grabbable: false), at + Vector3.up * 0.55f);
            Prop crumb = ToyCatalog.Add(ctx, ToyId.CheeseWedge, at + Vector3.up * (1.1f + 0.105f), Quaternion.Euler(0f, 180f, 0f), 0.4f);
            placed[index++] = new Stop { Name = "Pedestal", Prop = crumb, Target = at + Vector3.up * 1.1f, View = ViewPoint(at, 0.9f) };

            // Building blocks that are not the player's: a barricade in Birch.
            at = Place(index);
            for (int i = 0; i < 3; i++)
            {
                var size = new Vector3(i == 2 ? 1f : 1.6f, 0.8f, 0.8f);
                var offset = new Vector3(i == 0 ? -0.85f : i == 1 ? 0.85f : 0f, i == 2 ? 1.2f : 0.4f, 0f);
                ctx.AddStatic(ToyFactory.WoodenBlock(size, grabbable: false), at + offset);
            }
            placed[index++] = new Stop { Name = "Barricade Blocks", Target = at + Vector3.up * 0.8f, View = ViewPoint(at, 1.8f) };

            // The fan as the machine of Level 5 (there at scale 11).
            at = Place(index);
            GameObject fan = ctx.AddStatic(ToyFactory.DeskFan(grabbable: false), at, Quaternion.Euler(0f, 150f, 0f));
            fan.transform.localScale = Vector3.one * 1.8f;
            placed[index++] = new Stop { Name = "Fan Machine", Target = at + Vector3.up * 1.2f, View = ViewPoint(at, 1.9f) };

            // The seesaw of Level 7: a ruler over a fat marker, pivoting a third of the way along, low end on the floor.
            at = Place(index);
            ctx.AddStatic(ToyFactory.Marker(0.45f, 2.2f), at + Vector3.up * 0.45f);
            const float tilt = 14f, length = 5.6f, thickness = 0.1f;
            float sin = Mathf.Sin(tilt * Mathf.Deg2Rad), cos = Mathf.Cos(tilt * Mathf.Deg2Rad);
            var centre = new Vector3(length / 6f * cos + thickness * 0.5f * sin, 0.9f - length / 6f * sin + thickness * 0.5f * cos, 0f);
            ctx.AddStatic(ToyFactory.Ruler(new Vector3(1.2f, thickness, length), grabbable: false), at + centre, Quaternion.Euler(0f, 90f, 0f) * Quaternion.Euler(tilt, 0f, 0f));
            placed[index] = new Stop { Name = "Seesaw", Target = at + Vector3.up * 0.9f, View = ViewPoint(at, 2.4f) };
        }

        public override IEnumerator Solve(Bot bot)
        {
            Game game = bot.Game;
            foreach (Stop stop in stops)
            {
                bool detour = stop.Path.Count > 1;
                foreach (Vector3 point in stop.Path)
                    yield return bot.WalkTo(point, 0.35f, 12f, detour);
                if (stop.Prop != null && !stop.Prop.Removed) yield return bot.LookAt(stop.Prop);
                else yield return bot.LookAt(stop.Target);
                int until = stop.EndTick;
                if (game.LevelTicks < until) yield return bot.Until(() => game.LevelTicks >= until, 30f);
            }
        }
    }
}
