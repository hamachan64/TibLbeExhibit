using System.Reflection;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Prototype
{
    /// <summary>
    /// 試作(#38 の結合確認): 運営PCのクローンを -batchmode で動かすための補助。
    /// SessionHost より先に起動し、組み込みのパラメータをメモリ上で短縮する（ファイルは置かない・アセットは保存しない）。
    /// 参加できる端末が来て少し待ったら「開始」を 1 回だけ押す。本番のシーンには置かない（ProtoBatchRunner が実行時に追加する）。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GrabHostDriver : MonoBehaviour
    {
        public float Opening = 3f, Practice = 5f, Playing = 240f, Result = 6f, StartDelay = 3f;

        SessionHost _host;
        float _eligibleSince = -1f;
        bool _started;
        bool _sawRound;

        void Awake()
        {
            _host = FindFirstObjectByType<SessionHost>();
            if (_host == null) { Debug.LogError("[GrabHostDriver] SessionHost がありません"); return; }
            var f = typeof(SessionHost).GetField("_defaults", BindingFlags.NonPublic | BindingFlags.Instance);
            var asset = (ThParamsAsset)f.GetValue(_host);
            var p = asset.values.progress;
            p.openingSeconds = Opening; p.practiceSeconds = Practice; p.playingSeconds = Playing; p.resultSeconds = Result;
            // このマシンでは NetSync のサーバー自動探索が localhost を見つけられないことがあるので、アドレスを直接指定する
            var nm = FindFirstObjectByType<Styly.NetSync.NetSyncManager>();
            if (nm != null)
                typeof(Styly.NetSync.NetSyncManager).GetField("_serverAddress", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(nm, "localhost");
            Debug.Log($"[GrabHostDriver] params shortened opening={Opening} practice={Practice} playing={Playing} result={Result}");
        }

        // 結合確認: ホストも NetSync の端末なので、端末が送る ThPoses が別プロセスに届いていることをログで示す
        int _poseMsgs, _poseEntries;
        float _poseLogAt;
        Vector3 _lastPose;
        System.IDisposable _poseSub;

        void Start()
        {
            SubscribePoses(destroyCancellationToken).Forget();
        }

        async Cysharp.Threading.Tasks.UniTaskVoid SubscribePoses(System.Threading.CancellationToken ct)
        {
            try { while (RpcManager.Instance == null) await Cysharp.Threading.Tasks.UniTask.Yield(ct); }
            catch (System.OperationCanceledException) { return; }
            _poseSub = RpcManager.Instance.AsObservable(Rpc.ThPoses).Subscribe(d =>
            {
                if (!ThMessages.TryDecode(d.Parameters, out long _, out ThPosesMsg m) || m.poses == null) return;
                _poseMsgs++; _poseEntries += m.poses.Length;
                if (m.poses.Length > 0) _lastPose = m.poses[0].pos;
            });
        }

        void LogPoses()
        {
            if (Time.realtimeSinceStartup - _poseLogAt < 1f) return;
            _poseLogAt = Time.realtimeSinceStartup;
            if (_poseMsgs > 0) Debug.Log($"[GrabHostDriver] ThPoses received msgs/s={_poseMsgs} entries={_poseEntries} last={_lastPose.ToString("F2")}");
            _poseMsgs = 0; _poseEntries = 0;
        }

        void OnDestroy() => _poseSub?.Dispose();

        void Update()
        {
            LogPoses();
            if (_host == null || _host.Core == null) return;
            // 回が終わって待機に戻ったら、次の回を始められるようにする（確認を何度かやり直すため）
            if (_host.Core.Machine.State != RoundState.Waiting) _sawRound = true;
            if (_started && _sawRound && _host.Core.Machine.State == RoundState.Waiting) { _started = false; _sawRound = false; _eligibleSince = -1f; }
            if (_started) return;
            if (_host.Core.Machine.State != RoundState.Waiting) return;
            if (_host.Core.EligibleCount.CurrentValue >= 1)
            {
                if (_eligibleSince < 0) _eligibleSince = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - _eligibleSince >= StartDelay && _host.RequestStart())
                {
                    _started = true;
                    Debug.Log("[GrabHostDriver] start requested");
                }
            }
            else _eligibleSince = -1f;
        }
    }
}
