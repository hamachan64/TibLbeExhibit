using System.Collections.Generic;
using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// アイテム出現候補点。ErosionPatch（侵蝕パッチ）/ RuinPillar・FloatingRock（遺跡台座）/
    /// 街並みプロップ（木の幹・小屋の作業台・祭壇など）上に配置する。
    ///
    /// ItemSpawnManager がシーン内の全アンカーを階層順（Transform ルート順→siblingIndex 深さ優先）で
    /// 列挙し、GameSession.SessionSeed による選択のみで決定論的にスポーン位置として割り当てる
    /// （docs/MR_STAGE_PLAN.md §6.2 のIF契約）。
    ///
    /// allowedCardIds が空なら全アイテム共通の汎用アンカー（従来挙動）。
    /// cardId を指定すると、そのアイテム専用のアンカーになる（例: 木の幹アンカーにクワガタのみ）。
    ///
    /// このクラス自体はデータ保持のみを行う（配置・列挙・割り当てロジックは持たない）。
    /// フィールド名・プロパティ名は他Issue（#1/#2）実装との結合点のため変更しないこと。
    /// </summary>
    public class StageSpawnAnchor : MonoBehaviour
    {
        [SerializeField, Tooltip("台座（RuinPillar/FloatingRock）上のアンカーなら true、床の侵蝕パッチ（ErosionPatch）上なら false")]
        private bool isElevated; // 台座上（true）か床パッチ上（false）か

        [SerializeField, Tooltip("このアンカーに出現してよいアイテムの cardId（空 = 全アイテム可の汎用アンカー）")]
        private List<string> allowedCardIds = new();

        [SerializeField, Tooltip("スポーン時にこのアンカーの回転をアイテムへ適用する（木の幹への張り付き・壁龕の立ち姿など）。false ならランダム yaw（従来挙動）")]
        private bool alignToAnchorRotation;

        /// <summary>台座上（true）か床パッチ上（false）か</summary>
        public bool IsElevated => isElevated;

        /// <summary>このアンカーに出現してよいアイテムの cardId（空 = 汎用アンカー）</summary>
        public IReadOnlyList<string> AllowedCardIds => allowedCardIds;

        /// <summary>スポーン時にアンカーの回転をアイテムへ適用するか</summary>
        public bool AlignToAnchorRotation => alignToAnchorRotation;

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = allowedCardIds != null && allowedCardIds.Count > 0
                ? new Color(0.4f, 0.7f, 1f, 0.9f)
                : isElevated
                    ? new Color(1f, 0.75f, 0.2f, 0.9f)
                    : new Color(0.3f, 0.9f, 0.5f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.12f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 0.25f);
            if (alignToAnchorRotation)
            {
                // 整列アンカーは向きが重要なため forward 矢印を表示する
                Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.2f);
            }
        }
#endif
    }
}
