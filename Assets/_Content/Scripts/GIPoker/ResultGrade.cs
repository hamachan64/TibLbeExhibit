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
    /// 閾値の設計根拠（GI_POKER_PLAN.md「3. カードランクとスコアシステム」より）:
    /// - 単体得点: D=1 / C=2 / B=3 / A=5 / S=100 / SS=300
    /// - ボーナス: ストレート+200 / S-A-A+150 / 同ランク3枚+50
    /// - 5分プレイの平均スコアは 30〜60 点を想定し、
    ///   Sカード獲得や大きな役の成立による 100 点超えを「上振れ」とする。
    /// </summary>
    public static class ResultGrade
    {
        /// <summary>
        /// C評価の下限（15点）。
        /// D/Cカードを10枚前後（例: D×5 + C×5 = 15点）集めた最低限の収集ペース。
        /// これ未満はほとんどカード化できなかったプレイとして D 評価。
        /// </summary>
        public const int ThresholdC = 15;

        /// <summary>
        /// B評価の下限（40点）。
        /// 想定平均帯（30〜60点）の中央値付近。同ランク3枚セット1回（+50）が
        /// 成立すればほぼ確実に届く「標準〜良好」の入口。
        /// </summary>
        public const int ThresholdB = 40;

        /// <summary>
        /// A評価の下限（100点）。
        /// Sカード「ブループラネット」1枚（100点）の獲得、または役の複合成立に相当する
        /// 「上振れ」の入口。平均帯からは役なしでは届かない。
        /// </summary>
        public const int ThresholdA = 100;

        /// <summary>
        /// S評価の下限（300点）。
        /// SSイベント報酬（300点）、またはストレート＋Sカード（111+200=311点）級の
        /// 一発逆転が決まったプレイのみ到達できる最高評価。
        /// </summary>
        public const int ThresholdS = 300;

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
                _ => "見習いハンター",
            };
        }
    }
}
