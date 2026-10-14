using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;

namespace STYLY.TIB_Template
{
    /// <summary>
    /// MR⇔VR遷移（Issue #14）用の全天フェード。Camera.main に追従する内向き球体で
    /// 視界全体を覆い、指定色への白フェードアウト／パススルーへの白フェードインを行う。
    ///
    /// - MRパススルー上で正しく合成されるよう、専用シェーダー
    ///   (TIB/ScreenFader) が framebuffer alpha も書き込む
    ///   （.claude/agent-memory/lbe-learnings.md の MRパススルー知見に準拠）。
    /// - フェード駆動は DOTween（DOTween.To）で行い、完了は UniTask.WaitUntil で待機する
    ///   （本プロジェクトの DOTween は DLL 版のため UNITASK_DOTWEEN_SUPPORT が未定義で
    ///   Tween.ToUniTask は使えない。コルーチン不使用）。
    /// - alpha=0 の間は Renderer を無効化し、無駄な overdraw を避ける。
    /// </summary>
    [DisallowMultipleComponent]
    public class ScreenFader : MonoBehaviour
    {
        // Resources 配下に置いたシェーダーを参照する。ランタイム Shader.Find のみで参照される
        // シェーダーはどのアセットからも参照されないため、Resources 同梱でビルド除外
        // （stripping）を防ぐ必要がある
        private const string ShaderName = "TIB/ScreenFader";
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField, Tooltip("フェード球の半径（m）。カメラの near/far クリップ内に収まる値にする")]
        private float sphereRadius = 3f;

        private MeshRenderer sphereRenderer;
        private Material fadeMaterial;
        private Tween fadeTween;

        /// <summary>現在の色（RGB）を保持したまま alpha だけを 0..1 で管理する</summary>
        private Color fadeColor = new(1f, 1f, 1f, 0f);

        /// <summary>
        /// Camera.main に追従する全天フェード用オブジェクトを生成する。
        /// 初期状態は完全透明・非描画（Renderer.enabled = false）。
        /// </summary>
        public static ScreenFader Create()
        {
            var go = new GameObject("ScreenFader");
            var fader = go.AddComponent<ScreenFader>();
            fader.BuildSphere();
            return fader;
        }

        private void BuildSphere()
        {
            if (sphereRenderer != null)
            {
                return;
            }

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "FadeSphere";
            var collider = sphere.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
            sphere.transform.SetParent(transform, false);
            // プリミティブ球の直径は1mなので、半径指定に合わせて2倍のスケールにする
            sphere.transform.localScale = Vector3.one * (sphereRadius * 2f);

            sphereRenderer = sphere.GetComponent<MeshRenderer>();
            sphereRenderer.shadowCastingMode = ShadowCastingMode.Off;
            sphereRenderer.receiveShadows = false;
            sphereRenderer.lightProbeUsage = LightProbeUsage.Off;
            sphereRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            // 全天フェード球はカメラ追従のため動的オクルージョンカリングの対象から外す
            sphereRenderer.allowOcclusionWhenDynamic = false;

            var shader = Shader.Find(ShaderName);
            if (shader != null)
            {
                fadeMaterial = new Material(shader) { name = "ScreenFader_Mat" };
                sphereRenderer.sharedMaterial = fadeMaterial;
            }
            else
            {
                Debug.LogError(
                    $"[ScreenFader] シェーダー '{ShaderName}' が見つかりません。" +
                    "フェードが描画されずMR⇔VR遷移が機能しない致命的状態です（Resources同梱を確認してください）。");
            }

            ApplyColor();
            sphereRenderer.enabled = false;
        }

        private void LateUpdate()
        {
            // Camera.main を毎フレーム直接参照する
            // （STYLY XR Rig はランタイム生成のためキャッシュせず都度取得する）
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }
            transform.position = cam.transform.position;
        }

        /// <summary>指定色へ alpha 0→color.a までフェードアウトする（視界を覆っていく）</summary>
        public async UniTask FadeOutAsync(Color color, float duration, CancellationToken ct)
        {
            fadeColor = color;
            await AnimateAlphaAsync(color.a, duration, ct);
        }

        /// <summary>直前の色を維持したまま alpha を 0 までフェードインする（パススルーを見せる）</summary>
        public async UniTask FadeInAsync(float duration, CancellationToken ct)
        {
            await AnimateAlphaAsync(0f, duration, ct);
        }

        private async UniTask AnimateAlphaAsync(float targetAlpha, float duration, CancellationToken ct)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, destroyCancellationToken);
            var token = linked.Token;

            fadeTween?.Kill();

            if (sphereRenderer != null)
            {
                sphereRenderer.enabled = true;
            }

            if (duration <= 0f)
            {
                SetAlpha(targetAlpha);
            }
            else
            {
                fadeTween = DOTween.To(() => fadeColor.a, SetAlpha, targetAlpha, duration)
                    .SetEase(Ease.InOutSine)
                    .SetLink(gameObject);

                // Tween 非依存の待機（完了時は autoKill により IsActive() が false になる）
                try
                {
                    await UniTask.WaitUntil(
                        () => fadeTween == null || !fadeTween.IsActive(),
                        cancellationToken: token);
                }
                catch (OperationCanceledException)
                {
                    fadeTween?.Kill();
                    throw;
                }
            }

            if (sphereRenderer != null)
            {
                sphereRenderer.enabled = fadeColor.a > 0.0001f;
            }
        }

        private void SetAlpha(float alpha)
        {
            fadeColor.a = alpha;
            ApplyColor();
        }

        private void ApplyColor()
        {
            if (fadeMaterial != null)
            {
                fadeMaterial.SetColor(ColorId, fadeColor);
            }
        }

        private void OnDestroy()
        {
            fadeTween?.Kill();
            if (fadeMaterial != null)
            {
                Destroy(fadeMaterial);
            }
        }
    }
}
