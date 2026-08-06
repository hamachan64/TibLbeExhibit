namespace STYLY.TIB_Template
{
    public enum Rpc
    {
        /// <summary>コンテンツ開始（引数: [0]=スポーン用乱数シード）</summary>
        ContentStart,

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
        /// <summary>セッション終了（引数: [0]=最終TotalScore）。運営PCの本日通算トップ3記録に使う</summary>
        GiSessionFinished,
        /// <summary>宝籤（ロトリー）による新カード出現（引数: [0]=newItemId, [1]=cardId, [2..4]=位置xyz）</summary>
        GiSpawnLottery,
    }
}
