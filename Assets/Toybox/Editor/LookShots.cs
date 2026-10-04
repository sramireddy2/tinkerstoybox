using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Toys;
using UnityEngine;

namespace Toybox.EditorTools
{
    /// <summary>
    /// A small stage for looking at the look (ART_BIBLE 2.5, 2.6, 8, 9): catalog toys of every recipe on
    /// the room's own play surface, a level platform with a ramp, a non-grabbable prop and a pressure
    /// plate. Its solve script is a timetable, not a puzzle: the bot picks the apple up at
    /// <see cref="GrabAt"/> seconds, keeps it in hand while it turns (the picture of a held toy), lets go
    /// at <see cref="ReleaseAt"/> seconds over the far end of the rug (the apple has grown several times
    /// by then: pool flood, splash ring, shadow back) and then stands still. It is not registered; the
    /// capture tool below builds it by hand.
    /// </summary>
    public sealed class LookCheckLevel : LevelDefinition
    {
        public const float GrabAt = 1f, ReleaseAt = 4f;

        readonly string environment;
        Prop apple;

        public LookCheckLevel(string environment = "sunny-rug") => this.environment = environment;

        public override string Slug => "look-check";
        public override string Title => "Look Check";
        public override string Blurb => "A stage for looking at the look.";
        public override string[] Hints => new[] { "Nothing to solve here.", "The apple is the one the bot picks up.", "It lets go four seconds in." };
        public override string Environment => environment;

        public override void Build(LevelContext ctx)
        {
            // Level statics (light tops, deep sides): a platform with a ramp on the right, a low wall behind.
            ctx.AddStatic(BasicToys.Slab(new Vector3(7f, 1.6f, 6f)), new Vector3(6.5f, 0.8f, 13f));
            ctx.AddStatic(BasicToys.Ramp(4f, 1.6f, 3f), new Vector3(6.5f, 0.8f, 8f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(22f, 4f, 1f)), new Vector3(0f, 2f, 19f));
            ctx.AddStatic(BasicToys.Slab(new Vector3(1f, 2.4f, 9f)), new Vector3(-10.5f, 1.2f, 14f));

            // One toy of every recipe, at different distances, all on the room's play surface.
            apple = Toy(ctx, ToyId.Apple, 1.2f, 0f, 5f, 1f);
            Toy(ctx, ToyId.CheeseWedge, -2.2f, 0f, 4.2f, 1.4f);
            Toy(ctx, ToyId.WoodenBlock, 2.6f, 0f, 5.2f, 1f);
            Toy(ctx, ToyId.Marble, -1.2f, 0f, 6.6f, 1.2f);
            Toy(ctx, ToyId.Thimble, -3.6f, 0f, 7f, 1.6f);
            Toy(ctx, ToyId.BouncyBall, 3.4f, 0f, 3.4f, 0.8f);
            Toy(ctx, ToyId.Sponge, -4.6f, 0f, 4.4f, 1.6f);
            Toy(ctx, ToyId.Eraser, 0.2f, 0f, 3.1f, 1f);
            Toy(ctx, ToyId.Feather, -1.4f, 0f, 2.6f, 1.6f);
            Toy(ctx, ToyId.GiftBox, -5.6f, 0f, 9.6f, 2.2f);
            Toy(ctx, ToyId.Key, 1.8f, 0f, 2.6f, 1f);
            Toy(ctx, ToyId.PlayingCard, 4.6f, 0f, 6.4f, 1.2f);
            Toy(ctx, ToyId.Domino, -7.4f, 0f, 6.4f, 1.6f);
            Toy(ctx, ToyId.Pebble, 6.4f, 1.6f, 12f, 1.4f);
            // A set piece nobody can lift (Birch, no rim, no pool) next to the toys.
            ctx.AddStatic(ToyFactory.WoodenBlock(1.6f, grabbable: false), new Vector3(-8.2f, 0.8f, 10.5f));

            // One gadget, for the Ink body and its one signal element.
            new PressurePlate(ctx, new PressurePlateOptions
            {
                Name = "Look Plate",
                Sensor = Zone.Cylinder(4.4f, 9.6f, 0.9f, -0.2f, 1.2f),
                MinMass = 1f,
            });

            ctx.AddExit(new Vector3(-6f, 1.5f, 17f), new Vector3(3f, 3f, 2f)).Lock();
            ctx.SetSpawn(new Vector3(0f, 0f, 0f), 0f);
            ctx.Say("Things keep the size they appear to have.", 6f);
        }

