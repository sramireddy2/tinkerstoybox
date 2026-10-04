using System.Collections.Generic;
using UnityEngine;

namespace Toybox.Engine
{
    /// <summary>
    /// "The sun lands on the level" (ART_BIBLE 6.2): from a preset and the bounds of what a level built,
    /// works out the sun, the window it shines through, where the shell stands, and where the backdrop
    /// furniture finds room. Pure arithmetic on its arguments - the same level always gets the same room.
    /// </summary>
    public static class EnvironmentSolver
    {
        /// <summary>Furniture keeps at least this far from the footprint of the level's static geometry.</summary>
        public const float KeepOut = 12f;
        /// <summary>The window wall stands at least this far beyond the level's bounds.</summary>
        public const float WallMargin = 20f;
        /// <summary>The sun never stands lower than this: longer shadows stop reading as size.</summary>
        public const float MinElevation = 38f;
        /// <summary>The window's centre above the play plane: sill 30 + half of 105.</summary>
        public const float WindowCenter = EnvironmentDescriptor.WindowSill + EnvironmentDescriptor.WindowHeight * 0.5f;

        /// <summary>The window opening keeps this much wall between itself and the room's corners.</summary>
        public const float WindowFrame = 3f;

        const float ShellThickness = 4f;
        const float PaneThickness = 1f;
        /// <summary>
        /// The window pane stands this far proud of the wall's inner face, so that a ray at the window meets
        /// the pane and not the wall behind it.
        /// </summary>
        public const float PaneInset = 0.05f;
        const float RugWidth = 160f, RugDepth = 110f, RugThickness = 0.5f;
        const float BenchThickness = 4f, ShelfThickness = 3f;

        /// <summary>
        /// The bounds of a level's static geometry: every enabled, non-trigger collider on the Default layer
        /// under the level's root that has no Rigidbody. A level without any gets a point at its spawn.
        /// </summary>
        public static Bounds StaticBounds(Transform levelRoot, Vector3 fallback)
        {
            bool any = false;
            Bounds bounds = default;
            if (levelRoot != null)
            {
                foreach (Collider collider in levelRoot.GetComponentsInChildren<Collider>())
                {
                    if (!collider.enabled || collider.isTrigger) continue;
                    if (collider.gameObject.layer != Layers.Default || collider.attachedRigidbody != null) continue;
                    if (!any) bounds = collider.bounds;
                    else bounds.Encapsulate(collider.bounds);
                    any = true;
                }
            }
            return any ? bounds : new Bounds(fallback, Vector3.zero);
        }

        /// <summary>
        /// The sun of a preset on its n-th repeat visit: 2 degrees lower each time (never below 38) and
        /// swung by 6 degrees, in whichever direction keeps it within 30 degrees of the window wall's
        /// outward direction and 30 to 75 degrees off the travel axis (+Z). It keeps swinging the way it
        /// went last; when neither direction is allowed it stays.
        /// </summary>
        public static void Sun(EnvironmentPreset preset, int visit, out float elevation, out float azimuth)
        {
            elevation = preset.SunElevation;
            azimuth = Mathf.Repeat(preset.SunAzimuth, 360f);
            float wall = preset.WindowSide < 0 ? 270f : 90f;
            int direction = 1;
            for (int v = 0; v < visit; v++)
            {
                elevation = Mathf.Max(MinElevation, elevation - 2f);
                if (SunAllowed(azimuth + 6f * direction, wall)) azimuth += 6f * direction;
                else if (SunAllowed(azimuth - 6f * direction, wall))
                {
                    direction = -direction;
                    azimuth += 6f * direction;
                }
                azimuth = Mathf.Repeat(azimuth, 360f);
            }
        }

        static bool SunAllowed(float azimuth, float wall)
        {
            const float slack = 0.01f;
            float delta = Mathf.Abs(Mathf.DeltaAngle(azimuth, wall));
            float offAxis = Mathf.Abs(Mathf.DeltaAngle(azimuth, 0f));
            return delta <= 30f + slack && offAxis >= 30f - slack && offAxis <= 75f + slack;
        }

