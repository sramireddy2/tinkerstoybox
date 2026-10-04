using System.Collections.Generic;
using Toybox.Art;
using UnityEngine;

namespace Toybox.Engine
{
    /// <summary>What the play plane of a preset is made of.</summary>
    public enum IslandKind
    {
        /// <summary>The shell's own floor is the play plane.</summary>
        None,
        /// <summary>A rug 0.5 thick lying on the floor under the level.</summary>
        Rug,
        /// <summary>A bench top along the back wall, with the real floor far below.</summary>
        Bench,
        /// <summary>A shelf along the back wall, with the real floor far below.</summary>
        Shelf,
    }

    /// <summary>The backdrop furniture of ART_BIBLE 6.3. Each piece has one to four collider boxes.</summary>
    public enum FurnitureKind
    {
        Bed,
        ToyChest,
        Beanbag,
        Curtain,
        Chair,
        ChairLegs,
        Table,
        TableLegs,
        Radiator,
        Door,
        Hammer,
        Screwdriver,
        Spanner,
        DeskLamp,
        BoxStack,
        Blanket,
        FloorLamp,
        FairyLights,
        BookRun,
        PaperLantern,
        NightLight,
    }

    /// <summary>The scale the music of a preset is played in (ART_BIBLE 6.4, 11.4).</summary>
    public enum MusicScale
    {
        MajorPentatonic,
        LydianPentatonic,
        DorianPentatonic,
        MinorPentatonic,
    }

    /// <summary>
    /// One of the six environment presets of ART_BIBLE 6.4, as data: the look (dip, patterns, backdrop),
    /// the light and mood numbers, and what the solve of 6.2 needs. Simulation-side, because the room's
    /// colliders decide where a held toy stops; the render side reads the same object through
    /// <c>game.Environment.Preset</c>. Colours are sRGB as written in the document.
    /// </summary>
    public sealed class EnvironmentPreset
    {
        /// <summary>The key a level returns from <see cref="LevelDefinition.Environment"/>.</summary>
        public string Key { get; private set; }
        public Dip Dip { get; private set; }
        public string Place { get; private set; }
        public bool Night { get; private set; }
        /// <summary>False only for <see cref="None"/>: no shell, no window, no furniture, no colliders.</summary>
        public bool HasRoom { get; private set; } = true;

        // ---- Look -------------------------------------------------------------------------------------
        /// <summary>Pattern of the play surface: the island if there is one, else the shell floor.</summary>
        public PatternSpec PlayPattern { get; private set; }
        /// <summary>Pattern of the shell's real floor (beyond the rug, below the bench).</summary>
        public PatternSpec FloorPattern { get; private set; }
        /// <summary>Pattern of the shell walls; None means "dado only".</summary>
        public PatternSpec WallPattern { get; private set; }
        /// <summary>Pattern of the back (+Z) wall where it differs from the others.</summary>
        public PatternSpec BackWallPattern { get; private set; }
        public IReadOnlyList<FurnitureKind> Backdrop { get; private set; }

        // ---- Light and mood ---------------------------------------------------------------------------
        public Color SunColor { get; private set; }
        public float SunIntensity { get; private set; }
        /// <summary>Degrees above the horizon on the first visit.</summary>
        public float SunElevation { get; private set; }
        /// <summary>Degrees clockwise from +Z seen from above, on the first visit.</summary>
        public float SunAzimuth { get; private set; }
        /// <summary>The shell wall on the sun side: -1 is the -X wall, +1 the +X wall.</summary>
        public int WindowSide { get; private set; }
        public float HazeDensity { get; private set; }
        public Color PatchColor { get; private set; }
        public float PatchGain { get; private set; }
        public float PoolGain { get; private set; }
        public float ToyGlowGain { get; private set; }
        public float Bloom { get; private set; }
        /// <summary>Post exposure in EV.</summary>
        public float Exposure { get; private set; }
        /// <summary>The key as written in the art bible, e.g. "C major pentatonic".</summary>
        public string MusicKey { get; private set; }
        /// <summary>Semitones of the key's root above C.</summary>
        public int MusicRoot { get; private set; }
        public MusicScale MusicScale { get; private set; }
        public int Bpm { get; private set; }

