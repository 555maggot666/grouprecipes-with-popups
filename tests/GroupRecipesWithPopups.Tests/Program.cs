using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GroupRecipesWithPopups.Core;

namespace GroupRecipesWithPopups.Tests
{
    /// <summary>Minimal self-contained test runner: one "PASS name" or "FAIL name :: detail" line per case.</summary>
    internal static class Program
    {
        private sealed class CheckFailed : Exception
        {
            public CheckFailed(string message) : base(message)
            {
            }
        }

        private static readonly List<(string Name, Action Body)> Cases = new List<(string, Action)>
        {
            ("cap-40-25", CapFortyTwentyFive),
            ("live-no-cap", LiveNoCap),
            ("order-material-first", OrderMaterialFirst),
            ("dedupe-ledger", DedupeLedger),
            ("file-roundtrip", FileRoundtrip),
            ("station-max-only", StationMaxOnly),
            ("snapshot-loopback", SnapshotLoopback),
            ("pump-pacing", PumpPacing),
            ("snapshot-diff-station-level", SnapshotDiffStationLevel),
            ("plan-popups-disabled-keeps-all", PlanPopupsDisabled),
            ("file-corrupt-lines-skipped", FileCorruptLines),
            ("file-unsupported-version-readonly", FileUnsupportedVersion),
            ("file-append-repairs-missing-newline", FileMissingNewline),
            ("file-station-upgrade-appended", FileStationUpgrade),
            ("pump-silent-drain", PumpSilentDrain),
            ("paths-sanitize", PathsSanitize),
            ("version-consistency", VersionConsistency),
        };

        private static int Main()
        {
            int failed = 0;
            foreach ((string name, Action body) in Cases)
            {
                try
                {
                    body();
                    Console.WriteLine("PASS " + name);
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine("FAIL " + name + " :: " + ex.Message.Replace('\n', ' ').Replace('\r', ' '));
                }
            }

            Console.WriteLine((Cases.Count - failed) + "/" + Cases.Count + " passed");
            return failed == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------- helpers

        private static void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new CheckFailed(message);
            }
        }