        /// <summary>Unit vector toward a sun at this elevation and azimuth (degrees; azimuth clockwise from +Z seen from above).</summary>
        public static Vector3 SunDirection(float elevation, float azimuth)
        {
            float e = elevation * Mathf.Deg2Rad, a = azimuth * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
        }

        /// <summary>The solve. <paramref name="bounds"/> is the level's static AABB, <paramref name="groundY"/> its play plane.</summary>
        public static EnvironmentDescriptor Solve(EnvironmentPreset preset, Bounds bounds, float groundY, int visit = 0)
        {
            preset ??= EnvironmentPreset.None;
            var d = new EnvironmentDescriptor { Preset = preset, Visit = Mathf.Max(0, visit), LevelBounds = bounds, GroundY = groundY };

            // 1. Focus: the centre of the bounds on the play plane.
            Vector3 focus = new Vector3(bounds.center.x, groundY, bounds.center.z);
            d.Focus = focus;

            // 2. Sun.
            Sun(preset, d.Visit, out float elevation, out float azimuth);
            Vector3 sun = SunDirection(elevation, azimuth);
            d.SunElevation = elevation;
            d.SunAzimuth = azimuth;
            d.SunDirection = sun;
            Vector3 right = Vector3.Cross(Vector3.up, sun).normalized;
            d.GlintRight = right;
            d.GlintUp = Vector3.Cross(sun, right).normalized;

            // 3. Window wall, and the angle between the sun and its outward direction.
            int side = preset.WindowSide < 0 ? -1 : 1;
            float delta = Mathf.Abs(Mathf.DeltaAngle(azimuth, side < 0 ? 270f : 90f));
            d.WindowSide = side;
            d.Delta = delta;
            d.WindowNormal = new Vector3(-side, 0f, 0f);
            d.WindowTangent = Vector3.forward;

            // 4. Wall distance: the ray from F toward the sun passes the wall at the height of the window's centre.
            float cosDelta = Mathf.Cos(delta * Mathf.Deg2Rad), tanElevation = Mathf.Tan(elevation * Mathf.Deg2Rad);
            float wanted = WindowCenter * cosDelta / tanElevation;
            float least = bounds.extents.x + WallMargin;
            float distance = Mathf.Max(wanted, least);
            d.WallDistance = distance;
            d.WallClamped = wanted < least;
            // If the level pushed the wall out, the window goes up with the ray - up to a point.
            float height = Mathf.Min(distance * tanElevation / cosDelta, EnvironmentDescriptor.MaxWindowCenter);
            d.WindowCenterHeight = height;

            // 6. Shell (before the window: the window has to fit into its wall).
            float minX = side < 0 ? focus.x - distance : focus.x + distance - EnvironmentDescriptor.ShellSize;
            float maxZ = preset.Elevated ? bounds.max.z + preset.BackWallGap : focus.z + EnvironmentDescriptor.ShellSize * 0.5f;
            d.ShellMin = new Vector3(minX, groundY - preset.FloorDrop, maxZ - EnvironmentDescriptor.ShellSize);
            d.ShellMax = new Vector3(minX + EnvironmentDescriptor.ShellSize, groundY + EnvironmentDescriptor.ShellHeight, maxZ);

            // 5. Window: where the ray F + s t meets the wall plane.
            float lateral = distance * sun.z / Mathf.Max(1e-4f, Mathf.Abs(sun.x));
            float halfWidth = EnvironmentDescriptor.WindowWidth * 0.5f;
            // The opening has to lie in its wall with room for a frame. Where the back wall of an elevated
            // preset stands too near (a short level), the window slides along the wall and the patch with it.
            float wantedZ = focus.z + lateral;
            float windowZ = Mathf.Clamp(wantedZ, d.ShellMin.z + halfWidth + WindowFrame, d.ShellMax.z - halfWidth - WindowFrame);
            d.WindowShift = windowZ - wantedZ;
            d.WindowCenter = new Vector3(focus.x + side * distance, groundY + height, windowZ);

            if (!preset.HasRoom) return d;

            SolveIsland(d);
            AddShell(d);
            new Furniture(d).PlaceAll();
            return d;
        }

