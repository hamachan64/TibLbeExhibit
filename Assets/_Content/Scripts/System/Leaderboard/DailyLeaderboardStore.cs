using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace STYLY.TIB_Template.Leaderboard
{
    /// <summary>
    /// DailyLeaderboard の JSON（JsonUtility）Load/Save を担当する。
    ///
    /// 保存先パスはコンストラクタで注入する（テスト時は一時パス、実運用時は
    /// Application.persistentDataPath 配下を LeaderboardTracker が組み立てて渡す）。
    ///
    /// JsonUtility は readonly フィールドを持つ型（LeaderboardEntry は readonly struct）を
    /// 正しく復元できないため、保存用に可変フィールドのDTOを内部で用いて変換する。
    /// </summary>
    public sealed class DailyLeaderboardStore
    {
        private const string DateFormat = "yyyy-MM-dd";

        private readonly string filePath;

        public DailyLeaderboardStore(string filePath)
        {
            this.filePath = filePath;
        }

        [Serializable]
        private class EntryDto
        {
            public int ClientNo;
            public int Score;
        }

        [Serializable]
        private class StateDto
        {
            public string RecordedDate;
            public List<EntryDto> Top3 = new();
        }

        /// <summary>
        /// 現在の状態を保存する。保存先ディレクトリが無ければ作成する。
        /// ディスク書き込み失敗（権限・容量不足等）は例外を外へ伝播させず、ログを出して握りつぶす。
        /// 呼び出し元は RPC 受信ハンドラ内から呼ばれるため、ここで例外が漏れると
        /// 以後その購読ごと停止し「本日通算トップ3」の記録が黙って効かなくなってしまう。
        /// </summary>
        public void Save(DailyLeaderboard leaderboard)
        {
            if (leaderboard == null)
            {
                return;
            }

            try
            {
                var dto = new StateDto
                {
                    RecordedDate = leaderboard.RecordedDate.ToString(DateFormat, CultureInfo.InvariantCulture),
                    Top3 = leaderboard.Top3.Select(e => new EntryDto { ClientNo = e.ClientNo, Score = e.Score }).ToList(),
                };

                var json = JsonUtility.ToJson(dto);
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(filePath, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DailyLeaderboardStore] 保存に失敗しました: {e.Message}");
            }
        }

        /// <summary>
        /// 保存済みの状態を読み込む。ファイルが無い/壊れている場合は
        /// fallbackDate を記録日とする空のリーダーボードを返す。
        /// </summary>
        public DailyLeaderboard Load(DateTime fallbackDate)
        {
            if (!File.Exists(filePath))
            {
                return new DailyLeaderboard(fallbackDate);
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var dto = JsonUtility.FromJson<StateDto>(json);
                if (dto == null
                    || string.IsNullOrEmpty(dto.RecordedDate)
                    || !DateTime.TryParseExact(dto.RecordedDate, DateFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var recordedDate))
                {
                    return new DailyLeaderboard(fallbackDate);
                }

                var leaderboard = new DailyLeaderboard(recordedDate);
                var entries = (dto.Top3 ?? new List<EntryDto>())
                    .Select(e => new LeaderboardEntry(e.ClientNo, e.Score));
                leaderboard.RestoreState(recordedDate, entries);
                return leaderboard;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DailyLeaderboardStore] 読み込みに失敗したため空の状態から開始します: {e.Message}");
                return new DailyLeaderboard(fallbackDate);
            }
        }
    }
}
