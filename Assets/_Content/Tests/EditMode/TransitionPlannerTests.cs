using NUnit.Framework;

namespace STYLY.TIB_Template.GIPoker.Tests
{
    /// <summary>
    /// TransitionPlanner（MR⇔VR遷移の状態→演出コマンド列決定ロジック）のEditModeテスト。
    /// 状態遷移と演出の対応は Issue #14 の状態遷移表に準拠する。
    /// </summary>
    public class TransitionPlannerTests
    {
        [Test]
        public void WaitingForStart_To_Tutorial_ReturnsFadeShowFadePlan()
        {
            var plan = TransitionPlanner.Plan(SessionState.WaitingForStart, SessionState.Tutorial);

            Assert.That(plan, Is.EqualTo(new[]
            {
                TransitionStep.FadeOutWhite,
                TransitionStep.ShowTutorialSpace,
                TransitionStep.FadeInWhite,
            }));
        }

        [Test]
        public void Tutorial_To_Playing_ReturnsFadeHideFadePlan()
        {
            var plan = TransitionPlanner.Plan(SessionState.Tutorial, SessionState.Playing);

            Assert.That(plan, Is.EqualTo(new[]
            {
                TransitionStep.FadeOutWhite,
                TransitionStep.HideTutorialSpace,
                TransitionStep.FadeInWhite,
            }));
        }

        [Test]
        public void WaitingForStart_To_Playing_SkipTutorial_ReturnsEmptyPlan()
        {
            // skipTutorial 時は Tutorial を経由せず直接 Playing へ遷移する。
            // フェード・VR空間表示は一切行わない。
            var plan = TransitionPlanner.Plan(SessionState.WaitingForStart, SessionState.Playing);

            Assert.That(plan, Is.Empty);
        }

        [Test]
        public void Playing_To_Finished_ReturnsEmptyPlan()
        {
            // 本編終了はすでにMRのため演出不要
            var plan = TransitionPlanner.Plan(SessionState.Playing, SessionState.Finished);

            Assert.That(plan, Is.Empty);
        }

        [Test]
        public void Finished_To_Tutorial_ReturnsFadeShowFadePlan()
        {
            // StartSession() は WaitingForStart 以外に Finished からも呼べる（セッション再開）。
            // どちらもMR基準状態のため同じ「入室」演出になる。
            var plan = TransitionPlanner.Plan(SessionState.Finished, SessionState.Tutorial);

            Assert.That(plan, Is.EqualTo(new[]
            {
                TransitionStep.FadeOutWhite,
                TransitionStep.ShowTutorialSpace,
                TransitionStep.FadeInWhite,
            }));
        }

        [Test]
        public void Finished_To_Playing_SkipTutorial_ReturnsEmptyPlan()
        {
            var plan = TransitionPlanner.Plan(SessionState.Finished, SessionState.Playing);

            Assert.That(plan, Is.Empty);
        }

        [Test]
        public void SameState_ReturnsEmptyPlan()
        {
            var plan = TransitionPlanner.Plan(SessionState.Tutorial, SessionState.Tutorial);

            Assert.That(plan, Is.Empty);
        }

        [Test]
        public void WaitingForStart_To_Finished_ReturnsEmptyPlan()
        {
            // GameSession の正規フローでは起きないが、防御的に空プランであることを確認する
            var plan = TransitionPlanner.Plan(SessionState.WaitingForStart, SessionState.Finished);

            Assert.That(plan, Is.Empty);
        }
    }
}
