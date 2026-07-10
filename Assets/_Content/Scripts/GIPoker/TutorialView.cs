using DG.Tweening;
using R3;
using TMPro;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// チュートリアルの指示パネル（プロシージャル生成・視線緩追従）。
    ///
    /// - MRパススルーの視界を塞がないよう小さめのパネルを目線のやや下に置き、
    ///   BinderView と同じ方式でプレイヤーの視線に遅延追従させる
    /// - 現在ステップの指示テキスト、進行ドット（達成済み/現在/未到達）、
    ///   本編開始までのカウントダウンを表示する
    /// - 日本語テキストには GIPokerAssets.JapaneseFont を適用する（null 許容）
    /// </summary>
    public class TutorialView : MonoBehaviour
    {
        [SerializeField, Tooltip("プレイヤー正面からの距離（m）")]
        private float followDistance = 0.55f;
        [SerializeField, Tooltip("頭からどれだけ下に置くか（m）")]
        private float heightBelowHead = 0.18f;
        [SerializeField, Tooltip("追従の速さ")]
        private float followSpeed = 3f;

        private const float DotSpacing = 0.032f;
        private const float DotSize = 0.012f;

        private Transform panelRoot;
        private TextMeshPro instructionText;
        private TextMeshPro countdownText;
        private Renderer[] dotRenderers;
        private Material dotDoneMaterial;
        private Material dotCurrentMaterial;
        private Material dotPendingMaterial;

        /// <summary>セッションと結線し、パネルの見た目を構築する</summary>
        public void Init(GameSession session, int stepCount)
        {
            BuildPanel(stepCount);

            // 本編開始までのカウントダウン（チュートリアルは固定時間）
            session.TutorialRemainingTime
                .Subscribe(UpdateCountdown)
                .AddTo(this);
        }

        /// <summary>現在ステップの指示テキストと進行ドットを更新する</summary>
        public void ShowStep(int stepIndex, TutorialStep step)
        {
            if (instructionText != null)
            {
                instructionText.text = step.Instruction;
            }
            if (dotRenderers != null)
            {
                for (var i = 0; i < dotRenderers.Length; i++)
                {
                    if (dotRenderers[i] == null)
                    {
                        continue;
                    }
                    dotRenderers[i].sharedMaterial =
                        i < stepIndex ? dotDoneMaterial
                        : i == stepIndex ? dotCurrentMaterial
                        : dotPendingMaterial;
                }
            }

            // ステップ切り替えのポップ演出
            if (panelRoot != null)
            {
                panelRoot.DOKill();
                panelRoot.localScale = Vector3.one * 0.9f;
                panelRoot.DOScale(1f, 0.25f).SetEase(Ease.OutBack).SetLink(panelRoot.gameObject);
            }
        }

        private void BuildPanel(int stepCount)
        {
            var root = new GameObject("Panel");
            root.transform.SetParent(transform, false);
            panelRoot = root.transform;

            // 背景（小さめの濃緑パネル。視界を塞がない）
            var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bg.name = "BG";
            Destroy(bg.GetComponent<Collider>());
            bg.transform.SetParent(panelRoot, false);
            bg.transform.localPosition = new Vector3(0f, 0f, 0.006f);
            bg.transform.localScale = new Vector3(0.44f, 0.17f, 0.008f);
            bg.GetComponent<Renderer>().sharedMaterial =
                CardVisualFactory.CreateLitMaterial(new Color(0.06f, 0.09f, 0.07f), new Color(0.01f, 0.02f, 0.01f));

            // タイトル
            CreateText("Title", "チュートリアル", new Vector3(0f, 0.062f, 0f), 5f,
                new Color(0.95f, 0.78f, 0.25f), FontStyles.Bold);

            // 指示テキスト（中央）
            instructionText = CreateText("Instruction", string.Empty, new Vector3(0f, 0.008f, 0f), 6.5f,
                Color.white, FontStyles.Bold);

            // 進行ドット
            BuildDots(stepCount);

            // 本編開始カウントダウン（下部・小）
            countdownText = CreateText("Countdown", string.Empty, new Vector3(0f, -0.068f, 0f), 4f,
                new Color(0.6f, 0.82f, 1f), FontStyles.Normal);
        }

        private void BuildDots(int stepCount)
        {
            dotDoneMaterial = CardVisualFactory.CreateLitMaterial(
                new Color(0.95f, 0.78f, 0.25f), new Color(0.6f, 0.45f, 0.1f));
            dotCurrentMaterial = CardVisualFactory.CreateLitMaterial(
                Color.white, new Color(0.5f, 0.5f, 0.5f));
            dotPendingMaterial = CardVisualFactory.CreateLitMaterial(
                new Color(0.3f, 0.32f, 0.3f), Color.black);

            dotRenderers = new Renderer[stepCount];
            var startX = -(stepCount - 1) * 0.5f * DotSpacing;
            for (var i = 0; i < stepCount; i++)
            {
                var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dot.name = $"Dot{i}";
                Destroy(dot.GetComponent<Collider>());
                dot.transform.SetParent(panelRoot, false);
                dot.transform.localPosition = new Vector3(startX + i * DotSpacing, -0.045f, 0f);
                dot.transform.localScale = Vector3.one * DotSize;
                dotRenderers[i] = dot.GetComponent<Renderer>();
                dotRenderers[i].sharedMaterial = dotPendingMaterial;
            }
        }

        private TextMeshPro CreateText(string name, string content,
            Vector3 localPos, float fontSize, Color color, FontStyles style)
        {
            var go = new GameObject(name);
            go.transform.SetParent(panelRoot, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * 0.01f;

            var tmp = go.AddComponent<TextMeshPro>();
            if (GIPokerAssets.JapaneseFont != null)
            {
                tmp.font = GIPokerAssets.JapaneseFont;
            }
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(42f, 8f);
            return tmp;
        }

        private void UpdateCountdown(float remaining)
        {
            if (countdownText == null)
            {
                return;
            }
            countdownText.text = $"本編開始まで {Mathf.CeilToInt(remaining)}秒";
        }

        private void LateUpdate()
        {
            // BinderView と同じ視線緩追従（急な頭の動きでも酔いにくい）
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            var flatForward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.001f)
            {
                flatForward = transform.forward;
            }

            var targetPos = cam.transform.position + flatForward * followDistance + Vector3.down * heightBelowHead;
            var targetRot = Quaternion.LookRotation(flatForward) * Quaternion.Euler(-15f, 0f, 0f);

            var t = Mathf.Clamp01(Time.deltaTime * followSpeed);
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, targetPos, t),
                Quaternion.Slerp(transform.rotation, targetRot, t));
        }
    }
}
