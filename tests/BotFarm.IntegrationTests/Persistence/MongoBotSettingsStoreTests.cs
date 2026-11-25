using BotFarm.Core.Models;
using BotFarm.Core.Services;
using MongoDB.Driver;

namespace BotFarm.IntegrationTests.Persistence;

/// <summary>
/// Runs the store of each bot's settings against a real MongoDB server.
/// </summary>
[TestFixture]
public class MongoBotSettingsStoreTests
{
    private string _databaseName = null!;
    private MongoBotSettingsStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _databaseName = $"botsettings_{Guid.NewGuid():N}";
        _store = new MongoBotSettingsStore(MongoServer.Client.GetDatabase(_databaseName));
    }

    [TearDown]
    public Task TearDown()
    {
        return MongoServer.Client.DropDatabaseAsync(_databaseName);
    }

    [Test]
    public async Task GetAsync_ForABotWithoutSettings_ReturnsNull()
    {
        Assert.That(await _store.GetAsync("TestBot"), Is.Null);
    }

    [Test]
    public async Task UpsertAsync_NewBotWithAnotherBotsTelegramBotId_ThrowsDuplicateKeyAndStoresNothing()
    {
        await _store.UpsertAsync(Settings("TestBot", telegramBotId: 100));

        var exception = await Assert.ThrowsAsync<MongoWriteException>(
            () => _store.UpsertAsync(Settings("OtherBot", telegramBotId: 100)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.WriteError.Category, Is.EqualTo(ServerErrorCategory.DuplicateKey));
            Assert.That(await _store.GetAsync("OtherBot"), Is.Null);
        }
    }

    [Test]
    public async Task UpsertAsync_ReplacesTheBotsSettings()
    {
        await _store.UpsertAsync(Settings("TestBot", telegramBotId: 100, adminChatId: 1));
        await _store.UpsertAsync(Settings("OtherBot", telegramBotId: 300, adminChatId: 3));
        var replacement = Settings("TestBot", telegramBotId: 200, adminChatId: 2) with
        {
            ProtectedWebhookSecret = "protected-secret",
            Emoji = "🤖",
            UpdatedBy = "admin"
        };

        await _store.UpsertAsync(replacement);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _store.GetAsync("TestBot"), Is.EqualTo(replacement));
            Assert.That((await _store.GetAsync("OtherBot"))?.AdminChatId, Is.EqualTo(3));
        }
    }

    [Test]
    public async Task UpsertAsync_ForABotWithoutSettings_InsertsThem()
    {
        var settings = Settings("TestBot", telegramBotId: 100);

        await _store.UpsertAsync(settings);

        Assert.That(await _store.GetAsync("TestBot"), Is.EqualTo(settings));
    }

    [Test]
    public async Task UpsertAsync_WithAnotherBotsTelegramBotId_ThrowsDuplicateKeyAndKeepsTheSettings()
    {
        await _store.UpsertAsync(Settings("TestBot", telegramBotId: 100));
        var original = Settings("OtherBot", telegramBotId: 200);
        await _store.UpsertAsync(original);

        var exception = await Assert.ThrowsAsync<MongoWriteException>(
            () => _store.UpsertAsync(Settings("OtherBot", telegramBotId: 100)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.WriteError.Category, Is.EqualTo(ServerErrorCategory.DuplicateKey));
            Assert.That(await _store.GetAsync("OtherBot"), Is.EqualTo(original));
        }
    }

    [Test]
    public async Task UpsertAsync_SeveralBotsWithoutAToken_AreAllStored()
    {
        var first = Settings("TestBot", telegramBotId: null, adminChatId: 1);
        var second = Settings("OtherBot", telegramBotId: null, adminChatId: 2);

        await _store.UpsertAsync(first);
        await _store.UpsertAsync(second);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _store.GetAsync("TestBot"), Is.EqualTo(first));
            Assert.That(await _store.GetAsync("OtherBot"), Is.EqualTo(second));
        }
    }

    [Test]
    public async Task FindBotNameByTelegramBotIdAsync_ReturnsTheBotStoringThatId()
    {
        await _store.UpsertAsync(Settings("TestBot", telegramBotId: 100));
        await _store.UpsertAsync(Settings("OtherBot", telegramBotId: 200));
        await _store.UpsertAsync(Settings("NoTokenBot", telegramBotId: null));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await _store.FindBotNameByTelegramBotIdAsync(200), Is.EqualTo("OtherBot"));
            Assert.That(await _store.FindBotNameByTelegramBotIdAsync(999), Is.Null);
        }
    }

    private static BotSettings Settings(string botName, long? telegramBotId, long? adminChatId = 42)
    {
        return new BotSettings
        {
            BotName = botName,
            ProtectedToken = telegramBotId is null ? null : $"protected-token-{telegramBotId}",
            TelegramBotId = telegramBotId,
            AdminChatId = adminChatId,
            UpdatedAtUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            UpdatedBy = "configuration"
        };
    }
}
