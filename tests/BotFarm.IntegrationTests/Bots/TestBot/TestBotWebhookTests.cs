using System.Net;
using BotFarm.IntegrationTests.Support;
using MongoDB.Bson;
using MongoDB.Driver;
using TestBotConstants = TestBot.Constants;

namespace BotFarm.IntegrationTests.Bots.TestBot;

/// <summary>
/// Delivers updates to TestBot's webhook in the running app and checks the GIFs it stores and sends back.
/// </summary>
[TestFixture]
public class TestBotWebhookTests
{
    private const string BotName = TestBotConstants.Name;

    private BotFarmAppFactory _app = null!;

    [OneTimeSetUp]
    public async Task StartApp()
    {
        _app = await BotFarmAppFactory.CreateAsync();
    }

    [OneTimeTearDown]
    public async Task StopApp()
    {
        await _app.DisposeAsync();
    }

    [Test]
    public async Task GifMessage_IsSavedForItsChatAndSender()
    {
        var chatId = -TelegramUpdates.NewChatId();
        const long userId = 777;

        using var response = await _app.SendUpdateAsync(
            BotName,
            TelegramUpdates.Message(TelegramUpdates.GroupChat(chatId), userId).WithAnimation("saved-gif").AsUpdate());

        var stored = await MongoServer.Client.GetDatabase("testbot").GetCollection<BsonDocument>("GifData")
            .Find(new BsonDocument("_id", new BsonDocument { { "ChatId", chatId }, { "UserId", userId } }))
            .SingleOrDefaultAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(stored?["FileId"].AsString, Is.EqualTo("saved-gif"));
        }
    }

    [Test]
    public async Task GetLastGifCommand_RepliesWithTheSendersLastGif()
    {
        var chat = TelegramUpdates.GroupChat(-TelegramUpdates.NewChatId());
        var chatId = chat["id"]!.GetValue<long>();
        (await _app.SendUpdateAsync(
            BotName,
            TelegramUpdates.Message(chat).WithAnimation("last-gif").AsUpdate())).Dispose();
        var command = $"{TestBotConstants.Commands.GetLastGif}@{FakeTelegramApi.UsernameOf(BotName)}";

        using var response = await _app.SendUpdateAsync(
            BotName,
            TelegramUpdates.Message(chat).WithCommand(command).AsUpdate());

        var replies = _app.Telegram.CallsTo(BotName, "sendAnimation", chatId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(replies, Has.Count.EqualTo(1));
            Assert.That(replies.FirstOrDefault()?.Parameters["animation"]?.GetValue<string>(), Is.EqualTo("last-gif"));
        }
    }
}
