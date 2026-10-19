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
        /// <summary>お宝カタログのハッシュ（ホストの物。端末の物と違えば参加端末から外す）</summary>
        public string catalogHash = "";
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
        /// <summary>この端末のお宝カタログのハッシュ</summary>
        public string catalogHash = "";
    }

    [Serializable]
    public class ThParamsMsg
    {
        /// <summary>宛先の ClientNo（-1 = 全員）</summary>
        public int targetClientNo = -1;
        public string version = "";
        public string json = "";
    }

    // ---- お宝（#38。設計書 4.4・5章・6章）。item はお宝の番号、gen は世代番号、reqId は申請ID ----

    /// <summary>端末→ホスト つかむ申請（kind: 0=S）</summary>
    [Serializable]
    public class ThGrabMsg
    {
        public int item;
        public int reqId;
        /// <summary>申請した端末が知っている世代</summary>
        public int gen;
        public int kind;
    }

    /// <summary>ホスト→全員 つかめた</summary>
    [Serializable]
    public class ThGrabbedMsg
    {
        public int item;
        public int clientNo;
        public int reqId;
        public int gen;
        /// <summary>現在の価値</summary>
        public int value;
    }

    /// <summary>ホスト→全員 つかめなかった</summary>
    [Serializable]
    public class ThGrabDeniedMsg
    {
        public int item;
        public int clientNo;
        public int reqId;
    }

    /// <summary>端末→ホスト 取りやめ・落とした（reason は ReleaseReason）</summary>
    [Serializable]
    public class ThReleaseMsg
    {
        public int item;
        public int reqId;
        public int gen;
        public int reason;
    }

    /// <summary>ホスト→全員 つかめるに戻った（位置は VenueRoot ローカル）</summary>
    [Serializable]
    public class ThFreedMsg
    {
        public int item;
        public int gen;
        public Vector3 pos;
        public Quaternion rot = Quaternion.identity;
    }

    /// <summary>判定役→ホスト 置かれた（静止した）</summary>
    [Serializable]
    public class ThRestMsg
    {
        public int item;
        public int gen;
        public Vector3 pos;
        public Quaternion rot = Quaternion.identity;
    }

    /// <summary>判定役→ホスト 荷台への回収</summary>
    [Serializable]
    public class ThCollectMsg
    {
        public int item;
        public int gen;
    }

    /// <summary>ホスト→全員 回収した</summary>
    [Serializable]
    public class ThCollectedMsg
    {
        public int item;
        public int gen;
        /// <summary>回収後の合計額</summary>
        public int total;
        /// <summary>回収した価値</summary>
        public int value;
        /// <summary>傷物か（元の値段より下がっていた）</summary>
        public bool damaged;
    }

    [Serializable]
    public class ThPoseEntry
    {
        public int item;
        public Vector3 pos;
        public Quaternion rot = Quaternion.identity;
        /// <summary>減額の免除中か（#39。判定役が載せる。古い端末からは false で届く）。設計書 5章</summary>
        public bool exempt;
    }

    /// <summary>判定役→全員 動かしているお宝の位置（VenueRoot ローカル）。端末ごとに 1 つにまとめる</summary>
    [Serializable]
    public class ThPosesMsg
    {
        public ThPoseEntry[] poses = new ThPoseEntry[0];
    }

    /// <summary>判定役→ホスト ぶつけた（#39。送り直さない。設計書 5章・6.2）。位置は VenueRoot ローカル</summary>
    [Serializable]
    public class ThDamageMsg
    {
        /// <summary>自分のお宝</summary>
        public int a;
        /// <summary>相手のお宝（障害物なら -1）</summary>
        public int b = -1;
        /// <summary>当たった点</summary>
        public Vector3 pos;
        /// <summary>衝撃の速さ（ログ用）</summary>
        public float speed;
    }

    /// <summary>ホスト→全員 減額した</summary>
    [Serializable]
    public class ThDamagedMsg
    {
        public int item;
        public int gen;
        /// <summary>減額後のぶつけた回数</summary>
        public int hits;
        /// <summary>減額の量</summary>
        public int amount;
        /// <summary>減額後の価値</summary>
        public int value;
        public Vector3 pos;
        /// <summary>3 回目で砕けて消えた</summary>
        public bool destroyed;
        /// <summary>記録用: そのときの持ち主の ClientNo、いなければ判定役の ClientNo、どちらもいなければ -1</summary>
        public int by = -1;
        /// <summary>値段公開済みか（false なら「-NNG」を出さない）</summary>
        public bool revealed;
    }

    [Serializable]
    public class ThItemEntry
    {
        public int item;
        public int state;
        /// <summary>持ち主の ClientNo（いなければ -1）</summary>
        public int owner = -1;
        /// <summary>判定役の ClientNo（いなければ -1）</summary>
        public int ctl = -1;
        /// <summary>持ち主の承認済み申請ID</summary>
        public int reqId;
        public int gen;
        public int value;
        public int hits;
        public bool revealed;
        public bool gone;
        public Vector3 pos;
        public Quaternion rot = Quaternion.identity;
    }

    /// <summary>ホスト→全員 全お宝の状態表</summary>
    [Serializable]
    public class ThItemsMsg
    {
        public ThItemEntry[] items = new ThItemEntry[0];
    }

    /// <summary>引数の符号化・復号（純粋）。壊れた・空の引数は例外を出さずに false を返す。</summary>
    public static class ThMessages
    {
        public static string[] Encode<T>(long roundId, T dto) where T : class
        {
            return new[] { roundId.ToString(System.Globalization.CultureInfo.InvariantCulture), JsonUtility.ToJson(dto) };
        }

        /// <summary>型が実行時に決まる DTO 用（ホストの状態表が送る物）</summary>
        public static string[] EncodeObject(long roundId, object dto)
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
