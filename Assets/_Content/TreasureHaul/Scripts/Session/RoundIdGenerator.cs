namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// RoundId = 「開始」時の Unix ミリ秒。同じミリ秒・時計の巻き戻りでも、前の値より必ず増える（設計書 1章）。
    /// </summary>
    public static class RoundIdGenerator
    {
        public static long Next(long nowUnixMs, long previous) => nowUnixMs > previous ? nowUnixMs : previous + 1;
    }
}