        // ---- Layout ------------------------------------------------------------------------------------
        public IslandKind Island { get; private set; }
        /// <summary>How far the shell's real floor lies below the play plane (0.5 under a rug, 60 under the bench, 75 under the shelf).</summary>
        public float FloorDrop { get; private set; }
        /// <summary>Elevated presets put the +Z wall this far behind the level; 0 leaves the shell centred.</summary>
        public float BackWallGap { get; private set; }
        public bool Elevated => BackWallGap > 0f;

        EnvironmentPreset() { }

        public override string ToString() => Key;

        static readonly FurnitureKind[] NoFurniture = new FurnitureKind[0];

        public static readonly EnvironmentPreset SunnyRug = new EnvironmentPreset
        {
            Key = "sunny-rug", Dip = Palette.Mint, Place = "Bedroom floor: a rug island under the level, boards beyond",
            PlayPattern = PatternSpec.Dots, FloorPattern = PatternSpec.Planks, WallPattern = PatternSpec.Stripes, BackWallPattern = PatternSpec.Stripes,
            Backdrop = new[] { FurnitureKind.Bed, FurnitureKind.ToyChest, FurnitureKind.Beanbag, FurnitureKind.Curtain, FurnitureKind.Curtain },
            SunColor = Palette.Hex("#FFF1DC"), SunIntensity = 0.62f, SunElevation = 45f, SunAzimuth = 300f, WindowSide = -1,
            HazeDensity = 0.0024f, PatchColor = Palette.Hex("#FFE9C4"), PatchGain = 0.28f, PoolGain = 0.5f, ToyGlowGain = 1f,
            Bloom = 0.35f, Exposure = 0.2f, MusicKey = "C major pentatonic", MusicRoot = 0, MusicScale = MusicScale.MajorPentatonic, Bpm = 84,
            Island = IslandKind.Rug, FloorDrop = 0.5f,
        };

        public static readonly EnvironmentPreset BlockHall = new EnvironmentPreset
        {
            Key = "block-hall", Dip = Palette.Butter, Place = "Bare boards among furniture legs",
            PlayPattern = PatternSpec.Planks, FloorPattern = PatternSpec.Planks, WallPattern = PatternSpec.None, BackWallPattern = PatternSpec.None,
            Backdrop = new[] { FurnitureKind.Table, FurnitureKind.TableLegs, FurnitureKind.Chair, FurnitureKind.ChairLegs, FurnitureKind.Radiator, FurnitureKind.Door },
            SunColor = Palette.Hex("#FFF1DC"), SunIntensity = 0.66f, SunElevation = 40f, SunAzimuth = 60f, WindowSide = 1,
            HazeDensity = 0.0024f, PatchColor = Palette.Hex("#FFE9C4"), PatchGain = 0.34f, PoolGain = 0.5f, ToyGlowGain = 1f,
            Bloom = 0.35f, Exposure = 0.2f, MusicKey = "F major pentatonic", MusicRoot = 5, MusicScale = MusicScale.MajorPentatonic, Bpm = 88,
            Island = IslandKind.None, FloorDrop = 0f,
        };

