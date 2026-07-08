using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>リスキーダイスの結果</summary>
    public readonly struct DiceResult
    {
        /// <summary>出目（1〜20）</summary>
        public int Roll { get; }
        /// <summary>大吉か</summary>
        public bool IsBless { get; }
        /// <summary>大凶か</summary>
        public bool IsCurse { get; }
        /// <summary>ダイスを振った位置（演出・ネットワーク同期用）</summary>
        public Vector3 Position { get; }

        public DiceResult(int roll, bool isBless, bool isCurse, Vector3 position)
        {
            Roll = roll;
            IsBless = isBless;
            IsCurse = isCurse;
            Position = position;
        }
    }

    /// <summary>
    /// ゲイン成立時にカード効果を発動するサービス。
    /// GainGestureDetector.GainPerformed を購読して CardEffectType に応じた処理を行う。
    ///
    /// - 剣（AlchemySword）: 近くの未カード化Dランクアイテムをランダムなランク定義に錬金
    /// - 錬金キット（AlchemyKit）: バインダー内のDランクカード1枚をCランクに変換
    /// - 占い水晶（SlowFloating）: 一定時間、全カードの浮遊を遅くする
    /// - 彷徨うよろい（PauseCountdown）: 一定時間、全カードの1分ルールを停止
    /// - リスキーダイス（RiskyDice）: d20を振り、大吉でSSカード出現、大凶でバインダー内カード破壊
    /// </summary>
    public class CardEffectService : MonoBehaviour
    {
        [Header("錬金")]
        [SerializeField, Tooltip("錬金の変換先となるCランクのカード定義プール")]
        private List<CardDefinition> alchemyResultPool = new();
        [SerializeField, Tooltip("剣の錬金が届く範囲（m）")]
        private float swordAlchemyRadius = 2.5f;

        [Header("占い水晶")]
        [SerializeField, Tooltip("浮遊速度の倍率")]
        private float slowFloatingScale = 0.3f;
        [SerializeField, Tooltip("効果時間（秒）")]
        private float slowFloatingDuration = 12f;

        [Header("彷徨うよろい")]
        [SerializeField, Tooltip("カウントダウン停止時間（秒）")]
        private float pauseCountdownDuration = 15f;

        [Header("リスキーダイス")]
        [SerializeField, Tooltip("大吉時に出現するSSランクアイテムのプレハブ")]
        private GameObject ssItemPrefab;
        [SerializeField, Tooltip("大吉となる出目の下限（d20）")]
        private int blessThreshold = 18;
        [SerializeField, Tooltip("大凶となる出目の上限（d20）")]
        private int curseThreshold = 3;
        [SerializeField, Tooltip("大凶で破壊される枚数")]
        private int curseDiscardCount = 3;

        private readonly Subject<DiceResult> diceRolled = new();
        private readonly Subject<(CardEffectType effect, CardizableItemBase card)> effectActivated = new();
        private readonly Subject<CardizableItemBase> ssSpawned = new();

        /// <summary>ダイスの結果イベント（演出側・ネットワーク同期が購読する）</summary>
        public Observable<DiceResult> DiceRolled => diceRolled;

        /// <summary>効果発動イベント（演出・SE・ネットワーク同期用）</summary>
        public Observable<(CardEffectType effect, CardizableItemBase card)> EffectActivated => effectActivated;

        /// <summary>大吉によるSSカード出現イベント（ネットワーク同期用）</summary>
        public Observable<CardizableItemBase> SsSpawned => ssSpawned;

        private void OnDestroy()
        {
            diceRolled.Dispose();
            effectActivated.Dispose();
            ssSpawned.Dispose();
        }

        /// <summary>GainGestureDetector と結線する</summary>
        public void BindTo(GainGestureDetector detector)
        {
            detector.GainPerformed
                .Subscribe(Execute)
                .AddTo(this);
        }

        /// <summary>ゲインされたカードの効果を発動する</summary>
        public void Execute(CardizableItemBase gainedCard)
        {
            if (gainedCard == null || gainedCard.Definition == null)
            {
                return;
            }

            var effect = gainedCard.Definition.EffectType;
            switch (effect)
            {
                case CardEffectType.AlchemySword:
                    ExecuteSwordAlchemy(gainedCard);
                    break;
                case CardEffectType.AlchemyKit:
                    ExecuteAlchemyKit(gainedCard);
                    break;
                case CardEffectType.SlowFloating:
                    ExecuteSlowFloating(destroyCancellationToken).Forget();
                    break;
                case CardEffectType.PauseCountdown:
                    ExecutePauseCountdown(destroyCancellationToken).Forget();
                    break;
                case CardEffectType.RiskyDice:
                    ExecuteRiskyDice(gainedCard, destroyCancellationToken).Forget();
                    break;
                case CardEffectType.DuplicateCard:
                    ExecuteDuplicateCard(gainedCard);
                    break;
                case CardEffectType.None:
                default:
                    break;
            }
            effectActivated.OnNext((effect, gainedCard));
        }

        /// <summary>剣: 周囲の未カード化Dランクアイテムを1つ、Cランク定義に錬金する</summary>
        private void ExecuteSwordAlchemy(CardizableItemBase sword)
        {
            if (alchemyResultPool.Count == 0)
            {
                return;
            }
            CardizableItemBase target = null;
            var nearestSqr = swordAlchemyRadius * swordAlchemyRadius;
            foreach (var item in CardizableItemBase.ActiveItems)
            {
                if (item == sword
                    || item.State.CurrentValue != CardizableState.Item
                    || item.Definition == null
                    || item.Definition.Rank != CardRank.D)
                {
                    continue;
                }
                var sqr = (item.transform.position - sword.transform.position).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    target = item;
                }
            }
            if (target != null)
            {
                var newDef = alchemyResultPool[UnityEngine.Random.Range(0, alchemyResultPool.Count)];
                target.OverrideDefinition(newDef);
                GIPokerAssets.SpawnVfx(GIPokerAssets.GainVfx, target.transform.position);
            }
        }

        /// <summary>錬金キット: バインダー内のDランクカード1枚をCランクに変換する</summary>
        private void ExecuteAlchemyKit(CardizableItemBase kit)
        {
            if (alchemyResultPool.Count == 0)
            {
                return;
            }
            var binder = CardBinder.FindNearest(kit.transform.position);
            if (binder == null)
            {
                return;
            }
            foreach (var instance in binder.Cards)
            {
                if (instance.Rank == CardRank.D)
                {
                    var newDef = alchemyResultPool[UnityEngine.Random.Range(0, alchemyResultPool.Count)];
                    binder.TryConvertCard(instance, newDef);
                    return;
                }
            }
        }

        /// <summary>ギャラリーフェイク: バインダー内のランダムな1枚を複製する（SS・A級は複製不可）</summary>
        private void ExecuteDuplicateCard(CardizableItemBase source)
        {
            var binder = CardBinder.FindNearest(source.transform.position);
            if (binder == null || binder.Cards.Count == 0)
            {
                return;
            }
            var candidates = new List<CardInstance>();
            foreach (var instance in binder.Cards)
            {
                if (instance.Rank != CardRank.SS && instance.Rank != CardRank.A)
                {
                    candidates.Add(instance);
                }
            }
            if (candidates.Count == 0)
            {
                return;
            }
            var picked = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            binder.StoreCopy(picked.Definition);
        }

        /// <summary>
        /// リモートクライアントで発動した全体効果を自分の空間にも適用する
        /// （CardNetworkSync が GiEffect 受信時に呼ぶ）。
        /// </summary>
        public void ApplyEffectRemote(CardEffectType effect)
        {
            switch (effect)
            {
                case CardEffectType.SlowFloating:
                    ExecuteSlowFloating(destroyCancellationToken).Forget();
                    break;
                case CardEffectType.PauseCountdown:
                    ExecutePauseCountdown(destroyCancellationToken).Forget();
                    break;
            }
        }

        /// <summary>錬金の変換先プールから cardId で定義を検索する（GiConvert 受信用）</summary>
        public CardDefinition FindAlchemyDefinition(string cardId)
        {
            foreach (var def in alchemyResultPool)
            {
                if (def != null && def.CardId == cardId)
                {
                    return def;
                }
            }
            return null;
        }

        private async UniTask ExecuteSlowFloating(CancellationToken ct)
        {
            CardizableItemBase.SetGlobalFloatTimeScale(slowFloatingScale);
            await UniTask.Delay(TimeSpan.FromSeconds(slowFloatingDuration), cancellationToken: ct);
            CardizableItemBase.SetGlobalFloatTimeScale(1f);
        }

        private async UniTask ExecutePauseCountdown(CancellationToken ct)
        {
            foreach (var item in CardizableItemBase.ActiveItems)
            {
                var s = item.State.CurrentValue;
                if (s == CardizableState.FloatingCard || s == CardizableState.Held)
                {
                    item.PauseCountdown();
                }
            }
            await UniTask.Delay(TimeSpan.FromSeconds(pauseCountdownDuration), cancellationToken: ct);
            foreach (var item in CardizableItemBase.ActiveItems)
            {
                item.ResumeCountdown();
            }
        }

        private async UniTask ExecuteRiskyDice(CardizableItemBase dice, CancellationToken ct)
        {
            var dicePosition = dice.transform.position;
            var roll = UnityEngine.Random.Range(1, 21);
            var result = new DiceResult(roll, roll >= blessThreshold, roll <= curseThreshold, dicePosition);
            diceRolled.OnNext(result);

            // 演出（ダイスが転がる時間）を待ってから効果を適用する
            await UniTask.Delay(TimeSpan.FromSeconds(2f), cancellationToken: ct);

            if (result.IsBless)
            {
                GIPokerAssets.SpawnVfx(GIPokerAssets.DiceBlessVfx, dicePosition, 8f);
                var cam = Camera.main;
                var position = cam != null
                    ? cam.transform.position + cam.transform.forward * 0.8f
                    : dicePosition + Vector3.up * 0.5f;
                SpawnSsCardAt(position, announce: true);
            }
            else if (result.IsCurse)
            {
                GIPokerAssets.SpawnVfx(GIPokerAssets.DiceCurseVfx, dicePosition, 8f);
                var binder = CardBinder.FindNearest(dicePosition);
                if (binder != null)
                {
                    var lost = binder.DiscardRandom(curseDiscardCount);
                    Debug.Log($"[CardEffectService] 大凶! バインダーのカード{lost}枚が破壊された");
                }
            }
        }

        /// <summary>
        /// 大吉: SSランクアイテムをカード化済み状態で出現させる。
        /// ローカル発動時は announce=true（SsSpawned が流れ、CardNetworkSync が配信する）、
        /// リモートの GiSpawnSS 受信適用時は announce=false で呼ぶ。
        /// </summary>
        public CardizableItemBase SpawnSsCardAt(Vector3 position, bool announce)
        {
            if (ssItemPrefab == null)
            {
                Debug.LogWarning("[CardEffectService] SSアイテムプレハブが未設定です");
                return null;
            }
            var go = Instantiate(ssItemPrefab, position, Quaternion.identity);
            var item = go.GetComponent<CardizableItemBase>();
            if (item != null)
            {
                item.TryCardify();
                if (announce)
                {
                    ssSpawned.OnNext(item);
                }
            }
            return item;
        }
    }
}
