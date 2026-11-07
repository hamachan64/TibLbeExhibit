using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    /// <summary>遺跡の亡霊: 経路・視界・見つかる判定（純粋）</summary>
    public class ThPhantomLogicTests
    {
        static PhantomRoute Square() => new PhantomRoute(new[] { new Vector2(0, 0), new Vector2(4, 0), new Vector2(4, 4), new Vector2(0, 4) });

        [Test] public void 経路_全長と経過0は出発位置()
        {
            var r = Square();
            Assert.AreEqual(16f, r.Length, 1e-4f);
            r.At(0f, 0.4f, 0f, out var p, out _);
            Assert.AreEqual(0f, Vector2.Distance(p, new Vector2(0, 0)), 1e-4f);
            // 出発位置の割合: 0.5 = 全長の半分（ここでは (4,4)）
            r.At(0f, 0.4f, 0.5f, out p, out _);
            Assert.AreEqual(0f, Vector2.Distance(p, new Vector2(4, 4)), 1e-4f);
        }

        [Test] public void 経路_一周の長さ割る速さの時間で元の位置に戻る()
        {
            var r = Square();
            float speed = 0.4f;
            r.At(r.Length / speed, speed, 0.25f, out var p, out var yaw);
            r.At(0f, speed, 0.25f, out var p0, out var yaw0);
            Assert.AreEqual(0f, Vector2.Distance(p, p0), 1e-3f);
            Assert.AreEqual(yaw0, yaw, 1e-2f);
        }

        [Test] public void 経路_区間の途中の位置と向き()
        {
            var r = Square();
            r.Sample(2f, out var p, out var yaw);   // (0,0)→(4,0) の中央。+X 向き = 90 度
            Assert.AreEqual(0f, Vector2.Distance(p, new Vector2(2, 0)), 1e-4f);
            Assert.AreEqual(90f, yaw, 1e-3f);
            r.Sample(6f, out p, out yaw);           // (4,0)→(4,4) の中央。+Z 向き = 0 度
            Assert.AreEqual(0f, Vector2.Distance(p, new Vector2(4, 2)), 1e-4f);
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, yaw), 1e-3f);
        }

        [Test] public void 経路_曲がり角で向きが次の区間へ変わる()
        {
            var r = Square();
            r.Sample(4f, out _, out var atCorner);   // 角: 90 と 0 の真ん中 = 45
            Assert.AreEqual(45f, Mathf.DeltaAngle(0f, atCorner), 1e-2f);
            r.Sample(4f - PhantomRoute.CornerBlend, out _, out var before);
            Assert.AreEqual(90f, before, 1e-2f, "角の手前の丸めの始まりは前の区間の向き");
            r.Sample(4f + PhantomRoute.CornerBlend + 0.01f, out _, out var after);
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, after), 1e-2f, "角を過ぎたら次の区間の向き");
        }

        [Test] public void 経路_負の距離や全長超えは巻き戻る()
        {
            var r = Square();
            r.Sample(-2f, out var p, out _);
            Assert.AreEqual(0f, Vector2.Distance(p, new Vector2(0, 2)), 1e-4f);
            r.Sample(18f, out p, out _);
            Assert.AreEqual(0f, Vector2.Distance(p, new Vector2(2, 0)), 1e-4f);
        }

        [Test] public void 経路_点が足りない_長さ0は例外()
        {
            Assert.Throws<ArgumentException>(() => new PhantomRoute(new[] { Vector2.zero }));
            Assert.Throws<ArgumentException>(() => new PhantomRoute(new[] { Vector2.zero, Vector2.zero }));
        }

        [Test] public void 初期経路は通路の中央で一周する閉じた折れ線()
        {
            var r = new PhantomRoute(PhantomRoute.DefaultPoints);
            // 塊の外面 X ±1.08 と外周壁内面 ±2.28 の間の中央 ±1.68、奥行は 3.55〜8.8
            foreach (var p in PhantomRoute.DefaultPoints)
            {
                Assert.GreaterOrEqual(p.y, 3.55f - 1e-4f); Assert.LessOrEqual(p.y, 8.8f + 1e-4f);
                Assert.LessOrEqual(Mathf.Abs(p.x), 1.68f + 1e-4f);
            }
            Assert.Greater(r.Length, 15f);
            r.At(0f, 0.4f, 0.5f, out var mid, out _);
            Assert.AreEqual(0f, Vector2.Distance(mid, new Vector2(0f, 8.8f)), 1e-3f, "出発位置 0.5 は奥の横通路の中央");
        }

        [Test] public void 視界_距離と角度の内外と境界()
        {
            var eye = Vector2.zero;   // 向き 0 = +Z
            Assert.IsTrue(PhantomSight.InView(eye, 0f, new Vector2(0, 2), 3f, 35f));
            Assert.IsTrue(PhantomSight.InView(eye, 0f, new Vector2(0, 3), 3f, 35f), "距離の境界は内");
            Assert.IsFalse(PhantomSight.InView(eye, 0f, new Vector2(0, 3.01f), 3f, 35f));
            var edge = new Vector2(Mathf.Sin(35f * Mathf.Deg2Rad), Mathf.Cos(35f * Mathf.Deg2Rad)) * 2f;
            Assert.IsTrue(PhantomSight.InView(eye, 0f, edge, 3f, 35f), "角度の境界は内");
            var outside = new Vector2(Mathf.Sin(36f * Mathf.Deg2Rad), Mathf.Cos(36f * Mathf.Deg2Rad)) * 2f;
            Assert.IsFalse(PhantomSight.InView(eye, 0f, outside, 3f, 35f));
        }

        [Test] public void 視界_真後ろと向きの回転()
        {
            Assert.IsFalse(PhantomSight.InView(Vector2.zero, 0f, new Vector2(0, -1), 3f, 35f), "真後ろ");
            Assert.IsTrue(PhantomSight.InView(Vector2.zero, 90f, new Vector2(2, 0), 3f, 35f), "向き 90 度なら +X が正面");
            Assert.IsFalse(PhantomSight.InView(Vector2.zero, 90f, new Vector2(0, 2), 3f, 35f));
            Assert.IsTrue(PhantomSight.InView(new Vector2(1, 1), 0f, new Vector2(1, 1), 3f, 35f), "目の真下は見える");
        }

        [Test] public void 見つかる判定_続けて見られると見つかる()
        {
            var d = new PhantomSpotDetector(0.7f, 3f);
            double t = 0;
            bool spotted = false;
            for (int i = 0; i < 6; i++) { t += 0.1; spotted |= d.Update(true, t); }
            Assert.IsFalse(spotted, "0.5 秒ではまだ");
            Assert.IsTrue(d.Watching);
            Assert.IsFalse(d.Update(true, t += 0.1));   // 0.6
            Assert.IsTrue(d.Update(true, t += 0.1), "0.7 秒で見つかる");
            Assert.IsFalse(d.Watching);
        }

        [Test] public void 見つかる判定_途中で視界を出たら数え直す()
        {
            var d = new PhantomSpotDetector(0.7f, 3f);
            double t = 0;
            for (int i = 0; i < 6; i++) d.Update(true, t += 0.1);   // 0.5 秒
            d.Update(false, t += 0.1);
            Assert.IsFalse(d.Watching);
            bool spotted = false;
            for (int i = 0; i < 6; i++) spotted |= d.Update(true, t += 0.1);   // また 0.5 秒
            Assert.IsFalse(spotted, "数え直しなので足りない");
            Assert.IsTrue(d.Update(true, t += 0.2));
        }

        [Test] public void 見つかる判定_無視時間の間は見つからない_過ぎたらまた見つかる()
        {
            var d = new PhantomSpotDetector(0.7f, 3f);
            double t = 0;
            d.Update(true, t);
            bool first = false;
            for (int i = 0; i < 8; i++) first |= d.Update(true, t += 0.1);
            Assert.IsTrue(first);
            bool again = false;
            double until = t + 2.5;
            while (t < until) again |= d.Update(true, t += 0.1);
            Assert.IsFalse(again, "3 秒以内は見られていても見つからない");
            Assert.IsFalse(d.Watching);
            again = false;
            for (int i = 0; i < 20; i++) again |= d.Update(true, t += 0.1);
            Assert.IsTrue(again, "無視時間の後はまた見つかる");
        }

        [Test] public void 見つかる判定_長い停止の後の1回で一気に数えない()
        {
            var d = new PhantomSpotDetector(0.7f, 3f);
            d.Update(true, 0.0);
            Assert.IsFalse(d.Update(true, 5.0), "5 秒止まっても 1 回で数えるのは 0.1 秒まで");
            Assert.AreEqual(PhantomSpotDetector.MaxStep / 0.7, d.Progress, 0.01);
        }

        [Test] public void 見つかる判定_Resetで全部忘れる()
        {
            var d = new PhantomSpotDetector(0.7f, 3f);
            double t = 0;
            for (int i = 0; i < 9; i++) d.Update(true, t += 0.1);
            d.Reset();
            d.Update(true, t += 0.1);
            Assert.IsFalse(d.Update(true, t += 0.1));
            Assert.AreEqual(0.1f / 0.7f, d.Progress, 0.01f);
        }

        [Test] public void 扇メッシュは頂点と三角形を作る()
        {
            var m = new Mesh();
            PhantomView.BuildFan(m, 3f, 35f);
            Assert.AreEqual(18, m.vertexCount);
            Assert.AreEqual(16 * 3, m.triangles.Length);
            Assert.AreEqual(3f, m.bounds.max.z, 0.01f);
            UnityEngine.Object.DestroyImmediate(m);
        }
    }

    /// <summary>遺跡の亡霊: 設定・パラメータ・文言</summary>
    public class ThPhantomSettingsTests
    {
        [Test] public void 設定の亡霊は読み書きできる()
        {
            foreach (var mode in new[] { ThPhantomMode.Off, ThPhantomMode.TakeTreasure, ThPhantomMode.PlayerOut })
            {
                var json = JsonUtility.ToJson(new ThGameSettings { phantom = mode });
                var s = ThGameSettings.Parse(json, out var w);
                Assert.IsNull(w);
                Assert.AreEqual(mode, s.phantom);
            }
        }

        [Test] public void 設定の亡霊が不正値なら初期値と警告()
        {
            var s = ThGameSettings.Parse("{\"phantom\":9}", out var w);
            Assert.IsNotNull(w);
            Assert.AreEqual(ThPhantomMode.Off, s.phantom);
            Assert.AreEqual(ThMode.TargetAndScore, s.mode);
        }

        [Test] public void 古い設定のgolemは読み捨てて亡霊はなし()
        {
            var s = ThGameSettings.Parse("{\"golem\":true}", out var w);
            Assert.IsNull(w);
            Assert.AreEqual(ThPhantomMode.Off, s.phantom);
        }

        [Test] public void 追加要素の欄に亡霊の種類が残る()
        {
            Assert.AreEqual("", RoundRecord.ExtrasOf(new ThGameSettings()));
            Assert.AreEqual("phantom-take", RoundRecord.ExtrasOf(new ThGameSettings { phantom = ThPhantomMode.TakeTreasure }));
            Assert.AreEqual("phantom-out", RoundRecord.ExtrasOf(new ThGameSettings { phantom = ThPhantomMode.PlayerOut }));
        }

        [Test] public void 亡霊のパラメータの初期値は範囲内で警告なし()
        {
            var r = ThParamsLoader.Load(new ThParams(), null, null);
            Assert.IsEmpty(r.Warnings.Where(x => x.Contains("亡霊")));
            Assert.AreEqual(0.4f, r.Params.phantom.speed);
            Assert.AreEqual(3f, r.Params.phantom.sightDistance);
            Assert.AreEqual(35f, r.Params.phantom.sightHalfAngle);
            Assert.AreEqual(0.7f, r.Params.phantom.spotSeconds);
            Assert.AreEqual(3f, r.Params.phantom.ignoreSeconds);
        }

        [Test] public void 亡霊のパラメータが範囲外なら初期値に戻る()
        {
            var r = ThParamsLoader.Load(new ThParams(), "{\"phantom\":{\"speed\":99}}", null);
            Assert.AreEqual(0.4f, r.Params.phantom.speed);
            Assert.IsTrue(r.Warnings.Any(x => x.Contains("speed")));
        }

        [Test] public void 亡霊の文言は組み込みの既定がある()
        {
            var t = ScriptableObject.CreateInstance<ThTextTable>();
            Assert.AreEqual("亡霊に見つかると、持っているお宝を奪われる。光の中に入らないで", t.Get(ThTextId.PhantomNoticeTake));
            Assert.AreEqual("亡霊に見つかると、その回はゲームオーバー。光の中に入らないで", t.Get(ThTextId.PhantomNoticeOut));
            StringAssert.Contains("この回はここまで", t.Get(ThTextId.PhantomOutSelf));
            StringAssert.Contains("亡霊に見つかった", t.Format(ThTextId.PhantomOutPeerFormat, "赤●"));
            UnityEngine.Object.DestroyImmediate(t);
        }
    }

    /// <summary>遺跡の亡霊: ホストの受け付けと罰（ホスト＋端末を偽の通信でつなぐ）</summary>
    public class ThPhantomHostTests
    {
        const int HostNo = 1;
        static readonly Bounds Zone = new Bounds(new Vector3(0, 0.9f, 0.8f), new Vector3(3f, 1.2f, 1f));

        double _now;
        FakeNetwork _net;
        SessionHostCore _host;
        readonly Dictionary<int, string> _resolve = new Dictionary<int, string>();
        readonly List<SessionClientCore> _clients = new List<SessionClientCore>();
        readonly Dictionary<string, FakeNetwork.Node> _nodes = new Dictionary<string, FakeNetwork.Node>();
        readonly List<RoundRecord> _records = new List<RoundRecord>();
        readonly List<ThPhantomPunishMsg> _punished = new List<ThPhantomPunishMsg>();

        static ThTreasureSetup MakeSetup()
        {
            var pts = new List<PlacementPoint>();
            for (int i = 0; i < 30; i++) pts.Add(new PlacementPoint(new Vector3((i % 5) * 0.75f - 1.5f, 0, 3f + i / 5), i % 3));
            return new ThTreasureSetup
            {
                Points = pts,
                Catalog = new[] { new SpawnCatalogEntry(TreasureKind.S, 0), new SpawnCatalogEntry(TreasureKind.S, 1), new SpawnCatalogEntry(TreasureKind.M, 2) },
                CollectBounds = Zone,
                CatalogHash = "h1",
            };
        }

        static ThParams ShortParams()
        {
            var p = new ThParams();
            p.operation.autoConfirmAlignment = true;
            p.progress.openingSeconds = 3; p.progress.practiceSeconds = 5; p.progress.playingSeconds = 30; p.progress.resultSeconds = 3;
            return p;
        }

        void Build(ThPhantomMode mode)
        {
            _now = 100;
            _net = new FakeNetwork();
            var node = _net.Add(HostNo);
            _host = new SessionHostCore(node, ThParamsLoader.Load(ShortParams(), null, null), new ThGameSettings { phantom = mode }, null, 0,
                no => _resolve.TryGetValue(no, out var v) ? v : null, MakeSetup());
            _host.OwnClientNo = HostNo;
            node.OnReceive = (rpc, sender, args) => _host.OnRpc(rpc, sender, args, _now);
            _host.RoundFinished.Subscribe(r => _records.Add(r));
            _clients.Clear(); _resolve.Clear(); _nodes.Clear(); _records.Clear(); _punished.Clear();
        }

        SessionClientCore AddClient(int no, string id)
        {
            var node = _net.Add(no);
            var c = new SessionClientCore(id, node, new ThParams(), null, _now, MakeSetup()) { OwnClientNo = no };
            node.OnReceive = (rpc, sender, args) => c.OnRpc(rpc, sender, args, _now);
            if (_clients.Count == 0) c.PhantomPunished.Subscribe(m => _punished.Add(m));   // 最初の端末の受信だけ数える（全端末に同じ知らせが届くため）
            _clients.Add(c); _resolve[no] = id; _nodes[id] = node;
            c.OnReady(_now);
            _net.Pump();
            Advance(1.0);
            return c;
        }

        void Advance(double seconds)
        {
            int steps = (int)Math.Round(seconds / 0.1);
            for (int i = 0; i < steps; i++)
            {
                _now += 0.1;
                _host.Tick(_now); _net.Pump();
                foreach (var c in _clients) c.Tick(_now);
                _net.Pump();
            }
        }

        void StartPlaying()
        {
            Assert.IsTrue(_host.Start(_now, 5000, 4242));
            _net.Pump();
            Advance(3.3);
            Advance(5.3);
            Assert.AreEqual(RoundState.Playing, _host.Machine.State);
            _net.Pump();
        }

        [TearDown] public void TearDown() { _host?.Dispose(); foreach (var c in _clients) c.Dispose(); }

        int FirstS(ClientItemTable t, int skip = 0) => t.Items.Where(i => i.Kind == TreasureKind.S).Skip(skip).First().Index;

        void Grab(SessionClientCore c, int idx)
        {
            Assert.IsTrue(c.Items.CurrentValue.TryBeginGrab(idx, _now));
            _net.Pump();
            Assert.IsTrue(c.Items.CurrentValue.Get(idx).Approved);
        }

        void Spot(string id)
        {
            _clients.First(c => c.DeviceId == id).SendPhantomSpot(1f);
            _net.Pump();
        }

        [Test] public void 罰A_持っているお宝が消滅して世代が進み全端末で吸い込まれる()
        {
            Build(ThPhantomMode.TakeTreasure);
            var a = AddClient(2, "devA"); var b = AddClient(3, "devB");
            StartPlaying();
            int i1 = FirstS(a.Items.CurrentValue), i2 = FirstS(a.Items.CurrentValue, 1);
            Grab(a, i1); Grab(a, i2);
            int gen1 = _host.Items.Get(i1).Gen;
            int v1 = _host.Items.Get(i1).Value, v2 = _host.Items.Get(i2).Value;
            var events = new List<ItemEventKind>();
            b.Items.CurrentValue.Events.Subscribe(e => events.Add(e.Kind));

            Spot("devA");

            foreach (int i in new[] { i1, i2 })
            {
                var h = _host.Items.Get(i);
                Assert.IsTrue(h.Gone); Assert.AreEqual(ItemState.Destroyed, h.State); Assert.AreEqual(0, h.Value);
                Assert.IsTrue(a.Items.CurrentValue.Get(i).Gone); Assert.IsTrue(b.Items.CurrentValue.Get(i).Gone);
            }
            Assert.AreEqual(gen1 + 1, _host.Items.Get(i1).Gen, "世代 +1");
            Assert.AreEqual(2, events.Count(k => k == ItemEventKind.Absorbed), "他の端末では吸い込まれる演出のイベント");
            Assert.AreEqual(0, events.Count(k => k == ItemEventKind.Destroyed), "砕ける演出ではない");
            Assert.AreEqual(1, _punished.Count(m => m.device == "devA" && m.lost == v1 + v2));
            Assert.AreEqual(0, _host.Items.Total, "回収額には入らない");
            Assert.IsFalse(_host.IsPhantomOut("devA"), "罰 A では退場しない");

            _host.Abort(_now); _net.Pump();
            var rec = _records.Single().Players().Single(p => p.DeviceId == "devA");
            Assert.AreEqual(1, rec.PhantomFoundCount);
            Assert.AreEqual(v1 + v2, rec.PhantomLostAmount);
            Assert.AreEqual(0, _records.Single().Players().Single(p => p.DeviceId == "devB").PhantomFoundCount);
        }

        [Test] public void 罰A_ThItemsで追いつく()
        {
            Build(ThPhantomMode.TakeTreasure);
            var a = AddClient(2, "devA"); var b = AddClient(3, "devB");
            StartPlaying();
            int i1 = FirstS(a.Items.CurrentValue);
            Grab(a, i1);
            _net.Drop = (s, r, args, to) => r == Rpc.ThPhantomPunish && to == 3;   // B は罰の知らせを取りこぼす
            Spot("devA");
            Assert.IsFalse(b.Items.CurrentValue.Get(i1).Gone);
            _net.Drop = null;
            Advance(1.5);   // ThItems（1 秒ごと）
            Assert.IsTrue(b.Items.CurrentValue.Get(i1).Gone, "状態表の gone + Destroyed で追いつく");
        }

        [Test] public void 罰A_何も持っていなければ罰なし_ただし報告は数えて無視時間をかける()
        {
            Build(ThPhantomMode.TakeTreasure);
            var a = AddClient(2, "devA");
            StartPlaying();
            Spot("devA");
            Assert.AreEqual(0, _punished.Count, "ThPhantomPunish は送らない");
            // 無視時間（3 秒）の間に持っても、2 回目の報告は捨てる（数えない・奪わない）
            int i1 = FirstS(a.Items.CurrentValue);
            Grab(a, i1);
            Advance(1.0);
            Spot("devA");
            Assert.AreEqual(0, _punished.Count);
            Assert.IsFalse(_host.Items.Get(i1).Gone);
            _host.Abort(_now); _net.Pump();
            var rec = _records.Single().Players().Single();
            Assert.AreEqual(1, rec.PhantomFoundCount, "受け付けた報告は罰 A で何も持っていなくても数える");
            Assert.AreEqual(0, rec.PhantomLostAmount, "失った額は奪った額だけ");
        }

        [Test] public void 罰A_無視時間内の2回目は捨てる_過ぎたら適用()
        {
            Build(ThPhantomMode.TakeTreasure);
            var a = AddClient(2, "devA");
            StartPlaying();
            int i1 = FirstS(a.Items.CurrentValue), i2 = FirstS(a.Items.CurrentValue, 1);
            Grab(a, i1);
            Spot("devA");
            Assert.AreEqual(1, _punished.Count);
            Grab(a, i2);
            Advance(1.0);
            Spot("devA");
            Assert.AreEqual(1, _punished.Count, "3 秒以内は捨てる");
            Assert.IsFalse(_host.Items.Get(i2).Gone);
            Advance(2.5);
            Spot("devA");
            Assert.AreEqual(2, _punished.Count, "3 秒たてば適用");
            Assert.IsTrue(_host.Items.Get(i2).Gone);
        }

        [Test] public void 設定がなしなら捨てる()
        {
            Build(ThPhantomMode.Off);
            var a = AddClient(2, "devA");
            StartPlaying();
            int i1 = FirstS(a.Items.CurrentValue);
            Grab(a, i1);
            Spot("devA");
            Assert.AreEqual(0, _punished.Count);
            Assert.IsFalse(_host.Items.Get(i1).Gone);
        }

        [Test] public void Playing以外は捨てる()
        {
            Build(ThPhantomMode.PlayerOut);
            var a = AddClient(2, "devA");
            Spot("devA");   // Waiting
            Assert.IsFalse(_host.IsPhantomOut("devA"));
            Assert.IsTrue(_host.Start(_now, 5000, 1)); _net.Pump();
            Advance(3.3);   // Tutorial
            Assert.AreEqual(RoundState.Tutorial, _host.Machine.State);
            Spot("devA");
            Assert.IsFalse(_host.IsPhantomOut("devA"));
            Assert.AreEqual(0, _punished.Count);
        }

        [Test] public void 見学の端末の報告は捨てる()
        {
            Build(ThPhantomMode.PlayerOut);
            var a = AddClient(2, "devA"); AddClient(3, "devB");
            StartPlaying();
            _host.Registry.SetSpectator("devA", true);
            Spot("devA");
            Assert.IsFalse(_host.IsPhantomOut("devA"));
            Assert.AreEqual(0, _punished.Count);
        }

        [Test] public void 罰B_退場してお宝がそっと置かれ_つかむ申請は否認され_ThStateに載る()
        {
            Build(ThPhantomMode.PlayerOut);
            var a = AddClient(2, "devA"); var b = AddClient(3, "devB");
            StartPlaying();
            int i1 = FirstS(a.Items.CurrentValue), i2 = FirstS(a.Items.CurrentValue, 1);
            Grab(a, i1);
            int value = _host.Items.Get(i1).Value;
            int damageBefore = _host.Items.DamageTotal;

            Spot("devA");

            Assert.IsTrue(_host.IsPhantomOut("devA"));
            Assert.IsFalse(_host.IsPhantomOut("devB"));
            Assert.AreEqual(ItemState.Placing, _host.Items.Get(i1).State, "見学への切り替えと同じ解放（そっと置く）");
            Assert.AreEqual(value, _host.Items.Get(i1).Value, "減額なし");
            Assert.AreEqual(damageBefore, _host.Items.DamageTotal);
            Assert.IsTrue(a.Tracker.IsPhantomOut.CurrentValue, "本人はすぐ退場の状態になる");
            Assert.IsFalse(b.Tracker.IsPhantomOut.CurrentValue);
            CollectionAssert.Contains(b.Tracker.PhantomOutDevices, "devA");

            // 退場した端末のつかむ申請: 端末側の自己判断を通しても、ホストは認めない
            a.Items.CurrentValue.ParticipantPlaying = true;
            a.Items.CurrentValue.TryBeginGrab(i2, _now);
            _net.Pump();
            Assert.AreEqual(ItemState.Resting, _host.Items.Get(i2).State);
            Assert.AreEqual(-1, _host.Items.Get(i2).OwnerClientNo);

            // 仲間は運べる
            Advance(0.5);   // 置く時間の後、つかめるに戻る
            Assert.AreEqual(ItemState.Resting, _host.Items.Get(i1).State);
            Assert.IsTrue(b.Items.CurrentValue.TryBeginGrab(i1, _now));
        }

        [Test] public void 罰B_UpdateGrabPhaseで退場した端末はつかめなくなる()
        {
            Build(ThPhantomMode.PlayerOut);
            var a = AddClient(2, "devA"); AddClient(3, "devB");
            StartPlaying();
            a.UpdateGrabPhase(a.Items.CurrentValue);
            Assert.IsTrue(a.Items.CurrentValue.ParticipantPlaying);
            Spot("devA");
            a.UpdateGrabPhase(a.Items.CurrentValue);
            Assert.IsFalse(a.Items.CurrentValue.ParticipantPlaying);
        }

        [Test] public void 罰B_同じ通し番号の古いThStateが遅れて届いても退場は戻らない()
        {
            Build(ThPhantomMode.PlayerOut);
            var a = AddClient(2, "devA"); AddClient(3, "devB");
            StartPlaying();
            Spot("devA");
            Assert.IsTrue(a.Tracker.IsPhantomOut.CurrentValue);
            // 退場を載せていない同じ回・同じ通し番号の ThState が遅れて届いても、回の中では戻さない
            var stale = new ThStateMsg { seq = a.Tracker.Seq, state = (int)RoundState.Playing, remaining = 20f, participants = a.Tracker.Participants, settings = new ThGameSettings { phantom = ThPhantomMode.PlayerOut } };
            a.Tracker.OnState(a.Tracker.RoundId, stale, _now);
            Assert.IsTrue(a.Tracker.IsPhantomOut.CurrentValue);
        }

        [Test] public void 罰B_ThPhantomPunishもThStateも取りこぼしてもHeartbeatからResyncで追いつく()
        {
            Build(ThPhantomMode.PlayerOut);
            var a = AddClient(2, "devA"); AddClient(3, "devB");
            StartPlaying();
            int seqBefore = a.Tracker.Seq;
            _net.Drop = (s, r, args, to) => to == 2 && (r == Rpc.ThPhantomPunish || r == Rpc.ThState);
            Spot("devA");
            Assert.IsTrue(_host.IsPhantomOut("devA"));
            Assert.AreEqual(seqBefore, a.Tracker.Seq, "本人はどちらも受け取っていない");
            Assert.IsFalse(a.Tracker.IsPhantomOut.CurrentValue);
            Assert.Greater(_host.Machine.Seq, seqBefore, "退場させるたびに通し番号が進む");
            _net.Drop = null;
            Advance(3.0);   // Heartbeat（1 秒ごと）→ 通し番号の食い違い → ThResync → ThState
            Assert.IsTrue(a.Tracker.IsPhantomOut.CurrentValue, "Resync で追いつく");
            Assert.AreEqual(_host.Machine.Seq, a.Tracker.Seq);
        }

        [Test] public void 罰B_退場の通し番号は退場させるたびに進む()
        {
            Build(ThPhantomMode.PlayerOut);
            AddClient(2, "devA"); AddClient(3, "devB");
            StartPlaying();
            int s0 = _host.Machine.Seq;
            Spot("devA");
            int s1 = _host.Machine.Seq;
            Spot("devB");
            Assert.AreEqual(s0 + 1, s1);
            Assert.AreEqual(s1 + 1, _host.Machine.Seq);
        }

        [Test] public void 罰B_次の回では退場が消える()
        {
            Build(ThPhantomMode.PlayerOut);
            var a = AddClient(2, "devA"); AddClient(3, "devB");
            StartPlaying();
            Spot("devA");
            Assert.IsTrue(_host.IsPhantomOut("devA"));
            Assert.IsTrue(_host.Abort(_now)); _net.Pump();
            Advance(0.5);
            Assert.IsFalse(_host.IsPhantomOut("devA"), "待機に戻ると消える");
            Assert.IsFalse(a.Tracker.IsPhantomOut.CurrentValue);
            Assert.IsTrue(_host.Start(_now, 6000, 2)); _net.Pump();
            Advance(0.5);
            Assert.IsFalse(_host.IsPhantomOut("devA"));
            Assert.IsFalse(a.Tracker.IsPhantomOut.CurrentValue);
        }

        [Test] public void 罰B_参加端末が全員退場したらその時点で結果へ進む()
        {
            Build(ThPhantomMode.PlayerOut);
            var a = AddClient(2, "devA"); var b = AddClient(3, "devB");
            StartPlaying();
            Spot("devA");
            Advance(0.3);
            Assert.AreEqual(RoundState.Playing, _host.Machine.State, "まだ 1 人残っている");
            Spot("devB");
            Advance(0.3);
            Assert.AreEqual(RoundState.Result, _host.Machine.State);
            Assert.IsTrue(a.Items.CurrentValue.Frozen && b.Items.CurrentValue.Frozen);
            Assert.AreEqual(RoundState.Result, a.Tracker.State.CurrentValue);
            Assert.IsNotNull(a.Tracker.Result);
            Assert.IsFalse(a.Tracker.Result.cleared, "目標額に届いていなければ失敗");
        }

        [Test] public void 罰B_もう1人が切断中なら戻ってくるかもしれないので回を終わらせない()
        {
            Build(ThPhantomMode.PlayerOut);
            AddClient(2, "devA"); AddClient(3, "devB");
            StartPlaying();
            _host.OnClientDisconnected(3, _now); _net.Pump();
            Spot("devA");
            Advance(0.5);
            Assert.IsTrue(_host.IsPhantomOut("devA"));
            Assert.AreEqual(RoundState.Playing, _host.Machine.State, "切断中の参加端末は「まだ遊んでいる」と数える");
        }

        [Test] public void 罰B_もう1人が見学に切り替えられていれば回を終える()
        {
            Build(ThPhantomMode.PlayerOut);
            AddClient(2, "devA"); AddClient(3, "devB");
            StartPlaying();
            Assert.IsTrue(_host.SetSpectator("devB", true, _now)); _net.Pump();
            Spot("devA");
            Advance(0.3);
            Assert.AreEqual(RoundState.Result, _host.Machine.State);
        }

        [Test] public void 罰B_もう1人が位置合わせ未確認に切り替えられていれば回を終える()
        {
            Build(ThPhantomMode.PlayerOut);
            AddClient(2, "devA"); AddClient(3, "devB");
            StartPlaying();
            Assert.IsTrue(_host.SetAlignmentConfirmed("devB", false, _now)); _net.Pump();
            Spot("devA");
            Advance(0.3);
            Assert.AreEqual(RoundState.Result, _host.Machine.State);
        }

        [Test] public void 罰B_見学がいても全員が見学か退場でなければ終わらない_退場がいなければ終わらない()
        {
            Build(ThPhantomMode.PlayerOut);
            AddClient(2, "devA"); AddClient(3, "devB");
            StartPlaying();
            Assert.IsTrue(_host.SetSpectator("devB", true, _now)); _net.Pump();
            Advance(0.5);
            Assert.AreEqual(RoundState.Playing, _host.Machine.State, "退場が 1 人もいないなら、見学だけでは終わらない");
        }

        [Test] public void 罰B_解放でMの片手予約が戻り判定役のFallingがそっと置かれる()
        {
            Build(ThPhantomMode.PlayerOut);
            var a = AddClient(2, "devA"); AddClient(3, "devB");
            StartPlaying();
            var m = _host.Items.Items.First(i => i.Kind == TreasureKind.M);
            int s = FirstS(a.Items.CurrentValue);
            Grab(a, s);
            a.Items.CurrentValue.OnHandReleased(s, _now); _net.Pump();
            var falling = _host.Items.Get(s);
            Assert.AreEqual(ItemState.Falling, falling.State);
            Assert.AreEqual("devA", falling.ControllerDevice);
            // M の片手予約は、端末の表を通さずホストの表へ直接入れる（端末は自分の申請でない承認を取りやめてしまうため）
            _host.Items.OnGrab("devA", 2, new ThGrabMsg { item = m.Index, reqId = 77, gen = m.Gen, kind = (int)TreasureKind.M });
            Assert.AreEqual(ItemState.Locked, m.State);
            Assert.AreEqual("devA", m.OwnerDevice);

            Spot("devA");

            Assert.AreEqual(ItemState.Resting, m.State, "M の片手予約は解除される");
            Assert.IsNull(m.OwnerDevice);
            Assert.AreEqual(ItemState.Placing, falling.State, "判定役の Falling はそっと置かれる");
            Assert.IsNull(falling.ControllerDevice);
        }

        [Test] public void 参加端末でない端末の報告は捨てる()
        {
            Build(ThPhantomMode.PlayerOut);
            AddClient(2, "devA");
            StartPlaying();
            var c = AddClient(4, "devC");   // 回の途中から入った端末は、この回は参加しない
            Assert.IsTrue(c.Tracker.WaitNextRound);
            Spot("devC");
            Assert.IsFalse(_host.IsPhantomOut("devC"));
            Assert.AreEqual(0, _punished.Count);
            Assert.AreEqual(RoundState.Playing, _host.Machine.State);
        }

        [Test] public void 罰B_1人参加なら退場で結果へ進む()
        {
            Build(ThPhantomMode.PlayerOut);
            var a = AddClient(2, "devA");
            StartPlaying();
            Spot("devA");
            Advance(0.3);
            Assert.AreEqual(RoundState.Result, _host.Machine.State);
        }

        [Test] public void 亡霊の切り替えは待機中だけで回の途中は変えない()
        {
            Build(ThPhantomMode.Off);
            var a = AddClient(2, "devA");
            Assert.IsTrue(_host.SetPhantomMode(ThPhantomMode.PlayerOut, _now));
            Assert.AreEqual(ThPhantomMode.PlayerOut, _host.PhantomMode.CurrentValue);
            Assert.AreEqual(ThPhantomMode.PlayerOut, _host.Settings.phantom);
            Assert.IsFalse(_host.SetPhantomMode((ThPhantomMode)9, _now), "不正値は受け付けない");
            Assert.IsTrue(_host.Start(_now, 5000, 1)); _net.Pump();
            Assert.IsFalse(_host.SetPhantomMode(ThPhantomMode.Off, _now), "回の途中は切り替えない");
            Assert.AreEqual(ThPhantomMode.PlayerOut, _host.Settings.phantom);
        }

        [Test] public void 設定は保存して読み直せる()
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "th_phantom_" + Guid.NewGuid().ToString("N"));
            try
            {
                ThSettingsFiles.SaveSettings(dir, new ThGameSettings { phantom = ThPhantomMode.TakeTreasure });
                var s = ThSettingsFiles.LoadSettings(dir, out var w);
                Assert.IsNull(w);
                Assert.AreEqual(ThPhantomMode.TakeTreasure, s.phantom);
            }
            finally { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); }
        }
    }
}

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    /// <summary>遺跡の亡霊: 罰 A の対象（ホストの状態表の単体）と、奪った後に遅れて届くメッセージ</summary>
    public class ThPhantomTakeTableTests
    {
        static readonly Bounds Zone = new Bounds(new Vector3(0, 0.9f, 0.8f), new Vector3(3f, 1.2f, 1f));

        readonly List<(Rpc rpc, object dto)> _out = new List<(Rpc, object)>();
        HostItemTable _t;

        // 0: S, 1: M, 2: S, 3: S, 4: M
        [SetUp]
        public void SetUp()
        {
            _out.Clear();
            var spawned = new List<SpawnedTreasure>();
            var kinds = new[] { TreasureKind.S, TreasureKind.M, TreasureKind.S, TreasureKind.S, TreasureKind.M };
            for (int i = 0; i < kinds.Length; i++)
                spawned.Add(new SpawnedTreasure { Index = i, Kind = kinds[i], Value = 300 + i * 100, LocalPosition = new Vector3(i, 0, 5), CatalogIndex = 0 });
            _t = new HostItemTable(spawned, Zone, (r, d) => _out.Add((r, d)));
        }

        void GrabS(int i, string dev = "devA", int req = 1) => _t.OnGrab(dev, 2, new ThGrabMsg { item = i, reqId = req, gen = _t.Get(i).Gen, kind = (int)TreasureKind.S });
        void LockM(int i, string dev = "devA", int req = 1) => _t.OnGrab(dev, 2, new ThGrabMsg { item = i, reqId = req, gen = _t.Get(i).Gen, kind = (int)TreasureKind.M });
        void LiftM(int i, string dev = "devA", int req = 1) => _t.OnLift(dev, 2, new ThLiftMsg { item = i, reqId = req, gen = _t.Get(i).Gen });

        [Test] public void Mを両手で持っている_Held_は消滅する()
        {
            LockM(1); LiftM(1);
            Assert.AreEqual(ItemState.Held, _t.Get(1).State);
            var taken = _t.TakeHeldBy("devA");
            Assert.AreEqual(1, taken.Count);
            Assert.AreEqual(1, taken[0].Item);
            Assert.AreEqual(_t.Get(1).Gen, taken[0].Gen);
            Assert.AreEqual(ItemState.Destroyed, _t.Get(1).State);
            Assert.IsTrue(_t.Get(1).Gone);
            Assert.AreEqual(0, _t.Get(1).Value);
        }

        [Test] public void Mの片手予約_Locked_は残る()
        {
            LockM(1);
            Assert.AreEqual(ItemState.Locked, _t.Get(1).State);
            Assert.AreEqual(0, _t.TakeHeldBy("devA").Count);
            Assert.AreEqual(ItemState.Locked, _t.Get(1).State);
            Assert.IsFalse(_t.Get(1).Gone);
        }

        [Test] public void Falling_回収済み_消滅済みは対象外_他の端末のお宝も残る()
        {
            // 0: Falling
            GrabS(0);
            _t.OnRelease("devA", new ThReleaseMsg { item = 0, reqId = 1, gen = _t.Get(0).Gen, reason = (int)ReleaseReason.Dropped });
            Assert.AreEqual(ItemState.Falling, _t.Get(0).State);
            // 2: 回収済み
            GrabS(2);
            _t.OnCollect("devA", new ThCollectMsg { item = 2, gen = _t.Get(2).Gen });
            Assert.AreEqual(ItemState.Collected, _t.Get(2).State);
            // 3: 消滅済み（3 回ぶつけた）
            GrabS(3);
            for (int k = 0; k < 3; k++) _t.OnDamage("devA", new ThDamageMsg { a = 3, b = -1, speed = 2f }, 10.0 + k * 5);
            Assert.AreEqual(ItemState.Destroyed, _t.Get(3).State);
            // 1: 他の端末が持っている
            _t.OnGrab("devB", 3, new ThGrabMsg { item = 1, reqId = 1, gen = 0, kind = (int)TreasureKind.M });
            _t.OnLift("devB", 3, new ThLiftMsg { item = 1, reqId = 1, gen = _t.Get(1).Gen });
            Assert.AreEqual(ItemState.Held, _t.Get(1).State);

            var taken = _t.TakeHeldBy("devA");
            Assert.AreEqual(0, taken.Count, "Falling・回収済み・消滅済みは対象外");
            Assert.AreEqual(ItemState.Held, _t.Get(1).State, "他の端末のお宝は奪わない");
            Assert.AreEqual(ItemState.Falling, _t.Get(0).State);
        }

        [Test] public void 奪った後に遅れて届くThDamage_ThRest_ThCollect_ThLiftは捨てられる()
        {
            GrabS(0);
            LockM(1); LiftM(1);
            int g0 = _t.Get(0).Gen, g1 = _t.Get(1).Gen;
            var taken = _t.TakeHeldBy("devA");
            Assert.AreEqual(2, taken.Count);
            _out.Clear();

            _t.OnDamage("devA", new ThDamageMsg { a = 0, b = -1, speed = 3f }, 50.0);
            _t.OnDamage("devA", new ThDamageMsg { a = 1, b = 0, speed = 3f }, 51.0);
            _t.OnRest("devA", new ThRestMsg { item = 0, gen = g0, pos = Vector3.zero }, 52.0);
            _t.OnCollect("devA", new ThCollectMsg { item = 0, gen = g0 }, 53.0);
            _t.OnCollect("devA", new ThCollectMsg { item = 1, gen = g1 }, 53.0);
            _t.OnLift("devA", 2, new ThLiftMsg { item = 1, reqId = 1, gen = g1 });

            Assert.AreEqual(0, _t.Total, "回収額に入らない");
            Assert.AreEqual(0, _t.DamageTotal, "減額も数えない");
            foreach (int i in new[] { 0, 1 }) { Assert.AreEqual(ItemState.Destroyed, _t.Get(i).State); Assert.AreEqual(0, _t.Get(i).Value); Assert.IsTrue(_t.Get(i).Gone); }
            Assert.IsFalse(_out.Any(o => o.rpc == Rpc.ThCollected || o.rpc == Rpc.ThDamaged || o.rpc == Rpc.ThLifted || o.rpc == Rpc.ThFreed));
        }

        [Test] public void 練習用の表は奪わない()
        {
            var spawned = new List<SpawnedTreasure> { new SpawnedTreasure { Index = 100, Kind = TreasureKind.S, Value = 150, LocalPosition = Vector3.zero, CatalogIndex = -1 } };
            var table = new HostItemTable(spawned, Zone, (r, d) => { }, practiceTable: true);
            table.Items[0].PracticeDevice = "devA";
            table.OnGrab("devA", 2, new ThGrabMsg { item = 100, reqId = 1, gen = 0, kind = (int)TreasureKind.S });
            Assert.AreEqual(ItemState.Held, table.Get(100).State);
            Assert.AreEqual(0, table.TakeHeldBy("devA").Count);
        }

        [Test] public void 退場した端末のつかむ申請と持ち上げは否認される()
        {
            _t.DeviceBlocked = d => d == "devA";
            GrabS(0);
            Assert.AreEqual(ItemState.Resting, _t.Get(0).State);
            LockM(1);
            Assert.AreEqual(ItemState.Resting, _t.Get(1).State);
            Assert.AreEqual(2, _out.Count(o => o.rpc == Rpc.ThGrabDenied));
            GrabS(0, "devB", 5);
            Assert.AreEqual(ItemState.Held, _t.Get(0).State, "他の端末は持てる");
        }
    }

    /// <summary>遺跡の亡霊の見た目（PhantomView）: 非表示→再表示を繰り返しても揺れの基準の高さが変わらない</summary>
    public class ThPhantomViewTests
    {
        [Test] public void 再表示を繰り返しても揺れの基準の高さは変わらない()
        {
            var root = new GameObject("PhantomViewTest");
            try
            {
                var body = new GameObject("BodyRoot");
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = new Vector3(0f, 0.25f, 0f);
                var view = root.AddComponent<PhantomView>();
                var so = new UnityEditor.SerializedObject(view);
                so.FindProperty("_bodyRoot").objectReferenceValue = body.transform;
                so.ApplyModifiedPropertiesWithoutUndo();
                // EditMode では Unity が Awake / OnEnable / OnDisable を呼ばないので、直接呼んで表示・非表示を再現する
                void Call(string name) => typeof(PhantomView).GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(view, null);
                Call("Awake"); Call("OnEnable");
                float baseY = view.BodyBaseY;
                Assert.AreEqual(0.25f, baseY, 1e-5f);
                for (int i = 0; i < 6; i++)
                {
                    body.transform.localPosition = new Vector3(0f, baseY + 0.05f, 0f);   // 揺れの途中で止まった想定
                    Call("OnDisable");
                    Assert.AreEqual(baseY, body.transform.localPosition.y, 1e-5f, "非表示で基準へ戻る");
                    body.transform.localPosition = new Vector3(0f, baseY + 0.07f, 0f);
                    Call("OnEnable");
                    Assert.AreEqual(baseY, view.BodyBaseY, 1e-5f, "基準は上がっていかない");
                    Assert.AreEqual(baseY, body.transform.localPosition.y, 1e-5f, "再表示は基準から始まる");
                    Assert.IsTrue(view.IsBobbing);
                }
                Call("OnDisable");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
