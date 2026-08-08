using NUnit.Framework;

namespace STYLY.TIB_Template.GIPoker.Tests
{
    /// <summary>
    /// TutorialAudioController.PlanAction（GameSession.State の previous/next から
    /// 発火すべき音響アクションを決定する純ロジック）のEditModeテスト。
    /// TransitionPlannerTests と同じ状態遷移表に対して、音響アクションが
    /// Tutorial への突入／Tutorial から Playing への遷移のときだけ発火することを確認する。
    /// </summary>
    public class TutorialAudioControllerTests
    {
        [Test]
        public void WaitingForStart_To_Tutorial_ReturnsEnterTutorial()
        {
            var action = TutorialAudioController.PlanAction(SessionState.WaitingForStart, SessionState.Tutorial);

            Assert.That(action, Is.EqualTo(TutorialAudioAction.EnterTutorial));
        }

        [Test]
        public void Finished_To_Tutorial_ReturnsEnterTutorial()
        {
            // StartSession() は WaitingForStart 以外に Finished からも呼べる（セッション再開）。
            // どちらもMR基準状態のため同じ「Tutorial突入」扱いになる。
            var action = TutorialAudioController.PlanAction(SessionState.Finished, SessionState.Tutorial);

            Assert.That(action, Is.EqualTo(TutorialAudioAction.EnterTutorial));
        }

        [Test]
        public void Tutorial_To_Playing_ReturnsExitTutorialToPlaying()
        {
            var action = TutorialAudioController.PlanAction(SessionState.Tutorial, SessionState.Playing);

            Assert.That(action, Is.EqualTo(TutorialAudioAction.ExitTutorialToPlaying));
        }

        [Test]
        public void WaitingForStart_To_Playing_SkipTutorial_ReturnsNone()
        {
            // skipTutorial 時は Tutorial を経由せず直接 Playing へ遷移する。BGM切り替えは発火しない。
            var action = TutorialAudioController.PlanAction(SessionState.WaitingForStart, SessionState.Playing);

            Assert.That(action, Is.EqualTo(TutorialAudioAction.None));
        }

        [Test]
        public void Playing_To_Finished_ReturnsEnterFinished()
        {
            var action = TutorialAudioController.PlanAction(SessionState.Playing, SessionState.Finished);

            Assert.That(action, Is.EqualTo(TutorialAudioAction.EnterFinished));
        }

        [Test]
        public void SameState_ReturnsNone()
        {
            var action = TutorialAudioController.PlanAction(SessionState.Tutorial, SessionState.Tutorial);

            Assert.That(action, Is.EqualTo(TutorialAudioAction.None));
        }

        [Test]
        public void Finished_To_Playing_SkipTutorial_ReturnsNone()
        {
            var action = TutorialAudioController.PlanAction(SessionState.Finished, SessionState.Playing);

            Assert.That(action, Is.EqualTo(TutorialAudioAction.None));
        }
    }
}
