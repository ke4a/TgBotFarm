using BotFarm.Core.Abstractions;
using BotFarm.IntegrationTests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using NSubstitute;
using TestBot.Models;
using TestBot.Services;

namespace BotFarm.IntegrationTests.Bots.TestBot;

/// <summary>
/// Runs TestBot's GIF storage against a real MongoDB server.
/// </summary>
[TestFixture]
public class TestBotDatabaseServiceTests
{
    private const string GifCollectionName = "GifData";

    private BotDatabaseScope<TestBotDatabaseService> _bot = null!;

    private TestBotDatabaseService Service => _bot.Service;

    [SetUp]
    public void SetUp()
    {
        _bot = BotDatabaseScope.Create((identity, cache) => new TestBotDatabaseService(
            identity,
            MongoServer.Client,
            NullLogger<TestBotDatabaseService>.Instance,
            Substitute.For<INotificationService>(),
            cache));
    }

    [TearDown]
    public async Task TearDown()
    {
        await _bot.DisposeAsync();
    }

    [Test]
    public async Task SaveGifData_KeepsTheLatestGifOfEachUserInEachChat()
    {
        await Service.SaveGifData(-100, 1, "first");
        await Service.SaveGifData(-100, 1, "second");
        await Service.SaveGifData(-100, 2, "other user");

        var storedCount = await _bot.Database.GetCollection<BsonDocument>(GifCollectionName)
            .CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        using (Assert.EnterMultipleScope())
        {
            Assert.That((await Service.GetGifData(-100, 1))?.FileId, Is.EqualTo("second"));
            Assert.That((await Service.GetGifData(-100, 2))?.FileId, Is.EqualTo("other user"));
            Assert.That(await Service.GetGifData(-200, 1), Is.Null);
            Assert.That(storedCount, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task ClearChatData_RemovesOnlyTheGifsOfThatChat()
    {
        await Service.SaveGifData(-100, 1, "a");
        await Service.SaveGifData(-100, long.MaxValue, "b");
        await Service.SaveGifData(-1001, 1, "c");
        await Service.SaveGifData(-99, 1, "d");

        await Service.ClearChatData(-100);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await Service.GetGifData(-100, 1), Is.Null);
            Assert.That(await Service.GetGifData(-100, long.MaxValue), Is.Null);
            Assert.That((await Service.GetGifData(-1001, 1))?.FileId, Is.EqualTo("c"));
            Assert.That((await Service.GetGifData(-99, 1))?.FileId, Is.EqualTo("d"));
        }
    }

    [Test]
    public async Task MigrateChat_MovesTheGifsAndKeepsGifsAlreadyStoredForTheNewChat()
    {
        const long fromChatId = -100;
        const long toChatId = -1001000000100;
        await Service.SetChatLanguage<TestBotChatSettings>(fromChatId, "ru-RU");
        await Service.SaveGifData(fromChatId, 1, "old gif of user 1");
        await Service.SaveGifData(fromChatId, 2, "old gif of user 2");
        await Service.SaveGifData(toChatId, 1, "new gif of user 1");

        await Service.MigrateChat(fromChatId, toChatId);

        using (Assert.EnterMultipleScope())
        {
            Assert.That((await Service.GetGifData(toChatId, 1))?.FileId, Is.EqualTo("new gif of user 1"));
            Assert.That((await Service.GetGifData(toChatId, 2))?.FileId, Is.EqualTo("old gif of user 2"));
            Assert.That(await Service.GetGifData(fromChatId, 1), Is.Null);
            Assert.That(await Service.GetGifData(fromChatId, 2), Is.Null);
        }
    }

    [Test]
    public async Task DeleteChat_RemovesTheChatsGifs()
    {
        await Service.SaveGifData(-100, 1, "deleted");
        await Service.SaveGifData(-200, 1, "kept");

        await Service.DeleteChat(-100);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await Service.GetGifData(-100, 1), Is.Null);
            Assert.That((await Service.GetGifData(-200, 1))?.FileId, Is.EqualTo("kept"));
        }
    }
}
