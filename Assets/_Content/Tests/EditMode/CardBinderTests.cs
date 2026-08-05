using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker.Tests
{
    /// <summary>
    /// CardBinder のEditModeテスト。20260805-binder-card-storage-fix.md の受け入れ基準のうち、
    /// capacity算出（8ページ×9ポケット=72）と、StoreCopy（Source==nullの複製カード）を
    /// TryTakeOut が拒否すること（バグ修正1）を検証する。
    /// BinderView は意図的にアタッチしない（RebuildMiniCardsがCardVisualFactory経由で
    /// Object.Destroy(Collider)を呼び、Edit Modeで失敗するため。CardBinder単体はその依存が無い）。
    /// </summary>
    public class CardBinderTests
    {
        private GameObject binderGo;
        private CardBinder binder;
        private readonly List<CardDefinition> createdDefinitions = new();

        [SetUp]
        public void SetUp()
        {
            binderGo = new GameObject("CardBinder(Test)");
            binder = binderGo.AddComponent<CardBinder>();
        }

        [TearDown]
        public void TearDown()
        {
            if (binderGo != null)
            {
                Object.DestroyImmediate(binderGo);
            }
            foreach (var definition in createdDefinitions)
            {
                if (definition != null)
                {
                    Object.DestroyImmediate(definition);
                }
            }
            createdDefinitions.Clear();
        }

        private CardDefinition CreateDefinition()
        {
            var definition = ScriptableObject.CreateInstance<CardDefinition>();
            createdDefinitions.Add(definition);
            return definition;
        }

        [Test]
        public void PageCount_Is8_And_SlotsPerPage_Is9()
        {
            Assert.That(CardBinder.PageCount, Is.EqualTo(8));
            Assert.That(CardBinder.SlotsPerPage, Is.EqualTo(9));
        }

        [Test]
        public void Capacity_Is72_AllowsExactly72_RejectsThe73rd()
        {
            for (var i = 0; i < 72; i++)
            {
                Assert.That(binder.StoreCopy(CreateDefinition()), Is.True, $"{i + 1}枚目は収納できるはず");
            }
            Assert.That(binder.IsFull, Is.True);
            Assert.That(binder.Cards.Count, Is.EqualTo(72));

            Assert.That(binder.StoreCopy(CreateDefinition()), Is.False, "73枚目は収納を拒否するべき");
            Assert.That(binder.Cards.Count, Is.EqualTo(72));
        }

        [Test]
        public void TryTakeOut_RejectsStoreCopy_AndKeepsItInBinder()
        {
            var definition = CreateDefinition();
            Assert.That(binder.StoreCopy(definition), Is.True);
            var copyInstance = binder.Cards.First();
            Assert.That(copyInstance.Source, Is.Null, "前提: StoreCopyのインスタンスはSourceを持たない");

            var result = binder.TryTakeOut(copyInstance, Vector3.zero);

            Assert.That(result, Is.False, "Source==nullのカードは取り出し不可であるべき（バグ修正1）");
            Assert.That(binder.Cards.Count, Is.EqualTo(1), "拒否された場合はバインダーから消えずに残るべき");
        }

        [Test]
        public void TryTakeOut_NullInstance_ReturnsFalse()
        {
            Assert.That(binder.TryTakeOut(null, Vector3.zero), Is.False);
        }
    }
}
