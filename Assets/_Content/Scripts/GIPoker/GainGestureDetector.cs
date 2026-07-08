using R3;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// 「ゲイン（具現化）」のジェスチャー判定。
    ///
    /// 採用ジェスチャー: **カードを両手で引き裂く**
    ///   1. 片手のピンチでカードを保持する
    ///   2. もう一方の手でカードの近く（assistRadius 以内）をピンチする
    ///   3. 両手をピンチしたまま tearDistance 以上引き離すとゲイン成立
    ///
    /// ボタンのないハンドトラッキングでも直感的で、
    /// 「両手・近接開始・一定距離の引き離し」という3条件により誤爆しにくい
    /// （hand_tracking_interaction.md の明確な状態判定ルールに対応）。
    /// </summary>
    public class GainGestureDetector : MonoBehaviour
    {
        [SerializeField, Tooltip("保持カードからこの距離（m）以内で逆手をピンチすると引き裂き開始")]
        private float assistRadius = 0.15f;

        [SerializeField, Tooltip("両手のピンチ位置がこの距離（m）以上離れるとゲイン成立")]
        private float tearDistance = 0.25f;

        private HandPinchTracker leftTracker;
        private HandPinchTracker rightTracker;
        private CardGrabInteractor leftInteractor;
        private CardGrabInteractor rightInteractor;

        private CardizableItemBase tearingCard;
        private readonly ReactiveProperty<float> tearProgress = new(0f);
        private readonly Subject<CardizableItemBase> gainPerformed = new();

        /// <summary>引き裂きの進行度 0〜1（演出用: カードを震わせる等に使える）</summary>
        public ReadOnlyReactiveProperty<float> TearProgress => tearProgress;

        /// <summary>ゲイン成立イベント（CardEffectService が購読して効果を発動する）</summary>
        public Observable<CardizableItemBase> GainPerformed => gainPerformed;

        /// <summary>ランタイム生成直後に左右の手のコンポーネントを結線する</summary>
        public void Initialize(
            HandPinchTracker left, CardGrabInteractor leftGrab,
            HandPinchTracker right, CardGrabInteractor rightGrab)
        {
            leftTracker = left;
            leftInteractor = leftGrab;
            rightTracker = right;
            rightInteractor = rightGrab;

            // 逆手のピンチ開始 = 引き裂き開始の候補判定
            leftTracker.PinchStarted
                .Subscribe(pose => TryBeginTear(rightInteractor, pose.position))
                .AddTo(this);
            rightTracker.PinchStarted
                .Subscribe(pose => TryBeginTear(leftInteractor, pose.position))
                .AddTo(this);

            Observable.EveryUpdate()
                .Subscribe(_ => UpdateTear())
                .AddTo(this);
        }

        private void OnDestroy()
        {
            tearProgress.Dispose();
            gainPerformed.Dispose();
        }

        private void TryBeginTear(CardGrabInteractor holderSide, Vector3 assistPinchPosition)
        {
            if (tearingCard != null || holderSide == null)
            {
                return;
            }
            var held = holderSide.HeldCard.CurrentValue;
            if (held == null)
            {
                return;
            }
            if (Vector3.Distance(held.transform.position, assistPinchPosition) > assistRadius)
            {
                return;
            }
            tearingCard = held;
            tearProgress.Value = 0f;
        }

        private void UpdateTear()
        {
            if (tearingCard == null)
            {
                return;
            }

            // 成立前にどちらかの手を離した、またはカードが保持状態でなくなったらキャンセル
            if (!leftTracker.IsPinching.CurrentValue
                || !rightTracker.IsPinching.CurrentValue
                || tearingCard.State.CurrentValue != CardizableState.Held)
            {
                CancelTear();
                return;
            }

            var separation = Vector3.Distance(
                leftTracker.PinchPose.CurrentValue.position,
                rightTracker.PinchPose.CurrentValue.position);
            tearProgress.Value = Mathf.Clamp01(separation / tearDistance);

            if (separation >= tearDistance)
            {
                var card = tearingCard;
                CancelTear();
                if (card.TryGain())
                {
                    gainPerformed.OnNext(card);
                }
            }
        }

        private void CancelTear()
        {
            tearingCard = null;
            tearProgress.Value = 0f;
        }
    }
}
