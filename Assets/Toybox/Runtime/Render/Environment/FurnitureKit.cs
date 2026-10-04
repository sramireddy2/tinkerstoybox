using System;
using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using UnityEngine;

namespace Toybox.Render
{
    /// <summary>
    /// The draw calls of a room's backdrop: every piece of furniture adds its geometry to these bags and
    /// the room turns each into one mesh (ART_BIBLE 6.3: one merged mesh for all pieces, one merged Paper
    /// trim mesh; cardboard and the things that glow are a material of their own).
    /// </summary>
    public sealed class RoomBags
    {
        /// <summary>Top = mid, side = deep.</summary>
        public readonly MeshBag Furniture = new MeshBag();
        /// <summary>Paper: skirting, frames, plates, lamp shades, the radiator.</summary>
        public readonly MeshBag Trim = new MeshBag();
        /// <summary>Furniture tones with the corrugated pattern: box stacks.</summary>
        public readonly MeshBag Cardboard = new MeshBag();
        /// <summary>Warm white emitters: fairy bulbs, the night-light, lamp bulbs, the sliver under the door.</summary>
        public readonly MeshBag Glow = new MeshBag();
        /// <summary>Hull shadows on the floor (multiply).</summary>
        public readonly MeshBag Shadow = new MeshBag();
        /// <summary>HDR gain of the glow material: 2 for bulbs, 2.2 where the night-light is.</summary>
        public float GlowGain = 2f;
    }

    /// <summary>
    /// The furniture silhouettes of ART_BIBLE 6.3, built to fill exactly the collider boxes the
    /// environment solve made for each piece (<see cref="EnvironmentDescriptor.Pieces"/>): a held toy
    /// lands on colliders, so nothing visible may stand outside them by more than a knob. Every shape is
    /// authored at the size the solve uses today and scaled to the box it is given.
    ///
    /// Pure geometry: no materials, no objects, the same room for the same descriptor every time
    /// (variations come from a local generator seeded by the piece, never from the simulation's).
    /// </summary>
    public static class FurnitureKit
    {
        /// <summary>The foot of every standing piece is darkened to this over <see cref="AoHeight"/> units.</summary>
        public const float AoDark = 0.75f, AoHeight = 6f;

        static readonly Color White = Color.white;

        /// <summary>Adds every piece of the room to the bags, with its baked contact shading.</summary>
        public static void Build(EnvironmentDescriptor env, RoomBags bags)
        {
            if (env == null || bags == null) return;
            bags.GlowGain = 2f;
            foreach (EnvironmentPiece piece in env.Pieces)
            {
                int furniture = bags.Furniture.IndexCount, trim = bags.Trim.IndexCount, cardboard = bags.Cardboard.IndexCount;
                BuildPiece(env, piece, bags);
                if (!Stands(piece.Kind)) continue;
                float baseY = BaseOf(env, piece);
                bags.Furniture.ShadeBase(furniture, baseY, AoHeight, AoDark);
                bags.Trim.ShadeBase(trim, baseY, AoHeight, AoDark);
                bags.Cardboard.ShadeBase(cardboard, baseY, AoHeight, AoDark);
            }
        }

