using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Platform;
using UnityEditor;
using UnityEngine;

namespace Toybox.EditorTools
{
    /// <summary>
    /// The room's setup steps (orders 200-299): the template materials of Toybox/RoomLit and Toybox/Flat
    /// under Resources/Materials. That is all it takes for the two shaders to ship in a player build and
    /// for <c>Materials.Room</c> / <c>Materials.Flat</c> to switch to them (they clone these templates and
    /// set every property by name).
    ///
    ///   tools\unity.ps1 exec -Method Toybox.EditorTools.ProjectSetup.Run -UnityArgs '-toyboxSteps','RoomSetup'
    /// </summary>
    public static class RoomSetup
    {
        public const string RoomLitShader = "Toybox/RoomLit";
        public const string FlatShader = "Toybox/Flat";
        public const string RoomLitPath = SetupUtil.MaterialsDir + "/RoomLit.mat";
        public const string FlatPath = SetupUtil.MaterialsDir + "/Flat.mat";

        [SetupStep(200)]
        public static void CreateMaterials()
        {
            SetupUtil.EnsureFolder(SetupUtil.MaterialsDir);
            Template(RoomLitPath, RoomLitShader);
            Template(FlatPath, FlatShader);
            AssetDatabase.SaveAssets();
        }

        // The game's shaders have no keywords and no instancing variant: a template carries the shader and
        // nothing else that could ask for a variant the build does not have.
        static void Template(string path, string shaderName)
        {
            Material material = SetupUtil.LoadOrCreateMaterial(path, shaderName);
            bool changed = false;
            if (material.enableInstancing)
            {
                material.enableInstancing = false;
                changed = true;
            }
            if (material.shaderKeywords.Length > 0)
            {
                material.shaderKeywords = new string[0];
                changed = true;
            }
            if (changed) EditorUtility.SetDirty(material);
        }
    }

    /// <summary>
    /// A registered level put into another room: it builds and solves exactly like the level it wraps and
    /// only answers differently when asked for its environment (and, optionally, where the player starts
    /// - for looking at the room from somewhere else). For pictures and tests of the six presets; the
    /// simulation solves the room and makes its colliders as for any level.
    /// </summary>
    public sealed class RoomPreviewLevel : LevelDefinition
    {
        readonly LevelDefinition inner;
        readonly string environment;
        readonly int visit;

        /// <summary>When set: feet position, yaw and pitch the player starts with instead of the level's own spawn.</summary>
        public Vector3? SpawnPosition;
        public float SpawnYaw, SpawnPitch;

        public RoomPreviewLevel(LevelDefinition inner, string environment, int visit = 0)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            this.environment = environment;
            this.visit = visit;
        }

        public LevelDefinition Inner => inner;
        public override string Slug => inner.Slug + "-in-" + environment;
        public override string Title => inner.Title;
        public override string Blurb => inner.Blurb;
        public override string[] Hints => inner.Hints;
        public override string Environment => environment;
        public override int EnvironmentVisit => visit;
        public override float GroundY => inner.GroundY;
        public override float KillY => inner.KillY;

        public override void Build(LevelContext ctx)
        {
            inner.Build(ctx);
            if (SpawnPosition.HasValue) ctx.SetSpawn(SpawnPosition.Value, SpawnYaw, SpawnPitch);
        }

