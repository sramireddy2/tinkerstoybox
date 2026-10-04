using System;
using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Gadgets;
using Toybox.Platform;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Toybox.Render
{
    /// <summary>
    /// The view through a <see cref="PortalDoorway"/> (LEVELS 2.3, Level 14): what the player will see
    /// once they have stepped through and are the size of the door.
    ///
    /// Medium and High: one extra camera stands at <c>ViewEye(eye)</c> - the eye, scaled about the
    /// threshold by the ratio of the sizes - and looks through the door with an off-axis frustum whose
    /// near plane is the door's own plane and whose window is the part of the opening that is on screen.
    /// Its picture (HDR, no post-processing: the game's camera does that once, to everything) is shown
    /// on a card in the opening, inside a thin rim of the card's own warm light (and, in the day rooms, a
    /// hairline of the room's deep tone where that light ends) - so an open portal is told from a plain
    /// doorway even where the far side is a bare floor. Because the window is cut to what is visible, the texture's pixels go
    /// where the screen's are: a shin-high door across the hall and a lintel that swallows the screen
    /// are equally sharp. The door's own frame is left out of that picture (it would stand inside
    /// itself) but still casts its shadow in it. One door at a time: the nearest that is in view.
    ///
    /// Low, a device without the HDR format, a door farther off than 25 of its own sizes, every door but
    /// the nearest: no second camera. The opening shows a flat, softly lit card with the silhouette of
    /// a figure as tall as the player will be.
    ///
    /// In the hand a door is no portal, and what its opening would show - the room behind it, in true
    /// perspective inside a frame of constant size on the screen - is exactly the depth cue the held-object
    /// rule forbids. So while it is held the opening is closed with an opaque card of the same warm light,
    /// on the Held layer: part of the sticker, on every tier, no camera (LEVELS, Level 14).
    ///
    /// Nothing is drawn while the door is settling or cooling down (it is not a portal then), nor for a
    /// settled door of the player's own size (it is just a door).
    /// </summary>
    [Presenter(240, ProvidesLook = true)]
    public sealed class PortalView : GadgetVisual
    {
        /// <summary>The live view reaches this many door scales from the threshold; the card takes over across the last fifth.</summary>
        public const float RangeInDoorScales = 25f, FadeShare = 0.8f;
        /// <summary>A door whose size is within this of the player's own is just a door.</summary>
        public const float SameSize = 0.03f;
        /// <summary>The window is cut for a screen at least this wide for its height, whatever the camera says right now.</summary>
        public const float SafeAspect = 2.4f;
        /// <summary>The name of the shader the picture is shown with: unlit, textured, always in a build.</summary>
        public const string ViewShader = "Sprites/Default";

        /// <summary>The rim of light round a live view: this share of the opening's width.</summary>
        public const float RimShare = 0.085f;
        /// <summary>
        /// Where the rim's light ends, a hairline of the room's deep tone, this share of the rim wide: on a
        /// sunlit floor the warm rim is white on white, and the line is what still says "a picture hangs here".
        /// The night room has none - there the rim glows on its own.
        /// </summary>
        public const float RimLineShare = 0.22f, RimLineAlpha = 0.55f;

        /// <summary>For tests and tools: false keeps every door on its card, whatever the tier.</summary>
        public static bool AllowCamera = true;

        sealed class Door
        {
            public PortalDoorway Portal;
            public Renderer[] Frame;
            public ShadowCastingMode[] FrameModes;
            public Transform Card, Figure;
            public MeshRenderer CardRenderer, FigureRenderer, RimRenderer, HeldRenderer;
            public MaterialPropertyBlock Block;
            public float CardShown;
            public bool Open, Live, Tried, InHand;
        }

        readonly List<Door> doors = new List<Door>();
        readonly Vector3[] clipA = new Vector3[16], clipB = new Vector3[16];
        readonly List<Vector3> viewVertices = new List<Vector3>(4);
        readonly List<Vector2> viewUvs = new List<Vector2>(4);
        Camera portalCamera;
        RenderTexture texture;
        Material viewMaterial;
        Mesh viewMesh;
        MeshRenderer viewRenderer;
        bool cameraBroken, warmed;
        Color glow, ink, rimLine;

        public int Count => doors.Count;
        /// <summary>The second camera, once a view has been rendered.</summary>
        public Camera Camera => portalCamera;
        public RenderTexture Texture => texture;
        public Renderer ViewRenderer => viewRenderer;
        /// <summary>Pixels of the texture the last view was rendered into.</summary>
        public Vector2Int ViewPixels { get; private set; }
        /// <summary>Views rendered since the presenter was attached.</summary>
        public int Renders { get; private set; }
        /// <summary>True if this tier and this device get the second camera at all.</summary>
        public bool CameraAvailable =>
            AllowCamera && !cameraBroken && Context != null && Context.HasGraphics && Tier != QualityTier.Low && Quality.HdrRenderable && Shader.Find(ViewShader) != null;

        /// <summary>True while the door's opening shows the live view.</summary>
        public bool IsLive(PortalDoorway portal) => Find(portal)?.Live ?? false;
        /// <summary>True while the door is a portal that leads to another size.</summary>
        public bool IsOpen(PortalDoorway portal) => Find(portal)?.Open ?? false;
        /// <summary>How opaque the door's card is, 0 (hidden) .. 1.</summary>
        public float CardOf(PortalDoorway portal) => Find(portal)?.CardShown ?? 0f;
        public Renderer CardRendererOf(PortalDoorway portal) => Find(portal)?.CardRenderer;
        public Renderer FigureRendererOf(PortalDoorway portal) => Find(portal)?.FigureRenderer;
        public Renderer RimRendererOf(PortalDoorway portal) => Find(portal)?.RimRenderer;
        /// <summary>True while the door is carried: its opening is closed with the opaque card.</summary>
        public bool IsInHand(PortalDoorway portal) => Find(portal)?.InHand ?? false;
        public Renderer HeldCardRendererOf(PortalDoorway portal) => Find(portal)?.HeldRenderer;

        /// <summary>The picture's size on a tier: (width, height) of the texture the view is rendered into.</summary>
        public static Vector2Int TextureSize(QualityTier tier) => tier == QualityTier.High ? new Vector2Int(1920, 1080) : new Vector2Int(1280, 720);

        protected override void Begin()
        {
            Dip dip = Materials.Dip;
            glow = GadgetFx.Lin(Palette.WarmWhite, dip.Night ? 1.7f : 1.12f);
            ink = GadgetFx.Lin(Palette.Ink, 1f, 0.88f);
            rimLine = GadgetFx.Lin(Palette.Mix(dip.Deep, Palette.Ink, 0.35f), 1f, dip.Night ? 0f : RimLineAlpha);
            // The texture is as big as its tier says; a tier change brings a new one.
            Vector2Int size = TextureSize(Tier);
            if (texture != null && (texture.width != size.x || texture.height != size.y)) DropTexture();
        }

        protected override void Adopt(Gadget gadget)
        {
            if (!(gadget is PortalDoorway portal) || portal.Prop == null) return;
            var door = new Door { Portal = portal, Block = new MaterialPropertyBlock() };
            door.Frame = portal.Prop.GameObject.GetComponentsInChildren<Renderer>(true);
            door.FrameModes = new ShadowCastingMode[door.Frame.Length];

            var card = new GameObject("Portal Card " + portal.Name) { hideFlags = HideFlags.DontSave };
            card.transform.SetParent(Root, false);
            Keep(card);
            door.Card = card.transform;
            Mesh cardMesh = Keep(CardMesh());
            door.CardRenderer = Visual("Card", cardMesh, GadgetFx.Flat("Portal Card", FlatBlend.Alpha, FlatShape.Quad, 0f, 0), card.transform);
            // The same card, opaque and on the layer of the toy in the hand: drawn by the sticker pass with the door.
            door.HeldRenderer = Visual("Held Card", cardMesh, GadgetFx.Flat("Portal Held Card", FlatBlend.Opaque), card.transform);
            door.HeldRenderer.gameObject.layer = Layers.Held;
            door.HeldRenderer.enabled = false;
            door.FigureRenderer = Visual("Figure", FigureMesh, GadgetFx.Flat("Portal Figure", FlatBlend.Alpha, FlatShape.Quad, 0f, 1), card.transform);
            door.Figure = door.FigureRenderer.transform;
            Vector2 opening = portal.OpeningSize;
            door.RimRenderer = Visual("Rim", Keep(RimMesh(opening.x / Mathf.Max(1e-4f, opening.y))), GadgetFx.Flat("Portal Rim", FlatBlend.Alpha, FlatShape.Quad, 0f, 3), card.transform);
            card.SetActive(false);
            doors.Add(door);
            if (!warmed) Warm();
        }

        // The first render of a new camera makes the pipeline set itself up for it (targets, passes, shader
        // programs): a hitch. It is taken here, while the level with the first portal is being loaded,
        // instead of on the frame the player first looks at a door.
        void Warm()
        {
            Camera main = Context.Camera;
            if (main == null || !CameraAvailable) return;
            warmed = true;
            try
            {
                EnsureCamera(main);
                EnsureTexture();
                Transform eye = main.transform;
                portalCamera.transform.SetPositionAndRotation(eye.position, eye.rotation);
                portalCamera.ResetProjectionMatrix();
                portalCamera.fieldOfView = main.fieldOfView;
                portalCamera.nearClipPlane = main.nearClipPlane;
                portalCamera.farClipPlane = main.farClipPlane;
                portalCamera.clearFlags = main.clearFlags;
                portalCamera.backgroundColor = main.backgroundColor;
                portalCamera.cullingMask = main.cullingMask & ~Layers.HeldMask & ~(1 << 5);
                portalCamera.rect = new Rect(0f, 0f, 64f / texture.width, 64f / texture.height);
                if (!Application.isPlaying) portalCamera.scene = Game.Scene;
                portalCamera.Render();
                viewMaterial.SetPass(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                cameraBroken = true;
            }
        }

        // The card: brighter at the foot than at the lintel, like light coming in under a door.
        Mesh CardMesh()
        {
            var fx = new FxMesh("Portal Card");
            Color foot = glow, head = new Color(glow.r * 0.72f, glow.g * 0.72f, glow.b * 0.72f, 1f);
            fx.Quad(new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), foot, foot, head, head);
            fx.Apply();
            return fx.Mesh;
        }

        // The rim: a frame just inside the unit card, as thick everywhere as RimShare of the opening's width,
        // bright at the frame and gone toward the middle.
        Mesh RimMesh(float aspect)
        {
            var fx = new FxMesh("Portal Rim");
            float bx = RimShare, by = RimShare * aspect;
            Color edge = new Color(glow.r * 1.3f, glow.g * 1.3f, glow.b * 1.3f, 0.9f), clear = edge;
            clear.a = 0f;
            var a = new Vector3(-0.5f, -0.5f, 0f);
            var b = new Vector3(0.5f, -0.5f, 0f);
            var c = new Vector3(0.5f, 0.5f, 0f);
            var d = new Vector3(-0.5f, 0.5f, 0f);
            Vector3 ia = a + new Vector3(bx, by, 0f), ib = b + new Vector3(-bx, by, 0f), ic = c + new Vector3(-bx, -by, 0f), id = d + new Vector3(bx, -by, 0f);
            fx.Quad(a, b, ib, ia, edge, edge, clear, clear);
            fx.Quad(b, c, ic, ib, edge, edge, clear, clear);
            fx.Quad(c, d, id, ic, edge, edge, clear, clear);
            fx.Quad(d, a, ia, id, edge, edge, clear, clear);
            if (rimLine.a > 0f)
            {
                // The hairline, just inside the light.
                float lx = bx * RimLineShare, ly = by * RimLineShare;
                Vector3 ja = ia + new Vector3(lx, ly, 0f), jb = ib + new Vector3(-lx, ly, 0f), jc = ic + new Vector3(-lx, -ly, 0f), jd = id + new Vector3(lx, -ly, 0f);
                fx.Quad(ia, ib, jb, ja, rimLine);
                fx.Quad(ib, ic, jc, jb, rimLine);
                fx.Quad(ic, id, jd, jc, rimLine);
                fx.Quad(id, ia, ja, jd, rimLine);
            }
            fx.Apply();
            return fx.Mesh;
        }

        // A toy figure one unit tall standing on y = 0: a body that widens to the shoulders and a round head.
        static Mesh FigureMesh => MeshKit.Cached("PortalView Figure", () =>
        {
            var fx = new FxMesh("PortalView Figure");
            Color white = Color.white;
            fx.Quad(new Vector3(-0.13f, 0f, 0f), new Vector3(0.13f, 0f, 0f), new Vector3(0.19f, 0.7f, 0f), new Vector3(-0.19f, 0.7f, 0f), white);
            fx.Polygon(new Vector3(0f, 0.86f, 0f), Vector3.right, Vector3.up, 0.14f, 20, 0f, white);
            fx.Apply();
            return fx.Mesh;
        });

        protected override void Draw(float dt, float alpha)
        {
            Camera main = Context.Camera;
            Vector3 eye = main != null ? main.transform.position : Game.Player.Eye;
            bool cameraAvailable = main != null && CameraAvailable;

            // Which doors are portals right now.
            for (int i = 0; i < doors.Count; i++)
            {
                Door door = doors[i];
                PortalDoorway portal = door.Portal;
                door.Live = false;
                door.Tried = false;
                door.Open = !portal.Disposed && portal.Active && Mathf.Abs(portal.ViewRatio - 1f) > SameSize;
                door.InHand = !portal.Disposed && !portal.Prop.Removed && portal.Prop.Held;
            }

            // Which one gets the camera: the nearest whose opening is on screen. (A door behind the player
            // costs a few dot products, not a render.)
            bool live = false;
            float chosenDistance = float.MaxValue;
            while (cameraAvailable && !live)
            {
                Door chosen = null;
                chosenDistance = float.MaxValue;
                for (int i = 0; i < doors.Count; i++)
                {
                    Door door = doors[i];
                    if (!door.Open || door.Tried) continue;
                    float distance = Vector3.Distance(eye, door.Portal.Threshold);
                    if (distance >= RangeInDoorScales * door.Portal.Prop.Scale || distance >= chosenDistance) continue;
                    chosen = door;
                    chosenDistance = distance;
                }
                if (chosen == null) break;
                chosen.Tried = true;
                try
                {
                    live = RenderView(chosen, main);
                }
                catch (Exception e)
                {
                    // A device that cannot do it gets the cards from here on; the game goes on.
                    Debug.LogException(e);
                    cameraBroken = true;
                    cameraAvailable = false;
                }
                chosen.Live = live;
            }
            if (viewRenderer != null) viewRenderer.enabled = live;

            for (int i = 0; i < doors.Count; i++)
            {
                Door door = doors[i];
                float shown = 0f;
                if (door.Open)
                {
                    shown = 1f;
                    if (door.Live)
                    {
                        float range = RangeInDoorScales * door.Portal.Prop.Scale;
                        shown = GadgetFx.Smooth((chosenDistance - range * FadeShare) / (range * (1f - FadeShare)));
                    }
                }
                door.CardShown = shown;
                PlaceCard(door, shown);
            }
        }

        void PlaceCard(Door door, float shown)
        {
            if (door.Card == null) return;
            bool any = door.Open || door.InHand;
            if (door.Card.gameObject.activeSelf != any) door.Card.gameObject.SetActive(any);
            if (!any) return;
            bool visible = door.Open && shown > 0.001f;
            door.CardRenderer.enabled = visible;
            door.FigureRenderer.enabled = visible;
            door.RimRenderer.enabled = door.Open && door.Live;
            door.HeldRenderer.enabled = door.InHand;
            // Where the door is drawn this frame (in the hand: on the crosshair, at the size it has there).
            PortalDoorway portal = door.Portal;
            Vector2 opening = portal.OpeningSize;
            door.Card.SetPositionAndRotation(portal.OpeningCenter, portal.Rotation);
            door.Card.localScale = new Vector3(opening.x * 0.998f, opening.y * 0.998f, 1f);
            if (!door.Open) return;

            // The figure: as tall as the player will be, standing on the threshold (cut off at the lintel).
            float height = Mathf.Min(Player.BaseHeight * portal.TargetScale, opening.y * 0.97f);
            door.Figure.localPosition = new Vector3(0f, -0.5f, 0f);
            door.Figure.localScale = new Vector3(height / Mathf.Max(1e-4f, opening.x), height / Mathf.Max(1e-4f, opening.y), 1f);

            door.Block.SetVector(GadgetFx.ColorId, new Vector4(1f, 1f, 1f, shown));
            door.CardRenderer.SetPropertyBlock(door.Block);
            Color figure = ink;
            figure.a *= shown;
            door.Block.SetVector(GadgetFx.ColorId, figure);
            door.FigureRenderer.SetPropertyBlock(door.Block);
            door.Block.SetVector(GadgetFx.ColorId, new Vector4(1f, 1f, 1f, 1f - shown));
            door.RimRenderer.SetPropertyBlock(door.Block);
        }

        // Renders the room from the other side's eye into the texture and lays the picture into the opening.
        // False if nothing of the opening is on screen (or the eye is in the door's own plane).
        bool RenderView(Door door, Camera main)
        {
            PortalDoorway portal = door.Portal;
            Vector3 threshold = portal.Threshold;
            Quaternion rotation = portal.Rotation;
            Vector2 opening = portal.OpeningSize;
            Vector3 right = rotation * Vector3.right, up = rotation * Vector3.up, normal = rotation * Vector3.forward;
            Transform eyeTransform = main.transform;
            Vector3 eye = eyeTransform.position;
            float side = Vector3.Dot(eye - threshold, normal);
            float ratio = portal.ViewRatio;
            float distance = Mathf.Abs(side) * ratio;
            if (distance < 1e-4f) return false;

            // The opening, cut to what the game's camera can see (in its own space: x right, y up, z ahead).
            float half = opening.x * 0.5f;
            clipA[0] = eyeTransform.InverseTransformPoint(threshold - right * half);
            clipA[1] = eyeTransform.InverseTransformPoint(threshold + right * half);
            clipA[2] = eyeTransform.InverseTransformPoint(threshold + right * half + up * opening.y);
            clipA[3] = eyeTransform.InverseTransformPoint(threshold - right * half + up * opening.y);
            float tanV = Mathf.Tan(main.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * Mathf.Max(main.aspect, SafeAspect);
            int count = ClipToView(4, tanH, tanV, main.nearClipPlane);
            if (count < 3) return false;

            // What is left, as a rectangle on the door (x along its width, y up from the threshold) and as
            // an angle on the screen.
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            float sx0 = float.MaxValue, sx1 = float.MinValue, sy0 = float.MaxValue, sy1 = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = clipA[i];
                Vector3 onDoor = eyeTransform.TransformPoint(p) - threshold;
                float x = Vector3.Dot(onDoor, right), y = Vector3.Dot(onDoor, up);
                x0 = Mathf.Min(x0, x);
                x1 = Mathf.Max(x1, x);
                y0 = Mathf.Min(y0, y);
                y1 = Mathf.Max(y1, y);
                float sx = p.x / p.z, sy = p.y / p.z;
                sx0 = Mathf.Min(sx0, sx);
                sx1 = Mathf.Max(sx1, sx);
                sy0 = Mathf.Min(sy0, sy);
                sy1 = Mathf.Max(sy1, sy);
            }
            x0 = Mathf.Max(x0, -half);
            x1 = Mathf.Min(x1, half);
            y0 = Mathf.Max(y0, 0f);
            y1 = Mathf.Min(y1, opening.y);
            if (x1 - x0 < opening.x * 1e-3f || y1 - y0 < opening.y * 1e-3f) return false;

            EnsureCamera(main);
            EnsureTexture();

            // As many pixels as that piece of the screen has on this tier, within the texture.
            float perTangent = TierSpec.Of(Tier).PixelHeight * GadgetFx.Pick(Tier, 0.5f, 0.66f, 0.75f) / (2f * tanV);
            float wide = (sx1 - sx0) * perTangent, tall = (sy1 - sy0) * perTangent;
            float fit = Mathf.Min(1f, Mathf.Min(texture.width / Mathf.Max(1f, wide), texture.height / Mathf.Max(1f, tall)));
            int width = Mathf.Clamp(Mathf.CeilToInt(wide * fit), 32, texture.width);
            int height = Mathf.Clamp(Mathf.CeilToInt(tall * fit), 32, texture.height);
            ViewPixels = new Vector2Int(width, height);

            // The other side's eye, looking squarely through the door; the window is the cut opening,
            // scaled about the threshold like the eye.
            Vector3 viewEye = portal.ViewEye(eye);
            Vector3 forward = side > 0f ? -normal : normal;
            Vector3 cameraRight = Vector3.Cross(up, forward);
            float handed = Vector3.Dot(cameraRight, right) >= 0f ? 1f : -1f;
            float originX = Vector3.Dot(threshold - viewEye, cameraRight), originY = Vector3.Dot(threshold - viewEye, up);
            float xa = originX + handed * x0 * ratio, xb = originX + handed * x1 * ratio;
            float left = Mathf.Min(xa, xb), rightEdge = Mathf.Max(xa, xb);
            float bottom = originY + y0 * ratio, top = originY + y1 * ratio;
            float near = Mathf.Max(distance, CameraRig.NearPlane * 0.2f * Mathf.Min(1f, ratio));
            float far = Mathf.Max(near * 2f, main.farClipPlane * Mathf.Max(1f, ratio));
            float toNear = near / distance;

            Transform view = portalCamera.transform;
            view.SetPositionAndRotation(viewEye, Quaternion.LookRotation(forward, up));
            portalCamera.nearClipPlane = near;
            portalCamera.farClipPlane = far;
            portalCamera.projectionMatrix = Matrix4x4.Frustum(left * toNear, rightEdge * toNear, bottom * toNear, top * toNear, near, far);
            portalCamera.clearFlags = main.clearFlags;
            portalCamera.backgroundColor = main.backgroundColor;
            portalCamera.cullingMask = main.cullingMask & ~Layers.HeldMask & ~(1 << 5);
            portalCamera.rect = new Rect(0f, 0f, (float)width / texture.width, (float)height / texture.height);
            // Outside Play Mode the simulation lives in a preview scene that only a camera bound to it draws.
            if (!Application.isPlaying) portalCamera.scene = Game.Scene;

            // The picture must not contain the door it is seen through (it would stand inside itself), nor
            // any of the cards; the frame keeps its shadow.
            for (int i = 0; i < door.Frame.Length; i++)
            {
                Renderer renderer = door.Frame[i];
                if (renderer == null) continue;
                door.FrameModes[i] = renderer.shadowCastingMode;
                if (renderer.shadowCastingMode != ShadowCastingMode.Off) renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                else renderer.forceRenderingOff = true;
            }
            bool viewWasOn = viewRenderer.enabled;
            viewRenderer.enabled = false;
            for (int i = 0; i < doors.Count; i++) SetCardHidden(doors[i], true);
            try
            {
                portalCamera.Render();
                Renders++;
            }
            finally
            {
                for (int i = 0; i < door.Frame.Length; i++)
                {
                    Renderer renderer = door.Frame[i];
                    if (renderer == null) continue;
                    renderer.shadowCastingMode = door.FrameModes[i];
                    renderer.forceRenderingOff = false;
                }
                viewRenderer.enabled = viewWasOn;
                for (int i = 0; i < doors.Count; i++) SetCardHidden(doors[i], false);
            }

            // The picture goes onto the cut rectangle; its part of the texture, half a texel in from the edge.
            viewVertices.Clear();
            viewUvs.Clear();
            for (int corner = 0; corner < 4; corner++)
            {
                float x = corner == 0 || corner == 3 ? x0 : x1, y = corner < 2 ? y0 : y1;
                viewVertices.Add(threshold + right * x + up * y);
                float u = (originX + handed * x * ratio - left) / (rightEdge - left);
                float v = (originY + y * ratio - bottom) / (top - bottom);
                viewUvs.Add(new Vector2((0.5f + u * (width - 1)) / texture.width, (0.5f + v * (height - 1)) / texture.height));
            }
            viewMesh.SetVertices(viewVertices);
            viewMesh.SetUVs(0, viewUvs);
            viewMesh.RecalculateBounds();
            return true;
        }

        static void SetCardHidden(Door door, bool hidden)
        {
            if (door.CardRenderer != null) door.CardRenderer.forceRenderingOff = hidden;
            if (door.FigureRenderer != null) door.FigureRenderer.forceRenderingOff = hidden;
            if (door.RimRenderer != null) door.RimRenderer.forceRenderingOff = hidden;
            if (door.HeldRenderer != null) door.HeldRenderer.forceRenderingOff = hidden;
        }

        // Sutherland-Hodgman against the five planes of a view (camera space, z ahead): what of the polygon in
        // clipA is in front of the near plane and inside the four sides. The result is in clipA again.
        int ClipToView(int count, float tanH, float tanV, float near)
        {
            for (int plane = 0; plane < 5 && count >= 3; plane++)
            {
                int kept = 0;
                for (int i = 0; i < count; i++)
                {
                    Vector3 a = clipA[i], b = clipA[(i + 1) % count];
                    float da = Inside(a, plane, tanH, tanV, near), db = Inside(b, plane, tanH, tanV, near);
                    if (da >= 0f) clipB[kept++] = a;
                    if ((da >= 0f) != (db >= 0f)) clipB[kept++] = Vector3.Lerp(a, b, da / (da - db));
                }
                count = Mathf.Min(kept, clipA.Length);
                for (int i = 0; i < count; i++) clipA[i] = clipB[i];
            }
            return count;
        }

        static float Inside(Vector3 p, int plane, float tanH, float tanV, float near)
        {
            switch (plane)
            {
                case 0: return p.z - near;
                case 1: return p.z * tanH - p.x;
                case 2: return p.z * tanH + p.x;
                case 3: return p.z * tanV - p.y;
                default: return p.z * tanV + p.y;
            }
        }

        void EnsureCamera(Camera main)
        {
            if (portalCamera != null) return;
            var go = new GameObject("Portal Camera") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(Root, false);
            portalCamera = go.AddComponent<Camera>();
            // Rendered by hand, once per frame, before the game's camera: never by the pipeline's own loop.
            portalCamera.enabled = false;
            // A view of the scene for a surface in it, like a reflection: the lens blur and the sticker
            // pass (both skip this camera type) belong to the game's camera alone.
            portalCamera.cameraType = CameraType.Reflection;
            portalCamera.allowHDR = true;
            portalCamera.allowMSAA = false;
            portalCamera.useOcclusionCulling = false;
            portalCamera.depth = main.depth - 1f;
            UniversalAdditionalCameraData data = portalCamera.GetUniversalAdditionalCameraData();
            data.renderShadows = true;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.dithering = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;

            viewMesh = new Mesh { name = "Portal View", hideFlags = HideFlags.DontSave };
            viewMesh.MarkDynamic();
            viewMesh.SetVertices(new[] { Vector3.zero, Vector3.right, new Vector3(1f, 1f, 0f), Vector3.up });
            viewMesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            viewMesh.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });
            viewMesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            viewMaterial = new Material(Shader.Find(ViewShader)) { name = "Portal View", hideFlags = HideFlags.DontSave };
            var quad = new GameObject("Portal View") { hideFlags = HideFlags.DontSave };
            quad.transform.SetParent(Root, false);
            // The mesh is written in world space.
            quad.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            quad.AddComponent<MeshFilter>().sharedMesh = viewMesh;
            viewRenderer = quad.AddComponent<MeshRenderer>();
            viewRenderer.sharedMaterial = viewMaterial;
            viewRenderer.shadowCastingMode = ShadowCastingMode.Off;
            viewRenderer.receiveShadows = false;
            viewRenderer.enabled = false;
        }

        void EnsureTexture()
        {
            if (texture != null) return;
            Vector2Int size = TextureSize(Tier);
            texture = new RenderTexture(new RenderTextureDescriptor(size.x, size.y, GraphicsFormat.B10G11R11_UFloatPack32, 24) { msaaSamples = 1, useMipMap = false })
            {
                name = "Toybox Portal View", hideFlags = HideFlags.DontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
            };
            portalCamera.targetTexture = texture;
            viewMaterial.mainTexture = texture;
        }

        void DropTexture()
        {
            if (texture == null) return;
            if (portalCamera != null) portalCamera.targetTexture = null;
            if (viewMaterial != null) viewMaterial.mainTexture = null;
            texture.Release();
            Sim.Destroy(texture);
            texture = null;
        }

        Door Find(PortalDoorway portal)
        {
            for (int i = 0; i < doors.Count; i++)
                if (doors[i].Portal == portal) return doors[i];
            return null;
        }

        protected override void Forget()
        {
            doors.Clear();
            if (viewRenderer != null) viewRenderer.enabled = false;
        }

        protected override void Release()
        {
            DropTexture();
            if (viewRenderer != null) Sim.Destroy(viewRenderer.gameObject);
            if (portalCamera != null) Sim.Destroy(portalCamera.gameObject);
            if (viewMesh != null) Sim.Destroy(viewMesh);
            if (viewMaterial != null) Sim.Destroy(viewMaterial);
            viewRenderer = null;
            portalCamera = null;
            viewMesh = null;
            viewMaterial = null;
        }
    }
}
