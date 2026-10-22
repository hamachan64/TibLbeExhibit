using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using R3;
using UnityEditor;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Editor
{
    /// <summary>
    /// PlayMode の確認用（エディタだけ）。NetSync のサーバーが無くても、UserClient シーン（＋Environment シーン）の中で
    /// 運営PC（<see cref="SessionHostCore"/>）と端末（<see cref="SessionClientCore"/>）をメモリ上の通信でつなぎ、本編のお宝を出して、手をスクリプトから動かす。
    /// 端末は 4 台（先頭の 1 台だけが本物の <see cref="SessionClient"/> に差し込まれ、残りは人数合わせの見えない端末）。
    /// 使い方: UserClient ＋ Environment を開いて Play → <see cref="Start"/> → <see cref="BeginRound"/> → （練習の後）<see cref="BeginPlay"/>。
    /// 手は <see cref="SetHand"/> / <see cref="AimHand"/> で動かす（XR Hands の追跡は止め、握り・手のひらの位置を直接書く）。何も保存しない。
    /// </summary>
    public static class ThLoopbackHarness
    {
        const int HostNo = 1;
        const int FirstClientNo = 2;
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        sealed class Net
        {
            public sealed class Node : IThTransport
            {
                public int No;
                public Action<Rpc, int, string[]> OnReceive;
                public Net Owner;
                public void Send(Rpc rpc, string[] args) => Owner._queue.Enqueue((No, rpc, args));
            }

            readonly List<Node> _nodes = new List<Node>();
            readonly Queue<(int sender, Rpc rpc, string[] args)> _queue = new Queue<(int, Rpc, string[])>();
            public Node Add(int no) { var n = new Node { No = no, Owner = this }; _nodes.Add(n); return n; }

            public void Pump()
            {
                int guard = 0;
                while (_queue.Count > 0 && guard++ < 10000)
                {
                    var (sender, rpc, args) = _queue.Dequeue();
                    foreach (var n in _nodes) n.OnReceive?.Invoke(rpc, sender, args);
                }
            }
        }

        static Net _net;
        static SessionHostCore _host;
        static readonly List<SessionClientCore> _clients = new List<SessionClientCore>();
        static SessionClient _session;
        static TreasureClient _treasure;
        static int _seed = 1;
        static bool _running;
        /// <summary>差し替える前の SessionClient._core（Stop で戻す）</summary>
        static object _originalCore;
        static bool _coreReplaced;

        public static bool Running => _running;
        public static SessionHostCore Host => _host;
        public static SessionClientCore Client => _clients.Count > 0 ? _clients[0] : null;

        static double Now => Time.realtimeSinceStartupAsDouble;

        /// <summary>つなぐ。players = 端末の数（1〜4。先頭が本物の端末）</summary>
        public static string Start(int players, int seed)
        {
            if (!EditorApplication.isPlaying) return "Play 中ではありません";
            Stop();
            _seed = seed;
            _session = UnityEngine.Object.FindFirstObjectByType<SessionClient>();
            _treasure = UnityEngine.Object.FindFirstObjectByType<TreasureClient>();
            var venue = ThVenue.Instance;
            if (_session == null || _treasure == null || venue == null) return $"見つかりません session={_session != null} treasure={_treasure != null} venue={venue != null}";
            var asset = (ThParamsAsset)typeof(SessionClient).GetField("_defaults", Any).GetValue(_session);
            var p = JsonUtility.FromJson<ThParams>(JsonUtility.ToJson(asset.values));
            p.progress.openingSeconds = 1;
            p.progress.practiceSeconds = 600;     // 練習は自分で終わらせる（BeginPlay）
            p.progress.playingSeconds = 3600;
            p.progress.resultSeconds = 5;
            var env = venue.CreateEnvironmentInfo();
            var setup = venue.CreateSetup(_session.Content.treasures);
            var hostParams = ThParamsLoader.Load(p, null, env);

            _net = new Net();
            var hostNode = _net.Add(HostNo);
            _host = new SessionHostCore(hostNode, hostParams, new ThGameSettings(), null, 1, no => no >= FirstClientNo && no < FirstClientNo + 4 ? "harness" + no : null, setup);
            _host.OwnClientNo = HostNo;
            hostNode.OnReceive = (rpc, sender, args) => _host.OnRpc(rpc, sender, args, Now);

            _clients.Clear();
            for (int i = 0; i < Mathf.Clamp(players, 1, 4); i++)
            {
                int no = FirstClientNo + i;
                var node = _net.Add(no);
                var c = new SessionClientCore("harness" + no, node, p, env, Now, setup) { OwnClientNo = no };
                node.OnReceive = (rpc, sender, args) => c.OnRpc(rpc, sender, args, Now);
                _clients.Add(c);
                c.OnReady(Now);
            }
            var coreField = typeof(SessionClient).GetField("_core", Any);
            _originalCore = coreField.GetValue(_session);
            coreField.SetValue(_session, _clients[0]);
            _coreReplaced = true;
            _running = true;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            return $"ok players={_clients.Count} seed={seed} points={venue.PlacementPointCount} catalog={(_session.Content.treasures != null ? "ok" : "null")}";
        }

        static void OnPlayModeChanged(PlayModeStateChange s) { if (s == PlayModeStateChange.ExitingPlayMode) Stop(); }

        public static void Stop()
        {
            if (!_running && _host == null) return;
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            _running = false;
            if (_coreReplaced)
            {
                if (_session != null) typeof(SessionClient).GetField("_core", Any).SetValue(_session, _originalCore);
                _coreReplaced = false;
                _originalCore = null;
            }
            foreach (var c in _clients) c.Dispose();
            _clients.Clear();
            _host?.Dispose();
            _host = null;
        }

        static void Tick()
        {
            if (!_running || _host == null) return;
            double now = Now;
            try
            {
                _host.Tick(now);
                _net.Pump();
                foreach (var c in _clients) c.Tick(now);
                _net.Pump();
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>運営PCの「開始」</summary>
        public static string BeginRound() => !Ready(nameof(BeginRound)) ? "Start 前です" : $"start={_host.Start(Now, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _seed)} state={_host.Machine.State}";

        /// <summary>運営PCの「本編開始」（練習を飛ばす）</summary>
        public static string BeginPlay() => !Ready(nameof(BeginPlay)) ? "Start 前です" : $"beginPlay={_host.BeginPlay(Now)} state={_host.Machine.State}";

        public static string Status()
        {
            if (!Ready(nameof(Status))) return "Start 前です";
            var sb = new StringBuilder();
            sb.AppendLine($"host state={_host?.Machine.State} remaining={_host?.Machine.RemainingSeconds(Now):F1} eligible={_host?.Registry.EligibleCount}");
            if (Client != null) sb.AppendLine($"client state={Client.Tracker.State.CurrentValue} participant={Client.Tracker.IsParticipant.CurrentValue} items={(Client.Items.CurrentValue != null ? Client.Items.CurrentValue.Items.Count : -1)} views={_treasure.ViewCount}");
            return sb.ToString();
        }

        static bool Ready(string caller)
        {
            if (_running && _host != null) return true;
            Debug.LogWarning($"[ThLoopbackHarness] {caller}: Start が呼ばれていないので何もしません");
            return false;
        }

        // ---- 手 ----
        static HandGrabber Grabber(string side) => side == "L" ? _treasure.Left : _treasure.Right;

        static object Field(object o, string name) => o.GetType().GetField(name, Any).GetValue(o);

        /// <summary>頭の位置を合わせる（XR Rig を動かす）。yaw は度（Z+ が 0）</summary>
        public static string SetHead(Vector3 head, float yawDegrees)
        {
            var cam = Camera.main;
            if (cam == null) return "Main Camera がありません";
            var rig = cam.transform.root;
            rig.rotation = Quaternion.Euler(0f, yawDegrees - cam.transform.eulerAngles.y + rig.eulerAngles.y, 0f);
            rig.position += head - cam.transform.position;
            return $"head={cam.transform.position:F2} yaw={cam.transform.eulerAngles.y:F0}";
        }

        /// <summary>肩の推定位置（TreasureClient.EstimateShoulder と同じ）</summary>
        public static Vector3 Shoulder(string side)
        {
            var g = Client.Params.grip;
            var head = Camera.main.transform;
            var yaw = Quaternion.Euler(0f, head.eulerAngles.y, 0f);
            float s = side == "L" ? -g.shoulderSide : g.shoulderSide;
            return head.position + yaw * new Vector3(s, -g.shoulderDown, 0f);
        }

        /// <summary>手のひらを直接置く。tracked=false で見失い。grip=true で握る（false で開く＝Released(Opened)）</summary>
        public static string SetHand(string side, bool tracked, Vector3 palm, Quaternion rotation, bool grip)
        {
            var g = Grabber(side);
            var tracker = g.Tracker;
            tracker.enabled = false;   // XR Hands の更新を止める（以降はここで書く）
            var t = tracker.GetType();
            t.GetProperty("HasPalm").GetSetMethod(true).Invoke(tracker, new object[] { tracked });
            t.GetProperty("PalmPose").GetSetMethod(true).Invoke(tracker, new object[] { new Pose(palm, rotation) });
            var tr = (ReactiveProperty<bool>)Field(tracker, "_isTracked");
            if (tr.Value != tracked) tr.Value = tracked;
            var gr = (ReactiveProperty<bool>)Field(tracker, "_isGripping");
            bool wasGrip = gr.Value;
            if (!tracked && wasGrip) { gr.Value = false; ((Subject<GripReleaseReason>)Field(tracker, "_released")).OnNext(GripReleaseReason.Lost); }
            else if (grip != wasGrip)
            {
                gr.Value = grip;
                if (!grip) ((Subject<GripReleaseReason>)Field(tracker, "_released")).OnNext(GripReleaseReason.Opened);
            }
            return $"{side} tracked={tracked} grip={gr.Value} palm={palm:F2}";
        }

        /// <summary>肩から target へ向かう線上（肩から distance の所）に手のひらを置く（握らない）。ビームが target を指す</summary>
        public static Vector3 AimHand(string side, Vector3 target, float distance)
        {
            var sh = Shoulder(side);
            var dir = (target - sh).normalized;
            var palm = sh + dir * distance;
            SetHand(side, true, palm, Quaternion.LookRotation(dir), Field(Grabber(side).Tracker, "_isGripping") is ReactiveProperty<bool> r && r.Value);
            return palm;
        }

        public static string HandInfo(string side)
        {
            var g = Grabber(side);
            var beam = (LineRenderer)Field(g, "_beam");
            var aimed = g.Aimed;
            string beamInfo = beam != null && beam.enabled ? $"beam on end={beam.GetPosition(1):F2}" : "beam off";
            return $"{side} aimed={(aimed != null ? aimed.name + "#" + aimed.Model.Index : "none")} zone={g.AimedZone} held={(g.Held != null ? g.Held.name + "#" + g.Held.Model.Index + " state=" + g.Held.Model.State : "none")} {beamInfo}";
        }

        /// <summary>出ているお宝の一覧（番号・種類・プレハブ名・ワールド位置・Y 回転・状態）</summary>
        public static List<(int index, string name, Vector3 pos, float yaw, ItemState state, int value)> Items()
        {
            var list = new List<(int, string, Vector3, float, ItemState, int)>();
            for (int i = 0; i < 300; i++)
            {
                var t = _treasure.GetTreasure(i);
                if (t == null) continue;
                list.Add((i, t.name, t.transform.position, t.transform.eulerAngles.y, t.Model.State, t.Model.Value));
            }
            return list;
        }

        public static Treasure Get(int index) => _treasure.GetTreasure(index);
        public static TreasureClient TreasureClient => _treasure;
    }
}
