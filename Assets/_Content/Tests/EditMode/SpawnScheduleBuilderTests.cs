using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker.Tests
{
    /// <summary>
    /// SpawnScheduleBuilder（ItemSpawnManager のアンカー対応スポーンスケジュール構築ロジック）のEditModeテスト。
    /// 仕様は docs/MR_STAGE_PLAN.md §6.2 および Issue #9 の受け入れ条件に準拠する:
    /// - 同一シードで全クライアントが同一のアンカー割り当てになること
    /// - アンカー枯渇時は箱型ランダム配置へフォールバックし、二重割り当てが起きないこと
    /// - アンカー0件のシーンでは現行（アンカー導入前）の挙動を完全維持すること
    /// </summary>
    public class SpawnScheduleBuilderTests
    {
        private const int Seed = 20260711;
        private static readonly Vector3 BasePosition = Vector3.zero;
        private static readonly Vector3 SpawnAreaSize = new(4f, 0f, 4f);
        private const float MinHeight = 0.6f;
        private const float MaxHeight = 1.5f;
        private const float MinSpacing = 0.5f;
        private const float TrickleInterval = 12f;

        private static SpawnScheduleBuilder CreateBuilder()
        {
            return new SpawnScheduleBuilder(BasePosition, SpawnAreaSize, MinHeight, MaxHeight, MinSpacing, TrickleInterval);
        }

        /// <summary>Dランク×2種＋Bランク×1種を模した最小カタログ（初期出現6件、追加出現19件＝計25件）。</summary>
        private static List<SpawnEntrySpec> CreateEntries()
        {
            return new List<SpawnEntrySpec>
            {
                new(initialCount: 3, maxCount: 10),
                new(initialCount: 2, maxCount: 10),
                new(initialCount: 1, maxCount: 5),
            };
        }

        private static List<SpawnAnchorSpec> CreateAnchors(int count)
        {
            var anchors = new List<SpawnAnchorSpec>(count);
            for (var i = 0; i < count; i++)
            {
                anchors.Add(new SpawnAnchorSpec(new Vector3(i, 1f, -i), isElevated: i % 2 == 0));
            }
            return anchors;
        }

        [Test]
        public void Build_SameSeed_ProducesIdenticalSchedule()
        {
            var entries = CreateEntries();

            var scheduleA = CreateBuilder().Build(Seed, entries, CreateAnchors(4));
            var scheduleB = CreateBuilder().Build(Seed, entries, CreateAnchors(4));

            Assert.That(scheduleA.Count, Is.EqualTo(scheduleB.Count));
            for (var i = 0; i < scheduleA.Count; i++)
            {
                Assert.That(scheduleA[i].NetworkId, Is.EqualTo(scheduleB[i].NetworkId));
                Assert.That(scheduleA[i].EntryIndex, Is.EqualTo(scheduleB[i].EntryIndex));
                Assert.That(scheduleA[i].Time, Is.EqualTo(scheduleB[i].Time));
                Assert.That(scheduleA[i].UsedAnchor, Is.EqualTo(scheduleB[i].UsedAnchor));
                Assert.That(scheduleA[i].Position, Is.EqualTo(scheduleB[i].Position));
                Assert.That(scheduleA[i].YawDegrees, Is.EqualTo(scheduleB[i].YawDegrees));
            }
        }

        [Test]
        public void Build_AnchorCountExceedsSpawnCount_AllSpawnsUseDistinctAnchors()
        {
            var entries = new List<SpawnEntrySpec> { new(initialCount: 2, maxCount: 2) };
            var anchors = CreateAnchors(10);

            var schedule = CreateBuilder().Build(Seed, entries, anchors);

            Assert.That(schedule.Count, Is.EqualTo(2));
            Assert.That(schedule.All(s => s.UsedAnchor), Is.True);

            var usedPositions = schedule.Select(s => s.Position).Distinct().ToList();
            Assert.That(usedPositions.Count, Is.EqualTo(2), "同一アンカーが二重割り当てされていないこと");
            foreach (var pos in usedPositions)
            {
                Assert.That(anchors.Any(a => a.Position == pos), Is.True);
            }
        }

        [Test]
        public void Build_AnchorsExhausted_FallsBackToBoxPlacementDeterministically()
        {
            var entries = CreateEntries(); // 初期出現合計 = 3+2+1 = 6件
            var anchors = CreateAnchors(2); // アンカーは2件のみ → 残りはフォールバック

            var schedule = CreateBuilder().Build(Seed, entries, anchors);

            var anchorSpawns = schedule.Where(s => s.UsedAnchor).ToList();
            var fallbackSpawns = schedule.Where(s => !s.UsedAnchor).ToList();

            Assert.That(anchorSpawns.Count, Is.EqualTo(2), "アンカー数ぶんだけが使用されること");
            Assert.That(fallbackSpawns.Count, Is.EqualTo(schedule.Count - 2));

            // 同一アンカーの二重割り当てがない
            var distinctAnchorPositions = anchorSpawns.Select(s => s.Position).Distinct().ToList();
            Assert.That(distinctAnchorPositions.Count, Is.EqualTo(2));
            foreach (var pos in distinctAnchorPositions)
            {
                Assert.That(anchors.Any(a => a.Position == pos), Is.True);
            }

            // フォールバック位置はアンカー位置と重複しない（箱型ランダム配置に切り替わっている）
            foreach (var spawn in fallbackSpawns)
            {
                Assert.That(anchors.Any(a => a.Position == spawn.Position), Is.False);
            }

            // 枯渇判定自体も決定論的（同一入力で再構築しても同じ枯渇パターンになる）
            var rebuilt = CreateBuilder().Build(Seed, entries, CreateAnchors(2));
            CollectionAssert.AreEqual(
                schedule.Select(s => s.UsedAnchor).ToList(),
                rebuilt.Select(s => s.UsedAnchor).ToList());
        }

        [Test]
        public void Build_NoAnchors_AllSpawnsUseBoxFallback()
        {
            var entries = CreateEntries();

            var schedule = CreateBuilder().Build(Seed, entries, new List<SpawnAnchorSpec>());

            Assert.That(schedule.Count, Is.GreaterThan(0));
            Assert.That(schedule.All(s => !s.UsedAnchor), Is.True);
        }

        [Test]
        public void Build_NullAnchors_TreatedAsNoAnchors()
        {
            var entries = CreateEntries();

            var schedule = CreateBuilder().Build(Seed, entries, null);

            Assert.That(schedule.Count, Is.GreaterThan(0));
            Assert.That(schedule.All(s => !s.UsedAnchor), Is.True);
        }

        [Test]
        public void Build_NoAnchors_MatchesLegacyBoxOnlyAlgorithm()
        {
            // アンカー導入前の ItemSpawnManager.BuildSchedule と同一の rng 消費順で
            // 独立に再実装した参照実装。SpawnScheduleBuilder がアンカー0件の場合に
            // 完全に同じ結果を返すこと（後方互換）を担保する。
            var entries = CreateEntries();

            var expected = LegacyBoxOnlyBuild(Seed, entries, BasePosition, SpawnAreaSize, MinHeight, MaxHeight, MinSpacing, TrickleInterval);
            var actual = CreateBuilder().Build(Seed, entries, null);

            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            for (var i = 0; i < actual.Count; i++)
            {
                Assert.That(actual[i].NetworkId, Is.EqualTo(expected[i].NetworkId));
                Assert.That(actual[i].EntryIndex, Is.EqualTo(expected[i].EntryIndex));
                Assert.That(actual[i].Time, Is.EqualTo(expected[i].Time));
                Assert.That(actual[i].Position, Is.EqualTo(expected[i].Position));
                Assert.That(actual[i].YawDegrees, Is.EqualTo(expected[i].YawDegrees));
                Assert.That(actual[i].UsedAnchor, Is.False);
            }
        }

        [Test]
        public void Build_EmptyEntries_ReturnsEmptySchedule()
        {
            var schedule = CreateBuilder().Build(Seed, new List<SpawnEntrySpec>(), CreateAnchors(3));

            Assert.That(schedule, Is.Empty);
        }

        /// <summary>アンカー導入前の ItemSpawnManager.BuildSchedule のロジックを忠実に再現した参照実装（テスト専用）。</summary>
        private static List<ScheduledSpawn> LegacyBoxOnlyBuild(
            int seed, IReadOnlyList<SpawnEntrySpec> entries, Vector3 basePosition, Vector3 spawnAreaSize,
            float minHeight, float maxHeight, float minSpacing, float trickleInterval)
        {
            var schedule = new List<ScheduledSpawn>();
            var rng = new System.Random(seed);
            var placedPositions = new List<Vector3>();
            var networkId = 0;

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var count = Mathf.Min(entry.InitialCount, entry.MaxCount);
                for (var n = 0; n < count; n++)
                {
                    LegacyAddSpawn(schedule, ref networkId, i, 0f, rng, placedPositions,
                        basePosition, spawnAreaSize, minHeight, maxHeight, minSpacing);
                }
            }

            var remaining = new List<int>();
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var rest = entry.MaxCount - Mathf.Min(entry.InitialCount, entry.MaxCount);
                for (var n = 0; n < rest; n++)
                {
                    remaining.Add(i);
                }
            }
            for (var i = remaining.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (remaining[i], remaining[j]) = (remaining[j], remaining[i]);
            }
            for (var k = 0; k < remaining.Count; k++)
            {
                LegacyAddSpawn(schedule, ref networkId, remaining[k], trickleInterval * (k + 1), rng, placedPositions,
                    basePosition, spawnAreaSize, minHeight, maxHeight, minSpacing);
            }

            return schedule;
        }

        private static void LegacyAddSpawn(
            List<ScheduledSpawn> schedule, ref int networkId, int entryIndex, float time,
            System.Random rng, List<Vector3> placedPositions,
            Vector3 basePosition, Vector3 spawnAreaSize, float minHeight, float maxHeight, float minSpacing)
        {
            var position = LegacyFindSpawnPosition(rng, placedPositions, basePosition, spawnAreaSize, minHeight, maxHeight, minSpacing);
            placedPositions.Add(position);
            var yaw = (float)(rng.NextDouble() * 360.0);
            schedule.Add(new ScheduledSpawn(networkId, entryIndex, position, yaw, time, usedAnchor: false));
            networkId++;
        }

        private static Vector3 LegacyFindSpawnPosition(
            System.Random rng, List<Vector3> placedPositions,
            Vector3 basePosition, Vector3 spawnAreaSize, float minHeight, float maxHeight, float minSpacing)
        {
            Vector3 candidate = basePosition + Vector3.up * ((minHeight + maxHeight) * 0.5f);
            for (var attempt = 0; attempt < 12; attempt++)
            {
                candidate = basePosition + new Vector3(
                    ((float)rng.NextDouble() - 0.5f) * spawnAreaSize.x,
                    minHeight + (float)rng.NextDouble() * (maxHeight - minHeight),
                    ((float)rng.NextDouble() - 0.5f) * spawnAreaSize.z);

                var minSqr = minSpacing * minSpacing;
                var ok = true;
                foreach (var placed in placedPositions)
                {
                    if ((placed - candidate).sqrMagnitude < minSqr)
                    {
                        ok = false;
                        break;
                    }
                }
                if (ok)
                {
                    return candidate;
                }
            }
            return candidate;
        }
    }
}
