using DG.Tweening;
using TMPro;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 減額の表示（F-DMG-4。全員に見える）。お宝の値札の位置に赤い「-90G」を出し、少し浮き上がりながら消える。
    /// 「ぶつけると値段が下がる」の一文にも同じ部品を使う（浮き上がりなし）。カメラの方を向き、近いときは縮める（値札と同じ）。
    /// 見た目の値（高さ・時間）は演出の調整値なので、ここの定数で持つ（ThParams の対象ではない）。
    /// </summary>
    public sealed class DamagePopup : MonoBehaviour
    {
        /// <summary>これより近いと距離に比例して縮める（m）。値札と同じ</summary>
        const float NearShrinkDistance = 1.5f;
        /// <summary>「-NNG」の浮き上がる高さ（m）と表示時間（秒）。ぶつけた瞬間に目に入り、1 秒ほどで消える長さ</summary>
        public const float RiseHeight = 0.25f;
        public const float AmountSeconds = 1.2f;
        /// <summary>初めての一文の表示時間（秒）。読める長さ</summary>
        public const float NoteSeconds = 2.5f;
        const float FadeSeconds = 0.4f;

        TextMeshPro _tmp;
        Transform _anchor;
        Vector3 _anchorOffset;
        Vector3 _lastBase;
        float _rise;
        Transform _cam;
        Tween _tween;

        public string Text => _tmp != null ? _tmp.text : "";

        /// <summary>
        /// anchor の真上 heightOffset の位置に出す（anchor が消えたら最後の位置に残る）。
        /// rise が 0 より大きければその高さまで浮き上がり、seconds 後に消える。
        /// </summary>
        public static DamagePopup Spawn(Transform parent, Transform anchor, float heightOffset, string text, TMP_FontAsset font, Color color, float fontSize, float rise, float seconds)
        {
            var go = new GameObject("DamagePopup");
            go.transform.SetParent(parent, false);
            var p = go.AddComponent<DamagePopup>();
            p._anchor = anchor;
            p._anchorOffset = Vector3.up * heightOffset;
            p._lastBase = anchor != null ? anchor.position + p._anchorOffset : parent.position;
            go.transform.position = p._lastBase;
            p._tmp = go.AddComponent<TextMeshPro>();
            if (font != null) p._tmp.font = font;
            p._tmp.alignment = TextAlignmentOptions.Center;
            p._tmp.fontSize = fontSize;
            p._tmp.textWrappingMode = TextWrappingModes.NoWrap;
            p._tmp.color = color;
            p._tmp.rectTransform.sizeDelta = new Vector2(2f, 0.5f);
            p._tmp.text = text;
            p.Play(rise, seconds);
            return p;
        }

        void Play(float rise, float seconds)
        {
            var seq = DOTween.Sequence();
            if (rise > 0f) seq.Insert(0f, DOVirtual.Float(0f, rise, seconds, v => _rise = v).SetEase(Ease.OutCubic));
            // 入れ子の Tween の SetDelay は Sequence の中では効かないので、Insert で開始時刻を指定する
            seq.Insert(Mathf.Max(0f, seconds - FadeSeconds), DOTween.To(() => _tmp.alpha, a => _tmp.alpha = a, 0f, FadeSeconds));
            seq.OnComplete(() => { if (this != null) Destroy(gameObject); });
            seq.SetLink(gameObject);
            _tween = seq;
        }

        void LateUpdate()
        {
            if (_anchor != null) _lastBase = _anchor.position + _anchorOffset;
            transform.position = _lastBase + Vector3.up * _rise;
            if (_cam == null)
            {
                var c = Camera.main;
                if (c == null) return;
                _cam = c.transform;
            }
            var dir = transform.position - _cam.position;
            if (dir.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            transform.localScale = Vector3.one * Mathf.Clamp01(dir.magnitude / NearShrinkDistance);
        }

        void OnDestroy()
        {
            if (_tween != null && _tween.IsActive()) _tween.Kill();
        }
    }
}
