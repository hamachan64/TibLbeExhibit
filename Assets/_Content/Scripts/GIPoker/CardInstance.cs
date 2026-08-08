using System;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// カード化された個々のカードのランタイムインスタンス。
    /// バインダーのインベントリにはこのインスタンスが格納される。
    /// </summary>
    public sealed class CardInstance
    {
        /// <summary>インスタンスごとの一意なID</summary>
        public Guid InstanceId { get; } = Guid.NewGuid();

        /// <summary>カードの静的定義（ランク・効果など）。錬金効果で差し替わることがある</summary>
        public CardDefinition Definition { get; private set; }

        /// <summary>カード化元のシーン上オブジェクト（ゲイン時の具現化に使用）</summary>
        public CardizableItemBase Source { get; }

        /// <summary>
        /// ボーナスタイム（残り30秒以降）中に収納されたカードかどうか。
        /// true のとき、スコア計算で得点が 1.5 倍になる。
        /// </summary>
        public bool IsBonus { get; private set; }

        public CardRank Rank => Definition.Rank;

        public CardInstance(CardDefinition definition, CardizableItemBase source)
        {
            Definition = definition;
            Source = source;
        }

        /// <summary>錬金効果（剣・錬金キット）による定義の差し替え</summary>
        internal void ReplaceDefinition(CardDefinition newDefinition)
        {
            Definition = newDefinition;
        }

        /// <summary>ボーナスタイム中に収納されたことをマークする（CardBinder から呼ぶ）</summary>
        internal void MarkAsBonus()
        {
            IsBonus = true;
        }
    }
}
