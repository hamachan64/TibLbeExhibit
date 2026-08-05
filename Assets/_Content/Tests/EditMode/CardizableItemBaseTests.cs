using NUnit.Framework;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker.Tests
{
    /// <summary>
    /// CardizableItemBase のEditModeテスト。20260805-binder-card-storage-fix.md のバグ修正2
    /// （バインダーから取り出した直後は1分ルールのカウントダウンを一時停止し、
    /// ゲイン・再収納をせず手放したときだけ再開する）を検証する。
    ///
    /// TryCardify() は CardVisualFactory.Create() 経由で Object.Destroy(Collider) を呼び、
    /// Edit Modeでは失敗する（"Destroy may not be called from edit mode"）ため、
    /// このテストでは TryCardify() を使わず、MarkStored() で直接 Stored 状態を作ってから
    /// ReleaseFromBinder() を呼ぶことでこの依存を回避する
    /// （MarkStored/ReleaseFromBinderはstateの遷移のみで見た目生成を行わない）。
    /// </summary>
    public class CardizableItemBaseTests
    {
        private GameObject itemGo;
        private CardizableItemBase item;

        [SetUp]
        public void SetUp()
        {
            itemGo = new GameObject("CardizableItem(Test)");
            item = itemGo.AddComponent<CardizableItemBase>();
            item.MarkStored(); // TryCardify()（CardVisualFactory経由）を経由せずStored状態を作る
        }

        [TearDown]
        public void TearDown()
        {
            if (itemGo != null)
            {
                Object.DestroyImmediate(itemGo);
            }
        }

        [Test]
        public void ReleaseFromBinder_PausesCountdownImmediately()
        {
            item.ReleaseFromBinder(Vector3.zero);

            Assert.That(item.State.CurrentValue, Is.EqualTo(CardizableState.FloatingCard));
            Assert.That(item.IsCountdownPaused.CurrentValue, Is.True, "取り出し直後はカウントダウンが一時停止しているべき");
        }

        [Test]
        public void ReleaseFromBinder_ThenGrab_StaysPausedWhileHeld()
        {
            item.ReleaseFromBinder(Vector3.zero);

            var grabbed = item.TryGrab();

            Assert.That(grabbed, Is.True);
            Assert.That(item.State.CurrentValue, Is.EqualTo(CardizableState.Held));
            Assert.That(item.IsCountdownPaused.CurrentValue, Is.True, "掴んでいる間は一時停止のままであるべき");
        }

        [Test]
        public void ReleaseFromBinder_ThenGrabThenRelease_ResumesCountdown()
        {
            item.ReleaseFromBinder(Vector3.zero);
            item.TryGrab();

            item.Release();

            Assert.That(item.State.CurrentValue, Is.EqualTo(CardizableState.FloatingCard));
            Assert.That(item.IsCountdownPaused.CurrentValue, Is.False,
                "ゲインも再収納もせず手放した時点でカウントダウンが再開するべき（バグ修正2）");
        }

        [Test]
        public void ReleaseFromBinder_WhenNotStored_IsNoOp()
        {
            // MarkStored() を経ていない（state == Item の）アイテムに ReleaseFromBinder() を
            // 呼んでも、事前条件（Stored状態）を満たさないため何も起きないことを確認する。
            var freshGo = new GameObject("FreshItem(Test)");
            var freshItem = freshGo.AddComponent<CardizableItemBase>();
            try
            {
                freshItem.ReleaseFromBinder(Vector3.zero);

                Assert.That(freshItem.State.CurrentValue, Is.EqualTo(CardizableState.Item));
                Assert.That(freshItem.IsCountdownPaused.CurrentValue, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(freshGo);
            }
        }
    }
}
