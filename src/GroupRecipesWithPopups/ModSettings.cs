using BepInEx.Configuration;

namespace GroupRecipesWithPopups
{
    /// <summary>All user-facing configuration entries (BepInEx/config/555maggot666.GroupRecipesWithPopups.cfg).</summary>
    internal sealed class ModSettings
    {
        public ModSettings(ConfigFile config)
        {
            ShowNotifications = config.Bind("General", "ShowNotifications", true,
                "Show the summary line (for example '12 more unlocks were synchronised') when entries were applied without a popup. " +
                "The original Valheim unlock popups are not affected by this setting.");

            SyncItems = config.Bind("Sync", "Items", true,
                "Share discovered materials and items. Applies to sending and receiving.");
            SyncRecipesAndPieces = config.Bind("Sync", "RecipesAndPieces", true,
                "Share learned recipes and build pieces. Applies to sending and receiving.");
            SyncCraftingStations = config.Bind("Sync", "CraftingStations", true,
                "Share crafting station levels (only ever raised, never lowered). Applies to sending and receiving.");

            EnablePopups = config.Bind("Display", "EnablePopups", true,
                "Show the original Valheim unlock popup (icon plus 'New material' / 'Recipe learned' ...) for every entry received from other players. " +
                "When disabled, received entries are still applied, just silently.");
            MaxPopupsOnJoin = config.Bind("Display", "MaxPopupsOnJoin", 25,
                new ConfigDescription(
                    "Maximum number of popups when a large batch arrives (joining a world, or another player uploading their whole knowledge). " +
                    "Further entries are applied silently, nothing is lost.",
                    new AcceptableValueRange<int>(0, 500)));
            PopupIntervalSeconds = config.Bind("Display", "PopupIntervalSeconds", 0.3f,
                new ConfigDescription(
                    "Delay in seconds between two applied entries, so the popups appear one after another.",
                    new AcceptableValueRange<float>(0f, 5f)));
        }

        public ConfigEntry<bool> ShowNotifications { get; }

        public ConfigEntry<bool> SyncItems { get; }

        public ConfigEntry<bool> SyncRecipesAndPieces { get; }

        public ConfigEntry<bool> SyncCraftingStations { get; }

        public ConfigEntry<bool> EnablePopups { get; }

        public ConfigEntry<int> MaxPopupsOnJoin { get; }

        public ConfigEntry<float> PopupIntervalSeconds { get; }

        /// <summary>Whether entries of this kind take part in syncing (sending and applying).</summary>
        public bool IsKindEnabled(GroupRecipesWithPopups.Core.KnowledgeKind kind)
        {
            switch (kind)
            {
                case GroupRecipesWithPopups.Core.KnowledgeKind.Material: return SyncItems.Value;
                case GroupRecipesWithPopups.Core.KnowledgeKind.Recipe:
                case GroupRecipesWithPopups.Core.KnowledgeKind.Piece: return SyncRecipesAndPieces.Value;
                case GroupRecipesWithPopups.Core.KnowledgeKind.Station: return SyncCraftingStations.Value;
                default: return false;
            }
        }
    }
}
