using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
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
    /// - リスキーダイス（RiskyDice）: ゲイン後、掴んで離した瞬間に転がる演出を再生し、
    ///   演出終了時に必ず大吉として解決する（ロール自体はダイス個体ごとに初回の1回のみ発火）。
    ///   ただしSSカードは直接出現させない。「①（カードを1枚出現させる効果。未実装）を使うと
    ///   必ずSSが出る」という権利フラグ（HasPendingDiceJackpot / ConsumeDiceJackpot）を立てる
    ///   だけに留め、実出現は①側が消費するタイミングに委ねる（同一クライアント内に複数個体
    ///   出現しうるSS出現上限「1枚」を、この権利フラグ自体を初回のみに限定することで守る）
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
        [SerializeField, Tooltip("ゲイン後のダイスを離してから大吉が確定するまでの演出時間（秒）")]
        private float diceRollAnimationSeconds = 2.2f;

        /// <summary>「転がっている最中」VFXの生存時間（秒）。ローカル発火／リモートRPC受信の両方で同じ値を使う</summary>
        public const float DiceRollVfxLifetimeSeconds = 2.5f;

        private readonly Subject<DiceResult> diceRolled = new();
        private readonly Subject<(CardEffectType effect, CardizableItemBase card)> effectActivated = new();
        private readonly Subject<CardizableItemBase> ssSpawned = new();
        private readonly Subject<Vector3> diceRollStarted = new();

        /// <summary>既に1回ロール済みのリスキーダイス個体（1回制限。演出は個体ごとに1度きり）</summary>
        private readonly HashSet<CardizableItemBase> rolledRiskyDice = new();

        /// <summary>ロール開始済みだがまだ解決（大吉確定）していないダイス個体。
        /// 演出中に再グラブ→リリースされた際、実物理への復帰を上書きしてスクリプト制御を維持するために使う</summary>
        private readonly HashSet<CardizableItemBase> pendingRiskyDiceRolls = new();

        /// <summary>再生中の転がり演出Tween（再グラブ時に止めるため個体ごとに保持する）</summary>
        private readonly Dictionary<CardizableItemBase, Tween> activeRollTweens = new();

        /// <summary>大吉により「①（カードを1枚出現させる効果。未実装）を使うと必ずSSが出る」権利が
        /// 有効な間だけ true。①側が消費するまで有効で、消費されると false に戻る</summary>
        private bool riskyDiceJackpotPending;

        /// <summary>クライアント内で一度でも大吉の権利を立てたことがあるか。
        /// 一度trueになったら以後は二度と riskyDiceJackpotPending を立てない
        /// （SSカード出現上限「1枚」を守るためのガード）</summary>
        private bool riskyDiceJackpotEverGranted;

        /// <summary>ダイスの結果イベント（演出側・ネットワーク同期が購読する）</summary>
        public Observable<DiceResult> DiceRolled => diceRolled;

        /// <summary>効果発動イベント（演出・SE・ネットワーク同期用）</summary>
        public Observable<(CardEffectType effect, CardizableItemBase card)> EffectActivated => effectActivated;

        /// <summary>大吉によるSSカード出現イベント（ネットワーク同期用）</summary>
        public Observable<CardizableItemBase> SsSpawned => ssSpawned;

        /// <summary>リスキーダイスが転がり始めたイベント（位置。ネットワーク同期・演出用）</summary>
        public Observable<Vector3> DiceRollStarted => diceRollStarted;

        /// <summary>
        /// 大吉により「次に①（カードを1枚出現させる効果。未実装）を使うと必ずSSが出る」権利が
        /// 有効かどうか。①実装時に、このプロパティが true なら `SpawnSsCardAt(...)` を呼んだ上で
        /// `ConsumeDiceJackpot()` を呼ぶ使い方を想定している。
        /// </summary>
        public bool HasPendingDiceJackpot => riskyDiceJackpotPending;

        /// <summary>
        /// ①側が大吉の権利を消費してSSを出現させたときに呼ぶ。
        /// 以後は同じ権利は二度と立たない（SS出現上限「1枚」を守るため）。
        /// </summary>
        public void ConsumeDiceJackpot()
        {
            riskyDiceJackpotPending = false;
        }

        private void OnDestroy()
        {
            diceRolled.Dispose();
            effectActivated.Dispose();
            ssSpawned.Dispose();
            diceRollStarted.Dispose();
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
                    PrepareRiskyDiceRoll(gainedCard);
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

        /// <summary>
        /// ゲインした瞬間に一度だけ呼ばれる。以後このダイスの State 遷移を購読し、
        /// 「ゲイン後に掴んで離す」操作を検知したときだけロール演出を開始する
        /// （CardNetworkSync.Register と同じ previous/current 手動追跡パターン）。
        /// </summary>
        private void PrepareRiskyDiceRoll(CardizableItemBase dice)
        {
            var previous = dice.State.CurrentValue;
            dice.State
                .Subscribe(current =>
                {
                    var prev = previous;
                    previous = current;

                    if (current == CardizableState.GainedHeld
                        && activeRollTweens.TryGetValue(dice, out var runningTween))
                    {
                        // 再グラブされた: FollowPinchPose のTransform操作と競合しないよう演出Tweenは止める
                        // （大吉確定自体はUniTask.Delayの固定時間待機側で継続するため止まらない）
                        runningTween.Kill();
                        activeRollTweens.Remove(dice);
                    }

                    if (current == CardizableState.Gained && pendingRiskyDiceRolls.Contains(dice))
                    {
                        // ロール解決前（UniTask.Delay待機中）に再グラブ→リリースされたケース。
                        // Release() の DropWithGravity() が isKinematic=false/useGravity=true に
                        // 戻してしまうため、大吉確定までスクリプト制御であることを上書きし直す
                        // （実物理で早期に転がり出してしまうのを防ぐ）
                        var rb = dice.GetComponent<Rigidbody>();
                        if (rb != null)
                        {
                            rb.isKinematic = true;
                            rb.useGravity = false;
                            rb.linearVelocity = Vector3.zero;
                            rb.angularVelocity = Vector3.zero;
                        }
                    }

                    if (ShouldStartRoll(prev, current, rolledRiskyDice.Contains(dice)))
                    {
                        rolledRiskyDice.Add(dice);
                        ExecuteRiskyDiceRoll(dice, destroyCancellationToken).Forget();
                    }
                })
                .AddTo(dice);
        }

        /// <summary>
        /// ロール演出を開始すべき状態遷移かどうかを判定する純粋関数。
        /// ゲイン後のダイスを「掴んで（GainedHeld）→離す（Gained）」瞬間かつ、
        /// まだ一度もロールしていない個体のときのみ true になる。
        /// </summary>
        public static bool ShouldStartRoll(CardizableState previous, CardizableState current, bool alreadyRolled)
        {
            return previous == CardizableState.GainedHeld
                && current == CardizableState.Gained
                && !alreadyRolled;
        }

        private async UniTask ExecuteRiskyDiceRoll(CardizableItemBase dice, CancellationToken ct)
        {
            var startPosition = dice.transform.position;
            pendingRiskyDiceRolls.Add(dice);

            // 実物理を止めてスクリプト演出に切り替える（DropWithGravity() により既に存在する想定）
            var rb = dice.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            // 「転がっている最中」の演出（既存のキラキラVFXを流用）
            GIPokerAssets.SpawnVfx(GIPokerAssets.CardSparkleVfx, startPosition, DiceRollVfxLifetimeSeconds);
            diceRollStarted.OnNext(startPosition);

            activeRollTweens[dice] = BuildRollSequence(dice.transform, startPosition, diceRollAnimationSeconds);

            // 大吉確定はTween完了コールバックに依存させず、固定時間待機で必ず行う
            await UniTask.Delay(TimeSpan.FromSeconds(diceRollAnimationSeconds), cancellationToken: ct);

            if (activeRollTweens.TryGetValue(dice, out var tween))
            {
                activeRollTweens.Remove(dice);
                tween.Complete();
            }

            // 待機中にダイスのGameObjectが破棄されていても、解決処理（VFX・SS出現・文字表示）は必ず行う。
            // その場合は破棄前の startPosition にフォールバックする
            var diceAlive = dice != null;
            var resolvePosition = diceAlive ? dice.transform.position : startPosition;

            // 今回は必ず大吉として確定する（大凶はスコープ外）
            var result = new DiceResult(roll: 20, isBless: true, isCurse: false, resolvePosition);
            diceRolled.OnNext(result);

            GIPokerAssets.SpawnVfx(GIPokerAssets.DiceBlessVfx, resolvePosition, 8f);

            // SSカードはここでは直接出現させない。「①（カードを1枚出現させる効果。未実装）を
            // 使うと必ずSSが出る」権利フラグを立てるだけに留める（実出現は①側が消費するタイミングに委ねる）。
            // クライアント内で一度でも権利を立てたら、以後は二度と立てない（SS出現上限「1枚」を守るため）
            if (!riskyDiceJackpotEverGranted)
            {
                riskyDiceJackpotPending = true;
                riskyDiceJackpotEverGranted = true;
            }

            DiceResultLabel.ShowAt(resolvePosition, "大吉");

            pendingRiskyDiceRolls.Remove(dice);

            // 演出後、まだゲイン済みのまま（=誰にも再収納/破壊されていない）なら通常の物理小物に戻す
            if (diceAlive && rb != null && dice.State.CurrentValue == CardizableState.Gained)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
            }
        }

        /// <summary>
        /// バウンド→回転しながら転がる→減速して停止する見た目のみのDOTween演出を構築して再生する。
        /// 合計時間は duration に一致させる（大吉確定の待機時間と同期させるため）。
        /// </summary>
        private static Tween BuildRollSequence(Transform diceTransform, Vector3 startPosition, float duration)
        {
            var bounceUp = duration * 0.12f;
            var bounceDown = duration * 0.18f;
            var rollDuration = Mathf.Max(0.1f, duration - bounceUp - bounceDown);

            var rollDirection = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward;
            var rollDistance = UnityEngine.Random.Range(0.2f, 0.45f);
            var endPosition = startPosition + rollDirection * rollDistance;

            var sequence = DOTween.Sequence().SetLink(diceTransform.gameObject);
            sequence.Append(diceTransform.DOMoveY(startPosition.y + 0.12f, bounceUp).SetEase(Ease.OutQuad));
            sequence.Append(diceTransform.DOMoveY(startPosition.y, bounceDown).SetEase(Ease.InQuad));
            sequence.Join(diceTransform.DOMove(endPosition, bounceDown + rollDuration).SetEase(Ease.OutCubic));
            sequence.Insert(0f, diceTransform.DORotate(
                new Vector3(
                    UnityEngine.Random.Range(360f, 720f),
                    UnityEngine.Random.Range(180f, 540f),
                    UnityEngine.Random.Range(360f, 720f)),
                duration, RotateMode.FastBeyond360).SetEase(Ease.OutCubic));

            return sequence.Play();
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
