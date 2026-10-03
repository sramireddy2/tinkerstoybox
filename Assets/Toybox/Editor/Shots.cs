using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Render;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Toybox.EditorTools
{
    public sealed class ShotRequest
    {
        /// <summary>Id of a registered level. It also names the files.</summary>
        public int Level;
        /// <summary>When set, this level is captured instead of the registered one (unregistered levels, tests).</summary>
        public LevelDefinition Definition;
        /// <summary>Seconds of bot autoplay before each capture; 0 is right after the spawn.</summary>
        public float[] Times = { 0f };
        public int Width = 1280, Height = 720;
        /// <summary>Absolute, or relative to the project folder.</summary>
        public string OutputDirectory = "tools/out/shots";
        /// <summary>Also capture the whole level from high above at every time.</summary>
        public bool Overview;
    }

    /// <summary>
    /// Renders PNGs of a level without Play Mode and without a build, for looking at what the game shows:
    ///
    ///   tools\unity.ps1 exec -Method Toybox.EditorTools.Shots.Capture
    ///       -UnityArgs '-toyboxLevel','0','-toyboxTimes','0,2,5','-toyboxSize','1280x720','-toyboxOverview'
    ///
    /// -toyboxLevel id (0), -toyboxTimes seconds of autoplay before each capture ("0"), -toyboxSize WxH
    /// (1280x720), -toyboxOut directory (tools/out/shots), -toyboxOverview adds a third-person overview.
    ///
    /// It creates a Game exactly like a test does, puts the game's own camera rig on it, lets the bot play
    /// the level's Solve script and renders through URP into a texture. Files are named
    /// levelNN-tSS.png (and levelNN-tSS-overview.png); each is reported as "[Toybox] shot: path".
    /// </summary>
    public static class Shots
    {
        const float OverviewFieldOfView = 40f;
        static readonly Quaternion OverviewRotation = Quaternion.Euler(52f, 28f, 0f);
        static readonly Color MarkerColor = new Color(0.95f, 0.15f, 0.65f);

        [MenuItem("Toybox/Capture Shots")]
        public static void Capture()
        {
            var request = new ShotRequest
            {
                Level = ToyboxArgs.GetInt("-toyboxLevel", 0),
                Times = ParseTimes(ToyboxArgs.Get("-toyboxTimes", "0")),
                OutputDirectory = ToyboxArgs.Get("-toyboxOut", "tools/out/shots"),
                Overview = ToyboxArgs.Has("-toyboxOverview"),
            };
            ParseSize(ToyboxArgs.Get("-toyboxSize", "1280x720"), ref request.Width, ref request.Height);
            Run(request);
        }

        /// <summary>Captures the shots and returns the absolute paths of the files written, in order.</summary>
        public static List<string> Run(ShotRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (Application.isPlaying) throw new InvalidOperationException("Shots.Capture works outside Play Mode only.");
            if (request.Definition == null && !LevelRegistry.Has(request.Level))
                throw new ArgumentException("There is no level with id " + request.Level + ".");

            string directory = request.OutputDirectory ?? "tools/out/shots";
            if (!Path.IsPathRooted(directory))
                directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), directory);
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);

            float[] times = Normalize(request.Times);
            var files = new List<string>();

            // A Game left behind by an interrupted run would make Game.Create throw.
            Game.Current?.Dispose();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Without this the first render shows cyan placeholders while shader variants compile in the background.
            bool previousAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;

            Game game = null;
            CameraRig rig = null;
            RenderTexture target = null;
            try
            {
                game = Game.Create(new GameOptions());
                game.LoadLevel(request.Definition ?? LevelRegistry.Get(request.Level));
                rig = CameraRig.Create(game);

                target = new RenderTexture(request.Width, request.Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = "Toybox Shot",
                    antiAliasing = 4,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                target.Create();
                Warm(rig.Camera, target);

                // The bot only produces input, so the level plays exactly as it does in its test.
                var bot = new Bot(game);
                BotRunner script = null;
                try
                {
                    script = new BotRunner(game.Level.Solve(bot));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Toybox] bot error: " + e.Message);
                }

                int tick = 0;
                foreach (float time in times)
                {
                    int until = Mathf.RoundToInt(time / Sim.Dt);
                    for (; tick < until; tick++)
                    {
                        if (script != null)
                        {
                            try
                            {
                                // Once the script has ended the bot stands still and the level just keeps ticking.
                                if (!script.Advance()) script = null;
                            }
                            catch (Exception e)
                            {
                                script = null;
                                Debug.LogWarning("[Toybox] bot error: " + e.Message);
                            }
                        }
                        game.Tick();
                    }

                    string name = "level" + request.Level.ToString("00", CultureInfo.InvariantCulture) + "-t" + TimeLabel(time);
                    rig.Apply(1f);
                    files.Add(Shoot(rig.Camera, target, Path.Combine(directory, name + ".png")));
                    if (request.Overview)
                        files.Add(ShootOverview(game, rig, target, Path.Combine(directory, name + "-overview.png")));
                }
            }
            finally
            {
                rig?.Dispose();
                game?.Dispose();
                if (target != null)
                {
                    target.Release();
                    Object.DestroyImmediate(target);
                }
                ShaderUtil.allowAsyncCompilation = previousAsync;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            return files;
        }

        // The very first render after a script reload (the pipeline has just been created) draws every
        // object with the constants of one material - all toys the colour of the floor. From the second
        // render on it is right, so one frame is rendered and thrown away.
        static void Warm(Camera camera, RenderTexture target)
        {
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = target;
                camera.Render();
            }
            finally
            {
                camera.targetTexture = previousTarget;
            }
        }

        internal static string Shoot(Camera camera, RenderTexture target, string path)
        {
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            // No alpha channel: the PNG must not come out see-through where the camera left alpha at zero.
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(image);
            }
            Debug.Log("[Toybox] shot: " + path);
            return path;
        }

        // The whole level seen from high up, with a marker where the player (who has no body) stands.
        static string ShootOverview(Game game, CameraRig rig, RenderTexture target, string path)
        {
            Bounds bounds = LevelBounds(game);
            var cameraObject = new GameObject("Overview Camera") { hideFlags = HideFlags.DontSave };
            cameraObject.transform.SetParent(game.Root.transform, false);
            GameObject marker = PlayerMarker(game);

            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float previousShadowDistance = urp != null ? urp.shadowDistance : 0f;
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.CopyFrom(rig.Camera);
                camera.scene = game.Scene;
                camera.fieldOfView = OverviewFieldOfView;
                camera.aspect = (float)target.width / target.height;

                float distance = FitDistance(bounds, OverviewRotation, OverviewFieldOfView, camera.aspect);
                float radius = bounds.extents.magnitude;
                camera.transform.SetPositionAndRotation(bounds.center - OverviewRotation * Vector3.forward * distance, OverviewRotation);
                camera.nearClipPlane = Mathf.Max(0.1f, distance - radius * 1.2f);
                camera.farClipPlane = distance + radius * 1.2f;
                // From this far away the whole level lies beyond the game's shadow distance.
                if (urp != null) urp.shadowDistance = distance + radius;
                return Shoot(camera, target, path);
            }
            finally
            {
                if (urp != null) urp.shadowDistance = previousShadowDistance;
                Object.DestroyImmediate(marker);
                Object.DestroyImmediate(cameraObject);
            }
        }

        static Bounds LevelBounds(Game game)
        {
            Renderer[] renderers = game.LevelRoot != null ? game.LevelRoot.GetComponentsInChildren<Renderer>() : new Renderer[0];
            if (renderers.Length == 0) return new Bounds(game.Player.Position, Vector3.one * 10f);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            bounds.Encapsulate(game.Player.Position);
            return bounds;
        }

        // How far back along its view direction a camera has to be for the whole box to be in the picture.
        static float FitDistance(Bounds bounds, Quaternion rotation, float verticalFov, float aspect)
        {
            float tanV = Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * aspect;
            Quaternion toCamera = Quaternion.Inverse(rotation);
            Vector3 e = bounds.extents;
            float distance = 1f;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                Vector3 local = toCamera * corner;
                distance = Mathf.Max(distance, Mathf.Abs(local.x) / tanH - local.z, Mathf.Abs(local.y) / tanV - local.z);
            }
            return distance * 1.04f;
        }

        // Renderers only - a collider here would join the simulation.
        static GameObject PlayerMarker(Game game)
        {
            Player player = game.Player;
            var marker = new GameObject("Player Marker") { hideFlags = HideFlags.DontSave };
            marker.transform.SetParent(game.Root.transform, false);
            marker.transform.SetPositionAndRotation(player.Position, Quaternion.Euler(0f, player.Yaw, 0f));

            Part(marker, ToyMeshes.Cylinder, new Vector3(0f, player.Height * 0.5f, 0f),
                new Vector3(player.Radius * 2f, player.Height, player.Radius * 2f), MarkerColor);
            // A nose at eye height shows which way the player faces.
            Part(marker, ToyMeshes.Cube, new Vector3(0f, player.EyeHeight, player.Radius * 1.5f),
                new Vector3(player.Radius * 0.6f, player.Radius * 0.6f, player.Radius * 2f), ToyMaterials.Ink);
            return marker;
        }

        static void Part(GameObject parent, Mesh mesh, Vector3 localPosition, Vector3 size, Color color)
        {
            var part = new GameObject("Part") { hideFlags = HideFlags.DontSave };
            part.transform.SetParent(parent.transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = size;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = ToyMaterials.Get(ToyMaterialKind.Matte, color);
        }

        /// <summary>"0,2,5" to seconds: sorted, without duplicates or negative values; {0} if nothing is usable.</summary>
        public static float[] ParseTimes(string text)
        {
            var times = new List<float>();
            if (!string.IsNullOrEmpty(text))
            {
                foreach (string part in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds))
                        times.Add(seconds);
            }
            return Normalize(times.ToArray());
        }

        /// <summary>"1280x720" to width and height; anything unusable leaves the values as they are.</summary>
        public static void ParseSize(string text, ref int width, ref int height)
        {
            if (string.IsNullOrEmpty(text)) return;
            string[] parts = text.Split(new[] { 'x', 'X', '*', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return;
            if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)) return;
            if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)) return;
            width = Mathf.Clamp(w, 16, 8192);
            height = Mathf.Clamp(h, 16, 8192);
        }

        static float[] Normalize(float[] times)
        {
            var sorted = new List<float>();
            if (times != null)
                foreach (float time in times)
                    if (time >= 0f && !float.IsInfinity(time) && !float.IsNaN(time) && !sorted.Contains(time)) sorted.Add(time);
            if (sorted.Count == 0) sorted.Add(0f);
            sorted.Sort();
            return sorted.ToArray();
        }

        // 0 -> "00", 2 -> "02", 2.5 -> "02.5", 120 -> "120"
        static string TimeLabel(float seconds) => seconds.ToString("00.##", CultureInfo.InvariantCulture);
    }
}
