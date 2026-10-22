using System;
using System.Collections.Generic;

namespace GroupRecipesWithPopups.Core
{
    /// <summary>
    /// What this client has already reported to (or received from) the group. <see cref="Diff"/> compares the
    /// player's current knowledge against it; <see cref="Record"/> marks an entry as handled so that it is
    /// never sent or shown again (loopback protection).
    /// </summary>
    public sealed class KnowledgeSnapshot
    {
        private readonly HashSet<string> _materials = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _recipes = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _pieces = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _stations = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>True when the snapshot already covers the entry (for stations: recorded level &gt;= entry level).</summary>
        public bool Knows(KnowledgeEntry entry)
        {
            if (entry.IsEmpty)
            {
                return true;
            }

            switch (entry.Kind)
            {
                case KnowledgeKind.Material: return _materials.Contains(entry.Key);
                case KnowledgeKind.Recipe: return _recipes.Contains(entry.Key);
                case KnowledgeKind.Piece: return _pieces.Contains(entry.Key);
                case KnowledgeKind.Station:
                    return _stations.TryGetValue(entry.Key, out int level) && level >= entry.Level;
                default: return true;
            }
        }

        /// <summary>Marks the entry as handled; station levels only ever increase.</summary>
        public void Record(KnowledgeEntry entry)
        {
            if (entry.IsEmpty)
            {
                return;
            }

            switch (entry.Kind)
            {
                case KnowledgeKind.Material: _materials.Add(entry.Key); break;
                case KnowledgeKind.Recipe: _recipes.Add(entry.Key); break;
                case KnowledgeKind.Piece: _pieces.Add(entry.Key); break;
                case KnowledgeKind.Station:
                    if (!_stations.TryGetValue(entry.Key, out int level) || level < entry.Level)
                    {
                        _stations[entry.Key] = entry.Level;
                    }

                    break;
            }
        }

        public void RecordRange(IEnumerable<KnowledgeEntry> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            foreach (KnowledgeEntry entry in entries)
            {
                Record(entry);
            }
        }

        public void Clear()
        {
            _materials.Clear();
            _recipes.Clear();
            _pieces.Clear();
            _stations.Clear();
        }

        /// <summary>
        /// Returns the entries of <paramref name="current"/> that the snapshot does not cover yet (new entries,
        /// or stations with a higher level). Duplicates in the input are collapsed; the snapshot is not modified.
        /// </summary>
        public List<KnowledgeEntry> Diff(IEnumerable<KnowledgeEntry> current)
        {
            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            var result = new List<KnowledgeEntry>();
            var indexByIdentity = new Dictionary<KnowledgeEntry, int>(KnowledgeEntry.IdentityComparer);
            foreach (KnowledgeEntry entry in current)
            {
                if (Knows(entry))
                {
                    continue;
                }

                if (indexByIdentity.TryGetValue(entry, out int index))
                {
                    if (entry.Level > result[index].Level)
                    {
                        result[index] = entry;
                    }

                    continue;
                }

                indexByIdentity.Add(entry, result.Count);
                result.Add(entry);
            }

            return result;
        }
    }
}
