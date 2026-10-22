using System;
using System.Collections.Generic;

namespace GroupRecipesWithPopups.Core
{
    /// <summary>One entry to apply, plus whether the original unlock popup should be shown for it.</summary>
    public readonly struct PopupStep
    {
        public PopupStep(KnowledgeEntry entry, bool showPopup)
        {
            Entry = entry;
            ShowPopup = showPopup;
        }

        public KnowledgeEntry Entry { get; }

        public bool ShowPopup { get; }
    }

    /// <summary>Ordered list of <see cref="PopupStep"/>s. Every pending entry is part of the plan.</summary>
    public sealed class PopupPlan
    {
        public PopupPlan(IReadOnlyList<PopupStep> steps)
        {
            Steps = steps ?? throw new ArgumentNullException(nameof(steps));
            int popups = 0;
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].ShowPopup)
                {
                    popups++;
                }
            }

            PopupCount = popups;
            HiddenCount = steps.Count - popups;
        }

        public IReadOnlyList<PopupStep> Steps { get; }

        /// <summary>Number of steps that show a popup.</summary>
        public int PopupCount { get; }

        /// <summary>Number of steps that are applied silently (beyond the popup cap, or popups disabled).</summary>
        public int HiddenCount { get; }
    }
}
