using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker.Tests
{
    /// <summary>
    /// StageSpawnAnchor（docs/MR_STAGE_PLAN.md §6.2 のIF契約）のEditModeテスト。
    /// isElevated がインスペクタからシリアライズ可能な private フィールドとして存在し、
    /// IsElevated プロパティへ正しく反映されることを検証する
    /// （他Issue実装が SerializedObject 経由でアンカーを配置する際の結合点のため）。
    /// </summary>
    public class StageSpawnAnchorTests
    {
        private GameObject go;

        [TearDown]
        public void TearDown()
        {
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void IsElevated_DefaultsToFalse()
        {
            go = new GameObject("Anchor");
            var anchor = go.AddComponent<StageSpawnAnchor>();

            Assert.That(anchor.IsElevated, Is.False);
        }

        [Test]
        public void IsElevated_ReflectsSerializedIsElevatedField()
        {
            go = new GameObject("Anchor");
            var anchor = go.AddComponent<StageSpawnAnchor>();

            var so = new SerializedObject(anchor);
            var prop = so.FindProperty("isElevated");
            Assert.That(prop, Is.Not.Null, "IF契約どおり isElevated という名前の SerializeField が存在すること");

            prop.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(anchor.IsElevated, Is.True);
        }

        [Test]
        public void AllowedCardIds_DefaultsToEmpty_AndAlignDefaultsToFalse()
        {
            go = new GameObject("Anchor");
            var anchor = go.AddComponent<StageSpawnAnchor>();

            Assert.That(anchor.AllowedCardIds, Is.Empty, "デフォルトは汎用アンカー（従来挙動）であること");
            Assert.That(anchor.AlignToAnchorRotation, Is.False, "デフォルトはランダム yaw（従来挙動）であること");
        }

        [Test]
        public void AllowedCardIds_ReflectsSerializedAllowedCardIdsField()
        {
            // Editor セットアップスクリプト（GIPokerPropSetup）が SerializedObject 経由で
            // アンカーへ cardId を書き込む際の結合点のため、フィールド名を契約として検証する。
            go = new GameObject("Anchor");
            var anchor = go.AddComponent<StageSpawnAnchor>();

            var so = new SerializedObject(anchor);
            var prop = so.FindProperty("allowedCardIds");
            Assert.That(prop, Is.Not.Null, "allowedCardIds という名前の SerializeField が存在すること");

            prop.arraySize = 1;
            prop.GetArrayElementAtIndex(0).stringValue = "c_stag_beetle";
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(anchor.AllowedCardIds, Is.EqualTo(new[] { "c_stag_beetle" }));
        }

        [Test]
        public void AlignToAnchorRotation_ReflectsSerializedField()
        {
            go = new GameObject("Anchor");
            var anchor = go.AddComponent<StageSpawnAnchor>();

            var so = new SerializedObject(anchor);
            var prop = so.FindProperty("alignToAnchorRotation");
            Assert.That(prop, Is.Not.Null, "alignToAnchorRotation という名前の SerializeField が存在すること");

            prop.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(anchor.AlignToAnchorRotation, Is.True);
        }
    }
}
