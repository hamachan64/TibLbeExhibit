using System.Collections.Generic;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>お宝の種類。<b>末尾にだけ足す</b>（シリアライズ・通信は数値）。L・秘宝は追加要素*</summary>
    public enum TreasureKind
    {
        S = 0,
        M = 1,
        L = 2,
        Secret = 3,
    }

    /// <summary>お宝の状態（設計書 4.2）。<b>末尾にだけ足す</b>（ThItems で数値のまま送る）</summary>
    public enum ItemState
    {
        Resting = 0,
        Locked = 1,
        Flying = 2,
        Held = 3,
        Hovering = 4,
        Falling = 5,
        Placing = 6,
        Collected = 7,
        Destroyed = 8,
    }

    /// <summary>ThRelease の理由。<b>末尾にだけ足す</b></summary>
    public enum ReleaseReason
    {
        /// <summary>取りやめ（承認前に手を開いた・否認・待ち上限）</summary>
        Cancel = 0,
        /// <summary>手を開いて落とした</summary>
        Dropped = 1,
        /// <summary>M の片手予約の解除（予約した手を開いた・見失い待ちの間に戻らなかった）</summary>
        Unreserve = 2,
    }

    /// <summary>左右の手（端末の表は XR Hands の型を知らないので、番号で持つ）</summary>
    public enum ThHand
    {
        Left = 0,
        Right = 1,
    }

    /// <summary>配置候補点。位置は VenueRoot のローカル座標、band は荷台からの距離帯（0=近い / 1=中 / 2=遠い）</summary>
    public readonly struct PlacementPoint
    {
        public readonly Vector3 LocalPosition;
        public readonly int Band;
        public PlacementPoint(Vector3 localPosition, int band) { LocalPosition = localPosition; Band = band; }
    }

    /// <summary>配置に使うカタログの 1 件（「使う」お宝だけ）。EntryIndex は ThTreasureCatalog.entries の何番目か</summary>
    public readonly struct SpawnCatalogEntry
    {
        public readonly TreasureKind Kind;
        public readonly int EntryIndex;
        public SpawnCatalogEntry(TreasureKind kind, int entryIndex) { Kind = kind; EntryIndex = entryIndex; }
    }

    /// <summary>配置された 1 つのお宝</summary>
    public sealed class SpawnedTreasure
    {
        /// <summary>番号（0 始まり。全端末とホストで同じ）</summary>
        public int Index;
        public TreasureKind Kind;
        public int Value;
        public int PointIndex;
        public Vector3 LocalPosition;
        public float YawDegrees;
        /// <summary>カタログの何番目か（ThTreasureCatalog.entries の添字。種類に合う「使う」お宝が無ければ -1）</summary>
        public int CatalogIndex;
    }

    public sealed class SpawnResult
    {
        public readonly List<SpawnedTreasure> Items = new List<SpawnedTreasure>();
        public readonly List<string> Warnings = new List<string>();
        /// <summary>ホストと端末で配置が同じことを突き合わせるためのハッシュ（ログ用）</summary>
        public string Fingerprint()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var t in Items)
                sb.Append(t.Index).Append(':').Append((int)t.Kind).Append(':').Append(t.Value).Append(':').Append(t.PointIndex).Append(':').Append(t.CatalogIndex).Append(':').Append(Mathf.RoundToInt(t.YawDegrees)).Append(';');
            byte[] bytes;
            using (var sha = System.Security.Cryptography.SHA256.Create()) bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
            var o = new System.Text.StringBuilder();
            for (int i = 0; i < 4; i++) o.Append(bytes[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            return o.ToString();
        }
    }

    /// <summary>
    /// 配置とカタログの束（ホストも端末も同じ物を使う）。位置は VenueRoot のローカル座標。
    /// 荷台の回収範囲 CollectBounds も VenueRoot ローカル。
    /// </summary>
    public sealed class ThTreasureSetup
    {
        public IReadOnlyList<PlacementPoint> Points = new PlacementPoint[0];
        public IReadOnlyList<SpawnCatalogEntry> Catalog = new SpawnCatalogEntry[0];
        public Bounds CollectBounds;
        public string CatalogHash = "";
    }
}
