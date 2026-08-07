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
        public void Build_AppendingZeroCapacityEntry_DoesNotAffectExistingEntriesSchedule()
        {
            // 20260807-card-rank-config-consolidation.md: ItemCatalog.entries の末尾に
            // InitialCount=0/MaxCount=0 のエントリ（SSランクの ss_beach 等、通常スポーンの対象外）を
            // 追加しても、rng を一切消費しないため既存エントリのスケジュール（EntryIndex/Position/Time等）は
            // 完全に不変であること（決定論的スポーンの回帰防止）。
            var withoutExtra = CreateEntries();
            var withExtra = CreateEntries();
            withExtra.Add(new SpawnEntrySpec(initialCount: 0, maxCount: 0, rank: CardRank.SS));

            var anchors = CreateAnchors(4);
            var scheduleWithout = CreateBuilder().Build(Seed, withoutExtra, anchors);
            var scheduleWith = CreateBuilder().Build(Seed, withExtra, CreateAnchors(4));

            // 追加エントリ自体は一切スケジュールされない
            Assert.That(scheduleWith.Any(s => s.EntryIndex == withExtra.Count - 1), Is.False);

            // 既存エントリ分の件数・内容が完全一致する
            Assert.That(scheduleWith.Count, Is.EqualTo(scheduleWithout.Count));
            for (var i = 0; i < scheduleWithout.Count; i++)
            {
                Assert.That(scheduleWith[i].NetworkId, Is.EqualTo(scheduleWithout[i].NetworkId));
                Assert.That(scheduleWith[i].EntryIndex, Is.EqualTo(scheduleWithout[i].EntryIndex));
                Assert.That(scheduleWith[i].Time, Is.EqualTo(scheduleWithout[i].Time));
                Assert.That(scheduleWith[i].UsedAnchor, Is.EqualTo(scheduleWithout[i].UsedAnchor));
                Assert.That(scheduleWith[i].Position, Is.EqualTo(scheduleWithout[i].Position));
                Assert.That(scheduleWith[i].YawDegrees, Is.EqualTo(scheduleWithout[i].YawDegrees));
            }
        }

        [Test]
        public void Build_AppendingZeroCapacityEntry_DoesNotAffectExistingEntriesSchedule_WithBonusWeights()
        {
            // ItemSpawnManager が実際に使う重み付きオーバーロード（ボーナスタイム対応）でも、
            // 0/0容量エントリの追加が既存エントリのスケジュールに影響しないこと。
            var withoutExtra = CreateRankedEntries();
            var withExtra = CreateRankedEntries();
            withExtra.Add(new SpawnEntrySpec(initialCount: 0, maxCount: 0, rank: CardRank.SS));

            var scheduleWithout = CreateBuilder().Build(Seed, withoutExtra, null, 10f, 3f, UniformRankWeights, HighRankSkewedWeights);
            var scheduleWith = CreateBuilder().Build(Seed, withExtra, null, 10f, 3f, UniformRankWeights, HighRankSkewedWeights);

            Assert.That(scheduleWith.Any(s => s.EntryIndex == withExtra.Count - 1), Is.False);
            Assert.That(scheduleWith.Count, Is.EqualTo(scheduleWithout.Count));
            for (var i = 0; i < scheduleWithout.Count; i++)
            {
                Assert.That(scheduleWith[i].NetworkId, Is.EqualTo(scheduleWithout[i].NetworkId));
                Assert.That(scheduleWith[i].EntryIndex, Is.EqualTo(scheduleWithout[i].EntryIndex));
                Assert.That(scheduleWith[i].Time, Is.EqualTo(scheduleWithout[i].Time));
                Assert.That(scheduleWith[i].Position, Is.EqualTo(scheduleWithout[i].Position));
                Assert.That(scheduleWith[i].YawDegrees, Is.EqualTo(scheduleWithout[i].YawDegrees));
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

        // ---- 専用アンカー（AllowedEntryIndices）と整列回転（AlignRotation）のテスト ----

        private static SpawnAnchorSpec CreateDedicatedAnchor(Vector3 position, int[] allowedEntries, Quaternion rotation, bool alignRotation)
        {
            return new SpawnAnchorSpec(position, isElevated: false, allowedEntries, rotation, alignRotation);
        }

        [Test]
        public void Build_AllGenericAnchors_MatchesPreviousAnchorAlgorithm()
        {
            // 汎用アンカーのみのシーンでは、専用アンカー導入前のアンカー選択実装と
            // ビット単位で同一の結果になること（後方互換）。
            var entries = CreateEntries();
            var anchors = CreateAnchors(4);

            var expected = LegacyGenericAnchorBuild(Seed, entries, anchors, BasePosition, SpawnAreaSize, MinHeight, MaxHeight, MinSpacing, TrickleInterval);
            var actual = CreateBuilder().Build(Seed, entries, anchors);

            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            for (var i = 0; i < actual.Count; i++)
            {
                Assert.That(actual[i].NetworkId, Is.EqualTo(expected[i].NetworkId));
                Assert.That(actual[i].EntryIndex, Is.EqualTo(expected[i].EntryIndex));
                Assert.That(actual[i].Time, Is.EqualTo(expected[i].Time));
                Assert.That(actual[i].Position, Is.EqualTo(expected[i].Position));
                Assert.That(actual[i].YawDegrees, Is.EqualTo(expected[i].YawDegrees));
                Assert.That(actual[i].UsedAnchor, Is.EqualTo(expected[i].UsedAnchor));
                Assert.That(actual[i].Rotation, Is.EqualTo(Quaternion.Euler(0f, actual[i].YawDegrees, 0f)));
            }
        }

        [Test]
        public void Build_DedicatedAnchor_OnlyAssignedToSpecifiedEntry()
        {
            // エントリ1専用アンカー1点のみ（汎用なし）。エントリ0のスポーンには使われないこと。
            var entries = new List<SpawnEntrySpec>
            {
                new(initialCount: 3, maxCount: 3),
                new(initialCount: 1, maxCount: 1),
            };
            var dedicatedPos = new Vector3(9f, 1f, -9f);
            var anchors = new List<SpawnAnchorSpec>
            {
                CreateDedicatedAnchor(dedicatedPos, new[] { 1 }, Quaternion.identity, alignRotation: false),
            };

            var schedule = CreateBuilder().Build(Seed, entries, anchors);

            foreach (var spawn in schedule)
            {
                if (spawn.EntryIndex == 0)
                {
                    Assert.That(spawn.UsedAnchor, Is.False, "専用アンカーは指定外エントリに消費されないこと");
                    Assert.That(spawn.Position, Is.Not.EqualTo(dedicatedPos));
                }
            }
            var entry1Spawn = schedule.Single(s => s.EntryIndex == 1);
            Assert.That(entry1Spawn.UsedAnchor, Is.True);
            Assert.That(entry1Spawn.Position, Is.EqualTo(dedicatedPos));
        }

        [Test]
        public void Build_DedicatedAnchors_PreferredOverGeneric()
        {
            // 専用アンカーがある限り、指定エントリは汎用より専用を優先して使うこと。
            var entries = new List<SpawnEntrySpec> { new(initialCount: 2, maxCount: 2) };
            var dedicatedA = new Vector3(10f, 1f, 0f);
            var dedicatedB = new Vector3(11f, 1f, 0f);
            var anchors = new List<SpawnAnchorSpec>
            {
                new(new Vector3(0f, 1f, 0f), isElevated: false), // 汎用
                CreateDedicatedAnchor(dedicatedA, new[] { 0 }, Quaternion.identity, alignRotation: false),
                new(new Vector3(1f, 1f, -1f), isElevated: false), // 汎用
                CreateDedicatedAnchor(dedicatedB, new[] { 0 }, Quaternion.identity, alignRotation: false),
            };

            var schedule = CreateBuilder().Build(Seed, entries, anchors);

            Assert.That(schedule.Count, Is.EqualTo(2));
            var usedPositions = schedule.Select(s => s.Position).ToList();
            CollectionAssert.AreEquivalent(new[] { dedicatedA, dedicatedB }, usedPositions,
                "汎用アンカーが残っていても専用アンカーが優先されること");
        }

        [Test]
        public void Build_DedicatedExhausted_FallsBackToGenericThenBox()
        {
            // 専用1点＋汎用1点でエントリ0を3体出すと、専用→汎用→箱型の順に遷移すること。
            var entries = new List<SpawnEntrySpec> { new(initialCount: 3, maxCount: 3) };
            var dedicatedPos = new Vector3(10f, 1f, 0f);
            var genericPos = new Vector3(0f, 1f, -2f);
            var anchors = new List<SpawnAnchorSpec>
            {
                new(genericPos, isElevated: false),
                CreateDedicatedAnchor(dedicatedPos, new[] { 0 }, Quaternion.identity, alignRotation: false),
            };

            var schedule = CreateBuilder().Build(Seed, entries, anchors);

            Assert.That(schedule.Count, Is.EqualTo(3));
            Assert.That(schedule[0].Position, Is.EqualTo(dedicatedPos), "1体目は専用アンカー");
            Assert.That(schedule[1].Position, Is.EqualTo(genericPos), "2体目は汎用アンカー");
            Assert.That(schedule[2].UsedAnchor, Is.False, "3体目は箱型フォールバック");
        }

        [Test]
        public void Build_AlignRotationAnchor_UsesAnchorRotation()
        {
            var entries = new List<SpawnEntrySpec> { new(initialCount: 2, maxCount: 2) };
            var anchorRotation = Quaternion.Euler(10f, 200f, 30f); // 木の幹への張り付きを模した回転
            var alignedPos = new Vector3(10f, 1f, 0f);
            var anchors = new List<SpawnAnchorSpec>
            {
                CreateDedicatedAnchor(alignedPos, new[] { 0 }, anchorRotation, alignRotation: true),
                new(new Vector3(0f, 1f, -2f), isElevated: false),
            };

            var schedule = CreateBuilder().Build(Seed, entries, anchors);

            var aligned = schedule.Single(s => s.Position == alignedPos);
            var randomYaw = schedule.Single(s => s.Position != alignedPos);
            Assert.That(aligned.Rotation, Is.EqualTo(anchorRotation), "整列アンカーはアンカーの回転を適用すること");
            Assert.That(randomYaw.Rotation, Is.EqualTo(Quaternion.Euler(0f, randomYaw.YawDegrees, 0f)),
                "非整列スポーンは従来どおりランダム yaw であること");
        }

        [Test]
        public void Build_AlignRotation_DoesNotAffectRandomStream()
        {
            // 整列フラグの有無で rng の消費順が変わらないこと
            // （変わると整列アンカー導入で後続すべてのスポーン位置がズレ、既存配置が激変してしまう）。
            var entries = CreateEntries();
            var rotation = Quaternion.Euler(0f, 90f, 45f);
            List<SpawnAnchorSpec> CreateMixedAnchors(bool align)
            {
                return new List<SpawnAnchorSpec>
                {
                    CreateDedicatedAnchor(new Vector3(10f, 1f, 0f), new[] { 0 }, rotation, align),
                    new(new Vector3(0f, 1f, -1f), isElevated: false),
                    new(new Vector3(1f, 1f, -2f), isElevated: false),
                };
            }

            var withAlign = CreateBuilder().Build(Seed, entries, CreateMixedAnchors(align: true));
            var withoutAlign = CreateBuilder().Build(Seed, entries, CreateMixedAnchors(align: false));

            Assert.That(withAlign.Count, Is.EqualTo(withoutAlign.Count));
            for (var i = 0; i < withAlign.Count; i++)
            {
                Assert.That(withAlign[i].Position, Is.EqualTo(withoutAlign[i].Position));
                Assert.That(withAlign[i].YawDegrees, Is.EqualTo(withoutAlign[i].YawDegrees));
            }
        }

        [Test]
        public void Build_WithDedicatedAnchors_SameSeedProducesIdenticalSchedule()
        {
            var entries = CreateEntries();
            List<SpawnAnchorSpec> CreateMixed()
            {
                var anchors = CreateAnchors(3);
                anchors.Add(CreateDedicatedAnchor(new Vector3(10f, 1f, 0f), new[] { 1 }, Quaternion.Euler(0f, 45f, 0f), alignRotation: true));
                return anchors;
            }

            var scheduleA = CreateBuilder().Build(Seed, entries, CreateMixed());
            var scheduleB = CreateBuilder().Build(Seed, entries, CreateMixed());

            Assert.That(scheduleA.Count, Is.EqualTo(scheduleB.Count));
            for (var i = 0; i < scheduleA.Count; i++)
            {
                Assert.That(scheduleA[i].Position, Is.EqualTo(scheduleB[i].Position));
                Assert.That(scheduleA[i].Rotation, Is.EqualTo(scheduleB[i].Rotation));
                Assert.That(scheduleA[i].UsedAnchor, Is.EqualTo(scheduleB[i].UsedAnchor));
            }
        }

        // ---- ResolveAllowedEntryIndices（cardId → エントリインデックス解決）のテスト ----

        private static readonly Dictionary<string, int> CardIdMap = new()
        {
            { "d_flower", 0 },
            { "c_stag_beetle", 1 },
            { "a_risky_dice", 2 },
        };

        [Test]
        public void ResolveAllowedEntryIndices_NullOrEmpty_ReturnsNull()
        {
            Assert.That(SpawnScheduleBuilder.ResolveAllowedEntryIndices(null, CardIdMap), Is.Null);
            Assert.That(SpawnScheduleBuilder.ResolveAllowedEntryIndices(new List<string>(), CardIdMap), Is.Null);
        }

        [Test]
        public void ResolveAllowedEntryIndices_ResolvesKnownIdsInOrder()
        {
            var result = SpawnScheduleBuilder.ResolveAllowedEntryIndices(
                new List<string> { "a_risky_dice", "d_flower" }, CardIdMap);

            CollectionAssert.AreEqual(new[] { 2, 0 }, result);
        }

        [Test]
        public void ResolveAllowedEntryIndices_UnknownIdsAreIgnoredAndCollected()
        {
            var unknown = new List<string>();
            var result = SpawnScheduleBuilder.ResolveAllowedEntryIndices(
                new List<string> { "c_stag_beetle", "typo_id" }, CardIdMap, unknown);

            CollectionAssert.AreEqual(new[] { 1 }, result);
            CollectionAssert.AreEqual(new[] { "typo_id" }, unknown);
        }

        [Test]
        public void ResolveAllowedEntryIndices_AllUnknown_ReturnsEmptyNotNull()
        {
            // 全件未知（typo）の専用アンカーは「何も出ないアンカー」になる（汎用に落とすと誤配置になるため）
            var result = SpawnScheduleBuilder.ResolveAllowedEntryIndices(
                new List<string> { "typo_a", "typo_b" }, CardIdMap);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ResolveAllowedEntryIndices_DuplicateIdsAreDeduplicated()
        {
            var result = SpawnScheduleBuilder.ResolveAllowedEntryIndices(
                new List<string> { "d_flower", "d_flower" }, CardIdMap);

            CollectionAssert.AreEqual(new[] { 0 }, result);
        }

        // ---- ボーナスタイム（終盤の出現率・レア度アップ）のテスト ----

        /// <summary>D/C/B/A/S 各ランク1種ずつ・追加出現20件ずつを模したカタログ（重み付き抽選の検証用）。</summary>
        private static List<SpawnEntrySpec> CreateRankedEntries(int maxCountPerRank = 20)
        {
            return new List<SpawnEntrySpec>
            {
                new(initialCount: 0, maxCount: maxCountPerRank, rank: CardRank.D),
                new(initialCount: 0, maxCount: maxCountPerRank, rank: CardRank.C),
                new(initialCount: 0, maxCount: maxCountPerRank, rank: CardRank.B),
                new(initialCount: 0, maxCount: maxCountPerRank, rank: CardRank.A),
                new(initialCount: 0, maxCount: maxCountPerRank, rank: CardRank.S),
            };
        }

        private static readonly Dictionary<CardRank, float> UniformRankWeights = new()
        {
            { CardRank.D, 1f }, { CardRank.C, 1f }, { CardRank.B, 1f }, { CardRank.A, 1f }, { CardRank.S, 1f }, { CardRank.SS, 1f },
        };

        private static readonly Dictionary<CardRank, float> HighRankSkewedWeights = new()
        {
            { CardRank.D, 1.0f }, { CardRank.C, 1.4f }, { CardRank.B, 2.2f }, { CardRank.A, 3.2f }, { CardRank.S, 4.5f }, { CardRank.SS, 4.5f },
        };

        [Test]
        public void Build_WithBonusParameters_SameSeed_ProducesIdenticalSchedule()
        {
            var entries = CreateRankedEntries();

            var scheduleA = CreateBuilder().Build(Seed, entries, CreateAnchors(4), 10f, 3f, UniformRankWeights, HighRankSkewedWeights);
            var scheduleB = CreateBuilder().Build(Seed, entries, CreateAnchors(4), 10f, 3f, UniformRankWeights, HighRankSkewedWeights);

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
        public void Build_BonusWindow_SwitchesToBonusTrickleIntervalAfterThreshold()
        {
            // 単一エントリのみ（重み付き抽選は常にそのエントリを選ぶため、時刻の挙動だけを純粋に検証できる）。
            var entries = new List<SpawnEntrySpec> { new(initialCount: 0, maxCount: 6, rank: CardRank.D) };
            const float bonusWindowStart = 20f;
            const float bonusInterval = 4f;

            var schedule = CreateBuilder().Build(Seed, entries, null, bonusWindowStart, bonusInterval, UniformRankWeights, UniformRankWeights);

            var times = schedule.Select(s => s.Time).ToList();
            Assert.That(times.Count, Is.EqualTo(6));
            // 通常間隔（TrickleInterval=12）で 12, 24 まで進み、24 の時点でボーナスタイム開始(20)を跨いでいるため、
            // 以降は bonusInterval(4) 刻みになる。
            Assert.That(times[0], Is.EqualTo(TrickleInterval));
            Assert.That(times[1], Is.EqualTo(TrickleInterval * 2));
            Assert.That(times[1], Is.GreaterThanOrEqualTo(bonusWindowStart));
            for (var i = 2; i < times.Count; i++)
            {
                Assert.That(times[i] - times[i - 1], Is.EqualTo(bonusInterval).Within(0.0001f));
            }
        }

        [Test]
        public void Build_BonusWeightsSkewedToHighRank_IncreasesAverageRankInBonusWindow()
        {
            // Build() は while (pool.Count > 0) でプールを完全に空にするまで出現させ切るため、
            // 重みは「出現する順番」だけを左右し、「最終的に出現する総数」（＝各ランクの出現数）は
            // 変えない。そのため schedule 全体（このカタログでは合計100件）の平均ランクを比較すると、
            // 重みに関わらず必ず D,C,B,A,S 各20件ずつを含む同一の多重集合になり、平均ランクは常に
            // ちょうど2.0で完全一致してしまう（差が出ない＝このテストが検証したい効果を測れない）。
            // 「重みが高ランクを出やすくする」効果は時系列の先頭（早い時刻）に集中して現れるため、
            // 時刻昇順の先頭 prefixSize 件（全体100件よりも十分小さい範囲）だけを比較する。
            var entries = CreateRankedEntries();
            const float bonusWindowStart = 0f; // 最初のトリクルからボーナス扱いにして差を最大化する
            const int prefixSize = 15; // 合計100件に対し、重みの影響がまだ強く残っている先頭範囲

            var uniformSchedule = CreateBuilder().Build(Seed, entries, null, bonusWindowStart, TrickleInterval, UniformRankWeights, UniformRankWeights);
            var skewedSchedule = CreateBuilder().Build(Seed, entries, null, bonusWindowStart, TrickleInterval, UniformRankWeights, HighRankSkewedWeights);

            Assert.That(uniformSchedule.Count, Is.EqualTo(skewedSchedule.Count));

            double PrefixAverageRank(List<ScheduledSpawn> schedule) =>
                schedule.Take(prefixSize).Average(s => (int)entries[s.EntryIndex].Rank);

            var uniformAverage = PrefixAverageRank(uniformSchedule);
            var skewedAverage = PrefixAverageRank(skewedSchedule);

            Assert.That(skewedAverage, Is.GreaterThan(uniformAverage),
                "ボーナス重みを高ランクに寄せた場合、時系列の先頭付近（ボーナス時間帯の出現）の平均ランクが上がること");
        }

        [Test]
        public void Build_WithBonusWeights_NeverExceedsMaxCountPerEntry()
        {
            var entries = CreateRankedEntries(maxCountPerRank: 7);
            const float bonusWindowStart = 15f;
            const float bonusInterval = 3f;

            var schedule = CreateBuilder().Build(Seed, entries, CreateAnchors(3), bonusWindowStart, bonusInterval, UniformRankWeights, HighRankSkewedWeights);

            var countsByEntry = new int[entries.Count];
            foreach (var spawn in schedule)
            {
                countsByEntry[spawn.EntryIndex]++;
            }
            for (var i = 0; i < entries.Count; i++)
            {
                Assert.That(countsByEntry[i], Is.LessThanOrEqualTo(entries[i].MaxCount),
                    $"エントリ{i}（{entries[i].Rank}）が maxCount を超えて出現していないこと");
            }
        }

        [Test]
        public void Build_NewOverloadWithBonusDisabled_MatchesLegacyBoxOnlyAlgorithm()
        {
            // normalWeights/bonusWeights を両方 null にした新オーバーロード呼び出しは、
            // 既存の Fisher-Yates コードパス（LegacyBoxOnlyBuild と同一）を使うこと（後方互換の再確認）。
            var entries = CreateEntries();

            var expected = LegacyBoxOnlyBuild(Seed, entries, BasePosition, SpawnAreaSize, MinHeight, MaxHeight, MinSpacing, TrickleInterval);
            var actual = CreateBuilder().Build(Seed, entries, null, float.PositiveInfinity, TrickleInterval, null, null);

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
        public void Build_NewOverloadWithBonusDisabled_MatchesLegacyGenericAnchorAlgorithm()
        {
            // normalWeights/bonusWeights を両方 null にした新オーバーロード呼び出しは、
            // 既存のアンカー選択込み Fisher-Yates コードパス（LegacyGenericAnchorBuild と同一）を使うこと。
            var entries = CreateEntries();
            var anchors = CreateAnchors(4);

            var expected = LegacyGenericAnchorBuild(Seed, entries, anchors, BasePosition, SpawnAreaSize, MinHeight, MaxHeight, MinSpacing, TrickleInterval);
            var actual = CreateBuilder().Build(Seed, entries, anchors, float.PositiveInfinity, TrickleInterval, null, null);

            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            for (var i = 0; i < actual.Count; i++)
            {
                Assert.That(actual[i].NetworkId, Is.EqualTo(expected[i].NetworkId));
                Assert.That(actual[i].EntryIndex, Is.EqualTo(expected[i].EntryIndex));
                Assert.That(actual[i].Time, Is.EqualTo(expected[i].Time));
                Assert.That(actual[i].Position, Is.EqualTo(expected[i].Position));
                Assert.That(actual[i].YawDegrees, Is.EqualTo(expected[i].YawDegrees));
                Assert.That(actual[i].UsedAnchor, Is.EqualTo(expected[i].UsedAnchor));
            }
        }

        [Test]
        public void SpawnEntrySpec_TwoArgConstructor_DefaultsRankToD()
        {
            var spec = new SpawnEntrySpec(initialCount: 1, maxCount: 3);

            Assert.That(spec.Rank, Is.EqualTo(CardRank.D));
        }

        /// <summary>
        /// 専用アンカー導入前の SpawnScheduleBuilder（汎用アンカー選択のみ）のロジックを
        /// 忠実に再現した参照実装（テスト専用）。全アンカー汎用時のビット互換の担保に使う。
        /// </summary>
        private static List<ScheduledSpawn> LegacyGenericAnchorBuild(
            int seed, IReadOnlyList<SpawnEntrySpec> entries, IReadOnlyList<SpawnAnchorSpec> anchors,
            Vector3 basePosition, Vector3 spawnAreaSize,
            float minHeight, float maxHeight, float minSpacing, float trickleInterval)
        {
            var schedule = new List<ScheduledSpawn>();
            var rng = new System.Random(seed);
            var placedPositions = new List<Vector3>();
            var availableAnchors = new List<SpawnAnchorSpec>(anchors);
            var networkId = 0;

            void LegacyAnchorAddSpawn(int entryIndex, float time)
            {
                Vector3 position;
                var usedAnchor = availableAnchors.Count > 0;
                if (usedAnchor)
                {
                    var index = rng.Next(availableAnchors.Count);
                    position = availableAnchors[index].Position;
                    var last = availableAnchors.Count - 1;
                    availableAnchors[index] = availableAnchors[last];
                    availableAnchors.RemoveAt(last);
                }
                else
                {
                    position = LegacyFindSpawnPosition(rng, placedPositions, basePosition, spawnAreaSize, minHeight, maxHeight, minSpacing);
                }
                placedPositions.Add(position);
                var yaw = (float)(rng.NextDouble() * 360.0);
                schedule.Add(new ScheduledSpawn(networkId, entryIndex, position, yaw, time, usedAnchor));
                networkId++;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var count = Mathf.Min(entry.InitialCount, entry.MaxCount);
                for (var n = 0; n < count; n++)
                {
                    LegacyAnchorAddSpawn(i, 0f);
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
                LegacyAnchorAddSpawn(remaining[k], trickleInterval * (k + 1));
            }

            return schedule;
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
