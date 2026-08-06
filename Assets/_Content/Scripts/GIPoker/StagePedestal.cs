using DG.Tweening;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>台座の種類（遺跡の石柱／浮遊岩）</summary>
    public enum StagePedestalKind
    {
        /// <summary>床に根を張る石柱。侵蝕で地面から迫り上がった後は静止する</summary>
        Pillar,
        /// <summary>宙に浮く岩。迫り上がった後もゆったり浮遊し続ける</summary>
        FloatingRock,
    }

    /// <summary>
    /// 遺跡台座（RuinPillar）／浮遊岩（FloatingRock）共通の演出コンポーネント。
    ///
    /// <c>StageDirector</c>（Issue #7 実装）には直接依存しない。
    /// <see cref="StageDecorationDirector"/> が Phase を購読し、本コンポーネントの
    /// <see cref="EnterPhase"/> を呼び出すことで間接的にフェーズへ追従する
    /// （IF契約への依存箇所を1クラスに集約するための設計）。
    ///
    /// 階層構成（Editor セットアップスクリプトが生成）:
    /// <code>
    /// StagePedestal (このコンポーネント)
    /// ├─ VisualRoot        … 迫り上がり/浮遊/沈み込みで上下するメッシュ一式
    /// │   └─ AnchorPoint    … 台座上面のアイテム出現アンカー（後続Issueが利用）
    /// └─ GroundHalo         … 常に床面（ローカルY=0）に固定される接地光輪クアッド
    /// </code>
    ///
    /// 「浮き」対策（企画書 §4-1）として GroundHalo は VisualRoot の子に**しない**。
    /// FloatingRock が宙に浮いていても、足元の光がずっと現実の床に落ちているように見せるため。
    /// </summary>
    [DisallowMultipleComponent]
    public class StagePedestal : MonoBehaviour
    {
        [SerializeField, Tooltip("台座の種類")]
        private StagePedestalKind kind = StagePedestalKind.Pillar;

        [SerializeField, Tooltip("アイテム出現アンカー（台座上面）。子Transform名は必ず AnchorPoint にすること")]
        private Transform anchorPoint;

        [SerializeField, Tooltip("上下に動く見た目のルート（プレハブ上では『迫り上がった後』の最終位置に配置しておく）")]
        private Transform visualRoot;

        [SerializeField, Tooltip("床に固定される接地光輪（GIPoker/Stage/RadialGlow マテリアルのRendererを持つQuad）")]
        private Renderer groundHalo;

        [SerializeField, Tooltip("侵蝕前、地面の下にどれだけ沈んでいるか（m）")]
        private float sunkenDepth = 0.6f;

        [SerializeField, Tooltip("迫り上がり演出の所要時間（秒）")]
        private float riseDuration = 1.3f;

        [SerializeField, Tooltip("沈み込み（崩れる）演出の所要時間（秒）")]
        private float sinkDuration = 1.1f;

        [SerializeField, Tooltip("FloatingRock浮遊時の上下振幅（m）")]
        private float bobAmplitude = 0.05f;

        [SerializeField, Tooltip("FloatingRock浮遊時の周期（秒）")]
        private float bobPeriod = 2.6f;

        [SerializeField, Tooltip("光輪の基準アルファ（迫り上がり完了時）")]
        private float haloAlpha = 0.55f;

        [SerializeField, Tooltip("Unstableフェーズで光輪が色づく警告色")]
        private Color unstableTint = new(1f, 0.75f, 0.25f, 1f);

        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        /// <summary>台座上面のアイテム出現アンカー（後続Issueが StageSpawnAnchor を付与する）</summary>
        public Transform AnchorPoint => anchorPoint;

        public StagePedestalKind Kind => kind;

        private Vector3 risenLocalPos;
        private Vector3 sunkenLocalPos;
        private Vector3 grownScale;
        private Material haloMaterialInstance;
        private Color haloBaseColor;
        private Tween positionTween;
        private Tween scaleTween;
        private Tween haloFadeTween;
        private Sequence unstableFlicker;

        private void Awake()
        {
            if (visualRoot == null)
            {
                Debug.LogWarning($"[StagePedestal] {name}: visualRoot が未設定です");
                enabled = false;
                return;
            }

            // パススルーMRには実世界床によるオクルージョンが無いため、地中に沈めるだけでは
            // 実際には見えてしまう。起動直後は visualRoot ごと非アクティブ化しておく。
            visualRoot.gameObject.SetActive(false);

            // プレハブ上の配置＝「迫り上がった後」の最終姿勢として記録し、
            // 起動直後は地中に沈めておく（ここでの位置移動はロード直後の初期化であり、
            // プレイヤーに見えている状態でのポップイン/アウトではない）。
            risenLocalPos = visualRoot.localPosition;
            sunkenLocalPos = risenLocalPos + Vector3.down * sunkenDepth;
            grownScale = visualRoot.localScale;
            visualRoot.localPosition = sunkenLocalPos;
            visualRoot.localScale = grownScale * 0.85f;

            if (groundHalo != null)
            {
                // 光輪は個体ごとにフェード時間をずらせるよう専用インスタンスを持つ
                // （台座数は少数のため、マテリアル数増加のコストは無視できる）。
                haloMaterialInstance = groundHalo.material;
                haloBaseColor = haloMaterialInstance.color;
                var c = haloBaseColor;
                c.a = 0f;
                haloMaterialInstance.color = c;
            }
        }

        /// <summary>
        /// StageDecorationDirector からフェーズ変化の通知を受けて演出を切り替える。
        /// </summary>
        /// <param name="phase">遷移先のステージフェーズ</param>
        /// <param name="delay">個体ごとの発生ずらし（秒）。0で同時発生</param>
        /// <param name="immediate">true の場合はアニメーションせず最終状態へ即座に反映する（初回購読時のみ使用）</param>
        public void EnterPhase(StagePhase phase, float delay = 0f, bool immediate = false)
        {
            switch (phase)
            {
                case StagePhase.Reality:
                case StagePhase.Awakening:
                    if (immediate)
                    {
                        KillAllTweens();
                        visualRoot.localPosition = sunkenLocalPos;
                        visualRoot.localScale = grownScale * 0.85f;
                        SetHaloAlphaImmediate(0f);
                        visualRoot.gameObject.SetActive(false);
                    }
                    else
                    {
                        Sink(delay);
                    }
                    break;
                case StagePhase.Eroded:
                    Rise(delay, immediate);
                    break;
                case StagePhase.Unstable:
                    // 万一まだ沈んでいたら先に迫り上げる（保険。通常はEroded経由で既に上がっている）
                    if (Vector3.Distance(visualRoot.localPosition, risenLocalPos) > 0.01f)
                    {
                        Rise(delay, immediate);
                    }
                    StartUnstableFlicker();
                    break;
                case StagePhase.Receding:
                    StopUnstableFlicker();
                    Sink(delay);
                    break;
            }
        }

        private void Rise(float delay, bool immediate)
        {
            StopUnstableFlicker();
            KillAllTweens();

            // 沈んだ状態のまま非表示→アクティブ化→迫り上がりトゥイーン、の順で見た目が
            // 破綻しないよう、アニメーション開始前（immediate/非immediate問わず）に再アクティブ化する。
            visualRoot.gameObject.SetActive(true);

            if (immediate)
            {
                visualRoot.localPosition = risenLocalPos;
                visualRoot.localScale = grownScale;
                SetHaloAlphaImmediate(haloAlpha);
                StartIdleBobIfFloatingRock();
                return;
            }

            positionTween = visualRoot
                .DOLocalMoveY(risenLocalPos.y, riseDuration)
                .SetDelay(delay)
                .SetEase(Ease.OutCubic)
                .OnComplete(StartIdleBobIfFloatingRock);

            scaleTween = visualRoot
                .DOScale(grownScale, riseDuration)
                .SetDelay(delay)
                .SetEase(Ease.OutBack, 1.2f);

            FadeHalo(haloAlpha, riseDuration * 0.8f, delay);
        }

        private void Sink(float delay)
        {
            StopUnstableFlicker();
            KillAllTweens();

            positionTween = visualRoot
                .DOLocalMoveY(sunkenLocalPos.y, sinkDuration)
                .SetDelay(delay)
                .SetEase(Ease.InCubic)
                .OnComplete(() =>
                {
                    // パススルーMRには実世界床のオクルージョンが無いため、沈めるだけでは
                    // 実際には見えてしまう。沈み込み完了時に非アクティブ化して確実に隠す。
                    if (visualRoot != null)
                    {
                        visualRoot.gameObject.SetActive(false);
                    }
                });

            scaleTween = visualRoot
                .DOScale(grownScale * 0.85f, sinkDuration)
                .SetDelay(delay)
                .SetEase(Ease.InCubic);

            FadeHalo(0f, sinkDuration * 0.6f, delay);
        }

        private void StartIdleBobIfFloatingRock()
        {
            if (kind != StagePedestalKind.FloatingRock)
            {
                return;
            }
            positionTween?.Kill();
            positionTween = visualRoot
                .DOLocalMoveY(risenLocalPos.y + bobAmplitude, bobPeriod)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo);
        }

        private void StartUnstableFlicker()
        {
            if (haloMaterialInstance == null || unstableFlicker != null)
            {
                return;
            }
            haloMaterialInstance.color = unstableTint;
            unstableFlicker = DOTween.Sequence()
                .Append(haloMaterialInstance.DOFloat(1.6f, IntensityId, 0.18f).SetEase(Ease.InOutQuad))
                .Append(haloMaterialInstance.DOFloat(0.5f, IntensityId, 0.22f).SetEase(Ease.InOutQuad))
                .SetLoops(-1, LoopType.Restart);
        }

        private void StopUnstableFlicker()
        {
            if (unstableFlicker == null)
            {
                return;
            }
            unstableFlicker.Kill();
            unstableFlicker = null;
            if (haloMaterialInstance != null)
            {
                haloMaterialInstance.SetFloat(IntensityId, 1f);
                // アルファはフェードツイーンが握っているため、RGBのみベース色へ戻す
                var current = haloMaterialInstance.color;
                haloMaterialInstance.color = new Color(haloBaseColor.r, haloBaseColor.g, haloBaseColor.b, current.a);
            }
        }

        private void FadeHalo(float targetAlpha, float duration, float delay)
        {
            if (haloMaterialInstance == null)
            {
                return;
            }
            haloFadeTween?.Kill();
            haloFadeTween = haloMaterialInstance.DOFade(targetAlpha, duration).SetDelay(delay);
        }

        private void SetHaloAlphaImmediate(float alpha)
        {
            if (haloMaterialInstance == null)
            {
                return;
            }
            haloFadeTween?.Kill();
            haloFadeTween = null;
            var c = haloBaseColor;
            c.a = alpha;
            haloMaterialInstance.color = c;
        }

        private void KillAllTweens()
        {
            positionTween?.Kill();
            positionTween = null;
            scaleTween?.Kill();
            scaleTween = null;
        }

        private void OnDestroy()
        {
            KillAllTweens();
            StopUnstableFlicker();
            haloFadeTween?.Kill();
            haloFadeTween = null;
            if (haloMaterialInstance != null)
            {
                Destroy(haloMaterialInstance);
            }
        }
    }
}
