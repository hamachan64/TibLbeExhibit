using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// 練習アイテム用の「素手で触れてカード化」判定。
    ///
    /// ランタイム生成の練習アイテムは CardizableItemBase の handLayerMask
    /// （シリアライズ値）を設定できないため、FingertipTouchCollider と同じ
    /// レイヤー規約（HandBone、未定義なら Default）をこのコンポーネントが
    /// 代行して判定し、TryCardify を呼ぶ。
    /// </summary>
    public class TutorialTouchTrigger : MonoBehaviour
    {
        private CardizableItemBase item;
        private int handLayer;

        /// <summary>ランタイム生成（AddComponent）直後に呼んで対象アイテムを結線する</summary>
        public void Initialize(CardizableItemBase target)
        {
            item = target;
            var layer = LayerMask.NameToLayer(FingertipTouchCollider.HandBoneLayerName);
            // FingertipTouchCollider のフォールバック（Default レイヤー）と揃える
            handLayer = layer >= 0 ? layer : 0;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (item == null || other.gameObject.layer != handLayer)
            {
                return;
            }
            item.TryCardify();
        }
    }
}