        static void SolveIsland(EnvironmentDescriptor d)
        {
            EnvironmentPreset preset = d.Preset;
            Bounds level = d.LevelBounds;
            d.Island = preset.Island;
            float minX, maxX, minZ, maxZ, thickness;
            switch (preset.Island)
            {
                case IslandKind.Rug:
                    float halfX = Mathf.Max(RugWidth, level.size.x + 40f) * 0.5f, halfZ = Mathf.Max(RugDepth, level.size.z + 40f) * 0.5f;
                    minX = Mathf.Max(d.Focus.x - halfX, d.ShellMin.x);
                    maxX = Mathf.Min(d.Focus.x + halfX, d.ShellMax.x);
                    minZ = Mathf.Max(d.Focus.z - halfZ, d.ShellMin.z);
                    maxZ = Mathf.Min(d.Focus.z + halfZ, d.ShellMax.z);
                    thickness = RugThickness;
                    break;
                case IslandKind.Bench:
                case IslandKind.Shelf:
                    // Built in along the back wall, from side wall to side wall, deep enough for the level.
                    bool bench = preset.Island == IslandKind.Bench;
                    float depth = Mathf.Max(bench ? 100f : 60f, level.size.z + preset.BackWallGap + (bench ? 40f : 30f));
                    minX = d.ShellMin.x;
                    maxX = d.ShellMax.x;
                    maxZ = d.ShellMax.z;
                    minZ = maxZ - depth;
                    thickness = bench ? BenchThickness : ShelfThickness;
                    break;
                default:
                    d.IslandBounds = new Bounds(d.Focus, Vector3.zero);
                    return;
            }
            var size = new Vector3(maxX - minX, thickness, maxZ - minZ);
            d.IslandBounds = new Bounds(new Vector3((minX + maxX) * 0.5f, d.GroundY - thickness * 0.5f, (minZ + maxZ) * 0.5f), size);
        }

        static void AddShell(EnvironmentDescriptor d)
        {
            Vector3 min = d.ShellMin, max = d.ShellMax;
            Vector3 center = (min + max) * 0.5f, size = max - min;
            const float t = ShellThickness;
            float wide = size.x + 2f * t, deep = size.z + 2f * t;
            Add(d, EnvironmentBoxKind.Floor, "Shell Floor", new Vector3(center.x, min.y - t * 0.5f, center.z), new Vector3(wide, t, deep));
            Add(d, EnvironmentBoxKind.Ceiling, "Shell Ceiling", new Vector3(center.x, max.y + t * 0.5f, center.z), new Vector3(wide, t, deep));
            Add(d, EnvironmentBoxKind.WallNegX, "Shell Wall -X", new Vector3(min.x - t * 0.5f, center.y, center.z), new Vector3(t, size.y, deep));
            Add(d, EnvironmentBoxKind.WallPosX, "Shell Wall +X", new Vector3(max.x + t * 0.5f, center.y, center.z), new Vector3(t, size.y, deep));
            Add(d, EnvironmentBoxKind.WallNegZ, "Shell Wall -Z", new Vector3(center.x, center.y, min.z - t * 0.5f), new Vector3(size.x, size.y, t));
            Add(d, EnvironmentBoxKind.WallPosZ, "Shell Wall +Z", new Vector3(center.x, center.y, max.z + t * 0.5f), new Vector3(size.x, size.y, t));

            // The glass: a hair proud of the wall's inner plane, so a toy lands on the pane (almost) exactly
            // where it would land on the wall, and it is the pane that a ray at the window meets.
            Vector3 pane = d.WindowCenter - d.WindowNormal * (PaneThickness * 0.5f - PaneInset);
            Add(d, EnvironmentBoxKind.WindowPane, "Window Pane", pane, new Vector3(PaneThickness, EnvironmentDescriptor.WindowHeight, EnvironmentDescriptor.WindowWidth));

            if (d.Island != IslandKind.None)
                Add(d, EnvironmentBoxKind.Island, d.Island.ToString(), d.IslandBounds.center, d.IslandBounds.size);
        }