        /// <summary>True for pieces that rest on the floor or the island (they get contact shading and a hull shadow).</summary>
        public static bool Stands(FurnitureKind kind)
        {
            switch (kind)
            {
                case FurnitureKind.Bed:
                case FurnitureKind.ToyChest:
                case FurnitureKind.Beanbag:
                case FurnitureKind.ChairLegs:
                case FurnitureKind.TableLegs:
                case FurnitureKind.Radiator:
                case FurnitureKind.DeskLamp:
                case FurnitureKind.BoxStack:
                case FurnitureKind.Blanket:
                case FurnitureKind.FloorLamp:
                case FurnitureKind.BookRun:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The height a piece stands on: the lowest point of its boxes.</summary>
        public static float BaseOf(EnvironmentDescriptor env, EnvironmentPiece piece)
        {
            float lowest = float.MaxValue;
            for (int i = 0; i < piece.BoxCount; i++) lowest = Mathf.Min(lowest, env.Boxes[piece.FirstBox + i].Bounds.min.y);
            return lowest;
        }

        /// <summary>Adds one piece's geometry to the bags (without the contact shading <see cref="Build"/> bakes afterwards).</summary>
        public static void BuildPiece(EnvironmentDescriptor env, EnvironmentPiece piece, RoomBags bags)
        {
            EnvironmentBox Box(int i) => env.Boxes[piece.FirstBox + Mathf.Min(i, piece.BoxCount - 1)];
            MeshBag f = bags.Furniture, t = bags.Trim;
            switch (piece.Kind)
            {
                case FurnitureKind.Bed:
                {
                    Frame body = Frame.Of(Box(0), 115f, 17f, 65f);
                    f.Add(Rounded(113f, 10f, 63f, 0.8f), body.At(0f, -3.5f, 0f), Tint(0.92f));
                    f.Add(Shape("Bed Blanket", BlanketOutline, 65f, 1.2f), body.At(0f, 0f, 0f));
                    if (piece.BoxCount > 1)
                    {
                        Frame head = Frame.Of(Box(1), 4f, 40f, 65f);
                        f.Add(Shape("Bed Headboard", HeadboardOutline, 4f, 0.8f), head.At(0f, 0f, 0f, Quaternion.Euler(0f, 90f, 0f)));
                    }
                    break;
                }
                case FurnitureKind.ToyChest:
                {
                    Frame chest = Frame.Of(Box(0), 34f, 20f, 20f);
                    f.Add(Rounded(33f, 15.5f, 19f, 0.8f), chest.At(0f, -2.25f, 0f));
                    f.Add(Rounded(34f, 4.5f, 20f, 1.2f, 2), chest.At(0f, 7.75f, 0f), Tint(1.08f));
                    t.Add(UnitBox, chest.At(new Vector3(0f, 4f, 9.75f), Quaternion.identity, new Vector3(3.2f, 3.2f, 0.5f)));
                    break;
                }
                case FurnitureKind.Beanbag:
                {
                    // One lathe across both boxes: a slumped sack, wide in the lower box, narrow in the upper.
                    EnvironmentBox low = Box(0), high = Box(1);
                    float r0 = low.Size.x * 0.5f, h0 = low.Size.y, r1 = high.Size.x * 0.5f, h1 = piece.BoxCount > 1 ? high.Size.y : 0f;
                    Mesh sack = MeshKit.Cached(MeshKit.Key("Room/Beanbag", r0, h0, r1, h1), () => MeshKit.Lathe(new[]
                    {
                        new Vector2(0f, 0f), new Vector2(r0 * 0.82f, 0f), new Vector2(r0 * 0.97f, h0 * 0.18f), new Vector2(r0, h0 * 0.45f),
                        new Vector2(r0 * 0.93f, h0 * 0.78f), new Vector2(Mathf.Lerp(r1, r0, 0.25f), h0 * 0.96f), new Vector2(r1, h0 + h1 * 0.08f),
                        new Vector2(r1 * 0.97f, h0 + h1 * 0.4f), new Vector2(r1 * 0.8f, h0 + h1 * 0.75f), new Vector2(r1 * 0.5f, h0 + h1 * 0.94f),
                        new Vector2(r1 * 0.2f, h0 + h1), new Vector2(0f, h0 + h1),
                    }, 20, 80f));
                    f.Add(sack, Matrix4x4.TRS(low.Center - low.Rotation * new Vector3(0f, h0 * 0.5f, 0f), low.Rotation, Vector3.one));
                    break;
                }
                case FurnitureKind.Curtain:
                {
                    Frame curtain = Frame.Of(Box(0), 25f, 105f, 3f);
                    f.Add(Shape("Curtain", CurtainOutline, 105f, 0f), curtain.At(0f, 0f, 0f, Quaternion.Euler(-90f, 0f, 0f)));
                    break;
                }
                case FurnitureKind.Chair:
                {
                    f.Add(Rounded(15f, 1.5f, 15f, 0.5f), Frame.Of(Box(0), 15f, 1.5f, 15f).At(0f, 0f, 0f));
                    if (piece.BoxCount > 1) f.Add(Rounded(15f, 17f, 1.5f, 0.5f), Frame.Of(Box(1), 15f, 17f, 1.5f).At(0f, 0f, 0f));
                    break;
                }
                case FurnitureKind.ChairLegs:
                {
                    Mesh leg = Turned("Chair Leg", 12, new Vector2(0f, -6.75f), new Vector2(0.55f, -6.75f), new Vector2(0.8f, 4.5f), new Vector2(0.8f, 6.75f), new Vector2(0f, 6.75f));
                    for (int i = 0; i < piece.BoxCount; i++) f.Add(leg, Frame.Of(Box(i), 1.6f, 13.5f, 1.6f).At(0f, 0f, 0f));
                    break;
                }
                case FurnitureKind.Table:
                {
                    f.Add(Rounded(60f, 2f, 35f, 0.6f), Frame.Of(Box(0), 60f, 2f, 35f).At(0f, 0f, 0f));
                    break;
                }
                case FurnitureKind.TableLegs:
                {
                    Mesh leg = Turned("Table Leg", 14, new Vector2(0f, -11.5f), new Vector2(1f, -11.5f), new Vector2(1.5f, 5f), new Vector2(1.2f, 7f), new Vector2(1.5f, 9f),
                        new Vector2(1.5f, 11.5f), new Vector2(0f, 11.5f));
                    for (int i = 0; i < piece.BoxCount; i++) f.Add(leg, Frame.Of(Box(i), 3f, 23f, 3f).At(0f, 0f, 0f));
                    break;
                }
                case FurnitureKind.Radiator:
                {
                    Frame radiator = Frame.Of(Box(0), 30f, 20f, 4f);
                    Mesh fin = Rounded(1.95f, 20f, 4f, 0.7f);
                    for (int i = 0; i < 12; i++) t.Add(fin, radiator.At(-15f + 2.5f * (i + 0.5f), 0f, 0f));
                    break;
                }
                case FurnitureKind.Door:
                {
                    Frame door = Frame.Of(Box(0), 27f, 67f, 1.5f);
                    f.Add(Rounded(27f, 67f, 1.5f, 0.3f), door.At(0f, 0f, 0f));
                    // Two raised panels on the face that looks into the room, and the knob at 33 above the floor.
                    f.Add(UnitBox, door.At(new Vector3(0f, 15f, 0.76f), Quaternion.identity, new Vector3(19f, 28f, 0.16f)), Tint(1.12f));
                    f.Add(UnitBox, door.At(new Vector3(0f, -16.5f, 0.76f), Quaternion.identity, new Vector3(19f, 25f, 0.16f)), Tint(1.12f));
                    t.Add(Turned("Door Knob", 12, new Vector2(0f, -1f), new Vector2(0.5f, -1f), new Vector2(0.5f, -0.2f), new Vector2(1.1f, 0.2f), new Vector2(1.1f, 0.7f),
                        new Vector2(0.6f, 1f), new Vector2(0f, 1f)), door.At(10.5f, -0.9f, 1.7f, Quaternion.Euler(90f, 0f, 0f)));

                    // The frame on the wall, and behind an open door the dark of the hallway.
                    Frame wall = Frame.Of(piece);
                    float height = Box(0).Size.y + 0.4f, half = Box(0).Size.x * 0.5f;
                    t.Add(UnitBox, wall.At(new Vector3(-half - 1.1f, height * 0.5f + 1.1f, 0.5f), Quaternion.identity, new Vector3(2.2f, height + 2.2f, 1f)));
                    t.Add(UnitBox, wall.At(new Vector3(half + 1.1f, height * 0.5f + 1.1f, 0.5f), Quaternion.identity, new Vector3(2.2f, height + 2.2f, 1f)));
                    t.Add(UnitBox, wall.At(new Vector3(0f, height + 1.1f, 0.5f), Quaternion.identity, new Vector3(half * 2f, 2.2f, 1f)));
                    bool ajar = Quaternion.Angle(Box(0).Rotation, piece.Rotation) > 1f;
                    if (ajar)
                    {
                        Vector3 normal = piece.Rotation * Vector3.forward;
                        f.Quad(wall.Point(-half, 0f, 0.08f), wall.Point(half, 0f, 0.08f), wall.Point(half, height, 0.08f), wall.Point(-half, height, 0.08f), normal, Tint(0.3f));
                    }
                    else
                    {
                        // Closed: light from the hallway under the door.
                        bags.Glow.Add(UnitBox, wall.At(new Vector3(0f, 0.2f, 0.75f), Quaternion.identity, new Vector3(half * 2f - 1f, 0.36f, 0.5f)));
                    }
                    break;
                }
                case FurnitureKind.Hammer:
                {
                    f.Add(Shape("Hammer Handle", () => new List<Vector2> { new Vector2(-2f, -18f), new Vector2(2f, -18f), new Vector2(1.4f, 18f), new Vector2(-1.4f, 18f) }, 2f, 0.5f),
                        Frame.Of(Box(0), 4f, 36f, 2f).At(0f, 0f, 0f));
                    if (piece.BoxCount > 1)
                        f.Add(Shape("Hammer Head", () => new List<Vector2>
                        {
                            new Vector2(-8f, -3f), new Vector2(-3f, -4.5f), new Vector2(3f, -4.5f), new Vector2(8f, -1.5f),
                            new Vector2(8f, 0.5f), new Vector2(3f, 4.5f), new Vector2(-3f, 4.5f), new Vector2(-8f, 3f),
                        }, 3f, 0.6f), Frame.Of(Box(1), 16f, 9f, 3f).At(0f, 0f, 0f));
                    break;
                }
                case FurnitureKind.Screwdriver:
                {
                    f.Add(Shape("Screwdriver Handle", () => new List<Vector2>
                    {
                        new Vector2(-1.5f, -6f), new Vector2(1.5f, -6f), new Vector2(2.5f, -3f), new Vector2(2.5f, 4f),
                        new Vector2(1.5f, 6f), new Vector2(-1.5f, 6f), new Vector2(-2.5f, 4f), new Vector2(-2.5f, -3f),
                    }, 3f, 0.8f), Frame.Of(Box(0), 5f, 12f, 3f).At(0f, 0f, 0f));
                    if (piece.BoxCount > 1) t.Add(Tube(0.55f, 23f, 8), Frame.Of(Box(1), 1.6f, 23f, 1.6f).At(0f, 0f, 0f));
                    break;
                }
                case FurnitureKind.Spanner:
                {
                    f.Add(Shape("Spanner", SpannerOutline, 1.5f, 0.35f), Frame.Of(Box(0), 6f, 30f, 1.5f).At(0f, 0f, 0f));
                    break;
                }
                case FurnitureKind.DeskLamp:
                {
                    f.Add(Turned("Desk Lamp Base", 18, new Vector2(0f, -0.75f), new Vector2(4.5f, -0.75f), new Vector2(4.5f, 0.1f), new Vector2(3.4f, 0.75f), new Vector2(0f, 0.75f)),
                        Frame.Of(Box(0), 9f, 1.5f, 9f).At(0f, 0f, 0f));
                    if (piece.BoxCount > 1) f.Add(Tube(0.55f, 23f, 8), Frame.Of(Box(1), 1.6f, 23f, 1.6f).At(0f, 0f, 0f));
                    if (piece.BoxCount > 2)
                    {
                        Frame head = Frame.Of(Box(2), 9f, 5.5f, 12f);
                        f.Add(Rounded(1f, 1f, 8f, 0.3f), head.At(0f, 1.6f, -2f));
                        t.Add(Turned("Desk Lamp Shade", 18, new Vector2(0f, -2.75f), new Vector2(4.4f, -2.75f), new Vector2(4.4f, -2.3f), new Vector2(1.6f, 2.75f), new Vector2(0f, 2.75f)),
                            head.At(0f, 0f, 1.5f));
                        bags.Glow.Add(Tube(3.5f, 0.2f, 16), head.At(0f, -2.86f, 1.5f));
                    }
                    break;
                }
                case FurnitureKind.BoxStack:
                {
                    for (int i = 0; i < piece.BoxCount; i++)
                    {
                        EnvironmentBox box = Box(i);
                        Vector3 size = box.Size;
                        Frame crate = Frame.Of(box, size.x, size.y, size.z);
                        bags.Cardboard.Add(Rounded(size.x, size.y, size.z, 0.6f), crate.At(0f, 0f, 0f), Tint(i % 2 == 0 ? 1f : 1.07f));
                        // Paper tape across the lid and a third of the way down two sides.
                        t.Add(UnitBox, crate.At(new Vector3(0f, size.y * 0.5f, 0f), Quaternion.identity, new Vector3(3f, 0.12f, size.z + 0.12f)));
                        t.Add(UnitBox, crate.At(new Vector3(0f, size.y / 3f, size.z * 0.5f), Quaternion.identity, new Vector3(3f, size.y / 3f, 0.12f)));
                        t.Add(UnitBox, crate.At(new Vector3(0f, size.y / 3f, -size.z * 0.5f), Quaternion.identity, new Vector3(3f, size.y / 3f, 0.12f)));
                    }
                    break;
                }
                case FurnitureKind.Blanket:
                {
                    f.Add(Shape("Blanket Heap", HeapOutline, 80f, 2.5f), Frame.Of(Box(0), 120f, 15f, 80f).At(0f, 0f, 0f));
                    break;
                }
                case FurnitureKind.FloorLamp:
                {
                    f.Add(Turned("Floor Lamp Base", 18, new Vector2(0f, -1f), new Vector2(6f, -1f), new Vector2(6f, 0f), new Vector2(4.5f, 1f), new Vector2(0f, 1f)),
                        Frame.Of(Box(0), 12f, 2f, 12f).At(0f, 0f, 0f));
                    if (piece.BoxCount > 1) f.Add(Tube(0.6f, 53f, 8), Frame.Of(Box(1), 1.6f, 53f, 1.6f).At(0f, 0f, 0f));
                    if (piece.BoxCount > 2)
                        t.Add(Turned("Floor Lamp Shade", 20, new Vector2(0f, -6f), new Vector2(8f, -6f), new Vector2(5.2f, 6f), new Vector2(0f, 6f)),
                            Frame.Of(Box(2), 16f, 12f, 16f).At(0f, 0f, 0f));
                    break;
                }
                case FurnitureKind.FairyLights:
                {
                    Frame line = Frame.Of(Box(0), 120f, 6f, 2f);
                    Mesh bulb = MeshKit.Cached("Room/Fairy Bulb", () => MeshKit.Sphere(0.5f, 8, 6));
                    const int bulbs = 24;
                    Vector3 previous = default;
                    for (int i = 0; i < bulbs; i++)
                    {
                        float x = -58f + 116f * i / (bulbs - 1);
                        float k = x / 58f;
                        var wire = new Vector3(x, 2.6f - 4.4f * (1f - k * k), 0f);
                        bags.Glow.Add(bulb, line.At(wire.x, wire.y - 0.6f, 0f));
                        if (i > 0)
                        {
                            Vector3 along = wire - previous;
                            f.Add(UnitBox, line.At((wire + previous) * 0.5f, Quaternion.FromToRotation(Vector3.right, along.normalized), new Vector3(along.magnitude, 0.14f, 0.14f)));
                        }
                        previous = wire;
                    }
                    break;
                }
                case FurnitureKind.BookRun:
                {
                    for (int i = 0; i < piece.BoxCount; i++) Books(Box(i), new Dice(piece.Index * 31 + i * 7 + 5), f, t);
                    break;
                }
                case FurnitureKind.PaperLantern:
                {
                    EnvironmentBox box = Box(0);
                    Frame lantern = Frame.Of(box, 20f, 20f, 20f);
                    t.Add(MeshKit.Cached("Room/Paper Lantern", LanternMesh), lantern.At(0f, 0f, 0f));
                    // The cord up to the ceiling.
                    float top = box.Center.y + box.Size.y * 0.5f * 0.85f;
                    float length = env.ShellMax.y - top;
                    if (length > 0.1f)
                        t.Add(UnitBox, Matrix4x4.TRS(new Vector3(box.Center.x, top + length * 0.5f, box.Center.z), Quaternion.identity, new Vector3(0.25f, length, 0.25f)), Tint(0.8f));
                    break;
                }
                case FurnitureKind.NightLight:
                {
                    bags.GlowGain = 2.2f;
                    bags.Glow.Add(Shape("Night Light", StarOutline, 1.2f, 0.3f), Frame.Of(Box(0), 6f, 6f, 2f).At(0f, 0f, 0.2f));
                    break;
                }
            }
        }

        // ---- Outlines (2D, in the XY plane of MeshKit.Extrude) -----------------------------------------------

        // The bed's cover seen from the side: flat on top, a scalloped hem half way down.
        static List<Vector2> BlanketOutline()
        {
            const float half = 57.5f, top = 8.5f, hem = -1.5f, drop = 2.6f;
            const int scallops = 8, steps = 6;
            var outline = new List<Vector2> { new Vector2(-half, top), new Vector2(-half, hem) };
            for (int s = 0; s < scallops; s++)
                for (int k = 1; k <= steps; k++)
                {
                    float u = (s + (float)k / steps) / scallops;
                    outline.Add(new Vector2(-half + 2f * half * u, hem - drop * Mathf.Sin(Mathf.PI * k / steps)));
                }
            outline.Add(new Vector2(half, top));
            return outline;
        }

        // A headboard: a slab with its two top corners rounded.
        static List<Vector2> HeadboardOutline()
        {
            const float half = 32.5f, bottom = -20f, top = 20f, radius = 9f;
            var outline = new List<Vector2> { new Vector2(-half, bottom), new Vector2(half, bottom) };
            for (int i = 0; i <= 6; i++)
            {
                float a = Mathf.PI * 0.5f * i / 6f;
                outline.Add(new Vector2(half - radius + radius * Mathf.Cos(a), top - radius + radius * Mathf.Sin(a)));
            }
            for (int i = 0; i <= 6; i++)
            {
                float a = Mathf.PI * 0.5f * (1f + i / 6f);
                outline.Add(new Vector2(-half + radius + radius * Mathf.Cos(a), top - radius + radius * Mathf.Sin(a)));
            }
            return outline;
        }

        // A curtain seen from above: a ribbon in five folds.
        static List<Vector2> CurtainOutline()
        {
            const float half = 12.5f, amplitude = 1.1f, thickness = 0.5f;
            const int steps = 30, folds = 5;
            var outline = new List<Vector2>();
            for (int i = 0; i <= steps; i++)
                outline.Add(new Vector2(-half + 2f * half * i / steps, amplitude * Mathf.Sin(Mathf.PI * 2f * folds * i / steps) + thickness * 0.5f));
            for (int i = steps; i >= 0; i--)
                outline.Add(new Vector2(-half + 2f * half * i / steps, amplitude * Mathf.Sin(Mathf.PI * 2f * folds * i / steps) - thickness * 0.5f));
            return outline;
        }

        // A blanket thrown on the floor, seen from the side: flat underneath, rumpled on top, thin at both ends.
        static List<Vector2> HeapOutline()
        {
            const float half = 60f, bottom = -7.5f;
            var outline = new List<Vector2> { new Vector2(-half, bottom), new Vector2(half, bottom) };
            for (float x = half; x >= -half - 0.01f; x -= 3f)
            {
                float edge = Mathf.SmoothStep(0f, 1f, (half - Mathf.Abs(x)) / 10f);
                outline.Add(new Vector2(x, bottom + edge * (9f + 4f * Mathf.Sin(x * 0.11f) + 1.8f * Mathf.Sin(x * 0.31f + 1f))));
            }
            return outline;
        }

        // An open-ended spanner: a shaft with a jaw at each end.
        static List<Vector2> SpannerOutline() => new List<Vector2>
        {
            new Vector2(-3f, -12f), new Vector2(-3f, -15f), new Vector2(-1.2f, -15f), new Vector2(-1.2f, -12.6f), new Vector2(1.2f, -12.6f),
            new Vector2(1.2f, -15f), new Vector2(3f, -15f), new Vector2(3f, -12f), new Vector2(1.1f, -9.5f), new Vector2(1.1f, 9.5f),
            new Vector2(3f, 12f), new Vector2(3f, 15f), new Vector2(1.2f, 15f), new Vector2(1.2f, 12.6f), new Vector2(-1.2f, 12.6f),
            new Vector2(-1.2f, 15f), new Vector2(-3f, 15f), new Vector2(-3f, 12f), new Vector2(-1.1f, 9.5f), new Vector2(-1.1f, -9.5f),
        };

        static List<Vector2> StarOutline()
        {
            var outline = new List<Vector2>();
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI * 0.5f + Mathf.PI * 2f * i / 10f;
                float r = i % 2 == 0 ? 3f : 1.45f;
                outline.Add(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r));
            }
            return outline;
        }

