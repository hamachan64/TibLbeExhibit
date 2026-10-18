using R3;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 持ち方（設計書 4.5）。「ビームの長さ」と「つかんだ後の目標の決め方」を差し替えられる形にしておく。
    /// 切り替え（直接つかむ／離れたまま揺れて運ぶ）自体は #38 では作らない。
    /// </summary>
    public interface IGrabStyle
    {
        float BeamLength(ThGripParams grip);
        /// <summary>つかんだ後の目標（手のひらの位置・向き → お宝を合わせる点）</summary>
        Pose HoldTarget(Pose palm, Vector3 beamDirection);
    }

    /// <summary>標準: ビームを持ち手に当てて握ると手元へ飛んできて、手にしっかりついてくる</summary>
    public sealed class BeamGrabStyle : IGrabStyle
    {
        public float BeamLength(ThGripParams grip) => grip.beamDistance;
        public Pose HoldTarget(Pose palm, Vector3 beamDirection) => palm;
    }

    /// <summary>
    /// 手ごとに 1 つ（左右で独立）。ビーム（肩の推定位置→手のひらの方向への SphereCast）、持ち手の狙い、握ったときのつかむ要求を受け持つ。
    /// 判断（つかめるか）は ClientItemTable にあり、ここは配線だけ。
    /// ビームは持ち手（専用レイヤーのトリガー）だけに当たる。瓦礫・床・アバターには止められない（低い瓦礫の陰のお宝にも届くように）。
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class HandGrabber : MonoBehaviour
    {
        [SerializeField] HandGripTracker _tracker;
        [SerializeField] LineRenderer _beam;

        const int MaxHits = 16;
        readonly RaycastHit[] _hits = new RaycastHit[MaxHits];

        TreasureClient _client;
        Transform _anchor;
        IGrabStyle _style = new BeamGrabStyle();
        int _handleMask;

        Treasure _aimed;
        Treasure _lastAimed;
        double _lastAimedAt = double.NegativeInfinity;
        Vector3 _beamDir = Vector3.forward;

        public HandGripTracker Tracker => _tracker;
        public Handedness Handedness => _tracker != null ? _tracker.Handedness : Handedness.Right;
        /// <summary>手に合わせて動く目標（お宝はこれに追従する）</summary>
        public Transform Anchor => _anchor;
        /// <summary>今ビームが当たっている持ち手のお宝（つかめるものだけ）</summary>
        public Treasure Aimed => _aimed;
        /// <summary>この手が申請中・持っているお宝</summary>
        public Treasure Held { get; set; }
        public IGrabStyle Style { get => _style; set => _style = value ?? new BeamGrabStyle(); }

        public void Initialize(TreasureClient client, HandGripTracker tracker, LineRenderer beam, Material beamMaterial, int handleLayer)
        {
            _client = client;
            _tracker = tracker;
            _beam = beam;
            _handleMask = handleLayer >= 0 ? 1 << handleLayer : 0;
            var go = new GameObject("GrabAnchor_" + Handedness);
            go.transform.SetParent(client.transform, false);
            _anchor = go.transform;
            if (_beam != null)
            {
                _beam.positionCount = 2;
                _beam.useWorldSpace = true;
                _beam.widthMultiplier = 0.006f;
                if (beamMaterial != null) _beam.sharedMaterial = beamMaterial;
                _beam.enabled = false;
            }
            _tracker.IsGripping.Subscribe(OnGripChanged).AddTo(this);
            _tracker.Released.Subscribe(_ => OnReleased()).AddTo(this);
        }

        static double Now => Time.realtimeSinceStartupAsDouble;

        void Update()
        {
            if (_client == null || _tracker == null) return;
            if (!_tracker.IsTracked.CurrentValue || !_tracker.HasPalm)
            {
                SetBeam(false, default, default);
                _aimed = null;
                return;
            }

            var palm = _tracker.PalmPose;
            var shoulder = _client.EstimateShoulder(Handedness);
            var dir = palm.position - shoulder;
            _beamDir = dir.sqrMagnitude > 1e-6f ? dir.normalized : _beamDir;
            var target = _style.HoldTarget(palm, _beamDir);
            _anchor.SetPositionAndRotation(target.position, target.rotation);

            float length = _style.BeamLength(_client.Grip);
            _aimed = Held == null && _client.CanGrab ? FindAimed(palm.position, _beamDir, length) : null;
            if (_aimed != null) { _lastAimed = _aimed; _lastAimedAt = Now; }
            SetBeam(true, palm.position, palm.position + _beamDir * length);
        }

        Treasure FindAimed(Vector3 origin, Vector3 dir, float length)
        {
            if (_handleMask == 0) return null;
            int n = Physics.SphereCastNonAlloc(origin, _client.Grip.snapRadius, dir, _hits, length, _handleMask, QueryTriggerInteraction.Collide);
            Treasure best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var t = _hits[i].collider.GetComponentInParent<Treasure>();
                if (t == null || !_client.IsTargetable(t)) continue;
                if (_hits[i].distance < bestDist) { bestDist = _hits[i].distance; best = t; }
            }
            return best;
        }

        void SetBeam(bool on, Vector3 a, Vector3 b)
        {
            if (_beam == null) return;
            if (_beam.enabled != on) _beam.enabled = on;
            if (on) { _beam.SetPosition(0, a); _beam.SetPosition(1, b); }
        }

        void OnGripChanged(bool gripping)
        {
            if (!gripping || _client == null || Held != null) return;
            // 握った瞬間にビームが外れていても、〔狙いの保持〕以内に当たっていた持ち手を対象にする
            var target = _aimed;
            if (target == null && _lastAimed != null && Now - _lastAimedAt <= _client.Grip.aimHoldSeconds && _client.IsTargetable(_lastAimed))
                target = _lastAimed;
            if (target == null || !_client.CanGrab) return;
            _client.TryGrab(this, target);
        }

        void OnReleased()
        {
            if (Held == null || _client == null) return;
            // TODO(#41): 解除の理由が見失い（GripReleaseReason.Lost）のときは Hovering にする。#38 では手を開いたときと同じ扱い。
            _client.OnHandReleased(this);
        }

        public void ClearAim() { _aimed = null; _lastAimed = null; }
    }
}
