using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Platform;
using UnityEngine;
using UnityEngine.Rendering;
using Volume = Toybox.Engine.Volume;

namespace Toybox.Render
{
    /// <summary>
    /// The way out, drawn (ART_BIBLE 2.3, 4.4): every exit of the level shows the four-pane mark at its
    /// middle - white at three times over the bloom threshold while it is open, small and faint Paper
    /// while it is locked (state never depends on hue alone: a locked mark is smaller and does not glow).
    /// The mark is a flat card that turns about the vertical to face the camera; the room's walls hide it
    /// like anything else. It grows to its size over 180 ms when the exit unlocks.
    ///
    /// Renderers only: an exit's trigger is the simulation's and stays as it is.
    /// </summary>
    [Presenter(220)]
    public sealed class ExitMarks : IPresenter
    {
        /// <summary>The open mark's gain over white (the Exit signal of 2.3).</summary>
        public const float OpenGain = 3f;
        /// <summary>A locked mark: this share of the open one's size, and this opaque.</summary>
        public const float LockedSize = 0.55f, LockedAlpha = 0.45f;
        /// <summary>The mark fills this share of the smaller side of the exit's box, and is never larger than <see cref="MaxSize"/>.</summary>
        public const float Fill = 0.7f, MinSize = 0.5f, MaxSize = 6f;
        public const float PopSeconds = 0.18f;
        /// <summary>
        /// The mark thins out as the eye comes up to it - gone at this many times its size, whole at
        /// <see cref="FadeFar"/> times - so that walking into the exit is not walking into a white wall.
        /// </summary>
        public const float FadeNear = 0.6f, FadeFar = 1.8f;

        static readonly int ColorId = Shader.PropertyToID("_Color");

        sealed class Mark
        {
            public Exit Exit;
            public GameObject Object;
            public MeshRenderer Renderer;
            public float Size;
            /// <summary>0 locked .. 1 open, eased.</summary>
            public float Open;
            public bool WasLocked;
            /// <summary>The alpha factor last written to the renderer; negative before the first write.</summary>
            public float Fade = -1f;
        }

        readonly List<Mark> marks = new List<Mark>();
        Game game;
        PresentationContext context;
        Mesh quad;
        Material open, locked;
        Color openColor, lockedColor;
        MaterialPropertyBlock block;
        bool attached;

        /// <summary>Marks standing right now.</summary>
        public int Count => marks.Count;
        /// <summary>The renderer of the i-th exit's mark (in the order of game.Exits).</summary>
        public Renderer RendererOf(int index) => index >= 0 && index < marks.Count ? marks[index].Renderer : null;
        /// <summary>How open the i-th mark is drawn, 0..1.</summary>
        public float OpenOf(int index) => index >= 0 && index < marks.Count ? marks[index].Open : 0f;

        public void Attach(Game game, PresentationContext context)
        {
            this.game = game;
            this.context = context;
            attached = true;
            game.Events.LevelLoaded += OnLevelLoaded;
            game.Events.LevelUnloading += OnLevelUnloading;
            Rebuild();
        }

        public void Frame(float dt, float alpha)
        {
            if (!attached) return;
            // An exit added after the level was built (a gadget may do that) gets its mark too.
            if (game.Exits.Count != marks.Count) Rebuild();

            Camera camera = context.Camera;
            for (int i = 0; i < marks.Count; i++)
            {
                Mark mark = marks[i];
                if (mark.Object == null) continue;
                bool isLocked = mark.Exit.Locked;
                if (isLocked != mark.WasLocked)
                {
                    mark.WasLocked = isLocked;
                    mark.Renderer.sharedMaterial = isLocked ? locked : open;
                    mark.Fade = -1f;
                }
                mark.Open = Mathf.MoveTowards(mark.Open, isLocked ? 0f : 1f, dt / PopSeconds);
                float eased = mark.Open * mark.Open * (3f - 2f * mark.Open);
                float size = mark.Size * Mathf.Lerp(LockedSize, 1f, eased);

                Transform transform = mark.Object.transform;
                transform.position = mark.Exit.Position;
                transform.localScale = new Vector3(size, size, size);
                float fade = 1f;
                if (camera != null)
                {
                    Vector3 toCamera = camera.transform.position - transform.position;
                    fade = Mathf.Clamp01((toCamera.magnitude / Mathf.Max(size, 1e-3f) - FadeNear) / (FadeFar - FadeNear));
                    toCamera.y = 0f;
                    if (toCamera.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
                }
                if (fade != mark.Fade)
                {
                    mark.Fade = fade;
                    Color color = isLocked ? lockedColor : openColor;
                    color.a *= fade;
                    block.SetVector(ColorId, color);
                    mark.Renderer.SetPropertyBlock(block);
                }
            }
        }

        public void Dispose()
        {
            if (!attached) return;
            attached = false;
            game.Events.LevelLoaded -= OnLevelLoaded;
            game.Events.LevelUnloading -= OnLevelUnloading;
            Clear();
            if (quad != null) Sim.Destroy(quad);
            quad = null;
        }

        void OnLevelLoaded(LevelEvent e) => Rebuild();

        void OnLevelUnloading(LevelEvent e) => Clear();

        void Clear()
        {
            for (int i = 0; i < marks.Count; i++)
                if (marks[i].Object != null) Sim.Destroy(marks[i].Object);
            marks.Clear();
        }

        void Rebuild()
        {
            Clear();
            if (game.Level == null || !context.HasGraphics) return;
            if (quad == null) quad = MakeQuad();
            block ??= new MaterialPropertyBlock();
            // White at the Exit signal's gain; alpha blended, so the mark also reads against a pale wall.
            openColor = Color.white * OpenGain;
            openColor.a = 1f;
            open = Materials.Flat(new FlatRecipe { Name = "Exit Mark", Color = openColor, Shape = FlatShape.FourPane, Blend = FlatBlend.Alpha, Soft = 0.06f });
            lockedColor = Palette.Lin(Palette.Paper);
            lockedColor.a = LockedAlpha;
            locked = Materials.Flat(new FlatRecipe { Name = "Exit Mark Locked", Color = lockedColor, Shape = FlatShape.FourPane, Blend = FlatBlend.Alpha, Soft = 0.06f });

            IReadOnlyList<Exit> exits = game.Exits;
            for (int i = 0; i < exits.Count; i++)
            {
                Exit exit = exits[i];
                Volume volume = exit.Trigger.Volume;
                float across = volume.Shape == VolumeShape.Box ? Mathf.Min(Mathf.Max(volume.Size.x, volume.Size.z), volume.Size.y) : volume.Radius * 2f;

                var go = new GameObject("Exit Mark") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(context.Root, false);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sharedMaterial = exit.Locked ? locked : open;
                marks.Add(new Mark
                {
                    Exit = exit,
                    Object = go,
                    Renderer = renderer,
                    Size = Mathf.Clamp(across * Fill, MinSize, MaxSize),
                    Open = exit.Locked ? 0f : 1f,
                    WasLocked = exit.Locked,
                });
            }
            Frame(0f, 1f);
        }

        // A unit square in the XY plane, its UVs the unit square the shape mask lives in.
        static Mesh MakeQuad()
        {
            var mesh = new Mesh { name = "Exit Mark", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
            mesh.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            mesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            mesh.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
