using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Toybox.Render
{
    /// <summary>
    /// Compiles the game's shader programs before the first frame that needs them (ART_BIBLE 3.8): the
    /// variants listed in Resources/ToyboxVariants.shadervariants, which the pipeline's setup step writes
    /// from the game's five shaders. In a browser a program is otherwise compiled the first time something
    /// is drawn with it, and the first grab would hitch.
    ///
    /// The game runs it once, when its presentation starts (behind the loading screen); it is safe to
    /// call again.
    /// </summary>
    public static class ShaderWarmup
    {
        /// <summary>The variant collection, under Resources.</summary>
        public const string Resource = "ToyboxVariants";

        static bool missingReported;

        /// <summary>The collection has been warmed up in this session.</summary>
        public static bool Done { get; private set; }
        /// <summary>Shaders and variants in the collection (0 until <see cref="Run"/>).</summary>
        public static int Shaders { get; private set; }
        public static int Variants { get; private set; }
        /// <summary>How long the warm-up took, in milliseconds.</summary>
        public static float Milliseconds { get; private set; }

        /// <summary>Warms the collection up if that has not happened yet. Returns true if it did the work now.</summary>
        public static bool Run()
        {
            if (Done) return false;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return false;

            var collection = Resources.Load<ShaderVariantCollection>(Resource);
            if (collection == null)
            {
                if (!missingReported)
                    Debug.LogWarning("[Toybox] Resources/" + Resource + ".shadervariants is missing: run Toybox.EditorTools.ProjectSetup.Run. Shaders will compile on first use.");
                missingReported = true;
                return false;
            }

            Shaders = collection.shaderCount;
            Variants = collection.variantCount;
            var watch = Stopwatch.StartNew();
            if (!collection.isWarmedUp) collection.WarmUp();
            watch.Stop();
            Milliseconds = (float)watch.Elapsed.TotalMilliseconds;
            Done = true;
            return true;
        }

        /// <summary>Forgets that the warm-up ran (tests).</summary>
        public static void Reset()
        {
            Done = false;
            missingReported = false;
            Shaders = 0;
            Variants = 0;
            Milliseconds = 0f;
        }
    }
}
