using System.Collections.Generic;
using Toybox.Art;
using Toybox.Engine;
using Toybox.Gadgets;
using UnityEngine;

namespace Toybox.Levels
{
    /// <summary>
    /// The looks of Level 4's barricade: a wall of building blocks in Birch (not the player's: ART_BIBLE
    /// 2.5 rule 4) with one block missing, through which the exit glows, and a shudder when something
    /// bonks it (LEVELS, Level 4: "the barricade shudders in proportion to mass / 14"). The wall's one box
    /// collider belongs to the <see cref="Breakable"/>, which also hides the blocks when it breaks; the
    /// flying debris is the render layer's (BreakDebris), in this wall's material.
    /// </summary>
    sealed class Level04Barricade
    {
        public const int Rows = 8, Columns = 6;
        /// <summary>The course (from the floor) and the block in it that is left out: the peephole.</summary>
        public const int PeepRow = 2, PeepBlock = 3;

        const float ShudderSeconds = 0.5f;

        readonly Transform visual;
        int shudderTick = -1;
        float shudder;

        /// <summary>The wall: a box collider of the whole size and the blocks as one mesh. Hand it to the Breakable.</summary>
        public GameObject Body { get; }

        public Level04Barricade(Vector3 size)
        {
            Body = new GameObject("Barricade");
            Body.AddComponent<BoxCollider>().size = size;
            Mesh mesh = MeshKit.Cached(MeshKit.Key("Level04/Barricade", size.x, size.y, size.z), () =>
            {
                var whole = new Vector3(size.x / Columns, size.y / Rows, size.z);
                var half = new Vector3(whole.x * 0.5f, whole.y, whole.z);
                Mesh wholeBlock = BlockMesh(whole), halfBlock = BlockMesh(half);
                var parts = new List<MeshPart>();
                // Courses of blocks, every other one shifted by half a block, and one block left out.
                for (int r = 0; r < Rows; r++)
                {
                    float y = -size.y * 0.5f + (r + 0.5f) * whole.y;
                    if (r % 2 == 1)
                    {
                        for (int c = 0; c < Columns; c++)
                            parts.Add(new MeshPart(wholeBlock, new Vector3(-size.x * 0.5f + (c + 0.5f) * whole.x, y, 0f)));
                        continue;
                    }
                    parts.Add(new MeshPart(halfBlock, new Vector3(-size.x * 0.5f + whole.x * 0.25f, y, 0f)));
                    for (int c = 1; c < Columns; c++)
                    {
                        if (r == PeepRow && c == PeepBlock) continue;
                        parts.Add(new MeshPart(wholeBlock, new Vector3(-size.x * 0.5f + c * whole.x, y, 0f)));
                    }
                    parts.Add(new MeshPart(halfBlock, new Vector3(size.x * 0.5f - whole.x * 0.25f, y, 0f)));
                }
                Mesh merged = MeshKit.Merge("Level04 Barricade", parts);
                MeshKit.Release(wholeBlock);
                MeshKit.Release(halfBlock);
                return merged;
            });
            visual = GadgetKit.Visual(Body.transform, "Visual", mesh, Materials.Toy(ToyRecipe.PlainProp, Palette.Birch)).transform;
        }

        static Mesh BlockMesh(Vector3 blockSize) =>
            MeshKit.RoundedBox(blockSize, Mathf.Min(blockSize.x, Mathf.Min(blockSize.y, blockSize.z)) * 0.06f, 1);

        /// <summary>Something hit the wall and it held: it shudders, the more the heavier the hit was.</summary>
        public void Shudder(LevelContext ctx, float share)
        {
            shudder = Mathf.Lerp(0.03f, 0.14f, Mathf.Clamp01(share));
            shudderTick = ctx.Ticks;
        }

        /// <summary>One tick of the shudder: the blocks sway along the wall's depth and settle. The collider stays put.</summary>
        public void Tick(LevelContext ctx)
        {
            if (shudderTick < 0 || visual == null) return;
            float t = (ctx.Ticks - shudderTick) * Sim.Dt;
            if (t >= ShudderSeconds)
            {
                shudderTick = -1;
                visual.localPosition = Vector3.zero;
                return;
            }
            visual.localPosition = new Vector3(0f, 0f, shudder * Mathf.Sin(t * 60f) * (1f - t / ShudderSeconds));
        }
    }
}
