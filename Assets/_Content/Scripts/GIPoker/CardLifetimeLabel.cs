using R3;
using TMPro;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// 浮遊カードの上に「1分ルール」の残り秒数を表示するラベル。
    /// 残り10秒を切ると赤く強調し、カウントダウン停止中（彷徨うよろい効果）は
    /// STOP 表示になる。
    /// </summary>
    public class CardLifetimeLabel : MonoBehaviour
    {
        private const float WarningThreshold = 10f;

        private TextMeshPro text;

        /// <summary>カード見た目ルートの上部にラベルを生成して購読を開始する</summary>
        public static CardLifetimeLabel Attach(GameObject visualRoot, CardizableItemBase item)
        {
            var go = new GameObject("LifetimeLabel");
            go.transform.SetParent(visualRoot.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.075f, 0f);
            go.transform.localScale = Vector3.one * 0.01f;

            var label = go.AddComponent<CardLifetimeLabel>();
            label.text = go.AddComponent<TextMeshPro>();
            if (GIPokerAssets.JapaneseFont != null)
            {
                label.text.font = GIPokerAssets.JapaneseFont;
            }
            label.text.fontSize = 5f;
            label.text.alignment = TextAlignmentOptions.Center;
            label.text.rectTransform.sizeDelta = new Vector2(2f, 1f);

            item.RemainingLifetime
                .CombineLatest(item.IsCountdownPaused, (remaining, paused) => (remaining, paused))
                .Subscribe(x => label.UpdateLabel(x.remaining, x.paused))
                .AddTo(go);

            return label;
        }

        private void UpdateLabel(float remaining, bool paused)
        {
            if (text == null)
            {
                return;
            }
            if (paused)
            {
                text.text = "STOP";
                text.color = new Color(0.4f, 0.8f, 1f);
                return;
            }
            var seconds = Mathf.CeilToInt(remaining);
            text.text = seconds.ToString();
            text.color = remaining <= WarningThreshold
                ? new Color(1f, 0.25f, 0.2f)
                : new Color(1f, 1f, 1f, 0.85f);
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
