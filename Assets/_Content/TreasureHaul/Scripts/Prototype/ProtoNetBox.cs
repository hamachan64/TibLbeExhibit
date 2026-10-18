using System;
using System.Collections.Generic;
using Styly.NetSync;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Prototype
{
    /// <summary>
    /// 試作(S-2): NetSyncObject を付けた「お宝の代わりの箱」。持ち主の画面では決まった軌道で動かし、
    /// 相手の画面では NetSync が動かす。軌道は「全端末で共通の時刻（UTC秒）」の関数なので、
    /// 相手の画面に映った位置から、どの時刻の位置かを逆算して遅れを測れる。
    /// 持ち主かどうかで物理（追従）を切り替える: 持ち主 = 動く Rigidbody（速度追従）、相手 = kinematic（transform を NetSync が書く）。
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(NetSyncObject), typeof(Rigidbody), typeof(BoxCollider))]
    [RequireComponent(typeof(ProtoVelocityFollower))]
    public sealed class ProtoNetBox : MonoBehaviour
    {
        public enum Kind { None, Circle, Cross, Head }

        public const double CirclePeriod = 4.0;
        public const float CircleRadius = 1.0f;
        public const float CrossAmplitude = 1.5f;

        [SerializeField] int _index;      // 0 = A, 1 = B
        [SerializeField] string _label = "A";

        NetSyncObject _net;
        Rigidbody _rb;
        ProtoVelocityFollower _follower;
        Renderer _renderer;
        Transform _target;
        MaterialPropertyBlock _mpb;
        Kind _kind = Kind.None;
        bool _physics;
        double _u0;
        bool _configuredMine;
        Kind _configuredKind = (Kind)(-1);
        bool _configuredPhysics;

        // 観測（相手の箱として見ているとき）
        readonly List<(double u, Vector3 p)> _samples = new List<(double, Vector3)>();
        bool _recording;

        public int Index => _index;
        public string Label => _label;
        public NetSyncObject Net => _net;
        public Kind CurrentKind => _kind;
        public IReadOnlyList<(double u, Vector3 p)> Samples => _samples;
        public Vector3 RestPosition => _index == 0 ? new Vector3(-2f, 0.5f, -1f) : new Vector3(2f, 0.5f, -1f);

        /// <summary>目標（軌道上の今の位置）と実際の位置の距離。持ち主の画面で障害物に止められた量を見る用。</summary>
        public float TargetDistance => _target != null ? Vector3.Distance(_target.position, transform.position) : 0f;

        public static double UtcNow() => (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;

        public static Vector3 CircleCenter(int index) => index == 0 ? new Vector3(-1.5f, 0.5f, 0.8f) : new Vector3(1.5f, 0.5f, 0.8f);

        public static Vector3 PathAt(Kind kind, int index, double u, double u0)
        {
            switch (kind)
            {
                case Kind.Circle:
                {
                    double phi = ProtoNetStats.ExpectedPhase(u, CirclePeriod);
                    return CircleCenter(index) + new Vector3(CircleRadius * (float)Math.Cos(phi), 0f, CircleRadius * (float)Math.Sin(phi));
                }
                case Kind.Cross:
                {
                    // A は x 軸、B は z 軸に沿って往復し、原点で交わる（同じ位相なので原点を同時に通る）
                    double phi = ProtoNetStats.ExpectedPhase(u, CirclePeriod);
                    float s = CrossAmplitude * (float)Math.Sin(phi);
                    return index == 0 ? new Vector3(s, 0.5f, 0.5f) : new Vector3(0f, 0.5f, 0.5f + s);
                }
                case Kind.Head:
                {
                    // 正面衝突: 互いの向こう側まで目標を進め、そのまま押し続ける（止まった絵を撮れる）
                    float t = Mathf.Clamp01((float)((u - u0) / 2.0));
                    float x = index == 0 ? Mathf.Lerp(-1.5f, 0.6f, t) : Mathf.Lerp(1.5f, -0.6f, t);
                    return new Vector3(x, 0.5f, 0.5f);
                }
                default:
                    return Vector3.zero;
            }
        }

        void Awake()
        {
            _net = GetComponent<NetSyncObject>();
            _rb = GetComponent<Rigidbody>();
            _follower = GetComponent<ProtoVelocityFollower>();
            _renderer = GetComponent<Renderer>();
            _mpb = new MaterialPropertyBlock();
            var t = new GameObject("Target_" + _label);
            _target = t.transform;
            _target.position = transform.position;
            _follower.Target = _target;
            _follower.enabled = false;
            _rb.isKinematic = true;
        }

        void OnDestroy()
        {
            if (_target != null) Destroy(_target.gameObject);
        }

        public void StartKind(Kind kind, bool physics, double u0)
        {
            _kind = kind;
            _physics = physics;
            _u0 = u0;
            if (kind != Kind.None && _net.IsOwnedByMe)
            {
                var p = PathAt(kind, _index, Math.Max(UtcNow(), u0), u0);
                transform.position = p;
                _rb.position = p;
                _target.position = p;
            }
        }

        public void StopKind() => _kind = Kind.None;

        public void TeleportLocal(Vector3 p)
        {
            transform.position = p;
            _rb.position = p;
            if (_target != null) _target.position = p;
        }

        public void BeginRecord() { _samples.Clear(); _recording = true; }
        public void EndRecord() => _recording = false;

        void Update()
        {
            bool mine = _net.IsOwnedByMe;
            ApplyConfig(mine);

            if (mine && _kind != Kind.None)
            {
                double u = UtcNow();
                var p = PathAt(_kind, _index, u, _u0);
                if (_physics) _target.position = p;
                else { transform.position = p; _target.position = p; }
            }

            // 色: 自分が持ち主 = 緑、他人が持ち主 = オレンジ、持ち主なし = 灰色
            int owner = _net.OwnerClientNo;
            Color c = mine ? new Color(0.2f, 0.8f, 0.3f) : owner != 0 ? new Color(1f, 0.55f, 0.1f) : new Color(0.55f, 0.55f, 0.55f);
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            _renderer.SetPropertyBlock(_mpb);
        }

        void LateUpdate()
        {
            if (_recording && !_net.IsOwnedByMe) _samples.Add((UtcNow(), transform.position));
        }

        void ApplyConfig(bool mine)
        {
            if (mine == _configuredMine && _kind == _configuredKind && _physics == _configuredPhysics) return;
            _configuredMine = mine; _configuredKind = _kind; _configuredPhysics = _physics;
            bool dynamic = mine && _physics && _kind != Kind.None;
            if (dynamic)
            {
                _target.position = transform.position;
                _rb.isKinematic = false;
                _rb.interpolation = RigidbodyInterpolation.Interpolate;
                _follower.enabled = true;
            }
            else
            {
                _follower.enabled = false;
                _rb.interpolation = RigidbodyInterpolation.None;
                if (!_rb.isKinematic) { _rb.linearVelocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
                _rb.isKinematic = true;
            }
        }
    }
}
