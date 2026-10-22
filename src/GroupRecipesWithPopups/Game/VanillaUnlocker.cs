using System;
using GroupRecipesWithPopups.Core;
using UnityEngine;

namespace GroupRecipesWithPopups.Game
{
    /// <summary>
    /// Applies one entry to the local player through the game's own unlock path, so the original popup
    /// (icon plus topic) appears. Falls back to direct writes plus a hand-queued popup when a vanilla method is
    /// unavailable. Never throws.
    /// </summary>
    internal sealed class VanillaUnlocker
    {
        private readonly GameCatalog _catalog;
        private readonly PlayerKnowledgeBridge _bridge;

        public VanillaUnlocker(GameCatalog catalog, PlayerKnowledgeBridge bridge)
        {
            _catalog = catalog;
            _bridge = bridge;
        }

        /// <summary>Applies the entry; returns true when it changed the player's knowledge.</summary>
        public bool Apply(Player player, KnowledgeEntry entry, bool showPopup)
        {
            if (player == null)
            {
                return false;
            }

            try
            {
                if (_bridge.IsKnown(player, entry))
                {
                    return false;
                }

                switch (entry.Kind)
                {
                    case KnowledgeKind.Material: return ApplyMaterial(player, entry, showPopup);
                    case KnowledgeKind.Recipe: return ApplyRecipe(player, entry, showPopup);
                    case KnowledgeKind.Piece: return ApplyPiece(player, entry, showPopup);
                    case KnowledgeKind.Station: return ApplyStation(player, entry, showPopup);
                    default: return false;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Applying " + entry + " failed: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>Runs the vanilla refresh methods once after a batch of applied entries.</summary>
        public void Refresh(Player player)
        {
            try
            {
                _bridge.Refresh(player);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Refreshing the player's known lists failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private bool ApplyMaterial(Player player, KnowledgeEntry entry, bool showPopup)
        {
            if (!_catalog.TryGetItem(entry.Key, out ItemDrop.ItemData item))
            {
                Plugin.WarnOnce("missing-item-" + entry.Key, "Item '" + entry.Key + "' does not exist in this game; adding it to the known materials directly.");
                bool written = _bridge.TryWrite(player, entry);
                if (written && showPopup)
                {
                    QueuePopup(null, "$msg_newmaterial", entry.Key);
                }

                return written;
            }

            if (showPopup)
            {
                player.AddKnownItem(item);
                return _bridge.IsKnown(player, entry);
            }

            return _bridge.TryWrite(player, entry);
        }

        private bool ApplyRecipe(Player player, KnowledgeEntry entry, bool showPopup)
        {
            if (!_catalog.TryGetRecipe(entry.Key, out Recipe recipe))
            {
                Plugin.WarnOnce("unmapped-recipe-" + entry.Key, "'" + entry.Key + "' matches no recipe or piece in this game; adding it to the known recipes silently.");
                return _bridge.TryWrite(player, entry);
            }

            if (!showPopup)
            {
                return _bridge.TryWrite(player, entry);
            }

            if (_bridge.TryInvokeAddKnownRecipe(player, recipe) && _bridge.IsKnown(player, entry))
            {
                return true;
            }

            Plugin.WarnOnce("fallback-recipe", "Vanilla AddKnownRecipe is unavailable; using the direct write plus manual popup fallback.");
            if (!_bridge.TryWrite(player, entry))
            {
                return false;
            }

            Sprite icon = recipe.m_item != null && recipe.m_item.m_itemData != null ? recipe.m_item.m_itemData.GetIcon() : null;
            QueuePopup(icon, "$msg_newrecipe", entry.Key);
            return true;
        }

        private bool ApplyPiece(Player player, KnowledgeEntry entry, bool showPopup)
        {
            if (!_catalog.TryGetPiece(entry.Key, out Piece piece))
            {
                Plugin.WarnOnce("missing-piece-" + entry.Key, "Piece '" + entry.Key + "' does not exist in this game; adding it to the known recipes directly.");
                bool written = _bridge.TryWrite(player, entry);
                if (written && showPopup)
                {
                    QueuePopup(null, "$msg_newpiece", entry.Key);
                }

                return written;
            }

            if (!showPopup)
            {
                return _bridge.TryWrite(player, entry);
            }

            if (_bridge.TryInvokeAddKnownPiece(player, piece) && _bridge.IsKnown(player, entry))
            {
                return true;
            }

            Plugin.WarnOnce("fallback-piece", "Vanilla AddKnownPiece is unavailable; using the direct write plus manual popup fallback.");
            if (!_bridge.TryWrite(player, entry))
            {
                return false;
            }

            bool dish = piece.m_category == Piece.PieceCategory.Feasts ||
                        piece.m_category == Piece.PieceCategory.Food ||
                        piece.m_category == Piece.PieceCategory.Meads;
            QueuePopup(piece.m_icon, dish ? "$msg_newdish" : "$msg_newpiece", entry.Key);
            return true;
        }

        private bool ApplyStation(Player player, KnowledgeEntry entry, bool showPopup)
        {
            // AddKnownStation needs a live CraftingStation instance, so the vanilla behaviour is rebuilt here.
            bool existed = _bridge.TryGetStationLevel(player, entry.Key, out int oldLevel);
            if (existed && oldLevel >= entry.Level)
            {
                return false;
            }

            if (!_bridge.TryWrite(player, entry))
            {
                return false;
            }

            if (showPopup)
            {
                Sprite icon = _catalog.GetStationIcon(entry.Key);
                if (existed)
                {
                    QueuePopup(icon, "$msg_newstation_level", entry.Key + " $msg_level " + entry.Level);
                }
                else
                {
                    QueuePopup(icon, "$msg_newstation", entry.Key);
                }
            }

            _bridge.RefreshKnownRecipes(player);
            return true;
        }

        private static void QueuePopup(Sprite icon, string topic, string description)
        {
            MessageHud hud = MessageHud.instance;
            if (hud != null)
            {
                hud.QueueUnlockMsg(icon, topic, description);
            }
        }
    }
}
