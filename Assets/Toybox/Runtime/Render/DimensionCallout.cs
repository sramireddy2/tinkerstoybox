using System.Globalization;
using TMPro;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Platform;
using Toybox.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Render
{
    /// <summary>
    /// The dimension callout of a release (ART_BIBLE 9.4): when a toy is let go at a size more than 15%
    /// off the size it was picked up with, a catalogue drawing stands beside it for a second and a half -
    /// a vertical dimension line of the toy's true height with end ticks, the factor ("x3.2") at half
    /// height, and a flat 1.7-unit figure at the toy's base, for scale. Ink (Paper in the night room, where
    /// Ink would not show), in the world, depth-tested, turned about the vertical to face the camera. In
    /// over 120 ms, out over 300 ms.
    ///
    /// It shows true size, which is its job: the toy is no longer held when it appears.
    /// </summary>
    [Presenter(215)]
    public sealed class DimensionCallout : IPresenter
    {
        /// <summary>The change of scale over a hold, as a factor, below which there is nothing to call out.</summary>
        public const float Threshold = 0.15f;
        public const float Seconds = 1.5f, InSeconds = 0.12f, OutSeconds = 0.3f;
        public const float FigureHeight = 1.7f;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly Vector3[] LineVertices = new Vector3[12];
        static readonly int[] LineTriangles = { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6, 8, 10, 9, 8, 11, 10 };

        Game game;
        PresentationContext context;
        bool active;
        GameObject root, lineObject, figureObject, labelObject;
        Mesh line, figure;
        MeshRenderer lineRenderer, figureRenderer;
        TextMeshPro label;
        Material ink;
        Color inkColor;
        MaterialPropertyBlock block;

        Prop prop;
        float age = float.MaxValue, height, reach, thickness;

        /// <summary>The toy the drawing stands beside, or null while none is shown.</summary>
        public Prop Shown => age < Seconds ? prop : null;
        /// <summary>The height the dimension line measures: the toy's true height at the release.</summary>
        public float Height => height;
        /// <summary>The label's text ("x3.2"), or null without a label.</summary>
        public string Text => label != null ? label.text : null;
        /// <summary>0..1: how opaque the drawing is right now.</summary>
        public float Opacity { get; private set; }
        /// <summary>The drawing's root (inactive while nothing is shown).</summary>
        public Transform Root => root != null ? root.transform : null;

        public void Attach(Game game, PresentationContext context)
        {
            this.game = game;
            this.context = context;
            // A drawing in the look's own ink; the plain look does without.
            if (context.Plain || !context.HasGraphics) return;
            active = true;
            // Made up front and put away, like the menus: showing it later creates nothing (no hitch at the
            // first release, and nothing that looks like a leak to whoever counts objects).
            Build();
            game.Events.PropDropped += OnDropped;
            game.Events.PropGrabbed += OnGrabbed;
            game.Events.LevelUnloading += OnLevelUnloading;
        }

        public void Frame(float dt, float alpha)
        {
            if (!active || root == null || age >= Seconds) return;
            age += dt;
            if (age >= Seconds || prop == null || prop.Removed || prop.Held)
            {
                Hide();
                return;
            }
            Opacity = Mathf.Clamp01(Mathf.Min(age / InSeconds, (Seconds - age) / OutSeconds));
            Place();
            inkColor.a = Opacity;
            block.SetVector(ColorId, inkColor);
            lineRenderer.SetPropertyBlock(block);
            figureRenderer.SetPropertyBlock(block);
            if (label != null) label.alpha = Opacity;
        }

        public void Dispose()
        {
            if (!active) return;
            active = false;
            game.Events.PropDropped -= OnDropped;
            game.Events.PropGrabbed -= OnGrabbed;
            game.Events.LevelUnloading -= OnLevelUnloading;
            if (root != null) Sim.Destroy(root);
            if (line != null) Sim.Destroy(line);
            if (figure != null) Sim.Destroy(figure);
            root = null;
            line = null;
            figure = null;
            prop = null;
        }

        void OnDropped(PropHoldEvent e)
        {
            if (e.Prop == null || e.Prop.Removed) return;
            float factor = e.Factor;
            if (!(factor > 0f) || Mathf.Abs(factor - 1f) <= Threshold) return;
            Show(e.Prop, factor);
        }

        void OnGrabbed(PropHoldEvent e) => Hide();

        void OnLevelUnloading(LevelEvent e) => Hide();

        void Hide()
        {
            age = float.MaxValue;
            Opacity = 0f;
            prop = null;
            if (root != null && root.activeSelf) root.SetActive(false);
        }

        void Show(Prop target, float factor)
        {
            prop = target;
            age = 0f;
            Opacity = 0f;
            // Ink on the Plum of the night room would be a drawing nobody sees.
            bool night = game.Environment != null && game.Environment.Preset.Night;
            inkColor = Palette.Lin(night ? Palette.Paper : Palette.Ink);
            if (label != null) label.color = night ? Palette.Paper : Palette.Ink;

            Bounds bounds = BoundsOf(target);
            height = Mathf.Max(bounds.size.y, 0.05f);
            thickness = Mathf.Clamp(height * 0.012f, 0.02f, 0.12f);
            DrawLine();

            if (label != null)
            {
                label.text = "×" + factor.ToString(factor >= 10f ? "0" : "0.0", CultureInfo.InvariantCulture);
                label.fontSize = Mathf.Clamp(height * 1.4f, 3f, 24f);
            }
            root.SetActive(true);
            Place();
        }

        // Beside the toy on the camera's right, standing on the level of the toy's base, facing the camera.
        void Place()
        {
            Bounds bounds = BoundsOf(prop);
            Camera camera = context.Camera;
            Vector3 toCamera = camera != null ? camera.transform.position - bounds.center : Vector3.back;
            toCamera.y = 0f;
            Vector3 forward = toCamera.sqrMagnitude > 1e-6f ? -toCamera.normalized : Vector3.forward;
            Quaternion facing = Quaternion.LookRotation(forward, Vector3.up);
            Vector3 right = facing * Vector3.right;
            float gap = Mathf.Clamp(height * 0.15f, 0.2f, 1.5f);
            // How far the toy reaches toward the camera's right: a long plank seen end-on is narrow.
            reach = Mathf.Abs(right.x) * bounds.extents.x + Mathf.Abs(right.z) * bounds.extents.z;
            Vector3 foot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) + right * (reach + gap);

            root.transform.SetPositionAndRotation(foot, facing);
            float figureScale = game.Player != null ? game.Player.Scale : 1f;
            float tick = thickness * 8f;
            figureObject.transform.localPosition = new Vector3(tick + 0.45f * figureScale, 0f, 0f);
            figureObject.transform.localScale = new Vector3(figureScale, figureScale, 0.03f * figureScale);
            // At half height - or over the figure's head, where half height would put it across the figure.
            if (labelObject != null)
                labelObject.transform.localPosition = new Vector3(tick, Mathf.Max(height * 0.5f, FigureHeight * figureScale + 0.12f * label.fontSize), 0f);
        }

        // A vertical bar from the base to the true height, with a tick at either end.
        void DrawLine()
        {
            float t = thickness * 0.5f, tick = thickness * 4f;
            Quad(0, -t, 0f, t, height);
            Quad(4, -tick, 0f, tick, thickness);
            Quad(8, -tick, height - thickness, tick, height);
            line.SetVertices(LineVertices);
            line.RecalculateBounds();
        }

        static void Quad(int first, float x0, float y0, float x1, float y1)
        {
            LineVertices[first] = new Vector3(x0, y0, 0f);
            LineVertices[first + 1] = new Vector3(x1, y0, 0f);
            LineVertices[first + 2] = new Vector3(x1, y1, 0f);
            LineVertices[first + 3] = new Vector3(x0, y1, 0f);
        }

        static Bounds BoundsOf(Prop target)
        {
            Collider[] colliders = target.Colliders;
            if (colliders == null || colliders.Length == 0) return new Bounds(target.Center, Vector3.one * (target.Radius * 2f));
            Bounds bounds = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++) bounds.Encapsulate(colliders[i].bounds);
            return bounds;
        }

        void Build()
        {
            if (root != null) return;
            block = new MaterialPropertyBlock();
            inkColor = Palette.Lin(Palette.Ink);
            inkColor.a = 1f;
            ink = Materials.Flat(new FlatRecipe { Name = "Callout", Color = inkColor, Blend = FlatBlend.Alpha });

            root = new GameObject("Dimension Callout") { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(context.Root, false);

            line = new Mesh { name = "Callout Line", hideFlags = HideFlags.DontSave };
            line.MarkDynamic();
            line.SetVertices(LineVertices);
            var uvs = new Vector2[12];
            var colors = new Color[12];
            for (int i = 0; i < 12; i++)
            {
                uvs[i] = new Vector2(0.5f, 0.5f);
                colors[i] = Color.white;
            }
            line.SetUVs(0, uvs);
            line.SetColors(colors);
            line.SetTriangles(LineTriangles, 0);
            lineObject = Part("Line", line, out lineRenderer);

            figure = RoomVisuals.FigureMesh();
            figure.hideFlags = HideFlags.DontSave;
            figureObject = Part("Figure", figure, out figureRenderer);

            if (UiFonts.Available)
            {
                labelObject = new GameObject("Factor") { hideFlags = HideFlags.DontSave };
                labelObject.transform.SetParent(root.transform, false);
                label = labelObject.AddComponent<TextMeshPro>();
                label.font = UiFonts.Display;
                Material preset = UiFonts.Preset(UiFont.Display);
                if (preset != null) label.fontSharedMaterial = preset;
                label.color = Palette.Ink;
                label.alignment = TextAlignmentOptions.Left;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.rectTransform.pivot = new Vector2(0f, 0.5f);
                label.rectTransform.sizeDelta = new Vector2(20f, 4f);
                // The card faces the camera with its back (LookRotation points away from it): turn the text round.
                labelObject.transform.localRotation = Quaternion.identity;
                MeshRenderer text = label.GetComponent<MeshRenderer>();
                text.shadowCastingMode = ShadowCastingMode.Off;
                text.receiveShadows = false;
            }
            root.SetActive(false);
        }

        GameObject Part(string name, Mesh mesh, out MeshRenderer renderer)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ink;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }
    }
}
