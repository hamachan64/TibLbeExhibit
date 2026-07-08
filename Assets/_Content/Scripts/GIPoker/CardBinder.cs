using System.Collections.Generic;
using System.Linq;
using ObservableCollections;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// G.I.のバインダー（ブック）。カードのインベントリ管理とスコア計算を担うマネージャー。
    ///
    /// - インベントリは ObservableList として公開し、バインダーUIは差分を購読して描画する
    /// - カードが出し入れされるたびに PokerHandEvaluator でスコアを再計算し、
    ///   Score（ReactiveProperty）として公開する
    /// - 収納判定用に「収納ポイント＋半径」の圏内チェック API を持つ
    ///   （CardGrabInteractor がカードを離したときに参照する）
    ///
    /// バインダーの GameObject（UserClient シーン）にアタッチする。
    /// </summary>
    public class CardBinder : MonoBehaviour
    {
        [SerializeField, Tooltip("収納判定の中心（未設定なら自身の Transform）")]
        private Transform storePoint;

        [SerializeField, Tooltip("この半径（m）内でカードを離すと収納される")]
        private float storeRadius = 0.2f;

        [SerializeField, Tooltip("バインダーの最大収納枚数")]
        private int capacity = 40;

        private static readonly List<CardBinder> activeBinders = new();

        private readonly ObservableList<CardInstance> cards = new();
        private readonly ReactiveProperty<ScoreResult> score = new(ScoreResult.Empty);
        private readonly Subject<CardInstance> cardStored = new();
        private readonly Subject<CardInstance> cardTakenOut = new();
        private readonly Subject<CardInstance> cardDiscarded = new();

        /// <summary>収納中のカード一覧（バインダーUIが差分購読する）</summary>
        public IReadOnlyObservableList<CardInstance> Cards => cards;

        /// <summary>現在のスコア（基本得点＋役ボーナスの内訳つき）。収納内容が変わるたび更新される</summary>
        public ReadOnlyReactiveProperty<ScoreResult> Score => score;

        /// <summary>カードが収納されたイベント</summary>
        public Observable<CardInstance> CardStored => cardStored;

        /// <summary>カードが取り出されたイベント</summary>
        public Observable<CardInstance> CardTakenOut => cardTakenOut;

        /// <summary>カードが破壊されたイベント（リスキーダイス「大凶」等）</summary>
        public Observable<CardInstance> CardDiscarded => cardDiscarded;

        public Vector3 StorePosition => (storePoint != null ? storePoint : transform).position;

        public bool IsFull => cards.Count >= capacity;

        private void Awake()
        {
            // 出し入れで枚数は必ず変化するため、CountChanged をスコア再計算のトリガーにする
            cards.ObserveCountChanged()
                .Subscribe(_ => RecalculateScore())
                .AddTo(this);
        }

        private void OnEnable()
        {
            activeBinders.Add(this);
        }

        private void OnDisable()
        {
            activeBinders.Remove(this);
        }

        private void OnDestroy()
        {
            score.Dispose();
            cardStored.Dispose();
            cardTakenOut.Dispose();
            cardDiscarded.Dispose();
        }

        /// <summary>指定位置が収納圏内かどうか</summary>
        public bool IsInStoreRange(Vector3 position)
        {
            return Vector3.Distance(position, StorePosition) <= storeRadius;
        }

        /// <summary>
        /// カードの収納を試みる。成功するとカードのカウントダウンは停止し、
        /// シーン上の実体は非表示になってインベントリに登録される。
        /// </summary>
        public bool TryStore(CardizableItemBase card)
        {
            if (card == null || card.CardInstance == null || IsFull)
            {
                return false;
            }
            var currentState = card.State.CurrentValue;
            if (currentState != CardizableState.Held && currentState != CardizableState.FloatingCard)
            {
                return false;
            }

            card.MarkStored();
            cards.Add(card.CardInstance);
            cardStored.OnNext(card.CardInstance);
            return true;
        }

        /// <summary>
        /// カードの取り出し（ゲイン準備など）を試みる。成功するとカードの実体が
        /// 指定位置で再浮遊し、1分ルールのカウントダウンが仕切り直しで再開する。
        /// </summary>
        public bool TryTakeOut(CardInstance instance, Vector3 position)
        {
            if (instance == null || !cards.Remove(instance))
            {
                return false;
            }

            if (instance.Source != null)
            {
                instance.Source.ReleaseFromBinder(position);
            }
            cardTakenOut.OnNext(instance);
            return true;
        }

        /// <summary>
        /// バインダー内カードの定義を差し替える（「錬金キット」効果用）。
        /// 枚数が変わらないためスコアは明示的に再計算する。
        /// </summary>
        public bool TryConvertCard(CardInstance instance, CardDefinition newDefinition)
        {
            if (instance == null || newDefinition == null || !cards.Contains(instance))
            {
                return false;
            }
            instance.ReplaceDefinition(newDefinition);
            instance.Source?.OverrideDefinition(newDefinition);
            RecalculateScore();
            return true;
        }

        /// <summary>
        /// 定義だけを複製したカードを収納する（「ギャラリーフェイク」効果用）。
        /// 複製カードには実体（Source）がないため、取り出し・ゲインはできない。
        /// </summary>
        public bool StoreCopy(CardDefinition definition)
        {
            if (definition == null || IsFull)
            {
                return false;
            }
            var copy = new CardInstance(definition, null);
            cards.Add(copy);
            cardStored.OnNext(copy);
            return true;
        }

        /// <summary>
        /// バインダー内のカードをランダムに破壊する（リスキーダイス「大凶」用）。
        /// </summary>
        /// <returns>実際に破壊された枚数</returns>
        public int DiscardRandom(int count)
        {
            var discarded = 0;
            while (discarded < count && cards.Count > 0)
            {
                var index = Random.Range(0, cards.Count);
                var instance = cards[index];
                cards.RemoveAt(index);
                if (instance.Source != null)
                {
                    Destroy(instance.Source.gameObject);
                }
                cardDiscarded.OnNext(instance);
                discarded++;
            }
            return discarded;
        }

        /// <summary>
        /// 指定位置に最も近いアクティブなバインダーを返す（存在しなければ null）。
        /// </summary>
        public static CardBinder FindNearest(Vector3 position)
        {
            CardBinder nearest = null;
            var nearestSqrDistance = float.MaxValue;
            foreach (var binder in activeBinders)
            {
                var sqrDistance = (binder.StorePosition - position).sqrMagnitude;
                if (sqrDistance < nearestSqrDistance)
                {
                    nearestSqrDistance = sqrDistance;
                    nearest = binder;
                }
            }
            return nearest;
        }

        private void RecalculateScore()
        {
            score.Value = PokerHandEvaluator.Evaluate(cards.Select(c => c.Rank));
        }
    }
}
