using System;
using System.Collections.Generic;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>MR⇔VR遷移の1ステップ。TransitionDirector が実際の演出（フェード・VR空間の表示制御）にマッピングする。</summary>
    public enum TransitionStep
    {
        /// <summary>白へフェードアウトする（視界を覆う）</summary>
        FadeOutWhite,
        /// <summary>TutorialSpaceDirector.ShowAsync でVR空間を表示する</summary>
        ShowTutorialSpace,
        /// <summary>TutorialSpaceDirector.HideAsync でVR空間を非表示にする（MRへ戻る）</summary>
        HideTutorialSpace,
        /// <summary>白からフェードインする（パススルー／本編を見せる）</summary>
        FadeInWhite,
    }

    /// <summary>
    /// GameSession.State の遷移（前状態→次状態）から、実行すべき演出コマンド列を決定する
    /// 純ロジック（MonoBehaviour・DOTween・TutorialSpaceDirector 非依存）。
    /// TransitionDirector から呼ばれ、EditModeテストで単独検証できる（Issue #14）。
    ///
    /// 演出テーブル（.claude/agent-memory/lbe-learnings.md / Issue #14 の状態遷移表準拠）:
    /// - 他状態 → Tutorial: 白フェードアウト → VR空間表示 → 白フェードイン
    /// - Tutorial → Playing: 白フェードアウト → VR空間非表示 → 白フェードイン
    /// - それ以外（WaitingForStart→Playing の skipTutorial 直行、Playing→Finished 等）は演出なし。
    ///   skipTutorial のケースを特別扱いしなくても、「次が Tutorial」でも「前が Tutorial」でもない
    ///   遷移は自動的に空プランになるため、GameSession 側の分岐を関知しなくてよい。
    /// </summary>
    public static class TransitionPlanner
    {
        private static readonly IReadOnlyList<TransitionStep> EnterTutorialPlan = new[]
        {
            TransitionStep.FadeOutWhite,
            TransitionStep.ShowTutorialSpace,
            TransitionStep.FadeInWhite,
        };

        private static readonly IReadOnlyList<TransitionStep> ExitTutorialPlan = new[]
        {
            TransitionStep.FadeOutWhite,
            TransitionStep.HideTutorialSpace,
            TransitionStep.FadeInWhite,
        };

        private static readonly IReadOnlyList<TransitionStep> NoOpPlan = Array.Empty<TransitionStep>();

        /// <summary>
        /// 状態遷移（previous→next）に対応する演出コマンド列を返す。演出不要な遷移は空配列を返す。
        /// </summary>
        public static IReadOnlyList<TransitionStep> Plan(SessionState previous, SessionState next)
        {
            if (previous == next)
            {
                return NoOpPlan;
            }
            if (next == SessionState.Tutorial)
            {
                return EnterTutorialPlan;
            }
            if (previous == SessionState.Tutorial && next == SessionState.Playing)
            {
                return ExitTutorialPlan;
            }
            return NoOpPlan;
        }
    }
}
