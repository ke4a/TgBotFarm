using BotFarm.Core.Abstractions;
using BotFarm.Core.Services;
using BotFarm.TestKit;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using NSubstitute;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class MongoConnectionManagerTests
{
    private const string ServiceName = "TestService";
    private const string LogPrefix = "[TestService]";
    private const string DatabaseName = "TestDatabase";

    private ILogger _logger;
    private INotificationService _notificationService;
    private IMongoClient _client;

    [SetUp]
    public void SetUp()
    {
        _logger = Substitute.For<ILogger>();
        _notificationService = Substitute.For<INotificationService>();
        _client = Substitute.For<IMongoClient>();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
    }

    [Test]
    public void Constructor_GetsDatabaseHandleOnce()
    {
        var database = Substitute.For<IMongoDatabase>();

        var manager = CreateManager(database);

        Assert.That(manager.Instance, Is.SameAs(database));
        _client.Received(1).GetDatabase(DatabaseName, null);
    }

    [Test]
    public async Task GetDatabaseStats_WithStatsDocument_ReturnsMappedStats()
    {
        var database = Substitute.For<IMongoDatabase>();
        database.RunCommandAsync(
            Arg.Any<Command<BsonDocument>>(),
            Arg.Any<ReadPreference>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BsonDocument
            {
                { "db", DatabaseName },
                { "collections", 5 },
                { "storageSize", 1024.5 },
                { "indexes", 2 },
                { "indexSize", 128.5 },
                { "ok", 1.0 }
            }));
        var manager = CreateManager(database);

        var result = await manager.GetDatabaseStats();

        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.DatabaseName, Is.EqualTo(DatabaseName));
            Assert.That(result.Collections, Is.EqualTo(5));
            Assert.That(result.StorageSize, Is.EqualTo(1024.5));
            Assert.That(result.IndexSize, Is.EqualTo(128.5));
            Assert.That(result.TotalSize, Is.EqualTo(1153.0));
            Assert.That(result.Ok, Is.EqualTo(1.0));
        }
    }

    [Test]
    public async Task GetCollectionNames_WithCollections_ReturnsNames()
    {
        var cursor = MongoCursorFactory.Create(["users", "jobs", "logs"]);
        var database = Substitute.For<IMongoDatabase>();
        database.ListCollectionNamesAsync(Arg.Any<ListCollectionNamesOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(cursor));
        var manager = CreateManager(database);

        var result = (await manager.GetCollectionNames()).ToList();

        Assert.That(result, Is.EquivalentTo(["users", "jobs", "logs"]));
    }

    [Test]
    public void GetCollectionNames_WhenExceptionOccurs_PropagatesException()
    {
        var database = Substitute.For<IMongoDatabase>();
        database.ListCollectionNamesAsync(Arg.Any<ListCollectionNamesOptions>(), Arg.Any<CancellationToken>())
            .Returns<Task<IAsyncCursor<string>>>(_ => throw new MongoException("Boom"));
        var manager = CreateManager(database);

        Assert.ThrowsAsync<MongoException>(async () => await manager.GetCollectionNames());
    }

    [Test]
    public async Task GetCollectionData_WithDocuments_ReturnsDocuments()
    {
        var documents = new List<BsonDocument>
        {
            new() { ["_id"] = 1, ["name"] = "alpha" },
            new() { ["_id"] = 2, ["name"] = "beta" }
        };
        var cursor = MongoCursorFactory.Create(documents);
        var collection = Substitute.For<IMongoCollection<BsonDocument>>();
        collection.FindAsync(
                Arg.Any<FilterDefinition<BsonDocument>>(),
                Arg.Any<FindOptions<BsonDocument, BsonDocument>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(cursor));

        var database = Substitute.For<IMongoDatabase>();
        database.GetCollection<BsonDocument>("items", Arg.Any<MongoCollectionSettings>())
            .Returns(collection);

        var manager = CreateManager(database);

        var collectionData = manager.GetCollectionData("items");
        await cursor.DidNotReceive().MoveNextAsync(Arg.Any<CancellationToken>());
        var result = new List<BsonDocument>();
        await foreach (var document in collectionData)
        {
            result.Add(document);
        }

        Assert.That(result, Has.Count.EqualTo(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Any(x => x["name"] == "alpha"), Is.True);
            Assert.That(result.Any(x => x["name"] == "beta"), Is.True);
        }
    }

    [Test]
    public void GetCollectionData_WhenExceptionOccurs_PropagatesException()
    {
        var database = Substitute.For<IMongoDatabase>();
        database.When(x => x.GetCollection<BsonDocument>("broken", Arg.Any<MongoCollectionSettings>()))
            .Do(_ => throw new MongoException("Boom"));
        var manager = CreateManager(database);

        var collectionData = manager.GetCollectionData("broken");

        Assert.ThrowsAsync<MongoException>(async () =>
        {
            await foreach (var _ in collectionData)
            {
            }
        });
    }

    [Test]
    public async Task DropCollection_WithValidCollection_ReturnsTrue()
    {
        var database = Substitute.For<IMongoDatabase>();
        database.DropCollectionAsync("items", Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var manager = CreateManager(database);

        var result = await manager.DropCollection("items");

        Assert.That(result, Is.True);
        await database.Received(1).DropCollectionAsync("items", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DropCollection_WhenExceptionOccurs_ReturnsFalseAndSendsNotification()
    {
        var database = Substitute.For<IMongoDatabase>();
        database.DropCollectionAsync("items", Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new MongoException("Boom"));
        var manager = CreateManager(database);

        var result = await manager.DropCollection("items");

        Assert.That(result, Is.False);
        await _notificationService.Received(1).SendErrorNotification(
            Arg.Is<string>(message => message.Contains("Could not drop collection 'items'")),
            ServiceName);
    }

    [Test]
    public async Task CreateAndPopulateCollection_WithValidData_ReturnsTrue()
    {
        var documents = new List<BsonDocument> { new() { ["_id"] = 1 }, new() { ["_id"] = 2 } };
        var collection = Substitute.For<IMongoCollection<BsonDocument>>();
        collection.InsertManyAsync(documents, Arg.Any<InsertManyOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var database = Substitute.For<IMongoDatabase>();
        database.GetCollection<BsonDocument>("items", Arg.Any<MongoCollectionSettings>())
            .Returns(collection);

        var manager = CreateManager(database);

        var result = await manager.CreateAndPopulateCollection("items", documents);

        Assert.That(result, Is.True);
        await collection.Received(1).InsertManyAsync(documents, Arg.Any<InsertManyOptions>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateCollection_WithValidName_ReturnsTrue()
    {
        var database = Substitute.For<IMongoDatabase>();
        database.CreateCollectionAsync("items", null, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var manager = CreateManager(database);

        var result = await manager.CreateCollection("items");

        Assert.That(result, Is.True);
        await database.Received(1).CreateCollectionAsync("items", null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateAndPopulateCollection_WhenExceptionOccurs_ReturnsFalseAndSendsNotification()
    {
        var documents = new List<BsonDocument> { new() { ["_id"] = 1 } };
        var collection = Substitute.For<IMongoCollection<BsonDocument>>();
        collection.InsertManyAsync(documents, Arg.Any<InsertManyOptions>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new MongoException("Boom"));

        var database = Substitute.For<IMongoDatabase>();
        database.GetCollection<BsonDocument>("items", Arg.Any<MongoCollectionSettings>())
            .Returns(collection);

        var manager = CreateManager(database);

        var result = await manager.CreateAndPopulateCollection("items", documents);

        Assert.That(result, Is.False);
        await _notificationService.Received(1).SendErrorNotification(
            Arg.Is<string>(message => message.Contains("Could not create and populate collection 'items'")),
            ServiceName);
    }

    [Test]
    public async Task RenameCollection_WithDropTarget_ReturnsTrue()
    {
        var database = Substitute.For<IMongoDatabase>();
        database.RenameCollectionAsync(
                "staging",
                "items",
                Arg.Is<RenameCollectionOptions>(options => options.DropTarget == true),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var manager = CreateManager(database);

        var result = await manager.RenameCollection("staging", "items", dropTarget: true);

        Assert.That(result, Is.True);
        await database.Received(1).RenameCollectionAsync(
            "staging",
            "items",
            Arg.Is<RenameCollectionOptions>(options => options.DropTarget == true),
            Arg.Any<CancellationToken>());
    }

    private MongoConnectionManager CreateManager(IMongoDatabase database)
    {
        _client.GetDatabase(DatabaseName, null).Returns(database);
        return new MongoConnectionManager(_client, _logger, _notificationService, ServiceName, LogPrefix, DatabaseName);
    }

    private bool HasLogged(LogLevel level, string messageSubstring)
    {
        return _logger.ReceivedCalls().Any(call =>
        {
            var arguments = call.GetArguments();
            return arguments.Length >= 3
                && arguments[0] is LogLevel loggedLevel
                && loggedLevel == level
                && arguments[2]?.ToString()?.Contains(messageSubstring) == true;
        });
    }

}
