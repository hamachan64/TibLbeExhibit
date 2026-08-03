using System;
using System.Collections.Generic;
using System.Linq;

namespace STYLY.TIB_Template.Leaderboard
{
    /// <summary>
    /// 「本日通算のトップ3」を保持するUnityEngine非依存の純粋C#クラス。
    ///
    /// - Record() でスコアを1件登録すると、上位3件（同点は既存順=先着優先を維持）だけが残る。
    /// - NeedsReset(now) で日付ロールオーバー（記録日と異なる日になったか）を判定する。
    ///   判定に使う「現在時刻」は呼び出し側が注入するため、テスト容易性が高い。
    /// - 永続化（JSON Load/Save）は関知しない。それは DailyLeaderboardStore の責務。
    /// </summary>
    public sealed class DailyLeaderboard
    {
        private const int MaxEntries = 3;

        private readonly List<LeaderboardEntry> top3 = new();

        public DailyLeaderboard(DateTime recordedDate)
        {
            RecordedDate = recordedDate.Date;
        }

        /// <summary>この記録が対象とする日付（時刻部分は無視される）</summary>
        public DateTime RecordedDate { get; private set; }

        /// <summary>現在保持しているトップ3（降順、同点は先着優先）</summary>
        public IReadOnlyList<LeaderboardEntry> Top3 => top3;

        /// <summary>
        /// スコアを1件登録する。トップ3を超えた分（同点含む）は落ちる。
        /// 同点の場合は既に保持している（＝先に記録された）エントリを優先し、
        /// 後から追加された同点エントリが押し出される。
        /// </summary>
        public void Record(int clientNo, int score)
        {
            top3.Add(new LeaderboardEntry(clientNo, score));

            // OrderByDescending は安定ソート（同点は入力順=既存順を維持）
            var sorted = top3.OrderByDescending(e => e.Score).Take(MaxEntries).ToList();
            top3.Clear();
            top3.AddRange(sorted);
        }

        /// <summary>
        /// 指定した日時が記録日と異なる日ならリセットが必要。
        /// </summary>
        public bool NeedsReset(DateTime now)
        {
            return now.Date != RecordedDate.Date;
        }

        /// <summary>トップ3をクリアし、記録日を更新する。</summary>
        public void Reset(DateTime newRecordedDate)
        {
            top3.Clear();
            RecordedDate = newRecordedDate.Date;
        }

        /// <summary>
        /// 保存済みの状態を復元する（DailyLeaderboardStore のロード処理専用）。
        /// </summary>
        public void RestoreState(DateTime recordedDate, IEnumerable<LeaderboardEntry> entries)
        {
            RecordedDate = recordedDate.Date;
            top3.Clear();
            if (entries != null)
            {
                top3.AddRange(entries.OrderByDescending(e => e.Score).Take(MaxEntries));
            }
        }
    }
}
