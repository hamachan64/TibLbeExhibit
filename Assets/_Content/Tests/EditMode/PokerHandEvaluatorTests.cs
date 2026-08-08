using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker.Tests
{
    /// <summary>
    /// PokerHandEvaluator（基本得点＋組み合わせボーナス）のEditModeテスト。
    /// 得点仕様は現行の PokerHandEvaluator.DefaultBasePoints
    ///   D=10, C=20, B=50, A=100, S=500, SS=10000 に準拠する。
    /// 組み合わせボーナス: 全ランク獲得 +5000 / 全種類獲得 +10000。
    /// </summary>
    public class PokerHandEvaluatorTests
    {
        // ---- ヘルパー: CardInstance を簡単に作るファクトリ ----

        /// <summary>
        /// テスト用に最低限の CardDefinition (ScriptableObject) を生成し、
        /// ランクをリフレクションで設定した CardInstance を返す。
        /// </summary>
        private static CardInstance MakeCard(CardRank rank, bool isBonus = false)
        {
            var def = ScriptableObject.CreateInstance<CardDefinition>();
            // CardDefinition.rank は [SerializeField] private フィールドなのでリフレクションで設定
            var rankField = typeof(CardDefinition).GetField(
                "rank",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            rankField?.SetValue(def, rank);

            var instance = new CardInstance(def, null);
            if (isBonus)
            {
                // MarkAsBonus() は internal なのでリフレクションで呼ぶ
                typeof(CardInstance)
                    .GetMethod("MarkAsBonus",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.Invoke(instance, null);
            }
            return instance;
        }

        private static CardInstance[] MakeCards(params CardRank[] ranks)
        {
            var cards = new CardInstance[ranks.Length];
            for (var i = 0; i < ranks.Length; i++)
                cards[i] = MakeCard(ranks[i]);
            return cards;
        }

        // ---- 基本得点テスト ----

        [Test]
        public void Evaluate_Empty_ReturnsZero()
        {
            var result = PokerHandEvaluator.Evaluate(System.Array.Empty<CardInstance>());

            Assert.That(result.BaseScore,  Is.EqualTo(0));
            Assert.That(result.BonusScore, Is.EqualTo(0));
            Assert.That(result.TotalScore, Is.EqualTo(0));
            Assert.That(result.Combos,     Is.Empty);
        }

        [TestCase(CardRank.D,  10)]
        [TestCase(CardRank.C,  20)]
        [TestCase(CardRank.B,  50)]
        [TestCase(CardRank.A,  100)]
        [TestCase(CardRank.S,  500)]
        [TestCase(CardRank.SS, 10000)]
        public void Evaluate_SingleCard_ReturnsDefaultBasePoint(CardRank rank, int expected)
        {
            var result = PokerHandEvaluator.Evaluate(new[] { MakeCard(rank) });

            Assert.That(result.BaseScore,  Is.EqualTo(expected));
            Assert.That(result.BonusScore, Is.EqualTo(0));
            Assert.That(result.Combos,     Is.Empty);
        }

        [Test]
        public void Evaluate_BonusTimeCard_ScoreIsMultiplied()
        {
            // D(10点) × 1.5 = 15（小数切り捨て）
            var result = PokerHandEvaluator.Evaluate(new[] { MakeCard(CardRank.D, isBonus: true) });

            Assert.That(result.BaseScore, Is.EqualTo(15));
        }

        [Test]
        public void Evaluate_MultipleCards_SumsBaseScore()
        {
            // D(10) + A(100) + S(500) = 610
            var result = PokerHandEvaluator.Evaluate(MakeCards(CardRank.D, CardRank.A, CardRank.S));

            Assert.That(result.BaseScore, Is.EqualTo(610));
            Assert.That(result.Combos,    Is.Empty);
        }

        // ---- 全ランク獲得ボーナステスト ----

        [Test]
        public void Evaluate_AllRanksPresent_AddsAllRankBonus()
        {
            // D+C+B+A+S 各1枚 → 基本: 10+20+50+100+500 = 680、ボーナス: +5000
            var result = PokerHandEvaluator.Evaluate(
                MakeCards(CardRank.D, CardRank.C, CardRank.B, CardRank.A, CardRank.S));

            Assert.That(result.BaseScore,    Is.EqualTo(680));
            Assert.That(result.BonusScore,   Is.EqualTo(PokerHandEvaluator.AllRankBonus));
            Assert.That(result.TotalScore,   Is.EqualTo(680 + PokerHandEvaluator.AllRankBonus));
            Assert.That(result.Combos.Count, Is.EqualTo(1));
        }

        [Test]
        public void Evaluate_AllRanksWithExtraCards_StillAddsBonus()
        {
            // D×2 + C + B + A + S → 全ランク揃い判定はカウントが 1 以上あればOK
            var result = PokerHandEvaluator.Evaluate(
                MakeCards(CardRank.D, CardRank.D, CardRank.C, CardRank.B, CardRank.A, CardRank.S));

            Assert.That(result.BonusScore,   Is.EqualTo(PokerHandEvaluator.AllRankBonus));
            Assert.That(result.Combos.Count, Is.EqualTo(1));
        }

        [Test]
        public void Evaluate_MissingOneRank_NoAllRankBonus()
        {
            // S が抜けているので全ランク揃いボーナスなし
            var result = PokerHandEvaluator.Evaluate(
                MakeCards(CardRank.D, CardRank.C, CardRank.B, CardRank.A));

            Assert.That(result.BonusScore, Is.EqualTo(0));
            Assert.That(result.Combos,     Is.Empty);
        }

        // ---- basePoints 注入オーバーロード（null フォールバック） ----

        [Test]
        public void Evaluate_BasePointsNull_FallsBackToDefaultBasePoints()
        {
            // null を明示的に渡しても DefaultBasePoints と同じ結果になる
            var withDefault = PokerHandEvaluator.Evaluate(MakeCards(CardRank.D, CardRank.S));
            var withNullArg = PokerHandEvaluator.Evaluate(MakeCards(CardRank.D, CardRank.S), null);

            Assert.That(withNullArg.BaseScore,  Is.EqualTo(withDefault.BaseScore));
            Assert.That(withNullArg.TotalScore, Is.EqualTo(510)); // D(10) + S(500)
        }

        [Test]
        public void Evaluate_CustomBasePoints_OverridesDefaultValues()
        {
            var customBasePoints = new Dictionary<CardRank, int>
            {
                { CardRank.D, 99 },
                { CardRank.C, 1  },
            };

            var result = PokerHandEvaluator.Evaluate(MakeCards(CardRank.D, CardRank.C), customBasePoints);

            Assert.That(result.BaseScore, Is.EqualTo(100));
        }

        [Test]
        public void Evaluate_CustomBasePoints_RankMissingFromDictionary_TreatedAsZero()
        {
            // 渡した辞書にないランクは 0 点扱い（DefaultBasePoints へはフォールバックしない）
            var customBasePoints = new Dictionary<CardRank, int>
            {
                { CardRank.D, 10 },
            };

            var result = PokerHandEvaluator.Evaluate(MakeCards(CardRank.D, CardRank.S), customBasePoints);

            Assert.That(result.BaseScore, Is.EqualTo(10)); // S は辞書にないので 0
        }

        [Test]
        public void Evaluate_CustomBasePoints_BonusComboUnaffectedByBasePoints()
        {
            // basePoints をカスタムにしてもランク揃い判定はランクの有無だけで決まる
            var customBasePoints = new Dictionary<CardRank, int>
            {
                { CardRank.D, 1 }, { CardRank.C, 1 }, { CardRank.B, 1 }, { CardRank.A, 1 }, { CardRank.S, 1 },
            };

            var result = PokerHandEvaluator.Evaluate(
                MakeCards(CardRank.D, CardRank.C, CardRank.B, CardRank.A, CardRank.S),
                customBasePoints);

            Assert.That(result.BaseScore,  Is.EqualTo(5));
            Assert.That(result.BonusScore, Is.EqualTo(PokerHandEvaluator.AllRankBonus));
        }

        // ---- 全種類ボーナステスト ----

        [Test]
        public void Evaluate_AllCardIdsNull_NoAllCardTypeBonus()
        {
            var result = PokerHandEvaluator.Evaluate(MakeCards(CardRank.D), allCardIds: null);

            Assert.That(result.BonusScore, Is.EqualTo(0));
        }

        [Test]
        public void Evaluate_AllCardIdsEmpty_NoAllCardTypeBonus()
        {
            var result = PokerHandEvaluator.Evaluate(
                MakeCards(CardRank.D),
                allCardIds: System.Array.Empty<string>());

            Assert.That(result.BonusScore, Is.EqualTo(0));
        }
    }
}
