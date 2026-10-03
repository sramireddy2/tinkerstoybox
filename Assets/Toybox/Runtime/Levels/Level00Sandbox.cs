using System.Collections;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.Levels
{
    /// <summary>
    /// A test room for the core mechanic: a floor, walls, a walkable ramp up to a lookout, a pile of toys
    /// and a gap that is too wide to jump. The plank on the floor is far too short to bridge it - until
    /// it is picked up and put down across the gap from a distance.
    /// </summary>
    [Level(0, "sandbox", "Sandbox", Phase = 0)]
    public sealed class Level00Sandbox : LevelDefinition
    {
        const float GapNear = 8f, GapFar = 18f;
        const float PlankLength = 4f, PlankThickness = 0.15f;

        Prop plank;

        public override string Blurb => "Cross the gap.";

        public override string[] Hints => new[]
        {
            "The plank is too short to reach across.",
            "Things keep the size they appear to have. Far away, that is a lot bigger.",
            "Pick the plank up, look at the middle of the gap and let go.",
        };

        public override void Build(LevelContext ctx)
        {
            // Floors: x in [-10, 10]; the near one ends at z = 8, the far one starts at z = 18.
            ctx.AddStatic(BasicToys.Slab(new Vector3(20f, 1f, 20f)), new Vector3(0f, -0.5f, -2f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(20f, 1f, 12f)), new Vector3(0f, -0.5f, 24f));

            // Walls around both halves, open only downward into the gap.
            ctx.AddStatic(BasicToys.Slab(new Vector3(1f, 9f, 43f), ToyMaterials.Wall), new Vector3(-10.5f, 3.5f, 9f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(1f, 9f, 43f), ToyMaterials.Wall), new Vector3(10.5f, 3.5f, 9f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(22f, 9f, 1f), ToyMaterials.Wall), new Vector3(0f, 3.5f, -12.5f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(22f, 9f, 1f), ToyMaterials.Wall), new Vector3(0f, 3.5f, 30.5f));

            // A lookout in the back left corner with a 25 degree ramp leading up to it.
            ctx.AddStatic(BasicToys.Slab(new Vector3(4f, 2f, 4f), ToyMaterials.Wall), new Vector3(-8f, 1f, -10f));
            ctx.AddStatic(BasicToys.Wedge(4.3f, 2f, 4f, ToyMaterials.Wall), new Vector3(-8f, 1f, -5.85f), Quaternion.Euler(0f, 180f, 0f));

            plank = ctx.AddProp(BasicToys.Block(new Vector3(1.2f, PlankThickness, PlankLength), ToyMaterials.Orange),
                new Vector3(0f, PlankThickness * 0.5f, 2f), new PropOptions { Name = "Plank", Friction = 0.8f });

            ctx.AddProp(BasicToys.Block(0.5f, ToyMaterials.Red), new Vector3(4f, 0.25f, 0f), new PropOptions { Name = "Small Block" });
            ctx.AddProp(BasicToys.Block(2f, ToyMaterials.Purple), new Vector3(6.5f, 1f, -6f), new PropOptions { Name = "Big Block" });
            ctx.AddProp(BasicToys.Ball(0.4f), new Vector3(-4f, 0.4f, 1f), new PropOptions { Name = "Ball", Bounciness = 0.6f });
            ctx.AddProp(BasicToys.Cylinder(0.5f, 1.2f), new Vector3(5f, 0.6f, 4f), new PropOptions { Name = "Drum" });
            ctx.AddProp(BasicToys.Wedge(2f, 1f, 1.5f), new Vector3(-5f, 0.5f, 5f), new PropOptions { Name = "Wedge" });

            ctx.AddCheckpoint(Volume.Box(20f, 4f, 3f), new Vector3(0f, 2f, 20f));
            ctx.AddExit(new Vector3(0f, 1.5f, 27f), new Vector3(4f, 3f, 2f));
            ctx.SetSpawn(new Vector3(0f, 0f, -8f), 0f);
            ctx.Say("Cross the gap.", 5f);
        }

        public override IEnumerator Solve(Bot bot)
        {
            Game game = bot.Game;

            yield return bot.WalkTo(new Vector3(0f, 0f, -2f));
            yield return bot.Grab(plank);
            // Looking at a point just above the middle of the gap carries the plank out until both of its
            // ends come down on the floors; by then it is several times its original length.
            yield return bot.DropAt(new Vector3(0f, 0.35f, (GapNear + GapFar) * 0.5f));
            yield return bot.Wait(1f);

            // Step up onto the near end of the bridge with a hop, then walk across.
            float nearEnd = plank.Center.z - PlankLength * 0.5f * plank.Scale;
            yield return bot.WalkTo(new Vector3(0f, 0f, nearEnd - 0.9f), 0.2f);
            yield return bot.Jump();
            yield return bot.WalkTo(new Vector3(0f, 0f, nearEnd + 2f));
            yield return bot.WalkTo(new Vector3(0f, 0f, 27f), 0.5f, 15f);
            yield return bot.Until(() => game.LevelCompleted, 3f);
        }
    }
}
