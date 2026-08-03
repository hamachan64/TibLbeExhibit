using System;
using NUnit.Framework;
using STYLY.TIB_Template.Leaderboard;

namespace STYLY.TIB_Template.Tests
{
    /// <summary>
    /// DailyLeaderboard（本日通算トップ3）のEditModeテスト。
    /// </summary>
    public class DailyLeaderboardTests
    {
        private static readonly DateTime BaseDate = new(2026, 8, 3);

        [Test]
        public void Record_FourthEntry_DropsLowestScore()
        {
            var board = new DailyLeaderboard(BaseDate);
            board.Record(1, 10);
            board.Record(2, 30);
            board.Record(3, 20);
            board.Record(4, 5); // 4件目（最下位）は落ちる

            Assert.That(board.Top3.Count, Is.EqualTo(3));
            Assert.That(board.Top3[0].ClientNo, Is.EqualTo(2));
            Assert.That(board.Top3[0].Score, Is.EqualTo(30));
            Assert.That(board.Top3[1].ClientNo, Is.EqualTo(3));
            Assert.That(board.Top3[1].Score, Is.EqualTo(20));
            Assert.That(board.Top3[2].ClientNo, Is.EqualTo(1));
            Assert.That(board.Top3[2].Score, Is.EqualTo(10));

            // clientNo=4（スコア5）は上位3件から漏れている
            Assert.That(board.Top3, Has.None.Matches<LeaderboardEntry>(e => e.ClientNo == 4));
        }

        [Test]
        public void Record_TiedScores_KeepsExistingOrder()
        {
            var board = new DailyLeaderboard(BaseDate);
            board.Record(1, 50);
            board.Record(2, 50); // 同点。先着(clientNo=1)が優先される
            board.Record(3, 50);
            board.Record(4, 50); // 4人目の同点は落ちる

            Assert.That(board.Top3.Count, Is.EqualTo(3));
            Assert.That(board.Top3[0].ClientNo, Is.EqualTo(1));
            Assert.That(board.Top3[1].ClientNo, Is.EqualTo(2));
            Assert.That(board.Top3[2].ClientNo, Is.EqualTo(3));
        }

        [Test]
        public void Record_FewerThanThreeEntries_KeepsAll()
        {
            var board = new DailyLeaderboard(BaseDate);
            board.Record(1, 10);
            board.Record(2, 20);

            Assert.That(board.Top3.Count, Is.EqualTo(2));
            Assert.That(board.Top3[0].ClientNo, Is.EqualTo(2));
            Assert.That(board.Top3[1].ClientNo, Is.EqualTo(1));
        }

        [Test]
        public void NeedsReset_SameDate_ReturnsFalse()
        {
            var board = new DailyLeaderboard(BaseDate);
            var laterSameDay = BaseDate.AddHours(23);
            Assert.That(board.NeedsReset(laterSameDay), Is.False);
        }

        [Test]
        public void NeedsReset_NextDate_ReturnsTrue()
        {
            var board = new DailyLeaderboard(BaseDate);
            var nextDay = BaseDate.AddDays(1);
            Assert.That(board.NeedsReset(nextDay), Is.True);
        }

        [Test]
        public void Reset_ClearsEntriesAndUpdatesRecordedDate()
        {
            var board = new DailyLeaderboard(BaseDate);
            board.Record(1, 10);
            var nextDay = BaseDate.AddDays(1);

            board.Reset(nextDay);

            Assert.That(board.Top3, Is.Empty);
            Assert.That(board.RecordedDate, Is.EqualTo(nextDay.Date));
            Assert.That(board.NeedsReset(nextDay), Is.False);
        }

        [Test]
        public void RestoreState_MoreThanThreeEntries_KeepsOnlyTopThree()
        {
            var board = new DailyLeaderboard(BaseDate);
            board.RestoreState(BaseDate, new[]
            {
                new LeaderboardEntry(1, 10),
                new LeaderboardEntry(2, 40),
                new LeaderboardEntry(3, 30),
                new LeaderboardEntry(4, 20),
            });

            Assert.That(board.Top3.Count, Is.EqualTo(3));
            Assert.That(board.Top3[0].ClientNo, Is.EqualTo(2));
            Assert.That(board.Top3[1].ClientNo, Is.EqualTo(3));
            Assert.That(board.Top3[2].ClientNo, Is.EqualTo(4));
        }
    }
}
