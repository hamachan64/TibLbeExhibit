using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 直近 N フレームの速度を覚えて平均を返す（純粋なリングバッファ。設計書 5章）。
    /// FixedUpdate ごとの実際の移動（位置の差 / dt）を入れ、衝突したときはその平均を衝撃の速さに使う。
    /// </summary>
    public sealed class VelocityHistory
    {
        Vector3[] _buf;
        int _count;
        int _head;

        public VelocityHistory(int capacity) { _buf = new Vector3[Mathf.Max(1, capacity)]; }

        public int Capacity => _buf.Length;
        public int Count => _count;

        /// <summary>覚えるフレーム数を変える（パラメータの更新）。覚えていた分は捨てる</summary>
        public void SetCapacity(int capacity)
        {
            capacity = Mathf.Max(1, capacity);
            if (capacity == _buf.Length) return;
            _buf = new Vector3[capacity];
            Reset();
        }

        public void Add(Vector3 velocity)
        {
            _buf[_head] = velocity;
            _head = (_head + 1) % _buf.Length;
            if (_count < _buf.Length) _count++;
        }

        /// <summary>覚えている分の平均。1 つも無ければ 0</summary>
        public Vector3 Average
        {
            get
            {
                if (_count == 0) return Vector3.zero;
                var sum = Vector3.zero;
                for (int i = 0; i < _count; i++) sum += _buf[i];
                return sum / _count;
            }
        }

        public void Reset() { _count = 0; _head = 0; }
    }
}
