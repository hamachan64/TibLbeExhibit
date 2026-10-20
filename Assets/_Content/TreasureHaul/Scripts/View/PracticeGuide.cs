using System;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>自分の練習用お宝の状態（案内の段階を決める入力）</summary>
    public enum PracticeItemState
    {
        /// <summary>自分の練習用お宝が無い（割り当てなし・次の回を待っている・Tutorial でない）</summary>
        None,
        /// <summary>置かれている（まだ持っていない・落とした）</summary>
        Resting,
        /// <summary>自分が持っている</summary>
        Held,
        /// <summary>持っていて、お宝が納品場所の範囲に入っている</summary>
        HeldInZone,
        /// <summary>回収された</summary>
        Collected,
    }

    /// <summary>練習の案内の段階（頭について来る面に出す）</summary>
    public enum PracticeStep
    {
        None,
        /// <summary>1. 持つ前: 持ち手に手を向けて、握ろう</summary>
        Grip,
        /// <summary>2. 持てた後: 納品場所まで運ぼう</summary>
        Carry,
        /// <summary>3. 納品場所の範囲に入ったら: 納品場所の上で手を開こう</summary>
        Release,
        /// <summary>4a. 回収の後の数秒: +150G！</summary>
        Success,
        /// <summary>4b. その後、本編が始まるまで: ぶつけると値段が下がる</summary>
        DamageNote,
    }

    /// <summary>
    /// 練習の案内の判断（純粋クラス。時刻は引数）。入力は自分の練習用お宝の状態と時刻。
    /// 回収を知った時刻から <see cref="SuccessSeconds"/> 秒は「+150G！」、その後は「ぶつけると値段が下がる」。
    /// </summary>
    public sealed class PracticeGuide
    {
        /// <summary>「+150G！」を大きく出す秒数（見た目の調整値）</summary>
        public const float SuccessSeconds = 2.5f;

        double _collectedAt = double.NaN;

        public void Reset() => _collectedAt = double.NaN;

        public PracticeStep Update(PracticeItemState state, double now)
        {
            if (state != PracticeItemState.Collected) _collectedAt = double.NaN;
            switch (state)
            {
                case PracticeItemState.Resting: return PracticeStep.Grip;
                case PracticeItemState.Held: return PracticeStep.Carry;
                case PracticeItemState.HeldInZone: return PracticeStep.Release;
                case PracticeItemState.Collected:
                    if (double.IsNaN(_collectedAt)) _collectedAt = now;
                    return now - _collectedAt < SuccessSeconds ? PracticeStep.Success : PracticeStep.DamageNote;
                default: return PracticeStep.None;
            }
        }

        /// <summary>
        /// 自分の練習用お宝の表から状態を出す。item = 自分の練習用お宝（無ければ null）、inZone = そのお宝の今の位置が納品場所の範囲内か。
        /// 持っている = 承認を受けた（Approved）自分のお宝の飛行・持ち・見失い待ち・そっと置く。承認前・落下中・置かれている・取りやめは Resting。
        /// </summary>
        public static PracticeItemState Classify(ClientItem item, bool inZone)
        {
            if (item == null) return PracticeItemState.None;
            if (item.Gone) return item.State == ItemState.Collected ? PracticeItemState.Collected : PracticeItemState.None;
            // 手を開いた後の落下中・回収待ち（ThCollected / ThRest の承認待ち）も「持っている」の続き: 範囲内なら段階 3 のまま、範囲外なら置かれた扱い
            if (item.Mine && (item.State == ItemState.Falling || item.PendingCollect || item.PendingRest))
                return inZone ? PracticeItemState.HeldInZone : PracticeItemState.Resting;
            bool holding = item.Mine && item.Approved && (item.State == ItemState.Flying || item.State == ItemState.Held
                || item.State == ItemState.Hovering || item.State == ItemState.Placing);
            if (!holding) return PracticeItemState.Resting;
            return inZone ? PracticeItemState.HeldInZone : PracticeItemState.Held;
        }
    }

    /// <summary>
    /// 10 秒のヒント（手のモデルの「開く⇄握る」）を出すかの判断（純粋クラス。時刻は引数）。
    /// 自分の練習用お宝がある参加端末で、Tutorial に入ってから〔ヒントまで〕たっても一度も持てていなければ出す。
    /// 一度持てたら（承認を受けたら）消し、その回は出さない。Tutorial を出たら消す。保留中も出し続ける。
    /// </summary>
    public sealed class PracticeHintJudge
    {
        long _roundId = -1;
        double _enteredAt = double.NaN;
        bool _grabbed;

        public void Reset() { _roundId = -1; _enteredAt = double.NaN; _grabbed = false; }

        /// <param name="inTutorial">今 Tutorial か</param>
        /// <param name="hasPracticeItem">自分の練習用お宝があるか（割り当てがあり、表に載っている）</param>
        /// <param name="item">自分の練習用お宝の状態（<see cref="PracticeGuide.Classify"/>）</param>
        /// <param name="hintSeconds">〔ヒントまで〕（practice.hintSeconds）</param>
        public bool ShouldShow(long roundId, bool inTutorial, bool hasPracticeItem, PracticeItemState item, float hintSeconds, double now)
        {
            if (!inTutorial || !hasPracticeItem)
            {
                Reset();
                return false;
            }
            if (roundId != _roundId)   // 新しい回の Tutorial
            {
                _roundId = roundId;
                _enteredAt = now;
                _grabbed = false;
            }
            if (item == PracticeItemState.Held || item == PracticeItemState.HeldInZone || item == PracticeItemState.Collected) _grabbed = true;
            return !_grabbed && now - _enteredAt >= hintSeconds;
        }
    }
}
