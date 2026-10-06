using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using MongoDB.Driver;

namespace BotFarm.Core.Services;

/// <summary>
/// Stores platform bot-control records in the dedicated control database.
/// </summary>
public sealed class MongoBotControlStateStore : IBotControlStateStore
{
    public const string DatabaseName = "BotFarmControl";
    public const string StateCollectionName = "BotControlStates";

    private readonly Func<IMongoDatabase> _databaseFactory;
    private readonly Lock _databaseLock = new();
    private IMongoDatabase? _database;

    public MongoBotControlStateStore(IMongoDatabase database)
        : this(() => database)
    {
    }

    public MongoBotControlStateStore(Func<IMongoDatabase> databaseFactory)
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

    private IMongoCollection<BotControlState> States => Database.GetCollection<BotControlState>(StateCollectionName);

    public async Task<BotControlState?> GetAsync(string botName, CancellationToken cancellationToken = default)
    {
        var filter = Builders<BotControlState>.Filter.Eq(state => state.BotName, botName);
        return await States.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> TryInsertAsync(BotControlState state, CancellationToken cancellationToken = default)
    {
        try
        {
            await States.InsertOneAsync(state, cancellationToken: cancellationToken);
            return true;
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<BotControlState?> TrySetDesiredStateAsync(
        string botName,
        bool desiredEnabled,
        string commandId,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<BotControlState>.Filter.Eq(state => state.BotName, botName);
        var update = Builders<BotControlState>.Update
            .Set(state => state.DesiredEnabled, desiredEnabled)
            .Set(state => state.LastCommandId, commandId)
            .Set(state => state.UpdatedAtUtc, updatedAtUtc);

        return await States.FindOneAndUpdateAsync(
            filter,
            update,
            new FindOneAndUpdateOptions<BotControlState> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
    }
}