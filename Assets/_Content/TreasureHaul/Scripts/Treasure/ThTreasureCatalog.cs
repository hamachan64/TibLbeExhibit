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
    }

    /// <summary>
    /// お宝の見た目の一覧（設計書 4.1・9.1）。1 件＝種類（S/M、追加要素の L*・秘宝*）、表示名、プレハブ、今回の展示で使うか。
    /// 練習用お宝のプレハブも持つ。ビルドに入れる（運営PCのファイルで変えるパラメータとは分ける）。
    /// 「使う」お宝の並び（種類と ID）のハッシュを、端末とホストで照合する。
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

        /// <summary>「使う」お宝の並び（種類と ID）の SHA-256 先頭 8 桁（16進小文字）</summary>
        public string ComputeHash()
        {
            var list = new List<(TreasureKind, string)>();
            foreach (var e in entries) if (e != null && e.use) list.Add((e.kind, e.id));
            return ComputeHash(list);
        }

        public static string ComputeHash(IEnumerable<(TreasureKind kind, string id)> usedEntries)
        {
            var sb = new StringBuilder();
            foreach (var (kind, id) in usedEntries) sb.Append((int)kind).Append(':').Append(id).Append(';');
            byte[] bytes;
            using (var sha = SHA256.Create()) bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            var o = new StringBuilder();
            for (int i = 0; i < 4; i++) o.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            return o.ToString();
        }

        /// <summary>配置に渡す「使う」お宝の一覧（EntryIndex は entries の添字）</summary>
        public List<SpawnCatalogEntry> ToSpawnEntries()
        {
            var list = new List<SpawnCatalogEntry>();
            for (int i = 0; i < entries.Count; i++)
                if (entries[i] != null && entries[i].use) list.Add(new SpawnCatalogEntry(entries[i].kind, i));
            return list;
        }
    }
}
