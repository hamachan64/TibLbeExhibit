using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 「そっと置く」の降ろす先を決める（設計書 4.2。物理の Raycast を使うので純粋クラスにはしない）。
    /// お宝の位置から真下へ Raycast し、当たった面の上の点を返す。対象は会場の物（床・荷台・瓦礫）だけで、
    /// Treasure・TreasureHandle・HandBone のレイヤー、トリガー、アバターのコライダーは除く。何にも当たらなければ VenueRoot の床（ローカル y=0）。
    /// お宝のプレハブは底が原点なので、当たった点をそのまま位置にする。端末とホスト（運営PC）の両方が使う。
    /// </summary>
    public static class ThPlaceSolver
    {
        /// <summary>
        /// Raycast の始点を持ち上げる量（m）。お宝が荷台や瓦礫に少しめり込んだ状態でも、始点がコライダーの中に入らず
        /// （中から撃つと、そのコライダーに当たらない）、その上面に置けるようにするため。底 + 0.2m。
        /// </summary>
        const float RayLift = 0.2f;
        const float RayLength = 10f;
        static readonly RaycastHit[] s_hits = new RaycastHit[32];

        static readonly string[] s_excludedNames = { "Treasure", "TreasureHandle", FingertipTouchCollider.HandBoneLayerName };
        static int s_excluded = -1;

        static int ExcludedLayerMask()
        {
            if (s_excluded >= 0) return s_excluded;
            int mask = 0;
            foreach (var name in s_excludedNames)
            {
                int layer = LayerMask.NameToLayer(name);
                if (layer >= 0) mask |= 1 << layer;
            }
            return s_excluded = mask;
        }

        /// <summary>VenueRoot ローカルの位置から降ろす先（VenueRoot ローカル）を返す</summary>
        public static Vector3 Solve(Transform venueRoot, Vector3 localFrom)
        {
            if (venueRoot == null) return new Vector3(localFrom.x, 0f, localFrom.z);
            var origin = venueRoot.TransformPoint(localFrom) + Vector3.up * RayLift;
            int mask = Physics.DefaultRaycastLayers & ~ExcludedLayerMask();
            int n = Physics.RaycastNonAlloc(origin, Vector3.down, s_hits, RayLength, mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            bool found = false;
            Vector3 point = default;
            for (int i = 0; i < n; i++)
            {
                var h = s_hits[i];
                if (h.collider == null || h.distance >= best) continue;
                if (h.collider.GetComponentInParent<Styly.NetSync.NetSyncAvatar>() != null) continue;   // アバターには置かない
                best = h.distance;
                point = h.point;
                found = true;
            }
            if (found) return venueRoot.InverseTransformPoint(point);
            return new Vector3(localFrom.x, 0f, localFrom.z);
        }

        /// <summary>今の向きの Y 回転だけを残して立てる</summary>
        public static Quaternion Upright(Quaternion rot) => Quaternion.Euler(0f, rot.eulerAngles.y, 0f);
    }
}
