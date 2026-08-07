using System;
using System.Collections.Generic;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// ランク単位（D/C/B/A/S/SS）の数値バランス設定（ScriptableObject、単一アセット）。
    ///
    /// 以前は下記のように複数のC#ファイルにハードコードされていたランク別の値を
    /// 1つのリストに集約する:
    /// - 単体得点: PokerHandEvaluator.BasePoints
    /// - 通常時／ボーナスタイムの追加出現抽選重み: ItemSpawnManager の normalRankWeights/bonusRankWeights
    /// - 宝籤（ロトリー）の抽選重み: CardEffectService.lotteryRankWeights
    /// - テーマカラー: CardVisualFactory.GetRankColor の switch 式
    ///
    /// GIPokerBootstrap がシーン上の SerializeField から GIPokerAssets.RankConfig へ流し込む。
    /// 未設定（null）でも各参照元は現行と同じ値へフォールバックする（GIPokerAssets の null許容方針を踏襲）。
    /// </summary>
    [CreateAssetMenu(fileName = "CardRankConfig", menuName = "GIPoker/Card Rank Config")]
    public class CardRankConfig : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("対象ランク")]
            public CardRank rank;

            [Tooltip("カード単体の得点（PokerHandEvaluator.BasePoints 相当）")]
            public int basePoints;

            [Tooltip("通常時（ボーナスタイム前）の追加出現抽選重み（ItemSpawnManager.normalRankWeights 相当）")]
            public float normalSpawnWeight = 1f;

            [Tooltip("ボーナスタイム中の追加出現抽選重み（ItemSpawnManager.bonusRankWeights 相当）")]
            public float bonusSpawnWeight = 1f;

            [Tooltip("宝籤（ロトリー）の抽選重み（CardEffectService.lotteryRankWeights 相当。SSは未使用）")]
            public float lotteryWeight;

            [Tooltip("ランクのテーマカラー（CardVisualFactory.GetRankColor 相当）")]
            public Color themeColor = Color.white;
        }

        [SerializeField]
        private List<Entry> entries = new();

        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>指定ランクのエントリを取得する。見つからなければ false。</summary>
        public bool TryGetEntry(CardRank rank, out Entry entry)
        {
            foreach (var e in entries)
            {
                if (e.rank == rank)
                {
                    entry = e;
                    return true;
                }
            }
            entry = null;
            return false;
        }

        /// <summary>
        /// PokerHandEvaluator 向けのランク別単体得点辞書を構築する。
        /// エントリが1件も無い場合は空の辞書を返す（呼び出し側の null チェックはこの型では行わない）。
        /// </summary>
        public IReadOnlyDictionary<CardRank, int> BasePoints
        {
            get
            {
                var dict = new Dictionary<CardRank, int>();
                foreach (var e in entries)
                {
                    dict[e.rank] = e.basePoints;
                }
                return dict;
            }
        }
    }
}
