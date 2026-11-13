using BotFarm.Core.Models;
using BotFarm.IntegrationTests.Support;
using MongoDB.Bson;
using MongoDB.Driver;
using CoreConstants = BotFarm.Core.Constants;

namespace BotFarm.IntegrationTests.Persistence;

/// <summary>
/// Runs the chat settings storage every bot database service inherits against a real MongoDB server.
/// </summary>
[TestFixture]
public class ChatSettingsStorageTests
{
    private BotDatabaseScope<PlatformDatabaseService> _bot = null!;

    private PlatformDatabaseService Service => _bot.Service;

    [SetUp]
    public void SetUp()
    {
        _bot = PlatformDatabaseService.CreateScope();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _bot.DisposeAsync();
    }

    [Test]
    public async Task GetChatLanguage_ForANewChat_StoresAndReturnsTheDefaultLanguage()
    {
        var language = await Service.GetChatLanguage<ChatSettings>(-100);

        var stored = await _bot.Database.GetCollection<BsonDocument>(nameof(ChatSettings))
            .Find(new BsonDocument("_id", -100L))
            .SingleAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(language, Is.EqualTo(CoreConstants.DefaultLanguage));
            Assert.That(stored[nameof(ChatSettings.Language)].AsString, Is.EqualTo(CoreConstants.DefaultLanguage));
            Assert.That(await Service.GetAllChatIds(), Is.EquivalentTo(new[] { -100L }));
        }
    }

    [Test]
    public async Task SetChatLanguage_ReplacesTheLanguageReadBack()
    {
        await Service.SetChatLanguage<ChatSettings>(-100, "ru-RU");
        await Service.SetChatLanguage<ChatSettings>(-100, "uk-UA");

        Assert.That(await Service.GetChatLanguage<ChatSettings>(-100), Is.EqualTo("uk-UA"));
    }

    [Test]
    public async Task MigrateChat_MovesTheSettingsToTheNewChat_AndIsSafeToRepeat()
    {
        const long fromChatId = -100;
        const long toChatId = -1001000000100;
        await Service.SetChatLanguage<ChatSettings>(fromChatId, "ru-RU");
        // Loads the chat list into the cache, so the test also shows that moving refreshes it.
        Assert.That(await Service.GetAllChatIds(), Is.EquivalentTo(new[] { fromChatId }));

        await Service.MigrateChat(fromChatId, toChatId);
        await AssertMigrated();

        // Repeating the move, as after an interrupted run, leaves the same result.
        await Service.MigrateChat(fromChatId, toChatId);
        await AssertMigrated();

        async Task AssertMigrated()
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(await Service.GetAllChatIds(), Is.EquivalentTo(new[] { toChatId }));
                Assert.That(await Service.GetChatLanguage<ChatSettings>(toChatId), Is.EqualTo("ru-RU"));
            }
        }
    }

    [Test]
    public async Task MigrateChat_ToAChatThatHasSettings_KeepsTheNewChatsSettings()
    {
        await Service.SetChatLanguage<ChatSettings>(-100, "ru-RU");
        await Service.SetChatLanguage<ChatSettings>(-200, "uk-UA");

        await Service.MigrateChat(-100, -200);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await Service.GetAllChatIds(), Is.EquivalentTo(new[] { -200L }));
            Assert.That(await Service.GetChatLanguage<ChatSettings>(-200), Is.EqualTo("uk-UA"));
        }
    }

    [Test]
    public async Task DeleteChat_RemovesOnlyThatChatsSettings()
    {
        await Service.SetChatLanguage<ChatSettings>(-100, "ru-RU");
        await Service.SetChatLanguage<ChatSettings>(-200, "uk-UA");
        Assert.That(await Service.GetAllChatIds(), Is.EquivalentTo(new[] { -100L, -200L }));

        await Service.DeleteChat(-100);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await Service.GetAllChatIds(), Is.EquivalentTo(new[] { -200L }));
            Assert.That(await Service.GetChatLanguage<ChatSettings>(-200), Is.EqualTo("uk-UA"));
        }
    }
}
