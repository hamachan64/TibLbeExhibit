using System;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// ClientItemTable の M（両手で持つ）の部分。設計書 4.4 の M の手順 1〜4・4.2 の〔M の猶予〕（U-17）・6.2 の追いつき。
    /// 手順: 片手予約（Locked。動かさない・値段も出さない）→ 両手成立（Flying → ThLifted で Held。ここが「持った」瞬間）。
    /// 純粋クラス。時刻は引数。見た目は購読者（TreasureClient）が <see cref="ClientItemTable.Events"/> を見て行う。
    /// </summary>
    public sealed partial class ClientItemTable
    {
        // ---- ビームの対象 ----

        /// <summary>
        /// この手・この持ち手を狙ってよいか。S と M の Resting は今までどおり（<see cref="IsGrabbable"/>）。
        /// 自分が予約中の M は、予約していない方の手で、予約していない方の持ち手だけを対象にする。
        /// </summary>
        public bool IsTargetFor(ClientItem it, ThHand hand, int handleIndex)
        {
            if (it == null) return false;
            if (IsOwnReservation(it))
            {
                return !Frozen && !it.Gone && ParticipantPlaying && !it.LiftPending && !it.LiftQueued && !it.ReserveLostWait
                    && (int)hand != it.ReserveHand && handleIndex != it.LockHandle;
            }
            return IsGrabbable(it);
        }

        /// <summary>自分が予約中の M か（予約の承認待ちを含む。持ち上げの申請に進む前）</summary>
        public static bool IsOwnReservation(ClientItem it) =>
            it.Kind == TreasureKind.M && it.Mine && it.State == ItemState.Locked && !it.Gone;

        // ---- 予約（手順 1・2） ----

        /// <summary>M を片手で握った。条件を満たせば予約を申請して Locked にする（お宝は動かさない）</summary>
        public bool TryBeginLock(int index, ThHand hand, int handleIndex, double now)
        {
            var it = Get(index);
            if (it == null || it.Kind != TreasureKind.M || !ParticipantPlaying || !IsGrabbable(it)) return false;
            it.Mine = true;
            it.MyReqId = ++_nextReqId;
            it.ReqPending = true;
            it.Approved = false;
            it.ReqSentAt = now;
            it.State = ItemState.Locked;
            it.Interp.Clear();
            ClearTwoHand(it);
            it.LockHandle = handleIndex;
            it.ReserveHand = (int)hand;
            Send(Rpc.ThGrab, new ThGrabMsg { item = it.Index, reqId = it.MyReqId, gen = it.Gen, kind = (int)TreasureKind.M });
            Raise(it, ItemEventKind.LockStarted);
            return true;
        }

        /// <summary>自分宛ての予約の承認（reserve=true）。値段は公開せず、発見演出も位置の送信もしない</summary>
        void OnReserveApproved(ClientItem it, ThGrabbedMsg m, double now)
        {
            if (it.MyReqId == m.reqId && it.Mine && !it.ReqPending) return;   // 重複
            if (it.MyReqId == m.reqId && it.Mine && it.ReqPending && it.Kind == TreasureKind.M)
            {
                it.Gen = m.gen;
                ApproveReservation(it, now);
                return;
            }
            // 最新ではない申請への承認。その申請IDで取りやめを返す（S の手順 5 と同じ）
            it.Gen = Math.Max(it.Gen, m.gen);
            Send(Rpc.ThRelease, new ThReleaseMsg { item = m.item, reqId = m.reqId, gen = m.gen, reason = (int)ReleaseReason.Cancel });
        }

        void ApproveReservation(ClientItem it, double now)
        {
            it.ReqPending = false;
            it.Reserved = true;
            it.OwnerClientNo = _ownClientNo();
            if (it.LiftQueued) StartLift(it, now);   // 承認の前に 2 本目の手が握っていた
        }

        /// <summary>他の端末が予約した。つかめなくする。値段は公開しない・お宝は置いた位置のまま</summary>
        void SetRemoteLocked(ClientItem it, int ownerClientNo, bool posChanged = false)
        {
            bool changed = posChanged || it.State != ItemState.Locked || it.OwnerClientNo != ownerClientNo;
            it.OwnerClientNo = ownerClientNo;
            it.State = ItemState.Locked;
            it.Interp.Clear();
            if (changed) Raise(it, ItemEventKind.RemoteLocked);   // 置き位置（RestPos）に置き直す
        }

        // ---- 両手成立（手順 3） ----

        /// <summary>
        /// 自分が予約中の M を、予約していない方の手・持ち手で握った。予約が認められていれば ThLift を送って手元へ飛ばす。
        /// まだ承認待ちなら覚えておき、承認の瞬間に持ち上げへ進む。
        /// </summary>
        public bool TryBeginLift(int index, ThHand hand, int handleIndex, double now)
        {
            var it = Get(index);
            if (it == null || !IsTargetFor(it, hand, handleIndex)) return false;
            if (!IsOwnReservation(it)) return false;
            it.SecondHand = (int)hand;
            it.SecondHandle = handleIndex;
            if (it.Reserved) StartLift(it, now);
            else it.LiftQueued = true;
            return true;
        }

        void StartLift(ClientItem it, double now)
        {
            it.LiftQueued = false;
            it.LiftPending = true;
            it.LiftSentAt = now;
            it.State = ItemState.Flying;
            Send(Rpc.ThLift, new ThLiftMsg { item = it.Index, reqId = it.MyReqId, gen = it.Gen });
            Raise(it, ItemEventKind.LiftStarted);
        }

        /// <summary>自分宛ての ThLifted（最新の申請ID）で Held にする。値段の公開・発見演出はここから（M の「持った」瞬間）</summary>
        public void OnLifted(ThLiftedMsg m, double now)
        {
            var it = m == null ? null : Get(m.item);
            if (it == null || it.Gone || Frozen) return;
            if (m.gen < it.Gen) return;   // 古い
            if (m.clientNo == _ownClientNo())
            {
                if (it.MyReqId == m.reqId && it.Mine && it.Approved) return;   // 重複
                if (it.MyReqId == m.reqId && it.Mine && it.LiftPending)
                {
                    it.Gen = Math.Max(it.Gen, m.gen);
                    ApproveLift(it, m.value, true);
                    return;
                }
                // 最新ではない申請への承認。その申請IDで取りやめを返す
                it.Gen = Math.Max(it.Gen, m.gen);
                Send(Rpc.ThRelease, new ThReleaseMsg { item = m.item, reqId = m.reqId, gen = m.gen, reason = (int)ReleaseReason.Cancel });
                return;
            }

            // 他の端末が持った（S の「他の端末が持った」と同じ）
            if (it.Mine) { var wasPending = it.ReqPending || it.LiftPending; Revert(it, wasPending); }
            it.Gen = Math.Max(it.Gen, m.gen);
            SetRemoteHeld(it, m.clientNo);
            SetRevealed(it, m.value, true);
        }

        void ApproveLift(ClientItem it, int value, bool discover)
        {
            it.LiftPending = false;
            it.LiftQueued = false;
            it.Approved = true;
            it.OwnerClientNo = _ownClientNo();
            it.State = ItemState.Held;
            SetRevealed(it, value, discover);
            Raise(it, ItemEventKind.Approved);
        }

        // ---- 手を開いた・見失った・握り直した（手順 4・猶予） ----

        /// <summary>
        /// M を握っていた手が外れた（lost: 見失いか、開いたか）。いまの段階で動きが変わる:
        /// 予約の承認前 = 取りやめ / 予約中 = 開いたらすぐ解除、見失いなら〔見失い待ち〕 / 2 本目の承認前 = 取りやめ /
        /// 両手で持っている = 〔M の猶予〕の始まり（残った手で持ち続ける）、猶予の間に残った手も外れたらすぐ落とす。
        /// </summary>
        public void OnMHandReleased(int index, ThHand hand, bool lost, double now)
        {
            var it = Get(index);
            if (it == null || it.Kind != TreasureKind.M || !it.Mine || it.Gone || Frozen) return;
            int h = (int)hand;

            if (it.State == ItemState.Locked)
            {
                if (h == it.ReserveHand)
                {
                    // 承認前は取りやめ、予約中は〔見失い待ち〕（下）。お宝は動いていないので、元に戻すだけでよい（Hovering にはしない）
                    if (it.ReqPending) { Revert(it, true); return; }   // 承認前に予約の手を開いた（S の手順 4 と同じ）
                    if (!it.Reserved) return;
                    if (lost) { if (!it.ReserveLostWait) { it.ReserveLostWait = true; it.ReserveLostSince = now; } return; }
                    Unreserve(it);
                    return;
                }
                if (h == it.SecondHand && it.LiftQueued) { it.LiftQueued = false; it.SecondHand = -1; it.SecondHandle = -1; }   // 2 本目が先に開いた
                return;
            }

            if (it.LiftPending)
            {
                // ThLifted の前にどちらかの手を開いた: 元の位置へ戻して Resting、Cancel（予約も消える）
                // 持ち上げの申請中は飛ぶ途中の短い間なので、見失いでも開いても元の位置へ戻すだけでよい（Hovering にはしない）
                if (h == it.ReserveHand || h == it.SecondHand) Revert(it, true);
                return;
            }

            if (it.Approved && it.State == ItemState.Held)
            {
                if (h != it.ReserveHand && h != it.SecondHand) return;
                if (!it.GraceActive)
                {
                    it.GraceActive = true;
                    it.GraceSince = now;
                    it.GraceHand = h;
                    it.GraceLost = lost;
                    Raise(it, ItemEventKind.GraceStarted);
                }
                else if (h != it.GraceHand)
                {
                    // 猶予の間に、残った手も外れた: 見失いなら両手とも見えない状態なので〔見失い待ち〕、開いたならすぐ落とす
                    if (lost) BeginHover(it, now);
                    else Drop(it);
                }
            }
        }

        /// <summary>この手が握り直したら、猶予・予約の見失い待ちから戻れるか</summary>
        public bool CanRegrip(ClientItem it, ThHand hand)
        {
            if (it == null || it.Kind != TreasureKind.M || !it.Mine || it.Gone || Frozen) return false;
            int h = (int)hand;
            if (it.GraceActive && it.State == ItemState.Held && it.GraceHand == h) return true;
            return it.Reserved && it.ReserveLostWait && it.State == ItemState.Locked && it.ReserveHand == h;
        }

        /// <summary>外れた手が握り直した（ビームの当て直しは要らない）。猶予なら両手持ちに戻る。見失い待ちなら予約のまま</summary>
        public bool OnMHandRegripped(int index, ThHand hand, double now)
        {
            var it = Get(index);
            if (!CanRegrip(it, hand)) return false;
            if (it.GraceActive)
            {
                it.GraceActive = false;
                it.GraceHand = -1;
                Raise(it, ItemEventKind.GraceEnded);
            }
            else it.ReserveLostWait = false;
            return true;
        }

        /// <summary>予約した手を見失っていて、手が開いた状態で戻った: 予約を解除する</summary>
        public void OnReserveHandReturnedOpen(int index, ThHand hand)
        {
            var it = Get(index);
            if (it == null || it.Kind != TreasureKind.M || !it.Mine || it.Gone || Frozen) return;
            if (it.State == ItemState.Locked && it.Reserved && it.ReserveLostWait && it.ReserveHand == (int)hand) Unreserve(it);
        }

        /// <summary>予約を解除する: ThRelease(予約解除) を送り、動いていないのでそのまま Resting に戻す</summary>
        void Unreserve(ClientItem it)
        {
            Send(Rpc.ThRelease, new ThReleaseMsg { item = it.Index, reqId = it.MyReqId, gen = it.Gen, reason = (int)ReleaseReason.Unreserve });
            it.Mine = false;
            it.Reserved = false;
            it.OwnerClientNo = -1;
            it.State = ItemState.Resting;
            ClearTwoHand(it);
            Raise(it, ItemEventKind.Unreserved);
        }

        /// <summary>両手持ちを落とす（Falling。世代は変えない）。猶予が切れた・猶予の間に残った手も外れた</summary>
        void Drop(ClientItem it)
        {
            it.State = ItemState.Falling;
            ClearTwoHand(it);
            Send(Rpc.ThRelease, new ThReleaseMsg { item = it.Index, reqId = it.MyReqId, gen = it.Gen, reason = (int)ReleaseReason.Dropped });
            Raise(it, ItemEventKind.Dropped);
        }

        /// <summary>M の毎フレーム: 〔見失い待ち〕・〔M の猶予〕・ThLift の承認の待ち上限。そのお宝の処理を終えたら true</summary>
        bool TickTwoHand(ClientItem it, double now)
        {
            if (it.LiftPending && now - it.LiftSentAt >= _p.grip.approvalWaitSeconds) { Revert(it, true); return true; }
            if (it.Reserved && it.ReserveLostWait && it.State == ItemState.Locked && now - it.ReserveLostSince >= _p.grip.lostWaitSeconds)
            {
                Unreserve(it);   // 戻らなかった
                return true;
            }
            if (it.GraceActive && it.State == ItemState.Held && now - it.GraceSince >= _p.grip.mGraceSeconds)
            {
                // 外れた理由が見失いなら、落とさずその場でそっと置く（Placing）。開いたなら落とす
                if (it.GraceLost) BeginPlace(it, now);
                else Drop(it);
                return true;
            }
            return false;
        }

        // ---- 状態表への追いつき（6.2） ----

        /// <summary>ホストの状態表で、持ち主が自分で申請IDも合っている M。取りこぼした ThGrabbed・ThLifted に追いつく</summary>
        void ReconcileOwnM(ClientItem it, ThItemEntry e, ItemState hostState, double now)
        {
            if (hostState == ItemState.Locked)
            {
                if (it.ReqPending) ApproveReservation(it, now);   // ThGrabbed を取りこぼした（LiftQueued ならそのまま持ち上げへ）
            }
            else if (hostState == ItemState.Held)
            {
                if (it.ReqPending) ApproveReservation(it, now);   // ThGrabbed も取りこぼした（ここでは持ち上げの申請までは進む）
                if (it.LiftPending) ApproveLift(it, e.value, false);   // ThLifted を取りこぼした
            }
        }

        /// <summary>M の状態（予約・持ち上げ・猶予・見失い待ち）を消す。申請が終わった・取りやめた・手放したとき</summary>
        static void ClearTwoHand(ClientItem it)
        {
            it.LockHandle = -1;
            it.ReserveHand = -1;
            it.Reserved = false;
            it.LiftPending = false;
            it.LiftQueued = false;
            it.SecondHand = -1;
            it.SecondHandle = -1;
            it.ReserveLostWait = false;
            it.GraceActive = false;
            it.GraceHand = -1;
            it.GraceLost = false;
            ResetTracking(it);
        }
    }
}
