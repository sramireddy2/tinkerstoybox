using System;
using System.Collections.Generic;
using Toybox.Engine;
using Toybox.Platform;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Toybox.UI
{
    /// <summary>
    /// One canvas of the UI (ART_BIBLE 10.2): Scale With Screen Size against 1920 x 1080, matching height,
    /// so every size in the UI code is a reference pixel. It also is the clock of its stickers: the
    /// presenter that owns it calls <see cref="Advance"/> once per frame.
    ///
    /// In the game it is a screen-space overlay. For a capture (<see cref="UiCapture"/>) it becomes a
    /// world-space canvas that fills the game camera's view exactly, drawn by a camera stacked on top of
    /// the game's (<see cref="UiCapture.OverlayFor"/>) - so the screenshot tool, which renders world
    /// cameras into a texture, gets the UI into the same picture, and gets it the way the game shows it:
    /// on top of the finished frame, after the post-processing, not through it.
    /// </summary>
    public sealed class UiRoot : MonoBehaviour
    {
        readonly List<UiTween> tweens = new List<UiTween>();
        Camera fitCamera;

        public Canvas Canvas { get; private set; }
        public CanvasScaler Scaler { get; private set; }
        public RectTransform Rect { get; private set; }
        /// <summary>True while the canvas is drawn in the world for a capture.</summary>
        public bool InWorld { get; private set; }
        public int TweenCount => tweens.Count;

        public bool Visible
        {
            get => Canvas != null && Canvas.enabled;
            set
            {
                if (Canvas != null && Canvas.enabled != value) Canvas.enabled = value;
            }
        }

        public static UiRoot Create(PresentationContext context, string name, int sortingOrder, bool takesClicks)
        {
            var go = new GameObject(name, typeof(RectTransform)) { hideFlags = HideFlags.DontSave, layer = UiTheme.Layer };
            go.transform.SetParent(context.Root, false);
            UiRoot root = go.AddComponent<UiRoot>();
            root.Rect = (RectTransform)go.transform;
            root.Canvas = go.AddComponent<Canvas>();
            root.Canvas.sortingOrder = sortingOrder;
            // TextMeshPro needs these two for its outline and underlay; the rest is not used.
            root.Canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;

            if (UiCapture.Active && context.Camera != null)
            {
                root.InWorld = true;
                root.fitCamera = context.Camera;
                root.Canvas.renderMode = RenderMode.WorldSpace;
                root.Canvas.worldCamera = UiCapture.OverlayFor(context.Camera);
                root.Rect.sizeDelta = new Vector2(UiTheme.ReferenceWidth, UiTheme.ReferenceHeight);
                root.Fit(context.Camera);
            }
            else
            {
                root.Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                root.Scaler = go.AddComponent<CanvasScaler>();
                root.Scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                root.Scaler.referenceResolution = new Vector2(UiTheme.ReferenceWidth, UiTheme.ReferenceHeight);
                root.Scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                root.Scaler.matchWidthOrHeight = 1f;
                root.Scaler.referencePixelsPerUnit = 100f;
            }
            if (takesClicks) go.AddComponent<GraphicRaycaster>();
            return root;
        }

        internal void Register(UiTween tween)
        {
            if (tween != null) tweens.Add(tween);
        }

        /// <summary>Advances every sticker's motion by real seconds, and keeps a captured canvas on the camera.</summary>
        public void Advance(float dt)
        {
            for (int i = tweens.Count - 1; i >= 0; i--)
            {
                UiTween tween = tweens[i];
                if (tween == null)
                {
                    tweens.RemoveAt(i);
                    continue;
                }
                if (tween.Phase == TweenPhase.Hidden && !tween.IsMoving) continue;
                tween.Advance(dt);
            }
            if (InWorld && fitCamera != null) Fit(fitCamera);
        }

        /// <summary>
        /// Capture only: puts the canvas right behind the camera's near plane, sized so that its 1080
        /// reference pixels fill the view's height and its width the width of the picture (<see cref="UiCapture.Aspect"/>).
        /// </summary>
        public void Fit(Camera camera)
        {
            if (!InWorld || camera == null) return;
            // The shape of the picture that will be taken, not of whatever the camera renders to right now.
            float aspect = UiCapture.Aspect;
            float distance = camera.nearClipPlane * 1.5f + (Canvas.sortingOrder > 15 ? 0f : camera.nearClipPlane * 0.1f);
            float height = 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float scale = height / UiTheme.ReferenceHeight;
            var size = new Vector2(UiTheme.ReferenceHeight * aspect, UiTheme.ReferenceHeight);
            Transform view = camera.transform;
            Vector3 position = view.position + view.forward * distance;
            if (Rect.sizeDelta != size) Rect.sizeDelta = size;
            Rect.SetPositionAndRotation(position, view.rotation);
            Rect.localScale = new Vector3(scale, scale, scale);
            UiCapture.Follow(camera);
        }

        /// <summary>Nothing to undo any more; kept so that presenters can let go of a root without caring how it was shown.</summary>
        public void Unhook() { }
    }

    /// <summary>
    /// Asks the UI to draw itself into the game camera's picture instead of onto the screen, and to put up
    /// a particular screen - for looking at the UI through the screenshot tool, which has no game flow:
    ///
    ///   tools\unity.ps1 exec -Method Toybox.EditorTools.Shots.Capture -UnityArgs '-toyboxLevel','0','-toyboxUi','pause'
    ///
    /// Screens: hud (the default), toast (the HUD with two sample hints), title, pause, hints, settings, select, catalogue (the level select with
    /// the fifteen levels of the campaign plan and some progress, whatever is registered), complete.
    /// Outside Play Mode only.
    /// </summary>
    public static class UiCapture
    {
        const string ArgsKey = "Toybox.Server.Args";
        const string UiArgument = "-toyboxUi", SizeArgument = "-toyboxSize";

        static string request;
        static bool set;
        static float aspect;

        /// <summary>
        /// Width over height of the picture being taken: what -toyboxSize says (the screenshot tool's own
        /// argument; 1280x720 if it is not given), unless it was set by hand.
        /// </summary>
        public static float Aspect
        {
            get
            {
                if (aspect > 0f) return aspect;
                string size = Value(SizeArgument);
                if (size != null)
                {
                    string[] parts = size.Split('x', 'X', '*', ',');
                    if (parts.Length == 2 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float w) &&
                        float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float h) && w > 0f && h > 0f)
                        return w / h;
                }
                return UiTheme.ReferenceWidth / UiTheme.ReferenceHeight;
            }
            set => aspect = value;
        }

        /// <summary>
        /// Height in pixels of the picture being taken (what -toyboxSize says, 720 if it is not given): a
        /// camera picture shown on a card is made this tall, the camera having no screen to measure.
        /// </summary>
        public static int PictureHeight
        {
            get
            {
                string size = Value(SizeArgument);
                if (size != null)
                {
                    string[] parts = size.Split('x', 'X', '*', ',');
                    if (parts.Length == 2 && int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int h) && h > 0)
                        return Mathf.Clamp(h, 16, 4096);
                }
                return 720;
            }
        }

        /// <summary>The screen asked for, or null when nothing is being captured. Setting it overrides the command line.</summary>
        public static string Request
        {
            get => set ? request : FromArguments();
            set
            {
                request = string.IsNullOrEmpty(value) ? null : value.Trim().ToLowerInvariant();
                set = value != null;
            }
        }

        /// <summary>Back to what the command line says (tests call this when they are done).</summary>
        public static void Reset()
        {
            request = null;
            set = false;
            aspect = 0f;
        }

        public static bool Active => !Application.isPlaying && Request != null;

        static Camera overlay;
        static bool emitting;

        // The engine hands a camera its canvases' geometry when it is one of the cameras being rendered. A
        // tool renders the game camera alone (Camera.Render), and the cameras stacked on it come along
        // inside the pipeline, unseen by the engine - so the overlay camera asks for its geometry itself.
        static void EmitCanvases(ScriptableRenderContext context, Camera camera)
        {
            if (overlay == null)
            {
                RenderPipelineManager.beginCameraRendering -= EmitCanvases;
                emitting = false;
                return;
            }
            if (camera == overlay) ScriptableRenderContext.EmitGeometryForCamera(camera);
        }

        /// <summary>The camera that draws the captured canvases of this game camera, or null if there is none.</summary>
        public static Camera Overlay => overlay;

        /// <summary>
        /// Capture only: the camera that draws the UI layer on top of what the game's camera has finished,
        /// post-processing included - an overlay camera in the game camera's stack, with its view. The
        /// game's camera stops drawing the UI layer itself. Made on first use; it lives and dies with the
        /// camera it is a child of.
        /// </summary>
        public static Camera OverlayFor(Camera game)
        {
            if (game == null) return null;
            if (overlay != null && overlay.transform.parent == game.transform) return overlay;

            var go = new GameObject("UI Capture Camera") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(game.transform, false);
            overlay = go.AddComponent<Camera>();
            overlay.clearFlags = CameraClearFlags.Nothing;
            overlay.cullingMask = 1 << UiTheme.Layer;
            overlay.useOcclusionCulling = false;
            UniversalAdditionalCameraData data = overlay.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Overlay;
            data.renderPostProcessing = false;
            data.renderShadows = false;

            game.cullingMask &= ~(1 << UiTheme.Layer);
            UniversalAdditionalCameraData gameData = game.GetUniversalAdditionalCameraData();
            if (!gameData.cameraStack.Contains(overlay)) gameData.cameraStack.Add(overlay);
            if (!emitting)
            {
                RenderPipelineManager.beginCameraRendering += EmitCanvases;
                emitting = true;
            }
            Follow(game);
            return overlay;
        }

        /// <summary>Keeps the overlay camera's view the game camera's (its scene, lens and clip planes).</summary>
        public static void Follow(Camera game)
        {
            if (overlay == null || game == null || overlay.transform.parent != game.transform) return;
            // Outside Play Mode the simulation is in a preview scene, which only a camera bound to it renders.
            if (overlay.scene != game.scene) overlay.scene = game.scene;
            overlay.fieldOfView = game.fieldOfView;
            overlay.nearClipPlane = game.nearClipPlane;
            overlay.farClipPlane = game.farClipPlane;
        }

        public static bool Wants(string screen) => Active && Request == screen;

        /// <summary>The flow state the UI shows: the real one, or the one a capture request stands for.</summary>
        public static FlowState StateFor(PresentationContext context, string captureRequest)
        {
            switch (captureRequest)
            {
                case "title": return FlowState.Title;
                case "pause":
                case "hints":
                case "settings": return FlowState.Paused;
                case "select":
                case "catalogue": return FlowState.LevelSelect;
                case "complete": return FlowState.LevelComplete;
                default: return context.State;
            }
        }

        // The campaign as LEVELS.md plans it: what the catalogue will hold once the levels exist.
        static readonly string[] DemoSlugs =
        {
            "cheese-wedge", "thimble-chasm", "shrinking-apple", "domino-effect", "fan-feather", "bouncing-eraser", "teeter-totter", "funnel-physics",
            "moving-train", "matryoshka-boxes", "blocking-lasers", "the-keyhole", "fishbowl", "infinite-hallway", "rube-goldberg",
        };

        static readonly string[] DemoTitles =
        {
            "The Cheese Wedge", "The Thimble Chasm", "Shrinking the Apple", "Domino Effect", "The Fan and the Feather", "Bouncing Eraser",
            "The Teeter-Totter", "Funnel Physics", "The Moving Train", "Matryoshka Boxes", "Blocking Lasers", "The Keyhole", "Escaping the Fishbowl",
            "The Infinite Hallway", "The Rube Goldberg Machine",
        };

        sealed class DemoLevel : LevelDefinition
        {
            readonly int number;

            public DemoLevel(int number) => this.number = number;

            public override string Slug => DemoSlugs[number - 1];
            public override string Title => DemoTitles[number - 1];
            public override string Environment => EnvironmentPreset.KeyForLevel(number, true);

            public override void Build(LevelContext ctx) { }
        }

        /// <summary>Fifteen stand-in levels with the planned titles, for looking at the catalogue before the campaign exists.</summary>
        public static LevelList DemoLevels()
        {
            var ids = new int[DemoSlugs.Length];
            for (int i = 0; i < ids.Length; i++) ids[i] = i + 1;
            return new LevelList(ids, id => new DemoLevel(id));
        }

        /// <summary>Progress to go with them: the first six collected.</summary>
        public static Progress DemoProgress()
        {
            var progress = new Progress(new MemoryStore());
            for (int id = 1; id <= 6; id++) progress.RecordCompletion(id, 31f + id * 17.3f);
            return progress;
        }

        static string FromArguments()
        {
            string value = Value(UiArgument);
            if (value == null) return null;
            return value.Length == 0 || value.StartsWith("-") ? "hud" : value.Trim().ToLowerInvariant();
        }

        // The value after a named argument ("" if it is the last one), or null if the argument is not there.
        static string Value(string name)
        {
#if UNITY_EDITOR
            try
            {
                string served = UnityEditor.SessionState.GetString(ArgsKey, "");
                string value = Find(served.Split((char)10), name);
                return value ?? Find(Environment.GetCommandLineArgs(), name);
            }
            catch (Exception)
            {
                return null;
            }
#else
            return null;
#endif
        }

        static string Find(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
                if (args[i] == name) return i + 1 < args.Length ? args[i + 1] ?? "" : "";
            return null;
        }
    }
}
