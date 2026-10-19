using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    /// <summary>減額と表示（#39）: 計算・速度・再接触・端末の報告条件・ホストの受け付け・端末の反映</summary>
    public class ThDamageTests
    {
        // ---- DamageCalculator ----

        static DamageResult Hit(int baseValue, ref int value, ref int hits, int p1 = 30, int p2 = 30, int p3 = 40)
        {
            var r = DamageCalculator.Apply(baseValue, value, hits, p1, p2, p3);
            if (r.Applied) { value = r.Value; hits = r.Hits; }
            return r;
        }

        [Test] public void 計算_300Gは90_90_120で3回目に0になり消滅する()
        {
            int v = 300, h = 0;
            var r1 = Hit(300, ref v, ref h); Assert.AreEqual((90, 210, 1, false), (r1.Amount, r1.Value, r1.Hits, r1.Destroyed));
            var r2 = Hit(300, ref v, ref h); Assert.AreEqual((90, 120, 2, false), (r2.Amount, r2.Value, r2.Hits, r2.Destroyed));
            var r3 = Hit(300, ref v, ref h); Assert.AreEqual((120, 0, 3, true), (r3.Amount, r3.Value, r3.Hits, r3.Destroyed));
        }

        [Test] public void 計算_100Gは30_30_40()
        {
            int v = 100, h = 0;
            Assert.AreEqual((30, 70), (Hit(100, ref v, ref h).Amount, v));
            Assert.AreEqual((30, 40), (Hit(100, ref v, ref h).Amount, v));
            var r = Hit(100, ref v, ref h);
            Assert.AreEqual((40, 0, true), (r.Amount, r.Value, r.Destroyed));
        }

        [Test] public void 計算_すでに3回なら何もしない()
        {
            var r = DamageCalculator.Apply(300, 0, 3, 30, 30, 40);
            Assert.IsFalse(r.Applied);
            Assert.AreEqual(0, r.Amount);
            Assert.AreEqual(3, r.Hits);
        }

        [Test] public void 計算_減額率の合計が100でなくても3回目は必ず0になる()
        {
            int v = 300, h = 0;
            Hit(300, ref v, ref h, 10, 10, 10);   // -30 → 270
            Hit(300, ref v, ref h, 10, 10, 10);   // -30 → 240
            var r = Hit(300, ref v, ref h, 10, 10, 10);
            Assert.AreEqual((240, 0, true), (r.Amount, r.Value, r.Destroyed));
        }

        [Test] public void 計算_減額が今の価値より大きければ今の価値で止める()
        {
            var r = DamageCalculator.Apply(300, 50, 0, 50, 30, 20);   // 150 > 50
            Assert.AreEqual((50, 0, 1, false), (r.Amount, r.Value, r.Hits, r.Destroyed));
        }

        // ---- VelocityHistory ----

        [Test] public void 速度履歴_直近Nフレームの平均()
        {
            var h = new VelocityHistory(3);
            h.Add(new Vector3(3, 0, 0)); h.Add(new Vector3(6, 0, 0)); h.Add(new Vector3(9, 0, 0));
            Assert.AreEqual(6f, h.Average.x, 1e-5f);
            h.Add(new Vector3(12, 0, 0));   // 3 が押し出される
            Assert.AreEqual(9f, h.Average.x, 1e-5f);
            Assert.AreEqual(3, h.Count);
        }

        [Test] public void 速度履歴_フレーム数より少ないときは入っている分の平均()
        {
            var h = new VelocityHistory(3);
            Assert.AreEqual(Vector3.zero, h.Average);
            h.Add(new Vector3(2, 0, 0)); h.Add(new Vector3(4, 0, 0));
            Assert.AreEqual(3f, h.Average.x, 1e-5f);
        }

        [Test] public void 速度履歴_リセットとフレーム数の変更で忘れる()
        {
            var h = new VelocityHistory(3);
            h.Add(Vector3.one);
            h.Reset();
            Assert.AreEqual(0, h.Count);
            Assert.AreEqual(Vector3.zero, h.Average);
            h.Add(Vector3.one);
            h.SetCapacity(5);
            Assert.AreEqual(5, h.Capacity);
            Assert.AreEqual(0, h.Count);
        }

        // ---- PoseInterpolator の速度 ----

        const double Win = 0.1, Dt = 1.0 / 30.0;

        [Test] public void 補間_速度は時間幅の最古と最新から出す_1点だけ_古すぎるときは出さない()
        {
            var p = new PoseInterpolator();
            Assert.IsFalse(p.TryGetVelocity(1, 0.2, Win, Dt, 10f, out _));
            p.Add(1.0, Vector3.zero, Quaternion.identity);
            Assert.IsFalse(p.TryGetVelocity(1.0, 0.2, Win, Dt, 10f, out _), "1 点だけ");
            p.Add(1.0 + Dt, new Vector3(0.1f, 0, 0), Quaternion.identity);
            Assert.IsTrue(p.TryGetVelocity(1.05, 0.2, Win, Dt, 10f, out var v));
            Assert.AreEqual(3f, v.x, 1e-2f);
            Assert.IsFalse(p.TryGetVelocity(1.5, 0.2, Win, Dt, 10f, out _), "最後のサンプルが古い");
        }

        [Test] public void 補間_同じフレームにまとめて届いても速度が跳ね上がらない()
        {
            var p = new PoseInterpolator();
            // 実際は 1/30 秒ごとに 1cm ずつ動いた 3 点が、受信時刻 1e-5 秒おきに届いた
            p.Add(10.0, Vector3.zero, Quaternion.identity);
            p.Add(10.00001, new Vector3(0.01f, 0, 0), Quaternion.identity);
            p.Add(10.00002, new Vector3(0.02f, 0, 0), Quaternion.identity);
            Assert.IsTrue(p.TryGetVelocity(10.01, 0.2, Win, Dt, 10f, out var v));
            Assert.Less(v.magnitude, 1f, "数百 m/s にならない（分母は位置の送信間隔）");
            Assert.AreEqual(0.02f / (float)Dt, v.x, 1e-2f);
        }

        [Test] public void 補間_受信間隔がぶれても速さが数倍にならない()
        {
            // 一定速度 0.5 m/s で動く物の位置が、5ms・33ms・5ms・33ms… の間隔で届く
            var p = new PoseInterpolator();
            double t = 20.0;
            double[] gaps = { 0.005, 0.033, 0.005, 0.033, 0.005, 0.033 };
            double t0 = t;
            p.Add(t, Vector3.zero, Quaternion.identity);
            foreach (var g in gaps) { t += g; p.Add(t, new Vector3(0.5f * (float)(t - t0), 0, 0), Quaternion.identity); }
            Assert.IsTrue(p.TryGetVelocity(t, 0.2, Win, Dt, 10f, out var v));
            Assert.AreEqual(0.5f, v.x, 0.15f);
        }

        [Test] public void 補間_速度は追従速度の上限で切る()
        {
            var p = new PoseInterpolator();
            p.Add(1.0, Vector3.zero, Quaternion.identity);
            p.Add(1.0 + Dt, new Vector3(2f, 0, 0), Quaternion.identity);   // 60 m/s 相当
            Assert.IsTrue(p.TryGetVelocity(1.05, 0.2, Win, Dt, 10f, out var v));
            Assert.AreEqual(10f, v.magnitude, 1e-3f);
        }

        // ---- ContactTracker ----

        [Test] public void 再接触_接触中の再Enterは数えない_別コライダーも同じ相手()
        {
            var t = new ContactTracker();
            long k = ContactTracker.KeyOfItem(3);
            Assert.IsTrue(t.OnEnter(k, 10, 0.5f));
            Assert.IsFalse(t.OnEnter(k, 10.1, 0.5f), "押し付け続けても 1 回だけ");
            Assert.IsFalse(t.OnEnter(k, 11, 0.5f), "同じ相手の別のコライダー");
            t.OnExit(k, 11.5); t.OnExit(k, 11.8);
            Assert.IsFalse(t.OnEnter(k, 11.9, 0.5f), "まだ接触が残っている間は数えない");
            t.OnExit(k, 11.95); t.OnExit(k, 12);   // すべて離れた
            Assert.IsTrue(t.OnEnter(k, 13, 0.5f));
        }

        [Test] public void 再接触_離れて間隔未満は数えず_以上は数える()
        {
            var t = new ContactTracker();
            long k = ContactTracker.KeyOfCollider(1234);
            t.OnEnter(k, 10, 0.5f);
            t.OnExit(k, 11);
            Assert.IsFalse(t.OnEnter(k, 11.4, 0.5f));
            t.OnExit(k, 11.45);
            Assert.IsTrue(t.OnEnter(k, 11.95, 0.5f), "離れてから 0.5 秒");
        }

        [Test] public void 再接触_相手ごとに独立して数える_お宝の番号とコライダーIDは混ざらない()
        {
            var t = new ContactTracker();
            Assert.IsTrue(t.OnEnter(ContactTracker.KeyOfItem(5), 1, 0.5f));
            Assert.IsTrue(t.OnEnter(ContactTracker.KeyOfCollider(5), 1, 0.5f));
            Assert.IsTrue(t.OnEnter(ContactTracker.KeyOfItem(6), 1, 0.5f));
            Assert.AreEqual(3, t.TrackedCount);
        }

        [Test] public void 再接触_クリアで忘れる_数えていない離脱は無視する()
        {
            var t = new ContactTracker();
            long k = ContactTracker.KeyOfItem(1);
            t.OnEnter(k, 1, 0.5f);
            t.Clear();
            t.OnExit(k, 1.1);   // 例外にならず、記録も残らない
            Assert.IsTrue(t.OnEnter(k, 1.2, 0.5f));
        }

        // ---- 端末の報告の条件 ----

        static readonly Bounds Zone = new Bounds(new Vector3(0, 0.9f, 0.8f), new Vector3(3f, 1.2f, 1f));

        static List<SpawnedTreasure> Spawned(params TreasureKind[] kinds)
        {
            var list = new List<SpawnedTreasure>();
            for (int i = 0; i < kinds.Length; i++)
                list.Add(new SpawnedTreasure { Index = i, Kind = kinds[i], Value = 300, LocalPosition = new Vector3(i, 0, 5), CatalogIndex = 0 });
            return list;
        }

        static ThTreasureSetup Setup() => new ThTreasureSetup { CollectBounds = Zone, CatalogHash = "h" };

        sealed class ClientRig
        {
            public readonly CaptureTransport Tx = new CaptureTransport();
            public readonly ClientItemTable Table;
            public readonly List<ItemEvent> Events = new List<ItemEvent>();
            public int Own = 2;
            public ClientRig(int count = 1)
            {
                Table = new ClientItemTable(Tx, () => 100, () => Own, new ThParams(), Setup(), Spawned(Enumerable.Repeat(TreasureKind.S, count).ToArray())) { ParticipantPlaying = true };
                Table.Events.Subscribe(e => Events.Add(e));
            }
            /// <summary>自分が持った状態（承認済み・Held）にする</summary>
            public void Hold(int index, int reqNo = 1)
            {
                Assert.IsTrue(Table.TryBeginGrab(index, 10));
                var g = Tx.Of<ThGrabMsg>(Rpc.ThGrab).Last();
                Table.OnGrabbed(new ThGrabbedMsg { item = index, clientNo = Own, reqId = g.reqId, gen = reqNo, value = 300 }, 10.1);
            }
            public List<ThDamageMsg> Sent => Tx.Of<ThDamageMsg>(Rpc.ThDamage);
        }

        [Test] public void 報告_数える衝撃未満は送らず以上は送る()
        {
            var c = new ClientRig();
            c.Hold(0);
            Assert.IsFalse(c.Table.TryReportDamage(0, -1, Vector3.zero, 0.79f));
            Assert.AreEqual(0, c.Sent.Count);
            Assert.IsTrue(c.Table.TryReportDamage(0, -1, new Vector3(1, 2, 3), 0.8f));
            var m = c.Sent.Single();
            Assert.AreEqual((0, -1, 0.8f), (m.a, m.b, m.speed));
            Assert.AreEqual(new Vector3(1, 2, 3), m.pos);
        }

        [Test] public void 報告_落下中のお宝も送る_お宝同士は相手の番号を載せる()
        {
            var c = new ClientRig(2);
            c.Hold(0);
            c.Table.OnHandReleased(0, 11);
            Assert.AreEqual(ItemState.Falling, c.Table.Items[0].State);
            Assert.IsTrue(c.Table.TryReportDamage(0, 1, Vector3.zero, 2f));
            Assert.AreEqual(1, c.Sent.Single().b);
        }

        [Test] public void 報告_参加端末でない_Playing以外_Frozen_Mineでない_消えたお宝は送らない()
        {
            var c = new ClientRig(2);
            c.Hold(0);
            c.Table.ParticipantPlaying = false;
            Assert.IsFalse(c.Table.TryReportDamage(0, -1, Vector3.zero, 5f), "参加端末でない / Playing 以外");
            c.Table.ParticipantPlaying = true;
            Assert.IsFalse(c.Table.TryReportDamage(1, -1, Vector3.zero, 5f), "Mine でない");
            Assert.IsFalse(c.Table.TryReportDamage(9, -1, Vector3.zero, 5f), "存在しない番号");
            c.Table.Freeze();
            Assert.IsFalse(c.Table.TryReportDamage(0, -1, Vector3.zero, 5f), "Frozen");
            Assert.AreEqual(0, c.Sent.Count);
        }

        [Test] public void 報告_申請中_飛んでいる途中はまだ承認前なので送らない()
        {
            var c = new ClientRig();
            Assert.IsTrue(c.Table.TryBeginGrab(0, 10));
            Assert.IsFalse(c.Table.TryReportDamage(0, -1, Vector3.zero, 5f));
        }

        // ---- ホスト ----

        sealed class HostRig
        {
            public readonly List<(Rpc rpc, object dto)> Out = new List<(Rpc, object)>();
            public readonly HostItemTable Table;
            public HostRig(params TreasureKind[] kinds)
            {
                Table = new HostItemTable(Spawned(kinds), Zone, (r, d) => Out.Add((r, d)), null, new ThParams().treasure);
            }
            public List<ThDamagedMsg> Damaged => Out.Where(o => o.rpc == Rpc.ThDamaged).Select(o => (ThDamagedMsg)o.dto).ToList();
            /// <summary>deviceId が番号 index のお宝を持った状態にする</summary>
            public void Hold(string device, int clientNo, int index, int req = 1) =>
                Table.OnGrab(device, clientNo, new ThGrabMsg { item = index, reqId = req });
        }

        static ThDamageMsg Dmg(int a, int b = -1) => new ThDamageMsg { a = a, b = b, pos = new Vector3(1, 0, 2), speed = 2f };

        [Test] public void ホスト_障害物との衝突で1回減ってThDamagedが出る()
        {
            var h = new HostRig(TreasureKind.S);
            h.Hold("A", 2, 0);
            h.Table.OnDamage("A", Dmg(0), 100);
            var d = h.Damaged.Single();
            Assert.AreEqual((0, 1, 90, 210, false, 2, true), (d.item, d.hits, d.amount, d.value, d.destroyed, d.by, d.revealed));
            Assert.AreEqual(new Vector3(1, 0, 2), d.pos);
            Assert.AreEqual(210, h.Table.Items[0].Value);
            Assert.AreEqual(1, h.Table.Items[0].Hits);
            Assert.AreEqual(90, h.Table.DamageTotal);
            Assert.AreEqual(0, h.Table.Total, "合計額には何も足さない");
        }

        [Test] public void ホスト_お宝同士は両方を1回ずつ数える()
        {
            var h = new HostRig(TreasureKind.S, TreasureKind.S);
            h.Hold("A", 2, 0); h.Hold("B", 3, 1);
            h.Table.OnDamage("A", Dmg(0, 1), 100);
            var ds = h.Damaged;
            Assert.AreEqual(2, ds.Count);
            Assert.AreEqual(new[] { 0, 1 }, ds.Select(d => d.item).OrderBy(i => i).ToArray());
            Assert.AreEqual(1, h.Table.Items[0].Hits); Assert.AreEqual(1, h.Table.Items[1].Hits);
            Assert.AreEqual(180, h.Table.DamageTotal);
        }

        [Test] public void ホスト_片方が免除中ならもう片方だけ減る_報告による免除()
        {
            var h = new HostRig(TreasureKind.S, TreasureKind.S);
            h.Hold("A", 2, 0); h.Hold("B", 3, 1);
            h.Table.OnPoses("B", new ThPosesMsg { poses = new[] { new ThPoseEntry { item = 1, exempt = true } } });
            h.Table.OnDamage("A", Dmg(0, 1), 100);
            var d = h.Damaged.Single();
            Assert.AreEqual(0, d.item);
            Assert.AreEqual(0, h.Table.Items[1].Hits);
        }

        [Test] public void ホスト_片方が免除中ならもう片方だけ減る_状態による免除()
        {
            var h = new HostRig(TreasureKind.S, TreasureKind.S);
            h.Hold("A", 2, 0); h.Hold("B", 3, 1);
            double now = 100;
            foreach (var st in new[] { ItemState.Flying, ItemState.Placing, ItemState.Hovering })
            {
                now += 1;
                h.Out.Clear();
                h.Table.Items[1].State = st;
                h.Table.Items[0].Hits = 0; h.Table.Items[0].Value = 300;
                h.Table.OnDamage("A", Dmg(0, 1), now);
                Assert.AreEqual(0, h.Damaged.Single().item, st.ToString());
                Assert.AreEqual(0, h.Table.Items[1].Hits, st.ToString());
            }
        }

        [Test] public void ホスト_練習用_回収済み_消滅済みには適用しない_もう片方には適用する()
        {
            var h = new HostRig(TreasureKind.S, TreasureKind.S, TreasureKind.S, TreasureKind.S);
            h.Hold("A", 2, 0);
            h.Table.Items[1].Practice = true;
            h.Table.Items[2].Gone = true; h.Table.Items[2].State = ItemState.Collected;
            h.Table.Items[3].Gone = true; h.Table.Items[3].State = ItemState.Destroyed;
            h.Table.OnDamage("A", Dmg(0, 1), 100);
            h.Table.OnDamage("A", Dmg(0, 2), 101);
            h.Table.OnDamage("A", Dmg(0, 3), 102);
            Assert.AreEqual(3, h.Damaged.Count);
            Assert.IsTrue(h.Damaged.All(d => d.item == 0));
            Assert.AreEqual(3, h.Table.Items[0].Hits);
            Assert.AreEqual(0, h.Table.Items[1].Hits);
        }

        [Test] public void ホスト_0_3秒以内の同じ組み合わせは捨て_捨てた報告でも時刻が延びる()
        {
            var h = new HostRig(TreasureKind.S);
            h.Hold("A", 2, 0);
            h.Table.OnDamage("A", Dmg(0), 100.0);
            h.Table.OnDamage("A", Dmg(0), 100.2);   // 重複
            Assert.AreEqual(1, h.Damaged.Count);
            h.Table.OnDamage("A", Dmg(0), 100.45);  // 捨てた 100.2 から 0.25 → 重複（時刻が延びている）
            Assert.AreEqual(1, h.Damaged.Count);
            h.Table.OnDamage("A", Dmg(0), 100.9);   // 100.45 から 0.45 → 数える
            Assert.AreEqual(2, h.Damaged.Count);
        }

        [Test] public void ホスト_0_3秒を過ぎれば数える()
        {
            var h = new HostRig(TreasureKind.S);
            h.Hold("A", 2, 0);
            h.Table.OnDamage("A", Dmg(0), 100.0);
            h.Table.OnDamage("A", Dmg(0), 100.31);
            Assert.AreEqual(2, h.Damaged.Count);
        }

        [Test] public void ホスト_組み合わせの向きは同じキー_別の組み合わせは別に数える()
        {
            var h = new HostRig(TreasureKind.S, TreasureKind.S, TreasureKind.S);
            h.Hold("A", 2, 0); h.Hold("B", 3, 1); h.Hold("C", 4, 2);
            h.Table.OnDamage("A", Dmg(0, 1), 100.0);
            h.Table.OnDamage("B", Dmg(1, 0), 100.1);   // 向きが逆でも同じ衝突
            Assert.AreEqual(2, h.Damaged.Count);
            h.Table.OnDamage("A", Dmg(0, 2), 100.1);   // 別の組み合わせ
            Assert.AreEqual(4, h.Damaged.Count);
            h.Table.OnDamage("A", Dmg(0), 100.1);      // 障害物は (0, -1) で別のキー
            Assert.AreEqual(5, h.Damaged.Count);
        }

        [Test] public void ホスト_判定役でない送り手の報告は捨てて重複の時刻も更新しない()
        {
            var h = new HostRig(TreasureKind.S);
            h.Hold("A", 2, 0);
            h.Table.OnDamage("X", Dmg(0), 100.0);   // 判定役ではない
            Assert.AreEqual(0, h.Damaged.Count);
            h.Table.OnDamage("A", Dmg(0), 100.1);   // X の報告が時刻を更新していなければ数える
            Assert.AreEqual(1, h.Damaged.Count);
        }

        [Test] public void ホスト_3回目で消滅し世代が進み合計額は変わらず以後のメッセージは通らない()
        {
            var h = new HostRig(TreasureKind.S);
            h.Hold("A", 2, 0);
            int gen0 = h.Table.Items[0].Gen;
            h.Table.OnDamage("A", Dmg(0), 100);
            h.Table.OnDamage("A", Dmg(0), 101);
            h.Table.OnDamage("A", Dmg(0), 102);
            var last = h.Damaged.Last();
            Assert.IsTrue(last.destroyed);
            Assert.AreEqual((3, 120, 0), (last.hits, last.amount, last.value));
            var it = h.Table.Items[0];
            Assert.IsTrue(it.Gone);
            Assert.AreEqual(ItemState.Destroyed, it.State);
            Assert.IsNull(it.OwnerDevice);
            Assert.AreEqual(gen0 + 1, it.Gen);
            Assert.AreEqual(it.Gen, last.gen);
            Assert.AreEqual(0, h.Table.Total);
            Assert.AreEqual(300, h.Table.DamageTotal);

            h.Out.Clear();
            h.Table.OnCollect("A", new ThCollectMsg { item = 0, gen = it.Gen });
            h.Table.OnRest("A", new ThRestMsg { item = 0, gen = it.Gen, pos = Vector3.zero });
            h.Table.OnRelease("A", new ThReleaseMsg { item = 0, reqId = 1, gen = it.Gen, reason = (int)ReleaseReason.Dropped });
            Assert.AreEqual(0, h.Out.Count, "回収・置く・落とすは捨てる");
            h.Table.OnGrab("B", 3, new ThGrabMsg { item = 0, reqId = 5 });
            Assert.AreEqual(Rpc.ThGrabDenied, h.Out.Single().rpc, "つかむ申請は否認");
            h.Table.OnDamage("A", Dmg(0), 110);
            Assert.AreEqual(0, h.Damaged.Count(d => d.hits > 3), "消滅後は何もしない");
        }

        [Test] public void ホスト_ThItemsは回数と価値と消滅を載せる()
        {
            var h = new HostRig(TreasureKind.S, TreasureKind.S);
            h.Hold("A", 2, 0);
            h.Table.OnDamage("A", Dmg(0), 100);
            var e0 = h.Table.BuildItems().items[0];
            Assert.AreEqual((1, 210, false), (e0.hits, e0.value, e0.gone));
            h.Table.OnDamage("A", Dmg(0), 101); h.Table.OnDamage("A", Dmg(0), 102);
            var e1 = h.Table.BuildItems().items[0];
            Assert.AreEqual((3, 0, true, (int)ItemState.Destroyed), (e1.hits, e1.value, e1.gone, e1.state));
        }

        [Test] public void ホスト_byは持ち主_いなければ判定役_どちらもいなければ負()
        {
            var h = new HostRig(TreasureKind.S);
            h.Hold("A", 2, 0);
            h.Table.OnDamage("A", Dmg(0), 100);
            Assert.AreEqual(2, h.Damaged.Last().by);
            h.Table.OnRelease("A", new ThReleaseMsg { item = 0, reqId = 1, gen = 1, reason = (int)ReleaseReason.Dropped });   // 持ち主なし（落下中）
            h.Table.OnDamage("A", Dmg(0), 101);
            Assert.AreEqual(2, h.Damaged.Last().by, "判定役の ClientNo");
            var h2 = new HostRig(TreasureKind.S);
            h2.Table.Items[0].ControllerDevice = "B";   // 判定役だけ（クライアント番号は不明）
            h2.Table.OnDamage("B", Dmg(0), 100);
            Assert.AreEqual(-1, h2.Damaged.Last().by);
            // 誰も持ったことのないお宝に当てた: 当てた側の人には付けない
            var h3 = new HostRig(TreasureKind.S, TreasureKind.S);
            h3.Hold("A", 2, 0);
            h3.Table.OnDamage("A", new ThDamageMsg { a = 0, b = 1 }, 100);
            Assert.AreEqual(-1, h3.Damaged.Single(d => d.item == 1).by);
        }

        [Test] public void ホスト_ThPosesの免除は判定役からだけ覚え_持ち主が替わる_戻る_回収で消える()
        {
            var h = new HostRig(TreasureKind.S);
            h.Hold("A", 2, 0);
            var exempt = new ThPosesMsg { poses = new[] { new ThPoseEntry { item = 0, exempt = true } } };
            h.Table.OnPoses("X", exempt);   // 判定役でない
            Assert.IsFalse(h.Table.Items[0].ExemptReported);
            h.Table.OnPoses("A", exempt);
            Assert.IsTrue(h.Table.Items[0].ExemptReported);
            Assert.IsTrue(h.Table.Items[0].IsDamageExempt);
            h.Table.OnPoses("A", new ThPosesMsg { poses = new[] { new ThPoseEntry { item = 0, exempt = false } } });
            Assert.IsFalse(h.Table.Items[0].ExemptReported, "免除が終わった報告");

            h.Table.OnPoses("A", exempt);
            h.Table.OnRelease("A", new ThReleaseMsg { item = 0, reqId = 1, gen = 1, reason = (int)ReleaseReason.Cancel });   // つかめるに戻る
            Assert.IsFalse(h.Table.Items[0].ExemptReported, "ThFreed で消える");
            h.Hold("B", 3, 0, 2);   // 持ち主が替わる
            h.Table.OnPoses("A", exempt);   // 古い判定役 A の報告は覚えない
            Assert.IsFalse(h.Table.Items[0].ExemptReported);
            h.Table.OnPoses("B", exempt);
            Assert.IsTrue(h.Table.Items[0].ExemptReported);
            h.Table.OnCollect("B", new ThCollectMsg { item = 0, gen = h.Table.Items[0].Gen });
            Assert.IsFalse(h.Table.Items[0].ExemptReported, "回収で消える");
            h.Table.OnPoses("B", exempt);
            Assert.IsFalse(h.Table.Items[0].ExemptReported, "消えたお宝の報告は覚えない");
        }

        [Test] public void ホスト_Restになった後に遅れて届いた免除は覚えない()
        {
            var h = new HostRig(TreasureKind.S);
            h.Hold("A", 2, 0);
            h.Table.OnRelease("A", new ThReleaseMsg { item = 0, reqId = 1, gen = 1, reason = (int)ReleaseReason.Dropped });
            h.Table.OnRest("A", new ThRestMsg { item = 0, gen = 1, pos = new Vector3(0, 0, 5) });   // Resting へ
            Assert.AreEqual(ItemState.Resting, h.Table.Items[0].State);
            h.Table.OnPoses("A", new ThPosesMsg { poses = new[] { new ThPoseEntry { item = 0, exempt = true } } });
            Assert.IsFalse(h.Table.Items[0].ExemptReported);
        }

        [Test] public void ホスト_Droppedで免除の報告が消える_持ち主がいない間は判定役だけが送れる()
        {
            var h = new HostRig(TreasureKind.S);
            h.Hold("A", 2, 0);
            var ex = new ThPosesMsg { poses = new[] { new ThPoseEntry { item = 0, exempt = true } } };
            h.Table.OnPoses("A", ex);
            Assert.IsTrue(h.Table.Items[0].ExemptReported);
            h.Table.OnRelease("A", new ThReleaseMsg { item = 0, reqId = 1, gen = 1, reason = (int)ReleaseReason.Dropped });
            Assert.AreEqual(ItemState.Falling, h.Table.Items[0].State);
            Assert.IsFalse(h.Table.Items[0].ExemptReported, "Dropped で消える");
            h.Table.OnPoses("X", ex);   // 持ち主はいない。判定役でもない
            Assert.IsFalse(h.Table.Items[0].ExemptReported);
            h.Table.OnPoses("A", ex);   // 判定役（Falling 中）
            Assert.IsTrue(h.Table.Items[0].ExemptReported);
        }

        [Test] public void ホスト_持ち主がいるとき判定役が別の人でも持ち主以外の免除は覚えない()
        {
            var h = new HostRig(TreasureKind.S);
            h.Hold("A", 2, 0);
            h.Table.Items[0].ControllerDevice = "B";   // 持ち主 A と判定役 B が食い違う異常な状態
            h.Table.OnPoses("B", new ThPosesMsg { poses = new[] { new ThPoseEntry { item = 0, exempt = true } } });
            Assert.IsFalse(h.Table.Items[0].ExemptReported);
        }

        [Test] public void ホスト_受け付けの記録が区別して出る()
        {
            var h = new HostRig(TreasureKind.S, TreasureKind.S);
            var log = new List<string>();
            h.Table.Log = log.Add;
            h.Hold("A", 2, 0); h.Hold("B", 3, 1);
            h.Table.OnDamage("X", Dmg(0), 100);
            h.Table.OnDamage("A", Dmg(0), 101);
            h.Table.OnDamage("A", Dmg(0), 101.1);
            h.Table.Items[1].State = ItemState.Placing;
            h.Table.OnDamage("A", Dmg(0, 1), 105);
            Assert.IsTrue(log.Any(l => l.Contains("判定役でない")));
            Assert.IsTrue(log.Any(l => l.Contains("適用した") && l.Contains("item=0")));
            Assert.IsTrue(log.Any(l => l.Contains("重複")));
            Assert.IsTrue(log.Any(l => l.Contains("免除")));
        }

        // ---- 端末の反映 ----

        static ThDamagedMsg Damaged(int item, int hits, int amount, int value, bool destroyed = false, bool revealed = true, int gen = 1) =>
            new ThDamagedMsg { item = item, gen = gen, hits = hits, amount = amount, value = value, pos = Vector3.one, destroyed = destroyed, revealed = revealed };

        [Test] public void 端末_ThDamagedで価値と回数が更新されイベントが出る()
        {
            var c = new ClientRig();
            c.Table.OnDamaged(Damaged(0, 1, 90, 210));
            var it = c.Table.Items[0];
            Assert.AreEqual((1, 210), (it.Hits, it.Value));
            var ev = c.Events.Single(e => e.Kind == ItemEventKind.Damaged);
            Assert.AreEqual(90, ev.Damage.amount);
        }

        [Test] public void 端末_古い_同じ回数のThDamagedは捨てる()
        {
            var c = new ClientRig();
            c.Table.OnDamaged(Damaged(0, 2, 90, 120));
            c.Events.Clear();
            c.Table.OnDamaged(Damaged(0, 1, 90, 210));
            c.Table.OnDamaged(Damaged(0, 2, 90, 120));
            Assert.AreEqual(0, c.Events.Count);
            Assert.AreEqual((2, 120), (c.Table.Items[0].Hits, c.Table.Items[0].Value));
        }

        [Test] public void 端末_値段が公開前のお宝は公開されないまま価値だけ更新する()
        {
            var c = new ClientRig();
            c.Table.OnDamaged(Damaged(0, 1, 90, 210, revealed: false));
            var it = c.Table.Items[0];
            Assert.IsFalse(it.Revealed, "値札は「？」のまま");
            Assert.AreEqual(210, it.Value);
            Assert.IsFalse(c.Events.Single(e => e.Kind == ItemEventKind.Damaged).Damage.revealed);
            // 持った瞬間にホストの価値（下がった後）が出る
            c.Table.TryBeginGrab(0, 10);
            var g = c.Tx.Of<ThGrabMsg>(Rpc.ThGrab).Last();
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 2, reqId = g.reqId, gen = 1, value = 210 }, 10.1);
            Assert.IsTrue(it.Revealed);
            Assert.AreEqual(210, it.Value);
        }

        [Test] public void 端末_初めての一文は自分が動かしているお宝で1回だけ_値段が公開前でも出る()
        {
            var c = new ClientRig(2);
            c.Hold(0);
            c.Table.OnDamaged(Damaged(0, 1, 90, 210, revealed: false));
            Assert.IsTrue(c.Events.Last(e => e.Kind == ItemEventKind.Damaged).ShowNote);
            Assert.IsTrue(c.Table.DamageNoteShown);
            c.Table.OnDamaged(Damaged(0, 2, 90, 120));
            Assert.IsFalse(c.Events.Last(e => e.Kind == ItemEventKind.Damaged).ShowNote, "2 回目以降は出さない");
        }

        [Test] public void 端末_自分が動かしていないお宝では一文を出さず消費もしない()
        {
            var c = new ClientRig(2);
            c.Table.OnDamaged(Damaged(1, 1, 90, 210));
            Assert.IsFalse(c.Events.Last().ShowNote);
            Assert.IsFalse(c.Table.DamageNoteShown);
            c.Hold(0);
            c.Table.OnDamaged(Damaged(0, 1, 90, 210));
            Assert.IsTrue(c.Events.Last(e => e.Kind == ItemEventKind.Damaged).ShowNote);
        }

        [Test] public void 端末_消滅でMineと送り直しが消えDestroyedイベントが出る()
        {
            var c = new ClientRig();
            c.Hold(0);
            c.Table.OnHandReleased(0, 11);   // Falling
            c.Table.NoteSettled(0, new Vector3(1, 0, 6), Quaternion.identity, 12);   // 置いた後の承認待ち（PendingRest）
            Assert.IsTrue(c.Table.Items[0].PendingRest);
            c.Table.OnDamaged(Damaged(0, 3, 120, 0, destroyed: true, gen: 3));
            var it = c.Table.Items[0];
            Assert.IsTrue(it.Gone);
            Assert.IsFalse(it.Mine); Assert.IsFalse(it.PendingRest); Assert.IsFalse(it.PendingCollect); Assert.IsFalse(it.Approved);
            Assert.AreEqual(ItemState.Destroyed, it.State);
            Assert.IsTrue(c.Events.Any(e => e.Kind == ItemEventKind.Destroyed));
            // 送り直しもしない
            c.Tx.Sent.Clear();
            c.Table.Tick(100);
            Assert.AreEqual(0, c.Tx.Sent.Count);
            // 以後の ThDamaged は反映しない
            c.Events.Clear();
            c.Table.OnDamaged(Damaged(0, 3, 0, 0, destroyed: true));
            Assert.AreEqual(0, c.Events.Count);
        }

        [Test] public void 端末_ThDamagedを捨ててもThItemsで回数と消滅が追いつく()
        {
            var c = new ClientRig(2);
            c.Table.OnItems(new ThItemsMsg { items = new[] { new ThItemEntry { item = 0, state = (int)ItemState.Resting, gen = 0, value = 210, hits = 1, revealed = true } } }, 1);
            Assert.AreEqual((1, 210), (c.Table.Items[0].Hits, c.Table.Items[0].Value));
            Assert.IsTrue(c.Events.Any(e => e.Kind == ItemEventKind.HitsSynced));
            Assert.IsFalse(c.Events.Any(e => e.Kind == ItemEventKind.Damaged), "「-NNG」と音は出さない");

            c.Table.OnItems(new ThItemsMsg { items = new[] { new ThItemEntry { item = 0, state = (int)ItemState.Destroyed, gen = 1, value = 0, hits = 3, revealed = true, gone = true } } }, 2);
            Assert.IsTrue(c.Table.Items[0].Gone);
            Assert.AreEqual(ItemState.Destroyed, c.Table.Items[0].State);
            Assert.IsTrue(c.Events.Any(e => e.Kind == ItemEventKind.DestroyedQuiet));
            Assert.IsFalse(c.Events.Any(e => e.Kind == ItemEventKind.Destroyed), "砕ける演出・音は出さない");
        }

        [Test] public void 端末_ThItemsのgone_Collectedは今まで通り回収として扱う()
        {
            var c = new ClientRig();
            c.Table.OnItems(new ThItemsMsg { items = new[] { new ThItemEntry { item = 0, state = (int)ItemState.Collected, gen = 1, value = 300, revealed = true, gone = true } } }, 1);
            Assert.IsTrue(c.Events.Any(e => e.Kind == ItemEventKind.Collected));
            Assert.IsFalse(c.Events.Any(e => e.Kind == ItemEventKind.DestroyedQuiet));
        }

        [Test] public void 端末_古いThItemsの回数では戻らない()
        {
            var c = new ClientRig();
            c.Table.OnDamaged(Damaged(0, 2, 90, 120));
            c.Table.OnItems(new ThItemsMsg { items = new[] { new ThItemEntry { item = 0, state = (int)ItemState.Resting, gen = 0, value = 210, hits = 1, revealed = true } } }, 1);
            Assert.AreEqual((2, 120), (c.Table.Items[0].Hits, c.Table.Items[0].Value));
        }
    }
}
