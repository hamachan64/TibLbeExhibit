using System;

namespace STYLY.TIB_Template.TreasureHaul
{
    /// <summary>頭について来る面に出す内容の種類</summary>
    public enum ThPanelKind
    {
        /// <summary>何も出さない（練習用お宝が割り当てられていない端末の練習中・次の回を待っている間）</summary>
        None,
        Waiting,
        Opening,
        Playing,
        Result,
        /// <summary>練習の案内（#43。末尾に足した）</summary>
        Practice,
    }

    /// <summary>面の判断への入力（端末の追従 <see cref="ClientRoundTracker"/> と合計額から作る）</summary>
    public struct ThPanelInput
    {
        public RoundState State;
        public long RoundId;
        /// <summary>回の途中から入って次の回を待っている</summary>
        public bool WaitNextRound;
        public float Remaining;
        public float OpeningSeconds;
        public int Target;
        public int Total;
        public ThResultMsg Result;
        /// <summary>自分の練習用お宝の状態（Tutorial のとき。無い・割り当てが無い端末は None で、案内を出さない）</summary>
        public PracticeItemState Practice;
        /// <summary>練習用お宝の価値（「+150G！」に出す）</summary>
        public int PracticeValue;
    }

    /// <summary>面に出す内容と、この 1 回の更新で起きた合図</summary>
    public struct ThPanelFrame
    {
        public ThPanelKind Kind;
        /// <summary>オープニングの字幕の番号（1〜5。それ以外は 0）</summary>
        public int OpeningLine;
        public int RemainMinutes;
        public int RemainSeconds;
        public int Target;
        public int Total;
        /// <summary>「目標達成！」を大きく出している間</summary>
        public bool ShowTargetReached;
        public ThResultMsg Result;
        /// <summary>練習の案内の段階（Kind が Practice のとき）</summary>
        public PracticeStep PracticeStep;
        public int PracticeValue;

        // ---- 合図（その更新だけ true / 非 0） ----
        /// <summary>オープニングが始まった（効果音を鳴らす）</summary>
        public bool OpeningStarted;
        /// <summary>新しい字幕が始まった番号（1〜5。無ければ 0。その文のナレーションを鳴らす）</summary>
        public int LineStarted;
        /// <summary>目標額に届いた瞬間（音を鳴らす）</summary>
        public bool TargetReachedNow;
    }

    /// <summary>
    /// 頭について来る面の判断（純粋クラス。時刻は引数）。状態・残り秒・合計額・目標額・結果から、
    /// 出す内容・字幕の番号・音を鳴らす合図・目標達成の合図を返す。MonoBehaviour（<see cref="ThPlayerPanel"/>）は見た目と音だけ。
    /// </summary>
    public sealed class ThPanelPresenter
    {
        public const int OpeningLineCount = 5;
        /// <summary>「目標達成！」を大きく出す秒数（見た目の調整値）</summary>
        public const float TargetReachedSeconds = 3f;

        long _roundId = -1;
        bool _openingNotified;
        int _lastLine;
        bool _belowSeen;
        bool _reached;
        double _reachedAt;
        readonly PracticeGuide _guide = new PracticeGuide();

        /// <summary>
        /// 残り秒を「分」と「秒」にする。<b>切り上げ</b>（135.0 → 2:15、134.2 → 2:15、0.1 → 0:01、0 → 0:00）。
        /// 本編の開始直後が 3:00 と出て、0:00 になるのは本当に 0 のときだけ。運営UIの残り秒（CeilToInt）とそろえる。
        /// </summary>
        public static void SplitRemaining(float remaining, out int minutes, out int seconds)
        {
            int total = Math.Max(0, (int)Math.Ceiling(Math.Max(0f, remaining) - 1e-4));
            minutes = total / 60;
            seconds = total % 60;
        }

        /// <summary>オープニングの字幕の番号（1〜5）。〔オープニング〕を 5 等分して 1 枚ずつ。</summary>
        public static int OpeningLineIndex(float remaining, float openingSeconds)
        {
            if (openingSeconds <= 0f) return 1;
            float elapsed = Math.Min(Math.Max(0f, openingSeconds - remaining), openingSeconds);
            int i = (int)(elapsed / (openingSeconds / OpeningLineCount)) + 1;
            return Math.Min(Math.Max(i, 1), OpeningLineCount);
        }

