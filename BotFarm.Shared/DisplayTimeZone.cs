using System.Globalization;

namespace BotFarm.Shared;

/// <summary>
/// The time zone the dashboard shows times in: the viewer's browser time zone, or the server's own time zone until
/// the browser's is known. <see cref="MainLayout"/> cascades it to every page.
/// </summary>
public sealed class DisplayTimeZone
{
    /// <summary>
    /// Shows times in the server's time zone.
    /// </summary>
    public static DisplayTimeZone ServerLocal { get; } = new(TimeZoneInfo.Local);

    public DisplayTimeZone(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        Zone = zone;
    }

    /// <summary>
    /// The time zone times are converted to.
    /// </summary>
    public TimeZoneInfo Zone { get; }

    /// <summary>
    /// The time zone with the IANA or Windows ID <paramref name="timeZoneId"/>, or <see cref="ServerLocal"/> when the
    /// ID is empty or unknown.
    /// </summary>
    public static DisplayTimeZone FromId(string? timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone)
            ? new DisplayTimeZone(zone)
            : ServerLocal;

    /// <summary>
    /// Converts <paramref name="value"/> to this time zone. A value of unspecified kind is taken as UTC, as
    /// <see cref="DateTime.ToLocalTime"/> does.
    /// </summary>
    public DateTime Convert(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Local
            ? value.ToUniversalTime()
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);
    }

    /// <summary>
    /// Converts <paramref name="value"/> to this time zone and formats it with <paramref name="format"/> in the
    /// current culture.
    /// </summary>
    public string Format(DateTime value, string format) =>
        Convert(value).ToString(format, CultureInfo.CurrentCulture);
}
