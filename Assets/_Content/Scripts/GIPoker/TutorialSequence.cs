using System;
using System.Collections.Generic;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>チュートリアルのステップ種別。</summary>
    public enum TutorialStepKind
    {
        /// <summary>イントロ（自動送りのみ）</summary>
        Intro,
        /// <summary>カード化: 練習アイテムに素手で触れる</summary>
        Cardify,
        /// <summary>掴む: カードをピンチ（つまむ）で掴む</summary>
        Grab,
        /// <summary>収納: バインダーの圏内で指を離して収納する</summary>
        Store,
        /// <summary>ゲイン: カードを両手で引き裂いて効果を発動する</summary>
        Gain,
        /// <summary>ルール確認（以降は本編開始までカウントダウン表示）</summary>
        Rules,
    }

    /// <summary>チュートリアル1ステップ分の定義（指示テキスト＋タイムアウト＋達成判定の有無）。</summary>
    public sealed class TutorialStep
    {
        public TutorialStepKind Kind { get; }

        /// <summary>体験者への指示テキスト</summary>
        public string Instruction { get; }

        /// <summary>達成できなくても自動送りするまでの秒数（PositiveInfinity で自動送りなし）</summary>
        public float TimeoutSeconds { get; }

        /// <summary>達成判定を持つステップか（false は自動送り専用のステップ）</summary>
        public bool RequiresAchievement { get; }

        public TutorialStep(TutorialStepKind kind, string instruction, float timeoutSeconds, bool requiresAchievement)
        {
            Kind = kind;
            Instruction = instruction;
            TimeoutSeconds = timeoutSeconds;
            RequiresAchievement = requiresAchievement;
        }
    }

    /// <summary>
    /// チュートリアルのステップ進行を管理する状態機械（純ロジック・MonoBehaviour 非依存）。
    ///
    /// - 達成通知（NotifyAchieved）またはタイムアウト（Tick）で次のステップへ進む
    /// - 展示で詰まらせないため、達成判定を持つステップもタイムアウトで必ず自動送りされる
    /// - 1回の Tick で進むステップは最大1つ（フレーム駆動が前提）
    /// - チュートリアル全体の終了は本クラスではなく GameSession の固定時間タイマーが決める
    ///   （最終ステップは TimeoutSeconds を PositiveInfinity にして Playing 遷移まで留まる）
    /// </summary>
    public sealed class TutorialSequence
    {
        /// <summary>達成型ステップの既定タイムアウト（秒）。達成できなくてもこの時間で自動送りする</summary>
        public const float DefaultStepTimeoutSeconds = 15f;

        /// <summary>イントロステップの表示時間（秒）</summary>
        public const float IntroDurationSeconds = 5f;

        public IReadOnlyList<TutorialStep> Steps { get; }

        /// <summary>現在のステップ番号（0始まり。全ステップ完了後は Steps.Count）</summary>
        public int CurrentIndex { get; private set; }

        /// <summary>現在のステップでの経過時間（秒）</summary>
        public float StepElapsedSeconds { get; private set; }

        /// <summary>全ステップを消化したか</summary>
        public bool IsFinished => CurrentIndex >= Steps.Count;

        /// <summary>現在のステップ（終了後は null）</summary>
        public TutorialStep CurrentStep => IsFinished ? null : Steps[CurrentIndex];

        /// <summary>現在のステップの自動送りまでの残り秒数（終了後は 0）</summary>
        public float StepRemainingSeconds =>
            IsFinished ? 0f : Math.Max(0f, CurrentStep.TimeoutSeconds - StepElapsedSeconds);

        public TutorialSequence(IReadOnlyList<TutorialStep> steps)
        {
            if (steps == null || steps.Count == 0)
            {
                throw new ArgumentException("チュートリアルのステップが空です", nameof(steps));
            }
            Steps = steps;
        }

        /// <summary>
        /// 既定のステップ構成（GI_POKER_PLAN.md のコアメカニクスに対応する6ステップ）を生成する。
        /// </summary>
        public static IReadOnlyList<TutorialStep> CreateDefaultSteps()
        {
            return new List<TutorialStep>
            {
                new(TutorialStepKind.Intro,
                    "素手でアイテムに触れてカードに変え、\nバインダーへ集めよう！",
                    IntroDurationSeconds, requiresAchievement: false),
                new(TutorialStepKind.Cardify,
                    "まずは目の前のアイテムに\n素手で触れてみよう",
                    DefaultStepTimeoutSeconds, requiresAchievement: true),
                new(TutorialStepKind.Grab,
                    "指でそっとつまんで\nカードを掴もう",
                    DefaultStepTimeoutSeconds, requiresAchievement: true),
                new(TutorialStepKind.Store,
                    "カードをバインダーに近づけて\n指を離すと収納されるよ",
                    DefaultStepTimeoutSeconds, requiresAchievement: true),
                new(TutorialStepKind.Gain,
                    "両手でカードを引き裂こう（ゲイン）\n元のアイテムに戻り、効果が発動する",
                    DefaultStepTimeoutSeconds, requiresAchievement: true),
                new(TutorialStepKind.Rules,
                    "カード化して1分放置すると消滅する！\n制限時間5分でハイスコアを狙おう",
                    float.PositiveInfinity, requiresAchievement: false),
            };
        }

        /// <summary>
        /// 経過時間を加算し、タイムアウトに達していれば次のステップへ進める。
        /// </summary>
        /// <returns>ステップが進んだら true</returns>
        public bool Tick(float deltaTime)
        {
            if (IsFinished || deltaTime <= 0f)
            {
                return false;
            }
            StepElapsedSeconds += deltaTime;
            if (StepElapsedSeconds >= CurrentStep.TimeoutSeconds)
            {
                Advance();
                return true;
            }
            return false;
        }

        /// <summary>
        /// 現在のステップの達成を通知する。達成判定を持つステップのみ次へ進む
        /// （自動送り専用のステップでは無視される）。
        /// </summary>
        /// <returns>ステップが進んだら true</returns>
        public bool NotifyAchieved()
        {
            if (IsFinished || !CurrentStep.RequiresAchievement)
            {
                return false;
            }
            Advance();
            return true;
        }

        private void Advance()
        {
            CurrentIndex++;
            StepElapsedSeconds = 0f;
        }
    }
}
