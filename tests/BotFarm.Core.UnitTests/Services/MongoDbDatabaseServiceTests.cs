using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using NSubstitute;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class MongoDbDatabaseServiceTests
{
    private TestableMongoDbDatabaseService _service;
    private ILogger<MongoDbDatabaseService> _logger;
    private IHostApplicationLifetime _appLifetime;
    private INotificationService _notificationService;
    private IConfiguration _configuration;
    private IMongoDatabase _mockDatabase;
    private IMongoCollection<TestChatSettings> _mockSettingsCollection;
    private IMongoCollection<ChatSettings> _mockBaseSettingsCollection;
    private HybridCache _hybridCache;

    public class TestChatSettings : ChatSettings
    {
    }

    private class TestableMongoDbDatabaseService : MongoDbDatabaseService
    {
        public override string Name { get; }
        
        public TestableMongoDbDatabaseService(
            ILogger<MongoDbDatabaseService> logger,
            IHostApplicationLifetime appLifetime,
            INotificationService notificationService,
            IConfiguration configuration,
            HybridCache cache) : base(logger, appLifetime, notificationService, configuration, cache)
        {
            Name = "TestService";
            DatabaseName = "TestDatabase";
        }

        public IMongoDatabase GetInstance()
        {
            return Instance;
        }

        public void SetInstance(IMongoDatabase instance)
        {
            Instance = instance;
        }

        // Expose protected methods for testing
        public Task<TestChatSettings> TestSaveChatSettings(TestChatSettings settings)
        {
            return SaveChatSettings(settings);
        }

        public Task<TestChatSettings> TestUpdateChatSettings(long chatId, UpdateDefinition<TestChatSettings> update)
        {
            return UpdateChatSettings(chatId, update);
        }

        public Task<TestChatSettings?> TestGetChatSettings(long chatId)
        {
            return GetChatSettings<TestChatSettings>(chatId);
        }

        public IAsyncEnumerable<TestChatSettings> TestGetAllChatSettings()
        {
            return GetAllChatSettings<TestChatSettings>();
        }

        public Task<string> TestGetChatLanguage(long chatId)
        {
            return GetChatLanguage<TestChatSettings>(chatId);
        }

        public Task TestSetChatLanguage(long chatId, string language)
        {
            return SetChatLanguage<TestChatSettings>(chatId, language);
        }
    }

    [SetUp]
    public void SetUp()
    {
        _logger = Substitute.For<ILogger<MongoDbDatabaseService>>();
        _appLifetime = Substitute.For<IHostApplicationLifetime>();
        _notificationService = Substitute.For<INotificationService>();
        _configuration = Substitute.For<IConfiguration>();
        _configuration.GetConnectionString("MongoDb").Returns("mongodb://localhost:1984");
        _mockDatabase = Substitute.For<IMongoDatabase>();
        _mockSettingsCollection = Substitute.For<IMongoCollection<TestChatSettings>>();
        _mockBaseSettingsCollection = Substitute.For<IMongoCollection<ChatSettings>>();

        // Create a real HybridCache instance for testing
        var services = new ServiceCollection();
        services.AddHybridCache();
        var serviceProvider = services.BuildServiceProvider();
        _hybridCache = serviceProvider.GetRequiredService<HybridCache>();

        _service = new TestableMongoDbDatabaseService(
            _logger,
            _appLifetime,
            _notificationService,
            _configuration,
            _hybridCache);
        _service.SetInstance(_mockDatabase);
    }

    [Test]
    public void Constructor_WithMissingConnectionString_ThrowsInvalidOperationException()
    {
        // Arrange
        var invalidConfig = Substitute.For<IConfiguration>();
        invalidConfig.GetConnectionString("MongoDb").Returns((string)null);

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            new TestableMongoDbDatabaseService(
                _logger,
                _appLifetime,
                _notificationService,
                invalidConfig,
                _hybridCache));
    }

    [Test]
    public async Task GetDatabaseStats_WithValidInstance_ReturnsMappedStats()
    {
        // Arrange
        var statsDocument = new BsonDocument
        {
            { "db", "TestDatabase" },
            { "collections", 4 },
            { "storageSize", 1024.0 },
            { "indexes", 2 },
            { "indexSize", 256.0 },
            { "totalSize", 2048.0 },
            { "ok", 1.0 }
        };

        _mockDatabase.RunCommandAsync(
            Arg.Any<Command<BsonDocument>>(),
            Arg.Any<ReadPreference>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(statsDocument));

        // Act
        var result = await _service.GetDatabaseStats();

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.DatabaseName, Is.EqualTo("TestDatabase"));
            Assert.That(result.Collections, Is.EqualTo(4));
            Assert.That(result.StorageSize, Is.EqualTo(1024.0));
            Assert.That(result.Indexes, Is.EqualTo(2));
            Assert.That(result.IndexSize, Is.EqualTo(256.0));
            Assert.That(result.TotalSize, Is.EqualTo(2048.0));
            Assert.That(result.Ok, Is.EqualTo(1.0));
        }
    }

    [Test]
    public async Task GetDatabaseStats_WhenInstanceIsNull_ReturnsNull()
    {
        // Arrange
        _service.SetInstance(null);

        // Act
        var result = await _service.GetDatabaseStats();

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetCollectionDocumentCount_ReturnsCountFromCollection()
    {
        // Arrange
        const string collectionName = "testCollection";
        var expectedCount = 42L;
        var mockCollection = Substitute.For<IMongoCollection<BsonDocument>>();

        _mockDatabase.GetCollection<BsonDocument>(collectionName, Arg.Any<MongoCollectionSettings>())
                     .Returns(mockCollection);
        mockCollection.CountDocumentsAsync(
            Arg.Any<FilterDefinition<BsonDocument>>(),
            Arg.Any<CountOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(expectedCount));

        // Act
        var result = await _service.GetCollectionDocumentCount(collectionName);

        // Assert
        Assert.That(result, Is.EqualTo(expectedCount));
        await mockCollection.Received(1).CountDocumentsAsync(
            Arg.Any<FilterDefinition<BsonDocument>>(),
            Arg.Any<CountOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public void GetCollectionNames_WithExistingCollections_ReturnsCollectionNames()
    {
        // Arrange
        var collectionNames = new List<string> { "collection1", "collection2", "collection3" };
        var mockCursor = Substitute.For<IAsyncCursor<string>>();
        mockCursor.MoveNext(Arg.Any<CancellationToken>()).Returns(true, false);
        mockCursor.Current.Returns(collectionNames);
        _mockDatabase.ListCollectionNames(Arg.Any<ListCollectionNamesOptions>(), Arg.Any<CancellationToken>())
                     .Returns(mockCursor);

        // Act
        var result = _service.GetCollectionNames().ToList();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(result, Contains.Item("collection1"));
            Assert.That(result, Contains.Item("collection2"));
            Assert.That(result, Contains.Item("collection3"));
        }
    }

    [Test]
    public void GetCollectionNames_WithEmptyDatabase_ReturnsEmptyCollection()
    {
        // Arrange
        var mockCursor = Substitute.For<IAsyncCursor<string>>();
        mockCursor.MoveNext(Arg.Any<CancellationToken>()).Returns(false);
        mockCursor.Current.Returns([]);
        _mockDatabase.ListCollectionNames(Arg.Any<ListCollectionNamesOptions>(), Arg.Any<CancellationToken>())
                     .Returns(mockCursor);

        // Act
        var result = _service.GetCollectionNames().ToList();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetCollectionNames_WhenExceptionOccurs_ReturnsEmptyCollection()
    {
        // Arrange
        _mockDatabase.When(x => x.ListCollectionNames(Arg.Any<ListCollectionNamesOptions>(), Arg.Any<CancellationToken>()))
                     .Do(x => throw new MongoException("Connection failed"));

        // Act
        var result = _service.GetCollectionNames().ToList();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetCollectionData_WithExistingData_ReturnsDocuments()
    {
        // Arrange
        var collectionName = "testCollection";
        var documents = new List<BsonDocument>
        {
            new() { ["_id"] = 1, ["name"] = "test1", ["value"] = 100 },
            new() { ["_id"] = 2, ["name"] = "test2", ["value"] = 200 }
        };

        var mockCollection = Substitute.For<IMongoCollection<BsonDocument>>();
        var mockCursor = Substitute.For<IAsyncCursor<BsonDocument>>();
        mockCursor.MoveNext(Arg.Any<CancellationToken>()).Returns(true, false);
        mockCursor.Current.Returns(documents);
        
        _mockDatabase.GetCollection<BsonDocument>(collectionName, Arg.Any<MongoCollectionSettings>()).Returns(mockCollection);
        mockCollection.FindSync(Arg.Any<FilterDefinition<BsonDocument>>(), Arg.Any<FindOptions<BsonDocument>>(), Arg.Any<CancellationToken>())
                      .Returns(mockCursor);

        // Act
        var result = _service.GetCollectionData(collectionName).ToList();

        // Assert
        Assert.That(result, Has.Count.EqualTo(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Any(d => d["name"] == "test1"), Is.True);
            Assert.That(result.Any(d => d["name"] == "test2"), Is.True);
            Assert.That(result.Any(d => d["value"] == 100), Is.True);
            Assert.That(result.Any(d => d["value"] == 200), Is.True);
        }
    }

    [Test]
    public void GetCollectionData_WithNonExistentCollection_ReturnsEmptyCollection()
    {
        // Arrange
        var mockCollection = Substitute.For<IMongoCollection<BsonDocument>>();
        var mockCursor = Substitute.For<IAsyncCursor<BsonDocument>>();
        mockCursor.MoveNext(Arg.Any<CancellationToken>()).Returns(false);
        mockCursor.Current.Returns([]);
        _mockDatabase.GetCollection<BsonDocument>(Arg.Any<string>(), Arg.Any<MongoCollectionSettings>()).Returns(mockCollection);
        mockCollection.FindSync(Arg.Any<FilterDefinition<BsonDocument>>(), Arg.Any<FindOptions<BsonDocument>>(), Arg.Any<CancellationToken>()).Returns(mockCursor);

        // Act
        var result = _service.GetCollectionData("nonExistentCollection").ToList();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetCollectionData_WhenExceptionOccurs_ReturnsEmptyCollection()
    {
        // Arrange
        var collectionName = "errorCollection";
        _mockDatabase.When(x => x.GetCollection<BsonDocument>(collectionName, Arg.Any<MongoCollectionSettings>()))
                     .Do(x => throw new MongoException("Collection error"));

        // Act
        var result = _service.GetCollectionData(collectionName).ToList();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task Disconnect_WithValidInstance_ReturnsTrue()
    {
        // Arrange
        Assert.That(_service.GetInstance(), Is.Not.Null);

        // Act
        var result = await _service.Disconnect();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(_service.GetInstance(), Is.Null);
        }
    }

    [Test]
    public async Task DropCollection_WithValidCollection_ReturnsTrue()
    {
        // Arrange
        var collectionName = "testCollection";
        _mockDatabase.DropCollectionAsync(collectionName, Arg.Any<CancellationToken>())
                     .Returns(Task.CompletedTask);

        // Act
        var result = await _service.DropCollection(collectionName);

        // Assert
        Assert.That(result, Is.True);
        await _mockDatabase.Received(1).DropCollectionAsync(collectionName, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DropCollection_WhenExceptionOccurs_ReturnsFalseAndSendsNotification()
    {
        // Arrange
        var collectionName = "testCollection";
        _mockDatabase.DropCollectionAsync(collectionName, Arg.Any<CancellationToken>())
                     .Returns<Task>(x => throw new MongoException("Drop failed"));

        // Act
        var result = await _service.DropCollection(collectionName);

        // Assert
        Assert.That(result, Is.False);
        await _notificationService.Received(1).SendErrorNotification(
            Arg.Is<string>(s => s.Contains($"Could not drop collection '{collectionName}'")),
            "TestService");
    }

    [Test]
    public async Task CreateAndPopulateCollection_WithValidData_ReturnsTrue()
    {
        // Arrange
        var collectionName = "newCollection";
        var documents = new List<BsonDocument>
        {
            new() { ["_id"] = 1, ["name"] = "doc1" },
            new() { ["_id"] = 2, ["name"] = "doc2" }
        };
        var mockCollection = Substitute.For<IMongoCollection<BsonDocument>>();
        _mockDatabase.GetCollection<BsonDocument>(collectionName, Arg.Any<MongoCollectionSettings>())
                     .Returns(mockCollection);
        mockCollection.InsertManyAsync(documents, Arg.Any<InsertManyOptions>(), Arg.Any<CancellationToken>())
                      .Returns(Task.CompletedTask);

        // Act
        var result = await _service.CreateAndPopulateCollection(collectionName, documents);

        // Assert
        Assert.That(result, Is.True);
        await mockCollection.Received(1).InsertManyAsync(documents, Arg.Any<InsertManyOptions>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateAndPopulateCollection_WhenExceptionOccurs_ReturnsFalseAndSendsNotification()
    {
        // Arrange
        var collectionName = "newCollection";
        var documents = new List<BsonDocument> { new() { ["_id"] = 1 } };
        var mockCollection = Substitute.For<IMongoCollection<BsonDocument>>();
        _mockDatabase.GetCollection<BsonDocument>(collectionName, Arg.Any<MongoCollectionSettings>())
                     .Returns(mockCollection);
        mockCollection.InsertManyAsync(documents, Arg.Any<InsertManyOptions>(), Arg.Any<CancellationToken>())
                      .Returns<Task>(x => throw new MongoException("Insert failed"));

        // Act
        var result = await _service.CreateAndPopulateCollection(collectionName, documents);

        // Assert
        Assert.That(result, Is.False);
        await _notificationService.Received(1).SendErrorNotification(
            Arg.Is<string>(s => s.Contains($"Could not create and populate collection '{collectionName}'")),
            "TestService");
    }

    [Test]
    public async Task GetAllChatIds_WithMultipleChats_ReturnsAllChatIds()
    {
        // Arrange
        var chatIds = new long[] { 111, 222, 333 };
        var settings = chatIds.Select(id => new ChatSettings { ChatId = id, Language = "en-US" }).ToList();
        var mockCursor = Substitute.For<IAsyncCursor<ChatSettings>>();
        mockCursor.Current.Returns(settings);
        mockCursor.MoveNext(Arg.Any<CancellationToken>()).Returns(true, false);
        _mockDatabase.GetCollection<ChatSettings>(nameof(ChatSettings), null).Returns(_mockBaseSettingsCollection);
        _mockBaseSettingsCollection.FindSync(Arg.Any<FilterDefinition<ChatSettings>>(), Arg.Any<FindOptions<ChatSettings>>(), Arg.Any<CancellationToken>())
                                .Returns(mockCursor);

        // Act
        var retrievedChatIds = (await _service.GetAllChatIds()).ToList();

        // Assert
        Assert.That(retrievedChatIds, Has.Count.EqualTo(3));
        foreach (var chatId in chatIds)
        {
            Assert.That(retrievedChatIds, Contains.Item(chatId));
        }
    }

    [Test]
    public async Task GetAllChatIds_WithNoChats_ReturnsEmptyCollection()
    {
        // Arrange
        var mockCursor = Substitute.For<IAsyncCursor<ChatSettings>>();
        mockCursor.Current.Returns([]);
        mockCursor.MoveNext(Arg.Any<CancellationToken>()).Returns(false);
        _mockDatabase.GetCollection<ChatSettings>(nameof(ChatSettings), null).Returns(_mockBaseSettingsCollection);
        _mockBaseSettingsCollection.FindSync(Arg.Any<FilterDefinition<ChatSettings>>(), Arg.Any<FindOptions<ChatSettings>>(), Arg.Any<CancellationToken>())
                                .Returns(mockCursor);

        // Act
        var chatIds = await _service.GetAllChatIds();

        // Assert
        Assert.That(chatIds, Is.Empty);
    }

    [Test]
    public async Task GetAllChatIds_WithCaching_ReturnsFromCache()
    {
        // Arrange
        var chatIds = new long[] { 111, 222 };
        var settings = chatIds.Select(id => new ChatSettings { ChatId = id, Language = "en-US" }).ToList();
        var mockCursor = Substitute.For<IAsyncCursor<ChatSettings>>();
        mockCursor.Current.Returns(settings);
        mockCursor.MoveNext(Arg.Any<CancellationToken>()).Returns(true, false);
        _mockDatabase.GetCollection<ChatSettings>(nameof(ChatSettings), null).Returns(_mockBaseSettingsCollection);
        _mockBaseSettingsCollection.FindSync(Arg.Any<FilterDefinition<ChatSettings>>(), Arg.Any<FindOptions<ChatSettings>>(), Arg.Any<CancellationToken>())
                                .Returns(mockCursor);

        // Act - First call should populate cache
        var firstResult = await _service.GetAllChatIds();
        // Second call should use cache
        var secondResult = await _service.GetAllChatIds();

        using (Assert.EnterMultipleScope())
        {
            // Assert
            Assert.That(firstResult.Count(), Is.EqualTo(2));
            Assert.That(secondResult.Count(), Is.EqualTo(2));
        }
        // FindSync should only be called once (first time, not from cache)
        _mockBaseSettingsCollection.Received(1).FindSync(Arg.Any<FilterDefinition<ChatSettings>>(), Arg.Any<FindOptions<ChatSettings>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SaveChatSettings_WithNewSettings_SavesAndCaches()
    {
        // Arrange
        const long chatId = 12345;
        var settings = new TestChatSettings { ChatId = chatId, Language = "es-ES" };
        _mockDatabase.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_mockSettingsCollection);
        _mockSettingsCollection.FindOneAndReplaceAsync(
            Arg.Any<FilterDefinition<TestChatSettings>>(),
            settings,
            Arg.Any<FindOneAndReplaceOptions<TestChatSettings, TestChatSettings>>(),
            Arg.Any<CancellationToken>())
            .Returns(settings);

        // Act
        var result = await _service.TestSaveChatSettings(settings);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.ChatId, Is.EqualTo(chatId));
        await _mockSettingsCollection.Received(1).FindOneAndReplaceAsync(
            Arg.Any<FilterDefinition<TestChatSettings>>(),
            settings,
            Arg.Is<FindOneAndReplaceOptions<TestChatSettings, TestChatSettings>>(opts => opts.IsUpsert == true && opts.ReturnDocument == ReturnDocument.After),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateChatSettings_WithExistingChat_UpdatesAndCaches()
    {
        // Arrange
        const long chatId = 12345;
        var updatedSettings = new TestChatSettings { ChatId = chatId, Language = "fr-FR" };
        var update = Builders<TestChatSettings>.Update.Set(x => x.Language, "fr-FR");
        
        // Setup for GetAllChatIds call within UpdateChatSettings
        var allSettings = new List<TestChatSettings> { updatedSettings };
        var mockCursor = Substitute.For<IAsyncCursor<TestChatSettings>>();
        mockCursor.Current.Returns(allSettings);
        mockCursor.MoveNext(Arg.Any<CancellationToken>()).Returns(true, false);
        
        _mockDatabase.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_mockSettingsCollection);
        _mockSettingsCollection.FindOneAndUpdateAsync(
            Arg.Any<FilterDefinition<TestChatSettings>>(),
            update,
            Arg.Any<FindOneAndUpdateOptions<TestChatSettings, TestChatSettings>>(),
            Arg.Any<CancellationToken>())
            .Returns(updatedSettings);
        _mockSettingsCollection.FindSync(Arg.Any<FilterDefinition<TestChatSettings>>(), Arg.Any<FindOptions<TestChatSettings>>(), Arg.Any<CancellationToken>())
                               .Returns(mockCursor);

        // Act
        var result = await _service.TestUpdateChatSettings(chatId, update);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Language, Is.EqualTo("fr-FR"));
        await _mockSettingsCollection.Received(1).FindOneAndUpdateAsync(
            Arg.Any<FilterDefinition<TestChatSettings>>(),
            update,
            Arg.Is<FindOneAndUpdateOptions<TestChatSettings, TestChatSettings>>(opts => opts.IsUpsert == true && opts.ReturnDocument == ReturnDocument.After),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetChatSettings_WithExistingSettings_ReturnsSettings()
    {
        // Arrange
        const long chatId = 12345;
        var expectedSettings = new TestChatSettings { ChatId = chatId, Language = "de-DE" };
        var mockCursor = Substitute.For<IAsyncCursor<TestChatSettings>>();
        mockCursor.Current.Returns([expectedSettings]);
        mockCursor.MoveNext(Arg.Any<CancellationToken>()).Returns(true, false);
        _mockDatabase.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_mockSettingsCollection);
        _mockSettingsCollection.FindSync(Arg.Any<FilterDefinition<TestChatSettings>>(), Arg.Any<FindOptions<TestChatSettings>>(), Arg.Any<CancellationToken>())
                               .Returns(mockCursor);

        // Act
        var result = await _service.TestGetChatSettings(chatId);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ChatId, Is.EqualTo(chatId));
            Assert.That(result.Language, Is.EqualTo("de-DE"));
        }
    }

    [Test]
    public async Task GetChatSettings_WithNonExistentSettings_ReturnsNull()
    {
        // Arrange
        const long chatId = 99999;
        var mockCursor = Substitute.For<IAsyncCursor<TestChatSettings>>();
        mockCursor.Current.Returns([]);
        mockCursor.MoveNext(Arg.Any<CancellationToken>()).Returns(false);
        _mockDatabase.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_mockSettingsCollection);
        _mockSettingsCollection.FindSync(Arg.Any<FilterDefinition<TestChatSettings>>(), Arg.Any<FindOptions<TestChatSettings>>(), Arg.Any<CancellationToken>())
                               .Returns(mockCursor);

        // Act
        var result = await _service.TestGetChatSettings(chatId);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetAllChatSettings_WithMultipleSettings_ReturnsAllAndCaches()
    {
        // Arrange
        var settings = new List<TestChatSettings>
        {
            new() { ChatId = 111, Language = "en-US" },
            new() { ChatId = 222, Language = "es-ES" },
            new() { ChatId = 333, Language = "fr-FR" }
        };
        var mockCursor = Substitute.For<IAsyncCursor<TestChatSettings>>();
        mockCursor.Current.Returns(settings);
        mockCursor.MoveNext(Arg.Any<CancellationToken>()).Returns(true, false);
        _mockDatabase.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_mockSettingsCollection);
        _mockSettingsCollection.FindSync(Arg.Any<FilterDefinition<TestChatSettings>>(), Arg.Any<FindOptions<TestChatSettings>>(), Arg.Any<CancellationToken>())
                               .Returns(mockCursor);

        // Act
        var result = new List<TestChatSettings>();
        await foreach (var setting in _service.TestGetAllChatSettings())
        {
            result.Add(setting);
        }

        // Assert
        Assert.That(result, Has.Count.EqualTo(3));
        Assert.That(result.Select(s => s.ChatId), Is.EquivalentTo([111L, 222L, 333L]));
    }

    [Test]
    public async Task GetChatLanguage_WithExistingSettings_ReturnsLanguage()
    {
        // Arrange
        const long chatId = 12345;
        const string expectedLanguage = "es-ES";
        var settings = new TestChatSettings { ChatId = chatId, Language = expectedLanguage };
        var mockCursor = Substitute.For<IAsyncCursor<TestChatSettings>>();
        mockCursor.Current.Returns([settings]);
        mockCursor.MoveNext(Arg.Any<CancellationToken>()).Returns(true, false);
        _mockDatabase.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_mockSettingsCollection);
        _mockSettingsCollection.FindSync(Arg.Any<FilterDefinition<TestChatSettings>>(), Arg.Any<FindOptions<TestChatSettings>>(), Arg.Any<CancellationToken>())
                               .Returns(mockCursor);

        // Act
        var language = await _service.TestGetChatLanguage(chatId);

        // Assert
        Assert.That(language, Is.EqualTo(expectedLanguage));
    }

    [Test]
    public async Task GetChatLanguage_WithNonExistentSettings_ReturnsDefaultLanguage()
    {
        // Arrange
        const long chatId = 12345;
        var defaultSettings = new TestChatSettings { ChatId = chatId, Language = Constants.DefaultLanguage };
        var mockCursor = Substitute.For<IAsyncCursor<TestChatSettings>>();
        mockCursor.Current.Returns([]);
        mockCursor.MoveNext(Arg.Any<CancellationToken>()).Returns(false);
        _mockDatabase.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_mockSettingsCollection);
        _mockSettingsCollection.FindSync(Arg.Any<FilterDefinition<TestChatSettings>>(), Arg.Any<FindOptions<TestChatSettings>>(), Arg.Any<CancellationToken>())
                               .Returns(mockCursor);
        _mockSettingsCollection.FindOneAndUpdateAsync(
            Arg.Any<FilterDefinition<TestChatSettings>>(),
            Arg.Any<UpdateDefinition<TestChatSettings>>(),
            Arg.Any<FindOneAndUpdateOptions<TestChatSettings, TestChatSettings>>(),
            Arg.Any<CancellationToken>())
            .Returns(defaultSettings);

        // Act
        var language = await _service.TestGetChatLanguage(chatId);

        // Assert
        Assert.That(language, Is.EqualTo(Constants.DefaultLanguage));
        await _mockSettingsCollection.Received(1).FindOneAndUpdateAsync(
            Arg.Any<FilterDefinition<TestChatSettings>>(),
            Arg.Any<UpdateDefinition<TestChatSettings>>(),
            Arg.Is<FindOneAndUpdateOptions<TestChatSettings, TestChatSettings>>(opts => opts.IsUpsert == true && opts.ReturnDocument == ReturnDocument.After),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SetChatLanguage_WithNewChatId_CreatesNewSettings()
    {
        // Arrange
        const long chatId = 12345;
        const string language = "fr-FR";
        var newSettings = new TestChatSettings { ChatId = chatId, Language = language };
        var mockCursor = Substitute.For<IAsyncCursor<TestChatSettings>>();
        mockCursor.Current.Returns([]);
        mockCursor.MoveNext(Arg.Any<CancellationToken>()).Returns(false);
        _mockDatabase.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_mockSettingsCollection);
        _mockSettingsCollection.FindSync(Arg.Any<FilterDefinition<TestChatSettings>>(), Arg.Any<FindOptions<TestChatSettings>>(), Arg.Any<CancellationToken>())
                               .Returns(mockCursor);
        _mockSettingsCollection.FindOneAndUpdateAsync(
            Arg.Any<FilterDefinition<TestChatSettings>>(),
            Arg.Any<UpdateDefinition<TestChatSettings>>(),
            Arg.Any<FindOneAndUpdateOptions<TestChatSettings, TestChatSettings>>(),
            Arg.Any<CancellationToken>())
            .Returns(newSettings);

        // Act
        await _service.TestSetChatLanguage(chatId, language);

        // Assert
        await _mockSettingsCollection.Received(1).FindOneAndUpdateAsync(
            Arg.Any<FilterDefinition<TestChatSettings>>(),
            Arg.Any<UpdateDefinition<TestChatSettings>>(),
            Arg.Is<FindOneAndUpdateOptions<TestChatSettings, TestChatSettings>>(opts => opts.IsUpsert == true && opts.ReturnDocument == ReturnDocument.After),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SetChatLanguage_WithExistingChatId_UpdatesSettings()
    {
        // Arrange
        const long chatId = 12345;
        const string newLanguage = "de-DE";
        var existingSettings = new TestChatSettings { ChatId = chatId, Language = Constants.DefaultLanguage };
        var updatedSettings = new TestChatSettings { ChatId = chatId, Language = newLanguage };
        var mockCursor = Substitute.For<IAsyncCursor<TestChatSettings>>();
        mockCursor.Current.Returns([existingSettings]);
        mockCursor.MoveNext(Arg.Any<CancellationToken>()).Returns(true, false);
        _mockDatabase.GetCollection<TestChatSettings>(nameof(ChatSettings), null).Returns(_mockSettingsCollection);
        _mockSettingsCollection.FindSync(Arg.Any<FilterDefinition<TestChatSettings>>(), Arg.Any<FindOptions<TestChatSettings>>(), Arg.Any<CancellationToken>())
                               .Returns(mockCursor);
        _mockSettingsCollection.FindOneAndUpdateAsync(
            Arg.Any<FilterDefinition<TestChatSettings>>(),
            Arg.Any<UpdateDefinition<TestChatSettings>>(),
            Arg.Any<FindOneAndUpdateOptions<TestChatSettings, TestChatSettings>>(),
            Arg.Any<CancellationToken>())
            .Returns(updatedSettings);

        // Act
        await _service.TestSetChatLanguage(chatId, newLanguage);

        // Assert
        await _mockSettingsCollection.Received(1).FindOneAndUpdateAsync(
            Arg.Any<FilterDefinition<TestChatSettings>>(),
            Arg.Any<UpdateDefinition<TestChatSettings>>(),
            Arg.Is<FindOneAndUpdateOptions<TestChatSettings, TestChatSettings>>(opts => opts.IsUpsert == true && opts.ReturnDocument == ReturnDocument.After),
            Arg.Any<CancellationToken>());
    }
}
