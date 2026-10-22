using System;
using System.Collections.Generic;

namespace GroupRecipesWithPopups.Core
{
    /// <summary>
    /// The set of all discoveries known to the group. Strictly additive: entries are never removed.
    /// Duplicates (same <see cref="KnowledgeEntry.HasSameIdentity"/>) are ignored; for stations only a
    /// strictly higher level counts as a change and replaces the stored level.
    /// </summary>
    public sealed class Ledger
    {
        private readonly List<KnowledgeEntry> _entries = new List<KnowledgeEntry>();
        private readonly Dictionary<KnowledgeEntry, int> _indexByIdentity =
            new Dictionary<KnowledgeEntry, int>(KnowledgeEntry.IdentityComparer);

        public int Count => _entries.Count;

        /// <summary>All entries in the order they were first added (stations carry their highest level).</summary>
        public IReadOnlyList<KnowledgeEntry> Entries => _entries;

        /// <summary>True when the ledger already covers the entry (for stations: stored level &gt;= entry level).</summary>
        public bool Contains(KnowledgeEntry entry)
        {
            if (entry.IsEmpty || !_indexByIdentity.TryGetValue(entry, out int index))
            {
                return false;
            }

            return entry.Kind != KnowledgeKind.Station || _entries[index].Level >= entry.Level;
        }

        public bool TryGetStationLevel(string stationKey, out int level)
        {
            if (stationKey != null &&
                _indexByIdentity.TryGetValue(new KnowledgeEntry(KnowledgeKind.Station, stationKey), out int index))
            {
                level = _entries[index].Level;
                return true;
            }

            level = 0;
            return false;
        }

        /// <summary>Adds the entry; returns true when it was new or raised a station level.</summary>
        public bool Add(KnowledgeEntry entry)
        {
            if (entry.IsEmpty || entry.Key.Length == 0)
            {
                return false;
            }

            if (_indexByIdentity.TryGetValue(entry, out int index))
            {
                if (entry.Kind == KnowledgeKind.Station && entry.Level > _entries[index].Level)
                {
                    _entries[index] = entry;
                    return true;
                }

                return false;
            }

            _indexByIdentity.Add(entry, _entries.Count);
            _entries.Add(entry);
            return true;
        }

        /// <summary>Adds all entries and returns those that changed the ledger (new, or raised station level).</summary>
        public List<KnowledgeEntry> AddRange(IEnumerable<KnowledgeEntry> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            var changed = new List<KnowledgeEntry>();
            foreach (KnowledgeEntry entry in entries)
            {
                if (Add(entry))
                {
                    changed.Add(entry);
                }
            }

            return changed;
        }
    }
}
