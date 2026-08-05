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
    }
}
