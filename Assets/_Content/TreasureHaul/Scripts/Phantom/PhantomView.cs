using DG.Tweening;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 亡霊の見た目（仮の形。半透明のカプセルの体 + 光る目 + 床の視界の扇）。当たり判定は持たない。
    /// 見られている間は、その端末で目と扇を赤くする。位置・向きは PhantomClient が決める。
    /// </summary>
    public sealed class PhantomView : MonoBehaviour
    {
        [SerializeField] Transform _bodyRoot;
        [SerializeField] Renderer _body;
        [SerializeField] Renderer[] _eyes = new Renderer[0];
        [SerializeField] MeshFilter _fan;
        [SerializeField] Renderer _fanRenderer;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>通常 / 見られている間の色</summary>
        public static readonly Color EyeNormal = new Color(0.2f, 0.7f, 0.9f, 1f);
        public static readonly Color EyeAlert = new Color(1f, 0.15f, 0.1f, 1f);
        public static readonly Color FanNormal = new Color(0.6f, 0.95f, 1f, 0.12f);
        public static readonly Color FanAlert = new Color(1f, 0.15f, 0.1f, 0.3f);

        const float FanOffsetY = 0.02f;
        const int FanSegments = 16;

        MaterialPropertyBlock _mpb;
        Mesh _fanMesh;
        bool _alert;
        bool _colorsApplied;
        Tween _bob;
        Vector3 _bodyBasePos;
        bool _hasBase;
        const float BobHeight = 0.08f;

        /// <summary>揺れの基準の高さ（Awake で控えた localPosition.y。再表示を繰り返しても変わらない）</summary>
        public float BodyBaseY => _bodyBasePos.y;
        public bool IsBobbing => _bob != null && _bob.IsActive();
        public Transform BodyRoot => _bodyRoot;

        /// <summary>お宝が吸い込まれる先（体の中心。ワールド）</summary>
        public Vector3 AbsorbPoint => _bodyRoot != null ? _bodyRoot.position : transform.position + Vector3.up;

        void Awake()
        {
            // 当たり判定は持たない（お宝・手と当たらない）。プレハブに残っていても外す
            foreach (var c in GetComponentsInChildren<Collider>(true)) Destroy(c);
            if (_bodyRoot != null) { _bodyBasePos = _bodyRoot.localPosition; _hasBase = true; }
        }

        void OnEnable()
        {
            if (_bodyRoot != null && (_bob == null || !_bob.IsActive()))
            {
                if (!_hasBase) { _bodyBasePos = _bodyRoot.localPosition; _hasBase = true; }
                _bodyRoot.localPosition = _bodyBasePos;   // 非表示で止まった揺れの途中の高さから始めない
                _bob = _bodyRoot.DOLocalMoveY(_bodyBasePos.y + BobHeight, 1.4f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetLink(gameObject);
            }
        }

        void OnDisable()
        {
            if (_bob != null && _bob.IsActive()) _bob.Kill();
            _bob = null;
            if (_bodyRoot != null && _hasBase) _bodyRoot.localPosition = _bodyBasePos;
        }

        void OnDestroy()
        {
            if (_fanMesh != null) Destroy(_fanMesh);
        }

        /// <summary>視界（距離・半角）に合わせて床の扇を作り直し、目の高さに目を置く（目は体の根元 BodyRoot の子。BodyRoot の基準の高さは 0）</summary>
        public void Configure(float range, float halfAngleDegrees, float eyeHeight)
        {
            if (_fanMesh == null) { _fanMesh = new Mesh { name = "PhantomFan" }; }
            BuildFan(_fanMesh, range, halfAngleDegrees);
            if (_fan != null) _fan.sharedMesh = _fanMesh;
            foreach (var e in _eyes)
                if (e != null) { var lp = e.transform.localPosition; e.transform.localPosition = new Vector3(lp.x, eyeHeight, lp.z); }
            _colorsApplied = false;
            ApplyColors();
        }

        /// <summary>前方 +Z に向けた扇形（床。原点が要）の三角形の並び。頂点 = 要 + 弧の分割点</summary>
        public static void BuildFan(Mesh mesh, float range, float halfAngleDegrees)
        {
            var verts = new Vector3[FanSegments + 2];
            var tris = new int[FanSegments * 3];
            verts[0] = new Vector3(0f, FanOffsetY, 0f);
            for (int i = 0; i <= FanSegments; i++)
            {
                float a = Mathf.Lerp(-halfAngleDegrees, halfAngleDegrees, i / (float)FanSegments) * Mathf.Deg2Rad;
                verts[i + 1] = new Vector3(Mathf.Sin(a) * range, FanOffsetY, Mathf.Cos(a) * range);
            }
            for (int i = 0; i < FanSegments; i++)
            {
                // 上から見て時計回り（上向きの面）
                tris[i * 3] = 0; tris[i * 3 + 1] = i + 2; tris[i * 3 + 2] = i + 1;
            }
            mesh.Clear();
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
        }

        /// <summary>見られている間は目と扇を赤くする（この端末だけ）</summary>
        public void SetAlert(bool alert)
        {
            if (_alert == alert && _colorsApplied) return;
            _alert = alert;
            ApplyColors();
        }

        void ApplyColors()
        {
            _colorsApplied = true;
            _mpb ??= new MaterialPropertyBlock();
            Paint(_eyes, _alert ? EyeAlert : EyeNormal);
            if (_fanRenderer != null) Paint(_fanRenderer, _alert ? FanAlert : FanNormal);
        }

        void Paint(Renderer[] rs, Color c) { foreach (var r in rs) if (r != null) Paint(r, c); }

        void Paint(Renderer r, Color c)
        {
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);
            _mpb.SetColor(ColorId, c);
            r.SetPropertyBlock(_mpb);
        }

        public bool Alert => _alert;
        public Color CurrentEyeColor => _alert ? EyeAlert : EyeNormal;
    }
}
