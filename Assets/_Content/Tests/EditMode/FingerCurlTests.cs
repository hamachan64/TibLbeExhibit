using NUnit.Framework;
using STYLY.TIB_Template.TreasureHaul.Prototype;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    public class FingerCurlTests
    {
        // 指を +Z 方向に伸ばした5点（骨の長さ 3cm）
        static Vector3[] Straight() => new[]
        {
            new Vector3(0, 0, 0), new Vector3(0, 0, 0.03f), new Vector3(0, 0, 0.06f),
            new Vector3(0, 0, 0.09f), new Vector3(0, 0, 0.12f),
        };

        // 各関節で angleDeg ずつ（+Y 側へ）曲げた5点
        static Vector3[] Bent(float angleDeg)
        {
            var pts = new Vector3[5];
            Vector3 pos = Vector3.zero;
            float dir = 0f; // Z 軸から Y 軸方向への角度
            pts[0] = pos;
            for (int i = 1; i < 5; i++)
            {
                if (i > 1) dir += angleDeg;
                var d = new Vector3(0, Mathf.Sin(dir * Mathf.Deg2Rad), Mathf.Cos(dir * Mathf.Deg2Rad)) * 0.03f;
                pos += d;
                pts[i] = pos;
            }
            return pts;
        }

        [Test]
        public void StraightFinger_IsLow()
        {
            Assert.Less(FingerCurl.ComputeFinger(Straight()), 0.1f);
        }

        [Test]
        public void BentFinger_IsHigh()
        {
            Assert.Greater(FingerCurl.ComputeFinger(Bent(70f)), 0.7f);
        }

        [Test]
        public void MoreBend_GivesLargerValue()
        {
            Assert.Less(FingerCurl.ComputeFinger(Bent(20f)), FingerCurl.ComputeFinger(Bent(45f)));
        }

        [Test]
        public void Result_IsIndependentOfHandPose()
        {
            var rot = Quaternion.Euler(30, 100, -50);
            var pts = Bent(50f);
            float expected = FingerCurl.ComputeFinger(pts);
            for (int i = 0; i < pts.Length; i++) pts[i] = rot * pts[i] + new Vector3(1, 2, 3);
            Assert.AreEqual(expected, FingerCurl.ComputeFinger(pts), 1e-3f);
        }

        [Test]
        public void MissingJoints_IsInvalid()
        {
            Assert.AreEqual(FingerCurl.Invalid, FingerCurl.ComputeFinger(new Vector3[4]));
            Assert.AreEqual(FingerCurl.Invalid, FingerCurl.ComputeFinger(System.Array.Empty<Vector3>()));
        }

        [Test]
        public void ZeroLengthSegment_OrNaN_IsInvalid()
        {
            Assert.AreEqual(FingerCurl.Invalid, FingerCurl.ComputeFinger(new Vector3[5]));
            var pts = Straight();
            pts[2] = new Vector3(float.NaN, 0, 0);
            Assert.AreEqual(FingerCurl.Invalid, FingerCurl.ComputeFinger(pts));
        }

        [Test]
        public void Grip_IsAverageOfFourFingers()
        {
            Assert.IsTrue(FingerCurl.TryComputeGrip(Straight(), Straight(), Straight(), Straight(), out var open));
            Assert.Less(open, 0.3f);
            Assert.IsTrue(FingerCurl.TryComputeGrip(Bent(70f), Bent(70f), Bent(70f), Bent(70f), out var fist));
            Assert.Greater(fist, 0.7f);
            Assert.IsTrue(FingerCurl.TryComputeGrip(Straight(), Straight(), Bent(70f), Bent(70f), out var half));
            Assert.AreEqual((open + fist) * 0.5f, half, 0.05f);
        }

        [Test]
        public void Grip_WithAnyInvalidFinger_IsInvalid()
        {
            Assert.IsFalse(FingerCurl.TryComputeGrip(Straight(), Straight(), Straight(), new Vector3[2], out var grip));
            Assert.AreEqual(FingerCurl.Invalid, grip);
        }
    }
}
