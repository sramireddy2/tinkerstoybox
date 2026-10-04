using System;
using Toybox.Art;
using UnityEngine;

namespace Toybox.Platform
{
    /// <summary>The quality choice of the settings menu. Auto leaves the tier to the pipeline's governor.</summary>
    public enum QualitySetting
    {
        Auto = 0,
        Low = 1,
        Medium = 2,
        High = 3,
    }

    /// <summary>Names the settings, for <see cref="Settings.Changed"/>.</summary>
    public enum Setting
    {
        Quality,
        LensBlur,
        MouseSensitivity,
        FieldOfView,
        MasterVolume,
        MusicVolume,
        SfxVolume,
        ReduceMotion,
        HighVisibility,
    }

    /// <summary>
    /// The player's settings (ART_BIBLE 10.5): one static store, kept in PlayerPrefs, with an event for
    /// every change. Whoever applies a setting reads it here and listens to <see cref="Changed"/>; the
    /// settings menu only writes. Values are clamped to their range when set and when loaded.
    ///
    /// Outside Play Mode the default store is in memory (see <see cref="PrefStores"/>); tests use
    /// <see cref="Use"/> to give it a store of their own and to get the defaults back.
    /// </summary>
    public static class Settings
    {
        public const float DefaultLensBlur = 0.6f;
        /// <summary>Degrees of view rotation per pixel of mouse movement.</summary>
        public const float DefaultMouseSensitivity = 0.1f, MinMouseSensitivity = 0.02f, MaxMouseSensitivity = 0.4f;
        /// <summary>Vertical, in degrees. The art direction assumes 70.</summary>
        public const float DefaultFieldOfView = 70f, MinFieldOfView = 50f, MaxFieldOfView = 90f;
        public const float DefaultMasterVolume = 0.8f, DefaultMusicVolume = 0.7f, DefaultSfxVolume = 1f;

        const string Prefix = "toybox.settings.";

        static IPrefStore custom, loadedFrom;
        static QualitySetting quality;
        static float lensBlur, mouseSensitivity, fieldOfView, masterVolume, musicVolume, sfxVolume;
        static bool reduceMotion, highVisibility;

        /// <summary>Raised after a setting has changed (not when it is set to the value it already has).</summary>
        public static event Action<Setting> Changed;

        /// <summary>The store the settings are kept in.</summary>
        public static IPrefStore Store => custom ?? PrefStores.Default;

        /// <summary>Auto, or a tier the player picked (which switches the governor off).</summary>
        public static QualitySetting Quality
        {
            get { Load(); return quality; }
            set
            {
                Load();
                if (!Enum.IsDefined(typeof(QualitySetting), value)) value = QualitySetting.Auto;
                if (quality == value) return;
                quality = value;
                Store.SetInt(Prefix + "quality", (int)value);
                Raise(Setting.Quality);
            }
        }

        /// <summary>The tier a manual quality choice stands for, or null for Auto.</summary>
        public static QualityTier? ForcedTier
        {
            get
            {
                switch (Quality)
                {
                    case QualitySetting.Low: return QualityTier.Low;
                    case QualitySetting.Medium: return QualityTier.Medium;
                    case QualitySetting.High: return QualityTier.High;
                    default: return null;
                }
            }
        }

        /// <summary>Strength of the lens blur, 0..1; it multiplies the blur radius (default 60%).</summary>
        public static float LensBlur
        {
            get { Load(); return lensBlur; }
            set => Set(ref lensBlur, Mathf.Clamp01(value), "lensBlur", Setting.LensBlur);
        }

        /// <summary>Degrees of view rotation per pixel of mouse movement.</summary>
        public static float MouseSensitivity
        {
            get { Load(); return mouseSensitivity; }
            set => Set(ref mouseSensitivity, Mathf.Clamp(value, MinMouseSensitivity, MaxMouseSensitivity), "mouseSensitivity", Setting.MouseSensitivity);
        }

        /// <summary>Vertical field of view in degrees.</summary>
        public static float FieldOfView
        {
            get { Load(); return fieldOfView; }
            set => Set(ref fieldOfView, Mathf.Clamp(value, MinFieldOfView, MaxFieldOfView), "fieldOfView", Setting.FieldOfView);
        }

        public static float MasterVolume
        {
            get { Load(); return masterVolume; }
            set => Set(ref masterVolume, Mathf.Clamp01(value), "masterVolume", Setting.MasterVolume);
        }

        public static float MusicVolume
        {
            get { Load(); return musicVolume; }
            set => Set(ref musicVolume, Mathf.Clamp01(value), "musicVolume", Setting.MusicVolume);
        }

        public static float SfxVolume
        {
            get { Load(); return sfxVolume; }
            set => Set(ref sfxVolume, Mathf.Clamp01(value), "sfxVolume", Setting.SfxVolume);
        }

