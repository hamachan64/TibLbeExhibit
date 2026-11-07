#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Prototype
{
    /// <summary>
    /// 試作（遺跡の亡霊の PlayMode 確認用）: NetSync なしで、1 つのエディタの中に運営PC（SessionHostCore）と端末（SessionClientCore）を
    /// メモリ上の通信でつなぎ、端末側を SessionClient に差し込む。頭（XR Rig）を亡霊の視界・壁の裏へ動かして、見つかる・罰を確かめる。
    /// 本番のシーンには置かない（uloop で実行時に追加する）。何も保存しない。
    /// </summary>
    public sealed class PhantomPlayDriver : MonoBehaviour
    {
        public ThPhantomMode Mode = ThPhantomMode.TakeTreasure;
        public string ScreenshotDir = "";
        public float PlayingSeconds = 120f;
        [Tooltip("2 人参加（もう 1 台 devB を足す）。罰 B でも結果へ進まず、本人の大表示・仲間への知らせを見られる")] public bool TwoPlayers;
        [Tooltip("2 人参加のとき、画面に映す端末を devB（仲間）にする。devA が見つかったと報告し、仲間への知らせを見る")] public bool ViewerIsPeer;

        sealed class Node : IThTransport
        {
            public readonly Queue<(Rpc rpc, string[] args)> Out = new Queue<(Rpc, string[])>();
            public void Send(Rpc rpc, string[] args) => Out.Enqueue((rpc, args));
        }

        readonly Node _hostNode = new Node(), _clientNode = new Node(), _otherNode = new Node();
        SessionHostCore _host;
        SessionClientCore _client;   // 画面に映す端末（SessionClient に差し込む）
        SessionClientCore _other;    // もう 1 台（TwoPlayers のとき）
        SessionClientCore _devA;     // devA（頭で見つかる、または見つかったと報告する端末）
        SessionClient _session;
        PhantomClient _phantom;
        Transform _rig;
        Vector3? _headWanted;   // VenueRoot ローカル
        public readonly List<string> Log = new List<string>();

        static double Now => Time.realtimeSinceStartupAsDouble;

        void L(string s) { Log.Add(s); Debug.Log("[PhantomPlayDriver] " + s); }

        async UniTaskVoid Start()
        {
            var ct = destroyCancellationToken;
            try { await Run(ct); }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogException(e); }
        }

        async UniTask Run(System.Threading.CancellationToken ct)
        {
            _session = FindFirstObjectByType<SessionClient>();
            _phantom = FindFirstObjectByType<PhantomClient>();
            while (ThVenue.Instance == null) await UniTask.Yield(ct);
            var venue = ThVenue.Instance;
            var defaults = (ThParamsAsset)typeof(SessionClient).GetField("_defaults", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_session);
            var p = defaults.values.Clone();
            p.progress.openingSeconds = 1; p.progress.practiceSeconds = 1; p.progress.playingSeconds = PlayingSeconds; p.progress.resultSeconds = 30;
            p.operation.autoConfirmAlignment = true;
            var env = venue.CreateEnvironmentInfo();
            var setup = venue.CreateSetup(_session.Content.treasures);
            _host = new SessionHostCore(_hostNode, ThParamsLoader.Load(p, null, env), new ThGameSettings { phantom = Mode }, null, 0,
                no => no == 2 ? "devA" : no == 3 ? "devB" : null, setup, local => ThPlaceSolver.Solve(venue.Root, local)) { OwnClientNo = 1 };
            var a = new SessionClientCore("devA", _clientNode, p, env, Now, setup) { OwnClientNo = 2 };
            _devA = a;
            SessionClientCore b = null;
            if (TwoPlayers) b = new SessionClientCore("devB", _otherNode, p, env, Now, setup) { OwnClientNo = 3 };
            _client = ViewerIsPeer && b != null ? b : a;
            _other = _client == a ? b : a;
            typeof(SessionClient).GetField("_core", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_session, _client);
            _client.PhantomPunished.Subscribe(m => L($"punish mode={m.mode} device={m.device} items={m.items.Length} lost={m.lost}"));
            _client.OnReady(Now);
            _other?.OnReady(Now);
            var go = GameObject.Find("STYLY XR Rig");
            _rig = go != null ? go.transform : null;
            L($"ready mode={Mode} rig={(_rig != null)} root={venue.Root.position}");

            await Pump(ct, 0.5f);
            if (!_host.Start(Now, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 1234)) { L("start failed"); return; }
            while (_host.Machine.State != RoundState.Playing) await Pump(ct, 0.1f);
            L($"playing items={_host.Items?.Items.Count} clientItems={_client.Items.CurrentValue?.Items.Count}");
            var pp = _client.Params.phantom;

            // 1. 壁の裏（外周壁の外）: 奥の横通路を東へ向かう亡霊の正面だが、壁に遮られて見つからないはず
            _headWanted = new Vector3(2.9f, 1.6f, 8.8f);
            int spotsBefore = _phantom.SpotCount;
            await Pump(ct, 2.2f);
            L($"wall test: watching={_phantom.Watching} spotted={_phantom.SpotCount - spotsBefore} view={(_phantom.View != null ? _phantom.View.transform.localPosition.ToString("F2") : "none")}");
            Shot("1_wall_behind");

            if (ViewerIsPeer && TwoPlayers)
            {
                // 仲間（devA）が見つかったと報告する。画面に映す devB に「（色と記号）が亡霊に見つかった」が出るはず
                _devA.SendPhantomSpot(1f);
                await Pump(ct, 0.6f);
                L($"peer: hostOut(devA)={_host.IsPhantomOut("devA")} hostState={_host.Machine.State} viewerOut={_client.Tracker.IsPhantomOut.CurrentValue}");
                Shot("6_peer_notice");
                await Pump(ct, 1.0f);
                L("done");
                return;
            }

            // 2. 亡霊の少し前（通路の中）。罰 A のときは先にお宝を持たせる
            var tbl = _client.Items.CurrentValue;
            int idx = -1;
            foreach (var it in tbl.Items) if (it.Kind == TreasureKind.S) { idx = it.Index; break; }
            if (Mode == ThPhantomMode.TakeTreasure)
            {
                _client.UpdateGrabPhase(tbl);
                bool ok = tbl.TryBeginGrab(idx, Now);
                await Pump(ct, 0.3f);
                var h = _host.Items.Get(idx);
                L($"grabbed ok={ok} hostState={h.State} owner={h.OwnerDevice} value={h.Value} gen={h.Gen}");
                Shot("2_holding");
            }
            var view = _phantom.View;
            var vp = view.transform.localPosition;
            var fwd = view.transform.localRotation * Vector3.forward;
            _headWanted = vp + fwd * 1.0f + Vector3.up * 1.6f;
            L($"head placed at {_headWanted.Value.ToString("F2")} phantom={vp.ToString("F2")} yaw={view.transform.localEulerAngles.y:F0}");
            double t0 = Now;
            bool shotRed = false;
            while (Now - t0 < 4.0 && _host.Machine.State == RoundState.Playing)
            {
                await Pump(ct, 0.05f);
                if (!shotRed && _phantom.Watching && Now - t0 > 0.3) { shotRed = true; L("watching (red)"); Shot("3_watching_red"); }
                if (Log.Exists(x => x.Contains("punish"))) break;
            }
            await Pump(ct, 0.3f);
            Shot("4_after_punish_absorbing");
            await Pump(ct, 1.0f);
            Shot("5_after_punish");
            if (Mode == ThPhantomMode.TakeTreasure)
            {
                var h = _host.Items.Get(idx);
                L($"after take: hostState={h.State} gone={h.Gone} value={h.Value} gen={h.Gen} clientGone={_client.Items.CurrentValue.Get(idx).Gone}");
            }
            else
            {
                L($"after out: hostOut={_host.IsPhantomOut("devA")} clientOut={_client.Tracker.IsPhantomOut.CurrentValue} hostState={_host.Machine.State} clientState={_client.Tracker.State.CurrentValue} result={_client.Tracker.Result?.cleared}");
                var bi = _host.Items.Get(idx);
                L($"item {idx}: hostState={bi.State} value={bi.Value}");
            }
            L("done");
        }

        int Count(string s) => Log.FindAll(x => x.Contains(s)).Count;

        void Shot(string name)
        {
            if (string.IsNullOrEmpty(ScreenshotDir)) return;
            var path = System.IO.Path.Combine(ScreenshotDir, $"phantom_{Mode}_{name}.png");
            ScreenCapture.CaptureScreenshot(path);
            L("shot " + path);
        }

        async UniTask Pump(System.Threading.CancellationToken ct, float seconds)
        {
            double end = Now + seconds;
            do { await UniTask.Yield(PlayerLoopTiming.Update, ct); Step(); } while (Now < end);
        }

        void Step()
        {
            double now = Now;
            DrainClients(now);
            _host.Tick(now);
            while (_hostNode.Out.Count > 0)
            {
                var m = _hostNode.Out.Dequeue();
                _devA.OnRpc(m.rpc, 1, m.args, now);
                if (_devA != _client) _client.OnRpc(m.rpc, 1, m.args, now);
                if (_other != null && _other != _devA) _other.OnRpc(m.rpc, 1, m.args, now);
            }
            _devA.Tick(now);
            if (_client != _devA) _client.Tick(now);
            if (_other != null && _other != _devA) _other.Tick(now);
            DrainClients(now);
        }

        void DrainClients(double now)
        {
            while (_clientNode.Out.Count > 0) { var m = _clientNode.Out.Dequeue(); _host.OnRpc(m.rpc, 2, m.args, now); }
            while (_otherNode.Out.Count > 0) { var m = _otherNode.Out.Dequeue(); _host.OnRpc(m.rpc, 3, m.args, now); }
        }

        void LateUpdate()
        {
            if (_rig == null || _headWanted == null || ThVenue.Instance == null) return;
            var root = ThVenue.Instance.Root;
            var cam = Camera.main;
            if (cam == null) return;
            var want = root.TransformPoint(_headWanted.Value);
            _rig.position += want - cam.transform.position;
            // 亡霊の方を向く
            if (_phantom != null && _phantom.View != null)
            {
                var d = _phantom.View.transform.position + Vector3.up * 1.2f - cam.transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 1e-4f) _rig.rotation = Quaternion.LookRotation(d, Vector3.up);
            }
        }

        void OnDestroy()
        {
            if (_session != null) typeof(SessionClient).GetField("_core", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_session, null);
            _host?.Dispose();
            _client?.Dispose();
            if (_other != null && _other != _client) _other.Dispose();
            if (_devA != null && _devA != _client && _devA != _other) _devA.Dispose();
        }
    }
}
#endif
