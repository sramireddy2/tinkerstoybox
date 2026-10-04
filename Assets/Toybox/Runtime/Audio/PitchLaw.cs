using System;

namespace Toybox.Audio
{
    /// <summary>
    /// The size-to-pitch law (ART_BIBLE 11.2). S is a toy's TRUE bounding diameter in units - absolute, not
    /// relative to the grab:
    ///
    ///   semis = clamp(-6 x log2(S), -24, +24), snapped to a scale;  AudioSource.pitch = 2^(semis / 12).
    ///
    /// Big is low: four times the size is an octave down, and a lower pitch also plays the clip slower, so
    /// bigger toys ring longer for free. A toy of one unit plays a clip as it was written.
    /// </summary>
    public static class PitchLaw
    {
        public const float SemisPerDoubling = -6f;
        public const float MinSemis = -24f, MaxSemis = 24f;
        /// <summary>
        /// The fastest a clip is played: two octaves up, where both the law (+24 semitones) and the landing
        /// rule (<c>clamp(S^-0.7, 0.25, 4)</c>) stop. An AudioSource set to 4 from code keeps it (measured:
        /// the range of -3..3 is the inspector's, not the engine's).
        /// </summary>
        public const float MaxPitch = 4f;
        public const float MinPitch = 0.25f;

        /// <summary>The note each pitched clip is written at: the grab's pluck (C5), the hold loop (A3), the release thock (G3).</summary>
        public const int GrabNote = 72, HoldNote = 57, ThockNote = 55;

        /// <summary>The law itself, unsnapped: semitones for a true diameter in units.</summary>
        public static float Semis(float diameter)
        {
            if (!(diameter > 0f)) return MaxSemis;
            float semis = SemisPerDoubling * (float)(Math.Log(diameter) / Math.Log(2.0));
            return semis < MinSemis ? MinSemis : semis > MaxSemis ? MaxSemis : semis;
        }

        /// <summary>The playback rate of a number of semitones, within what an AudioSource can do.</summary>
        public static float Pitch(float semis)
        {
            float pitch = Synth.Ratio(semis);
            return pitch < MinPitch ? MinPitch : pitch > MaxPitch ? MaxPitch : pitch;
        }

        /// <summary>
        /// The law snapped to a scale: the semitones (whole) by which a clip written at
        /// <paramref name="clipNote"/> is shifted so that what sounds is a note of <paramref name="root"/> +
        /// <paramref name="degrees"/>. Never lower for a smaller toy.
        /// </summary>
        public static int SnappedSemis(float diameter, int clipNote, int root, int[] degrees)
        {
            int note = Scales.Snap(clipNote + Semis(diameter), root, degrees);
            // Back inside the law's two octaves either way, staying on the scale.
            int highest = clipNote + (int)MaxSemis;
            int lowest = clipNote + (int)MinSemis;
            for (int guard = 0; note > highest && guard < 12; guard++)
            {
                note--;
                while (!Scales.Contains(note, root, degrees)) note--;
            }
            for (int guard = 0; note < lowest && guard < 12; guard++)
            {
                note++;
                while (!Scales.Contains(note, root, degrees)) note++;
            }
            return note - clipNote;
        }

        /// <summary>The law snapped to the key's pentatonic scale (the grab's pluck).</summary>
        public static int SnappedSemis(float diameter, int clipNote, MusicKey key) => SnappedSemis(diameter, clipNote, key.Root, key.Pentatonic);

        /// <summary>The law snapped to the pentatonic of a chord of the key (the hold tone, which is the lead while holding).</summary>
        public static int HoldSemis(float diameter, MusicKey key, int chord)
        {
            Chord c = key.Chord(chord);
            return SnappedSemis(diameter, HoldNote, key.Root + c.Root, c.Pentatonic);
        }

        /// <summary>The law snapped to the root and fifth of a chord of the key (the release thock).</summary>
        public static int ThockSemis(float diameter, MusicKey key, int chord)
        {
            Chord c = key.Chord(chord);
            return SnappedSemis(diameter, ThockNote, key.Root + c.Root, c.Anchors);
        }

        /// <summary>Landing: <c>pitch = clamp(S^-0.7, 0.25, max)</c>, unsnapped - an impact is a noise, not a note.</summary>
        public static float LandPitch(float diameter)
        {
            if (!(diameter > 0f)) return MaxPitch;
            float pitch = (float)Math.Pow(diameter, -0.7);
            return pitch < MinPitch ? MinPitch : pitch > MaxPitch ? MaxPitch : pitch;
        }

        /// <summary>
        /// Landing: <c>volume = clamp(speed / 12, 0, 1)</c>, leaned on by the mass - a feather-weight is a
        /// little quieter and a heavy toy a little louder at the same speed (15% per decade around mass 1).
        /// </summary>
        public static float LandVolume(float speed, float mass)
        {
            if (!(speed > 0f)) return 0f;
            float volume = speed / 12f;
            float weight = 1f + 0.15f * (float)Math.Log10(Math.Max(mass, 1e-3f));
            weight = weight < 0.55f ? 0.55f : weight > 1.3f ? 1.3f : weight;
            volume *= weight;
            return volume < 0f ? 0f : volume > 1f ? 1f : volume;
        }

        /// <summary>
        /// How much of the sub thump a landing gets: none below mass 30, rising with the decade of the mass,
        /// and only for a real fall.
        /// </summary>
        public static float LandSubVolume(float speed, float mass)
        {
            if (!(mass > 30f) || !(speed > 3f)) return 0f;
            float weight = ((float)Math.Log10(mass) - 1.5f) * 0.4f;
            weight = weight < 0f ? 0f : weight > 0.8f ? 0.8f : weight;
            float fall = speed / 8f;
            return weight * (fall > 1f ? 1f : fall);
        }
    }
}
