using System;
using System.Collections.Generic;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// チュートリアル全体の進行役（GIPokerBootstrap がランタイム生成する）。
    ///
    /// GameSession が Tutorial 状態の間だけ動作し、TutorialSequence（純ロジック）を
    /// フレーム駆動しながら、練習アイテムの出現・達成判定・TutorialView の表示更新を行う。
    ///
    /// 設計上のポイント:
    /// - 練習アイテム（TutorialPracticeItem）は CardNetworkSync に登録しないため、
    ///   チュートリアル中の操作で Gi* RPC は一切配信されない
    /// - Playing へ遷移した時点（固定時間経過・skipTutorial 時・CompleteTutorial 含む）で
    ///   練習アイテム・チュートリアルUIを完全撤去し、バインダーに残った練習カードも
    ///   CardBinder.Clear() で空へ戻す（本番スコアを汚さない）
    /// - 達成判定は「一度でも到達したか」の単調フラグで持ち、練習アイテムの State 変化と
    ///   ステップ切り替えの両方のタイミングで評価する（先回り操作にも対応）
    /// - 達成判定を持つステップ（BinderShow/Cardify/Store/Gain）は「音声読み上げ完了 かつ 達成」の
    ///   両方が揃うまで次へ進まない（読み上げ中に達成しても読み上げ完了を待つ）。
    ///   ただしタイムアウト（音声長+バッファ秒）による自動送りは常に有効で、展示で詰まらせない
    /// - ステップのタイムアウト自動送りにより、達成できない体験者がいても詰まらない
    /// </summary>
    public class TutorialDirector : MonoBehaviour
    {
        [SerializeField, Tooltip("練習アイテムを出す位置: プレイヤー正面からの距離（m）")]
        private float itemDistance = 0.45f;

        [SerializeField, Tooltip("練習アイテムを出す位置: 頭からどれだけ下か（m）")]
        private float itemHeightBelowHead = 0.15f;

        /// <summary>達成判定を持たないステップ（Intro/Caution）の音声完了後の追加待機秒数</summary>
        private const float AutoAdvanceStepVoiceBufferSeconds = 1f;

        /// <summary>最終ステップ（Ready）の音声完了後の追加待機秒数（この後 GameSession 側の5秒カウントダウンへ続く）</summary>
        private const float ReadyStepVoiceBufferSeconds = 2f;

        /// <summary>
        /// 達成判定を持つステップ（BinderShow/Cardify/Store/Gain）の音声完了後に確保する、
        /// ジェスチャー実行のための安全側タイムアウトバッファ秒。
        /// 通常はこのタイムアウトより先に達成＋読み上げ完了で次へ進む想定で、
        /// あくまで「体験者が達成できなかった場合に詰まらせない」ための保険値。
        /// </summary>
        private const float AchievementStepVoiceBufferSeconds = 12f;

        private GameSession session;
        private CardBinder binder;
        private BinderView binderView;
        private TutorialSequence sequence;
        private TutorialView view;
        private TutorialPracticeItem practiceItem1; // カード化→収納の練習用
        private TutorialPracticeItem practiceItem2; // ゲイン（引き裂き）の練習用
        private IDisposable tickSubscription;

        /// <summary>チュートリアルが進行中か（撤去処理を1回だけ走らせるためのガード）</summary>
        private bool tutorialActive;

        // 練習アイテムの到達状況（一度達成したら true のままの単調フラグ）
        private bool practiceCardified;
        private bool practiceStored;
        private bool practiceGained;

        /// <summary>GIPokerBootstrap から結線する</summary>
        public void Init(GameSession gameSession, CardBinder cardBinder, BinderView cardBinderView)
        {
            session = gameSession;
            binder = cardBinder;
            binderView = cardBinderView;

            session.State
                .Subscribe(s =>
                {
                    switch (s)
                    {
                        case SessionState.Tutorial:
                            StartTutorial();
                            break;
                        case SessionState.Playing:
                        case SessionState.Finished:
                            CleanupTutorial();
                            break;
                    }
                })
                .AddTo(this);

            // バインダー表示状態は「BinderShow ステップの達成判定」の再評価トリガーとしてのみ使う
            // （フラグとして保持せず、評価時に binderView.IsVisible.CurrentValue を直接参照する）
            if (binderView != null)
            {
                binderView.IsVisible
                    .Subscribe(_ => EvaluateAchievement())
                    .AddTo(this);
            }
        }

        private void StartTutorial()
        {
            if (tutorialActive)
            {
                return;
            }
            tutorialActive = true;
            practiceCardified = false;
            practiceStored = false;
            practiceGained = false;

            sequence = new TutorialSequence(BuildStepsWithVoiceTimeouts());

            var viewGo = new GameObject("GIPoker_TutorialView");
            view = viewGo.AddComponent<TutorialView>();
            view.Init(session, sequence.Steps.Count);

            tickSubscription = Observable.EveryUpdate()
                .Subscribe(_ => TickSequence())
                .AddTo(this);

            OnStepEntered();
        }

        /// <summary>
        /// TutorialSequence.CreateDefaultSteps() をベースに、GIPokerAssets の音声クリップ長を使って
        /// 各ステップの TimeoutSeconds を「音声長 + バッファ秒」で上書きした新しいステップ列を組み立てる。
        /// 対応する音声クリップが無いステップはフォールバック値（CreateDefaultSteps の既定値）のまま。
        /// TutorialStep はイミュータブルなため、WithTimeoutSeconds で新インスタンスに差し替える。
        /// </summary>
        private static IReadOnlyList<TutorialStep> BuildStepsWithVoiceTimeouts()
        {
            var baseSteps = TutorialSequence.CreateDefaultSteps();
            var clips = GIPokerAssets.TutorialStepVoiceClips;
            var result = new List<TutorialStep>(baseSteps.Count);

            for (var i = 0; i < baseSteps.Count; i++)
            {
                var step = baseSteps[i];
                var clip = clips != null && i < clips.Length ? clips[i] : null;
                if (clip != null)
                {
                    var buffer = step.RequiresAchievement
                        ? AchievementStepVoiceBufferSeconds
                        : step.Kind == TutorialStepKind.Ready
                            ? ReadyStepVoiceBufferSeconds
                            : AutoAdvanceStepVoiceBufferSeconds;
                    step = step.WithTimeoutSeconds(clip.length + buffer);
                }
                result.Add(step);
            }

            return result;
        }

        private void TickSequence()
        {
            if (sequence == null)
            {
                return;
            }
            if (sequence.Tick(Time.deltaTime))
            {
                OnStepEntered();
            }
            else
            {
                // 達成条件は満たしているが読み上げ完了待ちのケースを、
                // 読み上げが完了したフレームで確実に拾うための毎フレーム再評価
                EvaluateAchievement();
            }
        }

        /// <summary>ステップ開始時の処理（練習アイテムの出現など）と表示更新</summary>
        private void OnStepEntered()
        {
            var step = sequence?.CurrentStep;
            if (step == null)
            {
                return;
            }

            switch (step.Kind)
            {
                case TutorialStepKind.Cardify:
                    SpawnPracticeItem1();
                    break;
                case TutorialStepKind.Store:
                    // 触れられずタイムアウトした場合でも収納の練習ができるよう強制カード化する
                    EnsureItem1Cardified();
                    break;
                case TutorialStepKind.Gain:
                    SpawnPracticeItem2();
                    break;
            }

            if (view != null)
            {
                view.ShowStep(sequence.CurrentIndex, step);
            }

            // 先回り操作で既に条件を満たしている場合は即座に次へ送る
            EvaluateAchievement();
        }

        /// <summary>
        /// 現在のステップの達成条件を評価し、「達成 かつ 読み上げ完了」であれば次のステップへ進める。
        /// 達成判定を持たないステップでは何もしない（タイムアウトのみで進む）。
        /// </summary>
        private void EvaluateAchievement()
        {
            var step = sequence?.CurrentStep;
            if (step == null || !step.RequiresAchievement)
            {
                return;
            }
            if (view == null || !view.IsVoiceFinished)
            {
                // 達成が先に起きても読み上げ完了までは進めない（音声を打ち切らない）
                return;
            }

            var achieved = step.Kind switch
            {
                TutorialStepKind.BinderShow => binderView != null && binderView.IsVisible.CurrentValue,
                TutorialStepKind.Cardify => practiceCardified,
                TutorialStepKind.Store => practiceStored,
                TutorialStepKind.Gain => practiceGained,
                _ => false,
            };
            if (achieved && sequence.NotifyAchieved())
            {
                OnStepEntered();
            }
        }

        private void SpawnPracticeItem1()
        {
            if (practiceItem1 != null)
            {
                return;
            }
            // 花をイメージしたオレンジの球
            practiceItem1 = TutorialPracticeItem.Create("GIPoker_PracticeItem1", new Color(0.95f, 0.55f, 0.25f));
            PlaceInFront(practiceItem1.transform);
            practiceItem1.gameObject.SetActive(true);

            practiceItem1.State
                .Subscribe(OnPracticeStateChanged)
                .AddTo(practiceItem1);
        }

        private void SpawnPracticeItem2()
        {
            if (practiceItem2 != null)
            {
                return;
            }
            practiceItem2 = TutorialPracticeItem.Create("GIPoker_PracticeItem2", new Color(0.35f, 0.75f, 0.95f));
            PlaceInFront(practiceItem2.transform);
            practiceItem2.gameObject.SetActive(true);

            practiceItem2.State
                .Subscribe(OnPracticeStateChanged)
                .AddTo(practiceItem2);

            // 引き裂き（ゲイン）の練習に集中できるよう、最初からカード化しておく
            practiceItem2.TryCardify();
        }

        /// <summary>練習アイテムの状態変化から到達フラグを更新する（両アイテム共通）</summary>
        private void OnPracticeStateChanged(CardizableState state)
        {
            switch (state)
            {
                case CardizableState.FloatingCard:
                    practiceCardified = true;
                    break;
                case CardizableState.Held:
                    practiceCardified = true;
                    break;
                case CardizableState.Stored:
                    practiceCardified = true;
                    practiceStored = true;
                    break;
                case CardizableState.Gained:
                    practiceGained = true;
                    break;
            }
            EvaluateAchievement();
        }

        private void EnsureItem1Cardified()
        {
            if (practiceItem1 == null)
            {
                SpawnPracticeItem1();
            }
            if (practiceItem1 != null && practiceItem1.State.CurrentValue == CardizableState.Item)
            {
                practiceItem1.TryCardify();
            }
        }

        /// <summary>プレイヤー正面・手の届く距離にアイテムを配置する</summary>
        private void PlaceInFront(Transform target)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                target.position = new Vector3(0f, 1.2f, 0.5f);
                return;
            }
            var flatForward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.001f)
            {
                flatForward = Vector3.forward;
            }
            target.position = cam.transform.position
                              + flatForward * itemDistance
                              + Vector3.down * itemHeightBelowHead;
        }

        /// <summary>
        /// 練習アイテム・チュートリアルUIを完全撤去する（チュートリアルから抜けるときに1回だけ実行）。
        /// skipTutorial 時（Tutorial を経ずに Playing になった場合）は何も生成されていないため
        /// 何もしない。Finished 遷移時にも呼ばれるが、tutorialActive ガードにより
        /// 本番プレイで集めたバインダーの中身を誤って消すことはない。
        /// </summary>
        private void CleanupTutorial()
        {
            if (!tutorialActive)
            {
                return;
            }
            tutorialActive = false;

            tickSubscription?.Dispose();
            tickSubscription = null;
            sequence = null;

            if (practiceItem1 != null)
            {
                Destroy(practiceItem1.gameObject);
                practiceItem1 = null;
            }
            if (practiceItem2 != null)
            {
                Destroy(practiceItem2.gameObject);
                practiceItem2 = null;
            }
            if (view != null)
            {
                Destroy(view.gameObject);
                view = null;
            }

            // バインダーに練習カードが残っていても本番スコアを汚さないよう空へ戻す
            if (binder != null && binder.Cards.Count > 0)
            {
                binder.Clear();
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// エディタ実行時限定のデバッグ機能: 現在のチュートリアルステップを強制的にスキップする
        /// （読み上げ完了待ち・達成条件を無視して次のステップへ進める）。検証時間短縮用で、
        /// 実機ビルドには含めない。GIPokerBootstrap から N キー押下時に呼び出される想定。
        /// </summary>
        public void SkipCurrentStep()
        {
            if (sequence == null || sequence.IsFinished)
            {
                return;
            }

            var advanced = sequence.NotifyAchieved();
            if (!advanced)
            {
                advanced = sequence.Tick(sequence.CurrentStep.TimeoutSeconds);
            }
            if (advanced)
            {
                OnStepEntered();
            }
        }
#endif
    }
}
