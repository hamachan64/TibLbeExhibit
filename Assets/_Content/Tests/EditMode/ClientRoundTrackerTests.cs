using NUnit.Framework;

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    public class ClientRoundTrackerTests
    {
        static ThProgressParams Times() => new ThProgressParams { syncTolerance = 0.25f, hostLostSeconds = 10f, resendInterval = 0.5f };

        static ThStateMsg State(RoundState s, int seq, float remaining, params string[] participants) =>
            new ThStateMsg { seq = seq, state = (int)s, remaining = remaining, participants = participants, paramsVersion = "v1" };

        static ThHeartbeatMsg Beat(int seq, float remaining) => new ThHeartbeatMsg { seq = seq, remaining = remaining, paramsVersion = "v1" };

        static ClientRoundTracker Make(double now = 0) => new ClientRoundTracker("me", Times(), now) { LocalParamsVersion = "v1" };

        [Test] public void ThStateを採用し残り秒を自分で数える()
        {
            var t = Make();
            t.OnState(1000, State(RoundState.Opening, 1, 10f, "me"), 0);
            Assert.AreEqual(RoundState.Opening, t.State.CurrentValue);
            Assert.AreEqual(1000, t.RoundId);
            Assert.AreEqual(1, t.Seq);
            Assert.IsTrue(t.IsParticipant.CurrentValue);
            t.Tick(2.5);
            Assert.AreEqual(7.5f, t.RemainingSeconds.CurrentValue, 0.001f);
        }

        [Test] public void ずれがしきい値未満なら合わせない()
        {
            var t = Make();
            t.OnState(1000, State(RoundState.Playing, 1, 100f, "me"), 0);
            t.Tick(5);                                   // 自分: 95
            t.OnHeartbeat(1000, Beat(1, 94.9f), 5);      // ずれ 0.1
            t.Tick(5);
            Assert.AreEqual(95f, t.RemainingSeconds.CurrentValue, 0.001f);
        }

        [Test] public void ずれがしきい値以上なら合わせる()
        {
            var t = Make();
            t.OnState(1000, State(RoundState.Playing, 1, 100f, "me"), 0);
            t.Tick(5);                                   // 自分: 95
            t.OnHeartbeat(1000, Beat(1, 94.7f), 5);      // ずれ 0.3
            Assert.AreEqual(94.7f, t.RemainingSeconds.CurrentValue, 0.001f);
            t.Tick(6);
            Assert.AreEqual(93.7f, t.RemainingSeconds.CurrentValue, 0.001f);
        }

        [Test] public void ちょうどしきい値のずれも合わせる()
        {
            var t = Make();
            t.OnState(1000, State(RoundState.Playing, 1, 100f, "me"), 0);
            t.Tick(5);                                   // 95
            t.OnHeartbeat(1000, Beat(1, 95.25f), 5);
            Assert.AreEqual(95.25f, t.RemainingSeconds.CurrentValue, 0.001f);
        }

        [Test] public void 古いRoundIdは捨てる()
        {
            var t = Make();
            t.OnState(2000, State(RoundState.Opening, 1, 10f, "me"), 0);
            t.OnState(1000, State(RoundState.Waiting, 5, 0f), 1);
            t.OnHeartbeat(1000, Beat(5, 3f), 1);
            Assert.AreEqual(RoundState.Opening, t.State.CurrentValue);
            Assert.AreEqual(2000, t.RoundId);
            Assert.AreEqual(1, t.Seq);
        }

        [Test] public void 同じ回の古い通し番号のThStateは捨てる()
        {
            var t = Make();
            t.OnState(1000, State(RoundState.Tutorial, 2, 30f, "me"), 0);
            t.OnState(1000, State(RoundState.Opening, 1, 10f, "me"), 0.1);
            Assert.AreEqual(RoundState.Tutorial, t.State.CurrentValue);
        }

        [Test] public void 通し番号が違うHeartbeatで再送要求()
        {
            var t = Make();
            t.OnState(1000, State(RoundState.Opening, 1, 10f, "me"), 0);
            t.NoteResyncSent(0);
            t.OnHeartbeat(1000, Beat(1, 10f), 0);
            Assert.IsFalse(t.NeedsResync);
            t.OnHeartbeat(1000, Beat(2, 5f), 5);   // ThState(2) を取りこぼした
            Assert.IsTrue(t.NeedsResync);
            Assert.IsTrue(t.TryTakeResync(5));
        }

        [Test] public void 新しいRoundIdのHeartbeatだけ来たら再送要求()
        {
            var t = Make();
            t.OnState(0, State(RoundState.Waiting, 0, 0f), 0);
            t.OnHeartbeat(3000, Beat(1, 9f), 1);
            Assert.IsTrue(t.NeedsResync);
        }

        [Test] public void 再送要求は再送間隔より短く繰り返さない()
        {
            var t = Make();
            t.OnHeartbeat(1000, Beat(5, 5f), 0);   // まだ ThState を受けていない
            Assert.IsTrue(t.TryTakeResync(1.0));
            Assert.IsFalse(t.TryTakeResync(1.2));
            Assert.IsFalse(t.TryTakeResync(1.49));
            Assert.IsTrue(t.TryTakeResync(1.5));
            t.OnState(1000, State(RoundState.Opening, 5, 5f, "me"), 1.6);
            Assert.IsFalse(t.TryTakeResync(5.0), "追いついたら要求しない");
        }

        [Test] public void パラメータ版が違うと再送要求()
        {
            var t = Make();
            t.OnState(1000, State(RoundState.Opening, 1, 10f, "me"), 0);
            t.NoteResyncSent(0);
            t.LocalParamsVersion = "old";
            Assert.IsTrue(t.NeedsResync);
            t.LocalParamsVersion = "v1";
            Assert.IsFalse(t.NeedsResync);
        }

        [Test] public void 回の途中から入ったらその回は参加しない()
        {
            var t = Make();
            t.OnState(1000, State(RoundState.Playing, 4, 100f, "me", "other"), 0);
            Assert.IsTrue(t.WaitNextRound);
            Assert.IsFalse(t.IsParticipant.CurrentValue);
            t.OnState(1000, State(RoundState.Result, 5, 15f, "me", "other"), 1);
            Assert.IsFalse(t.IsParticipant.CurrentValue);
            t.OnState(0, State(RoundState.Waiting, 6, 0f), 2);   // 次の Waiting で解除
            Assert.IsFalse(t.WaitNextRound);
            t.OnState(2000, State(RoundState.Opening, 7, 10f, "me"), 3);
            Assert.IsTrue(t.IsParticipant.CurrentValue);
        }

        [Test] public void Openingから見ていれば参加する()
        {
            var t = Make();
            t.OnState(1000, State(RoundState.Opening, 1, 10f, "me"), 0);
            t.OnState(1000, State(RoundState.Tutorial, 2, 30f, "me"), 10);
            Assert.IsFalse(t.WaitNextRound);
            Assert.IsTrue(t.IsParticipant.CurrentValue);
        }

        [Test] public void 参加端末に入っていなければ参加しない()
        {
            var t = Make();
            t.OnState(1000, State(RoundState.Opening, 1, 10f, "other"), 0);
            Assert.IsFalse(t.IsParticipant.CurrentValue);
        }

        [Test] public void Heartbeatが途絶えると回を中止してWaitingに戻る()
        {
            var t = Make();
            t.OnState(1000, State(RoundState.Playing, 3, 100f, "me"), 0);
            t.OnHeartbeat(1000, Beat(3, 99f), 1);
            t.Tick(10.9);
            Assert.AreEqual(RoundState.Playing, t.State.CurrentValue);
            Assert.IsFalse(t.IsHostLost.CurrentValue);
            t.Tick(11.0);
            Assert.AreEqual(RoundState.Waiting, t.State.CurrentValue);
            Assert.IsTrue(t.IsHostLost.CurrentValue);
            Assert.IsFalse(t.IsParticipant.CurrentValue);
        }

        [Test] public void 中止した回の続きは受け付けず再開後の新しい回には参加できる()
        {
            var t = Make();
            t.OnState(1000, State(RoundState.Playing, 3, 100f, "me"), 0);
            t.Tick(10);
            Assert.AreEqual(RoundState.Waiting, t.State.CurrentValue);
            t.OnHeartbeat(1000, Beat(3, 80f), 12);        // 同じ回が続いていると言われても戻らない
            t.OnState(1000, State(RoundState.Playing, 3, 80f, "me"), 12);
            Assert.AreEqual(RoundState.Waiting, t.State.CurrentValue);
            Assert.IsFalse(t.IsHostLost.CurrentValue, "ホストは生きている");
            Assert.IsFalse(t.NeedsResync);
            t.OnState(5000, State(RoundState.Opening, 1, 10f, "me"), 20);
            Assert.AreEqual(RoundState.Opening, t.State.CurrentValue);
            Assert.IsTrue(t.IsParticipant.CurrentValue);
        }

        [Test] public void 待機中にHeartbeatが途絶えると運営PC未接続のフラグだけ立つ()
        {
            var t = Make();
            t.OnState(0, State(RoundState.Waiting, 0, 0f), 0);
            t.OnHeartbeat(0, Beat(0, 0f), 1);
            t.Tick(11.0);
            Assert.AreEqual(RoundState.Waiting, t.State.CurrentValue);
            Assert.IsTrue(t.IsHostLost.CurrentValue);
            t.OnHeartbeat(0, Beat(0, 0f), 12);
            Assert.IsFalse(t.IsHostLost.CurrentValue);
        }

        [Test] public void 運営PCを再起動して通し番号が戻っても待機に追いつく()
        {
            var t = Make();
            t.OnState(1000, State(RoundState.Result, 40, 5f, "me"), 0);
            t.OnHeartbeat(0, Beat(0, 0f), 1);               // 再起動後のホスト（回なし・通し番号 0）
            Assert.IsTrue(t.NeedsResync);
            t.OnState(0, State(RoundState.Waiting, 0, 0f), 1.1);
            Assert.AreEqual(RoundState.Waiting, t.State.CurrentValue);
            Assert.AreEqual(0, t.RoundId);
            Assert.IsTrue(t.TryTakeResync(2.0));
            Assert.IsFalse(t.NeedsResync);
        }

        [Test] public void 運営PCが再起動したらHeartbeatの起動値の変化で状況報告を送り直す()
        {
            var t = Make();
            t.OnState(0, new ThStateMsg { seq = 0, state = (int)RoundState.Waiting, paramsVersion = "v1", hostEpoch = 100 }, 0);
            Assert.IsTrue(t.NeedsResync, "初めて知った起動値にも 1 回報告する");
            Assert.IsTrue(t.TryTakeResync(0));
            t.OnHeartbeat(0, new ThHeartbeatMsg { seq = 0, paramsVersion = "v1", hostEpoch = 100 }, 1);
            Assert.IsFalse(t.NeedsResync);
            // 運営PCを再起動（通し番号も回も同じ 0 のまま、起動値だけ変わる）
            t.OnHeartbeat(0, new ThHeartbeatMsg { seq = 0, paramsVersion = "v1", hostEpoch = 200 }, 2);
            Assert.IsTrue(t.NeedsResync);
            Assert.IsTrue(t.TryTakeResync(2));
            Assert.IsFalse(t.NeedsResync);
            t.OnHeartbeat(0, new ThHeartbeatMsg { seq = 0, paramsVersion = "v1", hostEpoch = 200 }, 3);
            Assert.IsFalse(t.NeedsResync);
        }

        [Test] public void 再起動後に他の端末の報告への返信のThStateを先に受けても自分も報告を送る()
        {
            var t = Make();
            t.OnState(0, new ThStateMsg { seq = 0, state = (int)RoundState.Waiting, paramsVersion = "v1", hostEpoch = 100 }, 0);
            // 再起動後、他の端末への返信の ThState（新しい起動値）が先に届く
            t.OnState(0, new ThStateMsg { seq = 0, state = (int)RoundState.Waiting, paramsVersion = "v1", hostEpoch = 200 }, 1);
            Assert.IsTrue(t.NeedsResync);
            t.OnHeartbeat(0, new ThHeartbeatMsg { seq = 0, paramsVersion = "v1", hostEpoch = 200 }, 1.1);
            Assert.IsTrue(t.NeedsResync, "ThState を受けても消えない");
            Assert.IsTrue(t.TryTakeResync(1.2));
            Assert.IsFalse(t.NeedsResync);
        }

        [Test] public void 回の途中から入ったら報告を送る()
        {
            var t = Make();
            t.OnState(1000, State(RoundState.Playing, 4, 100f, "me"), 0);
            Assert.IsTrue(t.WaitNextRound);
            Assert.IsTrue(t.TryTakeResync(0));
        }

        [Test] public void RequestResyncは間隔制限を通る()
        {
            var t = Make();
            t.OnState(0, State(RoundState.Waiting, 0, 0f), 0);
            t.NoteResyncSent(0);   // 起動時の報告は済み
            t.RequestResync();
            Assert.IsTrue(t.TryTakeResync(1.0));
            t.RequestResync();
            Assert.IsFalse(t.TryTakeResync(1.2));
            Assert.IsTrue(t.TryTakeResync(1.5));
        }
    }
}
