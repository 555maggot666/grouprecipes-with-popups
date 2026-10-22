using System;
using System.Collections.Generic;
using GroupRecipesWithPopups.Core;
using UnityEngine;

namespace GroupRecipesWithPopups.Game
{
    /// <summary>
    /// Cached name lookups into the game's databases (items, recipes, build pieces, crafting stations).
    /// The tables are rebuilt only when the underlying <see cref="ObjectDB"/> / <see cref="ZNetScene"/> instance
    /// (or its size) changes, never on every tick.
    /// </summary>
    internal sealed class GameCatalog
    {
        private readonly Dictionary<string, Recipe> _recipes = new Dictionary<string, Recipe>(StringComparer.Ordinal);
        private readonly Dictionary<string, Piece> _pieces = new Dictionary<string, Piece>(StringComparer.Ordinal);
        private readonly Dictionary<string, ItemDrop.ItemData> _items = new Dictionary<string, ItemDrop.ItemData>(StringComparer.Ordinal);
        private readonly Dictionary<string, CraftingStation> _stations = new Dictionary<string, CraftingStation>(StringComparer.Ordinal);

        private ObjectDB _db;
        private int _dbItemCount = -1;
        private int _dbRecipeCount = -1;
        private ZNetScene _scene;
        private int _sceneCount = -1;
        private float _lastForcedRebuild = float.NegativeInfinity;

        /// <summary>
        /// Rebuilds the tables when a lookup missed and the caches may be stale, for example because a mod
        /// registered build pieces without touching the item or recipe counts. Throttled so a permanently
        /// unknown key cannot force a rebuild every tick.
        /// </summary>
        public bool TryRebuildAfterMiss()
        {
            ObjectDB db = ObjectDB.instance;
            if (db == null || db.m_items == null || db.m_recipes == null || db.m_items.Count == 0)
            {
                return false;
            }

            float now = Time.realtimeSinceStartup;
            if (now - _lastForcedRebuild < 2f)
            {
                return false;
            }

            _lastForcedRebuild = now;
            RebuildObjectTables(db);
            return true;
        }

        /// <summary>
        /// Brings the caches up to date. Returns false while the object database is not ready yet
        /// (main menu, world still loading); callers simply try again later.
        /// </summary>
        public bool Refresh()
        {
            ObjectDB db = ObjectDB.instance;
            if (db == null || db.m_items == null || db.m_recipes == null || db.m_items.Count == 0 || db.m_recipes.Count == 0)
            {
                return false;
            }

            if (!ReferenceEquals(db, _db) || db.m_items.Count != _dbItemCount || db.m_recipes.Count != _dbRecipeCount)
            {
                RebuildObjectTables(db);
            }

            ZNetScene scene = ZNetScene.instance;
            if (scene != null && scene.m_prefabs != null &&
                (!ReferenceEquals(scene, _scene) || scene.m_prefabs.Count != _sceneCount))
            {
                RebuildStationTable(scene);
            }

            return true;
        }

        public bool TryGetRecipe(string key, out Recipe recipe)
        {
            return _recipes.TryGetValue(key, out recipe) && recipe != null;
        }

        public bool TryGetPiece(string key, out Piece piece)
        {
            return _pieces.TryGetValue(key, out piece) && piece != null;
        }

        public bool TryGetItem(string key, out ItemDrop.ItemData item)
        {
            return _items.TryGetValue(key, out item) && item != null;
        }

        public bool TryGetStation(string key, out CraftingStation station)
        {
            return _stations.TryGetValue(key, out station) && station != null;
        }

        /// <summary>Icon of the crafting station with this name, or null when no prefab is available.</summary>
        public Sprite GetStationIcon(string key)
        {
            return TryGetStation(key, out CraftingStation station) ? station.m_icon : null;
        }

        /// <summary>
        /// Decides which kind an entry of the player's known-recipes set belongs to. Entries that match neither a
        /// recipe nor a piece are reported as <see cref="KnowledgeKind.Recipe"/>.
        /// </summary>
        public KnowledgeKind ClassifyKnownRecipeKey(string key)
        {
            if (_recipes.ContainsKey(key))
            {
                return KnowledgeKind.Recipe;
            }

            return _pieces.ContainsKey(key) ? KnowledgeKind.Piece : KnowledgeKind.Recipe;
        }

        private void RebuildObjectTables(ObjectDB db)
        {
            _items.Clear();
            _recipes.Clear();
            _pieces.Clear();

            foreach (GameObject prefab in db.m_items)
            {
                if (prefab == null)
                {
                    continue;
                }

                ItemDrop drop = prefab.GetComponent<ItemDrop>();
                if (drop == null || drop.m_itemData == null || drop.m_itemData.m_shared == null)
                {
                    continue;
                }

                string name = drop.m_itemData.m_shared.m_name;
                if (!string.IsNullOrEmpty(name))
                {
                    _items.TryAdd(name, drop.m_itemData);
                }
            }

            foreach (Recipe recipe in db.m_recipes)
            {
                if (recipe == null || recipe.m_item == null || recipe.m_item.m_itemData == null ||
                    recipe.m_item.m_itemData.m_shared == null)
                {
                    continue;
                }

                string name = recipe.m_item.m_itemData.m_shared.m_name;
                if (!string.IsNullOrEmpty(name))
                {
                    _recipes.TryAdd(name, recipe);
                }
            }

            List<Piece> pieces = db.GetAllBuildPieces(true);
            if (pieces != null)
            {
                foreach (Piece piece in pieces)
                {
                    if (piece != null && !string.IsNullOrEmpty(piece.m_name))
                    {
                        _pieces.TryAdd(piece.m_name, piece);
                    }
                }
            }

            _db = db;
            _dbItemCount = db.m_items.Count;
            _dbRecipeCount = db.m_recipes.Count;
        }

        private void RebuildStationTable(ZNetScene scene)
        {
            _stations.Clear();
            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (prefab == null)
                {
                    continue;
                }

                CraftingStation station = prefab.GetComponent<CraftingStation>();
                if (station != null && !string.IsNullOrEmpty(station.m_name))
                {
                    _stations.TryAdd(station.m_name, station);
                }
            }

            _scene = scene;
            _sceneCount = scene.m_prefabs.Count;
        }
    }
}
