using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BotFarm.Core.Base;

/// <summary>
/// Thin facade base class for bot-specific MongoDB database services. The database handle and
/// collection operations are delegated to <see cref="MongoConnectionManager"/>, and
/// chat-settings caching/persistence is delegated to <see cref="MongoChatSettingsRepository"/>.
/// </summary>
public abstract class MongoDbDatabaseService : IMongoDbDatabaseService
{
    private readonly MongoConnectionManager _connection;
    private readonly MongoChatSettingsRepository _chatSettings;
    private readonly ILogger<MongoDbDatabaseService> _logger;

    protected readonly BotIdentity Identity;

    /// <summary>
    /// The bot's MongoDB database.
    /// </summary>
    protected IMongoDatabase Instance => _connection.Instance;

    protected IMongoClient Client => _connection.Client;

    /// <inheritdoc />
    public string Name => Identity.Name;

    public string DatabaseName { get; }

    /// <summary>
    /// Creates the shared MongoDB infrastructure used by a bot-specific database service.
    /// </summary>
    protected MongoDbDatabaseService(
        BotIdentity identity,
        IMongoClient client,
        ILogger<MongoDbDatabaseService> logger,
        INotificationService notificationService,
        HybridCache cache,
        string? databaseName = null)
    {
        Identity = identity;
        _logger = logger;
        DatabaseName = databaseName ?? identity.Name.ToLowerInvariant();

        ArgumentNullException.ThrowIfNull(client);

        _connection = new MongoConnectionManager(
            client,
            logger,
            notificationService,
            identity.Name,
            identity.LogPrefix,
            DatabaseName);

        _chatSettings = new MongoChatSettingsRepository(
            getInstance: () => Instance,
            cache,
            identity.Name);
    }

    /// <inheritdoc />
    public virtual Task<MongoDatabaseStats?> GetDatabaseStats()
    {
        return _connection.GetDatabaseStats();
    }

    /// <inheritdoc />
    public virtual Task<IEnumerable<string>> GetCollectionNames()
    {
        return _connection.GetCollectionNames();
    }

    /// <inheritdoc />
    public virtual IAsyncEnumerable<BsonDocument> GetCollectionData(
        string collectionName,
        CancellationToken cancellationToken = default)
    {
        return _connection.GetCollectionData(collectionName, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DropCollection(string collectionName)
    {
        return _connection.DropCollection(collectionName);
    }

    /// <inheritdoc />
    public Task<bool> CreateCollection(string collectionName)
    {
        return _connection.CreateCollection(collectionName);
    }

    /// <inheritdoc />
    public Task<bool> CreateAndPopulateCollection(string collectionName, IEnumerable<BsonDocument> data)
    {
        return _connection.CreateAndPopulateCollection(collectionName, data);
    }

    /// <inheritdoc />
    public Task<bool> RenameCollection(string sourceCollectionName, string targetCollectionName, bool dropTarget)
    {
        return _connection.RenameCollection(sourceCollectionName, targetCollectionName, dropTarget);
    }

    /// <inheritdoc />
    public Task<long> GetCollectionDocumentCount(string collectionName, bool estimated = true)
    {
        return _connection.GetCollectionDocumentCount(collectionName, estimated);
    }

    /// <inheritdoc />
    public Task<IEnumerable<long>> GetAllChatIds()
    {
        return _chatSettings.GetAllChatIds();
    }

    /// <inheritdoc />
    public Task<long> GetChatCount(bool estimated = true)
    {
        return _chatSettings.GetChatCount(estimated);
    }

    /// <inheritdoc />
    public Task<string> GetChatLanguage<TSettings>(long chatId) where TSettings : ChatSettings
    {
        return _chatSettings.GetChatLanguage<TSettings>(chatId);
    }

    /// <inheritdoc />
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
    protected IAsyncEnumerable<TSettings> GetAllChatSettings<TSettings>(
        CancellationToken cancellationToken = default) where TSettings : ChatSettings
    {
        return _chatSettings.GetAllChatSettings<TSettings>(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Moves the chat settings first, then calls <see cref="MigrateBotChatData"/> for the bot's own per-chat
    /// data. A failure in <see cref="MigrateBotChatData"/> propagates and leaves the moved settings in place.
    /// Does nothing when both chat IDs are equal.
    /// </remarks>
    public async Task MigrateChat(long fromChatId, long toChatId)
    {
        if (fromChatId == toChatId)
        {
            return;
        }

        var settingsResult = await _chatSettings.MoveChatSettings(fromChatId, toChatId);
        _logger.LogInformation(
            "{LogPrefix} Chat settings migration from {FromChatId} to {ToChatId} for bot '{BotName}': {Outcome}.",
            Identity.LogPrefix,
            fromChatId,
            toChatId,
            Identity.Name,
            settingsResult);

        await MigrateBotChatData(fromChatId, toChatId);
        _logger.LogInformation(
            "{LogPrefix} Bot chat data migration from {FromChatId} to {ToChatId} for bot '{BotName}' completed.",
            Identity.LogPrefix,
            fromChatId,
            toChatId,
            Identity.Name);
    }

    /// <summary>
    /// Moves the bot's own per-chat data, stored outside the chat settings, from <paramref name="fromChatId"/>
    /// to <paramref name="toChatId"/>. Called by <see cref="MigrateChat"/> after the chat settings have moved.
    /// The default implementation does nothing.
    /// </summary>
    /// <remarks>
    /// An override must touch only this bot's database, keep data already stored under
    /// <paramref name="toChatId"/> on conflict, and be safe to repeat: completing it after a partial run must
    /// give the same end state.
    /// </remarks>
    protected virtual Task MigrateBotChatData(long fromChatId, long toChatId)
    {
        return Task.CompletedTask;
    }
}