        public override IEnumerator Solve(Bot bot) => inner.Solve(bot);
    }

    /// <summary>
    /// Pictures of a level in each of the six rooms, without touching the level:
    ///
    ///   tools\unity.ps1 exec -Method Toybox.EditorTools.RoomShots.Capture -UnityArgs '-toyboxEnv','all','-toyboxOut','tools/out/shots/room'
    ///
    /// -toyboxEnv a preset key, several separated by commas, or "all" (the default); -toyboxLevel,
    /// -toyboxTimes, -toyboxSize, -toyboxOut, -toyboxOverview and -toyboxQuality as for Shots.Capture;
    /// -toyboxVisit n the repeat-visit index (it moves the sun); -toyboxSpawn "x,y,z,yaw,pitch" puts the
    /// player somewhere else (feet position), to look at the room rather than the level; -toyboxTag text
    /// is added to the file names; -toyboxPresenters "LightingRig,RoomVisuals,PoolSystem" renders with
    /// only the presenters whose type names are listed (to look at one area's work without the others').
    /// Files are named &lt;key&gt;[-tag]-tSS.png.
    /// </summary>
    public static class RoomShots
    {
        public static void Capture()
        {
            string keys = ToyboxArgs.Get("-toyboxEnv", "all");
            var presets = new List<string>();
            if (string.IsNullOrEmpty(keys) || keys == "all") presets.AddRange(EnvironmentPreset.Keys);
            else presets.AddRange(keys.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries));

            int level = ToyboxArgs.GetInt("-toyboxLevel", 0);
            int width = 1280, height = 720;
            Shots.ParseSize(ToyboxArgs.Get("-toyboxSize", "1280x720"), ref width, ref height);
            float[] spawn = ParseNumbers(ToyboxArgs.Get("-toyboxSpawn"));
            string tag = ToyboxArgs.Get("-toyboxTag");
            List<PresenterRegistry.Entry> presenters = PickPresenters(ToyboxArgs.Get("-toyboxPresenters"));

            foreach (string raw in presets)
            {
                string key = raw.Trim();
                if (EnvironmentPreset.Find(key) == null)
                {
                    Debug.LogWarning("[Toybox] room shots: there is no environment '" + key + "'. The presets are: " + string.Join(", ", EnvironmentPreset.Keys) + ".");
                    continue;
                }
                var preview = new RoomPreviewLevel(LevelRegistry.Get(level), key, ToyboxArgs.GetInt("-toyboxVisit", 0));
                if (spawn != null && spawn.Length >= 3)
                {
                    preview.SpawnPosition = new Vector3(spawn[0], spawn[1], spawn[2]);
                    preview.SpawnYaw = spawn.Length > 3 ? spawn[3] : 0f;
                    preview.SpawnPitch = spawn.Length > 4 ? spawn[4] : 0f;
                }
                var request = new ShotRequest
                {
                    Level = level,
                    Definition = preview,
                    Times = Shots.ParseTimes(ToyboxArgs.Get("-toyboxTimes", "0")),
                    Width = width,
                    Height = height,
                    OutputDirectory = ToyboxArgs.Get("-toyboxOut", "tools/out/shots/room"),
                    Overview = ToyboxArgs.Has("-toyboxOverview"),
                    Quality = Shots.ParseQuality(ToyboxArgs.Get("-toyboxQuality")),
                    Presenters = presenters,
                };
                foreach (string file in Shots.Run(request))
                {
                    // levelNN-tSS[-overview].png -> <key>[-tag]-tSS[-overview].png, so the six rooms sit side by side.
                    string name = Path.GetFileName(file);
                    int cut = name.IndexOf("-t", StringComparison.Ordinal);
                    string renamed = Path.Combine(Path.GetDirectoryName(file), key + (string.IsNullOrEmpty(tag) ? "" : "-" + tag) + (cut >= 0 ? name.Substring(cut) : "-" + name));
                    if (File.Exists(renamed)) File.Delete(renamed);
                    File.Move(file, renamed);
                    Debug.Log("[Toybox] room shot: " + renamed);
                }
            }
        }

        // The registered presenters whose type name is one of the comma-separated names; null (all) for no list.
        static List<PresenterRegistry.Entry> PickPresenters(string names)
        {
            if (string.IsNullOrEmpty(names)) return null;
            var wanted = new HashSet<string>(names.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
            var picked = new List<PresenterRegistry.Entry>();
            foreach (PresenterRegistry.Entry entry in PresenterRegistry.All)
                if (wanted.Contains(entry.Type.Name)) picked.Add(entry);
            return picked;
        }

        static float[] ParseNumbers(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var numbers = new List<float>();
            foreach (string part in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) numbers.Add(value);
            return numbers.ToArray();
        }
    }
}
