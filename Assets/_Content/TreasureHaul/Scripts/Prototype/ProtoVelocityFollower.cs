using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Prototype
{
    /// <summary>
    /// 試作(S-4): 重力なしの Rigidbody を、目標 Transform へ「位置は速度、向きは角速度」で追従させる。
    /// 設計書 4.3。何もない所では1物理ステップで追いつき、障害物に当たると止まって表面に沿って滑る（はず）。
    /// 目標の更新より後に動かすため、実行順を後ろにする。
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ProtoVelocityFollower : MonoBehaviour
    {
        [SerializeField] Transform _target;
        [SerializeField, Min(0.1f)] float _maxSpeed = 10f;
        [SerializeField, Min(0.1f)] float _maxAngularSpeed = 30f;

        [Tooltip("true: 速度を決める前に SweepTest で障害物の手前までに切り、表面に沿う成分は残す（めり込み対策。既定 false = 従来どおり）")]
        [SerializeField] bool _sweepStop;
        [SerializeField, Min(0f)] float _sweepSkin = 0.002f;

        const int MaxSweepIterations = 3;
        Rigidbody _rb;
        bool _contactSinceLastStep;

        public bool SweepStop { get => _sweepStop; set => _sweepStop = value; }

        public Transform Target { get => _target; set => _target = value; }
        public float MaxSpeed => _maxSpeed;
        public Rigidbody Body => _rb;

        /// <summary>前回の物理ステップ以降に何かと接触していたか。読むと false に戻す。</summary>
        public bool ConsumeContact()
        {
            bool c = _contactSinceLastStep;
            _contactSinceLastStep = false;
            return c;
        }

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _rb.maxAngularVelocity = _maxAngularSpeed;
        }

        void FixedUpdate()
        {
            if (_target == null) return;
            float dt = Time.fixedDeltaTime;

            Vector3 v = (_target.position - _rb.position) / dt;
            if (v.sqrMagnitude > _maxSpeed * _maxSpeed) v = v.normalized * _maxSpeed;
            if (_sweepStop) v = ClampBySweep(v, dt);
            _rb.linearVelocity = v;

            Quaternion dq = _target.rotation * Quaternion.Inverse(_rb.rotation);
            dq.ToAngleAxis(out float angleDeg, out Vector3 axis);
            if (angleDeg > 180f) angleDeg -= 360f;
            Vector3 w = float.IsFinite(axis.x + axis.y + axis.z) ? axis * (angleDeg * Mathf.Deg2Rad / dt) : Vector3.zero;
            if (w.sqrMagnitude > _maxAngularSpeed * _maxAngularSpeed) w = w.normalized * _maxAngularSpeed;
            _rb.angularVelocity = w;
        }

        /// <summary>
        /// 速度のうち障害物に向かう成分だけを、障害物の手前で止まる大きさに切る。表面に沿う成分は残す。
        /// 角では1回の切り直しで別の面に当たるので、数回繰り返す。
        /// </summary>
        Vector3 ClampBySweep(Vector3 v, float dt)
        {
            for (int i = 0; i < MaxSweepIterations; i++)
            {
                float speed = v.magnitude;
                if (speed < 1e-4f) return v;
                Vector3 dir = v / speed;
                float reach = speed * dt + _sweepSkin;
                if (!_rb.SweepTest(dir, out RaycastHit hit, reach, QueryTriggerInteraction.Ignore)) return v;

                Vector3 n = hit.normal;
                float into = Vector3.Dot(v, -n);
                if (into <= 0f) return v;
                _contactSinceLastStep = true; // 手前で止めた場合も「押し付け中」として数える（計測用）
                // 面までの法線方向の隙間（sweep の距離は進行方向に沿った値なので、法線方向に直す）
                float gapN = hit.distance * Vector3.Dot(dir, -n);
                float allowedN = Mathf.Max(0f, gapN - _sweepSkin) / dt;
                Vector3 tangential = v + n * into;
                v = tangential + (-n) * Mathf.Min(into, allowedN);
            }
            return v;
        }

        void OnCollisionEnter(Collision c) => _contactSinceLastStep = true;
        void OnCollisionStay(Collision c) => _contactSinceLastStep = true;
    }
}
