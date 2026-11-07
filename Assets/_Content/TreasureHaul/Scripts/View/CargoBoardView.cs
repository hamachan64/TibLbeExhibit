using DG.Tweening;
using TMPro;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 納品場所の上の表示板（発掘隊の電光掲示板）。その回の本編の合計額だけを 7 セグメントで出す。
    /// 増えたときは数字が数え上がり、画面と通電ランプが一瞬明るくなる。減ったとき・最初の値・非表示からの再表示はすぐその額。
    /// Tutorial〜Result の間だけ表示し、Waiting・Opening では消す（呼び出し側が SetVisible を呼ぶ）。
    /// 札の文言は呼び出し側（ThTextTable）から入れる。SerializeField が未設定でも例外を出さない。
    /// </summary>
    public sealed class CargoBoardView : MonoBehaviour
    {
        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField, Tooltip("7 セグメントの画面")] SevenSegmentDisplay _display;
        [SerializeField, Tooltip("通電ランプ（未設定なら無視）")] Renderer _lamp;
        [SerializeField, Tooltip("「納品額」の札の TMP（未設定なら無視）")] TMP_Text _label;
        [SerializeField, Tooltip("表示・非表示を切り替える見た目の親（未設定なら画面の GameObject）")] GameObject _visual;
        [SerializeField, Min(0f), Tooltip("数え上げの長さ（秒）")] float _countSeconds = 0.6f;
        [SerializeField, Min(0f), Tooltip("光る長さ（秒）")] float _flashSeconds = 0.4f;

        MaterialPropertyBlock _lampBlock;
        Color _lampBaseColor = Color.white;
        bool _lampHasBaseColor;
        bool _lampChecked;
        Tween _countTween, _flashTween;
        bool _hasValue;
        int _target;

        public bool Visible { get; private set; }
        /// <summary>いま画面に出している額（数え上げの途中はその途中の値）</summary>
        public int DisplayedTotal { get; private set; }

        /// <summary>
        /// 数え上げるか。表示中で、すでに値を出していて、新しい額が出ている額より大きいときだけ。
        /// 減ったとき・最初の値・非表示のとき（再表示の前に額が来る）はすぐその額にする。額は 99,999 に頭打ち。
        /// </summary>
        public static bool ShouldCountUp(bool visible, bool hasValue, int displayed, int target)
            => visible && hasValue && SevenSegment.Clamp(target) > SevenSegment.Clamp(displayed);

        void Awake() => SetVisible(false);

        void OnDestroy() => KillTweens();

        public void SetVisible(bool visible)
        {
            if (!visible)
            {
                KillTweens();
                ApplyFlash(0f);
                if (_hasValue) Apply(_target);   // 数え上げの途中で隠れたら、次に出すときは最終の額
            }
            Visible = visible;
            var go = _visual != null ? _visual : (_display != null ? _display.gameObject : null);
            if (go != null && go.activeSelf != visible) go.SetActive(visible);
        }

        /// <summary>札の文言（「納品額」など）</summary>
        public void SetLabel(string text)
        {
            if (_label != null && _label.text != text) _label.text = text;
        }

        public void SetTotal(int total)
        {
            int target = SevenSegment.Clamp(total);
            // 回の状態だけが変わって同じ額が届いたときは、数え上げ・光りをやり直さない
            if (_hasValue && target == _target) return;
            bool count = ShouldCountUp(Visible, _hasValue, DisplayedTotal, target);
            int from = DisplayedTotal;
            _hasValue = true;
            _target = target;
            _countTween?.Kill();
            _countTween = null;

            if (!count)
            {
                Apply(target);
                return;
            }

            _countTween = DOTween.To(() => (float)from, v => Apply(Mathf.RoundToInt(v)), (float)target, _countSeconds)
                .SetEase(Ease.OutCubic)
                .SetTarget(this)
                .OnComplete(() => Apply(_target));
            Flash();
        }

        void Apply(int value)
        {
            DisplayedTotal = value;
            if (_display != null) _display.Show(value);
        }

        void Flash()
        {
            _flashTween?.Kill();
            ApplyFlash(1f);
            _flashTween = DOTween.To(() => 1f, ApplyFlash, 0f, _flashSeconds).SetEase(Ease.OutQuad).SetTarget(this);
        }

        void ApplyFlash(float f)
        {
            if (_display != null) _display.SetFlash(f);
            if (_lamp == null) return;
            if (!_lampChecked)
            {
                _lampChecked = true;
                var m = _lamp.sharedMaterial;
                _lampHasBaseColor = m != null && m.HasProperty(BaseColorId);
                if (_lampHasBaseColor) _lampBaseColor = m.GetColor(BaseColorId);
                _lampBlock = new MaterialPropertyBlock();
            }
            _lamp.GetPropertyBlock(_lampBlock);
            _lampBlock.SetFloat(FlashId, f);                                 // _Flash を持つシェーダー用
            if (_lampHasBaseColor) _lampBlock.SetColor(BaseColorId, Color.Lerp(_lampBaseColor, Color.white, f));
            _lamp.SetPropertyBlock(_lampBlock);
        }

        void KillTweens()
        {
            _countTween?.Kill();
            _flashTween?.Kill();
            _countTween = null;
            _flashTween = null;
        }
    }
}
