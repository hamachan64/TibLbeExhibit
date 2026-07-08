using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// 人差し指の先端に「素手で触れた」判定用の小さなコライダーを追従させるコンポーネント。
    /// CardizableItemBase の OnTriggerEnter がこのコライダーとの接触でカード化を発火する。
    ///
    /// コライダーは HandBone レイヤー（未定義なら Default）に置かれ、
    /// Kinematic Rigidbody 付きなのでトリガーイベントが確実に発生する。
    /// </summary>
    public class FingertipTouchCollider : MonoBehaviour
    {
        public const string HandBoneLayerName = "HandBone";

        [SerializeField, Tooltip("追跡する手（Left / Right）")]
        private Handedness handedness = Handedness.Right;

        [SerializeField, Tooltip("指先コライダーの半径（m）")]
        private float fingertipRadius = 0.012f;

        private XRHandSubsystem subsystem;
        private Transform fingertip;

        /// <summary>ランタイム生成（AddComponent）直後に呼んで対象の手を設定する</summary>
        public void Initialize(Handedness hand)
        {
            handedness = hand;
        }

        private void Awake()
        {
            CreateFingertipObject();
            AcquireSubsystemAsync(destroyCancellationToken).Forget();
        }

        private void OnDestroy()
        {
            if (subsystem != null)
            {
                subsystem.updatedHands -= OnUpdatedHands;
                subsystem = null;
            }
            if (fingertip != null)
            {
                Destroy(fingertip.gameObject);
            }
        }

        private void CreateFingertipObject()
        {
            // 追従遅延を避けるため、手の親ではなくシーン直下に置く
            var go = new GameObject($"Fingertip_{handedness}");
            var layer = LayerMask.NameToLayer(HandBoneLayerName);
            if (layer >= 0)
            {
                go.layer = layer;
            }
            else
            {
                Debug.LogWarning($"[FingertipTouchCollider] レイヤー '{HandBoneLayerName}' が未定義のため Default レイヤーを使用します");
            }

            var sphere = go.AddComponent<SphereCollider>();
            sphere.radius = fingertipRadius;
            sphere.isTrigger = false;

            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            fingertip = go.transform;
            go.SetActive(false); // トラッキング確立まで無効化
        }

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
            if (updateType != XRHandSubsystem.UpdateType.Dynamic || fingertip == null)
            {
                return;
            }

            var hand = handedness == Handedness.Left ? sub.leftHand : sub.rightHand;
            if (!hand.isTracked || !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var pose))
            {
                fingertip.gameObject.SetActive(false);
                return;
            }

            var worldPose = XrOriginLocator.ToWorldPose(pose);
            fingertip.SetPositionAndRotation(worldPose.position, worldPose.rotation);
            if (!fingertip.gameObject.activeSelf)
            {
                fingertip.gameObject.SetActive(true);
            }
        }
    }
}
