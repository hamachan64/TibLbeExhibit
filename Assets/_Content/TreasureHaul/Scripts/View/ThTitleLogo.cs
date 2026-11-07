using DG.Tweening;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// タイトルロゴ（TREASURE HAUL）の面 1 枚。空間のロゴ（待機中、納品場所の上）と正面のロゴ（開始の合図）の両方に使う。
    /// 出し消しの判断は静的メソッド（純粋）、見た目は DOTween。最初は非表示。
    /// 光の影響を受けない透過の材質を使う。濃さは MaterialPropertyBlock の _BaseColor のアルファで変える（材質は共有のまま）。
    /// </summary>
    public sealed class ThTitleLogo : MonoBehaviour
    {
        [SerializeField] Renderer _renderer;
        [SerializeField, Tooltip("幅（m）。高さは画像の縦横比（1866×755）から決める")] float _width = 2.4f;

        // 見た目の定数（ThParams には足さない。仕様書 2026-11-03）
        public const float AspectHeightOverWidth = 755f / 1866f;
        const float VenueFadeIn = 0.5f, VenueFadeOut = 0.4f, VenueStartScale = 0.9f;
        const float BobAmplitude = 0.03f, BobPeriod = 4f;
        const float CuePop1 = 0.25f, CuePop2 = 0.15f, CueHold = 1.1f, CueOut = 0.4f;
        const float CuePopStartScale = 0.6f, CuePopPeakScale = 1.1f, CueOutScale = 1.15f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        MaterialPropertyBlock _mpb;
        Sequence _seq;
        Tween _bob;
        bool _venueShown;
        float _alpha;
        Vector3 _basePos;
        bool _inited;
        bool _noTexture;

        // ---- 判断（純粋）----

        /// <summary>空間のロゴを出すか。待機中だけ（端末の WaitNextRound には関係しない）</summary>
        public static bool ShowVenueLogo(RoundState state) => state == RoundState.Waiting;

        /// <summary>
        /// 正面のロゴを出す合図か。Waiting → Opening で、かつ Opening が始まったばかり（残り秒が openingSeconds - tolerance 以上）のときだけ。
        /// Tracker は購読直後に初期値 Waiting を流し、ホスト途絶でも自分で Waiting に戻るので、途中から知った Opening を残り秒で見分ける。
        /// </summary>
        public static bool IsStartCue(RoundState? prev, RoundState next, float remaining, float openingSeconds, float tolerance = 1f) =>
            prev == RoundState.Waiting && next == RoundState.Opening && remaining >= openingSeconds - tolerance;

        // ---- 見た目 ----

        void Init()
        {
            if (_inited) return;
            _inited = true;
            _mpb = new MaterialPropertyBlock();
            if (_renderer == null) _renderer = GetComponentInChildren<Renderer>(true);
            // ロゴ画像（TH_TitleLogo.png）は git に入れていない。画像が無い環境では白い四角を出さないよう、面を出さない
            _noTexture = _renderer == null || _renderer.sharedMaterial == null || _renderer.sharedMaterial.GetTexture(BaseMapId) == null;
            if (_noTexture) Debug.LogWarning($"[ThTitleLogo] {name}: ロゴ画像が無いので出しません（Assets/_Content/TreasureHaul/Textures/TH_TitleLogo.png は git 管理外。DEVELOPMENT.md の「git に入れていない素材」参照）");
            _basePos = transform.localPosition;
            transform.localScale = new Vector3(_width, _width * AspectHeightOverWidth, 1f);
            SetAlpha(0f);
            SetActiveVisual(false);
        }

        void Awake() => Init();

        void OnDestroy() => KillTweens();

        void KillTweens()
        {
            _seq?.Kill(); _seq = null;
            _bob?.Kill(); _bob = null;
        }

        Vector3 BaseScale => new Vector3(_width, _width * AspectHeightOverWidth, 1f);

        void SetAlpha(float a)
        {
            _alpha = a;
            if (_renderer == null) return;
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, new Color(1f, 1f, 1f, a));
            _renderer.SetPropertyBlock(_mpb);
        }

        void SetActiveVisual(bool on)
        {
            if (_noTexture) on = false;
            if (_renderer != null && _renderer.enabled != on) _renderer.enabled = on;
        }

        void SetScaleMul(float m) => transform.localScale = BaseScale * m;

        /// <summary>空間のロゴを出す（0.5 秒で透明・0.9 倍から元へ。以降は上下にゆっくり揺れる）。すでに出ているときは何もしない</summary>
        public void ShowVenue()
        {
            Init();
            if (_venueShown) return;
            _venueShown = true;
            KillTweens();
            transform.localPosition = _basePos;
            SetActiveVisual(true);
            float a0 = _alpha;   // 消える途中から出直すときは今の濃さから
            float s0 = Mathf.Lerp(VenueStartScale, 1f, a0);
            SetScaleMul(s0);
            _seq = DOTween.Sequence()
                .Join(DOTween.To(() => _alpha, SetAlpha, 1f, VenueFadeIn))
                .Join(DOTween.To(() => 0f, v => SetScaleMul(Mathf.Lerp(s0, 1f, v)), 1f, VenueFadeIn).SetEase(Ease.OutCubic))
                .OnComplete(() => _seq = null);
            _bob = transform.DOLocalMoveY(_basePos.y + BobAmplitude, BobPeriod * 0.5f).From(_basePos.y - BobAmplitude)
                .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo);
        }

        /// <summary>空間のロゴを消す（0.4 秒で透明へ）。出ていなければ何もしない</summary>
        public void HideVenue()
        {
            Init();
            if (!_venueShown) return;
            _venueShown = false;
            KillTweens();
            _seq = DOTween.Sequence()
                .Append(DOTween.To(() => _alpha, SetAlpha, 0f, VenueFadeOut))
                .OnComplete(() => { _seq = null; SetActiveVisual(false); });
        }

        /// <summary>
        /// 正面のロゴを出す（開始の合図）。camera の水平方向 distance 先・目の高さ +heightOffset に置き、体験者の方を向け、頭には追従させない。
        /// 0.25 秒で 0.6 倍・透明から 1.1 倍、続けて 0.15 秒で 1.0 倍、1.1 秒止まって、0.4 秒で 1.15 倍・透明へ消える。
        /// </summary>
        public void PlayCue(Transform camera, float distance = 1.5f, float heightOffset = 0.22f)
        {
            Init();
            if (camera == null) return;
            _venueShown = false;
            KillTweens();
            var fwd = Vector3.ProjectOnPlane(camera.forward, Vector3.up);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.ProjectOnPlane(camera.up, Vector3.up);   // 真上・真下を向いているとき
            fwd.Normalize();
            transform.position = camera.position + fwd * distance + Vector3.up * heightOffset;
            transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);   // Quad は -Z 側から見える。+Z を体験者から遠ざける向きにして、表を体験者に向ける
            SetActiveVisual(true);
            SetScaleMul(CuePopStartScale);
            SetAlpha(0f);
            _seq = DOTween.Sequence()
                .Append(DOTween.To(() => 0f, v => { SetScaleMul(Mathf.Lerp(CuePopStartScale, CuePopPeakScale, v)); SetAlpha(v); }, 1f, CuePop1).SetEase(Ease.OutQuad))
                .Append(DOTween.To(() => 0f, v => SetScaleMul(Mathf.Lerp(CuePopPeakScale, 1f, v)), 1f, CuePop2).SetEase(Ease.InOutQuad))
                .AppendInterval(CueHold)
                .Append(DOTween.To(() => 0f, v => { SetScaleMul(Mathf.Lerp(1f, CueOutScale, v)); SetAlpha(1f - v); }, 1f, CueOut).SetEase(Ease.InQuad))
                .OnComplete(() => { _seq = null; SetActiveVisual(false); });
        }

        /// <summary>正面のロゴをすぐ消す（出している途中で中断したとき）</summary>
        public void HideNow()
        {
            Init();
            _venueShown = false;
            KillTweens();
            SetAlpha(0f);
            SetActiveVisual(false);
        }
    }
}
