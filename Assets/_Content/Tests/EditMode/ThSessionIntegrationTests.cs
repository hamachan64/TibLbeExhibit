using System;
using System.Collections.Generic;
using NUnit.Framework;
using R3;

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    /// <summary>メモリ上の偽の通信。RPC は全員（送り手自身を含む）に届く。破棄の条件を差し替えられる。</summary>
    sealed class FakeNetwork
    {
        public sealed class Node : IThTransport
        {
            public int ClientNo;
            public Action<Rpc, int, string[]> OnReceive;
            public FakeNetwork Net;
            public bool Online = true;
            public void Send(Rpc rpc, string[] args) { if (Online) Net.Enqueue(ClientNo, rpc, args); }
        }

        readonly List<Node> _nodes = new List<Node>();
        readonly Queue<(int sender, Rpc rpc, string[] args)> _queue = new Queue<(int, Rpc, string[])>();
        /// <summary>true を返すと、その RPC を捨てる（受信側 clientNo も渡す）</summary>
        public Func<int, Rpc, string[], int, bool> Drop;
        public readonly List<(int sender, Rpc rpc)> Log = new List<(int, Rpc)>();

        public Node Add(int clientNo)
        {
            var n = new Node { ClientNo = clientNo, Net = this };
            _nodes.Add(n);
            return n;
        }

        void Enqueue(int sender, Rpc rpc, string[] args) { _queue.Enqueue((sender, rpc, args)); }

        public void Pump()
        {
            int guard = 0;
            while (_queue.Count > 0 && guard++ < 10000)
            {
                var (sender, rpc, args) = _queue.Dequeue();
                Log.Add((sender, rpc));
                foreach (var n in _nodes)
                {
                    if (!n.Online) continue;
                    if (Drop != null && Drop(sender, rpc, args, n.ClientNo)) continue;
                    n.OnReceive?.Invoke(rpc, sender, args);
                }
            }
        }
    }

    public class ThSessionIntegrationTests
    {
        const int HostNo = 1;
        double _now;
        FakeNetwork _net;
        SessionHostCore _host;
        FakeNetwork.Node _hostNode;
        readonly Dictionary<int, string> _resolve = new Dictionary<int, string>();   // NetSync の GetDeviceIdByClientNo の代わり
        readonly Dictionary<string, FakeNetwork.Node> _nodes = new Dictionary<string, FakeNetwork.Node>();
        readonly List<SessionClientCore> _clients = new List<SessionClientCore>();
        ThParams _defaults;

        static ThParams ShortParams()
        {
            var p = new ThParams();
            p.operation.autoConfirmAlignment = true;   // 位置合わせは確認済み始まりで検証する（#44: 既定は未確認始まり）
            p.progress.openingSeconds = 3; p.progress.practiceSeconds = 5; p.progress.playingSeconds = 10; p.progress.resultSeconds = 3;
            return p;
        }

        [SetUp]
        public void SetUp()
        {
            _now = 100;
            _net = new FakeNetwork();
            _defaults = new ThParams();
            // ホストは短縮したパラメータで動く。端末は組み込みの初期値で起動する（版が違う）
            var hostParams = ThParamsLoader.Load(ShortParams(), null, null);
            _hostNode = _net.Add(HostNo);
            _host = new SessionHostCore(_hostNode, hostParams, new ThGameSettings(), null, 0, no => _resolve.TryGetValue(no, out var v) ? v : null);
            _host.OwnClientNo = HostNo;
            _hostNode.OnReceive = (rpc, sender, args) => _host.OnRpc(rpc, sender, args, _now);
            _clients.Clear();
        }

        [TearDown] public void TearDown() { _host.Dispose(); foreach (var c in _clients) c.Dispose(); }

        SessionClientCore AddClient(int clientNo, string deviceId, ThEnvironmentInfo env = null)
        {
            var node = _net.Add(clientNo);
            var c = new SessionClientCore(deviceId, node, _defaults, env, _now) { OwnClientNo = clientNo };
            node.OnReceive = (rpc, sender, args) => c.OnRpc(rpc, sender, args, _now);
            _clients.Add(c);
            _resolve[clientNo] = deviceId;
            _nodes[deviceId] = node;
            c.OnReady(_now);   // 端末の登録は状況報告（ThResync）で行われる
            _net.Pump();
            Advance(1.0);      // ThParams の採用 → 報告し直し（間隔制限を通る）まで進める
            return c;
        }

        SessionClientCore AddClientNoSettle(int clientNo, string deviceId)
        {
            var node = _net.Add(clientNo);
            var c = new SessionClientCore(deviceId, node, _defaults, null, _now) { OwnClientNo = clientNo };
            node.OnReceive = (rpc, sender, args) => c.OnRpc(rpc, sender, args, _now);
            _clients.Add(c);
            _resolve[clientNo] = deviceId;
            _nodes[deviceId] = node;
            c.OnReady(_now);
            _net.Pump();
            return c;
        }

        /// <summary>時間を進める。0.1 秒刻みでホスト→端末の順に Tick する。</summary>
        void Advance(double seconds)
        {
            int steps = (int)Math.Round(seconds / 0.1);
            for (int i = 0; i < steps; i++)
            {
                _now += 0.1;
                _host.Tick(_now);
                _net.Pump();
                foreach (var c in _clients) c.Tick(_now);
                _net.Pump();
            }
        }

        [Test] public void 版の違う端末はThParamsを受けて参加に戻る()
        {
            var a = AddClient(2, "devA");
            // 端末は初期値の版、ホストは短縮版。報告→ThParams→再報告で一致する
            Assert.AreEqual(_host.ParamsVersion, a.Tracker.LocalParamsVersion);
            Assert.AreEqual(1, _host.Registry.EligibleCount);
            Assert.AreEqual(3f, a.Params.progress.openingSeconds);
        }

        [Test] public void 開始から待機まで通す()
        {
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            Advance(1);
            Assert.IsTrue(_host.Start(_now, 1_700_000_000_000, 99));
            _net.Pump();
            foreach (var c in new[] { a, b })
            {
                Assert.AreEqual(RoundState.Opening, c.Tracker.State.CurrentValue);
                Assert.AreEqual(1_700_000_000_000, c.Tracker.RoundId);
                Assert.IsTrue(c.Tracker.IsParticipant.CurrentValue);
                Assert.AreEqual(99, c.Tracker.Seed);
            }
            var seen = new List<RoundState>();
            using var sub1 = a.Tracker.State.Subscribe(s => seen.Add(s));
            Advance(3.1);
            Assert.AreEqual(RoundState.Tutorial, b.Tracker.State.CurrentValue);
            Advance(5.1);
            Assert.AreEqual(RoundState.Playing, a.Tracker.State.CurrentValue);
            Assert.AreEqual(RoundState.Playing, b.Tracker.State.CurrentValue);
            Assert.AreEqual(a.Tracker.RemainingSeconds.CurrentValue, _host.Machine.RemainingSeconds(_now), 0.5f);
            Advance(10.1);
            Assert.AreEqual(RoundState.Result, a.Tracker.State.CurrentValue);
            Advance(3.1);
            Assert.AreEqual(RoundState.Waiting, a.Tracker.State.CurrentValue);
            Assert.AreEqual(RoundState.Waiting, b.Tracker.State.CurrentValue);
            CollectionAssert.AreEqual(
                new[] { RoundState.Opening, RoundState.Tutorial, RoundState.Playing, RoundState.Result, RoundState.Waiting },
                seen);
            Assert.IsFalse(a.Tracker.IsParticipant.CurrentValue);
        }

        [Test] public void 中断で全端末が待機に戻る()
        {
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            _host.Start(_now, 1000, 1);
            Advance(6);
            Assert.AreEqual(RoundState.Tutorial, a.Tracker.State.CurrentValue);
            Assert.IsTrue(_host.Abort(_now));
            _net.Pump();
            Assert.AreEqual(RoundState.Waiting, a.Tracker.State.CurrentValue);
            Assert.AreEqual(RoundState.Waiting, b.Tracker.State.CurrentValue);
            Assert.IsFalse(_host.Abort(_now), "待機中の中断は何もしない");
        }

        [Test] public void ThStateを捨ててもHeartbeatからResyncで追いつく()
        {
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            // 端末 b への ThState をすべて捨てる
            _net.Drop = (sender, rpc, args, to) => to == 3 && rpc == Rpc.ThState;
            _host.Start(_now, 1000, 1);
            _net.Pump();
            Assert.AreEqual(RoundState.Opening, a.Tracker.State.CurrentValue);
            Assert.AreEqual(RoundState.Waiting, b.Tracker.State.CurrentValue, "取りこぼした");
            // 捨てるのをやめる（以降の ThState は届く）。Heartbeat → ThResync → ThState で追いつく
            _net.Drop = null;
            Advance(1.2);
            Assert.AreEqual(RoundState.Opening, b.Tracker.State.CurrentValue);
            Assert.AreEqual(1000, b.Tracker.RoundId);
            Assert.IsTrue(b.Tracker.IsParticipant.CurrentValue, "Opening から追いついたので参加できる");
        }

        [Test] public void Tutorial以降で追いついた端末は次の回を待つ()
        {
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            _net.Drop = (sender, rpc, args, to) => to == 3 && rpc == Rpc.ThState;
            _host.Start(_now, 1000, 1);
            Advance(3.5);   // Tutorial へ。b は ThState を受けていない
            _net.Drop = null;
            Advance(1.5);
            Assert.AreEqual(RoundState.Tutorial, b.Tracker.State.CurrentValue);
            Assert.IsTrue(b.Tracker.WaitNextRound);
            Assert.IsFalse(b.Tracker.IsParticipant.CurrentValue);
            Assert.IsTrue(a.Tracker.IsParticipant.CurrentValue);
            // ホストも b を「次の回を待つ」にして、本編の参加端末から外す
            Advance(4.0);
            Assert.AreEqual(RoundState.Playing, _host.Machine.State);
            CollectionAssert.AreEqual(new[] { "devA" }, new List<string>(_host.Machine.Participants));
            // Waiting に戻れば解除され、次の回には入れる
            Advance(14.0);
            Assert.AreEqual(RoundState.Waiting, _host.Machine.State);
            Assert.IsFalse(_host.Registry.Get("devB").WaitNextRound);
            _host.Start(_now, 9000, 1);
            Assert.AreEqual(2, _host.Machine.Participants.Count);
        }

        [Test] public void 再送要求は間隔を守る()
        {
            var a = AddClient(2, "devA");
            _net.Drop = (sender, rpc, args, to) => to == 2 && rpc == Rpc.ThState;
            _host.Start(_now, 1000, 1);
            _net.Log.Clear();
            Advance(3.0);
            int resyncs = _net.Log.FindAll(l => l.rpc == Rpc.ThResync).Count;
            Assert.LessOrEqual(resyncs, 7, "3 秒で 0.5 秒間隔なら最大 6〜7 回");
            Assert.GreaterOrEqual(resyncs, 4);
        }

        [Test] public void 自分が送ったRPCの戻りは捨てる()
        {
            var a = AddClient(2, "devA");
            int before = _host.Registry.ConnectedCount;
            // ホスト自身の ThResync が戻ってきても登録されない
            _host.OnRpc(Rpc.ThResync, HostNo, ThMessages.Encode(0, new ThResyncMsg { deviceId = "ghost", paramsVersion = "x" }), _now);
            Assert.AreEqual(before, _host.Registry.ConnectedCount);
            Assert.IsNull(_host.Registry.Get("ghost"));
        }

        [Test] public void 壊れたThResyncでホストは落ちない()
        {
            AddClient(2, "devA");
            Assert.DoesNotThrow(() =>
            {
                _host.OnRpc(Rpc.ThResync, 2, null, _now);
                _host.OnRpc(Rpc.ThResync, 2, new[] { "1", "{ broken" }, _now);
                _host.OnRpc(Rpc.ThResync, 2, new string[0], _now);
            });
        }

        [Test] public void 運営PCを再起動しても端末が報告し直して参加に戻る()
        {
            var a = AddClient(2, "devA");
            Assert.AreEqual(1, _host.Registry.EligibleCount);
            // 再起動: 登録の無い新しいホスト（起動値が違う）が同じ通信路で動き出す
            _host.Dispose();
            _host = new SessionHostCore(_hostNode, ThParamsLoader.Load(ShortParams(), null, null), new ThGameSettings(), null, 777) { OwnClientNo = HostNo };
            Assert.AreEqual(0, _host.Registry.EligibleCount);
            Advance(2.5);
            Assert.AreEqual(1, _host.Registry.EligibleCount, "Heartbeat の起動値の変化から端末が状況報告を送り直した");
            Assert.IsTrue(_host.Start(_now, 5000, 1));
        }

        [Test] public void 切断してから再接続しても参加できる()
        {
            AddClient(2, "devA");
            _host.OnClientDisconnected(2, _now);
            Assert.IsFalse(_host.Registry.IsEligible("devA"));
            _resolve[5] = "devA";
            _host.Registry.Connect("devA", 5);
            Assert.IsTrue(_host.Registry.IsEligible("devA"));
        }

        [Test] public void 端末側だけ版が違う状況でも往復は一定の回数に収まる()
        {
            // ホストは practiceValue=300。端末は TutorialSlot が 3 個の環境なので、その値を初期値に戻して版が合わない
            var hp = ShortParams(); hp.practice.practiceValue = 300;
            _host.Dispose();
            _host = new SessionHostCore(_hostNode, ThParamsLoader.Load(hp, null, null), new ThGameSettings(), null, 0, no => _resolve.TryGetValue(no, out var v) ? v : null) { OwnClientNo = HostNo };
            var a = AddClient(2, "devA", new ThEnvironmentInfo { TutorialSlotCount = 3 });
            _net.Log.Clear();
            Advance(10.0);
            int resyncs = _net.Log.FindAll(l => l.rpc == Rpc.ThResync).Count;
            int paramsSent = _net.Log.FindAll(l => l.rpc == Rpc.ThParams).Count;
            Assert.AreNotEqual(_host.ParamsVersion, a.Tracker.LocalParamsVersion, "版は合わないまま");
            Assert.LessOrEqual(resyncs, 25, "ThResync");
            Assert.LessOrEqual(paramsSent, 25, "ThParams");
            Assert.AreEqual(0, _host.Registry.EligibleCount);
            Assert.LessOrEqual(a.Warnings.Count, SessionClientCore.MaxWarnings);
            CollectionAssert.AllItemsAreUnique(a.Warnings);
        }

        [Test] public void ClientNoが入れ替わっても両方とも正しい端末として参加できる()
        {
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            Assert.AreEqual(2, _host.Registry.EligibleCount);
            // サーバーの再起動で、接続・切断のイベントなしに番号が入れ替わった
            _nodes["devA"].ClientNo = 3; _nodes["devB"].ClientNo = 2;
            _resolve[3] = "devA"; _resolve[2] = "devB";
            a.OwnClientNo = 3; b.OwnClientNo = 2;
            Advance(2.0);
            Assert.AreEqual("devA", _host.Registry.GetDeviceId(3));
            Assert.AreEqual("devB", _host.Registry.GetDeviceId(2));
            Assert.IsTrue(_host.Registry.IsEligible("devA"));
            Assert.IsTrue(_host.Registry.IsEligible("devB"));
            Assert.AreEqual(2, _host.Registry.EligibleCount);
        }

        [Test] public void 報告を送るまではステルスの端末を接続中に数えない()
        {
            Assert.AreEqual(0, _host.Registry.ConnectedCount);
            AddClient(2, "devA");
            Assert.AreEqual(1, _host.Registry.ConnectedCount);
        }

        [Test] public void 開始時に版が違った端末はオープニング中にThParamsを受けて報告すればTutorialに残る()
        {
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            // devB が開始の直前にホストの版と違う状態になった（報告が古い）
            _host.Registry.ReportParamsVersion("devB", "stale");
            Assert.IsTrue(_host.Start(_now, 1000, 1));
            CollectionAssert.AreEquivalent(new[] { "devA", "devB" }, new List<string>(_host.Machine.Participants), "版が違っても開始時の参加端末に入る");
            _net.Pump();
            Advance(1.0);   // ThParams を受けて報告する
            Advance(2.5);
            Assert.AreEqual(RoundState.Tutorial, _host.Machine.State);
            CollectionAssert.AreEquivalent(new[] { "devA", "devB" }, new List<string>(_host.Machine.Participants));
        }

        [Test] public void 開始時に版が違った端末は報告しなければTutorialで外れる()
        {
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            _host.Registry.ReportParamsVersion("devB", "stale");
            b.Dispose();
            _net.Drop = (sender, rpc, args, to) => to == 3 || sender == 3;   // devB は通信できない
            Assert.IsTrue(_host.Start(_now, 1000, 1));
            Advance(3.5);
            Assert.AreEqual(RoundState.Tutorial, _host.Machine.State);
            CollectionAssert.AreEqual(new[] { "devA" }, new List<string>(_host.Machine.Participants));
        }

        [Test] public void 端末が運営PCより先に起動しても全員が報告して参加できる()
        {
            // 端末 A・B を先に Ready にする（報告はホストに届かない）
            var hostOnline = _hostNode.Online;
            _hostNode.Online = false;
            var a = AddClientNoSettle(2, "devA");
            var b = AddClientNoSettle(3, "devB");
            _hostNode.Online = hostOnline;
            Assert.AreEqual(0, _host.Registry.ConnectedCount);
            // A の報告への返信（全員宛ての ThState）を B が先に受けても、B も報告する
            Advance(3.0);
            Assert.AreEqual(2, _host.Registry.ConnectedCount);
            Assert.AreEqual(2, _host.Registry.EligibleCount);
        }

        [Test] public void 参加できる端末が無いと開始できない()
        {
            Assert.IsFalse(_host.Start(_now, 1000, 1));
            var a = AddClient(2, "devA");
            _host.Registry.SetSpectator("devA", true);
            Assert.IsFalse(_host.Start(_now, 1000, 1));
        }

        [Test] public void 待機中のパラメータ再読み込みは新しい版を配り再び参加できる()
        {
            var a = AddClient(2, "devA");
            var p2 = ShortParams();
            p2.practice.practiceValue = 300;
            var r2 = ThParamsLoader.Load(p2, null, null);
            Assert.IsTrue(_host.ReloadParams(r2, new ThGameSettings(), null, _now));
            Assert.AreEqual(0, _host.Registry.EligibleCount, "再報告前は版が違う");
            Advance(1.0);   // ThParams の採用 → 間隔制限を通した再報告
            Assert.AreEqual(r2.Version, a.Tracker.LocalParamsVersion);
            Assert.AreEqual(300, a.Params.practice.practiceValue);
            Assert.AreEqual(1, _host.Registry.EligibleCount);
        }

        [Test] public void 本編中はパラメータを再読み込みできない()
        {
            AddClient(2, "devA");
            _host.Start(_now, 1000, 1);
            Assert.IsFalse(_host.ReloadParams(ThParamsLoader.Load(ShortParams(), null, null), new ThGameSettings(), null, _now));
        }

        [Test] public void ホストが止まると端末は待機に戻る()
        {
            var a = AddClient(2, "devA");
            _host.Start(_now, 1000, 1);
            Advance(1);
            _net.Drop = (sender, rpc, args, to) => sender == HostNo;   // ホストの送信がすべて届かない
            // 端末側だけ Tick を進める
            for (int i = 0; i < 115; i++) { _now += 0.1; a.Tick(_now); _net.Pump(); }
            Assert.AreEqual(RoundState.Waiting, a.Tracker.State.CurrentValue);
            Assert.IsTrue(a.Tracker.IsHostLost.CurrentValue);
        }

        [Test] public void 切断した端末は次の開始で参加端末に入らない()
        {
            AddClient(2, "devA");
            AddClient(3, "devB");
            _host.OnClientDisconnected(3, _now);
            _host.Start(_now, 1000, 1);
            CollectionAssert.AreEqual(new[] { "devA" }, new List<string>(_host.Machine.Participants));
        }

        [Test] public void 運営UIが購読する状態が更新される()
        {
            AddClient(2, "devA");
            Assert.AreEqual(1, _host.ConnectedCount.CurrentValue);
            Assert.AreEqual(1, _host.EligibleCount.CurrentValue);
            _host.Start(_now, 1234, 1);
            Assert.AreEqual(RoundState.Opening, _host.State.CurrentValue);
            Assert.AreEqual(1234, _host.RoundId.CurrentValue);
            Assert.AreEqual(3f, _host.RemainingSeconds.CurrentValue, 0.01f);
            Assert.AreEqual(_host.ParamsVersion, _host.ParamsVersionText.CurrentValue);
        }
    }

    public class AbortConfirmGateTests
    {
        [Test] public void 初回は待ち受けで二回目に実行()
        {
            var g = new AbortConfirmGate(3f);
            Assert.IsFalse(g.Press(10));
            Assert.IsTrue(g.IsArmed(11));
            Assert.IsTrue(g.Press(12));
            Assert.IsFalse(g.IsArmed(12.1));
        }

        [Test] public void 時間が過ぎたら待ち受けは解除される()
        {
            var g = new AbortConfirmGate(3f);
            g.Press(10);
            Assert.IsFalse(g.IsArmed(13.0));
            Assert.IsFalse(g.Press(14), "解除後の 1 回目は再び待ち受け");
            Assert.IsTrue(g.IsArmed(14.5));
        }
    }
}