        private static void AreEqual<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new CheckFailed(what + ": expected <" + expected + "> but was <" + actual + ">");
            }
        }

        private static KnowledgeEntry M(string key) => new KnowledgeEntry(KnowledgeKind.Material, key);

        private static KnowledgeEntry R(string key) => new KnowledgeEntry(KnowledgeKind.Recipe, key);

        private static KnowledgeEntry P(string key) => new KnowledgeEntry(KnowledgeKind.Piece, key);

        private static KnowledgeEntry S(string key, int level) => new KnowledgeEntry(KnowledgeKind.Station, key, level);

        private static List<KnowledgeEntry> Forty()
        {
            var list = new List<KnowledgeEntry>();
            for (int i = 0; i < 40; i++)
            {
                switch (i % 4)
                {
                    case 0: list.Add(M("$item_m" + i)); break;
                    case 1: list.Add(R("$item_r" + i)); break;
                    case 2: list.Add(P("$piece_p" + i)); break;
                    default: list.Add(S("$piece_s" + i, 1 + i % 5)); break;
                }
            }

            return list;
        }

        private static string TempFile()
        {
            string dir = Path.Combine(Path.GetTempPath(), "grwp-tests-" + Guid.NewGuid().ToString("N"));
            return Path.Combine(dir, "nested", "ledger.txt");
        }

        private static void Cleanup(string file)
        {
            string root = Path.GetDirectoryName(Path.GetDirectoryName(file));
            if (root != null && Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }

        // ---------------------------------------------------------------- required cases

        private static void CapFortyTwentyFive()
        {
            List<KnowledgeEntry> pending = Forty();
            PopupPlan plan = PopupPlanner.BuildPlan(pending, 25);
            AreEqual(40, plan.Steps.Count, "steps");
            AreEqual(25, plan.Steps.Count(s => s.ShowPopup), "popup steps");
            AreEqual(15, plan.Steps.Count(s => !s.ShowPopup), "silent steps");
            AreEqual(15, plan.HiddenCount, "HiddenCount");
            AreEqual(25, plan.PopupCount, "PopupCount");
            var planned = new HashSet<KnowledgeEntry>(plan.Steps.Select(s => s.Entry));
            Check(pending.All(planned.Contains), "an entry is missing from the plan (data loss)");
        }

        private static void LiveNoCap()
        {
            var pending = new List<KnowledgeEntry> { M("$item_a"), R("$item_b"), S("$piece_c", 2) };
            PopupPlan plan = PopupPlanner.BuildPlan(pending, PopupPlanner.Unlimited);
            AreEqual(3, plan.Steps.Count, "steps");
            AreEqual(3, plan.Steps.Count(s => s.ShowPopup), "popups");
            AreEqual(0, plan.Steps.Count(s => !s.ShowPopup), "silent");
            AreEqual(0, plan.HiddenCount, "HiddenCount");
        }

        private static void OrderMaterialFirst()
        {
            var pending = new List<KnowledgeEntry>
            {
                S("$piece_forge", 2), R("$item_axe"), M("$item_copper"), P("$piece_bench"),
                M("$item_tin"), S("$piece_cauldron", 1), R("$item_bow"),
            };
            PopupPlan plan = PopupPlanner.BuildPlan(pending, 25);
            int Rank(KnowledgeKind k) => k == KnowledgeKind.Material ? 0 : k == KnowledgeKind.Station ? 2 : 1;
            int[] ranks = plan.Steps.Select(s => Rank(s.Entry.Kind)).ToArray();
            Check(ranks.SequenceEqual(ranks.OrderBy(r => r)), "plan order is not Material < Recipe/Piece < Station: " +
                                                              string.Join(",", plan.Steps.Select(s => s.Entry.ToString())));
            AreEqual(7, plan.Steps.Count, "steps");
            AreEqual(KnowledgeKind.Material, plan.Steps[0].Entry.Kind, "first kind");
            AreEqual(KnowledgeKind.Station, plan.Steps[plan.Steps.Count - 1].Entry.Kind, "last kind");
        }

        private static void DedupeLedger()
        {
            var ledger = new Ledger();
            Check(ledger.Add(M("$item_copper")), "first add must be a change");
            Check(!ledger.Add(M("$item_copper")), "second add must not be a change");
            AreEqual(1, ledger.Count, "Count after Add twice");

            var ledger2 = new Ledger();
            List<KnowledgeEntry> changed = ledger2.AddRange(new[] { R("$item_axe"), R("$item_axe") });
            AreEqual(1, ledger2.Count, "Count after AddRange with duplicate");
            AreEqual(1, changed.Count, "changed entries");

            PopupPlan plan = PopupPlanner.BuildPlan(new[] { R("$item_axe"), R("$item_axe") }, 25);
            AreEqual(1, plan.Steps.Count, "plan steps for duplicate entry");

            // The same key under a different kind is a different discovery.
            Check(ledger.Add(R("$item_copper")), "same key, other kind is a separate entry");
            AreEqual(2, ledger.Count, "Count with two kinds");
        }

        private static void FileRoundtrip()
        {
            string file = TempFile();
            try
            {
                var entries = new List<KnowledgeEntry>
                {
                    M("$item_tab\tkey"),
                    R("line\nbreak and\r\ncarriage"),
                    P("back\\slash and \\t literal"),
                    M("Umlaute äöüß ÄÖÜ"),
                    R("Emoji \U0001F528\U0001F3F9 end"),
                    S("$piece_station\twith\\odd\nchars \U0001F525", 7),
                    S("$piece_workbench", 3),
                };

                var written = new Ledger();
                var store = new LedgerFile(file);
                Check(store.Append(written.AddRange(entries)), "first append wrote nothing");

                var reloaded = new Ledger();
                LedgerParseResult result = new LedgerFile(file).Load(reloaded);
                AreEqual(0, result.Warnings.Count, "warnings on reload: " + string.Join("|", result.Warnings));
                AreEqual(entries.Count, reloaded.Count, "reloaded count");
                Check(new HashSet<KnowledgeEntry>(entries).SetEquals(reloaded.Entries), "reloaded set differs from written set");

                // Append-only: the ledger dedupes, so a second append of the same data writes nothing.
                string before = File.ReadAllText(file);
                bool wroteAgain = store.Append(written.AddRange(entries));
                string after = File.ReadAllText(file);
                Check(!wroteAgain, "second append reported a write");
                AreEqual(before, after, "file content after second append");
                AreEqual(entries.Count + 1, File.ReadAllLines(file).Length, "line count (header + entries)");
                AreEqual(LedgerFileFormat.Header, File.ReadAllLines(file)[0], "header line");

                // New data is only appended behind the existing lines.
                Check(store.Append(written.AddRange(new[] { M("$item_new") })), "append of new entry");
                string grown = File.ReadAllText(file);
                Check(grown.StartsWith(before, StringComparison.Ordinal), "existing content was modified");
                AreEqual(before.Length + LedgerFileFormat.FormatLine(M("$item_new")).Length + 1, grown.Length, "grown length");
            }
            finally
            {
                Cleanup(file);
            }
        }

        private static void StationMaxOnly()
        {
            var ledger = new Ledger();
            ledger.Add(S("$piece_workbench", 2));

            Check(!ledger.Add(S("$piece_workbench", 1)), "lower level must not count as change");
            Check(ledger.TryGetStationLevel("$piece_workbench", out int level), "station level missing");
            AreEqual(2, level, "level after incoming 1");

            Check(ledger.Add(S("$piece_workbench", 4)), "higher level must count as change");
            ledger.TryGetStationLevel("$piece_workbench", out level);
            AreEqual(4, level, "level after incoming 4");
            AreEqual(1, ledger.Count, "ledger count");

            Check(!ledger.Add(S("$piece_workbench", 4)), "same level must not count as change");
            Check(ledger.Contains(S("$piece_workbench", 3)), "Contains(level 3) must be covered by 4");
            Check(!ledger.Contains(S("$piece_workbench", 5)), "Contains(level 5) must not be covered by 4");
            AreEqual(4, ledger.Entries[0].Level, "entry level in list");
        }

        private static void SnapshotLoopback()
        {
            var snapshot = new KnowledgeSnapshot();
            var received = new List<KnowledgeEntry> { M("$item_a"), R("$item_b"), P("$piece_c"), S("$piece_d", 3) };
            foreach (KnowledgeEntry entry in received)
            {
                snapshot.Record(entry);
            }

            AreEqual(0, snapshot.Diff(received).Count, "diff after record");

            var withNew = new List<KnowledgeEntry>(received) { M("$item_fresh") };
            List<KnowledgeEntry> diff = snapshot.Diff(withNew);
            AreEqual(1, diff.Count, "diff with one new entry");
            AreEqual(M("$item_fresh"), diff[0], "new entry");
        }

        private static void PumpPacing()
        {
            PopupPlan plan = PopupPlanner.BuildPlan(Forty(), 25);
            var pump = new PopupPump(plan, 0.3);

            pump.Tick(0.29);
            Check(!pump.TryDequeue(out _, out _), "an element was delivered after Tick(0.29)");

            var delivered = new List<(KnowledgeEntry Entry, bool Popup)>();
            for (int i = 0; i < 40; i++)
            {
                pump.Tick(0.3);
                if (pump.TryDequeue(out KnowledgeEntry entry, out bool popup))
                {
                    delivered.Add((entry, popup));
                }
            }

            AreEqual(40, delivered.Count, "delivered after 40 ticks");
            AreEqual(25, delivered.Count(d => d.Popup), "delivered with popup");
            Check(pump.IsEmpty, "pump not empty at the end");
            AreEqual(40, delivered.Select(d => d.Entry).Distinct().Count(), "distinct delivered entries");
        }

        // ---------------------------------------------------------------- additional cases

        private static void SnapshotDiffStationLevel()
        {
            var snapshot = new KnowledgeSnapshot();
            snapshot.Record(S("$piece_workbench", 2));
            AreEqual(0, snapshot.Diff(new[] { S("$piece_workbench", 2) }).Count, "equal level");
            AreEqual(0, snapshot.Diff(new[] { S("$piece_workbench", 1) }).Count, "lower level");
            List<KnowledgeEntry> raised = snapshot.Diff(new[] { S("$piece_workbench", 3), S("$piece_workbench", 5) });
            AreEqual(1, raised.Count, "raised count");
            AreEqual(5, raised[0].Level, "highest level wins");
            snapshot.Record(S("$piece_workbench", 1));
            AreEqual(1, snapshot.Diff(new[] { S("$piece_workbench", 3) }).Count, "record must not lower the level");
        }

        private static void PlanPopupsDisabled()
        {
            PopupPlan plan = PopupPlanner.BuildPlan(Forty(), 0);
            AreEqual(40, plan.Steps.Count, "steps");
            AreEqual(0, plan.PopupCount, "popups");
            AreEqual(40, plan.HiddenCount, "hidden");
            PopupPlan negative = PopupPlanner.BuildPlan(Forty(), -5);
            AreEqual(0, negative.PopupCount, "negative cap behaves like zero");
        }

        private static void FileCorruptLines()
        {
            string file = TempFile();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, string.Join("\n",
                    LedgerFileFormat.Header,
                    "Material\t$item_ok\t0",
                    "garbage without tabs",
                    "Bogus\t$item_x\t0",
                    "Recipe\t\\x\t0",
                    "Station\t$piece_bench\t-3",
                    "Station\t$piece_bench\tabc",
                    "Recipe\t\t0",
                    "# a comment",
                    "",
                    "Station\t$piece_bench\t2") + "\n");

                var ledger = new Ledger();
                LedgerParseResult result = new LedgerFile(file).Load(ledger);
                AreEqual(2, ledger.Count, "valid entries");
                AreEqual(6, result.Warnings.Count, "warnings: " + string.Join(" | ", result.Warnings));
                Check(ledger.Contains(M("$item_ok")), "valid material missing");
                Check(ledger.Contains(S("$piece_bench", 2)), "valid station missing");
            }
            finally
            {
                Cleanup(file);
            }
        }

        private static void FileUnsupportedVersion()
        {
            string file = TempFile();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                const string content = "#grwp-ledger v2\nMaterial\t$item_a\t0\n";
                File.WriteAllText(file, content);
                var ledger = new Ledger();
                var store = new LedgerFile(file);
                LedgerParseResult result = store.Load(ledger);
                Check(!result.IsSupported, "version 2 reported as supported");
                AreEqual(0, ledger.Count, "ledger count");
                Check(!store.Append(new[] { M("$item_b") }), "append to unsupported file must be refused");
                AreEqual(content, File.ReadAllText(file), "unsupported file was modified");
            }
            finally
            {
                Cleanup(file);
            }
        }

        private static void FileMissingNewline()
        {
            string file = TempFile();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, LedgerFileFormat.Header + "\nMaterial\t$item_a\t0", new UTF8Encoding(false));
                var ledger = new Ledger();
                var store = new LedgerFile(file);
                store.Load(ledger);
                Check(store.Append(ledger.AddRange(new[] { M("$item_b") })), "append failed");
                var reloaded = new Ledger();
                LedgerParseResult result = new LedgerFile(file).Load(reloaded);
                AreEqual(0, result.Warnings.Count, "warnings: " + string.Join("|", result.Warnings));
                AreEqual(2, reloaded.Count, "reloaded count");
            }
            finally
            {
                Cleanup(file);
            }
        }

        private static void FileStationUpgrade()
        {
            string file = TempFile();
            try
            {
                var ledger = new Ledger();
                var store = new LedgerFile(file);
                store.Append(ledger.AddRange(new[] { S("$piece_workbench", 1) }));
                store.Append(ledger.AddRange(new[] { S("$piece_workbench", 3) }));
                store.Append(ledger.AddRange(new[] { S("$piece_workbench", 2) }));
                AreEqual(3, File.ReadAllLines(file).Length, "header + two station lines");

                var reloaded = new Ledger();
                new LedgerFile(file).Load(reloaded);
                AreEqual(1, reloaded.Count, "reloaded count");
                Check(reloaded.TryGetStationLevel("$piece_workbench", out int level), "station missing");
                AreEqual(3, level, "reloaded level");
            }
            finally
            {
                Cleanup(file);
            }
        }

        private static void PumpSilentDrain()
        {
            PopupPlan plan = PopupPlanner.BuildPlan(new[] { M("$item_a"), M("$item_b"), M("$item_c") }, 1);
            var pump = new PopupPump(plan, 0.3);
            Check(!pump.TryDequeueSilent(out _), "head is a popup step, must not be drained silently");
            pump.Tick(0.3);
            Check(pump.TryDequeue(out _, out bool popup) && popup, "first step must be a popup");
            Check(pump.TryDequeueSilent(out _), "second step is silent");
            Check(pump.TryDequeueSilent(out _), "third step is silent");
            Check(!pump.TryDequeueSilent(out _), "queue must be empty");
            Check(pump.IsEmpty, "pump must be empty");

            var all = new PopupPump(PopupPlanner.BuildPlan(new[] { M("$item_a") }, 0), 0.3);
            Check(!all.TryDequeue(out _, out _), "paced dequeue before the interval");
            Check(all.TryDequeueSilent(out KnowledgeEntry entry), "silent step available without pacing");
            AreEqual(M("$item_a"), entry, "silent entry");
        }

        private static void PathsSanitize()
        {
            AreEqual("Midgard_123.txt", GrwpPaths.LedgerFileName("Midgard", 123), "plain name");
            AreEqual("a_b_c_d", GrwpPaths.SanitizeFileComponent("a/b\\c:d"), "separators");
            AreEqual("world", GrwpPaths.SanitizeFileComponent("   "), "blank");
            AreEqual("world", GrwpPaths.SanitizeFileComponent(null), "null");
            AreEqual("world", GrwpPaths.SanitizeFileComponent("..."), "dots only");
            AreEqual("_etc_passwd", GrwpPaths.SanitizeFileComponent("../etc/passwd"), "traversal neutralised");
            Check(!GrwpPaths.SanitizeFileComponent("../../x").Contains("/"), "traversal keeps a separator");
            Check(GrwpPaths.SanitizeFileComponent(new string('x', 500)).Length <= 64, "length cap");
            AreEqual("Welt_ÄÖ_-42.txt", GrwpPaths.LedgerFileName("Welt_ÄÖ", -42), "umlauts and negative uid");
        }

        private static void VersionConsistency()
        {
            string dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "GroupRecipesWithPopups.sln")))
            {
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            }

            Check(dir != null, "repo root with GroupRecipesWithPopups.sln not found");
            string plugin = File.ReadAllText(Path.Combine(dir, "src/GroupRecipesWithPopups/Plugin.cs"));
            System.Text.RegularExpressions.Match match =
                System.Text.RegularExpressions.Regex.Match(plugin, "PluginVersion\\s*=\\s*\"([^\"]+)\"");
            Check(match.Success, "PluginVersion constant not found in Plugin.cs");
            string version = match.Groups[1].Value;

            string manifest = File.ReadAllText(Path.Combine(dir, "manifest.json"));
            Check(manifest.Contains("\"version_number\": \"" + version + "\""),
                "manifest.json version_number differs from PluginVersion " + version);

            string pluginProject = File.ReadAllText(Path.Combine(dir, "src/GroupRecipesWithPopups/GroupRecipesWithPopups.csproj"));
            Check(pluginProject.Contains("<Version>" + version + "</Version>"),
                "plugin csproj Version differs from PluginVersion " + version);

            string coreProject = File.ReadAllText(Path.Combine(dir, "src/GroupRecipesWithPopups.Core/GroupRecipesWithPopups.Core.csproj"));
            Check(coreProject.Contains("<Version>" + version + "</Version>"),
                "core csproj Version differs from PluginVersion " + version);
        }
    }
}
