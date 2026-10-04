using System;
using Toybox.Engine;

namespace Toybox.Audio
{
    /// <summary>One chord of a key's progression: its root as semitones above the key's root, and its quality.</summary>
    public readonly struct Chord
    {
        static readonly int[] MajorPentatonic = { 0, 2, 4, 7, 9 };
        static readonly int[] MinorPentatonic = { 0, 3, 5, 7, 10 };
        static readonly int[] RootAndFifth = { 0, 7 };

        /// <summary>Semitones above the key's root, 0..11.</summary>
        public readonly int Root;
        public readonly bool Minor;

        public Chord(int root, bool minor)
        {
            Root = root;
            Minor = minor;
        }

        /// <summary>Semitones from the chord's root to its third.</summary>
        public int Third => Minor ? 3 : 4;

        /// <summary>The pentatonic scale on the chord's own root, as semitones above it: what the hold tone is snapped to.</summary>
        public int[] Pentatonic => Minor ? MinorPentatonic : MajorPentatonic;

        /// <summary>Root and fifth, as semitones above the chord's root: what the release thock is tuned to.</summary>
        public int[] Anchors => RootAndFifth;
    }

    /// <summary>
    /// The key a level's sound is in (ART_BIBLE 6.4, 11.4): a root and a pentatonic scale, from the
    /// environment preset. The key is a transposition and the scale is the note set the music picks from;
    /// pitched effects are snapped to it, so a grab, a hold tone and a button are always in tune with the
    /// music box.
    /// </summary>
    public readonly struct MusicKey : IEquatable<MusicKey>
    {
        public const int ChordCount = 4;

        // The five notes of each scale, as semitones above the root.
        static readonly int[][] Pentatonics =
        {
            new[] { 0, 2, 4, 7, 9 },    // major
            new[] { 0, 2, 4, 6, 7 },    // lydian: the raised fourth instead of the sixth
            new[] { 0, 2, 3, 7, 9 },    // dorian: minor with the major sixth
            new[] { 0, 3, 5, 7, 10 },   // minor
        };

        // Degrees 1-3-5-6-8 of the seven-note mode the pentatonic comes from (the level-complete arpeggio).
        static readonly int[][] Arpeggios =
        {
            new[] { 0, 4, 7, 9, 12 },
            new[] { 0, 4, 7, 9, 12 },
            new[] { 0, 3, 7, 9, 12 },
            new[] { 0, 3, 7, 8, 12 },
        };

        // "I-vi-IV-V", in each mode's own chords: where the mode has a diminished chord or a ninth outside
        // it at one of those degrees, the neighbour that keeps every note of the add9 chord in the mode.
        static readonly Chord[][] Progressions =
        {
            new[] { new Chord(0, false), new Chord(9, true), new Chord(5, false), new Chord(7, false) },    // I   vi   IV  V
            new[] { new Chord(0, false), new Chord(9, true), new Chord(2, false), new Chord(7, false) },    // I   vi   II  V
            new[] { new Chord(0, true), new Chord(10, false), new Chord(5, false), new Chord(7, true) },    // i   bVII IV  v
            new[] { new Chord(0, true), new Chord(8, false), new Chord(5, true), new Chord(10, false) },    // i   bVI  iv  bVII
        };

        /// <summary>Pitch class of the root: semitones above C, 0..11.</summary>
        public readonly int Root;
        public readonly MusicScale Scale;

        public MusicKey(int root, MusicScale scale)
        {
            Root = ((root % 12) + 12) % 12;
            Scale = (int)scale >= 0 && (int)scale < Pentatonics.Length ? scale : MusicScale.MajorPentatonic;
        }

        public static readonly MusicKey CMajor = new MusicKey(0, MusicScale.MajorPentatonic);

        /// <summary>The key of a preset; C major pentatonic without one.</summary>
        public static MusicKey Of(EnvironmentPreset preset) => preset != null ? new MusicKey(preset.MusicRoot, preset.MusicScale) : CMajor;

        /// <summary>How far the root lies from C the short way, -5..+6 semitones: the transposition of anything written in C.</summary>
        public int Transpose => Root > 6 ? Root - 12 : Root;

        /// <summary>The scale's five notes as semitones above the root.</summary>
        public int[] Pentatonic => Pentatonics[(int)Scale];

        /// <summary>Scale degrees 1-3-5-6-8 of the mode, as semitones above the root.</summary>
        public int[] Arpeggio => Arpeggios[(int)Scale];

        /// <summary>The chord of the progression: two bars each, <see cref="ChordCount"/> to a cycle.</summary>
        public Chord Chord(int index) => Progressions[(int)Scale][((index % ChordCount) + ChordCount) % ChordCount];

        /// <summary>True if the MIDI note is one of the scale's.</summary>
        public bool Contains(int note) => Scales.Contains(note, Root, Pentatonic);

        /// <summary>The scale note nearest to a (fractional) MIDI note; of two equally near, the lower.</summary>
        public int Snap(float note) => Scales.Snap(note, Root, Pentatonic);

        /// <summary>Pitch class of a chord's root, semitones above C.</summary>
        public int ChordRoot(int index) => (Root + Chord(index).Root) % 12;

        public bool Equals(MusicKey other) => Root == other.Root && Scale == other.Scale;
        public override bool Equals(object obj) => obj is MusicKey other && Equals(other);
        public override int GetHashCode() => Root * 16 + (int)Scale;
        public static bool operator ==(MusicKey a, MusicKey b) => a.Equals(b);
        public static bool operator !=(MusicKey a, MusicKey b) => !a.Equals(b);

        public override string ToString() => Scales.NoteName(Root) + " " + Scale;
    }

    /// <summary>Note arithmetic on MIDI numbers (60 is C4) and pitch classes (0 is C).</summary>
    public static class Scales
    {
        static readonly string[] Names = { "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" };

        public static string NoteName(int note) => Names[((note % 12) + 12) % 12];

        /// <summary>True if the note is <paramref name="root"/> plus one of <paramref name="degrees"/>, in any octave.</summary>
        public static bool Contains(int note, int root, int[] degrees)
        {
            int pitchClass = (((note - root) % 12) + 12) % 12;
            for (int i = 0; i < degrees.Length; i++)
                if (degrees[i] % 12 == pitchClass) return true;
            return false;
        }

        /// <summary>
        /// The nearest note that is <paramref name="root"/> plus one of <paramref name="degrees"/> in some
        /// octave; of two equally near, the lower. Monotonic: a higher input never gives a lower note.
        /// </summary>
        public static int Snap(float note, int root, int[] degrees)
        {
            int around = (int)Math.Floor(note);
            int best = around;
            float bestDistance = float.MaxValue;
            for (int candidate = around - 11; candidate <= around + 12; candidate++)
            {
                if (!Contains(candidate, root, degrees)) continue;
                float distance = Math.Abs(candidate - note);
                if (distance < bestDistance - 1e-4f)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
            return best;
        }

        /// <summary>The note of this pitch class that lies in [low, low + 11].</summary>
        public static int NoteIn(int pitchClass, int low) => low + ((((pitchClass - low) % 12) + 12) % 12);
    }
}
