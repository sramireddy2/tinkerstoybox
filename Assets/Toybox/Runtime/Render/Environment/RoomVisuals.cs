using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Platform;
using UnityEngine;
using UnityEngine.Rendering;

namespace Toybox.Render
{
    /// <summary>
    /// Everything of the playroom that can be seen (ART_BIBLE 6): on every level load it reads
    /// <c>game.Environment</c> - the room the simulation solved around the level and made the colliders
    /// of - and builds the visuals to match: the shell and its island, skirting, window frame and wall
    /// plates, the furniture silhouettes with their hull shadows, the sky card outside the window, and on
    /// Medium and High the light shaft with its dust motes. One mesh per material, in world space, a
    /// dozen draw calls for a whole room; no colliders (those are the simulation's).
    ///
    /// It also keeps the player's shadow: an action-figure silhouette that only casts (ART_BIBLE 5.3),
    /// the yardstick beside every released toy.
    /// </summary>
    [Presenter(110, ProvidesLook = true)]
    public sealed class RoomVisuals : IPresenter
    {
        /// <summary>Dust motes alive in the light shaft by tier (none on Low).</summary>
        public const int MotesMedium = 120, MotesHigh = 300;
        public const float MoteMinSize = 0.04f, MoteMaxSize = 0.12f, MoteDrift = 0.15f;

        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<Renderer> renderers = new List<Renderer>();

        Game game;
        PresentationContext context;
        GameObject root;
        GameObject shaftObject, motesObject, shadowObject;
        ParticleSystem motes;
        bool attached;

        /// <summary>Parent of the room's objects, or null while there is no room.</summary>
        public Transform Root => root != null ? root.transform : null;
        /// <summary>The room's mesh renderers: one draw call each (the light shaft included; the dust is separate).</summary>
        public IReadOnlyList<Renderer> Renderers => renderers;
        /// <summary>Triangles in the room's meshes.</summary>
        public int Triangles { get; private set; }
        /// <summary>How long the last room took to build, in milliseconds (it happens once per level load).</summary>
        public float BuildMilliseconds { get; private set; }
        /// <summary>The dust motes in the light shaft, or null (Low, or no room).</summary>
        public ParticleSystem Motes => motes;
        /// <summary>The renderer of the player's shadow figure.</summary>
        public Renderer PlayerShadow { get; private set; }

        public void Attach(Game game, PresentationContext context)
        {
            this.game = game;
            this.context = context;
            game.Events.LevelLoaded += OnLevelLoaded;
            game.Events.LevelUnloading += OnLevelUnloading;
            context.QualityChanged += OnQualityChanged;
            attached = true;
            CreatePlayerShadow();
            Build();
        }

