using System;
using System.IO;
using NUnit.Framework;
using STYLY.TIB_Template.Leaderboard;

namespace STYLY.TIB_Template.Tests
{
    /// <summary>
    /// DailyLeaderboardStore の一時パスへのJSON Save/Load往復テスト。
    /// </summary>
    public class DailyLeaderboardStoreTests
    {
        private static readonly DateTime BaseDate = new(2026, 8, 3);

        private string tempFilePath;

        [SetUp]
        public void SetUp()
        {
            tempFilePath = Path.Combine(Path.GetTempPath(), $"gi_poker_leaderboard_test_{Guid.NewGuid():N}.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }

        [Test]
        public void SaveThenLoad_RoundTripsEntriesAndDate()
        {
            var store = new DailyLeaderboardStore(tempFilePath);
            var board = new DailyLeaderboard(BaseDate);
            board.Record(1, 30);
            board.Record(2, 50);
            board.Record(3, 10);

            store.Save(board);

            Assert.That(File.Exists(tempFilePath), Is.True);

            var loaded = store.Load(BaseDate.AddDays(10)); // フォールバック日は使われないはず

            Assert.That(loaded.RecordedDate, Is.EqualTo(BaseDate.Date));
            Assert.That(loaded.Top3.Count, Is.EqualTo(3));
            Assert.That(loaded.Top3[0].ClientNo, Is.EqualTo(2));
            Assert.That(loaded.Top3[0].Score, Is.EqualTo(50));
            Assert.That(loaded.Top3[1].ClientNo, Is.EqualTo(1));
            Assert.That(loaded.Top3[2].ClientNo, Is.EqualTo(3));
        }

        [Test]
        public void Load_FileDoesNotExist_ReturnsEmptyBoardWithFallbackDate()
        {
            var store = new DailyLeaderboardStore(tempFilePath);
            var fallback = new DateTime(2026, 1, 1);

            var loaded = store.Load(fallback);

            Assert.That(loaded.Top3, Is.Empty);
            Assert.That(loaded.RecordedDate, Is.EqualTo(fallback.Date));
        }

        [Test]
        public void Load_CorruptedFile_ReturnsEmptyBoardWithFallbackDate()
        {
            File.WriteAllText(tempFilePath, "{ this is not valid json !!");
            var store = new DailyLeaderboardStore(tempFilePath);
            var fallback = new DateTime(2026, 2, 2);

            var loaded = store.Load(fallback);

            Assert.That(loaded.Top3, Is.Empty);
            Assert.That(loaded.RecordedDate, Is.EqualTo(fallback.Date));
        }

        [Test]
        public void Save_CreatesMissingDirectory()
        {
            var nestedPath = Path.Combine(Path.GetTempPath(), $"gi_poker_nested_{Guid.NewGuid():N}", "leaderboard.json");
            var store = new DailyLeaderboardStore(nestedPath);
            var board = new DailyLeaderboard(BaseDate);
            board.Record(1, 5);

            store.Save(board);

            Assert.That(File.Exists(nestedPath), Is.True);

            // 後片付け
            File.Delete(nestedPath);
            Directory.Delete(Path.GetDirectoryName(nestedPath)!);
        }
    }
}
