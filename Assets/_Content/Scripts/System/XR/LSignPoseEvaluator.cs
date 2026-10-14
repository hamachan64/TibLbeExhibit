using UnityEngine;

namespace STYLY.TIB_Template
{
    /// <summary>
    /// 「Lサイン（フィンガーガン）」ポーズの純粋判定ロジック。
    ///
    /// XRHandJointID など XR Hands 固有の型に依存せず、関節のワールド座標（Vector3）のみで
    /// 判定できるように切り出している。これにより BinderSummonGestureDetector（MonoBehaviour）を
    /// 経由せずに EditMode テストで判定条件そのものを検証できる。
    ///
    /// 判定条件:
    ///   - 人差し指・親指が伸びている（隣接セグメントの向きがほぼ同方向 = 内積が高い）
    ///   - 中指・薬指・小指が握られている（隣接セグメントが大きく折れ曲がる = 内積が低い）
    ///   - 親指と人差し指が十分に開いている（指先の距離・伸び方向の内積の両方で判定し、
    ///     ピンチ等の指先を近づける他ジェスチャーとの誤爆を防ぐ）
    /// </summary>
    public static class LSignPoseEvaluator
    {
        /// <summary>
        /// 指の伸展度を隣接セグメント方向の内積で評価する。
        /// 1.0に近いほどまっすぐ伸びている（同一方向）、-1.0に近いほど大きく折れ曲がっている。
        /// セグメント長が極端に短い（関節取得異常）場合は 0（中立）を返す。
        /// </summary>
        public static float ComputeExtension(Vector3 baseJoint, Vector3 midJoint, Vector3 tipJoint)
        {
            var seg1 = midJoint - baseJoint;
            var seg2 = tipJoint - midJoint;
            if (seg1.sqrMagnitude < 1e-8f || seg2.sqrMagnitude < 1e-8f)
            {
                return 0f;
            }
            return Vector3.Dot(seg1.normalized, seg2.normalized);
        }

        /// <summary>1本の指を構成する3関節（付け根→中間→指先）のワールド座標</summary>
        public readonly struct FingerJoints
        {
            public readonly Vector3 Base;
            public readonly Vector3 Mid;
            public readonly Vector3 Tip;

            public FingerJoints(Vector3 baseJoint, Vector3 midJoint, Vector3 tipJoint)
            {
                Base = baseJoint;
                Mid = midJoint;
                Tip = tipJoint;
            }
        }

        /// <summary>判定に必要な5本指の関節座標一式</summary>
        public struct Input
        {
            /// <summary>親指: Proximal→Distal→Tip</summary>
            public FingerJoints Thumb;
            /// <summary>人差し指: Proximal(MCP)→Intermediate(PIP)→Tip</summary>
            public FingerJoints Index;
            /// <summary>中指: Proximal(MCP)→Intermediate(PIP)→Tip</summary>
            public FingerJoints Middle;
            /// <summary>薬指: Proximal(MCP)→Intermediate(PIP)→Tip</summary>
            public FingerJoints Ring;
            /// <summary>小指: Proximal(MCP)→Intermediate(PIP)→Tip</summary>
            public FingerJoints Little;
        }

        /// <summary>判定しきい値一式（成立判定・解除判定でそれぞれ別の値を渡してヒステリシスを作る）</summary>
        public struct Thresholds
        {
            /// <summary>この内積以上で指が「伸びている」とみなす</summary>
            public float ExtendedDot;
            /// <summary>この内積以下で指が「握られている」とみなす</summary>
            public float CurledDot;
            /// <summary>親指・人差し指の指先がこの距離（m）以上離れていること</summary>
            public float ThumbIndexMinSpread;
            /// <summary>親指・人差し指の伸び方向（付け根→指先）の内積がこの値以下であること（十分に開いているか）</summary>
            public float ThumbIndexMaxDirectionDot;
        }

        /// <summary>
        /// 与えられた関節座標がLサインポーズの条件を満たすかどうかを判定する。
        /// </summary>
        public static bool Evaluate(in Input input, in Thresholds thresholds)
        {
            var indexExtension = ComputeExtension(input.Index.Base, input.Index.Mid, input.Index.Tip);
            var thumbExtension = ComputeExtension(input.Thumb.Base, input.Thumb.Mid, input.Thumb.Tip);
            if (indexExtension < thresholds.ExtendedDot || thumbExtension < thresholds.ExtendedDot)
            {
                return false;
            }

            var middleExtension = ComputeExtension(input.Middle.Base, input.Middle.Mid, input.Middle.Tip);
            var ringExtension = ComputeExtension(input.Ring.Base, input.Ring.Mid, input.Ring.Tip);
            var littleExtension = ComputeExtension(input.Little.Base, input.Little.Mid, input.Little.Tip);
            if (middleExtension > thresholds.CurledDot
                || ringExtension > thresholds.CurledDot
                || littleExtension > thresholds.CurledDot)
            {
                return false;
            }

            // 親指・人差し指が十分に開いていること（距離・方向の両方）を確認し、
            // 指先を近づけるピンチ等の他ジェスチャーとの誤爆を防ぐ。
            var spread = Vector3.Distance(input.Thumb.Tip, input.Index.Tip);
            if (spread < thresholds.ThumbIndexMinSpread)
            {
                return false;
            }

            var thumbDir = input.Thumb.Tip - input.Thumb.Base;
            var indexDir = input.Index.Tip - input.Index.Base;
            if (thumbDir.sqrMagnitude < 1e-8f || indexDir.sqrMagnitude < 1e-8f)
            {
                return false;
            }
            var directionDot = Vector3.Dot(thumbDir.normalized, indexDir.normalized);
            if (directionDot > thresholds.ThumbIndexMaxDirectionDot)
            {
                return false;
            }

            return true;
        }
    }
}
