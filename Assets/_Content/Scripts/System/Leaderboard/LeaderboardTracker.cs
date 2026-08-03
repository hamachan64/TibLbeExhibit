using System;
using System.Collections.Generic;
using System.Linq;
using R3;
using Styly.NetSync;
using UnityEngine;

namespace STYLY.TIB_Template.Leaderboard
{
    /// <summary>
    /// OperatorClient.unity に配置するシングルトン。
    /// 「今セッションのトップ3」「本日通算のトップ3」「残り時間」を追跡し、
    /// ScoreMonitorView が購読する ReactiveProperty として公開する。
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class LeaderboardTracker : MonoBehaviour
    {
        public static LeaderboardTracker Instance { get; private set; }

        private const string SaveFileName = "gi_poker_daily_leaderboard.json";
        private const int Top3Count = 3;

        /// <summary>接続中クライアントのスコア（切断されたら追跡から外す＝今セッションTOP3の絞り込み対象外）</summary>
        private readonly Dictionary<int, int> connectedScores = new();

        private readonly ReactiveProperty<IReadOnlyList<LeaderboardEntry>> liveTop3 =
            new(Array.Empty<LeaderboardEntry>());

        private readonly ReactiveProperty<IReadOnlyList<LeaderboardEntry>> dailyTop3 =
            new(Array.Empty<LeaderboardEntry>());

        private readonly ReactiveProperty<float> remainingTime = new(0f);

        private DailyLeaderboardStore store;
        private DailyLeaderboard dailyLeaderboard;

        /// <summary>今セッションのトップ3（接続中の全クライアント対象、絞り込みなし）</summary>
        public ReadOnlyReactiveProperty<IReadOnlyList<LeaderboardEntry>> LiveTop3 => liveTop3;

        /// <summary>本日通算のトップ3（セッション終了時の最終スコアを記録、永続化される）</summary>
        public ReadOnlyReactiveProperty<IReadOnlyList<LeaderboardEntry>> DailyTop3 => dailyTop3;

        /// <summary>セッションの残り時間（秒）。複数クライアントから届くが直近の値をそのまま採用する</summary>
        public ReadOnlyReactiveProperty<float> RemainingTime => remainingTime;

        private void Awake()
        {
            Instance = this;

            var path = System.IO.Path.Combine(Application.persistentDataPath, SaveFileName);
            store = new DailyLeaderboardStore(path);
            dailyLeaderboard = store.Load(DateTime.Now);

            // 起動時点で既に日付が変わっていたら、最初のセッション終了を待たずにリセットしておく
            if (dailyLeaderboard.NeedsReset(DateTime.Now))
            {
                dailyLeaderboard.Reset(DateTime.Now);
                store.Save(dailyLeaderboard);
            }

            dailyTop3.Value = dailyLeaderboard.Top3.ToArray();
        }

        private void Start()
        {
            if (NetSyncManager.Instance == null || VariableManager.Instance == null || RpcManager.Instance == null)
            {
                Debug.LogWarning("[LeaderboardTracker] NetSync 関連のマネージャーが見つからないため動作しません（単体シーンテスト中は正常）");
                return;
            }

            // OnAvatarConnected の購読を張るより前から接続済みだったクライアント（例: OperatorClient再起動時に
            // 既に接続中の UserClient）は以後 OnAvatarConnected を受け取れず集計から漏れてしまうため、
            // 購読開始前に現在の接続済みクライアント一覧で connectedScores をシードしておく。
            SeedAlreadyConnectedClients();

            // 接続中クライアントの追跡（サードパーティAPI＝UnityEvent<int> の受け口を Observable.Create でR3化する）
            AvatarConnectedAsObservable(NetSyncManager.Instance)
                .Subscribe(clientNo =>
                {
                    // AsObservableOnChanged は変更時のみ流れるため、接続時点の現在値を明示的に補完する
                    var initialScore = VariableManager.Instance.Get(UserVariable.GiScore, clientNo, 0);
                    connectedScores[clientNo] = initialScore;
                    RecomputeLiveTop3();
                })
                .AddTo(this);

            AvatarDisconnectedAsObservable(NetSyncManager.Instance)
                .Subscribe(clientNo =>
                {
                    connectedScores.Remove(clientNo);
                    RecomputeLiveTop3();
                })
                .AddTo(this);

            VariableManager.Instance.AsObservableOnChanged<int>(UserVariable.GiScore)
                .Subscribe(data =>
                {
                    if (!connectedScores.ContainsKey(data.ClientNo))
                    {
                        return; // 切断済み等、追跡対象外のクライアントの値は無視する
                    }
                    connectedScores[data.ClientNo] = data.Value;
                    RecomputeLiveTop3();
                })
                .AddTo(this);

            VariableManager.Instance.AsObservableOnChanged<float>(UserVariable.GiRemainingTime)
                .Subscribe(data => remainingTime.Value = data.Value)
                .AddTo(this);

            RpcManager.Instance.AsObservable(Rpc.GiSessionFinished)
                .Subscribe(data =>
                {
                    if (data.Parameters == null
                        || data.Parameters.Length == 0
                        || !int.TryParse(data.Parameters[0], out var finalScore))
                    {
                        return;
                    }
                    RecordDailyResult(data.ClientNo, finalScore);
                })
                .AddTo(this);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void RecordDailyResult(int clientNo, int finalScore)
        {
            var now = DateTime.Now;
            if (dailyLeaderboard.NeedsReset(now))
            {
                dailyLeaderboard.Reset(now);
            }
            dailyLeaderboard.Record(clientNo, finalScore);
            store.Save(dailyLeaderboard);
            dailyTop3.Value = dailyLeaderboard.Top3.ToArray();
        }

        /// <summary>
        /// OnAvatarConnected の購読開始前から接続済みのクライアントを connectedScores に取り込む。
        /// GetAliveClients はステルスクライアントも含めて取得し（絞り込みなしの要件に合わせる）、
        /// 各クライアントの現在の GiScore を明示的に取得してシードする。
        /// </summary>
        private void SeedAlreadyConnectedClients()
        {
            var aliveClients = NetSyncManager.Instance.GetAliveClients(includeStealthClients: true);
            foreach (var clientNo in aliveClients)
            {
                connectedScores[clientNo] = VariableManager.Instance.Get(UserVariable.GiScore, clientNo, 0);
            }
            RecomputeLiveTop3();
        }

        private void RecomputeLiveTop3()
        {
            liveTop3.Value = connectedScores
                .Select(kv => new LeaderboardEntry(kv.Key, kv.Value))
                .OrderByDescending(e => e.Score)
                .ThenBy(e => e.ClientNo)
                .Take(Top3Count)
                .ToArray();
        }

        /// <summary>NetSyncManager.OnAvatarConnected（UnityEvent&lt;int&gt;）をR3のObservableへラップする</summary>
        private static Observable<int> AvatarConnectedAsObservable(NetSyncManager manager)
        {
            return Observable.Create<int>(observer =>
            {
                void Handler(int clientNo) => observer.OnNext(clientNo);
                manager.OnAvatarConnected.AddListener(Handler);
                return Disposable.Create(() => manager.OnAvatarConnected.RemoveListener(Handler));
            });
        }

        /// <summary>NetSyncManager.OnAvatarDisconnected（UnityEvent&lt;int&gt;）をR3のObservableへラップする</summary>
        private static Observable<int> AvatarDisconnectedAsObservable(NetSyncManager manager)
        {
            return Observable.Create<int>(observer =>
            {
                void Handler(int clientNo) => observer.OnNext(clientNo);
                manager.OnAvatarDisconnected.AddListener(Handler);
                return Disposable.Create(() => manager.OnAvatarDisconnected.RemoveListener(Handler));
            });
        }
    }
}
