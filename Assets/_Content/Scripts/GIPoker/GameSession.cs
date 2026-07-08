using System.Threading;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>ゲームセッションの状態</summary>
    public enum SessionState
    {
        /// <summary>開始待ち（オペレーターの開始トリガー待機）</summary>
        WaitingForStart,
        /// <summary>プレイ中（5分カウントダウン進行）</summary>
        Playing,
        /// <summary>終了（リザルト表示）</summary>
        Finished,
    }

    /// <summary>
    /// 1セッション（5分間）の状態と制限時間を管理するマネージャー。
    ///
    /// - オペレーターの ContentStart RPC（STYLY NetSync 経由）で開始する
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

        /// <summary>ContentStart RPCにシードが含まれていなかった場合の共通フォールバック値</summary>
        private const int FallbackSeed = 20260708;

        private readonly ReactiveProperty<SessionState> state = new(SessionState.WaitingForStart);
        private readonly ReactiveProperty<float> remainingTime = new(0f);

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

        public float SessionDurationSeconds => sessionDurationSeconds;

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
        }

        /// <summary>ローカル起点でセッションを開始する（エディタ自動開始・単体テスト用）</summary>
        public void StartSession()
        {
            StartSession(FallbackSeed);
        }

        /// <summary>セッションを開始する（多重開始は無視される）</summary>
        public void StartSession(int seed)
        {
            if (state.Value == SessionState.Playing)
            {
                return;
            }
            SessionSeed = seed;
            sessionCts?.Cancel();
            sessionCts?.Dispose();
            sessionCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);

            remainingTime.Value = sessionDurationSeconds;
            state.Value = SessionState.Playing;
            RunSessionAsync(sessionCts.Token).Forget();
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
