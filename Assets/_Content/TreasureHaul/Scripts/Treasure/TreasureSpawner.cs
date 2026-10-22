using System;
using System.Collections.Generic;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// お宝の配置と値段を決める純粋クラス（設計書 4.1、F-ITEM-1/2）。同じ入力から必ず同じ表を作る。
    /// 乱数は System.Random(seed) 1 つだけ。辞書や HashSet の列挙順には頼らない（配列・List の添字順だけを使う）。
    /// <b>乱数を呼ぶ順番（変えると全端末の配置が変わる）</b>:
    ///   1. 置き場所のシャッフル（Fisher-Yates。全点を対象に Next(i + 1) を i = n-1 → 1 で呼ぶ）。以降の「シャッフル順」はこの並び
    ///   2. 置き場所の選択（乱数を使わない）: シャッフル順に見て、まず M を出現数だけ（「M を置ける」置き場所で、M のお宝が 1 つでも入るもの）、
    ///      次に S を出現数だけ（「S を置ける」置き場所で、S のお宝が 1 つでも入るもの。M に選ばれた点は除く）。
    ///      足りなければ置ける数だけにして Warnings に書く。お宝の番号（Index）は、選ばれた置き場所のシャッフル順の並び（0 始まり）
    ///   3. 各お宝（番号順）のお宝の選択: 置き場所に入る大きさ（幅・奥行・高さ）の、その種類のお宝を候補にし、
    ///      すでに置いた同じお宝から水平距離 <see cref="SameTreasureMinDistance"/> 未満の置き場所を除く（候補が空になるときだけこの決まりを外し、Warnings に書く）。
    ///      候補を「値段の帯の中央値の安い順（同じなら ID の序数順）」に並べ、距離帯 0: Next(候補数) を 2 回引いて小さい方 / 1: 1 回 / 2: 2 回引いて大きい方、を添字とする
    ///   4. 各お宝（番号順）の値段: そのお宝の帯の中で <see cref="ValueStep"/> 刻み。段数 = (最大 - 最小) / 100 + 1 として、3. と同じ引き方（Next(段数)）
    ///   向き（YawDegrees）は置き場所の正面の向き（乱数は使わない）。
    /// </summary>
    public static class TreasureSpawner
    {
        public const int ValueStep = 100;
        /// <summary>同じお宝（同じ ID）を並べない水平距離（m）。これ未満の置き場所には置かない</summary>
        public const float SameTreasureMinDistance = 1.5f;

        public static SpawnResult Spawn(int seed, int playerCount, ThParams p, IReadOnlyList<PlacementPoint> points, IReadOnlyList<SpawnCatalogEntry> catalog)
        {
            var result = new SpawnResult();
            int rowIndex = Mathf.Clamp(playerCount, 1, p.layout.rows.Length) - 1;
            var row = p.layout.rows[rowIndex];
            var rng = new System.Random(seed);

            // 1. 置き場所のシャッフル
            int n = points.Count;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            for (int i = n - 1; i >= 1; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            // 2. 置き場所の選択（M が先、次に S。番号はシャッフル順）
            var taken = new bool[n];          // order の添字ごとに「使う」
            var kindAt = new TreasureKind[n];
            int placedM = PickSlots(TreasureKind.M, row.spawnM, order, points, catalog, taken, kindAt);
            int placedS = PickSlots(TreasureKind.S, row.spawnS, order, points, catalog, taken, kindAt);
            if (placedM < row.spawnM) result.Warnings.Add($"M を置ける置き場所が足りません（必要 {row.spawnM} / 置けた {placedM}）");
            if (placedS < row.spawnS) result.Warnings.Add($"S を置ける置き場所が足りません（必要 {row.spawnS} / 置けた {placedS}）");
            for (int k = 0; k < n; k++)
            {
                if (!taken[k]) continue;
                int pi = order[k];
                result.Items.Add(new SpawnedTreasure
                {
                    Index = result.Items.Count,
                    Kind = kindAt[k],
                    PointIndex = pi,
                    LocalPosition = points[pi].LocalPosition,
                    YawDegrees = points[pi].YawDegrees,
                    CatalogIndex = -1,
                });
            }

            // 3. お宝の選択
            long minMm = Mathf.RoundToInt(SameTreasureMinDistance * 1000f);
            long minSqMm = minMm * minMm;
            var chosen = new SpawnCatalogEntry[result.Items.Count];
            for (int i = 0; i < result.Items.Count; i++)
            {
                var t = result.Items[i];
                var pt = points[t.PointIndex];
                var fits = FittingEntries(t.Kind, pt, catalog);
                var apart = new List<SpawnCatalogEntry>(fits.Count);
                foreach (var e in fits)
                {
                    bool near = false;
                    for (int j = 0; j < i; j++)
                    {
                        if (result.Items[j].CatalogIndex != e.EntryIndex) continue;
                        if (HorizontalDistanceSqMm(points[result.Items[j].PointIndex].LocalPosition, pt.LocalPosition) < minSqMm) { near = true; break; }
                    }
                    if (!near) apart.Add(e);
                }
                if (apart.Count == 0)
                {
                    apart = fits;
                    result.Warnings.Add($"同じお宝を {SameTreasureMinDistance}m 以内に置かない決まりを外しました（番号 {i} / 置き場所 {t.PointIndex}）");
                }
                apart.Sort(CompareByPrice);
                var picked = apart[PickBiased(rng, apart.Count, pt.Band)];
                chosen[i] = picked;
                t.CatalogIndex = picked.EntryIndex;
            }

            // 4. 値段
            for (int i = 0; i < result.Items.Count; i++)
            {
                var e = chosen[i];
                int steps = Math.Max(1, (e.ValueMax - e.ValueMin) / ValueStep + 1);
                result.Items[i].Value = e.ValueMin + PickBiased(rng, steps, points[result.Items[i].PointIndex].Band) * ValueStep;
            }
            return result;
        }

        /// <summary>シャッフル順に見て、kind を置ける（フラグと大きさ）点を最大 want 個選ぶ。選べた数を返す</summary>
        static int PickSlots(TreasureKind kind, int want, int[] order, IReadOnlyList<PlacementPoint> points, IReadOnlyList<SpawnCatalogEntry> catalog, bool[] taken, TreasureKind[] kindAt)
        {
            int got = 0;
            for (int k = 0; k < order.Length && got < want; k++)
            {
                if (taken[k]) continue;
                var pt = points[order[k]];
                if (!(kind == TreasureKind.M ? pt.AllowM : pt.AllowS)) continue;
                if (FittingEntries(kind, pt, catalog).Count == 0) continue;
                taken[k] = true;
                kindAt[k] = kind;
                got++;
            }
            return got;
        }

        /// <summary>その種類で、置き場所の入る大きさ以下のお宝（カタログの並び順）</summary>
        static List<SpawnCatalogEntry> FittingEntries(TreasureKind kind, PlacementPoint pt, IReadOnlyList<SpawnCatalogEntry> catalog)
        {
            var list = new List<SpawnCatalogEntry>();
            for (int i = 0; i < catalog.Count; i++)
            {
                var e = catalog[i];
                if (e.Kind != kind) continue;
                if (e.Size.x > pt.MaxSize.x || e.Size.y > pt.MaxSize.y || e.Size.z > pt.MaxSize.z) continue;
                list.Add(e);
            }
            return list;
        }

        static int CompareByPrice(SpawnCatalogEntry a, SpawnCatalogEntry b)
        {
            int c = (a.ValueMin + a.ValueMax).CompareTo(b.ValueMin + b.ValueMax);
            return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
        }

        /// <summary>距離帯で偏らせた添字（0〜count-1）。帯 0: 2 回引いて小さい方 / 1: 1 回 / 2: 2 回引いて大きい方</summary>
        static int PickBiased(System.Random rng, int count, int band)
        {
            if (band <= 0) return Math.Min(rng.Next(count), rng.Next(count));
            if (band == 1) return rng.Next(count);
            return Math.Max(rng.Next(count), rng.Next(count));
        }

        /// <summary>水平距離（mm）の 2 乗。位置を mm の整数に丸めてから比べる（端末の浮動小数のずれで判定が分かれないように）</summary>
        static long HorizontalDistanceSqMm(Vector3 a, Vector3 b)
        {
            long dx = Mathf.RoundToInt(a.x * 1000f) - Mathf.RoundToInt(b.x * 1000f);
            long dz = Mathf.RoundToInt(a.z * 1000f) - Mathf.RoundToInt(b.z * 1000f);
            return dx * dx + dz * dz;
        }
    }
}
