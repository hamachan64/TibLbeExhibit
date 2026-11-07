using System;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// 7 セグメント表示の純関数（表示板の合計額）。文字 → セグメントのビット、合計額 → 5 桁の桁分け。
    /// セグメントのビット: 0=a（上）1=b（右上）2=c（右下）3=d（下）4=e（左下）5=f（左上）6=g（中）。
    /// </summary>
    public static class SevenSegment
    {
        public const int DigitCount = 5;
        public const int SegmentCount = 7;
        public const int MaxTotal = 99999;
        /// <summary>千の位の区切り（,）を点ける最小の額</summary>
        public const int CommaThreshold = 1000;

        const int A = 1, B = 2, C = 4, D = 8, E = 16, F = 32, G = 64;

        static readonly int[] DigitMasks =
        {
            A | B | C | D | E | F,      // 0
            B | C,                      // 1
            A | B | G | E | D,          // 2
            A | B | G | C | D,          // 3
            F | G | B | C,              // 4
            A | F | G | C | D,          // 5
            A | F | G | E | D | C,      // 6
            A | B | C,                  // 7
            A | B | C | D | E | F | G,  // 8
            A | B | C | D | F | G,      // 9
        };

        /// <summary>文字のセグメントのビット。'0'〜'9'・'G'・空白に対応。それ以外は 0（全消灯）。</summary>
        public static int Mask(char c)
        {
            if (c >= '0' && c <= '9') return DigitMasks[c - '0'];
            if (c == 'G' || c == 'g') return A | F | E | D | C;
            return 0;
        }

        /// <summary>表示できる範囲（0〜99,999）に収める。負の数は 0、99,999 を超えたら 99,999。</summary>
        public static int Clamp(int total) => Math.Max(0, Math.Min(MaxTotal, total));

        /// <summary>5 桁の文字（右詰め）。上の桁の 0 は空白（消灯）。0 のときは一の位に '0'。</summary>
        public static char[] Digits(int total)
        {
            int v = Clamp(total);
            var result = new char[DigitCount];
            for (int i = DigitCount - 1; i >= 0; i--)
            {
                bool leading = i != DigitCount - 1 && v == 0;
                result[i] = leading ? ' ' : (char)('0' + v % 10);
                v /= 10;
            }
            return result;
        }

        /// <summary>千の位の区切り（,）を点灯するか（1,000 以上）</summary>
        public static bool CommaLit(int total) => Clamp(total) >= CommaThreshold;
    }
}
