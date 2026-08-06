using NUnit.Framework;

namespace STYLY.TIB_Template.GIPoker.Tests
{
    /// <summary>
    /// BinderLayout（バインダーのページ・グリッド・角タブの実測値ベースレイアウト計算）のEditModeテスト。
    /// CardBinder.fbx の実測値（.claude/loops/20260805-binder-card-storage-fix.md）から
    /// 手計算した期待値と突き合わせる。
    /// </summary>
    public class BinderLayoutTests
    {
        private const float Tolerance = 0.0002f;

        [Test]
        public void PageCount_Is8()
        {
            Assert.That(BinderLayout.PageCount, Is.EqualTo(8));
        }

        [Test]
        public void SlotsPerPage_Is9_3x3()
        {
            Assert.That(BinderLayout.GridColumns, Is.EqualTo(3));
            Assert.That(BinderLayout.GridRows, Is.EqualTo(3));
            Assert.That(BinderLayout.SlotsPerPage, Is.EqualTo(9));
        }

        [Test]
        public void Capacity_Is72_PageCountTimesSlotsPerPage()
        {
            Assert.That(BinderLayout.Capacity, Is.EqualTo(72));
        }

        [Test]
        public void PageCenterX_MatchesMeasuredMidpoint()
        {
            // (-0.007 + 0.214) / 2 = 0.1035
            Assert.That(BinderLayout.PageCenterX, Is.EqualTo(0.1035f).Within(Tolerance));
        }

        [Test]
        public void CellSize_MatchesMeasuredPageDividedBy3()
        {
            // 0.221 / 3 ≈ 0.073667, 0.290 / 3 ≈ 0.096667
            Assert.That(BinderLayout.CellWidth, Is.EqualTo(0.073667f).Within(Tolerance));
            Assert.That(BinderLayout.CellHeight, Is.EqualTo(0.096667f).Within(Tolerance));
        }

        // slotIndex: 列優先（col = slot % 3, row = slot / 3）。row0が上、col0が左（既存GridPositionの符号規約を踏襲）。
        [TestCase(0, 0.029833f, 0.096667f)] // col0,row0（左上）
        [TestCase(1, 0.1035f, 0.096667f)]   // col1,row0（中央上）
        [TestCase(2, 0.177167f, 0.096667f)] // col2,row0（右上）
        [TestCase(3, 0.029833f, 0f)]        // col0,row1（左中）
        [TestCase(4, 0.1035f, 0f)]          // col1,row1（中央）
        [TestCase(5, 0.177167f, 0f)]        // col2,row1（右中）
        [TestCase(6, 0.029833f, -0.096667f)] // col0,row2（左下）
        [TestCase(7, 0.1035f, -0.096667f)]   // col1,row2（中央下）
        [TestCase(8, 0.177167f, -0.096667f)] // col2,row2（右下）
        public void SlotLocalPosition_MatchesHandComputedGrid(int slotIndex, float expectedX, float expectedZ)
        {
            var position = BinderLayout.SlotLocalPosition(slotIndex);

            Assert.That(position.x, Is.EqualTo(expectedX).Within(Tolerance));
            Assert.That(position.z, Is.EqualTo(expectedZ).Within(Tolerance));
            Assert.That(position.y, Is.EqualTo(BinderLayout.MiniCardSurfaceOffset).Within(Tolerance));
        }

        [Test]
        public void SlotLocalPosition_RightmostColumnEdge_MatchesPageMeshMaxX()
        {
            // 3列グリッドの右端セルの右辺は、ページ実メッシュの右端(X=0.214)にちょうど一致する
            // （「ポケット領域はページ境界ぎりぎりまで埋まる」設計の確認）。
            var rightCellCenterX = BinderLayout.SlotLocalPosition(2).x; // col2,row0
            var rightEdge = rightCellCenterX + BinderLayout.CellWidth * 0.5f;

            Assert.That(rightEdge, Is.EqualTo(BinderLayout.PageMeshMaxX).Within(Tolerance));
        }

        [Test]
        public void SlotLocalPosition_LeftmostColumnEdge_MatchesPageMeshMinX()
        {
            var leftCellCenterX = BinderLayout.SlotLocalPosition(0).x; // col0,row0
            var leftEdge = leftCellCenterX - BinderLayout.CellWidth * 0.5f;

            Assert.That(leftEdge, Is.EqualTo(BinderLayout.PageMeshMinX).Within(Tolerance));
        }

