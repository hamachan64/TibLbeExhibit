using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template
{
    /// <summary>ゲームセッションの状態</summary>
    public enum SessionState
    {
        /// <summary>開始待ち（オペレーターの開始トリガー待機）</summary>
        WaitingForStart,
        /// <summary>チュートリアル中（操作説明・練習。固定時間の経過で Playing へ移行）</summary>
        Tutorial,
        /// <summary>プレイ中（5分カウントダウン進行）</summary>
        Playing,
        /// <summary>終了（リザルト表示）</summary>
        Finished,
    }

    /// <summary>
    /// 1セッション（5分間）の状態と制限時間を管理するマネージャー。
    ///
    /// 状態遷移: WaitingForStart → (ContentStart RPC / StartSession) → Tutorial
    ///           → (固定時間経過) → Playing → Finished
    ///
    /// - オペレーターの ContentStart RPC（STYLY NetSync 経由）で開始する
    /// - 開始後はまず固定時間のチュートリアル（Tutorial）を経て本編（Playing）に入る。
    ///   チュートリアルの長さを進行度に依らず固定にしているのは、ContentStart が
    ///   全クライアント同報のため、全員の Playing 開始時刻（＝シード付きアイテム
    ///   スポーンの開始時刻）を揃える必要があるため
    /// - skipTutorial でチュートリアルなしの従来フローに戻せる（開発・デバッグ用）
    /// - エディタでは autoStartInEditor でRPCなしの自動開始も可能
    /// - 状態・残り時間は R3 の ReactiveProperty として公開し、
    ///   スポーンマネージャーやUIが購読する
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class GameSession : MonoBehaviour
    {
        public static GameSession Instance { get; private set; }

        [SerializeField, Tooltip("セッションの制限時間（秒）")]
        private float sessionDurationSeconds = 300f;

        [SerializeField, Tooltip("エディタ実行時、この秒数後に自動でセッションを開始する（0以下で無効）")]
        private float autoStartInEditorDelay = 3f;

        [SerializeField, Tooltip("チュートリアルを飛ばして直接プレイを開始する（開発・デバッグ用の後方互換）")]
        private bool skipTutorial = false;

        [SerializeField, Tooltip("チュートリアルの固定時間（秒）。全クライアントの Playing 開始時刻を揃えるため、進行度に関わらずこの時間で本編へ移行する")]
        private float tutorialDurationSeconds = 60f;

        /// <summary>ContentStart RPCにシードが含まれていなかった場合の共通フォールバック値</summary>
        private const int FallbackSeed = 20260708;

        private readonly ReactiveProperty<SessionState> state = new(SessionState.WaitingForStart);
        private readonly ReactiveProperty<float> remainingTime = new(0f);
        private readonly ReactiveProperty<float> tutorialRemainingTime = new(0f);

        private CancellationTokenSource sessionCts;

        /// <summary>
        /// このセッションのスポーン用乱数シード。
        /// 全クライアントが同じ値を共有し、アイテム配置を決定論的に一致させる。
        /// </summary>
        public int SessionSeed { get; private set; } = FallbackSeed;

        /// <summary>現在のセッション状態</summary>
        public ReadOnlyReactiveProperty<SessionState> State => state;

        /// <summary>セッションの残り時間（秒）</summary>
        public ReadOnlyReactiveProperty<float> RemainingTime => remainingTime;

        /// <summary>チュートリアルの残り時間（秒）。TutorialView がカウントダウン表示に使う</summary>
        public ReadOnlyReactiveProperty<float> TutorialRemainingTime => tutorialRemainingTime;

        public float SessionDurationSeconds => sessionDurationSeconds;

        public float TutorialDurationSeconds => tutorialDurationSeconds;

        private void Awake()
        {
            Instance = this;
            remainingTime.Value = sessionDurationSeconds;
        }

        private void Start()
        {
            // オペレーターからの開始通知（NetSync RPC）を購読する
            // 引数[0]にスポーン用シードが載る（旧形式の引数なし送信はフォールバック値で全員一致させる）
            if (RpcManager.Instance != null)
            {
                RpcManager.Instance.AsObservable(Rpc.ContentStart)
                    .Subscribe(data =>
                    {
                        var seed = data.Parameters != null
                                   && data.Parameters.Length > 0
                                   && int.TryParse(data.Parameters[0], out var s)
                            ? s
                            : FallbackSeed;
                        StartSession(seed);
                    })
                    .AddTo(this);
            }
            else
            {
                Debug.LogWarning("[GameSession] RpcManager が見つかりません（単一シーンテスト中は正常）");
            }

#if UNITY_EDITOR
            if (autoStartInEditorDelay > 0f)
            {
                AutoStartAsync(destroyCancellationToken).Forget();
            }
#endif
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            sessionCts?.Cancel();
            sessionCts?.Dispose();
            state.Dispose();
            remainingTime.Dispose();
            tutorialRemainingTime.Dispose();
        }

        /// <summary>ローカル起点でセッションを開始する（エディタ自動開始・単体テスト用）</summary>
        public void StartSession()
        {
            StartSession(FallbackSeed);
        }

        /// <summary>
        /// セッションを開始する（WaitingForStart / Finished からのみ有効。
        /// Tutorial / Playing 中の多重開始要求は無視される）。
        /// skipTutorial が false ならまず Tutorial へ、true なら従来どおり直接 Playing へ遷移する。
        /// </summary>
        public void StartSession(int seed)
        {
            if (state.Value == SessionState.Tutorial || state.Value == SessionState.Playing)
            {
                return;
            }
            SessionSeed = seed;
            RestartSessionCts();
            remainingTime.Value = sessionDurationSeconds;

            if (skipTutorial)
            {
                // 従来フロー: チュートリアルなしで直接プレイ開始（開発・デバッグ用）
                BeginPlaying();
                return;
            }

            tutorialRemainingTime.Value = tutorialDurationSeconds;
            state.Value = SessionState.Tutorial;
            RunTutorialAsync(sessionCts.Token).Forget();
        }

        /// <summary>
        /// チュートリアルを打ち切って即プレイを開始する（Tutorial 中のみ有効。テスト・デバッグ用）。
        /// 既定フローでは呼ばない（全クライアントの Playing 開始時刻を固定時間で揃えるため）。
        /// </summary>
        public void CompleteTutorial()
        {
            if (state.Value != SessionState.Tutorial)
            {
                return;
            }
            RestartSessionCts(); // 走行中のチュートリアルタイマーをキャンセルする
            tutorialRemainingTime.Value = 0f;
            BeginPlaying();
        }

        /// <summary>Playing へ遷移して5分タイマーを開始する</summary>
        private void BeginPlaying()
        {
            remainingTime.Value = sessionDurationSeconds;
            state.Value = SessionState.Playing;
            RunSessionAsync(sessionCts.Token).Forget();
        }

        private async UniTask RunTutorialAsync(CancellationToken ct)
        {
            while (tutorialRemainingTime.Value > 0f)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                tutorialRemainingTime.Value = Mathf.Max(0f, tutorialRemainingTime.Value - Time.deltaTime);
            }
            BeginPlaying();
        }

        private async UniTask RunSessionAsync(CancellationToken ct)
        {
            while (remainingTime.Value > 0f)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                remainingTime.Value = Mathf.Max(0f, remainingTime.Value - Time.deltaTime);
            }
            state.Value = SessionState.Finished;
        }

        private void RestartSessionCts()
        {
            sessionCts?.Cancel();
            sessionCts?.Dispose();
            sessionCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
        }

#if UNITY_EDITOR
        private async UniTask AutoStartAsync(CancellationToken ct)
        {
            await UniTask.Delay(System.TimeSpan.FromSeconds(autoStartInEditorDelay), cancellationToken: ct);
            if (state.Value == SessionState.WaitingForStart)
            {
                Debug.Log("[GameSession] エディタ自動開始");
                StartSession();
            }
        }
#endif
    }
}
