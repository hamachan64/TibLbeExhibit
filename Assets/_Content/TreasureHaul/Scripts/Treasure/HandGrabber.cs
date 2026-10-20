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
        readonly HandJumpFilter _jumpFilter = new HandJumpFilter();

        Treasure _aimed;
        int _aimedHandle = -1;
        Treasure _lastAimed;
        int _lastAimedHandle = -1;
        double _lastAimedAt = double.NegativeInfinity;
        Vector3 _beamDir = Vector3.forward;

        public HandGripTracker Tracker => _tracker;
        public Handedness Handedness => _tracker != null ? _tracker.Handedness : Handedness.Right;
        /// <summary>端末の表（ClientItemTable）で使う左右</summary>
        public ThHand Hand => Handedness == Handedness.Left ? ThHand.Left : ThHand.Right;
        /// <summary>手に合わせて動く目標（お宝はこれに追従する）</summary>
        public Transform Anchor => _anchor;
        /// <summary>今ビームが当たっている持ち手のお宝（つかめるものだけ）</summary>
        public Treasure Aimed => _aimed;
        /// <summary>今ビームが当たっている持ち手の番号（Treasure.Handles の何番目か。-1 = なし）</summary>
        public int AimedHandle => _aimedHandle;
        /// <summary>この手が申請中・持っているお宝（M は片手予約・両手で持っている間もそのお宝）</summary>
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
            _tracker.Released.Subscribe(OnReleased).AddTo(this);
        }

        static double Now => Time.realtimeSinceStartupAsDouble;

        void Update()
        {
            if (_client == null || _tracker == null) return;
            if (!_tracker.IsTracked.CurrentValue || !_tracker.HasPalm)
            {
                _jumpFilter.Update(false, default, 0f);   // 見失ったら前の位置を忘れる（戻った最初のフレームは比べない）
                SetBeam(false, default, default);
                _aimed = null;
                _aimedHandle = -1;
                return;
            }

            var palm = _tracker.PalmPose;
            var shoulder = _client.EstimateShoulder(Handedness);
            var dir = palm.position - shoulder;
            _beamDir = dir.sqrMagnitude > 1e-6f ? dir.normalized : _beamDir;
            var target = _style.HoldTarget(palm, _beamDir);
            // 手の目標が 1 フレームで〔飛びの判定〕以上動いたら飛び: そのフレームは目標を前のまま止め、持っているお宝の〔補正の無判定時間〕を始める
            var jump = _jumpFilter.Update(true, target.position, _client.JumpDistance);
            if (jump.Jumped) _client.OnHandJumped(this);
            else _anchor.SetPositionAndRotation(target.position, target.rotation);

            float length = _style.BeamLength(_client.Grip);
            _aimed = Held == null && _client.CanGrab ? FindAimed(palm.position, _beamDir, length, out _aimedHandle) : null;
            if (_aimed == null) _aimedHandle = -1;
            else { _lastAimed = _aimed; _lastAimedHandle = _aimedHandle; _lastAimedAt = Now; }
            SetBeam(true, palm.position, palm.position + _beamDir * length);
        }

        Treasure FindAimed(Vector3 origin, Vector3 dir, float length, out int handleIndex)
        {
            handleIndex = -1;
            if (_handleMask == 0) return null;
            int n = Physics.SphereCastNonAlloc(origin, _client.Grip.snapRadius, dir, _hits, length, _handleMask, QueryTriggerInteraction.Collide);
            Treasure best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var t = _hits[i].collider.GetComponentInParent<Treasure>();
                if (t == null) continue;
                int handle = System.Array.IndexOf(t.Handles, _hits[i].collider);   // M は左右 2 つのどちらか
                if (handle < 0) continue;   // 持ち手として登録されていないコライダーは対象にしない
                if (!_client.IsTargetable(t, Hand, handle)) continue;
                if (_hits[i].distance < bestDist) { bestDist = _hits[i].distance; best = t; handleIndex = handle; }
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
            // M を両手で持っている間に外れた手・予約中に見失った手が、もう一度握った: ビームの当て直しは要らない
            if (_client.TryRegrip(this)) return;
            // 握った瞬間にビームが外れていても、〔狙いの保持〕以内に当たっていた持ち手を対象にする
            var target = _aimed;
            int handle = _aimedHandle;
            if (target == null && _lastAimed != null && Now - _lastAimedAt <= _client.Grip.aimHoldSeconds && _client.IsTargetable(_lastAimed, Hand, _lastAimedHandle))
            {
                target = _lastAimed;
                handle = _lastAimedHandle;
            }
            if (target == null || !_client.CanGrab) return;
            _client.TryGrab(this, target, Mathf.Max(0, handle));
        }

        void OnReleased(GripReleaseReason reason)
        {
            if (Held == null || _client == null) return;
            // 理由（開いた / 見失い）は ClientItemTable が見て、落とす・見失い待ち（Hovering）・猶予などを決める
            _client.OnHandReleased(this, reason == GripReleaseReason.Lost);
        }

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        MaterialPropertyBlock _beamBlock;
        Color? _beamColor;

        /// <summary>ビームの色を変える（MaterialPropertyBlock。共有マテリアルは書き換えない）。同じ色なら何もしない</summary>
        public void SetBeamColor(Color color)
        {
            if (_beam == null || (_beamColor.HasValue && _beamColor.Value == color)) return;
            _beamColor = color;
            _beamBlock ??= new MaterialPropertyBlock();
            _beam.GetPropertyBlock(_beamBlock);
            _beamBlock.SetColor(BaseColorId, color);
            _beamBlock.SetColor(ColorId, color);
            _beam.SetPropertyBlock(_beamBlock);
        }

        /// <summary>今のビームの色（まだ設定していなければ null = マテリアルの色のまま）</summary>
        public Color? BeamColor => _beamColor;

        public void ClearAim() { _aimed = null; _lastAimed = null; _aimedHandle = -1; _lastAimedHandle = -1; }
    }
}
