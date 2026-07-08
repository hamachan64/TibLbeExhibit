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
            [Tooltip("アイテムのプレハブ（CardizableItemBase 付き）")]
            public GameObject prefab;

            [Tooltip("セッション開始時に出現する数")]
            public int initialCount = 1;

            [Tooltip("セッション全体での出現上限（カード化限度枚数）")]
            public int maxCount = 1;
        }

        [SerializeField]
        private List<Entry> entries = new();

        public IReadOnlyList<Entry> Entries => entries;
    }
}
