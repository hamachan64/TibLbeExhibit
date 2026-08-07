using System;
using System.Collections.Generic;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// セッション中に出現するアイテムのラインナップ定義（ScriptableObject）。
    /// 企画書の「カード化限度枚数（出現上限）」をエントリごとの maxCount で表現する。
    /// </summary>
    [CreateAssetMenu(fileName = "ItemCatalog", menuName = "GIPoker/Item Catalog")]
    public class ItemCatalog : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            [Tooltip("このエントリのカード定義（プレハブ・初期出現数・出現上限は CardDefinition 側で保持する）")]
            public CardDefinition definition;

            /// <summary>アイテムのプレハブ（CardizableItemBase 付き）</summary>
            public GameObject Prefab => definition != null ? definition.Prefab : null;

            /// <summary>セッション開始時に出現する数</summary>
            public int InitialCount => definition != null ? definition.InitialCount : 0;

            /// <summary>セッション全体での出現上限（カード化限度枚数）</summary>
            public int MaxCount => definition != null ? definition.MaxCount : 0;
        }

        [SerializeField]
        private List<Entry> entries = new();

        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>
        /// cardId からエントリのプレハブを逆引きする（宝籤の候補選定・
        /// リモートRPC受信でのアイテム解決に使う）。見つからなければ null。
        /// </summary>
        public GameObject FindPrefabByCardId(string cardId)
        {
            if (string.IsNullOrEmpty(cardId))
            {
                return null;
            }
            foreach (var entry in entries)
            {
                if (entry.definition != null && entry.definition.CardId == cardId)
                {
                    return entry.definition.Prefab;
                }
            }
            return null;
        }

        /// <summary>
        /// ランクからエントリの定義を逆引きする（SS等、ランクごとに1種類しかないカードの解決に使う）。
        /// 同ランクが複数ある場合は最初に見つかったものを返す。見つからなければ null。
        /// </summary>
        public CardDefinition FindDefinitionByRank(CardRank rank)
        {
            foreach (var entry in entries)
            {
                if (entry.definition != null && entry.definition.Rank == rank)
                {
                    return entry.definition;
                }
            }
            return null;
        }
    }
}
