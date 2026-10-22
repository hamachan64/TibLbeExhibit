using System;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    // 要件定義書 P章・設計書 P章のパラメータ。グループ（P-1〜P-6）ごとに 1 クラス。
    // 値の置き場所はここ（と ThParamsDefault.asset）だけ。コードやシーンに同じ値を重ねて書かない（N-7）。
    // 金額は 100G 刻み。「【仮】」「【要検証】」は実機テストで調整する値。

    /// <summary>P-1 進行</summary>
    [Serializable]
    public class ThProgressParams
    {
        [Range(1f, 120f)] public float openingSeconds = 10f;
        [Range(1f, 600f)] public float practiceSeconds = 30f;
        [Range(0f, 600f)] public float holdMaxSeconds = 30f;
        [Range(5f, 1800f)] public float playingSeconds = 180f;
        [Range(1f, 300f)] public float resultSeconds = 15f;
        /// <summary>【仮】運営PC切断の待ち</summary>
        [Range(1f, 120f)] public float hostLostSeconds = 10f;
        [Range(0.1f, 10f)] public float heartbeatInterval = 1f;
        /// <summary>時間を合わせるずれ</summary>
        [Range(0.01f, 5f)] public float syncTolerance = 0.25f;
        /// <summary>【仮】状態表（ThItems）の送信間隔</summary>
        [Range(0.1f, 10f)] public float itemsInterval = 1f;
        /// <summary>【仮】再送間隔（ThResync・ThRest・ThCollect）</summary>
        [Range(0.1f, 10f)] public float resendInterval = 0.5f;
        /// <summary>RPC の上限（回/秒。ConfigureRpcLimit）</summary>
        [Range(10, 300)] public int rpcLimit = 60;
        /// <summary>【仮】状況報告（ThStatus。手の認識・電池）の送信間隔（秒）。この 3 倍を過ぎて届かない値は運営UIで「不明」</summary>
        [Range(0.5f, 30f)] public float statusInterval = 2f;
    }

    /// <summary>P-2 練習</summary>
    [Serializable]
    public class ThPracticeParams
    {
        [Range(1f, 120f)] public float hintSeconds = 10f;
        [Range(100, 1000)] public int practiceValue = 150;
    }

    /// <summary>P-3 持ち方</summary>
    [Serializable]
    public class ThGripParams
    {
        [Range(0.3f, 5f)] public float beamDistance = 1.5f;
        [Range(0.05f, 2f)] public float flyTime = 0.3f;
        /// <summary>【仮】握りの「つかむ」しきい値（#36 の観測: 開いた手 0.03〜0.19・握った手 0.98〜1.00。#49 で調整）</summary>
        [Range(0f, 1f)] public float gripThreshold = 0.7f;
        /// <summary>【仮】握りの「離す」しきい値</summary>
        [Range(0f, 1f)] public float releaseThreshold = 0.4f;
        /// <summary>【仮】離す判定の継続時間</summary>
        [Range(0f, 2f)] public float releaseHoldSeconds = 0.25f;
        [Range(0f, 3f)] public float mGraceSeconds = 0.5f;
        /// <summary>見失い待ち</summary>
        [Range(0f, 10f)] public float lostWaitSeconds = 1.5f;
        /// <summary>離れたら置く距離（m）</summary>
        [Range(0.05f, 3f)] public float detachDistance = 0.5f;
        /// <summary>【仮】狙いの保持</summary>
        [Range(0f, 2f)] public float aimHoldSeconds = 0.3f;
        /// <summary>【要検証】肩のずらし量（下）</summary>
        [Range(0f, 1f)] public float shoulderDown = 0.15f;
        /// <summary>【要検証】肩のずらし量（左右）</summary>
        [Range(0f, 1f)] public float shoulderSide = 0.18f;
        /// <summary>【要検証】吸い付き半径（m）</summary>
        [Range(0f, 0.5f)] public float snapRadius = 0.05f;
        /// <summary>【要検証】追従速度の上限（m/s）</summary>
        [Range(1f, 50f)] public float followSpeedMax = 10f;
        /// <summary>見失いの猶予</summary>
        [Range(0f, 2f)] public float trackingLossGrace = 0.1f;
        /// <summary>置く時間</summary>
        [Range(0.05f, 2f)] public float placeSeconds = 0.3f;
        /// <summary>承認の待ち上限</summary>
        [Range(0.1f, 10f)] public float approvalWaitSeconds = 1f;
        /// <summary>位置の送信間隔（ThPoses。1/30 秒）</summary>
        [Range(0.01f, 1f)] public float poseSendInterval = 1f / 30f;
        /// <summary>【仮】補間の遅れ</summary>
        [Range(0f, 1f)] public float interpDelay = 0.05f;
    }

    /// <summary>P-4 お宝・減額（金額は 100G 刻み）</summary>
    [Serializable]
    public class ThTreasureParams
    {
        [Range(100, 5000)] public int sValueMin = 100;
        [Range(100, 5000)] public int sValueMax = 300;
        [Range(100, 5000)] public int mValueMin = 400;
        [Range(100, 5000)] public int mValueMax = 800;
        /// <summary>L*</summary>
        [Range(100, 10000)] public int lValueMin = 1500;
        [Range(100, 10000)] public int lValueMax = 2500;
        /// <summary>秘宝*</summary>
        [Range(100, 10000)] public int secretValue = 3000;
        /// <summary>発見演出の価値</summary>
        [Range(100, 10000)] public int discoverValue = 600;
        /// <summary>【仮】数える衝撃（m/s）</summary>
        [Range(0f, 10f)] public float impactSpeed = 0.8f;
        /// <summary>減額率 1回目（%）</summary>
        [Range(0, 100)] public int damagePercent1 = 30;
        [Range(0, 100)] public int damagePercent2 = 30;
        [Range(0, 100)] public int damagePercent3 = 40;
        /// <summary>再減額の間隔</summary>
        [Range(0f, 5f)] public float redamageInterval = 0.5f;
        /// <summary>補正の無判定時間</summary>
        [Range(0f, 5f)] public float correctionGraceSeconds = 0.5f;
        /// <summary>【仮】飛びの判定（m）</summary>
        [Range(0.01f, 3f)] public float jumpDistance = 0.3f;
        /// <summary>速さをならすフレーム数</summary>
        [Range(1, 30)] public int speedSmoothFrames = 3;
        /// <summary>止まったとみなす時間</summary>
        [Range(0f, 2f)] public float stopSeconds = 0.2f;
        /// <summary>【仮】重複とみなす時間</summary>
        [Range(0f, 2f)] public float dedupSeconds = 0.3f;
    }

    /// <summary>P-5 の人数別の 1 行</summary>
    [Serializable]
    public class ThPlayerCountRow
    {
        [Range(0, 100)] public int spawnS;
        [Range(0, 100)] public int spawnM;
        [Range(0, 100000)] public int targetAmount;

        public ThPlayerCountRow() { }
        public ThPlayerCountRow(int s, int m, int target) { spawnS = s; spawnM = m; targetAmount = target; }
    }

    /// <summary>P-5 配置・バランス</summary>
    [Serializable]
    public class ThLayoutParams
    {
        /// <summary>【仮】縁の余白（m）</summary>
        [Range(0f, 2f)] public float edgeMargin = 0.3f;
        /// <summary>L 1個あたりの目標額の加算*</summary>
        [Range(0, 10000)] public int lTargetBonus = 1000;
        /// <summary>参加人数 1〜4 の 4 行（添字 0 が 1 人）</summary>
        public ThPlayerCountRow[] rows =
        {
            new ThPlayerCountRow(8, 4, 2000),
            new ThPlayerCountRow(10, 5, 2500),
            new ThPlayerCountRow(12, 7, 3300),
            new ThPlayerCountRow(14, 9, 4100),
        };
    }

    /// <summary>P-6 運営</summary>
    [Serializable]
    public class ThOperationParams
    {
        /// <summary>【仮】電池の警告（%）</summary>
        [Range(0, 100)] public int batteryWarningPercent = 20;
        /// <summary>「中断」の確認の待ち（秒）。1回目を押してから、この時間内にもう一度押すと中断</summary>
        [Range(1f, 30f)] public float abortConfirmSeconds = 3f;
        /// <summary>
        /// true なら、新しく登録された端末・再接続した端末を「位置合わせ確認済み」で始める（エディタの ParrelSync 検証用）。
        /// false（初期値）なら「未確認」で始まり、運営UIで確認するまで参加できない。
        /// </summary>
        public bool autoConfirmAlignment = false;
    }

    /// <summary>全パラメータ。JSON のキーはフィールド名（グループ単位で書ける）。</summary>
    [Serializable]
    public class ThParams
    {
        public ThProgressParams progress = new ThProgressParams();
        public ThPracticeParams practice = new ThPracticeParams();
        public ThGripParams grip = new ThGripParams();
        public ThTreasureParams treasure = new ThTreasureParams();
        public ThLayoutParams layout = new ThLayoutParams();
        public ThOperationParams operation = new ThOperationParams();

        public ThParams Clone() => JsonUtility.FromJson<ThParams>(JsonUtility.ToJson(this));
    }
}
