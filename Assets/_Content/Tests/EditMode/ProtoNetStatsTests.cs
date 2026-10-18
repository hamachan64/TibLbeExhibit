using NUnit.Framework;
using STYLY.TIB_Template.TreasureHaul.Prototype;

namespace STYLY.TIB_Template.TreasureHaul.Tests
{
    public class ProtoNetStatsTests
    {
        [Test]
        public void Lag_IsZero_WhenObservedEqualsExpected()
        {
            double e = ProtoNetStats.ExpectedPhase(12.3, 4.0);
            Assert.AreEqual(0.0, ProtoNetStats.LagSeconds(e, e, 4.0), 1e-9);
        }

        [Test]
        public void Lag_IsPositive_WhenObservedBehindExpected()
        {
            // 100ms 遅れた画面は、100ms 前の位相を表示している
            double now = ProtoNetStats.ExpectedPhase(10.0, 4.0);
            double seen = ProtoNetStats.ExpectedPhase(9.9, 4.0);
            Assert.AreEqual(0.1, ProtoNetStats.LagSeconds(now, seen, 4.0), 1e-9);
        }

        [Test]
        public void Lag_WrapsAroundPeriodBoundary()
        {
            double now = ProtoNetStats.ExpectedPhase(8.02, 4.0);   // 周期の頭を少し過ぎた
            double seen = ProtoNetStats.ExpectedPhase(7.95, 4.0);  // 直前の周期の終わり
            Assert.AreEqual(0.07, ProtoNetStats.LagSeconds(now, seen, 4.0), 1e-9);
        }

        [Test]
        public void Percentile_Interpolates()
        {
            var v = new[] { 10.0, 20.0, 30.0, 40.0 };
            Assert.AreEqual(25.0, ProtoNetStats.Percentile(v, 0.5), 1e-9);
            Assert.AreEqual(40.0, ProtoNetStats.Percentile(v, 1.0), 1e-9);
        }

        [Test]
        public void Fnv1a_DiffersForDifferentStrings()
        {
            Assert.AreNotEqual(ProtoNetStats.Fnv1a("abc"), ProtoNetStats.Fnv1a("abd"));
            Assert.AreEqual(ProtoNetStats.Fnv1a("abc"), ProtoNetStats.Fnv1a("abc"));
        }
    }
}
