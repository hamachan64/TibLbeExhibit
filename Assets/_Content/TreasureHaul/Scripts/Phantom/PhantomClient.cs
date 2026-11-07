using DG.Tweening;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 体験者の端末の亡霊（UserClient）。Playing の経過時間（残り秒）から全端末で同じ位置・向きに亡霊を置き、
    /// 自分の頭（XR のカメラ）が視界に入って壁にさえぎられず〔見つかるまでの時間〕続いたら、ホストへ「見つかった」を送る。
    /// 罰を決めるのはホスト。ここは見た目・知らせの表示だけ。判断は <see cref="PhantomRoute"/> / <see cref="PhantomSight"/> / <see cref="PhantomSpotDetector"/>。
    /// </summary>
    public sealed class PhantomClient : MonoBehaviour
    {
        [SerializeField] SessionClient _session;
        [SerializeField, Tooltip("知らせの文字のフォント（日本語フォント）")] TMP_FontAsset _font;
        [Tooltip("エディタの検証用: 頭の代わりにする Transform（空なら Camera.main）")] public Transform HeadOverride;

        /// <summary>本編の開始時の一文を出す秒数・仲間への知らせの秒数</summary>
        const float IntroSeconds = 5f, PeerSeconds = 3f;
        const float FadeSeconds = 0.25f;
        static readonly Vector3 NoticeOffset = new Vector3(0f, 0.12f, 1.0f);
        static readonly Vector3 OutOffset = new Vector3(0f, 0.02f, 1.0f);

        SessionClientCore _core;
        ThParams _configuredFor;
        PhantomView _view;
        PhantomRoute _route;
        ThPhantomAssets _routeFrom;
        PhantomSpotDetector _detector;
        System.IDisposable _punishSub;
        int _wallMask = -1;
        bool _warnedMissing;
        bool _viewBroken;
        bool _warnedNoSound;
        bool _wasWatching;
        long _introRound = -1;

        // 知らせの文字（頭について来る）
        TextMeshPro _notice;
        LazyFollow _noticeFollow;
        Tween _noticeFade;
        string _noticeText = "";
        double _noticeUntil;
        bool _outShown;

        static double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>お宝が吸い込まれる先（亡霊の体の中心。亡霊がいなければ vector.zero）</summary>
        public Vector3 AbsorbWorldPoint => _view != null && _view.gameObject.activeSelf ? _view.AbsorbPoint : Vector3.zero;
        public bool HasAbsorbPoint => _view != null && _view.gameObject.activeSelf;
        public PhantomView View => _view;
        /// <summary>この端末が「見つかった」とホストへ報告した回数（検証用）</summary>
        public int SpotCount { get; private set; }
        public bool Watching => _detector != null && _detector.Watching;

        void Awake()
        {
            int wall = LayerMask.NameToLayer(TreasureClient.WallLayerName);
            _wallMask = wall >= 0 ? 1 << wall : 0;
            if (wall < 0) Debug.LogWarning($"[PhantomClient] レイヤー {TreasureClient.WallLayerName} がありません（壁にさえぎられない判定になります）");
        }

        void OnDestroy()
        {
            _punishSub?.Dispose();
            _noticeFade?.Kill();
            if (_view != null) Destroy(_view.gameObject);
            if (_notice != null) Destroy(_notice.gameObject);
        }

        void Update()
        {
            if (_session == null) return;
            var core = _session.Core;
            if (core != _core) Bind(core);
            if (_core == null) return;

            var t = _core.Tracker;
            var assets = _session.Content != null ? _session.Content.phantom : null;
            var venue = ThVenue.Instance;
            bool active = t.State.CurrentValue == RoundState.Playing
                && t.Settings.phantom != ThPhantomMode.Off
                && !_viewBroken && assets != null && assets.prefab != null && venue != null;
            if (!active)
            {
                if (t.State.CurrentValue == RoundState.Playing && t.Settings.phantom != ThPhantomMode.Off && !_warnedMissing && (assets == null || assets.prefab == null))
                {
                    _warnedMissing = true;
                    Debug.LogWarning("[PhantomClient] 亡霊の素材（ThContentSet.phantom）がありません。亡霊は出ません");
                }
                HideView();
                UpdateNotice(t, false);
                return;
            }

            EnsureView(assets, venue);
            if (_view == null) { UpdateNotice(t, false); return; }   // プレハブに PhantomView が無い: 警告 1 回で以後は亡霊を出さない
            var pp = _core.Params.phantom;
            if (!ReferenceEquals(_configuredFor, _core.Params))
            {
                _configuredFor = _core.Params;
                _view.Configure(pp.sightDistance, pp.sightHalfAngle, pp.eyeHeight);
                _detector = new PhantomSpotDetector(pp.spotSeconds, pp.ignoreSeconds);
            }
            if (_route == null || _routeFrom != assets)
            {
                _routeFrom = assets;
                try { _route = assets.BuildRoute(); }
                catch (System.ArgumentException e) { Debug.LogError("[PhantomClient] 経路が不正です: " + e.Message); HideView(); return; }
            }

            float elapsed = Mathf.Max(0f, _core.Params.progress.playingSeconds - t.RemainingSeconds.CurrentValue);
            _route.At(elapsed, pp.speed, pp.startFraction, out var pos, out var yaw);
            _view.gameObject.SetActive(true);
            _view.transform.SetLocalPositionAndRotation(new Vector3(pos.x, 0f, pos.y), Quaternion.Euler(0f, yaw, 0f));

            bool judged = t.IsParticipant.CurrentValue && !t.IsPhantomOut.CurrentValue && _core.Items.CurrentValue != null;
            var head = HeadOverride != null ? HeadOverride : (Camera.main != null ? Camera.main.transform : null);
            bool seen = false;
            if (judged && head != null)
            {
                var headLocal = venue.Root.InverseTransformPoint(head.position);
                if (PhantomSight.InView(pos, yaw, new Vector2(headLocal.x, headLocal.z), pp.sightDistance, pp.sightHalfAngle))
                {
                    var eye = venue.Root.TransformPoint(new Vector3(pos.x, pp.eyeHeight, pos.y));
                    seen = !HandGrabber.IsBlockedByWall(eye, head.position, _wallMask);   // ビームを止める壁と同じ判定
                }
            }
            bool spotted = _detector.Update(seen, Now);
            bool watching = _detector.Watching;
            _view.SetAlert(watching);
            if (watching && !_wasWatching) PlayWarning(head);
            _wasWatching = watching;
            if (spotted)
            {
                SpotCount++;
                Debug.Log($"[PhantomClient] 見つかった elapsed={elapsed:F1}");
                _core.SendPhantomSpot(elapsed);
            }

            // 本編の開始時の一文（この回で 1 回。参加している端末だけ）
            if (_introRound != t.RoundId && t.IsParticipant.CurrentValue)
            {
                _introRound = t.RoundId;
                ShowNotice(_session.Content.text.Get(t.Settings.phantom == ThPhantomMode.PlayerOut ? ThTextId.PhantomNoticeOut : ThTextId.PhantomNoticeTake), IntroSeconds);
            }
            UpdateNotice(t, true);
        }

        void Bind(SessionClientCore core)
        {
            _punishSub?.Dispose();
            _punishSub = null;
            _core = core;
            _configuredFor = null;
            if (core == null) return;
            _punishSub = core.PhantomPunished.Subscribe(OnPunished);
        }

        void EnsureView(ThPhantomAssets assets, ThVenue venue)
        {
            if (_view != null) return;
            var go = Instantiate(assets.prefab, venue.Root);
            go.name = "Phantom";
            _view = go.GetComponent<PhantomView>();
            if (_view == null)
            {
                _viewBroken = true;
                Debug.LogError("[PhantomClient] プレハブに PhantomView がありません。亡霊は出しません");
                Destroy(go);
                return;
            }
            _configuredFor = null;
        }

        void HideView()
        {
            if (_view != null && _view.gameObject.activeSelf) _view.gameObject.SetActive(false);
            _detector?.Reset();
            _wasWatching = false;
        }

        void PlayWarning(Transform head)
        {
            var audio = _session.Content != null ? _session.Content.audio : null;
            var at = head != null ? head.position : transform.position;
            if (!ThAudioPlayer.Play(audio, ThSoundId.PhantomWarning, at) && !_warnedNoSound)
            {
                _warnedNoSound = true;
                Debug.Log("[PhantomClient] 警告音の素材がありません（ThSoundId.PhantomWarning）。音なしで続けます");
            }
        }

        // ---- ホストの罰の知らせ ----

        void OnPunished(ThPhantomPunishMsg m)
        {
            if (_session == null || _session.Content == null) return;
            if (m.mode == (int)ThPhantomMode.TakeTreasure)
            {
                if (m.device == _core.DeviceId) ShowNotice(_session.Content.text.Get(ThTextId.PhantomTakenSelf), PeerSeconds);   // 本人に理由を伝える
                return;
            }
            if (m.mode != (int)ThPhantomMode.PlayerOut || m.device == _core.DeviceId) return;   // 本人の画面は「この回はここまで」を出し続ける
            var palette = _session.Content.palette;
            var slot = palette != null ? palette.Get(_core.Tracker.ColorOf(m.device)) : null;
            string who = slot != null ? $"<color=#{ColorUtility.ToHtmlStringRGB(slot.practiceColor)}>{slot.colorName}{slot.symbol}</color>" : "仲間";
            ShowNotice(_session.Content.text.Format(ThTextId.PhantomOutPeerFormat, who), PeerSeconds);
        }

        // ---- 知らせの文字（頭について来る） ----

        void ShowNotice(string text, float seconds)
        {
            _noticeText = text;
            _noticeUntil = Now + seconds;
        }

        void UpdateNotice(ClientRoundTracker t, bool phantomActive)
        {
            bool outSelf = t.State.CurrentValue == RoundState.Playing && t.IsPhantomOut.CurrentValue;
            string want = "";
            bool large = false;
            if (outSelf && _session.Content != null) { want = _session.Content.text.Get(ThTextId.PhantomOutSelf); large = true; }
            else if (Now < _noticeUntil && t.State.CurrentValue == RoundState.Playing) want = _noticeText;
            if (want.Length == 0 && _notice == null) return;
            EnsureNotice();
            if (_notice == null) return;
            if (want.Length > 0 && (_notice.text != want || large != _outShown))
            {
                _notice.text = want;
                _notice.fontSize = large ? 0.9f : 0.4f;
                _notice.color = large ? new Color(1f, 0.35f, 0.3f, 0f) : new Color(1f, 1f, 1f, 0f);
                _outShown = large;
                _noticeFollow.targetOffset = large ? OutOffset : NoticeOffset;
            }
            float targetAlpha = want.Length > 0 ? 1f : 0f;
            if (!Mathf.Approximately(_notice.alpha, targetAlpha) && (_noticeFade == null || !_noticeFade.IsActive() || _fadeTarget != targetAlpha))
            {
                _fadeTarget = targetAlpha;
                _noticeFade?.Kill();
                _noticeFade = DOTween.To(() => _notice.alpha, a => _notice.alpha = a, targetAlpha, FadeSeconds).SetLink(_notice.gameObject);
            }
            if (_noticeFollow.target == null && Camera.main != null) _noticeFollow.target = Camera.main.transform;
        }

        float _fadeTarget = -1f;

        void EnsureNotice()
        {
            if (_notice != null) return;
            var go = new GameObject("PhantomNotice");
            _notice = go.AddComponent<TextMeshPro>();
            if (_font != null) _notice.font = _font;
            _notice.alignment = TextAlignmentOptions.Center;
            _notice.textWrappingMode = TextWrappingModes.Normal;
            _notice.richText = true;
            _notice.rectTransform.sizeDelta = new Vector2(1.6f, 0.5f);
            _notice.alpha = 0f;
            _notice.text = "";
            _noticeFollow = go.AddComponent<LazyFollow>();
            _noticeFollow.positionFollowMode = LazyFollow.PositionFollowMode.Follow;
            _noticeFollow.rotationFollowMode = LazyFollow.RotationFollowMode.LookAtWithWorldUp;
            _noticeFollow.targetOffset = NoticeOffset;
            _noticeFollow.movementSpeed = 4f;
            if (Camera.main != null) _noticeFollow.target = Camera.main.transform;
        }
    }
}
