using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// つかむ範囲（GrabZone）の箱の割り付け（純粋な計算。テストできる形に分けてある）。
    /// お宝のどこにビームを当てても握れば持てるように、モデルの大きさから見えない箱を作る。
    /// S（と M 以外）: モデル全体を覆う箱 1 つ。M: モデルを水平の長い軸（ローカル X と Z の長い方。同じなら X）で左右に分けた 2 つ。
    /// </summary>
    public static class GrabZoneLayout
    {
        /// <summary>箱を見た目より大きくする量（各辺に足す。m）</summary>
        public const float DefaultMargin = 0.02f;

        /// <summary>M の分割軸が X か（X と Z が同じなら X）</summary>
        public static bool SplitsAlongX(Bounds model) => model.size.x >= model.size.z;

        /// <summary>
        /// つかむ範囲の箱（Treasure ローカル）。M は 2 つで、分割軸の負側が 0・正側が 1。それ以外は 1 つ。
        /// </summary>
        public static Bounds[] Compute(Bounds model, TreasureKind kind, float margin)
        {
            float m = Mathf.Max(0f, margin);
            if (kind != TreasureKind.M) return new[] { Pad(model.min, model.max, m) };

            var min = model.min;
            var max = model.max;
            if (SplitsAlongX(model))
            {
                float mid = model.center.x;
                return new[]
                {
                    Pad(min, new Vector3(mid, max.y, max.z), m),
                    Pad(new Vector3(mid, min.y, min.z), max, m),
                };
            }
            float midZ = model.center.z;
            return new[]
            {
                Pad(min, new Vector3(max.x, max.y, midZ), m),
                Pad(new Vector3(min.x, min.y, midZ), max, m),
            };
        }

        /// <summary>つかむ範囲の数（M は 2、それ以外は 1）</summary>
        public static int CountFor(TreasureKind kind) => kind == TreasureKind.M ? 2 : 1;

        static Bounds Pad(Vector3 min, Vector3 max, float margin)
        {
            var b = new Bounds();
            b.SetMinMax(min - Vector3.one * margin, max + Vector3.one * margin);
            return b;
        }
    }

    /// <summary>ビームの当たり 1 件（つかむ範囲に当たったもの）。有効かどうか（お宝として対象にできるか）は呼び出し側が決めて渡す</summary>
    public struct GrabCandidate
    {
        public float distance;
        public bool valid;
        public GrabCandidate(float distance, bool valid) { this.distance = distance; this.valid = valid; }
    }

    /// <summary>ビームの対象の選び方（純粋な計算）。壁に遮られるかは候補ごとに呼び出し側が判定して valid に反映する</summary>
    public static class GrabTargetPicker
    {
        /// <summary>壁が無いことを表す距離</summary>
        public const float NoWall = float.PositiveInfinity;

        /// <summary>当たりの一覧から、有効なもので一番近いものの添字を返す（無ければ -1）</summary>
        public static int Pick(GrabCandidate[] candidates, int count)
        {
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < count && i < candidates.Length; i++)
            {
                var c = candidates[i];
                if (!c.valid) continue;
                if (c.distance < bestDist) { bestDist = c.distance; best = i; }
            }
            return best;
        }

        /// <summary>ビームの終点までの長さ（壁があればそこで止める）</summary>
        public static float BeamLength(float length, float wallDistance) => Mathf.Min(length, wallDistance);
    }
}
