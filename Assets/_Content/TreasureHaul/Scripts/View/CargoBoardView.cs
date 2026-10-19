using TMPro;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 荷台の上の表示板（F-SCORE-2〔変更後〕）。その回の本編の合計額だけを出す。
    /// Tutorial〜Result の間だけ表示し、Waiting・Opening では消す。文言は ThTextTable から取る（呼び出し側が文字列を渡す）。
    /// </summary>
    public sealed class CargoBoardView : MonoBehaviour
    {
        [SerializeField] TMP_Text _text;
        [SerializeField, Tooltip("表示・非表示を切り替える見た目の親（未設定ならこの GameObject の子すべて）")] GameObject _visual;

        public bool Visible { get; private set; }
        public string CurrentText => _text != null ? _text.text : "";

        void Awake() => SetVisible(false);

        public void SetVisible(bool visible)
        {
            Visible = visible;
            var go = _visual != null ? _visual : (_text != null ? _text.gameObject : null);
            if (go != null && go.activeSelf != visible) go.SetActive(visible);
        }

        public void SetText(string text)
        {
            if (_text != null && _text.text != text) _text.text = text;
        }
    }
}
