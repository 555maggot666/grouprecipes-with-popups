using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using GroupRecipesWithPopups.Core;

namespace GroupRecipesWithPopups.Net
{
    /// <summary>
    /// Persistence of the world ledger: <c>BepInEx/config/GroupRecipesWithPopups/&lt;world&gt;_&lt;uid&gt;.txt</c>,
    /// append-only. Disk problems are logged and never propagate.
    /// </summary>
    internal sealed class LedgerStore
    {
        private readonly LedgerFile _file;

        public LedgerStore(string worldName, long worldUid)
        {
            string path = System.IO.Path.Combine(Paths.ConfigPath, "GroupRecipesWithPopups",
                GrwpPaths.LedgerFileName(worldName, worldUid));
            _file = new LedgerFile(path);
        }

        public string Path => _file.Path;

        /// <summary>
        /// Loads the file into <paramref name="ledger"/>. Entries that the ledger already held in memory but the
        /// file lacked (for example collected before the world was identified) are appended to the file.
        /// </summary>
        public void LoadInto(Ledger ledger)
        {
            try
            {
                var fromFile = new Ledger();
                LedgerParseResult result = _file.Load(fromFile);
                foreach (string warning in result.Warnings)
                {
                    Plugin.Log.LogWarning("Ledger file " + _file.Path + ": " + warning);
                }

                if (!result.IsSupported)
                {
                    Plugin.Log.LogWarning("Ledger file " + _file.Path + " has an unsupported version; it is left untouched and not used for persistence.");
                }

                List<KnowledgeEntry> missingInFile = fromFile.AddRange(ledger.Entries);
                ledger.AddRange(fromFile.Entries);
                if (missingInFile.Count > 0)
                {
                    Append(missingInFile);
                }

                Plugin.Log.LogInfo("Ledger " + _file.Path + " loaded: " + ledger.Count + " entries.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Could not read ledger file " + _file.Path + ": " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        public void Append(IReadOnlyCollection<KnowledgeEntry> entries)
        {
            if (entries.Count == 0)
            {
                return;
            }

            try
            {
                _file.Append(entries);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Plugin.WarnOnce("ledger-write-" + ex.GetType().Name,
                    "Could not write ledger file " + _file.Path + ": " + ex.GetType().Name + ": " + ex.Message);
            }
            catch (Exception ex)
            {
                Plugin.WarnOnce("ledger-write-other", "Unexpected error while writing " + _file.Path + ": " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
