using Microsoft.Extensions.Caching.Hybrid;

namespace BotFarm.Core.Services;

/// <summary>
/// Cache settings for the statistics shown on a bot's dashboard, which do not need to be current to the second.
/// </summary>
internal static class StatsCache
{
    /// <summary>
    /// How long cached statistics are kept before they are read from the database again.
    /// </summary>
    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Entry options that expire cached statistics after <see cref="Duration"/>.
    /// </summary>
    public static readonly HybridCacheEntryOptions EntryOptions = new()
    {
        Expiration = Duration,
        LocalCacheExpiration = Duration
    };
}
