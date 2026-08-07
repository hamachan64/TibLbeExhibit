using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace STYLY.TIB_Template.GIPoker.Tests
{
    /// <summary>
    /// PokerHandEvaluator（基本得点＋組み合わせボーナス）のEditModeテスト。
    /// 得点仕様は GI_POKER_PLAN.md「3. カードランクとスコアシステム」に準拠する。
    /// </summary>
    public class PokerHandEvaluatorTests
    {
        [Test]
        public void Evaluate_Empty_ReturnsZero()
        {
            var result = PokerHandEvaluator.Evaluate(Array.Empty<CardRank>());

            Assert.That(result.BaseScore, Is.EqualTo(0));
            Assert.That(result.BonusScore, Is.EqualTo(0));
            Assert.That(result.TotalScore, Is.EqualTo(0));
            Assert.That(result.Combos, Is.Empty);
        }

        [TestCase(CardRank.D, 1)]
        [TestCase(CardRank.C, 2)]
        [TestCase(CardRank.B, 3)]
        [TestCase(CardRank.A, 5)]
        [TestCase(CardRank.S, 100)]
        [TestCase(CardRank.SS, 300)]
        public void Evaluate_SingleCard_ReturnsBasePoint(CardRank rank, int expected)
        {
            var result = PokerHandEvaluator.Evaluate(new[] { rank });

            Assert.That(result.TotalScore, Is.EqualTo(expected));
            Assert.That(result.Combos, Is.Empty);
        }

        [Test]
        public void Evaluate_Straight_Adds200Bonus()
        {
            // D+C+B+A+S = 1+2+3+5+100 = 111（基本）+ 200（ストレート）= 311
            var result = PokerHandEvaluator.Evaluate(new[]
            {
                CardRank.D, CardRank.C, CardRank.B, CardRank.A, CardRank.S,
            });

            Assert.That(result.BaseScore, Is.EqualTo(111));
            Assert.That(result.BonusScore, Is.EqualTo(200));
            Assert.That(result.TotalScore, Is.EqualTo(311));
            Assert.That(result.Combos.Count, Is.EqualTo(1));
        }

        [Test]
        public void Evaluate_SpecialCombo_SAA_Adds150Bonus()
        {
            // S+A+A = 100+5+5 = 110（基本）+ 150（指定コンボ）= 260
            var result = PokerHandEvaluator.Evaluate(new[]
            {
                CardRank.S, CardRank.A, CardRank.A,
            });

            Assert.That(result.BaseScore, Is.EqualTo(110));
            Assert.That(result.BonusScore, Is.EqualTo(150));
            Assert.That(result.TotalScore, Is.EqualTo(260));
        }

        [Test]
        public void Evaluate_SameRankTriple_Adds50Bonus()
        {
            // D×3 = 3（基本）+ 50（同ランクセット）= 53
            var result = PokerHandEvaluator.Evaluate(new[]
            {
                CardRank.D, CardRank.D, CardRank.D,
            });

            Assert.That(result.TotalScore, Is.EqualTo(53));
        }

        [Test]
        public void Evaluate_StraightConsumesCards_BeforeSpecialCombo()
        {
            // D,C,B,A,S,A,A: ストレートが最優先でSを消費するため、S-A-Aは成立しない。
            // 基本 = 111 + 5 + 5 = 121、ボーナス = 200 のみ。
            var result = PokerHandEvaluator.Evaluate(new[]
            {
                CardRank.D, CardRank.C, CardRank.B, CardRank.A, CardRank.S,
                CardRank.A, CardRank.A,
            });

            Assert.That(result.BaseScore, Is.EqualTo(121));
            Assert.That(result.BonusScore, Is.EqualTo(200));
            Assert.That(result.Combos.Count, Is.EqualTo(1));
        }

        [Test]
        public void Evaluate_SpecialComboThenTriple_GreedyConsumption()
        {
            // S,A×5: S-A-A（150）でS1枚+A2枚を消費し、残りA3枚で同ランクセット（50）。
            // 基本 = 100 + 25 = 125、ボーナス = 200、合計 = 325。
            var result = PokerHandEvaluator.Evaluate(new[]
            {
                CardRank.S, CardRank.A, CardRank.A, CardRank.A, CardRank.A, CardRank.A,
            });

            Assert.That(result.BaseScore, Is.EqualTo(125));
            Assert.That(result.BonusScore, Is.EqualTo(200));
            Assert.That(result.TotalScore, Is.EqualTo(325));
            Assert.That(result.Combos.Count, Is.EqualTo(2));
        }

        [Test]
        public void Evaluate_MultipleStraights_CountedRepeatedly()
        {
            // 各ランク2枚ずつ → ストレート2回成立。基本 = 222、ボーナス = 400。
            var ranks = new[]
            {
                CardRank.D, CardRank.C, CardRank.B, CardRank.A, CardRank.S,
            };
            var result = PokerHandEvaluator.Evaluate(ranks.Concat(ranks));

            Assert.That(result.BaseScore, Is.EqualTo(222));
            Assert.That(result.BonusScore, Is.EqualTo(400));
            Assert.That(result.Combos.Count, Is.EqualTo(2));
        }

        [Test]
        public void Evaluate_TriplesAcrossRanks_EachCounted()
        {
            // D×3 + A×3 → 同ランクセット2回。基本 = 3 + 15 = 18、ボーナス = 100。
            var result = PokerHandEvaluator.Evaluate(new[]
            {
                CardRank.D, CardRank.D, CardRank.D,
                CardRank.A, CardRank.A, CardRank.A,
            });

            Assert.That(result.BaseScore, Is.EqualTo(18));
            Assert.That(result.BonusScore, Is.EqualTo(100));
            Assert.That(result.Combos.Count, Is.EqualTo(2));
        }

        // ========== basePoints 注入オーバーロード（GIPokerAssets.RankConfig?.BasePoints 想定） ==========

        [Test]
        public void Evaluate_BasePointsOmitted_FallsBackToDefaultBasePoints()
        {
            // 引数省略時（null）は DefaultBasePoints を使う想定。既存の単一引数呼び出しと結果が一致すること。
            var withDefault = PokerHandEvaluator.Evaluate(new[] { CardRank.D, CardRank.S });
            var withExplicitNull = PokerHandEvaluator.Evaluate(new[] { CardRank.D, CardRank.S }, null);

            Assert.That(withExplicitNull.BaseScore, Is.EqualTo(withDefault.BaseScore));
            Assert.That(withExplicitNull.TotalScore, Is.EqualTo(101));
        }

        [Test]
        public void Evaluate_CustomBasePoints_OverridesDefaultValues()
        {
            // CardRankConfig.BasePoints 相当のカスタム辞書を渡すと、そちらが使われる（Default 側は無視される）
            var customBasePoints = new Dictionary<CardRank, int>
            {
                { CardRank.D, 10 },
                { CardRank.C, 20 },
            };

            var result = PokerHandEvaluator.Evaluate(new[] { CardRank.D, CardRank.C }, customBasePoints);

            Assert.That(result.BaseScore, Is.EqualTo(30));
        }

        [Test]
        public void Evaluate_CustomBasePoints_RankMissingFromDictionary_TreatedAsZero()
        {
            // 渡された辞書にランクが無い場合は0点扱い（DefaultBasePointsへはフォールバックしない）
            var customBasePoints = new Dictionary<CardRank, int>
            {
                { CardRank.D, 10 },
            };

            var result = PokerHandEvaluator.Evaluate(new[] { CardRank.D, CardRank.S }, customBasePoints);

            Assert.That(result.BaseScore, Is.EqualTo(10));
        }

        [Test]
        public void Evaluate_CustomBasePoints_BonusComboCalculationUnaffected()
        {
            // basePoints を差し替えてもボーナス役（組み合わせ）判定ロジック自体はランクの数だけで決まる
            var customBasePoints = new Dictionary<CardRank, int>
            {
                { CardRank.D, 1 }, { CardRank.C, 1 }, { CardRank.B, 1 }, { CardRank.A, 1 }, { CardRank.S, 1 },
            };

            var result = PokerHandEvaluator.Evaluate(new[]
            {
                CardRank.D, CardRank.C, CardRank.B, CardRank.A, CardRank.S,
            }, customBasePoints);

            Assert.That(result.BaseScore, Is.EqualTo(5));
            Assert.That(result.BonusScore, Is.EqualTo(200));
        }
    }
}
