using System.Globalization;

namespace BotFarm.Shared.Utilities;

/// <summary>
/// Formatting helpers used by the dashboard UI.
/// </summary>
public static class FormatUtils
{
    /// <summary>
    /// Formats a byte count into a human-readable IEC size string.
    /// </summary>
    public static string FormatBytes(double? bytes, int decimals = 2)
    {
        if (bytes == null)
        {
            return "N/A";
        }
        if (bytes == 0)
        {
            return "0 Bytes";
        }

        const double k = 1024d;
        var dm = decimals < 0 ? 0 : decimals;
        string[] sizes = ["Bytes", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB", "ZiB", "YiB"];

        var i = bytes.Value < 1
            ? 0
            : Math.Min((int)Math.Floor(Math.Log(bytes.Value) / Math.Log(k)), sizes.Length - 1);
        var value = Math.Round(bytes.Value / Math.Pow(k, i), dm);
        return $"{value.ToString(CultureInfo.InvariantCulture)} {sizes[i]}";
    }
}
