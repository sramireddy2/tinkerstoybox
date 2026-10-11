using System;
using System.Collections.Generic;
using UnityEngine;

namespace Toybox.Platform
{
    /// <summary>
    /// What the player has achieved: which levels are completed, the best time of each (and how many
    /// pick-ups that run took), the level they played last, and how often they reached the ending. Kept
    /// in a store (PlayerPrefs in the game) and written through on every change.
    /// </summary>
    public sealed class Progress
    {
        const string Prefix = "toybox.progress.";

        readonly IPrefStore store;

        /// <summary>Raised with the level id after a completion was recorded, or with -1 after the progress was reset or an ending was counted.</summary>
        public event Action<int> Changed;

        public Progress(IPrefStore store = null)
        {
            this.store = store ?? PrefStores.Default;
        }

        public IPrefStore Store => store;

        public bool IsCompleted(int levelId) => store.GetInt(Prefix + "done." + levelId, 0) != 0;

        /// <summary>Best completion time in seconds, or a negative number if the level was never completed.</summary>
        public float BestTime(int levelId)
        {
            if (!IsCompleted(levelId)) return -1f;
            float best = store.GetFloat(Prefix + "best." + levelId, -1f);
            return float.IsNaN(best) ? -1f : best;
        }

        /// <summary>How often the level was completed.</summary>
        public int Completions(int levelId) => Mathf.Max(0, store.GetInt(Prefix + "done." + levelId, 0));

        /// <summary>The level to continue with, or -1 if nothing was played yet.</summary>
        public int LastPlayed
        {
            get => store.GetInt(Prefix + "last", -1);
            set
            {
                if (store.GetInt(Prefix + "last", -1) == value) return;
                store.SetInt(Prefix + "last", value);
                store.Save();
            }
        }

        /// <summary>How many toys were picked up in the level's best run, or a negative number if that is not known.</summary>
        public int PickUps(int levelId) => IsCompleted(levelId) ? store.GetInt(Prefix + "picks." + levelId, -1) : -1;

        /// <summary>How often the campaign was played to its ending.</summary>
        public int Endings => Mathf.Max(0, store.GetInt(Prefix + "ended", 0));

        /// <summary>
        /// Notes a completion. Returns true if the time is a new best (the first completion always is).
        /// The pick-ups of the run are kept with a best time (negative: not counted).
        /// </summary>
        public bool RecordCompletion(int levelId, float seconds, int pickUps = -1)
        {
            if (levelId < 0) return false;
            if (float.IsNaN(seconds) || seconds < 0f) seconds = 0f;
            int count = Completions(levelId);
            float best = BestTime(levelId);
            bool isBest = count == 0 || best < 0f || seconds < best;
            store.SetInt(Prefix + "done." + levelId, count + 1);
            if (isBest)
            {
                store.SetFloat(Prefix + "best." + levelId, seconds);
                if (pickUps >= 0) store.SetInt(Prefix + "picks." + levelId, pickUps);
                else store.Delete(Prefix + "picks." + levelId);
            }
            store.Save();
            Raise(levelId);
            return isBest;
        }

        /// <summary>Notes that the campaign was played to its ending.</summary>
        public void RecordEnding()
        {
            store.SetInt(Prefix + "ended", Endings + 1);
            store.Save();
            Raise(-1);
        }

        /// <summary>The best times of these levels added up (levels that are not completed count nothing).</summary>
        public float TotalTime(IEnumerable<int> levelIds)
        {
            float total = 0f;
            foreach (int id in levelIds)
            {
                float best = BestTime(id);
                if (best > 0f) total += best;
            }
            return total;
        }

        /// <summary>The pick-ups of these levels' best runs added up (levels without a count add nothing).</summary>
        public int TotalPickUps(IEnumerable<int> levelIds)
        {
            int total = 0;
            foreach (int id in levelIds) total += Mathf.Max(0, PickUps(id));
            return total;
        }

        /// <summary>How many of these levels are completed.</summary>
        public int CompletedCount(IEnumerable<int> levelIds)
        {
            int count = 0;
            foreach (int id in levelIds)
                if (IsCompleted(id)) count++;
            return count;
        }

        /// <summary>
        /// The catalogue's lock: a level can be entered if it is completed, if it is the first of the
        /// campaign, or if the campaign level before it is completed. Levels outside the campaign are never locked.
        /// </summary>
        public bool IsUnlocked(int levelId, LevelList levels)
        {
            if (levels == null || !levels.Has(levelId)) return false;
            if (!levels.InCampaign(levelId) || levelId == levels.First || IsCompleted(levelId)) return true;
            return IsCompleted(levels.Before(levelId));
        }

        /// <summary>Forgets everything about these levels and the level played last.</summary>
        public void Reset(IEnumerable<int> levelIds)
        {
            foreach (int id in levelIds)
            {
                store.Delete(Prefix + "done." + id);
                store.Delete(Prefix + "best." + id);
                store.Delete(Prefix + "picks." + id);
            }
            store.Delete(Prefix + "last");
            store.Delete(Prefix + "ended");
            store.Save();
            Raise(-1);
        }

        void Raise(int levelId)
        {
            Action<int> handlers = Changed;
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<int>)handler)(levelId);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }
    }
}
