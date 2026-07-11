using DG.Tweening;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// プレイエリア境界の大魔法陣（BorderCircle）の演出コンポーネント。
    /// 既存 TiB_BorderLine.mat（プレイエリア床の白枠線）の「境界を淡く示す」役割を、
    /// 円形の魔法陣（実線リング＋回転するルーン風破線リング）として発展させたもの。
    ///
    /// フェーズごとの見た目（MR_STAGE_PLAN.md §3）:
    /// - Reality:   輪郭だけが淡く脈動（「何かが起きる」予感）
    /// - Awakening: 脈動をやめ、じわっと明るく安定。ルーンリングがゆっくり回り始める
    /// - Eroded:    完全点灯。回転継続
    /// - Unstable:  金色に色づき明滅（ラストスパートの焦燥感）
    /// - Receding:  ゆっくりフェードアウト
    ///
    /// StageDirector には直接依存せず、<see cref="StageDecorationDirector"/> から
    /// <see cref="EnterPhase"/> / <see cref="SetErosion"/> の呼び出しを受ける。
    /// </summary>
    [DisallowMultipleComponent]
    public class BorderCircle : MonoBehaviour
    {
        [SerializeField, Tooltip("外周の実線リング（GIPoker/Stage/RadialGlow マテリアル）")]
        private Renderer ringRenderer;

        [SerializeField, Tooltip("内側のルーン風破線リング（回転する）。任意")]
        private Renderer runeRenderer;

        [SerializeField, Tooltip("ルーンリングの回転周期（360度にかかる秒数）")]
        private float runeRotatePeriod = 40f;

        [SerializeField, Tooltip("Reality時の脈動アルファ（下限）")]
        private float pulseAlphaMin = 0.10f;

        [SerializeField, Tooltip("Reality時の脈動アルファ（上限）")]
        private float pulseAlphaMax = 0.28f;

        [SerializeField, Tooltip("脈動の片道時間（秒）")]
        private float pulsePeriod = 1.8f;

        [SerializeField, Tooltip("点灯時（Awakening以降）のアルファ")]
        private float litAlpha = 0.45f;

        [SerializeField, Tooltip("Unstableフェーズの警告色（金色）")]
        private Color unstableTint = new(1f, 0.72f, 0.22f, 1f);

        [SerializeField, Tooltip("侵蝕度による増光の最大倍率（1で無効）")]
        private float erosionBoost = 1.25f;

        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        private Material ringMaterial;
        private Material runeMaterial;
        private Color ringBaseColor;
        private Color runeBaseColor;
        private Tween pulseTween;
        private Tween runeRotateTween;
        private Tween ringFadeTween;
        private Tween runeFadeTween;
        private Sequence unstableFlicker;

        private void Awake()
        {
            if (ringRenderer != null)
            {
                ringMaterial = ringRenderer.material;
                ringBaseColor = ringMaterial.color;
            }
            if (runeRenderer != null)
            {
                runeMaterial = runeRenderer.material;
                runeBaseColor = runeMaterial.color;
            }
        }

        /// <summary>
        /// StageDecorationDirector からフェーズ変化の通知を受けて演出を切り替える。
        /// </summary>
        /// <param name="phase">遷移先のステージフェーズ</param>
        /// <param name="immediate">true の場合はアニメーションせず最終状態へ即座に反映する（初回購読時のみ使用）</param>
        public void EnterPhase(StagePhase phase, bool immediate = false)
        {
            switch (phase)
            {
                case StagePhase.Reality:
                    StopUnstableFlicker();
                    StopRuneRotation();
                    StartRealityPulse(immediate);
                    break;
                case StagePhase.Awakening:
                case StagePhase.Eroded:
                    StopUnstableFlicker();
                    StopRealityPulse();
                    LightUp(immediate);
                    StartRuneRotation();
                    break;
                case StagePhase.Unstable:
                    StopRealityPulse();
                    StartRuneRotation();
                    StartUnstableFlicker();
                    break;
                case StagePhase.Receding:
                    StopUnstableFlicker();
                    StopRealityPulse();
                    FadeOut(immediate);
                    break;
            }
        }

        /// <summary>
        /// 侵蝕度（0-1）に応じて魔法陣をわずかに増光させる。
        /// アルファはフェーズ演出（DOTweenツイーン）が握っているため、
        /// ここでは _Intensity のみを触って干渉を避ける。
        /// Unstable の明滅中は明滅シーケンスが _Intensity を優先する。
        /// </summary>
        public void SetErosion(float erosion)
        {
            if (unstableFlicker != null)
            {
                return;
            }
            var intensity = Mathf.Lerp(1f, erosionBoost, Mathf.Clamp01(erosion));
            ringMaterial?.SetFloat(IntensityId, intensity);
            runeMaterial?.SetFloat(IntensityId, intensity);
        }

        private void StartRealityPulse(bool immediate)
        {
            KillFadeTweens();
            SetRingColorBase();
            SetAlphaImmediate(runeMaterial, runeBaseColor, 0f); // ルーンリングはReality中は非表示

            if (ringMaterial == null)
            {
                return;
            }
            SetAlphaImmediate(ringMaterial, ringBaseColor, pulseAlphaMin);
            if (immediate)
            {
                return;
            }
            pulseTween?.Kill();
            pulseTween = ringMaterial
                .DOFade(pulseAlphaMax, pulsePeriod)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo);
        }

        private void StopRealityPulse()
        {
            pulseTween?.Kill();
            pulseTween = null;
        }

        private void LightUp(bool immediate)
        {
            KillFadeTweens();
            SetRingColorBase();
            if (immediate)
            {
                SetAlphaImmediate(ringMaterial, ringBaseColor, litAlpha);
                SetAlphaImmediate(runeMaterial, runeBaseColor, litAlpha * 0.8f);
                return;
            }
            if (ringMaterial != null)
            {
                ringFadeTween = ringMaterial.DOFade(litAlpha, 1.2f).SetEase(Ease.OutQuad);
            }
            if (runeMaterial != null)
            {
                runeFadeTween = runeMaterial.DOFade(litAlpha * 0.8f, 1.6f).SetEase(Ease.OutQuad);
            }
        }

        private void FadeOut(bool immediate)
        {
            KillFadeTweens();
            if (immediate)
            {
                SetAlphaImmediate(ringMaterial, ringBaseColor, 0f);
                SetAlphaImmediate(runeMaterial, runeBaseColor, 0f);
                StopRuneRotation();
                return;
            }
            if (ringMaterial != null)
            {
                ringFadeTween = ringMaterial.DOFade(0f, 2.2f).SetEase(Ease.InQuad);
            }
            if (runeMaterial != null)
            {
                runeFadeTween = runeMaterial.DOFade(0f, 2.2f).SetEase(Ease.InQuad)
                    .OnComplete(StopRuneRotation);
            }
        }

        private void StartRuneRotation()
        {
            if (runeRenderer == null || runeRotateTween != null)
            {
                return;
            }
            // クアッドは床向きに X+90 回転しているため、面の法線（ローカルZ）周りに回す
            runeRotateTween = runeRenderer.transform
                .DOLocalRotate(new Vector3(0f, 0f, 360f), runeRotatePeriod, RotateMode.LocalAxisAdd)
                .SetEase(Ease.Linear)
                .SetLoops(-1, LoopType.Incremental);
        }

        private void StopRuneRotation()
        {
            // フェードアウト完了後に呼ばれる場合はアルファ0のため、回転停止のポップは見えない
            runeRotateTween?.Kill();
            runeRotateTween = null;
        }

        private void StartUnstableFlicker()
        {
            if (ringMaterial == null || unstableFlicker != null)
            {
                return;
            }
            SetTint(unstableTint);
            unstableFlicker = DOTween.Sequence()
                .Append(ringMaterial.DOFloat(1.7f, IntensityId, 0.16f).SetEase(Ease.InOutQuad))
                .Append(ringMaterial.DOFloat(0.6f, IntensityId, 0.24f).SetEase(Ease.InOutQuad))
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
            ringMaterial?.SetFloat(IntensityId, 1f);
            SetRingColorBase();
        }

        /// <summary>リング／ルーンの色をアルファ以外ベース色へ戻す</summary>
        private void SetRingColorBase()
        {
            SetTint(Color.white);
        }

        private void SetTint(Color tint)
        {
            if (ringMaterial != null)
            {
                var rgb = tint == Color.white ? ringBaseColor : tint;
                ringMaterial.color = new Color(rgb.r, rgb.g, rgb.b, ringMaterial.color.a);
            }
            if (runeMaterial != null)
            {
                var rgb = tint == Color.white ? runeBaseColor : tint;
                runeMaterial.color = new Color(rgb.r, rgb.g, rgb.b, runeMaterial.color.a);
            }
        }

        private static void SetAlphaImmediate(Material material, Color baseColor, float alpha)
        {
            if (material == null)
            {
                return;
            }
            material.color = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);
        }

        private void KillFadeTweens()
        {
            ringFadeTween?.Kill();
            ringFadeTween = null;
            runeFadeTween?.Kill();
            runeFadeTween = null;
        }

        private void OnDestroy()
        {
            pulseTween?.Kill();
            runeRotateTween?.Kill();
            unstableFlicker?.Kill();
            KillFadeTweens();
            if (ringMaterial != null)
            {
                Destroy(ringMaterial);
            }
            if (runeMaterial != null)
            {
                Destroy(runeMaterial);
            }
        }
    }
}
