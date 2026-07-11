using System.Collections.Generic;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// StageSpawnAnchor を「Transform ルート順→siblingIndex 深さ優先」の決定論的な順序で列挙するユーティリティ。
    ///
    /// GameObject.Find 系や FindObjectsByType のインスタンスID順など、クライアント間で一致する保証のない
    /// 順序に依存すると、全クライアントで同じアンカーが同じアイテムに割り当てられる前提が崩れる。
    /// そのため本クラスはシーンルートからの手動深さ優先探索のみを用いる。
    /// </summary>
    public static class StageSpawnAnchorEnumerator
    {
        /// <summary>
        /// 指定したルート群を起点に、階層順（roots の並び順→各ノードの子は siblingIndex 順）で
        /// 深さ優先探索し、見つかった StageSpawnAnchor を列挙順に返す。
        /// </summary>
        /// <param name="roots">探索を開始するルート GameObject 群（呼び出し側が順序を保証する）</param>
        public static List<StageSpawnAnchor> EnumerateInHierarchyOrder(IReadOnlyList<GameObject> roots)
        {
            var result = new List<StageSpawnAnchor>();
            if (roots == null)
            {
                return result;
            }
            for (var i = 0; i < roots.Count; i++)
            {
                var root = roots[i];
                if (root == null)
                {
                    continue;
                }
                CollectDepthFirst(root.transform, result);
            }
            return result;
        }

        private static void CollectDepthFirst(Transform node, List<StageSpawnAnchor> result)
        {
            if (node.TryGetComponent<StageSpawnAnchor>(out var anchor))
            {
                result.Add(anchor);
            }
            var childCount = node.childCount;
            for (var i = 0; i < childCount; i++)
            {
                CollectDepthFirst(node.GetChild(i), result);
            }
        }
    }
}
