namespace GroupRecipesWithPopups.Core
{
    /// <summary>The kinds of discoveries that are shared between players.</summary>
    public enum KnowledgeKind
    {
        /// <summary>An entry of the player's known-material set (items, trophies, ...).</summary>
        Material = 1,

        /// <summary>A known recipe (keyed by the crafted item's shared name).</summary>
        Recipe = 2,

        /// <summary>A known build piece (keyed by the piece name).</summary>
        Piece = 3,

        /// <summary>A crafting station together with the highest level the player has seen.</summary>
        Station = 4,
    }
}
