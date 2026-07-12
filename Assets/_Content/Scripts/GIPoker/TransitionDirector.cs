using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// GameSession.State を購読し、MR⇔VR遷移（白フェード＋TutorialSpaceDirector の表示制御）を
    /// オーケストレーションする（Issue #14）。GIPokerBootstrap から結線する。
    ///
    /// 状態遷移と演出（TransitionPlanner が決定。.claude/agent-memory/lbe-learnings.md 準拠）:
    /// - 他状態 → Tutorial: 白フェードアウト(約0.8s) → TutorialSpaceDirector.ShowAsync → 白フェードイン(約0.8s)
    /// - Tutorial → Playing: 白フェードアウト(約0.8s) → HideAsync（MRに戻る） → 白フェードイン(約0.8s)
    /// - skipTutorial 時（WaitingForStart→Playing 直行）や Finished への遷移は演出なし
    ///   （TransitionPlanner.Plan が前後状態の組から自動的に空プランを返す）
    ///
    /// 安全性:
    /// - 状態変化のたびに走行中の遷移を CancellationTokenSource の張り替えでキャンセルしてから開始するため、
    ///   短時間での連続状態変化（多重発火）でも演出が二重に走らない
    /// - 破棄時は destroyCancellationToken 経由で自動キャンセルされる
    /// - TutorialSpaceDirector は Issue #13 の契約（Create() / ShowAsync(ct) / HideAsync(ct) / IsShown）
    ///   のみを前提にする（契約外メンバーは使用しない）
    /// </summary>
    public class TransitionDirector : MonoBehaviour
    {
        [SerializeField, Tooltip("白フェードの色（alpha=1 で完全に覆う）")]
        private Color fadeColor = Color.white;

        [SerializeField, Tooltip("フェードアウト／フェードインそれぞれの所要時間（秒）")]
        private float fadeDuration = 0.8f;

        private TutorialSpaceDirector space;
        private ScreenFader fader;

        private SessionState? previousState;
        private CancellationTokenSource transitionCts;

        /// <summary>GIPokerBootstrap から結線する。ScreenFader はここで内部生成する</summary>
        public void Init(GameSession session, TutorialSpaceDirector spaceDirector)
        {
            space = spaceDirector;
            fader = ScreenFader.Create();

            session.State
                .Subscribe(OnStateChanged)
                .AddTo(this);
        }

        /// <summary>
        /// R3 の ReactiveProperty は購読直後に現在値を1回流すため、初回の通知は
        /// 「前状態」を確定させるだけで演出は行わない（起動直後に誤発火させないため）。
        /// </summary>
        private void OnStateChanged(SessionState next)
        {
            if (previousState == null)
            {
                previousState = next;
                return;
            }

            var previous = previousState.Value;
            previousState = next;

            var plan = TransitionPlanner.Plan(previous, next);
            if (plan.Count == 0)
            {
                return;
            }

            RestartTransitionCts();
            RunPlanAsync(plan, transitionCts.Token).Forget();
        }

        private async UniTask RunPlanAsync(IReadOnlyList<TransitionStep> plan, CancellationToken ct)
        {
            try
            {
                foreach (var step in plan)
                {
                    ct.ThrowIfCancellationRequested();
                    await RunStepAsync(step, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // 新しい状態遷移や破棄によるキャンセルは正常系
                // （多重発火時に古い遷移演出を打ち切るための想定挙動）
            }
        }

        private UniTask RunStepAsync(TransitionStep step, CancellationToken ct)
        {
            switch (step)
            {
                case TransitionStep.FadeOutWhite:
                    return fader.FadeOutAsync(fadeColor, fadeDuration, ct);
                case TransitionStep.FadeInWhite:
                    return fader.FadeInAsync(fadeDuration, ct);
                case TransitionStep.ShowTutorialSpace:
                    return space != null ? space.ShowAsync(ct) : UniTask.CompletedTask;
                case TransitionStep.HideTutorialSpace:
                    return space != null ? space.HideAsync(ct) : UniTask.CompletedTask;
                default:
                    return UniTask.CompletedTask;
            }
        }

        private void RestartTransitionCts()
        {
            transitionCts?.Cancel();
            transitionCts?.Dispose();
            transitionCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        }

        private void OnDestroy()
        {
            transitionCts?.Cancel();
            transitionCts?.Dispose();
        }
    }
}
