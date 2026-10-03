using System.Collections.Generic;
using UnityEngine;

namespace Toybox.Engine
{
    /// <summary>
    /// Seeded, deterministic random numbers (xorshift32 over a SplitMix-scrambled seed). The simulation
    /// uses this instead of UnityEngine.Random so a level replays identically.
    /// </summary>
    public sealed class Rng
    {
        uint state;

        public Rng(int seed) => Reseed(seed);

        public int Seed { get; private set; }

        public void Reseed(int seed)
        {
            Seed = seed;
            // Scramble so that small consecutive seeds give unrelated streams; xorshift must not start at 0.
            uint z = unchecked((uint)seed + 0x9E3779B9u);
            z = unchecked((z ^ (z >> 16)) * 0x85EBCA6Bu);
            z = unchecked((z ^ (z >> 13)) * 0xC2B2AE35u);
            z ^= z >> 16;
            state = z == 0 ? 0x6D2B79F5u : z;
        }

        public uint NextUInt()
        {
            uint x = state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            state = x;
            return x;
        }

        /// <summary>Uniform in [0, 1).</summary>
        public float Value() => (NextUInt() >> 8) * (1f / 16777216f);

        /// <summary>Uniform in [min, max).</summary>
        public float Range(float min, float max) => min + (max - min) * Value();

        /// <summary>Uniform integer in [min, maxExclusive).</summary>
        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min) return min;
            return min + (int)(NextUInt() % (uint)(maxExclusive - min));
        }

        public bool Chance(float probability) => Value() < probability;

        public float Sign() => (NextUInt() & 1u) == 0 ? -1f : 1f;

        public T Pick<T>(IReadOnlyList<T> items) => items[Range(0, items.Count)];

        public Vector2 InsideUnitCircle()
        {
            float angle = Range(0f, Mathf.PI * 2f);
            float radius = Mathf.Sqrt(Value());
            return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }

        public Vector3 OnUnitSphere()
        {
            float y = Range(-1f, 1f);
            float angle = Range(0f, Mathf.PI * 2f);
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
            return new Vector3(Mathf.Cos(angle) * r, y, Mathf.Sin(angle) * r);
        }

        public Quaternion Rotation() => Quaternion.AngleAxis(Range(0f, 360f), OnUnitSphere());

        /// <summary>A rotation about the world up axis by a uniformly random angle.</summary>
        public Quaternion Yaw() => Quaternion.Euler(0f, Range(0f, 360f), 0f);
    }
}
