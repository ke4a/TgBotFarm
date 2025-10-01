namespace BotFarm.Core.Extensions;

public static class MathExtensions
{
    public static long MapToLong(this ulong ulongValue)
    {
        return unchecked((long)ulongValue + long.MinValue);
    }

    public static ulong MapToUlong(this long longValue)
    {
        return unchecked((ulong)(longValue - long.MinValue));
    }
}
