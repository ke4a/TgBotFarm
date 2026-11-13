using BotFarm.Core.Models;
using BotFarm.Core.Services;
using MongoDB.Driver;

namespace BotFarm.IntegrationTests.Persistence;

/// <summary>
/// Runs the store of each bot's desired enabled state against a real MongoDB server.
/// </summary>
[TestFixture]
public class MongoBotControlStateStoreTests
{
    private string _databaseName = null!;
    private MongoBotControlStateStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _databaseName = $"botcontrol_{Guid.NewGuid():N}";
        _store = new MongoBotControlStateStore(MongoServer.Client.GetDatabase(_databaseName));
    }

    [TearDown]
    public Task TearDown()
    {
        return MongoServer.Client.DropDatabaseAsync(_databaseName);
    }

    [Test]
    public async Task TryInsertAsync_ForABotThatAlreadyHasAState_ReturnsFalseAndKeepsTheFirstState()
    {
        var first = State("TestBot", desiredEnabled: true);
        var second = State("TestBot", desiredEnabled: false);

        var firstInserted = await _store.TryInsertAsync(first);
        var secondInserted = await _store.TryInsertAsync(second);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstInserted, Is.True);
            Assert.That(secondInserted, Is.False);
            Assert.That(await _store.GetAsync("TestBot"), Is.EqualTo(first));
        }
    }

    [Test]
    public async Task TrySetDesiredStateAsync_UpdatesTheBotsStateAndReturnsIt()
    {
        await _store.TryInsertAsync(State("TestBot", desiredEnabled: true));
        await _store.TryInsertAsync(State("OtherBot", desiredEnabled: true));
        var updatedAt = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);

        var updated = await _store.TrySetDesiredStateAsync("TestBot", false, "command-2", updatedAt);

        var expected = new BotControlState
        {
            BotName = "TestBot",
            DesiredEnabled = false,
            LastCommandId = "command-2",
            UpdatedAtUtc = updatedAt
        };
        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Is.EqualTo(expected));
            Assert.That(await _store.GetAsync("TestBot"), Is.EqualTo(expected));
            Assert.That((await _store.GetAsync("OtherBot"))?.DesiredEnabled, Is.True);
        }
    }

    [Test]
    public async Task TrySetDesiredStateAsync_ForABotWithoutAState_ReturnsNullAndStoresNothing()
    {
        var updated = await _store.TrySetDesiredStateAsync("TestBot", true, "command-1", DateTime.UtcNow);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(updated, Is.Null);
            Assert.That(await _store.GetAsync("TestBot"), Is.Null);
        }
    }

    private static BotControlState State(string botName, bool desiredEnabled)
    {
        return new BotControlState
        {
            BotName = botName,
            DesiredEnabled = desiredEnabled,
            LastCommandId = "command-1",
            UpdatedAtUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc)
        };
    }
}
