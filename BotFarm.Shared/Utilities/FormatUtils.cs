using System.Globalization;
using Microsoft.Extensions.Localization;

namespace BotFarm.Shared.Utilities;

/// <summary>
/// Formatting helpers used by the dashboard UI.
/// </summary>
public static class FormatUtils
{
    private static readonly string[] UnitKeys =
    [
        "Units.Bytes", "Units.KiB", "Units.MiB", "Units.GiB", "Units.TiB", "Units.PiB", "Units.EiB", "Units.ZiB",
        "Units.YiB",
    ];

    /// <summary>
    /// Formats a byte count into a human-readable IEC size string, such as <c>1.5 KiB</c>. The number is formatted
    /// with <see cref="CultureInfo.CurrentCulture"/>, and the unit labels come from <paramref name="units"/>.
    /// </summary>
    /// <param name="bytes">The byte count, or null when it is unknown.</param>
    /// <param name="decimals">The number of decimal places to round to; negative values mean none.</param>
    /// <param name="units">
    /// A localizer over the <c>BotFarm.Shared</c> set, which holds the <c>Units.*</c> labels and
    /// <c>Units.NotAvailable</c>, the text returned for a null count.
    /// </param>
    public static string FormatBytes(double? bytes, int decimals, IStringLocalizer units)
    {
        ArgumentNullException.ThrowIfNull(units);

        if (bytes == null)
        {
            return units["Units.NotAvailable"];
        }
        if (bytes == 0)
        {
            return $"0 {units["Units.Bytes"]}";
        }

        const double k = 1024d;
        var dm = decimals < 0 ? 0 : decimals;

        var i = bytes.Value < 1
            ? 0
            : Math.Min((int)Math.Floor(Math.Log(bytes.Value) / Math.Log(k)), UnitKeys.Length - 1);
        var value = Math.Round(bytes.Value / Math.Pow(k, i), dm);
        return $"{value.ToString(CultureInfo.CurrentCulture)} {units[UnitKeys[i]]}";
    }

    /// <summary>
    /// Shortens <paramref name="text"/> to at most <paramref name="maxLength"/> characters followed by an ellipsis.
    /// Counts user-perceived characters, so emoji and other multi-part characters are never split.
    /// </summary>
    /// <returns>The original text when it fits; otherwise the shortened text ending in "…".</returns>
    public static string Truncate(string text, int maxLength)
    {
        var info = new StringInfo(text);
        if (info.LengthInTextElements <= maxLength)
        {
            return text;
        }

        return info.SubstringByTextElements(0, maxLength).TrimEnd() + "…";
    }
}