        // A paper lantern: a slightly squashed ball with ribs.
        static Mesh LanternMesh()
        {
            var profile = new List<Vector2>();
            const int rows = 14;
            for (int i = 0; i <= rows; i++)
            {
                float a = Mathf.PI * (i / (float)rows - 0.5f);
                float rib = i % 2 == 1 ? 0.965f : 1f;
                profile.Add(new Vector2(i == 0 || i == rows ? 0f : 10f * Mathf.Cos(a) * rib, 8.5f * Mathf.Sin(a)));
            }
            return MeshKit.Lathe(profile, 20, 80f);
        }

        // Book spines 1 to 3 wide standing in a box: flush at its front, the tallest reaching its top.
        static void Books(EnvironmentBox box, Dice dice, MeshBag furniture, MeshBag trim)
        {
            Vector3 size = box.Size;
            Frame shelf = Frame.Of(box, size.x, size.y, size.z);
            float x = -size.x * 0.5f;
            int count = 0;
            while (x < size.x * 0.5f - 0.05f)
            {
                float left = size.x * 0.5f - x;
                float width = Mathf.Min(dice.Range(1f, 3f), left);
                if (left - width < 0.8f) width = left;
                float height = size.y * (count == 0 ? 1f : dice.Range(0.72f, 1f));
                float depth = size.z * dice.Range(0.8f, 1f);
                float shade = 0.84f + 0.1f * Mathf.Floor(dice.Range(0f, 3.999f));
                bool paper = dice.Next() < 0.22f;
                Matrix4x4 at = shelf.At(new Vector3(x + width * 0.5f, -size.y * 0.5f + height * 0.5f, size.z * 0.5f - depth * 0.5f), Quaternion.identity,
                    new Vector3(width - 0.08f, height, depth));
                if (paper) trim.Add(UnitBox, at);
                else furniture.Add(UnitBox, at, Tint(shade));
                x += width;
                count++;
            }
        }

