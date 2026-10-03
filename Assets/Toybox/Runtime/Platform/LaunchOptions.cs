using System;
using System.Collections.Generic;
using System.Globalization;
using Toybox.Engine;

namespace Toybox.Platform
{
    /// <summary>
    /// What the page address asks for: <c>?level=3&amp;autoplay=1</c>. The level may be given by id or by
    /// slug (<c>?level=cheese-wedge</c>).
    /// </summary>
    public struct LaunchOptions
    {
        /// <summary>Level id, or -1 if the address names none.</summary>
        public int Level;
        /// <summary>Level slug when the level was named rather than numbered, else null.</summary>
        public string LevelSlug;
        /// <summary>Let the bot play the level's own solution.</summary>
        public bool Autoplay;

        public static LaunchOptions Default => new LaunchOptions { Level = -1 };

        /// <summary>Parses the query string of a URL. Anything missing or malformed is left at its default.</summary>
        public static LaunchOptions FromUrl(string url)
        {
            LaunchOptions options = Default;
            if (string.IsNullOrEmpty(url)) return options;

            int question = url.IndexOf('?');
            if (question < 0) return options;
            string query = url.Substring(question + 1);
            int hash = query.IndexOf('#');
            if (hash >= 0) query = query.Substring(0, hash);

            foreach (string pair in query.Split('&'))
            {
                if (pair.Length == 0) continue;
                int equals = pair.IndexOf('=');
                string key = Decode(equals < 0 ? pair : pair.Substring(0, equals)).Trim().ToLowerInvariant();
                string value = equals < 0 ? null : Decode(pair.Substring(equals + 1)).Trim();

                switch (key)
                {
                    case "level":
                        if (string.IsNullOrEmpty(value)) break;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                        {
                            options.Level = id >= 0 ? id : -1;
                            options.LevelSlug = null;
                        }
                        else
                        {
                            options.Level = -1;
                            options.LevelSlug = value;
                        }
                        break;
                    case "autoplay":
                        // A bare "autoplay" counts as on.
                        options.Autoplay = value == null || IsTruthy(value);
                        break;
                }
            }
            return options;
        }

        static bool IsTruthy(string value)
        {
            switch (value.ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    return true;
                default:
                    return false;
            }
        }

        static string Decode(string text)
        {
            try
            {
                return Uri.UnescapeDataString(text.Replace('+', ' '));
            }
            catch (UriFormatException)
            {
                return text;
            }
        }
    }

    /// <summary>
    /// The levels the runner walks through, in order: the registry in the game, a fixed list in tests.
    /// Stepping past either end wraps around.
    /// </summary>
    public sealed class LevelList
    {
        readonly int[] ids;
        readonly Func<int, LevelDefinition> create;
        readonly Func<string, int> findSlug;

        /// <summary>Ids are sorted; `create` must return a fresh level for an id in the list.</summary>
        public LevelList(IEnumerable<int> ids, Func<int, LevelDefinition> create, Func<string, int> findSlug = null)
        {
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            var sorted = new List<int>(ids);
            sorted.Sort();
            for (int i = sorted.Count - 1; i > 0; i--)
                if (sorted[i] == sorted[i - 1]) sorted.RemoveAt(i);
            if (sorted.Count == 0) throw new ArgumentException("A level list needs at least one level.", nameof(ids));
            this.ids = sorted.ToArray();
            this.create = create ?? throw new ArgumentNullException(nameof(create));
            this.findSlug = findSlug;
        }

        /// <summary>Every level registered with a [Level] attribute.</summary>
        public static LevelList FromRegistry()
        {
            var ids = new List<int>();
            foreach (LevelEntry entry in LevelRegistry.All) ids.Add(entry.Id);
            return new LevelList(ids, LevelRegistry.Get, slug =>
            {
                LevelEntry entry = LevelRegistry.Find(slug);
                return entry != null ? entry.Id : -1;
            });
        }

        public IReadOnlyList<int> Ids => ids;
        public int First => ids[0];
        public int Last => ids[ids.Length - 1];

        public bool Has(int id) => Array.BinarySearch(ids, id) >= 0;

        /// <summary>The level after this one; after the last comes the first again.</summary>
        public int After(int id)
        {
            foreach (int candidate in ids)
                if (candidate > id) return candidate;
            return First;
        }

        /// <summary>The level before this one; before the first comes the last.</summary>
        public int Before(int id)
        {
            for (int i = ids.Length - 1; i >= 0; i--)
                if (ids[i] < id) return ids[i];
            return Last;
        }

        /// <summary>The level a launch asks for, or the first one if it asks for none or for one that does not exist.</summary>
        public int Resolve(LaunchOptions launch)
        {
            if (launch.Level >= 0 && Has(launch.Level)) return launch.Level;
            if (!string.IsNullOrEmpty(launch.LevelSlug) && findSlug != null)
            {
                int id = findSlug(launch.LevelSlug);
                if (Has(id)) return id;
            }
            return First;
        }

        public LevelDefinition Create(int id)
        {
            if (!Has(id)) throw new ArgumentException("There is no level with id " + id + ".", nameof(id));
            return create(id);
        }
    }
}
