using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>リザルトのグレード（総合評価）。カードランクとは独立した評価軸。</summary>
    public enum ResultGradeRank
    {
        D,
        C,
        B,
        A,
        S,
    }

    /// <summary>
    /// 最終スコア（TotalScore）からグレード（S/A/B/C/D）を判定する
    /// 純粋ロジック（MonoBehaviour 非依存・ユニットテスト可能）。
    ///
    /// 閾値の設計根拠:
    /// - 単体得点: D=10 / C=20 / B=50 / A=100 / S=500 / SS=10000
    /// - ボーナス: 全ランク獲得+5000 / 全種類獲得+10000 / ボーナスタイム収納 1.5倍
    /// </summary>
    public static class ResultGrade
    {
        /// <summary>C評価の下限（100点）。</summary>
        public const int ThresholdC = 100;

        /// <summary>B評価の下限（500点）。</summary>
        public const int ThresholdB = 500;

        /// <summary>A評価の下限（1,000点）。</summary>
        public const int ThresholdA = 1000;

        /// <summary>S評価の下限（5,000点）。</summary>
        public const int ThresholdS = 5000;

        /// <summary>最終スコアからグレードを判定する。</summary>
        public static ResultGradeRank Judge(int totalScore)
        {
            if (totalScore >= ThresholdS)
            {
                return ResultGradeRank.S;
            }
            if (totalScore >= ThresholdA)
            {
                return ResultGradeRank.A;
            }
            if (totalScore >= ThresholdB)
            {
                return ResultGradeRank.B;
            }
            if (totalScore >= ThresholdC)
            {
                return ResultGradeRank.C;
            }
            return ResultGradeRank.D;
        }

        /// <summary>グレードの表示色（S=金 / A=赤 / B=紫 / C=青 / D=灰）。</summary>
        public static Color GetColor(ResultGradeRank grade)
        {
            return grade switch
            {
                ResultGradeRank.S => new Color(0.95f, 0.78f, 0.25f), // ゴールド
                ResultGradeRank.A => new Color(0.90f, 0.25f, 0.25f), // レッド
                ResultGradeRank.B => new Color(0.60f, 0.30f, 0.85f), // バイオレット
                ResultGradeRank.C => new Color(0.25f, 0.45f, 0.85f), // ブルー
                _ => new Color(0.55f, 0.57f, 0.60f),                 // グレー
            };
        }

        /// <summary>グレードの日本語ラベル（リザルトのスタンプ下に表示する称号）。</summary>
        public static string GetLabel(ResultGradeRank grade)
        {
            return grade switch
            {
                ResultGradeRank.S => "伝説のハンター！",
                ResultGradeRank.A => "一流ハンター！",
                ResultGradeRank.B => "熟練ハンター",
                ResultGradeRank.C => "新米ハンター",
                _ => "一般人",
            };
        }
    }
}
