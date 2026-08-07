using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// 現実空間に配置される「カード化可能アイテム」の基底コンポーネント。
    ///
    /// - ハンドボーンのコライダー（handLayerMask で指定）がトリガーに触れるとカード化する
    /// - カード化後は DOTween でぷかぷか浮遊する
    /// - カード化から cardLifetimeSeconds（既定60秒 = 1分ルール）以内に
    ///   バインダーへ収納されなければ消滅する
    /// - 一度ゲイン（具現化）したオブジェクトは二度とカード化できない
    /// - ゲイン後のオブジェクトは再びピンチで掴めるが、離すと重力に従って落下・接地する
    ///   通常の物理オブジェクトになる（トリガーだったコライダーも実体コライダーに切り替わる）
    ///
    /// 個別アイテム固有の演出・効果は本クラスを継承して
    /// OnCardified / OnGained / OnExpired をオーバーライドする。
    /// </summary>
    [DisallowMultipleComponent]
    public class CardizableItemBase : MonoBehaviour, ICardizable
    {
        /// <summary>シーン上でアクティブな全カード化可能アイテム（効果システムが参照する）</summary>
        public static readonly List<CardizableItemBase> ActiveItems = new();

        /// <summary>浮遊アニメーションの全体倍率（「占い水晶」効果で一時的に下げる）</summary>
        private static float globalFloatTimeScale = 1f;

        // カード化⇔ゲイン時に追加再生する煙VFX（BinderSmokeVfx＝VFX_BinderSummonPoof）のスケール。
        // プレハブはバインダー実寸（幅約0.45m）基準で作り込まれているため、
        // カード実寸（CardVisualFactory基準、縦88mm）に合わせて縮小する。
        private const float CardSmokeVfxScale = 0.18f;

        // 上記煙VFXの再生時間（秒）。BinderView.SmokeVfxLifetime（1.5秒）と同じ値を用いる
        // （Smoke/ShockWaveが再生し切るまでの余裕を持たせた値）。
        private const float CardSmokeVfxLifetime = 1.5f;

        [Header("カード定義")]
        [SerializeField, Tooltip("このアイテムのカード定義（ScriptableObject）")]
        private CardDefinition definition;

        [Header("1分ルール")]
        [SerializeField, Tooltip("カード化してから消滅するまでの秒数（1分ルール）")]
        private float cardLifetimeSeconds = 60f;

        [Header("カード化トリガー")]
        [SerializeField, Tooltip("『素手で触れた』とみなすハンドボーンコライダーのレイヤー")]
        private LayerMask handLayerMask;

        [Header("ビジュアル切り替え")]
        [SerializeField, Tooltip("アイテム状態のときに表示するルート（任意）")]
        private GameObject itemVisualRoot;
        [SerializeField, Tooltip("カード状態のときに表示するルート（任意）")]
        private GameObject cardVisualRoot;

        [Header("浮遊演出 (DOTween)")]
        [SerializeField, Tooltip("上下浮遊の振幅（メートル）")]
        private float floatAmplitude = 0.05f;
        [SerializeField, Tooltip("上下浮遊の片道時間（秒）")]
        private float floatDuration = 1.2f;
        [SerializeField, Tooltip("Y軸1回転にかかる時間（秒）")]
        private float rotateDuration = 6f;

        private readonly ReactiveProperty<CardizableState> state = new(CardizableState.Item);
        private readonly ReactiveProperty<float> remainingLifetime = new(0f);
        private readonly ReactiveProperty<bool> isCountdownPaused = new(false);
        private readonly Subject<CardDefinition> definitionChanged = new();

        private CancellationTokenSource lifetimeCts;
        private Tween floatTween;
        private Tween rotateTween;
        private bool hasBeenGained;
        private CardInstance cardInstance;

        public ReadOnlyReactiveProperty<CardizableState> State => state;
        public ReadOnlyReactiveProperty<float> RemainingLifetime => remainingLifetime;
        public ReadOnlyReactiveProperty<bool> IsCountdownPaused => isCountdownPaused;
        public CardDefinition Definition => definition;

        /// <summary>錬金などで定義が差し替わったときに流れる（ネットワーク同期用）</summary>
        public Observable<CardDefinition> DefinitionChanged => definitionChanged;

        /// <summary>マルチユーザー同期用のID（CardNetworkSync が割り当てる。未登録は -1）</summary>
        public int NetworkId { get; set; } = -1;

        /// <summary>カード化後に生成されるランタイムインスタンス（未カード化なら null）</summary>
        public CardInstance CardInstance => cardInstance;

        public bool CanCardify => state.Value == CardizableState.Item && !hasBeenGained;

        /// <summary>浮遊中のカード、またはゲイン済みアイテムを掴める（Held/GainedHeld/Stored は掴めない）</summary>
        public bool CanBeGrabbed =>
            state.Value == CardizableState.FloatingCard || state.Value == CardizableState.Gained;

        private Renderer[] fallbackRenderers;

        protected virtual void Awake()
        {
            // ビジュアルルート未設定時のフォールバック用に自前のレンダラーを控えておく
            if (itemVisualRoot == null && cardVisualRoot == null)
            {
                fallbackRenderers = GetComponentsInChildren<Renderer>(true);
            }
            ApplyVisual(isCard: false);
        }

        protected virtual void OnEnable()
        {
            ActiveItems.Add(this);
        }

        protected virtual void OnDisable()
        {
            ActiveItems.Remove(this);
        }

        protected virtual void OnDestroy()
        {
            CancelLifetime();
            KillFloatingTweens();
            state.Dispose();
            remainingLifetime.Dispose();
            isCountdownPaused.Dispose();
            definitionChanged.Dispose();
        }

        private void OnTriggerEnter(Collider other)
        {
            // ハンドボーンのコライダー以外（他のアイテムや環境）では発火させない
            if ((handLayerMask.value & (1 << other.gameObject.layer)) == 0)
            {
                return;
            }
            TryCardify();
        }

        public bool TryCardify()
        {
            if (!CanCardify)
            {
                return false;
            }

            cardInstance = new CardInstance(definition, this);

            // カード見た目が未設定なら、定義情報からその場で自動生成する
            if (cardVisualRoot == null)
            {
                cardVisualRoot = CardVisualFactory.Create(transform, definition);
                CardLifetimeLabel.Attach(cardVisualRoot, this);
            }

            state.Value = CardizableState.FloatingCard;
            ApplyVisual(isCard: true);
            PlayFloatingAnimation();
            RestartLifetimeCountdown();
            OnCardified();
            return true;
        }

        /// <summary>
        /// カード定義を差し替える（「剣」「錬金キット」の錬金効果用）。
        /// カード化済みの場合は見た目も作り直す。
        /// </summary>
        public void OverrideDefinition(CardDefinition newDefinition)
        {
            definition = newDefinition;
            cardInstance?.ReplaceDefinition(newDefinition);
            if (cardVisualRoot != null && CardVisualFactory.IsGenerated(cardVisualRoot))
            {
                var wasActive = cardVisualRoot.activeSelf;
                Destroy(cardVisualRoot);
                cardVisualRoot = CardVisualFactory.Create(transform, newDefinition);
                cardVisualRoot.SetActive(wasActive);
            }
            definitionChanged.OnNext(newDefinition);
        }

        /// <summary>浮遊アニメーションの全体倍率を変更する（「占い水晶」効果用）</summary>
        public static void SetGlobalFloatTimeScale(float timeScale)
        {
            globalFloatTimeScale = Mathf.Max(0.01f, timeScale);
            foreach (var item in ActiveItems)
            {
                item.ApplyFloatTimeScale();
            }
        }

        private void ApplyFloatTimeScale()
        {
            if (floatTween != null)
            {
                floatTween.timeScale = globalFloatTimeScale;
            }
            if (rotateTween != null)
            {
                rotateTween.timeScale = globalFloatTimeScale;
            }
        }

        public void PauseCountdown() => isCountdownPaused.Value = true;

        public void ResumeCountdown() => isCountdownPaused.Value = false;

        /// <summary>
        /// ピンチジェスチャーで掴む（CardGrabInteractor から呼ばれる）。
        /// 掴んでいる間も1分ルールのカウントダウンは進行し続ける。
        /// </summary>
        public bool TryGrab()
        {
            if (!CanBeGrabbed)
            {
                return false;
            }
            if (state.Value == CardizableState.Gained)
            {
                // ゲイン後は手の動きに追従させたいので、落下中でも運動学に戻して手元に固定する
                SetKinematic(true);
                state.Value = CardizableState.GainedHeld;
                return true;
            }
            KillFloatingTweens();
            state.Value = CardizableState.Held;
            return true;
        }

        /// <summary>
        /// 掴んでいたものを離す。
        /// カードの場合はバインダー圏外で離した場合その場で再浮遊し、
        /// ゲイン後のアイテムの場合は以後重力に従って落下する物理オブジェクトになる。
        /// </summary>
        public void Release()
        {
            switch (state.Value)
            {
                case CardizableState.Held:
                    // バインダーから取り出した直後は1分ルールのカウントダウンを一時停止しているため
                    // （ReleaseFromBinder 参照）、ゲインも再収納もせず手放したこのタイミングで再開する
                    // （バグ修正2）。バインダー経由でない通常のカード化直後の掴み・解放では
                    // 既に停止していないため、この呼び出しは実質的な no-op になる。
                    ResumeCountdown();
                    state.Value = CardizableState.FloatingCard;
                    PlayFloatingAnimation();
                    break;
                case CardizableState.GainedHeld:
                    state.Value = CardizableState.Gained;
                    DropWithGravity();
                    break;
            }
        }

        /// <summary>
        /// バインダーへの収納が確定したときに CardBinder から呼ばれる。
        /// カウントダウンを停止し、シーン上の実体を非表示にする。
        /// </summary>
        public void MarkStored()
        {
            CancelLifetime();
            KillFloatingTweens();
            remainingLifetime.Value = 0f;
            state.Value = CardizableState.Stored;
            gameObject.SetActive(false);
        }

        /// <summary>
        /// バインダーから取り出されたときに CardBinder から呼ばれる。
        /// 指定位置で再浮遊するが、1分ルールのカウントダウンは仕切り直した上で
        /// 一時停止した状態にする（バグ修正2）。
        /// 取り出してすぐゲインしようとしているだけなのに、もたついたら消滅するのは
        /// 理不尽なため、掴んでいる間は停止のままにし、ゲインもバインダーへの再収納もせず
        /// 手放した場合のみ Release() 内で再開する。
        /// </summary>
        public void ReleaseFromBinder(Vector3 position)
        {
            if (state.Value != CardizableState.Stored)
            {
                return;
            }
            transform.position = position;
            gameObject.SetActive(true);
            state.Value = CardizableState.FloatingCard;
            ApplyVisual(isCard: true);
            PlayFloatingAnimation();
            RestartLifetimeCountdown();
            PauseCountdown();
        }

        /// <summary>
        /// ゲイン（具現化）する。カードが元のアイテムに戻り、効果が発動する。
        /// ゲインしたオブジェクトは二度とカード化できない（ゲイン制限）。
        /// 効果の実処理は OnGained のオーバーライド、または外部の効果システムが
        /// State を購読して行う。
        /// </summary>
        public bool TryGain()
        {
            if (state.Value != CardizableState.Held && state.Value != CardizableState.FloatingCard)
            {
                return false;
            }
            CancelLifetime();
            KillFloatingTweens();
            remainingLifetime.Value = 0f;
            hasBeenGained = true;
            state.Value = CardizableState.Gained;
            ApplyVisual(isCard: false);
            OnGained();
            return true;
        }

        private void RestartLifetimeCountdown()
        {
            CancelLifetime();
            lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            remainingLifetime.Value = cardLifetimeSeconds;
            isCountdownPaused.Value = false;
            CountdownAsync(lifetimeCts.Token).Forget();
        }

        private async UniTask CountdownAsync(CancellationToken ct)
        {
            while (remainingLifetime.Value > 0f)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                if (isCountdownPaused.Value)
                {
                    continue;
                }
                remainingLifetime.Value = Mathf.Max(0f, remainingLifetime.Value - Time.deltaTime);
            }
            Expire();
        }

        private void Expire()
        {
            CancelLifetime();
            KillFloatingTweens();
            state.Value = CardizableState.Expired;
            OnExpired();
        }

        private void CancelLifetime()
        {
            lifetimeCts?.Cancel();
            lifetimeCts?.Dispose();
            lifetimeCts = null;
        }

        private void ApplyVisual(bool isCard)
        {
            if (itemVisualRoot != null)
            {
                itemVisualRoot.SetActive(!isCard);
            }
            else if (fallbackRenderers != null)
            {
                // アイテム見た目ルート未設定時は自前のレンダラーごと切り替える
                foreach (var r in fallbackRenderers)
                {
                    if (r != null && (cardVisualRoot == null || !r.transform.IsChildOf(cardVisualRoot.transform)))
                    {
                        r.enabled = !isCard;
                    }
                }
            }
            if (cardVisualRoot != null)
            {
                cardVisualRoot.SetActive(isCard);
            }
        }

        /// <summary>浮遊アニメーションを開始する（キラキラ等の追加演出はオーバーライドで）</summary>
        protected virtual void PlayFloatingAnimation()
        {
            KillFloatingTweens();
            floatTween = transform
                .DOMoveY(transform.position.y + floatAmplitude, floatDuration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetLink(gameObject);
            rotateTween = transform
                .DORotate(new Vector3(0f, 360f, 0f), rotateDuration, RotateMode.FastBeyond360)
                .SetEase(Ease.Linear)
                .SetLoops(-1, LoopType.Incremental)
                .SetLink(gameObject);
            ApplyFloatTimeScale();
        }

        private void KillFloatingTweens()
        {
            floatTween?.Kill();
            floatTween = null;
            rotateTween?.Kill();
            rotateTween = null;
        }

        /// <summary>手に追従させるため運動学に固定する（重力オフ）</summary>
        private void SetKinematic(bool kinematic)
        {
            var rb = EnsureRigidbody();
            rb.isKinematic = kinematic;
            rb.useGravity = false;
        }

        /// <summary>以後は物理エンジンに委ねて落下・接地させる</summary>
        private void DropWithGravity()
        {
            var rb = EnsureRigidbody();
            rb.isKinematic = false;
            rb.useGravity = true;

            // タッチ/掴み判定用のトリガーだったコライダーを、床等と衝突する実体コライダーに切り替える
            var col = GetComponent<Collider>();
            if (col != null)
            {
                col.isTrigger = false;
            }
        }

        private Rigidbody EnsureRigidbody()
        {
            var rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
            }
            return rb;
        }

        /// <summary>カード化した瞬間の演出フック（パーティクル・SE など）</summary>
        protected virtual void OnCardified()
        {
            GIPokerAssets.SpawnVfx(GIPokerAssets.CardifyVfx, transform.position);
            PlayCardSizedSmokeVfx();
        }

        /// <summary>ゲイン（具現化）した瞬間の演出・効果発動フック</summary>
        protected virtual void OnGained()
        {
            GIPokerAssets.SpawnVfx(GIPokerAssets.GainVfx, transform.position);
            PlayCardSizedSmokeVfx();
        }

        /// <summary>
        /// カード化⇔ゲイン（アイテム化）の瞬間に、バインダー出し入れ（VFX_BinderSummonPoof）と
        /// 同じ白煙＋衝撃波VFXをカードサイズに縮小して追加再生する。
        /// バインダーはバインダー実寸（幅約0.45m）に対して等倍で作り込まれているため、
        /// カード実寸（CardVisualFactory基準の縦88mm）に合わせて CardSmokeVfxScale で縮小する。
        /// </summary>
        private void PlayCardSizedSmokeVfx()
        {
            GIPokerAssets.SpawnVfx(GIPokerAssets.BinderSmokeVfx, transform.position,
                lifetime: CardSmokeVfxLifetime, scale: CardSmokeVfxScale);
        }

        /// <summary>1分ルールで消滅するときの演出フック。既定では縮小して破棄する</summary>
        protected virtual void OnExpired()
        {
            GIPokerAssets.SpawnVfx(GIPokerAssets.ExpireVfx, transform.position);
            transform
                .DOScale(Vector3.zero, 0.4f)
                .SetEase(Ease.InBack)
                .SetLink(gameObject)
                .OnComplete(() => Destroy(gameObject));
        }
    }
}
