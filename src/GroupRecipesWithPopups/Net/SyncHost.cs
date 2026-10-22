using System.Collections.Generic;
using GroupRecipesWithPopups.Core;

namespace GroupRecipesWithPopups.Net
{
    /// <summary>
    /// Server role (dedicated server or the hosting player): owns the persistent ledger file, answers catch-up
    /// requests. Fresh batches reach the other peers through the game's own routed-RPC forwarding for
    /// target "Everybody", so no explicit relay is needed.
    /// </summary>
    internal sealed class SyncHost
    {
        private readonly Ledger _ledger;
        private LedgerStore _store;

        public SyncHost(Ledger ledger)
        {
            _ledger = ledger;
        }

        public bool IsOpen => _store != null;

        /// <summary>Opens the ledger file of the current world once the world is identified; harmless to call repeatedly.</summary>
        public void EnsureOpen(ZNet znet)
        {
            if (_store != null || znet == null)
            {
                return;
            }

            string worldName = znet.GetWorldName();
            if (string.IsNullOrEmpty(worldName))
            {
                return;
            }

            _store = new LedgerStore(worldName, znet.GetWorldUID());
            _store.LoadInto(_ledger);
        }

        /// <summary>Appends entries that changed the ledger to the world file.</summary>
        public void Persist(IReadOnlyCollection<KnowledgeEntry> changed)
        {
            if (_store != null && changed.Count > 0)
            {
                _store.Append(changed);
            }
        }

        /// <summary>Sends the complete ledger to one peer (in packages of bounded size).</summary>
        public void SendCatchUp(ZRoutedRpc rpc, long target)
        {
            var snapshot = new List<KnowledgeEntry>(_ledger.Entries);
            foreach (ZPackage package in SyncProtocol.EncodeChunks(snapshot, bulk: false))
            {
                rpc.InvokeRoutedRPC(target, SyncProtocol.RpcCatchUp, package);
            }
        }
    }
}
