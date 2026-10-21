using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using R3;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    /// <summary>端末の登録（未確認始まり）・端末番号・端末一覧の行（#44）</summary>
    public class ThDeviceListTests
    {
        // ---- DeviceRegistry: 未確認始まり ----

        [Test] public void 新しい端末は未確認で始まり参加できない()
        {
            var r = new DeviceRegistry { HostParamsVersion = "v" };
            r.Connect("devA", 3);
            r.ReportParamsVersion("devA", "v");
            Assert.IsFalse(r.Get("devA").AlignmentConfirmed);
            Assert.IsFalse(r.IsEligible("devA"));
            Assert.AreEqual(0, r.EligibleCount);
            r.SetAlignmentConfirmed("devA", true);
            Assert.IsTrue(r.IsEligible("devA"));
        }

        [Test] public void 再接続しても未確認のまま()
        {
            var r = new DeviceRegistry { HostParamsVersion = "v" };
            r.Connect("devA", 3);
            r.ReportParamsVersion("devA", "v");
            r.SetAlignmentConfirmed("devA", true);
            r.Disconnect(3);
            r.Connect("devA", 8);
            Assert.IsFalse(r.Get("devA").AlignmentConfirmed);
            Assert.IsFalse(r.IsEligible("devA"));
        }

        [Test] public void autoConfirmなら新規も再接続も確認済みで始まる()
        {
            var r = new DeviceRegistry { HostParamsVersion = "v", AutoConfirm = true };
            r.Connect("devA", 3);
            Assert.IsTrue(r.Get("devA").AlignmentConfirmed);
            r.Disconnect(3);
            Assert.IsFalse(r.Get("devA").AlignmentConfirmed, "切断で未確認に戻る");
            r.Connect("devA", 8);
            Assert.IsTrue(r.Get("devA").AlignmentConfirmed);
        }

        [Test] public void 同じ端末への接続の繰り返しは確認済みを変えない()
        {
            var r = new DeviceRegistry { HostParamsVersion = "v" };
            r.Connect("devA", 3);
            r.SetAlignmentConfirmed("devA", true);
            r.Connect("devA", 3);   // ThResync や RebindSender で何度も呼ばれる
            Assert.IsTrue(r.Get("devA").AlignmentConfirmed);
        }

        [Test] public void 見学は再接続しても残る()
        {
            var r = new DeviceRegistry { HostParamsVersion = "v", AutoConfirm = true };
            r.Connect("devA", 3);
            r.SetSpectator("devA", true);
            r.Disconnect(3);
            r.Connect("devA", 4);
            Assert.IsTrue(r.Get("devA").Spectator);
        }

        [Test] public void パラメータのautoConfirmAlignmentの初期値はfalse()
        {
            Assert.IsFalse(new ThParams().operation.autoConfirmAlignment);
            Assert.AreEqual(2f, new ThParams().progress.statusInterval, 1e-6f);
        }

        [Test] public void 状況報告を受けて切断すると手と電池は不明に戻る()
        {
            var r = new DeviceRegistry();
            r.Connect("devA", 3);
            r.ReportStatus("devA", 2, 80, 10.0);
            Assert.AreEqual(2, r.Get("devA").HandCount);
            Assert.AreEqual(80, r.Get("devA").BatteryPercent);
            r.ReportStatus("devA", 9, 500, 11.0);
            Assert.AreEqual(-1, r.Get("devA").HandCount, "範囲外は不明");
            Assert.AreEqual(-1, r.Get("devA").BatteryPercent);
            r.ReportStatus("devA", 1, 50, 12.0);
            r.Disconnect(3);
            Assert.AreEqual(-1, r.Get("devA").HandCount);
            Assert.AreEqual(-1, r.Get("devA").BatteryPercent);
        }

        // ---- ThDeviceNumbers ----

        const string TwoDevices = "{ \"devices\": [ { \"deviceId\": \"devB\", \"number\": 2 }, { \"deviceId\": \"devA\", \"number\": 1 } ] }";

        [Test] public void 端末番号を読み込める()
        {
            var n = ThDeviceNumbers.Parse(TwoDevices);
            Assert.AreEqual(1, n.NumberOf("devA"));
            Assert.AreEqual(2, n.NumberOf("devB"));
            Assert.AreEqual(0, n.NumberOf("devZ"), "未登録は 0");
            Assert.AreEqual(0, n.NumberOf(null));
            CollectionAssert.IsEmpty(n.Warnings);
            Assert.IsFalse(n.Broken);
        }

        [Test] public void ファイルが無い_空の場合は空の表で警告なし()
        {
            foreach (var text in new string[] { null, "", "  " })
            {
                var n = ThDeviceNumbers.Parse(text);
                Assert.AreEqual(0, n.NumberOf("devA"));
                CollectionAssert.IsEmpty(n.Warnings);
                Assert.IsFalse(n.Broken);
            }
        }

        [Test] public void 壊れたJSONは空の表と警告()
        {
            var n = ThDeviceNumbers.Parse("{ broken");
            Assert.AreEqual(0, n.NumberOf("devA"));
            Assert.AreEqual(1, n.Warnings.Count);
            Assert.IsTrue(n.Broken);
        }

        [Test] public void 読めなかったときの警告はそのまま足す()
        {
            var n = ThDeviceNumbers.Parse(null, "読めません");
            CollectionAssert.Contains(n.Warnings.ToList(), "読めません");
        }

        [Test] public void 同じ番号の重複とdeviceIdの重複は警告()
        {
            var dupNumber = ThDeviceNumbers.Parse("{ \"devices\": [ { \"deviceId\": \"a\", \"number\": 1 }, { \"deviceId\": \"b\", \"number\": 1 } ] }");
            Assert.IsTrue(dupNumber.Warnings.Any(w => w.Contains("重複") && w.Contains("1")));
            Assert.AreEqual(1, dupNumber.NumberOf("a"));
            Assert.AreEqual(1, dupNumber.NumberOf("b"));
            Assert.Less(dupNumber.Compare("a", "b"), 0, "同じ番号は DeviceId の順");

            var dupId = ThDeviceNumbers.Parse("{ \"devices\": [ { \"deviceId\": \"a\", \"number\": 1 }, { \"deviceId\": \"a\", \"number\": 2 } ] }");
            Assert.IsTrue(dupId.Warnings.Any(w => w.Contains("DeviceId")));
            Assert.AreEqual(2, dupId.NumberOf("a"), "後の行を使う");
        }

        [Test] public void 番号0は未登録扱い()
        {
            var n = ThDeviceNumbers.Parse("{ \"devices\": [ { \"deviceId\": \"a\", \"number\": 0 } ] }");
            Assert.AreEqual(0, n.NumberOf("a"));
            Assert.IsFalse(n.IsRegistered("a"));
            CollectionAssert.IsEmpty(n.Warnings, "0 同士の重複は警告しない");
        }

        [Test] public void 並びは端末番号順で未登録は後ろにDeviceId順()
        {
            var n = ThDeviceNumbers.Parse("{ \"devices\": [ { \"deviceId\": \"zz\", \"number\": 1 }, { \"deviceId\": \"aa\", \"number\": 3 }, { \"deviceId\": \"mm\", \"number\": 2 } ] }");
            var ids = new List<string> { "q2", "aa", "q1", "mm", "zz" };
            ids.Sort(n.Compare);
            CollectionAssert.AreEqual(new[] { "zz", "mm", "aa", "q1", "q2" }, ids);
        }

        [Test] public void 雛形は既存の行を残し無い端末をnumber0で足す()
        {
            var n = ThDeviceNumbers.Parse(TwoDevices);
            var json = n.BuildTemplateJson(new[] { "devC", "devA", "devC", null, "" });
            var back = ThDeviceNumbers.Parse(json);
            Assert.AreEqual(1, back.NumberOf("devA"));
            Assert.AreEqual(2, back.NumberOf("devB"), "接続していなくても既存の行は残す");
            Assert.AreEqual(0, back.NumberOf("devC"));
            Assert.AreEqual(3, back.Entries.Count, "devC は 1 行だけ足す");
            Assert.IsTrue(back.Entries.Any(e => e.deviceId == "devC" && e.number == 0));
        }

        [Test] public void 雛形の書き出しは壊れた既存ファイルを上書きしない()
        {
            var dir = Path.Combine(Path.GetTempPath(), "th_devices_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(ThSettingsFiles.DevicesPath(dir), "{ broken");
                Assert.IsNotNull(ThSettingsFiles.WriteDeviceTemplate(dir, new[] { "devA" }));
                Assert.AreEqual("{ broken", File.ReadAllText(ThSettingsFiles.DevicesPath(dir)));
                File.Delete(ThSettingsFiles.DevicesPath(dir));
                Assert.IsNull(ThSettingsFiles.WriteDeviceTemplate(dir, new[] { "devA" }));
                Assert.IsTrue(ThSettingsFiles.LoadDeviceNumbers(dir).Entries.Any(e => e.deviceId == "devA" && e.number == 0));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        // ---- 端末一覧の行 ----

        static DeviceRegistry MakeRegistry(string numbersJson)
        {
            var r = new DeviceRegistry { HostParamsVersion = "v", AutoConfirm = true };
            r.Numbers = ThDeviceNumbers.Parse(numbersJson);
            return r;
        }

        [Test] public void 行は端末番号順で未登録は後ろ_末尾6文字()
        {
            var r = MakeRegistry(TwoDevices);
            r.Connect("zzzz-unreg-abcdef", 5);
            r.Connect("devB", 3);
            r.Connect("devA", 4);
            var rows = DeviceRowBuilder.Build(r, RoundState.Waiting, null, 10.0, 2f, 20);
            CollectionAssert.AreEqual(new[] { "devA", "devB", "zzzz-unreg-abcdef" }, rows.Select(x => x.DeviceId).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, rows.Select(x => x.Number).ToArray());
            Assert.AreEqual("abcdef", DeviceRowData.ShortId(rows[2].DeviceId));
            Assert.AreEqual("abc", DeviceRowData.ShortId("abc"));
        }

        [Test] public void 手と電池は新しい報告だけ表示し間隔の3倍を過ぎると不明()
        {
            var r = MakeRegistry(TwoDevices);
            r.Connect("devA", 4);
            r.ReportStatus("devA", 2, 15, 10.0);
            var fresh = DeviceRowBuilder.Build(r, RoundState.Waiting, null, 15.9, 2f, 20)[0];
            Assert.AreEqual(ThHandStatus.Both, fresh.Hand);
            Assert.AreEqual(15, fresh.Battery);
            Assert.IsTrue(fresh.BatteryWarning, "15% は警告 20% 以下");
            var stale = DeviceRowBuilder.Build(r, RoundState.Waiting, null, 16.1, 2f, 20)[0];
            Assert.AreEqual(ThHandStatus.Unknown, stale.Hand);
            Assert.AreEqual(-1, stale.Battery);
            Assert.IsFalse(stale.BatteryWarning, "不明は警告にしない");
        }

        [Test] public void 手の台数と電池の境界()
        {
            var r = MakeRegistry(TwoDevices);
            r.Connect("devA", 4);
            r.ReportStatus("devA", 1, 21, 1.0);
            var one = DeviceRowBuilder.Build(r, RoundState.Waiting, null, 1.0, 2f, 20)[0];
            Assert.AreEqual(ThHandStatus.One, one.Hand);
            Assert.IsFalse(one.BatteryWarning, "21% は警告の外");
            r.ReportStatus("devA", 0, 20, 1.0);
            var none = DeviceRowBuilder.Build(r, RoundState.Waiting, null, 1.0, 2f, 20)[0];
            Assert.AreEqual(ThHandStatus.None, none.Hand);
            Assert.IsTrue(none.BatteryWarning, "20% ちょうどは警告");
            r.ReportStatus("devA", -1, -1, 1.0);
            var unk = DeviceRowBuilder.Build(r, RoundState.Waiting, null, 1.0, 2f, 20)[0];
            Assert.AreEqual(ThHandStatus.Unknown, unk.Hand);
            Assert.AreEqual(-1, unk.Battery);
        }

        [Test] public void 練習完了の列は練習中から結果の間だけ出る()
        {
            var r = MakeRegistry(TwoDevices);
            r.Connect("devA", 4);
            var done = new Dictionary<string, double> { { "devA", 5.0 } };
            foreach (var st in new[] { RoundState.Waiting, RoundState.Opening })
                Assert.IsFalse(DeviceRowBuilder.Build(r, st, done, 1.0, 2f, 20)[0].PracticeVisible, st.ToString());
            foreach (var st in new[] { RoundState.Tutorial, RoundState.Playing, RoundState.Result })
            {
                var row = DeviceRowBuilder.Build(r, st, done, 1.0, 2f, 20)[0];
                Assert.IsTrue(row.PracticeVisible, st.ToString());
                Assert.IsTrue(row.PracticeDone);
            }
            Assert.IsFalse(DeviceRowBuilder.Build(r, RoundState.Tutorial, new Dictionary<string, double>(), 1.0, 2f, 20)[0].PracticeDone);
        }

        [Test] public void 切断した端末は接続切断_未確認_手は不明で出る()
        {
            var r = MakeRegistry(TwoDevices);
            r.Connect("devA", 4);
            r.ReportStatus("devA", 2, 90, 1.0);
            r.Disconnect(4);
            var row = DeviceRowBuilder.Build(r, RoundState.Waiting, null, 1.0, 2f, 20)[0];
            Assert.IsFalse(row.Connected);
            Assert.IsFalse(row.AlignmentConfirmed);
            Assert.AreEqual(ThHandStatus.Unknown, row.Hand);
        }

        [Test] public void 行の同一判定は内容で行う()
        {
            var r = MakeRegistry(TwoDevices);
            r.Connect("devA", 4);
            var a = DeviceRowBuilder.Build(r, RoundState.Waiting, null, 1.0, 2f, 20);
            var b = DeviceRowBuilder.Build(r, RoundState.Waiting, null, 1.5, 2f, 20);
            Assert.IsTrue(DeviceRowData.SameList(a, b));
            r.SetSpectator("devA", true);
            Assert.IsFalse(DeviceRowData.SameList(a, DeviceRowBuilder.Build(r, RoundState.Waiting, null, 1.5, 2f, 20)));
        }

        [Test] public void 練習の色は端末番号の順_未登録は後ろにDeviceId順()
        {
            var n = ThDeviceNumbers.Parse("{ \"devices\": [ { \"deviceId\": \"devZ\", \"number\": 1 }, { \"deviceId\": \"devA\", \"number\": 2 } ] }");
            var m = PracticeColors.Assign(new[] { "devA", "devZ", "devY", "devB" }, n.Compare);
            Assert.AreEqual(0, m["devZ"]);
            Assert.AreEqual(1, m["devA"]);
            Assert.AreEqual(2, m["devB"]);
            Assert.AreEqual(3, m["devY"]);
            var plain = PracticeColors.Assign(new[] { "devA", "devZ" });
            Assert.AreEqual(0, plain["devA"], "比較を渡さなければ従来どおり DeviceId の文字列順");
        }
    }

    /// <summary>ホスト（SessionHostCore）の見学・未確認・状況報告・記録（#44）。端末は偽の送信口で代用し、メッセージを直接渡す</summary>
    public class ThOperatorHostTests
    {
        static CsvLabels Labels() => CsvLabels.From(ScriptableObject.CreateInstance<ThTextTable>());

        sealed class Sink : IThTransport
        {
            public readonly List<(Rpc rpc, string[] args)> Sent = new List<(Rpc, string[])>();
            public void Send(Rpc rpc, string[] args) => Sent.Add((rpc, args));
            public int Count(Rpc rpc) => Sent.Count(s => s.rpc == rpc);
        }

        static readonly Bounds Zone = new Bounds(new Vector3(0, 0.9f, 0.8f), new Vector3(3f, 1.2f, 1f));

        static ThTreasureSetup MakeSetup(bool slots)
        {
            var pts = new List<PlacementPoint>();
            for (int i = 0; i < 30; i++) pts.Add(new PlacementPoint(new Vector3((i % 5) * 0.75f - 1.5f, 0, 3f + i / 5), i % 3));
            var setup = new ThTreasureSetup
            {
                Points = pts,
                Catalog = new[] { new SpawnCatalogEntry(TreasureKind.S, 0), new SpawnCatalogEntry(TreasureKind.S, 1), new SpawnCatalogEntry(TreasureKind.M, 2) },
                CollectBounds = Zone,
                CatalogHash = "h1",
            };
            if (slots)
                setup.PracticeSlots = new[]
                {
                    new PracticeSlot(new Vector3(-1.5f, 0, 2.5f), 0), new PracticeSlot(new Vector3(-0.5f, 0, 2.5f), 0),
                    new PracticeSlot(new Vector3(0.5f, 0, 2.5f), 0), new PracticeSlot(new Vector3(1.5f, 0, 2.5f), 0),
                };
            return setup;
        }

        sealed class Rig
        {
            public readonly Sink Tx = new Sink();
            public readonly SessionHostCore Host;
            public double Now = 100;
            public readonly List<RoundRecord> Finished = new List<RoundRecord>();
            public readonly IDisposable Subscription;

            public Rig(bool auto = true, bool slots = false, Action<ThParams> tweak = null)
            {
                var p = new ThParams();
                p.operation.autoConfirmAlignment = auto;
                p.progress.openingSeconds = 3; p.progress.practiceSeconds = 5; p.progress.playingSeconds = 30; p.progress.resultSeconds = 3;
                tweak?.Invoke(p);
                Host = new SessionHostCore(Tx, ThParamsLoader.Load(p, null, null), new ThGameSettings(), null, 0, null, MakeSetup(slots)) { OwnClientNo = 1 };
                Subscription = Host.RoundFinished.Subscribe(r => Finished.Add(r));
            }

            public void Register(string id, int no) =>
                Host.OnRpc(Rpc.ThResync, no, ThMessages.Encode(0, new ThResyncMsg { deviceId = id, paramsVersion = Host.ParamsVersion, catalogHash = "h1" }), Now);

            public void Advance(double seconds)
            {
                int steps = (int)Math.Round(seconds / 0.1);
                for (int i = 0; i < steps; i++) { Now += 0.1; Host.Tick(Now); }
            }

            public void ToTutorial()
            {
                Assert.IsTrue(Host.Start(Now, 1700000000000L, 7));
                Advance(3.2);
                Assert.AreEqual(RoundState.Tutorial, Host.Machine.State);
            }

            public void ToPlaying()
            {
                ToTutorial();
                Advance(5.2);
                Assert.AreEqual(RoundState.Playing, Host.Machine.State);
            }

            public void Send(Rpc rpc, int no, object dto) => Host.OnRpc(rpc, no, ThMessages.EncodeObject(Host.Machine.RoundId, dto), Now);

            public HostItem FirstS(int skip = 0) => Host.Items.Items.Where(i => i.Kind == TreasureKind.S).Skip(skip).First();
            public void Grab(int no, HostItem it, int req) => Send(Rpc.ThGrab, no, new ThGrabMsg { item = it.Index, reqId = req, gen = it.Gen, kind = 0 });
        }

        // ---- 位置合わせ・見学で開始できるか ----

        [Test] public void 未確認始まりでは確認するまで開始できず_確認すると参加できる端末数が増える()
        {
            var r = new Rig(auto: false);
            r.Register("devA", 2);
            Assert.AreEqual(1, r.Host.ConnectedCount.CurrentValue);
            Assert.AreEqual(0, r.Host.EligibleCount.CurrentValue);
            Assert.IsFalse(r.Host.Machine.CanStart);
            Assert.IsFalse(r.Host.Start(r.Now, 1000, 1));
            Assert.IsTrue(r.Host.SetAlignmentConfirmed("devA", true, r.Now));
            Assert.AreEqual(1, r.Host.EligibleCount.CurrentValue);
            Assert.IsTrue(r.Host.Machine.CanStart);
            Assert.IsTrue(r.Host.SetSpectator("devA", true, r.Now));
            Assert.AreEqual(0, r.Host.EligibleCount.CurrentValue, "見学にすると参加できる端末数が減る");
            Assert.IsFalse(r.Host.Machine.CanStart);
            Assert.IsFalse(r.Host.SetSpectator("unknown", true, r.Now), "知らない端末は false");
        }

        [Test] public void 切り替えは現在の状態を反転する()
        {
            var r = new Rig(auto: false);
            r.Register("devA", 2);
            Assert.IsTrue(r.Host.ToggleAlignmentConfirmed("devA", r.Now));
            Assert.IsTrue(r.Host.Registry.Get("devA").AlignmentConfirmed);
            Assert.IsTrue(r.Host.ToggleAlignmentConfirmed("devA", r.Now));
            Assert.IsFalse(r.Host.Registry.Get("devA").AlignmentConfirmed);
            Assert.IsTrue(r.Host.ToggleSpectator("devA", r.Now));
            Assert.IsTrue(r.Host.Registry.Get("devA").Spectator);
        }

        [Test] public void 端末一覧の購読値に切り替えが反映される()
        {
            var r = new Rig(auto: false);
            r.Register("devA", 2);
            Assert.AreEqual(1, r.Host.Devices.CurrentValue.Length);
            Assert.IsFalse(r.Host.Devices.CurrentValue[0].AlignmentConfirmed);
            r.Host.SetAlignmentConfirmed("devA", true, r.Now);
            Assert.IsTrue(r.Host.Devices.CurrentValue[0].AlignmentConfirmed);
            r.Host.SetSpectator("devA", true, r.Now);
            Assert.IsTrue(r.Host.Devices.CurrentValue[0].Spectator);
        }

        // ---- 回の途中の見学・未確認 ----

        [Test] public void 本編中に見学にすると持っているお宝をそっと置き_参加人数と目標額は変わらず_申請は捨てられ_戻すと再びつかめる()
        {
            var r = new Rig();
            r.Register("devA", 2); r.Register("devB", 3);
            r.ToPlaying();
            int target = r.Host.Target.CurrentValue;
            Assert.AreEqual(SessionHostCore.TargetFor(2, r.Host.Params), target);
            var s1 = r.FirstS(); var s2 = r.FirstS(1);
            r.Grab(2, s1, 1);
            Assert.AreEqual(ItemState.Held, s1.State);
            Assert.AreEqual("devA", s1.OwnerDevice);

            Assert.IsTrue(r.Host.SetSpectator("devA", true, r.Now));
            Assert.AreEqual(ItemState.Placing, s1.State, "持っていたお宝はその場にそっと置かれる");
            Assert.IsNull(s1.OwnerDevice);
            Assert.IsTrue(r.Tx.Count(Rpc.ThForceRest) >= 1);
            Assert.AreEqual(2, r.Host.Machine.Participants.Count, "参加人数は変わらない");
            Assert.AreEqual(target, r.Host.Target.CurrentValue, "目標額は変わらない");

            r.Advance(0.5);
            Assert.AreEqual(ItemState.Resting, s1.State, "〔置く時間〕の後はつかめるに戻る");
            int denied = r.Tx.Count(Rpc.ThGrabDenied);
            r.Grab(2, s2, 2);
            Assert.AreEqual(ItemState.Resting, s2.State, "見学の間のつかむ申請は捨てられる");
            Assert.AreEqual(denied, r.Tx.Count(Rpc.ThGrabDenied), "拒否の返事も出さずに捨てる");

            Assert.IsTrue(r.Host.SetSpectator("devA", false, r.Now));
            r.Grab(2, s2, 3);
            Assert.AreEqual(ItemState.Held, s2.State, "本編開始時の参加端末だったので、戻すとすぐ再びつかめる");
            Assert.AreEqual(2, r.Host.Machine.Participants.Count);
        }

        [Test] public void 本編中に位置合わせ未確認にしても同じように解放される()
        {
            var r = new Rig();
            r.Register("devA", 2);
            r.ToPlaying();
            var s1 = r.FirstS();
            r.Grab(2, s1, 1);
            Assert.AreEqual(ItemState.Held, s1.State);
            Assert.IsTrue(r.Host.SetAlignmentConfirmed("devA", false, r.Now));
            Assert.AreEqual(ItemState.Placing, s1.State);
            Assert.AreEqual(1, r.Host.Machine.Participants.Count);
            r.Advance(0.5);
            r.Grab(2, r.FirstS(1), 2);
            Assert.AreEqual(ItemState.Resting, r.FirstS(1).State);
            r.Host.SetAlignmentConfirmed("devA", true, r.Now);
            r.Grab(2, r.FirstS(1), 3);
            Assert.AreEqual(ItemState.Held, r.FirstS(1).State);
        }

        [Test] public void 見学にした端末のMの片手予約も解除される()
        {
            var r = new Rig();
            r.Register("devA", 2);
            r.ToPlaying();
            var m = r.Host.Items.Items.First(i => i.Kind == TreasureKind.M);
            r.Send(Rpc.ThGrab, 2, new ThGrabMsg { item = m.Index, reqId = 1, gen = 0, kind = 1 });
            Assert.AreEqual(ItemState.Locked, m.State);
            r.Host.SetSpectator("devA", true, r.Now);
            Assert.AreEqual(ItemState.Resting, m.State);
            Assert.IsNull(m.OwnerDevice);
        }

        [Test] public void 本編開始時の参加端末に入っていなかった端末は参加に戻しても次の回から()
        {
            var r = new Rig();
            r.Register("devA", 2); r.Register("devB", 3);
            r.Host.Registry.SetSpectator("devB", true);
            r.ToPlaying();
            CollectionAssert.AreEqual(new[] { "devA" }, r.Host.Machine.Participants.ToArray());
            r.Host.SetSpectator("devB", false, r.Now);
            Assert.IsTrue(r.Host.Registry.IsEligible("devB"));
            var s = r.FirstS();
            r.Grab(3, s, 1);
            Assert.AreEqual(ItemState.Resting, s.State, "この回は参加端末に入っていないのでつかめない");
            Assert.AreEqual(1, r.Host.Machine.Participants.Count);
        }

        [Test] public void 練習中に見学にした端末の練習用お宝も解放される()
        {
            var r = new Rig(slots: true);
            r.Register("devA", 2);
            r.ToTutorial();
            var practice = r.Host.PracticeItems.Items[0];
            r.Send(Rpc.ThGrab, 2, new ThGrabMsg { item = practice.Index, reqId = 1, gen = 0, kind = 0 });
            Assert.AreEqual(ItemState.Held, practice.State);
            r.Host.SetSpectator("devA", true, r.Now);
            Assert.AreEqual(ItemState.Placing, practice.State);
            Assert.AreEqual(1, r.Host.Machine.Participants.Count);
        }

        // ---- 端末番号と練習の色 ----

        [Test] public void 練習の色は端末番号の順に割り当てられる()
        {
            var r = new Rig();
            r.Register("devA", 2); r.Register("devB", 3); r.Register("devC", 4);
            r.Host.SetDeviceNumbers(ThDeviceNumbers.Parse("{ \"devices\": [ { \"deviceId\": \"devB\", \"number\": 1 }, { \"deviceId\": \"devA\", \"number\": 2 } ] }"), r.Now);
            r.ToTutorial();
            Assert.AreEqual(0, r.Host.ColorAssignments["devB"]);
            Assert.AreEqual(1, r.Host.ColorAssignments["devA"]);
            Assert.AreEqual(2, r.Host.ColorAssignments["devC"], "未登録は後ろ");
        }

        [Test] public void 端末番号の警告は運営UIの警告に出る()
        {
            var r = new Rig();
            r.Host.SetDeviceNumbers(ThDeviceNumbers.Parse("{ broken"), r.Now);
            Assert.IsTrue(r.Host.Warnings.CurrentValue.Any(w => w.Contains(ThSettingsFiles.DevicesFileName)));
            r.Host.SetDeviceNumbers(ThDeviceNumbers.Parse(null), r.Now);
            Assert.IsFalse(r.Host.Warnings.CurrentValue.Any(w => w.Contains(ThSettingsFiles.DevicesFileName)));
        }

        [Test] public void 実行中の警告は足せて消せる()
        {
            var r = new Rig();
            r.Host.SetRuntimeWarning("roundlog", "書けません");
            CollectionAssert.Contains(r.Host.Warnings.CurrentValue.ToList(), "書けません");
            r.Host.SetRuntimeWarning("roundlog", null);
            CollectionAssert.DoesNotContain(r.Host.Warnings.CurrentValue.ToList(), "書けません");
        }

        // ---- ThStatus ----

        [Test] public void ThStatusを受けると一覧に手と電池が出て_間隔の3倍で不明に戻る()
        {
            var r = new Rig();
            r.Register("devA", 2);
            r.Send(Rpc.ThStatus, 2, new ThStatusMsg { hands = 2, battery = 15 });
            r.Advance(0.2);
            var row = r.Host.Devices.CurrentValue.Single();
            Assert.AreEqual(ThHandStatus.Both, row.Hand);
            Assert.AreEqual(15, row.Battery);
            Assert.IsTrue(row.BatteryWarning);
            r.Advance(6.5);   // 〔状況報告の間隔〕2 秒 × 3 を過ぎる
            row = r.Host.Devices.CurrentValue.Single();
            Assert.AreEqual(ThHandStatus.Unknown, row.Hand);
            Assert.AreEqual(-1, row.Battery);
        }

        [Test] public void 未登録の送り手のThStatusは無視する()
        {
            var r = new Rig();
            r.Send(Rpc.ThStatus, 9, new ThStatusMsg { hands = 2, battery = 50 });
            Assert.AreEqual(0, r.Host.Devices.CurrentValue.Length);
            r.Host.OnRpc(Rpc.ThStatus, 9, null, r.Now);
            r.Host.OnRpc(Rpc.ThStatus, 9, new[] { "0", "{ broken" }, r.Now);
        }

        [Test] public void 端末はThStatusを間隔ごとに送る()
        {
            var tx = new CaptureTransport();
            var c = new SessionClientCore("devA", tx, new ThParams(), null, 0)
            {
                OwnClientNo = 2,
                HandCountProvider = () => 1,
                BatteryPercentProvider = () => 77,
            };
            c.Tick(0.0);
            c.Tick(1.0);
            Assert.AreEqual(1, tx.Sent.Count(s => s.rpc == Rpc.ThStatus), "間隔の中では送らない");
            c.Tick(2.1);
            var all = tx.Of<ThStatusMsg>(Rpc.ThStatus);
            Assert.AreEqual(2, all.Count);
            Assert.AreEqual(1, all[0].hands);
            Assert.AreEqual(77, all[0].battery);
        }

        [Test] public void 手の台数を渡す関数が無い端末は不明で送る()
        {
            var tx = new CaptureTransport();
            var c = new SessionClientCore("devA", tx, new ThParams(), null, 0) { OwnClientNo = 2, BatteryPercentProvider = () => -1 };
            c.Tick(0.0);
            var m = tx.Of<ThStatusMsg>(Rpc.ThStatus).Single();
            Assert.AreEqual(-1, m.hands);
            Assert.AreEqual(-1, m.battery);
        }

        // ---- 記録（RoundFinished） ----

        [Test] public void 回が完了すると記録が1回だけ流れ_練習完了_初めて持った_減額_回収が入る()
        {
            var r = new Rig(slots: true);
            r.Register("devA", 2);
            r.Host.SetDeviceNumbers(ThDeviceNumbers.Parse("{ \"devices\": [ { \"deviceId\": \"devA\", \"number\": 3 } ] }"), r.Now);
            Assert.IsTrue(r.Host.Start(r.Now, 1700000000000L, 7));
            r.Advance(3.2);
            // 練習: 割り当てられた練習用お宝を持って回収
            var practice = r.Host.PracticeItems.Items[0];
            r.Send(Rpc.ThGrab, 2, new ThGrabMsg { item = practice.Index, reqId = 1, gen = 0, kind = 0 });
            r.Send(Rpc.ThCollect, 2, new ThCollectMsg { item = practice.Index, gen = practice.Gen });
            Assert.IsTrue(r.Host.PracticeCompletedAt.ContainsKey("devA"));
            r.Advance(5.2);
            Assert.AreEqual(RoundState.Playing, r.Host.Machine.State);

            var s1 = r.FirstS(); var s2 = r.FirstS(1);
            r.Grab(2, s1, 1);
            int v1 = s1.Value;
            r.Send(Rpc.ThCollect, 2, new ThCollectMsg { item = s1.Index, gen = s1.Gen });
            r.Grab(2, s2, 2);
            r.Send(Rpc.ThDamage, 2, new ThDamageMsg { a = s2.Index, b = -1, speed = 1.5f });
            int loss = r.Host.Items.DamageTotal;
            Assert.Greater(loss, 0);
            Assert.AreEqual(0, r.Finished.Count, "回の途中では流れない");

            r.Advance(30.5);
            Assert.AreEqual(RoundState.Result, r.Host.Machine.State);
            Assert.AreEqual(0, r.Finished.Count);
            r.Advance(3.5);
            Assert.AreEqual(RoundState.Waiting, r.Host.Machine.State);
            Assert.AreEqual(1, r.Finished.Count);

            var rec = r.Finished[0];
            Assert.IsFalse(rec.Aborted);
            Assert.AreEqual(RoundState.Waiting, rec.AbortedState);
            Assert.AreEqual(1, rec.ParticipantCount);
            Assert.AreEqual(v1, rec.CollectedAmount);
            Assert.AreEqual(v1, r.Finished[0].Players().Single().CollectedAmount);
            Assert.AreEqual(SessionHostCore.TargetFor(1, r.Host.Params), rec.TargetAmount);
            Assert.AreEqual(loss, rec.DamageTotal);
            Assert.AreEqual(r.Host.ParamsVersion, rec.ParamsVersion);
            Assert.IsFalse(rec.WasHeld);
            var p = rec.Players().Single();
            Assert.AreEqual("devA", p.DeviceId);
            Assert.AreEqual(3, p.Number);
            Assert.AreEqual(0, p.Color);
            Assert.IsNotNull(p.PracticeDoneSeconds);
            Assert.IsNotNull(p.FirstGrabSeconds);
            Assert.IsNotNull(p.FirstCollectSeconds);
            Assert.Greater(p.FirstGrabSeconds.Value, p.PracticeDoneSeconds.Value, "本編で持ったのは練習の後");
            Assert.AreEqual(1, p.CollectedCount);
            Assert.AreEqual(1, p.DamageCount);
            Assert.AreEqual(loss, p.DamageAmount);
        }

        [Test] public void 中断した回も記録され中断した状態と保留が入る()
        {
            var r = new Rig();
            r.Register("devA", 2);
            r.ToTutorial();
            Assert.IsTrue(r.Host.Hold(r.Now));
            r.Advance(2.0);
            Assert.IsTrue(r.Host.ReleaseHold(r.Now));
            Assert.IsTrue(r.Host.Abort(r.Now));
            Assert.AreEqual(1, r.Finished.Count);
            var rec = r.Finished[0];
            Assert.IsTrue(rec.Aborted);
            Assert.AreEqual(RoundState.Tutorial, rec.AbortedState);
            Assert.IsTrue(rec.WasHeld);
            Assert.AreEqual(2.0f, rec.HeldSeconds, 0.2f);
            Assert.AreEqual(1, rec.ParticipantCount, "中断しても参加端末の控えが残る");
            Assert.AreEqual(0, rec.CollectedAmount);
            Assert.IsFalse(rec.TargetReached);
            Assert.IsFalse(rec.ReachedPlaying);
            Assert.AreEqual(0, rec.TargetAmount, "本編に入らなかった回には目標額が無い");
            StringAssert.Contains(",0,,,0\r\n", RoundCsv.RoundsRow(rec, Labels()), "回収額 0・目標額と目標達成は空欄・損害額 0");
        }

        [Test] public void 本編中の中断では回収額と減額が入る_待機中の中断は何も流れない()
        {
            var r = new Rig();
            Assert.IsFalse(r.Host.Abort(r.Now));
            Assert.AreEqual(0, r.Finished.Count);
            r.Register("devA", 2);
            r.ToPlaying();
            var s1 = r.FirstS();
            r.Grab(2, s1, 1);
            r.Send(Rpc.ThCollect, 2, new ThCollectMsg { item = s1.Index, gen = s1.Gen });
            Assert.IsTrue(r.Host.Abort(r.Now));
            Assert.AreEqual(1, r.Finished.Count);
            Assert.AreEqual(RoundState.Playing, r.Finished[0].AbortedState);
            Assert.AreEqual(s1.Value, r.Finished[0].CollectedAmount);
            Assert.AreEqual(1, r.Finished[0].Players().Single().CollectedCount);
        }

        [Test] public void 見学にした端末が回の途中で入れ替わっても記録の人数は参加端末のまま()
        {
            var r = new Rig();
            r.Register("devA", 2); r.Register("devB", 3);
            r.ToPlaying();
            r.Host.SetSpectator("devB", true, r.Now);
            r.Host.Abort(r.Now);
            Assert.AreEqual(2, r.Finished[0].ParticipantCount, "人数は本編開始時の参加端末");
        }

        // ---- HostItemTable の確定した出来事 ----

        static List<SpawnedTreasure> Spawned(params TreasureKind[] kinds)
        {
            var list = new List<SpawnedTreasure>();
            for (int i = 0; i < kinds.Length; i++)
                list.Add(new SpawnedTreasure { Index = i, Kind = kinds[i], Value = 300, LocalPosition = new Vector3(i, 0, 5), CatalogIndex = 0 });
            return list;
        }

        [Test] public void 確定した出来事_Sは持った時_Mは持ち上げた時だけ_片手予約は含めない()
        {
            var events = new List<HostItemEvent>();
            var t = new HostItemTable(Spawned(TreasureKind.S, TreasureKind.M), Zone, (rpc, dto) => { }) { Confirmed = events.Add };
            t.OnGrab("A", 2, new ThGrabMsg { item = 0, reqId = 1, kind = 0 });
            t.OnGrab("A", 2, new ThGrabMsg { item = 0, reqId = 1, kind = 0 });   // 再送では出さない
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(HostItemEventKind.Grabbed, events[0].Kind);
            Assert.AreEqual("A", events[0].Device);

            events.Clear();
            t.OnGrab("B", 3, new ThGrabMsg { item = 1, reqId = 1, kind = 1 });   // M の片手予約
            Assert.AreEqual(0, events.Count);
            t.OnLift("B", 3, new ThLiftMsg { item = 1, reqId = 1, gen = t.Get(1).Gen });
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(HostItemEventKind.Lifted, events[0].Kind);
            Assert.AreEqual("B", events[0].Device);
            t.OnLift("B", 3, new ThLiftMsg { item = 1, reqId = 1, gen = t.Get(1).Gen });   // 再送
            Assert.AreEqual(1, events.Count);
        }

        [Test] public void 確定した出来事_減額は適用した分だけ_重複と免除は含めない()
        {
            var events = new List<HostItemEvent>();
            var t = new HostItemTable(Spawned(TreasureKind.S, TreasureKind.S), Zone, (rpc, dto) => { }) { Confirmed = events.Add };
            t.OnGrab("A", 2, new ThGrabMsg { item = 0, reqId = 1, kind = 0 });
            t.OnGrab("B", 3, new ThGrabMsg { item = 1, reqId = 1, kind = 0 });
            events.Clear();

            t.OnDamage("A", new ThDamageMsg { a = 0, b = -1, speed = 1.5f }, 10.0);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(HostItemEventKind.Damaged, events[0].Kind);
            Assert.AreEqual("A", events[0].Device);
            Assert.AreEqual(90, events[0].Amount, "300G の 1 回目は 30%");

            t.OnDamage("A", new ThDamageMsg { a = 0, b = -1, speed = 1.5f }, 10.05);   // 重複で捨てる
            Assert.AreEqual(1, events.Count);
            t.OnDamage("A", new ThDamageMsg { a = 0, b = -1, speed = 1.5f, exemptA = true }, 20.0);   // 免除
            Assert.AreEqual(1, events.Count);
            t.OnDamage("B", new ThDamageMsg { a = 0, b = -1, speed = 1.5f }, 30.0);   // 判定役でない
            Assert.AreEqual(1, events.Count);
        }

        [Test] public void 確定した出来事_お宝同士の衝突は両方のお宝の持ち主に1回ずつ()
        {
            var events = new List<HostItemEvent>();
            var t = new HostItemTable(Spawned(TreasureKind.S, TreasureKind.S), Zone, (rpc, dto) => { }) { Confirmed = events.Add };
            t.OnGrab("A", 2, new ThGrabMsg { item = 0, reqId = 1, kind = 0 });
            t.OnGrab("B", 3, new ThGrabMsg { item = 1, reqId = 1, kind = 0 });
            events.Clear();
            t.OnDamage("A", new ThDamageMsg { a = 0, b = 1, speed = 2f }, 10.0);
            var dmg = events.Where(e => e.Kind == HostItemEventKind.Damaged).ToList();
            Assert.AreEqual(2, dmg.Count);
            CollectionAssert.AreEquivalent(new[] { "A", "B" }, dmg.Select(e => e.Device).ToArray());
        }

        [Test] public void 確定した出来事_3回目で消滅しても持ち主に記録され_回収は判定役に付く()
        {
            var events = new List<HostItemEvent>();
            var t = new HostItemTable(Spawned(TreasureKind.S, TreasureKind.S), Zone, (rpc, dto) => { }) { Confirmed = events.Add };
            t.OnGrab("A", 2, new ThGrabMsg { item = 0, reqId = 1, kind = 0 });
            for (int i = 0; i < 3; i++) t.OnDamage("A", new ThDamageMsg { a = 0, b = -1, speed = 1.5f }, 10.0 + i);
            var dmg = events.Where(e => e.Kind == HostItemEventKind.Damaged).ToList();
            Assert.AreEqual(3, dmg.Count);
            Assert.IsTrue(dmg.All(e => e.Device == "A"), "消滅の 3 回目も持ち主に付く");
            Assert.AreEqual(300, dmg.Sum(e => e.Amount));

            t.OnGrab("B", 3, new ThGrabMsg { item = 1, reqId = 1, kind = 0 });
            events.Clear();
            t.OnCollect("B", new ThCollectMsg { item = 1, gen = t.Get(1).Gen }, 5.0);
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(HostItemEventKind.Collected, events[0].Kind);
            Assert.AreEqual("B", events[0].Device);
            Assert.AreEqual(300, events[0].Amount);
        }

        [Test] public void 確定した出来事_切断でそっと置かれて回収されたお宝は最後に持った人に付く()
        {
            var events = new List<HostItemEvent>();
            var t = new HostItemTable(Spawned(TreasureKind.S), Zone, (rpc, dto) => { }, placeSeconds: 0.3f) { Confirmed = events.Add };
            t.OnGrab("A", 2, new ThGrabMsg { item = 0, reqId = 1, kind = 0 });
            t.OnPoses("A", new ThPosesMsg { poses = new[] { new ThPoseEntry { item = 0, pos = new Vector3(0, 0.9f, 0.8f), exempt = false } } });
            events.Clear();
            t.PlaceSolver = p => p;   // 置く先を納品場所の範囲内のままにする
            t.ForceRestItemsOf("A", 10.0);
            t.Tick(11.0, true);
            Assert.IsTrue(t.Get(0).Gone, "納品場所の範囲内で置かれたので回収");
            var col = events.Single(e => e.Kind == HostItemEventKind.Collected);
            Assert.AreEqual("A", col.Device, "判定役が外れていても最後に持った人");
        }

    }

    /// <summary>1 回ごとの記録の集計・CSV の組み立て・ファイル書き込み（#44）</summary>
    public class ThRoundLogTests
    {
        static RoundRecord MakeRecord()
        {
            var r = new RoundRecord
            {
                RoundId = 1700000000123,
                StartedAt = new DateTimeOffset(2026, 11, 18, 10, 5, 3, TimeSpan.FromHours(9)),
                StartNow = 100.0,
                Mode = "TargetAndScore",
                Extras = RoundRecord.ExtrasOf(new ThGameSettings { statue = true, golem = true }),
                GripStyle = "Beam",
                ParamsVersion = "abcd1234",
            };
            r.SetParticipants(new[] { "devB", "devA" }, ThDeviceNumbers.Parse("{ \"devices\": [ { \"deviceId\": \"devA\", \"number\": 1 } ] }").Compare, id => id == "devA" ? 1 : 0);
            return r;
        }

        static CsvLabels Labels() => CsvLabels.From(ScriptableObject.CreateInstance<ThTextTable>());

        // ---- RoundRecord ----

        [Test] public void 追加要素の文字列()
        {
            Assert.AreEqual("", RoundRecord.ExtrasOf(new ThGameSettings()));
            Assert.AreEqual("statue|golem", RoundRecord.ExtrasOf(new ThGameSettings { statue = true, golem = true }));
            Assert.AreEqual("statue|large|golem|secret", RoundRecord.ExtrasOf(new ThGameSettings { statue = true, large = true, golem = true, secret = true }));
            Assert.AreEqual("", RoundRecord.ExtrasOf(null));
        }

        [Test] public void 参加端末は端末番号の順に並び_人数はその数()
        {
            var r = MakeRecord();
            Assert.AreEqual(2, r.ParticipantCount);
            CollectionAssert.AreEqual(new[] { "devA", "devB" }, r.Players().Select(p => p.DeviceId).ToArray());
            Assert.AreEqual(1, r.Players()[0].Number);
            Assert.AreEqual(0, r.Players()[1].Number);
        }

        [Test] public void 初めて持った秒は最初の1回だけ_回の開始からの経過()
        {
            var r = MakeRecord();
            r.NoteFirstHold("devA", 112.5);
            r.NoteFirstHold("devA", 130.0);
            Assert.AreEqual(12.5, r.Players()[0].FirstGrabSeconds.Value, 1e-9);
            Assert.IsNull(r.Players()[1].FirstGrabSeconds);
            r.NoteFirstHold(null, 1.0);
            r.NoteFirstHold("", 1.0);
        }

        [Test] public void 回収と減額と練習完了を集計する()
        {
            var r = MakeRecord();
            r.NoteCollect("devA", 200, 120.0);
            r.NoteCollect("devA", 120, 130.0);
            r.NoteDamage("devA", 60);
            r.NoteDamage("devA", 60);
            r.NoteDamage("devB", 90);
            r.NotePracticeDone("devB", 108.0);
            r.NotePracticeDone("devB", 150.0);
            var a = r.Players()[0]; var b = r.Players()[1];
            Assert.AreEqual(2, a.CollectedCount); Assert.AreEqual(320, a.CollectedAmount);
            Assert.AreEqual(20.0, a.FirstCollectSeconds.Value, 1e-9, "初めて回収した秒は最初の 1 回");
            Assert.AreEqual(2, a.DamageCount); Assert.AreEqual(120, a.DamageAmount);
            Assert.AreEqual(1, b.DamageCount); Assert.AreEqual(90, b.DamageAmount);
            Assert.AreEqual(8.0, b.PracticeDoneSeconds.Value, 1e-9, "練習完了は 1 回だけ");
            Assert.IsNull(a.PracticeDoneSeconds);
            Assert.AreEqual(0, b.CollectedCount);
        }

        [Test] public void 参加端末でない端末の出来事は行に出ない_回の開始前の時刻は0秒()
        {
            var r = MakeRecord();
            r.NoteCollect("devOther", 100, 120.0);
            Assert.AreEqual(2, r.Players().Count);
            r.NoteFirstHold("devA", 50.0);
            Assert.AreEqual(0.0, r.Players()[0].FirstGrabSeconds.Value);
        }

        [Test] public void 色の割り当てを行に入れる()
        {
            var r = MakeRecord();
            r.SetColors(new Dictionary<string, int> { { "devA", 2 } });
            Assert.AreEqual(2, r.Players()[0].Color);
            Assert.AreEqual(-1, r.Players()[1].Color);
        }

        [Test] public void 目標達成は目標が1以上で回収額が届いたとき()
        {
            var r = new RoundRecord { TargetAmount = 0, CollectedAmount = 100 };
            Assert.IsFalse(r.TargetReached);
            r.TargetAmount = 2000; r.CollectedAmount = 1999;
            Assert.IsFalse(r.TargetReached);
            r.CollectedAmount = 2000;
            Assert.IsTrue(r.TargetReached);
        }

        // ---- CSV ----

        [Test] public void Escapeはカンマ_改行_ダブルクォートを含む値だけ囲む()
        {
            Assert.AreEqual("abc", RoundCsv.Escape("abc"));
            Assert.AreEqual("", RoundCsv.Escape(null));
            Assert.AreEqual("\"a,b\"", RoundCsv.Escape("a,b"));
            Assert.AreEqual("\"a\nb\"", RoundCsv.Escape("a\nb"));
            Assert.AreEqual("\"a\r\nb\"", RoundCsv.Escape("a\r\nb"));
            Assert.AreEqual("\"say \"\"hi\"\"\"", RoundCsv.Escape("say \"hi\""));
            Assert.AreEqual("日本語", RoundCsv.Escape("日本語"));
        }

        [Test] public void Lineはカンマでつなぎ改行で終わる()
        {
            Assert.AreEqual("a,\"b,c\",,d\r\n", RoundCsv.Line(new[] { "a", "b,c", "", "d" }));
        }

        [Test] public void 見出しは15列と11列()
        {
            var l = Labels();
            Assert.AreEqual(15, l.RoundsHeader.Length);
            Assert.AreEqual(11, l.PlayersHeader.Length);
            Assert.AreEqual("開始時刻", l.RoundsHeader[0]);
            Assert.AreEqual("損害額", l.RoundsHeader[14]);
            Assert.AreEqual("減額の額", l.PlayersHeader[10]);
            Assert.AreEqual(14, RoundCsv.RoundsHeaderLine(l).Count(c => c == ','));
            Assert.AreEqual(10, RoundCsv.PlayersHeaderLine(l).Count(c => c == ','));
        }

        [Test] public void roundsの1行_完了()
        {
            var r = MakeRecord();
            r.WasHeld = true; r.HeldSeconds = 12.34f;
            r.CollectedAmount = 2600; r.TargetAmount = 2500; r.DamageTotal = 450;
            var row = RoundCsv.RoundsRow(r, Labels());
            Assert.AreEqual("2026-11-18T10:05:03+09:00,1700000000123,完了,,2,TargetAndScore,statue|golem,Beam,abcd1234,はい,12.3,2600,2500,はい,450\r\n", row);
        }

        [Test] public void roundsの1行_中断()
        {
            var r = MakeRecord();
            r.Aborted = true; r.AbortedState = RoundState.Playing;
            r.CollectedAmount = 800; r.TargetAmount = 2500;
            var row = RoundCsv.RoundsRow(r, Labels());
            Assert.AreEqual("2026-11-18T10:05:03+09:00,1700000000123,中断,Playing,2,TargetAndScore,statue|golem,Beam,abcd1234,いいえ,0.0,800,2500,いいえ,0\r\n", row);
        }

        [Test] public void playersの1行_無い値は空欄()
        {
            var r = MakeRecord();
            r.SetColors(new Dictionary<string, int> { { "devA", 0 } });
            r.NoteFirstHold("devA", 112.34);
            r.NoteCollect("devA", 200, 140.0);
            r.NoteDamage("devA", 60);
            var p = r.Players();
            Assert.AreEqual("1700000000123,1,devA,0,,12.3,40.0,1,200,1,60\r\n", RoundCsv.PlayersRow(r, p[0]));
            Assert.AreEqual("1700000000123,,devB,,,,,0,0,0,0\r\n", RoundCsv.PlayersRow(r, p[1]), "未登録の端末番号・色なし・無い秒は空欄");
        }

        [Test] public void 値にカンマが入ってもクォートされる()
        {
            var r = MakeRecord();
            r.ParamsVersion = "a,b";
            var row = RoundCsv.RoundsRow(r, Labels());
            StringAssert.Contains(",\"a,b\",", row);
        }

        [Test] public void ファイル名は回の開始時刻のローカル日付()
        {
            var t = new DateTimeOffset(2026, 11, 18, 23, 59, 59, TimeSpan.FromHours(9));
            Assert.AreEqual("rounds_20261118.csv", RoundCsv.RoundsFileName(t));
            Assert.AreEqual("players_20261118.csv", RoundCsv.PlayersFileName(t));
        }

        // ---- ファイル ----

        string _dir;

        [SetUp] public void SetUp() { _dir = Path.Combine(Path.GetTempPath(), "th_roundlog_" + Guid.NewGuid().ToString("N")); }
        [TearDown] public void TearDown()
        {
            if (_dir == null) return;
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
            else if (File.Exists(_dir)) File.Delete(_dir);
        }

        [Test] public void 初回は見出しから書き_BOM付きUTF8_2回目は追記だけ()
        {
            var logger = new RoundLogger(_dir, Labels());
            var r = MakeRecord();
            Assert.IsNull(logger.Write(r));
            var roundsPath = Path.Combine(_dir, "logs", "rounds_20261118.csv");
            var playersPath = Path.Combine(_dir, "logs", "players_20261118.csv");
            var bytes = File.ReadAllBytes(roundsPath);
            CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray(), "Excel 用の BOM");
            var lines = File.ReadAllText(roundsPath).TrimStart('\uFEFF').Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(2, lines.Length, "見出し + 1 行");
            Assert.IsTrue(lines[0].StartsWith("開始時刻,RoundId"));
            var plines = File.ReadAllText(playersPath).TrimStart('\uFEFF').Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(3, plines.Length, "見出し + 参加端末 2 行");

            r.RoundId++;
            Assert.IsNull(logger.Write(r));
            var bytes2 = File.ReadAllBytes(roundsPath);
            CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes2.Take(3).ToArray());
            int boms = 0;
            for (int k = 0; k + 2 < bytes2.Length; k++) if (bytes2[k] == 0xEF && bytes2[k + 1] == 0xBB && bytes2[k + 2] == 0xBF) boms++;
            Assert.AreEqual(1, boms, "BOM は先頭に 1 回だけ");
            var lines2 = File.ReadAllText(roundsPath).TrimStart('\uFEFF').Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(3, lines2.Length, "見出しは 1 回だけで、2 回目は行を足す");
            Assert.IsTrue(lines2[2].Contains("1700000000124"));
        }

        [Test] public void 回の開始日が違えば別のファイルに書く()
        {
            var logger = new RoundLogger(_dir, Labels());
            var r = MakeRecord();
            logger.Write(r);
            r.StartedAt = r.StartedAt.AddDays(1);
            logger.Write(r);
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "logs", "rounds_20261118.csv")));
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "logs", "rounds_20261119.csv")));
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "logs", "players_20261119.csv")));
        }

        [Test] public void 書けないときは例外を出さず警告を返す()
        {
            File.WriteAllText(_dir, "これはフォルダではなくファイル");   // logs フォルダを作れない
            var logger = new RoundLogger(_dir, Labels());
            string warning = null;
            Assert.DoesNotThrow(() => warning = logger.Write(MakeRecord()));
            Assert.IsNotNull(warning);
        }

        [Test] public void 書けなかった行は次のWriteで書き直す()
        {
            File.WriteAllText(_dir, "これはフォルダではなくファイル");   // logs フォルダを作れない
            var logger = new RoundLogger(_dir, Labels());
            Assert.IsNotNull(logger.Write(MakeRecord()));
            Assert.AreEqual(2, logger.PendingCount, "rounds と players の 2 件を覚えている");
            File.Delete(_dir);   // 書けるようにする
            Assert.IsNull(logger.Write(MakeRecord()));
            Assert.AreEqual(0, logger.PendingCount);
            var rounds = File.ReadAllText(Path.Combine(_dir, "logs", RoundCsv.RoundsFileName(MakeRecord().StartedAt))).TrimStart('\uFEFF')
                .Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            Assert.AreEqual(3, rounds.Length, "見出し + 書けなかった回 + 今回");
        }

        [Test] public void 参加端末が0台でもroundsは書きplayersは書かない()
        {
            var logger = new RoundLogger(_dir, Labels());
            var r = new RoundRecord { RoundId = 5, StartedAt = new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.FromHours(9)) };
            Assert.IsNull(logger.Write(r));
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "logs", "rounds_20261118.csv")));
            Assert.IsFalse(File.Exists(Path.Combine(_dir, "logs", "players_20261118.csv")));
        }
    }
}
