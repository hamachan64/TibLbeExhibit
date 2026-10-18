using System;
using UnityEngine;

namespace STYLY.TIB_Template
{
    /// <summary>
    /// 指の曲がり具合（0=伸びている, 1=完全に曲げている）を関節の位置から求める純粋な計算。
    /// XR Hands の型には依存しない（EditMode テストしやすくするため）。
    /// 1本の指は「付け根の骨(Metacarpal)・Proximal・Intermediate・Distal・Tip」の5点の位置で表す。
    /// 各関節での骨の向きの折れ曲がり角の合計を、伸びた時(OpenAngle)と握った時(FistAngle)で 0〜1 に正規化する。
    /// 位置だけを使うので、手の位置・向き・座標系に左右されない。
    /// （Issue #36 の試作 FingerCurl と同じ計算。本番側に写した物）
    /// </summary>
    public static class GripStrength
    {
        public const float Invalid = -1f;
        public const int JointsPerFinger = 5;

        /// <summary>伸ばした指の折れ曲がり角の合計(度)。これ以下は 0。</summary>
        public const float OpenAngle = 20f;
        /// <summary>握った指の折れ曲がり角の合計(度)。これ以上は 1。</summary>
        public const float FistAngle = 180f;

        const float MinSegmentLength = 1e-5f;

        /// <summary>1本の指の曲がり具合。関節が足りない・骨の長さが0・NaN のときは Invalid(-1)。</summary>
        public static float ComputeFinger(ReadOnlySpan<Vector3> joints)
        {
            if (joints.Length < JointsPerFinger) return Invalid;

            float total = 0f;
            Vector3 prev = default;
            for (int i = 0; i < JointsPerFinger - 1; i++)
            {
                var d = joints[i + 1] - joints[i];
                if (!IsFinite(d) || d.sqrMagnitude < MinSegmentLength * MinSegmentLength) return Invalid;
                if (i > 0) total += Vector3.Angle(prev, d);
                prev = d;
            }

            return Mathf.Clamp01((total - OpenAngle) / (FistAngle - OpenAngle));
        }

        /// <summary>人差し指〜小指の曲がり具合の平均＝握りの強さ。どれか1本でも無効なら false。</summary>
        public static bool TryCompute(
            ReadOnlySpan<Vector3> index, ReadOnlySpan<Vector3> middle,
            ReadOnlySpan<Vector3> ring, ReadOnlySpan<Vector3> little, out float grip)
        {
            grip = Invalid;
            float a = ComputeFinger(index), b = ComputeFinger(middle), c = ComputeFinger(ring), d = ComputeFinger(little);
            if (a < 0f || b < 0f || c < 0f || d < 0f) return false;
            grip = (a + b + c + d) * 0.25f;
            return true;
        }

        static bool IsFinite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}
