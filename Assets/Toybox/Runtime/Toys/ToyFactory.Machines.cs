using System.Collections.Generic;
using Toybox.Art;
using UnityEngine;

namespace Toybox.Toys
{
    // The toys with moving parts: the desk fan and the train.
    public static partial class ToyFactory
    {
        // ---------------------------------------------------------------------------------------------------
        // Desk fan
        // ---------------------------------------------------------------------------------------------------

        public static readonly Vector3 DeskFanSize = new Vector3(1f, 1.2f, 0.7f);
        public const float DeskFanGuardRadius = 0.5f, DeskFanGuardDepth = 0.3f;
        /// <summary>Middle of the guard (the hub) in the fan's own space: 0.7 above the origin.</summary>
        public static readonly Vector3 DeskFanHub = new Vector3(0f, 0.7f, 0f);
        /// <summary>Name of the child that carries the blades; turn it about its own Z axis to spin them.</summary>
        public const string DeskFanBlades = "Blades";

        /// <summary>
        /// A desk fan, 1.0 x 1.2 x 0.7, blowing along its +Z: a base 0.7 x 0.1 x 0.7, a stem and a round
        /// guard of radius 0.5 and depth 0.3 centred 0.7 up. The origin is the middle of the base's
        /// underside. Two boxes (base, stem) and a convex 16-sided disc (guard). The blades are a child of
        /// their own (<see cref="DeskFanBlades"/>). Not grabbable it is the machine of Level 5: an Ink body
        /// with Steel wires and blades - add it with AddStatic and give it its scale.
        /// </summary>
        public static GameObject DeskFan(Color? color = null, bool grabbable = true)
        {
            ToyLook look = grabbable ? ToyLook.Of(ToyRecipe.GlossyPlastic, Palette.Lagoon, color) : ToyLook.Gadget;
            Material body = look.Main;
            Material wires = grabbable ? look.Detail(Palette.Paper) : Materials.Gadget(GadgetPart.Metal);
            Material blade = grabbable ? body : wires;
            var kit = new ToyKit("Desk Fan", "DeskFan");
            Vector3 hub = DeskFanHub;

            kit.Box(new Vector3(0.7f, 0.1f, 0.7f), new Vector3(0f, 0.05f, 0f));
            kit.Box(new Vector3(0.12f, 0.25f, 0.12f), new Vector3(0f, 0.225f, 0f));
            kit.Hull("Guard", () =>
            {
                List<Vector3> points = ToyHull.Frustum(DeskFanGuardRadius, DeskFanGuardRadius, -DeskFanGuardDepth * 0.5f, DeskFanGuardDepth * 0.5f, 16);
                for (int i = 0; i < points.Count; i++) points[i] = Flat * points[i] + hub;
                return points;
            });

            kit.Visual("Visual", body, () =>
            {
                var motor = new List<Vector2>
                {
                    new Vector2(0f, -0.15f), new Vector2(0.1f, -0.15f), new Vector2(0.16f, -0.1f), new Vector2(0.16f, -0.02f), new Vector2(0.09f, 0.01f), new Vector2(0f, 0.01f),
                };
                var rim = new List<Vector2> { new Vector2(0.468f, -0.04f), new Vector2(0.5f, -0.04f), new Vector2(0.5f, 0.04f), new Vector2(0.468f, 0.04f) };
                List<Vector2> band = ToyGeo.Fillet(rim, 0.01f, 1, true);
                band.Add(band[0]);
                return new[]
                {
                    ToyKit.At(MeshKit.RoundedBox(new Vector3(0.7f, 0.1f, 0.7f), 0.035f), new Vector3(0f, 0.05f, 0f)),
                    ToyKit.At(MeshKit.Cylinder(0.055f, 0.3f, 12, 0.01f), new Vector3(0f, 0.23f, 0f)),
                    ToyKit.At(MeshKit.Lathe(ToyGeo.Fillet(motor, 0.03f, 1), 16), hub, Flat),
                    ToyKit.At(MeshKit.Lathe(band, 28, 80f), hub, Flat),
                };
            });
            kit.Visual("Detail Wires", wires, () =>
            {
                var parts = new List<MeshPart>();
                // Front: wires from the badge out to the rim, bowed forward. Back: the same, shallower.
                var front = new[] { new Vector2(0.07f, 0.146f), new Vector2(0.2f, 0.14f), new Vector2(0.34f, 0.116f), new Vector2(0.44f, 0.07f), new Vector2(0.486f, 0.03f) };
                var back = new[] { new Vector2(0.15f, -0.085f), new Vector2(0.3f, -0.12f), new Vector2(0.42f, -0.085f), new Vector2(0.486f, -0.03f) };
                Wires(parts, front, 14, hub, 0f);
                Wires(parts, back, 10, hub, 0.2f);
                parts.Add(ToyKit.At(MeshKit.Lathe(Ring(0.27f, 0.13f, 0.0075f), 28, 80f), hub, Flat));
                parts.Add(ToyKit.Facing(ToyGeo.Dot(0.078f, 16, 0.014f), hub + new Vector3(0f, 0f, 0.139f), Vector3.forward));
                return parts;
            });
            Transform blades = kit.Child(DeskFanBlades, hub + new Vector3(0f, 0f, 0.03f));
            kit.Visual("Visual", blade, () =>
            {
                var parts = new List<MeshPart> { ToyKit.At(MeshKit.Cylinder(0.062f, 0.09f, 12, 0.012f), Vector3.zero, Flat) };
                for (int i = 0; i < 4; i++) parts.Add(ToyKit.At(FanBlade(), Vector3.zero, Quaternion.Euler(0f, 0f, i * 90f + 20f)));
                return parts;
            }, blades);
            return kit.Finish(look);
        }

