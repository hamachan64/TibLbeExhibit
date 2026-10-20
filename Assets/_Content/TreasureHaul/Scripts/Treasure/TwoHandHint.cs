using System.Collections.Generic;
using UnityEngine;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 「両手で持とう」を出すお宝を決める（F-ITEM-5。純粋）。
    /// 本編で、頭から〔ビームの距離〕以内に、まだ誰も持っていない M（Resting）が初めて入ったとき、その M の番号を返す。
    /// 1 回の本編で、その端末に 1 回だけ（Reset で次の回に備える）。
    /// 距離は水平（XZ）で測る: 頭は床の置物より 1.5m 以上高く、3 次元の距離ではビームの距離以内に入れないため。
    /// </summary>
    public sealed class TwoHandHint
    {
        bool _shown;

        public bool Shown => _shown;

        public void Reset() => _shown = false;

        /// <param name="headLocal">頭の位置（VenueRoot ローカル。お宝の位置と同じ座標系）</param>
        /// <param name="distance">〔ビームの距離〕</param>
        /// <param name="participantPlaying">自分が参加端末で Playing 中か</param>
        /// <returns>出すお宝の番号。出さないなら -1</returns>
        public int Poll(IReadOnlyList<ClientItem> items, Vector3 headLocal, float distance, bool participantPlaying)
        {
            if (_shown || !participantPlaying || items == null) return -1;
            float sq = distance * distance;
            foreach (var it in items)
            {
                if (it.Kind != TreasureKind.M || it.Gone || it.State != ItemState.Resting || it.OwnerClientNo >= 0 || it.Mine) continue;
                float dx = it.RestPos.x - headLocal.x, dz = it.RestPos.z - headLocal.z;
                if (dx * dx + dz * dz > sq) continue;
                _shown = true;
                return it.Index;
            }
            return -1;
        }
    }
}
