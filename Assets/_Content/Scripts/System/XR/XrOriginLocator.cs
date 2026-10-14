using Unity.XR.CoreUtils;
using UnityEngine;

namespace STYLY.TIB_Template
{
    /// <summary>
    /// XR Origin をランタイムで検索・キャッシュするヘルパー。
    /// STYLY XR Rig は実行時にリグを生成するため、シーンには XR Origin が
    /// 事前に存在しない。ハンド関節ポーズ（トラッキング空間基準）を
    /// ワールド座標へ変換する用途で使う。
    /// </summary>
    public static class XrOriginLocator
    {
        private static XROrigin cachedOrigin;

        /// <summary>XR Origin の Transform を返す（未生成なら null）</summary>
        public static Transform Find()
        {
            if (cachedOrigin == null)
            {
                cachedOrigin = Object.FindAnyObjectByType<XROrigin>();
            }
            return cachedOrigin != null ? cachedOrigin.transform : null;
        }

        /// <summary>トラッキング空間のポーズをワールド座標へ変換する</summary>
        public static Pose ToWorldPose(Pose trackingSpacePose)
        {
            var origin = Find();
            if (origin == null)
            {
                return trackingSpacePose;
            }
            return trackingSpacePose.GetTransformedBy(new Pose(origin.position, origin.rotation));
        }
    }
}
