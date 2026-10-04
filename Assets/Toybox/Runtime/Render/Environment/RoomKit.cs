using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Render
{
    /// <summary>
    /// The geometry of the playroom itself (ART_BIBLE 6.3), built in world space from the environment
    /// descriptor the simulation solved: shell, island, skirting and wall plates, the window with its
    /// frame, the sky card outside it, the light shaft, and the hull shadows of the furniture. Everything
    /// lands in a <see cref="MeshBag"/> per material; nothing here makes an object or a collider.
    /// </summary>
    public static class RoomKit
    {
        public const float SkirtingHeight = 3.5f, SkirtingDepth = 0.5f;
        /// <summary>The sky card stands this far outside the window wall.</summary>
        public const float SkyDistance = 60f;
        /// <summary>
        /// HDR gain of the day sky, of its clouds, and of the moon. The art bible's 1.8 for the sky was
        /// written for a tonemapping curve; the game shows the palette without one (see PostLook.Tonemap),
        /// and there 1.8 clips the whole card, gradient and clouds, to white. So the gradient is shown as
        /// authored (with the preset's +0.2 EV its brightest channels just pass the bloom threshold of 1.1)
        /// and the clouds, which are what should glow, are 1.3.
        /// </summary>
        public const float SkyGain = 1f, CloudGain = 1.3f, MoonGain = 2.5f;
        /// <summary>Thickness of the wall the window sits in (the shell's collider boxes are this thick).</summary>
        public const float WallThickness = 4f;
        /// <summary>Hull shadows: darkest value, width of the feathered ring, height above the floor.</summary>
        public const float HullAlpha = 0.35f, HullFeather = 6f, HullLift = 0.12f;
        /// <summary>The shaft's additive strength at the window, and what is left of it where it lands.</summary>
        public const float ShaftAlpha = 0.06f, ShaftFloorFade = 0.2f;

        static readonly Color White = Color.white;

        // ---- Shell ---------------------------------------------------------------------------------------

        /// <summary>The shell's real floor: one quad, facing up.</summary>
        public static void Floor(EnvironmentDescriptor env, MeshBag floor)
        {
            Vector3 min = env.ShellMin, max = env.ShellMax;
            floor.Quad(new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z), Vector3.up);
        }

        /// <summary>
        /// The four walls, facing inward, with the opening for the window cut out of its wall. The back
        /// (+Z) wall goes into its own bag: two presets print it differently.
        /// </summary>
        public static void Walls(EnvironmentDescriptor env, MeshBag walls, MeshBag backWall)
        {
            Vector3 min = env.ShellMin, max = env.ShellMax;
            Vector3 window = env.WindowCenter;
            Vector2 half = env.WindowHalfSize;
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side < 0 ? min.x : max.x;
                var normal = new Vector3(-side, 0f, 0f);
                if (side == env.WindowSide)
                {
                    float z0 = window.z - half.x, z1 = window.z + half.x, y0 = window.y - half.y, y1 = window.y + half.y;
                    WallX(walls, x, normal, min.z, z0, min.y, max.y);
                    WallX(walls, x, normal, z1, max.z, min.y, max.y);
                    WallX(walls, x, normal, z0, z1, min.y, y0);
                    WallX(walls, x, normal, z0, z1, y1, max.y);
                }
                else
                {
                    WallX(walls, x, normal, min.z, max.z, min.y, max.y);
                }
            }
            WallZ(walls, min.z, Vector3.forward, min.x, max.x, min.y, max.y);
            WallZ(backWall, max.z, Vector3.back, min.x, max.x, min.y, max.y);
        }

        /// <summary>The ceiling, facing down. Height haze dissolves it.</summary>
        public static void Ceiling(EnvironmentDescriptor env, MeshBag ceiling)
        {
            Vector3 min = env.ShellMin, max = env.ShellMax;
            ceiling.Quad(new Vector3(min.x, max.y, min.z), new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z), Vector3.down);
        }

        static void WallX(MeshBag bag, float x, Vector3 normal, float z0, float z1, float y0, float y1)
        {
            if (z1 - z0 < 1e-3f || y1 - y0 < 1e-3f) return;
            bag.Quad(new Vector3(x, y0, z0), new Vector3(x, y0, z1), new Vector3(x, y1, z1), new Vector3(x, y1, z0), normal);
        }

        static void WallZ(MeshBag bag, float z, Vector3 normal, float x0, float x1, float y0, float y1)
        {
            if (x1 - x0 < 1e-3f || y1 - y0 < 1e-3f) return;
            bag.Quad(new Vector3(x0, y0, z), new Vector3(x1, y0, z), new Vector3(x1, y1, z), new Vector3(x0, y1, z), normal);
        }

        /// <summary>The rug, the bench top or the shelf: exactly the island's collider box, its edges eased.</summary>
        public static void Island(EnvironmentDescriptor env, MeshBag island)
        {
            if (env.Island == IslandKind.None) return;
            Bounds box = env.IslandBounds;
            float bevel = env.Island == IslandKind.Rug ? 0.2f : 0.4f;
            Mesh slab = MeshKit.RoundedBox(box.size, bevel, 1);
            island.Add(slab, Matrix4x4.Translate(box.center));
            MeshKit.Release(slab);
        }

        // ---- Trim: Paper --------------------------------------------------------------------------------

        /// <summary>Skirting boards, the window's frame and casing, outlet and switch plates.</summary>
        public static void Trim(EnvironmentDescriptor env, MeshBag trim)
        {
            Skirting(env, trim);
            WindowFrame(env, trim);
            Plates(env, trim);
        }

        // The skirting runs round the room at the real floor (3.5 high, 0.5 deep: twice the player's
        // height), leaving out the doorways. On a bench or a shelf a low upstand runs along the back wall.
        static void Skirting(EnvironmentDescriptor env, MeshBag trim)
        {
            Vector3 min = env.ShellMin, max = env.ShellMax;
            float y = min.y;

            // Doorways on the window wall, as intervals of z.
            var gaps = new List<Vector2>();
            foreach (EnvironmentPiece piece in env.Pieces)
            {
                if (piece.Kind != FurnitureKind.Door) continue;
                float halfWidth = env.Boxes[piece.FirstBox].Size.x * 0.5f + 2.2f;
                gaps.Add(new Vector2(piece.Position.z - halfWidth, piece.Position.z + halfWidth));
            }
            gaps.Sort((a, b) => a.x.CompareTo(b.x));

            float windowX = env.WindowSide < 0 ? min.x : max.x;
            float from = min.z;
            foreach (Vector2 gap in gaps)
            {
                SkirtingRun(trim, new Vector3(windowX, y, from), new Vector3(windowX, y, gap.x), env.WindowNormal, SkirtingHeight, SkirtingDepth);
                from = gap.y;
            }
            SkirtingRun(trim, new Vector3(windowX, y, from), new Vector3(windowX, y, max.z), env.WindowNormal, SkirtingHeight, SkirtingDepth);

            float farX = env.WindowSide < 0 ? max.x : min.x;
            SkirtingRun(trim, new Vector3(farX, y, min.z), new Vector3(farX, y, max.z), -env.WindowNormal, SkirtingHeight, SkirtingDepth);
            SkirtingRun(trim, new Vector3(min.x, y, min.z), new Vector3(max.x, y, min.z), Vector3.forward, SkirtingHeight, SkirtingDepth);
            SkirtingRun(trim, new Vector3(min.x, y, max.z), new Vector3(max.x, y, max.z), Vector3.back, SkirtingHeight, SkirtingDepth);

            if (env.Island == IslandKind.Bench || env.Island == IslandKind.Shelf)
                SkirtingRun(trim, new Vector3(min.x, env.GroundY, max.z), new Vector3(max.x, env.GroundY, max.z), Vector3.back, 1.2f, 0.35f);
        }

        // One straight run along a wall: a flat front, a chamfer, a top. inward is the wall's normal.
        static void SkirtingRun(MeshBag trim, Vector3 a, Vector3 b, Vector3 inward, float height, float depth)
        {
            if ((b - a).sqrMagnitude < 0.01f) return;
            Vector3 up = Vector3.up;
            float shoulder = height * 0.9f, lip = depth * 0.5f;
            Vector3 front = inward * depth, back = inward * lip;
            trim.Quad(a + front, b + front, b + front + up * shoulder, a + front + up * shoulder, inward);
            Vector3 chamfer = (inward * (height - shoulder) + up * (depth - lip)).normalized;
            trim.Quad(a + front + up * shoulder, b + front + up * shoulder, b + back + up * height, a + back + up * height, chamfer);
            trim.Quad(a + back + up * height, b + back + up * height, b + up * height, a + up * height, up);
            // The two ends, for where a run stops at a doorway.
            Vector3 along = (b - a).normalized;
            trim.Quad(a, a + front, a + front + up * shoulder, a + up * shoulder, -along);
            trim.Quad(b, b + front, b + front + up * shoulder, b + up * shoulder, along);
        }

        // The frame sits in the opening with its face flush with the pane the simulation put there; its
        // bars are exactly what the window patch's mullions are (ART_BIBLE 3.3: 0.04 of the half opening
        // to each side). A casing and a sill go round it on the wall.
        static void WindowFrame(EnvironmentDescriptor env, MeshBag trim)
        {
            Vector3 centre = env.WindowCenter, n = env.WindowNormal, u = env.WindowTangent, v = Vector3.up;
            Vector2 half = env.WindowHalfSize;
            Quaternion rotation = Quaternion.LookRotation(n, v);   // local +Z into the room; local X along the wall
            Vector3 right = rotation * Vector3.right;
            // Local x may run against the wall's tangent; the frame is symmetric, so it does not matter.
            float barU = EnvironmentDescriptor.WindowMullion * half.x, barV = EnvironmentDescriptor.WindowMullion * half.y;
            const float depth = 1.3f;
            float z = EnvironmentSolver.PaneInset - depth * 0.5f;

            void Bar(float x, float y, float width, float height, float thickness, float offset) =>
                trim.Box(centre + right * x + v * y + n * offset, new Vector3(width, height, thickness), rotation, White);

            // Around the opening and across it.
            Bar(-half.x + barU * 0.5f, 0f, barU, half.y * 2f, depth, z);
            Bar(half.x - barU * 0.5f, 0f, barU, half.y * 2f, depth, z);
            Bar(0f, half.y - barV * 0.5f, half.x * 2f, barV, depth, z);
            Bar(0f, -half.y + barV * 0.5f, half.x * 2f, barV, depth, z);
            Bar(0f, 0f, barU * 2f, half.y * 2f, depth, z);
            Bar(0f, 0f, half.x * 2f, barV * 2f, depth, z);

            // The reveal: the wall's thickness inside the opening.
            Vector3 outward = -n * WallThickness;
            Vector3 c00 = centre - u * half.x - v * half.y, c10 = centre + u * half.x - v * half.y;
            Vector3 c01 = centre - u * half.x + v * half.y, c11 = centre + u * half.x + v * half.y;
            trim.Quad(c00, c10, c10 + outward, c00 + outward, v);
            trim.Quad(c01, c11, c11 + outward, c01 + outward, -v);
            trim.Quad(c00, c01, c01 + outward, c00 + outward, u);
            trim.Quad(c10, c11, c11 + outward, c10 + outward, -u);

            // Casing on the wall and a sill under it.
            const float casing = 3f, proud = 0.5f;
            Bar(-half.x - casing * 0.5f, casing * 0.5f, casing, half.y * 2f + casing, proud, proud * 0.5f);
            Bar(half.x + casing * 0.5f, casing * 0.5f, casing, half.y * 2f + casing, proud, proud * 0.5f);
            Bar(0f, half.y + casing * 0.5f, half.x * 2f, casing, proud, proud * 0.5f);
            Bar(0f, -half.y - 0.75f, half.x * 2f + casing * 2f + 2f, 1.5f, 1.4f, 0.7f);
        }

        // Scale landmarks: an outlet plate 10 above the floor on the window wall (where the night-light
        // plugs in) and on the wall across, a light switch at 37 beside the door's place.
        static void Plates(EnvironmentDescriptor env, MeshBag trim)
        {
            Vector3 min = env.ShellMin, max = env.ShellMax;
            Vector3 window = env.WindowCenter;
            float half = env.WindowHalfSize.x;
            float windowX = env.WindowSide < 0 ? min.x : max.x, farX = env.WindowSide < 0 ? max.x : min.x;
            Quaternion fromWindow = Quaternion.LookRotation(env.WindowNormal, Vector3.up), fromFar = Quaternion.LookRotation(-env.WindowNormal, Vector3.up);
            Quaternion fromBack = Quaternion.LookRotation(Vector3.back, Vector3.up);

            float outletZ = Mathf.Clamp(window.z - half - 30f, min.z + 10f, max.z - 10f);
            Outlet(trim, new Vector3(windowX, env.FloorY + 10f, outletZ), fromWindow);
            Outlet(trim, new Vector3(farX, env.FloorY + 10f, Mathf.Clamp(env.Focus.z + 20f, min.z + 10f, max.z - 10f)), fromFar);
            if (!env.Preset.Elevated)
            {
                float switchZ = Mathf.Clamp(window.z + half + 22f, min.z + 10f, max.z - 10f);
                Switch(trim, new Vector3(windowX, env.FloorY + 37f, switchZ), fromWindow);
            }
            else
            {
                // Above a bench or a shelf the wall behind it carries the plates.
                Outlet(trim, new Vector3(Mathf.Clamp(env.Focus.x - 75f, min.x + 10f, max.x - 10f), env.GroundY + 10f, max.z), fromBack);
                Switch(trim, new Vector3(Mathf.Clamp(env.Focus.x + 80f, min.x + 10f, max.x - 10f), env.GroundY + 37f, max.z), fromBack);
            }
        }

        static void Outlet(MeshBag trim, Vector3 at, Quaternion rotation)
        {
            Vector3 n = rotation * Vector3.forward, right = rotation * Vector3.right;
            trim.Box(at + n * 0.1f, new Vector3(2.7f, 4f, 0.2f), rotation, White);
            var slot = new Color(0.22f, 0.2f, 0.28f, 1f);
            trim.Box(at + n * 0.2f - right * 0.45f + Vector3.up * 0.3f, new Vector3(0.24f, 0.9f, 0.06f), rotation, slot);
            trim.Box(at + n * 0.2f + right * 0.45f + Vector3.up * 0.3f, new Vector3(0.24f, 0.9f, 0.06f), rotation, slot);
            trim.Box(at + n * 0.2f - Vector3.up * 0.9f, new Vector3(0.34f, 0.34f, 0.06f), rotation, slot);
        }

        static void Switch(MeshBag trim, Vector3 at, Quaternion rotation)
        {
            Vector3 n = rotation * Vector3.forward;
            trim.Box(at + n * 0.1f, new Vector3(2.7f, 4f, 0.2f), rotation, White);
            trim.Box(at + n * 0.3f, new Vector3(0.9f, 1.7f, 0.3f), rotation * Quaternion.Euler(12f, 0f, 0f), new Color(0.9f, 0.9f, 0.9f, 1f));
        }

        // ---- Sky -----------------------------------------------------------------------------------------

        /// <summary>
        /// The card outside the window: a vertical gradient with three clouds by day, a moon by night.
        /// Colours are baked into the vertices, linear and HDR, for a white Toybox/Flat material.
        /// </summary>
        public static void Sky(EnvironmentDescriptor env, MeshBag sky)
        {
            bool night = env.Preset.Night;
            float gain = night ? 1f : SkyGain;
            Color top = Hdr(night ? Palette.NightSkyTop : Palette.SkyTop, gain), bottom = Hdr(night ? Palette.NightSkyBottom : Palette.SkyBottom, gain);

            Vector3 n = env.WindowNormal, u = env.WindowTangent;
            Vector3 centre = env.WindowCenter - n * SkyDistance;
            centre.y = env.GroundY;
            const float halfWidth = 520f;
            // The window shows the card between about 50 and 250 above the play plane from where the level
            // is: that is where the gradient runs.
            float[] heights = { -260f, 40f, 240f, 620f };
            Color[] tones = { bottom, bottom, top, top };
            for (int row = 0; row + 1 < heights.Length; row++)
            {
                Vector3 low = centre + Vector3.up * heights[row], high = centre + Vector3.up * heights[row + 1];
                sky.Quad(low - u * halfWidth, low + u * halfWidth, high + u * halfWidth, high - u * halfWidth, n, tones[row], tones[row], tones[row + 1], tones[row + 1]);
            }

            // Where the ray from the level toward the sun passes the card: the moon hangs there, and the
            // clouds drift around that spot, so both are in the window as seen from the play area.
            Vector3 s = env.SunDirection;
            float along = Vector3.Dot(centre - env.Focus, -n) / Mathf.Max(1e-3f, Vector3.Dot(s, -n));
            Vector3 spot = env.Focus + s * along;

            if (night)
            {
                Mesh disc = MeshKit.Cached("Room/Moon", () => MeshKit.Cylinder(9f, 0.2f, 32));
                sky.Add(disc, Matrix4x4.TRS(spot + n * 3f, Quaternion.FromToRotation(Vector3.up, n), Vector3.one), Hdr(Palette.Paper, MoonGain));
                return;
            }

            Mesh blob = MeshKit.Cached("Room/Cloud Blob", () => MeshKit.Sphere(1f, 16, 8));
            Color paper = Hdr(Palette.Paper, CloudGain);
            Quaternion facing = Quaternion.LookRotation(n, Vector3.up);   // local X along the wall, Z toward the room
            void Cloud(float du, float dv, float size)
            {
                Vector3 at = spot + n * 6f + u * du + Vector3.up * dv;
                sky.Add(blob, Matrix4x4.TRS(at, facing, new Vector3(30f, 8.5f, 4f) * size), paper);
                sky.Add(blob, Matrix4x4.TRS(at + (u * -13f + Vector3.up * 5f) * size, facing, new Vector3(15f, 8f, 4f) * size), paper);
                sky.Add(blob, Matrix4x4.TRS(at + (u * 10f + Vector3.up * 4f) * size, facing, new Vector3(18f, 7f, 4f) * size), paper);
            }
            Cloud(-62f, -48f, 1.15f);
            Cloud(48f, -18f, 0.9f);
            Cloud(-10f, 42f, 1.3f);
        }

        // ---- Light shaft ----------------------------------------------------------------------------------

        /// <summary>
        /// The window extruded along the sun down to the play plane: four additive faces, strongest at the
        /// window. Vertex alpha carries the fade; the material's colour carries the 0.06.
        /// </summary>
        public static void Shaft(EnvironmentDescriptor env, MeshBag shaft)
        {
            Vector3 s = env.SunDirection;
            if (s.y < 0.05f) return;
            Vector3 centre = env.WindowCenter, u = env.WindowTangent, v = Vector3.up;
            Vector2 half = env.WindowHalfSize;
            var near = new Vector3[4];
            var far = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                float su = i == 0 || i == 3 ? -1f : 1f, sv = i < 2 ? -1f : 1f;
                near[i] = centre + u * (half.x * su) + v * (half.y * sv);
                far[i] = near[i] - s * ((near[i].y - env.GroundY) / s.y);
            }
            Color strong = White, weak = new Color(1f, 1f, 1f, ShaftFloorFade);
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Vector3 normal = Vector3.Cross(near[j] - near[i], far[i] - near[i]).normalized;
                shaft.Quad(near[i], near[j], far[j], far[i], normal, strong, strong, weak, weak);
            }
        }

        // ---- Hull shadows ---------------------------------------------------------------------------------

        /// <summary>
        /// One soft shadow per standing piece of furniture, instead of a place in the shadow map
        /// (ART_BIBLE 5.3): the corners of its boxes projected along the sun onto the surface it stands
        /// on, their convex hull, and a feathered ring around it. Vertex alpha is the darkness.
        /// </summary>
        public static void HullShadows(EnvironmentDescriptor env, MeshBag shadow)
        {
            Vector3 s = env.SunDirection;
            if (s.y < 0.05f) return;
            var points = new List<Vector2>();
            foreach (EnvironmentPiece piece in env.Pieces)
            {
                if (!CastsHull(piece.Kind)) continue;
                // A piece's origin is on the surface it stands on (a chair's seat floats above it on its legs).
                float ground = piece.Position.y;
                // A lamp is a foot, a pole and a shade with air between them: one hull around all of it
                // would be a slab. Its parts get a hull each.
                bool apart = piece.Kind == FurnitureKind.FloorLamp || piece.Kind == FurnitureKind.DeskLamp;
                points.Clear();
                for (int b = 0; b < piece.BoxCount; b++)
                {
                    EnvironmentBox box = env.Boxes[piece.FirstBox + b];
                    Vector3 h = box.Size * 0.5f;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var local = new Vector3((corner & 1) == 0 ? -h.x : h.x, (corner & 2) == 0 ? -h.y : h.y, (corner & 4) == 0 ? -h.z : h.z);
                        Vector3 p = box.Center + box.Rotation * local;
                        p -= s * (Mathf.Max(0f, p.y - ground) / s.y);
                        points.Add(new Vector2(p.x, p.z));
                    }
                    if (!apart) continue;
                    Hull(shadow, ConvexHull(points), ground + HullLift);
                    points.Clear();
                }
                if (!apart) Hull(shadow, ConvexHull(points), ground + HullLift);
            }
        }

        /// <summary>
        /// True for the pieces that get a hull shadow: whatever stands on the floor or the island, and the
        /// tops of chairs and tables. Their legs get none - the top's shadow covers them.
        /// </summary>
        public static bool CastsHull(FurnitureKind kind)
        {
            if (kind == FurnitureKind.Chair || kind == FurnitureKind.Table) return true;
            return FurnitureKit.Stands(kind) && kind != FurnitureKind.ChairLegs && kind != FurnitureKind.TableLegs;
        }

        static void Hull(MeshBag shadow, List<Vector2> hull, float y)
        {
            int count = hull.Count;
            if (count < 3) return;
            Vector2 middle = Vector2.zero;
            foreach (Vector2 p in hull) middle += p;
            middle /= count;

            // The feathered ring is 6 wide around anything broad; a pole's shadow gets one in proportion.
            float narrowest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Vector2 a = hull[i], normal = Outward(a, hull[(i + 1) % count]);
                float reach = 0f;
                for (int k = 0; k < count; k++) reach = Mathf.Max(reach, Vector2.Dot(a - hull[k], normal));
                narrowest = Mathf.Min(narrowest, reach);
            }
            float feather = Mathf.Clamp(narrowest * 0.5f, 0.75f, HullFeather);

            // The hull runs counter-clockwise; outward is to the right of each edge.
            var outer = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                Vector2 before = hull[(i + count - 1) % count], here = hull[i], after = hull[(i + 1) % count];
                Vector2 n0 = Outward(before, here), n1 = Outward(here, after);
                float denominator = 1f + Vector2.Dot(n0, n1);
                Vector2 offset = denominator > 0.2f ? (n0 + n1) / denominator : (n0 + n1).normalized * 2f;
                outer[i] = here + Vector2.ClampMagnitude(offset, 2f) * feather;
            }

            var dark = new Color(1f, 1f, 1f, HullAlpha);
            var clear = new Color(1f, 1f, 1f, 0f);
            Vector3 centre = new Vector3(middle.x, y, middle.y);
            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                Vector3 a = new Vector3(hull[i].x, y, hull[i].y), b = new Vector3(hull[j].x, y, hull[j].y);
                shadow.Tri(centre, a, b, Vector3.up, dark, dark, dark);
                shadow.Quad(a, b, new Vector3(outer[j].x, y, outer[j].y), new Vector3(outer[i].x, y, outer[i].y), Vector3.up, dark, dark, clear, clear);
            }
        }

        static Vector2 Outward(Vector2 from, Vector2 to)
        {
            Vector2 along = (to - from).normalized;
            return new Vector2(along.y, -along.x);
        }

        /// <summary>Andrew's monotone chain: the convex hull of the points, counter-clockwise, without collinear points.</summary>
        public static List<Vector2> ConvexHull(List<Vector2> points)
        {
            var sorted = new List<Vector2>(points);
            sorted.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var hull = new List<Vector2>();
            if (sorted.Count < 3) return hull;
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int k = 0; k < sorted.Count; k++)
                {
                    Vector2 p = pass == 0 ? sorted[k] : sorted[sorted.Count - 1 - k];
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 1e-5f) hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        /// <summary>An sRGB colour as a linear vertex colour times a gain.</summary>
        public static Color Hdr(Color srgb, float gain)
        {
            Color linear = Palette.Lin(srgb);
            return new Color(linear.r * gain, linear.g * gain, linear.b * gain, 1f);
        }
    }
}
