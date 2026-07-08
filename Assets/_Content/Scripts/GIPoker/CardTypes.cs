namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// カードのランク（レア度）。G.I.のカードランクに相当する。
    /// </summary>
    public enum CardRank
    {
        D,
        C,
        B,
        A,
        S,
        SS,
    }

    /// <summary>
    /// カードの効果種別。ゲイン（具現化）時に発動する効果を表す。
    /// 効果の実処理は将来の効果システム側で実装する。
    /// </summary>
    public enum CardEffectType
    {
        /// <summary>効果なし（花・酒瓶・ブループラネット等）</summary>
        None,
        /// <summary>剣: 触れた別オブジェクトをD→Cにランダム変換（通常錬金）</summary>
        AlchemySword,
        /// <summary>錬金キット: 手持ちのDランクカード1枚をCランクに変換</summary>
        AlchemyKit,
        /// <summary>占い水晶: 一時的にカードの浮遊速度を遅くして掴みやすくする</summary>
        SlowFloating,
        /// <summary>彷徨うよろい: 1分ルールのカウントダウンを一時停止する</summary>
        PauseCountdown,
        /// <summary>リスキーダイス: 大吉でSSイベント、大凶でペナルティ</summary>
        RiskyDice,
        /// <summary>ギャラリーフェイク: バインダー内のカード1枚を複製する</summary>
        DuplicateCard,
    }

    /// <summary>
    /// カード化可能アイテムの状態。
    /// </summary>
    public enum CardizableState
    {
        /// <summary>現実空間のアイテム（未カード化）</summary>
        Item,
        /// <summary>カード化済み・浮遊中（1分ルールのカウントダウン進行中）</summary>
        FloatingCard,
        /// <summary>ピンチジェスチャーで掴まれている（カウントダウンは進行し続ける）</summary>
        Held,
        /// <summary>バインダーに収納済み（カウントダウン停止）</summary>
        Stored,
        /// <summary>ゲイン済み（アイテムに戻った状態。二度と再カード化できない）</summary>
        Gained,
        /// <summary>1分ルールにより破壊（消滅）された</summary>
        Expired,
    }
}
