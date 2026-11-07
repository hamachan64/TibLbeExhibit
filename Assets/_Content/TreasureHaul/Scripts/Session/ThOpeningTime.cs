namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>
    /// オープニングの長さ（純粋関数）。ホスト（<see cref="HostRoundMachine"/> の Opening の長さ）と各端末（字幕・正面のロゴの判定）が
    /// 同じ式を使うので、必ずここを通す。亡霊を入れる回だけ、〔亡霊のオープニング延長〕だけ長くなる（企画書 4.2）。
    /// </summary>
    public static class ThOpeningTime
    {
        /// <summary>その回のオープニングの延長分（秒）。亡霊を入れない回は 0</summary>
        public static float ExtraSeconds(ThProgressParams progress, ThPhantomMode phantom) =>
            phantom != ThPhantomMode.Off && progress != null ? System.Math.Max(0f, progress.phantomOpeningExtraSeconds) : 0f;

        /// <summary>その回のオープニングの長さ（秒）= 基本 + 延長分</summary>
        public static float TotalSeconds(ThProgressParams progress, ThPhantomMode phantom) =>
            (progress != null ? progress.openingSeconds : 0f) + ExtraSeconds(progress, phantom);
    }
}
