using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker.Tests
{
    /// <summary>
    /// ItemCatalog（CardDefinition参照ベースの Entry）のEditModeテスト。
    /// 20260807-card-rank-config-consolidation.md の受け入れ基準のうち、
    /// Entry の CardDefinition への委譲（Prefab/InitialCount/MaxCount）と、
    /// FindPrefabByCardId / FindDefinitionByRank の解決を検証する。
    /// </summary>
    public class ItemCatalogTests
    {
        private ItemCatalog catalog;
        private readonly List<CardDefinition> createdDefinitions = new();
        private readonly List<GameObject> createdGameObjects = new();

        [TearDown]
        public void TearDown()
        {
            if (catalog != null)
            {
                Object.DestroyImmediate(catalog);
                catalog = null;
            }
            foreach (var definition in createdDefinitions)
            {
                if (definition != null)
                {
                    Object.DestroyImmediate(definition);
                }
            }
            createdDefinitions.Clear();
            foreach (var go in createdGameObjects)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }
            createdGameObjects.Clear();
        }

        /// <summary>SerializedObject 経由で cardId/rank/prefab/initialCount/maxCount を設定した CardDefinition を作る。</summary>
        private CardDefinition CreateDefinition(string cardId, CardRank rank, GameObject prefab = null, int initialCount = 1, int maxCount = 1)
        {
            var definition = ScriptableObject.CreateInstance<CardDefinition>();
            createdDefinitions.Add(definition);

            var so = new SerializedObject(definition);
            so.FindProperty("cardId").stringValue = cardId;
            so.FindProperty("rank").enumValueIndex = (int)rank;
            so.FindProperty("prefab").objectReferenceValue = prefab;
            so.FindProperty("initialCount").intValue = initialCount;
            so.FindProperty("maxCount").intValue = maxCount;
            so.ApplyModifiedPropertiesWithoutUndo();

            return definition;
        }

        private GameObject CreatePrefabStub(string name)
        {
            var go = new GameObject(name);
            createdGameObjects.Add(go);
            return go;
        }

        /// <summary>SerializedObject 経由で catalog.entries に definition 参照を1件追加する。</summary>
        private void AddEntry(CardDefinition definition)
        {
            var so = new SerializedObject(catalog);
            var entriesProp = so.FindProperty("entries");
            var index = entriesProp.arraySize;
            entriesProp.InsertArrayElementAtIndex(index);
            entriesProp.GetArrayElementAtIndex(index).FindPropertyRelative("definition").objectReferenceValue = definition;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void Entry_DelegatesPrefabInitialCountMaxCount_FromDefinition()
        {
            catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            var prefab = CreatePrefabStub("Item_d_flower");
            var definition = CreateDefinition("d_flower", CardRank.D, prefab, initialCount: 1, maxCount: 2);
            AddEntry(definition);

            var entry = catalog.Entries[0];

            Assert.That(entry.Prefab, Is.SameAs(prefab));
            Assert.That(entry.InitialCount, Is.EqualTo(1));
            Assert.That(entry.MaxCount, Is.EqualTo(2));
        }

        [Test]
        public void Entry_ReturnsDefaults_WhenDefinitionIsNull()
        {
            catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            AddEntry(null);

            var entry = catalog.Entries[0];

            Assert.That(entry.Prefab, Is.Null);
            Assert.That(entry.InitialCount, Is.EqualTo(0));
            Assert.That(entry.MaxCount, Is.EqualTo(0));
        }

        [Test]
        public void FindPrefabByCardId_ReturnsMatchingPrefab()
        {
            catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            var flowerPrefab = CreatePrefabStub("Item_d_flower");
            var swordPrefab = CreatePrefabStub("Item_d_sword");
            AddEntry(CreateDefinition("d_flower", CardRank.D, flowerPrefab));
            AddEntry(CreateDefinition("d_sword", CardRank.D, swordPrefab));

            var found = catalog.FindPrefabByCardId("d_sword");

            Assert.That(found, Is.SameAs(swordPrefab));
        }

        [Test]
        public void FindPrefabByCardId_ReturnsNull_WhenNotFound()
        {
            catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            AddEntry(CreateDefinition("d_flower", CardRank.D, CreatePrefabStub("Item_d_flower")));

            var found = catalog.FindPrefabByCardId("unknown_card_id");

            Assert.That(found, Is.Null);
        }

        [Test]
        public void FindPrefabByCardId_ReturnsNull_WhenCardIdIsNullOrEmpty()
        {
            catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            AddEntry(CreateDefinition("d_flower", CardRank.D, CreatePrefabStub("Item_d_flower")));

            Assert.That(catalog.FindPrefabByCardId(null), Is.Null);
            Assert.That(catalog.FindPrefabByCardId(string.Empty), Is.Null);
        }

        [Test]
        public void FindDefinitionByRank_ReturnsFirstMatchingDefinition()
        {
            catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            var ssDefinition = CreateDefinition("ss_beach", CardRank.SS, CreatePrefabStub("Item_ss_beach"), initialCount: 0, maxCount: 0);
            AddEntry(CreateDefinition("d_flower", CardRank.D, CreatePrefabStub("Item_d_flower")));
            AddEntry(ssDefinition);

            var found = catalog.FindDefinitionByRank(CardRank.SS);

            Assert.That(found, Is.SameAs(ssDefinition));
        }

        [Test]
        public void FindDefinitionByRank_ReturnsNull_WhenNoneMatch()
        {
            catalog = ScriptableObject.CreateInstance<ItemCatalog>();
            AddEntry(CreateDefinition("d_flower", CardRank.D, CreatePrefabStub("Item_d_flower")));

            var found = catalog.FindDefinitionByRank(CardRank.SS);

            Assert.That(found, Is.Null);
        }
    }
}
