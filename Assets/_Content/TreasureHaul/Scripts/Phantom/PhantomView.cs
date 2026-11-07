using DG.Tweening;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 亡霊（墓守の霊）の見た目（半透明の体 + 光る目・灯り + 床の視界の扇。灯りは吊り点で小さく揺れる）。当たり判定は持たない。
    /// 見られている間は、その端末で目と扇を赤くする。位置・向きは PhantomClient が決める。
    /// </summary>
    public sealed class PhantomView : MonoBehaviour
    {
        [SerializeField] Transform _bodyRoot;
        [SerializeField] Renderer _body;
        [SerializeField] Renderer[] _eyes = new Renderer[0];
        [SerializeField, Tooltip("目と同じ色（通常・警告）だけを塗る物（灯りの光る芯など）。高さは変えない")] Renderer[] _glows = new Renderer[0];
        [SerializeField] MeshFilter _fan;
        [SerializeField] Renderer _fanRenderer;
        [SerializeField, Tooltip("BodyRoot の下のモデル全体（任意。あれば、目の高さが eyeHeight に合うよう、この物ごと上下させる。無ければ目だけを動かす）")] Transform _model;
        [SerializeField, Tooltip("灯りの吊り点（任意。あれば前後に小さく揺らす。無ければ揺らさない）")] Transform _lanternPivot;

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
        /// <summary>灯りの揺れ: 吊り点の前後（ローカル X 軸まわり）に ±度、片道の秒数（往復で周期 2 秒）</summary>
        const float LanternSwingDegrees = 6f;
        const float LanternSwingSeconds = 1f;
        bool _hasModelBase;
        Vector3 _modelBaseLocalPos;
        float _modelEyeHeight;   // モデルの目の高さ（BodyRoot から見た。最初に控える）
        Tween _lanternSwing;
        Quaternion _lanternBaseRot;
        bool _hasLanternBase;

        public bool IsLanternSwinging => _lanternSwing != null && _lanternSwing.IsActive();
        /// <summary>灯りの揺れの基準の角度（Awake で控えた localRotation。再表示を繰り返しても変わらない）</summary>
        public Quaternion LanternBaseRotation => _lanternBaseRot;

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
            if (_lanternPivot != null) { _lanternBaseRot = _lanternPivot.localRotation; _hasLanternBase = true; }
            CaptureModelBase();
        }

        /// <summary>モデルの基準の位置と目の高さを 1 回だけ控える（Awake、または Awake 前に Configure された場合は最初の Configure）</summary>
        void CaptureModelBase()
        {
            if (_hasModelBase || _model == null || _eyes.Length == 0 || _eyes[0] == null) return;
            var frame = _bodyRoot != null ? _bodyRoot : transform;
            _modelBaseLocalPos = _model.localPosition;
            _modelEyeHeight = frame.InverseTransformPoint(_eyes[0].transform.position).y;
            _hasModelBase = true;
        }

        void OnEnable()
        {
            if (_bodyRoot != null && (_bob == null || !_bob.IsActive()))
            {
                if (!_hasBase) { _bodyBasePos = _bodyRoot.localPosition; _hasBase = true; }
                _bodyRoot.localPosition = _bodyBasePos;   // 非表示で止まった揺れの途中の高さから始めない
                _bob = _bodyRoot.DOLocalMoveY(_bodyBasePos.y + BobHeight, 1.4f).SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetLink(gameObject);
            }
            if (_lanternPivot != null && (_lanternSwing == null || !_lanternSwing.IsActive()))
            {
                if (!_hasLanternBase) { _lanternBaseRot = _lanternPivot.localRotation; _hasLanternBase = true; }
                SetLanternAngle(-LanternSwingDegrees);   // 非表示で止まった揺れの途中の角度から始めない
                _lanternSwing = DOTween.To(SetLanternAngle, -LanternSwingDegrees, LanternSwingDegrees, LanternSwingSeconds)
                    .SetEase(Ease.InOutSine).SetLoops(-1, LoopType.Yoyo).SetLink(gameObject);
            }
        }

        void OnDisable()
        {
            if (_bob != null && _bob.IsActive()) _bob.Kill();
            _bob = null;
            if (_bodyRoot != null && _hasBase) _bodyRoot.localPosition = _bodyBasePos;
            if (_lanternSwing != null && _lanternSwing.IsActive()) _lanternSwing.Kill();
            _lanternSwing = null;
            if (_lanternPivot != null && _hasLanternBase) _lanternPivot.localRotation = _lanternBaseRot;
        }

        void SetLanternAngle(float degrees) => _lanternPivot.localRotation = _lanternBaseRot * Quaternion.Euler(degrees, 0f, 0f);

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
            CaptureModelBase();
            if (_hasModelBase)
            {
                // 目の高さを eyeHeight に合わせるため、モデル全体（目・闇・灯りが頭巾と一緒に）を上下させる。目だけを動かすと頭巾の穴から外れる
                _model.localPosition = _modelBaseLocalPos + Vector3.up * (eyeHeight - _modelEyeHeight);
            }
            else
            {
                // _model が無い古い構成: 従来どおり目を動かす。体の根元（BodyRoot。無ければこの物）から見た高さで合わせ、x・z は動かさない
                var frame = _bodyRoot != null ? _bodyRoot : transform;
                foreach (var e in _eyes)
                {
                    if (e == null) continue;
                    var lp = frame.InverseTransformPoint(e.transform.position);
                    e.transform.position = frame.TransformPoint(new Vector3(lp.x, eyeHeight, lp.z));
                }
            }
            _colorsApplied = false;
            ApplyColors();
        }

        /// <summary>前方 +Z に向けた扇形（床。原点が要）の三角形の並び。頂点 = 要 + 弧の分割点。UV は u = 角度（左端 0 〜 右端 1）・v = 要からの距離（要 0 〜 弧 1）</summary>
        public static void BuildFan(Mesh mesh, float range, float halfAngleDegrees)
        {
            var verts = new Vector3[FanSegments + 2];
            var uvs = new Vector2[FanSegments + 2];
            var tris = new int[FanSegments * 3];
            verts[0] = new Vector3(0f, FanOffsetY, 0f);
            uvs[0] = new Vector2(0.5f, 0f);   // 要: u = 中央・v = 0
            for (int i = 0; i <= FanSegments; i++)
            {
                float a = Mathf.Lerp(-halfAngleDegrees, halfAngleDegrees, i / (float)FanSegments) * Mathf.Deg2Rad;
                verts[i + 1] = new Vector3(Mathf.Sin(a) * range, FanOffsetY, Mathf.Cos(a) * range);
                uvs[i + 1] = new Vector2(i / (float)FanSegments, 1f);   // 弧: u = 左端 0 〜 右端 1・v = 1
            }
            for (int i = 0; i < FanSegments; i++)
            {
                // 上から見て時計回り（上向きの面）
                tris[i * 3] = 0; tris[i * 3 + 1] = i + 2; tris[i * 3 + 2] = i + 1;
            }
            mesh.Clear();
            mesh.vertices = verts;
            mesh.uv = uvs;
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
            Paint(_glows, _alert ? EyeAlert : EyeNormal);
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
        public System.Collections.Generic.IReadOnlyList<Renderer> Glows => _glows;
        public Color CurrentEyeColor => _alert ? EyeAlert : EyeNormal;
    }
}