        public ThPanelFrame Update(ThPanelInput inp, double now)
        {
            var f = new ThPanelFrame { Kind = ThPanelKind.None, Target = inp.Target, Total = inp.Total };

            // 待機に戻った・回が変わったら、その回の記憶を捨てる
            if (inp.State == RoundState.Waiting || inp.RoundId != _roundId)
            {
                _roundId = inp.State == RoundState.Waiting ? -1 : inp.RoundId;
                _openingNotified = false;
                _lastLine = 0;
                _belowSeen = false;
                _reached = false;
                _guide.Reset();
            }

            if (inp.State == RoundState.Waiting)
            {
                // ホスト途絶で自分だけ待機に戻った端末（WaitNextRound）は何も出さない。ホストの本当の Waiting を受けて外れたら待機の面を出す
                f.Kind = inp.WaitNextRound ? ThPanelKind.None : ThPanelKind.Waiting;
                return f;
            }
            // 版の不一致などで参加端末から外れた端末（WaitNextRound=false）にも面と音を出すのは、見学者向けにわざとそうしている
            if (inp.WaitNextRound) return f;   // 次の回を待っている端末は、Opening〜Result の間は何も出さない

            switch (inp.State)
            {
                case RoundState.Opening:
                    f.Kind = ThPanelKind.Opening;
                    // 時間合わせで残り秒が少し戻っても、字幕を戻さない（同じ文のナレーションを 2 回鳴らさない）
                    f.OpeningLine = Math.Max(_lastLine, OpeningLineIndex(inp.Remaining, inp.OpeningSeconds));
                    if (!_openingNotified) { _openingNotified = true; f.OpeningStarted = true; }
                    if (f.OpeningLine > _lastLine) { _lastLine = f.OpeningLine; f.LineStarted = f.OpeningLine; }
                    break;

                case RoundState.Playing:
                    f.Kind = ThPanelKind.Playing;
                    SplitRemaining(inp.Remaining, out f.RemainMinutes, out f.RemainSeconds);
                    // 目標達成の合図: 目標額を下回っている値を見たあとで、初めて目標額以上になった瞬間に 1 回だけ。
                    // 既に届いていた値を後から知った場合（途中参加・再接続）は出さない。目標額が未着（0）の間は判断しない。
                    if (inp.Target > 0)
                    {
                        if (inp.Total < inp.Target) _belowSeen = true;
                        else if (_belowSeen && !_reached)
                        {
                            _reached = true;
                            _reachedAt = now;
                            f.TargetReachedNow = true;
                        }
                    }
                    f.ShowTargetReached = _reached && now - _reachedAt < TargetReachedSeconds;
                    break;

                case RoundState.Result:
                    // 結果に入った後は目標達成を出さない。結果が無い（古いホスト）なら何も出さない
                    if (inp.Result != null && inp.Result.present)
                    {
                        f.Kind = ThPanelKind.Result;
                        f.Result = inp.Result;
                    }
                    break;

                case RoundState.Tutorial:
                    // 練習の案内: 自分の練習用お宝がある端末だけ。割り当てのない端末は何も出さない
                    f.PracticeStep = _guide.Update(inp.Practice, now);
                    f.PracticeValue = inp.PracticeValue;
                    if (f.PracticeStep != PracticeStep.None) f.Kind = ThPanelKind.Practice;
                    break;
            }
            return f;
        }

        /// <summary>面に出す文字（ThTextTable から。判断のあとの組み立てだけ）</summary>
        public static ThPanelTexts Compose(in ThPanelFrame f, ThTextTable t)
        {
            var r = new ThPanelTexts();
            switch (f.Kind)
            {
                case ThPanelKind.Waiting:
                    r.Main = t.Get(ThTextId.GoalSentence);
                    // 持ち方の設定が Beam 以外でも、今は GripBeam を出す（Direct・Swing の文言は追加要素 #4 で足す）
                    r.Sub = t.Get(ThTextId.GripBeam);
                    r.Credit = t.Get(ThTextId.Credits);
                    break;
                case ThPanelKind.Opening:
                    r.Main = t.Get(ThTextId.OpeningLine1 + (Math.Min(Math.Max(f.OpeningLine, 1), OpeningLineCount) - 1));
                    break;
                case ThPanelKind.Playing:
                    if (f.ShowTargetReached) { r.Main = t.Get(ThTextId.TargetReached); r.Large = true; }
                    else r.Main = t.Format(ThTextId.HudPlayingFormat, f.RemainMinutes, f.RemainSeconds, f.Target);
                    break;
                case ThPanelKind.Practice:
                    switch (f.PracticeStep)
                    {
                        case PracticeStep.Grip: r.Main = t.Get(ThTextId.PracticeGrip); break;
                        case PracticeStep.Carry: r.Main = t.Get(ThTextId.PracticeCarry); break;
                        case PracticeStep.Release: r.Main = t.Get(ThTextId.PracticeRelease); break;
                        case PracticeStep.Success: r.Main = t.Format(ThTextId.PracticeSuccessFormat, f.PracticeValue); r.Large = true; break;
                        case PracticeStep.DamageNote: r.Main = t.Get(ThTextId.PracticeDamageNote); break;
                    }
                    break;
                case ThPanelKind.Result:
                    r.Main = t.Format(ThTextId.ResultAmountFormat, f.Result.collected, f.Result.target);
                    r.Sub = f.Result.cleared
                        ? t.Get(ThTextId.ResultClear)
                        : t.Format(ThTextId.ResultFailFormat, Math.Max(0, f.Result.target - f.Result.collected));
                    break;
            }
            return r;
        }

        /// <summary>音の ID（字幕の番号 1〜5 から）</summary>
        public static ThSoundId LineSound(int line) => ThSoundId.OpeningLine1 + (Math.Min(Math.Max(line, 1), OpeningLineCount) - 1);
    }

    /// <summary>面に出す文字</summary>
    public struct ThPanelTexts
    {
        public string Main;
        public string Sub;
        public string Credit;
        /// <summary>大きく出す（目標達成）</summary>
        public bool Large;
    }
}
