using DG.Tweening;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    public enum TreasureMode
    {
        /// <summary>物理を止める（置かれている・他の端末が動かしている・飛んでいる途中）</summary>
        Kinematic,
        /// <summary>重力なしで手の目標へ追従（持っている）</summary>
        Follow,
        /// <summary>重力あり（落下）</summary>
        Falling,
    }

    /// <summary>
    /// お宝のプレハブに付ける部品（見た目と物理の配線）。判断は ClientItemTable / HostItemTable にあり、ここは動かすだけ。
    /// VenueRoot の子として置かれ、transform の位置・向きが VenueRoot ローカルの値になる。
    /// 持っている間は重力なしの Rigidbody を、手の目標へ「位置は速度、向きは角速度」で追従させ、
    /// 速度を決めた後に SweepTest で障害物に向かう成分だけを切る（設計書 4.3。試作 S-4 の方式を本番側に写した物）。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class Treasure : MonoBehaviour
    {
        [SerializeField, Tooltip("光る持ち手のコライダー（ビームが当たる用。見た目より大きめ）。M は左右 2 つ")] Collider[] _handles = new Collider[0];
        [SerializeField, Tooltip("持ち手の見た目（光らせる Renderer）")] Renderer[] _handleRenderers = new Renderer[0];
        [SerializeField, Tooltip("値札を出す位置")] Transform _labelAnchor;
        [SerializeField, Tooltip("手に持つときに手へ合わせる点（お宝のローカル座標。モデルの中心付近）")] Vector3 _holdPoint = new Vector3(0f, 0.1f, 0f);

        /// <summary>角で別の面に当たり直すぶんの切り直し回数（試作 S-4 で 3 回で足りた）</summary>
        const int MaxSweepIterations = 3;
        /// <summary>障害物の手前に残す隙間（m）。接触の数値誤差でめり込まないための 2mm（試作 S-4 と同じ）</summary>
        const float SweepSkin = 0.002f;
        /// <summary>追従の回転の角速度の上限（rad/s）。手の向きが急に変わっても振り回さないため（試作 S-4 と同じ）</summary>
        const float MaxAngularSpeed = 30f;

        Rigidbody _rb;
        ThGripParams _grip;
        Material _handleNormal, _handleAimed;
        Tween _flight;
        Transform _followTarget;
        Quaternion _followRelRot = Quaternion.identity;
        readonly RestDetector _rest = new RestDetector();
        Collider[] _bodyColliders;

        public int HandleCount => _handles == null ? 0 : _handles.Length;
        public Collider[] Handles => _handles;
        public Transform LabelAnchor => _labelAnchor != null ? _labelAnchor : transform;
        public ClientItem Model { get; private set; }
        public TreasureMode Mode { get; private set; } = TreasureMode.Kinematic;
        public Rigidbody Body => _rb;
        public bool IsFlying => _flight != null && _flight.IsActive();
        public bool Aimed { get; private set; }

        /// <summary>生成直後に呼ぶ。レイヤー（お宝本体 / 持ち手）と、持ち手の光り方を設定する。</summary>
        public void Initialize(ClientItem model, ThGripParams grip, Material handleNormal, Material handleAimed, int treasureLayer, int handleLayer)
        {
            Model = model;
            _grip = grip;
            _handleNormal = handleNormal;
            _handleAimed = handleAimed;
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = false;
            _rb.isKinematic = true;
            _rb.interpolation = RigidbodyInterpolation.None;   // kinematic で transform を直接動かすモードは補間なし。追従・落下のときだけ Interpolate にする
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _rb.maxAngularVelocity = MaxAngularSpeed;

            var handleSet = new System.Collections.Generic.HashSet<Collider>(_handles);
            var body = new System.Collections.Generic.List<Collider>();
            foreach (var c in GetComponentsInChildren<Collider>(true))
            {
                if (handleSet.Contains(c)) { c.isTrigger = true; if (handleLayer >= 0) c.gameObject.layer = handleLayer; }
                else { body.Add(c); if (treasureLayer >= 0) c.gameObject.layer = treasureLayer; }
            }
            _bodyColliders = body.ToArray();
            SetAimed(false);
        }

        public Collider[] BodyColliders => _bodyColliders;

        // ---- 持ち手の光 ----

        public void SetAimed(bool aimed)
        {
            Aimed = aimed;
            var mat = aimed ? _handleAimed : _handleNormal;
            if (mat == null) return;
            foreach (var r in _handleRenderers) if (r != null) r.sharedMaterial = mat;
        }

        public void SetHandlesActive(bool active)
        {
            foreach (var c in _handles) if (c != null) c.enabled = active;
            foreach (var r in _handleRenderers) if (r != null) r.enabled = active;
        }

        // ---- 動かし方 ----

        /// <summary>物理を止めて、VenueRoot ローカルの位置・向きに置く</summary>
        public void PlaceKinematic(Vector3 localPos, Quaternion localRot)
        {
            KillFlight();
            _followTarget = null;
            Mode = TreasureMode.Kinematic;
            SetKinematicMode();
            transform.localPosition = localPos;
            transform.localRotation = localRot;
        }

        public void StopInPlace()
        {
            KillFlight();
            _followTarget = null;
            Mode = TreasureMode.Kinematic;
            if (_rb == null) return;
            if (!_rb.isKinematic) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
            SetKinematicMode();
        }

        /// <summary>手元へ〔飛ぶ時間〕で飛ばし、着いたら追従に切り替える（飛んでいる間は障害物に当たらない＝減額しない）</summary>
        public void FlyToHand(Transform target, float duration)
        {
            KillFlight();
            Mode = TreasureMode.Kinematic;
            SetKinematicMode();
            _followTarget = null;
            // 着いたときの向き: 今の向きの Y 回転だけを残して立てた向きを、手の向きからの相対で覚える
            var upright = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            _followRelRot = Quaternion.Inverse(target.rotation) * upright;
            Vector3 startPos = transform.position;
            Quaternion startRot = transform.rotation;
            _flight = DOVirtual.Float(0f, 1f, Mathf.Max(0.01f, duration), t =>
            {
                var rot = target.rotation * _followRelRot;
                var pos = target.position - rot * _holdPoint;
                transform.position = Vector3.Lerp(startPos, pos, t);
                transform.rotation = Quaternion.Slerp(startRot, rot, t);
            }).SetEase(Ease.OutCubic).OnComplete(() =>
            {
                _flight = null;
                BeginFollow(target);
            }).SetLink(gameObject);
        }

        /// <summary>手の目標への追従を始める（重力なしの Rigidbody）</summary>
        public void BeginFollow(Transform target)
        {
            KillFlight();
            _followTarget = target;
            Mode = TreasureMode.Follow;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.isKinematic = false;
            _rb.useGravity = false;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        /// <summary>手を開いた。重力ありで落下させる</summary>
        public void BeginFalling()
        {
            KillFlight();
            _followTarget = null;
            Mode = TreasureMode.Falling;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.isKinematic = false;
            _rb.useGravity = true;
            _rest.Reset();
        }

        /// <summary>元の位置・向きへ戻す（否認・待ち上限・取りやめ）。戻ったら Kinematic で置く</summary>
        public void ReturnTo(Vector3 localPos, Quaternion localRot, float duration)
        {
            KillFlight();
            _followTarget = null;
            Mode = TreasureMode.Kinematic;
            SetKinematicMode();
            var parent = transform.parent;
            Vector3 startPos = transform.localPosition;
            Quaternion startRot = transform.localRotation;
            _flight = DOVirtual.Float(0f, 1f, Mathf.Max(0.01f, duration), t =>
            {
                transform.localPosition = Vector3.Lerp(startPos, localPos, t);
                transform.localRotation = Quaternion.Slerp(startRot, localRot, t);
            }).SetEase(Ease.OutCubic).OnComplete(() => _flight = null).SetLink(gameObject);
        }

        /// <summary>kinematic にして transform を直接動かす状態（補間は transform の書き込みを上書きするので切る）</summary>
        void SetKinematicMode()
        {
            _rb.isKinematic = true;
            _rb.useGravity = false;
            _rb.interpolation = RigidbodyInterpolation.None;
        }

        void KillFlight()
        {
            if (_flight != null && _flight.IsActive()) _flight.Kill();
            _flight = null;
        }

        void OnDestroy() => KillFlight();

        /// <summary>落下が止まったか（〔止まったとみなす時間〕静止した）</summary>
        public bool PollSettled(double now, float stopSeconds)
        {
            if (Mode != TreasureMode.Falling) return false;
            return _rest.Update(now, _rb.linearVelocity.magnitude, _rb.angularVelocity.magnitude, stopSeconds);
        }

        // ---- 追従 ----

        void FixedUpdate()
        {
            if (Mode != TreasureMode.Follow || _followTarget == null || _rb == null || _rb.isKinematic) return;
            float dt = Time.fixedDeltaTime;
            var targetRot = _followTarget.rotation * _followRelRot;
            var targetPos = _followTarget.position - targetRot * _holdPoint;

            Vector3 v = (targetPos - _rb.position) / dt;
            float max = _grip.followSpeedMax;
            if (v.sqrMagnitude > max * max) v = v.normalized * max;
            v = ClampBySweep(v, dt);
            _rb.linearVelocity = v;

            Quaternion dq = targetRot * Quaternion.Inverse(_rb.rotation);
            dq.ToAngleAxis(out float angleDeg, out Vector3 axis);
            if (angleDeg > 180f) angleDeg -= 360f;
            Vector3 w = float.IsFinite(axis.x + axis.y + axis.z) ? axis * (angleDeg * Mathf.Deg2Rad / dt) : Vector3.zero;
            if (w.sqrMagnitude > MaxAngularSpeed * MaxAngularSpeed) w = w.normalized * MaxAngularSpeed;
            _rb.angularVelocity = w;
        }

        /// <summary>
        /// 速度のうち障害物に向かう成分だけを、障害物の手前で止まる大きさに切る。表面に沿う成分は残す。
        /// 角では 1 回の切り直しで別の面に当たるので、最大 3 回繰り返す。
        /// </summary>
        Vector3 ClampBySweep(Vector3 v, float dt)
        {
            for (int i = 0; i < MaxSweepIterations; i++)
            {
                float speed = v.magnitude;
                if (speed < 1e-4f) return v;
                Vector3 dir = v / speed;
                float reach = speed * dt + SweepSkin;
                if (!_rb.SweepTest(dir, out RaycastHit hit, reach, QueryTriggerInteraction.Ignore)) return v;

                Vector3 n = hit.normal;
                float into = Vector3.Dot(v, -n);
                if (into <= 0f) return v;
                float gapN = hit.distance * Vector3.Dot(dir, -n);
                float allowedN = Mathf.Max(0f, gapN - SweepSkin) / dt;
                Vector3 tangential = v + n * into;
                v = tangential + (-n) * Mathf.Min(into, allowedN);
            }
            return v;
        }

        /// <summary>手の目標と、お宝の保持点との距離（デバッグ用）</summary>
        public float DistanceToTarget() => _followTarget == null ? 0f : Vector3.Distance(_followTarget.position, transform.TransformPoint(_holdPoint));
    }
}
