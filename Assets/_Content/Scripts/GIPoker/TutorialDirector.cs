using System;
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
    /// - ステップのタイムアウト自動送りにより、達成できない体験者がいても詰まらない
    /// </summary>
    public class TutorialDirector : MonoBehaviour
    {
        [SerializeField, Tooltip("練習アイテムを出す位置: プレイヤー正面からの距離（m）")]
        private float itemDistance = 0.45f;

        [SerializeField, Tooltip("練習アイテムを出す位置: 頭からどれだけ下か（m）")]
        private float itemHeightBelowHead = 0.15f;

        private GameSession session;
        private CardBinder binder;
        private TutorialSequence sequence;
        private TutorialView view;
        private TutorialPracticeItem practiceItem1; // カード化→掴む→収納の練習用
        private TutorialPracticeItem practiceItem2; // ゲイン（引き裂き）の練習用
        private IDisposable tickSubscription;

        /// <summary>チュートリアルが進行中か（撤去処理を1回だけ走らせるためのガード）</summary>
        private bool tutorialActive;

        // 練習アイテムの到達状況（一度達成したら true のままの単調フラグ）
        private bool practiceCardified;
        private bool practiceHeld;
        private bool practiceStored;
        private bool practiceGained;

        /// <summary>GIPokerBootstrap から結線する</summary>
        public void Init(GameSession gameSession, CardBinder cardBinder)
        {
            session = gameSession;
            binder = cardBinder;

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
        }

        private void StartTutorial()
        {
            if (tutorialActive)
            {
                return;
            }
            tutorialActive = true;
            practiceCardified = false;
            practiceHeld = false;
            practiceStored = false;
            practiceGained = false;

            sequence = new TutorialSequence(TutorialSequence.CreateDefaultSteps());

            var viewGo = new GameObject("GIPoker_TutorialView");
            view = viewGo.AddComponent<TutorialView>();
            view.Init(session, sequence.Steps.Count);

            tickSubscription = Observable.EveryUpdate()
                .Subscribe(_ => TickSequence())
                .AddTo(this);

            OnStepEntered();
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
                case TutorialStepKind.Grab:
                    // 触れられずタイムアウトした場合でも掴む練習ができるよう強制カード化する
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

        /// <summary>現在のステップの達成条件を評価し、満たしていれば次のステップへ進める</summary>
        private void EvaluateAchievement()
        {
            var step = sequence?.CurrentStep;
            if (step == null || !step.RequiresAchievement)
            {
                return;
            }

            var achieved = step.Kind switch
            {
                TutorialStepKind.Cardify => practiceCardified,
                TutorialStepKind.Grab => practiceHeld,
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
                    practiceHeld = true;
                    break;
                case CardizableState.Stored:
                    practiceCardified = true;
                    practiceHeld = true;
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
    }
}
