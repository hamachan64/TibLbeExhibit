using NUnit.Framework;
using STYLY.TIB_Template.TreasureHaul;

public class ThTitleLogoTests
{
    [TestCase(RoundState.Waiting, true)]
    [TestCase(RoundState.Opening, false)]
    [TestCase(RoundState.Tutorial, false)]
    [TestCase(RoundState.Playing, false)]
    [TestCase(RoundState.Result, false)]
    public void ShowVenueLogo_OnlyWaiting(RoundState s, bool expected) =>
        Assert.AreEqual(expected, ThTitleLogo.ShowVenueLogo(s));

    [Test]
    public void StartCue_WaitingToOpening_Only()
    {
        Assert.IsTrue(ThTitleLogo.IsStartCue(RoundState.Waiting, RoundState.Opening, 10f, 10f));
        Assert.IsFalse(ThTitleLogo.IsStartCue(RoundState.Waiting, RoundState.Waiting, 10f, 10f));
        Assert.IsFalse(ThTitleLogo.IsStartCue(RoundState.Opening, RoundState.Opening, 10f, 10f));
        foreach (var p in new[] { RoundState.Tutorial, RoundState.Playing, RoundState.Result, RoundState.Opening })
            Assert.IsFalse(ThTitleLogo.IsStartCue(p, RoundState.Opening, 10f, 10f), p.ToString());
        Assert.IsFalse(ThTitleLogo.IsStartCue(RoundState.Waiting, RoundState.Tutorial, 10f, 10f));
    }

    [Test]
    public void StartCue_FirstKnownStateOpening_IsNotCue() =>
        Assert.IsFalse(ThTitleLogo.IsStartCue(null, RoundState.Opening, 10f, 10f));

    [Test]
    public void Logic_InitialWaitingThenLateOpening_IsNotCue()
    {
        var l = new ThTitleLogoCueLogic();
        l.OnState(RoundState.Waiting, 0f, 10f);   // 購読直後の初期値
        var a = l.OnState(RoundState.Opening, 6f, 10f);
        Assert.IsFalse(a.PlayCue);
        Assert.IsFalse(a.HideFront);
    }

    [Test]
    public void Logic_WaitingThenFreshOpening_IsCue()
    {
        var l = new ThTitleLogoCueLogic();
        var w = l.OnState(RoundState.Waiting, 0f, 10f);
        Assert.IsTrue(w.ShowVenue); Assert.IsFalse(w.HideVenue);
        var a = l.OnState(RoundState.Opening, 9.8f, 10f);
        Assert.IsTrue(a.PlayCue); Assert.IsTrue(a.HideVenue);
    }

    [Test]
    public void Logic_AbortDuringOpening_HidesFrontAndShowsVenue()
    {
        var l = new ThTitleLogoCueLogic();
        l.OnState(RoundState.Waiting, 0f, 10f);
        l.OnState(RoundState.Opening, 9.8f, 10f);
        var a = l.OnState(RoundState.Waiting, 0f, 10f);
        Assert.IsTrue(a.HideFront); Assert.IsTrue(a.ShowVenue); Assert.IsFalse(a.PlayCue);
    }

    [Test]
    public void Logic_FirstKnownOpening_IsNotCue() =>
        Assert.IsFalse(new ThTitleLogoCueLogic().OnState(RoundState.Opening, 10f, 10f).PlayCue);
}
