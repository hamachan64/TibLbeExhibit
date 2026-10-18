using System;
using System.Collections.Generic;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>端末の追従の中身。SessionClient（MonoBehaviour）は通信・時計をここにつなぐだけ。</summary>
    public sealed class SessionClientCore : IDisposable
    {
        readonly string _deviceId;
        readonly IThTransport _tx;
        readonly ThParams _defaults;
        readonly ThEnvironmentInfo _env;

        public ClientRoundTracker Tracker { get; private set; }
        public ThParams Params { get; private set; }
        int _ownClientNo = -1;
        /// <summary>自分の ClientNo。サーバーの再起動などで変わることがあり、変わったら状況報告を送り直す。</summary>
        public int OwnClientNo
        {
            get => _ownClientNo;
            set
            {
                if (_ownClientNo == value) return;
                bool changed = _ownClientNo > 0 && value > 0;
                _ownClientNo = value;
                if (changed) Tracker.RequestResync();
            }
        }

        public const int MaxWarnings = 20;
        readonly List<string> _warnings = new List<string>();
        public IReadOnlyList<string> Warnings => _warnings;

        void AddWarning(string w)
        {
            if (_warnings.Count < MaxWarnings && !_warnings.Contains(w)) _warnings.Add(w);
        }

        public SessionClientCore(string deviceId, IThTransport tx, ThParams defaults, ThEnvironmentInfo env, double now)
        {
            _deviceId = deviceId;
            _tx = tx;
            _defaults = defaults;
            _env = env;
            var r = ThParamsLoader.Load(defaults, null, env);
            Params = r.Params;
            Tracker = new ClientRoundTracker(deviceId, r.Params.progress, now) { LocalParamsVersion = r.Version };
        }

        public void Dispose() => Tracker.Dispose();

        /// <summary>NetSync の準備ができたとき 1 回。状況報告（ThResync）を送る。</summary>
        public void OnReady(double now) => SendResync(now);

        public void OnRpc(Rpc rpc, int senderClientNo, string[] args, double now)
        {
            if (senderClientNo == OwnClientNo) return;   // 自分が送った RPC の戻り
            switch (rpc)
            {
                case Rpc.ThState:
                    if (ThMessages.TryDecode(args, out long sr, out ThStateMsg st)) Tracker.OnState(sr, st, now);
                    break;
                case Rpc.ThHeartbeat:
                    if (ThMessages.TryDecode(args, out long hr, out ThHeartbeatMsg hb)) Tracker.OnHeartbeat(hr, hb, now);
                    break;
                case Rpc.ThParams:
                    if (ThMessages.TryDecode(args, out long _, out ThParamsMsg pm)) HandleParams(pm, now);
                    break;
            }
        }

        void HandleParams(ThParamsMsg pm, double now)
        {
            if (pm.targetClientNo != -1 && pm.targetClientNo != OwnClientNo) return;
            var r = ThParamsLoader.Load(_defaults, pm.json, _env);
            Params = r.Params;
            Tracker.UpdateTimes(r.Params.progress);
            Tracker.LocalParamsVersion = r.Version;
            foreach (var w in r.Warnings) AddWarning(w);
            if (r.Version != pm.version) AddWarning($"ホストの版 {pm.version} と採用した版 {r.Version} が違います");
            Tracker.RequestResync();   // 新しい版を報告する（間隔制限を通して Tick で送る）
        }

        public void Tick(double now)
        {
            Tracker.Tick(now);
            if (Tracker.TryTakeResync(now)) SendResync(now);
        }

        void SendResync(double now)
        {
            Tracker.NoteResyncSent(now);
            _tx.Send(Rpc.ThResync, ThMessages.Encode(Tracker.RoundId, new ThResyncMsg
            {
                deviceId = _deviceId,
                seq = Tracker.Seq,
                paramsVersion = Tracker.LocalParamsVersion,
                waitNextRound = Tracker.WaitNextRound,
            }));
        }
    }
}
