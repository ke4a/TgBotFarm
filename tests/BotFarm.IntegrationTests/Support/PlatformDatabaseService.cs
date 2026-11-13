using BotFarm.Core.Abstractions;
using BotFarm.Core.Base;
using BotFarm.Core.Models;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BotFarm.IntegrationTests.Support;

/// <summary>
/// A bot database service with nothing of its own, for testing the storage every bot inherits from
/// <see cref="MongoDbDatabaseService"/> without depending on any real bot.
/// </summary>
public sealed class PlatformDatabaseService(BotIdentity identity, HybridCache cache)
    : MongoDbDatabaseService(
        identity,
        MongoServer.Client,
        NullLogger<MongoDbDatabaseService>.Instance,
        Substitute.For<INotificationService>(),
        cache)
{
    /// <summary>
    /// Creates the service on a database of its own.
    /// </summary>
    public static BotDatabaseScope<PlatformDatabaseService> CreateScope()
    {
        return BotDatabaseScope.Create((identity, cache) => new PlatformDatabaseService(identity, cache));
    }
}
