using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace STYLY.TIB_Template.GIPoker.Tests
{
    /// <summary>
    /// TutorialSequence（チュートリアルのステップ進行状態機械）のEditModeテスト。
    /// ステップ構成（新7ステップ: ルール／バインダーを出してみよう／カード化してみよう／
    /// バインダーに収納してみよう／カードをアイテムにしてみよう／注意事項／準備はいいか？）・
    /// 達成による進行・タイムアウト自動送りの仕様は GI_POKER_PLAN.md のコアメカニクスおよび
    /// .claude/loops/20260804-tutorial-7steps-voice.md に準拠する。
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
                new(TutorialStepKind.Cardify, "カード化してみよう", "触れる", 10f, requiresAchievement: true),
                new(TutorialStepKind.Store, "バインダーに収納してみよう", "収納する", 10f, requiresAchievement: true),
            });
        }

        [Test]
        public void Constructor_EmptySteps_Throws()
        {
            Assert.Throws<ArgumentException>(() => _ = new TutorialSequence(new List<TutorialStep>()));
            Assert.Throws<ArgumentException>(() => _ = new TutorialSequence(null));
        }

        [Test]
        public void DefaultSteps_ComposedOfSevenStepsInOrder()
        {
            var steps = TutorialSequence.CreateDefaultSteps();

            Assert.That(steps.Count, Is.EqualTo(7));
            Assert.That(steps[0].Kind, Is.EqualTo(TutorialStepKind.Intro));
            Assert.That(steps[1].Kind, Is.EqualTo(TutorialStepKind.BinderShow));
            Assert.That(steps[2].Kind, Is.EqualTo(TutorialStepKind.Cardify));
            Assert.That(steps[3].Kind, Is.EqualTo(TutorialStepKind.Store));
            Assert.That(steps[4].Kind, Is.EqualTo(TutorialStepKind.Gain));
            Assert.That(steps[5].Kind, Is.EqualTo(TutorialStepKind.Caution));
            Assert.That(steps[6].Kind, Is.EqualTo(TutorialStepKind.Ready));

            // 自動送り専用（Intro/Caution/Ready）以外は達成判定を持つ
            Assert.That(steps[0].RequiresAchievement, Is.False);
            Assert.That(steps[1].RequiresAchievement, Is.True);
            Assert.That(steps[2].RequiresAchievement, Is.True);
            Assert.That(steps[3].RequiresAchievement, Is.True);
            Assert.That(steps[4].RequiresAchievement, Is.True);
            Assert.That(steps[5].RequiresAchievement, Is.False);
            Assert.That(steps[6].RequiresAchievement, Is.False);
        }

        [Test]
        public void DefaultSteps_TitlesAndInstructionsMatchSpec()
        {
            var steps = TutorialSequence.CreateDefaultSteps();

            Assert.That(steps[0].Title, Is.EqualTo("ルール"));
            Assert.That(steps[0].Instruction,
                Is.EqualTo("アイテムに触れて得られたカードをバインダーに集めるゲーム\nレアリティの高いカードほど、高得点だぞ！"));

            Assert.That(steps[1].Title, Is.EqualTo("バインダーを出してみよう"));
            Assert.That(steps[1].Instruction,
                Is.EqualTo("右のように、L字のポーズを顔の前ですると、バインダーが現れるぞ！"));

            Assert.That(steps[2].Title, Is.EqualTo("カード化してみよう"));
            Assert.That(steps[2].Instruction, Is.EqualTo("目の前のアイテムに手で触れるとカードになるぞ"));

            Assert.That(steps[3].Title, Is.EqualTo("バインダーに収納してみよう"));
            Assert.That(steps[3].Instruction,
                Is.EqualTo("カードをバインダーに近づけると収納して、ポイントをゲットできるぞ！"));

            Assert.That(steps[4].Title, Is.EqualTo("カードをアイテムにしてみよう"));
            Assert.That(steps[4].Instruction,
                Is.EqualTo("カードの中には、効果を持つものがある。\nカードを両手で引き裂くと元のアイテムに戻りそのカード効果を使えるぞ！\nカードはアイテムにしたらカードに戻せないから気をつけろ！"));

            Assert.That(steps[5].Title, Is.EqualTo("注意事項"));
            Assert.That(steps[5].Instruction,
                Is.EqualTo("最後に注意事項だ。\n・カードはアイテムにしたらカードに戻せないから気をつけろ！\n・カード化から1分放置で消滅するぞ！"));

            Assert.That(steps[6].Title, Is.EqualTo("準備はいいか？"));
            Assert.That(steps[6].Instruction, Is.EqualTo("制限時間5分でハイスコアを狙え！"));
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

            // 2秒×回数を重ね、イントロのフォールバックタイムアウト（8秒）を超えたら進む
            Assert.That(sequence.Tick(2f), Is.False);
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
            sequence.Tick(TutorialSequence.IntroDurationSeconds); // → BinderShow

            var advanced = sequence.NotifyAchieved();

            Assert.That(advanced, Is.True);
            Assert.That(sequence.CurrentStep.Kind, Is.EqualTo(TutorialStepKind.Cardify));
        }

        [Test]
        public void NotifyAchieved_OnAutoAdvanceStep_IsIgnored()
        {
            var sequence = CreateDefault();

            // イントロ（ルール）は自動送り専用のため達成通知では進まない
            var advanced = sequence.NotifyAchieved();

            Assert.That(advanced, Is.False);
            Assert.That(sequence.CurrentIndex, Is.EqualTo(0));
        }

        [Test]
        public void Timeout_OnAchievementStep_AutoAdvances()
        {
            var sequence = CreateDefault();
            sequence.Tick(TutorialSequence.IntroDurationSeconds); // → BinderShow

            // 達成できなくても既定タイムアウトで自動送りされる（展示で詰まらせない）
            var advanced = sequence.Tick(TutorialSequence.DefaultStepTimeoutSeconds);

            Assert.That(advanced, Is.True);
            Assert.That(sequence.CurrentStep.Kind, Is.EqualTo(TutorialStepKind.Cardify));
        }

        [Test]
        public void NotifyAchieved_ResetsElapsedForNextStep()
        {
            var sequence = CreateDefault();
            sequence.Tick(TutorialSequence.IntroDurationSeconds); // → BinderShow
            sequence.Tick(10f); // BinderShow で10秒経過

            sequence.NotifyAchieved(); // → Cardify

            // 前ステップの経過時間は持ち越されない
            Assert.That(sequence.StepElapsedSeconds, Is.EqualTo(0f));
            Assert.That(sequence.Tick(TutorialSequence.DefaultStepTimeoutSeconds - 0.1f), Is.False);
        }

        [Test]
        public void AllSteps_CanProgressThroughAchievementToReadyStep()
        {
            var sequence = CreateDefault();
            sequence.Tick(TutorialSequence.IntroDurationSeconds); // → BinderShow
            sequence.NotifyAchieved(); // → Cardify
            sequence.NotifyAchieved(); // → Store
            sequence.NotifyAchieved(); // → Gain
            sequence.NotifyAchieved(); // → Caution（自動送り専用のため無視されるが呼び出し自体は安全）

            Assert.That(sequence.CurrentStep.Kind, Is.EqualTo(TutorialStepKind.Caution));

            sequence.Tick(TutorialSequence.CautionDurationSeconds); // → Ready
            Assert.That(sequence.CurrentStep.Kind, Is.EqualTo(TutorialStepKind.Ready));
            Assert.That(sequence.IsFinished, Is.False);
        }

        [Test]
        public void ReadyStep_TimesOutAndFinishesSequence()
        {
            var sequence = CreateDefault();
            sequence.Tick(TutorialSequence.IntroDurationSeconds); // → BinderShow
            sequence.NotifyAchieved(); // → Cardify
            sequence.NotifyAchieved(); // → Store
            sequence.NotifyAchieved(); // → Gain
            sequence.NotifyAchieved(); // → Caution
            sequence.Tick(TutorialSequence.CautionDurationSeconds); // → Ready

            Assert.That(sequence.CurrentStep.Kind, Is.EqualTo(TutorialStepKind.Ready));

            // Ready ステップはタイムアウトでシーケンス自体を終了させる
            // （本編開始までの最終5秒カウントダウンは GameSession 側の固定時間タイマーが担う）
            var advanced = sequence.Tick(TutorialSequence.ReadyDurationSeconds);

            Assert.That(advanced, Is.True);
            Assert.That(sequence.IsFinished, Is.True);
            Assert.That(sequence.CurrentStep, Is.Null);
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

        [Test]
        public void WithTimeoutSeconds_ReplacesOnlyTimeout_KeepsOtherFieldsImmutable()
        {
            var original = new TutorialStep(TutorialStepKind.Cardify, "カード化してみよう", "触れる", 10f, requiresAchievement: true);

            var replaced = original.WithTimeoutSeconds(99f);

            Assert.That(replaced.TimeoutSeconds, Is.EqualTo(99f));
            Assert.That(replaced.Kind, Is.EqualTo(original.Kind));
            Assert.That(replaced.Title, Is.EqualTo(original.Title));
            Assert.That(replaced.Instruction, Is.EqualTo(original.Instruction));
            Assert.That(replaced.RequiresAchievement, Is.EqualTo(original.RequiresAchievement));

            // 元インスタンスは変更されない（イミュータブル）
            Assert.That(original.TimeoutSeconds, Is.EqualTo(10f));
        }
    }
}
