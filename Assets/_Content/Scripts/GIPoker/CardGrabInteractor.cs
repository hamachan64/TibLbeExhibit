using R3;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// HandPinchTracker のピンチ開始/終了を購読して、浮遊カードの
    /// 「掴む → 追従 → 離す（バインダー収納 or その場で再浮遊）」を行うインタラクター。
    ///
    /// - ピンチ開始: ピンチ位置の近傍（grabRadius）にある浮遊カードのうち最も近いものを掴む
    /// - ピンチ中: カードがピンチ位置に追従する
    /// - ピンチ終了: バインダーの収納圏内なら収納、圏外ならその場で再浮遊
    ///
    /// HandPinchTracker と同じ GameObject（左右の手それぞれ）にアタッチする。
    /// </summary>
    public class CardGrabInteractor : MonoBehaviour
    {
        [SerializeField, Tooltip("購読するピンチトラッカー（未設定なら同じ GameObject から取得）")]
        private HandPinchTracker pinchTracker;

        [SerializeField, Tooltip("ピンチ位置からこの半径（m）内の浮遊カードを掴める")]
        private float grabRadius = 0.08f;

        [SerializeField, Tooltip("カードのコライダーが属するレイヤー（絞ると誤掴み防止と負荷軽減になる）")]
        private LayerMask cardLayerMask = ~0;

        [SerializeField, Tooltip("カードがピンチ位置へ追従する速さ")]
        private float followLerpSpeed = 20f;

        private static readonly Collider[] overlapBuffer = new Collider[16];

        private readonly ReactiveProperty<CardizableItemBase> heldCard = new(null);
        private readonly Subject<CardizableItemBase> cardGrabbed = new();
        private readonly Subject<CardizableItemBase> cardReleased = new();

        /// <summary>現在掴んでいるカード（未保持なら null）</summary>
        public ReadOnlyReactiveProperty<CardizableItemBase> HeldCard => heldCard;

        /// <summary>カードを掴んだイベント</summary>
        public Observable<CardizableItemBase> CardGrabbed => cardGrabbed;

        /// <summary>カードを離したイベント（収納された場合は CardBinder.CardStored 側で通知）</summary>
        public Observable<CardizableItemBase> CardReleased => cardReleased;

        private void Awake()
        {
            if (pinchTracker == null)
            {
                pinchTracker = GetComponent<HandPinchTracker>();
            }
            if (pinchTracker == null)
            {
                Debug.LogError("[CardGrabInteractor] HandPinchTracker が見つかりません", this);
                enabled = false;
                return;
            }

            pinchTracker.PinchStarted
                .Subscribe(pose => TryGrabNearestCard(pose.position))
                .AddTo(this);

            pinchTracker.PinchEnded
                .Subscribe(pose => ReleaseHeldCard(pose.position))
                .AddTo(this);

            Observable.EveryUpdate()
                .Subscribe(_ => FollowPinchPose())
                .AddTo(this);
        }

        private void OnDestroy()
        {
            heldCard.Dispose();
            cardGrabbed.Dispose();
            cardReleased.Dispose();
        }

        private void TryGrabNearestCard(Vector3 pinchPosition)
        {
            if (heldCard.Value != null)
            {
                return;
            }

            var hitCount = Physics.OverlapSphereNonAlloc(
                pinchPosition, grabRadius, overlapBuffer, cardLayerMask, QueryTriggerInteraction.Collide);

            CardizableItemBase nearest = null;
            var nearestSqrDistance = float.MaxValue;
            for (var i = 0; i < hitCount; i++)
            {
                var card = overlapBuffer[i].GetComponentInParent<CardizableItemBase>();
                if (card == null || !card.CanBeGrabbed)
                {
                    continue;
                }
                var sqrDistance = (card.transform.position - pinchPosition).sqrMagnitude;
                if (sqrDistance < nearestSqrDistance)
                {
                    nearestSqrDistance = sqrDistance;
                    nearest = card;
                }
            }

            if (nearest != null && nearest.TryGrab())
            {
                heldCard.Value = nearest;
                cardGrabbed.OnNext(nearest);
            }
        }

        private void FollowPinchPose()
        {
            var card = heldCard.Value;
            if (card == null)
            {
                return;
            }

            // 保持中に消滅（1分ルール）した場合などは参照を手放す
            var cardState = card.State.CurrentValue;
            if (cardState != CardizableState.Held && cardState != CardizableState.GainedHeld)
            {
                heldCard.Value = null;
                return;
            }

            var target = pinchTracker.PinchPose.CurrentValue;
            var t = Mathf.Clamp01(Time.deltaTime * followLerpSpeed);
            card.transform.SetPositionAndRotation(
                Vector3.Lerp(card.transform.position, target.position, t),
                Quaternion.Slerp(card.transform.rotation, target.rotation, t));
        }

        private void ReleaseHeldCard(Vector3 releasePosition)
        {
            var card = heldCard.Value;
            if (card == null)
            {
                return;
            }
            heldCard.Value = null;

            // バインダーの収納圏内で離したら収納、それ以外はその場で再浮遊
            var binder = CardBinder.FindNearest(releasePosition);
            if (binder != null && binder.IsInStoreRange(releasePosition) && binder.TryStore(card))
            {
                return;
            }

            card.Release();
            cardReleased.OnNext(card);
        }
    }
}
