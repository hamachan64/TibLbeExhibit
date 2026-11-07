namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>ロゴを今どうするか（<see cref="ThTitleLogoCueLogic.OnState"/> の結果）</summary>
    public readonly struct ThTitleLogoActions
    {
        public readonly bool ShowVenue, HideVenue, PlayCue, HideFront;
        public ThTitleLogoActions(bool showVenue, bool hideVenue, bool playCue, bool hideFront)
        { ShowVenue = showVenue; HideVenue = hideVenue; PlayCue = playCue; HideFront = hideFront; }
    }

    /// <summary>タイトルロゴの出し消しの判断（前の状態の保持を含む純粋クラス）。見た目と購読は <see cref="ThTitleLogoCue"/>。</summary>
    public sealed class ThTitleLogoCueLogic
    {
        RoundState? _prev;

        public void Reset() => _prev = null;

        public ThTitleLogoActions OnState(RoundState state, float remaining, float openingSeconds)
        {
            bool venue = ThTitleLogo.ShowVenueLogo(state);
            bool cue = ThTitleLogo.IsStartCue(_prev, state, remaining, openingSeconds);
            bool hideFront = !cue && state != RoundState.Opening;   // 出している途中で中断して Waiting に戻ったら、すぐ消す
            _prev = state;
            return new ThTitleLogoActions(venue, !venue, cue, hideFront);
        }
    }
}
