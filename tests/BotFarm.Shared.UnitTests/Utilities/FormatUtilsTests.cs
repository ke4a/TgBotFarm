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
}
