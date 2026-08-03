using BotFarm.Core.Models;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BotFarm.Core.Abstractions;

public abstract class MongoDbDatabaseService : IMongoDbDatabaseService
{
    protected readonly ILogger<MongoDbDatabaseService> _logger;
    protected readonly IHostApplicationLifetime _appLifetime;
    protected readonly INotificationService _notificationService;
    protected readonly HybridCache _cache;

    protected string logPrefix = $"[{nameof(MongoDbDatabaseService)}]";

    protected IMongoDatabase Instance { get; set; }

    protected MongoClient Client { get; }

    public abstract string Name { get; }

    public string DatabaseName { get; protected set; }

    public MongoDbDatabaseService(
        ILogger<MongoDbDatabaseService> logger,
        IHostApplicationLifetime appLifetime,
        INotificationService notificationService,
        IConfiguration configuration,
        HybridCache cache)
    {
        _logger = logger;
        _appLifetime = appLifetime;
        _notificationService = notificationService;
        var connectionString = configuration?.GetConnectionString("MongoDb")
            ?? throw new InvalidOperationException("MongoDB connection string not found in configuration.");
        Client = new MongoClient(connectionString);
        _cache = cache;
    }

    /// <summary>
    /// Runs an async database operation, logging (and optionally notifying) on failure
    /// and returning <paramref name="fallback"/> instead of throwing.
    /// </summary>
    protected async Task<T?> TryAsync<T>(Func<Task<T>> action, string errorContext, T? fallback = default, bool notify = false)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            var message = $"{logPrefix} {errorContext}. Error: '{ex.Message}'";
            _logger.LogError(message);

            if (notify)
            {
                await _notificationService.SendErrorNotification(message, Name);
            }

