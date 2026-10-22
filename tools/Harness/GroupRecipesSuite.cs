using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GroupRecipesWithPopups.Core;
using GroupRecipesWithPopups.Net;
using ValheimTestKit;
using UnityEngine;

namespace GroupRecipesWithPopups.Harness
{
    /// <summary>
    /// Scenarios for GroupRecipes with Popups, executed inside a live Valheim process by ValheimTestKit.
    ///
    /// This assembly is a test-only plugin: it is never published to Thunderstore and only exists so the mod's
    /// real code paths (wire format, ledger, popup planner, per-world file, server receive path) can be exercised
    /// with Unity types available.
    /// </summary>
    [TestKitSuite("grouprecipes")]
    public sealed class GroupRecipesSuite : TestSuite
    {
        private const long FakePeer = 987654321L;
        private const float SessionTimeoutSeconds = 240f;

        // Unique per run: the harness world keeps its ledger file across restarts, so fixed keys would be
        // deduplicated against the previous run (which is correct behaviour, but not what these scenarios test).
        private static readonly string RunTag = DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);

        public override string Name => "grouprecipes";

        public override IEnumerator Run()
        {
            SyncDriver driver = null;
            float until = Time.realtimeSinceStartup + SessionTimeoutSeconds;
            while (Time.realtimeSinceStartup < until)
            {
                driver = FindDriver();
                if (driver != null)
                {
                    break;
                }

                yield return null;
            }

            yield return Scenario("session-present", () =>
            {
                Assert(driver != null, "the mod's SyncDriver was not found after " + SessionTimeoutSeconds + "s");
                Assert(Reflect.Get(driver, "_ledger") is Ledger, "SyncDriver has no ledger");
                Detail("world: " + ZNet.instance.GetWorldName() + ", dedicated=" + ZNet.instance.IsDedicated());
            });

            yield return Scenario("wire-roundtrip", () =>
            {
                List<KnowledgeEntry> source = MakeEntries(1200);
                List<ZPackage> packages = SyncProtocol.EncodeChunks(source, bulk: false);
                Assert(packages.Count == 3, "expected 3 packages for 1200 entries, got " + packages.Count);

                var decoded = new List<KnowledgeEntry>();
                for (int i = 0; i < packages.Count; i++)
                {
                    // Encoding leaves the package at its end; the network layer hands the reader a fresh one.
                    RpcSynth.Rewind(packages[i]);
                    Assert(SyncProtocol.TryDecode(packages[i], out List<KnowledgeEntry> chunk, out byte flags, out string error),
                        "package " + i + " failed to decode: " + error);
                    Assert(((flags & SyncProtocol.FlagFinal) != 0) == (i == packages.Count - 1),
                        "final flag wrong on package " + i);
                    decoded.AddRange(chunk);
                }

                Assert(decoded.Count == source.Count, "entry count changed: " + decoded.Count + " != " + source.Count);
                for (int i = 0; i < source.Count; i++)
                {
                    Assert(decoded[i].Equals(source[i]), "entry " + i + " differs after roundtrip");
                }
            });

            yield return Scenario("wire-bulk-flag", () =>
            {
                List<ZPackage> packages = SyncProtocol.EncodeChunks(MakeEntries(500), bulk: true);
                RpcSynth.Rewind(packages[0]);
                Assert(SyncProtocol.TryDecode(packages[0], out _, out byte flags, out string error), "decode failed: " + error);
                Assert((flags & SyncProtocol.FlagBulk) != 0, "bulk flag missing");
            });

            yield return Scenario("wire-rejects-wrong-version", () =>
            {
                var package = new ZPackage();
                package.Write((byte)99);
                package.Write((byte)0);
                package.Write(0);
                RpcSynth.Rewind(package);
                Assert(!SyncProtocol.TryDecode(package, out _, out _, out _), "a package with an unknown version was accepted");
            });

            yield return Scenario("ledger-dedupe", () =>
            {
                var ledger = new Ledger();
                List<KnowledgeEntry> batch = MakeEntries(30);
                Assert(ledger.AddRange(batch).Count == 30, "first insert should add all 30");
                Assert(ledger.AddRange(batch).Count == 0, "second insert should add nothing");
                AssertEqual(30, ledger.Count, "ledger count");
            });

            yield return Scenario("ledger-station-upgrade", () =>
            {
                var ledger = new Ledger();
                ledger.Add(new KnowledgeEntry(KnowledgeKind.Station, "piece_workbench", 2));
                ledger.Add(new KnowledgeEntry(KnowledgeKind.Station, "piece_workbench", 1));
                Assert(ledger.TryGetStationLevel("piece_workbench", out int level), "station level lookup failed");
                AssertEqual(2, level, "station level must keep the highest value");
            });

            yield return Scenario("planner-cap", () =>
            {
                PopupPlan plan = PopupPlanner.BuildPlan(MakeEntries(100), maxPopups: 25);
                AssertEqual(100, plan.Steps.Count, "every pending entry must stay in the plan");
                AssertEqual(25, plan.PopupCount, "popups");
                AssertEqual(75, plan.HiddenCount, "silent steps");
            });

            yield return Scenario("planner-unlimited", () =>
            {
                PopupPlan plan = PopupPlanner.BuildPlan(MakeEntries(40), maxPopups: PopupPlanner.Unlimited);
                AssertEqual(40, plan.PopupCount, "popups with no cap");
                AssertEqual(0, plan.HiddenCount, "silent steps with no cap");
            });

            yield return Scenario("planner-no-popups", () =>
            {
                PopupPlan plan = PopupPlanner.BuildPlan(MakeEntries(10), maxPopups: 0);
                AssertEqual(0, plan.PopupCount, "popups with cap 0");
                AssertEqual(10, plan.HiddenCount, "steps with cap 0");
            });

            yield return Scenario("file-roundtrip", () =>
            {
                string world = "harness-" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
                var store = new LedgerStore(world, 4242L);
                if (File.Exists(store.Path))
                {
                    File.Delete(store.Path);
                }

                List<KnowledgeEntry> batch = MakeEntries(50);
                store.Append(batch);

                var reloaded = new Ledger();
                new LedgerStore(world, 4242L).LoadInto(reloaded);
                AssertEqual(50, reloaded.Count, "entries after reload");
                Assert(reloaded.Contains(batch[49]), "last entry missing after reload");
                Detail("ledger file: " + store.Path);
                File.Delete(store.Path);
            });

            yield return Scenario("file-survives-garbage-line", () =>
            {
                string world = "harness-garbage-" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
                var store = new LedgerStore(world, 4343L);
                if (File.Exists(store.Path))
                {
                    File.Delete(store.Path);
                }

                store.Append(MakeEntries(20));
                File.AppendAllText(store.Path, "this is not a ledger line\n");

                var reloaded = new Ledger();
                new LedgerStore(world, 4343L).LoadInto(reloaded);
                AssertEqual(20, reloaded.Count, "valid entries must survive a corrupt line");
                File.Delete(store.Path);
            });

            if (driver == null || Reflect.Get(driver, "_ledger") is not Ledger ledger)
            {
                Skip("server-receive-path", "no live ledger available");
                yield break;
            }

            yield return Scenario("server-receive-path", () =>
            {
                int before = ledger.Count;
                ZPackage package = SyncProtocol.EncodeChunks(LiveEntries(40, "recv"), bulk: false)[0];
                RpcSynth.Rewind(package);
                Assert(RpcSynth.Deliver(SyncProtocol.RpcNew, package, FakePeer), "no handler registered for " + SyncProtocol.RpcNew);
                AssertEqual(before + 40, ledger.Count, "server ledger grew by the injected batch");
                Detail("server ledger: " + before + " -> " + ledger.Count);
            });

            yield return Scenario("server-receive-duplicate-dropped", () =>
            {
                int before = ledger.Count;
                ZPackage package = SyncProtocol.EncodeChunks(LiveEntries(40, "recv"), bulk: false)[0];
                RpcSynth.Rewind(package);
                RpcSynth.Deliver(SyncProtocol.RpcNew, package, FakePeer);
                AssertEqual(before, ledger.Count, "a resent batch must not change the ledger");
            });

            yield return Scenario("server-ignores-own-sender", () =>
            {
                int before = ledger.Count;
                ZPackage package = SyncProtocol.EncodeChunks(LiveEntries(10, "self"), bulk: false)[0];
                RpcSynth.Rewind(package);
                RpcSynth.Deliver(SyncProtocol.RpcNew, package, ZNet.GetUID());
                AssertEqual(before, ledger.Count, "a batch from our own uid must be ignored");
            });

            yield return Scenario("server-ignores-broken-package", () =>
            {
                int before = ledger.Count;
                var package = new ZPackage();
                package.Write((byte)7);
                package.Write((byte)7);
                package.Write(3);
                package.Write("only half an entry");
                RpcSynth.Rewind(package);
                RpcSynth.Deliver(SyncProtocol.RpcNew, package, FakePeer);
                AssertEqual(before, ledger.Count, "a broken package must not change the ledger");
            });

            yield return Scenario("server-persists-to-world-file", () =>
            {
                object host = Reflect.Get(driver, "_host");
                if (host == null)
                {
                    Skip("server-persists-to-world-file", "not the server role");
                    return Empty();
                }

                Assert(Reflect.Get(host, "_store") != null, "the world ledger file was never opened");
                var reloaded = new Ledger();
                new LedgerStore(ZNet.instance.GetWorldName(), ZNet.instance.GetWorldUID()).LoadInto(reloaded);
                Assert(reloaded.Count >= ledger.Count,
                    "world file holds " + reloaded.Count + " entries, ledger has " + ledger.Count);
                Detail("world file entries: " + reloaded.Count + " (ledger " + ledger.Count + ")");
                return Empty();
            });
        }

        private static SyncDriver FindDriver()
        {
            try
            {
                Type type = Reflect.FindType("GroupRecipesWithPopups.SyncDriver");
                return Reflect.FindComponent(type) as SyncDriver;
            }
            catch (TypeLoadException)
            {
                return null;
            }
        }

        private static List<KnowledgeEntry> MakeEntries(int count)
        {
            var list = new List<KnowledgeEntry>(count);
            for (int i = 0; i < count; i++)
            {
                KnowledgeKind kind = (i % 3) == 0
                    ? KnowledgeKind.Material
                    : (i % 3) == 1 ? KnowledgeKind.Recipe : KnowledgeKind.Piece;
                list.Add(new KnowledgeEntry(kind, "harness_" + kind + "_" + i.ToString(CultureInfo.InvariantCulture), 0));
            }

            return list;
        }

        private static List<KnowledgeEntry> LiveEntries(int count, string prefix)
        {
            var list = new List<KnowledgeEntry>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(new KnowledgeEntry(KnowledgeKind.Material,
                    "harness_" + prefix + "_" + RunTag + "_" + i.ToString(CultureInfo.InvariantCulture), 0));
            }

            return list;
        }
    }
}
