using BotFarm.Core.Models;
using BotFarm.Core.Services;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using NSubstitute;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class MongoChatSettingsRepositoryTests
{
    private const string RepositoryName = "TestService";

    private MongoChatSettingsRepository _repository;
    private IMongoDatabase _database;
    private IMongoCollection<TestChatSettings> _settingsCollection;
    private IMongoCollection<ChatSettings> _baseSettingsCollection;
    private HybridCache _cache;
    private ServiceProvider _serviceProvider;

    [SetUp]
    public void SetUp()
    {
        _database = Substitute.For<IMongoDatabase>();
        _settingsCollection = Substitute.For<IMongoCollection<TestChatSettings>>();
        _baseSettingsCollection = Substitute.For<IMongoCollection<ChatSettings>>();

        var services = new ServiceCollection();
        services.AddHybridCache();
        _serviceProvider = services.BuildServiceProvider();
        _cache = _serviceProvider.GetRequiredService<HybridCache>();

        _repository = new MongoChatSettingsRepository(() => _database, _cache, RepositoryName);
    }

    [TearDown]
    public void TearDown()
    {
        _serviceProvider.Dispose();
    }

    [Test]
    public async Task GetAllChatIds_WhenCalledTwice_ReturnsCachedIds()
    {
        var settings = new List<ChatSettings>
        {
            new() { ChatId = 111, Language = "en-US" },
            new() { ChatId = 222, Language = "es-ES" }
        };
        var cursor = CreateCursor(settings);
        _database.GetCollection<ChatSettings>(nameof(ChatSettings), null).Returns(_baseSettingsCollection);
        _baseSettingsCollection.FindSync(
                Arg.Any<FilterDefinition<ChatSettings>>(),
                Arg.Any<FindOptions<ChatSettings>>(),
                Arg.Any<CancellationToken>())
            .Returns(cursor);

        var firstResult = (await _repository.GetAllChatIds()).ToList();
        var secondResult = (await _repository.GetAllChatIds()).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstResult, Is.EquivalentTo([111L, 222L]));
            Assert.That(secondResult, Is.EquivalentTo([111L, 222L]));
        }
        _baseSettingsCollection.Received(1).FindSync(
            Arg.Any<FilterDefinition<ChatSettings>>(),
            Arg.Any<FindOptions<ChatSettings>>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetChatLanguage_WhenSettingsAreMissing_ReturnsDefaultLanguageAndPersistsIt()
    {
        const long chatId = 12345;
        var defaultSettings = new TestChatSettings { ChatId = chatId, Language = Constants.DefaultLanguage };
        var emptySettingsCursor = CreateCursor<TestChatSettings>([]);
        var emptyChatIdsCursor = CreateCursor<ChatSettings>([]);
        _database.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_settingsCollection);
        _database.GetCollection<ChatSettings>(nameof(ChatSettings), null).Returns(_baseSettingsCollection);
        _settingsCollection.FindSync(
                Arg.Any<FilterDefinition<TestChatSettings>>(),
                Arg.Any<FindOptions<TestChatSettings>>(),
                Arg.Any<CancellationToken>())
            .Returns(emptySettingsCursor);
        _settingsCollection.FindOneAndUpdateAsync(
                Arg.Any<FilterDefinition<TestChatSettings>>(),
                Arg.Any<UpdateDefinition<TestChatSettings>>(),
                Arg.Any<FindOneAndUpdateOptions<TestChatSettings, TestChatSettings>>(),
                Arg.Any<CancellationToken>())
            .Returns(defaultSettings);
        _baseSettingsCollection.FindSync(
                Arg.Any<FilterDefinition<ChatSettings>>(),
                Arg.Any<FindOptions<ChatSettings>>(),
                Arg.Any<CancellationToken>())
            .Returns(emptyChatIdsCursor);

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
        var cursor = CreateCursor([settings]);
        _database.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_settingsCollection);
        _settingsCollection.FindSync(
                Arg.Any<FilterDefinition<TestChatSettings>>(),
                Arg.Any<FindOptions<TestChatSettings>>(),
                Arg.Any<CancellationToken>())
            .Returns(cursor);

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
        var existingChatIdsCursor = CreateCursor(new[] { new ChatSettings { ChatId = chatId, Language = "en-US" } });
        _database.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_settingsCollection);
        _database.GetCollection<ChatSettings>(nameof(ChatSettings), null).Returns(_baseSettingsCollection);
        _baseSettingsCollection.FindSync(
                Arg.Any<FilterDefinition<ChatSettings>>(),
                Arg.Any<FindOptions<ChatSettings>>(),
                Arg.Any<CancellationToken>())
            .Returns(existingChatIdsCursor);
        _settingsCollection.FindOneAndUpdateAsync(
                Arg.Any<FilterDefinition<TestChatSettings>>(),
                Arg.Any<UpdateDefinition<TestChatSettings>>(),
                Arg.Any<FindOneAndUpdateOptions<TestChatSettings, TestChatSettings>>(),
                Arg.Any<CancellationToken>())
            .Returns(updatedSettings);

        await _repository.GetAllChatIds();
        _database.ClearReceivedCalls();
        _settingsCollection.ClearReceivedCalls();
        _baseSettingsCollection.ClearReceivedCalls();

        await _repository.SetChatLanguage<TestChatSettings>(chatId, "fr-FR");

        _database.ClearReceivedCalls();
        _settingsCollection.ClearReceivedCalls();
        var cachedResult = await _repository.GetChatSettings<TestChatSettings>(chatId);
        var cachedIds = (await _repository.GetAllChatIds()).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cachedResult, Is.Not.Null);
            Assert.That(cachedResult!.Language, Is.EqualTo("fr-FR"));
            Assert.That(cachedIds, Is.EquivalentTo([chatId]));
        }
        _database.DidNotReceive().GetCollection<TestChatSettings>(nameof(ChatSettings), null);
        _baseSettingsCollection.DidNotReceive().FindSync(
            Arg.Any<FilterDefinition<ChatSettings>>(),
            Arg.Any<FindOptions<ChatSettings>>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SaveChatSettings_SavesCachesAndInvalidatesChatIdsCache()
    {
        var existing = new ChatSettings { ChatId = 111, Language = "en-US" };
        var saved = new TestChatSettings { ChatId = 222, Language = "it-IT" };
        var initialIdsCursor = CreateCursor([existing]);
        var refreshedIdsCursor = CreateCursor(new ChatSettings[] { existing, saved });
        _database.GetCollection<ChatSettings>(nameof(ChatSettings), null).Returns(_baseSettingsCollection);
        _database.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_settingsCollection);
        _baseSettingsCollection.FindSync(
                Arg.Any<FilterDefinition<ChatSettings>>(),
                Arg.Any<FindOptions<ChatSettings>>(),
                Arg.Any<CancellationToken>())
            .Returns(initialIdsCursor, refreshedIdsCursor);
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
        _baseSettingsCollection.Received(1).FindSync(
            Arg.Any<FilterDefinition<ChatSettings>>(),
            Arg.Any<FindOptions<ChatSettings>>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateChatSettings_WhenChatIdAlreadyCached_KeepsChatIdsCache()
    {
        const long chatId = 314;
        var updatedSettings = new TestChatSettings { ChatId = chatId, Language = "pt-BR" };
        var existingChatIdsCursor = CreateCursor(new[] { new ChatSettings { ChatId = chatId, Language = "en-US" } });
        _database.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_settingsCollection);
        _database.GetCollection<ChatSettings>(nameof(ChatSettings), null).Returns(_baseSettingsCollection);
        _baseSettingsCollection.FindSync(
                Arg.Any<FilterDefinition<ChatSettings>>(),
                Arg.Any<FindOptions<ChatSettings>>(),
                Arg.Any<CancellationToken>())
            .Returns(existingChatIdsCursor);
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

        var result = await _repository.UpdateChatSettings(chatId, Builders<TestChatSettings>.Update.Set(x => x.Language, "pt-BR"));

        _database.ClearReceivedCalls();
        _baseSettingsCollection.ClearReceivedCalls();
        var cachedIds = (await _repository.GetAllChatIds()).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Language, Is.EqualTo("pt-BR"));
            Assert.That(cachedIds, Is.EquivalentTo([chatId]));
        }
        _baseSettingsCollection.DidNotReceive().FindSync(
            Arg.Any<FilterDefinition<ChatSettings>>(),
            Arg.Any<FindOptions<ChatSettings>>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateChatSettings_WhenChatIdIsNew_InvalidatesChatIdsCache()
    {
        var existing = new ChatSettings { ChatId = 111, Language = "en-US" };
        var updatedSettings = new TestChatSettings { ChatId = 222, Language = "nl-NL" };
        var initialIdsCursor = CreateCursor([existing]);
        var refreshedIdsCursor = CreateCursor(new ChatSettings[] { existing, updatedSettings });
        _database.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_settingsCollection);
        _database.GetCollection<ChatSettings>(nameof(ChatSettings), null).Returns(_baseSettingsCollection);
        _baseSettingsCollection.FindSync(
                Arg.Any<FilterDefinition<ChatSettings>>(),
                Arg.Any<FindOptions<ChatSettings>>(),
                Arg.Any<CancellationToken>())
            .Returns(initialIdsCursor, refreshedIdsCursor);
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
        _baseSettingsCollection.Received(1).FindSync(
            Arg.Any<FilterDefinition<ChatSettings>>(),
            Arg.Any<FindOptions<ChatSettings>>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetChatSettings_WhenCalledTwice_UsesCacheAfterDatabaseRead()
    {
        const long chatId = 8080;
        var settings = new TestChatSettings { ChatId = chatId, Language = "uk-UA" };
        var cursor = CreateCursor([settings]);
        _database.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_settingsCollection);
        _settingsCollection.FindSync(
                Arg.Any<FilterDefinition<TestChatSettings>>(),
                Arg.Any<FindOptions<TestChatSettings>>(),
                Arg.Any<CancellationToken>())
            .Returns(cursor);

        var firstResult = await _repository.GetChatSettings<TestChatSettings>(chatId);
        var secondResult = await _repository.GetChatSettings<TestChatSettings>(chatId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstResult, Is.Not.Null);
            Assert.That(secondResult, Is.Not.Null);
            Assert.That(secondResult!.Language, Is.EqualTo("uk-UA"));
        }
        _settingsCollection.Received(1).FindSync(
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
        var cursor = CreateCursor(settings);
        _database.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_settingsCollection);
        _settingsCollection.FindSync(
                Arg.Any<FilterDefinition<TestChatSettings>>(),
                Arg.Any<FindOptions<TestChatSettings>>(),
                Arg.Any<CancellationToken>())
            .Returns(cursor);

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

    private static IAsyncCursor<T> CreateCursor<T>(IReadOnlyCollection<T> items)
    {
        var cursor = Substitute.For<IAsyncCursor<T>>();
        cursor.Current.Returns(items);
        cursor.MoveNext(Arg.Any<CancellationToken>()).Returns(items.Count > 0, false);
        return cursor;
    }

    public class TestChatSettings : ChatSettings;
}
