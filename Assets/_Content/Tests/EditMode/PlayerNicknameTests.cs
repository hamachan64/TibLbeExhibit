using NUnit.Framework;

namespace STYLY.TIB_Template.Tests
{
    /// <summary>
    /// PlayerNickname.Format のフォーマット確認。
    /// </summary>
    public class PlayerNicknameTests
    {
        [TestCase(1, "ハンターNo.01")]
        [TestCase(9, "ハンターNo.09")]
        [TestCase(10, "ハンターNo.10")]
        [TestCase(23, "ハンターNo.23")]
        [TestCase(99, "ハンターNo.99")]
        public void Format_SingleAndDoubleDigit_ReturnsZeroPaddedName(int clientNo, string expected)
        {
            Assert.That(PlayerNickname.Format(clientNo), Is.EqualTo(expected));
        }

        [Test]
        public void Format_ThreeDigitClientNo_DoesNotTruncate()
        {
            // 3桁以上でも桁あふれせずそのまま表示される
            Assert.That(PlayerNickname.Format(123), Is.EqualTo("ハンターNo.123"));
        }

        [Test]
        public void Format_Zero_ReturnsZeroPadded()
        {
            Assert.That(PlayerNickname.Format(0), Is.EqualTo("ハンターNo.00"));
        }
    }
}
