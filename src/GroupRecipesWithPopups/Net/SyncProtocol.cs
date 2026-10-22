using System;
using System.Collections.Generic;
using GroupRecipesWithPopups.Core;

namespace GroupRecipesWithPopups.Net
{
    /// <summary>
    /// RPC names and the <see cref="ZPackage"/> wire format. Payload layout:
    /// version byte, flags byte, entry count (int), then per entry: kind byte, key string, level int.
    /// </summary>
    internal static class SyncProtocol
    {
        /// <summary>Client to server: "I joined, please send me the ledger".</summary>
        public const string RpcHello = "GRWP_Hello";

        /// <summary>A batch of fresh discoveries; the server's router forwards it to every other peer.</summary>
        public const string RpcNew = "GRWP_New";

        /// <summary>Server to one client: (a chunk of) the complete ledger.</summary>
        public const string RpcCatchUp = "GRWP_CatchUp";

        public const byte Version = 1;

        /// <summary>The batch is a large upload (a player's whole knowledge): receivers cap the popups.</summary>
        public const byte FlagBulk = 1;

        /// <summary>The last package of a transmission.</summary>
        public const byte FlagFinal = 2;

        public const int MaxEntriesPerPackage = 500;

        private const int MaxAcceptedCount = 100000;

        /// <summary>Splits the entries into packages of at most <see cref="MaxEntriesPerPackage"/>; the last one carries <see cref="FlagFinal"/>.</summary>
        public static List<ZPackage> EncodeChunks(IReadOnlyList<KnowledgeEntry> entries, bool bulk)
        {
            var packages = new List<ZPackage>();
            int index = 0;
            do
            {
                int count = Math.Min(MaxEntriesPerPackage, entries.Count - index);
                bool last = index + count >= entries.Count;
                byte flags = (byte)((bulk ? FlagBulk : 0) | (last ? FlagFinal : 0));
                packages.Add(Encode(entries, index, count, flags));
                index += count;
            }
            while (index < entries.Count);

            return packages;
        }

        public static ZPackage Encode(IReadOnlyList<KnowledgeEntry> entries, int start, int count, byte flags)
        {
            var package = new ZPackage();
            package.Write(Version);
            package.Write(flags);
            package.Write(count);
            for (int i = start; i < start + count; i++)
            {
                KnowledgeEntry entry = entries[i];
                package.Write((byte)entry.Kind);
                package.Write(entry.Key);
                package.Write(entry.Level);
            }

            return package;
        }

        public static bool TryDecode(ZPackage package, out List<KnowledgeEntry> entries, out byte flags, out string error)
        {
            entries = new List<KnowledgeEntry>();
            flags = 0;
            error = null;
            if (package == null)
            {
                error = "package is null";
                return false;
            }

            try
            {
                byte version = package.ReadByte();
                if (version != Version)
                {
                    error = "unsupported protocol version " + version;
                    return false;
                }

                flags = package.ReadByte();
                int count = package.ReadInt();
                if (count < 0 || count > MaxAcceptedCount)
                {
                    error = "implausible entry count " + count;
                    return false;
                }

                for (int i = 0; i < count; i++)
                {
                    byte kindValue = package.ReadByte();
                    string key = package.ReadString();
                    int level = package.ReadInt();
                    if (kindValue < (byte)KnowledgeKind.Material || kindValue > (byte)KnowledgeKind.Station || string.IsNullOrEmpty(key))
                    {
                        error = "invalid entry #" + i;
                        entries.Clear();
                        return false;
                    }

                    entries.Add(new KnowledgeEntry((KnowledgeKind)kindValue, key, level));
                }

                return true;
            }
            catch (Exception ex)
            {
                entries.Clear();
                error = "malformed package: " + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }
    }
}
