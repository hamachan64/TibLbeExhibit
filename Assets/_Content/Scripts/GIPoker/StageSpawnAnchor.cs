using UnityEngine;

namespace STYLY.TIB_Template.GIPoker
{
    /// <summary>
    /// アイテム出現候補点。ErosionPatch（侵蝕パッチ）/ RuinPillar・FloatingRock（遺跡台座）上に配置する。
    ///
    /// ItemSpawnManager がシーン内の全アンカーを階層順（Transform ルート順→siblingIndex 深さ優先）で
    /// 列挙し、GameSession.SessionSeed による選択のみで決定論的にスポーン位置として割り当てる
    /// （docs/MR_STAGE_PLAN.md §6.2 のIF契約）。
    ///
    /// このクラス自体はデータ保持のみを行う（配置・列挙・割り当てロジックは持たない）。
    /// フィールド名・プロパティ名は他Issue（#1/#2）実装との結合点のため変更しないこと。
    /// </summary>
    public class StageSpawnAnchor : MonoBehaviour
    {
        [SerializeField, Tooltip("台座（RuinPillar/FloatingRock）上のアンカーなら true、床の侵蝕パッチ（ErosionPatch）上なら false")]
        private bool isElevated; // 台座上（true）か床パッチ上（false）か

        /// <summary>台座上（true）か床パッチ上（false）か</summary>
        public bool IsElevated => isElevated;

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = isElevated
                ? new Color(1f, 0.75f, 0.2f, 0.9f)
                : new Color(0.3f, 0.9f, 0.5f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.12f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 0.25f);
        }
#endif
    }
}
