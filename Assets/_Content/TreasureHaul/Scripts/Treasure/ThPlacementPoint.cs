using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 配置候補点（置き場所。Environment の VenueRoot の下に置く印）。位置はお宝の底の中心。
    /// 置ける種類（S・M）、入る大きさ（幅・奥行・高さ m。お宝の正面を置き場所の正面に向けたとき）、正面の向き（Y 回転。度）、
    /// ゾーン名、隠す意図、距離帯（0〜2。ゾーンから決まる）を持つ。
    /// </summary>
    public sealed class ThPlacementPoint : MonoBehaviour
    {
        [SerializeField, Range(0, 2), Tooltip("距離帯（ゾーン名が既知のときはゾーンから決まる値が使われる）")] int _band;
        [SerializeField, Tooltip("S を置ける")] bool _allowS = true;
        [SerializeField, Tooltip("M を置ける")] bool _allowM = true;
        [SerializeField, Tooltip("入る大きさ（x=幅 / y=奥行 / z=高さ。m）")] Vector3 _maxSize = new Vector3(100f, 100f, 100f);
        [SerializeField, Tooltip("正面の向き（Y 回転。度。Z+ が 0、X+ が 90）")] float _yawDegrees;
        [SerializeField, Tooltip("ゾーン名（block_front / front_room / back_room / corridor_w / corridor_e / cross / hall）")] string _zone = "";
        [SerializeField, Tooltip("隠す意図のある置き場所か（見えにくい場所）")] bool _hidden;

        public int Band { get => ZoneBand(_zone, _band); set => _band = Mathf.Clamp(value, 0, 2); }
        public bool AllowS { get => _allowS; set => _allowS = value; }
        public bool AllowM { get => _allowM; set => _allowM = value; }
        public Vector3 MaxSize { get => _maxSize; set => _maxSize = value; }
        public float YawDegrees { get => _yawDegrees; set => _yawDegrees = value; }
        public string Zone { get => _zone; set => _zone = value ?? ""; }
        public bool Hidden { get => _hidden; set => _hidden = value; }

        /// <summary>ゾーン名から距離帯。block_front・corridor_w・corridor_e → 0 / front_room・back_room・cross → 1 / hall → 2。知らない名前は fallback</summary>
        public static int ZoneBand(string zone, int fallback)
        {
            switch (zone)
            {
                case "block_front":
                case "corridor_w":
                case "corridor_e": return 0;
                case "front_room":
                case "back_room":
                case "cross": return 1;
                case "hall": return 2;
                default: return Mathf.Clamp(fallback, 0, 2);
            }
        }

        /// <summary>配置に渡す純粋側の値（position は VenueRoot ローカル）</summary>
        public PlacementPoint ToPlacementPoint(Vector3 localPosition) =>
            new PlacementPoint(localPosition, Band, _allowS, _allowM, _maxSize, _yawDegrees, _zone, _hidden);

        void OnDrawGizmos()
        {
            int band = Band;
            Gizmos.color = band == 0 ? Color.green : band == 1 ? Color.yellow : Color.red;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.05f, 0.08f);
            var fwd = Quaternion.Euler(0f, _yawDegrees, 0f) * Vector3.forward;
            Gizmos.DrawLine(transform.position + Vector3.up * 0.05f, transform.position + Vector3.up * 0.05f + fwd * 0.2f);
        }
    }
}
