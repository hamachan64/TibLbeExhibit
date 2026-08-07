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

        /// <summary>カード単体得点の合計（ボーナスタイム1.5倍を含む）</summary>
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
    /// バインダー内カードの構成から「基本得点＋組み合わせボーナス」を計算する
    /// 純粋ロジック（MonoBehaviour 非依存・ユニットテスト可能）。
    ///
    /// 【基本得点】
    ///   各カードのランク得点の合計。ボーナスタイム中に収納したカードは 1.5 倍（小数切り捨て）。
    ///
    /// 【組み合わせボーナス】
    ///   - 全ランク獲得ボーナス   : D～S の全ランクが1枚以上あれば +5,000
    ///   - 全種類獲得ボーナス     : バインダー内の CardId が全カタログ種類を網羅すれば +10,000
    ///     ※ 全種類ボーナスはゲームセッション上で参照できる ItemCatalog を使う。
    ///        カタログが未設定の場合は判定をスキップする（ボーナスなし）。
    /// </summary>
    public static class PokerHandEvaluator
    {
        /// <summary>
        /// ランクごとの単体得点のフォールバック値。
        /// GIPokerAssets.RankConfig（CardRankConfig.BasePoints）が未設定のときに Evaluate から使われる。
        /// </summary>
        public static readonly IReadOnlyDictionary<CardRank, int> DefaultBasePoints = new Dictionary<CardRank, int>
        {
            { CardRank.D,  10 },
            { CardRank.C,  20 },
            { CardRank.B,  50 },
            { CardRank.A,  100 },
            { CardRank.S,  500 },
            { CardRank.SS, 10000 },
        };

        /// <summary>D～S 全ランク揃いボーナス</summary>
        public const int AllRankBonus = 5000;

        /// <summary>全種類（カタログ収録全カード）獲得ボーナス</summary>
        public const int AllCardTypeBonus = 10000;

        /// <summary>ボーナスタイム中に収納したカードのスコア倍率</summary>
        public const float BonusTimeMultiplier = 1.5f;

        /// <summary>全ランク揃いに必要なランク（D～S）</summary>
        private static readonly CardRank[] AllRanks =
        {
            CardRank.D, CardRank.C, CardRank.B, CardRank.A, CardRank.S,
        };

        private const string AllRankBonusName = "全ランク獲得ボーナス (D-C-B-A-S)";
        private const string AllCardTypeBonusName = "全種類獲得ボーナス";

        /// <summary>
        /// カードインスタンスの一覧からスコアを計算する。
        /// </summary>
        /// <param name="cards">評価対象のカードインスタンス一覧</param>
        /// <param name="basePoints">
        /// ランクごとの単体得点。未指定（null）の場合は DefaultBasePoints にフォールバックする
        /// （CardBinder からは GIPokerAssets.RankConfig?.BasePoints を渡す想定）。
        /// </param>
        /// <param name="allCardIds">
        /// 全種類ボーナス判定に使うカタログの全 CardId。
        /// null または空の場合は全種類ボーナスの判定をスキップする。
        /// </param>
        public static ScoreResult Evaluate(
            IEnumerable<CardInstance> cards,
            IReadOnlyDictionary<CardRank, int> basePoints = null,
            IReadOnlyCollection<string> allCardIds = null)
        {
            basePoints ??= DefaultBasePoints;

            // --- 基本得点（ボーナスタイム 1.5 倍込み） ---
            var baseScore = 0;
            var rankCounts = new Dictionary<CardRank, int>();
            var collectedCardIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var card in cards)
            {
                var rank = card.Rank;

                var rawPoint = basePoints.TryGetValue(rank, out var point) ? point : 0;
                // ボーナスタイム中に収納したカードは 1.5 倍（小数切り捨て）
                var finalPoint = card.IsBonus
                    ? (int)(rawPoint * BonusTimeMultiplier)
                    : rawPoint;
                baseScore += finalPoint;

                rankCounts[rank] = rankCounts.TryGetValue(rank, out var n) ? n + 1 : 1;

                if (!string.IsNullOrEmpty(card.Definition?.CardId))
                {
                    collectedCardIds.Add(card.Definition.CardId);
                }
            }

            // --- 組み合わせボーナス ---
            var combos = new List<AchievedCombo>();

            // 1. 全ランク獲得ボーナス（D～S が各1枚以上）
            var hasAllRanks = true;
            foreach (var rank in AllRanks)
            {
                if (!rankCounts.TryGetValue(rank, out var cnt) || cnt < 1)
                {
                    hasAllRanks = false;
                    break;
                }
            }
            if (hasAllRanks)
            {
                combos.Add(new AchievedCombo(AllRankBonusName, AllRankBonus));
            }

            // 2. 全種類獲得ボーナス（カタログに登録された全 CardId を網羅）
            if (allCardIds != null && allCardIds.Count > 0)
            {
                var hasAllTypes = true;
                foreach (var id in allCardIds)
                {
                    if (!collectedCardIds.Contains(id))
                    {
                        hasAllTypes = false;
                        break;
                    }
                }
                if (hasAllTypes)
                {
                    combos.Add(new AchievedCombo(AllCardTypeBonusName, AllCardTypeBonus));
                }
            }

            return new ScoreResult(baseScore, combos);
        }
    }
}
