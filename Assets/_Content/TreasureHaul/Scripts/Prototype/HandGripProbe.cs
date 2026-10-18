using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace STYLY.TIB_Template.TreasureHaul.Prototype
{
    /// <summary>
    /// 試作: XRHandSubsystem の関節から、左右の手の握りの強さ(0〜1)を出してログとワールド空間のテキストに表示する。
    /// 実機(#49)でも、頭につけたまま読めるようにワールド空間のテキストにも出す。
    /// </summary>
    public sealed class HandGripProbe : MonoBehaviour
    {
        [SerializeField] TMP_Text _leftText;
        [SerializeField] TMP_Text _rightText;
        [SerializeField, Min(0.05f)] float _logInterval = 0.5f;

        static readonly XRHandJointID[][] s_fingers =
        {
            new[] { XRHandJointID.IndexMetacarpal, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip },
            new[] { XRHandJointID.MiddleMetacarpal, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip },
            new[] { XRHandJointID.RingMetacarpal, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip },
            new[] { XRHandJointID.LittleMetacarpal, XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, XRHandJointID.LittleTip },
        };

        readonly Vector3[][] _buf = { new Vector3[5], new Vector3[5], new Vector3[5], new Vector3[5] };
        readonly List<XRHandSubsystem> _subsystems = new List<XRHandSubsystem>();
        XRHandSubsystem _subsystem;
        float _nextLog;
        string _lastLeftText, _lastRightText;

        /// <summary>直近の握りの強さ。取れなかったとき FingerCurl.Invalid。</summary>
        public float LeftGrip { get; private set; } = FingerCurl.Invalid;
        public float RightGrip { get; private set; } = FingerCurl.Invalid;

        void Update()
        {
            if (_subsystem == null || !_subsystem.running)
            {
                _subsystem = null;
                SubsystemManager.GetSubsystems(_subsystems);
                foreach (var s in _subsystems)
                {
                    if (s.running) { _subsystem = s; break; }
                }
            }

            string l, r;
            if (_subsystem == null)
            {
                LeftGrip = RightGrip = FingerCurl.Invalid;
                l = r = "no XRHandSubsystem";
            }
            else
            {
                l = Measure(_subsystem.leftHand, out var lg); LeftGrip = lg;
                r = Measure(_subsystem.rightHand, out var rg); RightGrip = rg;
            }

            // 値が変わったときだけ書き換える（TMP の再生成を避ける。実機 #49 用）
            if (_leftText != null && l != _lastLeftText) { _lastLeftText = l; _leftText.text = "L " + l; }
            if (_rightText != null && r != _lastRightText) { _lastRightText = r; _rightText.text = "R " + r; }

            if (Time.unscaledTime >= _nextLog)
            {
                _nextLog = Time.unscaledTime + _logInterval;
                Debug.Log($"[GripProbe] L={l} R={r}");
            }
        }

        string Measure(XRHand hand, out float grip)
        {
            grip = FingerCurl.Invalid;
            if (!hand.isTracked) return "untracked";
            for (int f = 0; f < 4; f++)
            {
                for (int j = 0; j < 5; j++)
                {
                    if (!hand.GetJoint(s_fingers[f][j]).TryGetPose(out var pose)) return "joint missing";
                    _buf[f][j] = pose.position;
                }
            }
            if (!FingerCurl.TryComputeGrip(_buf[0], _buf[1], _buf[2], _buf[3], out grip)) return "invalid";
            return grip.ToString("F2");
        }
    }
}
