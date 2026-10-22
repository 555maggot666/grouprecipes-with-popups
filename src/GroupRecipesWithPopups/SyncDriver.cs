using System;
using System.Collections.Generic;
using GroupRecipesWithPopups.Core;
using GroupRecipesWithPopups.Game;
using GroupRecipesWithPopups.Net;
using UnityEngine;

namespace GroupRecipesWithPopups
{
    /// <summary>
    /// Owns one sync session per connection (<see cref="ZRoutedRpc"/> instance): registers the RPCs, runs the
    /// 1 Hz tick, the per-frame popup pump and the join handshake. Every entry point is wrapped so that a failure
    /// can never escape into the game.
    /// </summary>
    internal sealed class SyncDriver : MonoBehaviour
    {
        private const float TickIntervalSeconds = 1f;
        private const int StableTicksBeforeFirstSync = 3;

        private GameCatalog _catalog;
        private PlayerKnowledgeBridge _bridge;
        private VanillaUnlocker _unlocker;

        private ZRoutedRpc _rpc;
        private ZNet _znet;
        private Ledger _ledger;
        private SyncHost _host;
        private SyncClient _client;
        private bool _helloDone;
        private float _tickTimer;
        private int _playerTicks;

        private void Awake()
        {
            _catalog = new GameCatalog();
            _bridge = new PlayerKnowledgeBridge();
            _unlocker = new VanillaUnlocker(_catalog, _bridge);
        }

        private void Update()
        {
            try
            {
                ZRoutedRpc rpc = ZRoutedRpc.instance;
                ZNet znet = ZNet.instance;
                if (rpc == null || znet == null)
                {
                    if (_rpc != null)
                    {
                        EndSession();
                    }

                    return;
                }

                if (!ReferenceEquals(rpc, _rpc) || !ReferenceEquals(znet, _znet))
                {
                    BeginSession(rpc, znet);
                }

                _host?.EnsureOpen(znet);
                float dt = Time.unscaledDeltaTime;
                _client?.Update(dt);

                _tickTimer += dt;
                if (_tickTimer >= TickIntervalSeconds)
                {
                    _tickTimer = 0f;
                    SyncTick();
                }
            }
            catch (Exception ex)
            {
                Failed("update", ex);
            }
        }

        private void BeginSession(ZRoutedRpc rpc, ZNet znet)
        {
            EndSession();
            _rpc = rpc;
            _znet = znet;
            _ledger = new Ledger();
            _host = znet.IsServer() ? new SyncHost(_ledger) : null;
            _client = znet.IsDedicated()
                ? null
                : new SyncClient(_ledger, _catalog, _bridge, _unlocker, Plugin.Settings, SendNew);
            _helloDone = false;
            _tickTimer = 0f;
            _playerTicks = 0;

            try
            {
                rpc.Register<ZPackage>(SyncProtocol.RpcHello, OnHello);
                rpc.Register<ZPackage>(SyncProtocol.RpcNew, OnNew);
                rpc.Register<ZPackage>(SyncProtocol.RpcCatchUp, OnCatchUp);
            }
            catch (Exception ex)
            {
                Failed("rpc registration", ex);
            }

            Plugin.Log.LogInfo("Sync session started (" + (_host != null ? "server role" : "client role") +
                               (_client == null ? ", no local player" : string.Empty) + ").");
        }

        private void EndSession()
        {
            _client?.Reset();
            _client = null;
            _host = null;
            _ledger = null;
            _rpc = null;
            _znet = null;
            _helloDone = false;
        }

        /// <summary>1 Hz: join handshake first, then report new local discoveries.</summary>
        private void SyncTick()
        {
            Player player = Player.m_localPlayer;
            if (_client == null || player == null)
            {
                _playerTicks = 0;
                return;
            }

            // Give the freshly spawned player a moment to finish loading its saved knowledge.
            if (++_playerTicks < StableTicksBeforeFirstSync || !_catalog.Refresh())
            {
                return;
            }

            if (!_helloDone)
            {
                _helloDone = true;
                if (_host != null)
                {
                    _host.EnsureOpen(_znet);
                    _client.Receive(new List<KnowledgeEntry>(_ledger.Entries), ReceiveMode.CatchUp, true, 0L);
                }
                else
                {
                    _rpc.InvokeRoutedRPC(SyncProtocol.RpcHello, SyncProtocol.Encode(new List<KnowledgeEntry>(), 0, 0, SyncProtocol.FlagFinal));
                }
            }

            _client.Tick(player);
        }

        /// <summary>Sends fresh local discoveries (called by the client). The server's router forwards them.</summary>
        private void SendNew(List<KnowledgeEntry> changed, bool bulk)
        {
            if (_host != null)
            {
                _host.EnsureOpen(_znet);
                _host.Persist(changed);
            }

            foreach (ZPackage package in SyncProtocol.EncodeChunks(changed, bulk))
            {
                _rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, SyncProtocol.RpcNew, package);
            }
        }

        private void OnHello(long sender, ZPackage package)
        {
            try
            {
                if (_host == null || _rpc == null || sender == ZNet.GetUID())
                {
                    return;
                }

                _host.EnsureOpen(_znet);
                _host.SendCatchUp(_rpc, sender);
            }
            catch (Exception ex)
            {
                Failed("HELLO handler", ex);
            }
        }

        private void OnNew(long sender, ZPackage package)
        {
            HandleEntries(sender, package, isCatchUp: false);
        }

        private void OnCatchUp(long sender, ZPackage package)
        {
            HandleEntries(sender, package, isCatchUp: true);
        }

        private void HandleEntries(long sender, ZPackage package, bool isCatchUp)
        {
            string where = isCatchUp ? "CATCHUP handler" : "NEW handler";
            try
            {
                if (_ledger == null || sender == ZNet.GetUID())
                {
                    return;
                }

                if (isCatchUp && _host != null)
                {
                    return;
                }

                if (!SyncProtocol.TryDecode(package, out List<KnowledgeEntry> entries, out byte flags, out string error))
                {
                    Plugin.WarnOnce(where + "-decode-" + error, where + ": ignoring bad package from " + sender + " (" + error + ")");
                    return;
                }

                // Entries the ledger already holds are ignored completely: that is what stops popup loops.
                List<KnowledgeEntry> changed = _ledger.AddRange(entries);
                if (_host != null)
                {
                    _host.EnsureOpen(_znet);
                    _host.Persist(changed);
                }

                ReceiveMode mode = isCatchUp
                    ? ReceiveMode.CatchUp
                    : (flags & SyncProtocol.FlagBulk) != 0 ? ReceiveMode.Bulk : ReceiveMode.Live;
                _client?.Receive(changed, mode, (flags & SyncProtocol.FlagFinal) != 0, sender);
            }
            catch (Exception ex)
            {
                Failed(where, ex);
            }
        }

        private static void Failed(string where, Exception ex)
        {
            Plugin.WarnOnce("failure-" + where + "-" + ex.GetType().Name + "-" + ex.Message,
                "GroupRecipes with Popups: " + where + " failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }
}
