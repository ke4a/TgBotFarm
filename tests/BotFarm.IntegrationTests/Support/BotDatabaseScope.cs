using BotFarm.Core.Base;
using BotFarm.Core.Models;
using BotFarm.TestKit;
using Microsoft.Extensions.Caching.Hybrid;
using MongoDB.Driver;

namespace BotFarm.IntegrationTests.Support;

/// <summary>
/// A bot database service on a database of its own on the test MongoDB server, which is dropped when the scope is
/// disposed. Lets tests run a bot's database code without starting the app.
/// </summary>
/// <example>
/// <code>
/// await using var scope = BotDatabaseScope.Create((identity, cache) =&gt; new WeatherBotDatabaseService(
///     identity, MongoServer.Client, NullLogger&lt;WeatherBotDatabaseService&gt;.Instance,
///     Substitute.For&lt;INotificationService&gt;(), cache));
/// </code>
/// </example>
public static class BotDatabaseScope
{
    /// <summary>
    /// Creates the service with <paramref name="createService"/>, giving it a bot identity with a name of its own, so
    /// its database is named after that, and an empty cache.
    /// </summary>
    public static BotDatabaseScope<TService> Create<TService>(Func<BotIdentity, HybridCache, TService> createService)
        where TService : MongoDbDatabaseService
    {
        var identity = new BotIdentity($"Bot{Guid.NewGuid():N}");
        var cache = HybridCacheScope.Create();

        return new BotDatabaseScope<TService>(createService(identity, cache.Cache), cache);
    }
}

/// <inheritdoc cref="BotDatabaseScope"/>
public sealed class BotDatabaseScope<TService> : IAsyncDisposable
    where TService : MongoDbDatabaseService
{
    private readonly HybridCacheScope _cache;

    internal BotDatabaseScope(TService service, HybridCacheScope cache)
    {
        Service = service;
        _cache = cache;
        Database = MongoServer.Client.GetDatabase(service.DatabaseName);
    }

    /// <summary>
    /// The bot database service under test.
    /// </summary>
    public TService Service { get; }

    /// <summary>
    /// The service's database, for reading and changing its data directly.
    /// </summary>
    public IMongoDatabase Database { get; }

    /// <summary>
    /// The bot name the service was given.
    /// </summary>
    public string BotName => Service.Name;

    /// <summary>
    /// The cache the service uses.
    /// </summary>
    public HybridCache Cache => _cache.Cache;

    public async ValueTask DisposeAsync()
    {
        await MongoServer.Client.DropDatabaseAsync(Service.DatabaseName);
        _cache.Dispose();
    }
}
