namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 「中断」の確認（1 回目で待ち受け、〔数秒〕以内にもう一度押すと実行）。純粋クラス。時刻は引数。
    /// </summary>
    public sealed class AbortConfirmGate
    {
        double _armedAt = double.NegativeInfinity;
        float _window;

        /// <param name="windowSeconds">もう一度押せる時間（ThParams の abortConfirmSeconds）</param>
        public AbortConfirmGate(float windowSeconds) { _window = windowSeconds; }

        public void SetWindow(float windowSeconds) { _window = windowSeconds; }

        public bool IsArmed(double now) => now - _armedAt < _window;

        /// <summary>押した。確認済みで実行してよいなら true。1 回目は待ち受けにして false。</summary>
        public bool Press(double now)
        {
            if (IsArmed(now))
            {
                _armedAt = double.NegativeInfinity;
                return true;
            }
            _armedAt = now;
            return false;
        }

        public void Reset() { _armedAt = double.NegativeInfinity; }
    }
}
