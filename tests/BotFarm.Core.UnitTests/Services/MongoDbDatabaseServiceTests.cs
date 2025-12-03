using BotFarm.Core.Abstractions;
using Microsoft.Extensions.Configuration;
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

    [SetUp]
    public void SetUp()
    {
        _logger = Substitute.For<ILogger<MongoDbDatabaseService>>();
        _appLifetime = Substitute.For<IHostApplicationLifetime>();
        _notificationService = Substitute.For<INotificationService>();
        _configuration = Substitute.For<IConfiguration>();
        _configuration.GetConnectionString("MongoDb").Returns("mongodb://localhost:1984");
        _mockDatabase = Substitute.For<IMongoDatabase>();
        _service = new TestableMongoDbDatabaseService(
            _logger,
            _appLifetime,
            _notificationService,
            _configuration);
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
                invalidConfig));
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
        mockCursor.Current.Returns(new List<string>());
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
            new BsonDocument { ["_id"] = 1, ["name"] = "test1", ["value"] = 100 },
            new BsonDocument { ["_id"] = 2, ["name"] = "test2", ["value"] = 200 }
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
        mockCursor.Current.Returns(new List<BsonDocument>());
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
            new BsonDocument { ["_id"] = 1, ["name"] = "doc1" },
            new BsonDocument { ["_id"] = 2, ["name"] = "doc2" }
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
        var documents = new List<BsonDocument> { new BsonDocument { ["_id"] = 1 } };
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

    private class TestableMongoDbDatabaseService : MongoDbDatabaseService
    {
        public TestableMongoDbDatabaseService(
            ILogger<MongoDbDatabaseService> logger,
            IHostApplicationLifetime appLifetime,
            INotificationService notificationService,
            IConfiguration configuration) : base(logger, appLifetime, notificationService, configuration)
        {
            Name = "TestService";
            DatabaseName = "TestDatabase";
        }

        // Test helper methods to manipulate internal state
        public IMongoDatabase GetInstance()
        {
            return Instance;
        }

        public void SetInstance(IMongoDatabase instance)
        {
            Instance = instance;
        }
    }
}
