using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker.Tests
{
    /// <summary>
    /// CardRankConfig（ランク単位の数値バランス設定）のEditModeテスト。
    /// 20260807-card-rank-config-consolidation.md の受け入れ基準のうち、
    /// ランク別エントリ取得（TryGetEntry）と PokerHandEvaluator 向け BasePoints 辞書構築を検証する。
    /// </summary>
    public class CardRankConfigTests
    {
        private CardRankConfig config;

        [TearDown]
        public void TearDown()
        {
            if (config != null)
            {
                Object.DestroyImmediate(config);
            }
        }

        /// <summary>SerializedObject 経由で entries（private List&lt;Entry&gt;）に1件追加する。</summary>
        private void AddEntry(CardRank rank, int basePoints, float normalWeight, float bonusWeight, float lotteryWeight, Color themeColor)
        {
            var so = new SerializedObject(config);
            var entriesProp = so.FindProperty("entries");
            var index = entriesProp.arraySize;
            entriesProp.InsertArrayElementAtIndex(index);
            var element = entriesProp.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("rank").enumValueIndex = (int)rank;
            element.FindPropertyRelative("basePoints").intValue = basePoints;
            element.FindPropertyRelative("normalSpawnWeight").floatValue = normalWeight;
            element.FindPropertyRelative("bonusSpawnWeight").floatValue = bonusWeight;
            element.FindPropertyRelative("lotteryWeight").floatValue = lotteryWeight;
            element.FindPropertyRelative("themeColor").colorValue = themeColor;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void TryGetEntry_ReturnsTrueAndEntry_WhenRankExists()
        {
            config = ScriptableObject.CreateInstance<CardRankConfig>();
            AddEntry(CardRank.A, basePoints: 5, normalWeight: 0.6f, bonusWeight: 3.2f, lotteryWeight: 12f, Color.magenta);

            var found = config.TryGetEntry(CardRank.A, out var entry);

            Assert.That(found, Is.True);
            Assert.That(entry.rank, Is.EqualTo(CardRank.A));
            Assert.That(entry.basePoints, Is.EqualTo(5));
            Assert.That(entry.normalSpawnWeight, Is.EqualTo(0.6f));
            Assert.That(entry.bonusSpawnWeight, Is.EqualTo(3.2f));
            Assert.That(entry.lotteryWeight, Is.EqualTo(12f));
            Assert.That(entry.themeColor, Is.EqualTo(Color.magenta));
        }

        [Test]
        public void TryGetEntry_ReturnsFalse_WhenRankMissing()
        {
            config = ScriptableObject.CreateInstance<CardRankConfig>();
            AddEntry(CardRank.D, basePoints: 1, normalWeight: 1f, bonusWeight: 1f, lotteryWeight: 40f, Color.white);

            var found = config.TryGetEntry(CardRank.S, out var entry);

            Assert.That(found, Is.False);
            Assert.That(entry, Is.Null);
        }

        [Test]
        public void BasePoints_BuildsDictionaryFromEntries()
        {
            config = ScriptableObject.CreateInstance<CardRankConfig>();
            AddEntry(CardRank.D, basePoints: 1, normalWeight: 1f, bonusWeight: 1f, lotteryWeight: 40f, Color.white);
            AddEntry(CardRank.S, basePoints: 100, normalWeight: 0.4f, bonusWeight: 4.5f, lotteryWeight: 8f, Color.yellow);

            var basePoints = config.BasePoints;

            Assert.That(basePoints.Count, Is.EqualTo(2));
            Assert.That(basePoints[CardRank.D], Is.EqualTo(1));
            Assert.That(basePoints[CardRank.S], Is.EqualTo(100));
        }

        [Test]
        public void BasePoints_ReturnsEmptyDictionary_WhenNoEntries()
        {
            config = ScriptableObject.CreateInstance<CardRankConfig>();

            var basePoints = config.BasePoints;

            Assert.That(basePoints, Is.Empty);
        }
    }
}
