using R3;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// HandPinchTracker のピンチ開始/終了を購読して、浮遊カードの
    /// 「掴む → 追従 → 離す（バインダー収納 or その場で再浮遊）」を行うインタラクター。
    /// あわせて、バインダー内ミニカードの取り出しと、バインダーのページめくりも扱う。
    ///
    /// ピンチ開始時のヒット判定の優先順位（優先度の高い順）:
    ///   1. 浮遊カード（既存。Physics.OverlapSphereNonAlloc）
    ///   2. バインダー内ミニカード（新規。距離判定のみ、Colliderは使わない。ヒットしたら
    ///      CardBinder.TryTakeOut → その場で TryGrab() して実寸カードとしてHeldにする）
    ///   3. ページの角タブ（新規。ヒットしたらページめくりドラッグを開始する）
    /// ミニカードが優先的にマッチした場合はページめくり判定を行わない（BinderView側の
    /// 角タブ非重なり設計により、通常はそもそも競合しない想定の保険）。
    ///
    /// ピンチ中: カードを保持していれば追従させつつ、最寄りのバインダーの収納圏内に
    /// 入ったかどうかを毎フレーム通知してハイライトを制御する。ページめくり中なら
    /// ヒンジ回転をピンチ位置に追従させる。
    /// ピンチ終了: 保持中カードならバインダー収納圏内で収納、圏外ならその場で再浮遊。
    /// ページめくり中ならしきい値判定でめくり切り／バネで元に戻す。
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

        /// <summary>ページめくりドラッグ中のバインダー（未操作なら null）</summary>
        private BinderView turningPageBinderView;

        /// <summary>収納OKハイライトを点けている対象バインダー（未通知なら null）</summary>
        private CardBinder highlightedBinder;

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
                .Subscribe(pose => OnPinchEnded(pose.position))
                .AddTo(this);

            Observable.EveryUpdate()
                .Subscribe(_ => OnEveryUpdate())
                .AddTo(this);
        }

        private void OnDestroy()
        {
            ClearHighlight();
            heldCard.Dispose();
            cardGrabbed.Dispose();
            cardReleased.Dispose();
        }

        private void TryGrabNearestCard(Vector3 pinchPosition)
        {
            if (heldCard.Value != null || turningPageBinderView != null)
            {
                return;
            }

            if (TryGrabFloatingCard(pinchPosition) || TryTakeOutMiniCard(pinchPosition))
            {
                return;
            }

            var binderView = FindVisibleBinderView(pinchPosition);
            if (binderView != null && binderView.TryBeginPageTurn(pinchPosition))
            {
                turningPageBinderView = binderView;
            }
        }

        private bool TryGrabFloatingCard(Vector3 pinchPosition)
        {
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

            return GrabCard(nearest);
        }

        private bool TryTakeOutMiniCard(Vector3 pinchPosition)
        {
            var binderView = FindVisibleBinderView(pinchPosition);
            if (binderView == null)
            {
                return false;
            }
            return binderView.TryTakeOutNearestMiniCard(pinchPosition, out var card) && GrabCard(card);
        }

        private bool GrabCard(CardizableItemBase card)
        {
            if (card == null || !card.TryGrab())
            {
                return false;
            }
            heldCard.Value = card;
            cardGrabbed.OnNext(card);
            return true;
        }

        /// <summary>指定位置に最も近い「表示中の」バインダーの BinderView を返す（存在しなければ null）</summary>
        private static BinderView FindVisibleBinderView(Vector3 position)
        {
            var binder = CardBinder.FindNearest(position);
            if (binder == null)
            {
                return null;
            }
            var view = binder.GetComponent<BinderView>();
            return view != null && view.IsVisible.CurrentValue ? view : null;
        }

        private void OnEveryUpdate()
        {
            if (turningPageBinderView != null)
            {
                turningPageBinderView.UpdatePageTurn(pinchTracker.PinchPose.CurrentValue.position);
                return;
            }
            FollowPinchPose();
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
                ClearHighlight();
                return;
            }

            var target = pinchTracker.PinchPose.CurrentValue;
            var t = Mathf.Clamp01(Time.deltaTime * followLerpSpeed);
            card.transform.SetPositionAndRotation(
                Vector3.Lerp(card.transform.position, target.position, t),
                Quaternion.Slerp(card.transform.rotation, target.rotation, t));

            NotifyNearestBinder(card.transform.position);
        }

        /// <summary>
        /// 保持中カードの位置を最寄りのバインダーへ通知し、収納圏内かどうかに応じて
        /// そのバインダーの収納OKハイライトをON/OFFする。
        /// </summary>
        private void NotifyNearestBinder(Vector3 position)
        {
            var nearest = CardBinder.FindNearest(position);
            if (nearest != highlightedBinder)
            {
                ClearHighlight();
            }

            if (nearest != null && nearest.IsInStoreRange(position))
            {
                highlightedBinder = nearest;
                highlightedBinder.GetComponent<BinderView>()?.SetHighlighted(this, true);
            }
            else
            {
                ClearHighlight();
            }
        }

        private void ClearHighlight()
        {
            if (highlightedBinder == null)
            {
                return;
            }
            highlightedBinder.GetComponent<BinderView>()?.SetHighlighted(this, false);
            highlightedBinder = null;
        }

        private void OnPinchEnded(Vector3 releasePosition)
        {
            if (turningPageBinderView != null)
            {
                turningPageBinderView.EndPageTurn(releasePosition);
                turningPageBinderView = null;
                return;
            }

            ReleaseHeldCard(releasePosition);
        }

        private void ReleaseHeldCard(Vector3 releasePosition)
        {
            var card = heldCard.Value;
            if (card == null)
            {
                return;
            }
            heldCard.Value = null;
            ClearHighlight();

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
