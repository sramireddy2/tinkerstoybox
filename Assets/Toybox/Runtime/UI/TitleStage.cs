using Toybox.Art;
using Toybox.Engine;
using Toybox.Platform;
using UnityEngine;

namespace Toybox.UI
{
    /// <summary>
    /// What the title screen shows of the world (ART_BIBLE 10.5 "Title"): one giant glossy toy turning in
    /// front of the figure, seen from a little above eye height; when play starts the toy goes and the
    /// camera drops into first person. The toy is a renderer only - a collider under the presentation
    /// root would join the simulation - and it stands in whatever level waits behind the title.
    /// </summary>
    public sealed class TitleStage
    {
        public const float DegreesPerSecond = 14f, Raise = 0.9f, DropTime = UiTheme.Slow;
        const float MaxDistance = 12f, MinDistance = 4f, SizeShare = 0.34f, Aside = 0.56f;

        readonly PresentationContext context;
        readonly Game game;
        GameObject toy;
        float size = 1f, angle;
        // 1 on the title, falling to 0 as the camera drops into first person.
        float blend;
        bool onTitle;

        public GameObject Toy => toy;
        public float Blend => blend;
        public bool Active => onTitle || blend > 0f;

        public TitleStage(Game game, PresentationContext context)
        {
            this.game = game;
            this.context = context;
        }

        /// <summary>The title is up: the toy stands in front of the figure and the camera looks from above its head.</summary>
        public void Enter()
        {
            if (onTitle) return;
            onTitle = true;
            blend = 1f;
            Build();
        }

        /// <summary>
        /// Another level was loaded. Behind the title the toy moves into it; otherwise (a level picked from
        /// the catalogue) the toy belonged to the level that is gone, and goes with it.
        /// </summary>
        public void LevelLoaded(bool behindTheTitle)
        {
            if (onTitle && behindTheTitle) Build();
            else Clear();
        }

        /// <summary>Play starts: the camera drops (at once with "Reduce motion").</summary>
        public void Leave()
        {
            if (!onTitle) return;
            onTitle = false;
            if (Settings.ReduceMotion) blend = 0f;
            if (blend <= 0f) Clear();
        }

        /// <summary>After the camera rig placed the camera: turns the toy and lifts the camera by what is left of the drop.</summary>
        public void Frame(float dt)
        {
            if (!onTitle && blend > 0f)
            {
                blend = Mathf.Max(0f, blend - dt / DropTime);
                if (blend <= 0f) Clear();
            }
            if (toy != null)
            {
                angle = Mathf.Repeat(angle + DegreesPerSecond * dt, 360f);
                float k = UiTheme.Smooth(blend);
                toy.transform.localRotation = Quaternion.Euler(0f, angle, 0f);
                toy.transform.localScale = Vector3.one * (size * k);
            }
            Camera camera = context.Camera;
            if (blend > 0f && camera != null)
                camera.transform.position += Vector3.up * (Raise * game.Player.Scale * UiTheme.Smooth(blend));
        }

        public void Dispose()
        {
            onTitle = false;
            blend = 0f;
            Clear();
        }

        void Clear()
        {
            UiKit.Destroy(toy);
            toy = null;
        }

        void Build()
        {
            Clear();
            if (!context.HasGraphics || game.Level == null) return;
            Player player = game.Player;
            Vector3 forward = Quaternion.Euler(0f, player.Yaw, 0f) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0f, player.Yaw, 0f) * Vector3.right;

            // As far out as the level leaves room, and as big as that distance makes "giant".
            float room = MaxDistance / 0.75f;
            if (game.PhysicsScene.Raycast(player.Eye, forward, out RaycastHit hit, room, Layers.SolidMask, QueryTriggerInteraction.Ignore)) room = hit.distance;
            float distance = Mathf.Clamp(room * 0.75f, MinDistance, MaxDistance) * player.Scale;
            size = distance * SizeShare;

            Color candy = Palette.DipOf(game.Level.Environment).Hero;
            toy = new GameObject("Title Turntable") { hideFlags = HideFlags.DontSave };
            toy.transform.SetParent(context.Root, false);
            toy.transform.position = player.Position + forward * distance + right * (distance * Aside) + Vector3.up * (size * 0.5f);
            Mesh mesh = MeshKit.Cached(MeshKit.Key("Title Toy", 0.09f), () => MeshKit.RoundedBox(Vector3.one, 0.09f, 3));
            toy.AddComponent<MeshFilter>().sharedMesh = mesh;
            toy.AddComponent<MeshRenderer>().sharedMaterial = Materials.Toy(ToyRecipe.GlossyPlastic, candy);
            toy.transform.localScale = Vector3.one * size;
        }
    }
}
