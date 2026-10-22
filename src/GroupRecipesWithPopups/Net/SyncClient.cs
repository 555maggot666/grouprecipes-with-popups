using System;
using System.Collections.Generic;
using GroupRecipesWithPopups.Core;
using GroupRecipesWithPopups.Game;

namespace GroupRecipesWithPopups.Net
{
    /// <summary>How a batch of received entries is to be applied.</summary>
    internal enum ReceiveMode
    {
        /// <summary>One to a few fresh unlocks: every entry gets its popup.</summary>
        Live,

        /// <summary>Someone's whole knowledge arriving at once: popups are capped.</summary>
        Bulk,

        /// <summary>The world ledger after joining: popups are capped.</summary>
        CatchUp,
    }

    /// <summary>
    /// Client role: finds the local player's new discoveries and sends them, and schedules received entries
    /// for application through the <see cref="PopupPump"/>.
    /// </summary>
    internal sealed class SyncClient
    {
        private readonly struct Pending
        {
            public Pending(List<KnowledgeEntry> entries, ReceiveMode mode, bool final, long sender)
            {
                Entries = entries;
                Mode = mode;
                Final = final;
                Sender = sender;
            }

            public List<KnowledgeEntry> Entries { get; }

            public ReceiveMode Mode { get; }

            public bool Final { get; }

            public long Sender { get; }
        }

        private readonly Ledger _ledger;
        private readonly GameCatalog _catalog;
        private readonly PlayerKnowledgeBridge _bridge;
        private readonly VanillaUnlocker _unlocker;
        private readonly ModSettings _settings;
        private readonly Action<List<KnowledgeEntry>, bool> _send;
        private readonly KnowledgeSnapshot _snapshot = new KnowledgeSnapshot();
        private readonly List<Pending> _backlog = new List<Pending>();
        private readonly Dictionary<long, int> _popupBudgets = new Dictionary<long, int>();
        private PopupPump _pump;
        private double _pumpInterval;
        private bool _uploadedOnce;
        private int _hiddenTotal;
        private bool _noticeReady;

        public SyncClient(Ledger ledger, GameCatalog catalog, PlayerKnowledgeBridge bridge, VanillaUnlocker unlocker,
            ModSettings settings, Action<List<KnowledgeEntry>, bool> send)
        {
            _ledger = ledger;
            _catalog = catalog;
            _bridge = bridge;
            _unlocker = unlocker;
            _settings = settings;
            _send = send;
            _pumpInterval = settings.PopupIntervalSeconds.Value;
            _pump = new PopupPump(_pumpInterval);
        }

        /// <summary>
        /// Once per second: diffs the player's knowledge against the snapshot, records new entries immediately
        /// (loopback protection), stores them in the ledger and sends them.
        /// </summary>
        public void Tick(Player player)
        {
            if (player == null || !_catalog.Refresh())
            {
                return;
            }

            DrainBacklog();

            var current = new List<KnowledgeEntry>();
            if (!_bridge.TryRead(player, _catalog, current))
            {
                Plugin.WarnOnce("read-knowledge", "Could not read the player's knowledge; nothing is sent.");
                return;
            }

            var fresh = new List<KnowledgeEntry>();
            foreach (KnowledgeEntry entry in _snapshot.Diff(current))
            {
                if (_settings.IsKindEnabled(entry.Kind))
                {
                    fresh.Add(entry);
                }
            }

            bool firstUpload = !_uploadedOnce;
            _uploadedOnce = true;
            if (fresh.Count == 0)
            {
                return;
            }

            _snapshot.RecordRange(fresh);
            List<KnowledgeEntry> changed = _ledger.AddRange(fresh);
            if (changed.Count > 0)
            {
                bool bulk = firstUpload || changed.Count > _settings.MaxPopupsOnJoin.Value;
                _send(changed, bulk);
            }
        }

        /// <summary>
        /// Schedules received entries. Entries the ledger already held must have been filtered out by the caller;
        /// for the host's own catch-up the whole ledger is passed.
        /// </summary>
        public void Receive(List<KnowledgeEntry> entries, ReceiveMode mode, bool final, long sender)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                _backlog.Add(new Pending(entries, mode, final, sender));
                return;
            }

            var pending = new List<KnowledgeEntry>();
            foreach (KnowledgeEntry entry in entries)
            {
                _snapshot.Record(entry);
                if (_settings.IsKindEnabled(entry.Kind) && !_bridge.IsKnown(player, entry))
                {
                    pending.Add(entry);
                }
            }

            int cap = PopupPlanner.Unlimited;
            if (!_settings.EnablePopups.Value)
            {
                cap = 0;
            }
            else if (mode != ReceiveMode.Live)
            {
                if (!_popupBudgets.TryGetValue(sender, out int budget))
                {
                    budget = Math.Max(0, _settings.MaxPopupsOnJoin.Value);
                }

                cap = budget;
            }

            PopupPlan plan = PopupPlanner.BuildPlan(pending, cap);
            if (mode != ReceiveMode.Live && _settings.EnablePopups.Value)
            {
                if (final)
                {
                    _popupBudgets.Remove(sender);
                }
                else
                {
                    _popupBudgets[sender] = Math.Max(0, cap - plan.PopupCount);
                }
            }

            _pump.Enqueue(plan);
            _hiddenTotal += plan.HiddenCount;
            if (final && _hiddenTotal > 0)
            {
                _noticeReady = true;
            }
        }

        /// <summary>Every frame: hands out due steps, applies them and finally shows the summary line.</summary>
        public void Update(float deltaSeconds)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }

            if (_pump.IsEmpty && Math.Abs(_pumpInterval - _settings.PopupIntervalSeconds.Value) > 1e-6)
            {
                _pumpInterval = _settings.PopupIntervalSeconds.Value;
                _pump = new PopupPump(_pumpInterval);
            }

            _pump.Tick(deltaSeconds);
            _unlocker.Tick(player);
            int applied = 0;
            KnowledgeEntry entry;
            for (int guard = 0; guard < 500 && _pump.TryDequeueSilent(out entry); guard++)
            {
                if (_unlocker.Apply(player, entry, showPopup: false))
                {
                    applied++;
                }
            }

            if (_pump.TryDequeue(out entry, out bool popup) && _unlocker.Apply(player, entry, popup))
            {
                applied++;
            }

            if (applied > 0)
            {
                _unlocker.Refresh(player);
            }

            if (_noticeReady && _pump.IsEmpty)
            {
                ShowNotice(player);
            }
        }

        /// <summary>Forgets all per-session state (called when the connection / world changes).</summary>
        public void Reset()
        {
            _snapshot.Clear();
            _backlog.Clear();
            _popupBudgets.Clear();
            _pump.Clear();
            _unlocker.Reset();
            _uploadedOnce = false;
            _hiddenTotal = 0;
            _noticeReady = false;
        }

        private void DrainBacklog()
        {
            if (_backlog.Count == 0 || Player.m_localPlayer == null)
            {
                return;
            }

            var queued = new List<Pending>(_backlog);
            _backlog.Clear();
            foreach (Pending item in queued)
            {
                Receive(item.Entries, item.Mode, item.Final, item.Sender);
            }
        }

        private void ShowNotice(Player player)
        {
            int hidden = _hiddenTotal;
            _hiddenTotal = 0;
            _noticeReady = false;
            if (hidden > 0 && _settings.ShowNotifications.Value)
            {
                string noun = hidden == 1 ? "unlock was" : "unlocks were";
                player.Message(MessageHud.MessageType.TopLeft, "GroupRecipes: " + hidden + " more " + noun + " synchronised without popup");
            }
        }
    }
}
