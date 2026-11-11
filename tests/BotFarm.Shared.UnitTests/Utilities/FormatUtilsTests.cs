using BotFarm.Shared.Utilities;
using BotFarm.TestKit;
using Microsoft.Extensions.Localization;

namespace BotFarm.Shared.UnitTests.Utilities;

[TestFixture]
public class FormatUtilsTests : AdminUiTestFixture
{
    private static readonly IStringLocalizer<SharedResources> Units =
        AdminLocalizationTestExtensions.CreateAdminLocalizer<SharedResources>();

    [Test]
    public void FormatBytes_WithZero_ReturnsZeroBytes()
    {
        var result = FormatUtils.FormatBytes(0, 2, Units);

        Assert.That(result, Is.EqualTo("0 Bytes"));
    }

    [TestCase(1024, "1 KiB")]
    [TestCase(1536, "1.5 KiB")]
    [TestCase(1048576, "1 MiB")]
    public void FormatBytes_WithPositiveValues_ReturnsHumanReadableString(long bytes, string expected)
    {
        var result = FormatUtils.FormatBytes(bytes, 2, Units);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void FormatBytes_WithNegativeDecimals_UsesZeroDecimals()
    {
        var result = FormatUtils.FormatBytes(1536, -1, Units);

        Assert.That(result, Is.EqualTo("2 KiB"));
    }

    [Test]
    public void FormatBytes_WithNull_ReturnsNotAvailable()
    {
        Assert.That(FormatUtils.FormatBytes(null, 2, Units), Is.EqualTo("N/A"));
    }

    [TestCase(1, "1 Bytes")]
    [TestCase(1023, "1023 Bytes")]
    [TestCase(0.5, "0.5 Bytes")]
    [TestCase(-5, "-5 Bytes")]
    public void FormatBytes_WithSmallOrNegativeValues_DoesNotThrowAndUsesBytes(double bytes, string expected)
    {
        Assert.That(FormatUtils.FormatBytes(bytes, 2, Units), Is.EqualTo(expected));
    }

    [Test]
    public void FormatBytes_WithValueBeyondLargestUnit_ClampsToLargestUnit()
    {
        Assert.That(FormatUtils.FormatBytes(double.MaxValue, 2, Units), Does.EndWith(" YiB"));
    }

    [Test]
    public void FormatBytes_WithCustomDecimals_RoundsToRequestedPrecision()
    {
        Assert.That(FormatUtils.FormatBytes(1234567, 3, Units), Is.EqualTo("1.177 MiB"));
    }

    [Test]
    public void FormatBytes_InEnglish_UsesDecimalPointAndEnglishUnit()
    {
        Assert.That(FormatUtils.FormatBytes(1536, 1, Units), Is.EqualTo("1.5 KiB"));
    }

    [Test]
    public void FormatBytes_InRussian_UsesDecimalCommaAndRussianUnit()
    {
        using var _ = new CultureScope("ru-RU");

        Assert.That(FormatUtils.FormatBytes(1536, 1, Units), Is.EqualTo("1,5 КиБ"));
    }

    [Test]
    public void FormatBytes_InUkrainian_UsesUkrainianUnit()
    {
        using var _ = new CultureScope("uk-UA");

        Assert.That(FormatUtils.FormatBytes(1536, 1, Units), Is.EqualTo("1,5 КіБ"));
    }

    [Test]
    public void FormatBytes_WithNullInRussian_ReturnsLocalizedNotAvailable()
    {
        using var _ = new CultureScope("ru-RU");

        Assert.That(FormatUtils.FormatBytes(null, 2, Units), Is.EqualTo("н/д"));
    }

    [TestCase("")]
    [TestCase("Short name")]
    [TestCase("12345")]
    public void Truncate_WhenTextFits_ReturnsTextUnchanged(string text)
    {
        Assert.That(FormatUtils.Truncate(text, 15), Is.EqualTo(text));
    }

    [Test]
    public void Truncate_WhenTextIsExactlyMaxLength_ReturnsTextUnchanged()
    {
        Assert.That(FormatUtils.Truncate("abcde", 5), Is.EqualTo("abcde"));
    }

    [Test]
    public void Truncate_WhenTextIsLonger_CutsAtMaxLengthAndAddsEllipsis()
    {
        Assert.That(FormatUtils.Truncate("abcdefgh", 5), Is.EqualTo("abcde…"));
    }

    [Test]
    public void Truncate_WhenCutEndsInWhitespace_TrimsBeforeEllipsis()
    {
        Assert.That(FormatUtils.Truncate("abcd efgh", 5), Is.EqualTo("abcd…"));
    }

    [Test]
    public void Truncate_WithEmoji_CountsEachEmojiAsOneCharacterAndNeverSplitsIt()
    {
        Assert.That(FormatUtils.Truncate("👨‍👩‍👧‍👦🎉ab", 2), Is.EqualTo("👨‍👩‍👧‍👦🎉…"));
    }
}
