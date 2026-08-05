using System;
using System.Collections.Generic;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>チュートリアルのステップ種別（新7ステップ構成）。</summary>
    public enum TutorialStepKind
    {
        /// <summary>ルール説明（自動送りのみ）</summary>
        Intro,
        /// <summary>バインダーを出してみよう: Lサインでバインダーを表示する</summary>
        BinderShow,
        /// <summary>カード化してみよう: 練習アイテムに素手で触れる</summary>
        Cardify,
        /// <summary>バインダーに収納してみよう: カードをバインダーに収納する</summary>
        Store,
        /// <summary>カードをアイテムにしてみよう: カードを両手で引き裂いてゲインする</summary>
        Gain,
        /// <summary>注意事項（自動送りのみ）</summary>
        Caution,
        /// <summary>準備はいいか？（読み上げ完了後、本編開始までの最終ステップ）</summary>
        Ready,
    }

    /// <summary>チュートリアル1ステップ分の定義（タイトル＋指示テキスト＋タイムアウト＋達成判定の有無）。</summary>
    public sealed class TutorialStep
    {
        public TutorialStepKind Kind { get; }

        /// <summary>体験者へ表示するタイトル</summary>
        public string Title { get; }

        /// <summary>体験者への指示テキスト</summary>
        public string Instruction { get; }

        /// <summary>
        /// 達成できなくても自動送りするまでの秒数（PositiveInfinity で自動送りなし）。
        /// 音声クリップが設定されている場合、TutorialDirector が「音声長 + バッファ秒」で
        /// 上書きしたインスタンスに差し替える（本値はクリップ未設定時のフォールバック）。
        /// </summary>
        public float TimeoutSeconds { get; }

        /// <summary>達成判定を持つステップか（false は自動送り専用のステップ）</summary>
        public bool RequiresAchievement { get; }

        public TutorialStep(TutorialStepKind kind, string title, string instruction, float timeoutSeconds, bool requiresAchievement)
        {
            Kind = kind;
            Title = title;
            Instruction = instruction;
            TimeoutSeconds = timeoutSeconds;
            RequiresAchievement = requiresAchievement;
        }

        /// <summary>
        /// TimeoutSeconds だけ差し替えた新インスタンスを作る（イミュータブルなため）。
        /// 音声長ベースのタイムアウト上書きに使う。
        /// </summary>
        public TutorialStep WithTimeoutSeconds(float timeoutSeconds)
        {
            return new TutorialStep(Kind, Title, Instruction, timeoutSeconds, RequiresAchievement);
        }
    }

    /// <summary>
    /// チュートリアルのステップ進行を管理する状態機械（純ロジック・MonoBehaviour 非依存）。
    ///
    /// - 達成通知（NotifyAchieved）またはタイムアウト（Tick）で次のステップへ進む
    /// - 展示で詰まらせないため、達成判定を持つステップもタイムアウトで必ず自動送りされる
    /// - 1回の Tick で進むステップは最大1つ（フレーム駆動が前提）
    /// - 達成判定を持たないステップ（Intro/Caution/Ready）は TimeoutSeconds のみで自動送りされる
    ///   （TutorialDirector が音声長+バッファ秒で TimeoutSeconds を都度上書きする）
    /// - 最終ステップ（Ready）を消化した後の「本編開始までの5秒カウントダウン」は本クラスの
    ///   範囲外で、GameSession の固定時間タイマー（tutorialDurationSeconds）が担う
    /// </summary>
    public sealed class TutorialSequence
    {
        /// <summary>ステップ1（ルール）の音声未設定時フォールバック表示時間（秒）</summary>
        public const float IntroDurationSeconds = 8f;

        /// <summary>達成型ステップ（2〜5）の音声未設定時フォールバックタイムアウト（秒）</summary>
        public const float DefaultStepTimeoutSeconds = 15f;

        /// <summary>ステップ6（注意事項）の音声未設定時フォールバック表示時間（秒）</summary>
        public const float CautionDurationSeconds = 10f;

        /// <summary>ステップ7（準備はいいか？）の音声未設定時フォールバック表示時間（秒）</summary>
        public const float ReadyDurationSeconds = 6f;

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
        /// 既定のステップ構成（新7ステップ: ルール／バインダーを出してみよう／カード化してみよう／
        /// バインダーに収納してみよう／カードをアイテムにしてみよう／注意事項／準備はいいか？）を生成する。
        /// ここでの TimeoutSeconds はすべて「音声アセットが無い場合のフォールバック値」であり、
        /// 実際の音声長を使った上書きは TutorialDirector.StartTutorial() が行う。
        /// </summary>
        public static IReadOnlyList<TutorialStep> CreateDefaultSteps()
        {
            return new List<TutorialStep>
            {
                new(TutorialStepKind.Intro,
                    "ルール",
                    "アイテムに触れて得られたカードをバインダーに集めるゲーム\nレアリティの高いカードほど、高得点だぞ！",
                    IntroDurationSeconds, requiresAchievement: false),
                new(TutorialStepKind.BinderShow,
                    "バインダーを出してみよう",
                    "右のように、L字のポーズを顔の前ですると、バインダーが現れるぞ！",
                    DefaultStepTimeoutSeconds, requiresAchievement: true),
                new(TutorialStepKind.Cardify,
                    "カード化してみよう",
                    "目の前のアイテムに手で触れるとカードになるぞ",
                    DefaultStepTimeoutSeconds, requiresAchievement: true),
                new(TutorialStepKind.Store,
                    "バインダーに収納してみよう",
                    "カードをバインダーに近づけると収納して、ポイントをゲットできるぞ！",
                    DefaultStepTimeoutSeconds, requiresAchievement: true),
                new(TutorialStepKind.Gain,
                    "カードをアイテムにしてみよう",
                    "カードの中には、効果を持つものがある。\nカードを両手で引き裂くと元のアイテムに戻りそのカード効果を使えるぞ！\nカードはアイテムにしたらカードに戻せないから気をつけろ！",
                    DefaultStepTimeoutSeconds, requiresAchievement: true),
                new(TutorialStepKind.Caution,
                    "注意事項",
                    "最後に注意事項だ。\n・カードはアイテムにしたらカードに戻せないから気をつけろ！\n・カード化から1分放置で消滅するぞ！",
                    CautionDurationSeconds, requiresAchievement: false),
                new(TutorialStepKind.Ready,
                    "準備はいいか？",
                    "制限時間5分でハイスコアを狙え！",
                    ReadyDurationSeconds, requiresAchievement: false),
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