        public void Frame(float dt, float alpha)
        {
            if (!attached || game.IsDisposed) return;
            if (motes != null) motes.Simulate(dt, true, false, false);
            if (shadowObject != null)
            {
                // Under the camera, turned with it, as tall as the player is now.
                Player player = game.Player;
                Vector3 feet = player.EyeAt(alpha) - Vector3.up * player.EyeHeight;
                shadowObject.transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, player.Yaw, 0f));
                shadowObject.transform.localScale = Vector3.one * player.Scale;
            }
        }

        public void Dispose()
        {
            if (!attached) return;
            attached = false;
            game.Events.LevelLoaded -= OnLevelLoaded;
            game.Events.LevelUnloading -= OnLevelUnloading;
            context.QualityChanged -= OnQualityChanged;
            Clear();
            if (shadowObject != null) Sim.Destroy(shadowObject);
            shadowObject = null;
            PlayerShadow = null;
        }

        void OnLevelLoaded(LevelEvent e) => Build();
        void OnLevelUnloading(LevelEvent e) => Clear();

        void OnQualityChanged(QualityTier tier)
        {
            if (root == null || game.Environment == null) return;
            BuildShaft(game.Environment);
        }

        void Clear()
        {
            if (root != null) Sim.Destroy(root);
            root = null;
            shaftObject = null;
            motesObject = null;
            motes = null;
            foreach (Mesh mesh in meshes) MeshKit.Release(mesh);
            meshes.Clear();
            renderers.Clear();
            Triangles = 0;
        }

        // ---- The room ---------------------------------------------------------------------------------------

        void Build()
        {
            Clear();
            BuildMilliseconds = 0f;
            EnvironmentDescriptor env = game.Environment;
            if (env == null || !env.HasRoom || !context.HasGraphics) return;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            EnvironmentPreset preset = env.Preset;
            Dip dip = preset.Dip;

            root = new GameObject("Room") { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(context.Root, false);

            // Shell: the floor, the walls (the back wall apart where it is printed differently), the ceiling
            // with whichever of them is plain.
            var floor = new MeshBag();
            RoomKit.Floor(env, floor);
            PatternSpec floorPattern = env.Island == IslandKind.None ? preset.PlayPattern : preset.FloorPattern;
            Add("Shell Floor", floor, Materials.Room(RoomSurface.ShellFloor, dip, floorPattern, env.GroundY));

            var walls = new MeshBag();
            MeshBag back = preset.BackWallPattern.Equals(preset.WallPattern) ? walls : new MeshBag();
            RoomKit.Walls(env, walls, back);
            MeshBag ceiling = preset.WallPattern.Pattern == RoomPattern.None ? walls : preset.BackWallPattern.Pattern == RoomPattern.None ? back : new MeshBag();
            RoomKit.Ceiling(env, ceiling);
            Add("Shell Walls", walls, Materials.Room(RoomSurface.ShellWall, dip, preset.WallPattern, env.GroundY));
            if (back != walls) Add("Shell Back Wall", back, Materials.Room(RoomSurface.ShellWall, dip, preset.BackWallPattern, env.GroundY));
            if (ceiling != walls && ceiling != back) Add("Shell Ceiling", ceiling, Materials.Room(RoomSurface.ShellWall, dip, PatternSpec.None, env.GroundY));

            // The island: the play surface, printed with the preset's play pattern.
            var island = new MeshBag();
            RoomKit.Island(env, island);
            Add("Island", island, Materials.Room(IslandRecipe(env)));

            // Trim and furniture.
            var bags = new RoomBags();
            RoomKit.Trim(env, bags.Trim);
            FurnitureKit.Build(env, bags);
            RoomKit.HullShadows(env, bags.Shadow);
            Add("Trim", bags.Trim, Materials.Room(RoomSurface.Trim, dip));
            Add("Furniture", bags.Furniture, Materials.Room(RoomSurface.Furniture, dip));
            Add("Cardboard", bags.Cardboard, Materials.Room(RoomRecipe.For(RoomSurface.Furniture, dip, PatternSpec.Corrugated).With(r => r.Name = dip.Name + " Cardboard")));
            Color glow = Palette.Lin(Palette.WarmWhite) * bags.GlowGain;
            glow.a = 1f;
            Add("Glow", bags.Glow, Materials.Emissive(ToyRecipe.Lamp, Palette.WarmWhite, glow));
            Color deep = Palette.Lin(dip.Deep);
            deep.a = 1f;
            Add("Hull Shadows", bags.Shadow, Materials.Flat(new FlatRecipe { Name = "Hull Shadow", Color = deep, Blend = FlatBlend.Multiply }));

            // The sky outside the window: its colours are in the vertices.
            var sky = new MeshBag();
            RoomKit.Sky(env, sky);
            Add("Sky", sky, Materials.Flat(new FlatRecipe { Name = "Sky", Color = Color.white }));

            BuildShaft(env);
            BuildMilliseconds = (float)watch.Elapsed.TotalMilliseconds;
        }

        /// <summary>The material recipe of a room's island: furniture tones (mid on top, deep at the edge) with the play pattern.</summary>
        public static RoomRecipe IslandRecipe(EnvironmentDescriptor env)
        {
            Dip dip = env.Preset.Dip;
            return RoomRecipe.For(RoomSurface.Furniture, dip, env.Preset.PlayPattern).With(r =>
            {
                r.Name = dip.Name + " " + env.Island;
                // A bench or a shelf runs into the walls like a floor does; a rug lies free.
                r.Corner = env.Island != IslandKind.Rug;
            });
        }

        Renderer Add(string name, MeshBag bag, Material material)
        {
            if (bag == null || bag.Empty || material == null) return null;
            Mesh mesh = bag.Build("Room " + name);
            meshes.Add(mesh);
            var holder = new GameObject(name) { hideFlags = HideFlags.DontSave };
            holder.transform.SetParent(root.transform, false);
            holder.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = holder.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            // The room only receives: its own shadows would be a second, coarser story about the same light.
            Quiet(renderer, ShadowCastingMode.Off);
            renderers.Add(renderer);
            Triangles += bag.TriangleCount;
            return renderer;
        }

        static void Quiet(Renderer renderer, ShadowCastingMode shadows)
        {
            renderer.shadowCastingMode = shadows;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
        }

        // ---- Light shaft and dust (Medium and High) -----------------------------------------------------------

        void BuildShaft(EnvironmentDescriptor env)
        {
            if (shaftObject != null)
            {
                MeshFilter filter = shaftObject.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    Triangles -= MeshUtil.TriangleCount(filter.sharedMesh);
                    meshes.Remove(filter.sharedMesh);
                    MeshKit.Release(filter.sharedMesh);
                }
                renderers.Remove(shaftObject.GetComponent<Renderer>());
                Sim.Destroy(shaftObject);
            }
            if (motesObject != null) Sim.Destroy(motesObject);
            shaftObject = null;
            motesObject = null;
            motes = null;
            if (root == null || context.Quality == QualityTier.Low) return;

            var shaft = new MeshBag();
            RoomKit.Shaft(env, shaft);
            Color light = Palette.Lin(env.Preset.PatchColor);
            light.a = RoomKit.ShaftAlpha;
            Renderer renderer = Add("Light Shaft", shaft, Materials.Flat(new FlatRecipe { Name = "Light Shaft", Color = light, Blend = FlatBlend.Additive }));
            shaftObject = renderer != null ? renderer.gameObject : null;

            CreateMotes(env, context.Quality == QualityTier.High ? MotesHigh : MotesMedium);
        }

        // Snowflake-sized next to the player, which is a second scale cue. They hang where the shaft
        // reaches the play area - further up the beam nobody could see them.
        void CreateMotes(EnvironmentDescriptor env, int count)
        {
            Vector3 s = env.SunDirection;
            motesObject = new GameObject("Dust Motes") { hideFlags = HideFlags.DontSave };
            motesObject.transform.SetParent(root.transform, false);
            motesObject.transform.SetPositionAndRotation(env.Focus + s * 16f, Quaternion.LookRotation(s, Vector3.up));
            motes = motesObject.AddComponent<ParticleSystem>();
            motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            motes.useAutoRandomSeed = false;
            motes.randomSeed = 20261003;

            const float lifetime = 18f;
            ParticleSystem.MainModule main = motes.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 10f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.8f, lifetime * 1.2f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(MoteMinSize, MoteMaxSize);
            main.gravityModifier = 0f;
            Color paper = Palette.Paper;
            paper.a = 0.8f;
            main.startColor = paper;

            ParticleSystem.EmissionModule emission = motes.emission;
            emission.enabled = true;
            emission.rateOverTime = count / lifetime;

            // A slab of the beam: as wide as the window, as high as the window looks from the sun, 30 long.
            ParticleSystem.ShapeModule shape = motes.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(EnvironmentDescriptor.WindowWidth * 0.85f, EnvironmentDescriptor.WindowHeight * Mathf.Cos(env.SunElevation * Mathf.Deg2Rad) * 0.85f, 30f);

            ParticleSystem.VelocityOverLifetimeModule drift = motes.velocityOverLifetime;
            drift.enabled = true;
            drift.space = ParticleSystemSimulationSpace.World;
            drift.x = new ParticleSystem.MinMaxCurve(-MoteDrift, MoteDrift);
            drift.y = new ParticleSystem.MinMaxCurve(-MoteDrift * 0.5f, MoteDrift * 0.5f);
            drift.z = new ParticleSystem.MinMaxCurve(-MoteDrift, MoteDrift);

            ParticleSystem.ColorOverLifetimeModule fade = motes.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var renderer = motesObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = Materials.Flat(new FlatRecipe { Name = "Mote", Color = Color.white, Shape = FlatShape.SoftDisc, Soft = 0.5f, Blend = FlatBlend.Alpha });
            Quiet(renderer, ShadowCastingMode.Off);
            renderer.receiveShadows = false;

            // The beam is full of them from the first frame.
            motes.Simulate(lifetime * 0.5f, true, true, false);
            motes.Simulate(lifetime * 0.5f, true, false, false);
        }

        // ---- The player's shadow ------------------------------------------------------------------------------

        void CreatePlayerShadow()
        {
            if (!context.HasGraphics) return;
            shadowObject = new GameObject("Player Shadow") { hideFlags = HideFlags.DontSave };
            shadowObject.transform.SetParent(context.Root, false);
            shadowObject.AddComponent<MeshFilter>().sharedMesh = MeshKit.Cached("Room/Player Figure", FigureMesh);
            MeshRenderer renderer = shadowObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Materials.Room(RoomRecipe.Solid(Palette.Paper));
            Quiet(renderer, ShadowCastingMode.ShadowsOnly);
            PlayerShadow = renderer;
        }

        /// <summary>
        /// The action figure the player is, 1.7 tall with its feet on the origin: head, torso, two arms, two
        /// legs. Under 300 triangles; only its shadow is ever drawn.
        /// </summary>
        public static Mesh FigureMesh()
        {
            Mesh head = MeshKit.Sphere(0.14f, 8, 6);
            Mesh torso = MeshKit.RoundedBox(new Vector3(0.44f, 0.62f, 0.24f), 0.08f, 1);
            Mesh arm = MeshKit.Box(new Vector3(0.11f, 0.6f, 0.13f));
            Mesh leg = MeshKit.Box(new Vector3(0.17f, 0.8f, 0.19f));
            Mesh figure = MeshKit.Merge("Player Figure", new[]
            {
                new MeshPart(head, new Vector3(0f, 1.56f, 0f)),
                new MeshPart(torso, new Vector3(0f, 1.1f, 0f)),
                new MeshPart(arm, new Vector3(-0.3f, 1.08f, 0f), Quaternion.Euler(0f, 0f, -6f)),
                new MeshPart(arm, new Vector3(0.3f, 1.08f, 0f), Quaternion.Euler(0f, 0f, 6f)),
                new MeshPart(leg, new Vector3(-0.115f, 0.4f, 0f)),
                new MeshPart(leg, new Vector3(0.115f, 0.4f, 0f)),
            });
            MeshKit.Release(head);
            MeshKit.Release(torso);
            MeshKit.Release(arm);
            MeshKit.Release(leg);
            return figure;
        }
    }
}
