using DG.Tweening;
using TMPro;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// リスキーダイスの結果（「大吉」等）を一度だけ表示し、
    /// フェードアウトして自己破棄するワールド空間TMPラベル。
    /// CardLifetimeLabel と同じビルボード（常にカメラを向く）パターンを踏襲する。
    /// </summary>
    public class DiceResultLabel : MonoBehaviour
    {
        private const float PopDuration = 0.25f;
        private const float DisplayDuration = 1.2f;
        private const float FadeDuration = 0.6f;
        private const float RiseDistance = 0.3f;
        private const float TargetScale = 0.02f;

        private TextMeshPro text;

        /// <summary>指定位置に結果テキストを表示し、一定時間後にフェードアウトして自己破棄する</summary>
        public static void ShowAt(Vector3 position, string message)
        {
            var go = new GameObject("DiceResultLabel");
            go.transform.position = position + Vector3.up * 0.15f;
            go.transform.localScale = Vector3.zero;

            var label = go.AddComponent<DiceResultLabel>();
            label.text = go.AddComponent<TextMeshPro>();
            if (GIPokerAssets.JapaneseFont != null)
            {
                label.text.font = GIPokerAssets.JapaneseFont;
            }
            label.text.text = message;
            label.text.fontSize = 24f;
            label.text.color = new Color(0.95f, 0.78f, 0.25f);
            label.text.fontStyle = FontStyles.Bold;
            label.text.alignment = TextAlignmentOptions.Center;
            label.text.rectTransform.sizeDelta = new Vector2(4f, 1.5f);

            label.PlayAndDestroy();
        }

        private void PlayAndDestroy()
        {
            var sequence = DOTween.Sequence().SetLink(gameObject);
            sequence.Append(transform.DOScale(TargetScale, PopDuration).SetEase(Ease.OutBack));
            sequence.AppendInterval(DisplayDuration);

            var riseTargetY = transform.position.y + RiseDistance;
            sequence.Append(transform.DOMoveY(riseTargetY, FadeDuration).SetEase(Ease.OutSine));
            sequence.Join(DOTween.To(() => text.alpha, a => text.alpha = a, 0f, FadeDuration));
            sequence.OnComplete(() => Destroy(gameObject));
        }

        private void LateUpdate()
        {
            // 常にプレイヤー（メインカメラ）の方を向ける
            var cam = Camera.main;
            if (cam != null)
            {
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
            }
        }
    }
}
