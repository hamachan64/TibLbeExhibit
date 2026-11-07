using System.Collections.Generic;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 仮の会場（Environment の VenueRoot）をまとめて参照する部品（設計書 1章・4.1）。
    /// 同期する位置はすべて VenueRoot のローカル座標。配置候補点（ローカル座標・距離帯・置ける種類・入る大きさ・向き）と、荷台の回収範囲（VenueRoot ローカルの Bounds）を返す。
    /// VenueRoot は Y 軸の回転だけしてよい（拡縮・傾けはしない）。回収範囲は VenueRoot ローカルの軸並行 Bounds なので、CollectZone は VenueRoot に対して回転させない。
    /// VenueRoot を動かすのは編集時だけ（実行中は動かさない）。モジュールは静的バッチ、灯りとライトプローブはワールドで焼いてあるので、動かしたら「ステージの灯りを焼く」で焼き直す。
    /// </summary>
    public sealed class ThVenue : MonoBehaviour
    {
        [SerializeField, Tooltip("VenueRoot（同期する位置の基準）")] Transform _root;
        [SerializeField, Tooltip("配置候補点（並びは全端末で同じになるよう、ここで固定する）")] List<ThPlacementPoint> _points = new List<ThPlacementPoint>();
        [SerializeField, Tooltip("荷台の上の回収範囲（トリガー。VenueRoot に対して回転させない）")] BoxCollider _collectZone;
        [SerializeField, Tooltip("荷台の表示板")] CargoBoardView _board;
        [SerializeField, Tooltip("待機中に表示板の近くへ浮かべるタイトルロゴ（体験者の端末だけが出し消しする）")] ThTitleLogo _title;
        [SerializeField, Tooltip("練習用お宝の置き場所（色番号 0〜3 の 4 つ。納品場所の前）")] List<ThTutorialSlot> _tutorialSlots = new List<ThTutorialSlot>();

        public static ThVenue Instance { get; private set; }

        public Transform Root => _root != null ? _root : transform;
        public CargoBoardView Board => _board;
        public ThTitleLogo Title => _title;
        public int PlacementPointCount => _points.Count;
        public int TutorialSlotCount => _tutorialSlots.Count;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>
        /// t の位置を VenueRoot ローカルで返す。Root が祖先なら親をたどってローカル値から組み立てる（ワールドを往復しないので、
        /// 恒等の中間の親なら誤差ゼロ。配置の照合ハッシュが端末ごとに食い違わないようにする）。祖先でなければ InverseTransformPoint。
        /// </summary>
        Vector3 LocalPointOf(Transform t)
        {
            var root = Root;
            if (t == root) return Vector3.zero;
            bool inside = false;
            for (var a = t.parent; a != null; a = a.parent) if (a == root) { inside = true; break; }
            if (!inside) return root.InverseTransformPoint(t.position);
            var p = t.localPosition;
            for (var parent = t.parent; parent != root; parent = parent.parent)
                p = parent.localPosition + parent.localRotation * Vector3.Scale(parent.localScale, p);
            return p;
        }

        public IReadOnlyList<PlacementPoint> GetPlacementPoints()
        {
            var list = new List<PlacementPoint>(_points.Count);
            foreach (var p in _points) list.Add(p.ToPlacementPoint(LocalPointOf(p.transform)));
            return list;
        }

        /// <summary>荷台の回収範囲（VenueRoot ローカル）</summary>
        public Bounds GetCollectBounds()
        {
            if (_collectZone == null) return new Bounds(Vector3.zero, Vector3.zero);
            var t = _collectZone.transform;
            var center = _collectZone.center == Vector3.zero ? LocalPointOf(t) : Root.InverseTransformPoint(t.TransformPoint(_collectZone.center));
            var ls = t.lossyScale;
            var size = new Vector3(_collectZone.size.x * Mathf.Abs(ls.x), _collectZone.size.y * Mathf.Abs(ls.y), _collectZone.size.z * Mathf.Abs(ls.z));
            return new Bounds(center, size);
        }

        /// <summary>パラメータの組み合わせ検査用（配置候補点の数）</summary>
        public ThEnvironmentInfo CreateEnvironmentInfo()
        {
            int m = 0, sCount = 0;
            foreach (var p in _points)
            {
                if (p == null) continue;
                if (p.AllowM) m++;
                if (p.AllowS) sCount++;
            }
            return new ThEnvironmentInfo { PlacementPointCount = _points.Count, TutorialSlotCount = _tutorialSlots.Count, MPointCount = m, SPointCount = sCount };
        }

        /// <summary>練習用お宝の置き場所（色番号 → VenueRoot ローカルの位置・向き（Y 回転））。色番号の順に並べる。枠が 4 つに満たない・色番号が重なる・欠けるときは空（練習用お宝を作らない）</summary>
        public IReadOnlyList<PracticeSlot> GetPracticeSlots()
        {
            var slots = new PracticeSlot?[ThPractice.MaxColors];
            foreach (var s in _tutorialSlots)
            {
                if (s == null || s.ColorIndex < 0 || s.ColorIndex >= slots.Length || slots[s.ColorIndex].HasValue) return new PracticeSlot[0];
                var local = LocalPointOf(s.transform);
                var fwd = Root.InverseTransformDirection(s.transform.forward);
                slots[s.ColorIndex] = new PracticeSlot(new Vector3(local.x, 0f, local.z), Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg);
            }
            var list = new List<PracticeSlot>(slots.Length);
            foreach (var s in slots)
            {
                if (!s.HasValue) return new PracticeSlot[0];
                list.Add(s.Value);
            }
            return list;
        }

        public ThTreasureSetup CreateSetup(ThTreasureCatalog catalog)
        {
            var points = GetPlacementPoints();
            return new ThTreasureSetup
        {
            Points = points,
            Catalog = catalog != null ? catalog.ToSpawnEntries() : new List<SpawnCatalogEntry>(),
            CollectBounds = GetCollectBounds(),
            CatalogHash = ThTreasureSetup.ComputeHash(catalog != null ? catalog.ComputeHash() : "", points),
            PracticeSlots = GetPracticeSlots(),
            };
        }
    }
}
