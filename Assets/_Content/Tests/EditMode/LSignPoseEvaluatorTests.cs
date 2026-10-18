using NUnit.Framework;
using UnityEngine;

namespace STYLY.TIB_Template.Tests
{
    /// <summary>
    /// LSignPoseEvaluator（Lサイン＝フィンガーガンポーズの純粋判定ロジック）のEditModeテスト。
    /// BinderSummonGestureDetector が実際に使うのと同じデフォルトしきい値
    /// （extendedDotThreshold=0.7, curledDotThreshold=0.3, thumbIndexMinSpread=0.06,
    ///   thumbIndexMaxDirectionDot=0.5）で成立・不成立の境界を検証する。
    /// </summary>
    public class LSignPoseEvaluatorTests
    {
        private static readonly LSignPoseEvaluator.Thresholds DefaultThresholds = new()
        {
            ExtendedDot = 0.7f,
            CurledDot = 0.3f,
            ThumbIndexMinSpread = 0.06f,
            ThumbIndexMaxDirectionDot = 0.5f,
        };

        // ---- ComputeExtension（隣接セグメントの内積）の境界値 ----

        [Test]
        public void ComputeExtension_StraightFinger_ReturnsCloseToOne()
        {
            var extension = LSignPoseEvaluator.ComputeExtension(
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0.03f, 0f),
                new Vector3(0f, 0.06f, 0f));

            Assert.That(extension, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void ComputeExtension_RightAngleBend_ReturnsCloseToZero()
        {
            var extension = LSignPoseEvaluator.ComputeExtension(
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0.03f, 0f),
                new Vector3(0.03f, 0.03f, 0f));

            Assert.That(extension, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void ComputeExtension_FoldedBack_ReturnsCloseToMinusOne()
        {
            var extension = LSignPoseEvaluator.ComputeExtension(
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0.03f, 0f),
                new Vector3(0f, 0f, 0f));

            Assert.That(extension, Is.EqualTo(-1f).Within(0.0001f));
        }

        [Test]
        public void ComputeExtension_DegenerateZeroLengthSegment_ReturnsZero()
        {
            // 関節取得異常等でセグメント長がほぼ0の場合、内積計算をせず中立値を返す
            var extension = LSignPoseEvaluator.ComputeExtension(
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0.06f, 0f));

            Assert.That(extension, Is.EqualTo(0f));
        }

        // ---- Evaluate（Lサイン全体判定） ----

        /// <summary>
        /// 典型的なLサイン: 親指=-X方向に伸展、人差し指=+Y方向に伸展、中指・薬指・小指は握られている。
        /// </summary>
        private static LSignPoseEvaluator.Input CreateCanonicalLSign()
        {
            return new LSignPoseEvaluator.Input
            {
                Thumb = new LSignPoseEvaluator.FingerJoints(
                    new Vector3(0f, 0f, 0f),
                    new Vector3(-0.025f, 0f, 0f),
                    new Vector3(-0.05f, 0f, 0f)),
                Index = new LSignPoseEvaluator.FingerJoints(
                    new Vector3(0.01f, 0f, 0f),
                    new Vector3(0.01f, 0.03f, 0f),
                    new Vector3(0.01f, 0.06f, 0f)),
                Middle = CurledFinger(0.02f),
                Ring = CurledFinger(0.03f),
                Little = CurledFinger(0.04f),
            };
        }

        /// <summary>握られた指（付け根から一度伸びて、指先が付け根の高さまで折り返す）</summary>
        private static LSignPoseEvaluator.FingerJoints CurledFinger(float x)
        {
            return new LSignPoseEvaluator.FingerJoints(
                new Vector3(x, 0f, 0f),
                new Vector3(x, 0.02f, 0f),
                new Vector3(x, 0f, 0f));
        }

        /// <summary>伸びた指（付け根から指先までまっすぐ）</summary>
        private static LSignPoseEvaluator.FingerJoints ExtendedFinger(float x)
        {
            return new LSignPoseEvaluator.FingerJoints(
                new Vector3(x, 0f, 0f),
                new Vector3(x, 0.03f, 0f),
                new Vector3(x, 0.06f, 0f));
        }

        [Test]
        public void Evaluate_CanonicalLSign_ReturnsTrue()
        {
            var input = CreateCanonicalLSign();

            Assert.That(LSignPoseEvaluator.Evaluate(input, DefaultThresholds), Is.True);
        }

        [Test]
        public void Evaluate_MiddleFingerAlsoExtended_ReturnsFalse()
        {
            var input = CreateCanonicalLSign();
            input.Middle = ExtendedFinger(0.02f);

            Assert.That(LSignPoseEvaluator.Evaluate(input, DefaultThresholds), Is.False);
        }

        [Test]
        public void Evaluate_AllFingersCurled_Fist_ReturnsFalse()
        {
            var input = CreateCanonicalLSign();
            input.Thumb = CurledFinger(-0.02f);
            input.Index = CurledFinger(0.01f);

            Assert.That(LSignPoseEvaluator.Evaluate(input, DefaultThresholds), Is.False);
        }

        [Test]
        public void Evaluate_AllFingersExtended_OpenHand_ReturnsFalse()
        {
            var input = CreateCanonicalLSign();
            input.Middle = ExtendedFinger(0.02f);
            input.Ring = ExtendedFinger(0.03f);
            input.Little = ExtendedFinger(0.04f);

            Assert.That(LSignPoseEvaluator.Evaluate(input, DefaultThresholds), Is.False);
        }

        [Test]
        public void Evaluate_ThumbAndIndexParallelAndClose_PinchLike_ReturnsFalse()
        {
            // 親指・人差し指がともに+Y方向へ平行に伸び、指先も接近している
            // （ピンチに近い形）場合は、間隔・方向どちらの誤爆防止条件にも引っかかり不成立になる。
            var input = CreateCanonicalLSign();
            input.Thumb = new LSignPoseEvaluator.FingerJoints(
                new Vector3(0.01f, 0f, 0f),
                new Vector3(0.01f, 0.025f, 0f),
                new Vector3(0.01f, 0.05f, 0f));

            Assert.That(LSignPoseEvaluator.Evaluate(input, DefaultThresholds), Is.False);
        }

        [Test]
        public void Evaluate_ThumbIndexTooClose_ReturnsFalse()
        {
            // 方向は十分開いているが、指先同士の距離が近すぎる場合も不成立にする
            var input = CreateCanonicalLSign();
            input.Index = new LSignPoseEvaluator.FingerJoints(
                new Vector3(-0.045f, 0f, 0f),
                new Vector3(-0.045f, 0.015f, 0f),
                new Vector3(-0.045f, 0.03f, 0f));

            Assert.That(LSignPoseEvaluator.Evaluate(input, DefaultThresholds), Is.False);
        }
    }
}
