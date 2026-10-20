using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 体験者に見せる文字（待機の一文・オープニングの字幕・残り時間と目標額・目標達成・結果）を出す、頭について来る小さな面。
    /// 視界の正面やや下に置き、少し遅れてついて来る（XRI の LazyFollow）。半透明の背景と文字だけで、全画面は覆わない（F-SAFE-3）。
    /// ScreenFader は使わない。判断は <see cref="ThPanelPresenter"/>（純粋クラス）にあり、ここは入力を集めて見た目と音にするだけ。
    /// 荷台の光と「ここへ運ぼう」は作らない（2026-10-21 の決定。第1検証で迷う人がいたら足す）。
    /// </summary>
    public sealed class ThPlayerPanel : MonoBehaviour
    {
        [SerializeField] SessionClient _client;
        [SerializeField, Tooltip("お宝の見た目（練習の案内で、持っているお宝が納品場所の範囲に入ったかを見る）。空なら実行時に探す")] TreasureClient _treasure;
        [SerializeField, Tooltip("頭について来る動き（この GameObject に付ける）")] LazyFollow _follow;
        [SerializeField, Tooltip("面全体の濃さ（フェード用）")] CanvasGroup _group;
        [SerializeField] TMP_Text _main;
        [SerializeField] TMP_Text _sub;
        [SerializeField, Tooltip("待機のクレジット欄（小さく）")] TMP_Text _credit;
        [SerializeField, Tooltip("半透明の背景（文字が無いときは隠す）")] GameObject _background;
        [SerializeField, Tooltip("頭からの位置（正面やや下）")] Vector3 _offset = new Vector3(0f, -0.18f, 1.0f);
        [SerializeField] float _mainSize = 56f;
        [SerializeField] float _largeSize = 100f;
        [SerializeField, Tooltip("本編の帯のときの背景の高さ（px。F-SAFE-3: 3 分間ずっと出るので、文字の分だけに絞る）")] float _bandHeight = 110f;

        const float FadeSeconds = 0.2f;

        readonly ThPanelPresenter _presenter = new ThPanelPresenter();
        Tween _fade;
        string _lastMain, _lastSub, _lastCredit;
        bool _lastLarge;
        bool _shown;
        bool _lastBand;
        bool _hasLast;
        bool _treasureSearched;
        ThPanelFrame _lastFrame;

        static double Now => Time.realtimeSinceStartupAsDouble;

        void Awake()
        {
            if (_follow != null)
            {
                _follow.positionFollowMode = LazyFollow.PositionFollowMode.Follow;
                _follow.rotationFollowMode = LazyFollow.RotationFollowMode.LookAtWithWorldUp;
                _follow.targetOffset = _offset;
                _follow.movementSpeed = 4f;
            }
            if (_group != null) _group.alpha = 0f;
            Apply(default, new ThPanelTexts(), true);
        }

        void OnDestroy() => _fade?.Kill();

        void Update()
        {
            var core = _client != null ? _client.Core : null;
            if (core == null || _client.Content == null || _client.Content.text == null) return;

            if (_follow != null && _follow.target == null && Camera.main != null) _follow.target = Camera.main.transform;

            var t = core.Tracker;
            var inp = new ThPanelInput
            {
                State = t.State.CurrentValue,
                RoundId = t.RoundId,
                WaitNextRound = t.WaitNextRound,
                Remaining = t.RemainingSeconds.CurrentValue,
                OpeningSeconds = core.Params.progress.openingSeconds,
                Target = t.Target,
                Total = core.Total.CurrentValue,
                Result = t.Result,
                Practice = ClassifyPractice(core),
                PracticeValue = core.Params.practice.practiceValue,
            };
            var frame = _presenter.Update(inp, Now);
            PlaySounds(frame);
            // 表示に関わる中身が前のフレームと同じなら、文字の組み立て（string.Format・ボクシング）も反映も飛ばす
            if (_hasLast && SameDisplay(_lastFrame, frame)) return;
            _lastFrame = frame; _hasLast = true;
            var texts = ThPanelPresenter.Compose(frame, _client.Content.text);
            Apply(frame, texts, false);
        }

        /// <summary>自分の練習用お宝の状態（Tutorial のときだけ。練習用の表が無い・割り当てが無い端末は None）</summary>
        PracticeItemState ClassifyPractice(SessionClientCore core)
        {
            if (core.Tracker.State.CurrentValue != RoundState.Tutorial) return PracticeItemState.None;
            var table = core.Items.CurrentValue;
            if (table == null || !table.IsPractice) return PracticeItemState.None;
            var item = table.Get(core.OwnPracticeIndex);
            if (item == null) return PracticeItemState.None;
            bool inZone = false;
            if (!item.Gone)
            {
                if (_treasure == null && !_treasureSearched)
                {
                    _treasureSearched = true;   // 探すのは 1 回だけ
                    _treasure = FindFirstObjectByType<TreasureClient>();
                    if (_treasure == null) Debug.LogWarning("[ThPlayerPanel] TreasureClient が見つからないので、練習の案内の段階 3（納品場所の範囲内）は出ません");
                }
                var t = _treasure != null ? _treasure.GetTreasure(item.Index) : null;
                if (t != null) inZone = table.InCollectZone(t.transform.localPosition);   // 持っているお宝の今の位置（VenueRoot ローカル）
            }
            return PracticeGuide.Classify(item, inZone);
        }

        static bool SameDisplay(in ThPanelFrame a, in ThPanelFrame b) =>
            a.Kind == b.Kind && a.PracticeStep == b.PracticeStep && a.PracticeValue == b.PracticeValue && a.OpeningLine == b.OpeningLine && a.RemainMinutes == b.RemainMinutes && a.RemainSeconds == b.RemainSeconds
            && a.Target == b.Target && a.ShowTargetReached == b.ShowTargetReached && ReferenceEquals(a.Result, b.Result);

        void PlaySounds(in ThPanelFrame f)
        {
            // 注意（#47）: 中断しても、鳴り始めたナレーションは止まらない。音声を入れるときに、回の終わり（中断を含む）で止める仕組みを検討する。
            var audio = _client.Content.audio;
            var pos = Camera.main != null ? Camera.main.transform.position : transform.position;
            if (f.OpeningStarted) ThAudioPlayer.Play(audio, ThSoundId.OpeningSfx, pos);
            if (f.LineStarted > 0) ThAudioPlayer.Play(audio, ThPanelPresenter.LineSound(f.LineStarted), pos);
            if (f.TargetReachedNow) ThAudioPlayer.Play(audio, ThSoundId.TargetReached, pos);
        }

        void Apply(in ThPanelFrame f, in ThPanelTexts texts, bool force)
        {
            bool show = f.Kind != ThPanelKind.None;
            if (force || texts.Main != _lastMain || texts.Large != _lastLarge)
            {
                if (_main != null) { _main.text = texts.Main ?? ""; _main.fontSize = texts.Large ? _largeSize : _mainSize; }
                _lastMain = texts.Main; _lastLarge = texts.Large;
            }
            if (force || texts.Sub != _lastSub) { if (_sub != null) _sub.text = texts.Sub ?? ""; _lastSub = texts.Sub; }
            if (force || texts.Credit != _lastCredit) { if (_credit != null) _credit.text = texts.Credit ?? ""; _lastCredit = texts.Credit; }
            // 本編の帯（目標達成の大表示を除く）は、背景を帯の高さまで小さくする。それ以外は全体の大きさ
            bool band = f.Kind == ThPanelKind.Playing && !f.ShowTargetReached;
            if (force || band != _lastBand) { _lastBand = band; ApplyBackgroundSize(band); }
            if (force || show != _shown)
            {
                _shown = show;
                if (show && _background != null) _background.SetActive(true);
                if (_group != null)
                {
                    _fade?.Kill();
                    _fade = DOTween.To(() => _group.alpha, a => _group.alpha = a, show ? 1f : 0f, force ? 0f : FadeSeconds)
                        .OnComplete(() => { if (!_shown && _background != null) _background.SetActive(false); });
                }
                else if (!show && _background != null) _background.SetActive(false);
            }
        }

        void ApplyBackgroundSize(bool band)
        {
            if (_background == null) return;
            var rt = _background.transform as RectTransform;
            if (rt == null) return;
            if (!band) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; }
            else
            {
                // 帯の文字（Main の中央）のまわりだけ
                float h = Mathf.Max(1f, ((RectTransform)transform).rect.height);
                float cy = _main != null ? (_main.rectTransform.anchorMin.y + _main.rectTransform.anchorMax.y) * 0.5f : 0.5f;
                float half = _bandHeight / h * 0.5f;
                rt.anchorMin = new Vector2(0f, Mathf.Clamp01(cy - half));
                rt.anchorMax = new Vector2(1f, Mathf.Clamp01(cy + half));
            }
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }
    }
}
