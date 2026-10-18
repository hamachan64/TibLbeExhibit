using System.Collections.Generic;
using NUnit.Framework;

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    public class RoundIdGeneratorTests
    {
        [Test] public void 時刻が進めばその時刻() => Assert.AreEqual(2000, RoundIdGenerator.Next(2000, 1000));
        [Test] public void 同じミリ秒なら前の値より1大きい() => Assert.AreEqual(1001, RoundIdGenerator.Next(1000, 1000));
        [Test] public void 時計が巻き戻っても前の値より1大きい() => Assert.AreEqual(1501, RoundIdGenerator.Next(900, 1500));
        [Test] public void 連続で呼ぶと必ず増える()
        {
            long prev = 0;
            for (int i = 0; i < 100; i++)
            {
                long next = RoundIdGenerator.Next(5000, prev);
                Assert.Greater(next, prev);
                prev = next;
            }
        }
    }

    public class DeviceRegistryTests
    {
        static DeviceRegistry Make(string version = "v1")
        {
            var r = new DeviceRegistry { HostParamsVersion = version };
            return r;
        }

        [Test] public void 再接続でClientNoが変わっても同じ端末()
        {
            var r = Make();
            r.Connect("devA", 3);
            r.Disconnect(3);
            r.Connect("devA", 7);
            Assert.AreEqual(1, new List<DeviceInfo>(r.Devices).Count);
            Assert.AreEqual(7, r.Get("devA").ClientNo);
            Assert.AreEqual("devA", r.GetDeviceId(7));
            Assert.IsNull(r.GetDeviceId(3));
        }

        [Test] public void 切断なしでClientNoが変わっても同じ端末()
        {
            var r = Make();
            r.Connect("devA", 3);
            r.Connect("devA", 9);
            Assert.AreEqual(1, new List<DeviceInfo>(r.Devices).Count);
            Assert.IsNull(r.GetDeviceId(3));
            Assert.AreEqual("devA", r.GetDeviceId(9));
        }

        [Test] public void 切断してから再接続すると参加できる状態に戻る()
        {
            var r = Make("v1");
            r.Connect("devA", 3);
            r.ReportParamsVersion("devA", "v1");
            r.Disconnect(3);
            Assert.IsFalse(r.IsEligible("devA"));
            r.Connect("devA", 8);
            Assert.IsTrue(r.IsEligible("devA"), "#44 までは再接続で確認済みに戻す");
        }

        [Test] public void 次の回を待つ端末は参加できないがWaitingで解除される()
        {
            var r = Make("v1");
            r.Connect("devA", 3);
            r.ReportParamsVersion("devA", "v1");
            r.SetWaitNextRound("devA", true);
            Assert.IsFalse(r.IsEligible("devA"));
            Assert.IsFalse(r.IsJoinable("devA"));
            r.ClearWaitNextRound();
            Assert.IsTrue(r.IsEligible("devA"));
        }

        [Test] public void 切断すると位置合わせ未確認に戻る()
        {
            var r = Make();
            r.Connect("devA", 3);
            Assert.IsTrue(r.Get("devA").AlignmentConfirmed);   // この PR の既定は確認済み
            r.Disconnect(3);
            Assert.IsFalse(r.Get("devA").Connected);
            Assert.IsFalse(r.Get("devA").AlignmentConfirmed);
        }

        [Test] public void 参加できる条件は4つすべて()
        {
            var r = Make("v1");
            r.Connect("devA", 3);
            Assert.IsFalse(r.IsEligible("devA"), "版が未報告");
            r.ReportParamsVersion("devA", "v2");
            Assert.IsFalse(r.IsEligible("devA"), "版が違う");
            r.ReportParamsVersion("devA", "v1");
            Assert.IsTrue(r.IsEligible("devA"));
            r.SetSpectator("devA", true);
            Assert.IsFalse(r.IsEligible("devA"), "見学");
            r.SetSpectator("devA", false);
            r.SetAlignmentConfirmed("devA", false);
            Assert.IsFalse(r.IsEligible("devA"), "未確認");
            r.SetAlignmentConfirmed("devA", true);
            Assert.IsTrue(r.IsEligible("devA"));
            r.Disconnect(3);
            Assert.IsFalse(r.IsEligible("devA"), "切断");
        }

        [Test] public void ClientNoを別の端末が使い回すと前の端末は切断扱い()
        {
            var r = Make();
            r.Connect("devA", 3);
            r.Connect("devB", 3);
            Assert.IsFalse(r.Get("devA").Connected);
            Assert.IsTrue(r.Get("devB").Connected);
            Assert.AreEqual("devB", r.GetDeviceId(3));
        }
    }

    public class HostRoundMachineTests
    {
        sealed class Ctx
        {
            public DeviceRegistry Reg = new DeviceRegistry { HostParamsVersion = "v1" };
            public ThProgressParams Times = new ThProgressParams { openingSeconds = 3, practiceSeconds = 5, playingSeconds = 10, resultSeconds = 3 };
            public HostRoundMachine M;
            public Ctx(int devices = 2)
            {
                M = new HostRoundMachine(Reg, Times);
                for (int i = 0; i < devices; i++) AddDevice("dev" + i, 10 + i);
            }
            public void AddDevice(string id, int no, string ver = "v1")
            {
                Reg.Connect(id, no);
                Reg.ReportParamsVersion(id, ver);
            }
        }

        [Test] public void 参加できる端末が0台では開始できない()
        {
            var c = new Ctx(0);
            Assert.IsFalse(c.M.TryStart(0, 1000, 1));
            Assert.AreEqual(RoundState.Waiting, c.M.State);
            Assert.AreEqual(0, c.M.Seq);
        }

        [Test] public void 版が合わない端末だけでは開始できない()
        {
            var c = new Ctx(0);
            c.AddDevice("devX", 5, "old");
            Assert.IsFalse(c.M.TryStart(0, 1000, 1));
        }

        [Test] public void 開始でOpeningになりRoundIdとシードと参加端末が決まる()
        {
            var c = new Ctx();
            Assert.IsTrue(c.M.TryStart(0, 1000, 42));
            Assert.AreEqual(RoundState.Opening, c.M.State);
            Assert.AreEqual(1000, c.M.RoundId);
            Assert.AreEqual(42, c.M.Seed);
            CollectionAssert.AreEqual(new[] { "dev0", "dev1" }, new List<string>(c.M.Participants));
            Assert.AreEqual(3f, c.M.RemainingSeconds(0), 0.001f);
        }

        [Test] public void 待機以外での開始は無視される()
        {
            var c = new Ctx();
            c.M.TryStart(0, 1000, 1);
            Assert.IsFalse(c.M.TryStart(1, 2000, 2));
            Assert.AreEqual(1000, c.M.RoundId);
            Assert.AreEqual(1, c.M.Seq);
        }

        [Test] public void 各状態の長さで遷移し通し番号が変化ごとに増える()
        {
            var c = new Ctx();
            c.M.TryStart(0, 1000, 1);                       // Opening 3s
            Assert.IsFalse(c.M.Tick(2.9));
            Assert.IsTrue(c.M.Tick(3.0));
            Assert.AreEqual(RoundState.Tutorial, c.M.State);  // 5s
            Assert.AreEqual(2, c.M.Seq);
            Assert.IsFalse(c.M.Tick(7.9));
            Assert.IsTrue(c.M.Tick(8.0));
            Assert.AreEqual(RoundState.Playing, c.M.State);   // 10s
            Assert.AreEqual(10f, c.M.RemainingSeconds(8.0), 0.001f);
            Assert.AreEqual(5f, c.M.RemainingSeconds(13.0), 0.001f);
            Assert.IsFalse(c.M.Tick(17.9));
            Assert.IsTrue(c.M.Tick(18.0));
            Assert.AreEqual(RoundState.Result, c.M.State);    // 3s
            Assert.IsTrue(c.M.Tick(21.0));
            Assert.AreEqual(RoundState.Waiting, c.M.State);
            Assert.AreEqual(5, c.M.Seq);
            Assert.AreEqual(0, c.M.RoundId);
            Assert.AreEqual(0f, c.M.RemainingSeconds(21.0));
        }

        [TestCase(0.0)] [TestCase(4.0)] [TestCase(9.0)] [TestCase(19.0)]
        public void どの状態からでも中断で待機に戻る(double abortAt)
        {
            var c = new Ctx();
            c.M.TryStart(0, 1000, 1);
            for (double t = 0.5; t <= abortAt; t += 0.5) c.M.Tick(t);
            var seq = c.M.Seq;
            Assert.IsTrue(c.M.Abort(abortAt));
            Assert.AreEqual(RoundState.Waiting, c.M.State);
            Assert.AreEqual(seq + 1, c.M.Seq);
            Assert.AreEqual(0, c.M.Participants.Count);
        }

        [Test] public void 待機中の中断は何もしない()
        {
            var c = new Ctx();
            Assert.IsFalse(c.M.Abort(0));
            Assert.AreEqual(0, c.M.Seq);
        }

        [Test] public void 中断の後の開始はRoundIdが増える()
        {
            var c = new Ctx();
            c.M.TryStart(0, 1000, 1);
            c.M.Abort(1);
            c.M.TryStart(2, 1000, 1);   // 同じミリ秒
            Assert.AreEqual(1001, c.M.RoundId);
            c.M.Abort(3);
            c.M.TryStart(4, 500, 1);    // 時計の巻き戻り
            Assert.AreEqual(1002, c.M.RoundId);
        }

        [Test] public void OpeningからTutorialで版の合わない端末が外れる()
        {
            var c = new Ctx();
            c.M.TryStart(0, 1000, 1);
            c.Reg.ReportParamsVersion("dev1", "other");
            c.M.Tick(3.0);
            CollectionAssert.AreEqual(new[] { "dev0" }, new List<string>(c.M.Participants));
        }

        [Test] public void 本編中は参加端末が変わらない()
        {
            var c = new Ctx();
            c.M.TryStart(0, 1000, 1);
            c.M.Tick(3.0);
            c.M.Tick(8.0);   // Playing
            Assert.AreEqual(RoundState.Playing, c.M.State);
            c.Reg.Disconnect(11);
            c.AddDevice("late", 20);
            c.Reg.ReportParamsVersion("dev0", "zzz");
            CollectionAssert.AreEqual(new[] { "dev0", "dev1" }, new List<string>(c.M.Participants));
            c.M.Tick(18.0);
            CollectionAssert.AreEqual(new[] { "dev0", "dev1" }, new List<string>(c.M.Participants));
        }

        [Test] public void 練習から本編で参加端末が確定する()
        {
            var c = new Ctx();
            c.M.TryStart(0, 1000, 1);
            c.M.Tick(3.0);                       // Tutorial
            c.Reg.Disconnect(11);                // dev1 が切断
            c.M.Tick(8.0);                       // Playing
            CollectionAssert.AreEqual(new[] { "dev0" }, new List<string>(c.M.Participants));
        }

        [Test] public void 開始時の参加端末は版を問わず_次の回を待つ端末は含まない()
        {
            var c = new Ctx();
            c.AddDevice("old", 30, "other");
            c.AddDevice("waiting", 31);
            c.Reg.SetWaitNextRound("waiting", true);
            Assert.IsTrue(c.M.TryStart(0, 1000, 1));
            CollectionAssert.AreEquivalent(new[] { "dev0", "dev1", "old" }, new List<string>(c.M.Participants));
        }

        [Test] public void Waitingに戻ると次の回を待つが解除される()
        {
            var c = new Ctx();
            c.M.TryStart(0, 1000, 1);
            c.Reg.SetWaitNextRound("dev1", true);
            c.M.Tick(3.0);
            CollectionAssert.AreEqual(new[] { "dev0" }, new List<string>(c.M.Participants));
            c.M.Abort(4);
            Assert.IsFalse(c.Reg.Get("dev1").WaitNextRound);
        }

        [Test] public void 版が違う端末はオープニング中に一致すればTutorialに残り_しなければ外れる()
        {
            var c = new Ctx(1);
            c.AddDevice("late", 30, "other");
            c.AddDevice("never", 31, "other");
            c.M.TryStart(0, 1000, 1);
            c.Reg.ReportParamsVersion("late", "v1");
            c.M.Tick(3.0);
            CollectionAssert.AreEquivalent(new[] { "dev0", "late" }, new List<string>(c.M.Participants));
        }

        [Test] public void 待機中だけ時間設定を差し替えられる()
        {
            var c = new Ctx();
            Assert.IsTrue(c.M.TrySetTimes(new ThProgressParams { openingSeconds = 1 }));
            c.M.TryStart(0, 1000, 1);
            Assert.IsFalse(c.M.TrySetTimes(new ThProgressParams()));
            Assert.AreEqual(1f, c.M.RemainingSeconds(0), 0.001f);
        }
    }

    public class HostMessageGateTests
    {
        static (HostRoundMachine m, DeviceRegistry r, HostMessageGate g) Make()
        {
            var r = new DeviceRegistry { HostParamsVersion = "v1" };
            var m = new HostRoundMachine(r, new ThProgressParams());
            foreach (var (id, no) in new[] { ("a", 10), ("b", 11) }) { r.Connect(id, no); r.ReportParamsVersion(id, "v1"); }
            r.Connect("c", 12); r.ReportParamsVersion("c", "v1");
            return (m, r, new HostMessageGate(m, r));
        }

        [Test] public void 今の回の参加端末なら受け付ける()
        {
            var (m, r, g) = Make();
            m.TryStart(0, 1000, 1);
            Assert.IsTrue(g.Accept(1000, 10));
        }

        [Test] public void 古いRoundIdは拒む()
        {
            var (m, r, g) = Make();
            m.TryStart(0, 1000, 1);
            Assert.IsFalse(g.Accept(999, 10));
            Assert.IsFalse(g.Accept(0, 10));
            Assert.IsFalse(g.Accept(1001, 10));
        }

        [Test] public void 待機中はすべて拒む()
        {
            var (m, r, g) = Make();
            Assert.IsFalse(g.Accept(0, 10));
            Assert.IsFalse(g.Accept(1000, 10));
        }

        [Test] public void 参加端末でない送り手と未知のClientNoを拒む()
        {
            var (m, r, g) = Make();
            r.Connect("d", 13); r.ReportParamsVersion("d", "v1");   // 開始後に来た端末
            m.TryStart(0, 1000, 1);
            r.Connect("e", 14); r.ReportParamsVersion("e", "v1");
            Assert.IsFalse(g.Accept(1000, 14));
            Assert.IsFalse(g.Accept(1000, 99));
        }

        [Test] public void 参加できなくなった送り手を拒む()
        {
            var (m, r, g) = Make();
            m.TryStart(0, 1000, 1);
            r.SetSpectator("a", true);
            Assert.IsFalse(g.Accept(1000, 10), "見学");
            r.SetSpectator("a", false);
            r.Disconnect(11);
            Assert.IsFalse(g.Accept(1000, 11), "切断");
            r.ReportParamsVersion("c", "x");
            Assert.IsFalse(g.Accept(1000, 12), "版の不一致");
            Assert.IsTrue(g.Accept(1000, 10));
        }
    }
}
