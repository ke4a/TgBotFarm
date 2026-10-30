using BotFarm.Core.Models;
using BotFarm.Core.Services;
using BotFarm.TestKit;
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
public class MongoBotControlStateStoreTests
{
    [Test]
    public void BotControlState_DeserializesRecordsWithTheRemovedRevisionField()
    {
        var document = new BotControlState
        {
            BotName = "TestBot",
            DesiredEnabled = true,
            LastCommandId = "command-1",
            UpdatedAtUtc = DateTime.UtcNow
        }.ToBsonDocument();
        document["Revision"] = 1;

        var state = BsonSerializer.Deserialize<BotControlState>(document);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(state.BotName, Is.EqualTo("TestBot"));
            Assert.That(state.DesiredEnabled, Is.True);
            Assert.That(state.LastCommandId, Is.EqualTo("command-1"));
        }
    }

    [Test]
    public async Task Constructor_DefersDatabaseCreationUntilFirstOperationAndCanRetryAfterFailure()
    {
        var attempts = 0;
        var sut = new MongoBotControlStateStore(() =>
        {
            attempts++;
            throw new InvalidOperationException("MongoDB is not configured yet.");
        });

        Assert.That(attempts, Is.Zero);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetAsync("TestBot"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetAsync("TestBot"));

        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public async Task TryInsertAsync_PersistsStateInTheControlCollection()
    {
        var database = Substitute.For<IMongoDatabase>();
        var states = Substitute.For<IMongoCollection<BotControlState>>();
        database.GetCollection<BotControlState>(MongoBotControlStateStore.StateCollectionName, null).Returns(states);
        var sut = new MongoBotControlStateStore(database);
        var state = new BotControlState
        {
            BotName = "TestBot",
            DesiredEnabled = true,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var inserted = await sut.TryInsertAsync(state);

        Assert.That(inserted, Is.True);
        await states.Received(1).InsertOneAsync(state, null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetAsync_ReturnsStateFromTheControlCollection()
    {
        var database = Substitute.For<IMongoDatabase>();
        var states = Substitute.For<IMongoCollection<BotControlState>>();
        database.GetCollection<BotControlState>(MongoBotControlStateStore.StateCollectionName, null).Returns(states);
        var stored = new BotControlState { BotName = "TestBot", DesiredEnabled = true, UpdatedAtUtc = DateTime.UtcNow };
        var cursor = MongoCursorFactory.Create([stored]);
        states.FindAsync(
                Arg.Any<FilterDefinition<BotControlState>>(),
                Arg.Any<FindOptions<BotControlState, BotControlState>>(),
                Arg.Any<CancellationToken>())
            .Returns(cursor);
        var sut = new MongoBotControlStateStore(database);

        var result = await sut.GetAsync("TestBot");

        Assert.That(result, Is.SameAs(stored));
    }

    [Test]
    public async Task TryInsertAsync_WhenStateAlreadyExists_ReturnsFalse()
    {
        var database = Substitute.For<IMongoDatabase>();
        var states = Substitute.For<IMongoCollection<BotControlState>>();
        database.GetCollection<BotControlState>(MongoBotControlStateStore.StateCollectionName, null).Returns(states);
        states.InsertOneAsync(Arg.Any<BotControlState>(), Arg.Any<InsertOneOptions>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw CreateWriteException(ServerErrorCategory.DuplicateKey));
        var sut = new MongoBotControlStateStore(database);

        var inserted = await sut.TryInsertAsync(new BotControlState { BotName = "TestBot" });

        Assert.That(inserted, Is.False);
    }

    [Test]
    public void TryInsertAsync_WhenWriteFailsForAnotherReason_Throws()
    {
        var database = Substitute.For<IMongoDatabase>();
        var states = Substitute.For<IMongoCollection<BotControlState>>();
        database.GetCollection<BotControlState>(MongoBotControlStateStore.StateCollectionName, null).Returns(states);
        states.InsertOneAsync(Arg.Any<BotControlState>(), Arg.Any<InsertOneOptions>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw CreateWriteException(ServerErrorCategory.Uncategorized));
        var sut = new MongoBotControlStateStore(database);

        Assert.ThrowsAsync<MongoWriteException>(() => sut.TryInsertAsync(new BotControlState { BotName = "TestBot" }));
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

    [Test]
    public async Task TrySetDesiredStateAsync_UpdatesTheDesiredStateAndCommandId()
    {
        var database = Substitute.For<IMongoDatabase>();
        var states = Substitute.For<IMongoCollection<BotControlState>>();
        database.GetCollection<BotControlState>(MongoBotControlStateStore.StateCollectionName, null).Returns(states);
        var updated = new BotControlState
        {
            BotName = "TestBot",
            DesiredEnabled = true,
            LastCommandId = "command-1",
            UpdatedAtUtc = DateTime.UtcNow
        };
        states.FindOneAndUpdateAsync(
                Arg.Any<FilterDefinition<BotControlState>>(),
                Arg.Any<UpdateDefinition<BotControlState>>(),
                Arg.Any<FindOneAndUpdateOptions<BotControlState>>(),
                Arg.Any<CancellationToken>())
            .Returns(updated);
        var sut = new MongoBotControlStateStore(database);

        var result = await sut.TrySetDesiredStateAsync("TestBot", true, "command-1", updated.UpdatedAtUtc);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.LastCommandId, Is.EqualTo("command-1"));
            Assert.That(result.DesiredEnabled, Is.True);
        }

        await states.Received(1).FindOneAndUpdateAsync(
            Arg.Any<FilterDefinition<BotControlState>>(),
            Arg.Any<UpdateDefinition<BotControlState>>(),
            Arg.Any<FindOneAndUpdateOptions<BotControlState>>(),
            Arg.Any<CancellationToken>());
    }
}