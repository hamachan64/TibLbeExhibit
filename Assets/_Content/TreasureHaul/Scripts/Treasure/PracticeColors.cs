using System;
using System.Collections.Generic;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 練習の色の割り当て（設計書 3.4。純粋クラス）。ホストが Opening → Tutorial の時点の参加端末に色番号 0〜3 を割り当てる。
    /// 並べ方は <see cref="Order"/>（既定は DeviceId の文字列順）か、呼び出し側が渡す比較（本番は端末番号の順。<see cref="ThDeviceNumbers.Compare"/>）。
    /// </summary>
    public static class PracticeColors
    {
        /// <summary>色を割り当てる並びの既定: DeviceId の文字列順</summary>
        static int Order(string a, string b) => string.CompareOrdinal(a, b);

        /// <summary>
        /// DeviceId → 色番号。<paramref name="order"/>（null なら DeviceId の文字列順）で小さい順に 0〜（人数−1）。
        /// <see cref="ThPractice.MaxColors"/> を超える端末には色を付けない
        /// </summary>
        public static Dictionary<string, int> Assign(IEnumerable<string> deviceIds, Comparison<string> order = null)
        {
            var list = new List<string>();
            if (deviceIds != null) foreach (var id in deviceIds) if (!string.IsNullOrEmpty(id) && !list.Contains(id)) list.Add(id);
            list.Sort(order ?? Order);
            var map = new Dictionary<string, int>();
            for (int i = 0; i < list.Count && i < ThPractice.MaxColors; i++) map[list[i]] = i;
            return map;
        }

        /// <summary>ThState.tutorialColors（participants と同じ順で色番号。割り当てのない端末は -1）。割り当てが無ければ空</summary>
        public static int[] ToArray(IReadOnlyList<string> participants, IReadOnlyDictionary<string, int> colors)
        {
            if (colors == null || colors.Count == 0 || participants == null) return new int[0];
            var arr = new int[participants.Count];
            for (int i = 0; i < arr.Length; i++) arr[i] = colors.TryGetValue(participants[i], out int c) ? c : -1;
            return arr;
        }

        /// <summary>ThState の participants と tutorialColors から DeviceId → 色番号を読む（-1・範囲外・長さ不足は色なし）</summary>
        public static Dictionary<string, int> Read(string[] participants, int[] colors)
        {
            var map = new Dictionary<string, int>();
            if (participants == null || colors == null) return map;
            for (int i = 0; i < participants.Length && i < colors.Length; i++)
                if (colors[i] >= 0 && colors[i] < ThPractice.MaxColors && !string.IsNullOrEmpty(participants[i])) map[participants[i]] = colors[i];
            return map;
        }
    }
}