        // ---- Shared shapes ----------------------------------------------------------------------------------

        static Color Tint(float value) => new Color(value, value, value, 1f);

        /// <summary>A unit cube, 12 triangles: sized by the matrix it is added with.</summary>
        public static Mesh UnitBox => MeshKit.Cached("Room/UnitBox", () => MeshKit.Box(Vector3.one));

        static Mesh Rounded(float x, float y, float z, float bevel, int segments = 1) =>
            MeshKit.Cached(MeshKit.Key("Room/RoundedBox", x, y, z, bevel, segments), () => MeshKit.RoundedBox(new Vector3(x, y, z), bevel, segments));

        static Mesh Tube(float radius, float height, int sides) =>
            MeshKit.Cached(MeshKit.Key("Room/Tube", radius, height, sides), () => MeshKit.Cylinder(radius, height, sides));

        static Mesh Turned(string name, int segments, params Vector2[] profile) =>
            MeshKit.Cached("Room/Turned/" + name, () => MeshKit.Lathe(profile, segments));

        static Mesh Shape(string name, Func<List<Vector2>> outline, float depth, float bevel) =>
            MeshKit.Cached(MeshKit.Key("Room/Shape/" + name, depth, bevel), () => MeshKit.Extrude(outline(), depth, bevel));

        /// <summary>Where a shape goes: the pose of a collider box (scaled from the size the shape was authored for) or of a piece.</summary>
        readonly struct Frame
        {
            readonly Matrix4x4 matrix;

            Frame(Vector3 origin, Quaternion rotation, Vector3 scale) => matrix = Matrix4x4.TRS(origin, rotation, scale);

            /// <summary>The frame of a box whose shapes were authored for a box of x by y by z; the origin is its centre.</summary>
            public static Frame Of(EnvironmentBox box, float x, float y, float z) =>
                new Frame(box.Center, box.Rotation, new Vector3(Ratio(box.Size.x, x), Ratio(box.Size.y, y), Ratio(box.Size.z, z)));

            /// <summary>The frame of a piece: its origin on the floor or the wall, +Z its front.</summary>
            public static Frame Of(EnvironmentPiece piece) => new Frame(piece.Position, piece.Rotation, Vector3.one);

            static float Ratio(float actual, float authored) => authored > 1e-5f ? actual / authored : 1f;

            public Matrix4x4 At(float x, float y, float z) => matrix * Matrix4x4.Translate(new Vector3(x, y, z));
            public Matrix4x4 At(float x, float y, float z, Quaternion rotation) => matrix * Matrix4x4.TRS(new Vector3(x, y, z), rotation, Vector3.one);
            public Matrix4x4 At(Vector3 position, Quaternion rotation, Vector3 scale) => matrix * Matrix4x4.TRS(position, rotation, scale);
            public Vector3 Point(float x, float y, float z) => matrix.MultiplyPoint3x4(new Vector3(x, y, z));
        }

        /// <summary>A small deterministic generator for variations (book widths): never the simulation's Rng.</summary>
        struct Dice
        {
            uint state;

            public Dice(int seed)
            {
                state = (uint)seed * 2654435761u + 0x9E3779B9u;
                if (state == 0) state = 1;
            }

            public float Next()
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return (state & 0xFFFFFF) / 16777216f;
            }

            public float Range(float from, float to) => from + (to - from) * Next();
        }
    }
}
