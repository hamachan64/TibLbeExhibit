using System;
using System.Collections.Generic;

namespace STYLY.TIB_Template.TreasureHaul.Prototype
{
    /// <summary>試作(S-2/S-3): 計測値の計算（純ロジック。EditMode テストあり）。</summary>
    public static class ProtoNetStats
    {
        /// <summary>周期 period 秒の円運動で、時刻 u(秒) の位相（ラジアン、0〜2π）。送り手と受け手が同じ式を使うので、位相の差が遅れになる。</summary>
        public static double ExpectedPhase(double u, double period)
        {
            double m = u % period;
            if (m < 0) m += period;
            return 2.0 * Math.PI * m / period;
        }

        /// <summary>期待する位相と、相手の画面で観測した位相から、遅れ（秒）を求める。正 = 相手の画面の方が遅れている。</summary>
        public static double LagSeconds(double expectedPhase, double observedPhase, double period)
        {
            double d = expectedPhase - observedPhase;
            while (d > Math.PI) d -= 2.0 * Math.PI;
            while (d <= -Math.PI) d += 2.0 * Math.PI;
            return d / (2.0 * Math.PI) * period;
        }

        public static double Percentile(IReadOnlyList<double> values, double p)
        {
            if (values == null || values.Count == 0) return double.NaN;
            var s = new List<double>(values);
            s.Sort();
            double idx = Math.Clamp(p, 0.0, 1.0) * (s.Count - 1);
            int lo = (int)Math.Floor(idx);
            int hi = (int)Math.Ceiling(idx);
            return s[lo] + (s[hi] - s[lo]) * (idx - lo);
        }

        public static double Mean(IReadOnlyList<double> values)
        {
            if (values == null || values.Count == 0) return double.NaN;
            double sum = 0;
            for (int i = 0; i < values.Count; i++) sum += values[i];
            return sum / values.Count;
        }

        public static double StdDev(IReadOnlyList<double> values)
        {
            if (values == null || values.Count < 2) return double.NaN;
            double m = Mean(values), acc = 0;
            for (int i = 0; i < values.Count; i++) acc += (values[i] - m) * (values[i] - m);
            return Math.Sqrt(acc / values.Count);
        }

        /// <summary>FNV-1a。受け取った文字列が送った文字列と同じかを確かめる用。</summary>
        public static uint Fnv1a(string s)
        {
            uint h = 2166136261u;
            if (s == null) return h;
            for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619u; }
            return h;
        }
    }
}
