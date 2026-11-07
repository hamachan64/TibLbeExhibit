using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 遺跡の亡霊の素材（ThContentSet から参照）。見た目のプレハブと、経路の点（VenueRoot ローカルの XZ。閉じた折れ線）。
    /// Environment シーンは触らないので、経路の点はここに持つ。
    /// </summary>
    [CreateAssetMenu(fileName = "ThPhantomAssets", menuName = "Treasure Haul/Phantom Assets")]
    public sealed class ThPhantomAssets : ScriptableObject
    {
        [Tooltip("亡霊の見た目（PhantomView 付き。実行時に VenueRoot の子として作る）")] public GameObject prefab;
        [Tooltip("経路の点（VenueRoot ローカルの XZ。x = 幅、y = 奥行。通路の中央を通る閉じた折れ線）")]
        public Vector2[] routePoints = (Vector2[])PhantomRoute.DefaultPoints.Clone();

        public PhantomRoute BuildRoute() => new PhantomRoute(routePoints);
    }
}
