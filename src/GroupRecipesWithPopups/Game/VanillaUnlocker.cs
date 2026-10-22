using System;
using System.Collections.Generic;
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
        private const int MaxDeferred = 200;

        private readonly GameCatalog _catalog;
        private readonly PlayerKnowledgeBridge _bridge;
        private readonly List<KnowledgeEntry> _deferredPieces = new List<KnowledgeEntry>();
        private readonly HashSet<string> _deferredKeys = new HashSet<string>(StringComparer.Ordinal);

        public VanillaUnlocker(GameCatalog catalog, PlayerKnowledgeBridge bridge)
        {
            _catalog = catalog;
            _bridge = bridge;
        }

        /// <summary>
        /// The vanilla AddKnownPiece path writes into Hud.m_buildUi for the "recently used" strip. Calling it
        /// while the hud is still being constructed throws after the piece is already in the known set, which
        /// would leave that one piece without a popup. So pieces are held back until the build ui exists.
        /// </summary>
        private static bool IsBuildHudReady()
        {
            Hud hud = Hud.instance;
            return hud != null && hud.m_buildUi != null;
        }

        private bool DeferPiece(KnowledgeEntry entry)
        {
            if (_deferredPieces.Count >= MaxDeferred)
            {
                return false;
            }

            if (_deferredKeys.Add(entry.Key))
            {
                _deferredPieces.Add(entry);
            }

            return true;
        }

        /// <summary>Drops held back pieces; called when the connection or world changes.</summary>
        public void Reset()
        {
            _deferredPieces.Clear();
            _deferredKeys.Clear();
        }

        /// <summary>Retries pieces that were held back because the build ui was not ready yet.</summary>
        public void Tick(Player player)
        {
            if (player == null || _deferredPieces.Count == 0 || !IsBuildHudReady())
            {
                return;
            }

            KnowledgeEntry[] pending = _deferredPieces.ToArray();
            _deferredPieces.Clear();
            _deferredKeys.Clear();

            for (int i = 0; i < pending.Length; i++)
            {
                if (_bridge.IsKnown(player, pending[i]))
                {
                    continue;
                }

                try
                {
                    Apply(player, pending[i], showPopup: true);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("Retrying the held back piece " + pending[i] + " failed: " + ex.GetType().Name);
                }
            }
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
            if (!_catalog.TryGetPiece(entry.Key, out Piece piece) && _catalog.TryRebuildAfterMiss())
            {
                // A mod may have added build pieces without changing the item or recipe counts, which is what
                // the cache normally watches. Rebuild once and retry before treating the key as unknown.
                _catalog.TryGetPiece(entry.Key, out piece);
            }

            if (piece == null)
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

            if (!IsBuildHudReady())
            {
                // Still loading. Hold the piece back instead of letting the vanilla call half-finish.
                if (DeferPiece(entry))
                {
                    return false;
                }

                Plugin.Log.LogWarning("Build ui is not ready yet and the hold back list is full; applying " + entry + " without a popup.");
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
