using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using MongoDB.Driver;

namespace BotFarm.Core.Services;

/// <summary>
/// Stores each bot's settings in the platform control database.
/// </summary>
/// <remarks>
/// The sparse unique index on <see cref="BotSettings.TelegramBotId"/> is created on first use, so no two bots store
/// the same Telegram bot while any number of bots store no token.
/// </remarks>
public sealed class MongoBotSettingsStore : IBotSettingsStore
{
    public const string DatabaseName = MongoBotControlStateStore.DatabaseName;
    public const string SettingsCollectionName = "BotSettings";

    private readonly Func<IMongoDatabase> _databaseFactory;
    private readonly Lock _databaseLock = new();
    private IMongoDatabase? _database;
    private volatile bool _indexCreated;

    public MongoBotSettingsStore(IMongoDatabase database)
        : this(() => database)
    {
    }

    public MongoBotSettingsStore(Func<IMongoDatabase> databaseFactory)
    {
        ArgumentNullException.ThrowIfNull(databaseFactory);
        _databaseFactory = databaseFactory;
    }

    private IMongoDatabase Database
    {
        get
        {
            lock (_databaseLock)
            {
                return _database ??= _databaseFactory();
            }
        }
    }

    public async Task<BotSettings?> GetAsync(string botName, CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        return await settings.Find(ById(botName)).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task UpsertAsync(BotSettings settings, CancellationToken cancellationToken = default)
    {
        var collection = await GetSettingsAsync(cancellationToken);
        await collection.ReplaceOneAsync(
            ById(settings.BotName),
            settings,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    public async Task<string?> FindBotNameByTelegramBotIdAsync(
        long telegramBotId,
        CancellationToken cancellationToken = default)
    {
        var collection = await GetSettingsAsync(cancellationToken);
        return await collection
            .Find(Builders<BotSettings>.Filter.Eq(settings => settings.TelegramBotId, telegramBotId))
            .Project(settings => settings.BotName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static FilterDefinition<BotSettings> ById(string botName) =>
        Builders<BotSettings>.Filter.Eq(settings => settings.BotName, botName);

    private async Task<IMongoCollection<BotSettings>> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var collection = Database.GetCollection<BotSettings>(SettingsCollectionName);
        if (_indexCreated)
        {
            return collection;
        }

        // Creating an index that already exists with the same options does nothing.
        await collection.Indexes.CreateOneAsync(
            new CreateIndexModel<BotSettings>(
                Builders<BotSettings>.IndexKeys.Ascending(settings => settings.TelegramBotId),
                new CreateIndexOptions { Unique = true, Sparse = true }),
            cancellationToken: cancellationToken);
        _indexCreated = true;

        return collection;
    }
}