        public static readonly EnvironmentPreset PegboardWorkbench = new EnvironmentPreset
        {
            Key = "pegboard-workbench", Dip = Palette.Pool, Place = "A bench top, the real floor 60 below, a pegboard wall 30 behind the level",
            PlayPattern = PatternSpec.Pegboard, FloorPattern = PatternSpec.Tiles, WallPattern = PatternSpec.None, BackWallPattern = PatternSpec.Pegboard,
            Backdrop = new[] { FurnitureKind.Hammer, FurnitureKind.Screwdriver, FurnitureKind.Spanner, FurnitureKind.DeskLamp, FurnitureKind.BoxStack },
            SunColor = Palette.Hex("#FFF6EC"), SunIntensity = 0.62f, SunElevation = 48f, SunAzimuth = 285f, WindowSide = -1,
            HazeDensity = 0.0028f, PatchColor = Palette.Hex("#FFF0D6"), PatchGain = 0.26f, PoolGain = 0.5f, ToyGlowGain = 1f,
            Bloom = 0.35f, Exposure = 0.2f, MusicKey = "G lydian pentatonic", MusicRoot = 7, MusicScale = MusicScale.LydianPentatonic, Bpm = 92,
            Island = IslandKind.Bench, FloorDrop = 60f, BackWallGap = 30f,
        };

        public static readonly EnvironmentPreset CardboardBox = new EnvironmentPreset
        {
            Key = "cardboard-box", Dip = Palette.Peach, Place = "A den of boxes and blankets on the floor",
            PlayPattern = PatternSpec.Quilt, FloorPattern = PatternSpec.Quilt, WallPattern = PatternSpec.None, BackWallPattern = PatternSpec.None,
            Backdrop = new[] { FurnitureKind.BoxStack, FurnitureKind.BoxStack, FurnitureKind.Blanket, FurnitureKind.FloorLamp, FurnitureKind.FairyLights },
            SunColor = Palette.Hex("#FFEFD8"), SunIntensity = 0.60f, SunElevation = 50f, SunAzimuth = 75f, WindowSide = 1,
            HazeDensity = 0.0024f, PatchColor = Palette.Hex("#FFE4BC"), PatchGain = 0.30f, PoolGain = 0.55f, ToyGlowGain = 1f,
            Bloom = 0.40f, Exposure = 0.2f, MusicKey = "E-flat major pentatonic", MusicRoot = 3, MusicScale = MusicScale.MajorPentatonic, Bpm = 80,
            Island = IslandKind.None, FloorDrop = 0f,
        };

        public static readonly EnvironmentPreset HighShelf = new EnvironmentPreset
        {
            Key = "high-shelf", Dip = Palette.Lilac, Place = "A shelf top, the real floor 75 below, a wallpaper wall 30 behind the level",
            PlayPattern = PatternSpec.Stripes.WithGain(0.02f), FloorPattern = PatternSpec.Planks, WallPattern = PatternSpec.Quilt, BackWallPattern = PatternSpec.Quilt,
            Backdrop = new[] { FurnitureKind.BookRun, FurnitureKind.BookRun, FurnitureKind.DeskLamp, FurnitureKind.PaperLantern },
            SunColor = Palette.Hex("#FFF1DC"), SunIntensity = 0.64f, SunElevation = 42f, SunAzimuth = 290f, WindowSide = -1,
            HazeDensity = 0.0032f, PatchColor = Palette.Hex("#FFE9C4"), PatchGain = 0.30f, PoolGain = 0.5f, ToyGlowGain = 1f,
            Bloom = 0.35f, Exposure = 0.2f, MusicKey = "D dorian pentatonic", MusicRoot = 2, MusicScale = MusicScale.DorianPentatonic, Bpm = 92,
            Island = IslandKind.Shelf, FloorDrop = 75f, BackWallGap = 30f,
        };

