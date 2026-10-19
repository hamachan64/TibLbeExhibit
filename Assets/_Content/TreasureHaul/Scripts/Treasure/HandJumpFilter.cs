using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>手の飛びの判定の結果</summary>
    public readonly struct HandJumpResult
    {
        /// <summary>使う位置（飛んだフレームは前の位置のまま）</summary>
        public readonly Vector3 Position;
        /// <summary>このフレームで〔飛びの判定〕以上動いたか</summary>
        public readonly bool Jumped;
        public HandJumpResult(Vector3 position, bool jumped) { Position = position; Jumped = jumped; }
    }

    /// <summary>
    /// 手の目標の「飛び」の判定（設計書 4.3、F-DMG-3。純粋クラス。手ごとに 1 つ）。
    /// 手が見え続けている間の 1 フレームで〔飛びの判定〕以上動いたら飛びとし、そのフレームは前の位置のまま止める。
    /// 次のフレームからは新しい位置を使う（本当に手が動いたなら、追従の速度の上限で寄っていく）。
    /// 一瞬だけ飛んで戻った場合は戻りも飛びになるので、使う位置は動かない。
    /// 比べるのは前に見えたときの「生の位置」。手を見失っても覚えておく（〔見失いの猶予〕より短い見失いの後、
    /// 大きく離れた位置で戻ったときも、戻った最初のフレームで飛びと判定するため）。
    /// </summary>
    public sealed class HandJumpFilter
    {
        bool _has;
        Vector3 _prevRaw;
        Vector3 _used;

        public HandJumpResult Update(bool visible, Vector3 raw, float jumpDistance)
        {
            if (!visible)
            {
                return new HandJumpResult(_used, false);
            }
            if (!_has)
            {
                _has = true;
                _prevRaw = raw;
                _used = raw;
                return new HandJumpResult(raw, false);
            }
            bool jumped = (raw - _prevRaw).sqrMagnitude >= jumpDistance * jumpDistance;
            _prevRaw = raw;
            if (!jumped) _used = raw;
            return new HandJumpResult(_used, jumped);
        }

        public void Reset() { _has = false; }
    }
}
