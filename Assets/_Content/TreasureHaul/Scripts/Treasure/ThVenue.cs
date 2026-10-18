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

        public static ThVenue Instance { get; private set; }

        public Transform Root => _root != null ? _root : transform;
        public CargoBoardView Board => _board;
        public int PlacementPointCount => _points.Count;

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
        public ThEnvironmentInfo CreateEnvironmentInfo() => new ThEnvironmentInfo { PlacementPointCount = _points.Count };

        public ThTreasureSetup CreateSetup(ThTreasureCatalog catalog) => new ThTreasureSetup
        {
            Points = GetPlacementPoints(),
            Catalog = catalog != null ? catalog.ToSpawnEntries() : new List<SpawnCatalogEntry>(),
            CollectBounds = GetCollectBounds(),
            CatalogHash = catalog != null ? catalog.ComputeHash() : "",
        };
    }
}
