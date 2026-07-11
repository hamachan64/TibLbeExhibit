using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// MRステージ「島の侵蝕」のステージフェーズ（docs/MR_STAGE_PLAN.md §3 / §6.1）。
    /// </summary>
    public enum StagePhase { Reality, Awakening, Eroded, Unstable, Receding }

    /// <summary>
    /// Environment シーン常駐。侵蝕演出の進行を一元管理する（docs/MR_STAGE_PLAN.md §6.1）。
    ///
    /// - GameSession.State / RemainingTime を購読して StagePhase を導出する
    ///   （WaitingForStart→Reality / Tutorial→Awakening / Playing→Eroded /
    ///     Playing残り60秒→Unstable / Finished→Receding）
    /// - フェーズ遷移時に DOTween で ErosionProgress（侵蝕度 0〜1）を滑らかに遷移させる
    /// - 侵蝕度はグローバルシェーダープロパティ _GIP_Erosion として全シェーダーに配信する
    ///   （各要素が個別に購読するより安価で一貫する）
    /// - Unstable の「縁の金色化・明滅」用に補助グローバル _GIP_Unstable（0〜1）も配信する
    /// - GameSession は UserClient シーン側にあるため、ItemSpawnManager と同様に
    ///   UniTask.WaitUntil でマルチシーンのロードを待つ
    /// </summary>
    public class StageDirector : MonoBehaviour
    {
        public static StageDirector Instance { get; private set; }

        [SerializeField, Tooltip("Awakening（チュートリアル中）の侵蝕度の到達値")]
        private float awakeningErosion = 0.4f;

        [SerializeField, Tooltip("Awakening の侵蝕遷移にかける秒数（じわっと染み込む）")]
        private float awakeningDurationSeconds = 20f;

        [SerializeField, Tooltip("Eroded（本編開始）で侵蝕度 1.0 に到達するまでの秒数")]
        private float erodedDurationSeconds = 6f;

        [SerializeField, Tooltip("Receding（タイムアップ）で侵蝕が引いて 0 に戻るまでの秒数")]
        private float recedingDurationSeconds = 8f;

        [SerializeField, Tooltip("Playing の残り時間がこの秒数以下になったら Unstable へ移行する")]
        private float unstableThresholdSeconds = 60f;

        [SerializeField, Tooltip("Unstable の金色化（_GIP_Unstable 0→1）にかける秒数")]
        private float unstableBlendSeconds = 1.5f;

        /// <summary>侵蝕度のグローバルシェーダープロパティID</summary>
        private static readonly int ErosionPropertyId = Shader.PropertyToID("_GIP_Erosion");

        /// <summary>Unstable 演出（金色化）のグローバルシェーダープロパティID</summary>
        private static readonly int UnstablePropertyId = Shader.PropertyToID("_GIP_Unstable");

        private readonly ReactiveProperty<float> erosionProgress = new(0f);
        private readonly ReactiveProperty<StagePhase> phase = new(StagePhase.Reality);

        private Tween erosionTween;
        private Tween unstableTween;
        private float unstableBlend;

        /// <summary>侵蝕度 0=現実 → 1=完全侵蝕。DOTween で滑らかに遷移する。</summary>
        public ReadOnlyReactiveProperty<float> ErosionProgress => erosionProgress;

        /// <summary>現在のステージフェーズ。</summary>
        public ReadOnlyReactiveProperty<StagePhase> Phase => phase;

        private void Awake()
        {
            Instance = this;
            // 前回セッション・エディタの残留値をリセットしてから開始する
            SetErosion(0f);
            SetUnstableBlend(0f);
        }

        private void Start()
        {
            WaitAndBindAsync(destroyCancellationToken).Forget();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            erosionTween?.Kill();
            unstableTween?.Kill();
            // グローバル値を残すと次のシーン・エディタ編集画面に漏れるため戻す
            Shader.SetGlobalFloat(ErosionPropertyId, 0f);
            Shader.SetGlobalFloat(UnstablePropertyId, 0f);
            erosionProgress.Dispose();
            phase.Dispose();
        }

        private async UniTask WaitAndBindAsync(CancellationToken ct)
        {
            // GameSession は UserClient シーン側にあるため、マルチシーンのロードを待つ
            await UniTask.WaitUntil(() => GameSession.Instance != null, cancellationToken: ct);

            var session = GameSession.Instance;
            session.State
                .Subscribe(_ => EvaluatePhase())
                .AddTo(this);
            session.RemainingTime
                .Subscribe(_ => EvaluatePhase())
                .AddTo(this);
        }

        /// <summary>
        /// GameSession の現在値から StagePhase を導出し、変化していればフェーズ遷移を行う。
        /// SessionState に新しい状態が追加されても壊れないよう、既知の状態のみ判定する。
        /// </summary>
        private void EvaluatePhase()
        {
            var session = GameSession.Instance;
            if (session == null)
            {
                return;
            }

            var next = DerivePhase(session.State.CurrentValue, session.RemainingTime.CurrentValue);
            if (next == phase.Value)
            {
                return;
            }

            Debug.Log($"[StageDirector] フェーズ遷移: {phase.Value} → {next}");
            phase.Value = next;
            OnPhaseChanged(next);
        }

        /// <summary>SessionState と残り時間からステージフェーズを導出する（MR_STAGE_PLAN.md §3）</summary>
        private StagePhase DerivePhase(SessionState state, float remainingTime)
        {
            return state switch
            {
                SessionState.WaitingForStart => StagePhase.Reality,
                SessionState.Tutorial => StagePhase.Awakening,
                SessionState.Playing => remainingTime <= unstableThresholdSeconds
                    ? StagePhase.Unstable
                    : StagePhase.Eroded,
                SessionState.Finished => StagePhase.Receding,
                _ => phase.Value, // 未知の状態では現在フェーズを維持する
            };
        }

        /// <summary>フェーズ遷移に応じて侵蝕度・Unstable 演出値のトゥイーンを張り替える</summary>
        private void OnPhaseChanged(StagePhase next)
        {
            switch (next)
            {
                case StagePhase.Reality:
                    TweenErosion(0f, 1f, Ease.OutQuad);
                    TweenUnstable(0f);
                    break;
                case StagePhase.Awakening:
                    TweenErosion(awakeningErosion, awakeningDurationSeconds, Ease.InOutSine);
                    TweenUnstable(0f);
                    break;
                case StagePhase.Eroded:
                    TweenErosion(1f, erodedDurationSeconds, Ease.InOutCubic);
                    TweenUnstable(0f);
                    break;
                case StagePhase.Unstable:
                    // 侵蝕度は 1.0 のまま。縁の金色化・明滅で焦燥感を出す
                    TweenErosion(1f, 1f, Ease.OutQuad);
                    TweenUnstable(1f);
                    break;
                case StagePhase.Receding:
                    TweenErosion(0f, recedingDurationSeconds, Ease.InOutSine);
                    TweenUnstable(0f);
                    break;
            }
        }

        private void TweenErosion(float target, float duration, Ease ease)
        {
            erosionTween?.Kill();
            erosionTween = DOTween.To(() => erosionProgress.Value, SetErosion, target, duration)
                .SetEase(ease);
        }

        private void TweenUnstable(float target)
        {
            unstableTween?.Kill();
            unstableTween = DOTween.To(() => unstableBlend, SetUnstableBlend, target, unstableBlendSeconds)
                .SetEase(Ease.InOutSine);
        }

        /// <summary>侵蝕度を更新し、グローバルシェーダープロパティへ配信する</summary>
        private void SetErosion(float value)
        {
            erosionProgress.Value = value;
            Shader.SetGlobalFloat(ErosionPropertyId, value);
        }

        /// <summary>Unstable 演出値を更新し、グローバルシェーダープロパティへ配信する</summary>
        private void SetUnstableBlend(float value)
        {
            unstableBlend = value;
            Shader.SetGlobalFloat(UnstablePropertyId, value);
        }
    }
}
