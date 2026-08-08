using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.TestKit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static BotFarm.TestKit.TelegramRequestAssertHelpers;
using TestBot.Abstractions;
using TestBot.Models;
using TestBot.Services;

namespace TestBot.UnitTests.Services;

[TestFixture]
public class TestBotUpdateServiceTests
{
    private const long ChatId = 12345;
    private const long UserId = 67890;
    private const long BotUserId = 54321;

    private IBotService _botService;
    private ITestBotDatabaseService _databaseService;
    private ITestBotMarkupService _markupService;
    private ILocalizationService _localizationService;
    private IOptionsMonitor<BotConfig> _options;
    private INotificationService _notificationService;
    private TelegramBotClient _client;

    [SetUp]
    public void SetUp()
    {
        _botService = Substitute.For<IBotService>();
        _databaseService = Substitute.For<ITestBotDatabaseService>();
        _markupService = Substitute.For<ITestBotMarkupService>();
        _localizationService = Substitute.For<ILocalizationService>();
        _options = Substitute.For<IOptionsMonitor<BotConfig>>();
        _notificationService = Substitute.For<INotificationService>();
        _client = TelegramBotClientFactory.CreateSubstitute();

        _botService.Client.Returns(_client);
        _botService.Me.Returns(new User { Id = BotUserId, Username = "testbot" });
        _options.Get(Constants.Name).Returns(new BotConfig
        {
            Token = "123456789:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            Emoji = "🤖",
            Handle = "testbot"
        });
    }

    [Test]
    public async Task ProcessUpdate_WithBotCommand_DispatchesToRegisteredCommandHandler()
    {
        const string language = "en";
        var commandHandler = Substitute.For<ICommandHandler>();
        commandHandler.Command.Returns(Constants.Commands.GetLastGif);
        _databaseService.GetChatLanguage<TestBotChatSettings>(ChatId).Returns(language);
        var service = CreateService([commandHandler], []);
        var update = TelegramMessageFactory.CreateUpdate(message: TelegramMessageFactory.CreateMessage(
            ChatId,
            UserId,
            chatTitle: "Private Chat",
            username: "gif-user",
            text: Constants.Commands.GetLastGif,
            chatType: ChatType.Private,
            entities:
            [
                new MessageEntity
                {
                    Type = MessageEntityType.BotCommand,
                    Offset = 0,
                    Length = Constants.Commands.GetLastGif.Length
                }
            ]));

        await service.ProcessUpdate(update);

        await commandHandler.Received(1).Handle(update.Message!, language);
    }

    [Test]
    public async Task ProcessUpdate_WithCallbackQuery_DispatchesToRegisteredCallbackHandler()
    {
        const string language = "uk";
        var callbackHandler = Substitute.For<ICallbackHandler>();
        callbackHandler.CallbackKey.Returns("custom");
        _databaseService.GetChatLanguage<TestBotChatSettings>(ChatId).Returns(language);
        var service = CreateService([], [callbackHandler]);
        var message = TelegramMessageFactory.CreateMessage(ChatId, UserId, 12, "Group Chat", "gif-user");
        var user = TelegramMessageFactory.CreateUser(UserId, "gif-user");
        var update = TelegramMessageFactory.CreateUpdate(callbackQuery: TelegramMessageFactory.CreateCallbackQuery(
            "callback-1",
            "custom:yes",
            message,
            user,
            ChatId,
            UserId,
            "gif-user"));

        await service.ProcessUpdate(update);

        await callbackHandler.Received(1).Handle("callback-1", message, user, "yes", language);
    }

    [Test]
    public async Task ProcessUpdate_WithGifSaveFailure_SendsErrorNotification()
    {
        var service = CreateService([], []);
        var message = TelegramMessageFactory.CreateMessage(
            ChatId,
            UserId,
            chatTitle: "GIF chat",
            username: "gif-user",
            animation: new Animation { FileId = "gif-file-id" });
        _databaseService
            .When(service => service.SaveGifData(ChatId, Arg.Any<GifData>()))
            .Do(_ => throw new InvalidOperationException("boom"));

        await service.ProcessUpdate(TelegramMessageFactory.CreateUpdate(message: message));

        await _notificationService.Received(1).SendErrorNotification(
            Arg.Is<string>(text => text.Contains("Error saving GIF data")),
            Constants.Name,
            message);
    }

    [Test]
    public async Task ProcessUpdate_WhenBotIsAddedToChat_SendsWelcomeMessage()
    {
        _databaseService.GetChatLanguage<ChatSettings>(ChatId).Returns("en");
        _localizationService.GetLocalizedString(Constants.Name, "Welcome", "en").Returns("Welcome!");
        var service = CreateService([], []);
        var message = TelegramMessageFactory.CreateMessage(
            ChatId,
            UserId,
            chatTitle: "Group Chat",
            username: "gif-user",
            chatType: ChatType.Group,
            newChatMembers: [TelegramMessageFactory.CreateUser(BotUserId, "testbot")]);

        await service.ProcessUpdate(TelegramMessageFactory.CreateUpdate(message: message));

        var request = GetSingleRequest(_client, "SendMessageRequest");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(GetPropertyValue(request, "ChatId")?.ToString(), Is.EqualTo(ChatId.ToString()));
            Assert.That(GetPropertyValue(request, "Text"), Is.EqualTo("Welcome!"));
        }
    }

    private TestBotUpdateService CreateService(
        IEnumerable<ICommandHandler> commandHandlers,
        IEnumerable<ICallbackHandler> callbackHandlers)
    {
        return new TestBotUpdateService(
            _botService,
            Substitute.For<ILogger<TestBotUpdateService>>(),
            _databaseService,
            _markupService,
            _localizationService,
            _options,
            _notificationService,
            commandHandlers,
            callbackHandlers);
    }
}
