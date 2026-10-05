using System.Collections.Generic;
using Toybox.Art;
using UnityEngine;

namespace Toybox.Levels
{
    /// <summary>
    /// What Level 5 paints on its blotter: the feather's outline, seen from above, and dashed lines along
    /// it. Looks only - nothing here has a collider.
    /// </summary>
    static class Level05Shapes
    {
        /// <summary>Thickness of a coat of paint.</summary>
        public const float Coat = 0.004f;

        /// <summary>
        /// The feather's vane seen from above at a scale: x across, y along the quill (the tip at +y), about
        /// the feather's middle. The curve is the one the toy's vane is cut to (ToyFactory.Feather), without
        /// its splits; a closed loop, both ends pointed.
        /// </summary>
        public static List<Vector2> FeatherOutline(float scale, int steps = 28)
        {
            var loop = new List<Vector2>();
            for (int side = 1; side >= -1; side -= 2)
            {
                // Up the right side from the foot of the vane to its tip, then down the left.
                for (int i = 0; i < steps; i++)
                {
                    float u = side > 0 ? (float)i / steps : 1f - (float)i / steps;
                    float along = Mathf.Lerp(-0.36f, 0.49f, u);
                    float width = 0.19f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.7f)), 0.75f);
                    float hull = 0.2f * Mathf.Min(1f, (0.5f - Mathf.Abs(along)) / 0.25f) * 0.96f;
                    loop.Add(new Vector2(side * Mathf.Max(0f, Mathf.Min(width, hull)), along) * scale);
                }
            }
            return loop;
        }

        /// <summary>The quill: a straight line from its bare end to the tip of the vane.</summary>
        public static List<Vector2> FeatherShaft(float scale) =>
            new List<Vector2> { new Vector2(0f, -0.5f) * scale, new Vector2(0f, 0.47f) * scale };

        /// <summary>
        /// Dashes of paint along a path given in the plane (x, y) and laid into the floor (x, z): flat strips
        /// <paramref name="stroke"/> wide, one mesh. The pattern runs on round the corners of the path.
        /// </summary>
        public static Mesh Dashed(string name, IList<Vector2> points, bool closed, float stroke, float dash, float gap)
        {
            var parts = new List<MeshPart>();
            float period = dash + gap, travelled = 0f;
            int count = points.Count, segments = closed ? count : count - 1;
            for (int i = 0; i < segments; i++)
            {
                Vector2 a = points[i], b = points[(i + 1) % count];
                float length = (b - a).magnitude;
                if (length < 1e-5f) continue;
                Vector2 along = (b - a) / length;
                Quaternion turn = Quaternion.LookRotation(new Vector3(along.x, 0f, along.y), Vector3.up);
                float at = 0f;
                while (at < length - 1e-4f)
                {
                    float phase = Mathf.Repeat(travelled + at, period);
                    if (phase < dash - 1e-4f)
                    {
                        float run = Mathf.Min(dash - phase, length - at);
                        Vector2 middle = a + along * (at + run * 0.5f);
                        // A hair longer than its share, so that a dash that turns a corner has no notch in it.
                        parts.Add(new MeshPart(MeshKit.Box(new Vector3(stroke, Coat, run + stroke * 0.25f)), new Vector3(middle.x, 0f, middle.y), turn));
                        at += run;
                    }
                    else
                    {
                        at += Mathf.Min(period - phase, length - at);
                    }
                }
                travelled += length;
            }
            Mesh merged = MeshKit.Merge(name, parts);
            foreach (MeshPart part in parts) MeshKit.Release(part.Mesh);
            return merged;
        }
    }
}
