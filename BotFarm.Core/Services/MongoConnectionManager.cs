using BotFarm.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

using BotFarm.Core.Abstractions;

namespace BotFarm.Core.Services;

/// <summary>
/// Owns the MongoDB connection lifecycle (connect/reconnect/disconnect) and the
/// low-level collection operations used by <see cref="MongoDbBackupService"/>
/// (stats, collection enumeration, drop/populate).
/// </summary>
internal sealed class MongoConnectionManager
{
    private readonly ILogger _logger;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly INotificationService _notificationService;
    private readonly string _name;
    private readonly string _logPrefix;
    private readonly string _databaseName;

    public MongoConnectionManager(
        MongoClient client,
        ILogger logger,
        IHostApplicationLifetime appLifetime,
        INotificationService notificationService,
        string name,
        string logPrefix,
        string databaseName)
    {
        Client = client;
        _logger = logger;
        _appLifetime = appLifetime;
        _notificationService = notificationService;
        _name = name;
        _logPrefix = logPrefix;
        _databaseName = databaseName;
    }

    public MongoClient Client { get; }

    public IMongoDatabase Instance { get; set; } = null!;

    public Task<bool> Reconnect()
    {
        return TryAsync(
            async () =>
            {
                Instance = Client.GetDatabase(_databaseName);

                // Test the connection
                await Instance.RunCommandAsync((Command<BsonDocument>)"{ping:1}");

                _logger.LogInformation($"{_logPrefix} Reconnected to database {_databaseName}.");

                return true;
            },
            "Could not reconnect to database",
            fallback: false,
            notify: true,
            onFailure: () =>
            {
                _logger.LogWarning("Stopping application...");
                _appLifetime.StopApplication();
            });
    }

    public Task<bool> Disconnect()
    {
        Instance = null!;
        _logger.LogInformation($"{_logPrefix} Disconnected from database {_databaseName}.");

        return Task.FromResult(true);
    }

    public async Task<MongoDatabaseStats?> GetDatabaseStats()
    {
        if (Instance == null)
        {
            _logger.LogWarning($"{_logPrefix} Cannot get database stats because the database is not connected.");
            return null;
        }

        return await TryAsync(
            async () =>
            {
                var statsDocument = await Instance.RunCommandAsync<BsonDocument>(new BsonDocument("dbStats", 1));
                return (MongoDatabaseStats?)MapStats(statsDocument);
            },
            $"Error getting database stats for '{_databaseName}'");
    }

    public IEnumerable<string> GetCollectionNames()
    {
        return Try(
            () => Instance.ListCollectionNames().ToList().AsEnumerable(),
            "Error getting collection names",
            []);
    }

    public IEnumerable<BsonDocument> GetCollectionData(string collectionName)
    {
        return Try(
            () =>
            {
                var collection = Instance.GetCollection<BsonDocument>(collectionName);
                return collection.Find(Builders<BsonDocument>.Filter.Empty).ToList().AsEnumerable();
            },
            $"Error getting collection data for '{collectionName}'",
            []);
    }

    public async Task<bool> DropCollection(string collectionName)
    {
        return await TryAsync(
            async () =>
            {
                await Instance.DropCollectionAsync(collectionName);
                return true;
            },
            $"Could not drop collection '{collectionName}'",
            fallback: false,
            notify: true);
    }

    public async Task<bool> CreateAndPopulateCollection(string collectionName, IEnumerable<BsonDocument> data)
    {
        return await TryAsync(
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

    public Task<long> GetCollectionDocumentCount(string collectionName)
    {
        var collection = Instance.GetCollection<BsonDocument>(collectionName);
        return collection.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
    }

    private async Task<T?> TryAsync<T>(
        Func<Task<T>> action,
        string errorContext,
        T? fallback = default,
        bool notify = false,
        Action? onFailure = null)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            var message = $"{_logPrefix} {errorContext}. Error: '{ex.Message}'";
            _logger.LogError(message);

            if (notify)
            {
                await _notificationService.SendErrorNotification(message, _name);
            }

            onFailure?.Invoke();

            return fallback;
        }
    }

    private T Try<T>(Func<T> action, string errorContext, T fallback)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            _logger.LogError($"{_logPrefix} {errorContext}. Error: '{ex.Message}'");
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
