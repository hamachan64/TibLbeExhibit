using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// XR Hands の関節データから片手の「Lサイン（フィンガーガン）」ポーズを判定するトラッカー。
    /// バインダーの呼び出し／収納（BinderView.Toggle）のトリガーとして使う。
    ///
    /// 判定条件（実際の内積計算は LSignPoseEvaluator に切り出し、純粋関数としてテスト可能にしている）:
    ///   - 人差し指が伸びている（Proximal→Intermediate→Tip のセグメント方向がほぼ同方向）
    ///   - 親指が伸びている（Proximal→Distal→Tip のセグメント方向がほぼ同方向）
    ///   - 中指・薬指・小指が握られている（同様のセグメントが大きく折れ曲がる）
    ///   - 親指と人差し指が十分に開いている（距離・方向の両方）。ピンチ等との誤爆防止。
    ///
    /// 誤爆防止（hand_tracking_interaction.md 準拠。HandPinchTracker と同じ設計方針）:
    ///   (a) 成立/解除で別しきい値のヒステリシス（成立は厳しめ、解除は緩め）
    ///   (b) 成立確定までの連続フレーム数デバウンス
    ///       （既定8フレーム。バインダーの出し入れは頻度の低い操作のため HandPinchTracker の
    ///         ピンチ判定（既定3フレーム）より長めにして誤爆をさらに抑える）
    ///   (c) トラッキングロストが一定フレーム続いたら自動的にポーズ解除
    ///
    /// 左右の手それぞれに 1 つずつアタッチして使用する。
    /// </summary>
    public class BinderSummonGestureDetector : MonoBehaviour
    {
        [SerializeField, Tooltip("追跡する手（Left / Right）")]
        private Handedness handedness = Handedness.Right;

        [SerializeField, Tooltip("XR Origin の Transform。関節ポーズをワールド座標へ変換するために使用（未設定ならトラッキング空間の座標をそのまま使う）")]
        private Transform xrOrigin;

        [Header("Lサイン判定しきい値（成立: 厳しめ）")]
        [SerializeField, Tooltip("この内積以上で指が『伸びている』とみなす（成立判定）")]
        private float extendedDotThreshold = 0.7f;
        [SerializeField, Tooltip("この内積以下で指が『握られている』とみなす（成立判定）")]
        private float curledDotThreshold = 0.3f;
        [SerializeField, Tooltip("親指・人差し指の指先がこの距離（m）以上離れていること（成立判定。ピンチとの誤爆防止）")]
        private float thumbIndexMinSpread = 0.06f;
        [SerializeField, Tooltip("親指・人差し指の伸び方向の内積がこの値以下であること（成立判定。指が十分に開いているか）")]
        private float thumbIndexMaxDirectionDot = 0.5f;

        [Header("Lサイン判定しきい値（解除: 緩め＝ヒステリシス）")]
        [SerializeField, Tooltip("この内積を下回ったら指が伸びていないとみなす（解除判定。成立時のしきい値より低くしてバタつきを防ぐ）")]
        private float extendedExitDotThreshold = 0.55f;
        [SerializeField, Tooltip("この内積を上回ったら指が握られていないとみなす（解除判定。成立時のしきい値より高くしてバタつきを防ぐ）")]
        private float curledExitDotThreshold = 0.45f;
        [SerializeField, Tooltip("親指・人差し指の指先がこの距離（m）を下回ったら解除（解除判定）")]
        private float thumbIndexExitSpread = 0.03f;
        [SerializeField, Tooltip("親指・人差し指の伸び方向の内積がこの値を上回ったら解除（解除判定）")]
        private float thumbIndexExitDirectionDot = 0.75f;

        [Header("デバウンス／トラッキングロスト")]
        [SerializeField, Tooltip("ポーズ成立を確定するまでの連続フレーム数（誤爆防止。ピンチより長めが既定）")]
        private int poseConfirmFrames = 8;
        [SerializeField, Tooltip("トラッキングロストがこのフレーム数続いたら強制的にポーズ解除")]
        private int trackingLostReleaseFrames = 15;

        private readonly ReactiveProperty<bool> isPosing = new(false);
        private readonly ReactiveProperty<Pose> handPose = new(Pose.identity);
        private readonly Subject<Unit> poseEntered = new();
        private readonly Subject<Unit> poseExited = new();

        private XRHandSubsystem subsystem;
        private int candidateFrames;
        private int lostFrames;

        /// <summary>Lサインポーズ中かどうか</summary>
        public ReadOnlyReactiveProperty<bool> IsPosing => isPosing;

        /// <summary>ポーズ成立イベント（バインダーのトグルはこれを購読して行う）</summary>
        public Observable<Unit> PoseEntered => poseEntered;

        /// <summary>ポーズ解除イベント</summary>
        public Observable<Unit> PoseExited => poseExited;

        /// <summary>手のポーズ（人差し指付け根あたり、ワールド座標。煙VFXの発生位置に使う）</summary>
        public ReadOnlyReactiveProperty<Pose> HandPose => handPose;

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
            isPosing.Dispose();
            handPose.Dispose();
            poseEntered.Dispose();
            poseExited.Dispose();
        }

        /// <summary>
        /// XRHandSubsystem は XR プラグインの初期化タイミング次第で生成が遅れるため、
        /// 見つかるまでポーリングして取得する（HandPinchTracker と同じ方針）。
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

            if (!hand.isTracked || (flags & jointsFlag) == 0 || !TryBuildInput(hand, out var input, out var indexBaseLocal))
            {
                OnTrackingLostFrame();
                return;
            }

            lostFrames = 0;

            // XR Hands の関節ポーズはトラッキング空間基準なので XR Origin でワールドへ変換する
            var origin = xrOrigin != null ? xrOrigin : XrOriginLocator.Find();
            var indexBaseWorld = origin != null
                ? indexBaseLocal.GetTransformedBy(new Pose(origin.position, origin.rotation))
                : indexBaseLocal;
            handPose.Value = indexBaseWorld;

            UpdatePoseState(input);
        }

        /// <summary>判定に必要な5本指×3関節を取得し、LSignPoseEvaluator.Input に詰める</summary>
        private bool TryBuildInput(XRHand hand, out LSignPoseEvaluator.Input input, out Pose indexBasePose)
        {
            input = default;
            indexBasePose = Pose.identity;

            if (!hand.GetJoint(XRHandJointID.ThumbProximal).TryGetPose(out var thumbBase)
                || !hand.GetJoint(XRHandJointID.ThumbDistal).TryGetPose(out var thumbMid)
                || !hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumbTip)
                || !hand.GetJoint(XRHandJointID.IndexProximal).TryGetPose(out var indexBase)
                || !hand.GetJoint(XRHandJointID.IndexIntermediate).TryGetPose(out var indexMid)
                || !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var indexTip)
                || !hand.GetJoint(XRHandJointID.MiddleProximal).TryGetPose(out var middleBase)
                || !hand.GetJoint(XRHandJointID.MiddleIntermediate).TryGetPose(out var middleMid)
                || !hand.GetJoint(XRHandJointID.MiddleTip).TryGetPose(out var middleTip)
                || !hand.GetJoint(XRHandJointID.RingProximal).TryGetPose(out var ringBase)
                || !hand.GetJoint(XRHandJointID.RingIntermediate).TryGetPose(out var ringMid)
                || !hand.GetJoint(XRHandJointID.RingTip).TryGetPose(out var ringTip)
                || !hand.GetJoint(XRHandJointID.LittleProximal).TryGetPose(out var littleBase)
                || !hand.GetJoint(XRHandJointID.LittleIntermediate).TryGetPose(out var littleMid)
                || !hand.GetJoint(XRHandJointID.LittleTip).TryGetPose(out var littleTip))
            {
                return false;
            }

            input = new LSignPoseEvaluator.Input
            {
                Thumb = new LSignPoseEvaluator.FingerJoints(thumbBase.position, thumbMid.position, thumbTip.position),
                Index = new LSignPoseEvaluator.FingerJoints(indexBase.position, indexMid.position, indexTip.position),
                Middle = new LSignPoseEvaluator.FingerJoints(middleBase.position, middleMid.position, middleTip.position),
                Ring = new LSignPoseEvaluator.FingerJoints(ringBase.position, ringMid.position, ringTip.position),
                Little = new LSignPoseEvaluator.FingerJoints(littleBase.position, littleMid.position, littleTip.position),
            };
            indexBasePose = indexBase;
            return true;
        }

        private void UpdatePoseState(in LSignPoseEvaluator.Input input)
        {
            if (!isPosing.Value)
            {
                // 開始判定: 厳しめのしきい値が規定フレーム連続したら確定（デバウンス）
                var enterThresholds = new LSignPoseEvaluator.Thresholds
                {
                    ExtendedDot = extendedDotThreshold,
                    CurledDot = curledDotThreshold,
                    ThumbIndexMinSpread = thumbIndexMinSpread,
                    ThumbIndexMaxDirectionDot = thumbIndexMaxDirectionDot,
                };
                if (LSignPoseEvaluator.Evaluate(input, enterThresholds))
                {
                    candidateFrames++;
                    if (candidateFrames >= poseConfirmFrames)
                    {
                        BeginPose();
                    }
                }
                else
                {
                    candidateFrames = 0;
                }
            }
            else
            {
                // 終了判定: 緩めのしきい値を下回って初めて解除（ヒステリシス）
                var exitThresholds = new LSignPoseEvaluator.Thresholds
                {
                    ExtendedDot = extendedExitDotThreshold,
                    CurledDot = curledExitDotThreshold,
                    ThumbIndexMinSpread = thumbIndexExitSpread,
                    ThumbIndexMaxDirectionDot = thumbIndexExitDirectionDot,
                };
                if (!LSignPoseEvaluator.Evaluate(input, exitThresholds))
                {
                    EndPose();
                }
            }
        }

        private void OnTrackingLostFrame()
        {
            candidateFrames = 0;
            if (!isPosing.Value)
            {
                return;
            }
            lostFrames++;
            if (lostFrames >= trackingLostReleaseFrames)
            {
                EndPose();
            }
        }

        private void BeginPose()
        {
            candidateFrames = 0;
            isPosing.Value = true;
            poseEntered.OnNext(Unit.Default);
        }

        private void EndPose()
        {
            lostFrames = 0;
            candidateFrames = 0;
            isPosing.Value = false;
            poseExited.OnNext(Unit.Default);
        }
    }
}
