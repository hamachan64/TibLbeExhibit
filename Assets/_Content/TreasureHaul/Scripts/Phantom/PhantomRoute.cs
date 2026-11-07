using System;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 亡霊の経路（純粋）。VenueRoot ローカルの XZ（x = 幅、y = 奥行）の閉じた折れ線。
    /// 距離（m）から位置と向きを返す。曲がり角では、角の前後〔角の丸め〕の間で向きを次の区間へ滑らかに変える。
    /// </summary>
    public sealed class PhantomRoute
    {
        /// <summary>角の前後で向きを変える長さ（m）。区間の半分を超えない</summary>
        public const float CornerBlend = 0.5f;

        /// <summary>
        /// 初期の経路。中央の部屋の塊（外面 X ±1.08、奥行 3.9〜8.2）の周りの通路の中央を回る。
        /// 外周壁内面 X ±2.28 → 通路中央 X ±1.68、塊の前の中央 奥行 3.55、奥の横通路の中央 奥行 8.8。4 隅は 0.5m 切り落とす。
        /// 入口側の前の中央から、西通路（X −）→ 奥の横通路 → 東通路（X +）の順
        /// </summary>
        public static readonly Vector2[] DefaultPoints =
        {
            new Vector2(0f, 3.55f),
            new Vector2(-1.18f, 3.55f),
            new Vector2(-1.68f, 4.05f),
            new Vector2(-1.68f, 8.3f),
            new Vector2(-1.18f, 8.8f),
            new Vector2(0f, 8.8f),
            new Vector2(1.18f, 8.8f),
            new Vector2(1.68f, 8.3f),
            new Vector2(1.68f, 4.05f),
            new Vector2(1.18f, 3.55f),
        };

        readonly Vector2[] _p;
        readonly float[] _cum;   // _cum[i] = 点 i までの距離。_cum[n] = 全長

        public PhantomRoute(Vector2[] points)
        {
            if (points == null || points.Length < 2) throw new ArgumentException("経路の点は 2 つ以上");
            _p = (Vector2[])points.Clone();
            _cum = new float[_p.Length + 1];
            for (int i = 0; i < _p.Length; i++) _cum[i + 1] = _cum[i] + Vector2.Distance(_p[i], _p[(i + 1) % _p.Length]);
            if (_cum[_p.Length] <= 1e-4f) throw new ArgumentException("経路の長さが 0");
        }

        public int PointCount => _p.Length;
        public float Length => _cum[_p.Length];

        float SegLen(int i) => _cum[i + 1] - _cum[i];

        /// <summary>区間 i の進行方向の Y 回転（度。+Z が 0、+X が 90）</summary>
        public float SegmentYaw(int i)
        {
            var d = _p[(i + 1) % _p.Length] - _p[i % _p.Length];
            return Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
        }

        /// <summary>経路の上の距離（全長で巻き戻る。負も可）の位置（XZ）と向き（Y 回転、度）</summary>
        public void Sample(float distance, out Vector2 position, out float yawDegrees)
        {
            float len = Length;
            float d = distance % len;
            if (d < 0f) d += len;
            int n = _p.Length;
            int seg = 0;
            while (seg < n - 1 && d >= _cum[seg + 1]) seg++;
            float s = d - _cum[seg];
            float segLen = SegLen(seg);
            position = Vector2.Lerp(_p[seg], _p[(seg + 1) % n], segLen > 1e-6f ? s / segLen : 0f);

            int next = (seg + 1) % n;
            int prev = (seg + n - 1) % n;
            float yaw = SegmentYaw(seg);
            float bEnd = Mathf.Min(CornerBlend, segLen * 0.5f, SegLen(next) * 0.5f);
            float bStart = Mathf.Min(CornerBlend, segLen * 0.5f, SegLen(prev) * 0.5f);
            if (bEnd > 1e-4f && s > segLen - bEnd)
            {
                float t = (s - (segLen - bEnd)) / (2f * bEnd);   // 0 → 0.5
                yaw = Mathf.LerpAngle(yaw, SegmentYaw(next), Smooth(t));
            }
            else if (bStart > 1e-4f && s < bStart)
            {
                float t = 0.5f + s / (2f * bStart);   // 0.5 → 1
                yaw = Mathf.LerpAngle(SegmentYaw(prev), yaw, Smooth(t));
            }
            yawDegrees = yaw;
        }

        // 角の中心（t = 0.5）で 2 つの向きの真ん中になるよう、t を 0〜1 の smoothstep にする
        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        /// <summary>
        /// Playing の経過秒から位置・向きを決める（全端末で同じ結果）。
        /// 出発位置 = 全長 × startFraction、そこから speed (m/s) で進む。
        /// </summary>
        public void At(float elapsedSeconds, float speed, float startFraction, out Vector2 position, out float yawDegrees)
        {
            Sample(Length * startFraction + speed * Mathf.Max(0f, elapsedSeconds), out position, out yawDegrees);
        }
    }

    /// <summary>視界の判定（純粋）。水平の距離と角度だけ。壁による遮りは呼び出し側が Raycast で見る</summary>
    public static class PhantomSight
    {
        /// <summary>目（XZ）・向き（Y 回転、度）から見て、頭（XZ）が距離 range 以内・正面から halfAngle 度以内か（境界を含む）</summary>
        public static bool InView(Vector2 eye, float yawDegrees, Vector2 head, float range, float halfAngle)
        {
            var d = head - eye;
            float dist = d.magnitude;
            if (dist > range) return false;
            if (dist < 1e-4f) return true;   // 真下・真上（水平距離 0）は見える
            float rad = yawDegrees * Mathf.Deg2Rad;
            var fwd = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
            float cos = Vector2.Dot(fwd, d / dist);
            float angle = Mathf.Acos(Mathf.Clamp(cos, -1f, 1f)) * Mathf.Rad2Deg;
            return angle <= halfAngle + 1e-4f;
        }
    }

    /// <summary>
    /// 見つかる判定の状態機械（純粋。時刻は引数）。「見られているか」を毎フレーム受け取り、
    /// 〔見つかるまでの時間〕続いたら true を返す。視界を出たら数え直し、見つかった後は〔無視時間〕の間は数えない。
    /// </summary>
    public sealed class PhantomSpotDetector
    {
        /// <summary>1 回の Update で数える時間の上限（秒）</summary>
        public const double MaxStep = 0.1;
        readonly float _spotSeconds;
        readonly float _ignoreSeconds;
        double _lastNow = double.NaN;
        double _ignoreUntil = double.NegativeInfinity;
        float _accum;

        public PhantomSpotDetector(float spotSeconds, float ignoreSeconds)
        {
            _spotSeconds = spotSeconds;
            _ignoreSeconds = ignoreSeconds;
        }

        /// <summary>見られていて数えている最中（見た目を赤くする）</summary>
        public bool Watching { get; private set; }
        /// <summary>0〜1 の進み</summary>
        public float Progress => _spotSeconds > 0f ? Mathf.Clamp01(_accum / _spotSeconds) : 0f;

        public void Reset()
        {
            _lastNow = double.NaN;
            _accum = 0f;
            Watching = false;
            _ignoreUntil = double.NegativeInfinity;
        }

        /// <summary>毎フレーム。「見つかった」瞬間だけ true</summary>
        public bool Update(bool seen, double now)
        {
            // 一時停止・フリーズの後の 1 フレームで一気に数えない（1 フレームの上限 0.1 秒）
            double dt = double.IsNaN(_lastNow) ? 0.0 : Math.Min(MaxStep, Math.Max(0.0, now - _lastNow));
            _lastNow = now;
            if (now < _ignoreUntil || !seen)
            {
                _accum = 0f;
                Watching = false;
                return false;
            }
            Watching = true;
            _accum += (float)dt;
            if (_accum + 1e-6f < _spotSeconds) return false;
            _accum = 0f;
            Watching = false;
            _ignoreUntil = now + _ignoreSeconds;
            return true;
        }
    }
}
