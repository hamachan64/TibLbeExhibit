using System;
using System.Collections.Generic;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// お宝の配置と値段を決める純粋クラス（設計書 4.1、F-ITEM-1/2）。同じ入力から必ず同じ表を作る。
    /// 乱数は System.Random(seed) 1 つだけ。<b>乱数を呼ぶ順番（変えると全端末の配置が変わる）</b>:
    ///   1. 配置候補点のシャッフル（Fisher-Yates。全点を対象に Next(i + 1) を i = n-1 → 1 で呼ぶ）
    ///   2. 種類の割り当て（[S × 出現数, M × 出現数] の並びを Fisher-Yates でシャッフルし、シャッフル後の候補点の先頭から順に当てる）
    ///   3. 各お宝（番号順）の値段（距離帯 0: 2 回引いて小さい方 / 1: 1 回 / 2: 2 回引いて大きい方。各回 Next(段数)）
    ///   4. 各お宝（番号順）の Y 回転（NextDouble() × 360）
    ///   5. 各お宝（番号順）のプレハブ（その種類の「使う」お宝が 1 件以上あれば Next(件数)。0 件なら乱数を使わず -1）
    /// </summary>
    public static class TreasureSpawner
    {
        public const int ValueStep = 100;

        public static SpawnResult Spawn(int seed, int playerCount, ThParams p, IReadOnlyList<PlacementPoint> points, IReadOnlyList<SpawnCatalogEntry> catalog)
        {
            var result = new SpawnResult();
            int rowIndex = Mathf.Clamp(playerCount, 1, p.layout.rows.Length) - 1;
            var row = p.layout.rows[rowIndex];
            int wantS = row.spawnS, wantM = row.spawnM;
            var rng = new System.Random(seed);

            // 1. 候補点のシャッフル
            int n = points.Count;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            for (int i = n - 1; i >= 1; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            // 2. 種類の割り当て
            int want = wantS + wantM;
            int count = Math.Min(want, n);
            if (want > n) result.Warnings.Add($"配置候補点が足りません（必要 {want} / 候補点 {n}）。置ける {n} 個だけ置きます");
            var kinds = new List<TreasureKind>(want);
            for (int i = 0; i < wantS; i++) kinds.Add(TreasureKind.S);
            for (int i = 0; i < wantM; i++) kinds.Add(TreasureKind.M);
            for (int i = kinds.Count - 1; i >= 1; i--)
            {
                int j = rng.Next(i + 1);
                (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
            }

            for (int i = 0; i < count; i++)
            {
                var pt = points[order[i]];
                result.Items.Add(new SpawnedTreasure
                {
                    Index = i,
                    Kind = kinds[i],
                    PointIndex = order[i],
                    LocalPosition = pt.LocalPosition,
                });
            }

            // 3. 値段
            foreach (var t in result.Items)
            {
                int min = t.Kind == TreasureKind.M ? p.treasure.mValueMin : p.treasure.sValueMin;
                int max = t.Kind == TreasureKind.M ? p.treasure.mValueMax : p.treasure.sValueMax;
                int steps = Math.Max(1, (max - min) / ValueStep + 1);
                int band = points[t.PointIndex].Band;
                int step;
                if (band <= 0) step = Math.Min(rng.Next(steps), rng.Next(steps));
                else if (band == 1) step = rng.Next(steps);
                else step = Math.Max(rng.Next(steps), rng.Next(steps));
                t.Value = min + step * ValueStep;
            }

            // 4. Y 回転
            foreach (var t in result.Items) t.YawDegrees = (float)(rng.NextDouble() * 360.0);

            // 5. プレハブ
            foreach (var t in result.Items)
            {
                int c = 0;
                for (int i = 0; i < catalog.Count; i++) if (catalog[i].Kind == t.Kind) c++;
                t.CatalogIndex = -1;
                if (c == 0) continue;
                int pick = rng.Next(c);
                for (int i = 0; i < catalog.Count; i++)
                {
                    if (catalog[i].Kind != t.Kind) continue;
                    if (pick-- == 0) { t.CatalogIndex = catalog[i].EntryIndex; break; }
                }
            }
            return result;
        }
    }
}
