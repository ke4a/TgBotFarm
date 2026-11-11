using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.TestKit;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using NSubstitute;
using System.Net;
using System.Reflection;
using TestBot.Models;
using TestBot.Services;

namespace TestBot.UnitTests.Services;

[TestFixture]
public class TestBotDatabaseServiceTests
{
    private const long ChatId = -100123;
    private const long UserId = 67890;
    private const string GifCollectionName = "GifData";

    private IMongoClient _client = null!;
    private IMongoDatabase _database = null!;
    private IMongoCollection<GifData> _collection = null!;
    private HybridCacheScope _cacheScope = null!;
    private TestBotDatabaseService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _client = Substitute.For<IMongoClient>();
        _database = Substitute.For<IMongoDatabase>();
        _collection = Substitute.For<IMongoCollection<GifData>>();
        _client.GetDatabase(Arg.Any<string>(), Arg.Any<MongoDatabaseSettings>()).Returns(_database);
        _database.GetCollection<GifData>(GifCollectionName, Arg.Any<MongoCollectionSettings>()).Returns(_collection);
        _cacheScope = HybridCacheScope.Create();
        _service = new TestBotDatabaseService(
            new BotIdentity(Constants.Name),
            _client,
            Substitute.For<ILogger<TestBotDatabaseService>>(),
            Substitute.For<INotificationService>(),
            _cacheScope.Cache);
    }

    [TearDown]
    public void TearDown()
    {
        _cacheScope.Dispose();
    }

    [Test]
    public async Task GetGifData_FindsRecordByCompoundIdInGifCollection()
    {
        var expected = new GifData { Id = new GifDataKey { ChatId = ChatId, UserId = UserId }, FileId = "gif-file-id" };
        FilterDefinition<GifData>? capturedFilter = null;
        var cursor = MongoCursorFactory.Create([expected]);
        _collection.FindAsync<GifData>(
                Arg.Do<FilterDefinition<GifData>>(filter => capturedFilter = filter),
                Arg.Any<FindOptions<GifData, GifData>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(cursor));

        var result = await _service.GetGifData(ChatId, UserId);

        Assert.That(result, Is.SameAs(expected));
        _client.Received(1).GetDatabase(Constants.Name.ToLowerInvariant(), Arg.Any<MongoDatabaseSettings>());
        _database.Received(1).GetCollection<GifData>(GifCollectionName, Arg.Any<MongoCollectionSettings>());
        Assert.That(Render(capturedFilter!), Is.EqualTo(ExpectedKeyFilter()));
    }

    [Test]
    public async Task SaveGifData_ReplacesRecordByCompoundIdWithUpsertEnabled()
    {
        await _service.SaveGifData(ChatId, UserId, "gif-file-id");

        await _collection.Received(1).ReplaceOneAsync(
            Arg.Any<FilterDefinition<GifData>>(),
            Arg.Any<GifData>(),
            Arg.Is<ReplaceOptions>(options => options.IsUpsert),
            Arg.Any<CancellationToken>());
        _database.Received(1).GetCollection<GifData>(GifCollectionName, Arg.Any<MongoCollectionSettings>());
        var arguments = SingleCallArguments(nameof(IMongoCollection<GifData>.ReplaceOneAsync));
        var capturedDocument = (GifData)arguments[1]!;
        Assert.That(Render((FilterDefinition<GifData>)arguments[0]!), Is.EqualTo(ExpectedKeyFilter()));
        Assert.That(capturedDocument.Id.ChatId, Is.EqualTo(ChatId));
        Assert.That(capturedDocument.Id.UserId, Is.EqualTo(UserId));
        Assert.That(capturedDocument.FileId, Is.EqualTo("gif-file-id"));
    }

    [Test]
    public async Task SaveGifData_WhenFirstReplaceHitsDuplicateKey_RetriesOnceAndCompletes()
    {
        _collection.ReplaceOneAsync(
                Arg.Any<FilterDefinition<GifData>>(),
                Arg.Any<GifData>(),
                Arg.Any<ReplaceOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(
                _ => throw CreateWriteException(ServerErrorCategory.DuplicateKey),
                _ => Task.FromResult<ReplaceOneResult>(new ReplaceOneResult.Acknowledged(0, 1, null)));

        await _service.SaveGifData(ChatId, UserId, "gif-file-id");

        await _collection.Received(2).ReplaceOneAsync(
            Arg.Any<FilterDefinition<GifData>>(),
            Arg.Is<GifData>(document => document.FileId == "gif-file-id"),
            Arg.Is<ReplaceOptions>(options => options.IsUpsert),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void SaveGifData_WhenRetryAlsoHitsDuplicateKey_Throws()
    {
        _collection.ReplaceOneAsync(
                Arg.Any<FilterDefinition<GifData>>(),
                Arg.Any<GifData>(),
                Arg.Any<ReplaceOptions>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<ReplaceOneResult>>(_ => throw CreateWriteException(ServerErrorCategory.DuplicateKey));

        Assert.ThrowsAsync<MongoWriteException>(() => _service.SaveGifData(ChatId, UserId, "gif-file-id"));
        _collection.Received(2).ReplaceOneAsync(
            Arg.Any<FilterDefinition<GifData>>(),
            Arg.Any<GifData>(),
            Arg.Any<ReplaceOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ClearChatData_DeletesTheChatIdRange()
    {
        await _service.ClearChatData(ChatId);

        await _collection.Received(1).DeleteManyAsync(
            Arg.Any<FilterDefinition<GifData>>(),
            Arg.Any<CancellationToken>());
        var capturedFilter = (FilterDefinition<GifData>)SingleCallArguments(
            nameof(IMongoCollection<GifData>.DeleteManyAsync))[0]!;
        var expected = new BsonDocument("_id", new BsonDocument
        {
            { "$gte", new BsonDocument { { "ChatId", ChatId }, { "UserId", BsonMinKey.Value } } },
            { "$lte", new BsonDocument { { "ChatId", ChatId }, { "UserId", BsonMaxKey.Value } } }
        });
        Assert.That(Render(capturedFilter), Is.EqualTo(expected));
    }

    [Test]
    public async Task ClearChatData_NeverDropsACollection()
    {
        await _service.ClearChatData(ChatId);

        await _database.DidNotReceiveWithAnyArgs().DropCollectionAsync(default(string)!, default);
    }

    [Test]
    public async Task DeleteChat_DeletesTheChatGifRangeAndChatSettings()
    {
        var settings = Substitute.For<IMongoCollection<BsonDocument>>();
        _database.GetCollection<BsonDocument>(nameof(ChatSettings), Arg.Any<MongoCollectionSettings>()).Returns(settings);
        settings.DeleteOneAsync(Arg.Any<FilterDefinition<BsonDocument>>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteResult.Acknowledged(1));

        await _service.DeleteChat(ChatId);

        var capturedFilter = (FilterDefinition<GifData>)SingleCallArguments(
            nameof(IMongoCollection<GifData>.DeleteManyAsync))[0]!;
        var expected = new BsonDocument("_id", new BsonDocument
        {
            { "$gte", new BsonDocument { { "ChatId", ChatId }, { "UserId", BsonMinKey.Value } } },
            { "$lte", new BsonDocument { { "ChatId", ChatId }, { "UserId", BsonMaxKey.Value } } }
        });
        Assert.That(Render(capturedFilter), Is.EqualTo(expected));
        await settings.Received(1).DeleteOneAsync(Arg.Any<FilterDefinition<BsonDocument>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GifOperations_NeverUseAChatIdAsCollectionName()
    {
        var cursor = MongoCursorFactory.Create<GifData>([]);
        _collection.FindAsync<GifData>(
                Arg.Any<FilterDefinition<GifData>>(),
                Arg.Any<FindOptions<GifData, GifData>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(cursor));

        await _service.GetGifData(ChatId, UserId);
        await _service.SaveGifData(ChatId, UserId, "gif-file-id");
        await _service.ClearChatData(ChatId);

        _database.DidNotReceive().GetCollection<GifData>(
            Arg.Is<string>(name => IsNumeric(name)),
            Arg.Any<MongoCollectionSettings>());
        _database.Received(3).GetCollection<GifData>(GifCollectionName, Arg.Any<MongoCollectionSettings>());
    }

    [Test]
    public async Task MigrateChat_WithRecords_UpsertsThemUnderNewChatKeepingExistingThenDeletesOldRange()
    {
        const long newChatId = -1001234567890;
        const long otherUserId = 13579;
        SetupEmptySettingsCollection();
        SetupGifFind(
        [
            new GifData { Id = new GifDataKey { ChatId = ChatId, UserId = UserId }, FileId = "gif-1" },
            new GifData { Id = new GifDataKey { ChatId = ChatId, UserId = otherUserId }, FileId = "gif-2" }
        ]);

        await _service.MigrateChat(ChatId, newChatId);

        var bulkArguments = SingleCallArguments(nameof(IMongoCollection<GifData>.BulkWriteAsync));
        var models = ((IEnumerable<WriteModel<GifData>>)bulkArguments[0]!).ToList();
        var options = (BulkWriteOptions)bulkArguments[1]!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(options.IsOrdered, Is.False);
            Assert.That(models, Has.Count.EqualTo(2));
            Assert.That(models, Is.All.InstanceOf<UpdateOneModel<GifData>>());
        }
        var updates = models.Cast<UpdateOneModel<GifData>>().ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updates.Select(model => model.IsUpsert), Is.All.True);
            Assert.That(Render(updates[0].Filter),
                Is.EqualTo(new BsonDocument("_id", new BsonDocument { { "ChatId", newChatId }, { "UserId", UserId } })));
            Assert.That(Render(updates[1].Filter),
                Is.EqualTo(new BsonDocument("_id", new BsonDocument { { "ChatId", newChatId }, { "UserId", otherUserId } })));
            Assert.That(Render(updates[0].Update),
                Is.EqualTo(new BsonDocument("$setOnInsert", new BsonDocument("FileId", "gif-1"))));
            Assert.That(Render(updates[1].Update),
                Is.EqualTo(new BsonDocument("$setOnInsert", new BsonDocument("FileId", "gif-2"))));
        }
        var deleteFilter = (FilterDefinition<GifData>)SingleCallArguments(
            nameof(IMongoCollection<GifData>.DeleteManyAsync))[0]!;
        Assert.That(Render(deleteFilter), Is.EqualTo(ExpectedChatRangeFilter(ChatId)));
        Received.InOrder(() =>
        {
            _collection.BulkWriteAsync(Arg.Any<IEnumerable<WriteModel<GifData>>>(), Arg.Any<BulkWriteOptions>(), Arg.Any<CancellationToken>());
            _collection.DeleteManyAsync(Arg.Any<FilterDefinition<GifData>>(), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task MigrateChat_WithoutRecords_SkipsBulkWriteAndDeletesOldRange()
    {
        const long newChatId = -1001234567890;
        SetupEmptySettingsCollection();
        FilterDefinition<GifData>? findFilter = null;
        SetupGifFind([], filter => findFilter = filter);

        await _service.MigrateChat(ChatId, newChatId);

        await _collection.DidNotReceiveWithAnyArgs().BulkWriteAsync(default(IEnumerable<WriteModel<GifData>>)!, default, default);
        await _collection.Received(1).DeleteManyAsync(Arg.Any<FilterDefinition<GifData>>(), Arg.Any<CancellationToken>());
        Assert.That(Render(findFilter!), Is.EqualTo(ExpectedChatRangeFilter(ChatId)));
    }

    [Test]
    public async Task MigrateChat_WhenBulkWriteFails_ThrowsWithoutDeletingOldRange()
    {
        const long newChatId = -1001234567890;
        SetupEmptySettingsCollection();
        SetupGifFind([new GifData { Id = new GifDataKey { ChatId = ChatId, UserId = UserId }, FileId = "gif-1" }]);
        _collection.BulkWriteAsync(
                Arg.Any<IEnumerable<WriteModel<GifData>>>(),
                Arg.Any<BulkWriteOptions>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<BulkWriteResult<GifData>>>(_ => throw new MongoException("bulk write failed"));

        await Assert.ThrowsAsync<MongoException>(() => _service.MigrateChat(ChatId, newChatId));

        await _collection.DidNotReceiveWithAnyArgs().DeleteManyAsync(default(FilterDefinition<GifData>)!, default);
    }

    private void SetupEmptySettingsCollection()
    {
        var settings = Substitute.For<IMongoCollection<BsonDocument>>();
        settings.FindAsync<BsonDocument>(
                Arg.Any<FilterDefinition<BsonDocument>>(),
                Arg.Any<FindOptions<BsonDocument, BsonDocument>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(MongoCursorFactory.Create<BsonDocument>([])));
        _database.GetCollection<BsonDocument>(nameof(ChatSettings), Arg.Any<MongoCollectionSettings>()).Returns(settings);
    }

    private void SetupGifFind(IEnumerable<GifData> records, Action<FilterDefinition<GifData>>? captureFilter = null)
    {
        var cursor = MongoCursorFactory.Create(records);
        _collection.FindAsync<GifData>(
                Arg.Do<FilterDefinition<GifData>>(filter => captureFilter?.Invoke(filter)),
                Arg.Any<FindOptions<GifData, GifData>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(cursor));
    }

    private static BsonDocument ExpectedChatRangeFilter(long chatId)
    {
        return new BsonDocument("_id", new BsonDocument
        {
            { "$gte", new BsonDocument { { "ChatId", chatId }, { "UserId", BsonMinKey.Value } } },
            { "$lte", new BsonDocument { { "ChatId", chatId }, { "UserId", BsonMaxKey.Value } } }
        });
    }

    private static BsonValue Render(UpdateDefinition<GifData> update)
    {
        var registry = BsonSerializer.SerializerRegistry;
        return update.Render(new RenderArgs<GifData>(registry.GetSerializer<GifData>(), registry));
    }

    private object?[] SingleCallArguments(string methodName)
    {
        return _collection.ReceivedCalls().Single(call => call.GetMethodInfo().Name == methodName).GetArguments();
    }

    private static bool IsNumeric(string name)
    {
        return long.TryParse(name, out _);
    }

    private static BsonDocument ExpectedKeyFilter()
    {
        return new BsonDocument("_id", new BsonDocument { { "ChatId", ChatId }, { "UserId", UserId } });
    }

    private static BsonDocument Render(FilterDefinition<GifData> filter)
    {
        var registry = BsonSerializer.SerializerRegistry;
        return filter.Render(new RenderArgs<GifData>(registry.GetSerializer<GifData>(), registry));
    }

    private static MongoWriteException CreateWriteException(ServerErrorCategory category)
    {
        // WriteError has no public constructor, so build one the way the driver does.
        var writeError = (WriteError)Activator.CreateInstance(
            typeof(WriteError),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [category, 11000, "write failed", new BsonDocument()],
            culture: null)!;
        var connectionId = new ConnectionId(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017)));

        return new MongoWriteException(connectionId, writeError, null, null);
    }
}
