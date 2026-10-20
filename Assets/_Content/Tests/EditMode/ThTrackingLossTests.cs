using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    /// <summary>見失い・手の飛び・そっと置く・持ち主の切断（#41）: 握りの判定・手の飛び・端末（S/M）・ホスト・ホスト＋端末のつなぎ合わせ</summary>
    public class ThTrackingLossTests
    {
        static readonly Bounds Zone = new Bounds(new Vector3(0, 0.9f, 0.8f), new Vector3(3f, 1.2f, 1f));
        const TreasureKind S = TreasureKind.S, M = TreasureKind.M;

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
                Catalog = new[] { new SpawnCatalogEntry(S, 0), new SpawnCatalogEntry(S, 1), new SpawnCatalogEntry(M, 2) },
                CollectBounds = Zone,
                CatalogHash = "h1",
            };
        }

        // ---- 握りの判定（Resume） ----

        static GripStateMachine Gripping()
        {
            var g = new GripStateMachine();
            Assert.AreEqual(GripEvent.Gripped, g.Update(0, true, 1f));
            return g;
        }

        [Test] public void 握り_見失いで解除された後Resumeしてもイベントは出ず握っている状態に戻る()
        {
            var g = Gripping();
            g.Update(0.05, false, 0);
            Assert.AreEqual(GripEvent.Released, g.Update(0.2, false, 0));
            Assert.IsFalse(g.IsGripping);
            g.Resume();
            Assert.IsTrue(g.IsGripping);
            Assert.AreEqual(GripEvent.None, g.Update(1.0, true, 0.5f), "離す側のしきい値以上。新しい握りの確定は待たない");
        }

        [Test] public void 握り_Resumeの後に離す判定の間下回ると_開いた_で解除される()
        {
            var g = Gripping();
            g.Update(0.05, false, 0); g.Update(0.2, false, 0);
            g.Resume();
            Assert.AreEqual(GripEvent.None, g.Update(1.0, true, 0.1f));
            Assert.AreEqual(GripEvent.None, g.Update(1.2, true, 0.1f), "離す判定の途中");
            Assert.AreEqual(GripEvent.Released, g.Update(1.3, true, 0.1f));
            Assert.AreEqual(GripReleaseReason.Opened, g.LastReleaseReason);
        }

        [Test] public void 握り_Resumeの後に見失いが続くと_見失い_で解除される()
        {
            var g = Gripping();
            g.Update(0.05, false, 0); g.Update(0.2, false, 0);
            g.Resume();
            Assert.AreEqual(GripEvent.None, g.Update(1.0, false, 0));
            Assert.AreEqual(GripEvent.Released, g.Update(1.2, false, 0));
            Assert.AreEqual(GripReleaseReason.Lost, g.LastReleaseReason);
        }

        // ---- 手の飛び ----

        [Test] public void 飛び_判定より小さい動きは通す()
        {
            var f = new HandJumpFilter();
            f.Update(true, Vector3.zero, 0.3f);
            var r = f.Update(true, new Vector3(0.29f, 0, 0), 0.3f);
            Assert.IsFalse(r.Jumped);
            Assert.AreEqual(0.29f, r.Position.x, 1e-6f);
        }

        [Test] public void 飛び_判定以上は飛びでそのフレームだけ前の位置_次のフレームから新しい位置()
        {
            var f = new HandJumpFilter();
            f.Update(true, Vector3.zero, 0.3f);
            var r = f.Update(true, new Vector3(0.31f, 0, 0), 0.3f);
            Assert.IsTrue(r.Jumped);
            Assert.AreEqual(0f, r.Position.x, 1e-6f);
            var r2 = f.Update(true, new Vector3(0.32f, 0, 0), 0.3f);
            Assert.IsFalse(r2.Jumped);
            Assert.AreEqual(0.32f, r2.Position.x, 1e-6f);
        }

        [Test] public void 飛び_一瞬だけ飛んで戻ると使う位置は動かない()
        {
            var f = new HandJumpFilter();
            f.Update(true, Vector3.zero, 0.3f);
            var a = f.Update(true, new Vector3(1f, 0, 0), 0.3f);
            var b = f.Update(true, Vector3.zero, 0.3f);
            Assert.IsTrue(a.Jumped); Assert.IsTrue(b.Jumped, "戻りも飛び");
            Assert.AreEqual(0f, a.Position.x); Assert.AreEqual(0f, b.Position.x);
        }

        [Test] public void 飛び_見失っても前の位置を覚え_戻った最初のフレームも比べる()
        {
            var f = new HandJumpFilter();
            f.Update(true, Vector3.zero, 0.3f);
            f.Update(false, Vector3.zero, 0.3f);
            var r = f.Update(true, new Vector3(5f, 0, 0), 0.3f);
            Assert.IsTrue(r.Jumped, "短い見失いの後に大きく離れて戻っても飛び");
            Assert.AreEqual(0f, r.Position.x);
            var f2 = new HandJumpFilter();
            f2.Update(true, Vector3.zero, 0.3f);
            f2.Update(false, Vector3.zero, 0.3f);
            Assert.IsFalse(f2.Update(true, new Vector3(0.1f, 0, 0), 0.3f).Jumped);
            var f3 = new HandJumpFilter();
            Assert.IsFalse(f3.Update(true, new Vector3(5f, 0, 0), 0.3f).Jumped, "最初のフレームは比べる相手がない");
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

            /// <summary>S を持つ（承認済みで Held）</summary>
            public void HoldS(int i = 0, double t = 0)
            {
                Assert.IsTrue(Table.TryBeginGrab(i, t));
                Table.OnGrabbed(new ThGrabbedMsg { item = i, clientNo = Own, reqId = It(i).MyReqId, gen = 1, value = 200 }, t + 0.05);
            }
            /// <summary>M を両手で持つ（左手=持ち手0 で予約、右手=持ち手1 で両手成立）</summary>
            public void HoldM(double t = 0)
            {
                Assert.IsTrue(Table.TryBeginLock(0, ThHand.Left, 0, t));
                Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = Own, reqId = It().MyReqId, gen = 1, value = 0, reserve = true }, t + 0.05);
                Assert.IsTrue(Table.TryBeginLift(0, ThHand.Right, 1, t + 0.1));
                Table.OnLifted(new ThLiftedMsg { item = 0, clientNo = Own, reqId = It().MyReqId, gen = It().Gen, value = 700 }, t + 0.15);
            }
        }

        [Test] public void S_持っている手を見失うとHoveringで落とさず免除中()
        {
            var c = new ClientRig(S, S);
            c.HoldS();
            c.Table.OnHandReleased(0, 1.0, true);
            Assert.AreEqual(ItemState.Hovering, c.It().State);
            Assert.IsTrue(c.Has(ItemEventKind.Hovered));
            Assert.IsFalse(c.Has(ItemEventKind.Dropped));
            Assert.AreEqual(0, c.Releases(ReleaseReason.Dropped).Count, "ホストへは何も送らない");
            Assert.IsTrue(c.It().Mine); Assert.IsTrue(c.It().Approved);
            Assert.IsTrue(c.It().IsExemptAt(1.0));
        }

        [Test] public void S_Hovering中に握ったまま戻ると持ち続けて補正の無判定時間が始まる()
        {
            var c = new ClientRig(S);
            c.HoldS();
            c.Table.OnHandReleased(0, 1.0, true);
            c.Table.OnHoverObserved(0, false, 0f, 1.2);
            Assert.AreEqual(ItemState.Hovering, c.It().State, "見えていない間は待つ");
            c.Table.OnHoverObserved(0, true, 0.4f, 1.3);   // 離す側のしきい値ちょうど = 以上
            Assert.AreEqual(ItemState.Held, c.It().State);
            Assert.IsTrue(c.Has(ItemEventKind.HoverResumed));
            Assert.IsTrue(c.It().IsExemptAt(1.7), "補正の無判定時間の間");
            Assert.IsFalse(c.It().IsExemptAt(1.81), "0.5 秒後には免除が切れる");
            Assert.AreEqual(0, c.Tx.Of<ThRestMsg>(Rpc.ThRest).Count);
        }

        [Test] public void S_Hovering中に開いて戻るとPlacingになる()
        {
            var c = new ClientRig(S);
            c.HoldS();
            c.Table.OnHandReleased(0, 1.0, true);
            c.Table.OnHoverObserved(0, true, 0.39f, 1.2);
            Assert.AreEqual(ItemState.Hovering, c.It().State, "戻った直後の 1 フレームでは決めない");
            c.Table.OnHoverObserved(0, true, 0.39f, 1.44);
            Assert.AreEqual(ItemState.Hovering, c.It().State);
            c.Table.OnHoverObserved(0, true, 0.39f, 1.46);   // 離す判定 0.25 秒
            Assert.AreEqual(ItemState.Placing, c.It().State);
            Assert.IsTrue(c.Has(ItemEventKind.PlaceStarted));
        }

        [Test] public void S_Hovering中に開いて戻った時間は_見えなくなる_しきい値以上に戻ると数え直す()
        {
            var c = new ClientRig(S);
            c.HoldS();
            c.Table.OnHandReleased(0, 1.0, true);
            c.Table.OnHoverObserved(0, true, 0.1f, 1.2);
            c.Table.OnHoverObserved(0, false, 0f, 1.35);   // 見えなくなった
            c.Table.OnHoverObserved(0, true, 0.1f, 1.4);   // 数え直し
            c.Table.OnHoverObserved(0, true, 0.1f, 1.6);
            Assert.AreEqual(ItemState.Hovering, c.It().State);
            c.Table.OnHoverObserved(0, true, 0.1f, 1.66);
            Assert.AreEqual(ItemState.Placing, c.It().State);
            var d = new ClientRig(S);
            d.HoldS();
            d.Table.OnHandReleased(0, 1.0, true);
            d.Table.OnHoverObserved(0, true, 0.1f, 1.2);
            d.Table.OnHoverObserved(0, true, 0.5f, 1.4);   // しきい値以上に戻る = すぐ持ち続ける
            Assert.AreEqual(ItemState.Held, d.It().State);
        }

        [Test] public void S_見失い待ちの1_5秒の間に戻らなければPlacing()
        {
            var c = new ClientRig(S);
            c.HoldS();
            c.Table.OnHandReleased(0, 1.0, true);
            c.Table.Tick(2.49);
            Assert.AreEqual(ItemState.Hovering, c.It().State);
            c.Table.Tick(2.5);
            Assert.AreEqual(ItemState.Placing, c.It().State);
        }

        [Test] public void S_Placingから置く時間でThRestを送る_降ろした位置_荷台の範囲でも同じ()
        {
            foreach (var dest in new[] { new Vector3(1f, 0f, 6f), new Vector3(0f, 0.4f, 0.8f) })
            {
                var c = new ClientRig(S);
                c.HoldS();
                c.Table.OnHandReleased(0, 1.0, true);
                c.Table.Tick(2.5);
                c.Table.SetPlaceTarget(0, dest, Quaternion.Euler(0, 30, 0));
                c.Table.Tick(2.75);
                Assert.AreEqual(0, c.Tx.Of<ThRestMsg>(Rpc.ThRest).Count, "置く時間の前");
                c.Table.Tick(2.85);
                var r = c.Tx.Of<ThRestMsg>(Rpc.ThRest).Single();
                Assert.AreEqual(dest, r.pos);
                Assert.AreEqual(1, r.gen, "世代は変えない");
                Assert.AreEqual(ItemState.Resting, c.It().State);
                Assert.IsTrue(c.It().PendingRest);
                c.Tx.Sent.Clear();
                c.Table.Tick(3.6);   // 反映されるまで再送間隔で送り直す
                Assert.AreEqual(1, c.Tx.Of<ThRestMsg>(Rpc.ThRest).Count);
            }
        }

        [Test] public void S_承認前の見失いは今のまま取りやめ_開いたら今のまま落とす()
        {
            var c = new ClientRig(S);
            c.Table.TryBeginGrab(0, 0);
            c.Table.OnHandReleased(0, 0.1, true);
            Assert.IsTrue(c.Has(ItemEventKind.Reverted));
            Assert.AreEqual(ItemState.Resting, c.It().State);
            var d = new ClientRig(S);
            d.HoldS();
            d.Table.OnHandReleased(0, 1.0, false);
            Assert.IsTrue(d.Has(ItemEventKind.Dropped));
            Assert.AreEqual(ItemState.Falling, d.It().State);
            Assert.AreEqual(1, d.Releases(ReleaseReason.Dropped).Count);
        }

        [Test] public void 免除_手の飛びで補正の無判定時間が始まり_障害物は送らずお宝どうしはexemptAで送る()
        {
            var c = new ClientRig(S, S);
            c.HoldS();
            c.Table.NoteJump(0, 2.0);
            Assert.IsFalse(c.Table.TryReportDamage(0, -1, Vector3.zero, 2f, 2.2), "障害物は送らない");
            Assert.IsTrue(c.Table.TryReportDamage(0, 1, Vector3.zero, 2f, 2.2));
            Assert.IsTrue(c.Tx.Of<ThDamageMsg>(Rpc.ThDamage).Single().exemptA);
            Assert.IsTrue(c.Table.TryReportDamage(0, -1, Vector3.zero, 2f, 2.6), "0.5 秒後からは通常どおり");
            Assert.IsFalse(c.Tx.Of<ThDamageMsg>(Rpc.ThDamage).Last().exemptA);
        }

        [Test] public void 免除_Hovering_Placingの間も免除中()
        {
            var c = new ClientRig(S);
            c.HoldS();
            Assert.IsFalse(c.It().IsExemptAt(1.0));
            c.Table.OnHandReleased(0, 1.0, true);
            Assert.IsTrue(c.It().IsExemptAt(1.1));
            c.Table.Tick(2.5);
            Assert.AreEqual(ItemState.Placing, c.It().State);
            Assert.IsTrue(c.It().IsExemptAt(2.6));
        }

        [Test] public void 飛び_持っていない手の飛びでは補正の無判定時間は始まらない()
        {
            var c = new ClientRig(S);
            c.Table.NoteJump(0, 1.0);
            Assert.IsFalse(c.It().IsExemptAt(1.1));
            c.Table.TryBeginGrab(0, 1.2);   // 承認前
            c.Table.NoteJump(0, 1.3);
            Assert.IsTrue(double.IsNegativeInfinity(c.It().CorrectionUntil), "承認前の飛びでは始まらない");
        }

        [Test] public void 離れたら置く_判定の条件()
        {
            var c = new ClientRig(S);
            c.HoldS();
            var it = c.It();
            Assert.IsFalse(ClientItemTable.ShouldDetach(it, 0.49f, false, 1.0, 0.5f));
            Assert.IsTrue(ClientItemTable.ShouldDetach(it, 0.5f, false, 1.0, 0.5f));
            Assert.IsFalse(ClientItemTable.ShouldDetach(it, 2f, true, 1.0, 0.5f), "手元へ飛ぶ途中");
            c.Table.NoteJump(0, 1.0);
            Assert.IsFalse(ClientItemTable.ShouldDetach(it, 2f, false, 1.2, 0.5f), "補正の無判定時間の間");
            Assert.IsTrue(ClientItemTable.ShouldDetach(it, 2f, false, 1.5, 0.5f));
            c.Table.OnHandReleased(0, 2.0, true);
            Assert.IsFalse(ClientItemTable.ShouldDetach(it, 2f, false, 2.1, 0.5f), "Hovering");
        }

        [Test] public void 離れたら置く_CheckDetachでPlacingになる()
        {
            var c = new ClientRig(S);
            c.HoldS();
            Assert.IsFalse(c.Table.CheckDetach(0, 0.3f, false, 1.0));
            Assert.IsTrue(c.Table.CheckDetach(0, 0.6f, false, 1.0));
            Assert.AreEqual(ItemState.Placing, c.It().State);
            Assert.AreEqual(0, c.Releases(ReleaseReason.Dropped).Count, "減額も落下もしない");
        }

        // ---- 端末: M ----

        [Test] public void M_猶予の間に残った手も見失うとHovering_開いたなら今のまま落とす()
        {
            var c = new ClientRig(M);
            c.HoldM();
            c.Table.OnMHandReleased(0, ThHand.Right, true, 1.0);
            c.Table.OnMHandReleased(0, ThHand.Left, true, 1.2);
            Assert.AreEqual(ItemState.Hovering, c.It().State);
            Assert.IsFalse(c.It().GraceActive, "猶予は消える");
            Assert.AreEqual(0, c.Releases(ReleaseReason.Dropped).Count);
            var d = new ClientRig(M);
            d.HoldM();
            d.Table.OnMHandReleased(0, ThHand.Right, true, 1.0);
            d.Table.OnMHandReleased(0, ThHand.Left, false, 1.2);
            Assert.AreEqual(ItemState.Falling, d.It().State);
        }

        static ClientRig HoveringM()
        {
            var c = new ClientRig(M);
            c.HoldM();
            c.Table.OnMHandReleased(0, ThHand.Right, true, 1.0);
            c.Table.OnMHandReleased(0, ThHand.Left, true, 1.0);
            Assert.AreEqual(ItemState.Hovering, c.It().State);
            return c;
        }

        [Test] public void M_Hovering中に両手とも握って戻ると両手で持ち続ける()
        {
            var c = HoveringM();
            c.Table.OnMHoverObserved(0, true, 0.8f, false, 0f, 1.2);
            Assert.AreEqual(ItemState.Hovering, c.It().State, "片方だけ戻ったら待つ");
            c.Table.OnMHoverObserved(0, true, 0.8f, true, 0.4f, 1.3);
            Assert.AreEqual(ItemState.Held, c.It().State);
            Assert.IsTrue(c.Has(ItemEventKind.HoverResumed));
            Assert.IsTrue(c.It().IsExemptAt(1.5));
            Assert.IsFalse(c.It().GraceActive);
        }

        [Test] public void M_Hovering中にどちらかが開いて戻るとPlacing_戻らなければ1_5秒でPlacing()
        {
            var c = HoveringM();
            c.Table.OnMHoverObserved(0, true, 0.8f, true, 0.1f, 1.2);
            Assert.AreEqual(ItemState.Hovering, c.It().State, "離す判定の間は待つ");
            c.Table.OnMHoverObserved(0, true, 0.8f, true, 0.1f, 1.46);
            Assert.AreEqual(ItemState.Placing, c.It().State);
            var d = HoveringM();
            d.Table.OnMHoverObserved(0, false, 0f, true, 0.1f, 1.2);
            d.Table.OnMHoverObserved(0, false, 0f, true, 0.1f, 1.46);
            Assert.AreEqual(ItemState.Placing, d.It().State, "見えている方が開いていれば、もう片方が見えなくても置く");
            var e = HoveringM();
            e.Table.Tick(2.49);
            Assert.AreEqual(ItemState.Hovering, e.It().State);
            e.Table.Tick(2.5);
            Assert.AreEqual(ItemState.Placing, e.It().State);
            Assert.IsTrue(e.Has(ItemEventKind.PlaceStarted));
        }

        [Test] public void M_見失いで猶予が切れるとPlacing_開いて猶予が切れると今のまま落とす()
        {
            var c = new ClientRig(M);
            c.HoldM();
            c.Table.OnMHandReleased(0, ThHand.Right, true, 5.0);
            c.Table.Tick(5.6);
            Assert.AreEqual(ItemState.Placing, c.It().State);
            Assert.AreEqual(0, c.Releases(ReleaseReason.Dropped).Count);
            var d = new ClientRig(M);
            d.HoldM();
            d.Table.OnMHandReleased(0, ThHand.Right, false, 5.0);
            d.Table.Tick(5.6);
            Assert.AreEqual(ItemState.Falling, d.It().State);
            Assert.AreEqual(1, d.Releases(ReleaseReason.Dropped).Count);
        }

        [Test] public void M_Placingで両手の目標_猶予が消えThRestを送る()
        {
            var c = HoveringM();
            c.Table.Tick(2.5);
            c.Table.SetPlaceTarget(0, new Vector3(1, 0, 6), Quaternion.identity);
            Assert.AreEqual(-1, c.It().ReserveHand);
            Assert.AreEqual(-1, c.It().SecondHand);
            c.Table.Tick(2.85);
            Assert.AreEqual(new Vector3(1, 0, 6), c.Tx.Of<ThRestMsg>(Rpc.ThRest).Single().pos);
        }

        // ---- 端末: ThForceRest・追いつき ----

        [Test] public void 端末_ThForceRestで自分のお宝を手から外してPlacingにする()
        {
            var c = new ClientRig(S);
            c.HoldS();
            c.Table.OnForceRest(new ThForceRestMsg { item = 0, gen = 2, pos = new Vector3(1, 0, 2), rot = Quaternion.identity });
            Assert.AreEqual(ItemState.Placing, c.It().State);
            Assert.IsFalse(c.It().Mine); Assert.IsFalse(c.It().Approved);
            Assert.AreEqual(-1, c.It().OwnerClientNo);
            Assert.AreEqual(2, c.It().Gen);
            Assert.AreEqual(new Vector3(1, 0, 2), c.It().RestPos);
            Assert.IsTrue(c.Has(ItemEventKind.ForceRested));
            // その後の ThFreed を処理する
            c.Table.OnFreed(new ThFreedMsg { item = 0, gen = 3, pos = new Vector3(1, 0, 2), rot = Quaternion.identity });
            Assert.AreEqual(ItemState.Resting, c.It().State);
            Assert.IsTrue(c.Table.IsGrabbable(c.It()));
        }

        [Test] public void 端末_ThForceRestで他の人のお宝もPlacingにし_古い世代は捨てる()
        {
            var c = new ClientRig(S);
            c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 3, reqId = 1, gen = 4, value = 200 }, 0);
            c.Table.OnForceRest(new ThForceRestMsg { item = 0, gen = 4, pos = Vector3.one });   // 同じ世代 = 古い
            Assert.AreEqual(ItemState.Held, c.It().State);
            c.Table.OnForceRest(new ThForceRestMsg { item = 0, gen = 5, pos = Vector3.one });
            Assert.AreEqual(ItemState.Placing, c.It().State);
            Assert.AreEqual(-1, c.It().OwnerClientNo);
            Assert.AreEqual(1, c.Count(ItemEventKind.ForceRested));
        }

        [Test] public void 端末_ThItemsのPlacingで追いつき_同じ世代では二度目は何もしない()
        {
            var c = new ClientRig(S);
            c.HoldS();
            var e = new ThItemEntry { item = 0, state = (int)ItemState.Placing, owner = -1, gen = 2, value = 200, pos = new Vector3(2, 0, 2), rot = Quaternion.identity };
            c.Table.OnItems(new ThItemsMsg { items = new[] { e } }, 5);
            Assert.AreEqual(ItemState.Placing, c.It().State);
            Assert.IsFalse(c.It().Mine);
            Assert.AreEqual(new Vector3(2, 0, 2), c.It().RestPos);
            c.Table.OnItems(new ThItemsMsg { items = new[] { e } }, 6);
            Assert.AreEqual(1, c.Count(ItemEventKind.ForceRested));
        }

        [Test] public void M_Hovering中に片手が握って戻ってまた見失っても_両手が戻れば両手で持ち続け_片手を開くと猶予が始まる()
        {
            var c = HoveringM();
            c.Table.OnMHoverObserved(0, true, 0.8f, false, 0f, 1.2);   // 片手だけ戻る
            c.Table.OnMHandReleased(0, ThHand.Left, true, 1.3);          // その手がまた見失われる
            Assert.AreEqual(ItemState.Hovering, c.It().State, "待ち続ける");
            c.Table.OnMHoverObserved(0, true, 0.8f, true, 0.8f, 1.5);
            Assert.AreEqual(ItemState.Held, c.It().State);
            Assert.IsFalse(c.It().GraceActive);
            c.Table.OnMHandReleased(0, ThHand.Right, false, 2.0);
            Assert.IsTrue(c.It().GraceActive, "片手を開けば猶予が始まる");
            Assert.AreEqual(ItemState.Held, c.It().State);
        }

        [Test] public void Hovering中にThFreed_他の人のThGrabbed_消滅_Freezeが来ると見失い待ちと両手の状態が消える()
        {
            foreach (var kind in new[] { S, M })
            {
                for (int k = 0; k < 4; k++)
                {
                    var c = new ClientRig(kind);
                    if (kind == S) c.HoldS(); else c.HoldM();
                    if (kind == S) c.Table.OnHandReleased(0, 1.0, true);
                    else { c.Table.OnMHandReleased(0, ThHand.Right, true, 1.0); c.Table.OnMHandReleased(0, ThHand.Left, true, 1.0); }
                    Assert.AreEqual(ItemState.Hovering, c.It().State);
                    if (k == 0) c.Table.OnFreed(new ThFreedMsg { item = 0, gen = 5, pos = Vector3.zero });
                    else if (k == 1) c.Table.OnGrabbed(new ThGrabbedMsg { item = 0, clientNo = 3, reqId = 9, gen = 5, value = 200 }, 1.1);
                    else if (k == 2) c.Table.OnDamaged(new ThDamagedMsg { item = 0, gen = 5, hits = 3, amount = 100, value = 0, destroyed = true });
                    else c.Table.Freeze();
                    var it = c.It();
                    Assert.AreNotEqual(ItemState.Hovering, it.State, $"{kind} k={k}");
                    Assert.IsFalse(it.GraceActive); Assert.AreEqual(-1, it.ReserveHand); Assert.AreEqual(-1, it.SecondHand);
                    Assert.IsFalse(it.Approved);
                }
            }
        }

        [Test] public void Frozen_Placingの途中のお宝は降ろす先が決まっていれば残す()
        {
            var c = new ClientRig(S);
            c.HoldS();
            c.Table.OnHandReleased(0, 1.0, true);
            c.Table.Tick(2.5);
            c.Table.SetPlaceTarget(0, new Vector3(1, 0, 2), Quaternion.identity);
            c.Table.Freeze();
            Assert.AreEqual(ItemState.Placing, c.It().State);
            Assert.IsTrue(c.It().Mine && c.It().PlaceTargetSet);
            Assert.AreEqual(new Vector3(1, 0, 2), c.It().PlacePos);
            Assert.IsTrue(c.Has(ItemEventKind.Frozen));
        }

        // ---- ホスト ----

        sealed class HostRig
        {
            public readonly List<(Rpc rpc, object dto)> Out = new List<(Rpc, object)>();
            public readonly HostItemTable Table;
            public HostRig(params TreasureKind[] kinds) { Table = new HostItemTable(MakeSpawned(kinds), Zone, (r, d) => Out.Add((r, d))); }
            public List<T> Of<T>(Rpc rpc) => Out.Where(o => o.rpc == rpc).Select(o => (T)o.dto).ToList();
            public HostItem Item(int i) => Table.Items[i];
        }

        static ThGrabMsg GrabS(int item, int req) => new ThGrabMsg { item = item, reqId = req, kind = (int)S };
        static ThGrabMsg GrabM(int item, int req) => new ThGrabMsg { item = item, reqId = req, kind = (int)M };

        [Test] public void ホスト_exemptAのaは適用せずbは適用する()
        {
            var h = new HostRig(S, S);
            h.Table.OnGrab("A", 2, GrabS(0, 1));
            h.Table.OnGrab("B", 3, GrabS(1, 2));
            h.Table.OnDamage("A", new ThDamageMsg { a = 0, b = 1, speed = 2f, exemptA = true }, 10);
            Assert.AreEqual(0, h.Item(0).Hits);
            Assert.AreEqual(1, h.Item(1).Hits);
            var h2 = new HostRig(S, S);
            h2.Table.OnGrab("A", 2, GrabS(0, 1));
            h2.Table.OnGrab("B", 3, GrabS(1, 2));
            h2.Table.OnDamage("A", new ThDamageMsg { a = 0, b = 1, speed = 2f }, 10);   // 古い送り手（exemptA = false）
            Assert.AreEqual(1, h2.Item(0).Hits); Assert.AreEqual(1, h2.Item(1).Hits);
        }

        [Test] public void ホスト_exemptAは送り手がaの判定役のときだけ効く()
        {
            var h = new HostRig(S, S);
            h.Table.OnGrab("A", 2, GrabS(0, 1));
            h.Table.OnGrab("B", 3, GrabS(1, 2));
            // B が b=1 を自分のお宝として、a=0（A のお宝）に exemptA を付けて報告: A のお宝は免除されない
            h.Table.OnDamage("B", new ThDamageMsg { a = 0, b = 1, speed = 2f, exemptA = true }, 10);
            Assert.AreEqual(1, h.Item(0).Hits);
            Assert.AreEqual(1, h.Item(1).Hits);
        }

        [Test] public void ホスト_練習用はForceRestの対象になるがTickでは回収しない()
        {
            var h = new HostRig(S);
            h.Table.PlaceSolver = p => new Vector3(p.x, 0.5f, 0.8f);
            h.Table.OnGrab("A", 2, GrabS(0, 1));
            h.Item(0).Practice = true;
            h.Table.ForceRestItemsOf("A", 10);
            Assert.AreEqual(ItemState.Placing, h.Item(0).State);
            h.Table.Tick(10.31, true);
            Assert.IsFalse(h.Item(0).Gone);
            Assert.AreEqual(0, h.Table.Total);
            Assert.AreEqual(ItemState.Resting, h.Item(0).State);
        }

        [Test] public void ホスト_ThPosesで最後の位置を覚える_受け付け条件は免除の報告と同じ()
        {
            var h = new HostRig(S);
            h.Table.OnGrab("A", 2, GrabS(0, 1));
            h.Table.OnPoses("B", new ThPosesMsg { poses = new[] { new ThPoseEntry { item = 0, pos = Vector3.one } } });
            Assert.IsFalse(h.Item(0).HasLastPos, "判定役でない送り手は無視");
            h.Table.OnPoses("A", new ThPosesMsg { poses = new[] { new ThPoseEntry { item = 0, pos = new Vector3(1, 1, 3), rot = Quaternion.Euler(0, 40, 0) } } });
            Assert.IsTrue(h.Item(0).HasLastPos);
            Assert.AreEqual(new Vector3(1, 1, 3), h.Item(0).LastPos);
        }

        [Test] public void ホスト_ForceRestItemsOfでHeld_Flying_判定役のFallingが置かれ予約と他の端末は変わらない()
        {
            var h = new HostRig(S, S, M, S, S);
            h.Table.OnGrab("A", 2, GrabS(0, 1));                 // Held
            h.Table.OnGrab("B", 3, GrabS(1, 2));                 // 他の端末
            h.Table.OnGrab("A", 2, GrabM(2, 3));                 // Locked（予約）
            h.Table.OnGrab("A", 2, GrabS(3, 4));
            h.Table.OnRelease("A", new ThReleaseMsg { item = 3, reqId = 4, gen = 1, reason = (int)ReleaseReason.Dropped });   // Falling（判定役 A）
            h.Table.OnGrab("B", 3, GrabS(4, 5));
            h.Table.OnRelease("B", new ThReleaseMsg { item = 4, reqId = 5, gen = 1, reason = (int)ReleaseReason.Dropped });   // 他の端末の Falling
            h.Table.OnPoses("A", new ThPosesMsg { poses = new[] { new ThPoseEntry { item = 0, pos = new Vector3(1, 1.2f, 3), rot = Quaternion.Euler(10, 40, 20), exempt = true } } });
            h.Out.Clear();

            h.Table.ForceRestItemsOf("A", 50);

            var f = h.Of<ThForceRestMsg>(Rpc.ThForceRest);
            Assert.AreEqual(2, f.Count);
            var f0 = f.Single(x => x.item == 0);
            Assert.AreEqual(2, f0.gen, "世代 +1");
            Assert.AreEqual(new Vector3(1, 0, 3), f0.pos, "最後に受けた位置の真下（既定は床 y=0）");
            Assert.AreEqual(40f, f0.rot.eulerAngles.y, 1e-3f);
            Assert.AreEqual(0f, f0.rot.eulerAngles.x, 1e-3f);
            Assert.AreEqual(ItemState.Placing, h.Item(0).State);
            Assert.AreEqual(-1, h.Item(0).OwnerClientNo); Assert.IsNull(h.Item(0).OwnerDevice); Assert.IsNull(h.Item(0).ControllerDevice);
            Assert.IsFalse(h.Item(0).ExemptReported);
            Assert.AreEqual(new Vector3(1, 0, 3), h.Item(0).RestPos);
            Assert.AreEqual(ItemState.Placing, h.Item(3).State, "判定役の Falling");
            Assert.AreEqual(new Vector3(3, 0, 5), h.Item(3).RestPos, "位置を受けていなければ置き位置の真下");
            Assert.AreEqual(ItemState.Held, h.Item(1).State);
            Assert.AreEqual(ItemState.Locked, h.Item(2).State);
            Assert.AreEqual(ItemState.Falling, h.Item(4).State);
            Assert.AreEqual(0, h.Of<ThFreedMsg>(Rpc.ThFreed).Count);
        }

        [Test] public void ホスト_Tickで置く時間の後につかめるに戻す_荷台の範囲内なら回収_Playingでなければ回収しない()
        {
            foreach (var (inZone, playing) in new[] { (false, true), (true, true), (true, false) })
            {
                var h = new HostRig(S);
                if (inZone) h.Table.PlaceSolver = p => new Vector3(p.x, 0.5f, 0.8f);
                h.Table.OnGrab("A", 2, GrabS(0, 1));
                h.Table.ForceRestItemsOf("A", 10);
                h.Out.Clear();
                h.Table.Tick(10.25, playing);
                Assert.AreEqual(0, h.Out.Count, "置く時間の前");
                h.Table.Tick(10.31, playing);
                if (inZone && playing)
                {
                    Assert.IsTrue(h.Item(0).Gone);
                    Assert.AreEqual(400, h.Table.Total);
                    Assert.AreEqual(1, h.Of<ThCollectedMsg>(Rpc.ThCollected).Count);
                }
                else
                {
                    Assert.AreEqual(ItemState.Resting, h.Item(0).State);
                    var fr = h.Of<ThFreedMsg>(Rpc.ThFreed).Single();
                    Assert.AreEqual(3, fr.gen, "ThForceRest で +1、つかめるに戻すとき +1");
                    Assert.AreEqual(0, h.Table.Total);
                }
            }
        }

        [Test] public void ホスト_Placing中のThGrabは否認し_切れた端末の遅れたメッセージは捨てる()
        {
            var h = new HostRig(S);
            h.Table.OnGrab("A", 2, GrabS(0, 1));
            h.Table.ForceRestItemsOf("A", 10);
            h.Out.Clear();
            h.Table.OnGrab("B", 3, GrabS(0, 2));
            Assert.AreEqual(1, h.Of<ThGrabDeniedMsg>(Rpc.ThGrabDenied).Count);
            h.Table.OnRest("A", new ThRestMsg { item = 0, gen = 1, pos = new Vector3(0, 0.4f, 0.8f) });
            h.Table.OnRest("A", new ThRestMsg { item = 0, gen = 2, pos = new Vector3(0, 0.4f, 0.8f) });
            h.Table.OnCollect("A", new ThCollectMsg { item = 0, gen = 2 });
            h.Table.OnRelease("A", new ThReleaseMsg { item = 0, reqId = 1, gen = 2, reason = (int)ReleaseReason.Dropped });
            h.Table.OnPoses("A", new ThPosesMsg { poses = new[] { new ThPoseEntry { item = 0, exempt = true } } });
            Assert.AreEqual(ItemState.Placing, h.Item(0).State);
            Assert.IsFalse(h.Item(0).Gone);
            Assert.IsFalse(h.Item(0).ExemptReported);
            Assert.AreEqual(0, h.Of<ThFreedMsg>(Rpc.ThFreed).Count);
            Assert.AreEqual(0, h.Table.Total);
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
            p.progress.openingSeconds = 3; p.progress.practiceSeconds = 5; p.progress.playingSeconds = 60; p.progress.resultSeconds = 3;
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

        static int FirstS(ClientItemTable t, int skip = 0) => t.Items.Where(i => i.Kind == S).Skip(skip).First().Index;

        /// <summary>TreasureClient の代わりに、Placing になったら降ろす先（床 y=0）を渡す</summary>
        static void AutoPlace(ClientItemTable t, float x, float z) =>
            t.Events.Subscribe(e => { if (e.Kind == ItemEventKind.PlaceStarted) t.SetPlaceTarget(e.Item.Index, new Vector3(x, 0, z), Quaternion.identity); });

        [Test] public void 結合_Aが持ったまま切断するとBに届き_置く時間の後にBがつかめる()
        {
            Build();
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            StartPlaying();
            var ai = a.Items.CurrentValue; var bi = b.Items.CurrentValue;
            int idx = FirstS(ai);
            Assert.IsTrue(ai.TryBeginGrab(idx, _now)); _net.Pump();
            Assert.IsFalse(bi.IsGrabbable(bi.Get(idx)));
            _host.OnClientDisconnected(2, _now); _net.Pump();
            Assert.AreEqual(ItemState.Placing, _host.Items.Get(idx).State);
            Assert.AreEqual(ItemState.Placing, bi.Get(idx).State, "B に ThForceRest が届く");
            Assert.IsFalse(bi.IsGrabbable(bi.Get(idx)));
            Advance(0.5);
            Assert.AreEqual(ItemState.Resting, _host.Items.Get(idx).State);
            Assert.IsTrue(bi.IsGrabbable(bi.Get(idx)), "置く時間の後につかめる");
            Assert.AreEqual(_host.Items.Get(idx).RestPos, bi.Get(idx).RestPos, "降ろす先が端末とホストでずれない");
            Assert.IsTrue(bi.TryBeginGrab(idx, _now)); _net.Pump();
            Assert.IsTrue(bi.Get(idx).Approved);
        }

        [Test] public void 結合_Aが荷台の上で持ったまま切断すると回収されて合計が増える()
        {
            Build();
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            StartPlaying();
            var ai = a.Items.CurrentValue; var bi = b.Items.CurrentValue;
            int idx = FirstS(ai);
            int value = ai.Get(idx).BaseValue;
            _host.Items.PlaceSolver = p => new Vector3(p.x, 0.5f, 0.8f);   // 荷台の範囲内に降りる
            ai.TryBeginGrab(idx, _now); _net.Pump();
            _host.OnClientDisconnected(2, _now); _net.Pump();
            Advance(0.5);
            Assert.IsTrue(_host.Items.Get(idx).Gone);
            Assert.AreEqual(value, _host.TotalAmount);
            Assert.AreEqual(value, b.Total.CurrentValue);
            Assert.IsTrue(bi.Get(idx).Gone);
        }

        [Test] public void 結合_Aが持ったまま手を見失って1_5秒たつとThRestで全員の置き位置になりBがつかめる()
        {
            Build();
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            StartPlaying();
            var ai = a.Items.CurrentValue; var bi = b.Items.CurrentValue;
            AutoPlace(ai, 1f, 6f);
            int idx = FirstS(ai);
            ai.TryBeginGrab(idx, _now); _net.Pump();
            Assert.IsTrue(ai.Get(idx).Approved);
            ai.OnHandReleased(idx, _now, true);
            Advance(1.0);
            Assert.AreEqual(ItemState.Hovering, ai.Get(idx).State);
            Assert.AreEqual(ItemState.Held, _host.Items.Get(idx).State, "ホストの表は Held のまま");
            Assert.IsFalse(bi.IsGrabbable(bi.Get(idx)));
            Advance(1.0);   // 見失い待ち 1.5 秒 → Placing → 0.3 秒 → ThRest
            Advance(0.5);
            Assert.AreEqual(ItemState.Resting, _host.Items.Get(idx).State);
            Assert.AreEqual(new Vector3(1f, 0, 6f), _host.Items.Get(idx).RestPos);
            Assert.AreEqual(new Vector3(1f, 0, 6f), bi.Get(idx).RestPos);
            Assert.IsTrue(bi.IsGrabbable(bi.Get(idx)));
            Assert.AreEqual(0, _host.Items.Get(idx).Hits, "減額されない");
        }

        [Test] public void 結合_Aが見失いから握って戻ると持ち続けBはつかめないまま()
        {
            Build();
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            StartPlaying();
            var ai = a.Items.CurrentValue; var bi = b.Items.CurrentValue;
            int idx = FirstS(ai);
            ai.TryBeginGrab(idx, _now); _net.Pump();
            ai.OnHandReleased(idx, _now, true);
            Advance(0.5);
            ai.OnHoverObserved(idx, true, 0.6f, _now);
            Assert.AreEqual(ItemState.Held, ai.Get(idx).State);
            Advance(3.0);
            Assert.AreEqual(ItemState.Held, _host.Items.Get(idx).State);
            Assert.AreEqual(2, _host.Items.Get(idx).OwnerClientNo);
            Assert.IsFalse(bi.IsGrabbable(bi.Get(idx)));
            Assert.IsFalse(_net.Log.Any(l => l.sender == 2 && l.rpc == Rpc.ThRest));
        }

        [Test] public void 結合_免除中の衝突でお宝どうしなら相手だけ減額される()
        {
            Build();
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            StartPlaying();
            var ai = a.Items.CurrentValue; var bi = b.Items.CurrentValue;
            int i1 = FirstS(ai), i2 = FirstS(ai, 1);
            ai.TryBeginGrab(i1, _now); bi.TryBeginGrab(i2, _now); _net.Pump();
            ai.NoteJump(i1, _now);   // A のお宝は補正の無判定時間
            Assert.IsTrue(ai.TryReportDamage(i1, i2, Vector3.zero, 3f, _now)); _net.Pump();
            Assert.AreEqual(0, _host.Items.Get(i1).Hits, "免除中の a には適用しない");
            Assert.AreEqual(1, _host.Items.Get(i2).Hits, "相手は減額される");
            Assert.IsFalse(ai.TryReportDamage(i1, -1, Vector3.zero, 3f, _now), "障害物は送らない");
        }

        [Test] public void 結合_ThForceRestを捨ててもThItemsで追いつく()
        {
            Build();
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            StartPlaying();
            var ai = a.Items.CurrentValue; var bi = b.Items.CurrentValue;
            int idx = FirstS(ai);
            ai.TryBeginGrab(idx, _now); _net.Pump();
            _net.Drop = (s, r, args, to) => r == Rpc.ThForceRest && to == 3;
            _host.OnClientDisconnected(2, _now); _net.Pump();
            Assert.AreEqual(ItemState.Held, bi.Get(idx).State, "取りこぼした");
            _net.Drop = null;
            // 置く時間より先に届く ThItems（Placing）で追いつく
            _host.Items.Get(idx).PlaceSince = _now + 100;   // Tick で戻さず、ThItems の送信だけ確かめる
            Advance(1.2);
            Assert.AreEqual(ItemState.Placing, bi.Get(idx).State);
            Assert.AreEqual(-1, bi.Get(idx).OwnerClientNo);
        }
    }
}
