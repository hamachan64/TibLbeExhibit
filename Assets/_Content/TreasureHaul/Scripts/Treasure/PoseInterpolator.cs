using System;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 他の端末が動かしているお宝の位置の補間（純粋。設計書 6章）。
    /// 受信した時刻（呼び出し側の時計）と位置を並べて保持し、〔補間の遅れ〕だけ過去の時刻を、2 つの位置の間で補間して返す。
    /// 遅れて届いて最新のサンプルより先の時刻を求められたら、直近 2 点の速度で最大 maxExtrapolation 秒だけ先読みし、それ以上は止める。
    /// </summary>
    public sealed class PoseInterpolator
    {
        struct Sample { public double Time; public Vector3 Pos; public Quaternion Rot; }

        const int Capacity = 16;
        readonly Sample[] _buf = new Sample[Capacity];
        int _count;

        public int Count => _count;
        public bool HasSamples => _count > 0;

        public void Clear() { _count = 0; }

        /// <summary>サンプルを足す。時刻は単調増加（同じか古い時刻は捨てる）</summary>
        public void Add(double time, Vector3 pos, Quaternion rot)
        {
            if (_count > 0 && time <= _buf[_count - 1].Time) return;
            if (_count == Capacity)
            {
                Array.Copy(_buf, 1, _buf, 0, Capacity - 1);
                _count--;
            }
            _buf[_count++] = new Sample { Time = time, Pos = pos, Rot = rot };
        }

        /// <summary>now の時点で描く位置。サンプルが無ければ false</summary>
        public bool TrySample(double now, float delay, float maxExtrapolation, out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = Quaternion.identity;
            if (_count == 0) return false;
            double t = now - delay;
            if (_count == 1 || t <= _buf[0].Time)
            {
                pos = _buf[0].Pos; rot = _buf[0].Rot;
                return true;
            }
            var last = _buf[_count - 1];
            if (t >= last.Time)
            {
                var prev = _buf[_count - 2];
                double dt = last.Time - prev.Time;
                double ahead = Math.Min(t - last.Time, maxExtrapolation);
                if (dt <= 1e-6 || ahead <= 0) { pos = last.Pos; rot = last.Rot; return true; }
                pos = last.Pos + (last.Pos - prev.Pos) * (float)(ahead / dt);
                rot = last.Rot;
                return true;
            }
            for (int i = _count - 2; i >= 0; i--)
            {
                if (_buf[i].Time <= t)
                {
                    var a = _buf[i]; var b = _buf[i + 1];
                    float k = (float)((t - a.Time) / (b.Time - a.Time));
                    pos = Vector3.Lerp(a.Pos, b.Pos, k);
                    rot = Quaternion.Slerp(a.Rot, b.Rot, k);
                    return true;
                }
            }
            pos = _buf[0].Pos; rot = _buf[0].Rot;
            return true;
        }
    }
}
