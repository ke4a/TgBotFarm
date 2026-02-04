using BotFarm.Core.Abstractions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using TestBot.Abstractions;
using TestBot.Models;

namespace TestBot.Services;

public class TestBotDatabaseService : MongoDbDatabaseService, ITestBotDatabaseService
{

    public TestBotDatabaseService(
        ILogger<TestBotDatabaseService> logger,
        IHostApplicationLifetime appLifetime,
        INotificationService notificationService,
        IConfiguration configuration,
        HybridCache cache) : base(logger, appLifetime, notificationService, configuration, cache)
    {
        logPrefix = $"[{nameof(TestBotDatabaseService)}]";
        DatabaseName = Name.ToLower();
        Instance = Client.GetDatabase(DatabaseName);
    }
    public override string Name => Constants.Name;

    public GifData? GetGifData(long chatId, long userId)
    {
        var collection = Instance.GetCollection<GifData>($"{chatId}");
        var filter = Builders<GifData>.Filter.Eq(x => x.UserId, userId);

        return collection.Find(filter).FirstOrDefault();
    }

    public void SaveGifData(long chatId, GifData gifData)
    {
        var collection = Instance.GetCollection<GifData>($"{chatId}");

        var filter = Builders<GifData>.Filter.Eq(x => x.UserId, gifData.UserId);
        var options = new ReplaceOptions { IsUpsert = true };

        collection.ReplaceOne(filter, gifData, options);
    }

    public void ClearChatData(long chatId)
    {
        Instance.DropCollection($"{chatId}");
    }
}