        [TestCase(0, 0, 0)]
        [TestCase(8, 0, 8)]
        [TestCase(9, 1, 0)]
        [TestCase(17, 1, 8)]
        [TestCase(18, 2, 0)]
        [TestCase(71, 7, 8)]
        public void PageAndSlotIndex_MatchExpectedDivision(int cardIndex, int expectedPage, int expectedSlot)
        {
            Assert.That(BinderLayout.PageIndexOf(cardIndex), Is.EqualTo(expectedPage));
            Assert.That(BinderLayout.SlotIndexOf(cardIndex), Is.EqualTo(expectedSlot));
        }

        [Test]
        public void PageTab_DoesNotOverlapPocketGridRegion()
        {
            // 角タブの当たり判定円の最も内側（PageTabLocalX - 半径）が、
            // ポケットグリッドの右端（PageMeshMaxX、SlotLocalPosition_RightmostColumnEdge_～ で確認済み）
            // より確実に外側にあることを構造的に保証する。この重なり回避が最重要の設計要件。
            var tabInnerEdgeX = BinderLayout.PageTabLocalX - BinderLayout.PageTabRadius;

            Assert.That(tabInnerEdgeX, Is.GreaterThan(BinderLayout.PageMeshMaxX));
            // マージン定数どおりの余白が確保されていることも確認する
            Assert.That(tabInnerEdgeX - BinderLayout.PageMeshMaxX, Is.EqualTo(BinderLayout.PageTabEdgeMargin).Within(Tolerance));
        }

        [Test]
        public void PageTabLocalPosition_MatchesXAndZConstants()
        {
            var tab = BinderLayout.PageTabLocalPosition;

            Assert.That(tab.x, Is.EqualTo(BinderLayout.PageTabLocalX).Within(Tolerance));
            Assert.That(tab.z, Is.EqualTo(BinderLayout.PageTabLocalZ).Within(Tolerance));
        }

        [TestCase(0f, false)]
        [TestCase(45f, false)]
        [TestCase(89.9f, false)]
        [TestCase(90f, true)]
        [TestCase(120f, true)]
        [TestCase(180f, true)]
        public void ShouldCommitPageTurn_UsesThreshold(float angleDegrees, bool expectedCommit)
        {
            Assert.That(BinderLayout.ShouldCommitPageTurn(angleDegrees), Is.EqualTo(expectedCommit));
        }

        // 前めくり（forward=true, 0°→180°方向）は「開ききった（角度>=しきい値）」場合にのみ進む。
        [TestCase(true, 0f, false)]
        [TestCase(true, 45f, false)]
        [TestCase(true, 89.9f, false)]
        [TestCase(true, 90f, true)]
        [TestCase(true, 180f, true)]
        // 後ろめくり（forward=false, 180°→0°方向）は「閉じきった（角度<しきい値）」場合にのみ戻る。
        // 前めくりと判定が非対称であることの回帰防止テスト（実際にここで前後の判定を
        // 取り違えるバグが混入していた）。
        [TestCase(false, 0f, true)]
        [TestCase(false, 45f, true)]
        [TestCase(false, 89.9f, true)]
        [TestCase(false, 90f, false)]
        [TestCase(false, 120f, false)]
        [TestCase(false, 180f, false)]
        public void ShouldAdvancePage_IsAsymmetricBetweenForwardAndBackward(bool forward, float finalAngleDegrees, bool expectedAdvance)
        {
            Assert.That(BinderLayout.ShouldAdvancePage(forward, finalAngleDegrees), Is.EqualTo(expectedAdvance));
        }

        [Test]
        public void MiniCardScale_FitsWithinCell_AndIsNotTooSmall()
        {
            // セル(0.0737×0.0967)にカード実寸(0.063×0.088)がスケールMiniCardScaleで収まり、
            // かつ極端に小さくなりすぎない（0.5倍未満にはならない）ことを確認する。
            const float referenceCardWidth = 0.063f;
            const float referenceCardHeight = 0.088f;

            var scaledWidth = referenceCardWidth * BinderLayout.MiniCardScale;
            var scaledHeight = referenceCardHeight * BinderLayout.MiniCardScale;

            Assert.That(scaledWidth, Is.LessThanOrEqualTo(BinderLayout.CellWidth));
            Assert.That(scaledHeight, Is.LessThanOrEqualTo(BinderLayout.CellHeight));
            Assert.That(BinderLayout.MiniCardScale, Is.GreaterThan(0.5f));
        }
    }
}
