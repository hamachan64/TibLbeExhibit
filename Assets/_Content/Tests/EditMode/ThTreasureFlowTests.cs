using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    /// <summary>キャプチャ用の送信口</summary>
    sealed class CaptureTransport : IThTransport
    {
        public readonly List<(Rpc rpc, string[] args)> Sent = new List<(Rpc, string[])>();
        public void Send(Rpc rpc, string[] args) => Sent.Add((rpc, args));
        public List<T> Of<T>(Rpc rpc) where T : class
        {
            var list = new List<T>();
            foreach (var (r, a) in Sent) if (r == rpc && ThMessages.TryDecode(a, out long _, out T dto)) list.Add(dto);
            return list;
        }
    }

    /// <summary>お宝のつかむ手順・回収・状態表の追いつき（ホストの状態表と端末の表。偽の通信でホスト＋端末をつなぐ）</summary>
    public class ThTreasureFlowTests
    {
        // ---- 部品 ----

        static readonly Bounds Zone = new Bounds(new Vector3(0, 0.9f, 0.8f), new Vector3(3f, 1.2f, 1f));

        static List<SpawnedTreasure> MakeSpawned(params TreasureKind[] kinds)
        {
            var list = new List<SpawnedTreasure>();
            for (int i = 0; i < kinds.Length; i++)
                list.Add(new SpawnedTreasure { Index = i, Kind = kinds[i], Value = 200 + i * 100, LocalPosition = new Vector3(i, 0, 5), CatalogIndex = 0 });
            return list;
        }

        static ThTreasureSetup MakeSetup(string hash = "h1")
        {
            var pts = new List<PlacementPoint>();
            for (int i = 0; i < 30; i++) pts.Add(new PlacementPoint(new Vector3((i % 5) * 0.75f - 1.5f, 0, 3f + i / 5), i % 3));
            return new ThTreasureSetup
            {
                Points = pts,
                Catalog = new[] { new SpawnCatalogEntry(TreasureKind.S, 0), new SpawnCatalogEntry(TreasureKind.S, 1), new SpawnCatalogEntry(TreasureKind.M, 2) },
                CollectBounds = Zone,
                CatalogHash = hash,
            };
        }

        // ---- ホストの状態表（単体） ----

        sealed class HostRig
        {
            public readonly List<(Rpc rpc, object dto)> Out = new List<(Rpc, object)>();
            public readonly HostItemTable Table;
            public HostRig(params TreasureKind[] kinds) { Table = new HostItemTable(MakeSpawned(kinds), Zone, (r, d) => Out.Add((r, d))); }
            public List<T> Of<T>(Rpc rpc) => Out.Where(o => o.rpc == rpc).Select(o => (T)o.dto).ToList();
            public void Clear() => Out.Clear();
        }

        static ThGrabMsg Grab(int item, int req, int gen = 0) => new ThGrabMsg { item = item, reqId = req, gen = gen, kind = 0 };

        [Test] public void 承認_Sを先着で認め世代が進み値段が公開される()
        {
            var h = new HostRig(TreasureKind.S);
            h.Table.OnGrab("A", 2, Grab(0, 1));
            var g = h.Of<ThGrabbedMsg>(Rpc.ThGrabbed).Single();
            Assert.AreEqual(2, g.clientNo); Assert.AreEqual(1, g.reqId); Assert.AreEqual(1, g.gen); Assert.AreEqual(200, g.value);
            Assert.IsTrue(h.Table.Items[0].Revealed);
            Assert.AreEqual(ItemState.Held, h.Table.Items[0].State);
        }

        [Test] public void 同じお宝への同時申請は先着1人()
        {
            var h = new HostRig(TreasureKind.S);
            h.Table.OnGrab("A", 2, Grab(0, 1));
            h.Table.OnGrab("B", 3, Grab(0, 1));
            Assert.AreEqual(1, h.Of<ThGrabbedMsg>(Rpc.ThGrabbed).Count);
            var d = h.Of<ThGrabDeniedMsg>(Rpc.ThGrabDenied).Single();
            Assert.AreEqual(3, d.clientNo);
            Assert.AreEqual("A", h.Table.Items[0].OwnerDevice);
        }

        [Test] public void Mのお宝へのSの申請は否認する()
        {
            var h = new HostRig(TreasureKind.M);
            h.Table.OnGrab("A", 2, Grab(0, 1));
            Assert.AreEqual(0, h.Of<ThGrabbedMsg>(Rpc.ThGrabbed).Count);
            Assert.AreEqual(1, h.Of<ThGrabDeniedMsg>(Rpc.ThGrabDenied).Count);
            Assert.AreEqual(ItemState.Resting, h.Table.Items[0].State);
        }

        [Test] public void 荷台の範囲内に置かれたお宝はつかめない()
        {
            var sp = MakeSpawned(TreasureKind.S);
            sp[0].LocalPosition = new Vector3(0, 0.4f, 0.8f);
            var outBox = new List<(Rpc, object)>();
            var t = new HostItemTable(sp, Zone, (r, d) => outBox.Add((r, d)));
            t.OnGrab("A", 2, Grab(0, 1));
            Assert.AreEqual(Rpc.ThGrabDenied, outBox.Single().Item1);
        }

        [Test] public void 同じ申請の再送には同じ承認を送り直す()
        {
            var h = new HostRig(TreasureKind.S);
            h.Table.OnGrab("A", 2, Grab(0, 1));
            h.Table.OnGrab("A", 2, Grab(0, 1));
            var gs = h.Of<ThGrabbedMsg>(Rpc.ThGrabbed);
            Assert.AreEqual(2, gs.Count);
            Assert.AreEqual(gs[0].gen, gs[1].gen);
        }

        [Test] public void 取りやめ_承認済みの申請なら解除して世代が進み通知する()
        {
            var h = new HostRig(TreasureKind.S);
            h.Table.OnGrab("A", 2, Grab(0, 1));
            h.Table.OnRelease("A", new ThReleaseMsg { item = 0, reqId = 1, gen = 1, reason = (int)ReleaseReason.Cancel });
            var f = h.Of<ThFreedMsg>(Rpc.ThFreed).Single();
            Assert.AreEqual(2, f.gen);
            Assert.AreEqual(ItemState.Resting, h.Table.Items[0].State);
            Assert.IsTrue(h.Table.Items[0].Revealed);   // 公開は戻らない
        }

        [Test] public void 取りやめ_まだ届いていない申請は覚えて後から届いても認めない()
        {
            var h = new HostRig(TreasureKind.S);
            h.Table.OnRelease("A", new ThReleaseMsg { item = 0, reqId = 7, reason = (int)ReleaseReason.Cancel });
            h.Table.OnGrab("A", 2, Grab(0, 7));
            Assert.AreEqual(0, h.Of<ThGrabbedMsg>(Rpc.ThGrabbed).Count);
            Assert.AreEqual(0, h.Of<ThGrabDeniedMsg>(Rpc.ThGrabDenied).Count);
            Assert.AreEqual(ItemState.Resting, h.Table.Items[0].State);
            // 別の申請IDは認める
            h.Table.OnGrab("A", 2, Grab(0, 8));
            Assert.AreEqual(1, h.Of<ThGrabbedMsg>(Rpc.ThGrabbed).Count);
        }

        [Test] public void 取りやめ_再申請の後に古い申請の取りやめが届いても新しい承認は消えない()
        {
            var h = new HostRig(TreasureKind.S);
            h.Table.OnGrab("A", 2, Grab(0, 1));                                                         // 承認 gen1
            h.Table.OnRelease("A", new ThReleaseMsg { item = 0, reqId = 1, gen = 1, reason = 0 });      // 取りやめ → gen2
            h.Table.OnGrab("A", 2, Grab(0, 2));                                                         // 再申請 → gen3
            h.Table.OnRelease("A", new ThReleaseMsg { item = 0, reqId = 1, gen = 1, reason = 0 });      // 古い申請IDの取りやめ（手順 5 で返ってきた物）
            Assert.AreEqual(ItemState.Held, h.Table.Items[0].State);
            Assert.AreEqual(2, h.Table.Items[0].AcceptedReqId);
            Assert.AreEqual(3, h.Table.Items[0].Gen);
        }

        [Test] public void 落とした_世代は変えず持ち主を外してFallingにする()
        {
            var h = new HostRig(TreasureKind.S);
            h.Table.OnGrab("A", 2, Grab(0, 1));
            h.Table.OnRelease("A", new ThReleaseMsg { item = 0, reqId = 1, gen = 1, reason = (int)ReleaseReason.Dropped });
            Assert.AreEqual(ItemState.Falling, h.Table.Items[0].State);
            Assert.AreEqual(1, h.Table.Items[0].Gen);
            Assert.AreEqual(-1, h.Table.Items[0].OwnerClientNo);
            Assert.AreEqual(0, h.Of<ThFreedMsg>(Rpc.ThFreed).Count);
            // 他人の落とした通知・世代違いは受理しない
            var h2 = new HostRig(TreasureKind.S);
            h2.Table.OnGrab("A", 2, Grab(0, 1));
            h2.Table.OnRelease("B", new ThReleaseMsg { item = 0, reqId = 1, gen = 1, reason = 1 });
            h2.Table.OnRelease("A", new ThReleaseMsg { item = 0, reqId = 1, gen = 0, reason = 1 });
            Assert.AreEqual(ItemState.Held, h2.Table.Items[0].State);
        }

        [Test] public void 世代が違うRestとCollectは受理しない_判定役以外も受理しない()
        {
            var h = new HostRig(TreasureKind.S);
            h.Table.OnGrab("A", 2, Grab(0, 1));
            h.Table.OnRest("A", new ThRestMsg { item = 0, gen = 0, pos = Vector3.one });
            h.Table.OnRest("B", new ThRestMsg { item = 0, gen = 1, pos = Vector3.one });
            h.Table.OnCollect("A", new ThCollectMsg { item = 0, gen = 5 });
            h.Table.OnCollect("B", new ThCollectMsg { item = 0, gen = 1 });
            Assert.AreEqual(ItemState.Held, h.Table.Items[0].State);
            Assert.AreEqual(0, h.Table.Total);
        }

        [Test] public void Restは1回だけ受理して世代が進む_送り直しは捨てる()
        {
            var h = new HostRig(TreasureKind.S);
            h.Table.OnGrab("A", 2, Grab(0, 1));
            h.Table.OnRelease("A", new ThReleaseMsg { item = 0, reqId = 1, gen = 1, reason = 1 });
            var rest = new ThRestMsg { item = 0, gen = 1, pos = new Vector3(1, 0, 6) };
            h.Table.OnRest("A", rest);
            h.Table.OnRest("A", rest);
            var f = h.Of<ThFreedMsg>(Rpc.ThFreed);
            Assert.AreEqual(1, f.Count);
            Assert.AreEqual(2, f[0].gen);
            Assert.AreEqual(new Vector3(1, 0, 6), h.Table.Items[0].RestPos);
        }

        [Test] public void 落とすメッセージを取りこぼしてもRestが受理される()
        {
            var h = new HostRig(TreasureKind.S);
            h.Table.OnGrab("A", 2, Grab(0, 1));   // ThRelease(落とした) が届かない
            h.Table.OnRest("A", new ThRestMsg { item = 0, gen = 1, pos = Vector3.zero });
            Assert.AreEqual(ItemState.Resting, h.Table.Items[0].State);
        }

        [Test] public void 回収_送り直しを二重に数えず合計に加える()
        {
            var h = new HostRig(TreasureKind.S, TreasureKind.S);
            h.Table.OnGrab("A", 2, Grab(1, 1));   // 値段 300
            h.Table.OnRelease("A", new ThReleaseMsg { item = 1, reqId = 1, gen = 1, reason = 1 });
            var col = new ThCollectMsg { item = 1, gen = 1 };
            h.Table.OnCollect("A", col);
            h.Table.OnCollect("A", col);
            var c = h.Of<ThCollectedMsg>(Rpc.ThCollected);
            Assert.AreEqual(1, c.Count);
            Assert.AreEqual(300, c[0].value); Assert.AreEqual(300, c[0].total); Assert.IsFalse(c[0].damaged);
            Assert.AreEqual(300, h.Table.Total);
            Assert.IsTrue(h.Table.Items[1].Gone);
            // 回収済みはつかめない
            h.Table.OnGrab("A", 2, Grab(1, 2));
            Assert.AreEqual(1, h.Of<ThGrabbedMsg>(Rpc.ThGrabbed).Count);
            // もう1つ回収して合計
            h.Table.OnGrab("A", 2, Grab(0, 3));
            h.Table.OnCollect("A", new ThCollectMsg { item = 0, gen = 1 });
            Assert.AreEqual(500, h.Table.Total);
        }

        [Test] public void ClientNoが付け替わっても状態表の持ち主は今の番号になる()
        {
            var map = new Dictionary<string, int> { { "A", 2 } };
            var outBox = new List<(Rpc, object)>();
            var t = new HostItemTable(MakeSpawned(TreasureKind.S), Zone, (r, d) => outBox.Add((r, d)), id => map.TryGetValue(id, out var n) ? n : -1);
            t.OnGrab("A", 2, Grab(0, 1));
            Assert.AreEqual(2, t.BuildItems().items[0].owner);
            map["A"] = 7;   // サーバーの再起動などで ClientNo が入れ替わった
            var e = t.BuildItems().items[0];
            Assert.AreEqual(7, e.owner);
            Assert.AreEqual(7, e.ctl);
            // 再送された申請には、今の番号で承認を送り直す
            t.OnGrab("A", 7, Grab(0, 1));
            Assert.AreEqual(7, ((ThGrabbedMsg)outBox.Last(o => o.Item1 == Rpc.ThGrabbed).Item2).clientNo);
        }

        [Test] public void Restの位置が荷台の回収範囲の中なら回収として扱う()
        {
            var h = new HostRig(TreasureKind.S);
            h.Table.OnGrab("A", 2, Grab(0, 1));
            h.Table.OnRelease("A", new ThReleaseMsg { item = 0, reqId = 1, gen = 1, reason = 1 });
            h.Table.OnRest("A", new ThRestMsg { item = 0, gen = 1, pos = new Vector3(0, 0.4f, 0.8f) });
            Assert.IsTrue(h.Table.Items[0].Gone);
            Assert.AreEqual(200, h.Table.Total);
            Assert.AreEqual(1, h.Of<ThCollectedMsg>(Rpc.ThCollected).Count);
            Assert.AreEqual(0, h.Of<ThFreedMsg>(Rpc.ThFreed).Count);
        }

        [Test] public void 状態表は全お宝を載せる()
        {
            var h = new HostRig(TreasureKind.S, TreasureKind.M);
            h.Table.OnGrab("A", 2, Grab(0, 1));
            var items = h.Table.BuildItems().items;
            Assert.AreEqual(2, items.Length);
            Assert.AreEqual((int)ItemState.Held, items[0].state);
            Assert.AreEqual(2, items[0].owner);
            Assert.IsTrue(items[0].revealed);
            Assert.IsFalse(items[1].revealed);
        }

        // ---- 端末の表（単体） ----

        sealed class ClientRig
        {
            public readonly CaptureTransport Tx = new CaptureTransport();
            public readonly ClientItemTable Table;
            public readonly List<ItemEvent> Events = new List<ItemEvent>();
            public int Own = 2;
            public ClientRig(params TreasureKind[] kinds)
            {
                Table = new ClientItemTable(Tx, () => 100, () => Own, new ThParams(), MakeSetup(), MakeSpawned(kinds)) { ParticipantPlaying = true };
                Table.Events.Subscribe(e => Events.Add(e));
            }
            public bool Has(ItemEventKind k) => Events.Any(e => e.Kind == k);
        }

        [Test] public void 端末_承認で値札が公開され高い宝は発見演出が出る()
        {
            var c = new ClientRig(TreasureKind.S);
            c.Table.Items[0].BaseValue = 700; c.Table.Items[0].Value = 700;
            Assert.IsTrue(c.Table.TryBeginGrab(0, 10));
            var g = c.Tx.Of<ThGrabMsg>(Rpc.ThGrab).Single();
            Assert.AreEqual(ItemState.Flying, c.Table.Items[0].State);
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 2, reqId = g.reqId, gen = 1, value = 700 }, 10.1);
            Assert.IsTrue(c.Has(ItemEventKind.Approved));
            Assert.IsTrue(c.Has(ItemEventKind.Revealed));
            Assert.IsTrue(c.Has(ItemEventKind.Discovered));
            Assert.AreEqual(ItemState.Held, c.Table.Items[0].State);
        }

        [Test] public void 端末_発見演出は初めて公開されたときだけ_ThItemsで追いついたときは値札だけ()
        {
            var c = new ClientRig(TreasureKind.S);
            c.Table.Items[0].Value = 800;
            c.Table.OnItems(new ThItemsMsg { items = new[] { new ThItemEntry { item = 0, state = (int)ItemState.Held, owner = 3, gen = 1, value = 800, revealed = true } } }, 1);
            Assert.IsTrue(c.Has(ItemEventKind.Revealed));
            Assert.IsFalse(c.Has(ItemEventKind.Discovered));
            // 低い宝は演出なし
            var c2 = new ClientRig(TreasureKind.S);
            c2.Table.TryBeginGrab(0, 0);
            c2.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 2, reqId = 1, gen = 1, value = 200 }, 0.1);
            Assert.IsFalse(c2.Has(ItemEventKind.Discovered));
        }

        [Test] public void 端末_否認で元の位置へ戻し取りやめを送る()
        {
            var c = new ClientRig(TreasureKind.S);
            c.Table.TryBeginGrab(0, 0);
            c.Table.OnDenied(new ThGrabDeniedMsg { item = 0, clientNo = 2, reqId = 1 }, 0.2);
            Assert.IsTrue(c.Has(ItemEventKind.Reverted));
            Assert.AreEqual(ItemState.Resting, c.Table.Items[0].State);
            var r = c.Tx.Of<ThReleaseMsg>(Rpc.ThRelease).Single();
            Assert.AreEqual(1, r.reqId); Assert.AreEqual((int)ReleaseReason.Cancel, r.reason);
            Assert.IsTrue(c.Table.IsGrabbable(c.Table.Items[0]));
        }

        [Test] public void 端末_承認の待ち上限を超えたら取りやめる()
        {
            var c = new ClientRig(TreasureKind.S);
            c.Table.TryBeginGrab(0, 0);
            c.Table.Tick(0.9);
            Assert.IsFalse(c.Has(ItemEventKind.Reverted));
            c.Table.Tick(1.01);
            Assert.IsTrue(c.Has(ItemEventKind.Reverted));
            Assert.AreEqual(1, c.Tx.Of<ThReleaseMsg>(Rpc.ThRelease).Count);
        }

        [Test] public void 端末_承認前に手を開いたら取りやめる()
        {
            var c = new ClientRig(TreasureKind.S);
            c.Table.TryBeginGrab(0, 0);
            c.Table.OnHandReleased(0, 0.1);
            Assert.IsTrue(c.Has(ItemEventKind.Reverted));
            Assert.AreEqual((int)ReleaseReason.Cancel, c.Tx.Of<ThReleaseMsg>(Rpc.ThRelease).Single().reason);
        }

        [Test] public void 端末_取りやめ後の再申請に古い承認が届いても新しい申請は消えない()
        {
            var c = new ClientRig(TreasureKind.S);
            c.Table.TryBeginGrab(0, 0);                          // 申請1
            c.Table.OnHandReleased(0, 0.1);                      // 取りやめ
            Assert.IsTrue(c.Table.TryBeginGrab(0, 0.2));         // 申請2
            c.Tx.Sent.Clear();
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 2, reqId = 1, gen = 1, value = 200 }, 0.3);   // 申請1への古い承認
            Assert.IsFalse(c.Table.Items[0].Approved);
            Assert.IsTrue(c.Table.Items[0].ReqPending);
            var r = c.Tx.Of<ThReleaseMsg>(Rpc.ThRelease).Single();
            Assert.AreEqual(1, r.reqId);                         // 古い申請IDで取りやめを返す
            Assert.AreEqual((int)ReleaseReason.Cancel, r.reason);
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 2, reqId = 2, gen = 3, value = 200 }, 0.4);
            Assert.IsTrue(c.Table.Items[0].Approved);
        }

        [Test] public void 端末_取りやめ後の再申請の承認待ちの間に古い取りやめのThFreedが届いても新しい申請は消えない()
        {
            var c = new ClientRig(TreasureKind.S);
            c.Table.TryBeginGrab(0, 0);                          // 申請1
            c.Table.OnHandReleased(0, 0.1);                      // 取りやめ
            Assert.IsTrue(c.Table.TryBeginGrab(0, 0.2));         // 申請2（承認待ち）
            // 実際のホストの返し方: ThGrabbed(申請1, 世代1) → ThFreed(世代2) → ThGrabbed(申請2, 世代3)
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 2, reqId = 1, gen = 1, value = 200 }, 0.3);
            c.Events.Clear();
            c.Table.OnFreed(new ThFreedMsg { item = 0, gen = 2, pos = new Vector3(0, 0, 5) });
            Assert.IsTrue(c.Table.Items[0].ReqPending, "申請2 は消えない");
            Assert.IsTrue(c.Table.Items[0].Mine);
            Assert.IsFalse(c.Has(ItemEventKind.Freed));
            Assert.AreEqual(2, c.Table.Items[0].Gen);
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 2, reqId = 2, gen = 3, value = 200 }, 0.4);
            Assert.IsTrue(c.Table.Items[0].Approved);
            Assert.AreEqual(ItemState.Held, c.Table.Items[0].State);
        }

        [Test] public void 端末_他の人の承認を受けたらつかめなくなり先に申請していた自分は戻る()
        {
            var c = new ClientRig(TreasureKind.S);
            c.Table.TryBeginGrab(0, 0);
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 3, reqId = 1, gen = 1, value = 200 }, 0.1);
            Assert.IsTrue(c.Has(ItemEventKind.Reverted));
            Assert.IsTrue(c.Has(ItemEventKind.RemoteHeld));
            Assert.IsFalse(c.Table.IsGrabbable(c.Table.Items[0]));
            Assert.AreEqual(3, c.Table.Items[0].OwnerClientNo);
        }

        [Test] public void 端末_古い世代のメッセージは捨てる()
        {
            var c = new ClientRig(TreasureKind.S);
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 3, reqId = 1, gen = 5, value = 200 }, 0);
            c.Table.OnFreed(new ThFreedMsg { item = 0, gen = 4, pos = Vector3.zero });   // 古い
            Assert.AreEqual(3, c.Table.Items[0].OwnerClientNo);
            c.Table.OnFreed(new ThFreedMsg { item = 0, gen = 6, pos = new Vector3(2, 0, 2) });
            Assert.AreEqual(-1, c.Table.Items[0].OwnerClientNo);
            Assert.AreEqual(new Vector3(2, 0, 2), c.Table.Items[0].RestPos);
            Assert.AreEqual(6, c.Table.Items[0].Gen);
        }

        [Test] public void 端末_手を開いて落とすとRelease_Droppedを送りFallingになる()
        {
            var c = new ClientRig(TreasureKind.S);
            c.Table.TryBeginGrab(0, 0);
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 2, reqId = 1, gen = 1, value = 200 }, 0.1);
            c.Table.OnHandReleased(0, 1);
            Assert.IsTrue(c.Has(ItemEventKind.Dropped));
            Assert.AreEqual(ItemState.Falling, c.Table.Items[0].State);
            var r = c.Tx.Of<ThReleaseMsg>(Rpc.ThRelease).Single();
            Assert.AreEqual((int)ReleaseReason.Dropped, r.reason); Assert.AreEqual(1, r.gen);
        }

        [Test] public void 端末_ThRestとThCollectはThItemsに反映されるまで送り直す()
        {
            var c = new ClientRig(TreasureKind.S, TreasureKind.S);
            foreach (var i in new[] { 0, 1 })
            {
                c.Table.TryBeginGrab(i, 0);
                c.Table.OnGrabbed(new ThGrabbedMsg { item = i, clientNo = 2, reqId = i + 1, gen = 1, value = 200 }, 0.1);
                c.Table.OnHandReleased(i, 1);
            }
            c.Tx.Sent.Clear();
            c.Table.NoteSettled(0, new Vector3(1, 0, 6), Quaternion.identity, 2);
            c.Table.NoteInCollectZone(1, 2);
            Assert.AreEqual(1, c.Tx.Of<ThRestMsg>(Rpc.ThRest).Count);
            Assert.AreEqual(1, c.Tx.Of<ThCollectMsg>(Rpc.ThCollect).Count);
            c.Table.Tick(2.2);   // 再送間隔 0.5 秒より短い
            Assert.AreEqual(1, c.Tx.Of<ThRestMsg>(Rpc.ThRest).Count);
            c.Table.Tick(2.6);
            Assert.AreEqual(2, c.Tx.Of<ThRestMsg>(Rpc.ThRest).Count);
            Assert.AreEqual(2, c.Tx.Of<ThCollectMsg>(Rpc.ThCollect).Count);
            // ThItems に反映（置かれた: 世代が進んだ / 回収済み）されたら送り直しをやめる
            c.Table.OnItems(new ThItemsMsg
            {
                items = new[]
                {
                    new ThItemEntry { item = 0, state = (int)ItemState.Resting, owner = -1, gen = 2, value = 200, revealed = true, pos = new Vector3(1, 0, 6) },
                    new ThItemEntry { item = 1, state = (int)ItemState.Collected, gen = 2, gone = true, revealed = true },
                }
            }, 3);
            c.Tx.Sent.Clear();
            c.Table.Tick(10);
            Assert.AreEqual(0, c.Tx.Sent.Count);
            Assert.IsTrue(c.Table.Items[1].Gone);
            Assert.IsTrue(c.Table.IsGrabbable(c.Table.Items[0]));
        }

        [Test] public void 端末_結果に入るとすべてつかめなくなり何も送らない()
        {
            var c = new ClientRig(TreasureKind.S);
            c.Table.TryBeginGrab(0, 0);
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 2, reqId = 1, gen = 1, value = 200 }, 0.1);
            c.Tx.Sent.Clear();
            c.Table.Freeze();
            Assert.IsTrue(c.Has(ItemEventKind.Frozen));
            c.Table.OnHandReleased(0, 5);
            c.Table.Tick(10);
            Assert.AreEqual(0, c.Tx.Sent.Count);
            Assert.IsFalse(c.Table.TryBeginGrab(0, 6));
        }

        [Test] public void 端末_結果に入った後は落下中のお宝のThRestとThCollectも送らない()
        {
            var c = new ClientRig(TreasureKind.S, TreasureKind.S);
            foreach (var i in new[] { 0, 1 })
            {
                c.Table.TryBeginGrab(i, 0);
                c.Table.OnGrabbed(new ThGrabbedMsg { item = i, clientNo = 2, reqId = i + 1, gen = 1, value = 200 }, 0.1);
                c.Table.OnHandReleased(i, 1);   // Falling
            }
            c.Table.Freeze();
            c.Tx.Sent.Clear();
            c.Table.NoteSettled(0, new Vector3(1, 0, 6), Quaternion.identity, 2);
            c.Table.NoteInCollectZone(1, 2);
            c.Table.Tick(5);
            Assert.AreEqual(0, c.Tx.Sent.Count);
        }

        [Test] public void 端末_Mはビームの対象だがSの手順では持てず_参加していない端末はつかめない()
        {
            var c = new ClientRig(TreasureKind.M, TreasureKind.S);
            Assert.IsTrue(c.Table.IsGrabbable(c.Table.Items[0]), "M も #40 からビームの対象（予約は TryBeginLock）");
            Assert.IsFalse(c.Table.TryBeginGrab(0, 0), "M は S の手順では持てない");
            c.Table.ParticipantPlaying = false;
            Assert.IsFalse(c.Table.TryBeginGrab(1, 0));
        }

        [Test] public void 端末_ThPosesは受信時刻で並べて持ち主以外のお宝に入る()
        {
            var c = new ClientRig(TreasureKind.S);
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 3, reqId = 1, gen = 1, value = 200 }, 0);
            c.Table.OnPoses(new ThPosesMsg { poses = new[] { new ThPoseEntry { item = 0, pos = new Vector3(0, 0, 0) } } }, 1.0);
            c.Table.OnPoses(new ThPosesMsg { poses = new[] { new ThPoseEntry { item = 0, pos = new Vector3(1, 0, 0) } } }, 1.1);
            Assert.IsTrue(c.Table.Items[0].Interp.TrySample(1.15, 0.05f, 0.05f, out var p, out _));
            Assert.AreEqual(1f, p.x, 1e-3f);
        }

        // ---- ホスト＋端末をつなぐ ----

        const int HostNo = 1;
        double _now;
        FakeNetwork _net;
        SessionHostCore _host;
        readonly Dictionary<int, string> _resolve = new Dictionary<int, string>();
        readonly Dictionary<string, FakeNetwork.Node> _nodes = new Dictionary<string, FakeNetwork.Node>();
        readonly List<SessionClientCore> _clients = new List<SessionClientCore>();

        static ThParams ShortParams()
        {
            var p = new ThParams();
            p.operation.autoConfirmAlignment = true;   // 位置合わせは確認済み始まりで検証する（#44: 既定は未確認始まり）
            p.progress.openingSeconds = 3; p.progress.practiceSeconds = 5; p.progress.playingSeconds = 30; p.progress.resultSeconds = 3;
            p.grip.approvalWaitSeconds = 3;   // 結合テストで、ThItems による追いつきが待ち上限より先になる経路を固定するため
            return p;
        }

        void Build(string hostHash = "h1")
        {
            _now = 100;
            _net = new FakeNetwork();
            var node = _net.Add(HostNo);
            var sp = ThParamsLoader.Load(ShortParams(), null, null);
            _host = new SessionHostCore(node, sp, new ThGameSettings(), null, 0, no => _resolve.TryGetValue(no, out var v) ? v : null, MakeSetup(hostHash));
            _host.OwnClientNo = HostNo;
            node.OnReceive = (rpc, sender, args) => _host.OnRpc(rpc, sender, args, _now);
            _clients.Clear();
            _resolve.Clear();
            _nodes.Clear();
        }

        SessionClientCore AddClient(int no, string id, string hash = "h1")
        {
            var node = _net.Add(no);
            var c = new SessionClientCore(id, node, new ThParams(), null, _now, MakeSetup(hash)) { OwnClientNo = no };
            node.OnReceive = (rpc, sender, args) => c.OnRpc(rpc, sender, args, _now);
            _clients.Add(c); _resolve[no] = id; _nodes[id] = node;
            c.OnReady(_now);
            _net.Pump();
            Advance(1.0);
            return c;
        }

        void Advance(double seconds)
        {
            int steps = (int)Math.Round(seconds / 0.1);
            for (int i = 0; i < steps; i++)
            {
                _now += 0.1;
                _host.Tick(_now); _net.Pump();
                foreach (var c in _clients) c.Tick(_now);
                _net.Pump();
            }
        }

        void StartPlaying()
        {
            Assert.IsTrue(_host.Start(_now, 5000, 4242));
            _net.Pump();
            Advance(3.3);   // Opening → Tutorial
            Advance(5.3);   // Tutorial → Playing
            Assert.AreEqual(RoundState.Playing, _host.Machine.State);
            _net.Pump();
        }

        [TearDown] public void TearDown() { _host?.Dispose(); foreach (var c in _clients) c.Dispose(); }

        int FirstS(ClientItemTable t, int skip = 0) => t.Items.Where(i => i.Kind == TreasureKind.S).Skip(skip).First().Index;

        [Test] public void 結合_開始から回収まで通して全員の合計額が同じになる()
        {
            Build();
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            StartPlaying();

            // 両端末とホストで配置が同じ
            Assert.IsNotNull(a.Items.CurrentValue); Assert.IsNotNull(b.Items.CurrentValue);
            Assert.IsNotNull(_host.Items);
            Assert.AreEqual(_host.SpawnFingerprint, a.SpawnFingerprint);
            Assert.AreEqual(_host.SpawnFingerprint, b.SpawnFingerprint);
            Assert.AreEqual(a.Items.CurrentValue.Items.Count, _host.Items.Items.Count);
            Assert.AreEqual(15, _host.Items.Items.Count);   // 2人: S10 + M5

            int idx = FirstS(a.Items.CurrentValue);
            var ai = a.Items.CurrentValue; var bi = b.Items.CurrentValue;
            int value = ai.Get(idx).BaseValue;

            // A がつかむ
            Assert.IsTrue(ai.TryBeginGrab(idx, _now));
            _net.Pump();
            Assert.IsTrue(ai.Get(idx).Approved, "A に承認が届く");
            Assert.IsTrue(bi.Get(idx).Revealed, "B からも値段が見える");
            Assert.AreEqual(value, bi.Get(idx).Value);
            // B から見てつかめない
            Assert.IsFalse(bi.IsGrabbable(bi.Get(idx)));
            Assert.IsFalse(bi.TryBeginGrab(idx, _now));

            // A が置く（床に落ちて止まる）→ B がつかめる
            ai.OnHandReleased(idx, _now);
            _net.Pump();
            Assert.AreEqual(ItemState.Falling, _host.Items.Get(idx).State);
            ai.NoteSettled(idx, new Vector3(0.5f, 0, 6f), Quaternion.identity, _now);
            _net.Pump();
            Assert.AreEqual(ItemState.Resting, _host.Items.Get(idx).State);
            Assert.IsTrue(bi.IsGrabbable(bi.Get(idx)), "B がつかめる");
            Assert.AreEqual(new Vector3(0.5f, 0, 6f), bi.Get(idx).RestPos);

            // B がつかんで荷台で手を開く → 回収
            Assert.IsTrue(bi.TryBeginGrab(idx, _now));
            _net.Pump();
            Assert.IsTrue(bi.Get(idx).Approved);
            bi.OnHandReleased(idx, _now);
            _net.Pump();
            bi.NoteInCollectZone(idx, _now);
            _net.Pump();
            Assert.IsTrue(_host.Items.Get(idx).Gone);
            Assert.AreEqual(value, _host.TotalAmount);
            Assert.AreEqual(value, a.Total.CurrentValue);
            Assert.AreEqual(value, b.Total.CurrentValue);
            Assert.IsTrue(ai.Get(idx).Gone); Assert.IsTrue(bi.Get(idx).Gone);
        }

        [Test] public void 結合_Heartbeatの合計額も端末に届く()
        {
            Build();
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            StartPlaying();
            int idx = FirstS(a.Items.CurrentValue);
            a.Items.CurrentValue.TryBeginGrab(idx, _now); _net.Pump();
            a.Items.CurrentValue.OnHandReleased(idx, _now); _net.Pump();
            a.Items.CurrentValue.NoteInCollectZone(idx, _now); _net.Pump();
            int total = _host.TotalAmount;
            Assert.Greater(total, 0);
            // ThCollected を捨てても Heartbeat（と ThItems）で追いつく
            _net.Drop = (s, r, args, to) => r == Rpc.ThCollected;
            int idx2 = FirstS(a.Items.CurrentValue, 1);
            a.Items.CurrentValue.TryBeginGrab(idx2, _now); _net.Pump();
            a.Items.CurrentValue.OnHandReleased(idx2, _now); _net.Pump();
            a.Items.CurrentValue.NoteInCollectZone(idx2, _now); _net.Pump();
            int total2 = _host.TotalAmount;
            Assert.Greater(total2, total);
            Advance(1.2);
            Assert.AreEqual(total2, b.Total.CurrentValue);
            Assert.IsTrue(b.Items.CurrentValue.Get(idx2).Gone, "ThItems で消えたことに追いつく");
        }

        [Test] public void 結合_ThGrabbedとThFreedを捨ててもThItemsで状態がホストと一致する()
        {
            Build();
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            StartPlaying();
            int idx = FirstS(a.Items.CurrentValue);
            var ai = a.Items.CurrentValue; var bi = b.Items.CurrentValue;

            _net.Drop = (s, r, args, to) => (r == Rpc.ThGrabbed || r == Rpc.ThFreed) && to == 3;
            ai.TryBeginGrab(idx, _now); _net.Pump();
            Assert.IsTrue(_host.Items.Get(idx).OwnerClientNo == 2);
            Assert.IsTrue(bi.IsGrabbable(bi.Get(idx)), "B は ThGrabbed を取りこぼして、まだつかめると思っている");
            Advance(1.2);   // 次の ThItems
            Assert.IsFalse(bi.IsGrabbable(bi.Get(idx)));
            Assert.AreEqual(2, bi.Get(idx).OwnerClientNo);
            Assert.IsTrue(bi.Get(idx).Revealed);

            ai.OnHandReleased(idx, _now); _net.Pump();
            ai.NoteSettled(idx, new Vector3(1f, 0, 7f), Quaternion.identity, _now); _net.Pump();
            Assert.IsFalse(bi.IsGrabbable(bi.Get(idx)), "ThFreed を取りこぼした");
            Advance(1.2);
            Assert.IsTrue(bi.IsGrabbable(bi.Get(idx)));
            Assert.AreEqual(new Vector3(1f, 0, 7f), bi.Get(idx).RestPos);
        }

        [Test] public void 結合_ThGrabbedを取りこぼした本人もThItemsで承認に追いつく()
        {
            Build();
            var a = AddClient(2, "devA");
            AddClient(3, "devB");
            StartPlaying();
            var ai = a.Items.CurrentValue;
            int idx = FirstS(ai);
            _net.Drop = (s, r, args, to) => r == Rpc.ThGrabbed && to == 2;   // ThItems は届く。待ち上限は 3 秒なので、先に追いつく
            Assert.IsTrue(ai.TryBeginGrab(idx, _now));
            _net.Pump();
            Assert.IsFalse(ai.Get(idx).Approved);
            Advance(1.6);   // ThItems（1 秒ごと）
            Assert.IsTrue(ai.Get(idx).Approved, "ThItems で承認に追いつく");
            Assert.AreEqual(2, _host.Items.Get(idx).OwnerClientNo);
            Assert.AreEqual(ItemState.Held, ai.Get(idx).State);
        }

        [Test] public void 結合_待ち上限で取りやめて握り直すと新しい申請が承認される()
        {
            Build();
            var a = AddClient(2, "devA");
            AddClient(3, "devB");
            StartPlaying();
            var ai = a.Items.CurrentValue;
            int idx = FirstS(ai);
            // 承認も状態表も届かない → 待ち上限（3 秒）で取りやめる
            _net.Drop = (s, r, args, to) => (r == Rpc.ThGrabbed || r == Rpc.ThItems) && to == 2;
            Assert.IsTrue(ai.TryBeginGrab(idx, _now));
            Advance(3.3);
            Assert.IsFalse(ai.Get(idx).ReqPending, "待ち上限で取りやめた");
            Assert.AreEqual(-1, _host.Items.Get(idx).OwnerClientNo, "ホストも取りやめを受理して解放した");
            _net.Drop = null;
            Advance(0.3);   // ThFreed（世代 2）が届く
            Assert.IsTrue(ai.IsGrabbable(ai.Get(idx)));
            // 握り直す: 承認が届いて Held になる
            Assert.IsTrue(ai.TryBeginGrab(idx, _now));
            _net.Pump();
            Assert.IsTrue(ai.Get(idx).Approved);
            Assert.AreEqual(ItemState.Held, ai.Get(idx).State);
            Assert.AreEqual(2, _host.Items.Get(idx).OwnerClientNo);
        }

        [Test] public void 結合_待機を取りこぼして別の回に入ったら前の回の表を消す()
        {
            Build();
            var a = AddClient(2, "devA");
            StartPlaying();
            Assert.IsNotNull(a.Items.CurrentValue);
            _net.Drop = (s, r, args, to) => r == Rpc.ThState && to == 2 && args[1].Contains("\"state\":0");   // 待機の ThState を捨てる
            Assert.IsTrue(_host.Abort(_now));
            _net.Pump();
            _now += 0.2;
            Assert.IsTrue(_host.Start(_now + 0.0, 9000, 77));
            _net.Pump();
            Advance(0.3);
            Assert.AreEqual(RoundState.Opening, a.Tracker.State.CurrentValue);
            Assert.IsNull(a.Items.CurrentValue, "Playing 以外でも前の回の表を消す");
        }

        [Test] public void 結合_参加端末でない送り手の申請は拒否される()
        {
            Build();
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            StartPlaying();
            _host.Registry.SetSpectator("devB", true);
            int idx = FirstS(b.Items.CurrentValue);
            b.Items.CurrentValue.ParticipantPlaying = true;   // 端末側の自己判断を通して、ホストの拒否を確かめる
            b.Items.CurrentValue.TryBeginGrab(idx, _now);
            _net.Pump();
            Assert.AreEqual(-1, _host.Items.Get(idx).OwnerClientNo);
            Assert.AreEqual(ItemState.Resting, _host.Items.Get(idx).State);
        }

        [Test] public void 結合_Playing以外のThCollectは数えない_結果に入ったら以後のメッセージを受け付けない()
        {
            Build();
            var a = AddClient(2, "devA");
            AddClient(3, "devB");
            StartPlaying();
            var ai = a.Items.CurrentValue;
            int i1 = FirstS(ai), i2 = FirstS(ai, 1);
            ai.TryBeginGrab(i1, _now); _net.Pump();
            ai.TryBeginGrab(i2, _now); _net.Pump();
            ai.OnHandReleased(i2, _now); _net.Pump();
            Advance(30.5);   // 本編が終わって結果へ
            Assert.AreEqual(RoundState.Result, _host.Machine.State);
            Assert.IsTrue(ai.Frozen, "全端末で止まる");
            // 結果に入ってからの ThCollect はホストが受け付けない（端末側の表は止まっているので、メッセージを直接送る）
            _nodes["devA"].Send(Rpc.ThCollect, ThMessages.Encode(_host.Machine.RoundId, new ThCollectMsg { item = i2, gen = 1 }));
            _net.Pump();
            Assert.AreEqual(0, _host.TotalAmount);
            Assert.IsFalse(_host.Items.Get(i2).Gone);
        }

        [Test] public void 結合_待機に戻ったらお宝の表を全端末とホストで消す()
        {
            Build();
            var a = AddClient(2, "devA");
            StartPlaying();
            Assert.IsNotNull(a.Items.CurrentValue);
            Advance(30.5); Advance(3.5);
            Assert.AreEqual(RoundState.Waiting, _host.Machine.State);
            Assert.IsNull(a.Items.CurrentValue);
            Assert.IsNull(_host.Items);
            Assert.AreEqual(0, a.Total.CurrentValue);
        }

        [Test] public void 結合_カタログのハッシュが違う端末は参加端末から外れ警告が出る()
        {
            Build("h1");
            var a = AddClient(2, "devA", "h1");
            var b = AddClient(3, "devB", "different");
            Assert.IsTrue(_host.Registry.IsEligible("devA"));
            Assert.IsFalse(_host.Registry.IsEligible("devB"));
            Assert.IsTrue(_host.Warnings.CurrentValue.Any(w => w.Contains("devB") && w.Contains("カタログ")));
            Assert.IsTrue(_host.Start(_now, 5000, 1));
            Advance(3.3);
            Assert.AreEqual(RoundState.Tutorial, _host.Machine.State);
            CollectionAssert.AreEqual(new[] { "devA" }, _host.Machine.Participants);
        }

        [Test] public void 結合_回の途中から入った端末はその回のお宝を表示しない()
        {
            Build();
            var a = AddClient(2, "devA");
            StartPlaying();
            var late = AddClient(4, "devLate");
            Advance(1.5);
            Assert.IsTrue(late.Tracker.WaitNextRound);
            Assert.IsNull(late.Items.CurrentValue);
            Assert.IsNotNull(a.Items.CurrentValue);
        }

        [Test] public void 結合_参加していない端末は表示するが持てない()
        {
            Build();
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            _host.Registry.SetSpectator("devB", true);
            StartPlaying();
            Assert.IsNotNull(b.Items.CurrentValue, "見学の端末にも表示する");
            Assert.IsFalse(b.Tracker.IsParticipant.CurrentValue);
            Assert.IsFalse(b.Items.CurrentValue.TryBeginGrab(FirstS(b.Items.CurrentValue), _now));
        }

        // ---- 減額（#39）: ホスト1＋端末2をつなぐ ----

        [Test] public void 結合_障害物に当たると全員の価値と回数が同じになり_3回で消滅して全員から消える()
        {
            Build();
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            StartPlaying();
            var ai = a.Items.CurrentValue; var bi = b.Items.CurrentValue;
            int idx = FirstS(ai);
            int baseValue = ai.Get(idx).BaseValue;
            Assert.IsTrue(ai.TryBeginGrab(idx, _now)); _net.Pump();
            Assert.IsTrue(ai.Get(idx).Approved);
            var events = new List<ItemEvent>();
            bi.Events.Subscribe(e => events.Add(e));

            Assert.IsTrue(ai.TryReportDamage(idx, -1, Vector3.zero, 1.5f)); _net.Pump();
            int v1 = baseValue - baseValue * 30 / 100;
            foreach (var t in new[] { ai, bi }) { Assert.AreEqual(1, t.Get(idx).Hits); Assert.AreEqual(v1, t.Get(idx).Value); }
            Assert.AreEqual(v1, _host.Items.Get(idx).Value);
            Assert.IsTrue(events.Any(e => e.Kind == ItemEventKind.Damaged));
            Assert.IsTrue(ai.DamageNoteShown, "自分が動かしているお宝の初めての一文");
            Assert.IsFalse(bi.DamageNoteShown);

            Advance(0.5);
            Assert.IsTrue(ai.TryReportDamage(idx, -1, Vector3.zero, 1.5f)); _net.Pump();
            Advance(0.5);
            Assert.IsTrue(ai.TryReportDamage(idx, -1, Vector3.zero, 1.5f)); _net.Pump();
            foreach (var t in new[] { ai, bi }) { Assert.IsTrue(t.Get(idx).Gone); Assert.AreEqual(ItemState.Destroyed, t.Get(idx).State); Assert.AreEqual(3, t.Get(idx).Hits); }
            Assert.IsFalse(ai.Get(idx).Mine);
            Assert.IsTrue(events.Any(e => e.Kind == ItemEventKind.Destroyed));
            Assert.AreEqual(baseValue, _host.Items.DamageTotal);
            Assert.AreEqual(0, _host.TotalAmount);
            Assert.IsFalse(bi.IsGrabbable(bi.Get(idx)));
        }

        [Test] public void 結合_お宝同士の衝突を両方の端末が報告しても1回だけ数える()
        {
            Build();
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            StartPlaying();
            var ai = a.Items.CurrentValue; var bi = b.Items.CurrentValue;
            int x = FirstS(ai), y = FirstS(ai, 1);
            Assert.IsTrue(ai.TryBeginGrab(x, _now)); Assert.IsTrue(bi.TryBeginGrab(y, _now)); _net.Pump();
            Assert.IsTrue(ai.Get(x).Approved); Assert.IsTrue(bi.Get(y).Approved);
            Assert.IsTrue(ai.TryReportDamage(x, y, Vector3.zero, 2f));
            Assert.IsTrue(bi.TryReportDamage(y, x, Vector3.zero, 2f));
            _net.Pump();
            foreach (var t in new[] { ai, bi, })
            {
                Assert.AreEqual(1, t.Get(x).Hits, "x は 1 回だけ");
                Assert.AreEqual(1, t.Get(y).Hits, "y は 1 回だけ");
            }
            Assert.AreEqual(2, _net.Log.Count(l => l.rpc == Rpc.ThDamaged), "ThDamaged は 2 つのお宝に 1 つずつ");
        }

        [Test] public void 結合_ThDamagedを捨ててもThItemsで回数と消滅が追いつく()
        {
            Build();
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            StartPlaying();
            var ai = a.Items.CurrentValue; var bi = b.Items.CurrentValue;
            int idx = FirstS(ai);
            ai.TryBeginGrab(idx, _now); _net.Pump();
            _net.Drop = (s, r, args, to) => r == Rpc.ThDamaged && to == 3;
            ai.TryReportDamage(idx, -1, Vector3.zero, 2f); _net.Pump();
            Assert.AreEqual(0, bi.Get(idx).Hits, "B は取りこぼした");
            Advance(1.2);   // 次の ThItems
            Assert.AreEqual(1, bi.Get(idx).Hits);
            Assert.AreEqual(_host.Items.Get(idx).Value, bi.Get(idx).Value);
            Advance(0.5); ai.TryReportDamage(idx, -1, Vector3.zero, 2f); _net.Pump();
            Advance(0.5); ai.TryReportDamage(idx, -1, Vector3.zero, 2f); _net.Pump();
            Assert.IsFalse(bi.Get(idx).Gone);
            Advance(1.2);
            Assert.IsTrue(bi.Get(idx).Gone);
            Assert.AreEqual(ItemState.Destroyed, bi.Get(idx).State);
        }
    }
}
