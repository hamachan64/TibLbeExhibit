using System;
using System.Collections.Generic;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>成立した組み合わせボーナス（役）1件分。</summary>
    public readonly struct AchievedCombo
    {
        public string Name { get; }
        public int Bonus { get; }

        public AchievedCombo(string name, int bonus)
        {
            Name = name;
            Bonus = bonus;
        }
    }

    /// <summary>スコア計算結果（基本得点＋組み合わせボーナスの内訳）。</summary>
    public sealed class ScoreResult
    {
        public static readonly ScoreResult Empty = new(0, Array.Empty<AchievedCombo>());

        /// <summary>カード単体得点の合計</summary>
        public int BaseScore { get; }

        /// <summary>組み合わせボーナスの合計</summary>
        public int BonusScore { get; }

        /// <summary>最終スコア（基本得点＋ボーナス）</summary>
        public int TotalScore => BaseScore + BonusScore;

        /// <summary>成立した役の一覧（リザルトUI表示用）</summary>
        public IReadOnlyList<AchievedCombo> Combos { get; }

        public ScoreResult(int baseScore, IReadOnlyList<AchievedCombo> combos)
        {
            BaseScore = baseScore;
            Combos = combos;
            var bonus = 0;
            for (var i = 0; i < combos.Count; i++)
            {
                bonus += combos[i].Bonus;
            }
            BonusScore = bonus;
        }
    }

    /// <summary>
    /// バインダー内カードのランク構成から「基本得点＋組み合わせボーナス（役）」を計算する
    /// 純粋ロジック（MonoBehaviour 非依存・ユニットテスト可能）。
    ///
    /// 役の判定はボーナス点が高い順の貪欲法で行い、1枚のカードは1つの役にしか使われない。
    /// 判定順: ストレート(+200) → S-A-A(+150) → 同ランク3枚セット(+50)
    /// ※同一の役は成立する限り複数回カウントされる。
    /// </summary>
    public static class PokerHandEvaluator
    {
        /// <summary>
        /// ランクごとの単体得点。
        /// SS はイベント（リスキーダイス大吉）限定の特別報酬のため S を超える 300 点とする。
        /// </summary>
        public static readonly IReadOnlyDictionary<CardRank, int> BasePoints = new Dictionary<CardRank, int>
        {
            { CardRank.D, 1 },
            { CardRank.C, 2 },
            { CardRank.B, 3 },
            { CardRank.A, 5 },
            { CardRank.S, 100 },
            { CardRank.SS, 300 },
        };

        /// <summary>ストレート（D-C-B-A-S 各1枚）のボーナス</summary>
        public const int StraightBonus = 200;
        /// <summary>指定コンボ（S-A-A）のボーナス</summary>
        public const int SpecialComboBonus = 150;
        /// <summary>同ランク3枚セットのボーナス</summary>
        public const int SameRankTripleBonus = 50;

        private const string StraightName = "ストレート (D-C-B-A-S)";
        private const string SpecialComboName = "指定コンボ (S-A-A)";
        private const string TripleNameFormat = "同ランクセット ({0}×3)";

        private static readonly CardRank[] StraightRanks =
        {
            CardRank.D, CardRank.C, CardRank.B, CardRank.A, CardRank.S,
        };

        /// <summary>ランクの一覧からスコアを計算する。</summary>
        public static ScoreResult Evaluate(IEnumerable<CardRank> ranks)
        {
            var counts = new Dictionary<CardRank, int>();
            var baseScore = 0;
            foreach (var rank in ranks)
            {
                counts[rank] = counts.TryGetValue(rank, out var n) ? n + 1 : 1;
                baseScore += BasePoints.TryGetValue(rank, out var point) ? point : 0;
            }

            var combos = new List<AchievedCombo>();

            // 1. ストレート (D-C-B-A-S): 最高ボーナスのため最優先で消費する
            while (HasAll(counts, StraightRanks))
            {
                foreach (var rank in StraightRanks)
                {
                    counts[rank]--;
                }
                combos.Add(new AchievedCombo(StraightName, StraightBonus));
            }

            // 2. 指定コンボ (S-A-A)
            while (Count(counts, CardRank.S) >= 1 && Count(counts, CardRank.A) >= 2)
            {
                counts[CardRank.S]--;
                counts[CardRank.A] -= 2;
                combos.Add(new AchievedCombo(SpecialComboName, SpecialComboBonus));
            }

            // 3. 同ランク3枚セット（任意のランクで成立）
            foreach (var rank in StraightRanks)
            {
                while (Count(counts, rank) >= 3)
                {
                    counts[rank] -= 3;
                    combos.Add(new AchievedCombo(string.Format(TripleNameFormat, rank), SameRankTripleBonus));
                }
            }

            return new ScoreResult(baseScore, combos);
        }

        private static bool HasAll(Dictionary<CardRank, int> counts, CardRank[] required)
        {
            foreach (var rank in required)
            {
                if (Count(counts, rank) < 1)
                {
                    return false;
                }
            }
            return true;
        }

        private static int Count(Dictionary<CardRank, int> counts, CardRank rank)
        {
            return counts.TryGetValue(rank, out var n) ? n : 0;
        }
    }
}
