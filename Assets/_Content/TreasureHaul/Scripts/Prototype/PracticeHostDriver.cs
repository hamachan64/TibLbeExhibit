#if UNITY_EDITOR
using System.Reflection;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Prototype
{
    /// <summary>
    /// 試作(#43 の結合確認): 運営PCのクローンを -batchmode で動かす補助。パラメータをメモリ上で短縮し（ファイルは置かない）、
    /// 端末が <see cref="MinDevices"/> 台そろって <see cref="StartDelay"/> 秒たったら開始する。練習を長くして、人が（ツールで）練習の操作を確かめる時間を取る。
    /// 本番のシーンには置かない（ProtoBatchRunner が実行時に追加する）。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class PracticeHostDriver : MonoBehaviour
    {
        public float StartDelay = 6f;
        public int MinDevices = 1;
        public float PracticeSeconds = 100f;

        SessionHost _host;
        float _eligibleSince = -1f;
        bool _started, _everLeftWaiting;
        RoundState _lastState = RoundState.Waiting;
        float _lastLog;

        void Awake()
        {
            _host = FindFirstObjectByType<SessionHost>();
            if (_host == null) { Debug.LogError("[PracticeHostDriver] SessionHost がありません"); return; }
            var f = typeof(SessionHost).GetField("_defaults", BindingFlags.NonPublic | BindingFlags.Instance);
            var asset = (ThParamsAsset)f.GetValue(_host);
            var p = asset.values;
            p.progress.openingSeconds = 3; p.progress.practiceSeconds = PracticeSeconds; p.progress.holdMaxSeconds = 0; p.progress.playingSeconds = 25; p.progress.resultSeconds = 10;
            var nm = FindFirstObjectByType<Styly.NetSync.NetSyncManager>();
            if (nm != null)
                typeof(Styly.NetSync.NetSyncManager).GetField("_serverAddress", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(nm, "localhost");
            Debug.Log($"[PracticeHostDriver] params shortened (opening 3 / practice {PracticeSeconds} / playing 25 / result 10)");
        }

        void Update()
        {
            if (_host == null || _host.Core == null) return;
            var core = _host.Core;
            var st = core.Machine.State;
            float t = Time.realtimeSinceStartup;
            if (st != _lastState)
            {
                _lastState = st;
                Debug.Log($"[PracticeHostDriver] state={st} colors={string.Join(",", System.Linq.Enumerable.Select(core.ColorAssignments, kv => kv.Key + "=" + kv.Value))} practiceItems={core.PracticeItems?.Items.Count ?? -1} items={core.Items?.Items.Count ?? -1} done={string.Join(",", System.Linq.Enumerable.Select(core.PracticeCompletedAt, kv => kv.Key + "@" + kv.Value.ToString("F1")))}");
            }
            if (st == RoundState.Waiting && _started && _everLeftWaiting) { _started = false; _eligibleSince = -1f; _everLeftWaiting = false; }   // 何度でも回せる
            if (st != RoundState.Waiting) _everLeftWaiting = true;
            if (st == RoundState.Waiting && !_started)
            {
                if (core.EligibleCount.CurrentValue >= MinDevices)
                {
                    if (_eligibleSince < 0) _eligibleSince = t;
                    if (t - _eligibleSince >= StartDelay && _host.RequestStart()) { _started = true; Debug.Log("[PracticeHostDriver] start requested"); }
                }
                else _eligibleSince = -1f;
            }
            if (st == RoundState.Tutorial && t - _lastLog > 3f && core.PracticeItems != null)
            {
                _lastLog = t;
                var sb = new System.Text.StringBuilder();
                foreach (var it in core.PracticeItems.Items) sb.Append($"[{it.Index} {it.State} owner={it.OwnerClientNo} gone={it.Gone} rev={it.Revealed}] ");
                Debug.Log($"[PracticeHostDriver] practice {sb} done={core.PracticeCompletedAt.Count} total={core.TotalAmount}");
            }
        }
    }
}
#endif
