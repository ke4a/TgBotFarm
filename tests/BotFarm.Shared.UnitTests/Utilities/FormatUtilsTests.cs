using BotFarm.Shared.Utilities;

namespace BotFarm.Shared.UnitTests.Utilities;

[TestFixture]
public class FormatUtilsTests
{
    [Test]
    public void FormatBytes_WithZero_ReturnsZeroBytes()
    {
        var result = FormatUtils.FormatBytes(0);

        Assert.That(result, Is.EqualTo("0 Bytes"));
    }

    [TestCase(1024, "1 KiB")]
    [TestCase(1536, "1.5 KiB")]
    [TestCase(1048576, "1 MiB")]
    public void FormatBytes_WithPositiveValues_ReturnsHumanReadableString(long bytes, string expected)
    {
        var result = FormatUtils.FormatBytes(bytes);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void FormatBytes_WithNegativeDecimals_UsesZeroDecimals()
    {
        var result = FormatUtils.FormatBytes(1536, -1);

        Assert.That(result, Is.EqualTo("2 KiB"));
    }

    [Test]
    public void FormatBytes_WithNull_ReturnsNotAvailable()
    {
        Assert.That(FormatUtils.FormatBytes(null), Is.EqualTo("N/A"));
    }

    [TestCase(1, "1 Bytes")]
    [TestCase(1023, "1023 Bytes")]
    [TestCase(0.5, "0.5 Bytes")]
    [TestCase(-5, "-5 Bytes")]
    public void FormatBytes_WithSmallOrNegativeValues_DoesNotThrowAndUsesBytes(double bytes, string expected)
    {
        Assert.That(FormatUtils.FormatBytes(bytes), Is.EqualTo(expected));
    }

    [Test]
    public void FormatBytes_WithValueBeyondLargestUnit_ClampsToLargestUnit()
    {
        Assert.That(FormatUtils.FormatBytes(double.MaxValue), Does.EndWith(" YiB"));
    }

    [Test]
    public void FormatBytes_WithCustomDecimals_RoundsToRequestedPrecision()
    {
        Assert.That(FormatUtils.FormatBytes(1234567, 3), Is.EqualTo("1.177 MiB"));
    }
}
