using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    [Serializable]
    public class ThTreasureEntry
    {
        [Tooltip("お宝の ID（カタログの中で一意。ハッシュに入る）")] public string id = "";
        public TreasureKind kind = TreasureKind.S;
        public string displayName = "";
        [Tooltip("モデル（MeshRenderer）を持つプレハブ（Treasure コンポーネント付き。つかむ範囲は実行時に自動で作る）")] public GameObject prefab;
        [Tooltip("今回の展示で使うか")] public bool use = true;
        [Tooltip("値段の帯の下限（G。100 刻み）")] public int valueMin = 100;
        [Tooltip("値段の帯の上限（G。100 刻み）")] public int valueMax = 100;
        [Tooltip("大きさ（幅 m。正面を向けたときの X。プレハブのモデルの外形から入れ直す）")] public float width;
        [Tooltip("大きさ（奥行 m。Z）")] public float depth;
        [Tooltip("大きさ（高さ m。Y）")] public float height;

        public Vector3 Size => new Vector3(width, depth, height);
    }

    /// <summary>
    /// お宝の見た目の一覧（設計書 4.1・9.1）。1 件＝種類（S/M、追加要素の L*・秘宝*）、表示名、プレハブ、今回の展示で使うか、値段の帯（valueMin〜valueMax）、大きさ（幅・奥行・高さ）。
    /// 練習用お宝のプレハブも持つ。ビルドに入れる（運営PCのファイルで変えるパラメータとは分ける）。
    /// 「使う」お宝の並び（種類・ID・値段の帯・大きさ）のハッシュを、端末とホストで照合する。
    /// </summary>
    [CreateAssetMenu(fileName = "ThTreasureCatalog", menuName = "Treasure Haul/Treasure Catalog")]
    public sealed class ThTreasureCatalog : ScriptableObject
    {
        public List<ThTreasureEntry> entries = new List<ThTreasureEntry>();
        [Tooltip("練習用お宝のプレハブ（1 つ。#43 で使う）")] public GameObject practicePrefab;

        /// <summary>問題の一覧（空なら問題なし）</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            var ids = new HashSet<string>();
            var usedCount = new Dictionary<TreasureKind, int>();
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null) { errors.Add($"空の要素があります（{i} 番目）"); continue; }
                if (string.IsNullOrWhiteSpace(e.id)) errors.Add($"ID が空です（{i} 番目）");
                else if (!ids.Add(e.id)) errors.Add($"ID が重複しています: {e.id}");
                if (!Enum.IsDefined(typeof(TreasureKind), e.kind)) errors.Add($"種類が不正です: {e.id}");
                if (!e.use) continue;
                usedCount.TryGetValue(e.kind, out int c);
                usedCount[e.kind] = c + 1;
                if (e.valueMin <= 0 || e.valueMax < e.valueMin || e.valueMin % TreasureSpawner.ValueStep != 0 || e.valueMax % TreasureSpawner.ValueStep != 0)
                    errors.Add($"値段の帯が不正です（正の 100 刻みで、下限 <= 上限）: {e.id} {e.valueMin}〜{e.valueMax}");
                if (e.width <= 0f || e.depth <= 0f || e.height <= 0f) errors.Add($"大きさが未設定です（お宝カタログの大きさを入れ直してください）: {e.id}");
                if (e.prefab == null) { errors.Add($"プレハブが未設定です: {e.id}"); continue; }
                var t = e.prefab.GetComponent<Treasure>();
                if (t == null) { errors.Add($"プレハブに Treasure コンポーネントがありません: {e.id}"); continue; }
                if (e.prefab.GetComponentInChildren<MeshRenderer>(true) == null) errors.Add($"プレハブに MeshRenderer がありません（持てる形ではありません）: {e.id}");
            }
            // 今回の展示で出す種類（S・M）は 1 件以上「使う」が必要。L・秘宝は追加要素なので 0 件でよい
            foreach (var k in new[] { TreasureKind.S, TreasureKind.M })
                if (!usedCount.ContainsKey(k)) errors.Add($"種類 {k} の「使う」お宝が 0 件です");
            if (practicePrefab == null) errors.Add("練習用お宝のプレハブが未設定です");
            else if (practicePrefab.GetComponent<Treasure>() == null) errors.Add("練習用お宝のプレハブに Treasure コンポーネントがありません");
            return errors;
        }

        /// <summary>「使う」お宝の並び（種類・ID・値段の帯・大きさ（mm に丸める））の SHA-256 先頭 8 桁（16進小文字）</summary>
        public string ComputeHash()
        {
            var list = new List<ThTreasureEntry>();
            foreach (var e in entries) if (e != null && e.use) list.Add(e);
            return ComputeHash(list);
        }

        public static string ComputeHash(IEnumerable<ThTreasureEntry> usedEntries)
        {
            var sb = new StringBuilder();
            foreach (var e in usedEntries)
                sb.Append((int)e.kind).Append(':').Append(e.id).Append(':').Append(e.valueMin).Append('-').Append(e.valueMax).Append(':')
                  .Append(Mm(e.width)).Append('x').Append(Mm(e.depth)).Append('x').Append(Mm(e.height)).Append(';');
            byte[] bytes;
            using (var sha = SHA256.Create()) bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            var o = new StringBuilder();
            for (int i = 0; i < 4; i++) o.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return o.ToString();
        }

        static int Mm(float meters) => Mathf.RoundToInt(meters * 1000f);

        /// <summary>配置に渡す「使う」お宝の一覧（EntryIndex は entries の添字）</summary>
        public List<SpawnCatalogEntry> ToSpawnEntries()
        {
            var list = new List<SpawnCatalogEntry>();
            for (int i = 0; i < entries.Count; i++)
                if (entries[i] != null && entries[i].use) list.Add(new SpawnCatalogEntry(entries[i].kind, i, entries[i].id, entries[i].valueMin, entries[i].valueMax, entries[i].Size));
            return list;
        }
    }
}
