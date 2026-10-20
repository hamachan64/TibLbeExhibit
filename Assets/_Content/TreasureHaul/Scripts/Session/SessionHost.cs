using System;
using Cysharp.Threading.Tasks;
using R3;
using Styly.NetSync;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 運営PC（TreasureHaulOperatorClient）。通信・時計・ファイルを <see cref="SessionHostCore"/> につなぐだけの薄い層。
    /// </summary>
    public sealed class SessionHost : MonoBehaviour
    {
        [SerializeField] ThParamsAsset _defaults;
        [SerializeField, Tooltip("見た目・音・文言のまとめ役")] ThContentSet _content;

        public ThContentSet Content => _content;

        ThEnvironmentInfo _env;   // ThVenue から取る（Environment シーンの読み込み後。TutorialSlot の数は #43）
        string _dataDir;
        NetSyncManager _nm;

        public SessionHostCore Core { get; private set; }

        static double Now => Time.realtimeSinceStartupAsDouble;

        void Awake()
        {
            _dataDir = Application.persistentDataPath;
            if (_content != null) _content.LogWarnings("SessionHost");
            else Debug.LogWarning("[SessionHost] ThContentSet が未設定です");
            var p = LoadParams(out var warnings);
            var settings = LoadSettings(out var settingsWarnings);
            Core = new SessionHostCore(new RpcManagerTransport(), p, settings, settingsWarnings, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                no => _nm != null ? _nm.GetDeviceIdByClientNo(no) : null, null,
                local => ThPlaceSolver.Solve(ThVenue.Instance != null ? ThVenue.Instance.Root : null, local));   // 持ち主の切断で降ろす先（Environment の床・荷台・瓦礫への Raycast）
            foreach (var w in Core.Warnings.CurrentValue) Debug.LogWarning("[SessionHost] " + w);
        }

        ThParamsLoadResult LoadParams(out string warning)
        {
            var text = ThSettingsFiles.ReadTextOrNull(ThSettingsFiles.ParamsPath(_dataDir), out warning);
            var r = ThParamsLoader.Load(_defaults.values, text, _env);
            if (warning != null) r.Warnings.Add(warning);
            return r;
        }

        ThGameSettings LoadSettings(out string[] warnings)
        {
            var s = ThSettingsFiles.LoadSettings(_dataDir, out var w);
            warnings = w == null ? new string[0] : new[] { w };
            return s;
        }

        void Start() => RunAsync(destroyCancellationToken).Forget();

        async UniTaskVoid RunAsync(System.Threading.CancellationToken ct)
        {
            try
            {
                _nm = NetSyncManager.Instance;
                if (_nm == null) { Debug.LogError("[SessionHost] NetSyncManager がありません"); return; }

                await UniTask.WaitUntil(() => _nm.IsReady, cancellationToken: ct);
                _nm.ConfigureRpcLimit(Core.Params.progress.rpcLimit);
                Core.OwnClientNo = _nm.ClientNo;

                // Environment シーン（ThVenue）の読み込みを待つ。配置候補点の数・荷台の範囲・カタログのハッシュを渡し、パラメータを読み直す
                // 待つ時間に上限は設けない（Environment が間に合わないと、その起動中ずっとお宝の設定が無くなるため）。10秒ごとに警告する
                for (int i = 0; ThVenue.Instance == null; i++)
                {
                    if (i > 0 && i % 100 == 0) Debug.LogWarning("[SessionHost] ThVenue を待っています（Environment シーンの読み込み待ち）");
                    await UniTask.Delay(100, cancellationToken: ct);
                }
                var venue = ThVenue.Instance;
                if (venue == null) Debug.LogWarning("[SessionHost] ThVenue が見つかりません（お宝は出ません）");
                else
                {
                    _env = venue.CreateEnvironmentInfo();
                    Core.SetTreasureSetup(venue.CreateSetup(_content != null ? _content.treasures : null), Now);
                    RequestReloadParams();
                    Debug.Log($"[SessionHost] venue points={_env.PlacementPointCount} catalogHash={Core.Registry.HostCatalogHash}");
                }
                Debug.Log($"[SessionHost] ready clientNo={_nm.ClientNo} deviceId={_nm.DeviceId} paramsVersion={Core.ParamsVersion}");

                // 端末の登録は ThResync で行う（ステルスの運営PC・クローンを数えない）。切断だけ NetSync のイベントを使う。
                foreach (var rpc in new[] { Rpc.ThResync, Rpc.ThGrab, Rpc.ThLift, Rpc.ThRelease, Rpc.ThRest, Rpc.ThCollect, Rpc.ThDamage, Rpc.ThPoses })
                {
                    var r = rpc;
                    RpcManager.Instance.AsObservable(r)
                        .Subscribe(d => Core.OnRpc(r, d.ClientNo, d.Parameters, Now)).AddTo(this);
                }
                _nm.OnAvatarDisconnected.AsObservable()
                    .Subscribe(no => Core.OnClientDisconnected(no, Now)).AddTo(this);

                Core.State.Subscribe(s => Debug.Log($"[SessionHost] state={s} roundId={Core.Machine.RoundId} seq={Core.Machine.Seq} participants={string.Join("/", Core.Machine.Participants)} spawn={Core.SpawnFingerprint} items={Core.Items?.Items.Count ?? 0} total={Core.TotalAmount}")).AddTo(this);
                LogLoop(ct).Forget();

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        Core.OwnClientNo = _nm.ClientNo;   // 再接続で変わるので毎回読む
                        Core.Tick(Now);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);   // Heartbeat が黙って止まらないように、続ける
                    }
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
            catch (OperationCanceledException) { }
        }

        // 結合確認用: 約1秒ごとに残り秒を出す（端末のログと突き合わせる）
        async UniTaskVoid LogLoop(System.Threading.CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (Core.Machine.State != RoundState.Waiting)
                        Debug.Log($"[SessionHost] t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()} state={Core.Machine.State} remaining={Core.Machine.RemainingSeconds(Now):F2}");
                    await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: ct);
                }
            }
            catch (OperationCanceledException) { }
        }

        void OnDestroy() => Core?.Dispose();

        // ---- 運営UIから ----

        public bool RequestStart() =>
            Core != null && _nm != null && _nm.IsReady
            && Core.Start(Now, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), UnityEngine.Random.Range(int.MinValue, int.MaxValue));

        public bool RequestAbort() => Core != null && Core.Abort(Now);

        /// <summary>待機中だけ。パラメータとゲーム設定のファイルを読み直す。</summary>
        public bool RequestReloadParams()
        {
            if (Core == null || Core.Machine.State != RoundState.Waiting) return false;
            var p = LoadParams(out _);
            var s = LoadSettings(out var sw);
            bool ok = Core.ReloadParams(p, s, sw, Now);
            foreach (var w in Core.Warnings.CurrentValue) Debug.LogWarning("[SessionHost] " + w);
            Debug.Log($"[SessionHost] params reloaded version={Core.ParamsVersion}");
            return ok;
        }

        public void SaveSettings(ThGameSettings settings) => ThSettingsFiles.SaveSettings(_dataDir, settings);
    }
}
