using BotFarm.Core.Models;
using BotFarm.Core.Services;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Thin facade base class for bot-specific MongoDB database services. Connection lifecycle and
/// collection operations are delegated to <see cref="MongoConnectionManager"/>, and
/// chat-settings caching/persistence is delegated to <see cref="MongoChatSettingsRepository"/>.
/// </summary>
public abstract class MongoDbDatabaseService : IMongoDbDatabaseService
{
    private readonly MongoConnectionManager _connection;
    private readonly MongoChatSettingsRepository _chatSettings;

    protected readonly BotIdentity Identity;

    protected IMongoDatabase Instance
    {
        get => _connection.Instance;
        set => _connection.Instance = value;
    }

    protected IMongoClient Client => _connection.Client;

    public string Name => Identity.Name;

    public string DatabaseName { get; }

    /// <summary>
    /// Creates the shared MongoDB infrastructure used by a bot-specific database service.
    /// </summary>
    protected MongoDbDatabaseService(
        BotIdentity identity,
        IMongoClientFactory clientFactory,
        ILogger<MongoDbDatabaseService> logger,
        IHostApplicationLifetime appLifetime,
        INotificationService notificationService,
        IConfiguration configuration,
        HybridCache cache,
        string? databaseName = null)
    {
        Identity = identity;
        DatabaseName = databaseName ?? identity.Name.ToLowerInvariant();

        var connectionString = configuration?.GetConnectionString("MongoDb")
            ?? throw new InvalidOperationException("MongoDB connection string not found in configuration.");
        var client = clientFactory.Create(connectionString);

        _connection = new MongoConnectionManager(
            client,
            logger,
            appLifetime,
            notificationService,
            identity.Name,
            identity.LogPrefix,
            DatabaseName);

        _chatSettings = new MongoChatSettingsRepository(
            getInstance: () => Instance,
            cache,
            identity.Name);
    }

    public virtual Task<MongoDatabaseStats?> GetDatabaseStats()
    {
        return _connection.GetDatabaseStats();
    }

    /// <summary>
    /// Lists collection names.
    /// </summary>
    public virtual IEnumerable<string> GetCollectionNames()
    {
        return _connection.GetCollectionNames();
    }

    /// <summary>
    /// Reads all documents from a collection.
    /// </summary>
    public virtual IEnumerable<BsonDocument> GetCollectionData(string collectionName)
    {
        return _connection.GetCollectionData(collectionName);
    }

    /// <summary>
    /// Clears the cached database handle without disposing the shared Mongo client.
    /// </summary>
    public virtual Task<bool> Disconnect()
    {
        return _connection.Disconnect();
    }

    /// <summary>
    /// Reconnects to MongoDB and verifies the connection.
    /// </summary>
    public virtual Task<bool> Reconnect()
    {
        return _connection.Reconnect();
    }

    /// <summary>
    /// Removes a collection.
    /// </summary>
    public Task<bool> DropCollection(string collectionName)
    {
        return _connection.DropCollection(collectionName);
    }

    /// <summary>
    /// Recreates a collection from the supplied BSON documents.
    /// </summary>
    public Task<bool> CreateAndPopulateCollection(string collectionName, IEnumerable<BsonDocument> data)
    {
        return _connection.CreateAndPopulateCollection(collectionName, data);
    }

    /// <summary>
    /// Counts documents in a collection.
    /// </summary>
    public Task<long> GetCollectionDocumentCount(string collectionName)
    {
        return _connection.GetCollectionDocumentCount(collectionName);
    }

    /// <summary>
    /// Returns all known chat identifiers for this bot.
    /// </summary>
    public Task<IEnumerable<long>> GetAllChatIds()
    {
        return _chatSettings.GetAllChatIds();
    }

    /// <summary>
    /// Returns the persisted language for a chat, defaulting it when missing.
    /// </summary>
    public Task<string> GetChatLanguage<TSettings>(long chatId) where TSettings : ChatSettings
    {
        return _chatSettings.GetChatLanguage<TSettings>(chatId);
    }

    /// <summary>
    /// Stores the selected language for a chat.
    /// </summary>
    public Task SetChatLanguage<TSettings>(long chatId, string language) where TSettings : ChatSettings
    {
        return _chatSettings.SetChatLanguage<TSettings>(chatId, language);
    }

    /// <summary>
    /// Upserts a full <see cref="ChatSettings"/> document and refreshes the related cache entry.
    /// </summary>
    protected Task<TSettings> SaveChatSettings<TSettings>(TSettings settings) where TSettings : ChatSettings
    {
        return _chatSettings.SaveChatSettings(settings);
    }

    /// <summary>
    /// Applies a partial update to chat settings and returns the updated document.
    /// </summary>
    protected Task<TSettings> UpdateChatSettings<TSettings>(long chatId, UpdateDefinition<TSettings> update) where TSettings : ChatSettings
    {
        return _chatSettings.UpdateChatSettings(chatId, update);
    }

    /// <summary>
    /// Loads chat settings for a single chat, using the shared cache when possible.
    /// </summary>
    protected Task<TSettings?> GetChatSettings<TSettings>(long chatId) where TSettings : ChatSettings
    {
        return _chatSettings.GetChatSettings<TSettings>(chatId);
    }

    /// <summary>
    /// Loads all chat settings.
    /// </summary>
    protected IAsyncEnumerable<TSettings> GetAllChatSettings<TSettings>() where TSettings : ChatSettings
    {
        return _chatSettings.GetAllChatSettings<TSettings>();
    }
}
