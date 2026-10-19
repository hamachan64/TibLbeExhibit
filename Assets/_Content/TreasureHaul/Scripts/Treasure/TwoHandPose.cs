using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// M を両手で持つときの目標の位置・向き（純粋。設計書 4.3）。
    /// 位置は両手の中点。向きは、お宝の「予約した持ち手 → もう一方の持ち手」の向きを「予約した手 → もう一方の手」の向きに合わせ、
    /// お宝の上はできるだけ真上（両手を結ぶ線の回りにねじれない）にする。
    /// </summary>
    public static class TwoHandPose
    {
        /// <summary>これより近い 2 点では向きを決められない（m）</summary>
        const float MinSeparation = 1e-3f;

        /// <param name="reserveHand">予約した手の位置（ワールド）</param>
        /// <param name="secondHand">もう一方の手の位置（ワールド）</param>
        /// <param name="axisLocal">お宝のローカルで「予約した持ち手 → もう一方の持ち手」のベクトル</param>
        /// <param name="rotation">お宝の向き（ワールド）</param>
        /// <returns>決められたら true（手が重なっている・持ち手が重なっているときは false）</returns>
        public static bool TrySolve(Vector3 reserveHand, Vector3 secondHand, Vector3 axisLocal, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            var line = secondHand - reserveHand;
            if (line.sqrMagnitude < MinSeparation * MinSeparation || axisLocal.sqrMagnitude < MinSeparation * MinSeparation) return false;
            var d = line.normalized;
            var r0 = Quaternion.FromToRotation(axisLocal.normalized, d);
            // 線の回りのねじれを、お宝の上が真上に最も近くなるように直す
            var up = Vector3.ProjectOnPlane(r0 * Vector3.up, d);
            var want = Vector3.ProjectOnPlane(Vector3.up, d);
            if (up.sqrMagnitude < 1e-8f || want.sqrMagnitude < 1e-8f) { rotation = r0; return true; }   // 線が真上・真下: ねじれは決まらない
            float twist = Vector3.SignedAngle(up, want, d);
            rotation = Quaternion.AngleAxis(twist, d) * r0;
            return true;
        }

        public static Vector3 Midpoint(Vector3 a, Vector3 b) => (a + b) * 0.5f;
    }
}
