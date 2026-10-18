using System;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    // 設計書 6章。すべての Th* の引数は [0]=RoundId（10進文字列。回が無いときは 0）、[1]=中身の JSON。

    [Serializable]
    public class ThStateMsg
    {
        /// <summary>状態の通し番号</summary>
        public int seq;
        public int state;
        public float remaining;
        public int seed;
        /// <summary>参加端末（DeviceId の一覧）</summary>
        public string[] participants = new string[0];
        public ThGameSettings settings = new ThGameSettings();
        public string paramsVersion = "";
        /// <summary>ホストの起動ごとに変わる値（起動時の Unix ミリ秒）。運営PCの再起動を端末が見分けて、状況報告を送り直すため</summary>
        public long hostEpoch;
        /// <summary>練習の色の割り当て（この PR では空。#43 で入れる）。participants と同じ順で色番号</summary>
        public int[] tutorialColors = new int[0];
    }

    [Serializable]
    public class ThHeartbeatMsg
    {
        public int seq;
        public float remaining;
        /// <summary>合計額（この PR では常に 0）</summary>
        public int total;
        public string paramsVersion = "";
        /// <summary>ThState と同じ。変わったら運営PCが再起動した</summary>
        public long hostEpoch;
    }

    /// <summary>端末→ホスト。端末の状況報告を兼ねる。RoundId 引数には「知っている RoundId」を入れる。</summary>
    [Serializable]
    public class ThResyncMsg
    {
        public string deviceId = "";
        public int seq;
        public string paramsVersion = "";
        /// <summary>回の途中から入ったので、その回は参加しない</summary>
        public bool waitNextRound;
    }

    [Serializable]
    public class ThParamsMsg
    {
        /// <summary>宛先の ClientNo（-1 = 全員）</summary>
        public int targetClientNo = -1;
        public string version = "";
        public string json = "";
    }

    /// <summary>引数の符号化・復号（純粋）。壊れた・空の引数は例外を出さずに false を返す。</summary>
    public static class ThMessages
    {
        public static string[] Encode<T>(long roundId, T dto) where T : class
        {
            return new[] { roundId.ToString(System.Globalization.CultureInfo.InvariantCulture), JsonUtility.ToJson(dto) };
        }

        public static bool TryDecode<T>(string[] args, out long roundId, out T dto) where T : class
        {
            roundId = 0;
            dto = null;
            // 65,535 バイトを超えると NetSync は args の中身を空にする（null や空配列になる）
            if (args == null || args.Length < 2) return false;
            if (!long.TryParse(args[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out roundId) || roundId < 0)
            {
                roundId = 0;
                return false;
            }
            if (string.IsNullOrWhiteSpace(args[1])) return false;
            try
            {
                dto = JsonUtility.FromJson<T>(args[1]);
            }
            catch (Exception)
            {
                dto = null;
            }
            return dto != null;
        }
    }
}
