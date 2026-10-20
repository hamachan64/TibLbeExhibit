namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 落下したお宝が静止したかの判定（純粋。時刻は引数）。速さが小さい状態が〔止まったとみなす時間〕続いたら true。
    /// 速さのしきい値は物理演算の揺れを無視するための定数（ゲームの数値ではない）。
    /// </summary>
    public sealed class RestDetector
    {
        public const float StillSpeed = 0.05f;
        public const float StillAngularSpeed = 0.5f;

        double _stillSince = double.NaN;

        public bool Update(double now, float speed, float angularSpeed, float stopSeconds)
        {
            if (speed >= StillSpeed || angularSpeed >= StillAngularSpeed)
            {
                _stillSince = double.NaN;
                return false;
            }
            if (double.IsNaN(_stillSince)) _stillSince = now;
            return now - _stillSince >= stopSeconds;
        }

        public void Reset() => _stillSince = double.NaN;
    }
}
