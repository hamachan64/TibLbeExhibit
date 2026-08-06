using System.Collections.Generic;
using NUnit.Framework;

namespace STYLY.TIB_Template.GIPoker.Tests
{
    /// <summary>
    /// CardEffectService.ShouldStartRoll（リスキーダイスのロール開始判定）のEditModeテスト。
    /// 「ゲイン後のダイスを掴んで（GainedHeld）離す（Gained）」瞬間かつ未ロールのときだけ
    /// ロール演出を開始してよい、という受け入れ基準を検証する。
    /// </summary>
    public class CardEffectServiceTests
    {
        [Test]
        public void ShouldStartRoll_GainedHeldToGained_NotRolledYet_ReturnsTrue()
        {
            Assert.That(
                CardEffectService.ShouldStartRoll(
                    CardizableState.GainedHeld, CardizableState.Gained, alreadyRolled: false),
                Is.True);
        }

        [Test]
        public void ShouldStartRoll_GainedHeldToGained_AlreadyRolled_ReturnsFalse()
        {
            // 1個体につき初回の1回のみ発火する制限（SSカードの重複出現を防ぐ）
            Assert.That(
                CardEffectService.ShouldStartRoll(
                    CardizableState.GainedHeld, CardizableState.Gained, alreadyRolled: true),
                Is.False);
        }

        [TestCase(CardizableState.Item, CardizableState.FloatingCard)]
        [TestCase(CardizableState.FloatingCard, CardizableState.Held)]
        [TestCase(CardizableState.Held, CardizableState.FloatingCard)]
        [TestCase(CardizableState.FloatingCard, CardizableState.Gained)]
        [TestCase(CardizableState.Held, CardizableState.Gained)]
        [TestCase(CardizableState.Gained, CardizableState.GainedHeld)]
        [TestCase(CardizableState.GainedHeld, CardizableState.GainedHeld)]
        [TestCase(CardizableState.Gained, CardizableState.Gained)]
        [TestCase(CardizableState.Gained, CardizableState.Expired)]
        public void ShouldStartRoll_OtherTransitions_ReturnsFalse(
            CardizableState previous, CardizableState current)
        {
            // GainedHeld→Gained 以外の遷移では、未ロールであっても開始しない
            Assert.That(
                CardEffectService.ShouldStartRoll(previous, current, alreadyRolled: false),
                Is.False);
        }

        /// <summary>
        /// CardEffectService.ResolveLotteryRank（宝籤のランク抽選）のEditModeテスト。
        /// リスキーダイス使用時（jackpot=true）・未使用時（jackpot=false）の両方の分岐、
        /// 重みの単調性、出現上限に達したランクの除外を検証する。
        /// </summary>
        private static readonly (CardRank rank, float weight)[] DefaultWeights =
        {
            (CardRank.D, 40f),
            (CardRank.C, 24f),
            (CardRank.B, 16f),
            (CardRank.A, 12f),
            (CardRank.S, 8f),
        };

        [TestCase(0f)]
        [TestCase(0.3f)]
        [TestCase(0.5f)]
        [TestCase(0.999f)]
        [TestCase(1f)]
        public void ResolveLotteryRank_JackpotTrue_AlwaysReturnsSS(float randomValue01)
        {
            // リスキーダイス使用時（jackpot=true）は、抽選重みやランダム値に関わらず必ずSSを返す
            var rank = CardEffectService.ResolveLotteryRank(
                hasPendingDiceJackpot: true,
                availableWeights: DefaultWeights,
                randomValue01: randomValue01);

            Assert.That(rank, Is.EqualTo(CardRank.SS));
        }

