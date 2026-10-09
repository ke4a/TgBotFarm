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
    public async Task<GifData?> GetGifData(long chatId, long userId)
    {
        var collection = Instance.GetCollection<GifData>($"{chatId}");
        var filter = Builders<GifData>.Filter.Eq(x => x.UserId, userId);

        return await collection.Find(filter).FirstOrDefaultAsync();
    }

    /// <summary>
    /// Upserts the last GIF sent by a user in the specified chat.
    /// </summary>
    public Task SaveGifData(long chatId, GifData gifData)
    {
        var collection = Instance.GetCollection<GifData>($"{chatId}");

        var filter = Builders<GifData>.Filter.Eq(x => x.UserId, gifData.UserId);
        var options = new ReplaceOptions { IsUpsert = true };

        return collection.ReplaceOneAsync(filter, gifData, options);
    }

    /// <summary>
    /// Drops the chat-specific collection used by TestBot.
    /// </summary>
    public Task ClearChatData(long chatId)
    {
        return Instance.DropCollectionAsync($"{chatId}");
    }
}
