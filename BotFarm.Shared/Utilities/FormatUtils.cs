namespace BotFarm.Shared.Utilities;

public static class FormatUtils
{
    public static string FormatBytes(long bytes, int decimals = 2)
    {
        if (bytes == 0)
        {
            return "0 Bytes";
        }

        const double k = 1024d;
        var dm = decimals < 0 ? 0 : decimals;
        string[] sizes = ["Bytes", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB", "ZiB", "YiB"];

        var i = (int)Math.Floor(Math.Log(bytes) / Math.Log(k));
        return $"{Math.Round(bytes / Math.Pow(k, i), dm)} {sizes[i]}";
    }
}
