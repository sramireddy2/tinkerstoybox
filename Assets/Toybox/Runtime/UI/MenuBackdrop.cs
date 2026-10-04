using Toybox.Platform;
using Toybox.Render;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Toybox.UI
{
    public enum BackdropMode
    {
        /// <summary>Nothing: the live scene shows (title, play).</summary>
        None,
        /// <summary>
        /// The pause: the frame as it was on a card at 86% tilted -2 degrees, and behind it the same frame
        /// under the lens blur's full-screen 14 px and Paper at 35%. The world camera goes off as soon as
        /// that blur has arrived (180 ms; at once where there is no lens blur).
        /// </summary>
        Frozen,
        /// <summary>A plain surface (the catalogue). The world camera is off.</summary>
        Surface,
        /// <summary>Level complete: the camera renders into a texture shown on a card that shrinks to 86% and tilts 2 degrees; the scene stays live in it.</summary>
        Live,
    }

    /// <summary>
    /// What lies behind a menu (ART_BIBLE 9.7, 10.5 "Pause"). It owns the one render texture the menus
    /// use and decides whether the world camera runs: while the game is paused or the catalogue is open
    /// the camera is disabled and the GPU draws the UI only.
    ///
    /// URP draws overlay canvases as the last step of the camera that renders to the screen. While the
    /// game's camera does not - it is disabled, or its output goes to the card's texture - nobody would
    /// draw the menus. For that time a second camera stands in, the <see cref="ScreenCamera"/>: it sees
    /// nothing (culling mask 0, no shadows, no post-processing), clears the screen and carries the
    /// overlay, exactly the way the game's camera carries the HUD while playing. It only exists while it
    /// is needed.
    /// </summary>
    public sealed class MenuBackdrop
    {
        public const float CardScale = 0.86f, PauseTilt = -2f, CompleteTilt = 2f, WashAlpha = 0.35f;
        /// <summary>Longest the pause waits for the full-screen blur before it freezes the background anyway.</summary>
        public const float BlurWaitLimit = 0.4f;
        const float PictureInset = 22f;

        readonly PresentationContext context;
        readonly UiRoot root;
        readonly RectTransform holder, card;
        readonly RawImage full, picture;
        readonly Image wash;
        readonly Sticker cardSticker;
        Material frameMaterial;
        RenderTexture target;
        RenderTexture blurred;
        RenderTexture previousTarget;
        Camera screenCamera;
        bool cameraOff, redirected, blurPending;
        float cardTime, blurWait;
        float tilt;

        public BackdropMode Mode { get; private set; }
        public RenderTexture Target => target;
        /// <summary>The picture card (pause, level complete).</summary>
        public Sticker Card => cardSticker;
        /// <summary>True while the backdrop keeps the world camera from rendering to the screen.</summary>
        public bool CameraOff => cameraOff;
        public bool CameraRedirected => redirected;
        /// <summary>True while a pause still shows the live scene behind the card, waiting for the blur to arrive.</summary>
        public bool BlurPending => blurPending;
        /// <summary>The camera that carries the menus while the game's camera does not render to the screen, else null.</summary>
        public Camera ScreenCamera => screenCamera;
        /// <summary>Scale of the picture card now (1 falling to 0.86 over 240 ms on level complete).</summary>
        public float CardSize => card.localScale.x;
        public float CardTilt => card.localEulerAngles.z > 180f ? card.localEulerAngles.z - 360f : card.localEulerAngles.z;

        public MenuBackdrop(PresentationContext presentationContext, UiRoot uiRoot)
        {
            context = presentationContext;
            root = uiRoot;
            holder = UiKit.Rect(root.Rect, "Backdrop");
            holder.Fill();

            full = Raw(holder, "Frame");
            full.rectTransform.Fill();
            wash = UiKit.Image(holder, "Wash", UiShape.White, UiTheme.Alpha(UiTheme.Paper, WashAlpha), raycast: true);
            wash.rectTransform.Fill();

            // The card is the screen's size and scaled down, so the picture in it keeps the screen's shape.
            card = UiKit.Rect(holder, "Card");
            card.Fill();
            cardSticker = Sticker.Create(card, "Picture Card", StickerShape.Card, UiTheme.Paper, Vector2.zero);
            cardSticker.Rect.Fill();
            cardSticker.Radius = 26f;
            picture = Raw(cardSticker.Content, "Picture");
            picture.rectTransform.Fill(PictureInset);
            picture.rectTransform.offsetMax = new Vector2(-PictureInset, -PictureInset - 24f);
            cardSticker.Slot.rectTransform.SetAsLastSibling();

            Material template = Resources.Load<Material>(FrameMaterialPath);
            if (template != null)
            {
                // In the transparent queue like the rest of the UI: a world-space canvas (a capture) is sorted by
                // queue first, and an opaque picture would end up underneath the card it lies on.
                frameMaterial = new Material(template) { name = "Menu Frame", hideFlags = HideFlags.HideAndDontSave, renderQueue = 3000 };
            }
            holder.gameObject.SetActive(false);
        }

        /// <summary>Resources path of the material that shows a camera picture without its alpha (made by UiSetup).</summary>
        public const string FrameMaterialPath = "UI/Frame";

        static RawImage Raw(Transform parent, string name)
        {
            RectTransform rect = UiKit.Rect(parent, name);
            RawImage image = rect.gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            image.color = Color.white;
            return image;
        }

        /// <summary>Switches what is behind the menus. <paramref name="surface"/> is the colour of a plain or live backdrop.</summary>
        public void Set(BackdropMode mode, Color surface)
        {
            if (mode == Mode)
            {
                if (mode == BackdropMode.Surface || mode == BackdropMode.Live) wash.color = surface;
                return;
            }
            Restore(releaseTarget: mode == BackdropMode.None || mode == BackdropMode.Surface);
            Mode = mode;
            holder.gameObject.SetActive(mode != BackdropMode.None);
            if (mode == BackdropMode.None) return;

            Camera camera = context.Camera;
            bool graphics = context.HasGraphics && camera != null;
            cardTime = 0f;
            switch (mode)
            {
                case BackdropMode.Frozen:
                    // The card gets the frame as it was. The background is that frame blurred: the camera
                    // keeps running under the wash while the post-processing ramps its full-screen blur
                    // (10.5 step 1), then one more picture is taken and the camera goes off (step 2).
                    if (graphics) Snapshot(camera);
                    PostLook post = Post;
                    blurPending = graphics && Application.isPlaying && !root.InWorld && post != null && !post.FullBlurReached;
                    blurWait = 0f;
                    full.texture = target;
                    Show(full, graphics && !blurPending);
                    wash.color = UiTheme.Alpha(UiTheme.Paper, graphics ? WashAlpha : 1f);
                    card.gameObject.SetActive(graphics);
                    tilt = Settings.ReduceMotion ? 0f : PauseTilt;
                    Pose(1f);
                    if (!blurPending) SetCameraOff(true);
                    break;
                case BackdropMode.Surface:
                    Show(full, false);
                    wash.color = surface;
                    card.gameObject.SetActive(false);
                    SetCameraOff(true);
                    break;
                case BackdropMode.Live:
                    Show(full, false);
                    wash.color = surface;
                    card.gameObject.SetActive(graphics);
                    tilt = Settings.ReduceMotion ? 0f : CompleteTilt;
                    Pose(0f);
                    if (graphics) Redirect(camera);
                    break;
            }
        }

        PostLook Post => context.Presentation != null ? context.Presentation.Get<PostLook>() : null;

        public void Frame(float dt)
        {
            if (blurPending && Mode == BackdropMode.Frozen)
            {
                blurWait += dt;
                PostLook post = Post;
                if (post == null || post.FullBlurReached || blurWait >= BlurWaitLimit) FreezeBlurred();
            }

            // The screenshot tool renders the camera itself, into its own texture, right after a frame
            // without time: that is the moment to take the picture the cards show.
            if (root.InWorld && dt <= 0f && (Mode == BackdropMode.Frozen || Mode == BackdropMode.Live) && context.HasGraphics && context.Camera != null)
                Snapshot(context.Camera);
            if (Mode != BackdropMode.Live || cardTime >= CardTime) return;
            cardTime = Mathf.Min(CardTime, cardTime + dt);
            Pose(cardTime / CardTime);
        }

        public const float CardTime = 0.24f;

        // 0: the card is the whole screen; 1: 86% and tilted.
        void Pose(float t)
        {
            float k = UiTheme.EaseOut(t);
            float scale = Mathf.Lerp(1f, CardScale, k);
            card.localScale = new Vector3(scale, scale, 1f);
            card.localRotation = Quaternion.Euler(0f, 0f, tilt * k);
        }

        static void Show(RawImage image, bool on)
        {
            if (image.gameObject.activeSelf != on) image.gameObject.SetActive(on);
        }

        void EnsureTarget(Camera camera)
        {
            int width = Mathf.Clamp(camera.pixelWidth, 16, 4096), height = Mathf.Clamp(camera.pixelHeight, 16, 4096);
            if (root.InWorld)
            {
                // A capture: the camera has no screen; the card's picture is as large as the one being taken.
                height = UiCapture.PictureHeight;
                width = Mathf.Clamp(Mathf.RoundToInt(height * UiCapture.Aspect), 16, 4096);
            }
            int samples = Samples(camera);
            if (target != null && target.width == width && target.height == height && target.antiAliasing == samples) return;
            ReleaseTarget();
            target = NewTexture("Menu Frame", width, height, samples);
            full.texture = target;
            picture.texture = target;
            // The game's colour format has no alpha channel, so the UI's own shader shows the picture as it
            // is. Only the 8-bit fallback has one - and a camera leaves it at zero in places, which that
            // shader would read as see-through: there the picture is drawn by the opaque frame material.
            Material material = GraphicsFormatUtility.HasAlphaChannel(target.graphicsFormat) ? frameMaterial : null;
            full.material = material;
            picture.material = material;
        }

        // With post-processing the pipeline resolves its own multisampled target into the texture; without,
        // it draws straight into it, and the picture is only as smooth as the texture is.
        int Samples(Camera camera)
        {
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            bool direct = data == null || !data.renderPostProcessing;
            return direct && pipeline != null ? Mathf.Clamp(pipeline.msaaSampleCount, 1, 8) : 1;
        }

        // In the game's own colour format (ART_BIBLE 7.3): URP takes the format of its intermediate colour
        // target from the camera's target texture, so a camera pointed at an 8-bit texture renders without
        // HDR - no bloom, no glints - for as long as the card shows.
        static RenderTexture NewTexture(string name, int width, int height, int samples)
        {
            const GraphicsFormat hdr = GraphicsFormat.B10G11R11_UFloatPack32;
            RenderTexture texture = SystemInfo.IsFormatSupported(hdr, GraphicsFormatUsage.Render)
                ? new RenderTexture(new RenderTextureDescriptor(width, height, hdr, 24) { msaaSamples = samples })
                : new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = samples };
            texture.name = name;
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.Create();
            return texture;
        }

        // The blur has arrived (or took too long): the background becomes a picture of the blurred scene
        // and the camera stops.
        void FreezeBlurred()
        {
            blurPending = false;
            Camera camera = context.Camera;
            if (camera != null && target != null)
            {
                if (blurred == null || blurred.width != target.width || blurred.height != target.height)
                {
                    ReleaseBlurred();
                    blurred = NewTexture("Menu Frame Blurred", target.width, target.height, 1);
                }
                Render(camera, blurred);
                full.texture = blurred;
            }
            Show(full, true);
            SetCameraOff(true);
        }

        // One frame of the game camera into the texture, without the menus in it.
        void Snapshot(Camera camera)
        {
            EnsureTarget(camera);
            // Outside Play Mode nobody looks at the screen unless a capture is on: an EditMode test of the
            // pause gets its texture but no render (which would also be the pipeline's first, and leave its
            // pooled targets behind for whoever counts textures).
            if (!Application.isPlaying && !root.InWorld) return;
            Render(camera, target);
        }

        void Render(Camera camera, RenderTexture into)
        {
            RenderTexture before = camera.targetTexture;
            bool wasEnabled = root.Canvas.enabled;
            // In a capture the canvases are drawn by a camera stacked on the game camera: it sits this one out.
            Camera overlay = root.InWorld ? UiCapture.Overlay : null;
            bool overlayWas = overlay != null && overlay.enabled;
            try
            {
                root.Canvas.enabled = false;
                if (overlay != null) overlay.enabled = false;
                camera.targetTexture = into;
                camera.Render();
            }
            finally
            {
                camera.targetTexture = before;
                if (overlay != null) overlay.enabled = overlayWas;
                root.Canvas.enabled = wasEnabled;
            }
        }

        void Redirect(Camera camera)
        {
            EnsureTarget(camera);
            if (root.InWorld)
            {
                // A capture: the tool owns the camera's target. Frame() takes a picture instead.
                Snapshot(camera);
                return;
            }
            previousTarget = camera.targetTexture;
            camera.targetTexture = target;
            redirected = true;
            SetScreenCamera(true);
        }

        void SetCameraOff(bool off)
        {
            Camera camera = context.Camera;
            // A capture needs the camera: the canvas is drawn by it.
            if (camera == null || root.InWorld) return;
            if (off == cameraOff) return;
            cameraOff = off;
            camera.enabled = !off;
            SetScreenCamera(off || redirected);
        }

        // Something has to render to the screen for the pipeline to draw the overlay canvases with.
        void SetScreenCamera(bool wanted)
        {
            if (wanted == (screenCamera != null)) return;
            if (!wanted)
            {
                UiKit.Destroy(screenCamera.gameObject);
                screenCamera = null;
                return;
            }
            Camera game = context.Camera;
            var holderObject = new GameObject("Menu Screen Camera") { hideFlags = HideFlags.DontSave };
            holderObject.transform.SetParent(context.Root, false);
            screenCamera = holderObject.AddComponent<Camera>();
            screenCamera.clearFlags = CameraClearFlags.SolidColor;
            screenCamera.backgroundColor = UiTheme.Paper;
            screenCamera.cullingMask = 0;
            screenCamera.depth = (game != null ? game.depth : 0f) + 10f;
            screenCamera.allowHDR = false;
            screenCamera.allowMSAA = false;
            screenCamera.useOcclusionCulling = false;
            UniversalAdditionalCameraData data = screenCamera.GetUniversalAdditionalCameraData();
            data.renderShadows = false;
            data.renderPostProcessing = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
        }

        void Restore(bool releaseTarget)
        {
            Camera camera = context.Camera;
            if (redirected)
            {
                redirected = false;
                if (camera != null && camera.targetTexture == target) camera.targetTexture = previousTarget;
                previousTarget = null;
            }
            blurPending = false;
            SetCameraOff(false);
            SetScreenCamera(false);
            ReleaseBlurred();
            if (releaseTarget) ReleaseTarget();
        }

        void ReleaseBlurred()
        {
            if (blurred == null) return;
            if (full.texture == blurred) full.texture = target;
            blurred.Release();
            UiKit.Destroy(blurred);
            blurred = null;
        }

        void ReleaseTarget()
        {
            if (target == null) return;
            full.texture = null;
            picture.texture = null;
            target.Release();
            UiKit.Destroy(target);
            target = null;
        }

        public void Dispose()
        {
            Restore(true);
            Mode = BackdropMode.None;
            UiKit.Destroy(frameMaterial);
            frameMaterial = null;
        }
    }
}
