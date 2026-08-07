using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// カードの静的定義データ（ScriptableObject）。
    /// 「花」「剣」「リスキーダイス」など、カード1種類につき1アセットを作成する。
    /// Project ウィンドウの Create > GIPoker > Card Definition から生成できる。
    /// </summary>
    [CreateAssetMenu(fileName = "CardDefinition", menuName = "GIPoker/Card Definition")]
    public class CardDefinition : ScriptableObject
    {
        [SerializeField, Tooltip("カードの一意なID（例: d_flower, a_risky_dice）")]
        private string cardId;

        [SerializeField, Tooltip("表示名（例: 花、リスキーダイス）")]
        private string displayName;

        [SerializeField, Tooltip("カードランク（レア度）")]
        private CardRank rank = CardRank.D;

        [SerializeField, Tooltip("ゲイン時に発動する効果")]
        private CardEffectType effectType = CardEffectType.None;

        [SerializeField, TextArea, Tooltip("カードの説明文（バインダーUI表示用）")]
        private string description;

        [Header("出現設定")]
        [SerializeField, Tooltip("アイテムのプレハブ（CardizableItemBase 付き）")]
        private GameObject prefab;

        [SerializeField, Tooltip("セッション開始時に出現する数")]
        private int initialCount = 1;

        [SerializeField, Tooltip("セッション全体での出現上限（カード化限度枚数）")]
        private int maxCount = 1;

        public string CardId => cardId;
        public string DisplayName => displayName;
        public CardRank Rank => rank;
        public CardEffectType EffectType => effectType;
        public string Description => description;
        public GameObject Prefab => prefab;
        public int InitialCount => initialCount;
        public int MaxCount => maxCount;
    }
}
