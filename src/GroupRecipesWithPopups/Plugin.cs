using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace GroupRecipesWithPopups
{
    /// <summary>Entry point: binds the settings and starts the <see cref="SyncDriver"/>.</summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "555maggot666.GroupRecipesWithPopups";
        public const string PluginName = "GroupRecipes with Popups";
        public const string PluginVersion = "1.0.0";

        private static readonly HashSet<string> WarnedKeys = new HashSet<string>(StringComparer.Ordinal);

        internal static ManualLogSource Log { get; private set; }

        internal static ModSettings Settings { get; private set; }

        private void Awake()
        {
            Log = Logger;
            Settings = new ModSettings(Config);
            gameObject.AddComponent<SyncDriver>();
            Log.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        /// <summary>Logs a warning once per key, so that a recurring problem cannot flood the log.</summary>
        internal static void WarnOnce(string key, string message)
        {
            if (WarnedKeys.Add(key))
            {
                Log?.LogWarning(message);
            }
        }
    }
}
