using BotFarm.Core.Abstractions;
using BotFarm.TestKit;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using NSubstitute;
using TestBot.Models;
using TestBot.Services;

namespace TestBot.UnitTests.Services;

[TestFixture]
public class TestBotDatabaseServiceTests
{
    private const long ChatId = 12345;
    private const long UserId = 67890;

    private IMongoClient _client = null!;
    private IMongoDatabase _database = null!;
    private HybridCacheScope _cacheScope = null!;
    private TestBotDatabaseService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _client = Substitute.For<IMongoClient>();
        _database = Substitute.For<IMongoDatabase>();
        _client.GetDatabase(Arg.Any<string>(), Arg.Any<MongoDatabaseSettings>()).Returns(_database);
        _cacheScope = HybridCacheScope.Create();
        _service = new TestBotDatabaseService(
            _client,
            Substitute.For<ILogger<TestBotDatabaseService>>(),
            Substitute.For<IHostApplicationLifetime>(),
            Substitute.For<INotificationService>(),
            _cacheScope.Cache);
    }

    [TearDown]
    public void TearDown()
    {
        _cacheScope.Dispose();
    }

    [Test]
    public async Task GetGifData_FindsUserInChatSpecificCollection()
    {
        var expected = new GifData { UserId = UserId, FileId = "gif-file-id" };
        var collection = Substitute.For<IMongoCollection<GifData>>();
        var cursor = MongoCursorFactory.Create([expected]);
        _database.GetCollection<GifData>($"{ChatId}", Arg.Any<MongoCollectionSettings>())
            .Returns(collection);
        collection.FindAsync<GifData>(
                Arg.Any<FilterDefinition<GifData>>(),
                Arg.Any<FindOptions<GifData, GifData>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(cursor));

        var result = await _service.GetGifData(ChatId, UserId);

        Assert.That(result, Is.SameAs(expected));
        _client.Received(1).GetDatabase(Constants.Name.ToLowerInvariant(), Arg.Any<MongoDatabaseSettings>());
        _database.Received(1).GetCollection<GifData>($"{ChatId}", Arg.Any<MongoCollectionSettings>());
    }

    [Test]
    public async Task SaveGifData_ReplacesUserGifWithUpsertEnabled()
    {
        var gifData = new GifData { UserId = UserId, FileId = "gif-file-id" };
        var collection = Substitute.For<IMongoCollection<GifData>>();
        _database.GetCollection<GifData>($"{ChatId}", Arg.Any<MongoCollectionSettings>())
            .Returns(collection);

        await _service.SaveGifData(ChatId, gifData);

        await collection.Received(1).ReplaceOneAsync(
            Arg.Any<FilterDefinition<GifData>>(),
            gifData,
            Arg.Is<ReplaceOptions>(options => options.IsUpsert),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ClearChatData_DropsChatSpecificCollection()
    {
        _database.DropCollectionAsync($"{ChatId}", Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await _service.ClearChatData(ChatId);

        await _database.Received(1).DropCollectionAsync($"{ChatId}", Arg.Any<CancellationToken>());
    }
}
