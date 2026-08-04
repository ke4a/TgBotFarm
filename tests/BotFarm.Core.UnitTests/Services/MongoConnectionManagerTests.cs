using BotFarm.Core.Abstractions;
using BotFarm.Core.Services;
using Microsoft.Extensions.Hosting;
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
    private IHostApplicationLifetime _appLifetime;
    private INotificationService _notificationService;
    private IMongoClient _client;

    [SetUp]
    public void SetUp()
    {
        _logger = Substitute.For<ILogger>();
        _appLifetime = Substitute.For<IHostApplicationLifetime>();
        _notificationService = Substitute.For<INotificationService>();
        _client = Substitute.For<IMongoClient>();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
    }

    [Test]
    public async Task Reconnect_WhenPingSucceeds_SetsInstanceAndReturnsTrue()
    {
        var database = Substitute.For<IMongoDatabase>();
        database.RunCommandAsync(Arg.Any<Command<BsonDocument>>(), null, Arg.Any<CancellationToken>())
            .Returns(new BsonDocument("ok", 1.0));
        _client.GetDatabase(DatabaseName, null).Returns(database);
        var manager = CreateManager(_client);

        var result = await manager.Reconnect();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(manager.Instance, Is.SameAs(database));
        }
        Assert.That(HasLogged(LogLevel.Information, $"Reconnected to database {DatabaseName}."), Is.True);
    }

    [Test]
    public async Task Reconnect_WhenPingFails_ReturnsFalseAndStopsApplication()
    {
        var database = Substitute.For<IMongoDatabase>();
        database.RunCommandAsync(Arg.Any<Command<BsonDocument>>(), null, Arg.Any<CancellationToken>())
            .Returns<BsonDocument>(_ => throw new MongoException("Boom"));
        _client.GetDatabase(DatabaseName, null).Returns(database);
        var manager = CreateManager(_client);

        var result = await manager.Reconnect();

        Assert.That(result, Is.False);
        Assert.That(HasLogged(LogLevel.Error, "Could not reconnect to database"), Is.True);
        Assert.That(HasLogged(LogLevel.Warning, "Stopping application..."), Is.True);
        await _notificationService.Received(1).SendErrorNotification(
            Arg.Is<string>(message => message.Contains("Could not reconnect to database")),
            ServiceName);
        _appLifetime.Received(1).StopApplication();
    }

    [Test]
    public async Task Disconnect_WithConnectedInstance_SetsInstanceToNull()
    {
        var manager = CreateManager(_client);
        manager.Instance = Substitute.For<IMongoDatabase>();

        var result = await manager.Disconnect();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(manager.Instance, Is.Null);
        }
    }

    [Test]
    public async Task GetDatabaseStats_WhenInstanceIsNull_ReturnsNullAndLogsWarning()
    {
        var manager = CreateManager(_client);
        manager.Instance = null!;

        var result = await manager.GetDatabaseStats();

        Assert.That(result, Is.Null);
        Assert.That(HasLogged(LogLevel.Warning, "Cannot get database stats because the database is not connected."), Is.True);
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
        var manager = CreateManager(_client);
        manager.Instance = database;

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
    public void GetCollectionNames_WithCollections_ReturnsNames()
    {
        var cursor = CreateCursor(["users", "jobs", "logs"]);
        var database = Substitute.For<IMongoDatabase>();
        database.ListCollectionNames(Arg.Any<ListCollectionNamesOptions>(), Arg.Any<CancellationToken>())
            .Returns(cursor);
        var manager = CreateManager(_client);
        manager.Instance = database;

        var result = manager.GetCollectionNames().ToList();

        Assert.That(result, Is.EquivalentTo(["users", "jobs", "logs"]));
    }

    [Test]
    public void GetCollectionNames_WhenExceptionOccurs_ReturnsEmptyCollection()
    {
        var database = Substitute.For<IMongoDatabase>();
        database.When(x => x.ListCollectionNames(Arg.Any<ListCollectionNamesOptions>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new MongoException("Boom"));
        var manager = CreateManager(_client);
        manager.Instance = database;

        var result = manager.GetCollectionNames().ToList();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void GetCollectionData_WithDocuments_ReturnsDocuments()
    {
        var documents = new List<BsonDocument>
        {
            new() { ["_id"] = 1, ["name"] = "alpha" },
            new() { ["_id"] = 2, ["name"] = "beta" }
        };
        var cursor = CreateCursor(documents);
        var collection = Substitute.For<IMongoCollection<BsonDocument>>();
        collection.FindSync(
                Arg.Any<FilterDefinition<BsonDocument>>(),
                Arg.Any<FindOptions<BsonDocument>>(),
                Arg.Any<CancellationToken>())
            .Returns(cursor);

        var database = Substitute.For<IMongoDatabase>();
        database.GetCollection<BsonDocument>("items", Arg.Any<MongoCollectionSettings>())
            .Returns(collection);

        var manager = CreateManager(_client);
        manager.Instance = database;

        var result = manager.GetCollectionData("items").ToList();

        Assert.That(result, Has.Count.EqualTo(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Any(x => x["name"] == "alpha"), Is.True);
            Assert.That(result.Any(x => x["name"] == "beta"), Is.True);
        }
    }

    [Test]
    public void GetCollectionData_WhenExceptionOccurs_ReturnsEmptyCollection()
    {
        var database = Substitute.For<IMongoDatabase>();
        database.When(x => x.GetCollection<BsonDocument>("broken", Arg.Any<MongoCollectionSettings>()))
            .Do(_ => throw new MongoException("Boom"));
        var manager = CreateManager(_client);
        manager.Instance = database;

        var result = manager.GetCollectionData("broken").ToList();

        Assert.That(result, Is.Empty);
    }

    [Test]
    public async Task DropCollection_WithValidCollection_ReturnsTrue()
    {
        var database = Substitute.For<IMongoDatabase>();
        database.DropCollectionAsync("items", Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        var manager = CreateManager(_client);
        manager.Instance = database;

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
        var manager = CreateManager(_client);
        manager.Instance = database;

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

        var manager = CreateManager(_client);
        manager.Instance = database;

        var result = await manager.CreateAndPopulateCollection("items", documents);

        Assert.That(result, Is.True);
        await collection.Received(1).InsertManyAsync(documents, Arg.Any<InsertManyOptions>(), Arg.Any<CancellationToken>());
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

        var manager = CreateManager(_client);
        manager.Instance = database;

        var result = await manager.CreateAndPopulateCollection("items", documents);

        Assert.That(result, Is.False);
        await _notificationService.Received(1).SendErrorNotification(
            Arg.Is<string>(message => message.Contains("Could not create and populate collection 'items'")),
            ServiceName);
    }

    private MongoConnectionManager CreateManager(IMongoClient client)
    {
        return new MongoConnectionManager(client, _logger, _appLifetime, _notificationService, ServiceName, LogPrefix, DatabaseName);
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

    private static IAsyncCursor<T> CreateCursor<T>(IReadOnlyCollection<T> items)
    {
        var cursor = Substitute.For<IAsyncCursor<T>>();
        cursor.Current.Returns(items);
        cursor.MoveNext(Arg.Any<CancellationToken>()).Returns(items.Count > 0, false);
        return cursor;
    }
}
