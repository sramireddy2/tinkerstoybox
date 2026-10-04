using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Toybox.Platform
{
    /// <summary>
    /// Where settings and progress are kept: PlayerPrefs in the game (IndexedDB in the browser), a
    /// dictionary in tests and editor tools, so that nothing a test does ends up in the user's registry.
    /// </summary>
    public interface IPrefStore
    {
        bool Has(string key);
        int GetInt(string key, int fallback);
        float GetFloat(string key, float fallback);
        string GetString(string key, string fallback);
        void SetInt(string key, int value);
        void SetFloat(string key, float value);
        void SetString(string key, string value);
        void Delete(string key);
        /// <summary>Writes pending changes through (the browser only persists on request).</summary>
        void Save();
    }

    /// <summary>PlayerPrefs.</summary>
    public sealed class PlayerPrefsStore : IPrefStore
    {
        public bool Has(string key) => PlayerPrefs.HasKey(key);
        public int GetInt(string key, int fallback) => PlayerPrefs.GetInt(key, fallback);
        public float GetFloat(string key, float fallback) => PlayerPrefs.GetFloat(key, fallback);
        public string GetString(string key, string fallback) => PlayerPrefs.GetString(key, fallback);
        public void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);
        public void SetFloat(string key, float value) => PlayerPrefs.SetFloat(key, value);
        public void SetString(string key, string value) => PlayerPrefs.SetString(key, value);
        public void Delete(string key) => PlayerPrefs.DeleteKey(key);
        public void Save() => PlayerPrefs.Save();
    }

    /// <summary>A store that lives as long as the object does. Values are kept as text, like a real store would.</summary>
    public sealed class MemoryStore : IPrefStore
    {
        readonly Dictionary<string, string> values = new Dictionary<string, string>();

        /// <summary>How many times Save was called.</summary>
        public int Saves { get; private set; }
        public int Count => values.Count;
        public IEnumerable<string> Keys => values.Keys;

        public bool Has(string key) => values.ContainsKey(key);

        public int GetInt(string key, int fallback) =>
            values.TryGetValue(key, out string text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;

        public float GetFloat(string key, float fallback) =>
            values.TryGetValue(key, out string text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;

        public string GetString(string key, string fallback) => values.TryGetValue(key, out string text) ? text : fallback;

        public void SetInt(string key, int value) => values[key] = value.ToString(CultureInfo.InvariantCulture);
        public void SetFloat(string key, float value) => values[key] = value.ToString("R", CultureInfo.InvariantCulture);
        public void SetString(string key, string value) => values[key] = value ?? "";
        public void Delete(string key) => values.Remove(key);
        public void Save() => Saves++;
    }

    /// <summary>Which store the game uses when nobody says otherwise.</summary>
    public static class PrefStores
    {
        static IPrefStore playerPrefs, memory;

        /// <summary>
        /// Editor only: while a tool has set this SessionState flag, Play Mode too gets the memory store.
        /// The play check sets it, so that what it finds does not depend on the progress and settings saved
        /// on this machine (a bookmark left on a level without an exit made it play that level for ever)
        /// and it leaves none behind.
        /// </summary>
        public const string EditorMemoryKey = "Toybox.Prefs.Memory";

        /// <summary>
        /// PlayerPrefs while the game runs (Play Mode, the build); a session-long memory store outside Play
        /// Mode, where only tests and editor tools run the game's code.
        /// </summary>
        public static IPrefStore Default
        {
            get
            {
#if UNITY_EDITOR
                if (UnityEditor.SessionState.GetBool(EditorMemoryKey, false)) return memory ??= new MemoryStore();
#endif
                if (Application.isPlaying) return playerPrefs ??= new PlayerPrefsStore();
                return memory ??= new MemoryStore();
            }
        }
    }
}
