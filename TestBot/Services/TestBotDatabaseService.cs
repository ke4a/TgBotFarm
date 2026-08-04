using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using TestBot.Abstractions;
using TestBot.Models;

namespace TestBot.Services;

/// <summary>
/// Bot-specific database service for the reference TestBot implementation.
/// </summary>
public class TestBotDatabaseService : MongoDbDatabaseService, ITestBotDatabaseService
{
    /// <summary>
    /// Creates the TestBot database service and binds it to the bot's database.
    /// </summary>
    public TestBotDatabaseService(
        IMongoClientFactory clientFactory,
        ILogger<TestBotDatabaseService> logger,
        IHostApplicationLifetime appLifetime,
        INotificationService notificationService,
        IConfiguration configuration,
        HybridCache cache) : base(new BotIdentity(Constants.Name), clientFactory, logger, appLifetime, notificationService, configuration, cache)
    {
        Instance = Client.GetDatabase(DatabaseName);
    }

    /// <summary>
    /// Loads the last GIF stored for a user in the specified chat.
    /// </summary>
    public GifData? GetGifData(long chatId, long userId)
    {
        var collection = Instance.GetCollection<GifData>($"{chatId}");
        var filter = Builders<GifData>.Filter.Eq(x => x.UserId, userId);

        return collection.Find(filter).FirstOrDefault();
    }

    /// <summary>
    /// Upserts the last GIF sent by a user in the specified chat.
    /// </summary>
    public void SaveGifData(long chatId, GifData gifData)
    {
        var collection = Instance.GetCollection<GifData>($"{chatId}");

        var filter = Builders<GifData>.Filter.Eq(x => x.UserId, gifData.UserId);
        var options = new ReplaceOptions { IsUpsert = true };

        collection.ReplaceOne(filter, gifData, options);
    }

    /// <summary>
    /// Drops the chat-specific collection used by TestBot.
    /// </summary>
    public void ClearChatData(long chatId)
    {
        Instance.DropCollection($"{chatId}");
    }
}
