using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace STYLY.TIB_Template
{
    /// <summary>
    /// XR Hands の関節データから片手の「つまむ（ピンチ）／離す」を判定するトラッカー。
    ///
    /// - 親指先端と人差し指先端の距離で判定する
    /// - 誤爆防止のため、開始/終了で別しきい値（ヒステリシス）＋連続フレーム数の
    ///   デバウンスを設けている（hand_tracking_interaction.md のルールに対応）
    /// - トラッキングロスト時は一定フレーム経過で自動的にピンチ解除する
    /// - 判定結果はすべて R3 の Observable / ReactiveProperty として公開する
    ///
    /// 左右の手それぞれに 1 つずつアタッチして使用する。
    /// </summary>
    public class HandPinchTracker : MonoBehaviour
    {
        [SerializeField, Tooltip("追跡する手（Left / Right）")]
        private Handedness handedness = Handedness.Right;

        [SerializeField, Tooltip("XR Origin の Transform。関節ポーズをワールド座標へ変換するために使用（未設定ならトラッキング空間の座標をそのまま使う）")]
        private Transform xrOrigin;

        [Header("ピンチ判定しきい値")]
        [SerializeField, Tooltip("この距離（m）以下でピンチ開始候補")]
        private float pinchEnterDistance = 0.02f;
        [SerializeField, Tooltip("この距離（m）以上でピンチ終了（ヒステリシス）")]
        private float pinchExitDistance = 0.04f;
        [SerializeField, Tooltip("ピンチ開始を確定するまでの連続フレーム数（誤爆防止）")]
        private int pinchConfirmFrames = 3;
        [SerializeField, Tooltip("トラッキングロストがこのフレーム数続いたら強制的にピンチ解除")]
        private int trackingLostReleaseFrames = 15;

        private readonly ReactiveProperty<bool> isPinching = new(false);
        private readonly ReactiveProperty<bool> isTracked = new(false);
        private readonly ReactiveProperty<Pose> pinchPose = new(Pose.identity);
        private readonly Subject<Pose> pinchStarted = new();
        private readonly Subject<Pose> pinchEnded = new();

        private XRHandSubsystem subsystem;
        private int belowThresholdFrames;
        private int lostFrames;

        /// <summary>ピンチ中かどうか</summary>
        public ReadOnlyReactiveProperty<bool> IsPinching => isPinching;

        /// <summary>手（必要関節）がトラッキングできているか</summary>
        public ReadOnlyReactiveProperty<bool> IsTracked => isTracked;

        /// <summary>ピンチ位置（親指先端と人差し指先端の中点、ワールド座標）</summary>
        public ReadOnlyReactiveProperty<Pose> PinchPose => pinchPose;

        /// <summary>ピンチ開始イベント（値は開始時のピンチポーズ）</summary>
        public Observable<Pose> PinchStarted => pinchStarted;

        /// <summary>ピンチ終了イベント（値は終了時のピンチポーズ）</summary>
        public Observable<Pose> PinchEnded => pinchEnded;

        public Handedness Handedness => handedness;

        /// <summary>ランタイム生成（AddComponent）直後に呼んで対象の手を設定する</summary>
        public void Initialize(Handedness hand, Transform origin = null)
        {
            handedness = hand;
            xrOrigin = origin;
        }

        private void Awake()
        {
            AcquireSubsystemAsync(destroyCancellationToken).Forget();
        }

        private void OnDestroy()
        {
            if (subsystem != null)
            {
                subsystem.updatedHands -= OnUpdatedHands;
                subsystem = null;
            }
            isPinching.Dispose();
            isTracked.Dispose();
            pinchPose.Dispose();
            pinchStarted.Dispose();
            pinchEnded.Dispose();
        }

        /// <summary>
        /// XRHandSubsystem は XR プラグインの初期化タイミング次第で生成が遅れるため、
        /// 見つかるまでポーリングして取得する。
        /// </summary>
        private async UniTask AcquireSubsystemAsync(CancellationToken ct)
        {
            var candidates = new List<XRHandSubsystem>();
            while (!ct.IsCancellationRequested)
            {
                SubsystemManager.GetSubsystems(candidates);
                if (candidates.Count > 0)
                {
                    subsystem = candidates[0];
                    subsystem.updatedHands += OnUpdatedHands;
                    return;
                }
                await UniTask.Delay(TimeSpan.FromSeconds(0.5), cancellationToken: ct);
            }
        }

        private void OnUpdatedHands(
            XRHandSubsystem sub,
            XRHandSubsystem.UpdateSuccessFlags flags,
            XRHandSubsystem.UpdateType updateType)
        {
            // BeforeRender と Dynamic の2回呼ばれるため、ゲームロジックは Dynamic のみで処理する
            if (updateType != XRHandSubsystem.UpdateType.Dynamic)
            {
                return;
            }

            var hand = handedness == Handedness.Left ? sub.leftHand : sub.rightHand;
            var jointsFlag = handedness == Handedness.Left
                ? XRHandSubsystem.UpdateSuccessFlags.LeftHandJoints
                : XRHandSubsystem.UpdateSuccessFlags.RightHandJoints;

            var thumb = hand.GetJoint(XRHandJointID.ThumbTip);
            var index = hand.GetJoint(XRHandJointID.IndexTip);

            var tracked = hand.isTracked
                          && (flags & jointsFlag) != 0
                          && thumb.TryGetPose(out var thumbPose)
                          && index.TryGetPose(out var indexPose);

            if (!tracked)
            {
                OnTrackingLostFrame();
                return;
            }

            lostFrames = 0;
            isTracked.Value = true;

            // XR Hands の関節ポーズはトラッキング空間基準なので XR Origin でワールドへ変換する
            thumb.TryGetPose(out var thumbLocal);
            index.TryGetPose(out var indexLocal);
            var origin = xrOrigin != null ? xrOrigin : XrOriginLocator.Find();
            if (origin != null)
            {
                var originPose = new Pose(origin.position, origin.rotation);
                thumbLocal = thumbLocal.GetTransformedBy(originPose);
                indexLocal = indexLocal.GetTransformedBy(originPose);
            }

            var midpoint = (thumbLocal.position + indexLocal.position) * 0.5f;
            pinchPose.Value = new Pose(midpoint, indexLocal.rotation);

            var distance = Vector3.Distance(thumbLocal.position, indexLocal.position);
            UpdatePinchState(distance);
        }

        private void UpdatePinchState(float distance)
        {
            if (!isPinching.Value)
            {
                // 開始判定: enter しきい値以下が規定フレーム連続したら確定（デバウンス）
                if (distance <= pinchEnterDistance)
                {
                    belowThresholdFrames++;
                    if (belowThresholdFrames >= pinchConfirmFrames)
                    {
                        BeginPinch();
                    }
                }
                else
                {
                    belowThresholdFrames = 0;
                }
            }
            else if (distance >= pinchExitDistance)
            {
                // 終了判定: exit しきい値（enter より大きい）でヒステリシスを効かせる
                EndPinch();
            }
        }

        private void OnTrackingLostFrame()
        {
            isTracked.Value = false;
            belowThresholdFrames = 0;
            if (!isPinching.Value)
            {
                return;
            }
            lostFrames++;
            if (lostFrames >= trackingLostReleaseFrames)
            {
                EndPinch();
            }
        }

        private void BeginPinch()
        {
            belowThresholdFrames = 0;
            isPinching.Value = true;
            pinchStarted.OnNext(pinchPose.Value);
        }

        private void EndPinch()
        {
            lostFrames = 0;
            belowThresholdFrames = 0;
            isPinching.Value = false;
            pinchEnded.OnNext(pinchPose.Value);
        }
    }
}
