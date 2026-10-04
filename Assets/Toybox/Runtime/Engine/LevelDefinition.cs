using System;
using System.Collections;
using System.Reflection;

namespace Toybox.Engine
{
    /// <summary>Marks a LevelDefinition so LevelRegistry finds it. Ids are unique and define the play order.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class LevelAttribute : Attribute
    {
        public int Id { get; }
        public string Slug { get; }
        public string Title { get; }
        /// <summary>Chapter of the game the level belongs to (1..n); 0 for test and sandbox levels.</summary>
        public int Phase { get; set; }

        public LevelAttribute(int id, string slug, string title)
        {
            Id = id;
            Slug = slug;
            Title = title;
        }
    }

    /// <summary>
    /// One level. Build creates everything through the LevelContext; it may run more than once on the same
    /// instance (restart), so it must set up all of the level's own fields itself.
    /// </summary>
    public abstract class LevelDefinition
    {
        LevelAttribute info;
        bool infoRead;

        /// <summary>The [Level] attribute, or null for ad-hoc levels (tests).</summary>
        public LevelAttribute Info
        {
            get
            {
                if (!infoRead)
                {
                    info = GetType().GetCustomAttribute<LevelAttribute>();
                    infoRead = true;
                }
                return info;
            }
        }

        public int Id => Info != null ? Info.Id : -1;
        /// <summary>From the [Level] attribute. A level without one (an ad-hoc level in a test or a tool) may override these.</summary>
        public virtual string Slug => Info != null ? Info.Slug : GetType().Name;
        public virtual string Title => Info != null ? Info.Title : GetType().Name;
        public int Phase => Info != null ? Info.Phase : 0;

        /// <summary>One-line objective.</summary>
        public virtual string Blurb => "";
        public virtual string[] Hints => Array.Empty<string>();
        /// <summary>
        /// Key of the environment preset the level stands in (ART_BIBLE 6.4): "sunny-rug", "block-hall",
        /// "pegboard-workbench", "cardboard-box", "high-shelf", "night-light", or "none". The default is the
        /// art bible's level-to-preset table for the campaign levels 1-15, "sunny-rug" for any other level
        /// with a [Level] attribute, and "none" (no room, no colliders) for ad-hoc levels without one.
        /// </summary>
        public virtual string Environment => EnvironmentPreset.KeyForLevel(Id, Info != null);
        /// <summary>
        /// Height of the play plane: where the preset puts its floor, rug, bench top or shelf top
        /// (ART_BIBLE 6.2, LEVELS 0.4 request 6). A level with pits declares the bottom of the deepest
        /// one, or the room's floor would close them.
        /// </summary>
        public virtual float GroundY => 0f;
        /// <summary>
        /// How many earlier levels of the campaign use the same preset. Each repeat visit lowers the sun by
        /// 2 degrees and swings it by 6, so no two levels share a light (ART_BIBLE 6.2).
        /// </summary>
        public virtual int EnvironmentVisit => EnvironmentPreset.VisitForLevel(Id, Environment);
        /// <summary>Anything that falls below this height respawns.</summary>
        public virtual float KillY => -30f;

        public abstract void Build(LevelContext ctx);

        /// <summary>Scripted solution, used by the level's test and by autoplay. Each yield is one tick.</summary>
        public virtual IEnumerator Solve(Bot bot)
        {
            yield break;
        }
    }
}
