using NUnit.Framework;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker.Tests
{
    /// <summary>
    /// BinderView の表示制御（Show/Hide/Toggle → IsVisible）のEditModeテスト。
    /// バインダーはLサインジェスチャーで呼び出す仕様のため、Init直後は非表示であることも確認する。
    /// </summary>
    public class BinderViewTests
    {
        private GameObject binderGo;
        private GameObject dummyModelPrefab;
        private BinderView binderView;

        [SetUp]
        public void SetUp()
        {
            // CardBinderModelPrefab が未設定だと BuildBook() がプレースホルダー生成経路
            // （BuildPlaceholderBook 内で Object.Destroy(Collider) を呼ぶ）に入ってしまい、
            // Edit Mode では非対応（Destroy may not be called from edit mode）のため、
            // ダミーのモデルプレハブを与えてプレースホルダー経路を回避する。
            dummyModelPrefab = new GameObject("DummyBinderModel(Test)");
            GIPokerAssets.CardBinderModelPrefab = dummyModelPrefab;
            // 未設定時は警告ログのみでスキップされる仕様を利用し、煙VFXは付けない
            // （SpawnVfx内のDestroy(obj, lifetime)もEdit Mode非対応のため）。
            GIPokerAssets.BinderSmokeVfx = null;

            binderGo = new GameObject("GIPoker_Binder(Test)");
            var binder = binderGo.AddComponent<CardBinder>();
            binderView = binderGo.AddComponent<BinderView>();
            binderView.Init(binder);
        }

        [TearDown]
        public void TearDown()
        {
            if (binderGo != null)
            {
                Object.DestroyImmediate(binderGo);
            }
            if (dummyModelPrefab != null)
            {
                Object.DestroyImmediate(dummyModelPrefab);
            }
            GIPokerAssets.CardBinderModelPrefab = null;
            GIPokerAssets.BinderSmokeVfx = null;
        }

        [Test]
        public void Init_DefaultsToHidden()
        {
            Assert.That(binderView.IsVisible.CurrentValue, Is.False);
        }

        [Test]
        public void Show_MakesVisible()
        {
            binderView.Show();

            Assert.That(binderView.IsVisible.CurrentValue, Is.True);
        }

        [Test]
        public void Show_WhenAlreadyVisible_IsIdempotent()
        {
            binderView.Show();
            binderView.Show();

            Assert.That(binderView.IsVisible.CurrentValue, Is.True);
        }

        [Test]
        public void Hide_AfterShow_MakesHidden()
        {
            binderView.Show();
            binderView.Hide();

            Assert.That(binderView.IsVisible.CurrentValue, Is.False);
        }

        [Test]
        public void Hide_WhenAlreadyHidden_IsNoOp()
        {
            binderView.Hide();

            Assert.That(binderView.IsVisible.CurrentValue, Is.False);
        }

        [Test]
        public void Toggle_FromHidden_Shows()
        {
            binderView.Toggle();

            Assert.That(binderView.IsVisible.CurrentValue, Is.True);
        }

        [Test]
        public void Toggle_Twice_ReturnsToHidden()
        {
            binderView.Toggle();
            binderView.Toggle();

            Assert.That(binderView.IsVisible.CurrentValue, Is.False);
        }
    }
}
