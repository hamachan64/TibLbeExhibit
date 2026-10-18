using System;

namespace STYLY.TIB_Template
{
    /// <summary>握りが解除された理由</summary>
    public enum GripReleaseReason
    {
        /// <summary>手を開いた</summary>
        Opened = 0,
        /// <summary>手を見失った</summary>
        Lost = 1,
    }

    public enum GripEvent
    {
        None = 0,
        Gripped = 1,
        Released = 2,
    }

    /// <summary>
    /// 握る・開くの判定（純粋。時刻は引数）。設計書 4.4。
    /// 握りの強さが GripThreshold 以上で「握った」。握った状態から、強さが ReleaseThreshold を
    /// ReleaseHoldSeconds の間続けて下回ったら「開いた」（ヒステリシス＋継続時間）。少し下回って戻ったら開いたことにしない。
    /// 手を LostGraceSeconds 続けて見失ったら、理由=見失いで解除する。
    /// しきい値・時間は呼び出し側（TreasureHaul の ThParams.grip）から渡す。
    /// </summary>
    public sealed class GripStateMachine
    {
        public float GripThreshold { get; set; } = 0.7f;
        public float ReleaseThreshold { get; set; } = 0.4f;
        public float ReleaseHoldSeconds { get; set; } = 0.25f;
        public float LostGraceSeconds { get; set; } = 0.1f;

        public bool IsGripping { get; private set; }
        /// <summary>直近の握りの強さ（0〜1）。見失い中は最後の値のまま</summary>
        public float Strength { get; private set; }
        public bool IsTracked { get; private set; }
        public GripReleaseReason LastReleaseReason { get; private set; }

        double _belowSince = double.NaN;
        double _lostSince = double.NaN;

        /// <summary>毎フレーム呼ぶ。tracked=false のとき strength は使わない。</summary>
        public GripEvent Update(double now, bool tracked, float strength)
        {
            IsTracked = tracked;
            if (!tracked)
            {
                _belowSince = double.NaN;
                if (!IsGripping) { _lostSince = double.NaN; return GripEvent.None; }
                if (double.IsNaN(_lostSince)) _lostSince = now;
                if (now - _lostSince >= LostGraceSeconds)
                {
                    return Release(GripReleaseReason.Lost);
                }
                return GripEvent.None;
            }

            _lostSince = double.NaN;
            Strength = strength;
            if (!IsGripping)
            {
                if (strength >= GripThreshold)
                {
                    IsGripping = true;
                    _belowSince = double.NaN;
                    return GripEvent.Gripped;
                }
                return GripEvent.None;
            }

            if (strength < ReleaseThreshold)
            {
                if (double.IsNaN(_belowSince)) _belowSince = now;
                if (now - _belowSince >= ReleaseHoldSeconds) return Release(GripReleaseReason.Opened);
            }
            else
            {
                _belowSince = double.NaN;
            }
            return GripEvent.None;
        }

        GripEvent Release(GripReleaseReason reason)
        {
            IsGripping = false;
            LastReleaseReason = reason;
            _belowSince = double.NaN;
            _lostSince = double.NaN;
            return GripEvent.Released;
        }

        public void Reset()
        {
            IsGripping = false;
            Strength = 0f;
            IsTracked = false;
            _belowSince = double.NaN;
            _lostSince = double.NaN;
        }
    }
}
