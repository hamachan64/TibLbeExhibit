using TMPro;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// お宝の上に浮かぶ値札（F-ITEM-3）。全員に見える・カメラの方を向く。
    /// 値段公開前は「？」、公開後は「{0:N0}G」で現在の価値（文言は ThTextTable）。
    /// </summary>
    public sealed class PriceLabel : MonoBehaviour
    {
        /// <summary>これより近いと値札を距離に比例して縮める（m）。1.5m は手に持ったお宝の値札（目の前 0.3〜0.5m）が視界を覆わず、離れた仲間の値札は等倍で読める距離</summary>
        const float NearShrinkDistance = 1.5f;
        TextMeshPro _tmp;
        ThTextTable _text;
        Transform _cam;
        Transform _anchor;
        float _height;
        int _lastValue = int.MinValue;
        bool _lastRevealed;
        bool _hasState;

        public string CurrentText => _tmp != null ? _tmp.text : "";

        /// <summary>anchor の真上 height の位置に浮かぶ（お宝が転がっても値札は傾かない）。parent は VenueRoot</summary>
        public static PriceLabel Create(Transform parent, Transform anchor, float height, ThTextTable text, TMP_FontAsset font)
        {
            var go = new GameObject("PriceLabel");
            go.transform.SetParent(parent, false);
            var label = go.AddComponent<PriceLabel>();
            label._anchor = anchor;
            label._height = height;
            label._text = text;
            label._tmp = go.AddComponent<TextMeshPro>();
            if (font != null) label._tmp.font = font;
            label._tmp.alignment = TextAlignmentOptions.Center;
            label._tmp.fontSize = 0.8f;
            label._tmp.textWrappingMode = TextWrappingModes.NoWrap;
            label._tmp.color = Color.white;
            label._tmp.rectTransform.sizeDelta = new Vector2(2f, 0.5f);
            label.SetState(false, 0);
            return label;
        }

        /// <summary>値札を更新する（変化したときだけ文字を作り直す）</summary>
        public void SetState(bool revealed, int value)
        {
            if (_hasState && revealed == _lastRevealed && (!revealed || value == _lastValue)) return;
            _hasState = true;
            _lastRevealed = revealed;
            _lastValue = value;
            _tmp.text = revealed ? _text.Format(ThTextId.PriceFormat, value) : _text.Get(ThTextId.PriceUnknown);
        }

        void LateUpdate()
        {
            if (_anchor != null) transform.position = _anchor.position + Vector3.up * _height;
            if (_cam == null)
            {
                var c = Camera.main;
                if (c == null) return;
                _cam = c.transform;
            }
            var dir = transform.position - _cam.position;
            if (dir.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            // 近いほど画面を覆うので、NearShrinkDistance より近いときは距離に比例して縮める（見かけの大きさに上限を設ける）
            float d = dir.magnitude;
            transform.localScale = Vector3.one * Mathf.Clamp01(d / NearShrinkDistance);
        }
    }
}