        // A small circle as a lathe profile: a wire ring of the given radius at height z.
        static List<Vector2> Ring(float radius, float z, float wire)
        {
            var profile = new List<Vector2>();
            for (int i = 0; i <= 6; i++)
            {
                float angle = Mathf.PI * 2f * i / 6f;
                profile.Add(new Vector2(radius + Mathf.Cos(angle) * wire, z + Mathf.Sin(angle) * wire));
            }
            return profile;
        }

        // Wires in radial planes about the hub: each point is (distance from the axis, z).
        static void Wires(List<MeshPart> parts, Vector2[] shape, int count, Vector3 hub, float phase)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = Mathf.PI * 2f * (i + phase) / count;
                var radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                var path = new List<Vector3>();
                foreach (Vector2 p in shape) path.Add(hub + radial * p.x + Vector3.forward * p.y);
                parts.Add(ToyKit.At(ToyGeo.Tube(path, 0.007f, 0.007f, 4, Vector3.Cross(radial, Vector3.forward)), Vector3.zero));
            }
        }

        // One blade along +X about the origin, pitched, with two faces.
        static Mesh FanBlade()
        {
            const int along = 6, across = 2;
            const float pitch = 18f * Mathf.Deg2Rad;
            var chord = new Vector3(0f, Mathf.Cos(pitch), Mathf.Sin(pitch));
            Vector3 normal = Vector3.Cross(Vector3.right, chord).normalized;
            var b = new GeoBuilder();
            for (int face = -1; face <= 1; face += 2)
            {
                int first = b.Count;
                for (int i = 0; i <= along; i++)
                {
                    float s = (float)i / along;
                    float radius = Mathf.Lerp(0.05f, 0.42f, s);
                    float width = 0.2f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Lerp(0.12f, 0.93f, s)), 0.6f);
                    for (int k = 0; k <= across; k++)
                    {
                        float c = ((float)k / across - 0.5f) * width + 0.03f * s;
                        b.Add(Vector3.right * radius + chord * c + normal * (face * 0.004f), normal * face);
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
            return b.Build("Blade");
        }

        // ---------------------------------------------------------------------------------------------------
        // Train
        // ---------------------------------------------------------------------------------------------------

        public const float TrainWidth = 3f, TrainDeckThickness = 0.4f, TrainCarLength = 3.2f, TrainEngineLength = 3.9f;
        /// <summary>Height of the engine's body above its deck, and of the top of its funnel.</summary>
        public const float TrainEngineHeight = 1f, TrainFunnelHeight = 1.4f;

        static readonly Quaternion AxleX = Quaternion.Euler(0f, 0f, 90f);

        /// <summary>
        /// A flat wagon of the Level 9 train: a deck 3.0 wide (X) and 3.2 long (Z, the way it travels), 0.4
        /// thick. The origin is the middle of the deck's top, so its height is the deck's. One box; wheels
        /// and couplings are looks. A set piece: wooden in Birch (or the tone passed), never grabbable.
        /// </summary>
        public static GameObject TrainCar(Color? color = null)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.PlainProp, Palette.Birch, color, false);
            var kit = new ToyKit("Train Car", "TrainCar" + look.Hex);
            var deck = new Vector3(TrainWidth, TrainDeckThickness, TrainCarLength);
            Vector3 deckCentre = new Vector3(0f, -TrainDeckThickness * 0.5f, 0f);
            kit.Box(deck, deckCentre);
            kit.Visual("Visual", look.Main, () => new[] { ToyKit.At(MeshKit.RoundedBox(deck, 0.03f), deckCentre) });
            kit.Visual("Detail Wheels", Materials.Gadget(GadgetPart.Metal), () =>
            {
                var parts = new List<MeshPart>();
                TrainWheels(parts, new[] { -0.95f, 0.95f });
                for (int end = -1; end <= 1; end += 2)
                    parts.Add(ToyKit.At(MeshKit.RoundedBox(new Vector3(0.3f, 0.14f, 0.24f), 0.03f, 1), new Vector3(0f, -0.26f, end * (TrainCarLength * 0.5f + 0.05f))));
                return parts;
            });
            // The bed the plank sticks to.
            kit.Visual("Detail Bed", look.Detail(Palette.Paper), () => new[]
            {
                ToyKit.At(MeshKit.RoundedBox(new Vector3(TrainWidth - 0.5f, 0.016f, TrainCarLength - 0.5f), 0.006f, 1), Vector3.zero),
            });
            return kit.Finish(look);
        }

        /// <summary>
        /// The engine of the Level 9 train: a deck 3.0 x 0.4 x 3.9, a boiler and cab 1.0 tall and a funnel
        /// to 1.4 above the deck, facing +Z. The origin is the middle of the deck's top. Three boxes (deck,
        /// body, funnel). Headlamp and cab windows glow warm white. A set piece, never grabbable.
        /// </summary>
        public static GameObject TrainEngine(Color? color = null)
        {
            ToyLook look = ToyLook.Of(ToyRecipe.PlainProp, Palette.Birch, color, false);
            var kit = new ToyKit("Train Engine", "TrainEngine" + look.Hex);
            var deck = new Vector3(TrainWidth, TrainDeckThickness, TrainEngineLength);
            Vector3 deckCentre = new Vector3(0f, -TrainDeckThickness * 0.5f, 0f);
            kit.Box(deck, deckCentre);
            kit.Box(new Vector3(1.7f, TrainEngineHeight, 3.3f), new Vector3(0f, TrainEngineHeight * 0.5f, -0.1f));
            kit.Box(new Vector3(0.44f, TrainFunnelHeight - TrainEngineHeight, 0.44f), new Vector3(0f, (TrainFunnelHeight + TrainEngineHeight) * 0.5f, 1f));
            kit.Visual("Visual", look.Main, () => new[]
            {
                ToyKit.At(MeshKit.RoundedBox(deck, 0.03f), deckCentre),
                ToyKit.At(MeshKit.Cylinder(0.5f, 2.15f, 22, 0.05f), new Vector3(0f, 0.5f, 0.475f), Flat),
                ToyKit.At(MeshKit.RoundedBox(new Vector3(1.6f, 0.92f, 1.1f), 0.04f), new Vector3(0f, 0.46f, -1.2f)),
                ToyKit.At(MeshKit.RoundedBox(new Vector3(1.7f, 0.09f, 1.15f), 0.03f, 1), new Vector3(0f, 0.955f, -1.175f)),
            });
            kit.Visual("Detail Wheels", Materials.Gadget(GadgetPart.Metal), () =>
            {
                var parts = new List<MeshPart>();
                TrainWheels(parts, new[] { -1.3f, 0f, 1.3f });
                // Funnel, two boiler bands, a dome and the rear coupling.
                parts.Add(ToyKit.At(MeshKit.Cylinder(0.17f, 0.36f, 12, 0.015f, 1), new Vector3(0f, 1.1f, 1f)));
                parts.Add(ToyKit.At(MeshKit.Cylinder(0.22f, 0.12f, 12, 0.02f, 1), new Vector3(0f, 1.34f, 1f)));
                parts.Add(ToyKit.At(MeshKit.Cylinder(0.512f, 0.08f, 22, 0.012f, 1), new Vector3(0f, 0.5f, 0.1f), Flat));
                parts.Add(ToyKit.At(MeshKit.Cylinder(0.512f, 0.08f, 22, 0.012f, 1), new Vector3(0f, 0.5f, 1.2f), Flat));
                parts.Add(ToyKit.At(MeshKit.Sphere(0.15f, 12, 6), new Vector3(0f, 0.97f, 0.25f)));
                parts.Add(ToyKit.At(MeshKit.RoundedBox(new Vector3(0.3f, 0.14f, 0.24f), 0.03f, 1), new Vector3(0f, -0.26f, -(TrainEngineLength * 0.5f + 0.05f))));
                return parts;
            });
            Color glow = Palette.Lin(Palette.WarmWhite) * 2f;
            kit.Visual("Detail Lamps", Materials.Emissive(ToyRecipe.Lamp, Palette.WarmWhite, glow), () => new[]
            {
                ToyKit.Facing(ToyGeo.Dot(0.17f, 16, 0.03f), new Vector3(0f, 0.5f, 1.55f), Vector3.forward),
                ToyKit.At(MeshKit.RoundedBox(new Vector3(0.014f, 0.3f, 0.55f), 0.005f, 1), new Vector3(0.8f, 0.6f, -1.2f)),
                ToyKit.At(MeshKit.RoundedBox(new Vector3(0.014f, 0.3f, 0.55f), 0.005f, 1), new Vector3(-0.8f, 0.6f, -1.2f)),
            });
            return kit.Finish(look);
        }

        // A pair of wheels on either side for every axle, hanging under the deck.
        static void TrainWheels(List<MeshPart> parts, float[] axles)
        {
            foreach (float z in axles)
                for (int side = -1; side <= 1; side += 2)
                    parts.Add(ToyKit.At(MeshKit.Cylinder(0.34f, 0.16f, 14, 0.03f, 1), new Vector3(side * (TrainWidth * 0.5f - 0.3f), -TrainDeckThickness - 0.1f, z), AxleX));
        }
    }
}
