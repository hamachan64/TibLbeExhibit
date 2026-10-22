using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 「そっと置く」の降ろす先を決める（設計書 4.2。物理の Raycast を使うので純粋クラスにはしない）。
    /// お宝の位置から真下へ Raycast し、当たった面の上の点を返す。対象は会場の物（床・荷台・瓦礫）だけで、
    /// Treasure・TreasureGrabZone・HandBone のレイヤー、トリガー、アバターのコライダーは除く。何にも当たらなければ VenueRoot の床（ローカル y=0）。
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

        static readonly string[] s_excludedNames = { "Treasure", "TreasureGrabZone", FingertipTouchCollider.HandBoneLayerName };
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

        // ---- 横の押し出し（立て直した外形が壁・家具に食い込まないようにする） ----

        /// <summary>水平に押し出せる合計の上限（m）</summary>
        public const float MaxPush = 0.3f;
        const int MaxPushIterations = 3;
        /// <summary>ComputePenetration の結果に足す余裕（m）。1mm 以下の重なりに収めるため</summary>
        const float PushMargin = 0.0015f;
        const float MinHorizontal = 0.1f;
        static readonly Collider[] s_overlaps = new Collider[32];

        /// <summary>
        /// <see cref="Solve"/> の結果を基に、立て直した向きのお宝の本体コライダーが会場の物と重なるとき、重なりが無くなるまで水平に押し出す
        /// （最大 3 回・合計 <see cref="MaxPush"/> まで。押し出した後はその XZ で Solve をやり直して高さを決め直す）。
        /// 上限内で解消できなければそのまま置いて警告を出す。置くときに 1 回だけ呼ぶ（持ち主の端末のみ）。
        /// お宝の本体コライダーは凸なので、相手が非凸の MeshCollider（家具）でも ComputePenetration で押し出せる。
        /// </summary>
        public static Vector3 SolveClear(Transform venueRoot, Vector3 localFrom, Quaternion localRot, Collider[] bodyColliders, Transform treasureRoot)
        {
            var dest = Solve(venueRoot, localFrom);
            if (venueRoot == null || bodyColliders == null || bodyColliders.Length == 0 || treasureRoot == null) return dest;

            float pushed = 0f;
            for (int i = 0; i < MaxPushIterations; i++)
            {
                if (!TryFindPush(venueRoot, dest, localRot, bodyColliders, treasureRoot, out var push)) return dest;
                float len = push.magnitude;
                float room = MaxPush - pushed;
                if (room <= 0f) break;
                if (len > room) { push *= room / len; len = room; }
                pushed += len;
                // push は世界座標。VenueRoot ローカルへ直し、押し出した XZ で高さを決め直す
                var lp = venueRoot.InverseTransformVector(push);
                dest = Solve(venueRoot, new Vector3(dest.x + lp.x, dest.y, dest.z + lp.z));
            }
            if (TryFindPush(venueRoot, dest, localRot, bodyColliders, treasureRoot, out _))
                Debug.LogWarning("[ThPlaceSolver] 押し出しの上限内で重なりを解消できなかったので、そのまま置きます。");
            return dest;
        }

        /// <summary>dest（VenueRoot ローカル）に置いたとき重なる物があれば、水平の押し出しベクトル（世界座標）を返す</summary>
        static bool TryFindPush(Transform venueRoot, Vector3 dest, Quaternion localRot, Collider[] bodies, Transform treasureRoot, out Vector3 push)
        {
            push = default;
            var worldPos = venueRoot.TransformPoint(dest);
            var worldRot = venueRoot.rotation * localRot;
            int mask = Physics.DefaultRaycastLayers & ~ExcludedLayerMask();
            float bestLen = 0f;
            foreach (var body in bodies)
            {
                if (body == null) continue;
                var bt = body.transform;
                var relPos = treasureRoot.InverseTransformPoint(bt.position);
                var relRot = Quaternion.Inverse(treasureRoot.rotation) * bt.rotation;
                var pos = worldPos + worldRot * Vector3.Scale(relPos, treasureRoot.lossyScale);
                var rot = worldRot * relRot;
                float radius = body.bounds.extents.magnitude;
                int n = Physics.OverlapSphereNonAlloc(pos, radius, s_overlaps, mask, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < n; k++)
                {
                    var other = s_overlaps[k];
                    if (other == null || other == body) continue;
                    if (other.GetComponentInParent<Styly.NetSync.NetSyncAvatar>() != null) continue;
                    if (!Physics.ComputePenetration(body, pos, rot, other, other.transform.position, other.transform.rotation, out var dir, out var dist)) continue;
                    var h = new Vector3(dir.x, 0f, dir.z);
                    float hm = h.magnitude;
                    if (hm < MinHorizontal) continue;   // ほぼ真上下の重なりは Solve の高さで決まる
                    float amount = dist / hm + PushMargin;
                    if (amount > bestLen) { bestLen = amount; push = h / hm * amount; }
                }
            }
            return bestLen > 0f;
        }

        /// <summary>今の向きの Y 回転だけを残して立てる</summary>
        public static Quaternion Upright(Quaternion rot) => Quaternion.Euler(0f, rot.eulerAngles.y, 0f);
    }
}
