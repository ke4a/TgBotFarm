using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.TestKit;
using Microsoft.Extensions.Logging;
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
    private INotificationService _notificationService;
    private TelegramBotClient _client;

    [SetUp]
    public void SetUp()
    {
        _botService = Substitute.For<IBotService>();
        _databaseService = Substitute.For<ITestBotDatabaseService>();
        _markupService = Substitute.For<ITestBotMarkupService>();
        _localizationService = Substitute.For<ILocalizationService>();
        _notificationService = Substitute.For<INotificationService>();
        _client = TelegramBotClientFactory.CreateSubstitute();

        _botService.Client.Returns(_client);
        _botService.Me.Returns(new User { Id = BotUserId, Username = "testbot" });
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
    public async Task ProcessUpdate_WithGifWithoutSenderUser_SkipsSavingWithoutError()
    {
        var service = CreateService([], []);
        var message = TelegramMessageFactory.CreateMessage(
            ChatId,
            UserId,
            animation: new Animation { FileId = "gif-file-id" });
        message.From = null;

        await service.ProcessUpdate(TelegramMessageFactory.CreateUpdate(message: message));

        await _databaseService.DidNotReceiveWithAnyArgs().SaveGifData(default, default!);
        await _notificationService.DidNotReceiveWithAnyArgs().SendErrorNotification(default!, default!, default);
    }

    [Test]
    public async Task ProcessUpdate_WithGroupCommandAddressedToBotUsername_DispatchesWithoutSuffix()
    {
        var commandHandler = Substitute.For<ICommandHandler>();
        commandHandler.Command.Returns(Constants.Commands.GetLastGif);
        _databaseService.GetChatLanguage<TestBotChatSettings>(ChatId).Returns("en");
        var service = CreateService([commandHandler], []);
        var message = CreateCommandMessage($"{Constants.Commands.GetLastGif}@testbot", ChatType.Group);

        await service.ProcessUpdate(TelegramMessageFactory.CreateUpdate(message: message));

        await commandHandler.Received(1).Handle(message, "en");
    }

    [Test]
    public async Task ProcessUpdate_WithLanguageSetCallback_SavesLanguageAndAnswersCallback()
    {
        _databaseService.GetChatLanguage<TestBotChatSettings>(ChatId).Returns("en");
        var service = CreateService([], []);
        var message = TelegramMessageFactory.CreateMessage(ChatId, UserId, messageId: 5);
        var callbackQuery = TelegramMessageFactory.CreateCallbackQuery(
            "callback-1", $"{BotFarm.Core.Constants.Callbacks.LanguageSet}:uk-UA", message);

        await service.ProcessUpdate(TelegramMessageFactory.CreateUpdate(callbackQuery: callbackQuery));

        await _databaseService.Received(1).SetChatLanguage<TestBotChatSettings>(ChatId, "uk-UA");
        var answer = GetSingleRequest(_client, "AnswerCallbackQueryRequest");
        Assert.That(GetPropertyValue(answer, "CallbackQueryId"), Is.EqualTo("callback-1"));
    }

    [Test]
    public async Task ProcessUpdate_WithGif_SavesItForTheSender()
    {
        var service = CreateService([], []);
        var message = TelegramMessageFactory.CreateMessage(
            ChatId,
            UserId,
            animation: new Animation { FileId = "gif-file-id" });

        await service.ProcessUpdate(TelegramMessageFactory.CreateUpdate(message: message));

        await _databaseService.Received(1).SaveGifData(
            ChatId,
            Arg.Is<GifData>(gif => gif.FileId == "gif-file-id" && gif.UserId == UserId));
        await _notificationService.DidNotReceiveWithAnyArgs().SendErrorNotification(default!, default!, default);
    }

    private static Message CreateCommandMessage(string text, ChatType chatType)
    {
        return TelegramMessageFactory.CreateMessage(
            ChatId,
            UserId,
            text: text,
            chatType: chatType,
            entities: [new MessageEntity { Type = MessageEntityType.BotCommand, Offset = 0, Length = text.Length }]);
    }

    private TestBotUpdateService CreateService(
        IEnumerable<ICommandHandler> commandHandlers,
        IEnumerable<ICallbackHandler> callbackHandlers)
    {
        return new TestBotUpdateService(
            _botService,
            Substitute.For<IBotControlService>(),
            Substitute.For<ILogger<TestBotUpdateService>>(),
            _databaseService,
            _markupService,
            _localizationService,
            _notificationService,
            commandHandlers,
            callbackHandlers);
    }
}