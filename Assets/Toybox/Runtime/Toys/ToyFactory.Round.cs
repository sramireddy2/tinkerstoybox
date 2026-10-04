using System.Collections.Generic;
using Toybox.Art;
using UnityEngine;

namespace Toybox.Toys
{
    // The round toys: everything turned on a lathe, and the four spheres (apple, pebble, marble, bouncy
    // ball) that differ only in looks, density and bounce.
    public static partial class ToyFactory
    {
        /// <summary>Radius of the sphere toys at scale 1.</summary>
        public const float BallRadius = 0.5f;

        // ---------------------------------------------------------------------------------------------------
        // Thimble
        // ---------------------------------------------------------------------------------------------------

        public const float ThimbleRimRadius = 0.5f, ThimbleTopRadius = 0.49f, ThimbleHeight = 0.8f;

        /// <summary>
        /// A thimble standing on its rim, closed flat top up: rim radius 0.5, top radius 0.49, height 0.8.
        /// One convex collider (a 16-sided frustum) - it is a plug and a platform, so it is solid; the
        /// hollow underneath is a look.
        /// </summary>
        public static GameObject Thimble(Color? color = null)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.BrushedMetal, Palette.Tangerine, color);
            var kit = new ToyKit("Thimble", "Thimble");
            float half = ThimbleHeight * 0.5f;
            kit.Hull("Hull", () => ToyHull.Frustum(ThimbleRimRadius, ThimbleTopRadius, -half, half, 16));
            kit.Visual("Visual", look.Main, () =>
            {
                // From the middle of the cavity's ceiling, down the inside, round the rolled rim, up the
                // knurled wall and across the top.
                var inside = new List<Vector2>
                {
                    new Vector2(0f, 0.32f), new Vector2(0.41f, 0.32f), new Vector2(0.445f, 0.285f), new Vector2(0.458f, -0.34f),
                    new Vector2(0.47f, -0.4f), new Vector2(0.492f, -0.4f), new Vector2(0.5f, -0.385f), new Vector2(0.5f, -0.33f),
                    new Vector2(0.484f, -0.3f),
                };
                var profile = new List<Vector2>(ToyGeo.Fillet(inside, 0.012f, 2));
                profile.Add(new Vector2(ThimbleWall(-0.25f), -0.25f));
                var top = new List<Vector2> { new Vector2(ThimbleWall(0.33f), 0.33f), new Vector2(0.458f, 0.384f), new Vector2(0.425f, 0.4f), new Vector2(0f, 0.4f) };
                profile.AddRange(ToyGeo.Fillet(top, 0.012f, 2));
                return new[] { ToyKit.At(MeshKit.Lathe(profile, 24), Vector3.zero) };
            });
            // The dimples that hold the needle: rows round the upper wall and rings on the top.
            kit.Visual("Detail", look.Shade(0.5f), () =>
            {
                var parts = new List<MeshPart>();
                const int perRow = 14;
                for (int row = 0; row < 3; row++)
                {
                    float y = 0.25f - row * 0.115f;
                    for (int i = 0; i < perRow; i++)
                    {
                        float angle = Mathf.PI * 2f * (i + (row % 2) * 0.5f) / perRow;
                        var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                        parts.Add(ToyKit.Facing(ToyGeo.Dot(0.03f, 6, 0.003f), radial * (ThimbleWall(y) - 0.001f) + Vector3.up * y, radial));
                    }
                }
                parts.Add(ToyKit.At(ToyGeo.Dot(0.032f, 6, 0.003f), new Vector3(0f, half - 0.001f, 0f)));
                for (int ring = 1; ring <= 2; ring++)
                    for (int i = 0; i < ring * 6; i++)
                    {
                        float angle = Mathf.PI * 2f * (i + ring * 0.5f) / (ring * 6);
                        parts.Add(ToyKit.At(ToyGeo.Dot(0.032f, 6, 0.003f), new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (ring * 0.14f) + Vector3.up * (half - 0.001f)));
                    }
                return parts;
            });
            return kit.Finish(look);
        }

        // Radius of the thimble's wall at a height: just inside the collider.
        static float ThimbleWall(float y) => Mathf.Lerp(0.483f, 0.477f, (y + ThimbleHeight * 0.5f) / ThimbleHeight);

        // ---------------------------------------------------------------------------------------------------
        // The spheres
        // ---------------------------------------------------------------------------------------------------

        /// <summary>A glossy toy apple with a stem and a leaf. Sphere collider of radius 0.5; stem and leaf are looks.</summary>
        public static GameObject Apple(Color? color = null)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.GlossyPlastic, Palette.Cherry, color);
            var kit = new ToyKit("Apple", "Apple");
            kit.Sphere(BallRadius);
            kit.Visual("Visual", look.Main, () =>
            {
                var control = new List<Vector2>
                {
                    new Vector2(0f, -0.40f), new Vector2(0.07f, -0.455f), new Vector2(0.13f, -0.478f), new Vector2(0.22f, -0.445f),
                    new Vector2(0.33f, -0.36f), new Vector2(0.43f, -0.23f), new Vector2(0.485f, -0.06f), new Vector2(0.49f, 0.08f),
                    new Vector2(0.45f, 0.21f), new Vector2(0.37f, 0.32f), new Vector2(0.27f, 0.405f), new Vector2(0.17f, 0.445f),
                    new Vector2(0.09f, 0.42f), new Vector2(0.035f, 0.37f), new Vector2(0f, 0.345f),
                };
                List<Vector2> profile = ToyGeo.Spline(control, 2);
                // The apple stays inside its collider.
                for (int i = 0; i < profile.Count; i++)
                {
                    Vector2 p = profile[i];
                    p.x = Mathf.Max(0f, p.x);
                    if (p.magnitude > BallRadius - 0.003f) p = p.normalized * (BallRadius - 0.003f);
                    profile[i] = p;
                }
                // Level where it meets the axis, so that the two poles have one normal each (a pole whose
                // vertices lean apart is torn open by the shadow pass's normal bias).
                profile[0] = new Vector2(0f, profile[1].y);
                profile[profile.Count - 1] = new Vector2(0f, profile[profile.Count - 2].y);
                return new[] { ToyKit.At(MeshKit.Lathe(profile, 28, 80f), Vector3.zero) };
            });
            kit.Visual("Detail Stem", look.Detail(Palette.Ink, ToyRecipe.PaintedWood), () =>
            {
                var path = new List<Vector3> { new Vector3(0f, 0.33f, 0f), new Vector3(0.008f, 0.44f, 0f), new Vector3(0.03f, 0.54f, 0f), new Vector3(0.065f, 0.62f, 0f) };
                return new[] { ToyKit.At(ToyGeo.Tube(path, 0.02f, 0.02f, 6, Vector3.forward, true, t => 0.85f + 0.5f * t), Vector3.zero) };
            });
            kit.Visual("Detail Leaf", look.Detail(Palette.Paper), () =>
            {
                const int steps = 8;
                const float length = 0.3f;
                var right = new List<Vector2>();
                for (int i = 1; i < steps; i++)
                {
                    float s = (float)i / steps;
                    right.Add(new Vector2(0.075f * Mathf.Pow(Mathf.Sin(Mathf.PI * s), 0.8f) * (1.25f - 0.5f * s), length * s));
                }
                var outline = new List<Vector2> { Vector2.zero };
                outline.AddRange(right);
                outline.Add(new Vector2(0f, length));
                for (int i = right.Count - 1; i >= 0; i--) outline.Add(new Vector2(-right[i].x, right[i].y));
                // It stands up beside the stem, turned half toward the sky, so it shows from the side and from above.
                Vector3 along = new Vector3(0.66f, 0.72f, -0.2f).normalized;
                var toward = new Vector3(0.2f, 0.55f, -0.8f);
                Vector3 face = (toward - along * Vector3.Dot(toward, along)).normalized;
                return new[] { ToyKit.At(MeshKit.Extrude(outline, 0.022f, 0.008f), new Vector3(0.012f, 0.42f, 0f), Quaternion.LookRotation(face, along)) };
            });
            return kit.Finish(look);
        }

        /// <summary>Matte, hard, no squash: what a painted pebble is made of. Rubber's numbers with a knock for a landing.</summary>
        public static readonly ToyRecipe Stone = ToyRecipe.Rubber.With(r =>
        {
            r.Name = "Stone";
            r.BaseGain = 1f;
            r.Squash = 0f;
            r.Sound = LandSound.Wood;
        });

        /// <summary>A painted pebble: a lumpy ball inside a sphere collider of radius 0.5.</summary>
        public static GameObject Pebble(Color? color = null)
        {
            ToyLook look = ToyLook.Of(Stone, Palette.Lemon, color);
            var kit = new ToyKit("Pebble", "Pebble");
            kit.Sphere(BallRadius);
            kit.Visual("Visual", look.Main, () =>
            {
                Mesh mesh = MeshKit.Sphere(PebbleRadius, 28, 18);
                ToyGeo.Displace(mesh, (p, n) => PebbleLump(n));
                return new[] { ToyKit.At(mesh, Vector3.zero) };
            });
            // Flecks, as stone has them.
            kit.Visual("Detail", look.Detail(Palette.Ink), () =>
            {
                var parts = new List<MeshPart>();
                uint random = (uint)ToyGeo.Seed("flecks");
                for (int i = 0; i < 34; i++)
                {
                    float height = ToyGeo.Range(ref random, -0.95f, 0.95f), angle = ToyGeo.Range(ref random, 0f, Mathf.PI * 2f);
                    float ring = Mathf.Sqrt(1f - height * height);
                    var n = new Vector3(Mathf.Cos(angle) * ring, height, Mathf.Sin(angle) * ring);
                    float size = ToyGeo.Range(ref random, 0.012f, 0.03f);
                    parts.Add(ToyKit.Facing(ToyGeo.Dot(size, 5, 0.005f), n * (PebbleRadius + PebbleLump(n) - 0.003f), n));
                }
                return parts;
            });
            return kit.Finish(look);
        }

        const float PebbleRadius = 0.452f;

        // How far the pebble's surface is pushed out or in along a direction: up to a tenth of its radius.
        static float PebbleLump(Vector3 n)
        {
            int seed = ToyGeo.Seed("pebble");
            return PebbleRadius * (0.068f * ToyGeo.Noise(n * 1.35f + new Vector3(3.1f, 7.7f, 1.3f), seed) + 0.028f * ToyGeo.Noise(n * 3.1f, seed + 3));
        }

        /// <summary>A glass marble with a cat's eye inside. Sphere collider of radius 0.5.</summary>
        public static GameObject Marble(Color? color = null)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.Glass, Palette.Cherry, color);
            var kit = new ToyKit("Marble", "Marble");
            kit.Sphere(BallRadius);
            // The vanes are opaque and drawn before the glass around them.
            kit.Visual("Detail Swirl", look.Also(ToyRecipe.GlossyPlastic), () => new[] { ToyKit.At(MarbleSwirl(), Vector3.zero) });
            kit.Visual("Visual", look.MainSet, () => new[] { ToyKit.At(MeshKit.Sphere(BallRadius, 32, 20), Vector3.zero) });
            return kit.Finish(look);
        }

        // Three twisted vanes about the Y axis, each with two faces.
        static Mesh MarbleSwirl()
        {
            const int along = 10, across = 3;
            var b = new GeoBuilder();
            for (int vane = 0; vane < 3; vane++)
                for (int face = -1; face <= 1; face += 2)
                {
                    int first = b.Count;
                    for (int i = 0; i <= along; i++)
                    {
                        float v = -1f + 2f * i / along;
                        float angle = (vane * 120f + v * 75f) * Mathf.Deg2Rad;
                        float width = 0.27f * Mathf.Sqrt(Mathf.Max(0f, 1f - v * v));
                        var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                        var tangent = new Vector3(-radial.z, 0f, radial.x);
                        for (int k = 0; k <= across; k++)
                        {
                            float u = (float)k / across;
                            Vector3 p = radial * (width * u) + Vector3.up * (v * 0.36f) + tangent * (face * 0.014f * (1f - 0.7f * u));
                            b.Add(p, tangent * face);
                        }
                    }
                    int stride = across + 1;
                    for (int i = 0; i < along; i++)
                        for (int k = 0; k < across; k++)
                        {
                            int a = first + i * stride + k;
                            b.Quad(a, a + 1, a + stride + 1, a + stride);
                        }
                }
            return b.Build("Swirl");
        }

        /// <summary>A rubber ball with two painted bands. Sphere collider of radius 0.5.</summary>
        public static GameObject BouncyBall(Color? color = null)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.Rubber, Palette.Cherry, color);
            var kit = new ToyKit("Bouncy Ball", "BouncyBall");
            kit.Sphere(BallRadius);
            kit.Visual("Visual", look.Main, () => new[] { ToyKit.At(MeshKit.Sphere(BallRadius, 32, 20), Vector3.zero) });
            kit.Visual("Detail", look.Detail(Palette.Paper), () =>
            {
                var band = new List<Vector2>();
                for (int i = -2; i <= 2; i++)
                {
                    float angle = i * 4.5f * Mathf.Deg2Rad;
                    band.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (BallRadius + 0.0025f));
                }
                return new[]
                {
                    ToyKit.At(MeshKit.Lathe(band, 32, 80f), Vector3.zero, Quaternion.Euler(0f, 0f, 24f)),
                    ToyKit.At(MeshKit.Lathe(band, 32, 80f), Vector3.zero, Quaternion.Euler(66f, 0f, 24f)),
                };
            });
            return kit.Finish(look);
        }

        public const float BaseballRadius = 0.6f;

        /// <summary>
        /// The baseball of Level 15: a set piece (never grabbable), radius 0.6. Sphere collider; the
        /// stitches are looks. Leather in Birch, as a prop that is not the player's must be.
        /// </summary>
        public static GameObject Baseball(Color? color = null)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.PlainProp, Palette.Birch, color, false);
            var kit = new ToyKit("Baseball", "Baseball");
            kit.Sphere(BaseballRadius);
            kit.Visual("Visual", look.Main, () => new[] { ToyKit.At(MeshKit.Sphere(BaseballRadius, 32, 20), Vector3.zero) });
            kit.Visual("Detail", look.Detail(Palette.Ink), () =>
            {
                // The seam of a baseball: (a cos t + b cos 3t, a sin t - b sin 3t, 2 sqrt(ab) sin 2t) with
                // a + b = 1 lies on the unit sphere. A pair of stitches crosses it every few degrees.
                const float a = 0.76f, bb = 0.24f;
                const int count = 44;
                var parts = new List<MeshPart>();
                for (int i = 0; i < count; i++)
                {
                    float t = Mathf.PI * 2f * i / count;
                    Vector3 n = Seam(t, a, bb);
                    Vector3 tangent = (Seam(t + 0.01f, a, bb) - Seam(t - 0.01f, a, bb)).normalized;
                    Vector3 side = Vector3.Cross(n, tangent).normalized;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Vector3 on = (n + side * (s * 0.045f)).normalized * (BaseballRadius + 0.001f);
                        Vector3 along = (side * s + tangent * 0.55f).normalized;
                        Vector3 up = on.normalized;
                        along = (along - up * Vector3.Dot(along, up)).normalized;
                        parts.Add(ToyKit.At(MeshKit.Box(new Vector3(0.014f, 0.008f, 0.05f)), on, Quaternion.LookRotation(along, up)));
                    }
                }
                return parts;
            });
            return kit.Finish(look);
        }

        static Vector3 Seam(float t, float a, float b) =>
            new Vector3(a * Mathf.Cos(t) + b * Mathf.Cos(3f * t), a * Mathf.Sin(t) - b * Mathf.Sin(3f * t), 2f * Mathf.Sqrt(a * b) * Mathf.Sin(2f * t));

        // ---------------------------------------------------------------------------------------------------
        // Thread spool
        // ---------------------------------------------------------------------------------------------------

        public const float ThreadSpoolRadius = 0.55f, ThreadSpoolHeight = 1f;

        /// <summary>
        /// A spool of thread standing on end (axis Y): two flanges, the thread between them and a hole
        /// through the middle. One convex collider, a 16-sided prism - the hole is a look, and the flat top
        /// is something to stand on. With other numbers it is the pedestal the levels stand toys on:
        /// <c>ThreadSpool(0.4f, 1.1f, grabbable: false)</c>.
        /// </summary>
        public static GameObject ThreadSpool(float radius = ThreadSpoolRadius, float height = ThreadSpoolHeight, Color? color = null, bool grabbable = true)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.PaintedWood, Palette.Tangerine, color, grabbable);
            var kit = new ToyKit("Thread Spool", MeshKit.Key("ThreadSpool", radius, height) + look.Hex + (grabbable ? "T" : "P"));
            float half = height * 0.5f, flange = Mathf.Min(0.09f * height, 0.2f * radius), slope = Mathf.Min(0.06f * height, 0.12f * radius);
            float hole = 0.2f * radius, barrel = 0.62f * radius;
            kit.Hull("Hull", () => ToyHull.Frustum(radius, radius, -half, half, 16));
            kit.Visual("Visual", look.Main, () =>
            {
                // A closed loop that never touches the axis: outside going up, back down through the hole.
                var loop = new List<Vector2>
                {
                    new Vector2(hole, -half), new Vector2(radius, -half), new Vector2(radius, -half + flange), new Vector2(barrel, -half + flange + slope),
                    new Vector2(barrel, half - flange - slope), new Vector2(radius, half - flange), new Vector2(radius, half), new Vector2(hole, half),
                };
                List<Vector2> profile = ToyGeo.Fillet(loop, Mathf.Min(radius, height) * 0.035f, 2, true);
                profile.Add(profile[0]);
                Mesh wood = MeshKit.Lathe(profile, 24);
                ToyGeo.Chip(wood, look.Color, ToyGeo.Seed("spool"), ToyGeo.Bevels.Lathe);
                return new[] { ToyKit.At(wood, Vector3.zero) };
            });
            // The thread is the toy's colour as fibre; on a pedestal it is Kraft twine.
            Material thread = grabbable ? look.Also(ToyRecipe.Felt) : look.Detail(Palette.Paper);
            kit.Visual("Thread", thread, () =>
            {
                const int turns = 12;
                float from = -half + flange + slope * 0.2f, to = half - flange - slope * 0.2f;
                var profile = new List<Vector2>();
                for (int i = 0; i <= turns * 2; i++)
                {
                    float s = (float)i / (turns * 2);
                    float bulge = 1f - (2f * s - 1f) * (2f * s - 1f);
                    profile.Add(new Vector2(radius * (0.87f + 0.03f * bulge + (i % 2 == 1 ? 0.014f : 0f)), Mathf.Lerp(from, to, s)));
                }
                return new[] { ToyKit.At(MeshKit.Lathe(profile, 24, 80f), Vector3.zero) };
            });
            kit.Visual("Detail", look.Detail(Palette.Paper), () =>
            {
                // A round paper label on either end. Running the profile inward makes the ring face up.
                float lift = Mathf.Min(radius, height) * 0.004f;
                var up = new List<Vector2> { new Vector2(0.8f * radius, half + lift), new Vector2(0.34f * radius, half + lift) };
                var down = new List<Vector2> { new Vector2(0.34f * radius, -half - lift), new Vector2(0.8f * radius, -half - lift) };
                return new[] { ToyKit.At(MeshKit.Lathe(up, 24), Vector3.zero), ToyKit.At(MeshKit.Lathe(down, 24), Vector3.zero) };
            });
            return kit.Finish(look);
        }

        // ---------------------------------------------------------------------------------------------------
        // Marker
        // ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// A fat marker lying on its side along Z, cap toward +Z: the pivot of the rulers in Levels 7 and
        /// 15, so by default a set piece (pass it to AddStatic). One convex collider, a 16-sided prism of the
        /// full radius, so that a ruler rests on its top line.
        /// </summary>
        public static GameObject Marker(float radius = 0.4f, float length = 2f, Color? color = null, bool grabbable = false)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.GlossyPlastic, Palette.Lagoon, color, grabbable);
            var kit = new ToyKit("Marker", MeshKit.Key("Marker", radius, length) + look.Hex + (grabbable ? "T" : "P"));
            float half = length * 0.5f, cap = half - 0.34f * length;
            kit.Hull("Hull", () =>
            {
                List<Vector3> points = ToyHull.Frustum(radius, radius, -half, half, 16);
                for (int i = 0; i < points.Count; i++) points[i] = Flat * points[i];
                return points;
            });
            kit.Visual("Visual", look.Main, () =>
            {
                var barrel = new List<Vector2>
                {
                    new Vector2(0f, -half), new Vector2(0.72f * radius, -half), new Vector2(0.97f * radius, -half + 0.22f * radius),
                    new Vector2(0.97f * radius, cap), new Vector2(0.6f * radius, cap + 0.02f * length),
                };
                return new[] { ToyKit.At(MeshKit.Lathe(ToyGeo.Fillet(barrel, radius * 0.08f, 2), 24), Vector3.zero, Flat) };
            });
            kit.Visual("Detail Cap", look.Detail(Palette.Ink), () =>
            {
                var profile = new List<Vector2> { new Vector2(0.9f * radius, cap - 0.01f * length), new Vector2(radius, cap + 0.02f * length) };
                // Grip ridges along the cap, then its closed end.
                const int ridges = 5;
                float from = cap + 0.05f * length, to = half - 0.3f * radius;
                for (int i = 0; i <= ridges * 2; i++)
                    profile.Add(new Vector2(radius * (i % 2 == 1 ? 0.95f : 1f), Mathf.Lerp(from, to, (float)i / (ridges * 2))));
                profile.Add(new Vector2(0.86f * radius, half));
                profile.Add(new Vector2(0f, half));
                return new[] { ToyKit.At(MeshKit.Lathe(ToyGeo.Fillet(profile, radius * 0.05f, 1), 24), Vector3.zero, Flat) };
            });
            kit.Visual("Detail Label", look.Detail(Palette.Paper), () =>
            {
                var band = new List<Vector2> { new Vector2(0.978f * radius, -half + 0.3f * length), new Vector2(0.978f * radius, cap - 0.06f * length) };
                return new[] { ToyKit.At(MeshKit.Lathe(band, 24), Vector3.zero, Flat) };
            });
            return kit.Finish(look, Sphere(new Vector3(0f, 0f, -length * 0.27f), radius * 1.3f), Sphere(new Vector3(0f, 0f, length * 0.27f), radius * 1.3f));
        }
    }
}
