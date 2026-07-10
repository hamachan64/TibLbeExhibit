using NUnit.Framework;

namespace STYLY.TIB_Template.GIPoker.Tests
{
    /// <summary>
    /// ResultGrade（最終スコア → グレード判定）のEditModeテスト。
    /// 閾値の設計根拠は ResultGrade.cs のコメント
    /// （GI_POKER_PLAN.md「3. カードランクとスコアシステム」準拠）を参照。
    /// </summary>
    public class ResultGradeTests
    {
        // --- 閾値境界の判定 ---

        [TestCase(0, ResultGradeRank.D)]
        [TestCase(1, ResultGradeRank.D)]
        [TestCase(14, ResultGradeRank.D)]
        [TestCase(15, ResultGradeRank.C)]
        [TestCase(39, ResultGradeRank.C)]
        [TestCase(40, ResultGradeRank.B)]
        [TestCase(99, ResultGradeRank.B)]
        [TestCase(100, ResultGradeRank.A)]
        [TestCase(299, ResultGradeRank.A)]
        [TestCase(300, ResultGradeRank.S)]
        [TestCase(1000, ResultGradeRank.S)]
        public void Judge_BoundaryScores_ReturnsExpectedGrade(int totalScore, ResultGradeRank expected)
        {
            Assert.That(ResultGrade.Judge(totalScore), Is.EqualTo(expected));
        }

        [Test]
        public void Judge_NegativeScore_ReturnsD()
        {
            // スコアが負になることは通常ないが、最低評価に丸められること
            Assert.That(ResultGrade.Judge(-1), Is.EqualTo(ResultGradeRank.D));
        }

        // --- 得点体系との整合（代表的なプレイ結果がねらい通りのグレードになるか） ---

        [Test]
        public void Judge_AverageScoreBand_FallsIntoCB()
        {
            // 想定平均帯 30〜60点 は C〜B に収まる（S/A は「上振れ」専用）
            Assert.That(ResultGrade.Judge(30), Is.EqualTo(ResultGradeRank.C));
            Assert.That(ResultGrade.Judge(60), Is.EqualTo(ResultGradeRank.B));
        }

        [Test]
        public void Judge_SingleSCard_ReachesA()
        {
            // Sカード「ブループラネット」1枚（100点）で A 評価に到達する
            Assert.That(ResultGrade.Judge(100), Is.EqualTo(ResultGradeRank.A));
        }

        [Test]
        public void Judge_StraightWithSCard_ReachesS()
        {
            // ストレート（基本111点＋ボーナス200点＝311点）で最高評価 S に到達する
            Assert.That(ResultGrade.Judge(111 + 200), Is.EqualTo(ResultGradeRank.S));
        }

        [Test]
        public void Judge_SsEventReward_ReachesS()
        {
            // SSイベント報酬（300点）単独でも S 評価に到達する
            Assert.That(ResultGrade.Judge(300), Is.EqualTo(ResultGradeRank.S));
        }

        // --- 表示定義（色・ラベル）の網羅 ---

        [TestCase(ResultGradeRank.D)]
        [TestCase(ResultGradeRank.C)]
        [TestCase(ResultGradeRank.B)]
        [TestCase(ResultGradeRank.A)]
        [TestCase(ResultGradeRank.S)]
        public void GetLabel_AllGrades_ReturnsNonEmpty(ResultGradeRank grade)
        {
            Assert.That(ResultGrade.GetLabel(grade), Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void GetColor_AllGrades_AreDistinct()
        {
            var colors = new[]
            {
                ResultGrade.GetColor(ResultGradeRank.D),
                ResultGrade.GetColor(ResultGradeRank.C),
                ResultGrade.GetColor(ResultGradeRank.B),
                ResultGrade.GetColor(ResultGradeRank.A),
                ResultGrade.GetColor(ResultGradeRank.S),
            };
            for (var i = 0; i < colors.Length; i++)
            {
                for (var j = i + 1; j < colors.Length; j++)
                {
                    Assert.That(colors[i], Is.Not.EqualTo(colors[j]),
                        $"グレード色が重複: index {i} と {j}");
                }
            }
        }
    }
}
