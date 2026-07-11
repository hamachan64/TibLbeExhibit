using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker.Tests
{
    /// <summary>
    /// StageSpawnAnchorEnumerator（階層順アンカー列挙）のEditModeテスト。
    ///
    /// 列挙順（ルート順→siblingIndex 深さ優先）がクライアント間で一致することは
    /// マルチプレイの決定論的スポーン割り当ての前提（docs/MR_STAGE_PLAN.md §6.2）のため、
    /// GameObject の生成順や名前ではなく、渡したルート配列の順序と siblingIndex のみに
    /// 従って安定した順序になることを検証する。
    /// </summary>
    public class StageSpawnAnchorEnumeratorTests
    {
        private readonly List<GameObject> spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }
            spawned.Clear();
        }

        private GameObject CreateGameObject(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null)
            {
                go.transform.SetParent(parent, worldPositionStays: false);
            }
            spawned.Add(go);
            return go;
        }

        private static StageSpawnAnchor AddAnchor(GameObject go)
        {
            return go.AddComponent<StageSpawnAnchor>();
        }

        [Test]
        public void EnumerateInHierarchyOrder_FollowsProvidedRootOrder_NotCreationOrder()
        {
            // 生成順は rootB → rootA だが、渡すルート配列の順は rootA → rootB
            var rootB = CreateGameObject("RootB");
            var anchorB = AddAnchor(rootB);
            var rootA = CreateGameObject("RootA");
            var anchorA = AddAnchor(rootA);

            var result = StageSpawnAnchorEnumerator.EnumerateInHierarchyOrder(new[] { rootA, rootB });

            Assert.That(result, Is.EqualTo(new[] { anchorA, anchorB }));
        }

        [Test]
        public void EnumerateInHierarchyOrder_FollowsSiblingIndex_NotCreationOrder()
        {
            var root = CreateGameObject("Root");
            var childC = CreateGameObject("ChildC", root.transform);
            var anchorC = AddAnchor(childC);
            var childA = CreateGameObject("ChildA", root.transform);
            var anchorA = AddAnchor(childA);
            var childB = CreateGameObject("ChildB", root.transform);
            var anchorB = AddAnchor(childB);

            // 生成順は C, A, B。siblingIndex を A, B, C の順に強制し直す
            childA.transform.SetSiblingIndex(0);
            childB.transform.SetSiblingIndex(1);
            childC.transform.SetSiblingIndex(2);

            var result = StageSpawnAnchorEnumerator.EnumerateInHierarchyOrder(new[] { root });

            Assert.That(result, Is.EqualTo(new[] { anchorA, anchorB, anchorC }));
        }

        [Test]
        public void EnumerateInHierarchyOrder_TraversesDepthFirst_AcrossNestedLevels()
        {
            // Root
            //  ├ Branch1 (アンカーなし)
            //  │   └ Leaf1 (アンカー)
            //  └ Branch2 (アンカー)
            var root = CreateGameObject("Root");
            var branch1 = CreateGameObject("Branch1", root.transform);
            var leaf1 = CreateGameObject("Leaf1", branch1.transform);
            var anchorLeaf1 = AddAnchor(leaf1);
            var branch2 = CreateGameObject("Branch2", root.transform);
            var anchorBranch2 = AddAnchor(branch2);

            var result = StageSpawnAnchorEnumerator.EnumerateInHierarchyOrder(new[] { root });

            // 深さ優先: Branch1配下（Leaf1）を先に辿り切ってからBranch2へ
            Assert.That(result, Is.EqualTo(new[] { anchorLeaf1, anchorBranch2 }));
        }

        [Test]
        public void EnumerateInHierarchyOrder_NoAnchors_ReturnsEmpty()
        {
            var root = CreateGameObject("Root");
            CreateGameObject("Child", root.transform);

            var result = StageSpawnAnchorEnumerator.EnumerateInHierarchyOrder(new[] { root });

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void EnumerateInHierarchyOrder_NullOrEmptyRoots_ReturnsEmpty()
        {
            Assert.That(StageSpawnAnchorEnumerator.EnumerateInHierarchyOrder(null), Is.Empty);
            Assert.That(StageSpawnAnchorEnumerator.EnumerateInHierarchyOrder(new GameObject[0]), Is.Empty);
        }

        [Test]
        public void EnumerateInHierarchyOrder_IsStableAcrossRepeatedCalls()
        {
            var root = CreateGameObject("Root");
            var anchors = new List<StageSpawnAnchor>();
            for (var i = 0; i < 5; i++)
            {
                var child = CreateGameObject($"Child{i}", root.transform);
                anchors.Add(AddAnchor(child));
            }

            var first = StageSpawnAnchorEnumerator.EnumerateInHierarchyOrder(new[] { root });
            var second = StageSpawnAnchorEnumerator.EnumerateInHierarchyOrder(new[] { root });

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Is.EqualTo(anchors));
        }
    }
}
