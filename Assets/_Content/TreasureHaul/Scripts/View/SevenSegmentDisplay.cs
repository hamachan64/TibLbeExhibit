using System;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 7 セグメント表示（5 桁＋千の位の区切り「,」＋「G」）をコードで作るメッシュで出す。外部フォントなし。
    /// 点灯／消灯は頂点色の R（1=点灯）で切り替え、シェーダー TIB/TreasureHaul/SegmentDisplay が色にする。
    /// 変化がないときはメッシュを書き換えない。メッシュは +Z 側から見える向き（法線 +Z）。+Z 側から -Z を向いて見る人の右は -X なので、桁は X を反転して並べる（左から右へ読める）。
    /// 表示範囲（_width × _height m）の中心がこの GameObject の原点。
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class SevenSegmentDisplay : MonoBehaviour
    {
        // 桁の幾何（高さ 1 の単位。最後に表示範囲へ拡縮する）
        const float DigitW = 0.58f;
        const float Thick = 0.12f;
        const float Gap = 0.02f;
        const float DigitSpacing = 0.22f;
        const float CommaExtra = 0.2f;     // 2 桁目と 3 桁目の間に足す幅（区切りの置き場所）
        const float GSpacing = 0.34f;      // 一の位と G の間
        const int CellCount = SevenSegment.DigitCount + 1;     // 5 桁 + G
        const int VertsPerSegment = 6;
        const int CommaVerts = 4;
        const int CommaBase = CellCount * SevenSegment.SegmentCount * VertsPerSegment;
        const int SpurBase = CommaBase + CommaVerts;     // 「G」の右の内向きの短い横棒（C と見分けるため。常に点灯）
        const int TotalVerts = SpurBase + VertsPerSegment;
        static readonly int FlashId = Shader.PropertyToID("_Flash");

        [SerializeField, Tooltip("表示範囲の幅（m）。ここに収まるように桁を並べる")] float _width = 1.6f;
        [SerializeField, Tooltip("表示範囲の高さ（m）")] float _height = 0.4f;
        [SerializeField, Range(0f, 0.3f), Tooltip("斜体の傾き（高さに対する右への倒れ）")] float _skew = 0.1f;

        MeshFilter _filter;
        MeshRenderer _renderer;
        MaterialPropertyBlock _block;
        Mesh _mesh;
        Color32[] _colors;
        // 前回の点灯状態（5 桁のセグメントのビット・区切り）。同じなら頂点色を書き換えない
        readonly int[] _lastMasks = new int[SevenSegment.DigitCount];
        readonly int[] _newMasks = new int[SevenSegment.DigitCount];
        bool _lastComma;
        bool _hasState;
        bool _geometryDirty = true;
        float _lastFlash = -1f;

        /// <summary>いま表示している額（0〜99,999。まだ何も出していなければ -1）</summary>
        public int ValueShown { get; private set; } = -1;

        void OnValidate() => _geometryDirty = true;
        void OnDestroy()
        {
            if (_mesh == null) return;
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
        }

        /// <summary>表示範囲の大きさ（m）を変える。モデルの画面の範囲に合わせるとき</summary>
        public void SetSize(float width, float height)
        {
            if (Mathf.Approximately(_width, width) && Mathf.Approximately(_height, height)) return;
            _width = width; _height = height;
            _geometryDirty = true;
        }

        /// <summary>合計額を表示する（0〜99,999 に収める）。表示が変わらないときはメッシュを書き換えない。</summary>
        public void Show(int total)
        {
            int v = SevenSegment.Clamp(total);
            ValueShown = v;
            Ensure();
            var chars = SevenSegment.Digits(v);
            bool comma = SevenSegment.CommaLit(v);
            bool changed = !_hasState || comma != _lastComma;
            for (int i = 0; i < _newMasks.Length; i++)
            {
                _newMasks[i] = SevenSegment.Mask(chars[i]);
                if (_newMasks[i] != _lastMasks[i]) changed = true;
            }
            if (!changed) return;
            Array.Copy(_newMasks, _lastMasks, _newMasks.Length);
            _lastComma = comma;
            _hasState = true;
            WriteColors();
        }

        /// <summary>明るさの足し込み（0〜1）。納品で一瞬光らせる。値が変わらなければ何もしない。</summary>
        public void SetFlash(float flash)
        {
            flash = Mathf.Clamp01(flash);
            if (Mathf.Approximately(flash, _lastFlash)) return;
            _lastFlash = flash;
            Ensure();
            _renderer.GetPropertyBlock(_block);
            _block.SetFloat(FlashId, flash);
            _renderer.SetPropertyBlock(_block);
        }

        void Ensure()
        {
            if (_mesh == null)
            {
                _filter = GetComponent<MeshFilter>();
                _renderer = GetComponent<MeshRenderer>();
                _block = new MaterialPropertyBlock();
                _mesh = new Mesh { name = "SevenSegmentDisplay" };
                _colors = new Color32[TotalVerts];
                _filter.sharedMesh = _mesh;
                _geometryDirty = true;
                _hasState = false;
            }
            if (_geometryDirty)
            {
                _geometryDirty = false;
                BuildGeometry();
                if (_hasState) WriteColors();   // 形を作り直したので、いまの点灯状態を頂点色へ書き直す
            }
        }

        void WriteColors()
        {
            var off = new Color32(0, 0, 0, 255);
            var on = new Color32(255, 0, 0, 255);
            for (int c = 0; c < CellCount; c++)
            {
                int mask = c < SevenSegment.DigitCount ? _lastMasks[c] : SevenSegment.Mask('G');
                for (int s = 0; s < SevenSegment.SegmentCount; s++)
                {
                    var col = (mask & (1 << s)) != 0 ? on : off;
                    int b = (c * SevenSegment.SegmentCount + s) * VertsPerSegment;
                    for (int k = 0; k < VertsPerSegment; k++) _colors[b + k] = col;
                }
            }
            var cc = _lastComma ? on : off;
            for (int k = 0; k < CommaVerts; k++) _colors[CommaBase + k] = cc;
            for (int k = 0; k < VertsPerSegment; k++) _colors[SpurBase + k] = on;
            _mesh.SetColors(_colors);
        }

        // ---- 形 ----
        void BuildGeometry()
        {
            var v = new Vector3[TotalVerts];
            var tris = new int[(CellCount * SevenSegment.SegmentCount * 4 + 2 + 4) * 3];
            int ti = 0;

            float x = 0f;
            float commaX = 0f;
            float gX = 0f;
            for (int c = 0; c < CellCount; c++)
            {
                if (c == SevenSegment.DigitCount) gX = x;
                for (int s = 0; s < SevenSegment.SegmentCount; s++)
                {
                    int b = (c * SevenSegment.SegmentCount + s) * VertsPerSegment;
                    SegmentHexagon(s, x, v, b);
                    ti = AddHexTris(tris, ti, b);
                }
                float end = x + DigitW;
                x = end + DigitSpacing;
                if (c == 1) { x += CommaExtra; commaX = (end + x) * 0.5f; }   // 千の位の区切りは 2 桁目の後ろ
                if (c == SevenSegment.DigitCount - 1) x += GSpacing - DigitSpacing;
            }

            // 区切り（,）: 2 桁目と 3 桁目の間の下寄りに置く小さな斜めの四角
            const float cw = Thick * 0.9f, ch = Thick * 1.6f;
            const float y0 = -0.03f;
            v[CommaBase + 0] = Shear(commaX - cw * 0.5f - 0.03f, y0);
            v[CommaBase + 1] = Shear(commaX - cw * 0.5f + 0.03f, y0 + ch);
            v[CommaBase + 2] = Shear(commaX + cw * 0.5f + 0.03f, y0 + ch);
            v[CommaBase + 3] = Shear(commaX + cw * 0.5f - 0.03f, y0);
            // 組み立ては X 右・Y 上の平面で時計回り。最後に X を反転するので、+Z 側から見ると時計回り（Unity の表）になる
            tris[ti++] = CommaBase; tris[ti++] = CommaBase + 1; tris[ti++] = CommaBase + 2;
            tris[ti++] = CommaBase; tris[ti++] = CommaBase + 2; tris[ti++] = CommaBase + 3;

            // 「G」の内向きの横棒: 中段の右半分（c の上端につながる）
            SpurHexagon(gX, v, SpurBase);
            ti = AddHexTris(tris, ti, SpurBase);

            // 表示範囲に収めて中央へ（コンマの底は 1 桁の底より少し下がるので、全頂点の範囲で測る）
            var min = new Vector3(float.MaxValue, float.MaxValue, 0f);
            var max = new Vector3(float.MinValue, float.MinValue, 0f);
            for (int i = 0; i < v.Length; i++)
            {
                min = Vector3.Min(min, v[i]);
                max = Vector3.Max(max, v[i]);
            }
            float bw = Mathf.Max(1e-4f, max.x - min.x), bh = Mathf.Max(1e-4f, max.y - min.y);
            float scale = Mathf.Min(_width / bw, _height / bh);
            var center = (min + max) * 0.5f;
            var normals = new Vector3[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var p = v[i] - center;
                v[i] = new Vector3(-p.x * scale, p.y * scale, 0f);   // 見る人の右は -X（クラスのコメント参照）
                normals[i] = Vector3.forward;
            }
            // G の横棒は消灯の中段（g）と同じ面に重なるので、見る人の側へ少し出して奥行きのちらつきを防ぐ
            for (int i = SpurBase; i < SpurBase + VertsPerSegment; i++) v[i].z = 0.0005f;

            _mesh.Clear();
            _mesh.vertices = v;
            _mesh.normals = normals;
            _mesh.triangles = tris;
            _mesh.SetColors(_colors);
            _mesh.RecalculateBounds();
        }

        Vector3 Shear(float x, float y) => new Vector3(x + y * _skew, y, 0f);

        // セグメント s（0=a 〜 6=g）の六角形を、桁の左端 x0 の桁に置く。見る人（+Z 側）から見て時計回りの 6 頂点
        void SegmentHexagon(int s, float x0, Vector3[] v, int b)
        {
            float t = Thick, h = t * 0.5f;
            float lh = DigitW - t - 2 * Gap;           // 横のセグメントの長さ
            float lv = 0.5f - t - 2 * Gap;             // 縦のセグメントの長さ
            float cx, cy;
            bool horizontal;
            switch (s)
            {
                case 0: horizontal = true; cx = DigitW * 0.5f; cy = 1f - h; break;      // a
                case 1: horizontal = false; cx = DigitW - h; cy = 0.75f; break;         // b
                case 2: horizontal = false; cx = DigitW - h; cy = 0.25f; break;         // c
                case 3: horizontal = true; cx = DigitW * 0.5f; cy = h; break;           // d
                case 4: horizontal = false; cx = h; cy = 0.25f; break;                  // e
                case 5: horizontal = false; cx = h; cy = 0.75f; break;                  // f
                default: horizontal = true; cx = DigitW * 0.5f; cy = 0.5f; break;       // g
            }
            cx += x0;
            var p = new Vector2[6];
            if (horizontal)
            {
                float l = lh * 0.5f;
                p[0] = new Vector2(-l, 0); p[1] = new Vector2(-l + h, h); p[2] = new Vector2(l - h, h);
                p[3] = new Vector2(l, 0); p[4] = new Vector2(l - h, -h); p[5] = new Vector2(-l + h, -h);
            }
            else
            {
                float l = lv * 0.5f;
                p[0] = new Vector2(0, l); p[1] = new Vector2(h, l - h); p[2] = new Vector2(h, -l + h);
                p[3] = new Vector2(0, -l); p[4] = new Vector2(-h, -l + h); p[5] = new Vector2(-h, l - h);
            }
            for (int i = 0; i < 6; i++) v[b + i] = Shear(cx + p[i].x, cy + p[i].y);
        }

        // 「G」の中段の右半分の横棒（g の右半分）。頂点の並びは SegmentHexagon の横のセグメントと同じ
        void SpurHexagon(float x0, Vector3[] v, int b)
        {
            float t = Thick, h = t * 0.5f;
            float right = DigitW - Gap, left = DigitW * 0.42f;
            float cy = 0.5f;
            var p = new[]
            {
                new Vector2(left, 0), new Vector2(left + h, h), new Vector2(right - h, h),
                new Vector2(right, 0), new Vector2(right - h, -h), new Vector2(left + h, -h),
            };
            for (int i = 0; i < 6; i++) v[b + i] = Shear(x0 + p[i].x, cy + p[i].y);
        }

        // 六角形（凸・時計回りの 6 頂点）を、頂点 0 を軸に 4 枚の三角形へ
        static int AddHexTris(int[] t, int i, int b)
        {
            t[i++] = b; t[i++] = b + 1; t[i++] = b + 2;
            t[i++] = b; t[i++] = b + 2; t[i++] = b + 3;
            t[i++] = b; t[i++] = b + 3; t[i++] = b + 4;
            t[i++] = b; t[i++] = b + 4; t[i++] = b + 5;
            return i;
        }
    }
}
