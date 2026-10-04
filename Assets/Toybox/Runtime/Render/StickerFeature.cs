using System.Collections.Generic;
using Toybox.Engine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Toybox.Render
{
    /// <summary>
    /// What the sticker pass draws this frame. <see cref="HeldLook"/> writes it every frame; the pass
    /// reads it when a camera renders. Sizes are pixels at 1080p and scale with the height of the render
    /// target (ART_BIBLE conventions); colours are linear.
    ///
    /// Without a HeldLook (the plain look) <see cref="Border"/> stays false and the pass draws the held
    /// toy on top of everything with its own materials and nothing else.
    /// </summary>
    public static class StickerLook
    {
        /// <summary>Resting width of the die-cut border (9: 0.0042 x height).</summary>
        public const float BorderWidth = 4.5f;
        /// <summary>Where the peel shadow rests: right and down.</summary>
        public static readonly Vector2 PeelOffset = new Vector2(10f, -12f);
        /// <summary>
        /// The distance the held toy is drawn at, in near planes (4 units with the standard 0.05). See
        /// <see cref="HeldScale"/>.
        /// </summary>
        public const float DrawDistanceInNearPlanes = 80f;

        /// <summary>Draw the border and the peel shadow.</summary>
        public static bool Border;
        /// <summary>Border width right now.</summary>
        public static float BorderPx;
        /// <summary>Peel shadow offset right now: x right, y up.</summary>
        public static Vector2 PeelPx;
        /// <summary>Paper x 1.1.</summary>
        public static Color BorderColor = Color.white;
        /// <summary>lerp(white, Ink, 0.22); the frame is multiplied by it.</summary>
        public static Color PeelColor = Color.white;
        /// <summary>
        /// For tests and tools: true draws the Held layer as a sticker although no Game holds anything,
        /// false never does; null (the default) follows the game.
        /// </summary>
        public static bool? Force;
        /// <summary>For tests and tools: the point the homothety brings to the fixed distance, instead of the held prop's centre.</summary>
        public static Vector3? Centre;

        /// <summary>True while the sticker pass has something to draw: a Game is holding a prop.</summary>
        public static bool Active
        {
            get
            {
                if (Force.HasValue) return Force.Value;
                Game game = Game.Current;
                return game != null && !game.IsDisposed && game.Grabber.IsHolding;
            }
        }

        /// <summary>Back to "no border"; tests and tools also lose their overrides.</summary>
        public static void Reset()
        {
            Border = false;
            BorderPx = 0f;
            PeelPx = Vector2.zero;
            Force = null;
            Centre = null;
        }

        /// <summary>
        /// The factor the sticker pass scales view space by for a camera: it brings the held toy's centre
        /// to a fixed distance from that camera (ART_BIBLE 8.3). A uniform scaling about the eye does not
        /// change a perspective picture, so this is invisible - but depth precision and near-plane clipping
        /// no longer depend on how far away the toy is held. 1 where it does not apply.
        /// </summary>
        public static float HeldScale(Camera camera)
        {
            if (camera == null || camera.orthographic) return 1f;
            Vector3 centre;
            if (Centre.HasValue)
            {
                centre = Centre.Value;
            }
            else
            {
                Game game = Game.Current;
                Prop held = game != null && !game.IsDisposed ? game.Grabber.Held : null;
                if (held == null || held.Removed) return 1f;
                centre = held.Center;
            }
            float distance = Vector3.Distance(camera.transform.position, centre);
            if (!(distance > 1e-4f)) return 1f;
            return DrawDistanceInNearPlanes * camera.nearClipPlane / distance;
        }
    }

    /// <summary>
    /// The held-object rule's render pass (ART_BIBLE 8.1 step 5): the game's "always on top" layer. The
    /// renderer's opaque and transparent layer masks leave out the Held layer, so the ordinary passes skip
    /// the held toy; this feature draws it last, at BeforeRenderingPostProcessing + 1 (after the lens blur,
    /// before bloom and tonemapping), only while something is held:
    ///
    ///   1. clear depth and stencil - the toy can no longer intersect or z-fight the wall it rests on, and
    ///      nothing drawn earlier can cover it;
    ///   2. _ToyHeld = 1 (ToyLit drops the received shadow) and _ToyHeldScale (see StickerLook.HeldScale);
    ///   3. the Held layer with Sticker.mat pass 0 - the border, constant pixel width;
    ///   4. the Held layer with Sticker.mat pass 1 - the peel shadow, hard, offset, multiplied;
    ///   5. the Held layer with its own materials, opaque queue then transparent queue;
    ///   6. _ToyHeld = 0.
    ///
    /// The material is a serialized reference to Resources/Materials/Sticker.mat (ToyShadingSetup assigns
    /// it), which is what keeps Toybox/Sticker in a player build. Without it the toy is still drawn on top,
    /// just without border and shadow.
    /// </summary>
    public sealed class StickerFeature : ScriptableRendererFeature
    {
        public const string FeatureName = "Sticker";

        [SerializeField] Material material;

        StickerPass pass;

        /// <summary>The override material: Toybox/Sticker with its Border (0) and PeelShadow (1) passes.</summary>
        public Material Material
        {
            get => material;
            set => material = value;
        }

        public override void Create()
        {
            pass = new StickerPass { renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing + 1 };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (pass == null || !StickerLook.Active) return;
            CameraType type = renderingData.cameraData.cameraType;
            if (type == CameraType.Preview || type == CameraType.Reflection) return;
            pass.Material = material;
            renderer.EnqueuePass(pass);
        }

        sealed class StickerPass : ScriptableRenderPass
        {
            static readonly int ToyHeldId = Shader.PropertyToID("_ToyHeld");
            static readonly int ToyHeldScaleId = Shader.PropertyToID("_ToyHeldScale");
            static readonly int StickerPxId = Shader.PropertyToID("_StickerPx");
            static readonly int StickerColorId = Shader.PropertyToID("_StickerColor");
            static readonly int PeelColorId = Shader.PropertyToID("_PeelColor");

            // Whatever the held toy is made of: the game's lit and unlit shaders, and URP's own in the plain look.
            static readonly List<ShaderTagId> PassTags = new List<ShaderTagId>
            {
                new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly"), new ShaderTagId("SRPDefaultUnlit"),
            };

            public Material Material;

            public StickerPass()
            {
                profilingSampler = new ProfilingSampler(FeatureName);
            }

            sealed class PassData
            {
                public RendererListHandle Border, Peel, Opaque, Transparent;
                public bool DrawBorder;
                public Vector4 Px, BorderColor, PeelColor;
                public float HeldScale;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                UniversalRenderingData rendering = frameData.Get<UniversalRenderingData>();
                UniversalCameraData camera = frameData.Get<UniversalCameraData>();
                UniversalLightData lights = frameData.Get<UniversalLightData>();

                using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(FeatureName, out PassData data, profilingSampler))
                {
                    var held = new FilteringSettings(RenderQueueRange.opaque, Layers.HeldMask);
                    DrawingSettings own = RenderingUtils.CreateDrawingSettings(PassTags, rendering, camera, lights, SortingCriteria.CommonOpaque);
                    data.Opaque = renderGraph.CreateRendererList(new RendererListParams(rendering.cullResults, own, held));
                    held.renderQueueRange = RenderQueueRange.transparent;
                    DrawingSettings blended = RenderingUtils.CreateDrawingSettings(PassTags, rendering, camera, lights, SortingCriteria.CommonTransparent);
                    data.Transparent = renderGraph.CreateRendererList(new RendererListParams(rendering.cullResults, blended, held));
                    builder.UseRendererList(data.Opaque);
                    builder.UseRendererList(data.Transparent);

                    data.DrawBorder = Material != null && StickerLook.Border && StickerLook.BorderPx > 0f;
                    if (data.DrawBorder)
                    {
                        held.renderQueueRange = RenderQueueRange.all;
                        DrawingSettings edge = own;
                        edge.overrideMaterial = Material;
                        edge.overrideMaterialPassIndex = 0;
                        DrawingSettings peel = own;
                        peel.overrideMaterial = Material;
                        peel.overrideMaterialPassIndex = 1;
                        data.Border = renderGraph.CreateRendererList(new RendererListParams(rendering.cullResults, edge, held));
                        data.Peel = renderGraph.CreateRendererList(new RendererListParams(rendering.cullResults, peel, held));
                        builder.UseRendererList(data.Border);
                        builder.UseRendererList(data.Peel);
                    }

                    // Pixel sizes are quoted at 1080p and scale with the height of the render target.
                    float perPixel = camera.cameraTargetDescriptor.height / 1080f;
                    data.Px = new Vector4(StickerLook.BorderPx * perPixel, StickerLook.PeelPx.x * perPixel, StickerLook.PeelPx.y * perPixel, 0f);
                    data.BorderColor = StickerLook.BorderColor;
                    data.PeelColor = StickerLook.PeelColor;
                    data.HeldScale = StickerLook.HeldScale(camera.camera);

                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                    // The toy's own shader still samples the shadow map, although _ToyHeld discards the result.
                    if (resources.mainShadowsTexture.IsValid()) builder.UseTexture(resources.mainShadowsTexture, AccessFlags.Read);
                    builder.AllowGlobalStateModification(true);

                    builder.SetRenderFunc(static (PassData pass, RasterGraphContext context) =>
                    {
                        RasterCommandBuffer cmd = context.cmd;
                        cmd.ClearRenderTarget(RTClearFlags.DepthStencil, Color.clear, 1f, 0);
                        cmd.SetGlobalFloat(ToyHeldId, 1f);
                        cmd.SetGlobalFloat(ToyHeldScaleId, pass.HeldScale);
                        if (pass.DrawBorder)
                        {
                            cmd.SetGlobalVector(StickerPxId, pass.Px);
                            cmd.SetGlobalVector(StickerColorId, pass.BorderColor);
                            cmd.SetGlobalVector(PeelColorId, pass.PeelColor);
                            cmd.DrawRendererList(pass.Border);
                            cmd.DrawRendererList(pass.Peel);
                        }
                        cmd.DrawRendererList(pass.Opaque);
                        cmd.DrawRendererList(pass.Transparent);
                        cmd.SetGlobalFloat(ToyHeldId, 0f);
                        cmd.SetGlobalFloat(ToyHeldScaleId, 1f);
                    });
                }
            }
        }
    }
}
