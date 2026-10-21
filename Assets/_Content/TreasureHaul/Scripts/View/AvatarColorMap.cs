using System;
using System.Collections.Generic;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 仲間のアバターに塗る色の判断（純粋クラス）。ホストの割り当て（DeviceId → 色番号）と、DeviceId から今の ClientNo を引く関数から、
    /// ClientNo ごとの色番号を出す。ClientNo はサーバーの再起動などで入れ替わるので、毎回引き直す。
    /// </summary>
    public static class AvatarColorMap
    {
        /// <summary>
        /// その ClientNo のアバターの色番号（色が無ければ -1）。
        /// ownClientNo のアバター（自分）は塗らない（-1）。clientNoOf は DeviceId → 今の ClientNo（引けなければ 0 以下）。
        /// </summary>
        public static int ColorOfClient(IReadOnlyDictionary<string, int> deviceColors, Func<string, int> clientNoOf, int avatarClientNo, int ownClientNo)
        {
            if (deviceColors == null || clientNoOf == null || avatarClientNo <= 0 || avatarClientNo == ownClientNo) return -1;
            foreach (var kv in deviceColors)
                if (kv.Value >= 0 && clientNoOf(kv.Key) == avatarClientNo) return kv.Value;
            return -1;
        }
    }
}