            return fallback;
        }
    }

    /// <summary>
    /// Runs a synchronous database operation, logging on failure
    /// and returning <paramref name="fallback"/> instead of throwing.
    /// </summary>
    protected T Try<T>(Func<T> action, string errorContext, T fallback)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            _logger.LogError($"{logPrefix} {errorContext}. Error: '{ex.Message}'");
            return fallback;
        }
    }

    public virtual async Task<MongoDatabaseStats?> GetDatabaseStats()
    {
        if (Instance == null)
        {
            _logger.LogWarning($"{logPrefix} Cannot get database stats because the database is not connected.");
            return null;
        }

        return await TryAsync(
            async () =>
            {
                var statsDocument = await Instance.RunCommandAsync<BsonDocument>(new BsonDocument("dbStats", 1));
                return (MongoDatabaseStats?)MapStats(statsDocument);
            },
            $"Error getting database stats for '{DatabaseName}'");
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

    public virtual IEnumerable<string> GetCollectionNames()
    {
        return Try(
            () => Instance.ListCollectionNames().ToList().AsEnumerable(),
            "Error getting collection names",
            []);
    }

    public virtual IEnumerable<BsonDocument> GetCollectionData(string collectionName)
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

    public virtual async Task<bool> Disconnect()
    {
        Instance = null;
        _logger.LogInformation($"{logPrefix} Disconnected from database {DatabaseName}.");

        return await Task.FromResult(true);
    }

    public virtual async Task<bool> Reconnect()
    {
        try
        {
            Instance = Client.GetDatabase(DatabaseName);
            
            // Test the connection
            await Instance.RunCommandAsync((Command<BsonDocument>)"{ping:1}");
            
            _logger.LogInformation($"{logPrefix} Reconnected to database {DatabaseName}.");

            return true;
        }
        catch (Exception ex)
        {
            var message = $"{logPrefix} Could not reconnect to database. Error: '{ex.Message}'";
            _logger.LogError(message);
            await _notificationService.SendErrorNotification(message, Name);
            _logger.LogWarning("Stopping application...");
            _appLifetime.StopApplication();

            return false;
        }
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

    public async Task<IEnumerable<long>> GetAllChatIds()
    {
        var ids = await _cache.GetOrCreateAsync(
            $"{Name}|{nameof(ChatSettings)}|{nameof(GetAllChatIds)}",
            async (cancel) =>
            {
                var collection = Instance.GetCollection<ChatSettings>(nameof(ChatSettings));

                return collection.Find(Builders<ChatSettings>.Filter.Empty)
                                 .ToList(cancellationToken: cancel)
                                 .Select(c => c.ChatId);
            },
            tags: [Name, nameof(ChatSettings)]
        );

        return ids;
    }

    public async Task<string> GetChatLanguage<TSettings>(long chatId) where TSettings : ChatSettings
    {
        var settings = await GetChatSettings<TSettings>(chatId);

        var language = settings?.Language;
        if (string.IsNullOrWhiteSpace(language))
        {
            await SetChatLanguage<TSettings>(chatId, Constants.DefaultLanguage);
            return Constants.DefaultLanguage;
        }

        return language;
    }

    public async Task SetChatLanguage<TSettings>(long chatId, string language) where TSettings : ChatSettings
    {
        var update = Builders<TSettings>.Update.Set(x => x.Language, language);
        _ = await UpdateChatSettings(chatId, update);
    }

    public Task<long> GetCollectionDocumentCount(string collectionName)
    {
        var collection = Instance.GetCollection<BsonDocument>(collectionName);
        return collection.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
    }

    protected async Task<TSettings> SaveChatSettings<TSettings>(TSettings settings) where TSettings : ChatSettings
    {
        var collection = Instance.GetCollection<TSettings>(nameof(ChatSettings));
        var filter = Builders<TSettings>.Filter.Eq(x => x.ChatId, settings.ChatId);
        var options = new FindOneAndReplaceOptions<TSettings>()
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After
        };
        var updatedSettings = await collection.FindOneAndReplaceAsync(filter, settings, options);

        await _cache.SetAsync(
            $"{Name}|{nameof(ChatSettings)}|{settings.ChatId}",
            updatedSettings,
            tags: [Name, typeof(TSettings).Name, nameof(ChatSettings)]
        );

        await _cache.RemoveAsync($"{Name}|{nameof(ChatSettings)}|{nameof(GetAllChatIds)}");

        return updatedSettings;
    }

    protected async Task<TSettings> UpdateChatSettings<TSettings>(long chatId, UpdateDefinition<TSettings> update) where TSettings : ChatSettings
    {
        var collection = Instance.GetCollection<TSettings>(nameof(ChatSettings));
        var filter = Builders<TSettings>.Filter.Eq(x => x.ChatId, chatId);
        var options = new FindOneAndUpdateOptions<TSettings>()
        {
            IsUpsert = true,
            ReturnDocument = ReturnDocument.After
        };
        var updatedSettings = await collection.FindOneAndUpdateAsync(filter, update, options);

        await _cache.SetAsync(
            $"{Name}|{nameof(ChatSettings)}|{chatId}",
            updatedSettings,
            tags: [Name, typeof(TSettings).Name, nameof(ChatSettings)]
        );

        var allIds = await GetAllChatIds();
        if (!allIds.Contains(updatedSettings.ChatId))
        {
            await _cache.RemoveAsync($"{Name}|{nameof(ChatSettings)}|{nameof(GetAllChatIds)}");
        }

        return updatedSettings;
    }

    protected async Task<TSettings?> GetChatSettings<TSettings>(long chatId) where TSettings : ChatSettings
    {
        var settings = await _cache.GetOrCreateAsync(
            $"{Name}|{nameof(ChatSettings)}|{chatId}",
            async cancel =>
            {
                var collection = Instance.GetCollection<TSettings>(nameof(ChatSettings));
                var filter = Builders<TSettings>.Filter.Eq(x => x.ChatId, chatId);

                return collection.Find(filter).FirstOrDefault(cancel);
            },
            tags: [Name, typeof(TSettings).Name, nameof(ChatSettings)]
        );

        return settings;
    }

    protected async IAsyncEnumerable<TSettings> GetAllChatSettings<TSettings>() where TSettings : ChatSettings
    {
        var collection = Instance.GetCollection<TSettings>(nameof(ChatSettings));
        foreach (var chat in collection.Find(Builders<TSettings>.Filter.Empty).ToList())
        {
            await _cache.SetAsync(
                $"{Name}|{nameof(ChatSettings)}|{chat.ChatId}",
                chat,
                tags: [Name, typeof(TSettings).Name, nameof(ChatSettings)]
            );

            yield return chat;
        }
    }
}
