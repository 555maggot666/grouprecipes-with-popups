using System;
using System.Collections.Generic;

namespace GroupRecipesWithPopups.Core
{
    /// <summary>Turns a batch of pending entries into an ordered, capped <see cref="PopupPlan"/>.</summary>
    public static class PopupPlanner
    {
        /// <summary>Pass as <c>maxPopups</c> for the live path where every entry gets a popup.</summary>
        public const int Unlimited = int.MaxValue;

        /// <summary>
        /// Sorts materials before recipes/pieces before stations (stable within a group), collapses duplicates
        /// (the highest station level wins) and lets only the first <paramref name="maxPopups"/> steps show a
        /// popup. The remaining steps stay in the plan and are applied silently, so nothing is ever dropped.
        /// </summary>
        public static PopupPlan BuildPlan(IEnumerable<KnowledgeEntry> pending, int maxPopups)
        {
            if (pending == null)
            {
                throw new ArgumentNullException(nameof(pending));
            }

            var unique = new List<KnowledgeEntry>();
            var indexByIdentity = new Dictionary<KnowledgeEntry, int>(KnowledgeEntry.IdentityComparer);
            foreach (KnowledgeEntry entry in pending)
            {
                if (entry.IsEmpty)
                {
                    continue;
                }

                if (indexByIdentity.TryGetValue(entry, out int index))
                {
                    if (entry.Level > unique[index].Level)
                    {
                        unique[index] = entry;
                    }

                    continue;
                }

                indexByIdentity.Add(entry, unique.Count);
                unique.Add(entry);
            }

            var ordered = new List<KnowledgeEntry>(unique.Count);
            for (int rank = 0; rank <= 2; rank++)
            {
                foreach (KnowledgeEntry entry in unique)
                {
                    if (RankOf(entry.Kind) == rank)
                    {
                        ordered.Add(entry);
                    }
                }
            }

            int cap = Math.Max(0, maxPopups);
            var steps = new List<PopupStep>(ordered.Count);
            for (int i = 0; i < ordered.Count; i++)
            {
                steps.Add(new PopupStep(ordered[i], i < cap));
            }

            return new PopupPlan(steps);
        }

        private static int RankOf(KnowledgeKind kind)
        {
            switch (kind)
            {
                case KnowledgeKind.Material: return 0;
                case KnowledgeKind.Recipe:
                case KnowledgeKind.Piece: return 1;
                default: return 2;
            }
        }
    }
}
