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

    /// <summary>
    /// 配置候補点（置き場所）。位置は VenueRoot のローカル座標（お宝の底の中心を置く点）。
    /// Band は距離帯（0=近い / 1=中 / 2=遠い。ゾーンから決まる）。AllowS / AllowM は置ける種類、MaxSize は入る大きさ（幅・奥行・高さ m。
    /// お宝の正面を置き場所の正面に向けたときの寸法）、YawDegrees は正面の向き（Y 回転。度）。
    /// </summary>
    public readonly struct PlacementPoint
    {
        /// <summary>入る大きさを指定しないときの値（どんなお宝でも入る）</summary>
        public static readonly Vector3 Unlimited = new Vector3(100f, 100f, 100f);

        public readonly Vector3 LocalPosition;
        public readonly int Band;
        public readonly bool AllowS;
        public readonly bool AllowM;
        public readonly Vector3 MaxSize;
        public readonly float YawDegrees;
        public readonly string Zone;
        public readonly bool Hidden;

        public PlacementPoint(Vector3 localPosition, int band, bool allowS = true, bool allowM = true, Vector3? maxSize = null, float yawDegrees = 0f, string zone = "", bool hidden = false)
        {
            LocalPosition = localPosition;
            Band = band;
            AllowS = allowS;
            AllowM = allowM;
            MaxSize = maxSize ?? Unlimited;
            YawDegrees = yawDegrees;
            Zone = zone ?? "";
            Hidden = hidden;
        }
    }

    /// <summary>練習用お宝の番号の約束（ホスト・端末・テストが同じ物を使う）。番号は <see cref="IndexBase"/> ＋ 色番号。本編のお宝は 0〜22 なので重ならない</summary>
    public static class ThPractice
    {
        public const int IndexBase = 100;
        /// <summary>色（と TutorialSlot）の数。最大参加人数と同じ</summary>
        public const int MaxColors = 4;

        public static int IndexOf(int color) => IndexBase + color;
        public static bool IsPracticeIndex(int index) => index >= IndexBase && index < IndexBase + MaxColors;
        /// <summary>練習用お宝の番号から色番号（練習用でなければ -1）</summary>
        public static int ColorOf(int index) => IsPracticeIndex(index) ? index - IndexBase : -1;
    }

    /// <summary>練習用お宝の置き場所（TutorialSlot。VenueRoot のローカル座標。色番号 = 並びの添字）</summary>
    public readonly struct PracticeSlot
    {
        public readonly Vector3 LocalPosition;
        public readonly float YawDegrees;
        public PracticeSlot(Vector3 localPosition, float yawDegrees) { LocalPosition = localPosition; YawDegrees = yawDegrees; }
    }

    /// <summary>
    /// 配置に使うカタログの 1 件（「使う」お宝だけ）。EntryIndex は ThTreasureCatalog.entries の何番目か、Id は並べ替えの同順位の決め手。
    /// ValueMin / ValueMax は値段の帯（100G 刻み）、Size は大きさ（x=幅・y=奥行・z=高さ。m。正面を向けたときの寸法で、PlacementPoint.MaxSize と同じ並び）。
    /// </summary>
    public readonly struct SpawnCatalogEntry
    {
        public readonly TreasureKind Kind;
        public readonly int EntryIndex;
        public readonly string Id;
        public readonly int ValueMin;
        public readonly int ValueMax;
        public readonly Vector3 Size;
        public SpawnCatalogEntry(TreasureKind kind, int entryIndex, string id = null, int valueMin = 100, int valueMax = 100, Vector3 size = default)
        {
            Kind = kind;
            EntryIndex = entryIndex;
            Id = id ?? entryIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ValueMin = valueMin;
            ValueMax = valueMax;
            Size = size;
        }
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
        /// <summary>練習用お宝の置き場所（色番号 0〜3 の順。TutorialSlot の無い環境では空 = 練習用お宝を作らない）</summary>
        public IReadOnlyList<PracticeSlot> PracticeSlots = new PracticeSlot[0];

        static int Mm(float meters) => Mathf.RoundToInt(meters * 1000f);

        /// <summary>
        /// 端末とホストで照合するハッシュ。カタログのハッシュに、置き場所の一覧（並び順に、位置〔mm〕・S/M のフラグ・入る大きさ〔mm〕・正面の向き〔0.1° に丸める〕・距離帯）を混ぜる。
        /// 置き場所の属性がビルドや端末で食い違うと、配置がずれるのを照合で見つける（食い違いの扱いはカタログと同じ）。
        /// </summary>
        public static string ComputeHash(string catalogHash, IReadOnlyList<PlacementPoint> points)
        {
            var sb = new System.Text.StringBuilder(catalogHash ?? "").Append('|');
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (points != null)
                foreach (var p in points)
                    sb.Append(Mm(p.LocalPosition.x).ToString(inv)).Append(',').Append(Mm(p.LocalPosition.y).ToString(inv)).Append(',').Append(Mm(p.LocalPosition.z).ToString(inv)).Append(':')
                      .Append(p.AllowS ? 1 : 0).Append(p.AllowM ? 1 : 0).Append(':')
                      .Append(Mm(p.MaxSize.x).ToString(inv)).Append('x').Append(Mm(p.MaxSize.y).ToString(inv)).Append('x').Append(Mm(p.MaxSize.z).ToString(inv)).Append(':')
                      .Append(Mathf.RoundToInt(p.YawDegrees * 10f).ToString(inv)).Append(':').Append(p.Band).Append(';');
            byte[] bytes;
            using (var sha = System.Security.Cryptography.SHA256.Create()) bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
            var o = new System.Text.StringBuilder();
            for (int i = 0; i < 4; i++) o.Append(bytes[i].ToString("x2", inv));
            return o.ToString();
        }
    }
}