        public static readonly EnvironmentPreset NightLight = new EnvironmentPreset
        {
            Key = "night-light", Dip = Palette.Plum, Night = true, Place = "The sunny-rug layout after dark",
            PlayPattern = PatternSpec.Dots, FloorPattern = PatternSpec.Planks, WallPattern = PatternSpec.Stripes, BackWallPattern = PatternSpec.Stripes,
            Backdrop = new[] { FurnitureKind.Bed, FurnitureKind.ToyChest, FurnitureKind.Beanbag, FurnitureKind.NightLight, FurnitureKind.Door },
            SunColor = Palette.Hex("#BFD0FF"), SunIntensity = 0.50f, SunElevation = 45f, SunAzimuth = 300f, WindowSide = -1,
            HazeDensity = 0.0024f, PatchColor = Palette.Hex("#BFD0FF"), PatchGain = 0.35f, PoolGain = 0.9f, ToyGlowGain = 6f,
            Bloom = 0.55f, Exposure = 0.2f, MusicKey = "A minor pentatonic", MusicRoot = 9, MusicScale = MusicScale.MinorPentatonic, Bpm = 76,
            Island = IslandKind.Rug, FloorDrop = 0.5f,
        };

        /// <summary>
        /// No room at all: no shell, no window, no furniture, no colliders. What ad-hoc levels (tests, tools)
        /// get. The light and mood numbers are those of sunny-rug, so there is still a sun to draw with.
        /// </summary>
        public static readonly EnvironmentPreset None = new EnvironmentPreset
        {
            Key = "none", Dip = Palette.Mint, Place = "Nowhere", HasRoom = false,
            PlayPattern = PatternSpec.None, FloorPattern = PatternSpec.None, WallPattern = PatternSpec.None, BackWallPattern = PatternSpec.None,
            Backdrop = NoFurniture,
            SunColor = Palette.Hex("#FFF1DC"), SunIntensity = 0.62f, SunElevation = 45f, SunAzimuth = 300f, WindowSide = -1,
            HazeDensity = 0.0024f, PatchColor = Palette.Hex("#FFE9C4"), PatchGain = 0.28f, PoolGain = 0.5f, ToyGlowGain = 1f,
            Bloom = 0.35f, Exposure = 0.2f, MusicKey = "C major pentatonic", MusicRoot = 0, MusicScale = MusicScale.MajorPentatonic, Bpm = 84,
            Island = IslandKind.None, FloorDrop = 0f,
        };

        /// <summary>The six presets of the art bible, in its order.</summary>
        public static readonly EnvironmentPreset[] All = { SunnyRug, BlockHall, PegboardWorkbench, CardboardBox, HighShelf, NightLight };

        /// <summary>The keys of the six presets.</summary>
        public static IEnumerable<string> Keys
        {
            get
            {
                foreach (EnvironmentPreset preset in All) yield return preset.Key;
            }
        }

        // ART_BIBLE 6.4, "Level mapping": index is the level id, 1..15.
        static readonly EnvironmentPreset[] CampaignMap =
        {
            null,
            SunnyRug, PegboardWorkbench, CardboardBox, BlockHall, SunnyRug,
            BlockHall, HighShelf, PegboardWorkbench, HighShelf, CardboardBox,
            PegboardWorkbench, HighShelf, CardboardBox, NightLight, NightLight,
        };

        /// <summary>The preset with this key, <see cref="None"/> for "none", or null if there is no such preset.</summary>
        public static EnvironmentPreset Find(string key)
        {
            if (string.IsNullOrEmpty(key) || key == None.Key) return None;
            foreach (EnvironmentPreset preset in All)
                if (preset.Key == key) return preset;
            return null;
        }

        /// <summary>
        /// The default environment of a level: the art bible's table for the campaign (ids 1-15),
        /// sunny-rug for any other registered level, "none" for a level without a [Level] attribute.
        /// </summary>
        public static string KeyForLevel(int id, bool registered)
        {
            if (id >= 1 && id < CampaignMap.Length) return CampaignMap[id].Key;
            return registered ? SunnyRug.Key : None.Key;
        }

        /// <summary>How many campaign levels before this one use the preset (0 for the first visit and outside the campaign).</summary>
        public static int VisitForLevel(int id, string key)
        {
            if (id < 1 || id >= CampaignMap.Length) return 0;
            int visits = 0;
            for (int earlier = 1; earlier < id; earlier++)
                if (CampaignMap[earlier].Key == key) visits++;
            return visits;
        }
    }
}
