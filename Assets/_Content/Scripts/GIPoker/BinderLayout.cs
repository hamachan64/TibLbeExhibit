using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// CardBinder.fbx の実測値から算出した、バインダーのページ・グリッド・角タブに関する
    /// 純粋な（MonoBehaviourに依存しない）レイアウト定数と計算式。
    ///
    /// すべて EditModeテストで直接検証できるように、GameObject/シーンに一切依存しない
    /// static な定数・関数のみで構成する（BinderView / CardBinder から参照される）。
    ///
    /// 実測の出どころ: CardBinder.fbx をバイナリ解析した結果（2026-08、
    /// .claude/loops/20260805-binder-card-storage-fix.md 記載）。
    /// 各 Page_XX のメッシュ境界は、モデルローカル座標（BinderView が Model に適用する
    /// Euler(-90,0,0) 回転の「前」の値）で
    ///   X（見開き幅方向）: -0.007 〜 0.214（幅 0.221m）
    ///   Z（ページ縦方向）: -0.145 〜 0.145（高さ 0.290m）
    /// であり、「スロット63×88mm（トレカ実寸）× 3列 × 3行」で設計されている
    /// （.claude/agent-memory/blender-mcp-learnings.md 8章）。
    /// </summary>
    public static class BinderLayout
    {
        /// <summary>バインダーの総ページ数（CardBinder.fbx の Page_01〜Page_08）</summary>
        public const int PageCount = 8;

        /// <summary>1ページあたりの列数（スロット63mm幅 × 3列 の設計に対応）</summary>
        public const int GridColumns = 3;

        /// <summary>1ページあたりの行数（スロット88mm高 × 3行 の設計に対応）</summary>
        public const int GridRows = 3;

        /// <summary>1ページあたりのポケット数（3×3）</summary>
        public const int SlotsPerPage = GridColumns * GridRows;

        /// <summary>バインダー全体の収納上限（8ページ × 9ポケット）</summary>
        public const int Capacity = PageCount * SlotsPerPage;

        // ページ実メッシュ境界（モデルローカル座標、Model の Euler(-90,0,0) 回転前の値。実測値）
        // X軸: PlayMode中の実測（Page_01.MeshFilter.sharedMesh.bounds）により、スパイン側が
        // X≈+0.007、外側（ページをめくる側）がX≈-0.214であることを確認済み（当初の想定は符号が逆だった）。
        public const float PageMeshMinX = -0.214f;
        public const float PageMeshMaxX = 0.007f;
        public const float PageMeshMinZ = -0.145f;
        public const float PageMeshMaxZ = 0.145f;

        /// <summary>ページ幅（見開き方向）＝ 0.221m</summary>
        public const float PageMeshWidth = PageMeshMaxX - PageMeshMinX;

        /// <summary>ページ高さ（縦方向）＝ 0.290m</summary>
        public const float PageMeshHeight = PageMeshMaxZ - PageMeshMinZ;

        /// <summary>ページ中心のX座標（実測値から算出）＝ 約0.1035m</summary>
        public const float PageCenterX = (PageMeshMinX + PageMeshMaxX) * 0.5f;

        /// <summary>グリッドセル幅（ページ幅 ÷ 3）＝ 約0.0737m</summary>
        public const float CellWidth = PageMeshWidth / GridColumns;

        /// <summary>グリッドセル高さ（ページ高さ ÷ 3）＝ 約0.0967m</summary>
        public const float CellHeight = PageMeshHeight / GridRows;

        /// <summary>
        /// ミニカードをページ面からわずかに浮かせるオフセット（Page transform のローカルY軸＝
        /// ページ積層厚み方向）。z-fighting回避用。ページ1枚あたりの積層オフセット実測値
        /// （約1.4mm）より大きい値にして、隣接ページの面より確実に手前へ出す。
        /// </summary>
        public const float MiniCardSurfaceOffset = 0.012f;

        // CardVisualFactory.CardWidth/CardHeight（private const 0.063f / 0.088f、実寸トレカサイズ）
        // と同じ値。参照を増やさないためここでは値を複製している。CardVisualFactory側の値を
        // 変更する場合はこちらも同期させること。
        private const float ReferenceCardWidth = 0.063f;
        private const float ReferenceCardHeight = 0.088f;

        /// <summary>セルからカードがはみ出さないための安全マージン（1.0 未満）</summary>
        private const float MiniCardFitMargin = 0.78f;

        /// <summary>
        /// ミニカードのスケール。セル幅・セル高それぞれに対するカード実寸の比のうち、
        /// より厳しい（小さい）方＝高さ方向（CellHeight/ReferenceCardHeight ≈ 1.099、
        /// 幅方向の ≈1.169 より小さい）を基準にし、安全マージンを掛けて算出する。
        /// これにより、セルに食み出さず、かつ実寸に近い大きさ（≈0.99倍）になる。
        /// </summary>
        public const float MiniCardScale = (CellHeight / ReferenceCardHeight) * MiniCardFitMargin;

        // 角タブ（ページめくりの掴み起点）。ページ実メッシュの外側（X < PageMeshMinX、スパインから
        // 最も遠い、実際にページをめくる側の角）に置き、ポケットグリッド領域
        // （X >= PageMeshMinX、下記 SlotLocalPosition 参照）と物理的に重ならないようにする。
        public const float PageTabRadius = 0.02f;
        public const float PageTabEdgeMargin = 0.01f;

        /// <summary>角タブのローカルX座標。PageMeshMinX - 半径 - マージン で、ポケット領域と重ならないことを保証する</summary>
        public const float PageTabLocalX = PageMeshMinX - PageTabRadius - PageTabEdgeMargin;

        /// <summary>角タブのローカルZ座標。下端寄りの角（自然に指がかかる位置）</summary>
        public const float PageTabLocalZ = PageMeshMinZ + PageTabRadius + 0.01f;

        /// <summary>角タブの位置（Page Transform のローカル座標系）</summary>
        public static readonly Vector3 PageTabLocalPosition = new(PageTabLocalX, 0f, PageTabLocalZ);

        /// <summary>ミニカード取り出し判定の距離しきい値（セル幅の半分強。実機での微調整対象）</summary>
        public const float MiniCardGrabRadius = CellWidth * 0.55f;

        /// <summary>ページめくりを「めくり切り」として確定させる回転角のしきい値（度）</summary>
        public const float PageTurnCommitThresholdDegrees = 90f;

        /// <summary>カード全体でのインデックス（挿入順の0始まり）から、そのカードが属するページ番号を算出する</summary>
        public static int PageIndexOf(int cardIndex) => cardIndex / SlotsPerPage;

        /// <summary>カード全体でのインデックスから、そのページ内でのスロット番号（0〜8）を算出する</summary>
        public static int SlotIndexOf(int cardIndex) => cardIndex % SlotsPerPage;

        /// <summary>1枚目（左上スロット0）のカードのローカルX座標</summary>
        public const float FirstCardX = -0.044f;

        /// <summary>1枚目（左上スロット0）のカードのローカルZ座標</summary>
        public const float FirstCardZ = -0.09f;

        /// <summary>カードの横方向間隔（列ピッチ）</summary>
        public const float CardSpacingX = 0.033f;

        /// <summary>カードの縦方向間隔（行ピッチ）</summary>
        public const float CardSpacingZ = 0.09f;

        /// <summary>
        /// ページ内スロット番号（0〜8、列優先）から、そのページ Transform を基準としたローカル座標を返す。
        /// 原点はページ自身のローカル座標系（実測値ベース）。
        /// </summary>
        public static Vector3 SlotLocalPosition(int slotIndex)
        {
            var col = slotIndex % GridColumns;
            var row = slotIndex / GridColumns;
            var x = FirstCardX + col * CardSpacingX;
            var z = FirstCardZ + row * CardSpacingZ;
            return new Vector3(x, MiniCardSurfaceOffset, z);
        }

        /// <summary>ピンチ終了時の回転角から、めくり切り（180°側に収束）すべきかどうかを判定する</summary>
        public static bool ShouldCommitPageTurn(float angleDegrees) => angleDegrees >= PageTurnCommitThresholdDegrees;

        /// <summary>
        /// ページめくりドラッグ終了時、実際に currentPageIndex を切り替える（次/前のページへ進める）
        /// べきかどうかを判定する。
        /// 前めくり（forward=true、0°→180°方向）は開ききった場合に次のページへ進み、
        /// 後ろめくり（forward=false、180°→0°方向）は閉じきった場合に前のページへ戻る、
        /// という非対称な条件になる点に注意（開ききったまま／閉じたままキャンセルした場合は
        /// バネで元の角度に戻るだけで、ページは切り替わらない）。
        /// </summary>
        public static bool ShouldAdvancePage(bool forward, float finalAngleDegrees)
        {
            var settleOpen = ShouldCommitPageTurn(finalAngleDegrees);
            return forward ? settleOpen : !settleOpen;
        }
    }
}
