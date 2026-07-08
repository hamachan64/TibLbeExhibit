namespace STYLY.TIB_Template
{
    public enum UserVariable
    {
        /// <summary>
        /// デバイス固有ID
        /// </summary>
        DeviceId,
        
        /// <summary>
        /// ja: 日本語
        /// en: 英語
        /// </summary>
        Language,
        
        /// <summary>
        /// ハンドトラッキングロスト中は、手のモデルを非表示にする
        /// </summary>
        HandVisibility,
        
        /// <summary>
        /// バッテリーレベル
        /// STYLY-NetSyncが内部的に送っている
        /// </summary>
        BatteryLevel,

        /// <summary>
        /// 音量レベル
        /// 実機で挙動を確認したところ最大値は15
        /// </summary>
        AudioVolume,
        
        /// <summary>
        /// 登録が必要かどうか
        /// 1: 未登録のため登録が必要
        /// 0: 登録済み
        /// </summary>
        NeedRegistration,

        /// <summary>
        /// 具現化ポーカーの現在スコア（int）
        /// バインダーの内容が変わるたびに CardNetworkSync が更新する
        /// </summary>
        GiScore,
    }
}