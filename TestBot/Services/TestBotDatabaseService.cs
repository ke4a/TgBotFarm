using BotFarm.Core.Abstractions;
using BotFarm.Core.Base;
using BotFarm.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using TestBot.Abstractions;
using TestBot.Models;

namespace TestBot.Services;

/// <summary>
/// Bot-specific database service for the reference TestBot implementation.
/// </summary>
public class TestBotDatabaseService : MongoDbDatabaseService, ITestBotDatabaseService
{
    private const string GifCollectionName = "GifData";

    private readonly ILogger<TestBotDatabaseService> _logger;

    /// <summary>
    /// Creates the TestBot database service bound to the bot's database.
    /// </summary>
    public TestBotDatabaseService(
        [FromKeyedServices(Constants.Name)] BotIdentity identity,
        IMongoClient client,
        ILogger<TestBotDatabaseService> logger,
        INotificationService notificationService,
        HybridCache cache) : base(identity, client, logger, notificationService, cache)
    {
        _logger = logger;
    }

    /// <summary>
    /// The collection holding every GIF record of the bot, keyed by chat and user.
    /// </summary>
    private IMongoCollection<GifData> Collection => Instance.GetCollection<GifData>(GifCollectionName);

    /// <summary>
    /// Loads the last GIF stored for a user in the specified chat.
    /// </summary>
    public async Task<GifData?> GetGifData(long chatId, long userId)
    {
        return await Collection.Find(KeyFilter(chatId, userId)).FirstOrDefaultAsync();
    }

    /// <summary>
    /// Upserts the last GIF sent by a user in the specified chat. A concurrent save for the same chat and user
    /// can make the upsert's insert fail with a duplicate key; the replace is then retried once, since the
    /// record exists by that point. The last write wins.
    /// </summary>
    public async Task SaveGifData(long chatId, long userId, string fileId)
    {
        var filter = KeyFilter(chatId, userId);
        var gifData = new GifData
        {
            Id = new GifDataKey { ChatId = chatId, UserId = userId },
            FileId = fileId
        };
        var options = new ReplaceOptions { IsUpsert = true };

        try
        {
            await Collection.ReplaceOneAsync(filter, gifData, options);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            await Collection.ReplaceOneAsync(filter, gifData, options);
        }
    }

    /// <summary>
    /// Deletes every GIF record of the specified chat.
    /// </summary>
    public Task ClearChatData(long chatId)
    {
        return Collection.DeleteManyAsync(ChatRangeFilter(chatId));
    }

    /// <summary>
    /// Moves every GIF record of <paramref name="fromChatId"/> to <paramref name="toChatId"/>. Each record is
    /// upserted under the new chat ID with <c>$setOnInsert</c>, so a record already stored for the same user
    /// under <paramref name="toChatId"/> wins. The records under <paramref name="fromChatId"/> are deleted
    /// afterwards. Safe to repeat: a rerun after a partial move only repeats no-op upserts and the delete.
    /// </summary>
    protected override async Task MigrateBotChatData(long fromChatId, long toChatId)
    {
        var records = await Collection.Find(ChatRangeFilter(fromChatId)).ToListAsync();

        if (records.Count > 0)
        {
            var upserts = records.Select(record => new UpdateOneModel<GifData>(
                KeyFilter(toChatId, record.Id.UserId),
                Builders<GifData>.Update.SetOnInsert(x => x.FileId, record.FileId))
            {
                IsUpsert = true
            });

            await Collection.BulkWriteAsync(upserts, new BulkWriteOptions { IsOrdered = false });
        }

        await Collection.DeleteManyAsync(ChatRangeFilter(fromChatId));

        _logger.LogInformation(
            "{LogPrefix} Moved {RecordCount} GIF records from chat {FromChatId} to chat {ToChatId}.",
            Identity.LogPrefix,
            records.Count,
            fromChatId,
            toChatId);
    }

    /// <summary>
    /// Matches the single record of a chat and user by its <c>_id</c>.
    /// </summary>
    private static FilterDefinition<GifData> KeyFilter(long chatId, long userId)
    {
        var key = new GifDataKey { ChatId = chatId, UserId = userId };
        return Builders<GifData>.Filter.Eq(x => x.Id, key);
    }

    /// <summary>
    /// Matches every record of a chat with the <c>_id</c> range
    /// <c>{ ChatId: chatId, UserId: MinKey }</c> to <c>{ ChatId: chatId, UserId: MaxKey }</c>.
    /// MongoDB compares embedded documents field by field in order, and <c>MinKey</c>/<c>MaxKey</c> sort
    /// below and above every value, so the range covers exactly this chat's records. It relies on
    /// <see cref="GifDataKey"/> serializing <c>ChatId</c> first. The query is served by the <c>_id</c> index,
    /// which MongoDB always maintains and which a restore rebuilds, so no secondary index is needed.
    /// </summary>
    private static FilterDefinition<GifData> ChatRangeFilter(long chatId)
    {
        return new BsonDocument("_id", new BsonDocument
        {
            { "$gte", new BsonDocument { { nameof(GifDataKey.ChatId), chatId }, { nameof(GifDataKey.UserId), BsonMinKey.Value } } },
            { "$lte", new BsonDocument { { nameof(GifDataKey.ChatId), chatId }, { nameof(GifDataKey.UserId), BsonMaxKey.Value } } }
        });
    }
}
