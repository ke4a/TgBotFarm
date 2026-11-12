using BotFarm.Core.Models;
using BotFarm.TestKit;
using BotFarm.Core.Services;
using Microsoft.Extensions.Caching.Hybrid;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using NSubstitute;
using System.Net;
using System.Reflection;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class MongoChatSettingsRepositoryTests
{
    private const string RepositoryName = "TestService";

    private MongoChatSettingsRepository _repository;
    private IMongoDatabase _database;
    private IMongoCollection<TestChatSettings> _settingsCollection;
    private IMongoCollection<ChatSettings> _baseSettingsCollection;
    private IMongoCollection<BsonDocument> _documentCollection;
    private HybridCache _cache;
    private HybridCacheScope _cacheScope;

    [SetUp]
    public void SetUp()
    {
        _database = Substitute.For<IMongoDatabase>();
        _settingsCollection = Substitute.For<IMongoCollection<TestChatSettings>>();
        _baseSettingsCollection = Substitute.For<IMongoCollection<ChatSettings>>();
        _documentCollection = Substitute.For<IMongoCollection<BsonDocument>>();

        _cacheScope = HybridCacheScope.Create();
        _cache = _cacheScope.Cache;

        _repository = new MongoChatSettingsRepository(() => _database, _cache, RepositoryName);
    }

    [TearDown]
    public void TearDown()
    {
        _cacheScope.Dispose();
    }

    [Test]
    public async Task GetAllChatIds_WhenCalledTwice_ReturnsCachedIds()
    {
        var settings = new List<ChatSettings>
        {
            new() { ChatId = 111, Language = "en-US" },
            new() { ChatId = 222, Language = "es-ES" }
        };
        var cursor = MongoCursorFactory.Create(settings.Select(setting => setting.ChatId));
        _database.GetCollection<ChatSettings>(nameof(ChatSettings), null).Returns(_baseSettingsCollection);
        SetupFind(_baseSettingsCollection, cursor);

        var firstResult = (await _repository.GetAllChatIds()).ToList();
        var secondResult = (await _repository.GetAllChatIds()).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstResult, Is.EquivalentTo([111L, 222L]));
            Assert.That(secondResult, Is.EquivalentTo([111L, 222L]));
        }
        await _baseSettingsCollection.Received(1).FindAsync<long>(
            Arg.Any<FilterDefinition<ChatSettings>>(),
            Arg.Any<FindOptions<ChatSettings, long>>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetChatCount_ReturnsEstimatedCountWithoutLoadingChatIds()
    {
        const long expectedCount = 42;
        _database.GetCollection<ChatSettings>(nameof(ChatSettings), null).Returns(_baseSettingsCollection);
        _baseSettingsCollection.EstimatedDocumentCountAsync(
                Arg.Any<EstimatedDocumentCountOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(expectedCount);

        var result = await _repository.GetChatCount();

        Assert.That(result, Is.EqualTo(expectedCount));
        await _baseSettingsCollection.Received(1).EstimatedDocumentCountAsync(
            Arg.Any<EstimatedDocumentCountOptions>(),
            Arg.Any<CancellationToken>());
        await _baseSettingsCollection.DidNotReceive().CountDocumentsAsync(
            Arg.Any<FilterDefinition<ChatSettings>>(),
            Arg.Any<CountOptions>(),
            Arg.Any<CancellationToken>());
        await _baseSettingsCollection.DidNotReceive().FindAsync<long>(
            Arg.Any<FilterDefinition<ChatSettings>>(),
            Arg.Any<FindOptions<ChatSettings, long>>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetChatCount_WhenPreciseRequested_ReturnsExactCount()
    {
        const long expectedCount = 42;
        _database.GetCollection<ChatSettings>(nameof(ChatSettings), null).Returns(_baseSettingsCollection);
        _baseSettingsCollection.CountDocumentsAsync(
                Arg.Any<FilterDefinition<ChatSettings>>(),
                Arg.Any<CountOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(expectedCount);

        var result = await _repository.GetChatCount(estimated: false);

        Assert.That(result, Is.EqualTo(expectedCount));
        await _baseSettingsCollection.Received(1).CountDocumentsAsync(
            Arg.Any<FilterDefinition<ChatSettings>>(),
            Arg.Any<CountOptions>(),
            Arg.Any<CancellationToken>());
        await _baseSettingsCollection.DidNotReceive().EstimatedDocumentCountAsync(
            Arg.Any<EstimatedDocumentCountOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetChatLanguage_WhenSettingsAreMissing_ReturnsDefaultLanguageAndPersistsIt()
    {
        const long chatId = 12345;
        var defaultSettings = new TestChatSettings { ChatId = chatId, Language = Constants.DefaultLanguage };
        var emptySettingsCursor = MongoCursorFactory.Create(Array.Empty<TestChatSettings>());
        var emptyChatIdsCursor = MongoCursorFactory.Create(Array.Empty<long>());
        _database.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_settingsCollection);
        _database.GetCollection<ChatSettings>(nameof(ChatSettings), null).Returns(_baseSettingsCollection);
        SetupFind(_settingsCollection, emptySettingsCursor);
        _settingsCollection.FindOneAndUpdateAsync(
                Arg.Any<FilterDefinition<TestChatSettings>>(),
                Arg.Any<UpdateDefinition<TestChatSettings>>(),
                Arg.Any<FindOneAndUpdateOptions<TestChatSettings, TestChatSettings>>(),
                Arg.Any<CancellationToken>())
            .Returns(defaultSettings);
        SetupFind(_baseSettingsCollection, emptyChatIdsCursor);

        var result = await _repository.GetChatLanguage<TestChatSettings>(chatId);

        Assert.That(result, Is.EqualTo(Constants.DefaultLanguage));
        await _settingsCollection.Received(1).FindOneAndUpdateAsync(
            Arg.Any<FilterDefinition<TestChatSettings>>(),
            Arg.Any<UpdateDefinition<TestChatSettings>>(),
            Arg.Is<FindOneAndUpdateOptions<TestChatSettings, TestChatSettings>>(options => options.IsUpsert && options.ReturnDocument == ReturnDocument.After),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetChatLanguage_WhenSettingsAreCached_ReturnsCachedLanguage()
    {
        const long chatId = 555;
        var settings = new TestChatSettings { ChatId = chatId, Language = "de-DE" };
        var cursor = MongoCursorFactory.Create([settings]);
        _database.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_settingsCollection);
        SetupFind(_settingsCollection, cursor);

        _ = await _repository.GetChatSettings<TestChatSettings>(chatId);
        _database.ClearReceivedCalls();
        _settingsCollection.ClearReceivedCalls();

        var result = await _repository.GetChatLanguage<TestChatSettings>(chatId);

        Assert.That(result, Is.EqualTo("de-DE"));
        _database.DidNotReceive().GetCollection<TestChatSettings>(nameof(ChatSettings), null);
    }

    [Test]
    public async Task SetChatLanguage_WithExistingChat_UpdatesSettingsAndCachesResult()
    {
        const long chatId = 999;
        var updatedSettings = new TestChatSettings { ChatId = chatId, Language = "fr-FR" };
        _database.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_settingsCollection);
        _settingsCollection.FindOneAndUpdateAsync(
                Arg.Any<FilterDefinition<TestChatSettings>>(),
                Arg.Any<UpdateDefinition<TestChatSettings>>(),
                Arg.Any<FindOneAndUpdateOptions<TestChatSettings, TestChatSettings>>(),
                Arg.Any<CancellationToken>())
            .Returns(updatedSettings);

        await _repository.SetChatLanguage<TestChatSettings>(chatId, "fr-FR");

        _database.ClearReceivedCalls();
        _settingsCollection.ClearReceivedCalls();
        var cachedResult = await _repository.GetChatSettings<TestChatSettings>(chatId);

        Assert.That(cachedResult, Is.Not.Null);
        Assert.That(cachedResult!.Language, Is.EqualTo("fr-FR"));
        _database.DidNotReceive().GetCollection<TestChatSettings>(nameof(ChatSettings), null);
    }

    [Test]
    public async Task SaveChatSettings_SavesCachesAndInvalidatesChatIdsCache()
    {
        const long existingChatId = 111;
        var saved = new TestChatSettings { ChatId = 222, Language = "it-IT" };
        var initialIdsCursor = MongoCursorFactory.Create([existingChatId]);
        var refreshedIdsCursor = MongoCursorFactory.Create([existingChatId, saved.ChatId]);
        _database.GetCollection<ChatSettings>(nameof(ChatSettings), null).Returns(_baseSettingsCollection);
        _database.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_settingsCollection);
        SetupFind(_baseSettingsCollection, initialIdsCursor, refreshedIdsCursor);
        _settingsCollection.FindOneAndReplaceAsync(
                Arg.Any<FilterDefinition<TestChatSettings>>(),
                saved,
                Arg.Any<FindOneAndReplaceOptions<TestChatSettings, TestChatSettings>>(),
                Arg.Any<CancellationToken>())
            .Returns(saved);

        _ = await _repository.GetAllChatIds();
        _database.ClearReceivedCalls();
        _baseSettingsCollection.ClearReceivedCalls();

        var result = await _repository.SaveChatSettings(saved);

        _database.ClearReceivedCalls();
        _settingsCollection.ClearReceivedCalls();
        var cachedSettings = await _repository.GetChatSettings<TestChatSettings>(saved.ChatId);
        var refreshedIds = (await _repository.GetAllChatIds()).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Language, Is.EqualTo("it-IT"));
            Assert.That(cachedSettings, Is.Not.Null);
            Assert.That(cachedSettings!.Language, Is.EqualTo("it-IT"));
            Assert.That(refreshedIds, Is.EquivalentTo([111L, 222L]));
        }
        _database.DidNotReceive().GetCollection<TestChatSettings>(nameof(ChatSettings), null);
        await _baseSettingsCollection.Received(1).FindAsync<long>(
            Arg.Any<FilterDefinition<ChatSettings>>(),
            Arg.Any<FindOptions<ChatSettings, long>>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateChatSettings_DoesNotLoadChatIds()
    {
        const long chatId = 314;
        var updatedSettings = new TestChatSettings { ChatId = chatId, Language = "pt-BR" };
        _database.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_settingsCollection);
        _database.GetCollection<ChatSettings>(nameof(ChatSettings), null).Returns(_baseSettingsCollection);
        _settingsCollection.FindOneAndUpdateAsync(
                Arg.Any<FilterDefinition<TestChatSettings>>(),
                Arg.Any<UpdateDefinition<TestChatSettings>>(),
                Arg.Any<FindOneAndUpdateOptions<TestChatSettings, TestChatSettings>>(),
                Arg.Any<CancellationToken>())
            .Returns(updatedSettings);

        var result = await _repository.UpdateChatSettings(chatId, Builders<TestChatSettings>.Update.Set(x => x.Language, "pt-BR"));

        Assert.That(result.Language, Is.EqualTo("pt-BR"));
        await _baseSettingsCollection.DidNotReceive().FindAsync<long>(
            Arg.Any<FilterDefinition<ChatSettings>>(),
            Arg.Any<FindOptions<ChatSettings, long>>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateChatSettings_WhenChatIdIsNew_InvalidatesChatIdsCache()
    {
        const long existingChatId = 111;
        var updatedSettings = new TestChatSettings { ChatId = 222, Language = "nl-NL" };
        var initialIdsCursor = MongoCursorFactory.Create([existingChatId]);
        var refreshedIdsCursor = MongoCursorFactory.Create([existingChatId, updatedSettings.ChatId]);
        _database.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_settingsCollection);
        _database.GetCollection<ChatSettings>(nameof(ChatSettings), null).Returns(_baseSettingsCollection);
        SetupFind(_baseSettingsCollection, initialIdsCursor, refreshedIdsCursor);
        _settingsCollection.FindOneAndUpdateAsync(
                Arg.Any<FilterDefinition<TestChatSettings>>(),
                Arg.Any<UpdateDefinition<TestChatSettings>>(),
                Arg.Any<FindOneAndUpdateOptions<TestChatSettings, TestChatSettings>>(),
                Arg.Any<CancellationToken>())
            .Returns(updatedSettings);

        _ = await _repository.GetAllChatIds();
        _database.ClearReceivedCalls();
        _settingsCollection.ClearReceivedCalls();
        _baseSettingsCollection.ClearReceivedCalls();

        var result = await _repository.UpdateChatSettings(updatedSettings.ChatId, Builders<TestChatSettings>.Update.Set(x => x.Language, "nl-NL"));

        _database.ClearReceivedCalls();
        _baseSettingsCollection.ClearReceivedCalls();
        var refreshedIds = (await _repository.GetAllChatIds()).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Language, Is.EqualTo("nl-NL"));
            Assert.That(refreshedIds, Is.EquivalentTo([111L, 222L]));
        }
        await _baseSettingsCollection.Received(1).FindAsync<long>(
            Arg.Any<FilterDefinition<ChatSettings>>(),
            Arg.Any<FindOptions<ChatSettings, long>>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetChatSettings_WhenCalledTwice_UsesCacheAfterDatabaseRead()
    {
        const long chatId = 8080;
        var settings = new TestChatSettings { ChatId = chatId, Language = "uk-UA" };
        var cursor = MongoCursorFactory.Create([settings]);
        _database.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_settingsCollection);
        SetupFind(_settingsCollection, cursor);

        var firstResult = await _repository.GetChatSettings<TestChatSettings>(chatId);
        var secondResult = await _repository.GetChatSettings<TestChatSettings>(chatId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstResult, Is.Not.Null);
            Assert.That(secondResult, Is.Not.Null);
            Assert.That(secondResult!.Language, Is.EqualTo("uk-UA"));
        }
        await _settingsCollection.Received(1).FindAsync<TestChatSettings>(
            Arg.Any<FilterDefinition<TestChatSettings>>(),
            Arg.Any<FindOptions<TestChatSettings>>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetAllChatSettings_WithMultipleSettings_CachesEachYieldedItem()
    {
        var settings = new List<TestChatSettings>
        {
            new() { ChatId = 1, Language = "en-US" },
            new() { ChatId = 2, Language = "es-ES" },
            new() { ChatId = 3, Language = "fr-FR" }
        };
        var cursor = MongoCursorFactory.Create(settings);
        _database.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_settingsCollection);
        SetupFind(_settingsCollection, cursor);

        var result = new List<TestChatSettings>();
        await foreach (var item in _repository.GetAllChatSettings<TestChatSettings>())
        {
            result.Add(item);
        }

        _database.ClearReceivedCalls();
        _settingsCollection.ClearReceivedCalls();
        var cachedSetting = await _repository.GetChatSettings<TestChatSettings>(2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Select(x => x.ChatId), Is.EquivalentTo([1L, 2L, 3L]));
            Assert.That(cachedSetting, Is.Not.Null);
            Assert.That(cachedSetting!.Language, Is.EqualTo("es-ES"));
        }
        _database.DidNotReceive().GetCollection<TestChatSettings>(nameof(ChatSettings), null);
    }

    [Test]
    public async Task MoveChatSettings_WhenSourceIsMissing_ReturnsNotFoundWithoutWrites()
    {
        const long fromChatId = -100;
        const long toChatId = -1001;
        SetupDocumentCollection([]);
        await SeedMoveCacheEntries(fromChatId, toChatId);

        var result = await _repository.MoveChatSettings(fromChatId, toChatId);

        Assert.That(result, Is.EqualTo(ChatSettingsMoveResult.NotFound));
        await _documentCollection.DidNotReceiveWithAnyArgs().InsertOneAsync(default!, default, default);
        await _documentCollection.DidNotReceiveWithAnyArgs().DeleteOneAsync(default(FilterDefinition<BsonDocument>)!, default);
        await AssertMoveCacheEntriesRemoved(fromChatId, toChatId);
    }

    [Test]
    public async Task MoveChatSettings_WhenSourceExists_CopiesDocumentToNewIdAndDeletesSource()
    {
        const long fromChatId = -100;
        const long toChatId = -1001;
        var source = new BsonDocument
        {
            { "_id", fromChatId },
            { nameof(ChatSettings.Language), "de-DE" },
            { "CustomFlag", true }
        };
        SetupDocumentCollection([source]);
        await SeedMoveCacheEntries(fromChatId, toChatId);

        var result = await _repository.MoveChatSettings(fromChatId, toChatId);

        Assert.That(result, Is.EqualTo(ChatSettingsMoveResult.Moved));
        await _documentCollection.Received(1).InsertOneAsync(
            Arg.Is<BsonDocument>(doc =>
                doc["_id"] == toChatId
                && doc[nameof(ChatSettings.Language)] == "de-DE"
                && doc["CustomFlag"] == true
                && doc.ElementCount == 3),
            Arg.Any<InsertOneOptions>(),
            Arg.Any<CancellationToken>());
        await _documentCollection.Received(1).DeleteOneAsync(
            Arg.Is<FilterDefinition<BsonDocument>>(filter => RendersAsIdFilter(filter, fromChatId)),
            Arg.Any<CancellationToken>());
        Received.InOrder(() =>
        {
            _documentCollection.InsertOneAsync(Arg.Any<BsonDocument>(), Arg.Any<InsertOneOptions>(), Arg.Any<CancellationToken>());
            _documentCollection.DeleteOneAsync(Arg.Any<FilterDefinition<BsonDocument>>(), Arg.Any<CancellationToken>());
        });
        await AssertMoveCacheEntriesRemoved(fromChatId, toChatId);
    }

    [Test]
    public async Task MoveChatSettings_WhenTargetAlreadyExists_KeepsTargetAndDeletesSource()
    {
        const long fromChatId = -100;
        const long toChatId = -1001;
        SetupDocumentCollection([new BsonDocument { { "_id", fromChatId }, { nameof(ChatSettings.Language), "de-DE" } }]);
        _documentCollection.InsertOneAsync(Arg.Any<BsonDocument>(), Arg.Any<InsertOneOptions>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw CreateWriteException(ServerErrorCategory.DuplicateKey));
        await SeedMoveCacheEntries(fromChatId, toChatId);

        var result = await _repository.MoveChatSettings(fromChatId, toChatId);

        Assert.That(result, Is.EqualTo(ChatSettingsMoveResult.KeptExisting));
        await _documentCollection.Received(1).DeleteOneAsync(
            Arg.Is<FilterDefinition<BsonDocument>>(filter => RendersAsIdFilter(filter, fromChatId)),
            Arg.Any<CancellationToken>());
        await AssertMoveCacheEntriesRemoved(fromChatId, toChatId);
    }

    [Test]
    public async Task MoveChatSettings_WhenInsertFailsForAnotherReason_ThrowsWithoutDeletingSource()
    {
        const long fromChatId = -100;
        const long toChatId = -1001;
        SetupDocumentCollection([new BsonDocument { { "_id", fromChatId } }]);
        _documentCollection.InsertOneAsync(Arg.Any<BsonDocument>(), Arg.Any<InsertOneOptions>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw CreateWriteException(ServerErrorCategory.Uncategorized));

        await Assert.ThrowsAsync<MongoWriteException>(() => _repository.MoveChatSettings(fromChatId, toChatId));

        await _documentCollection.DidNotReceiveWithAnyArgs().DeleteOneAsync(default(FilterDefinition<BsonDocument>)!, default);
    }

    [Test]
    public async Task DeleteChatSettings_DeletesDocumentByIdAndRemovesCacheEntries()
    {
        const long chatId = -100;
        _database.GetCollection<BsonDocument>(nameof(ChatSettings), null).Returns(_documentCollection);
        _documentCollection.DeleteOneAsync(Arg.Any<FilterDefinition<BsonDocument>>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteResult.Acknowledged(1));
        var keys = new[]
        {
            $"{RepositoryName}|{nameof(ChatSettings)}|{chatId}",
            $"{RepositoryName}|{nameof(ChatSettings)}|{nameof(MongoChatSettingsRepository.GetAllChatIds)}"
        };
        foreach (var key in keys)
        {
            await _cache.SetAsync(key, "cached");
        }

        var deleted = await _repository.DeleteChatSettings(chatId);

        Assert.That(deleted, Is.True);
        await _documentCollection.Received(1).DeleteOneAsync(
            Arg.Is<FilterDefinition<BsonDocument>>(filter => RendersAsIdFilter(filter, chatId)),
            Arg.Any<CancellationToken>());
        foreach (var key in keys)
        {
            var value = await _cache.GetOrCreateAsync(key, _ => ValueTask.FromResult("missing"));
            Assert.That(value, Is.EqualTo("missing"), key);
        }
    }

    [Test]
    public async Task DeleteChatSettings_WhenChatHasNoSettings_ReturnsFalse()
    {
        _database.GetCollection<BsonDocument>(nameof(ChatSettings), null).Returns(_documentCollection);
        _documentCollection.DeleteOneAsync(Arg.Any<FilterDefinition<BsonDocument>>(), Arg.Any<CancellationToken>())
            .Returns(new DeleteResult.Acknowledged(0));

        var deleted = await _repository.DeleteChatSettings(-100);

        Assert.That(deleted, Is.False);
    }

    private void SetupDocumentCollection(IEnumerable<BsonDocument> documents)
    {
        _database.GetCollection<BsonDocument>(nameof(ChatSettings), null).Returns(_documentCollection);
        SetupFind(_documentCollection, MongoCursorFactory.Create(documents));
    }

    private static bool RendersAsIdFilter(FilterDefinition<BsonDocument> filter, long chatId)
    {
        var rendered = filter.Render(new RenderArgs<BsonDocument>(
            BsonSerializer.LookupSerializer<BsonDocument>(),
            BsonSerializer.SerializerRegistry));
        return rendered == new BsonDocument("_id", chatId);
    }

    private static string[] MoveCacheKeys(long fromChatId, long toChatId) =>
    [
        $"{RepositoryName}|{nameof(ChatSettings)}|{fromChatId}",
        $"{RepositoryName}|{nameof(ChatSettings)}|{toChatId}",
        $"{RepositoryName}|{nameof(ChatSettings)}|GetAllChatIds"
    ];

    private async Task SeedMoveCacheEntries(long fromChatId, long toChatId)
    {
        foreach (var key in MoveCacheKeys(fromChatId, toChatId))
        {
            await _cache.SetAsync(key, "cached");
        }
    }

    private async Task AssertMoveCacheEntriesRemoved(long fromChatId, long toChatId)
    {
        foreach (var key in MoveCacheKeys(fromChatId, toChatId))
        {
            var value = await _cache.GetOrCreateAsync(key, _ => ValueTask.FromResult("missing"));
            Assert.That(value, Is.EqualTo("missing"), key);
        }
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

    public class TestChatSettings : ChatSettings;

    private static void SetupFind<TDocument, TProjection>(
        IMongoCollection<TDocument> collection,
        IAsyncCursor<TProjection> cursor,
        IAsyncCursor<TProjection>? nextCursor = null)
    {
        var find = collection.FindAsync<TProjection>(
            Arg.Any<FilterDefinition<TDocument>>(),
            Arg.Any<FindOptions<TDocument, TProjection>>(),
            Arg.Any<CancellationToken>());

        if (nextCursor is null)
        {
            find.Returns(Task.FromResult(cursor));
        }
        else
        {
            find.Returns(Task.FromResult(cursor), Task.FromResult(nextCursor));
        }
    }
}
