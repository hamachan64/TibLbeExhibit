using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// ClientItemTable の見失い・手の飛び・そっと置く・持ち主の切断（#41）。設計書 4.2・4.3・5章。
    /// Hovering（見失い待ち）・Placing（そっと置く）は持ち主の端末だけの状態で、ホストの状態表は Held のまま。
    /// 他の端末へは ThPoses の exempt（免除中）と位置の送信で知らせる。純粋クラス。時刻・手の状態は引数。
    /// </summary>
    public sealed partial class ClientItemTable
    {
        /// <summary>見失い・補正・降ろす先の記録を消す（持ち主でなくなった・落とした・そっと置く前など）</summary>
        static void ResetTracking(ClientItem it)
        {
            it.CorrectionUntil = double.NegativeInfinity;
            it.PlaceTargetSet = false;
        }

        // ---- 見失い待ち（Hovering） ----

        /// <summary>持っている手を見失った: 落とさず、その場で止めて待つ（物理は止める。手は外さない）</summary>
        void BeginHover(ClientItem it, double now)
        {
            it.State = ItemState.Hovering;
            it.HoverSince = now;
            it.CorrectionUntil = double.NegativeInfinity;
            it.GraceActive = false;   // M: 猶予の間に残った手も見失った（猶予は消す）
            it.GraceHand = -1;
            it.GraceLost = false;
            it.HoverOpenSinceA = double.NaN; it.HoverOpenSinceB = double.NaN;
            Raise(it, ItemEventKind.Hovered);
        }

        /// <summary>
        /// S の見失い待ちの間、握っていた手の様子を毎フレーム渡す。
        /// 見えていて握りの強さが〔離す側のしきい値〕以上なら、すぐ持ち続ける。
        /// 未満の状態が〔離す判定〕の間見え続けたら（開いて戻った）そっと置く。戻った直後は指の関節が不安定なので、1 フレームでは決めない。
        /// 途中で見えなくなった・しきい値以上に戻ったら、続いた時間を数え直す。
        /// </summary>
        public void OnHoverObserved(int index, bool tracked, float strength, double now)
        {
            var it = Get(index);
            if (it == null || it.Kind != TreasureKind.S || it.State != ItemState.Hovering || !it.Mine || it.Gone || Frozen) return;
            if (!TrackOpen(ref it.HoverOpenSinceA, tracked, strength, now))
            {
                if (tracked && strength >= _p.grip.releaseThreshold) ResumeFromHover(it, now);
                return;
            }
            BeginPlace(it, now);
        }

        /// <summary>見えていて離す側のしきい値未満の状態が〔離す判定〕続いたら true。続きが途切れたら数え直す</summary>
        bool TrackOpen(ref double since, bool tracked, float strength, double now)
        {
            if (!tracked || strength >= _p.grip.releaseThreshold) { since = double.NaN; return false; }
            if (double.IsNaN(since)) since = now;
            return now - since >= _p.grip.releaseHoldSeconds;
        }

        /// <summary>
        /// M の見失い待ちの間、両手の様子を毎フレーム渡す（予約した手 = 1 本目、2 本目の手）。
        /// 両方が見えていて両方とも離す側のしきい値以上なら、すぐ両手で持ち続ける。
        /// どちらかが離す側のしきい値未満のまま〔離す判定〕の間見え続けたらそっと置く。片方だけ戻ったなら待つ。
        /// </summary>
        public void OnMHoverObserved(int index, bool reserveTracked, float reserveStrength, bool secondTracked, float secondStrength, double now)
        {
            var it = Get(index);
            if (it == null || it.Kind != TreasureKind.M || it.State != ItemState.Hovering || !it.Mine || it.Gone || Frozen) return;
            bool openA = TrackOpen(ref it.HoverOpenSinceA, reserveTracked, reserveStrength, now);
            bool openB = TrackOpen(ref it.HoverOpenSinceB, secondTracked, secondStrength, now);
            if (openA || openB) { BeginPlace(it, now); return; }
            float rel = _p.grip.releaseThreshold;
            if (reserveTracked && secondTracked && reserveStrength >= rel && secondStrength >= rel) ResumeFromHover(it, now);
        }

        void ResumeFromHover(ClientItem it, double now)
        {
            it.State = ItemState.Held;
            it.CorrectionUntil = now + _p.treasure.correctionGraceSeconds;
            Raise(it, ItemEventKind.HoverResumed);
        }

        // ---- 手の飛び・〔補正の無判定時間〕 ----

        /// <summary>持っている（承認済みで Held の）手で飛びが起きた: そのお宝の〔補正の無判定時間〕を始める（M は左右どちらの手でも）</summary>
        public void NoteJump(int index, double now)
        {
            var it = Get(index);
            if (it == null || !it.Mine || !it.Approved || it.State != ItemState.Held || it.Gone || Frozen) return;
            it.CorrectionUntil = System.Math.Max(it.CorrectionUntil, now + _p.treasure.correctionGraceSeconds);
        }

        // ---- 離れたら置く（F-GRAB-7） ----

        /// <summary>
        /// 手に合わせる点と手の目標が〔離れたら置く距離〕以上か。持っている（承認済みで Held の追従中。M は猶予の間を含む）ときだけ。
        /// 手元へ飛ぶ途中・〔補正の無判定時間〕・Hovering・Placing では判定しない。
        /// </summary>
        public static bool ShouldDetach(ClientItem it, float distance, bool flying, double now, float detachDistance) =>
            it != null && it.Mine && it.Approved && !it.Gone && it.State == ItemState.Held && !flying
            && now >= it.CorrectionUntil && distance >= detachDistance;

        /// <summary>離れすぎていたらそっと置く（Placing）。置いたら true</summary>
        public bool CheckDetach(int index, float distance, bool flying, double now)
        {
            var it = Get(index);
            if (Frozen || !ShouldDetach(it, distance, flying, now, _p.grip.detachDistance)) return false;
            BeginPlace(it, now);
            return true;
        }

        // ---- そっと置く（Placing） ----

        /// <summary>そっと置く: 手から外し（両手の目標・猶予も消す）、降ろす先は TreasureClient が決めて SetPlaceTarget で渡す</summary>
        void BeginPlace(ClientItem it, double now)
        {
            ClearTwoHand(it);
            it.State = ItemState.Placing;
            it.PlaceSince = now;
            it.PlaceTargetSet = false;
            Raise(it, ItemEventKind.PlaceStarted);
        }

        /// <summary>降ろす先（VenueRoot ローカル）を渡す。〔置く時間〕がたったらここへ置いたとして ThRest を送る</summary>
        public void SetPlaceTarget(int index, Vector3 pos, Quaternion rot)
        {
            var it = Get(index);
            if (it == null || it.State != ItemState.Placing) return;
            it.PlacePos = pos;
            it.PlaceRot = rot;
            it.PlaceTargetSet = true;
        }

        /// <summary>〔置く時間〕がたった: 降ろした位置に Resting として、落下が止まったときと同じく ThRest を送る（反映されるまで送り直す）</summary>
        void SettlePlace(ClientItem it, double now)
        {
            it.State = ItemState.Resting;
            if (it.PlaceTargetSet) { it.RestPos = it.PlacePos; it.RestRot = it.PlaceRot; }
            it.PendingRest = true;
            it.PendingGen = it.Gen;
            it.LastSendAt = now;
            SendRest(it);
        }

        // ---- 持ち主の切断など（ホストの ThForceRest。設計書 5章・3.3） ----

        /// <summary>
        /// ホストが降ろす先を決めてそっと置かせた（世代が今より新しいときだけ）。
        /// 自分が持っていたお宝なら手から外す（一瞬切れて戻った持ち主。申請・送り直しも消す）。補間はやめる。
        /// その後のホストの ThFreed・ThCollected は今までどおり処理する。
        /// </summary>
        public void OnForceRest(ThForceRestMsg m)
        {
            var it = m == null ? null : Get(m.item);
            if (it == null || it.Gone || Frozen) return;
            if (m.gen <= it.Gen) return;
            ApplyForceRest(it, m.gen, m.pos, m.rot);
        }

        void ApplyForceRest(ClientItem it, int gen, Vector3 pos, Quaternion rot)
        {
            it.Gen = gen;
            it.Mine = false; it.ReqPending = false; it.Approved = false; it.PendingRest = false; it.PendingCollect = false;
            ClearTwoHand(it);
            it.OwnerClientNo = -1;
            it.State = ItemState.Placing;
            it.RestPos = pos;
            it.RestRot = rot;
            it.Interp.Clear();
            Raise(it, ItemEventKind.ForceRested);
        }
    }
}
