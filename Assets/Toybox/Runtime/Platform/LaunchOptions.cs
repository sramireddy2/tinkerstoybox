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
        /// <summary>The debug look: URP Lit materials, plain lighting, the debug HUD (<c>?plain=1</c>, <c>-toyboxPlain</c>).</summary>
        public bool Plain;

        public static LaunchOptions Default => new LaunchOptions { Level = -1 };

        /// <summary>True if the address names a level, by id or by slug.</summary>
        public bool NamesLevel => Level >= 0 || !string.IsNullOrEmpty(LevelSlug);

        /// <summary>Editor only: a query string that Play Mode starts with, since the editor has no page address.</summary>
        public const string EditorOverrideKey = "Toybox.Launch.Url";

        /// <summary>
        /// What this run was started with: the page address in the browser, plus <c>-toyboxPlain</c> on the
        /// command line, plus (in the editor) the query string a tool left under <see cref="EditorOverrideKey"/>.
        /// </summary>
        public static LaunchOptions FromEnvironment()
        {
            string url = UnityEngine.Application.absoluteURL;
#if UNITY_EDITOR
            string forced = UnityEditor.SessionState.GetString(EditorOverrideKey, "");
            if (!string.IsNullOrEmpty(forced)) url = forced;
#endif
            LaunchOptions options = FromUrl(url);
            try
            {
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-toyboxPlain") >= 0) options.Plain = true;
            }
            catch (Exception)
            {
                // No command line on this platform: the address is all there is.
            }
            return options;
        }

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
                    case "plain":
                        options.Plain = value == null || IsTruthy(value);
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
    /// The levels of the game: the registry in the game, a fixed list in tests. Every level of the list can
    /// be loaded (and named in the page address); play moves through the campaign, in order, and stepping
    /// past either end wraps around. Levels outside the campaign (the sandbox, a showroom) are only ever
    /// reached by asking for them.
    /// </summary>
    public sealed class LevelList
    {
        readonly int[] ids;
        readonly int[] campaign;
        readonly Func<int, LevelDefinition> create;
        readonly Func<string, int> findSlug;

        /// <summary>
        /// Ids are sorted; create must return a fresh level for an id in the list. inCampaign picks the
        /// levels play moves through; without it (or if it picks none) that is all of them.
        /// </summary>
        public LevelList(IEnumerable<int> ids, Func<int, LevelDefinition> create, Func<string, int> findSlug = null, Func<int, bool> inCampaign = null)
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
            if (inCampaign != null) sorted.RemoveAll(id => !inCampaign(id));
            campaign = sorted.Count > 0 ? sorted.ToArray() : this.ids;
        }

        /// <summary>
        /// Every level registered with a [Level] attribute. The campaign is the levels with a Phase above 0
        /// (the sandbox and other Phase 0 levels stay reachable by address); while no such level exists yet,
        /// it is all of them.
        /// </summary>
        public static LevelList FromRegistry()
        {
            var ids = new List<int>();
            foreach (LevelEntry entry in LevelRegistry.All) ids.Add(entry.Id);
            return new LevelList(ids, LevelRegistry.Get, slug =>
            {
                LevelEntry entry = LevelRegistry.Find(slug);
                return entry != null ? entry.Id : -1;
            }, id =>
            {
                LevelEntry entry = LevelRegistry.Find(id);
                return entry != null && entry.Phase > 0;
            });
        }

        /// <summary>Every level that can be loaded, sorted.</summary>
        public IReadOnlyList<int> Ids => ids;
        /// <summary>The levels play moves through, sorted: what the catalogue shows.</summary>
        public IReadOnlyList<int> Campaign => campaign;
        /// <summary>The first level of the campaign.</summary>
        public int First => campaign[0];
        /// <summary>The last level of the campaign.</summary>
        public int Last => campaign[campaign.Length - 1];

        public bool Has(int id) => Array.BinarySearch(ids, id) >= 0;

        public bool InCampaign(int id) => Array.BinarySearch(campaign, id) >= 0;

        /// <summary>The campaign level after this one; after the last comes the first again.</summary>
        public int After(int id)
        {
            foreach (int candidate in campaign)
                if (candidate > id) return candidate;
            return First;
        }

        /// <summary>The campaign level before this one; before the first comes the last.</summary>
        public int Before(int id)
        {
            for (int i = campaign.Length - 1; i >= 0; i--)
                if (campaign[i] < id) return campaign[i];
            return Last;
        }

        /// <summary>The level a launch asks for (any level of the list), or the first of the campaign if it asks for none or for one that does not exist.</summary>
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
