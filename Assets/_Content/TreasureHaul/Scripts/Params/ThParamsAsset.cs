using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>組み込みの初期値（ThParams）を持つ ScriptableObject。差し替えは運営PCの JSON（ThParamsLoader）で行う。</summary>
    [CreateAssetMenu(fileName = "ThParamsDefault", menuName = "Treasure Haul/Params Asset")]
    public sealed class ThParamsAsset : ScriptableObject
    {
        public ThParams values = new ThParams();
    }
}
