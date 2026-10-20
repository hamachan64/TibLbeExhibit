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

    /// <summary>標準: ビームをお宝に当てて握ると手元へ飛んできて、手にしっかりついてくる</summary>
    public sealed class BeamGrabStyle : IGrabStyle
    {
        public float BeamLength(ThGripParams grip) => grip.beamDistance;
        public Pose HoldTarget(Pose palm, Vector3 beamDirection) => palm;
    }

    /// <summary>
    /// 手ごとに 1 つ（左右で独立）。ビーム（肩の推定位置→手のひらの方向への SphereCast）、つかむ範囲の狙い、握ったときのつかむ要求を受け持つ。
    /// 判断（つかめるか）は ClientItemTable にあり、ここは配線だけ。
    /// ビームはつかむ範囲（専用レイヤーのトリガー）だけに当たる。低い瓦礫・床・アバターには止められない（低い瓦礫の陰のお宝にも届くように）。
    /// 高い壁（<c>ThWall</c> レイヤー）だけはビームを止め、壁より奥のお宝は対象にならない（ビームの線も壁の点で止まる）。
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
        int _zoneMask;
        int _wallMask;
        readonly HandJumpFilter _jumpFilter = new HandJumpFilter();

        Treasure _aimed;
        int _aimedZone = -1;
        Treasure _lastAimed;
        int _lastAimedZone = -1;
        double _lastAimedAt = double.NegativeInfinity;
        Vector3 _beamDir = Vector3.forward;

        public HandGripTracker Tracker => _tracker;
        public Handedness Handedness => _tracker != null ? _tracker.Handedness : Handedness.Right;
        /// <summary>端末の表（ClientItemTable）で使う左右</summary>
        public ThHand Hand => Handedness == Handedness.Left ? ThHand.Left : ThHand.Right;
        /// <summary>手に合わせて動く目標（お宝はこれに追従する）</summary>
        public Transform Anchor => _anchor;
        /// <summary>今ビームが当たっているつかむ範囲のお宝（つかめるものだけ）</summary>
        public Treasure Aimed => _aimed;
        /// <summary>今ビームが当たっているつかむ範囲の番号（Treasure.GrabZones の何番目か。-1 = なし）</summary>
        public int AimedZone => _aimedZone;
        /// <summary>この手が申請中・持っているお宝（M は片手予約・両手で持っている間もそのお宝）</summary>
        public Treasure Held { get; set; }
        public IGrabStyle Style { get => _style; set => _style = value ?? new BeamGrabStyle(); }

        public void Initialize(TreasureClient client, HandGripTracker tracker, LineRenderer beam, Material beamMaterial, int grabZoneLayer, int wallLayer)
        {
            _client = client;
            _isTargetable = (t, zone) => _client.IsTargetable(t, Hand, zone);
            _tracker = tracker;
            _beam = beam;
            _zoneMask = grabZoneLayer >= 0 ? 1 << grabZoneLayer : 0;
            _wallMask = wallLayer >= 0 ? 1 << wallLayer : 0;
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
                _aimedZone = -1;
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
            // 手のひらが壁の中・壁の向こう（肩から手のひらまでの線が壁を横切る）なら、そのフレームは対象なし・ビームなし
            if (IsHandBehindWall(shoulder, palm.position, _wallMask))
            {
                _aimed = null;
                _aimedZone = -1;
                SetBeam(false, default, default);
                return;
            }
            float wallDistance = WallDistance(palm.position, _beamDir, length);
            _aimed = Held == null && _client.CanGrab ? FindAimed(palm.position, _beamDir, length, out _aimedZone) : null;
            if (_aimed == null) _aimedZone = -1;
            else { _lastAimed = _aimed; _lastAimedZone = _aimedZone; _lastAimedAt = Now; }
            SetBeam(true, palm.position, palm.position + _beamDir * GrabTargetPicker.BeamLength(length, wallDistance));
        }

        /// <summary>ビームの始点から壁（ThWall）までの距離。太さなしの Raycast（出入口の端でビームが止まりすぎないため）。壁が無ければ <see cref="GrabTargetPicker.NoWall"/></summary>
        float WallDistance(Vector3 origin, Vector3 dir, float length) => WallDistance(origin, dir, length, _wallMask);

        public static float WallDistance(Vector3 origin, Vector3 dir, float length, int wallMask)
        {
            if (wallMask == 0) return GrabTargetPicker.NoWall;
            return Physics.Raycast(origin, dir, out var hit, length, wallMask, QueryTriggerInteraction.Ignore) ? hit.distance : GrabTargetPicker.NoWall;
        }

        /// <summary>
        /// 手のひらが壁の中にある（<c>CheckSphere</c>）か、肩の推定位置から手のひらまでの線分が壁を横切る（壁の向こうへ腕を突き出した）か。
        /// 仮想の壁は体で通り抜けられるので、Raycast だけでは手のひらが壁の中のときに壁を検出できない。
        /// </summary>
        public static bool IsHandBehindWall(Vector3 shoulder, Vector3 palm, int wallMask)
        {
            if (wallMask == 0) return false;
            if (Physics.CheckSphere(palm, 0.01f, wallMask, QueryTriggerInteraction.Ignore)) return true;
            return Physics.Linecast(shoulder, palm, wallMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>始点から目標点（つかむ範囲のビーム上に一番近い点）への線が壁に遮られるか</summary>
        public static bool IsBlockedByWall(Vector3 origin, Vector3 point, int wallMask) =>
            wallMask != 0 && Physics.Linecast(origin, point, wallMask, QueryTriggerInteraction.Ignore);

        Treasure FindAimed(Vector3 origin, Vector3 dir, float length, out int zoneIndex)
        {
            return FindTarget(origin, dir, length, _client.Grip.snapRadius, _zoneMask, _wallMask, _hits, _candidates, _isTargetable, out zoneIndex);
        }

        readonly GrabCandidate[] _candidates = new GrabCandidate[MaxHits];
        /// <summary>Initialize で 1 回だけ作って使い回す（毎フレームのメモリ確保を避ける）</summary>
        System.Func<Treasure, int, bool> _isTargetable;

        /// <summary>
        /// 始点からのビーム（SphereCast）が当たったつかむ範囲のうち、壁に遮られていない有効な一番近いものを選ぶ。
        /// 壁の判定は候補ごとに、始点から「その範囲のビーム上の点に一番近い点」への Linecast で行う（薄い壁の裏に接したお宝を除くため）。
        /// isTargetable は「そのお宝のその範囲を今この手が狙えるか」（予約済みの範囲などを除く）。
        /// </summary>
        public static Treasure FindTarget(Vector3 origin, Vector3 dir, float length, float snapRadius, int zoneMask, int wallMask,
            RaycastHit[] hits, GrabCandidate[] candidates, System.Func<Treasure, int, bool> isTargetable, out int zoneIndex)
        {
            zoneIndex = -1;
            if (zoneMask == 0) return null;
            int n = Physics.SphereCastNonAlloc(origin, snapRadius, dir, hits, length, zoneMask, QueryTriggerInteraction.Collide);
            n = System.Math.Min(n, candidates.Length);
            for (int i = 0; i < n; i++)
            {
                var t = hits[i].collider.GetComponentInParent<Treasure>();
                int zone = t != null ? System.Array.IndexOf(t.GrabZones, hits[i].collider) : -1;   // M は左右 2 つのどちらか。登録されていないコライダーは対象にしない
                bool ok = t != null && zone >= 0 && isTargetable(t, zone);
                if (ok && wallMask != 0)
                {
                    var col = hits[i].collider;
                    var onBeam = origin + dir * hits[i].distance;   // distance = 0（初期重なり）でも同じ式
                    var closest = col.ClosestPoint(onBeam);
                    if (IsBlockedByWall(origin, closest, wallMask)) ok = false;
                }
                candidates[i] = new GrabCandidate(hits[i].distance, ok);
            }
            int best = GrabTargetPicker.Pick(candidates, n);
            if (best < 0) return null;
            var target = hits[best].collider.GetComponentInParent<Treasure>();
            zoneIndex = System.Array.IndexOf(target.GrabZones, hits[best].collider);
            return target;
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
            // 握った瞬間にビームが外れていても、〔狙いの保持〕以内に当たっていたお宝のつかむ範囲を対象にする
            var target = _aimed;
            int zone = _aimedZone;
            if (target == null && _lastAimed != null && Now - _lastAimedAt <= _client.Grip.aimHoldSeconds && _client.IsTargetable(_lastAimed, Hand, _lastAimedZone))
            {
                target = _lastAimed;
                zone = _lastAimedZone;
            }
            if (target == null || !_client.CanGrab) return;
            _client.TryGrab(this, target, Mathf.Max(0, zone));
        }

        void OnReleased(GripReleaseReason reason)
        {
            if (Held == null || _client == null) return;
            // 理由（開いた / 見失い）は ClientItemTable が見て、落とす・見失い待ち（Hovering）・猶予などを決める
            _client.OnHandReleased(this, reason == GripReleaseReason.Lost);
        }

        public void ClearAim() { _aimed = null; _lastAimed = null; _aimedZone = -1; _lastAimedZone = -1; }
    }
}
