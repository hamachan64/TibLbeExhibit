namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// Environment シーンに置かれている物の数（パラメータの組み合わせ検査用）。
    /// null（または各値が null）＝まだ決まっていない（#38・#43 で Environment に置く）ので、その検査は飛ばす。
    /// </summary>
    public sealed class ThEnvironmentInfo
    {
        /// <summary>配置候補点の数</summary>
        public int? PlacementPointCount;
        /// <summary>TutorialSlot の数</summary>
        public int? TutorialSlotCount;
        /// <summary>M を置ける配置候補点の数</summary>
        public int? MPointCount;
        /// <summary>S を置ける配置候補点の数</summary>
        public int? SPointCount;
    }
}
