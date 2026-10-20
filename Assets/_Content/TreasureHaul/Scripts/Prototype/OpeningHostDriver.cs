#if UNITY_EDITOR
using System.Reflection;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Prototype
{
    /// <summary>
    /// 試作(#42 の結合確認): 運営PCのクローンを -batchmode で動かす補助。パラメータをメモリ上で短縮し（ファイルは置かない）、
    /// 端末が来たら 1 回目: 開始 → 練習で保留 → 解除 → 本編開始 → 回収 → 結果、2 回目: 開始 → 練習で中断、を決まった時刻で操作する。
    /// 本番のシーンには置かない（ProtoBatchRunner が実行時に追加する）。
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class OpeningHostDriver : MonoBehaviour
    {
        public float StartDelay = 8f;

        SessionHost _host;
        float _eligibleSince = -1f;
        int _round;               // 0 = 未開始、1 = 1 回目、2 = 2 回目
        float _stateEnteredAt;
        RoundState _lastState = RoundState.Waiting;
        bool _held, _released, _began, _collected, _aborted, _roundDone;

        void Awake()
        {
            _host = FindFirstObjectByType<SessionHost>();
            if (_host == null) { Debug.LogError("[OpeningHostDriver] SessionHost がありません"); return; }
            var f = typeof(SessionHost).GetField("_defaults", BindingFlags.NonPublic | BindingFlags.Instance);
            var asset = (ThParamsAsset)f.GetValue(_host);
            var p = asset.values;
            p.progress.openingSeconds = 10; p.progress.practiceSeconds = 14; p.progress.holdMaxSeconds = 6; p.progress.playingSeconds = 30; p.progress.resultSeconds = 12;
            p.layout.rows[0].targetAmount = 100;   // 回収 1 つ（S は 100G 以上）で届く
            var nm = FindFirstObjectByType<Styly.NetSync.NetSyncManager>();
            if (nm != null)
                typeof(Styly.NetSync.NetSyncManager).GetField("_serverAddress", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(nm, "localhost");
            Debug.Log("[OpeningHostDriver] params shortened (opening 10 / practice 14 / hold max 6 / playing 30 / result 12, target(1p)=100)");
        }

        void Update()
        {
            if (_host == null || _host.Core == null) return;
            var core = _host.Core;
            var st = core.Machine.State;
            float t = Time.realtimeSinceStartup;
            if (st != _lastState) { _lastState = st; _stateEnteredAt = t; Debug.Log($"[OpeningHostDriver] state={st} round={_round}"); }
            float inState = t - _stateEnteredAt;

            if (st == RoundState.Waiting)
            {
                if (_round < 2)
                {
                    if (_roundDone) { _roundDone = false; _eligibleSince = -1f; }
                    if (_round < 2 && core.EligibleCount.CurrentValue >= 1)
                    {
                        if (_eligibleSince < 0) _eligibleSince = t;
                        if (t - _eligibleSince >= (_round == 0 ? StartDelay : 8f) && _host.RequestStart()) { _round++; _held = _released = _began = _collected = _aborted = false; Debug.Log("[OpeningHostDriver] start requested"); }
                    }
                    else if (core.EligibleCount.CurrentValue < 1) _eligibleSince = -1f;
                }
                return;
            }
            _roundDone = true;

            if (_round == 1)
            {
                if (st == RoundState.Tutorial)
                {
                    if (!_held && inState > 2f) { _held = true; Debug.Log($"[OpeningHostDriver] hold -> {_host.RequestToggleHold()}"); }
                    if (_held && !_released && inState > 6f) { _released = true; Debug.Log($"[OpeningHostDriver] release -> {_host.RequestToggleHold()}"); }
                    if (_released && !_began && inState > 9f) { _began = true; Debug.Log($"[OpeningHostDriver] begin play -> {_host.RequestBeginPlay()}"); }
                }
                else if (st == RoundState.Playing && !_collected && inState > 6f)
                {
                    _collected = true;
                    var items = core.Items;
                    var dev = core.Machine.Participants.Count > 0 ? core.Machine.Participants[0] : null;
                    var info = dev != null ? core.Registry.Get(dev) : null;
                    if (items != null && info != null)
                    {
                        items.OnGrab(dev, info.ClientNo, new ThGrabMsg { item = 0, reqId = 1, gen = 0, kind = 0 });
                        items.OnRelease(dev, new ThReleaseMsg { item = 0, reqId = 1, gen = 1, reason = 1 });
                        items.OnCollect(dev, new ThCollectMsg { item = 0, gen = 1 });
                    }
                    Debug.Log($"[OpeningHostDriver] collected total={core.TotalAmount} target={core.TargetAmount}");
                }
            }
            else if (_round == 2 && st == RoundState.Tutorial && !_aborted && inState > 3f)
            {
                _aborted = true;
                Debug.Log($"[OpeningHostDriver] abort -> {_host.RequestAbort()}");
            }
        }
    }
}
#endif
