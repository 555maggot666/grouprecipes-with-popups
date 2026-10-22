using System;
using System.Collections.Generic;

namespace GroupRecipesWithPopups.Core
{
    /// <summary>
    /// A single shared discovery. The identity of an entry is (<see cref="Kind"/>, <see cref="Key"/>);
    /// <see cref="Level"/> only carries meaning for <see cref="KnowledgeKind.Station"/> entries and is
    /// normalised to 0 for every other kind. Full equality additionally compares the station level.
    /// </summary>
    public readonly struct KnowledgeEntry : IEquatable<KnowledgeEntry>
    {
        public KnowledgeEntry(KnowledgeKind kind, string key, int level = 0)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            Kind = kind;
            Key = key;
            Level = kind == KnowledgeKind.Station ? Math.Max(0, level) : 0;
        }

        public KnowledgeKind Kind { get; }

        public string Key { get; }

        public int Level { get; }

        /// <summary>True when the entry was default-constructed and carries no key.</summary>
        public bool IsEmpty => Key == null;

        /// <summary>True when both entries describe the same discovery, ignoring the station level.</summary>
        public bool HasSameIdentity(KnowledgeEntry other)
        {
            return Kind == other.Kind && string.Equals(Key, other.Key, StringComparison.Ordinal);
        }

        public bool Equals(KnowledgeEntry other)
        {
            return HasSameIdentity(other) && Level == other.Level;
        }

        public override bool Equals(object obj)
        {
            return obj is KnowledgeEntry other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine((int)Kind, Key == null ? 0 : StringComparer.Ordinal.GetHashCode(Key), Level);
        }

        public static bool operator ==(KnowledgeEntry left, KnowledgeEntry right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(KnowledgeEntry left, KnowledgeEntry right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return Kind == KnowledgeKind.Station ? Kind + ":" + Key + "@" + Level : Kind + ":" + Key;
        }

        /// <summary>Compares entries by (Kind, Key) only; use it for sets and dictionaries that dedupe by identity.</summary>
        public static IEqualityComparer<KnowledgeEntry> IdentityComparer { get; } = new IdentityEqualityComparer();

        private sealed class IdentityEqualityComparer : IEqualityComparer<KnowledgeEntry>
        {
            public bool Equals(KnowledgeEntry x, KnowledgeEntry y)
            {
                return x.HasSameIdentity(y);
            }

            public int GetHashCode(KnowledgeEntry obj)
            {
                return HashCode.Combine((int)obj.Kind, obj.Key == null ? 0 : StringComparer.Ordinal.GetHashCode(obj.Key));
            }
        }
    }
}
