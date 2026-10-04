using Toybox.Art;

namespace Toybox.Audio
{
    /// <summary>
    /// Every clip the game has (ART_BIBLE 11.3, 11.4), in the order they are generated: UI, grab, hold,
    /// release, land, button, level complete, then the music bank.
    /// </summary>
    public enum SoundId
    {
        None = 0,

        UiHover,
        UiClick,

        FocusTick,
        Grab,

        Hold,
        HoldJump,

        ReleaseThock,
        ReleaseSub,
        ReleaseBell,

        LandPlastic,
        LandWood,
        LandRubber,
        LandMetal,
        LandGlass,
        LandFelt,
        LandCardboard,
        LandFeather,
        PlayerLand,

        ButtonPress,
        ButtonRelease,
        ExitOpen,
        ExitClose,

        /// <summary>Shutter, arpeggio, swell and stamp; rendered in the level's key and tempo.</summary>
        LevelComplete,

        /// <summary>The music box: every minor third from C4 (Box0) to C6 (Box8).</summary>
        Box0, Box1, Box2, Box3, Box4, Box5, Box6, Box7, Box8,

        KitWood,
        KitShaker,
        KitKick,

        /// <summary>One bar of each chord's root; rendered in the level's key and tempo.</summary>
        Bass0, Bass1, Bass2, Bass3,
        /// <summary>Two bars of each chord; rendered in the level's key and tempo.</summary>
        Pad0, Pad1, Pad2, Pad3,
    }

    public enum SoundGroup
    {
        Ui,
        Effect,
        Music,
    }

    /// <summary>What is fixed about a clip before it is synthesised.</summary>
    public readonly struct SoundSpec
    {
        public readonly SoundId Id;
        public readonly SoundGroup Group;
        public readonly int Rate;
        /// <summary>Peak level of the finished clip in dBFS (the gain staging that stands in for a compressor).</summary>
        public readonly float PeakDb;
        /// <summary>Level of the baked room.</summary>
        public readonly float Wet;
        public readonly bool Loop;
        /// <summary>Rendered per key and tempo at level load instead of once at startup.</summary>
        public readonly bool PerKey;

        public SoundSpec(SoundId id, SoundGroup group, int rate, float peakDb, float wet, bool loop = false, bool perKey = false)
        {
            Id = id;
            Group = group;
            Rate = rate;
            PeakDb = peakDb;
            Wet = wet;
            Loop = loop;
            PerKey = perKey;
        }
    }

    /// <summary>The table of clips: levels, rates and reverb per sound, and the lookups between them.</summary>
    public static class Sounds
    {
        /// <summary>Gain staging (ART_BIBLE 11.1): effects peak at -9 dBFS, music voices at -18, the hold loop at -29.</summary>
        public const float EffectPeakDb = -9f, MusicPeakDb = -18f, HoldPeakDb = -29f;
        /// <summary>Baked room levels: effects, music, the release sub.</summary>
        public const float EffectWet = 0.18f, MusicWet = 0.30f, SubWet = 0.6f;
        /// <summary>Tails are cut where they fall below this, dBFS.</summary>
        public const float TailFloorDb = -50f;

        public const int Count = (int)SoundId.Pad3 + 1;
        public const int BoxCount = 9;
        /// <summary>The note of Box0 (C4) and the distance between two box clips (a minor third).</summary>
        public const int BoxLowNote = 60, BoxInterval = 3;

        static readonly SoundSpec[] Specs = Build();

