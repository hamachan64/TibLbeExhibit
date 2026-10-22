using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    /// <summary>オープニング・保留・本編開始・結果（#42）</summary>
    public class ThOpeningResultTests
    {
        // ---- ホストの進行（保留・本編開始） ----

        static ThProgressParams Times(float hold = 10f) => new ThProgressParams { openingSeconds = 3, practiceSeconds = 10, holdMaxSeconds = hold, playingSeconds = 20, resultSeconds = 3 };

        static HostRoundMachine InTutorial(out double now, float hold = 10f)
        {
            var reg = new DeviceRegistry { HostParamsVersion = "v" };
            reg.Connect("A", 2); reg.ReportParamsVersion("A", "v"); reg.ReportCatalogHash("A", "");
            var m = new HostRoundMachine(reg, Times(hold));
            now = 100;
            Assert.IsTrue(m.TryStart(now, 1000, 1));
            now += 3.01;
            Assert.IsTrue(m.Tick(now));
            Assert.AreEqual(RoundState.Tutorial, m.State);
            return m;
        }

        [Test] public void 保留で残り秒が止まり解除で進む()
        {
            var m = InTutorial(out var now);
            now += 2; Assert.AreEqual(8f, m.RemainingSeconds(now), 0.01f);
            int seq = m.Seq;
            Assert.IsTrue(m.TryHold(now));
            Assert.AreEqual(seq + 1, m.Seq);
            now += 5; Assert.AreEqual(8f, m.RemainingSeconds(now), 0.01f);
            Assert.IsTrue(m.TryReleaseHold(now));
            Assert.AreEqual(seq + 2, m.Seq);
            now += 1; Assert.AreEqual(7f, m.RemainingSeconds(now), 0.01f);
        }

        [Test] public void 保留中は時間がたってもPlayingに進まず解除後に残りで進む()
        {
            var m = InTutorial(out var now);
            now += 2; m.TryHold(now);
            now += 8; Assert.IsFalse(m.Tick(now)); Assert.AreEqual(RoundState.Tutorial, m.State);   // 保留は 8 秒（上限 10 内）
            m.TryReleaseHold(now);
            now += 7.9; Assert.IsFalse(m.Tick(now));
            now += 0.2; Assert.IsTrue(m.Tick(now)); Assert.AreEqual(RoundState.Playing, m.State);
        }

        [Test] public void 上限で自動解除されTickがtrueを返しSeqが進み残りの練習時間から進む()
        {
            var m = InTutorial(out var now, 4f);
            now += 2; m.TryHold(now);   // 残り 8
            int seq = m.Seq;
            now += 4.5;                  // 上限 4 秒を 0.5 超えた
            Assert.IsTrue(m.Tick(now));
            Assert.IsFalse(m.IsHeld);
            Assert.AreEqual(seq + 1, m.Seq);
            Assert.AreEqual(7.5f, m.RemainingSeconds(now), 0.01f, "上限の時刻で解除したことにして、そこから数え直す");
            Assert.AreEqual(4f, m.HeldSeconds(now), 0.01f);
            Assert.AreEqual(0f, m.HoldRemainingSeconds(now), 0.01f);
        }

        [Test] public void 解除して押し直せるが合計は上限まで()
        {
            var m = InTutorial(out var now, 5f);
            m.TryHold(now); now += 3; m.TryReleaseHold(now);
            Assert.IsTrue(m.CanHold(now));
            Assert.AreEqual(2f, m.HoldRemainingSeconds(now), 0.01f);
            m.TryHold(now); now += 10; m.Tick(now);   // 2 秒で自動解除
            Assert.AreEqual(5f, m.HeldSeconds(now), 0.01f);
            Assert.IsFalse(m.CanHold(now)); Assert.IsFalse(m.TryHold(now));
        }

        [Test] public void 上限0なら保留できない()
        {
            var m = InTutorial(out var now, 0f);
            Assert.IsFalse(m.CanHold(now)); Assert.IsFalse(m.TryHold(now));
        }

        [Test] public void Tutorial以外では保留も本編開始もできない()
        {
            var reg = new DeviceRegistry { HostParamsVersion = "v" };
            reg.Connect("A", 2); reg.ReportParamsVersion("A", "v"); reg.ReportCatalogHash("A", "");
            var m = new HostRoundMachine(reg, Times());
            Assert.IsFalse(m.TryHold(0)); Assert.IsFalse(m.TryBeginPlay(0)); Assert.IsFalse(m.TryReleaseHold(0));   // Waiting
            m.TryStart(0, 1, 1);
            Assert.IsFalse(m.TryHold(1)); Assert.IsFalse(m.TryBeginPlay(1));   // Opening
            m.Tick(4); m.Tick(4 + 10.1);   // Playing
            Assert.AreEqual(RoundState.Playing, m.State);
            Assert.IsFalse(m.TryHold(15)); Assert.IsFalse(m.TryBeginPlay(15));
        }

        [Test] public void 本編開始は保留中でも即Playingで参加端末が確定する()
        {
            var reg = new DeviceRegistry { HostParamsVersion = "v" };
            reg.Connect("A", 2); reg.ReportParamsVersion("A", "v"); reg.ReportCatalogHash("A", "");
            reg.Connect("B", 3); reg.ReportParamsVersion("B", "v"); reg.ReportCatalogHash("B", "");
            var m = new HostRoundMachine(reg, Times());
            m.TryStart(0, 1, 1); m.Tick(3.1);
            m.TryHold(4);
            reg.Disconnect(3);   // 練習中に B が切れた
            int seq = m.Seq;
            Assert.IsTrue(m.TryBeginPlay(5));
            Assert.AreEqual(RoundState.Playing, m.State);
            Assert.AreEqual(seq + 1, m.Seq);
            Assert.IsFalse(m.IsHeld);
            CollectionAssert.AreEqual(new[] { "A" }, m.Participants);
            Assert.AreEqual(1f, m.HeldSeconds(5), 0.01f, "本編開始までの保留は合計に入る");
        }

        [Test] public void 保留の合計は回をまたいで残らない()
        {
            var m = InTutorial(out var now);
            m.TryHold(now); now += 3; m.TryReleaseHold(now);
            Assert.IsTrue(m.WasHeld);
            m.Abort(now);
            Assert.AreEqual(3f, m.HeldSeconds(now), 0.01f, "次の開始まで読める");
            m.TryStart(now, 2000, 1);
            Assert.AreEqual(0f, m.HeldSeconds(now)); Assert.IsFalse(m.WasHeld);
        }

        // ---- 目標額・結果（ホスト） ----

        [Test] public void 目標額は参加人数の行で人数は丸める()
        {
            var p = new ThParams();
            Assert.AreEqual(p.layout.rows[0].targetAmount, SessionHostCore.TargetFor(0, p));
            Assert.AreEqual(p.layout.rows[0].targetAmount, SessionHostCore.TargetFor(1, p));
            Assert.AreEqual(p.layout.rows[2].targetAmount, SessionHostCore.TargetFor(3, p));
            Assert.AreEqual(p.layout.rows[3].targetAmount, SessionHostCore.TargetFor(9, p));
        }

        // ---- 端末の追従 ----

        static ThStateMsg St(int seq, RoundState s, float remaining, bool held = false, int target = 0, ThResultMsg result = null) =>
            new ThStateMsg { seq = seq, state = (int)s, remaining = remaining, participants = new[] { "me" }, held = held, target = target, result = result ?? new ThResultMsg() };

        static ClientRoundTracker NewTracker() => new ClientRoundTracker("me", Times(), 0);

        [Test] public void 端末_heldのThStateで残り秒が止まり解除で再開する()
        {
            var t = NewTracker();
            t.OnState(5, St(1, RoundState.Opening, 3), 0);
            t.OnState(5, St(2, RoundState.Tutorial, 10), 1);
            t.Tick(3); Assert.AreEqual(8f, t.RemainingSeconds.CurrentValue, 0.01f);
            t.OnState(5, St(3, RoundState.Tutorial, 8, held: true), 3);
            Assert.IsTrue(t.IsHeld.CurrentValue);
            t.Tick(9); Assert.AreEqual(8f, t.RemainingSeconds.CurrentValue, 0.01f);
            t.OnState(5, St(4, RoundState.Tutorial, 8), 9);
            Assert.IsFalse(t.IsHeld.CurrentValue);
            t.Tick(10); Assert.AreEqual(7f, t.RemainingSeconds.CurrentValue, 0.01f);
        }

        [Test] public void 端末_Heartbeatのheldで残り秒が止まり解除で再開する()
        {
            var t = NewTracker();
            t.OnState(5, St(1, RoundState.Opening, 3), 0);
            t.OnState(5, St(2, RoundState.Tutorial, 10), 1);
            t.OnHeartbeat(5, new ThHeartbeatMsg { seq = 2, remaining = 8, held = true }, 3);
            Assert.IsTrue(t.IsHeld.CurrentValue);
            t.Tick(8); Assert.AreEqual(8f, t.RemainingSeconds.CurrentValue, 0.01f);
            t.OnHeartbeat(5, new ThHeartbeatMsg { seq = 2, remaining = 8, held = false }, 8);
            Assert.IsFalse(t.IsHeld.CurrentValue);
            t.Tick(9); Assert.AreEqual(7f, t.RemainingSeconds.CurrentValue, 0.01f);
        }

        [Test] public void 端末_目標額と結果を読め待機と新しい回で消える()
        {
            var t = NewTracker();
            t.OnState(5, St(1, RoundState.Opening, 3), 0);
            Assert.AreEqual(0, t.Target); Assert.IsNull(t.Result);
            t.OnState(5, St(2, RoundState.Playing, 20, target: 2500), 1);
            Assert.AreEqual(2500, t.Target);
            t.OnState(5, St(3, RoundState.Result, 3, target: 2500, result: new ThResultMsg { present = true, collected = 3000, target = 2500, cleared = true }), 2);
            Assert.AreEqual(3000, t.Result.collected); Assert.IsTrue(t.Result.cleared);
            t.OnState(0, St(4, RoundState.Waiting, 0), 3);
            Assert.AreEqual(0, t.Target); Assert.IsNull(t.Result);
            t.OnState(6, St(1, RoundState.Opening, 3), 4);
            Assert.AreEqual(0, t.Target); Assert.IsNull(t.Result);
        }

        [Test] public void 古い送り手の既定値では今の動きが変わらない()
        {
            var old = JsonUtility.FromJson<ThStateMsg>("{\"seq\":2,\"state\":2,\"remaining\":9,\"participants\":[\"me\"]}");
            Assert.IsFalse(old.held); Assert.AreEqual(0, old.target); Assert.IsFalse(old.result.present);
            var t = NewTracker();
            t.OnState(5, St(1, RoundState.Opening, 3), 0);
            t.OnState(5, old, 1);
            t.Tick(3); Assert.AreEqual(7f, t.RemainingSeconds.CurrentValue, 0.01f);
            Assert.IsNull(t.Result);
            var hb = JsonUtility.FromJson<ThHeartbeatMsg>("{\"seq\":2,\"remaining\":9}");
            Assert.IsFalse(hb.held);
        }

        [Test] public void メッセージはJSONの往復でheldとtargetとresultを保つ()
        {
            var m = St(1, RoundState.Result, 3, true, 2500, new ThResultMsg { present = true, collected = 100, target = 2500, cleared = false });
            Assert.IsTrue(ThMessages.TryDecode(ThMessages.Encode(7, m), out long r, out ThStateMsg back));
            Assert.AreEqual(7, r); Assert.IsTrue(back.held); Assert.AreEqual(2500, back.target);
            Assert.IsTrue(back.result.present); Assert.AreEqual(100, back.result.collected); Assert.IsFalse(back.result.cleared);
            Assert.IsTrue(ThMessages.TryDecode(ThMessages.Encode(7, St(1, RoundState.Playing, 3)), out long _, out ThStateMsg b2));
            Assert.IsFalse(b2.result.present);
        }

        // ---- 面の判断 ----

        static ThPanelInput In(RoundState s, float remaining = 0, int target = 0, int total = 0, long round = 1, bool wait = false, ThResultMsg res = null) =>
            new ThPanelInput { State = s, RoundId = round, Remaining = remaining, OpeningSeconds = 10, Target = target, Total = total, WaitNextRound = wait, Result = res };

        [Test] public void 面の内容は状態ごとに決まる()
        {
            var p = new ThPanelPresenter();
            Assert.AreEqual(ThPanelKind.Waiting, p.Update(In(RoundState.Waiting, round: 0), 0).Kind);
            Assert.AreEqual(ThPanelKind.Opening, p.Update(In(RoundState.Opening, 10), 0).Kind);
            Assert.AreEqual(ThPanelKind.None, p.Update(In(RoundState.Tutorial, 20), 0).Kind);
            Assert.AreEqual(ThPanelKind.Playing, p.Update(In(RoundState.Playing, 180, 2500), 0).Kind);
            Assert.AreEqual(ThPanelKind.None, p.Update(In(RoundState.Result, 10, res: null), 0).Kind, "結果が無ければ何も出さない");
            Assert.AreEqual(ThPanelKind.Result, p.Update(In(RoundState.Result, 10, res: new ThResultMsg { present = true }), 0).Kind);
        }

        [Test] public void 次の回を待つ端末はOpeningからResultまで何も出さない()
        {
            var p = new ThPanelPresenter();
            foreach (var s in new[] { RoundState.Opening, RoundState.Tutorial, RoundState.Playing })
                Assert.AreEqual(ThPanelKind.None, p.Update(In(s, 5, 100, wait: true), 0).Kind);
            Assert.AreEqual(ThPanelKind.None, p.Update(In(RoundState.Result, 5, wait: true, res: new ThResultMsg { present = true }), 0).Kind);
            Assert.AreEqual(ThPanelKind.Waiting, p.Update(In(RoundState.Waiting, round: 0), 0).Kind);
        }

        [Test] public void 字幕の番号はオープニングの5等分で進み各枚の始まりで合図が出る()
        {
            var expect = new[] { (10f, 1), (8.1f, 1), (7.9f, 2), (5.9f, 3), (3.9f, 4), (1.9f, 5), (0f, 5) };
            var p = new ThPanelPresenter();
            foreach (var (rem, line) in expect) Assert.AreEqual(line, ThPanelPresenter.OpeningLineIndex(rem, 10), rem.ToString());
            var started = new List<int>(); int sfx = 0;
            foreach (var rem in new[] { 10f, 9f, 7.9f, 7f, 5.9f, 3.9f, 1.9f, 0.5f })
            {
                var f = p.Update(In(RoundState.Opening, rem), 0);
                if (f.LineStarted > 0) started.Add(f.LineStarted);
                if (f.OpeningStarted) sfx++;
            }
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, started);
            Assert.AreEqual(1, sfx, "オープニングの効果音は回の始まりに 1 回");
        }

        [Test] public void 時間合わせで残り秒が戻っても字幕は戻らずナレーションを2回鳴らさない()
        {
            var p = new ThPanelPresenter();
            var started = new List<int>();
            foreach (var rem in new[] { 10f, 7.9f, 8.2f, 7.5f, 5.9f })   // 7.9 → 8.2 は Heartbeat の時間合わせで戻った
            {
                var f = p.Update(In(RoundState.Opening, rem), 0);
                if (rem == 8.2f) Assert.AreEqual(2, f.OpeningLine, "戻っても 2 枚目のまま");
                if (f.LineStarted > 0) started.Add(f.LineStarted);
            }
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, started);
        }

        [Test] public void 目標達成の合図は目標額をまたいだとき1回だけ()
        {
            var p = new ThPanelPresenter();
            Assert.IsFalse(p.Update(In(RoundState.Playing, 100, 500, 0), 0).TargetReachedNow);
            Assert.IsFalse(p.Update(In(RoundState.Playing, 99, 500, 400), 1).TargetReachedNow);
            var f = p.Update(In(RoundState.Playing, 98, 500, 600), 2);
            Assert.IsTrue(f.TargetReachedNow); Assert.IsTrue(f.ShowTargetReached);
            f = p.Update(In(RoundState.Playing, 97, 500, 900), 3);
            Assert.IsFalse(f.TargetReachedNow); Assert.IsTrue(f.ShowTargetReached);
            f = p.Update(In(RoundState.Playing, 90, 500, 900), 2 + ThPanelPresenter.TargetReachedSeconds + 0.1);
            Assert.IsFalse(f.ShowTargetReached, "数秒で帯に戻る");
            Assert.IsFalse(p.Update(In(RoundState.Playing, 80, 500, 1500), 20).TargetReachedNow, "その回 1 回だけ");
        }

        [Test] public void 目標達成は結果に入った後と既に届いていた値を後から知ったときと目標額が未着の間は出さない()
        {
            var p = new ThPanelPresenter();
            Assert.IsFalse(p.Update(In(RoundState.Playing, 100, 0, 0), 0).TargetReachedNow, "目標額が未着の間は判断しない");
            Assert.IsFalse(p.Update(In(RoundState.Playing, 99, 500, 900), 1).TargetReachedNow, "既に届いていた値を初めて知った");
            Assert.IsFalse(p.Update(In(RoundState.Playing, 98, 500, 1200), 2).TargetReachedNow);
            var f = p.Update(In(RoundState.Result, 5, 500, 1200, res: new ThResultMsg { present = true, collected = 1200, target = 500, cleared = true }), 3);
            Assert.IsFalse(f.TargetReachedNow); Assert.IsFalse(f.ShowTargetReached);
            // 次の回では、また下回ってから届けば出る
            Assert.IsFalse(p.Update(In(RoundState.Playing, 100, 500, 0, round: 2), 10).TargetReachedNow);
            Assert.IsTrue(p.Update(In(RoundState.Playing, 99, 500, 500, round: 2), 11).TargetReachedNow);
        }

        [Test] public void 残り時間は切り上げで分と秒にする()
        {
            void Check(float rem, int m, int s) { ThPanelPresenter.SplitRemaining(rem, out var mm, out var ss); Assert.AreEqual((m, s), (mm, ss), rem.ToString()); }
            Check(135f, 2, 15); Check(134.2f, 2, 15); Check(180f, 3, 0); Check(60f, 1, 0); Check(59.01f, 1, 0);
            Check(0.1f, 0, 1); Check(0f, 0, 0); Check(-1f, 0, 0);
        }

        [Test] public void 面の文字は文言の表から組み立てる()
        {
            var set = (ThContentSet)UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/_Content/TreasureHaul/Settings/ThContentSet.asset", typeof(ThContentSet));
            var t = set.text;
            var w = ThPanelPresenter.Compose(new ThPanelFrame { Kind = ThPanelKind.Waiting }, t);
            Assert.AreEqual(t.Get(ThTextId.GoalSentence), w.Main); Assert.AreEqual(t.Get(ThTextId.GripBeam), w.Sub); Assert.AreEqual(t.Get(ThTextId.Credits), w.Credit);
            Assert.AreEqual("みんなで、目標額を目指そう！", ThPanelPresenter.Compose(new ThPanelFrame { Kind = ThPanelKind.Opening, OpeningLine = 5 }, t).Main);
            Assert.AreEqual("残り 2:05 ／ 目標 2,500G", ThPanelPresenter.Compose(new ThPanelFrame { Kind = ThPanelKind.Playing, RemainMinutes = 2, RemainSeconds = 5, Target = 2500 }, t).Main);
            var big = ThPanelPresenter.Compose(new ThPanelFrame { Kind = ThPanelKind.Playing, ShowTargetReached = true }, t);
            Assert.AreEqual("目標達成！", big.Main); Assert.IsTrue(big.Large);
            var ok = ThPanelPresenter.Compose(new ThPanelFrame { Kind = ThPanelKind.Result, Result = new ThResultMsg { present = true, collected = 3000, target = 2500, cleared = true } }, t);
            Assert.AreEqual("3,000G ／ 目標 2,500G", ok.Main); Assert.AreEqual("クリア！", ok.Sub);
            var ng = ThPanelPresenter.Compose(new ThPanelFrame { Kind = ThPanelKind.Result, Result = new ThResultMsg { present = true, collected = 2000, target = 2500, cleared = false } }, t);
            Assert.AreEqual("あと 500G でクリア", ng.Sub);
            Assert.AreEqual(ThSoundId.OpeningLine3, ThPanelPresenter.LineSound(3));
        }

        // ---- ホストと端末のつなぎ合わせ ----

        const int HostNo = 1;
        double _now;
        FakeNetwork _net;
        SessionHostCore _host;
        readonly Dictionary<int, string> _resolve = new Dictionary<int, string>();
        readonly List<SessionClientCore> _clients = new List<SessionClientCore>();

        static ThTreasureSetup MakeSetup()
        {
            var pts = new List<PlacementPoint>();
            for (int i = 0; i < 30; i++) pts.Add(new PlacementPoint(new Vector3((i % 5) * 0.75f - 1.5f, 0, 3f + i / 5), i % 3));
            return new ThTreasureSetup
            {
                Points = pts,
                Catalog = new[] { new SpawnCatalogEntry(TreasureKind.S, 0), new SpawnCatalogEntry(TreasureKind.S, 1), new SpawnCatalogEntry(TreasureKind.M, 2) },
                CollectBounds = new Bounds(new Vector3(0, 0.9f, 0.8f), new Vector3(3f, 1.2f, 1f)),
                CatalogHash = "h1",
            };
        }

        [SetUp]
        public void SetUp()
        {
            _now = 100;
            _net = new FakeNetwork();
            var node = _net.Add(HostNo);
            var p = new ThParams();
            p.progress.openingSeconds = 3; p.progress.practiceSeconds = 6; p.progress.holdMaxSeconds = 10; p.progress.playingSeconds = 30; p.progress.resultSeconds = 3;
            p.layout.rows[0].targetAmount = 100; p.layout.rows[1].targetAmount = 100;
            _host = new SessionHostCore(node, ThParamsLoader.Load(p, null, null), new ThGameSettings(), null, 0, no => _resolve.TryGetValue(no, out var v) ? v : null, MakeSetup());
            _host.OwnClientNo = HostNo;
            node.OnReceive = (rpc, sender, args) => _host.OnRpc(rpc, sender, args, _now);
            _clients.Clear();
        }

        [TearDown] public void TearDown() { _host.Dispose(); foreach (var c in _clients) c.Dispose(); }

        SessionClientCore AddClient(int no, string id)
        {
            var node = _net.Add(no);
            var c = new SessionClientCore(id, node, new ThParams(), null, _now, MakeSetup()) { OwnClientNo = no };
            node.OnReceive = (rpc, sender, args) => c.OnRpc(rpc, sender, args, _now);
            _clients.Add(c); _resolve[no] = id;
            c.OnReady(_now); _net.Pump();
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

        [Test] public void つなぎ合わせ_保留から本編開始から回収から結果まで全端末で一致する()
        {
            var a = AddClient(2, "devA");
            var b = AddClient(3, "devB");
            Assert.IsTrue(_host.Start(_now, 5000, 7)); _net.Pump();
            Advance(3.3);
            foreach (var c in new[] { a, b }) Assert.AreEqual(RoundState.Tutorial, c.Tracker.State.CurrentValue);

            Advance(1);
            Assert.IsTrue(_host.Hold(_now)); _net.Pump();
            Assert.IsTrue(a.Tracker.IsHeld.CurrentValue && b.Tracker.IsHeld.CurrentValue);
            float frozen = a.Tracker.RemainingSeconds.CurrentValue;
            Advance(3);
            Assert.AreEqual(frozen, a.Tracker.RemainingSeconds.CurrentValue, 0.01f);
            Assert.AreEqual(frozen, b.Tracker.RemainingSeconds.CurrentValue, 0.01f);
            Assert.IsTrue(_host.Machine.IsHeld); Assert.IsTrue(_host.IsHeld.CurrentValue);
            Assert.IsTrue(_host.ReleaseHold(_now)); _net.Pump();
            Advance(1);
            Assert.IsFalse(a.Tracker.IsHeld.CurrentValue);
            Assert.AreEqual(frozen - 1f, a.Tracker.RemainingSeconds.CurrentValue, 0.3f, "解除で再開");

            Assert.IsTrue(_host.BeginPlay(_now)); _net.Pump();
            Advance(0.3);
            foreach (var c in new[] { a, b })
            {
                Assert.AreEqual(RoundState.Playing, c.Tracker.State.CurrentValue);
                Assert.AreEqual(100, c.Tracker.Target, "2 人の行の目標額");
            }
            Assert.AreEqual(100, _host.Target.CurrentValue);

            // 回収（端末の申請を経由せず、ホストの状態表に直接入れる）
            var items = _host.Items;
            int sIdx = System.Linq.Enumerable.First(items.Items, it => it.Kind == TreasureKind.S).Index;   // 番号は置き場所のシャッフル順なので、S のお宝を探す
            items.OnGrab("devA", 2, new ThGrabMsg { item = sIdx, reqId = 1, gen = 0, kind = 0 });
            items.OnRelease("devA", new ThReleaseMsg { item = sIdx, reqId = 1, gen = 1, reason = 1 });
            items.OnCollect("devA", new ThCollectMsg { item = sIdx, gen = 1 });
            _net.Pump();
            Advance(0.3);
            Assert.GreaterOrEqual(_host.TotalAmount, 100);
            Assert.AreEqual(_host.TotalAmount, a.Total.CurrentValue);
            Assert.GreaterOrEqual(a.Total.CurrentValue, a.Tracker.Target, "合計が目標以上");

            Advance(30);
            Assert.AreEqual(RoundState.Result, a.Tracker.State.CurrentValue);
            foreach (var c in new[] { a, b })
            {
                Assert.IsNotNull(c.Tracker.Result);
                Assert.AreEqual(_host.TotalAmount, c.Tracker.Result.collected);
                Assert.AreEqual(100, c.Tracker.Result.target);
                Assert.IsTrue(c.Tracker.Result.cleared);
            }
            Advance(3.3);
            foreach (var c in new[] { a, b }) { Assert.AreEqual(RoundState.Waiting, c.Tracker.State.CurrentValue); Assert.IsNull(c.Tracker.Result); Assert.AreEqual(0, c.Tracker.Target); }
            Assert.AreEqual(0, _host.Target.CurrentValue);
        }

        [Test] public void つなぎ合わせ_届かなかった結果はクリアでなく不足額が出せる()
        {
            var a = AddClient(2, "devA");
            _host.Start(_now, 5000, 7); _net.Pump();
            Advance(3.3); Advance(6.3);
            Assert.AreEqual(RoundState.Playing, a.Tracker.State.CurrentValue);
            Advance(30.3);
            Assert.AreEqual(RoundState.Result, a.Tracker.State.CurrentValue);
            Assert.IsFalse(a.Tracker.Result.cleared);
            Assert.AreEqual(0, a.Tracker.Result.collected);
            Assert.AreEqual(100, a.Tracker.Result.target);
        }

        // ---- レビュー対応（#42 差し戻し1） ----

        [Test] public void 面_次の回を待つ端末は待機でも何も出さず本当の待機を受けたら出す()
        {
            var p = new ThPanelPresenter();
            Assert.AreEqual(ThPanelKind.None, p.Update(In(RoundState.Waiting, round: 0, wait: true), 0).Kind);
            Assert.AreEqual(ThPanelKind.Waiting, p.Update(In(RoundState.Waiting, round: 0, wait: false), 1).Kind);
        }

        [Test] public void 面_字幕の番号が範囲外でも文言を引ける()
        {
            var set = (ThContentSet)UnityEditor.AssetDatabase.LoadAssetAtPath("Assets/_Content/TreasureHaul/Settings/ThContentSet.asset", typeof(ThContentSet));
            Assert.DoesNotThrow(() => ThPanelPresenter.Compose(new ThPanelFrame { Kind = ThPanelKind.Opening, OpeningLine = 0 }, set.text));
            Assert.DoesNotThrow(() => ThPanelPresenter.Compose(new ThPanelFrame { Kind = ThPanelKind.Opening, OpeningLine = 9 }, set.text));
        }

        [Test] public void 保留は練習の残り秒が0のときは始められない()
        {
            var m = InTutorial(out var now);
            Assert.IsTrue(m.CanHold(now));
            Assert.IsFalse(m.CanHold(now + 10.0), "残りが 0 以下");
        }

        [Test] public void つなぎ合わせ_保留のThStateを落としても再送間隔の後に追いつきHeartbeatで上書きされない()
        {
            var a = AddClient(2, "devA");
            _host.Start(_now, 5000, 7); _net.Pump();
            Advance(3.3);
            Advance(1);
            _net.Drop = (sender, rpc, args, to) => to == 2 && rpc == Rpc.ThState;
            Assert.IsTrue(_host.Hold(_now)); _net.Pump();
            _net.Drop = null;
            Assert.IsFalse(a.Tracker.IsHeld.CurrentValue, "取りこぼした");
            float prev = a.Tracker.RemainingSeconds.CurrentValue;
            bool caught = false;
            for (int i = 0; i < 20; i++)
            {
                Advance(0.1);
                float cur = a.Tracker.RemainingSeconds.CurrentValue;
                if (!caught && !a.Tracker.IsHeld.CurrentValue) Assert.LessOrEqual(cur, prev + 1e-3f, "追いつく前に Heartbeat で残り秒が戻らない");
                if (a.Tracker.IsHeld.CurrentValue) caught = true;
                prev = cur;
            }
            Assert.IsTrue(caught);
            Assert.AreEqual(_host.Machine.RemainingSeconds(_now), a.Tracker.RemainingSeconds.CurrentValue, 0.3f);
            // 解除の ThState を落とした場合
            _net.Drop = (sender, rpc, args, to) => to == 2 && rpc == Rpc.ThState;
            Assert.IsTrue(_host.ReleaseHold(_now)); _net.Pump();
            _net.Drop = null;
            Assert.IsTrue(a.Tracker.IsHeld.CurrentValue, "取りこぼした");
            Advance(2);
            Assert.IsFalse(a.Tracker.IsHeld.CurrentValue);
            Assert.AreEqual(_host.Machine.RemainingSeconds(_now), a.Tracker.RemainingSeconds.CurrentValue, 0.3f);
        }

        [Test] public void つなぎ合わせ_Resultに入った後に合計が変わってもresultは変わらない()
        {
            var a = AddClient(2, "devA");
            _host.Start(_now, 5000, 7); _net.Pump();
            Advance(3.3); Advance(6.3);
            var items = _host.Items;
            int sIdx = System.Linq.Enumerable.First(items.Items, it => it.Kind == TreasureKind.S).Index;   // 番号は置き場所のシャッフル順なので、S のお宝を探す
            items.OnGrab("devA", 2, new ThGrabMsg { item = sIdx, reqId = 1, gen = 0, kind = 0 });
            items.OnRelease("devA", new ThReleaseMsg { item = sIdx, reqId = 1, gen = 1, reason = 1 });
            items.OnCollect("devA", new ThCollectMsg { item = sIdx, gen = 1 });
            Advance(30.3);
            Assert.AreEqual(RoundState.Result, a.Tracker.State.CurrentValue);
            int collected = a.Tracker.Result.collected;
            Assert.Greater(collected, 0);
            // 結果に入った後に、ホストの合計が変わる
            typeof(HostItemTable).GetField("<Total>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(items, collected + 700);
            Assert.AreNotEqual(collected, _host.TotalAmount, "テストの前提: 合計が変わった");
            // 後から入った端末へ ThState を送り直させても、結果は同じ
            var c = AddClient(4, "devC");
            Assert.IsNotNull(c.Tracker.Result);
            Assert.AreEqual(collected, c.Tracker.Result.collected);
            Assert.AreEqual(collected, a.Tracker.Result.collected);
        }
    }
}
