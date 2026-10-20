using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    /// <summary>M（両手で持つ。#40）: ホストの予約・両手成立・解除、端末の手順・猶予・追いつき、両手の目標、「両手で持とう」、ホスト＋端末のつなぎ合わせ</summary>
    public class ThTwoHandTests
    {
        static readonly Bounds Zone = new Bounds(new Vector3(0, 0.9f, 0.8f), new Vector3(3f, 1.2f, 1f));

        static List<SpawnedTreasure> MakeSpawned(params TreasureKind[] kinds)
        {
            var list = new List<SpawnedTreasure>();
            for (int i = 0; i < kinds.Length; i++)
                list.Add(new SpawnedTreasure { Index = i, Kind = kinds[i], Value = 400 + i * 100, LocalPosition = new Vector3(i, 0, 5), CatalogIndex = 0 });
            return list;
        }

        static ThTreasureSetup MakeSetup()
        {
            var pts = new List<PlacementPoint>();
            for (int i = 0; i < 30; i++) pts.Add(new PlacementPoint(new Vector3((i % 5) * 0.75f - 1.5f, 0, 3f + i / 5), i % 3));
            return new ThTreasureSetup
            {
                Points = pts,
                Catalog = new[] { new SpawnCatalogEntry(TreasureKind.S, 0), new SpawnCatalogEntry(TreasureKind.S, 1), new SpawnCatalogEntry(TreasureKind.M, 2) },
                CollectBounds = Zone,
                CatalogHash = "h1",
            };
        }

        const TreasureKind S = TreasureKind.S, M = TreasureKind.M;

        // ---- ホスト ----

        sealed class HostRig
        {
            public readonly List<(Rpc rpc, object dto)> Out = new List<(Rpc, object)>();
            public readonly HostItemTable Table;
            public HostRig(params TreasureKind[] kinds) { Table = new HostItemTable(MakeSpawned(kinds), Zone, (r, d) => Out.Add((r, d))); }
            public List<T> Of<T>(Rpc rpc) => Out.Where(o => o.rpc == rpc).Select(o => (T)o.dto).ToList();
            public HostItem Item(int i) => Table.Items[i];
        }

        static ThGrabMsg GrabM(int item, int req) => new ThGrabMsg { item = item, reqId = req, kind = (int)M };
        static ThGrabMsg GrabS(int item, int req) => new ThGrabMsg { item = item, reqId = req, kind = (int)S };
        static ThLiftMsg Lift(int item, int req, int gen) => new ThLiftMsg { item = item, reqId = req, gen = gen };
        static ThReleaseMsg Rel(int item, int req, int gen, ReleaseReason r) => new ThReleaseMsg { item = item, reqId = req, gen = gen, reason = (int)r };

        [Test] public void ホスト_Mの予約が認められLockedで世代が進み値段は公開しない()
        {
            var h = new HostRig(M);
            h.Table.OnGrab("A", 2, GrabM(0, 1));
            var g = h.Of<ThGrabbedMsg>(Rpc.ThGrabbed).Single();
            Assert.IsTrue(g.reserve); Assert.AreEqual(0, g.value); Assert.AreEqual(2, g.clientNo); Assert.AreEqual(1, g.reqId); Assert.AreEqual(1, g.gen);
            Assert.AreEqual(ItemState.Locked, h.Item(0).State);
            Assert.IsFalse(h.Item(0).Revealed);
            Assert.AreEqual("A", h.Item(0).OwnerDevice);
            Assert.AreEqual("A", h.Item(0).ControllerDevice);
            Assert.IsFalse(h.Item(0).ExemptReported);
        }

        [Test] public void ホスト_種類が合う申請だけ受け付ける()
        {
            var h = new HostRig(S, M);
            h.Table.OnGrab("A", 2, GrabS(1, 1));   // M のお宝に S の申請
            h.Table.OnGrab("A", 2, GrabM(0, 2));   // S のお宝に M の申請
            Assert.AreEqual(0, h.Of<ThGrabbedMsg>(Rpc.ThGrabbed).Count);
            Assert.AreEqual(2, h.Of<ThGrabDeniedMsg>(Rpc.ThGrabDenied).Count);
            h.Table.OnGrab("A", 2, GrabS(0, 3));
            Assert.IsFalse(h.Of<ThGrabbedMsg>(Rpc.ThGrabbed).Single().reserve);
            Assert.IsTrue(h.Item(0).Revealed, "S は今までどおり値段を公開");
        }

        [Test] public void ホスト_L_秘宝は否認する()
        {
            var h = new HostRig(TreasureKind.L, TreasureKind.Secret);
            h.Table.OnGrab("A", 2, GrabM(0, 1));
            h.Table.OnGrab("A", 2, GrabS(1, 2));
            Assert.AreEqual(2, h.Of<ThGrabDeniedMsg>(Rpc.ThGrabDenied).Count);
        }

        [Test] public void ホスト_予約中は他の人のThGrabを否認し同じ申請の再送は承認を送り直す()
        {
            var h = new HostRig(M);
            h.Table.OnGrab("A", 2, GrabM(0, 1));
            h.Table.OnGrab("B", 3, GrabM(0, 1));
            Assert.AreEqual(3, h.Of<ThGrabDeniedMsg>(Rpc.ThGrabDenied).Single().clientNo);
            h.Table.OnGrab("A", 2, GrabM(0, 1));
            var gs = h.Of<ThGrabbedMsg>(Rpc.ThGrabbed);
            Assert.AreEqual(2, gs.Count);
            Assert.IsTrue(gs[1].reserve); Assert.AreEqual(gs[0].gen, gs[1].gen); Assert.AreEqual(0, gs[1].value);
        }

        [Test] public void ホスト_ThLiftの一致でHeldになり値段を公開し世代は変えない()
        {
            var h = new HostRig(M);
            h.Table.OnGrab("A", 2, GrabM(0, 1));
            h.Table.OnLift("A", 2, Lift(0, 1, 1));
            var l = h.Of<ThLiftedMsg>(Rpc.ThLifted).Single();
            Assert.AreEqual((2, 1, 1, 400), (l.clientNo, l.reqId, l.gen, l.value));
            Assert.AreEqual(ItemState.Held, h.Item(0).State);
            Assert.IsTrue(h.Item(0).Revealed);
            Assert.AreEqual(1, h.Item(0).Gen);
        }

        [Test] public void ホスト_持ち主_申請ID_世代のどれかが違うThLiftは否認する()
        {
            var h = new HostRig(M);
            h.Table.OnGrab("A", 2, GrabM(0, 1));
            h.Table.OnLift("B", 3, Lift(0, 1, 1));   // 持ち主でない
            h.Table.OnLift("A", 2, Lift(0, 9, 1));   // 申請ID
            h.Table.OnLift("A", 2, Lift(0, 1, 0));   // 世代
            Assert.AreEqual(3, h.Of<ThGrabDeniedMsg>(Rpc.ThGrabDenied).Count);
            Assert.AreEqual(0, h.Of<ThLiftedMsg>(Rpc.ThLifted).Count);
            Assert.AreEqual(ItemState.Locked, h.Item(0).State);
            Assert.IsFalse(h.Item(0).Revealed);
            // 予約していないお宝・S への ThLift
            var h2 = new HostRig(S, M);
            h2.Table.OnLift("A", 2, Lift(1, 1, 0));
            h2.Table.OnGrab("A", 2, GrabS(0, 2));
            h2.Table.OnLift("A", 2, Lift(0, 2, 1));
            Assert.AreEqual(2, h2.Of<ThGrabDeniedMsg>(Rpc.ThGrabDenied).Count);
        }

        [Test] public void ホスト_ThLiftの再送はThLiftedを送り直す()
        {
            var h = new HostRig(M);
            h.Table.OnGrab("A", 2, GrabM(0, 1));
            h.Table.OnLift("A", 2, Lift(0, 1, 1));
            h.Table.OnLift("A", 2, Lift(0, 1, 1));
            Assert.AreEqual(2, h.Of<ThLiftedMsg>(Rpc.ThLifted).Count);
            Assert.AreEqual(0, h.Of<ThGrabDeniedMsg>(Rpc.ThGrabDenied).Count);
            Assert.AreEqual(1, h.Item(0).Gen);
        }

        [Test] public void ホスト_Unreserveでつかめるに戻り世代が進みThFreedを送る()
        {
            var h = new HostRig(M);
            h.Table.OnGrab("A", 2, GrabM(0, 1));
            h.Table.OnRelease("A", Rel(0, 1, 1, ReleaseReason.Unreserve));
            var f = h.Of<ThFreedMsg>(Rpc.ThFreed).Single();
            Assert.AreEqual(2, f.gen);
            Assert.AreEqual(ItemState.Resting, h.Item(0).State);
            Assert.AreEqual(-1, h.Item(0).OwnerClientNo);
            Assert.IsFalse(h.Item(0).Revealed);
            h.Table.OnGrab("B", 3, GrabM(0, 1));
            Assert.AreEqual(3, h.Of<ThGrabbedMsg>(Rpc.ThGrabbed).Last().clientNo);
        }

        [Test] public void ホスト_持ち主でない_世代違い_Lockedでないを含むUnreserveは捨てる()
        {
            var h = new HostRig(M);
            h.Table.OnGrab("A", 2, GrabM(0, 1));
            h.Table.OnRelease("B", Rel(0, 1, 1, ReleaseReason.Unreserve));
            h.Table.OnRelease("A", Rel(0, 1, 0, ReleaseReason.Unreserve));
            h.Table.OnRelease("A", Rel(0, 5, 1, ReleaseReason.Unreserve));
            Assert.AreEqual(ItemState.Locked, h.Item(0).State);
            Assert.AreEqual(0, h.Of<ThFreedMsg>(Rpc.ThFreed).Count);
            h.Table.OnLift("A", 2, Lift(0, 1, 1));
            h.Table.OnRelease("A", Rel(0, 1, 1, ReleaseReason.Unreserve));   // もう Held
            Assert.AreEqual(ItemState.Held, h.Item(0).State);
        }

        [Test] public void ホスト_両手で持った後の落としは世代を変えずFallingにする_予約中のDroppedは捨てる()
        {
            var h = new HostRig(M);
            h.Table.OnGrab("A", 2, GrabM(0, 1));
            h.Table.OnRelease("A", Rel(0, 1, 1, ReleaseReason.Dropped));
            Assert.AreEqual(ItemState.Locked, h.Item(0).State);
            h.Table.OnLift("A", 2, Lift(0, 1, 1));
            h.Table.OnRelease("A", Rel(0, 1, 1, ReleaseReason.Dropped));
            Assert.AreEqual(ItemState.Falling, h.Item(0).State);
            Assert.AreEqual(1, h.Item(0).Gen);
        }

        [Test] public void ホスト_予約中と持ち上げ申請中のCancelは持ち主なら戻す()
        {
            var h = new HostRig(M, M);
            h.Table.OnGrab("A", 2, GrabM(0, 1));
            h.Table.OnRelease("A", Rel(0, 1, 1, ReleaseReason.Cancel));
            Assert.AreEqual(ItemState.Resting, h.Item(0).State);
            h.Table.OnGrab("A", 2, GrabM(1, 2));
            h.Table.OnLift("A", 2, Lift(1, 2, 1));
            h.Table.OnRelease("A", Rel(1, 2, 1, ReleaseReason.Cancel));
            Assert.AreEqual(ItemState.Resting, h.Item(1).State);
            Assert.AreEqual(2, h.Of<ThFreedMsg>(Rpc.ThFreed).Count);
        }

        [Test] public void ホスト_ReleaseReservationsOfは予約だけ外し持っているお宝は変えない()
        {
            var h = new HostRig(M, M, S);
            h.Table.OnGrab("A", 2, GrabM(0, 1));          // 予約
            h.Table.OnGrab("A", 2, GrabM(1, 2));
            h.Table.OnLift("A", 2, Lift(1, 2, 1));        // 持っている
            h.Table.OnGrab("A", 2, GrabS(2, 3));          // S を持っている
            h.Table.ReleaseReservationsOf("A");
            Assert.AreEqual(ItemState.Resting, h.Item(0).State);
            Assert.AreEqual(ItemState.Held, h.Item(1).State);
            Assert.AreEqual(ItemState.Held, h.Item(2).State);
            Assert.AreEqual(1, h.Of<ThFreedMsg>(Rpc.ThFreed).Count);
            h.Table.ReleaseReservationsOf("B");
            h.Table.ReleaseReservationsOf(null);
            Assert.AreEqual(1, h.Of<ThFreedMsg>(Rpc.ThFreed).Count);
        }

        [Test] public void ホスト_Lockedのお宝はぶつけられると減額される_値段は出さない()
        {
            var h = new HostRig(M, S);
            h.Table.OnGrab("A", 2, GrabM(0, 1));    // M を予約（値段は非公開）
            h.Table.OnGrab("B", 3, GrabS(1, 1));    // B が S を持つ
            Assert.IsFalse(h.Item(0).IsDamageExempt);
            h.Table.OnDamage("B", new ThDamageMsg { a = 1, b = 0, speed = 2f }, 10);
            var d = h.Of<ThDamagedMsg>(Rpc.ThDamaged);
            var m = d.Single(x => x.item == 0);
            Assert.AreEqual(1, m.hits); Assert.IsFalse(m.revealed);
            Assert.AreEqual(1, h.Item(0).Hits);
        }

        [Test] public void ホスト_状態表にLockedの持ち主と申請IDが載り値段は未公開のまま()
        {
            var h = new HostRig(M);
            h.Table.OnGrab("A", 2, GrabM(0, 1));
            var e = h.Table.BuildItems().items[0];
            Assert.AreEqual((int)ItemState.Locked, e.state); Assert.AreEqual(2, e.owner); Assert.AreEqual(1, e.reqId); Assert.IsFalse(e.revealed);
        }

        // ---- 端末 ----

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
            public ClientItem It(int i = 0) => Table.Items[i];
            public bool Has(ItemEventKind k) => Events.Any(e => e.Kind == k);
            public int Count(ItemEventKind k) => Events.Count(e => e.Kind == k);
            public List<ThReleaseMsg> Releases(ReleaseReason r) => Tx.Of<ThReleaseMsg>(Rpc.ThRelease).Where(x => x.reason == (int)r).ToList();

            /// <summary>左手・つかむ範囲0 で予約して承認まで進める</summary>
            public void Reserve(double t = 0, int gen = 1)
            {
                Assert.IsTrue(Table.TryBeginLock(0, ThHand.Left, 0, t));
                Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = Own, reqId = It().MyReqId, gen = gen, value = 0, reserve = true }, t + 0.05);
            }
            /// <summary>右手・つかむ範囲1 で両手成立まで進める（ThLifted まで）</summary>
            public void Lift(double t = 0.2, int value = 700)
            {
                Assert.IsTrue(Table.TryBeginLift(0, ThHand.Right, 1, t));
                Table.OnLifted(new ThLiftedMsg { item = 0, clientNo = Own, reqId = It().MyReqId, gen = It().Gen, value = value }, t + 0.05);
            }
        }

        [Test] public void 端末_予約の申請ではお宝を動かさず値段も出さない()
        {
            var c = new ClientRig(M);
            Assert.IsTrue(c.Table.TryBeginLock(0, ThHand.Left, 0, 0));
            var g = c.Tx.Of<ThGrabMsg>(Rpc.ThGrab).Single();
            Assert.AreEqual((int)M, g.kind);
            Assert.AreEqual(ItemState.Locked, c.It().State);
            Assert.IsTrue(c.It().Mine); Assert.IsTrue(c.It().ReqPending);
            Assert.IsTrue(c.Has(ItemEventKind.LockStarted));
            Assert.IsFalse(c.Has(ItemEventKind.GrabStarted)); Assert.IsFalse(c.Has(ItemEventKind.LiftStarted)); Assert.IsFalse(c.Has(ItemEventKind.Revealed));
            Assert.IsFalse(c.It().Revealed);
        }

        [Test] public void 端末_Sには予約を使えずMにはTryBeginGrabを使えない扱いにならない()
        {
            var c = new ClientRig(S, M);
            Assert.IsFalse(c.Table.TryBeginLock(0, ThHand.Left, 0, 0), "S は予約できない");
            Assert.IsTrue(c.Table.TryBeginGrab(0, 0), "S の流れは変わらない");
            c.Table.ParticipantPlaying = false;
            Assert.IsFalse(c.Table.TryBeginLock(1, ThHand.Left, 0, 0));
        }

        [Test] public void 端末_予約の承認でReservedになり値段公開も発見演出もしない()
        {
            var c = new ClientRig(M);
            c.It().Value = 900;
            c.Reserve();
            Assert.IsTrue(c.It().Reserved); Assert.IsFalse(c.It().ReqPending); Assert.IsFalse(c.It().Approved);
            Assert.AreEqual(2, c.It().OwnerClientNo);
            Assert.AreEqual(ItemState.Locked, c.It().State);
            Assert.IsFalse(c.It().Revealed);
            Assert.IsFalse(c.Has(ItemEventKind.Revealed)); Assert.IsFalse(c.Has(ItemEventKind.Discovered)); Assert.IsFalse(c.Has(ItemEventKind.Approved));
            // 重複は無視
            c.Events.Clear();
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 2, reqId = 1, gen = 1, reserve = true }, 0.2);
            Assert.AreEqual(0, c.Events.Count);
        }

        [Test] public void 端末_他の人の予約ではLockedで値段は公開せずつかめない()
        {
            var c = new ClientRig(M);
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 3, reqId = 1, gen = 1, value = 0, reserve = true }, 0);
            Assert.AreEqual(ItemState.Locked, c.It().State);
            Assert.AreEqual(3, c.It().OwnerClientNo);
            Assert.IsFalse(c.It().Revealed);
            Assert.IsFalse(c.Table.IsGrabbable(c.It()));
            Assert.IsFalse(c.Table.TryBeginLock(0, ThHand.Left, 0, 1));
            Assert.IsFalse(c.Table.IsTargetFor(c.It(), ThHand.Right, 1), "他の人の予約には 2 本目も使えない");
            Assert.AreEqual(Vector3.zero + new Vector3(0, 0, 5), c.It().RestPos, "置いた位置のまま");
        }

        [Test] public void 端末_他の人の予約より先に自分が申請していたら戻る()
        {
            var c = new ClientRig(M);
            c.Table.TryBeginLock(0, ThHand.Left, 0, 0);
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 3, reqId = 1, gen = 1, reserve = true }, 0.1);
            Assert.IsTrue(c.Has(ItemEventKind.Reverted));
            Assert.AreEqual(ItemState.Locked, c.It().State);
            Assert.IsFalse(c.It().Mine);
            Assert.AreEqual((int)ReleaseReason.Cancel, c.Tx.Of<ThReleaseMsg>(Rpc.ThRelease).Single().reason);
        }

        [Test] public void 端末_2本目の対象は予約していない方の手とつかむ範囲だけ()
        {
            var c = new ClientRig(M);
            Assert.IsTrue(c.Table.IsTargetFor(c.It(), ThHand.Left, 0), "予約前は S と同じく対象");
            c.Table.TryBeginLock(0, ThHand.Left, 0, 0);   // 承認待ちでも対象は同じ
            Assert.IsFalse(c.Table.IsTargetFor(c.It(), ThHand.Left, 0), "同じ手・同じつかむ範囲");
            Assert.IsFalse(c.Table.IsTargetFor(c.It(), ThHand.Left, 1), "同じ手");
            Assert.IsFalse(c.Table.IsTargetFor(c.It(), ThHand.Right, 0), "同じつかむ範囲");
            Assert.IsTrue(c.Table.IsTargetFor(c.It(), ThHand.Right, 1));
            Assert.IsFalse(c.Table.TryBeginLift(0, ThHand.Left, 1, 0.1));
            Assert.IsFalse(c.Table.TryBeginLift(0, ThHand.Right, 0, 0.1));
            c.Table.ParticipantPlaying = false;
            Assert.IsFalse(c.Table.IsTargetFor(c.It(), ThHand.Right, 1));
        }

        [Test] public void 端末_予約の承認の後に2本目が握るとThLiftを送って飛び始める()
        {
            var c = new ClientRig(M);
            c.Reserve();
            Assert.IsTrue(c.Table.TryBeginLift(0, ThHand.Right, 1, 0.2));
            var l = c.Tx.Of<ThLiftMsg>(Rpc.ThLift).Single();
            Assert.AreEqual((0, 1, 1), (l.item, l.reqId, l.gen));
            Assert.AreEqual(ItemState.Flying, c.It().State);
            Assert.IsTrue(c.It().LiftPending);
            Assert.IsTrue(c.Has(ItemEventKind.LiftStarted));
            Assert.IsFalse(c.It().Revealed, "ThLifted まで値段は出ない");
            Assert.IsFalse(c.Table.IsTargetFor(c.It(), ThHand.Right, 1), "持ち上げ中は対象にならない");
        }

        [Test] public void 端末_承認の前に2本目が握ったら承認の瞬間にThLiftを送る()
        {
            var c = new ClientRig(M);
            c.Table.TryBeginLock(0, ThHand.Left, 0, 0);
            Assert.IsTrue(c.Table.TryBeginLift(0, ThHand.Right, 1, 0.02));
            Assert.AreEqual(0, c.Tx.Of<ThLiftMsg>(Rpc.ThLift).Count);
            Assert.IsTrue(c.It().LiftQueued);
            Assert.IsFalse(c.Has(ItemEventKind.LiftStarted));
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 2, reqId = 1, gen = 1, reserve = true }, 0.05);
            var l = c.Tx.Of<ThLiftMsg>(Rpc.ThLift).Single();
            Assert.AreEqual((1, 1), (l.reqId, l.gen));
            Assert.IsTrue(c.Has(ItemEventKind.LiftStarted));
            Assert.AreEqual(ItemState.Flying, c.It().State);
            Assert.IsFalse(c.It().LiftQueued);
        }

        [Test] public void 端末_承認の前に2本目が開いたらキューを消し承認後は予約のまま()
        {
            var c = new ClientRig(M);
            c.Table.TryBeginLock(0, ThHand.Left, 0, 0);
            c.Table.TryBeginLift(0, ThHand.Right, 1, 0.02);
            c.Table.OnMHandReleased(0, ThHand.Right, false, 0.03);
            Assert.IsFalse(c.It().LiftQueued);
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 2, reqId = 1, gen = 1, reserve = true }, 0.05);
            Assert.AreEqual(0, c.Tx.Of<ThLiftMsg>(Rpc.ThLift).Count);
            Assert.IsTrue(c.It().Reserved);
            Assert.IsTrue(c.Table.IsTargetFor(c.It(), ThHand.Right, 1), "もう一度 2 本目に使える");
        }

        [Test] public void 端末_ThLiftedでHeldになり値段公開と発見演出が出る()
        {
            var c = new ClientRig(M);
            c.Reserve();
            c.Lift(0.2, 700);
            Assert.AreEqual(ItemState.Held, c.It().State);
            Assert.IsTrue(c.It().Approved); Assert.IsFalse(c.It().LiftPending);
            Assert.IsTrue(c.It().Revealed); Assert.AreEqual(700, c.It().Value);
            Assert.IsTrue(c.Has(ItemEventKind.Approved)); Assert.IsTrue(c.Has(ItemEventKind.Revealed)); Assert.IsTrue(c.Has(ItemEventKind.Discovered));
            // 重複は無視
            c.Events.Clear();
            c.Table.OnLifted(new ThLiftedMsg { item = 0, clientNo = 2, reqId = 1, gen = 1, value = 700 }, 0.5);
            Assert.AreEqual(0, c.Events.Count);
            // 持った後は位置の送信・減額の報告ができる
            Assert.IsTrue(c.Table.TryReportDamage(0, -1, Vector3.zero, 5f));
        }

        [Test] public void 端末_他の人のThLiftedは今のSの他の端末が持ったと同じ()
        {
            var c = new ClientRig(M);
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 3, reqId = 1, gen = 1, reserve = true }, 0);
            c.Table.OnLifted(new ThLiftedMsg { item = 0, clientNo = 3, reqId = 1, gen = 1, value = 800 }, 0.2);
            Assert.AreEqual(ItemState.Held, c.It().State);
            Assert.IsTrue(c.It().Revealed); Assert.AreEqual(800, c.It().Value);
            Assert.AreEqual(3, c.It().OwnerClientNo);
            Assert.IsTrue(c.Has(ItemEventKind.RemoteHeld));
            Assert.IsTrue(c.Has(ItemEventKind.Discovered));
        }

        [Test] public void 端末_ThLiftの否認で元の位置へ戻しCancelを送る()
        {
            var c = new ClientRig(M);
            c.Reserve();
            c.Table.TryBeginLift(0, ThHand.Right, 1, 0.2);
            c.Table.OnDenied(new ThGrabDeniedMsg { item = 0, clientNo = 2, reqId = 1 }, 0.3);
            Assert.IsTrue(c.Has(ItemEventKind.Reverted));
            Assert.AreEqual(ItemState.Resting, c.It().State);
            Assert.IsFalse(c.It().Mine); Assert.IsFalse(c.It().Reserved);
            Assert.AreEqual(1, c.Releases(ReleaseReason.Cancel).Count);
            Assert.IsTrue(c.Table.IsGrabbable(c.It()));
        }

        [Test] public void 端末_ThLiftedが来ないまま待ち上限を超えたら取りやめる()
        {
            var c = new ClientRig(M);
            c.Reserve();
            c.Table.TryBeginLift(0, ThHand.Right, 1, 1.0);
            c.Table.Tick(1.9);
            Assert.IsFalse(c.Has(ItemEventKind.Reverted));
            c.Table.Tick(2.01);
            Assert.IsTrue(c.Has(ItemEventKind.Reverted));
            Assert.AreEqual(1, c.Releases(ReleaseReason.Cancel).Count);
        }

        [Test] public void 端末_予約の承認待ちの上限と承認前に手を開いた場合はSと同じく取りやめる()
        {
            var c = new ClientRig(M);
            c.Table.TryBeginLock(0, ThHand.Left, 0, 0);
            c.Table.Tick(1.01);
            Assert.IsTrue(c.Has(ItemEventKind.Reverted));
            Assert.AreEqual(1, c.Releases(ReleaseReason.Cancel).Count);
            var d = new ClientRig(M);
            d.Table.TryBeginLock(0, ThHand.Left, 0, 0);
            d.Table.OnMHandReleased(0, ThHand.Left, false, 0.1);
            Assert.IsTrue(d.Has(ItemEventKind.Reverted));
            Assert.AreEqual(1, d.Releases(ReleaseReason.Cancel).Count);
        }

        [Test] public void 端末_ThLiftedの前にどちらかの手を開いたら元の位置へ戻してCancelを送る()
        {
            foreach (var hand in new[] { ThHand.Left, ThHand.Right })
            {
                var c = new ClientRig(M);
                c.Reserve();
                c.Table.TryBeginLift(0, ThHand.Right, 1, 0.2);
                c.Table.OnMHandReleased(0, hand, false, 0.25);
                Assert.IsTrue(c.Has(ItemEventKind.Reverted), hand.ToString());
                Assert.AreEqual(ItemState.Resting, c.It().State);
                Assert.AreEqual(1, c.Releases(ReleaseReason.Cancel).Count);
            }
        }

        [Test] public void 端末_予約した手を開くとすぐUnreserveを送ってRestingに戻る()
        {
            var c = new ClientRig(M);
            c.Reserve();
            c.Table.OnMHandReleased(0, ThHand.Left, false, 0.5);
            var r = c.Releases(ReleaseReason.Unreserve).Single();
            Assert.AreEqual((1, 1), (r.reqId, r.gen));
            Assert.AreEqual(ItemState.Resting, c.It().State);
            Assert.IsFalse(c.It().Mine); Assert.IsFalse(c.It().Reserved);
            Assert.IsTrue(c.Has(ItemEventKind.Unreserved));
            Assert.IsTrue(c.Table.IsGrabbable(c.It()));
        }

        [Test] public void 端末_予約した手を見失っても見失い待ちの間は予約のまま_握って戻れば続く()
        {
            var c = new ClientRig(M);
            c.Reserve();
            c.Table.OnMHandReleased(0, ThHand.Left, true, 1.0);
            Assert.IsTrue(c.It().ReserveLostWait);
            Assert.AreEqual(0, c.Releases(ReleaseReason.Unreserve).Count);
            c.Table.Tick(2.4);
            Assert.IsTrue(c.It().Reserved);
            Assert.IsFalse(c.Table.IsTargetFor(c.It(), ThHand.Right, 1), "見失い待ちの間は 2 本目に進まない");
            Assert.IsTrue(c.Table.CanRegrip(c.It(), ThHand.Left));
            Assert.IsFalse(c.Table.CanRegrip(c.It(), ThHand.Right));
            Assert.IsTrue(c.Table.OnMHandRegripped(0, ThHand.Left, 2.4));
            c.Table.Tick(10);
            Assert.IsTrue(c.It().Reserved, "握って戻ったので予約のまま");
            Assert.AreEqual(0, c.Releases(ReleaseReason.Unreserve).Count);
        }

        [Test] public void 端末_予約した手を見失って見失い待ちを過ぎたらUnreserveを送る()
        {
            var c = new ClientRig(M);
            c.Reserve();
            c.Table.OnMHandReleased(0, ThHand.Left, true, 1.0);
            c.Table.Tick(2.49);
            Assert.AreEqual(0, c.Releases(ReleaseReason.Unreserve).Count);
            c.Table.Tick(2.51);
            Assert.AreEqual(1, c.Releases(ReleaseReason.Unreserve).Count);
            Assert.AreEqual(ItemState.Resting, c.It().State);
            Assert.IsTrue(c.Has(ItemEventKind.Unreserved));
        }

        [Test] public void 端末_見失った手が開いた状態で戻ったらすぐUnreserveを送る()
        {
            var c = new ClientRig(M);
            c.Reserve();
            c.Table.OnMHandReleased(0, ThHand.Left, true, 1.0);
            c.Table.OnReserveHandReturnedOpen(0, ThHand.Right);   // 別の手では何も起きない
            Assert.AreEqual(0, c.Releases(ReleaseReason.Unreserve).Count);
            c.Table.OnReserveHandReturnedOpen(0, ThHand.Left);
            Assert.AreEqual(1, c.Releases(ReleaseReason.Unreserve).Count);
            Assert.AreEqual(ItemState.Resting, c.It().State);
        }

        [Test] public void 端末_ホストのThFreedで自分の予約が消えていれば手から外す()
        {
            var c = new ClientRig(M);
            c.Reserve();
            c.Events.Clear();
            c.Table.OnFreed(new ThFreedMsg { item = 0, gen = 2, pos = new Vector3(0, 0, 5) });
            Assert.IsTrue(c.Has(ItemEventKind.Freed));
            Assert.IsFalse(c.It().Mine); Assert.IsFalse(c.It().Reserved);
            Assert.AreEqual(ItemState.Resting, c.It().State);
        }

        // ---- 両手で持っている間の猶予 ----

        [Test] public void 端末_猶予の間に外れた手が握り直せば両手に戻り落とさない()
        {
            foreach (var lost in new[] { false, true })
            {
                var c = new ClientRig(M);
                c.Reserve(); c.Lift(0.2);
                c.Table.OnMHandReleased(0, ThHand.Right, lost, 5.0);
                Assert.IsTrue(c.It().GraceActive);
                Assert.IsTrue(c.Has(ItemEventKind.GraceStarted));
                Assert.AreEqual(ItemState.Held, c.It().State, "猶予の間は残った手で持ち続ける");
                c.Table.Tick(5.4);
                Assert.IsFalse(c.Has(ItemEventKind.Dropped));
                Assert.IsTrue(c.Table.CanRegrip(c.It(), ThHand.Right));
                Assert.IsFalse(c.Table.CanRegrip(c.It(), ThHand.Left), "残った手ではなく外れた手だけ");
                Assert.IsTrue(c.Table.OnMHandRegripped(0, ThHand.Right, 5.45));
                Assert.IsTrue(c.Has(ItemEventKind.GraceEnded));
                Assert.IsFalse(c.It().GraceActive);
                c.Table.Tick(10);
                Assert.IsFalse(c.Has(ItemEventKind.Dropped), "lost=" + lost);
                Assert.AreEqual(ItemState.Held, c.It().State);
                Assert.AreEqual(0, c.Releases(ReleaseReason.Dropped).Count);
            }
        }

        [Test] public void 端末_猶予が切れたらDroppedを送って落とす()
        {
            var c = new ClientRig(M);
            c.Reserve(); c.Lift(0.2);
            c.Table.OnMHandReleased(0, ThHand.Left, false, 5.0);
            c.Table.Tick(5.49);
            Assert.IsFalse(c.Has(ItemEventKind.Dropped));
            c.Table.Tick(5.51);
            Assert.IsTrue(c.Has(ItemEventKind.Dropped));
            Assert.AreEqual(ItemState.Falling, c.It().State);
            var r = c.Releases(ReleaseReason.Dropped).Single();
            Assert.AreEqual((1, 1), (r.reqId, r.gen));
            Assert.IsFalse(c.Table.CanRegrip(c.It(), ThHand.Left), "落とした後は戻れない");
        }

        [Test] public void 端末_猶予の中で残った手も開くとすぐDropped()
        {
            var c = new ClientRig(M);
            c.Reserve(); c.Lift(0.2);
            c.Table.OnMHandReleased(0, ThHand.Right, false, 5.0);
            c.Table.OnMHandReleased(0, ThHand.Left, false, 5.1);
            Assert.IsTrue(c.Has(ItemEventKind.Dropped));
            Assert.AreEqual(ItemState.Falling, c.It().State);
            Assert.AreEqual(1, c.Releases(ReleaseReason.Dropped).Count);
        }

        [Test] public void 端末_片手を見失って猶予が切れたらそっと置く()
        {
            var c = new ClientRig(M);
            c.Reserve(); c.Lift(0.2);
            c.Table.OnMHandReleased(0, ThHand.Right, true, 5.0);
            c.Table.Tick(5.6);
            Assert.AreEqual(ItemState.Placing, c.It().State, "見失いで猶予が切れたら、落とさずそっと置く（#41）");
            Assert.AreEqual(0, c.Releases(ReleaseReason.Dropped).Count);
        }

        [Test] public void 端末_落とした後は回収の範囲でThCollectを送れる()
        {
            var c = new ClientRig(M);
            c.Reserve(); c.Lift(0.2);
            c.Table.OnMHandReleased(0, ThHand.Right, false, 5.0);
            c.Table.OnMHandReleased(0, ThHand.Left, false, 5.1);
            c.Tx.Sent.Clear();
            c.Table.NoteInCollectZone(0, 5.2);
            Assert.AreEqual(1, c.Tx.Of<ThCollectMsg>(Rpc.ThCollect).Count);
        }

        [Test] public void 端末_猶予中は減額の報告もできる()
        {
            var c = new ClientRig(M);
            c.Reserve(); c.Lift(0.2);
            c.Table.OnMHandReleased(0, ThHand.Right, false, 5.0);
            Assert.IsTrue(c.Table.TryReportDamage(0, -1, Vector3.zero, 5f));
        }

        // ---- 状態表への追いつき ----

        static ThItemsMsg Items(ThItemEntry e) => new ThItemsMsg { items = new[] { e } };

        [Test] public void 端末_ThItemsで予約の承認に追いつく()
        {
            var c = new ClientRig(M);
            c.Table.TryBeginLock(0, ThHand.Left, 0, 0);
            c.Table.OnItems(Items(new ThItemEntry { item = 0, state = (int)ItemState.Locked, owner = 2, reqId = 1, gen = 1, value = 400 }), 0.5);
            Assert.IsTrue(c.It().Reserved);
            Assert.IsFalse(c.It().ReqPending);
            Assert.IsFalse(c.It().Revealed);
            Assert.IsFalse(c.Has(ItemEventKind.Revealed));
        }

        [Test] public void 端末_ThItemsで予約に追いついた瞬間に2本目が待っていれば持ち上げへ進む()
        {
            var c = new ClientRig(M);
            c.Table.TryBeginLock(0, ThHand.Left, 0, 0);
            c.Table.TryBeginLift(0, ThHand.Right, 1, 0.1);
            c.Table.OnItems(Items(new ThItemEntry { item = 0, state = (int)ItemState.Locked, owner = 2, reqId = 1, gen = 1, value = 400 }), 0.5);
            Assert.AreEqual(1, c.Tx.Of<ThLiftMsg>(Rpc.ThLift).Count);
            Assert.AreEqual(ItemState.Flying, c.It().State);
        }

        [Test] public void 端末_ThItemsで持ち上げ申請中にホストがHeldならThLiftedを受けたとして扱う()
        {
            var c = new ClientRig(M);
            c.Reserve();
            c.Table.TryBeginLift(0, ThHand.Right, 1, 0.2);
            c.Table.OnItems(Items(new ThItemEntry { item = 0, state = (int)ItemState.Held, owner = 2, reqId = 1, gen = 1, value = 700, revealed = true }), 0.5);
            Assert.AreEqual(ItemState.Held, c.It().State);
            Assert.IsTrue(c.It().Approved);
            Assert.IsTrue(c.It().Revealed);
            Assert.IsFalse(c.Has(ItemEventKind.Discovered), "追いついたときは演出なし");
        }

        [Test] public void 端末_ThItemsで持ち上げ申請中にホストがまだLockedなら待つ()
        {
            var c = new ClientRig(M);
            c.Reserve();
            c.Table.TryBeginLift(0, ThHand.Right, 1, 0.2);
            c.Table.OnItems(Items(new ThItemEntry { item = 0, state = (int)ItemState.Locked, owner = 2, reqId = 1, gen = 1, value = 400 }), 0.3);
            Assert.AreEqual(ItemState.Flying, c.It().State);
            Assert.IsTrue(c.It().LiftPending);
        }

        [Test] public void 端末_ThItemsで予約中のつもりがホストはRestingで世代が新しければ予約を消す()
        {
            var c = new ClientRig(M);
            c.Reserve();
            c.Table.OnItems(Items(new ThItemEntry { item = 0, state = (int)ItemState.Resting, owner = -1, reqId = 1, gen = 2, value = 400, pos = new Vector3(0, 0, 5) }), 1);
            Assert.IsTrue(c.Has(ItemEventKind.Freed));
            Assert.IsFalse(c.It().Mine); Assert.IsFalse(c.It().Reserved);
            Assert.AreEqual(ItemState.Resting, c.It().State);
            // 世代が同じ（ホストがまだ受けていない）なら予約のまま
            var d = new ClientRig(M);
            d.Reserve();
            d.Table.OnItems(Items(new ThItemEntry { item = 0, state = (int)ItemState.Resting, owner = -1, gen = 0, pos = new Vector3(0, 0, 5) }), 1);
            Assert.IsTrue(d.It().Reserved);
        }

        [Test] public void 端末_ThItemsで他の人のLockedはLockedで値段は公開せず_他の人のHeldは今のまま()
        {
            var c = new ClientRig(M, M);
            c.Table.OnItems(new ThItemsMsg
            {
                items = new[]
                {
                    new ThItemEntry { item = 0, state = (int)ItemState.Locked, owner = 3, reqId = 1, gen = 1, value = 400 },
                    new ThItemEntry { item = 1, state = (int)ItemState.Held, owner = 3, reqId = 1, gen = 1, value = 500, revealed = true },
                }
            }, 1);
            Assert.AreEqual(ItemState.Locked, c.It(0).State); Assert.AreEqual(3, c.It(0).OwnerClientNo); Assert.IsFalse(c.It(0).Revealed);
            Assert.AreEqual(ItemState.Held, c.It(1).State); Assert.IsTrue(c.It(1).Revealed);
            Assert.IsFalse(c.Table.IsGrabbable(c.It(0)));
        }

        [Test] public void 端末_ThItemsで自分の予約をホストが覚えていて端末が忘れていたらCancelを返す()
        {
            var c = new ClientRig(M);
            c.Table.OnItems(Items(new ThItemEntry { item = 0, state = (int)ItemState.Locked, owner = 2, reqId = 7, gen = 3 }), 1);
            var r = c.Tx.Of<ThReleaseMsg>(Rpc.ThRelease).Single();
            Assert.AreEqual((7, (int)ReleaseReason.Cancel), (r.reqId, r.reason));
        }

        [Test] public void 端末_持ち上げ申請中にThItemsで他の人のLockedになったら元の位置へ戻しホストの置き位置に合わせる()
        {
            var c = new ClientRig(M);
            c.Reserve();
            c.Table.TryBeginLift(0, ThHand.Right, 1, 0.2);   // Flying・LiftPending
            c.Events.Clear();
            var pos = new Vector3(2, 0, 7);
            c.Table.OnItems(Items(new ThItemEntry { item = 0, state = (int)ItemState.Locked, owner = 3, reqId = 4, gen = 5, value = 400, pos = pos }), 1);
            Assert.IsTrue(c.Has(ItemEventKind.Reverted), "空中に止めずに戻す");
            Assert.IsFalse(c.Has(ItemEventKind.OwnershipLost));
            Assert.AreEqual(ItemState.Locked, c.It().State);
            Assert.AreEqual(3, c.It().OwnerClientNo);
            Assert.AreEqual(pos, c.It().RestPos);
            Assert.IsFalse(c.It().Mine);
            Assert.IsFalse(c.It().Revealed);
        }

        [Test] public void 端末_ThFreedを取りこぼしてThItemsで他の人のLockedを受けたらRemoteLockedで置き位置が合う()
        {
            var c = new ClientRig(M);
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 3, reqId = 1, gen = 1, reserve = true }, 0);   // 前の人の予約
            c.Events.Clear();
            var pos = new Vector3(1, 0, 6);
            c.Table.OnItems(Items(new ThItemEntry { item = 0, state = (int)ItemState.Locked, owner = 4, reqId = 2, gen = 3, pos = pos }), 1);
            Assert.IsTrue(c.Has(ItemEventKind.RemoteLocked));
            Assert.AreEqual(4, c.It().OwnerClientNo);
            Assert.AreEqual(pos, c.It().RestPos);
            // 同じ内容がもう一度届いても出し直さない
            c.Events.Clear();
            c.Table.OnItems(Items(new ThItemEntry { item = 0, state = (int)ItemState.Locked, owner = 4, reqId = 2, gen = 3, pos = pos }), 2);
            Assert.AreEqual(0, c.Count(ItemEventKind.RemoteLocked));
        }

        [Test] public void 端末_同じ持ち主ですでにHeldなら同じ世代のLockedは戻さない()
        {
            var c = new ClientRig(M);
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 3, reqId = 1, gen = 1, reserve = true }, 0);
            c.Table.OnLifted(new ThLiftedMsg { item = 0, clientNo = 3, reqId = 1, gen = 1, value = 500 }, 0.1);
            Assert.AreEqual(ItemState.Held, c.It().State);
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 3, reqId = 1, gen = 1, reserve = true }, 0.2);
            Assert.AreEqual(ItemState.Held, c.It().State, "ThGrabbed(reserve) の遅れた再送");
            c.Table.OnItems(Items(new ThItemEntry { item = 0, state = (int)ItemState.Locked, owner = 3, reqId = 1, gen = 1, pos = Vector3.zero }), 0.3);
            Assert.AreEqual(ItemState.Held, c.It().State, "ThItems(Locked) の遅れ");
        }

        [Test] public void 端末_結果に入ると何も送らない()
        {
            var c = new ClientRig(M);
            c.Reserve();
            c.Table.Freeze();
            c.Tx.Sent.Clear();
            c.Table.OnMHandReleased(0, ThHand.Left, false, 1);
            c.Table.Tick(10);
            Assert.AreEqual(0, c.Tx.Sent.Count);
            Assert.IsFalse(c.Table.TryBeginLift(0, ThHand.Right, 1, 2));
        }

        [Test] public void 端末_Sの流れは変わらず左右の手でSを1つずつ持てる()
        {
            var c = new ClientRig(S, S, M);
            Assert.IsTrue(c.Table.TryBeginGrab(0, 0)); Assert.IsTrue(c.Table.TryBeginGrab(1, 0));
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 2, reqId = 1, gen = 1, value = 400 }, 0.1);
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 1, clientNo = 2, reqId = 2, gen = 1, value = 500 }, 0.1);
            Assert.AreEqual(ItemState.Held, c.It(0).State); Assert.AreEqual(ItemState.Held, c.It(1).State);
            Assert.IsTrue(c.It(0).Revealed && c.It(1).Revealed);
            c.Table.OnHandReleased(0, 1);
            Assert.AreEqual(ItemState.Falling, c.It(0).State);
        }

        // ---- 両手の目標 ----

        static float Angle(Vector3 a, Vector3 b) => Vector3.Angle(a, b);

        [Test] public void 両手の目標_線の向きにお宝の軸が合い上は真上のまま()
        {
            Assert.IsTrue(TwoHandPose.TrySolve(new Vector3(0, 1, 0), new Vector3(0, 1, 0.6f), Vector3.right * 0.6f, out var q));
            Assert.Less(Angle(q * Vector3.right, Vector3.forward), 0.1f);
            Assert.Less(Angle(q * Vector3.up, Vector3.up), 0.1f);
        }

        [Test] public void 両手の目標_予約した手が右なら反対向きになるが上は真上()
        {
            Assert.IsTrue(TwoHandPose.TrySolve(new Vector3(0.3f, 1, 0), new Vector3(-0.3f, 1, 0), Vector3.right * 0.6f, out var q));
            Assert.Less(Angle(q * Vector3.right, Vector3.left), 0.1f);
            Assert.Less(Angle(q * Vector3.up, Vector3.up), 0.1f);
        }

        [Test] public void 両手の目標_片手が高いと傾くが線の回りにはねじれない()
        {
            var a = new Vector3(0, 1, 0); var b = new Vector3(0.5f, 1.3f, 0);
            Assert.IsTrue(TwoHandPose.TrySolve(a, b, Vector3.right, out var q));
            var d = (b - a).normalized;
            Assert.Less(Angle(q * Vector3.right, d), 0.1f);
            // 上の軸は、線と真上を含む平面の中にある（ねじれがない）
            var up = q * Vector3.up;
            var normal = Vector3.Cross(d, Vector3.up).normalized;
            Assert.Less(Mathf.Abs(Vector3.Dot(up, normal)), 1e-3f);
            Assert.Greater(up.y, 0f);
        }

        [Test] public void 両手の目標_手やつかむ範囲が重なっていれば決めない_真上の線でも解ける()
        {
            Assert.IsFalse(TwoHandPose.TrySolve(Vector3.one, Vector3.one, Vector3.right, out _));
            Assert.IsFalse(TwoHandPose.TrySolve(Vector3.zero, Vector3.right, Vector3.zero, out _));
            Assert.IsTrue(TwoHandPose.TrySolve(Vector3.zero, Vector3.up, Vector3.right, out var q));
            Assert.Less(Angle(q * Vector3.right, Vector3.up), 0.1f);
        }

        [Test] public void 両手の目標_位置は両手の中点()
        {
            Assert.AreEqual(new Vector3(1, 2, 3), TwoHandPose.Midpoint(new Vector3(0, 1, 2), new Vector3(2, 3, 4)));
        }

        // ---- 「両手で持とう」 ----

        static List<ClientItem> HintItems(params (TreasureKind kind, Vector3 pos)[] defs)
        {
            var list = new List<ClientItem>();
            for (int i = 0; i < defs.Length; i++)
                list.Add(new ClientItem { Index = i, Kind = defs[i].kind, RestPos = defs[i].pos, HomePos = defs[i].pos });
            return list;
        }

        [Test] public void 両手で持とう_近い置かれたMで1回だけ出る()
        {
            var hint = new TwoHandHint();
            var items = HintItems((M, new Vector3(1, 0, 1)));
            Assert.AreEqual(-1, hint.Poll(items, new Vector3(5, 1.6f, 5), 1.5f, true), "遠い");
            Assert.AreEqual(0, hint.Poll(items, new Vector3(1, 1.6f, 2), 1.5f, true), "頭の高さではなく水平の距離で測る");
            Assert.AreEqual(-1, hint.Poll(items, new Vector3(1, 1.6f, 2), 1.5f, true), "2 回目は出ない");
            hint.Reset();
            Assert.AreEqual(0, hint.Poll(items, new Vector3(1, 1.6f, 2), 1.5f, true), "次の回では出る");
        }

        [Test] public void 両手で持とう_S_持たれている_予約されている_消えた_参加していないときは出ない()
        {
            var hint = new TwoHandHint();
            var items = HintItems((S, Vector3.zero), (M, Vector3.zero), (M, Vector3.zero), (M, Vector3.zero), (M, Vector3.zero));
            items[1].State = ItemState.Held; items[1].OwnerClientNo = 3;
            items[2].State = ItemState.Locked; items[2].OwnerClientNo = 3;
            items[3].Gone = true;
            items[4].Mine = true;   // 自分が予約中
            Assert.AreEqual(-1, hint.Poll(items, Vector3.zero, 1.5f, true));
            Assert.IsFalse(hint.Shown);
            Assert.AreEqual(-1, hint.Poll(HintItems((M, Vector3.zero)), Vector3.zero, 1.5f, false), "参加端末の本編中だけ");
            Assert.IsFalse(hint.Shown);
            // 条件を満たす M が現れたら出る（Resting に戻った）
            items[1].State = ItemState.Resting; items[1].OwnerClientNo = -1;
            Assert.AreEqual(1, hint.Poll(items, Vector3.zero, 1.5f, true));
        }

        [Test] public void 両手で持とう_ビームの距離ちょうどは出て_それより遠いと出ない()
        {
            var hint = new TwoHandHint();
            var items = HintItems((M, new Vector3(1.5f, 0, 0)));
            Assert.AreEqual(0, hint.Poll(items, Vector3.zero, 1.5f, true));
            var h2 = new TwoHandHint();
            Assert.AreEqual(-1, h2.Poll(items, new Vector3(-0.01f, 0, 0), 1.5f, true));
        }

        // ---- ホスト＋端末のつなぎ合わせ ----

        const int HostNo = 1;
        double _now;
        FakeNetwork _net;
        SessionHostCore _host;
        readonly Dictionary<int, string> _resolve = new Dictionary<int, string>();
        readonly List<SessionClientCore> _clients = new List<SessionClientCore>();

        static ThParams ShortParams()
        {
            var p = new ThParams();
            p.progress.openingSeconds = 3; p.progress.practiceSeconds = 5; p.progress.playingSeconds = 30; p.progress.resultSeconds = 3;
            p.grip.approvalWaitSeconds = 3;
            return p;
        }

        void Build()
        {
            _now = 100;
            _net = new FakeNetwork();
            var node = _net.Add(HostNo);
            var sp = ThParamsLoader.Load(ShortParams(), null, null);
            _host = new SessionHostCore(node, sp, new ThGameSettings(), null, 0, no => _resolve.TryGetValue(no, out var v) ? v : null, MakeSetup());
            _host.OwnClientNo = HostNo;
            node.OnReceive = (rpc, sender, args) => _host.OnRpc(rpc, sender, args, _now);
            _clients.Clear();
            _resolve.Clear();
        }

        SessionClientCore AddClient(int no, string id)
        {
            var node = _net.Add(no);
            var c = new SessionClientCore(id, node, new ThParams(), null, _now, MakeSetup()) { OwnClientNo = no };
            node.OnReceive = (rpc, sender, args) => c.OnRpc(rpc, sender, args, _now);
            _clients.Add(c); _resolve[no] = id;
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
            Advance(3.3);
            Advance(5.3);
            Assert.AreEqual(RoundState.Playing, _host.Machine.State);
            _net.Pump();
        }

        [TearDown] public void TearDown() { _host?.Dispose(); foreach (var c in _clients) c.Dispose(); }

        static int FirstM(ClientItemTable t) => t.Items.First(i => i.Kind == TreasureKind.M).Index;

        [Test] public void 結合_Aが予約_Bはつかめない_両手成立で全員がHeldと値段_落として回収()
        {
            Build();
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            StartPlaying();
            var ai = a.Items.CurrentValue; var bi = b.Items.CurrentValue;
            int idx = FirstM(ai);
            int value = ai.Get(idx).BaseValue;

            // A が片手で予約
            Assert.IsTrue(ai.TryBeginLock(idx, ThHand.Left, 0, _now));
            _net.Pump();
            Assert.IsTrue(ai.Get(idx).Reserved, "A に予約の承認が届く");
            Assert.AreEqual(ItemState.Locked, _host.Items.Get(idx).State);
            Assert.AreEqual(ItemState.Locked, bi.Get(idx).State, "B からも予約中に見える");
            Assert.IsFalse(bi.Get(idx).Revealed, "予約では値段は出ない");
            Assert.IsFalse(ai.Get(idx).Revealed);
            // B は同じ M をつかめない
            Assert.IsFalse(bi.IsGrabbable(bi.Get(idx)));
            Assert.IsFalse(bi.TryBeginLock(idx, ThHand.Left, 0, _now));

            // A がもう一方の手で両手成立
            Assert.IsTrue(ai.TryBeginLift(idx, ThHand.Right, 1, _now));
            _net.Pump();
            Assert.AreEqual(ItemState.Held, _host.Items.Get(idx).State);
            Assert.AreEqual(1, _host.Items.Get(idx).Gen);
            Assert.IsTrue(ai.Get(idx).Approved);
            Assert.AreEqual(ItemState.Held, ai.Get(idx).State);
            Assert.AreEqual(ItemState.Held, bi.Get(idx).State);
            Assert.AreEqual(2, bi.Get(idx).OwnerClientNo);
            Assert.IsTrue(ai.Get(idx).Revealed); Assert.IsTrue(bi.Get(idx).Revealed);
            Assert.AreEqual(value, bi.Get(idx).Value);

            // A が両手を開く → 猶予 → 残りも開くと落ちる
            ai.OnMHandReleased(idx, ThHand.Right, false, _now);
            Assert.AreEqual(ItemState.Held, ai.Get(idx).State);
            ai.OnMHandReleased(idx, ThHand.Left, false, _now + 0.1);
            _net.Pump();
            Assert.AreEqual(ItemState.Falling, _host.Items.Get(idx).State);
            Assert.AreEqual(1, _host.Items.Get(idx).Gen, "落とすだけでは世代は変わらない");
            // 荷台の範囲に落ちて回収
            ai.NoteInCollectZone(idx, _now);
            _net.Pump();
            Assert.IsTrue(_host.Items.Get(idx).Gone);
            Assert.AreEqual(value, _host.TotalAmount);
            Assert.IsTrue(ai.Get(idx).Gone); Assert.IsTrue(bi.Get(idx).Gone);
            Assert.AreEqual(value, a.Total.CurrentValue); Assert.AreEqual(value, b.Total.CurrentValue);
        }

        [Test] public void 結合_Aが予約の手を開くとBがつかめるようになる()
        {
            Build();
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            StartPlaying();
            var ai = a.Items.CurrentValue; var bi = b.Items.CurrentValue;
            int idx = FirstM(ai);
            ai.TryBeginLock(idx, ThHand.Left, 0, _now);
            _net.Pump();
            Assert.IsFalse(bi.IsGrabbable(bi.Get(idx)));
            ai.OnMHandReleased(idx, ThHand.Left, false, _now);
            _net.Pump();
            Assert.AreEqual(ItemState.Resting, _host.Items.Get(idx).State);
            Assert.IsTrue(ai.IsGrabbable(ai.Get(idx)));
            Assert.IsTrue(bi.IsGrabbable(bi.Get(idx)), "ThFreed でつかめるようになる");
            Assert.IsTrue(bi.TryBeginLock(idx, ThHand.Left, 0, _now));
            _net.Pump();
            Assert.IsTrue(bi.Get(idx).Reserved);
            Assert.AreEqual(3, _host.Items.Get(idx).OwnerClientNo);
        }

        [Test] public void 結合_Aが切断すると予約が外れ持っているお宝はそっと置かれる()
        {
            Build();
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            StartPlaying();
            var ai = a.Items.CurrentValue; var bi = b.Items.CurrentValue;
            var ms = ai.Items.Where(i => i.Kind == TreasureKind.M).Take(2).Select(i => i.Index).ToArray();
            ai.TryBeginLock(ms[0], ThHand.Left, 0, _now);
            ai.TryBeginLock(ms[1], ThHand.Left, 0, _now);
            _net.Pump();
            ai.TryBeginLift(ms[1], ThHand.Right, 1, _now);   // 2 つ目は持ち上げる
            _net.Pump();
            Assert.AreEqual(ItemState.Locked, _host.Items.Get(ms[0]).State);
            Assert.AreEqual(ItemState.Held, _host.Items.Get(ms[1]).State);
            _host.OnClientDisconnected(2, _now);
            _net.Pump();
            Assert.AreEqual(ItemState.Resting, _host.Items.Get(ms[0]).State);
            Assert.AreEqual(ItemState.Placing, _host.Items.Get(ms[1]).State, "持っているお宝はそっと置かれる（#41）");
            Assert.IsTrue(bi.IsGrabbable(bi.Get(ms[0])));
        }

        [Test] public void 結合_ThGrabbedとThLiftedを捨ててもThItemsで本人もほかの人もホストに追いつく()
        {
            Build();
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            StartPlaying();
            var ai = a.Items.CurrentValue; var bi = b.Items.CurrentValue;
            int idx = FirstM(ai);
            _net.Drop = (s, r, args, to) => (r == Rpc.ThGrabbed || r == Rpc.ThLifted) && to != HostNo;
            ai.TryBeginLock(idx, ThHand.Left, 0, _now);
            ai.TryBeginLift(idx, ThHand.Right, 1, _now);   // 承認前なのでキューされる
            _net.Pump();
            Advance(2.5);   // ThItems（1 秒ごと）を 2 回: 予約に追いつく → 持ち上げに追いつく。待ち上限は 3 秒なので先に追いつく
            Assert.AreEqual(ItemState.Held, _host.Items.Get(idx).State);
            Assert.AreEqual(ItemState.Held, ai.Get(idx).State, "予約 → 持ち上げまで進む");
            Assert.IsTrue(ai.Get(idx).Approved);
            Assert.AreEqual(ItemState.Held, bi.Get(idx).State);
            Assert.AreEqual(2, bi.Get(idx).OwnerClientNo);
            Assert.IsTrue(bi.Get(idx).Revealed);
        }

        [Test] public void 結合_SとMを同時に扱ってもSの流れは変わらない()
        {
            Build();
            var a = AddClient(2, "devA");
            AddClient(3, "devB");
            StartPlaying();
            var ai = a.Items.CurrentValue;
            int s = ai.Items.First(i => i.Kind == TreasureKind.S).Index;
            int m = FirstM(ai);
            Assert.IsTrue(ai.TryBeginGrab(s, _now));
            Assert.IsTrue(ai.TryBeginLock(m, ThHand.Right, 0, _now));
            _net.Pump();
            Assert.AreEqual(ItemState.Held, _host.Items.Get(s).State);
            Assert.IsTrue(_host.Items.Get(s).Revealed);
            Assert.AreEqual(ItemState.Locked, _host.Items.Get(m).State);
            Assert.IsFalse(_host.Items.Get(m).Revealed);
        }
    }
}
