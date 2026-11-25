using System.Globalization;
using BotFarm.TestKit;

namespace BotFarm.Shared.UnitTests;

[TestFixture]
public class DisplayTimeZoneTests
{
    private static readonly TimeZoneInfo PlusTwo =
        TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");

    private readonly DisplayTimeZone _plusTwo = new(PlusTwo);

    [Test]
    public void Convert_UtcTime_MovesItToTheZone()
    {
        var utc = new DateTime(2026, 11, 25, 10, 30, 0, DateTimeKind.Utc);

        Assert.That(_plusTwo.Convert(utc), Is.EqualTo(new DateTime(2026, 11, 25, 12, 30, 0)));
    }

    [Test]
    public void Convert_UnspecifiedTime_IsTakenAsUtc()
    {
        var unspecified = new DateTime(2026, 11, 25, 10, 30, 0, DateTimeKind.Unspecified);

        Assert.That(_plusTwo.Convert(unspecified), Is.EqualTo(new DateTime(2026, 11, 25, 12, 30, 0)));
    }

    [Test]
    public void Convert_ServerLocalTime_IsConvertedFromTheServerZone()
    {
        var utc = new DateTime(2026, 11, 25, 10, 30, 0, DateTimeKind.Utc);

        Assert.That(_plusTwo.Convert(utc.ToLocalTime()), Is.EqualTo(new DateTime(2026, 11, 25, 12, 30, 0)));
    }

    [Test]
    public void FromId_IanaId_ReturnsThatZone()
    {
        var zone = DisplayTimeZone.FromId("Asia/Tokyo");

        Assert.That(zone.Zone.BaseUtcOffset, Is.EqualTo(TimeSpan.FromHours(9)));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("Not/A_Zone")]
    public void FromId_MissingOrUnknownId_ReturnsTheServerZone(string? timeZoneId)
    {
        Assert.That(DisplayTimeZone.FromId(timeZoneId), Is.SameAs(DisplayTimeZone.ServerLocal));
    }

    [Test]
    public void Format_UsesTheZoneAndTheCurrentCulture()
    {
        using var _ = new CultureScope("ru-RU");
        var utc = new DateTime(2026, 11, 25, 22, 30, 0, DateTimeKind.Utc);

        Assert.That(_plusTwo.Format(utc, "dd MMM yyyy HH:mm"), Is.EqualTo(
            new DateTime(2026, 11, 26, 0, 30, 0).ToString("dd MMM yyyy HH:mm", CultureInfo.GetCultureInfo("ru-RU"))));
    }
}
