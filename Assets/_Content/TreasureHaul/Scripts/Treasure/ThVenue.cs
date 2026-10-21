using System.Collections.Generic;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 仮の会場（Environment の VenueRoot）をまとめて参照する部品（設計書 1章・4.1）。
    /// 同期する位置はすべて VenueRoot のローカル座標。配置候補点（ローカル座標と距離帯）と、荷台の回収範囲（VenueRoot ローカルの Bounds）を返す。
    /// VenueRoot は回転・拡縮しない前提（荷台の回収範囲を軸並行の Bounds で表すため）。
    /// </summary>
    public sealed class ThVenue : MonoBehaviour
    {
        [SerializeField, Tooltip("VenueRoot（同期する位置の基準）")] Transform _root;
        [SerializeField, Tooltip("配置候補点（並びは全端末で同じになるよう、ここで固定する）")] List<ThPlacementPoint> _points = new List<ThPlacementPoint>();
        [SerializeField, Tooltip("荷台の上の回収範囲（トリガー。VenueRoot に対して回転させない）")] BoxCollider _collectZone;
        [SerializeField, Tooltip("荷台の表示板")] CargoBoardView _board;
        [SerializeField, Tooltip("練習用お宝の置き場所（色番号 0〜3 の 4 つ。納品場所の前）")] List<ThTutorialSlot> _tutorialSlots = new List<ThTutorialSlot>();

        public static ThVenue Instance { get; private set; }

        public Transform Root => _root != null ? _root : transform;
        public CargoBoardView Board => _board;
        public int PlacementPointCount => _points.Count;
        public int TutorialSlotCount => _tutorialSlots.Count;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        public IReadOnlyList<PlacementPoint> GetPlacementPoints()
        {
            var list = new List<PlacementPoint>(_points.Count);
            foreach (var p in _points) list.Add(new PlacementPoint(Root.InverseTransformPoint(p.transform.position), p.Band));
            return list;
        }

        /// <summary>荷台の回収範囲（VenueRoot ローカル）</summary>
        public Bounds GetCollectBounds()
        {
            if (_collectZone == null) return new Bounds(Vector3.zero, Vector3.zero);
            var t = _collectZone.transform;
            var center = Root.InverseTransformPoint(t.TransformPoint(_collectZone.center));
            var ls = t.lossyScale;
            var size = new Vector3(_collectZone.size.x * Mathf.Abs(ls.x), _collectZone.size.y * Mathf.Abs(ls.y), _collectZone.size.z * Mathf.Abs(ls.z));
            return new Bounds(center, size);
        }

        /// <summary>パラメータの組み合わせ検査用（配置候補点の数）</summary>
        public ThEnvironmentInfo CreateEnvironmentInfo() => new ThEnvironmentInfo { PlacementPointCount = _points.Count, TutorialSlotCount = _tutorialSlots.Count };

        /// <summary>練習用お宝の置き場所（色番号 → VenueRoot ローカルの位置・向き（Y 回転））。色番号の順に並べる。枠が 4 つに満たない・色番号が重なる・欠けるときは空（練習用お宝を作らない）</summary>
        public IReadOnlyList<PracticeSlot> GetPracticeSlots()
        {
            var slots = new PracticeSlot?[ThPractice.MaxColors];
            foreach (var s in _tutorialSlots)
            {
                if (s == null || s.ColorIndex < 0 || s.ColorIndex >= slots.Length || slots[s.ColorIndex].HasValue) return new PracticeSlot[0];
                var local = Root.InverseTransformPoint(s.transform.position);
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

        public ThTreasureSetup CreateSetup(ThTreasureCatalog catalog) => new ThTreasureSetup
        {
            Points = GetPlacementPoints(),
            Catalog = catalog != null ? catalog.ToSpawnEntries() : new List<SpawnCatalogEntry>(),
            CollectBounds = GetCollectBounds(),
            CatalogHash = catalog != null ? catalog.ComputeHash() : "",
            PracticeSlots = GetPracticeSlots(),
        };
    }
}
