using System;
using System.Collections.Generic;
using System.Reflection;
using GroupRecipesWithPopups.Core;

namespace GroupRecipesWithPopups.Game
{
    /// <summary>
    /// Reflection access to the player's private knowledge state (known materials, recipes/pieces and station
    /// levels) and to the private refresh/unlock methods. Every member is resolved once; when one is missing in the
    /// running game version a warning is logged (once) and the dependent operation reports failure instead of throwing.
    /// </summary>
    internal sealed class PlayerKnowledgeBridge
    {
        private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;

        private readonly FieldInfo _knownMaterial;
        private readonly FieldInfo _knownRecipes;
        private readonly FieldInfo _knownStations;
        private readonly MethodInfo _addKnownRecipe;
        private readonly MethodInfo _addKnownPiece;
        private readonly MethodInfo _updateKnownRecipesList;
        private readonly MethodInfo _updateAvailablePiecesList;

        public PlayerKnowledgeBridge()
        {
            Type player = typeof(Player);
            _knownMaterial = FindField(player, "m_knownMaterial");
            _knownRecipes = FindField(player, "m_knownRecipes");
            _knownStations = FindField(player, "m_knownStations");
            _addKnownRecipe = FindMethod(player, "AddKnownRecipe", typeof(Recipe));
            _addKnownPiece = FindMethod(player, "AddKnownPiece", typeof(Piece));
            _updateKnownRecipesList = FindMethod(player, "UpdateKnownRecipesList");
            _updateAvailablePiecesList = FindMethod(player, "UpdateAvailablePiecesList");
        }

        /// <summary>True when the three knowledge fields could be resolved, i.e. reading and writing works at all.</summary>
        public bool CanAccessKnowledge => _knownMaterial != null && _knownRecipes != null && _knownStations != null;

        public bool HasAddKnownRecipe => _addKnownRecipe != null;

        public bool HasAddKnownPiece => _addKnownPiece != null;

        /// <summary>Appends everything the player knows (all four kinds) to <paramref name="sink"/>.</summary>
        public bool TryRead(Player player, GameCatalog catalog, List<KnowledgeEntry> sink)
        {
            if (player == null || !CanAccessKnowledge)
            {
                return false;
            }

            var materials = _knownMaterial.GetValue(player) as HashSet<string>;
            var recipes = _knownRecipes.GetValue(player) as HashSet<string>;
            var stations = _knownStations.GetValue(player) as Dictionary<string, int>;
            if (materials == null || recipes == null || stations == null)
            {
                return false;
            }

            foreach (string key in materials)
            {
                if (!string.IsNullOrEmpty(key))
                {
                    sink.Add(new KnowledgeEntry(KnowledgeKind.Material, key));
                }
            }

            foreach (string key in recipes)
            {
                if (!string.IsNullOrEmpty(key))
                {
                    sink.Add(new KnowledgeEntry(catalog.ClassifyKnownRecipeKey(key), key));
                }
            }

            foreach (KeyValuePair<string, int> station in stations)
            {
                if (!string.IsNullOrEmpty(station.Key))
                {
                    sink.Add(new KnowledgeEntry(KnowledgeKind.Station, station.Key, station.Value));
                }
            }

            return true;
        }

        /// <summary>True when the player already knows the entry (stations: level at least as high).</summary>
        public bool IsKnown(Player player, KnowledgeEntry entry)
        {
            if (player == null)
            {
                return false;
            }

            switch (entry.Kind)
            {
                case KnowledgeKind.Material:
                    return player.IsMaterialKnown(entry.Key);
                case KnowledgeKind.Recipe:
                case KnowledgeKind.Piece:
                    return player.IsRecipeKnown(entry.Key);
                case KnowledgeKind.Station:
                    return TryGetStationLevel(player, entry.Key, out int level) && level >= entry.Level;
                default:
                    return true;
            }
        }

        public bool TryGetStationLevel(Player player, string station, out int level)
        {
            level = 0;
            return _knownStations?.GetValue(player) is Dictionary<string, int> stations &&
                   stations.TryGetValue(station, out level);
        }

        /// <summary>Writes the entry straight into the player's known sets (no popup). Returns false if impossible.</summary>
        public bool TryWrite(Player player, KnowledgeEntry entry)
        {
            if (player == null || !CanAccessKnowledge)
            {
                Plugin.WarnOnce("bridge-fields", "Player knowledge fields were not found in this game version; cannot apply entries.");
                return false;
            }

            switch (entry.Kind)
            {
                case KnowledgeKind.Material:
                    return (_knownMaterial.GetValue(player) as HashSet<string>)?.Add(entry.Key) ?? false;
                case KnowledgeKind.Recipe:
                case KnowledgeKind.Piece:
                    return (_knownRecipes.GetValue(player) as HashSet<string>)?.Add(entry.Key) ?? false;
                case KnowledgeKind.Station:
                    if (_knownStations.GetValue(player) is Dictionary<string, int> stations)
                    {
                        if (!stations.TryGetValue(entry.Key, out int old) || old < entry.Level)
                        {
                            stations[entry.Key] = entry.Level;
                            return true;
                        }
                    }

                    return false;
                default:
                    return false;
            }
        }

        /// <summary>Calls the private vanilla <c>AddKnownRecipe</c> (adds the recipe and queues the vanilla popup).</summary>
        public bool TryInvokeAddKnownRecipe(Player player, Recipe recipe)
        {
            return TryInvoke(_addKnownRecipe, "AddKnownRecipe", player, recipe);
        }

        /// <summary>Calls the private vanilla <c>AddKnownPiece</c> (adds the piece and queues the vanilla popup).</summary>
        public bool TryInvokeAddKnownPiece(Player player, Piece piece)
        {
            return TryInvoke(_addKnownPiece, "AddKnownPiece", player, piece);
        }

        /// <summary>Calls the private refresh methods so that newly known entries show up in the crafting/build UI.</summary>
        public void Refresh(Player player)
        {
            TryInvoke(_updateKnownRecipesList, "UpdateKnownRecipesList", player);
            TryInvoke(_updateAvailablePiecesList, "UpdateAvailablePiecesList", player);
        }

        public void RefreshKnownRecipes(Player player)
        {
            TryInvoke(_updateKnownRecipesList, "UpdateKnownRecipesList", player);
        }

        private static bool TryInvoke(MethodInfo method, string name, Player player, params object[] args)
        {
            if (method == null || player == null)
            {
                return false;
            }

            try
            {
                method.Invoke(player, args);
                return true;
            }
            catch (Exception ex)
            {
                Exception cause = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Plugin.WarnOnce("invoke-" + name, "Player." + name + " failed: " + cause.GetType().Name + ": " + cause.Message);
                return false;
            }
        }

        private static FieldInfo FindField(Type type, string name)
        {
            FieldInfo field = type.GetField(name, InstanceNonPublic);
            if (field == null)
            {
                Plugin.WarnOnce("field-" + name, "Player." + name + " was not found in this game version.");
            }

            return field;
        }

        private static MethodInfo FindMethod(Type type, string name, params Type[] parameters)
        {
            MethodInfo method = type.GetMethod(name, InstanceNonPublic, null, parameters, null);
            if (method == null)
            {
                Plugin.WarnOnce("method-" + name, "Player." + name + " was not found in this game version; falling back to direct writes.");
            }

            return method;
        }
    }
}