        static SoundSpec[] Build()
        {
            var specs = new SoundSpec[Count];
            void Add(SoundId id, SoundGroup group, int rate, float peakDb, float wet, bool loop = false, bool perKey = false) =>
                specs[(int)id] = new SoundSpec(id, group, rate, peakDb, wet, loop, perKey);

            Add(SoundId.None, SoundGroup.Effect, Synth.Rate, EffectPeakDb, 0f);
            // Small, frequent sounds sit well under the effect peak.
            Add(SoundId.UiHover, SoundGroup.Ui, Synth.Rate, -24f, EffectWet);
            Add(SoundId.UiClick, SoundGroup.Ui, Synth.Rate, -14f, EffectWet);
            Add(SoundId.FocusTick, SoundGroup.Effect, Synth.Rate, -26f, EffectWet);
            Add(SoundId.Grab, SoundGroup.Effect, Synth.Rate, EffectPeakDb, EffectWet);
            Add(SoundId.Hold, SoundGroup.Effect, Synth.Rate, HoldPeakDb, EffectWet, loop: true);
            Add(SoundId.HoldJump, SoundGroup.Effect, Synth.Rate, -20f, EffectWet);
            Add(SoundId.ReleaseThock, SoundGroup.Effect, Synth.Rate, EffectPeakDb, EffectWet);
            Add(SoundId.ReleaseSub, SoundGroup.Effect, Synth.Rate, EffectPeakDb, SubWet);
            Add(SoundId.ReleaseBell, SoundGroup.Effect, Synth.Rate, -12f, EffectWet);
            Add(SoundId.LandPlastic, SoundGroup.Effect, Synth.Rate, EffectPeakDb, EffectWet);
            Add(SoundId.LandWood, SoundGroup.Effect, Synth.Rate, EffectPeakDb, EffectWet);
            Add(SoundId.LandRubber, SoundGroup.Effect, Synth.Rate, EffectPeakDb, EffectWet);
            Add(SoundId.LandMetal, SoundGroup.Effect, Synth.Rate, EffectPeakDb, EffectWet);
            Add(SoundId.LandGlass, SoundGroup.Effect, Synth.Rate, EffectPeakDb, EffectWet);
            // "-10 dB" and "-26 dB" in the land table are below the effect peak.
            Add(SoundId.LandFelt, SoundGroup.Effect, Synth.Rate, EffectPeakDb - 10f, EffectWet);
            Add(SoundId.LandCardboard, SoundGroup.Effect, Synth.Rate, EffectPeakDb, EffectWet);
            Add(SoundId.LandFeather, SoundGroup.Effect, Synth.Rate, EffectPeakDb - 26f, EffectWet);
            Add(SoundId.PlayerLand, SoundGroup.Effect, Synth.Rate, -12f, EffectWet);
            Add(SoundId.ButtonPress, SoundGroup.Effect, Synth.Rate, EffectPeakDb, EffectWet);
            Add(SoundId.ButtonRelease, SoundGroup.Effect, Synth.Rate, EffectPeakDb, EffectWet);
            Add(SoundId.ExitOpen, SoundGroup.Effect, Synth.Rate, -12f, EffectWet);
            Add(SoundId.ExitClose, SoundGroup.Effect, Synth.Rate, -14f, EffectWet);
            Add(SoundId.LevelComplete, SoundGroup.Effect, Synth.Rate, EffectPeakDb, EffectWet, perKey: true);
            for (int i = 0; i < BoxCount; i++) Add(SoundId.Box0 + i, SoundGroup.Music, Synth.Rate, MusicPeakDb, MusicWet);
            Add(SoundId.KitWood, SoundGroup.Music, Synth.Rate, MusicPeakDb, MusicWet);
            Add(SoundId.KitShaker, SoundGroup.Music, Synth.Rate, MusicPeakDb, MusicWet);
            Add(SoundId.KitKick, SoundGroup.Music, Synth.Rate, MusicPeakDb, MusicWet);
            for (int i = 0; i < MusicKey.ChordCount; i++)
            {
                Add(SoundId.Bass0 + i, SoundGroup.Music, Synth.LowRate, MusicPeakDb, MusicWet, perKey: true);
                Add(SoundId.Pad0 + i, SoundGroup.Music, Synth.LowRate, MusicPeakDb, MusicWet, perKey: true);
            }
            return specs;
        }

        public static SoundSpec Spec(SoundId id) => Specs[(int)id];

        public static bool IsPad(SoundId id) => id >= SoundId.Pad0 && id <= SoundId.Pad3;
        public static bool IsBass(SoundId id) => id >= SoundId.Bass0 && id <= SoundId.Bass3;
        public static bool IsBox(SoundId id) => id >= SoundId.Box0 && id <= SoundId.Box8;
        public static bool IsLand(SoundId id) => id >= SoundId.LandPlastic && id <= SoundId.LandFeather;

        /// <summary>The landing clip of a toy material (ART_BIBLE 11.3, "Land materials").</summary>
        public static SoundId Land(LandSound material)
        {
            switch (material)
            {
                case LandSound.Wood: return SoundId.LandWood;
                case LandSound.Rubber: return SoundId.LandRubber;
                case LandSound.Metal: return SoundId.LandMetal;
                case LandSound.Glass: return SoundId.LandGlass;
                case LandSound.Felt: return SoundId.LandFelt;
                case LandSound.Cardboard: return SoundId.LandCardboard;
                case LandSound.Feather: return SoundId.LandFeather;
                default: return SoundId.LandPlastic;
            }
        }

        /// <summary>
        /// The music-box clip nearest to a MIDI note and the playback rate that reaches the note from it:
        /// never more than a semitone away inside C4-C6, so the bell keeps its timbre.
        /// </summary>
        public static SoundId Box(int note, out float pitch)
        {
            int index = (int)System.Math.Round((note - BoxLowNote) / (double)BoxInterval);
            index = index < 0 ? 0 : index >= BoxCount ? BoxCount - 1 : index;
            pitch = Synth.Ratio(note - (BoxLowNote + BoxInterval * index));
            return SoundId.Box0 + index;
        }
    }
}
