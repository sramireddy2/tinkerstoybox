using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Toybox.Engine;
using Toybox.Platform;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Toybox.EditorTools
{
    /// <summary>
    /// A smoke check of the real thing: opens the Main scene, enters Play Mode, lets the Bootstrap create
    /// the GameRunner, turns autoplay on and watches the bot solve the first level through the real
    /// Update loop, takes pictures through the game's own camera, leaves Play Mode again and writes what it
    /// saw to tools/out/playcheck/result.txt. -toyboxPlain plays with the debug look; -toyboxUrl "?level=3"
    /// gives Play Mode the query string a page address would carry.
    ///
    /// CAUTION: Run returns as soon as Play Mode has been requested; the check itself takes many editor
    /// frames (about 15 seconds). While it runs, the editor is in Play Mode and must not be asked to run
    /// tests or other methods. tools/unity.ps1 releases its lock as soon as Run returns, so the caller
    /// has to hold the "TinkersToyboxUnityBatch" mutex itself until result.txt appears, and says that it
    /// does by passing -toyboxExclusive. Without that argument Run refuses to start.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayCheck
    {
        const string StateKey = "Toybox.PlayCheck.State";
        const string DeadlineKey = "Toybox.PlayCheck.Deadline";
        const string OutputDirectory = "tools/out/playcheck";
        const double TimeoutSeconds = 100.0;
        const float WatchSeconds = 45f;

        static readonly List<string> Report = new List<string>();
        static readonly List<string> Problems = new List<string>();
        static readonly float[] ShotTimes = { 0.5f, 3f, 8f };

        static GameRunner runner;
        static Game game;
        static double startedAt;
        static int frames, startTicks, loads, completions, shotsTaken;
        static float longestFrame;
        static bool wrapped;
        static RenderTexture target;
        static string firstScene;

        // Physics state before Play Mode, to see whether the game puts it back.
        static SimulationMode modeBefore;
        static Vector3 gravityBefore;

        static PlayCheck()
        {
            // Entering Play Mode reloads the scripting domain; pick the check up again afterwards.
            if (SessionState.GetString(StateKey, "") == "") return;
            EditorApplication.update += Pump;
            // From the first moment of Play Mode, so that a failure in the game's start-up is seen too.
            Application.logMessageReceived += OnLog;
        }

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        static string OutputPath => Path.Combine(ProjectRoot, OutputDirectory);
        static string ResultPath => Path.Combine(OutputPath, "result.txt");

        public static void Run()
        {
            if (!ToyboxArgs.Has("-toyboxExclusive"))
                throw new InvalidOperationException(
                    "PlayCheck.Run puts the shared editor into Play Mode for a while. Hold the batch mutex until " +
                    "tools/out/playcheck/result.txt appears and pass -toyboxExclusive (see the comment on PlayCheck).");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already in Play Mode.");
            Directory.CreateDirectory(OutputPath);
            if (File.Exists(ResultPath)) File.Delete(ResultPath);
            Game.Current?.Dispose();

            EditorSceneManager.OpenScene(ProjectSetup.ScenePath, OpenSceneMode.Single);
            // The editor has no page address; this is what the runner starts with instead.
            string url = ToyboxArgs.Get("-toyboxUrl", "");
            if (ToyboxArgs.Has("-toyboxPlain")) url += (url.Contains("?") ? "&" : "?") + "plain=1";
            SessionState.SetString(LaunchOptions.EditorOverrideKey, url);
            // The check starts from nothing: no saved progress or settings of this machine, and none left behind.
            SessionState.SetBool(PrefStores.EditorMemoryKey, true);
            SessionState.SetString(StateKey, "entering");
            SessionState.SetFloat(DeadlineKey, (float)(EditorApplication.timeSinceStartup + TimeoutSeconds));
            SessionState.SetInt("Toybox.PlayCheck.Mode", (int)Physics.simulationMode);
            SessionState.SetVector3("Toybox.PlayCheck.Gravity", Physics.gravity);
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
            // Normally the domain reload on entering Play Mode re-subscribes both; this covers a project
            // that has the reload switched off.
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            EditorApplication.EnterPlaymode();
            Debug.Log("[Toybox] play check: entering Play Mode; the result goes to " + ResultPath);
        }

        /// <summary>Leaves Play Mode whatever the check is doing.</summary>
        public static void Abort()
        {
            Problems.Add("aborted from outside");
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            SessionState.SetString(StateKey, "exiting");
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
        }

        static void Pump()
        {
            string state = SessionState.GetString(StateKey, "");
            if (state == "")
            {
                EditorApplication.update -= Pump;
                return;
            }

            try
            {
                if (state != "exiting" && EditorApplication.timeSinceStartup > SessionState.GetFloat(DeadlineKey, 0f))
                {
                    Problems.Add("timed out in state '" + state + "'");
                    Leave();
                    return;
                }

                switch (state)
                {
                    case "entering":
                        if (EditorApplication.isPlaying) Attach();
                        break;
                    case "running":
                        Watch();
                        break;
                    case "exiting":
                        if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish();
                        break;
                }
            }
            catch (Exception e)
            {
                Problems.Add("check threw " + e.GetType().Name + ": " + e.Message);
                if (state == "exiting") Finish();
                else Leave();
            }
        }

        static void Attach()
        {
            runner = Object.FindAnyObjectByType<GameRunner>();
            if (runner == null || runner.Game == null) return;   // Start has not run yet
            game = runner.Game;

            game.Events.LevelLoaded += e => loads++;
            game.Events.LevelCompleted += e => completions++;

            firstScene = game.Scene.name;
            Report.Add("entered Play Mode; GameRunner present; level " + runner.LevelId + " '" + (game.Level != null ? game.Level.Title : "none") + "'");
            Report.Add("simulation scene '" + game.Scene.name + "' loaded=" + game.Scene.isLoaded + " valid=" + game.Scene.IsValid() +
                       "; active scene '" + SceneManager.GetActiveScene().name + "'; scenes loaded " + SceneManager.sceneCount);
            Report.Add("camera " + (runner.Rig != null && runner.Rig.Camera != null ? "in scene '" + runner.Rig.Camera.gameObject.scene.name + "'" : "MISSING") +
                       "; flow " + runner.Flow.State + "; plain " + runner.Launch.Plain +
                       "; pointer locked " + runner.Human.PointerLocked + "; focused " + runner.Human.Frame.Focused);
            var names = new List<string>();
            if (runner.Presentation != null)
                foreach (IPresenter presenter in runner.Presentation.Presenters) names.Add(presenter.GetType().Name);
            Report.Add("presenters (" + names.Count + "): " + string.Join(", ", names));
            if (runner.Presentation == null) Problems.Add("the runner has no presentation");
            else if (names.Count == 0) Problems.Add("no presenter is active");
            Report.Add("physics while playing: mode " + Physics.simulationMode + ", gravity " + Physics.gravity.y.ToString("0.##", CultureInfo.InvariantCulture));

            frames = 0;
            loads = 0;
            completions = 0;
            shotsTaken = 0;
            longestFrame = 0f;
            wrapped = false;
            runner.SetAutoplay(true);
            startedAt = EditorApplication.timeSinceStartup;
            startTicks = game.TickCount;
            SessionState.SetString(StateKey, "running");
        }

        static void Watch()
        {
            if (runner == null || game == null || game.IsDisposed)
            {
                Problems.Add("the game went away while playing");
                Leave();
                return;
            }

            frames++;
            longestFrame = Mathf.Max(longestFrame, Time.unscaledDeltaTime);
            float elapsed = (float)(EditorApplication.timeSinceStartup - startedAt);

            if (shotsTaken < ShotTimes.Length && elapsed >= ShotTimes[shotsTaken])
            {
                Shoot("play-" + shotsTaken + ".png");
                Report.Add("  t=" + elapsed.ToString("0.0", CultureInfo.InvariantCulture) + " s: player at " + game.Player.Position +
                           " holding " + (game.Grabber.Held != null ? game.Grabber.Held.Name + " x" + game.Grabber.Held.Scale.ToString("0.00", CultureInfo.InvariantCulture) : "nothing") +
                           ", sim scene '" + game.Scene.name + "'");
                shotsTaken++;
            }

            // SetAutoplay reloaded the level once; the second load after a completion is the runner moving on.
            if (!wrapped && completions >= 1 && loads >= 2)
            {
                wrapped = true;
                Report.Add("autoplay completed the level after " + elapsed.ToString("0.0", CultureInfo.InvariantCulture) +
                           " s and the runner loaded level " + runner.LevelId + " (sim scene now '" + game.Scene.name + "', was '" + firstScene + "')");
                Shoot("play-next-level.png");
            }

            if (wrapped || elapsed > WatchSeconds)
            {
                int ticks = game.TickCount - startTicks;
                Report.Add("ran " + frames + " frames and " + ticks + " ticks in " + elapsed.ToString("0.0", CultureInfo.InvariantCulture) +
                           " s (" + (ticks / Mathf.Max(elapsed, 0.01f)).ToString("0", CultureInfo.InvariantCulture) + " ticks/s), longest frame " +
                           (longestFrame * 1000f).ToString("0", CultureInfo.InvariantCulture) + " ms; loads " + loads + ", completions " + completions +
                           "; autoplay error: " + (runner.AutoplayError ?? "none") + "; scenes loaded " + SceneManager.sceneCount +
                           (runner.Hud != null ? "; debug HUD drawn " + runner.Hud.DrawCount + " times" : "") +
                           "; presentation frames " + (runner.Presentation != null ? runner.Presentation.Context.FrameCount : 0));
                if (!wrapped) Problems.Add("autoplay did not complete the level within " + WatchSeconds + " s (player at " + game.Player.Position + ")");
                Leave();
            }
        }

        static void Shoot(string file)
        {
            if (runner.Rig == null || runner.Rig.Camera == null)
            {
                Problems.Add("no camera to take " + file + " with");
                return;
            }
            Shots.Shoot(runner.Rig.Camera, 1280, 720, Path.Combine(OutputPath, file));
        }

        static void Leave()
        {
            // Keep listening to the log: shutting the game down is part of what is being checked.
            SessionState.SetString(StateKey, "exiting");
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }

        static void Finish()
        {
            SessionState.SetString(StateKey, "");
            SessionState.EraseString(LaunchOptions.EditorOverrideKey);
            SessionState.EraseBool(PrefStores.EditorMemoryKey);
            EditorApplication.update -= Pump;
            Application.logMessageReceived -= OnLog;
            // Leaving Play Mode makes the engine stop calling log listeners until somebody subscribes again
            // (seen: the batch server no longer echoed "[Toybox]" lines after a play check, until the next
            // script reload). Subscribing tells it there are listeners; the server's own is still in the list.
            Application.logMessageReceived += IgnoreLog;
            Application.logMessageReceived -= IgnoreLog;

            modeBefore = (SimulationMode)SessionState.GetInt("Toybox.PlayCheck.Mode", (int)Physics.simulationMode);
            gravityBefore = SessionState.GetVector3("Toybox.PlayCheck.Gravity", Physics.gravity);
            Report.Add("left Play Mode; Game.Current " + (Game.Current == null ? "null" : "STILL ALIVE") + "; physics mode " + Physics.simulationMode +
                       " (before: " + modeBefore + "), gravity " + Physics.gravity.y.ToString("0.##", CultureInfo.InvariantCulture) +
                       " (before: " + gravityBefore.y.ToString("0.##", CultureInfo.InvariantCulture) + ")");
            if (Game.Current != null)
            {
                Problems.Add("the Game outlived Play Mode");
                Game.Current.Dispose();
            }
            if (Physics.simulationMode != modeBefore || Physics.gravity != gravityBefore)
            {
                Problems.Add("physics settings were not restored on leaving Play Mode");
                Physics.simulationMode = modeBefore;
                Physics.gravity = gravityBefore;
            }

            if (target != null)
            {
                target.Release();
                Object.DestroyImmediate(target);
                target = null;
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lines = new List<string> { Problems.Count == 0 ? "PLAYCHECK: OK" : "PLAYCHECK: PROBLEMS (" + Problems.Count + ")" };
            foreach (string problem in Problems) lines.Add("PROBLEM: " + problem);
            lines.AddRange(Report);
            Directory.CreateDirectory(OutputPath);
            File.WriteAllLines(ResultPath, lines);
            Report.Clear();
            Problems.Clear();
        }

        static void IgnoreLog(string condition, string stackTrace, LogType type) { }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Log || condition.StartsWith("[Toybox]")) return;
            if (type == LogType.Warning)
            {
                if (Report.Count < 60) Report.Add("  warning: " + condition);
                return;
            }
            if (Problems.Count < 30) Problems.Add(type + ": " + condition);
        }
    }
}
