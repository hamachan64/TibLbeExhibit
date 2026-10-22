namespace STYLY.TIB_Template
{
    public enum Rpc
    {
        /// <summary>コンテンツ開始（引数: [0]=スポーン用乱数シード）</summary>
        ContentStart,
        /// <summary>セッション終了（引数: [0]=最終スコア）。運営PCの本日通算トップ3記録に使う</summary>
        SessionFinished,

        // ===== 具現化ポーカー: カード状態同期（CardNetworkSync が送受信する） =====

        /// <summary>カード化（引数: [0]=itemId）</summary>
        GiCardify,
        /// <summary>ピンチで掴んだ（引数: [0]=itemId）</summary>
        GiGrab,
        /// <summary>離した/取り出した（引数: [0]=itemId, [1..3]=位置xyz）</summary>
        GiRelease,
        /// <summary>バインダーに収納した（引数: [0]=itemId）</summary>
        GiStore,
        /// <summary>ゲイン（具現化）した（引数: [0]=itemId, [1..3]=位置xyz）</summary>
        GiGain,
        /// <summary>錬金による定義変換（引数: [0]=itemId, [1]=変換先cardId）</summary>
        GiConvert,
        /// <summary>全体効果の発動（引数: [0]=CardEffectType数値）</summary>
        GiEffect,
        /// <summary>リスキーダイスの結果（引数: [0]=出目, [1]=大吉, [2]=大凶, [3..5]=位置xyz）</summary>
        GiDice,
        /// <summary>リスキーダイスが転がり始めた（引数: [0..2]=位置xyz）。転がっている最中のVFX演出を他クライアントにも同期する</summary>
        GiDiceRoll,
        /// <summary>SSカード出現（引数: [0]=itemId, [1..3]=位置xyz）</summary>
        GiSpawnSS,
        /// <summary>宝籤（ロトリー）による新カード出現（引数: [0]=newItemId, [1]=cardId, [2..4]=位置xyz）</summary>
        GiSpawnLottery,

        // ===== Treasure Haul（設計書 6.2）。引数は共通: [0]=RoundId（10進文字列。回が無いときは 0）、[1]=中身の JSON =====

        /// <summary>ホスト→全員 状態（通し番号・状態・残り秒・シード・参加端末・ゲーム設定・パラメータ版・練習の色）</summary>
        ThState,
        /// <summary>ホスト→全員 定期の生存通知（通し番号・残り秒・合計額・パラメータ版）</summary>
        ThHeartbeat,
        /// <summary>端末→ホスト 再送要求を兼ねた状況報告（DeviceId・知っている RoundId・通し番号・パラメータ版）</summary>
        ThResync,
        /// <summary>ホスト→宛先 パラメータ JSON と版</summary>
        ThParams,
        /// <summary>端末→ホスト つかむ申請</summary>
        ThGrab,
        /// <summary>端末→ホスト M の両手成立</summary>
        ThLift,
        /// <summary>端末→ホスト 置いた・落とした・取りやめ・予約解除</summary>
        ThRelease,
        /// <summary>ホスト→全員 つかめた</summary>
        ThGrabbed,
        /// <summary>ホスト→全員 つかめなかった</summary>
        ThGrabDenied,
        /// <summary>ホスト→全員 M を持ち上げた</summary>
        ThLifted,
        /// <summary>ホスト→全員 お宝が解放された</summary>
        ThFreed,
        /// <summary>判定役→全員 持たれている間の位置（端末ごとに 1 つにまとめる）</summary>
        ThPoses,
        /// <summary>判定役→ホスト 置かれた（静止した）</summary>
        ThRest,
        /// <summary>判定役→ホスト 衝突による減額の報告</summary>
        ThDamage,
        /// <summary>判定役→ホスト 荷台への回収の報告</summary>
        ThCollect,
        /// <summary>ホスト→全員 減額を適用した</summary>
        ThDamaged,
        /// <summary>ホスト→全員 回収した</summary>
        ThCollected,
        /// <summary>ホスト→全員 その場にそっと置かせる</summary>
        ThForceRest,
        /// <summary>ホスト→全員 全お宝の状態表</summary>
        ThItems,
        /// <summary>端末→ホスト 状況報告（手の認識状態・電池残量。〔状況報告の間隔〕ごと。#44）</summary>
        ThStatus,
    }
}