        [Test]
        public void ResolveLotteryRank_JackpotTrue_ReturnsSS_EvenWhenNoWeightsAvailable()
        {
            // 候補ランクが1つも無い（全ランク出現上限到達済み）状態でも、jackpot=trueならSSは出る
            var rank = CardEffectService.ResolveLotteryRank(
                hasPendingDiceJackpot: true,
                availableWeights: System.Array.Empty<(CardRank rank, float weight)>(),
                randomValue01: 0.5f);

            Assert.That(rank, Is.EqualTo(CardRank.SS));
        }

        [TestCase(0f)]
        [TestCase(0.1f)]
        [TestCase(0.39f)]
        [TestCase(0.4f)]
        [TestCase(0.5f)]
        [TestCase(0.63f)]
        [TestCase(0.64f)]
        [TestCase(0.75f)]
        [TestCase(0.79f)]
        [TestCase(0.8f)]
        [TestCase(0.9f)]
        [TestCase(0.999f)]
        [TestCase(1f)]
        public void ResolveLotteryRank_JackpotFalse_NeverReturnsSS(float randomValue01)
        {
            // 未使用時（jackpot=false）は、availableWeights にSSを含めない限りSSは絶対に選ばれない
            var rank = CardEffectService.ResolveLotteryRank(
                hasPendingDiceJackpot: false,
                availableWeights: DefaultWeights,
                randomValue01: randomValue01);

            Assert.That(rank, Is.Not.EqualTo(CardRank.SS));
        }

        [TestCase(0f, CardRank.D)]
        [TestCase(0.39f, CardRank.D)]
        [TestCase(0.4f, CardRank.C)]
        [TestCase(0.63f, CardRank.C)]
        [TestCase(0.64f, CardRank.B)]
        [TestCase(0.79f, CardRank.B)]
        [TestCase(0.8f, CardRank.A)]
        [TestCase(0.91f, CardRank.A)]
        [TestCase(0.92f, CardRank.S)]
        [TestCase(0.999f, CardRank.S)]
        public void ResolveLotteryRank_JackpotFalse_WeightedSelectionMatchesCumulativeRanges(
            float randomValue01, CardRank expectedRank)
        {
            // D=40,C=24,B=16,A=12,S=8（合計100）の累積範囲どおりに選ばれることを確認する
            // （ランクが高いほど選ばれる重みが小さい＝単調減少であることの裏付けにもなる）
            var rank = CardEffectService.ResolveLotteryRank(
                hasPendingDiceJackpot: false,
                availableWeights: DefaultWeights,
                randomValue01: randomValue01);

            Assert.That(rank, Is.EqualTo(expectedRank));
        }

        [Test]
        public void ResolveLotteryRank_JackpotFalse_EmptyAvailableWeights_ReturnsNull()
        {
            // 全ランクが出現上限に達していて候補が1つも無い場合は null（不発）を返す
            var rank = CardEffectService.ResolveLotteryRank(
                hasPendingDiceJackpot: false,
                availableWeights: System.Array.Empty<(CardRank rank, float weight)>(),
                randomValue01: 0.5f);

            Assert.That(rank, Is.Null);
        }

        [Test]
        public void ResolveLotteryRank_JackpotFalse_RankAtCapacityIsExcluded_AndWeightIsNotSelectable()
        {
            // 出現上限に達したランク（ここではD）は呼び出し側で availableWeights から除外される想定。
            // 除外された状態で D の重み範囲に相当する乱数値を渡しても、
            // 残った候補（C以降）から選ばれ、Dが返ることは無い
            var weightsWithoutD = new List<(CardRank rank, float weight)>
            {
                (CardRank.C, 24f),
                (CardRank.B, 16f),
                (CardRank.A, 12f),
                (CardRank.S, 8f),
            };

            var rank = CardEffectService.ResolveLotteryRank(
                hasPendingDiceJackpot: false,
                availableWeights: weightsWithoutD,
                randomValue01: 0f);

            Assert.That(rank, Is.Not.Null);
            Assert.That(rank, Is.Not.EqualTo(CardRank.D));
            Assert.That(rank, Is.EqualTo(CardRank.C));
        }
    }
}
