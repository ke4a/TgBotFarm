using BotFarm.Core.Extensions;
using BotFarm.Core.Models;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Runtime.CompilerServices;

using BotFarm.Core.Abstractions;

namespace BotFarm.Core.Services;

/// <summary>
/// Holds the bot's MongoDB database handle and the low-level collection operations used by <see cref="MongoDbBackupService"/>
/// (stats, collection enumeration, drop/populate).
/// </summary>
internal sealed class MongoConnectionManager
{
    // The server error MongoDB returns for a collection that does not exist.
    private const int NamespaceNotFoundCode = 26;

    private readonly ILogger _logger;
    private readonly INotificationService _notificationService;
    private readonly string _name;
    private readonly string _logPrefix;
    private readonly string _databaseName;

    public MongoConnectionManager(
        IMongoClient client,
        ILogger logger,
        INotificationService notificationService,
        string name,
        string logPrefix,
        string databaseName)
    {
        Client = client;
        _logger = logger;
        _notificationService = notificationService;
        _name = name;
        _logPrefix = logPrefix;
        _databaseName = databaseName;
        Instance = client.GetDatabase(databaseName);
    }

    public IMongoClient Client { get; }

    public IMongoDatabase Instance { get; }

    public async Task<MongoDatabaseStats?> GetDatabaseStats()
    {
        return await TryExecute(
            async () =>
            {
                var statsDocument = await Instance.RunCommandAsync<BsonDocument>(new BsonDocument("dbStats", 1));
                return (MongoDatabaseStats?)MapStats(statsDocument);
            },
            $"Error getting database stats for '{_databaseName}'");
    }

    public async Task<IEnumerable<string>> GetCollectionNames(CancellationToken cancellationToken = default)
    {
        using var cursor = await Instance.ListCollectionNamesAsync(cancellationToken: cancellationToken);
        return await cursor.ToListAsync(cancellationToken);
    }

    public async IAsyncEnumerable<BsonDocument> GetCollectionData(
        string collectionName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var collection = Instance.GetCollection<BsonDocument>(collectionName);
        using var cursor = await collection.Find(Builders<BsonDocument>.Filter.Empty)
            .ToCursorAsync(cancellationToken);

        while (await cursor.MoveNextAsync(cancellationToken))
        {
            foreach (var document in cursor.Current)
            {
                yield return document;
            }
        }
    }

    public async Task<bool> DropCollection(string collectionName)
    {
        return await TryExecute(
            async () =>
            {
                await Instance.DropCollectionAsync(collectionName);
                return true;
            },
            $"Could not drop collection '{collectionName}'",
            fallback: false,
            notify: true);
    }

    public Task<bool> CreateCollection(string collectionName)
    {
        return TryExecute(
            async () =>
            {
                await Instance.CreateCollectionAsync(collectionName);
                return true;
            },
            $"Could not create collection '{collectionName}'",
            fallback: false,
            notify: true);
    }

    public async Task<bool> CreateAndPopulateCollection(string collectionName, IEnumerable<BsonDocument> data)
    {
        return await TryExecute(
            async () =>
            {
                var collection = Instance.GetCollection<BsonDocument>(collectionName);
                await collection.InsertManyAsync(data);
                return true;
            },
            $"Could not create and populate collection '{collectionName}'",
            fallback: false,
            notify: true);
    }

    public async Task<bool> RenameCollection(string sourceCollectionName, string targetCollectionName, bool dropTarget)
    {
        return await TryExecute(
            async () =>
            {
                await Instance.RenameCollectionAsync(
                    sourceCollectionName,
                    targetCollectionName,
                    new RenameCollectionOptions { DropTarget = dropTarget });
                return true;
            },
            $"Could not rename collection '{sourceCollectionName}' to '{targetCollectionName}'",
            fallback: false,
            notify: true);
    }

    public async Task<IReadOnlyList<BsonDocument>> GetIndexDefinitions(string collectionName)
    {
        List<BsonDocument> indexes;
        try
        {
            var collection = Instance.GetCollection<BsonDocument>(collectionName);
            using var cursor = await collection.Indexes.ListAsync();
            indexes = await cursor.ToListAsync();
        }
        catch (MongoCommandException exception) when (exception.Code == NamespaceNotFoundCode)
        {
            return [];
        }

        // "v" (index format version) and "ns" (namespace) describe where the index lived, not what it is,
        // so they are left out and the server fills them in when the index is created again.
        return indexes
            .Where(index => index.GetValue("name", string.Empty).AsString != "_id_")
            .Select(index =>
            {
                index.Remove("v");
                index.Remove("ns");
                return index;
            })
            .ToList();
    }

    public Task<bool> CreateIndexes(string collectionName, IReadOnlyList<BsonDocument> indexDefinitions)
    {
        return TryExecute(
            async () =>
            {
                await Instance.RunCommandAsync<BsonDocument>(new BsonDocument
                {
                    { "createIndexes", collectionName },
                    { "indexes", new BsonArray(indexDefinitions) }
                });
                return true;
            },
            $"Could not create indexes on collection '{collectionName}'",
            fallback: false,
            notify: true);
    }

    public Task<long> GetCollectionDocumentCount(string collectionName, bool estimated = true)
    {
        var collection = Instance.GetCollection<BsonDocument>(collectionName);
        return collection.GetDocumentCountAsync(estimated);
    }

    private async Task<T?> TryExecute<T>(
        Func<Task<T>> action,
        string errorContext,
        T? fallback = default,
        bool notify = false)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            var message = $"{_logPrefix} {errorContext}. Error: '{ex.Message}'";
            _logger.LogError(
                ex,
                "{LogPrefix} {ErrorContext}. Error: '{ErrorMessage}'",
                _logPrefix,
                errorContext,
                ex.Message);

            if (notify)
            {
                await _notificationService.SendErrorNotification(message, _name);
            }

            return fallback;
        }
    }

    private static MongoDatabaseStats MapStats(BsonDocument statsDocument)
    {
        var stats = new MongoDatabaseStats
        {
            DatabaseName = GetString(statsDocument, "db"),
            Collections = GetLong(statsDocument, "collections"),
            StorageSize = GetDouble(statsDocument, "storageSize"),
            Indexes = GetLong(statsDocument, "indexes"),
            IndexSize = GetDouble(statsDocument, "indexSize"),
            Ok = GetDouble(statsDocument, "ok")
        };
        stats.TotalSize = stats.StorageSize + stats.IndexSize;

        return stats;
    }

    private static string GetString(BsonDocument document, string name)
    {
        return document.TryGetValue(name, out var value) ? value.ToString() : string.Empty;
    }

    private static long GetLong(BsonDocument document, string name)
    {
        return document.TryGetValue(name, out var value) && value.IsNumeric ? value.ToInt64() : 0;
    }

    private static double GetDouble(BsonDocument document, string name)
    {
        return document.TryGetValue(name, out var value) && value.IsNumeric ? value.ToDouble() : 0;
    }
}