        /// <summary>No exposure flashes, no camera shake, no card tilt; stick and peel become fades.</summary>
        public static bool ReduceMotion
        {
            get { Load(); return reduceMotion; }
            set => Set(ref reduceMotion, value, "reduceMotion", Setting.ReduceMotion);
        }

        /// <summary>"High-visibility toys": rim x 1.6, pool gain x 1.5.</summary>
        public static bool HighVisibility
        {
            get { Load(); return highVisibility; }
            set => Set(ref highVisibility, value, "highVisibility", Setting.HighVisibility);
        }

        /// <summary>Writes the store through (the browser persists PlayerPrefs only on request).</summary>
        public static void Save() => Store.Save();

        /// <summary>Every setting back to its default; raises Changed for those that change.</summary>
        public static void ResetToDefaults()
        {
            Quality = QualitySetting.Auto;
            LensBlur = DefaultLensBlur;
            MouseSensitivity = DefaultMouseSensitivity;
            FieldOfView = DefaultFieldOfView;
            MasterVolume = DefaultMasterVolume;
            MusicVolume = DefaultMusicVolume;
            SfxVolume = DefaultSfxVolume;
            ReduceMotion = false;
            HighVisibility = false;
        }

        /// <summary>
        /// Switches to another store (null: back to the default one) and loads the settings from it. Raises
        /// Changed for every setting whose value differs afterwards.
        /// </summary>
        public static void Use(IPrefStore store)
        {
            Load();
            QualitySetting oldQuality = quality;
            float oldBlur = lensBlur, oldSensitivity = mouseSensitivity, oldFov = fieldOfView;
            float oldMaster = masterVolume, oldMusic = musicVolume, oldSfx = sfxVolume;
            bool oldMotion = reduceMotion, oldVisibility = highVisibility;

            custom = store;
            loadedFrom = null;
            Load();

            if (quality != oldQuality) Raise(Setting.Quality);
            if (lensBlur != oldBlur) Raise(Setting.LensBlur);
            if (mouseSensitivity != oldSensitivity) Raise(Setting.MouseSensitivity);
            if (fieldOfView != oldFov) Raise(Setting.FieldOfView);
            if (masterVolume != oldMaster) Raise(Setting.MasterVolume);
            if (musicVolume != oldMusic) Raise(Setting.MusicVolume);
            if (sfxVolume != oldSfx) Raise(Setting.SfxVolume);
            if (reduceMotion != oldMotion) Raise(Setting.ReduceMotion);
            if (highVisibility != oldVisibility) Raise(Setting.HighVisibility);
        }

        // Reads everything once per store. The default store differs between Play Mode and the editor, so
        // "which store was this loaded from" is checked on every access; it is one reference comparison.
        static void Load()
        {
            IPrefStore store = Store;
            if (ReferenceEquals(loadedFrom, store)) return;
            loadedFrom = store;
            int storedQuality = store.GetInt(Prefix + "quality", (int)QualitySetting.Auto);
            quality = Enum.IsDefined(typeof(QualitySetting), storedQuality) ? (QualitySetting)storedQuality : QualitySetting.Auto;
            lensBlur = Mathf.Clamp01(Number(store, "lensBlur", DefaultLensBlur));
            mouseSensitivity = Mathf.Clamp(Number(store, "mouseSensitivity", DefaultMouseSensitivity), MinMouseSensitivity, MaxMouseSensitivity);
            fieldOfView = Mathf.Clamp(Number(store, "fieldOfView", DefaultFieldOfView), MinFieldOfView, MaxFieldOfView);
            masterVolume = Mathf.Clamp01(Number(store, "masterVolume", DefaultMasterVolume));
            musicVolume = Mathf.Clamp01(Number(store, "musicVolume", DefaultMusicVolume));
            sfxVolume = Mathf.Clamp01(Number(store, "sfxVolume", DefaultSfxVolume));
            reduceMotion = store.GetInt(Prefix + "reduceMotion", 0) != 0;
            highVisibility = store.GetInt(Prefix + "highVisibility", 0) != 0;
        }

        static float Number(IPrefStore store, string name, float fallback)
        {
            float value = store.GetFloat(Prefix + name, fallback);
            return float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;
        }

        static void Set(ref float field, float value, string name, Setting setting)
        {
            Load();
            if (float.IsNaN(value) || field == value) return;
            field = value;
            Store.SetFloat(Prefix + name, value);
            Raise(setting);
        }

        static void Set(ref bool field, bool value, string name, Setting setting)
        {
            Load();
            if (field == value) return;
            field = value;
            Store.SetInt(Prefix + name, value ? 1 : 0);
            Raise(setting);
        }

        static void Raise(Setting setting)
        {
            Action<Setting> handlers = Changed;
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                // One faulty listener must not keep the others from hearing about the change.
                try
                {
                    ((Action<Setting>)handler)(setting);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }
}
