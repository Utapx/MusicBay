using Common;
using NUnit.Framework;

namespace UnitTestCommon
{
    [TestFixture]
    public class VersionCompareTests
    {
        [TestCase("v0.2.0", "0.1.0", true)]
        [TestCase("0.1.1", "0.1.0", true)]
        [TestCase("v1.0.0", "0.9.9", true)]
        [TestCase("v0.10.0", "0.9.0", true)] // numeric, not alphabetical
        [TestCase("v0.1.0", "0.1.0", false)]
        [TestCase("v0.1", "0.1.0", false)]
        [TestCase("v0.1.0", "0.2.0", false)]
        [TestCase("beta", "0.1.0", false)]
        [TestCase("v0.2.0-beta", "0.1.0", false)]
        [TestCase("", "0.1.0", false)]
        [TestCase(null, "0.1.0", false)]
        [TestCase("v0.2.0", "", false)]
        public void IsNewer(string? release, string? installed, bool expected)
        {
            Assert.That(VersionCompare.IsNewer(release, installed), Is.EqualTo(expected));
        }
    }
}