        static void Add(EnvironmentDescriptor d, EnvironmentBoxKind kind, string name, Vector3 center, Vector3 size) =>
            d.boxes.Add(new EnvironmentBox { Kind = kind, Name = name, Center = center, Size = size, Rotation = Quaternion.identity, Piece = -1 });

        /// <summary>Creates one BoxCollider on the Default layer per box of the descriptor, as children of <paramref name="parent"/>.</summary>
        public static void CreateColliders(EnvironmentDescriptor descriptor, Transform parent)
        {
            IReadOnlyList<EnvironmentBox> boxes = descriptor.Boxes;
            for (int i = 0; i < boxes.Count; i++)
            {
                EnvironmentBox box = boxes[i];
                var holder = new GameObject(box.Name) { hideFlags = HideFlags.DontSave, layer = Layers.Default };
                holder.transform.SetParent(parent, false);
                holder.transform.SetPositionAndRotation(box.Center, box.Rotation);
                holder.AddComponent<BoxCollider>().size = box.Size;
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Furniture: sizes from ART_BIBLE 6.3, as one to four boxes per piece in the piece's own space
        // (origin on the floor in the middle of its footprint, or on the wall it hangs from; +Z is its front).
        // ------------------------------------------------------------------------------------------------

        struct Part
        {
            public Vector3 Center, Size;
            public float Yaw;

            public Part(float x, float y, float z, float sx, float sy, float sz, float yaw = 0f)
            {
                Center = new Vector3(x, y, z);
                Size = new Vector3(sx, sy, sz);
                Yaw = yaw;
            }
        }

        static readonly Part[] Bed = { new Part(0f, 8.5f, 0f, 115f, 17f, 65f), new Part(-55.5f, 20f, 0f, 4f, 40f, 65f) };
        static readonly Part[] ToyChest = { new Part(0f, 10f, 0f, 34f, 20f, 20f) };
        static readonly Part[] Beanbag = { new Part(0f, 5f, 0f, 28f, 10f, 28f), new Part(0f, 14f, 0f, 18f, 8f, 18f) };
        static readonly Part[] Curtain = { new Part(0f, 0f, 1.5f, 25f, 105f, 3f) };
        static readonly Part[] Chair = { new Part(0f, 14.25f, 0f, 15f, 1.5f, 15f), new Part(0f, 23.5f, -6.75f, 15f, 17f, 1.5f) };
        static readonly Part[] ChairLegs =
        {
            new Part(-6.2f, 6.75f, -6.2f, 1.6f, 13.5f, 1.6f), new Part(6.2f, 6.75f, -6.2f, 1.6f, 13.5f, 1.6f),
            new Part(-6.2f, 6.75f, 6.2f, 1.6f, 13.5f, 1.6f), new Part(6.2f, 6.75f, 6.2f, 1.6f, 13.5f, 1.6f),
        };
        static readonly Part[] Table = { new Part(0f, 24f, 0f, 60f, 2f, 35f) };
        static readonly Part[] TableLegs =
        {
            new Part(-27f, 11.5f, -14.5f, 3f, 23f, 3f), new Part(27f, 11.5f, -14.5f, 3f, 23f, 3f),
            new Part(-27f, 11.5f, 14.5f, 3f, 23f, 3f), new Part(27f, 11.5f, 14.5f, 3f, 23f, 3f),
        };
        static readonly Part[] Radiator = { new Part(0f, 13f, 2.5f, 30f, 20f, 4f) };
        static readonly Part[] DoorClosed = { new Part(0f, 33.9f, 0.75f, 27f, 67f, 1.5f) };
        // Ajar by 25 degrees about the hinge at x = -13.5, swinging into the room.
        static readonly Part[] DoorAjar = { new Part(-1.265f, 33.9f, 6.455f, 27f, 67f, 1.5f, -25f) };
        static readonly Part[] Hammer = { new Part(0f, -4.5f, 1f, 4f, 36f, 2f), new Part(0f, 18f, 1.5f, 16f, 9f, 3f) };
        static readonly Part[] Screwdriver = { new Part(0f, 11.5f, 1.5f, 5f, 12f, 3f), new Part(0f, -6f, 0.8f, 1.6f, 23f, 1.6f) };
        static readonly Part[] Spanner = { new Part(0f, 0f, 0.75f, 6f, 30f, 1.5f) };
        static readonly Part[] DeskLamp = { new Part(0f, 0.75f, 0f, 9f, 1.5f, 9f), new Part(0f, 13f, 0f, 1.6f, 23f, 1.6f), new Part(0f, 27.25f, 3f, 9f, 5.5f, 12f) };
        static readonly Part[] BoxStack =
        {
            new Part(0f, 20f, 0f, 40f, 40f, 40f, 4f), new Part(3f, 54f, -2f, 28f, 28f, 28f, -9f),
            new Part(-36f, 12f, 6f, 24f, 24f, 24f, 13f), new Part(-34f, 34f, 5f, 20f, 20f, 20f, -6f),
        };
        static readonly Part[] Blanket = { new Part(0f, 7.5f, 0f, 120f, 15f, 80f) };
        static readonly Part[] FloorLamp = { new Part(0f, 1f, 0f, 12f, 2f, 12f), new Part(0f, 28.5f, 0f, 1.6f, 53f, 1.6f), new Part(0f, 60f, 0f, 16f, 12f, 16f) };
        static readonly Part[] FairyLights = { new Part(0f, 0f, 1f, 120f, 6f, 2f) };
        static readonly Part[] BookRun =
        {
            new Part(-19f, 5.5f, 4f, 14f, 11f, 8f), new Part(-7f, 4f, 4f, 10f, 8f, 8f),
            new Part(6f, 5f, 4f, 16f, 10f, 8f), new Part(20f, 4.5f, 4f, 12f, 9f, 8f),
        };
        static readonly Part[] PaperLantern = { new Part(0f, 0f, 0f, 20f, 20f, 20f) };
        static readonly Part[] NightLight = { new Part(0f, 0f, 1f, 6f, 6f, 2f) };

        sealed class Furniture
        {
            readonly EnvironmentDescriptor d;
            readonly EnvironmentPreset preset;
            readonly float keepMinX, keepMaxX, keepMinZ, keepMaxZ;
            readonly int side, far;
            readonly Vector3 focus, window;
            readonly Quaternion fromWindowWall, fromBackWall;
            readonly Dictionary<FurnitureKind, int> seen = new Dictionary<FurnitureKind, int>();

            public Furniture(EnvironmentDescriptor descriptor)
            {
                d = descriptor;
                preset = d.Preset;
                Bounds level = d.LevelBounds;
                keepMinX = level.min.x - KeepOut;
                keepMaxX = level.max.x + KeepOut;
                keepMinZ = level.min.z - KeepOut;
                keepMaxZ = level.max.z + KeepOut;
                side = d.WindowSide;
                far = -side;
                focus = d.Focus;
                window = d.WindowCenter;
                // A piece's +Z points into the room.
                fromWindowWall = Quaternion.Euler(0f, side < 0 ? 90f : -90f, 0f);
                fromBackWall = Quaternion.Euler(0f, 180f, 0f);
            }

            // An x beyond the level's keep-out, on the side away from the window, this far out.
            float FarX(float gap) => (far > 0 ? keepMaxX : keepMinX) + far * gap;

            public void PlaceAll()
            {
                foreach (FurnitureKind kind in preset.Backdrop)
                {
                    seen.TryGetValue(kind, out int index);
                    seen[kind] = index + 1;
                    Place(kind, index);
                }
            }

            void Place(FurnitureKind kind, int index)
            {
                float halfWindow = EnvironmentDescriptor.WindowWidth * 0.5f;
                float backZ = d.ShellMax.z;
                switch (kind)
                {
                    case FurnitureKind.Bed:
                        // Ahead and away from the window, its long side along the travel axis, the headboard at the far end.
                        Stand(kind, index, Bed, FarX(20f + 32.5f), focus.z + 30f, 90f);
                        break;
                    case FurnitureKind.ToyChest:
                        Stand(kind, index, ToyChest, focus.x - far * 10f, keepMaxZ + 30f + 10f, 0f);
                        break;
                    case FurnitureKind.Beanbag:
                        Stand(kind, index, Beanbag, focus.x + far * 15f, keepMinZ - 25f - 14f, 0f);
                        break;
                    case FurnitureKind.Curtain:
                        // One on each side of the window.
                        float offset = (halfWindow + 12.5f + 2f) * (index == 0 ? -1f : 1f);
                        Hang(kind, index, Curtain, new Vector3(window.x, window.y, window.z + offset), fromWindowWall);
                        break;
                    case FurnitureKind.NightLight:
                        Hang(kind, index, NightLight, new Vector3(window.x, d.FloorY + 10f, window.z - halfWindow - 30f), fromWindowWall);
                        break;
                    case FurnitureKind.Door:
                        if (preset.Night) Hang(kind, index, DoorClosed, new Vector3(window.x, d.FloorY, window.z + halfWindow + 60.5f), fromWindowWall);
                        else Hang(kind, index, DoorAjar, new Vector3(window.x, d.FloorY, window.z + halfWindow + 43.5f), fromWindowWall);
                        break;
                    case FurnitureKind.Table:
                        Stand(kind, index, Table, focus.x, keepMaxZ + 20f + 17.5f, 0f);
                        break;
                    case FurnitureKind.TableLegs:
                        Stand(kind, index, TableLegs, focus.x, keepMaxZ + 20f + 17.5f, 0f);
                        break;
                    case FurnitureKind.Chair:
                        Stand(kind, index, Chair, FarX(18f + 7.5f), focus.z - 10f, far > 0 ? -90f : 90f);
                        break;
                    case FurnitureKind.ChairLegs:
                        Stand(kind, index, ChairLegs, FarX(18f + 7.5f), focus.z - 10f, far > 0 ? -90f : 90f);
                        break;
                    case FurnitureKind.Radiator:
                        // Under the window.
                        Hang(kind, index, Radiator, new Vector3(window.x, d.FloorY, window.z), fromWindowWall);
                        break;
                    case FurnitureKind.Hammer:
                        Hang(kind, index, Hammer, new Vector3(focus.x - 40f, d.GroundY + 45f, backZ), fromBackWall);
                        break;
                    case FurnitureKind.Screwdriver:
                        Hang(kind, index, Screwdriver, new Vector3(focus.x, d.GroundY + 45f, backZ), fromBackWall);
                        break;
                    case FurnitureKind.Spanner:
                        Hang(kind, index, Spanner, new Vector3(focus.x + 40f, d.GroundY + 45f, backZ), fromBackWall);
                        break;
                    case FurnitureKind.DeskLamp:
                        Stand(kind, index, DeskLamp, FarX(20f + 4.5f), backZ - 16f, 180f);
                        break;
                    case FurnitureKind.BoxStack:
                        if (preset.Elevated) Stand(kind, index, BoxStack, FarX(60f + 50f), backZ - 32f, far > 0 ? 0f : 180f);
                        else if (index == 0) Stand(kind, index, BoxStack, FarX(15f + 55f), focus.z + 40f, far > 0 ? 180f : 0f);
                        else Stand(kind, index, BoxStack, focus.x + side * 10f, keepMaxZ + 25f + 25f, far > 0 ? 0f : 180f);
                        break;
                    case FurnitureKind.Blanket:
                        Stand(kind, index, Blanket, focus.x, keepMinZ - 20f - 40f, 0f);
                        break;
                    case FurnitureKind.FloorLamp:
                        Stand(kind, index, FloorLamp, FarX(30f), focus.z - 30f, 0f);
                        break;
                    case FurnitureKind.FairyLights:
                        Hang(kind, index, FairyLights, new Vector3(window.x, d.GroundY + 70f, window.z - halfWindow - 70f), fromWindowWall);
                        break;
                    case FurnitureKind.BookRun:
                        // Standing on the shelf against the back wall.
                        Hang(kind, index, BookRun, new Vector3(focus.x + (index == 0 ? -45f : 35f), d.GroundY, backZ), fromBackWall);
                        break;
                    case FurnitureKind.PaperLantern:
                        Hang(kind, index, PaperLantern, new Vector3(FarX(40f), d.GroundY + 120f, focus.z - 20f), Quaternion.identity);
                        break;
                }
            }

            // A piece that stands on the play surface: on the island where its middle is over it, else on the shell's floor.
            void Stand(FurnitureKind kind, int index, Part[] parts, float x, float z, float yaw)
            {
                Bounds island = d.IslandBounds;
                bool onIsland = d.Island != IslandKind.None &&
                                x >= island.min.x && x <= island.max.x && z >= island.min.z && z <= island.max.z;
                // Up on a bench or a shelf there is nothing to stand on beside it.
                if (preset.Elevated && !onIsland) return;
                float y = onIsland ? d.GroundY : d.FloorY;
                AddPiece(kind, index, parts, new Vector3(x, y, z), Quaternion.Euler(0f, yaw, 0f));
            }

            // A piece that hangs on a wall or from the ceiling: the origin is given outright.
            void Hang(FurnitureKind kind, int index, Part[] parts, Vector3 origin, Quaternion rotation) =>
                AddPiece(kind, index, parts, origin, rotation);

            void AddPiece(FurnitureKind kind, int index, Part[] parts, Vector3 origin, Quaternion rotation)
            {
                int first = d.boxes.Count;
                int piece = d.pieces.Count;
                bool fits = true;
                for (int i = 0; i < parts.Length; i++)
                {
                    var box = new EnvironmentBox
                    {
                        Kind = EnvironmentBoxKind.Furniture,
                        Name = kind + (parts.Length > 1 ? " " + (i + 1) : ""),
                        Center = origin + rotation * parts[i].Center,
                        Size = parts[i].Size,
                        Rotation = rotation * Quaternion.Euler(0f, parts[i].Yaw, 0f),
                        Piece = piece,
                    };
                    if (!Fits(box.Bounds)) fits = false;
                    d.boxes.Add(box);
                }
                if (!fits)
                {
                    // No room for it in this level's room: the piece is left out, the same way every time.
                    d.boxes.RemoveRange(first, d.boxes.Count - first);
                    return;
                }
                d.pieces.Add(new EnvironmentPiece { Kind = kind, Index = index, Position = origin, Rotation = rotation, FirstBox = first, BoxCount = parts.Length });
            }

            // Inside the shell and clear of the level's footprint.
            bool Fits(Bounds box)
            {
                const float slack = 0.01f;
                Vector3 min = box.min, max = box.max;
                if (min.x < d.ShellMin.x - slack || max.x > d.ShellMax.x + slack) return false;
                if (min.z < d.ShellMin.z - slack || max.z > d.ShellMax.z + slack) return false;
                if (min.y < d.ShellMin.y - slack || max.y > d.ShellMax.y + slack) return false;
                bool overlapsX = min.x < keepMaxX && max.x > keepMinX;
                bool overlapsZ = min.z < keepMaxZ && max.z > keepMinZ;
                return !(overlapsX && overlapsZ);
            }
        }
    }
}
