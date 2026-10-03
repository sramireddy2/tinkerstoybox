using System;
using System.Collections.Generic;
using System.Reflection;

namespace Toybox.Engine
{
    /// <summary>Static description of a registered level; creating the level itself is cheap and done on demand.</summary>
    public sealed class LevelEntry
    {
        public int Id { get; }
        public string Slug { get; }
        public string Title { get; }
        public int Phase { get; }
        public Type Type { get; }

        internal LevelEntry(LevelAttribute info, Type type)
        {
            Id = info.Id;
            Slug = info.Slug;
            Title = info.Title;
            Phase = info.Phase;
            Type = type;
        }

        public LevelDefinition Create() => (LevelDefinition)Activator.CreateInstance(Type);
    }

    /// <summary>
    /// Every class in the Toybox assembly that derives from LevelDefinition and carries a [Level] attribute,
    /// sorted by id. (link.xml keeps the assembly from being stripped in the build.)
    /// </summary>
    public static class LevelRegistry
    {
        static List<LevelEntry> entries;

        public static IReadOnlyList<LevelEntry> All
        {
            get
            {
                if (entries == null) entries = Scan();
                return entries;
            }
        }

        public static bool Has(int id) => Find(id) != null;

        public static LevelEntry Find(int id)
        {
            foreach (LevelEntry entry in All)
                if (entry.Id == id) return entry;
            return null;
        }

        public static LevelEntry Find(string slug)
        {
            foreach (LevelEntry entry in All)
                if (string.Equals(entry.Slug, slug, StringComparison.OrdinalIgnoreCase)) return entry;
            return null;
        }

        /// <summary>A fresh instance of the level with this id.</summary>
        public static LevelDefinition Get(int id)
        {
            LevelEntry entry = Find(id);
            if (entry == null) throw new ArgumentException("There is no level with id " + id + ".", nameof(id));
            return entry.Create();
        }

        /// <summary>Id of the level after this one, or -1 if it is the last.</summary>
        public static int NextId(int id)
        {
            foreach (LevelEntry entry in All)
                if (entry.Id > id) return entry.Id;
            return -1;
        }

        static List<LevelEntry> Scan()
        {
            var found = new List<LevelEntry>();
            foreach (Type type in typeof(LevelRegistry).Assembly.GetTypes())
            {
                if (type.IsAbstract || !typeof(LevelDefinition).IsAssignableFrom(type)) continue;
                LevelAttribute info = type.GetCustomAttribute<LevelAttribute>();
                if (info == null) continue;
                if (type.GetConstructor(Type.EmptyTypes) == null)
                    throw new InvalidOperationException("Level " + type.Name + " needs a public parameterless constructor.");
                found.Add(new LevelEntry(info, type));
            }
            found.Sort((a, b) => a.Id.CompareTo(b.Id));
            for (int i = 1; i < found.Count; i++)
                if (found[i].Id == found[i - 1].Id)
                    throw new InvalidOperationException("Levels " + found[i - 1].Type.Name + " and " + found[i].Type.Name + " share id " + found[i].Id + ".");
            // The slug is what a page address names a level by (?level=slug); two of the same would hide one.
            for (int i = 0; i < found.Count; i++)
                for (int j = i + 1; j < found.Count; j++)
                    if (string.Equals(found[i].Slug, found[j].Slug, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Levels " + found[i].Type.Name + " and " + found[j].Type.Name + " share the slug '" + found[i].Slug + "'.");
            return found;
        }
    }
}
