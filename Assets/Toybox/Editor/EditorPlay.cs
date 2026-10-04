using Toybox.Engine;
using Toybox.Platform;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Toybox.EditorTools
{
    /// <summary>
    /// The game from inside the Unity editor. Pressing Play starts the game whichever scene is open, a
    /// freshly opened project shows the Main scene, and <b>Toybox &gt; Level Launcher</b> starts any level
    /// directly, with or without the bot playing it.
    ///
    /// Inactive in batch mode: the command-line tooling opens scenes and enters Play Mode on its own terms.
    /// </summary>
    [InitializeOnLoad]
    public static class EditorPlay
    {
        // This class put a query string under LaunchOptions.EditorOverrideKey and takes it away again.
        const string OwnsLaunchKey = "Toybox.EditorPlay.OwnsLaunch";
        // A launch asked for while Play Mode was still running: start it once the editor is back in Edit Mode.
        const string PendingKey = "Toybox.EditorPlay.Pending";
        const string GreetedKey = "Toybox.EditorPlay.Greeted";
        const string NoPending = "-";

        static EditorPlay()
        {
            if (Application.isBatchMode) return;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.delayCall += OnEditorReady;
        }

        static void OnEditorReady()
        {
            var main = AssetDatabase.LoadAssetAtPath<SceneAsset>(ProjectSetup.ScenePath);
            if (main == null) return;
            // Play always starts the game, also from an untitled scene or a scratch scene.
            EditorSceneManager.playModeStartScene = main;

            if (SessionState.GetBool(GreetedKey, false)) return;
            SessionState.SetBool(GreetedKey, true);
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            // A project opened for the first time comes up with an empty untitled scene: show the game's instead.
            var open = EditorSceneManager.GetActiveScene();
            if (EditorSceneManager.sceneCount == 1 && string.IsNullOrEmpty(open.path) && !open.isDirty)
                EditorSceneManager.OpenScene(ProjectSetup.ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("Toybox/Play Game", priority = 0)]
        public static void PlayFromTitle() => Launch("");

        /// <summary>Starts a level in Play Mode, as <c>?level=id</c> does in the browser.</summary>
        public static void PlayLevel(int id, bool autoplay) => Launch("?level=" + id + (autoplay ? "&autoplay=1" : ""));

        static void Launch(string query)
        {
            if (EditorApplication.isPlaying)
            {
                SessionState.SetString(PendingKey, query);
                EditorApplication.ExitPlaymode();
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            if (query.Length == 0) SessionState.EraseString(LaunchOptions.EditorOverrideKey);
            else SessionState.SetString(LaunchOptions.EditorOverrideKey, query);
            SessionState.SetBool(OwnsLaunchKey, query.Length > 0);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;

            // The next press of Play is an ordinary one again: the title screen, not the level asked for last.
            if (SessionState.GetBool(OwnsLaunchKey, false))
            {
                SessionState.EraseString(LaunchOptions.EditorOverrideKey);
                SessionState.SetBool(OwnsLaunchKey, false);
            }

            string pending = SessionState.GetString(PendingKey, NoPending);
            if (pending == NoPending) return;
            SessionState.SetString(PendingKey, NoPending);
            EditorApplication.delayCall += () => Launch(pending);
        }
    }

    /// <summary>Toybox &gt; Level Launcher: every registered level with a Play and a Watch Bot button.</summary>
    public sealed class LevelLauncherWindow : EditorWindow
    {
        Vector2 scroll;

        [MenuItem("Toybox/Level Launcher", priority = 1)]
        static void Open()
        {
            var window = GetWindow<LevelLauncherWindow>("Toybox Levels");
            window.minSize = new Vector2(360f, 240f);
        }

        void OnGUI()
        {
            EditorGUILayout.Space();
            if (GUILayout.Button("Play from the title screen", GUILayout.Height(28f))) EditorPlay.PlayFromTitle();
            EditorGUILayout.Space();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            Section("Campaign", true);
            Section("Extras (not part of the campaign)", false);
            EditorGUILayout.EndScrollView();
        }

        static void Section(string heading, bool campaign)
        {
            bool any = false;
            foreach (LevelEntry entry in LevelRegistry.All)
            {
                if ((entry.Phase > 0) != campaign) continue;
                if (!any)
                {
                    EditorGUILayout.LabelField(heading, EditorStyles.boldLabel);
                    any = true;
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    string phase = campaign ? "   phase " + entry.Phase : "";
                    EditorGUILayout.LabelField(new GUIContent(entry.Id.ToString("00") + "   " + entry.Title, entry.Slug + phase));
                    if (GUILayout.Button("Play", GUILayout.Width(60f))) EditorPlay.PlayLevel(entry.Id, false);
                    if (GUILayout.Button("Watch bot", GUILayout.Width(80f))) EditorPlay.PlayLevel(entry.Id, true);
                }
            }
            if (any) EditorGUILayout.Space();
        }
    }
}
