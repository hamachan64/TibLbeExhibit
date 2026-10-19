using System;
using Cysharp.Threading.Tasks;
using R3;
using Styly.NetSync;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 体験者の端末（TreasureHaulUserClient）。通信・時計を <see cref="SessionClientCore"/> につなぐだけの薄い層。
    /// 画面の表示は #42。ここではログだけ出す。
    /// </summary>
    public sealed class SessionClient : MonoBehaviour
    {
        [SerializeField] ThParamsAsset _defaults;
        [SerializeField, Tooltip("見た目・音・文言のまとめ役")] ThContentSet _content;

        public ThContentSet Content => _content;

        NetSyncManager _nm;
        SessionClientCore _core;

        public SessionClientCore Core => _core;

        static double Now => Time.realtimeSinceStartupAsDouble;

        void Start() => RunAsync(destroyCancellationToken).Forget();

        async UniTaskVoid RunAsync(System.Threading.CancellationToken ct)
        {
            try
            {
                if (_content != null) _content.LogWarnings("SessionClient");
                else Debug.LogWarning("[SessionClient] ThContentSet が未設定です");
                _nm = NetSyncManager.Instance;
                if (_nm == null) { Debug.LogError("[SessionClient] NetSyncManager がありません"); return; }

                await UniTask.WaitUntil(() => _nm.IsReady, cancellationToken: ct);
                int rpcLimit = _defaults.values.progress.rpcLimit;
                _nm.ConfigureRpcLimit(rpcLimit);

                // Environment シーン（ThVenue）の読み込みを待つ（配置候補点の数・荷台の範囲・カタログのハッシュに使う）
                // 待つ時間に上限は設けない（Environment が間に合わないと、その起動中ずっとお宝の設定が無くなるため）。10秒ごとに警告する
                for (int i = 0; ThVenue.Instance == null; i++)
                {
                    if (i > 0 && i % 100 == 0) Debug.LogWarning("[SessionClient] ThVenue を待っています（Environment シーンの読み込み待ち）");
                    await UniTask.Delay(100, cancellationToken: ct);
                }
                var venue = ThVenue.Instance;
                if (venue == null) Debug.LogWarning("[SessionClient] ThVenue が見つかりません（お宝は出ません）");
                var env = venue != null ? venue.CreateEnvironmentInfo() : null;
                var setup = venue != null && _content != null ? venue.CreateSetup(_content.treasures) : null;

                _core = new SessionClientCore(_nm.DeviceId, new RpcManagerTransport(), _defaults.values, env, Now, setup);
                _core.OwnClientNo = _nm.ClientNo;
                var t = _core.Tracker;

                foreach (var rpc in new[] { Rpc.ThState, Rpc.ThHeartbeat, Rpc.ThParams, Rpc.ThGrabbed, Rpc.ThGrabDenied, Rpc.ThFreed, Rpc.ThCollected, Rpc.ThItems, Rpc.ThPoses, Rpc.ThDamaged })
                {
                    var r = rpc;
                    RpcManager.Instance.AsObservable(r)
                        .Subscribe(d => _core.OnRpc(r, d.ClientNo, d.Parameters, Now)).AddTo(this);
                }

                t.State.Subscribe(s => Debug.Log($"[SessionClient] t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()} state={s} roundId={t.RoundId} seq={t.Seq} remaining={t.RemainingSeconds.CurrentValue:F2} participant={t.IsParticipant.CurrentValue} waitNext={t.WaitNextRound}")).AddTo(this);
                t.IsParticipant.Subscribe(p => Debug.Log($"[SessionClient] participant={p}")).AddTo(this);
                t.IsHostLost.Subscribe(l => Debug.Log($"[SessionClient] hostLost={l}")).AddTo(this);
                LogLoop(ct).Forget();

                _core.OnReady(Now);
                _core.Items.Subscribe(it => { if (it != null) Debug.Log($"[SessionClient] spawn fingerprint={_core.SpawnFingerprint} items={it.Items.Count}"); }).AddTo(this);
                Debug.Log($"[SessionClient] ready clientNo={_nm.ClientNo} deviceId={_nm.DeviceId} paramsVersion={t.LocalParamsVersion}");

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        _core.OwnClientNo = _nm.ClientNo;   // サーバーの再起動などで変わるので毎回読む
                        _core.Tick(Now);
                        if (_core.Params.progress.rpcLimit != rpcLimit)   // 採用したパラメータの値に合わせる（変わったときだけ）
                        {
                            rpcLimit = _core.Params.progress.rpcLimit;
                            _nm.ConfigureRpcLimit(rpcLimit);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
            catch (OperationCanceledException) { }
        }

        // 結合確認用: 約1秒ごとに残り秒を出す（ホストのログと突き合わせる）
        async UniTaskVoid LogLoop(System.Threading.CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var t = _core.Tracker;
                    if (t.State.CurrentValue != RoundState.Waiting)
                        Debug.Log($"[SessionClient] t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()} state={t.State.CurrentValue} remaining={t.RemainingSeconds.CurrentValue:F2}");
                    await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: ct);
                }
            }
            catch (OperationCanceledException) { }
        }

        void OnDestroy() => _core?.Dispose();
    }
}
