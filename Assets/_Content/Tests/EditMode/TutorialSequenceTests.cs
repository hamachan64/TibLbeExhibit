using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace STYLY.TIB_Template.GIPoker.Tests
{
    /// <summary>
    /// TutorialSequence（チュートリアルのステップ進行状態機械）のEditModeテスト。
    /// ステップ構成・達成による進行・タイムアウト自動送りの仕様は
    /// GI_POKER_PLAN.md のコアメカニクスおよび Issue #2 に準拠する。
    /// </summary>
    public class TutorialSequenceTests
    {
        private static TutorialSequence CreateDefault()
        {
            return new TutorialSequence(TutorialSequence.CreateDefaultSteps());
        }

        /// <summary>達成型ステップを2つ持つ最小構成（終了まで到達できる）</summary>
        private static TutorialSequence CreateTwoStepFinite()
        {
            return new TutorialSequence(new List<TutorialStep>
            {
                new(TutorialStepKind.Cardify, "触れる", 10f, requiresAchievement: true),
                new(TutorialStepKind.Grab, "掴む", 10f, requiresAchievement: true),
            });
        }

        [Test]
        public void Constructor_EmptySteps_Throws()
        {
            Assert.Throws<ArgumentException>(() => _ = new TutorialSequence(new List<TutorialStep>()));
            Assert.Throws<ArgumentException>(() => _ = new TutorialSequence(null));
        }

        [Test]
        public void DefaultSteps_ComposedOfSixStepsInOrder()
        {
            var steps = TutorialSequence.CreateDefaultSteps();

            Assert.That(steps.Count, Is.EqualTo(6));
            Assert.That(steps[0].Kind, Is.EqualTo(TutorialStepKind.Intro));
            Assert.That(steps[1].Kind, Is.EqualTo(TutorialStepKind.Cardify));
            Assert.That(steps[2].Kind, Is.EqualTo(TutorialStepKind.Grab));
            Assert.That(steps[3].Kind, Is.EqualTo(TutorialStepKind.Store));
            Assert.That(steps[4].Kind, Is.EqualTo(TutorialStepKind.Gain));
            Assert.That(steps[5].Kind, Is.EqualTo(TutorialStepKind.Rules));

            // イントロとルール確認は自動送り専用、中間4ステップは達成判定を持つ
            Assert.That(steps[0].RequiresAchievement, Is.False);
            Assert.That(steps[1].RequiresAchievement, Is.True);
            Assert.That(steps[2].RequiresAchievement, Is.True);
            Assert.That(steps[3].RequiresAchievement, Is.True);
            Assert.That(steps[4].RequiresAchievement, Is.True);
            Assert.That(steps[5].RequiresAchievement, Is.False);

            // 最終ステップは Playing 遷移（GameSession の固定時間）まで留まる
            Assert.That(float.IsPositiveInfinity(steps[5].TimeoutSeconds), Is.True);
        }

        [Test]
        public void InitialState_FirstStepActive()
        {
            var sequence = CreateDefault();

            Assert.That(sequence.CurrentIndex, Is.EqualTo(0));
            Assert.That(sequence.IsFinished, Is.False);
            Assert.That(sequence.CurrentStep.Kind, Is.EqualTo(TutorialStepKind.Intro));
            Assert.That(sequence.StepElapsedSeconds, Is.EqualTo(0f));
        }

        [Test]
        public void Tick_BeforeTimeout_DoesNotAdvance()
        {
            var sequence = CreateDefault();

            var advanced = sequence.Tick(TutorialSequence.IntroDurationSeconds - 0.1f);

            Assert.That(advanced, Is.False);
            Assert.That(sequence.CurrentIndex, Is.EqualTo(0));
        }

        [Test]
        public void Tick_PastTimeout_AdvancesAndResetsElapsed()
        {
            var sequence = CreateDefault();

            var advanced = sequence.Tick(TutorialSequence.IntroDurationSeconds);

            Assert.That(advanced, Is.True);
            Assert.That(sequence.CurrentIndex, Is.EqualTo(1));
            Assert.That(sequence.StepElapsedSeconds, Is.EqualTo(0f));
        }

        [Test]
        public void Tick_AccumulatesAcrossCalls()
        {
            var sequence = CreateDefault();

            // 2秒×3回 = 6秒 > イントロの5秒
            Assert.That(sequence.Tick(2f), Is.False);
            Assert.That(sequence.Tick(2f), Is.False);
            Assert.That(sequence.Tick(2f), Is.True);
            Assert.That(sequence.CurrentIndex, Is.EqualTo(1));
        }

        [Test]
        public void Tick_NegativeOrZeroDelta_Ignored()
        {
            var sequence = CreateDefault();

            Assert.That(sequence.Tick(-10f), Is.False);
            Assert.That(sequence.Tick(0f), Is.False);
            Assert.That(sequence.StepElapsedSeconds, Is.EqualTo(0f));
        }

        [Test]
        public void StepRemainingSeconds_ReflectsElapsed()
        {
            var sequence = CreateDefault();

            sequence.Tick(2f);

            Assert.That(sequence.StepRemainingSeconds,
                Is.EqualTo(TutorialSequence.IntroDurationSeconds - 2f).Within(0.0001f));
        }

        [Test]
        public void NotifyAchieved_OnAchievementStep_Advances()
        {
            var sequence = CreateDefault();
            sequence.Tick(TutorialSequence.IntroDurationSeconds); // → Cardify

            var advanced = sequence.NotifyAchieved();

            Assert.That(advanced, Is.True);
            Assert.That(sequence.CurrentStep.Kind, Is.EqualTo(TutorialStepKind.Grab));
        }

        [Test]
        public void NotifyAchieved_OnAutoAdvanceStep_IsIgnored()
        {
            var sequence = CreateDefault();

            // イントロは自動送り専用のため達成通知では進まない
            var advanced = sequence.NotifyAchieved();

            Assert.That(advanced, Is.False);
            Assert.That(sequence.CurrentIndex, Is.EqualTo(0));
        }

        [Test]
        public void Timeout_OnAchievementStep_AutoAdvances()
        {
            var sequence = CreateDefault();
            sequence.Tick(TutorialSequence.IntroDurationSeconds); // → Cardify

            // 達成できなくても既定タイムアウトで自動送りされる（展示で詰まらせない）
            var advanced = sequence.Tick(TutorialSequence.DefaultStepTimeoutSeconds);

            Assert.That(advanced, Is.True);
            Assert.That(sequence.CurrentStep.Kind, Is.EqualTo(TutorialStepKind.Grab));
        }

        [Test]
        public void NotifyAchieved_ResetsElapsedForNextStep()
        {
            var sequence = CreateDefault();
            sequence.Tick(TutorialSequence.IntroDurationSeconds); // → Cardify
            sequence.Tick(10f); // Cardify で10秒経過

            sequence.NotifyAchieved(); // → Grab

            // 前ステップの経過時間は持ち越されない
            Assert.That(sequence.StepElapsedSeconds, Is.EqualTo(0f));
            Assert.That(sequence.Tick(TutorialSequence.DefaultStepTimeoutSeconds - 0.1f), Is.False);
        }

        [Test]
        public void LastStep_WithInfiniteTimeout_NeverAdvancesByTick()
        {
            var sequence = CreateDefault();
            sequence.Tick(TutorialSequence.IntroDurationSeconds); // → Cardify
            sequence.NotifyAchieved(); // → Grab
            sequence.NotifyAchieved(); // → Store
            sequence.NotifyAchieved(); // → Gain
            sequence.NotifyAchieved(); // → Rules

            Assert.That(sequence.CurrentStep.Kind, Is.EqualTo(TutorialStepKind.Rules));

            // 最終ステップは GameSession の固定時間タイマーが終わるまで留まり続ける
            Assert.That(sequence.Tick(99999f), Is.False);
            Assert.That(sequence.IsFinished, Is.False);
        }

        [Test]
        public void CustomFiniteSteps_CanFinish_AndFurtherCallsAreNoOp()
        {
            var sequence = CreateTwoStepFinite();

            sequence.NotifyAchieved();
            sequence.NotifyAchieved();

            Assert.That(sequence.IsFinished, Is.True);
            Assert.That(sequence.CurrentStep, Is.Null);
            Assert.That(sequence.StepRemainingSeconds, Is.EqualTo(0f));

            // 終了後の Tick / NotifyAchieved は何もしない
            Assert.That(sequence.Tick(100f), Is.False);
            Assert.That(sequence.NotifyAchieved(), Is.False);
            Assert.That(sequence.CurrentIndex, Is.EqualTo(2));
        }

        [Test]
        public void MixedProgression_TimeoutAndAchievement()
        {
            var sequence = CreateTwoStepFinite();

            // 1ステップ目はタイムアウト、2ステップ目は達成で進む
            Assert.That(sequence.Tick(10f), Is.True);
            Assert.That(sequence.CurrentIndex, Is.EqualTo(1));
            Assert.That(sequence.NotifyAchieved(), Is.True);
            Assert.That(sequence.IsFinished, Is.True);
        }
    }
}
