using System.Collections.Generic;
using R3;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace STYLY.TIB_Template
{
    /// <summary>
    /// 握る・開くの判定（XR Hands から関節を読む MonoBehaviour）。判定の中身は <see cref="GripStateMachine"/>。
    /// 握りの強さ＝人差し指〜小指の曲がり具合の平均（<see cref="GripStrength"/>）。
    /// 前作用の <see cref="HandPinchTracker"/> とは別物（本作ではつまむ判定を使わない）。
    /// しきい値・時間は <see cref="Configure"/> で渡す（Core は TreasureHaul を参照できない）。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class HandGripTracker : MonoBehaviour
    {
        [SerializeField] Handedness _handedness = Handedness.Right;

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

        readonly GripStateMachine _machine = new GripStateMachine();
        readonly ReactiveProperty<bool> _isGripping = new ReactiveProperty<bool>(false);
        readonly ReactiveProperty<float> _strength = new ReactiveProperty<float>(0f);
        readonly ReactiveProperty<bool> _isTracked = new ReactiveProperty<bool>(false);
        readonly Subject<GripReleaseReason> _released = new Subject<GripReleaseReason>();

        public Handedness Handedness { get => _handedness; set => _handedness = value; }
        public ReadOnlyReactiveProperty<bool> IsGripping => _isGripping;
        /// <summary>握りの強さ 0〜1</summary>
        public ReadOnlyReactiveProperty<float> Strength => _strength;
        public ReadOnlyReactiveProperty<bool> IsTracked => _isTracked;
        /// <summary>握りが解除されたとき（理由つき）</summary>
        public Observable<GripReleaseReason> Released => _released;

        /// <summary>手のひらの位置・向き（ワールド）。取れないとき HasPalm=false</summary>
        public bool HasPalm { get; private set; }
        public Pose PalmPose { get; private set; }

        /// <summary>しきい値と時間を渡す（TreasureHaul 側が ThParams.grip から）</summary>
        public void Configure(float gripThreshold, float releaseThreshold, float releaseHoldSeconds, float lostGraceSeconds)
        {
            _machine.GripThreshold = gripThreshold;
            _machine.ReleaseThreshold = releaseThreshold;
            _machine.ReleaseHoldSeconds = releaseHoldSeconds;
            _machine.LostGraceSeconds = lostGraceSeconds;
        }

        /// <summary>見失い待ちから手が戻って握り続ける: 握りの判定を握っている状態へ戻す（<see cref="Released"/> は出さない）</summary>
        public void ResumeGripping()
        {
            _machine.Resume();
            if (!_isGripping.Value) _isGripping.Value = true;
        }

        void Update()
        {
            AcquireSubsystem();
            bool tracked = false;
            float strength = 0f;
            HasPalm = false;
            if (_subsystem != null)
            {
                var hand = _handedness == Handedness.Left ? _subsystem.leftHand : _subsystem.rightHand;
                if (hand.isTracked)
                {
                    tracked = true;
                    if (hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palm))
                    {
                        PalmPose = XrOriginLocator.ToWorldPose(palm);
                        HasPalm = true;
                    }
                    if (!TryMeasure(hand, out strength)) strength = _machine.Strength;   // 関節が欠けた一瞬は前の値のまま
                }
            }

            var ev = _machine.Update(Time.realtimeSinceStartupAsDouble, tracked, strength);
            if (_isTracked.Value != tracked) _isTracked.Value = tracked;
            if (!Mathf.Approximately(_strength.Value, _machine.Strength)) _strength.Value = _machine.Strength;
            if (ev == GripEvent.Gripped) _isGripping.Value = true;
            else if (ev == GripEvent.Released)
            {
                _isGripping.Value = false;
                _released.OnNext(_machine.LastReleaseReason);
            }
        }

        void AcquireSubsystem()
        {
            if (_subsystem != null && _subsystem.running) return;
            _subsystem = null;
            SubsystemManager.GetSubsystems(_subsystems);
            foreach (var s in _subsystems)
                if (s.running) { _subsystem = s; break; }
        }

        bool TryMeasure(XRHand hand, out float grip)
        {
            grip = 0f;
            for (int f = 0; f < 4; f++)
                for (int j = 0; j < 5; j++)
                {
                    if (!hand.GetJoint(s_fingers[f][j]).TryGetPose(out var pose)) return false;
                    _buf[f][j] = pose.position;
                }
            return GripStrength.TryCompute(_buf[0], _buf[1], _buf[2], _buf[3], out grip);
        }

        void OnDestroy()
        {
            _isGripping.Dispose(); _strength.Dispose(); _isTracked.Dispose(); _released.Dispose();
        }
    }
}