        static Prop Toy(LevelContext ctx, ToyId id, float x, float y, float z, float scale)
        {
            ToyDef def = ToyCatalog.Get(id);
            return ToyCatalog.Add(ctx, id, new Vector3(x, y + def.RestHeight * scale + 0.01f, z), Quaternion.Euler(0f, 20f, 0f), scale);
        }

        public override IEnumerator Solve(Bot bot)
        {
            Game game = bot.Game;
            yield return bot.Until(() => game.LevelTicks >= Mathf.RoundToInt(GrabAt / Sim.Dt) - 30, 5f);
            yield return bot.Grab(apple);
            // In hand: look up and to the left, where there is nothing but the far wall of the level.
            yield return bot.LookAt(new Vector3(-3f, 2.6f, 18f));
            yield return bot.Until(() => game.LevelTicks >= Mathf.RoundToInt(ReleaseAt / Sim.Dt) - 45, 10f);
            // Down to the rug, some way off and clear of the other toys: the apple comes to rest there, grown.
            yield return bot.LookAt(new Vector3(0.5f, 1.2f, 15f));
            yield return bot.Until(() => game.LevelTicks >= Mathf.RoundToInt(ReleaseAt / Sim.Dt), 10f);
            yield return bot.Drop();
            yield return bot.Wait(30f);
        }
    }

    /// <summary>
    /// Pictures of <see cref="LookCheckLevel"/>, for checking the look against the art bible:
    ///
    ///   tools\unity.ps1 exec -Method Toybox.EditorTools.LookShots.Capture -UnityArgs '-toyboxEnv','all','-toyboxOut','tools/out/shots/look'
    ///
    /// -toyboxEnv a preset key, several separated by commas, or "all" (default sunny-rug); -toyboxTimes
    /// (default "0.5,3,4.05,4.2,6": at rest, in hand, just after the release, the splash, settled);
    /// -toyboxSize, -toyboxOut, -toyboxOverview, -toyboxQuality, -toyboxPlain and -toyboxUi as for
    /// Shots.Capture; -toyboxTag text is added to the file names. Files are named
    /// look-&lt;key&gt;[-tag]-tSS.png.
    /// </summary>
    public static class LookShots
    {
        public static void Capture()
        {
            string keys = ToyboxArgs.Get("-toyboxEnv", "sunny-rug");
            var presets = new List<string>();
            if (keys == "all") presets.AddRange(EnvironmentPreset.Keys);
            else presets.AddRange(keys.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries));

            int width = 1280, height = 720;
            Shots.ParseSize(ToyboxArgs.Get("-toyboxSize", "1280x720"), ref width, ref height);
            string tag = ToyboxArgs.Get("-toyboxTag");

            foreach (string raw in presets)
            {
                string key = raw.Trim();
                if (EnvironmentPreset.Find(key) == null)
                {
                    Debug.LogWarning("[Toybox] look shots: there is no environment '" + key + "'.");
                    continue;
                }
                var request = new ShotRequest
                {
                    Level = 97,
                    Definition = new LookCheckLevel(key),
                    Times = Shots.ParseTimes(ToyboxArgs.Get("-toyboxTimes", "0.5,3,4.05,4.2,6")),
                    Width = width,
                    Height = height,
                    OutputDirectory = ToyboxArgs.Get("-toyboxOut", "tools/out/shots/look"),
                    Overview = ToyboxArgs.Has("-toyboxOverview"),
                    Plain = ToyboxArgs.Has("-toyboxPlain"),
                    Quality = Shots.ParseQuality(ToyboxArgs.Get("-toyboxQuality")),
                };
                foreach (string file in Shots.Run(request))
                {
                    string name = Path.GetFileName(file);
                    int cut = name.IndexOf("-t", StringComparison.Ordinal);
                    string renamed = Path.Combine(Path.GetDirectoryName(file), "look-" + key + (string.IsNullOrEmpty(tag) ? "" : "-" + tag) + (cut >= 0 ? name.Substring(cut) : "-" + name));
                    if (File.Exists(renamed)) File.Delete(renamed);
                    File.Move(file, renamed);
                    Debug.Log("[Toybox] look shot: " + renamed);
                }
            }
        }
    }
}
