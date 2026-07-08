using System.Text;
using DG.Tweening;
using R3;
using TMPro;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// タイムアップ時のリザルト表示。
    /// GameSession が Finished になったらプレイヤーの正面にパネルを生成し、
    /// 基本得点・成立した役・最終スコア（カウントアップ演出付き）を表示する。
    /// </summary>
    public class ResultView : MonoBehaviour
    {
        private GameSession session;
        private CardBinder binder;
        private bool shown;

        public void Init(GameSession gameSession, CardBinder cardBinder)
        {
            session = gameSession;
            binder = cardBinder;

            session.State
                .Where(s => s == SessionState.Finished)
                .Subscribe(_ => Show())
                .AddTo(this);
        }

        private void Show()
        {
            if (shown)
            {
                return;
            }
            shown = true;

            var cam = Camera.main;
            var forward = cam != null
                ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized
                : Vector3.forward;
            var origin = cam != null ? cam.transform.position : Vector3.up * 1.4f;

            var root = new GameObject("GIPoker_Result");
            root.transform.SetPositionAndRotation(
                origin + forward * 1.2f,
                Quaternion.LookRotation(forward));

            // 背景パネル
            var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bg.name = "BG";
            Destroy(bg.GetComponent<Collider>());
            bg.transform.SetParent(root.transform, false);
            bg.transform.localPosition = new Vector3(0f, 0f, 0.01f);
            bg.transform.localScale = new Vector3(0.85f, 0.65f, 0.01f);
            bg.GetComponent<Renderer>().sharedMaterial =
                CardVisualFactory.CreateLitMaterial(new Color(0.06f, 0.09f, 0.07f), new Color(0.01f, 0.02f, 0.01f));

            var result = binder != null ? binder.Score.CurrentValue : ScoreResult.Empty;

            // タイトル
            CreateText(root.transform, "Title", "TIME UP!", new Vector3(0f, 0.24f, 0f), 22f,
                new Color(0.95f, 0.78f, 0.25f), FontStyles.Bold);

            // 内訳（基本得点と成立した役）
            var sb = new StringBuilder();
            sb.AppendLine($"基本得点  {result.BaseScore}");
            foreach (var combo in result.Combos)
            {
                sb.AppendLine($"{combo.Name}  <color=#FFD34D>+{combo.Bonus}</color>");
            }
            if (result.Combos.Count == 0)
            {
                sb.AppendLine("<color=#888888>役なし</color>");
            }
            CreateText(root.transform, "Breakdown", sb.ToString(), new Vector3(0f, 0.04f, 0f), 10f,
                Color.white, FontStyles.Normal);

            // 最終スコア（カウントアップ演出）
            var totalText = CreateText(root.transform, "Total", "0", new Vector3(0f, -0.20f, 0f), 30f,
                new Color(0.95f, 0.78f, 0.25f), FontStyles.Bold);
            DOVirtual.Float(0f, result.TotalScore, 2.0f, v => totalText.text = Mathf.FloorToInt(v).ToString())
                .SetEase(Ease.OutCubic)
                .SetLink(root);

            // 登場演出
            root.transform.localScale = Vector3.zero;
            root.transform.DOScale(1f, 0.5f).SetEase(Ease.OutBack).SetLink(root);
            GIPokerAssets.SpawnVfx(GIPokerAssets.DiceBlessVfx, root.transform.position, 6f);
        }

        private TextMeshPro CreateText(Transform parent, string name, string content,
            Vector3 localPos, float fontSize, Color color, FontStyles style)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
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
            tmp.rectTransform.sizeDelta = new Vector2(80f, 40f);
            return tmp;
        }
    }
}
