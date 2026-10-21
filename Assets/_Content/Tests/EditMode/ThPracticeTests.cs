using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using R3;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    /// <summary>練習（#43）: 色の割り当て・練習用お宝・案内の段階・ヒント・アバターの色・プレーヤーの色の一覧</summary>
    public class ThPracticeTests
    {
        static readonly Bounds Zone = new Bounds(new Vector3(0, 0.9f, 0.8f), new Vector3(3f, 1.2f, 1f));
        static readonly Vector3[] SlotPos = { new Vector3(-1.05f, 0, 2.3f), new Vector3(-0.35f, 0, 2.3f), new Vector3(0.35f, 0, 2.3f), new Vector3(1.05f, 0, 2.3f) };

        static ThTreasureSetup MakeSetup(bool withSlots = true)
        {
            var pts = new List<PlacementPoint>();
            for (int i = 0; i < 30; i++) pts.Add(new PlacementPoint(new Vector3((i % 5) * 0.75f - 1.5f, 0, 3.5f + i / 5), i % 3));
            var setup = new ThTreasureSetup
            {
                Points = pts,
                Catalog = new[] { new SpawnCatalogEntry(TreasureKind.S, 0), new SpawnCatalogEntry(TreasureKind.S, 1), new SpawnCatalogEntry(TreasureKind.M, 2) },
                CollectBounds = Zone,
                CatalogHash = "h1",
            };
            if (withSlots) setup.PracticeSlots = SlotPos.Select(p => new PracticeSlot(p, 180f)).ToList();
            return setup;
        }

        // ---- 色の割り当て（純粋） ----

        [Test] public void 色はDeviceIdの文字列順に0から割り当てる()
        {
            var m = PracticeColors.Assign(new[] { "devC", "devA", "devB" });
            Assert.AreEqual(0, m["devA"]); Assert.AreEqual(1, m["devB"]); Assert.AreEqual(2, m["devC"]);
        }

        [Test] public void 色は4つまで_5台目以降は色なし_空や重複は無視()
        {
            var m = PracticeColors.Assign(new[] { "e", "d", "c", "b", "a", "a", "", null });
            Assert.AreEqual(4, m.Count);
            Assert.IsFalse(m.ContainsKey("e"));
            Assert.AreEqual(3, m["d"]);
        }

        [Test] public void tutorialColorsはparticipantsと同じ順で割り当てが無ければ_1()
        {
            var colors = new Dictionary<string, int> { { "b", 1 }, { "a", 0 } };
            CollectionAssert.AreEqual(new[] { 1, 0, -1 }, PracticeColors.ToArray(new[] { "b", "a", "z" }, colors));
            Assert.AreEqual(0, PracticeColors.ToArray(new[] { "b" }, new Dictionary<string, int>()).Length, "割り当て前は空");
            var read = PracticeColors.Read(new[] { "b", "a", "z" }, new[] { 1, 0, -1 });
            Assert.AreEqual(2, read.Count); Assert.AreEqual(1, read["b"]); Assert.IsFalse(read.ContainsKey("z"));
            Assert.AreEqual(1, PracticeColors.Read(new[] { "b", "a" }, new[] { 1 }).Count, "長さが足りない分は色なし");
            Assert.AreEqual(0, PracticeColors.Read(new[] { "a" }, new[] { 9 }).Count, "範囲外は色なし");
        }

        [Test] public void 練習用お宝の番号の約束()
        {
            Assert.AreEqual(100, ThPractice.IndexOf(0)); Assert.AreEqual(103, ThPractice.IndexOf(3));
            Assert.IsTrue(ThPractice.IsPracticeIndex(102)); Assert.IsFalse(ThPractice.IsPracticeIndex(22)); Assert.IsFalse(ThPractice.IsPracticeIndex(104));
            Assert.AreEqual(2, ThPractice.ColorOf(102)); Assert.AreEqual(-1, ThPractice.ColorOf(5));
        }

        // ---- ホストの状態表（練習用の表。単体） ----

        sealed class HostRig
        {
            public readonly List<(Rpc rpc, object dto)> Out = new List<(Rpc, object)>();
            public readonly HostItemTable Table;
            public readonly List<(string device, double at)> Completed = new List<(string, double)>();
            public HostRig()
            {
                var spawned = new List<SpawnedTreasure>();
                for (int c = 0; c < 2; c++)
                    spawned.Add(new SpawnedTreasure { Index = 100 + c, Kind = TreasureKind.S, Value = 150, LocalPosition = SlotPos[c], CatalogIndex = -1 });
                Table = new HostItemTable(spawned, Zone, (r, d) => Out.Add((r, d)), null, null, 0.3f, practiceTable: true);
                Table.Get(100).PracticeDevice = "A"; Table.Get(101).PracticeDevice = "B";
                Table.PracticeCollected = (d, t) => Completed.Add((d, t));
            }
            public List<T> Of<T>(Rpc rpc) => Out.Where(o => o.rpc == rpc).Select(o => (T)o.dto).ToList();
        }

        static ThGrabMsg GrabS(int item, int req) => new ThGrabMsg { item = item, reqId = req, kind = (int)TreasureKind.S };

        [Test] public void 練習用の表_番号は連続しなくてもGetで引ける()
        {
            var h = new HostRig();
            Assert.AreEqual(2, h.Table.Items.Count);
            Assert.IsNotNull(h.Table.Get(100)); Assert.IsNotNull(h.Table.Get(101));
            Assert.IsNull(h.Table.Get(0)); Assert.IsNull(h.Table.Get(102));
            Assert.IsTrue(h.Table.Get(100).Practice);
        }

        [Test] public void 練習用の表_割り当てられた端末だけがつかめる_他の端末はThGrabDenied()
        {
            var h = new HostRig();
            h.Table.OnGrab("B", 3, GrabS(100, 1));
            Assert.AreEqual(1, h.Of<ThGrabDeniedMsg>(Rpc.ThGrabDenied).Count);
            Assert.AreEqual(ItemState.Resting, h.Table.Get(100).State);
            h.Table.OnGrab("A", 2, GrabS(101, 2));
            Assert.AreEqual(2, h.Of<ThGrabDeniedMsg>(Rpc.ThGrabDenied).Count, "自分の色でない練習用お宝");
            h.Table.OnGrab("A", 2, GrabS(100, 3));
            Assert.AreEqual(ItemState.Held, h.Table.Get(100).State);
            var g = h.Of<ThGrabbedMsg>(Rpc.ThGrabbed).Single();
            Assert.AreEqual(150, g.value, "持った瞬間に値段がわかる");
            Assert.IsTrue(h.Table.Get(100).Revealed);
        }

        [Test] public void 練習用の表_減額は適用されない()
        {
            var h = new HostRig();
            h.Table.OnGrab("A", 2, GrabS(100, 1));
            h.Table.OnDamage("A", new ThDamageMsg { a = 100, b = -1, speed = 3f }, 10);
            Assert.AreEqual(0, h.Table.Get(100).Hits);
            Assert.AreEqual(150, h.Table.Get(100).Value);
            Assert.AreEqual(0, h.Of<ThDamagedMsg>(Rpc.ThDamaged).Count);
        }

        [Test] public void 練習用の表_ThCollectは1回だけ受理_合計は0のまま_完了は1回だけ記録()
        {
            var h = new HostRig();
            h.Table.OnGrab("A", 2, GrabS(100, 1));
            int gen = h.Table.Get(100).Gen;
            h.Table.OnCollect("A", new ThCollectMsg { item = 100, gen = gen }, 12.5);
            var c = h.Of<ThCollectedMsg>(Rpc.ThCollected);
            Assert.AreEqual(1, c.Count);
            Assert.AreEqual(0, c[0].total); Assert.AreEqual(150, c[0].value); Assert.IsFalse(c[0].damaged);
            Assert.AreEqual(0, h.Table.Total);
            Assert.IsTrue(h.Table.Get(100).Gone);
            CollectionAssert.AreEqual(new[] { ("A", 12.5) }, h.Completed);
            // 同じ世代の送り直し・世代が進んだ後の物は捨てる
            h.Table.OnCollect("A", new ThCollectMsg { item = 100, gen = gen }, 13);
            h.Table.OnCollect("A", new ThCollectMsg { item = 100, gen = gen + 1 }, 13);
            Assert.AreEqual(1, h.Of<ThCollectedMsg>(Rpc.ThCollected).Count);
            Assert.AreEqual(1, h.Completed.Count);
        }

        [Test] public void 練習用の表_納品場所の範囲で止まったThRestも回収_範囲外ならつかめるに戻す()
        {
            var h = new HostRig();
            h.Table.OnGrab("A", 2, GrabS(100, 1));
            h.Table.OnRelease("A", new ThReleaseMsg { item = 100, reqId = 1, gen = h.Table.Get(100).Gen, reason = (int)ReleaseReason.Dropped });
            h.Table.OnRest("A", new ThRestMsg { item = 100, gen = h.Table.Get(100).Gen, pos = new Vector3(5, 0, 5) }, 20);
            Assert.AreEqual(ItemState.Resting, h.Table.Get(100).State, "範囲外は置いただけ");
            Assert.AreEqual(0, h.Completed.Count);
            h.Table.OnGrab("A", 2, GrabS(100, 2));
            h.Table.OnRelease("A", new ThReleaseMsg { item = 100, reqId = 2, gen = h.Table.Get(100).Gen, reason = (int)ReleaseReason.Dropped });
            h.Table.OnRest("A", new ThRestMsg { item = 100, gen = h.Table.Get(100).Gen, pos = new Vector3(0, 0.9f, 0.8f) }, 21);
            Assert.IsTrue(h.Table.Get(100).Gone);
            Assert.AreEqual(0, h.Table.Total);
            CollectionAssert.AreEqual(new[] { ("A", 21.0) }, h.Completed);
        }

        [Test] public void 練習用の表_範囲内に置かせたPlacingの終わりも回収()
        {
            var h = new HostRig();
            h.Table.PlaceSolver = p => new Vector3(p.x, 0.5f, 0.8f);   // 納品場所の範囲内に降りる
            h.Table.OnGrab("A", 2, GrabS(100, 1));
            h.Table.ForceRestItemsOf("A", 10);
            h.Table.Tick(10.1, true);
            Assert.IsFalse(h.Table.Get(100).Gone, "〔置く時間〕の前");
            h.Table.Tick(10.31, true);
            Assert.IsTrue(h.Table.Get(100).Gone);
            Assert.AreEqual(0, h.Table.Total);
            Assert.AreEqual(1, h.Completed.Count);
            h.Table.Tick(11, true);
            Assert.AreEqual(1, h.Of<ThCollectedMsg>(Rpc.ThCollected).Count, "1回だけ");
        }

        [Test] public void 本編の表では練習用の行は引き続きつかめず回収もされない()
        {
            var spawned = new List<SpawnedTreasure> { new SpawnedTreasure { Index = 0, Kind = TreasureKind.S, Value = 200, LocalPosition = SlotPos[0], CatalogIndex = 0 } };
            var outs = new List<(Rpc, object)>();
            var t = new HostItemTable(spawned, Zone, (r, d) => outs.Add((r, d)));
            t.Get(0).Practice = true; t.Get(0).PracticeDevice = "A";
            t.OnGrab("A", 2, GrabS(0, 1));
            Assert.AreEqual(Rpc.ThGrabDenied, outs.Single().Item1);
            t.OnCollect("A", new ThCollectMsg { item = 0, gen = 0 }, 1);
            Assert.IsFalse(t.Get(0).Gone);
        }

        // ---- ホスト＋端末（偽の通信） ----

        sealed class Rig : IDisposable
        {
            public const int HostNo = 1;
            public double Now = 100;
            public readonly FakeNetwork Net = new FakeNetwork();
            public SessionHostCore Host;
            public readonly List<SessionClientCore> Clients = new List<SessionClientCore>();
            public readonly Dictionary<int, string> Resolve = new Dictionary<int, string>();
            public readonly Dictionary<string, FakeNetwork.Node> Nodes = new Dictionary<string, FakeNetwork.Node>();
            // 運営でも端末でもない傍受役（全員宛ての RPC を記録する）
            public readonly List<ThStateMsg> States = new List<ThStateMsg>();
            public readonly List<ThGrabDeniedMsg> Denied = new List<ThGrabDeniedMsg>();
            public readonly List<ThCollectedMsg> Collected = new List<ThCollectedMsg>();
            public readonly List<ThItemsMsg> ItemsMsgs = new List<ThItemsMsg>();
            readonly bool _withSlots;

            static ThParams ShortParams()
            {
                var p = new ThParams();
                p.progress.openingSeconds = 3; p.progress.practiceSeconds = 5; p.progress.playingSeconds = 30; p.progress.resultSeconds = 3;
                p.grip.approvalWaitSeconds = 3;
                return p;
            }

            public Rig(bool withSlots = true)
            {
                _withSlots = withSlots;
                var node = Net.Add(HostNo);
                var sp = ThParamsLoader.Load(ShortParams(), null, null);
                Host = new SessionHostCore(node, sp, new ThGameSettings(), null, 0, no => Resolve.TryGetValue(no, out var v) ? v : null, MakeSetup(withSlots));
                Host.OwnClientNo = HostNo;
                node.OnReceive = (rpc, sender, args) => Host.OnRpc(rpc, sender, args, Now);
                var spy = Net.Add(99);
                spy.OnReceive = (rpc, sender, args) =>
                {
                    if (rpc == Rpc.ThState && ThMessages.TryDecode(args, out long _, out ThStateMsg s)) States.Add(s);
                    else if (rpc == Rpc.ThGrabDenied && ThMessages.TryDecode(args, out long _, out ThGrabDeniedMsg d)) Denied.Add(d);
                    else if (rpc == Rpc.ThCollected && ThMessages.TryDecode(args, out long _, out ThCollectedMsg c)) Collected.Add(c);
                    else if (rpc == Rpc.ThItems && ThMessages.TryDecode(args, out long _, out ThItemsMsg it)) ItemsMsgs.Add(it);
                };
            }

            public SessionClientCore AddClient(int no, string id, bool settle = true)
            {
                var node = Net.Add(no);
                var c = new SessionClientCore(id, node, new ThParams(), null, Now, MakeSetup(_withSlots)) { OwnClientNo = no };
                node.OnReceive = (rpc, sender, args) => c.OnRpc(rpc, sender, args, Now);
                Clients.Add(c); Resolve[no] = id; Nodes[id] = node;
                c.OnReady(Now);
                Net.Pump();
                if (settle) Advance(1.0);
                return c;
            }

            public void Advance(double seconds)
            {
                int steps = (int)Math.Round(seconds / 0.1);
                for (int i = 0; i < steps; i++)
                {
                    Now += 0.1;
                    Host.Tick(Now); Net.Pump();
                    foreach (var c in Clients) c.Tick(Now);
                    Net.Pump();
                }
            }

            public void ToTutorial()
            {
                Assert.IsTrue(Host.Start(Now, 5000, 4242));
                Net.Pump();
                Advance(3.3);
                Assert.AreEqual(RoundState.Tutorial, Host.Machine.State);
            }

            public void ToPlaying()
            {
                ToTutorial();
                Advance(5.3);
                Assert.AreEqual(RoundState.Playing, Host.Machine.State);
            }

            public void Dispose() { Host.Dispose(); foreach (var c in Clients) c.Dispose(); }
        }

        [Test] public void 結合_Tutorialで参加端末が文字列順に色0から割り当てられThStateに載る()
        {
            using var r = new Rig();
            var c = r.AddClient(4, "devC"); var a = r.AddClient(2, "devA"); var b = r.AddClient(3, "devB");
            r.ToTutorial();
            Assert.AreEqual(0, r.Host.ColorAssignments["devA"]); Assert.AreEqual(1, r.Host.ColorAssignments["devB"]); Assert.AreEqual(2, r.Host.ColorAssignments["devC"]);
            var last = r.States.Last();
            Assert.AreEqual((int)RoundState.Tutorial, last.state);
            Assert.AreEqual(last.participants.Length, last.tutorialColors.Length);
            for (int i = 0; i < last.participants.Length; i++) Assert.AreEqual(r.Host.ColorAssignments[last.participants[i]], last.tutorialColors[i]);
            Assert.AreEqual(0, a.Tracker.OwnColor.CurrentValue); Assert.AreEqual(1, b.Tracker.OwnColor.CurrentValue); Assert.AreEqual(2, c.Tracker.OwnColor.CurrentValue);
            Assert.AreEqual(1, a.Tracker.ColorOf("devB")); Assert.AreEqual(-1, a.Tracker.ColorOf("nobody"));
            Assert.AreEqual(3, a.Tracker.DeviceColors.Count);
        }

        [Test] public void 結合_Openingの間はまだ色が無く_Playing_Resultでも色は載り続ける()
        {
            using var r = new Rig();
            var a = r.AddClient(2, "devA"); r.AddClient(3, "devB");
            Assert.IsTrue(r.Host.Start(r.Now, 5000, 1));
            r.Net.Pump();
            Assert.AreEqual(0, r.States.Last().tutorialColors.Length);
            Assert.AreEqual(-1, a.Tracker.OwnColor.CurrentValue);
            r.Advance(3.3 + 5.3);
            Assert.AreEqual(RoundState.Playing, r.Host.Machine.State);
            CollectionAssert.AreEqual(new[] { 0, 1 }, r.States.Last().tutorialColors);
            Assert.AreEqual(0, a.Tracker.OwnColor.CurrentValue);
            r.Advance(30.2);
            Assert.AreEqual(RoundState.Result, r.Host.Machine.State);
            CollectionAssert.AreEqual(new[] { 0, 1 }, r.States.Last().tutorialColors);
            Assert.AreEqual(0, a.Tracker.OwnColor.CurrentValue);
        }

        [Test] public void 結合_Tutorialで参加端末が減っても残った端末の色は変わらない()
        {
            using var r = new Rig();
            var a = r.AddClient(2, "devA"); var b = r.AddClient(3, "devB"); var c = r.AddClient(4, "devC");
            r.ToTutorial();
            r.Host.OnClientDisconnected(2, r.Now);   // 色 0 の devA が切れる
            r.Net.Pump();
            r.Advance(5.3);
            Assert.AreEqual(RoundState.Playing, r.Host.Machine.State);
            var last = r.States.Last();
            CollectionAssert.AreEqual(new[] { "devB", "devC" }, last.participants);
            CollectionAssert.AreEqual(new[] { 1, 2 }, last.tutorialColors, "詰めて振り直さない");
            Assert.AreEqual(1, b.Tracker.OwnColor.CurrentValue); Assert.AreEqual(2, c.Tracker.OwnColor.CurrentValue);
        }

        [Test] public void 結合_中断と待機に戻ると色の割り当てと完了の記録が消え新しい開始でも消える()
        {
            using var r = new Rig();
            var a = r.AddClient(2, "devA"); var b = r.AddClient(3, "devB");
            r.ToTutorial();
            Assert.AreEqual(2, r.Host.ColorAssignments.Count);
            Assert.IsTrue(r.Host.Abort(r.Now));
            r.Net.Pump();
            Assert.AreEqual(0, r.Host.ColorAssignments.Count);
            Assert.AreEqual(0, r.Host.PracticeCompletedAt.Count);
            Assert.IsNull(r.Host.PracticeItems);
            Assert.AreEqual(-1, a.Tracker.OwnColor.CurrentValue);
            Assert.AreEqual(0, a.Tracker.DeviceColors.Count);
            // 通常の Result → Waiting でも消える
            r.ToPlaying();
            Assert.AreEqual(2, r.Host.ColorAssignments.Count);
            r.Advance(30.2 + 3.2);
            Assert.AreEqual(RoundState.Waiting, r.Host.Machine.State);
            Assert.AreEqual(0, r.Host.ColorAssignments.Count);
            Assert.AreEqual(-1, b.Tracker.OwnColor.CurrentValue);
        }

        [Test] public void 結合_Tutorialに入ると割り当てのある色ごとに練習用お宝ができThItemsで送られる()
        {
            using var r = new Rig();
            var a = r.AddClient(2, "devA"); var b = r.AddClient(3, "devB"); var c = r.AddClient(4, "devC");
            r.ToTutorial();
            var items = r.Host.PracticeItems;
            Assert.IsNotNull(items);
            CollectionAssert.AreEqual(new[] { 100, 101, 102 }, items.Items.Select(i => i.Index).ToArray());
            for (int i = 0; i < 3; i++)
            {
                var it = items.Items[i];
                Assert.AreEqual(TreasureKind.S, it.Kind); Assert.IsTrue(it.Practice); Assert.AreEqual(150, it.Value);
                Assert.AreEqual(SlotPos[i], it.RestPos); Assert.AreEqual(180f, it.RestRot.eulerAngles.y, 1e-3f);
                Assert.AreEqual(ItemState.Resting, it.State); Assert.IsFalse(it.Revealed, "値段は持った瞬間に初めてわかる");
            }
            Assert.AreEqual("devA", items.Get(100).PracticeDevice); Assert.AreEqual("devC", items.Get(102).PracticeDevice);
            r.Advance(1.2);
            Assert.IsTrue(r.ItemsMsgs.Any(m => m.items.Length == 3 && m.items[0].item == 100), "ThItems で練習用の状態表が送られる");
            Assert.IsNull(r.Host.Items, "本編の表は Playing まで無い");
        }

        [Test] public void 結合_TutorialSlotが無い環境では練習用お宝を作らない()
        {
            using var r = new Rig(withSlots: false);
            var a = r.AddClient(2, "devA");
            r.ToTutorial();
            Assert.IsNull(r.Host.PracticeItems);
            Assert.IsNull(a.Items.CurrentValue);
            Assert.AreEqual(1, r.Host.ColorAssignments.Count, "色の割り当てはする");
        }

        [Test] public void 結合_端末の表が練習用お宝でできPlayingで本編の表に差し替わる_次の回を待つ端末ではできない()
        {
            using var r = new Rig();
            var a = r.AddClient(2, "devA"); var b = r.AddClient(3, "devB");
            Assert.IsTrue(r.Host.Start(r.Now, 5000, 4242)); r.Net.Pump();
            r.Advance(3.3);
            // Tutorial に入ってから接続した端末は、その回は参加しない
            var late = r.AddClient(4, "devZ", settle: false);
            r.Advance(0.3);
            Assert.IsTrue(late.Tracker.WaitNextRound);
            Assert.IsNull(late.Items.CurrentValue, "次の回を待つ端末は練習用の表を作らない");
            var ta = a.Items.CurrentValue;
            Assert.IsNotNull(ta); Assert.IsTrue(ta.IsPractice);
            CollectionAssert.AreEqual(new[] { 100, 101 }, ta.Items.Select(i => i.Index).ToArray());
            Assert.AreEqual(100, a.OwnPracticeIndex); Assert.AreEqual(101, b.OwnPracticeIndex);
            // Playing で本編の表に差し替わる
            r.Advance(5.3);
            var tp = a.Items.CurrentValue;
            Assert.AreNotSame(ta, tp);
            Assert.IsFalse(tp.IsPractice);
            Assert.AreEqual(15, tp.Items.Count);
            Assert.IsNull(tp.Get(100), "練習用お宝は消えた");
            Assert.IsNull(r.Host.PracticeItems);
            Assert.IsNotNull(r.Host.Items);
            Assert.AreEqual(15, r.Host.Items.Items.Count);
            Assert.AreEqual(0, r.Host.Items.Items[0].Index);
        }

        [Test] public void 結合_つかめるのは自分の練習用お宝だけ()
        {
            using var r = new Rig();
            var a = r.AddClient(2, "devA"); var b = r.AddClient(3, "devB");
            r.ToTutorial();
            var ta = a.Items.CurrentValue; var tb = b.Items.CurrentValue;
            Assert.IsTrue(ta.ParticipantPlaying);
            Assert.IsTrue(ta.IsGrabbable(ta.Get(100)));
            Assert.IsFalse(ta.IsGrabbable(ta.Get(101)), "他人の練習用お宝");
            Assert.IsFalse(ta.TryBeginGrab(101, r.Now));
            Assert.IsFalse(tb.IsGrabbable(tb.Get(100)));
            Assert.IsTrue(tb.IsGrabbable(tb.Get(101)));
            // 持つ → 全員で値段が公開される
            Assert.IsTrue(ta.TryBeginGrab(100, r.Now));
            r.Net.Pump();
            Assert.IsTrue(ta.Get(100).Approved);
            Assert.IsTrue(tb.Get(100).Revealed); Assert.AreEqual(150, tb.Get(100).Value);
            Assert.AreEqual(0, r.Denied.Count);
        }

        [Test] public void 結合_割り当てのない端末は練習用お宝をつかめない()
        {
            using var r = new Rig();
            // 5 台目は色の割り当てが無い（色は 4 つまで）。DeviceId の文字列順で最後になる名前にする
            r.AddClient(2, "devA"); r.AddClient(3, "devB"); r.AddClient(4, "devC"); r.AddClient(5, "devD");
            var e = r.AddClient(6, "devE");
            r.ToTutorial();
            Assert.AreEqual(-1, e.Tracker.OwnColor.CurrentValue);
            Assert.AreEqual(-1, e.OwnPracticeIndex);
            var t = e.Items.CurrentValue;
            Assert.IsNotNull(t);
            Assert.IsTrue(t.ParticipantPlaying, "Tick（UpdateGrabPhase）を通した後");
            Assert.AreEqual(-1, t.PracticeOwnIndex);
            foreach (var it in t.Items) Assert.IsFalse(t.IsGrabbable(it), it.Index.ToString());
            Assert.IsFalse(t.TryBeginGrab(100, r.Now));
            Assert.AreEqual(PracticeItemState.None, PracticeGuide.Classify(t.Get(e.OwnPracticeIndex), false), "案内は出ない");
        }

        [Test] public void 結合_他の端末からの練習用お宝のThGrabはホストがThGrabDeniedにする()
        {
            using var r = new Rig();
            r.AddClient(2, "devA"); r.AddClient(3, "devB");
            r.ToTutorial();
            r.Nodes["devB"].Send(Rpc.ThGrab, ThMessages.Encode(r.Host.Machine.RoundId, GrabS(100, 1)));
            r.Net.Pump();
            Assert.AreEqual(1, r.Denied.Count);
            Assert.AreEqual(100, r.Denied[0].item); Assert.AreEqual(3, r.Denied[0].clientNo);
            Assert.AreEqual(ItemState.Resting, r.Host.PracticeItems.Get(100).State);
        }

        [Test] public void 結合_Tutorial中のThDamageとThLiftは捨てる_練習用お宝からはThDamageを送らない()
        {
            using var r = new Rig();
            var a = r.AddClient(2, "devA");
            r.ToTutorial();
            var t = a.Items.CurrentValue;
            Assert.IsTrue(t.TryBeginGrab(100, r.Now)); r.Net.Pump();
            Assert.IsFalse(t.TryReportDamage(100, -1, Vector3.zero, 5f, r.Now), "練習用お宝は ThDamage を送らない");
            Assert.IsFalse(r.Net.Log.Any(l => l.rpc == Rpc.ThDamage));
            // 古い送り手が送っても、ホストは適用しない
            r.Nodes["devA"].Send(Rpc.ThDamage, ThMessages.Encode(r.Host.Machine.RoundId, new ThDamageMsg { a = 100, b = -1, speed = 5f }));
            r.Nodes["devA"].Send(Rpc.ThLift, ThMessages.Encode(r.Host.Machine.RoundId, new ThLiftMsg { item = 100, reqId = 1, gen = 1 }));
            r.Net.Pump();
            var it = r.Host.PracticeItems.Get(100);
            Assert.AreEqual(0, it.Hits); Assert.AreEqual(150, it.Value);
            Assert.AreEqual(ItemState.Held, it.State);
        }

        [Test] public void 結合_開始から練習の回収_本編開始までを2台で通す()
        {
            using var r = new Rig();
            var a = r.AddClient(2, "devA"); var b = r.AddClient(3, "devB");
            r.ToTutorial();
            var ta = a.Items.CurrentValue; var tb = b.Items.CurrentValue;

            // A が自分のお宝をつかむ。B のお宝はつかめない
            Assert.IsTrue(ta.TryBeginGrab(100, r.Now)); r.Net.Pump();
            Assert.IsFalse(ta.TryBeginGrab(101, r.Now));
            Assert.IsTrue(ta.Get(100).Approved);
            // A が納品場所で手を開く → 回収
            ta.OnHandReleased(100, r.Now); r.Net.Pump();
            ta.NoteInCollectZone(100, r.Now); r.Net.Pump();
            Assert.IsTrue(r.Host.PracticeItems.Get(100).Gone);
            Assert.AreEqual(0, r.Host.TotalAmount, "合計額は変わらない");
            Assert.AreEqual(0, a.Total.CurrentValue); Assert.AreEqual(0, b.Total.CurrentValue);
            var col = r.Collected.Single();
            Assert.AreEqual(100, col.item); Assert.AreEqual(0, col.total); Assert.AreEqual(150, col.value);
            Assert.IsTrue(r.Host.PracticeCompletedAt.ContainsKey("devA")); Assert.IsFalse(r.Host.PracticeCompletedAt.ContainsKey("devB"));
            // A だけに回収の合図（面の案内の入力 = 自分の練習用お宝の状態）
            Assert.AreEqual(PracticeItemState.Collected, PracticeGuide.Classify(ta.Get(a.OwnPracticeIndex), false));
            Assert.AreEqual(PracticeItemState.Resting, PracticeGuide.Classify(tb.Get(b.OwnPracticeIndex), false), "他人の回収では出ない");
            var pa = new ThPanelPresenter(); var pb = new ThPanelPresenter();
            Assert.AreEqual(PracticeStep.Success, pa.Update(Tut(PracticeGuide.Classify(ta.Get(a.OwnPracticeIndex), false)), 10).PracticeStep);
            Assert.AreEqual(PracticeStep.Grip, pb.Update(Tut(PracticeGuide.Classify(tb.Get(b.OwnPracticeIndex), false)), 10).PracticeStep);
            Assert.IsTrue(tb.Get(100).Gone, "B の画面でも A のお宝は消える");
            Assert.AreEqual(1, r.Host.PracticeCompletedAt.Count);

            // 本編開始で練習用お宝が両端末から消え、本編のお宝ができる
            r.Advance(5.3);
            Assert.AreEqual(RoundState.Playing, r.Host.Machine.State);
            foreach (var c in new[] { a, b })
            {
                var t = c.Items.CurrentValue;
                Assert.IsFalse(t.IsPractice);
                Assert.IsNull(t.Get(100)); Assert.IsNull(t.Get(101));
                Assert.AreEqual(15, t.Items.Count);
            }
            Assert.AreEqual(0, r.Host.TotalAmount);
            // 遅れて届いた練習用の番号のメッセージは例外なく捨てられる
            r.Nodes["devB"].Send(Rpc.ThGrab, ThMessages.Encode(r.Host.Machine.RoundId, GrabS(101, 9)));
            r.Nodes["devA"].Send(Rpc.ThCollect, ThMessages.Encode(r.Host.Machine.RoundId, new ThCollectMsg { item = 100, gen = 1 }));
            r.Nodes["devA"].Send(Rpc.ThRest, ThMessages.Encode(r.Host.Machine.RoundId, new ThRestMsg { item = 100, gen = 1, pos = Vector3.zero }));
            Assert.DoesNotThrow(() => r.Net.Pump());
            Assert.AreEqual(0, r.Host.TotalAmount);
            Assert.AreEqual(1, r.Collected.Count, "ThCollected は増えない");
        }

        [Test] public void 結合_ThItemsで取りこぼしても自分の練習用お宝の回収に追いつき合図が出る()
        {
            using var r = new Rig();
            var a = r.AddClient(2, "devA"); r.AddClient(3, "devB");
            r.ToTutorial();
            var ta = a.Items.CurrentValue;
            Assert.IsTrue(ta.TryBeginGrab(100, r.Now)); r.Net.Pump();
            ta.OnHandReleased(100, r.Now); r.Net.Pump();
            r.Net.Drop = (sender, rpc, args, to) => rpc == Rpc.ThCollected;   // ThCollected を捨てる
            ta.NoteInCollectZone(100, r.Now); r.Net.Pump();
            Assert.IsTrue(r.Host.PracticeItems.Get(100).Gone);
            Assert.IsFalse(ta.Get(100).Gone);
            Assert.AreNotEqual(PracticeItemState.Collected, PracticeGuide.Classify(ta.Get(100), true), "取りこぼした間は回収済みではない");
            r.Advance(1.3);   // ThItems で追いつく
            Assert.IsTrue(ta.Get(100).Gone);
            Assert.AreEqual(PracticeItemState.Collected, PracticeGuide.Classify(ta.Get(100), false), "追いついたときも「+150G！」の段階になる");
        }

        [Test] public void 結合_練習中に持ったまま切断するとそっと置かれ範囲外ならつかめるに戻る()
        {
            using var r = new Rig();
            var a = r.AddClient(2, "devA"); r.AddClient(3, "devB");
            r.ToTutorial();
            var ta = a.Items.CurrentValue;
            Assert.IsTrue(ta.TryBeginGrab(100, r.Now)); r.Net.Pump();
            Assert.AreEqual(ItemState.Held, r.Host.PracticeItems.Get(100).State);
            int genBefore = r.Host.PracticeItems.Get(100).Gen;
            var forced = new List<ThForceRestMsg>(); var freed = new List<ThFreedMsg>();
            var spy = r.Net.Add(98);
            spy.OnReceive = (rpc, sender, args) =>
            {
                if (rpc == Rpc.ThForceRest && ThMessages.TryDecode(args, out long _, out ThForceRestMsg f)) forced.Add(f);
                if (rpc == Rpc.ThFreed && ThMessages.TryDecode(args, out long _, out ThFreedMsg fr)) freed.Add(fr);
            };
            r.Host.OnClientDisconnected(2, r.Now);
            r.Net.Pump();
            Assert.AreEqual(1, forced.Count);
            Assert.AreEqual(100, forced[0].item);
            Assert.AreEqual(genBefore + 1, forced[0].gen);
            Assert.AreEqual(ItemState.Placing, r.Host.PracticeItems.Get(100).State);
            r.Advance(0.6);   // 〔置く時間〕の後
            Assert.AreEqual(ItemState.Resting, r.Host.PracticeItems.Get(100).State, "範囲外なのでつかめるに戻る");
            Assert.IsFalse(r.Host.PracticeItems.Get(100).Gone);
            Assert.IsTrue(freed.Any(f => f.item == 100));
            Assert.AreEqual(0, r.Host.PracticeCompletedAt.Count);
        }

        [Test] public void 結合_Tutorial中に練習用お宝を持ったまま本編が始まると両端末で本編の表に替わる()
        {
            using var r = new Rig();
            var a = r.AddClient(2, "devA"); var b = r.AddClient(3, "devB");
            r.ToTutorial();
            Assert.IsTrue(a.Items.CurrentValue.TryBeginGrab(100, r.Now)); r.Net.Pump();
            Assert.IsTrue(a.Items.CurrentValue.Get(100).Approved);
            Assert.IsTrue(r.Host.BeginPlay(r.Now)); r.Net.Pump();
            r.Advance(0.3);
            foreach (var c in new[] { a, b })
            {
                var t = c.Items.CurrentValue;
                Assert.IsFalse(t.IsPractice);
                Assert.IsNull(t.Get(100)); Assert.IsNull(t.Get(101));
                Assert.AreEqual(15, t.Items.Count);
                Assert.IsFalse(t.Items.Any(i => ThPractice.IsPracticeIndex(i.Index)), "端末の表に練習用の番号が無い");
            }
            Assert.IsNull(r.Host.PracticeItems);
        }

        [Test] public void 本編の_ThStateを取りこぼしてTutorialからResultに飛んだら練習用の表を捨てる()
        {
            var node = new CaptureTransport();
            var c = new SessionClientCore("devA", node, new ThParams(), null, 0, MakeSetup()) { OwnClientNo = 2 };
            c.Tracker.LocalParamsVersion = "v";
            ThStateMsg St(RoundState s, int seq, int[] colors) => new ThStateMsg { seq = seq, state = (int)s, remaining = 10, participants = new[] { "devA" }, tutorialColors = colors, paramsVersion = "v" };
            c.Tracker.OnState(7000, St(RoundState.Opening, 1, new int[0]), 0);
            c.Tracker.OnState(7000, St(RoundState.Tutorial, 2, new[] { 0 }), 1);
            Assert.IsTrue(c.Items.CurrentValue != null && c.Items.CurrentValue.IsPractice);
            c.Tracker.OnState(7000, St(RoundState.Result, 5, new[] { 0 }), 40);   // Playing の ThState を取りこぼした
            Assert.IsNull(c.Items.CurrentValue, "結果の間ずっと練習用お宝が見えない");
            c.Dispose();
        }

        // ---- 端末の表（単体） ----

        [Test] public void ClientRoundTracker_色は回が変わる_待機に戻ると消える()
        {
            var t = new ClientRoundTracker("devB", new ThProgressParams { hostLostSeconds = 10f }, 0) { LocalParamsVersion = "v" };
            t.OnState(1000, new ThStateMsg { seq = 1, state = (int)RoundState.Opening, remaining = 3, participants = new[] { "devA", "devB" }, paramsVersion = "v" }, 0);
            Assert.AreEqual(-1, t.OwnColor.CurrentValue);
            t.OnState(1000, new ThStateMsg { seq = 2, state = (int)RoundState.Tutorial, remaining = 30, participants = new[] { "devA", "devB" }, tutorialColors = new[] { 0, 1 }, paramsVersion = "v" }, 1);
            Assert.AreEqual(1, t.OwnColor.CurrentValue); Assert.AreEqual(0, t.ColorOf("devA"));
            // 色の無い ThState（古いホスト）の Playing では消える
            t.OnState(1000, new ThStateMsg { seq = 3, state = (int)RoundState.Playing, remaining = 30, participants = new[] { "devA", "devB" }, paramsVersion = "v" }, 2);
            Assert.AreEqual(-1, t.OwnColor.CurrentValue);
            t.OnState(1000, new ThStateMsg { seq = 4, state = (int)RoundState.Result, remaining = 3, participants = new[] { "devA", "devB" }, tutorialColors = new[] { 0, 1 }, paramsVersion = "v" }, 3);
            Assert.AreEqual(1, t.OwnColor.CurrentValue);
            t.OnState(0, new ThStateMsg { seq = 5, state = (int)RoundState.Waiting, paramsVersion = "v" }, 4);
            Assert.AreEqual(-1, t.OwnColor.CurrentValue); Assert.AreEqual(0, t.DeviceColors.Count);
        }

        [Test] public void ClientRoundTracker_ホスト途絶で待機に戻ると色が消える()
        {
            var t = new ClientRoundTracker("devA", new ThProgressParams { hostLostSeconds = 5f }, 0) { LocalParamsVersion = "v" };
            t.OnState(1000, new ThStateMsg { seq = 1, state = (int)RoundState.Opening, remaining = 3, participants = new[] { "devA" }, paramsVersion = "v" }, 0);
            t.OnState(1000, new ThStateMsg { seq = 2, state = (int)RoundState.Tutorial, remaining = 30, participants = new[] { "devA" }, tutorialColors = new[] { 0 }, paramsVersion = "v" }, 1);
            Assert.AreEqual(0, t.OwnColor.CurrentValue);
            t.Tick(10);
            Assert.AreEqual(RoundState.Waiting, t.State.CurrentValue);
            Assert.AreEqual(-1, t.OwnColor.CurrentValue); Assert.AreEqual(0, t.DeviceColors.Count);
        }

        // ---- 案内の段階（純粋） ----

        [Test] public void 案内は持つ前_持てた後_範囲内_回収の後の順に進む()
        {
            var g = new PracticeGuide();
            Assert.AreEqual(PracticeStep.None, g.Update(PracticeItemState.None, 0));
            Assert.AreEqual(PracticeStep.Grip, g.Update(PracticeItemState.Resting, 1));
            Assert.AreEqual(PracticeStep.Carry, g.Update(PracticeItemState.Held, 2));
            Assert.AreEqual(PracticeStep.Release, g.Update(PracticeItemState.HeldInZone, 3));
            Assert.AreEqual(PracticeStep.Carry, g.Update(PracticeItemState.Held, 3.5), "範囲から出たら戻る");
            Assert.AreEqual(PracticeStep.Grip, g.Update(PracticeItemState.Resting, 4), "落としたら最初に戻る");
        }

        [Test] public void 回収の後は数秒だけ_150G_その後はぶつけると値段が下がる()
        {
            var g = new PracticeGuide();
            g.Update(PracticeItemState.HeldInZone, 10);
            Assert.AreEqual(PracticeStep.Success, g.Update(PracticeItemState.Collected, 11));
            Assert.AreEqual(PracticeStep.Success, g.Update(PracticeItemState.Collected, 11 + PracticeGuide.SuccessSeconds - 0.01));
            Assert.AreEqual(PracticeStep.DamageNote, g.Update(PracticeItemState.Collected, 11 + PracticeGuide.SuccessSeconds));
            Assert.AreEqual(PracticeStep.DamageNote, g.Update(PracticeItemState.Collected, 60), "本編が始まるまで");
            g.Reset();
            Assert.AreEqual(PracticeStep.Success, g.Update(PracticeItemState.Collected, 100), "回が変わったら数え直す");
        }

        [Test] public void Classifyは自分の練習用お宝の状態から段階の入力を出す()
        {
            Assert.AreEqual(PracticeItemState.None, PracticeGuide.Classify(null, false));
            var it = new ClientItem { Index = 100, State = ItemState.Resting };
            Assert.AreEqual(PracticeItemState.Resting, PracticeGuide.Classify(it, true), "置かれているお宝は範囲内でも「持つ前」");
            it.Mine = true; it.Approved = false; it.State = ItemState.Flying;
            Assert.AreEqual(PracticeItemState.Resting, PracticeGuide.Classify(it, false), "承認前はまだ持てていない");
            it.Approved = true; it.State = ItemState.Held;
            Assert.AreEqual(PracticeItemState.Held, PracticeGuide.Classify(it, false));
            Assert.AreEqual(PracticeItemState.HeldInZone, PracticeGuide.Classify(it, true));
            it.State = ItemState.Falling;
            Assert.AreEqual(PracticeItemState.HeldInZone, PracticeGuide.Classify(it, true), "範囲内で手を開いた後の落下中は段階 3 のまま");
            Assert.AreEqual(PracticeItemState.Resting, PracticeGuide.Classify(it, false), "範囲外で落とした");
            it.Gone = true; it.State = ItemState.Collected;
            Assert.AreEqual(PracticeItemState.Collected, PracticeGuide.Classify(it, false));
        }

        [Test] public void Classifyは手を開いた後の落下中と回収待ちを範囲内ならHeldInZone_範囲外ならRestingにする()
        {
            var it = new ClientItem { Index = 100, Mine = true, Approved = true, State = ItemState.Falling };
            Assert.AreEqual(PracticeItemState.HeldInZone, PracticeGuide.Classify(it, true), "範囲内で手を開いた後の落下中");
            Assert.AreEqual(PracticeItemState.Resting, PracticeGuide.Classify(it, false), "範囲外で落とした後");
            it.State = ItemState.Resting; it.PendingCollect = true;
            Assert.AreEqual(PracticeItemState.HeldInZone, PracticeGuide.Classify(it, true), "ThCollected を待っている間");
            it.PendingCollect = false; it.PendingRest = true;
            Assert.AreEqual(PracticeItemState.Resting, PracticeGuide.Classify(it, false), "ThRest の承認待ち（範囲外）");
            // 案内は途切れず段階 3 のまま
            var g = new PracticeGuide();
            Assert.AreEqual(PracticeStep.Release, g.Update(PracticeItemState.HeldInZone, 1));
            it.State = ItemState.Falling; it.PendingRest = false;
            Assert.AreEqual(PracticeStep.Release, g.Update(PracticeGuide.Classify(it, true), 1.1));
        }

        // ---- 面（ThPanelPresenter） ----

        static ThPanelInput Tut(PracticeItemState s, long round = 1) =>
            new ThPanelInput { State = RoundState.Tutorial, RoundId = round, Remaining = 20, OpeningSeconds = 10, Practice = s, PracticeValue = 150 };

        [Test] public void 面_Tutorialで割り当てのない端末には何も出さず_ある端末には案内を段階どおりに出す()
        {
            var p = new ThPanelPresenter();
            Assert.AreEqual(ThPanelKind.None, p.Update(Tut(PracticeItemState.None), 0).Kind);
            var f = p.Update(Tut(PracticeItemState.Resting), 1);
            Assert.AreEqual(ThPanelKind.Practice, f.Kind); Assert.AreEqual(PracticeStep.Grip, f.PracticeStep);
            Assert.AreEqual(PracticeStep.Carry, p.Update(Tut(PracticeItemState.Held), 2).PracticeStep);
            Assert.AreEqual(PracticeStep.Release, p.Update(Tut(PracticeItemState.HeldInZone), 3).PracticeStep);
            Assert.AreEqual(PracticeStep.Success, p.Update(Tut(PracticeItemState.Collected), 4).PracticeStep);
            Assert.AreEqual(PracticeStep.DamageNote, p.Update(Tut(PracticeItemState.Collected), 4 + PracticeGuide.SuccessSeconds + 0.1).PracticeStep);
        }

        [Test] public void 面_次の回を待つ端末は練習の案内も出さず_回が変われば案内を数え直す()
        {
            var p = new ThPanelPresenter();
            var wait = Tut(PracticeItemState.Resting); wait.WaitNextRound = true;
            Assert.AreEqual(ThPanelKind.None, p.Update(wait, 0).Kind);
            p.Update(Tut(PracticeItemState.Collected, 1), 10);
            Assert.AreEqual(PracticeStep.DamageNote, p.Update(Tut(PracticeItemState.Collected, 1), 20).PracticeStep);
            Assert.AreEqual(PracticeStep.Success, p.Update(Tut(PracticeItemState.Collected, 2), 30).PracticeStep, "新しい回");
        }

        [Test] public void 面_案内の文言は既存と新規の文言から組み立てる()
        {
            var t = AssetDatabase.LoadAssetAtPath("Assets/_Content/TreasureHaul/Settings/ThTextTable.asset", typeof(ThTextTable)) as ThTextTable;
            string Main(PracticeStep s) => ThPanelPresenter.Compose(new ThPanelFrame { Kind = ThPanelKind.Practice, PracticeStep = s, PracticeValue = 150 }, t).Main;
            Assert.AreEqual("持ち手に手を向けて、握ろう", Main(PracticeStep.Grip));
            Assert.AreEqual("納品場所まで運ぼう", Main(PracticeStep.Carry));
            Assert.AreEqual("納品場所の上で手を開こう", Main(PracticeStep.Release));
            Assert.AreEqual("+150G！", Main(PracticeStep.Success));
            Assert.IsTrue(ThPanelPresenter.Compose(new ThPanelFrame { Kind = ThPanelKind.Practice, PracticeStep = PracticeStep.Success, PracticeValue = 150 }, t).Large);
            Assert.AreEqual("ぶつけると値段が下がる", Main(PracticeStep.DamageNote));
            Assert.AreEqual("宝を納品場所まで運ぼう。ぶつけると値段が下がる。みんなで目標額を目指そう", t.Get(ThTextId.GoalSentence));
            Assert.AreEqual("お宝を、納品場所まで運び出してくれ。", t.Get(ThTextId.OpeningLine3));
        }

        // ---- ヒント（純粋） ----

        [Test] public void ヒントはTutorialに入ってからヒントまでの秒数がたっても持てていなければ出る()
        {
            var j = new PracticeHintJudge();
            Assert.IsFalse(j.ShouldShow(1, true, true, PracticeItemState.Resting, 10f, 100));
            Assert.IsFalse(j.ShouldShow(1, true, true, PracticeItemState.Resting, 10f, 109.9));
            Assert.IsTrue(j.ShouldShow(1, true, true, PracticeItemState.Resting, 10f, 110.0));
            Assert.IsTrue(j.ShouldShow(1, true, true, PracticeItemState.Resting, 10f, 140), "保留中も出し続ける（時刻だけが進む）");
        }

        [Test] public void ヒントは持てたら消えてその回は出ない()
        {
            var j = new PracticeHintJudge();
            j.ShouldShow(1, true, true, PracticeItemState.Resting, 10f, 100);
            Assert.IsTrue(j.ShouldShow(1, true, true, PracticeItemState.Resting, 10f, 111));
            Assert.IsFalse(j.ShouldShow(1, true, true, PracticeItemState.Held, 10f, 112), "持てたら消える");
            Assert.IsFalse(j.ShouldShow(1, true, true, PracticeItemState.Resting, 10f, 130), "落としても、その回は出さない");
        }

        [Test] public void ヒントは10秒より前に持てていれば出ない_Tutorialを出たら消えて次の回で数え直す()
        {
            var j = new PracticeHintJudge();
            j.ShouldShow(1, true, true, PracticeItemState.Resting, 10f, 100);
            j.ShouldShow(1, true, true, PracticeItemState.Held, 10f, 105);
            Assert.IsFalse(j.ShouldShow(1, true, true, PracticeItemState.Resting, 10f, 120));
            // Tutorial を出る
            Assert.IsFalse(j.ShouldShow(1, false, true, PracticeItemState.Resting, 10f, 121));
            // 次の回の Tutorial
            Assert.IsFalse(j.ShouldShow(2, true, true, PracticeItemState.Resting, 10f, 200));
            Assert.IsTrue(j.ShouldShow(2, true, true, PracticeItemState.Resting, 10f, 210));
            Assert.IsFalse(j.ShouldShow(2, false, true, PracticeItemState.Resting, 10f, 211), "Tutorial を出たら消す");
        }

        [Test] public void ヒントは保留中も出し続ける_時刻が進むだけで入力は変わらない()
        {
            var j = new PracticeHintJudge();
            j.ShouldShow(1, true, true, PracticeItemState.Resting, 10f, 100);
            Assert.IsTrue(j.ShouldShow(1, true, true, PracticeItemState.Resting, 10f, 111));
            for (double t = 112; t < 200; t += 4) Assert.IsTrue(j.ShouldShow(1, true, true, PracticeItemState.Resting, 10f, t), "保留中の " + t);
        }

        [Test] public void ヒントは自分の練習用お宝が無い端末には出ない()
        {
            var j = new PracticeHintJudge();
            Assert.IsFalse(j.ShouldShow(1, true, false, PracticeItemState.None, 10f, 100));
            Assert.IsFalse(j.ShouldShow(1, true, false, PracticeItemState.None, 10f, 200));
        }

        // ---- アバターの色 ----

        [Test] public void アバターの色はDeviceIdから今のClientNoを引いて決める_自分と色なしは塗らない()
        {
            var colors = new Dictionary<string, int> { { "devA", 0 }, { "devB", 1 } };
            var nos = new Dictionary<string, int> { { "devA", 2 }, { "devB", 3 } };
            Func<string, int> resolve = id => nos.TryGetValue(id, out var n) ? n : 0;
            Assert.AreEqual(0, AvatarColorMap.ColorOfClient(colors, resolve, 2, 9));
            Assert.AreEqual(1, AvatarColorMap.ColorOfClient(colors, resolve, 3, 9));
            Assert.AreEqual(-1, AvatarColorMap.ColorOfClient(colors, resolve, 4, 9), "色のない人");
            Assert.AreEqual(-1, AvatarColorMap.ColorOfClient(colors, resolve, 3, 3), "自分のアバター");
            nos["devA"] = 5; nos["devB"] = 2;   // ClientNo の入れ替わり
            Assert.AreEqual(1, AvatarColorMap.ColorOfClient(colors, resolve, 2, 9));
            Assert.AreEqual(0, AvatarColorMap.ColorOfClient(colors, resolve, 5, 9));
            Assert.AreEqual(-1, AvatarColorMap.ColorOfClient(new Dictionary<string, int>(), resolve, 2, 9), "待機中は塗りを外す");
            Assert.AreEqual(-1, AvatarColorMap.ColorOfClient(colors, resolve, 0, 9));
        }

        // ---- プレーヤーの色の一覧 ----

        [Test] public void 色の一覧の初期値は4枠で記号が重ならない()
        {
            var p = ScriptableObject.CreateInstance<ThPlayerPalette>();
            CollectionAssert.IsEmpty(p.Validate());
            CollectionAssert.AreEqual(new[] { "●", "▲", "■", "★" }, p.slots.Select(s => s.symbol).ToArray());
            CollectionAssert.AreEqual(new[] { "赤", "青", "緑", "黄" }, p.slots.Select(s => s.colorName).ToArray());
            Assert.AreEqual(new Color(1.0f, 0.30f, 0.30f, 1f), p.slots[0].beamColor);
            Assert.AreEqual(p.defaultBeamColor, p.BeamColor(-1), "色がなければ既定");
            Assert.AreEqual(p.slots[2].beamColor, p.BeamColor(2));
            UnityEngine.Object.DestroyImmediate(p);
        }

        [Test] public void 色の一覧のValidateは枠の数_空の記号_重複を検出する()
        {
            var p = ScriptableObject.CreateInstance<ThPlayerPalette>();
            p.slots.RemoveAt(3);
            Assert.IsTrue(p.Validate().Any(e => e.Contains("4")));
            p.slots = ThPlayerPalette.DefaultSlots();
            p.slots[1].symbol = " ";
            Assert.IsTrue(p.Validate().Any(e => e.Contains("空")));
            p.slots[1].symbol = "●";
            Assert.IsTrue(p.Validate().Any(e => e.Contains("重複")));
            UnityEngine.Object.DestroyImmediate(p);
        }

        [Test] public void 組み込みの色の一覧はコンテンツセットから参照され問題が無い()
        {
            var set = AssetDatabase.LoadAssetAtPath("Assets/_Content/TreasureHaul/Settings/ThContentSet.asset", typeof(ThContentSet)) as ThContentSet;
            Assert.IsNotNull(set.palette);
            CollectionAssert.IsEmpty(set.palette.Validate());
            // コンテンツセットの検証が、色の一覧の未設定を検出する
            var copy = UnityEngine.Object.Instantiate(set);
            copy.palette = null;
            Assert.IsTrue(copy.Validate().Any(e => e.Contains("ThPlayerPalette")));
            UnityEngine.Object.DestroyImmediate(copy);
        }

        [Test] public void 色の記号と名前の全文字が表示できる()
        {
            const string FontPath = "Assets/_Content/Fonts/Noto_Sans_JP/NotoSansJP-VariableFont_wght SDF.asset";
            var font = AssetDatabase.LoadAssetAtPath(FontPath, typeof(TMP_FontAsset)) as TMP_FontAsset;
            var set = AssetDatabase.LoadAssetAtPath("Assets/_Content/TreasureHaul/Settings/ThContentSet.asset", typeof(ThContentSet)) as ThContentSet;
            Assert.IsNotNull(font);
            var missing = new List<char>();
            foreach (var slot in set.palette.slots)
                foreach (var ch in slot.symbol + slot.colorName) if (!font.HasCharacter(ch, true)) missing.Add(ch);   // 読み取りのみ
            Assert.AreEqual(0, missing.Count, "表示できない文字: " + new string(missing.ToArray()) + "（Tools/Treasure Haul/文言の文字をフォントに足す を実行）");
        }

        [Test] public void 予備フォントのメニューは色の記号も集める()
        {
            var set = AssetDatabase.LoadAssetAtPath("Assets/_Content/TreasureHaul/Settings/ThContentSet.asset", typeof(ThContentSet)) as ThContentSet;
            string chars = STYLY.TIB_Template.TreasureHaul.Editor.ThFontExtraMenu.CollectCharacters(set.text, set.palette);
            foreach (var ch in "●▲■★") StringAssert.Contains(ch.ToString(), chars);
        }
    }
}
