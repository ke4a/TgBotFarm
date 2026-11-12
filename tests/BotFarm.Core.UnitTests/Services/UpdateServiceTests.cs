using BotFarm.Core.Abstractions;
using BotFarm.Core.Base;
using BotFarm.Core.Models;
using BotFarm.TestKit;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static BotFarm.TestKit.TelegramRequestAssertHelpers;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class UpdateServiceTests
{
    private TestUpdateService _service;
    private IBotService _botService;
    private IBotControlService _botControlService;
    private ILogger _logger;
    private IDatabaseService _databaseService;
    private ILocalizationService _localizationService;
    private IMarkupService _markupService;
    private TelegramBotClient _mockClient;

    private const long TestChatId = 12345;
    private const long TestUserId = 67890;
    private const string TestBotName = "TestBot";
    private const long BotUserId = 54321;

    [SetUp]
    public void SetUp()
    {
        _botService = Substitute.For<IBotService>();
        _botControlService = Substitute.For<IBotControlService>();
        _logger = Substitute.For<ILogger>();
        _databaseService = Substitute.For<IDatabaseService>();
        _localizationService = Substitute.For<ILocalizationService>();
        _markupService = Substitute.For<IMarkupService>();
        _mockClient = TelegramBotClientFactory.CreateSubstitute();

        _botService.Client.Returns(_mockClient);
        _botService.Me.Returns(new User { Id = BotUserId, Username = "testbot" });

        _service = new TestUpdateService(
            _botService,
            _botControlService,
            _logger,
            _databaseService,
            _localizationService,
            _markupService);
    }

    [Test]
    public void Constructor_WithValidDependencies_CreatesInstance()
    {
        // Assert
        Assert.That(_service, Is.Not.Null);
        Assert.That(_service.Name, Is.EqualTo(TestBotName));
    }

    [Test]
    public void Name_ReturnsCorrectValue()
    {
        // Act
        var name = _service.Name;

        // Assert
        Assert.That(name, Is.EqualTo(TestBotName));
    }

    [Test]
    public async Task SetLanguage_UpdatesLanguageInDatabase()
    {
        // Arrange
        const string callbackId = "callback-123";
        const string newLanguage = "es-ES";
        const string localizedString = "I now speak Spanish";
        var message = TelegramMessageFactory.CreateMessage(TestChatId, TestUserId);
        var user = TelegramMessageFactory.CreateUser(TestUserId);

        _localizationService.GetAvailableLanguages(TestBotName).Returns(["en-US", newLanguage]);
        _localizationService.GetLocalizedString(TestBotName, "NowISpeak", newLanguage).Returns(localizedString);
        _databaseService.SetChatLanguage<TestChatSettings>(Arg.Any<long>(), Arg.Any<string>())
            .Returns(Task.CompletedTask);

        // Act
        await _service.TestSetLanguage(callbackId, message, user, newLanguage);

        // Assert
        var editRequest = GetSingleRequest(_mockClient, "EditMessageTextRequest");
        var callbackRequest = GetSingleRequest(_mockClient, "AnswerCallbackQueryRequest");

        await _databaseService.Received(1).SetChatLanguage<TestChatSettings>(TestChatId, newLanguage);
        _localizationService.Received(1).GetLocalizedString(TestBotName, "NowISpeak", newLanguage);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(GetPropertyValue(editRequest, "ChatId")?.ToString(), Is.EqualTo(TestChatId.ToString()));
            Assert.That(GetPropertyValue(editRequest, "MessageId"), Is.EqualTo(message.MessageId));
            Assert.That(GetPropertyValue(editRequest, "Text"), Is.EqualTo(localizedString));
            Assert.That(GetPropertyValue(callbackRequest, "CallbackQueryId"), Is.EqualTo(callbackId));
        }
    }

    [Test]
    public async Task Welcome_SendsWelcomeMessageWithCorrectLanguage()
    {
        // Arrange
        const string expectedLanguage = "en-US";
        const string welcomeMessage = "Welcome to the bot!";

        _databaseService.GetChatLanguage<TestChatSettings>(TestChatId).Returns(expectedLanguage);
        _localizationService.GetLocalizedString(TestBotName, "Welcome", expectedLanguage).Returns(welcomeMessage);

        // Act
        await _service.TestWelcome(TestChatId);

        // Assert
        var sendMessageRequest = GetSingleRequest(_mockClient, "SendMessageRequest");

        await _databaseService.Received(1).GetChatLanguage<TestChatSettings>(TestChatId);
        _localizationService.Received(1).GetLocalizedString(TestBotName, "Welcome", expectedLanguage);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(GetPropertyValue(sendMessageRequest, "ChatId")?.ToString(), Is.EqualTo(TestChatId.ToString()));
            Assert.That(GetPropertyValue(sendMessageRequest, "Text"), Is.EqualTo(welcomeMessage));
        }
    }

    [Test]
    public async Task HandleCommand_WithRegisteredHandler_InvokesHandlerWithMessageAndLanguage()
    {
        // Arrange
        const string language = "en-US";
        var message = TelegramMessageFactory.CreateMessage(TestChatId, TestUserId);
        var handler = Substitute.For<ICommandHandler>();
        handler.Command.Returns("/test");
        var service = new TestUpdateService(_botService, _botControlService, _logger, _databaseService,
            _localizationService, _markupService, [handler], []);

        // Act
        await service.TestHandleCommand("/test", message, language);

        // Assert
        await handler.Received(1).Handle(message, language);
    }

    [Test]
    public void HandleCommand_WithNoRegisteredHandler_DoesNothing()
    {
        // Arrange
        var service = new TestUpdateService(_botService, _botControlService, _logger, _databaseService,
            _localizationService, _markupService, [], []);
        var message = TelegramMessageFactory.CreateMessage(TestChatId, TestUserId);

        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await service.TestHandleCommand("/unknown", message, "en-US"));
    }

    [Test]
    public async Task HandleCallback_WithRegisteredHandler_InvokesHandlerWithAllArguments()
    {
        // Arrange
        const string language = "en-US";
        const string callbackId = "callback-1";
        const string parameter = "yes";
        var message = TelegramMessageFactory.CreateMessage(TestChatId, TestUserId);
        var user = TelegramMessageFactory.CreateUser(TestUserId);
        var handler = Substitute.For<ICallbackHandler>();
        handler.CallbackKey.Returns("test-callback");
        var service = new TestUpdateService(_botService, _botControlService, _logger, _databaseService,
            _localizationService, _markupService, [], [handler]);

        // Act
        await service.TestHandleCallback("test-callback", callbackId, message, user, parameter, language);

        // Assert
        await handler.Received(1).Handle(callbackId, message, user, parameter, language);
    }

    [Test]
    public void HandleCallback_WithNoRegisteredHandler_DoesNothing()
    {
        // Arrange
        var service = new TestUpdateService(_botService, _botControlService, _logger, _databaseService,
            _localizationService, _markupService, [], []);
        var message = TelegramMessageFactory.CreateMessage(TestChatId, TestUserId);
        var user = TelegramMessageFactory.CreateUser(TestUserId);

        // Act & Assert
        Assert.DoesNotThrowAsync(async () =>
            await service.TestHandleCallback("unknown-callback", "callback-1", message, user, "param", "en-US"));
    }

    [TestCase(null)]
    [TestCase("")]
    public void TryParseCallbackData_WithNullOrEmptyData_ReturnsFalse(string? data)
    {
        var parsed = TestUpdateService.TestTryParseCallbackData(data, out var key, out var parameter);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed, Is.False);
            Assert.That(key, Is.Empty);
            Assert.That(parameter, Is.Empty);
        }
    }

    [TestCase("language-set", "language-set", "")]
    [TestCase("language-set:", "language-set", "")]
    [TestCase("language-set:uk", "language-set", "uk")]
    [TestCase("custom:a:b:c", "custom", "a:b:c")]
    [TestCase(":value", "", "value")]
    public void TryParseCallbackData_SplitsAtFirstColon(string data, string expectedKey, string expectedParameter)
    {
        var parsed = TestUpdateService.TestTryParseCallbackData(data, out var key, out var parameter);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed, Is.True);
            Assert.That(key, Is.EqualTo(expectedKey));
            Assert.That(parameter, Is.EqualTo(expectedParameter));
        }
    }

    [Test]
    public async Task ChangeLanguage_WithoutSenderUser_SendsLanguagePrompt()
    {
        var message = TelegramMessageFactory.CreateMessage(TestChatId, TestUserId);
        message.From = null;
        _localizationService.GetLocalizedString(TestBotName, "ChooseLanguage", "en").Returns("Choose");

        await _service.TestChangeLanguage(message, "en");

        var request = GetSingleRequest(_mockClient, "SendMessageRequest");
        Assert.That(GetPropertyValue(request, "Text"), Is.EqualTo("Choose"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void CanProcessUpdates_AsksTheControlServiceForThisBot(bool canProcess)
    {
        _botControlService.CanProcessUpdates(TestBotName).Returns(canProcess);

        Assert.That(_service.CanProcessUpdates, Is.EqualTo(canProcess));
    }

    [Test]
    public async Task ChangeLanguage_RepliesToMessageWithLanguageKeyboard()
    {
        var message = TelegramMessageFactory.CreateMessage(TestChatId, TestUserId, messageId: 77);
        var markup = new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup();
        _markupService.GenerateChangeLanguageMarkup(TestBotName).Returns(markup);
        _localizationService.GetLocalizedString(TestBotName, "ChooseLanguage", "en").Returns("Choose");

        await _service.TestChangeLanguage(message, "en");

        var request = GetSingleRequest(_mockClient, "SendMessageRequest");
        var replyParameters = GetPropertyValue(request, "ReplyParameters") as ReplyParameters;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(GetPropertyValue(request, "ChatId")?.ToString(), Is.EqualTo(TestChatId.ToString()));
            Assert.That(GetPropertyValue(request, "ReplyMarkup"), Is.SameAs(markup));
            Assert.That(replyParameters?.MessageId, Is.EqualTo(77));
        }
    }

    [Test]
    public void Constructor_WithDuplicateCommandHandlers_Throws()
    {
        var first = Substitute.For<ICommandHandler>();
        first.Command.Returns("/test");
        var second = Substitute.For<ICommandHandler>();
        second.Command.Returns("/test");

        Assert.Throws<ArgumentException>(() => new TestUpdateService(_botService, _botControlService, _logger,
            _databaseService, _localizationService, _markupService, [first, second], []));
    }

    [Test]
    public async Task ProcessUpdate_WithCommandInPrivateChat_DispatchesToRegisteredHandler()
    {
        var handler = CreateCommandHandler("/test");
        var service = CreateService([handler], []);
        _databaseService.GetChatLanguage<TestChatSettings>(TestChatId).Returns("uk");
        var message = CreateCommandMessage("/test", ChatType.Private);

        await service.ProcessUpdate(TelegramMessageFactory.CreateUpdate(message: message));

        await handler.Received(1).Handle(message, "uk");
        Assert.That(service.OnUpdateCalls, Is.Empty);
    }

    [TestCase("/test@testbot")]
    [TestCase("/test@TestBot")]
    public async Task ProcessUpdate_WithGroupCommandAddressedToThisBot_DispatchesWithoutUsernameSuffix(string text)
    {
        var handler = CreateCommandHandler("/test");
        var service = CreateService([handler], []);
        _databaseService.GetChatLanguage<TestChatSettings>(TestChatId).Returns("en");
        var message = CreateCommandMessage(text, ChatType.Group);

        await service.ProcessUpdate(TelegramMessageFactory.CreateUpdate(message: message));

        await handler.Received(1).Handle(message, "en");
    }

    [TestCase("/test")]
    [TestCase("/test@otherbot")]
    [TestCase("/test@mytestbot")]
    public async Task ProcessUpdate_WithGroupCommandNotAddressedToThisBot_PassesItToOnUpdate(string text)
    {
        var handler = CreateCommandHandler("/test");
        var service = CreateService([handler], []);
        var update = TelegramMessageFactory.CreateUpdate(message: CreateCommandMessage(text, ChatType.Supergroup));

        await service.ProcessUpdate(update);

        await handler.DidNotReceiveWithAnyArgs().Handle(default!, default!);
        await _databaseService.DidNotReceiveWithAnyArgs().GetChatLanguage<TestChatSettings>(default);
        Assert.That(service.OnUpdateCalls, Is.EqualTo(new[] { update }));
    }

    [Test]
    public async Task ProcessUpdate_WithGroupCommandWhenBotUserIsUnknown_DoesNotDispatch()
    {
        _botService.Me.Returns((User)null!);
        var handler = CreateCommandHandler("/test");
        var service = CreateService([handler], []);

        await service.ProcessUpdate(TelegramMessageFactory.CreateUpdate(
            message: CreateCommandMessage("/test@testbot", ChatType.Group)));

        await handler.DidNotReceiveWithAnyArgs().Handle(default!, default!);
    }

    [Test]
    public async Task ProcessUpdate_WithPrivateCommandWhenBotUserIsUnknown_Dispatches()
    {
        _botService.Me.Returns((User)null!);
        var handler = CreateCommandHandler("/test");
        var service = CreateService([handler], []);
        _databaseService.GetChatLanguage<TestChatSettings>(TestChatId).Returns("en");
        var message = CreateCommandMessage("/test", ChatType.Private);

        await service.ProcessUpdate(TelegramMessageFactory.CreateUpdate(message: message));

        await handler.Received(1).Handle(message, "en");
    }

    [Test]
    public async Task ProcessUpdate_WithCommandNotAtStartOfMessage_PassesItToOnUpdate()
    {
        var handler = CreateCommandHandler("/test");
        var service = CreateService([handler], []);
        var message = TelegramMessageFactory.CreateMessage(TestChatId, TestUserId, text: "hi /test",
            entities: [new MessageEntity { Type = MessageEntityType.BotCommand, Offset = 3, Length = 5 }]);
        var update = TelegramMessageFactory.CreateUpdate(message: message);

        await service.ProcessUpdate(update);

        await handler.DidNotReceiveWithAnyArgs().Handle(default!, default!);
        Assert.That(service.OnUpdateCalls, Is.EqualTo(new[] { update }));
    }

    [Test]
    public async Task ProcessUpdate_WithCommandEntityButNoText_PassesItToOnUpdate()
    {
        var handler = CreateCommandHandler("/test");
        var service = CreateService([handler], []);
        var message = TelegramMessageFactory.CreateMessage(TestChatId, TestUserId,
            entities: [new MessageEntity { Type = MessageEntityType.BotCommand, Offset = 0, Length = 5 }]);
        var update = TelegramMessageFactory.CreateUpdate(message: message);

        await service.ProcessUpdate(update);

        await handler.DidNotReceiveWithAnyArgs().Handle(default!, default!);
        Assert.That(service.OnUpdateCalls, Is.EqualTo(new[] { update }));
    }

    [Test]
    public async Task ProcessUpdate_WithUnknownCommand_DoesNothing()
    {
        var handler = CreateCommandHandler("/test");
        var service = CreateService([handler], []);
        _databaseService.GetChatLanguage<TestChatSettings>(TestChatId).Returns("en");

        await service.ProcessUpdate(TelegramMessageFactory.CreateUpdate(
            message: CreateCommandMessage("/unknown", ChatType.Private)));

        await handler.DidNotReceiveWithAnyArgs().Handle(default!, default!);
        Assert.That(_mockClient.ReceivedCalls(), Is.Empty);
        Assert.That(service.OnUpdateCalls, Is.Empty);
    }

    [Test]
    public async Task ProcessUpdate_WithStartCommand_SendsWelcomeInChatLanguage()
    {
        _databaseService.GetChatLanguage<TestChatSettings>(TestChatId).Returns("uk");
        _localizationService.GetLocalizedString(TestBotName, "Welcome", "uk").Returns("Welcome!");

        await _service.ProcessUpdate(TelegramMessageFactory.CreateUpdate(
            message: CreateCommandMessage(Constants.Commands.Start, ChatType.Private)));

        var request = GetSingleRequest(_mockClient, "SendMessageRequest");
        Assert.That(GetPropertyValue(request, "Text"), Is.EqualTo("Welcome!"));
    }

    [Test]
    public async Task ProcessUpdate_WithChangeLanguageCommand_SendsLanguagePromptInChatLanguage()
    {
        _databaseService.GetChatLanguage<TestChatSettings>(TestChatId).Returns("uk");
        _localizationService.GetLocalizedString(TestBotName, "ChooseLanguage", "uk").Returns("Choose");

        await _service.ProcessUpdate(TelegramMessageFactory.CreateUpdate(
            message: CreateCommandMessage(Constants.Commands.ChangeLanguage, ChatType.Private)));

        var request = GetSingleRequest(_mockClient, "SendMessageRequest");
        Assert.That(GetPropertyValue(request, "Text"), Is.EqualTo("Choose"));
    }

    [Test]
    public async Task SetLanguage_WithUnsupportedLanguage_DoesNotSaveItAndAnswersCallback()
    {
        var message = TelegramMessageFactory.CreateMessage(TestChatId, TestUserId);
        var user = TelegramMessageFactory.CreateUser(TestUserId);
        _localizationService.GetAvailableLanguages(TestBotName).Returns(["en-US", "uk-UA"]);

        await _service.TestSetLanguage("callback-1", message, user, "zz");

        await _databaseService.DidNotReceiveWithAnyArgs().SetChatLanguage<TestChatSettings>(default, default!);
        var answer = GetSingleRequest(_mockClient, "AnswerCallbackQueryRequest");
        Assert.That(GetPropertyValue(answer, "CallbackQueryId"), Is.EqualTo("callback-1"));
    }

    [Test]
    public async Task SetLanguage_MatchesSupportedLanguageIgnoringCase()
    {
        var message = TelegramMessageFactory.CreateMessage(TestChatId, TestUserId);
        var user = TelegramMessageFactory.CreateUser(TestUserId);
        _localizationService.GetAvailableLanguages(TestBotName).Returns(["en-US", "uk-UA"]);

        await _service.TestSetLanguage("callback-1", message, user, "UK-ua");

        await _databaseService.Received(1).SetChatLanguage<TestChatSettings>(TestChatId, "UK-ua");
    }

    [Test]
    public async Task ProcessUpdate_WithLanguageSetCallback_SavesLanguageAndAnswersCallback()
    {
        var message = TelegramMessageFactory.CreateMessage(TestChatId, TestUserId, messageId: 5);
        var callbackQuery = TelegramMessageFactory.CreateCallbackQuery(
            "callback-1", $"{Constants.Callbacks.LanguageSet}:uk-UA", message);
        _localizationService.GetAvailableLanguages(TestBotName).Returns(["en-US", "uk-UA"]);

        await _service.ProcessUpdate(TelegramMessageFactory.CreateUpdate(callbackQuery: callbackQuery));

        await _databaseService.Received(1).SetChatLanguage<TestChatSettings>(TestChatId, "uk-UA");
        var answer = GetSingleRequest(_mockClient, "AnswerCallbackQueryRequest");
        Assert.That(GetPropertyValue(answer, "CallbackQueryId"), Is.EqualTo("callback-1"));
    }

    [TestCase("custom:yes", "yes")]
    [TestCase("custom", "")]
    public async Task ProcessUpdate_WithCallbackQuery_DispatchesToRegisteredHandler(string data,
        string expectedParameter)
    {
        var handler = Substitute.For<ICallbackHandler>();
        handler.CallbackKey.Returns("custom");
        var service = CreateService([], [handler]);
        _databaseService.GetChatLanguage<TestChatSettings>(TestChatId).Returns("uk");
        var message = TelegramMessageFactory.CreateMessage(TestChatId, TestUserId);
        var user = TelegramMessageFactory.CreateUser(TestUserId);

        await service.ProcessUpdate(TelegramMessageFactory.CreateUpdate(
            callbackQuery: TelegramMessageFactory.CreateCallbackQuery("callback-1", data, message, user)));

        await handler.Received(1).Handle("callback-1", message, user, expectedParameter, "uk");
    }

    [Test]
    public async Task ProcessUpdate_WithCallbackQueryWithoutMessage_IgnoresIt()
    {
        var handler = Substitute.For<ICallbackHandler>();
        handler.CallbackKey.Returns("custom");
        var service = CreateService([], [handler]);
        var callbackQuery = TelegramMessageFactory.CreateCallbackQuery("callback-1", "custom:yes");
        callbackQuery.Message = null;

        await service.ProcessUpdate(TelegramMessageFactory.CreateUpdate(callbackQuery: callbackQuery));

        await handler.DidNotReceiveWithAnyArgs().Handle(default!, default!, default!, default!, default!);
        await _databaseService.DidNotReceiveWithAnyArgs().GetChatLanguage<TestChatSettings>(default);
        Assert.That(service.OnUpdateCalls, Is.Empty);
    }

    [TestCase(null)]
    [TestCase("")]
    public async Task ProcessUpdate_WithCallbackQueryWithoutData_IgnoresIt(string? data)
    {
        var handler = Substitute.For<ICallbackHandler>();
        handler.CallbackKey.Returns("custom");
        var service = CreateService([], [handler]);
        var callbackQuery = TelegramMessageFactory.CreateCallbackQuery("callback-1", "custom:yes");
        callbackQuery.Data = data;

        await service.ProcessUpdate(TelegramMessageFactory.CreateUpdate(callbackQuery: callbackQuery));

        await handler.DidNotReceiveWithAnyArgs().Handle(default!, default!, default!, default!, default!);
        Assert.That(service.OnUpdateCalls, Is.Empty);
    }

    [Test]
    public async Task ProcessUpdate_WhenBotIsAddedToChat_SendsWelcomeMessage()
    {
        _databaseService.GetChatLanguage<TestChatSettings>(TestChatId).Returns("en");
        _localizationService.GetLocalizedString(TestBotName, "Welcome", "en").Returns("Welcome!");
        var message = TelegramMessageFactory.CreateMessage(TestChatId, TestUserId, chatType: ChatType.Group,
            newChatMembers: [TelegramMessageFactory.CreateUser(BotUserId, "testbot")]);

        await _service.ProcessUpdate(TelegramMessageFactory.CreateUpdate(message: message));

        var request = GetSingleRequest(_mockClient, "SendMessageRequest");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(GetPropertyValue(request, "ChatId")?.ToString(), Is.EqualTo(TestChatId.ToString()));
            Assert.That(GetPropertyValue(request, "Text"), Is.EqualTo("Welcome!"));
            Assert.That(_service.OnUpdateCalls, Is.Empty);
        }
    }

    [Test]
    public async Task ProcessUpdate_WhenOtherMembersJoin_PassesItToOnUpdate()
    {
        var message = TelegramMessageFactory.CreateMessage(TestChatId, TestUserId, chatType: ChatType.Group,
            newChatMembers: [TelegramMessageFactory.CreateUser(TestUserId)]);
        var update = TelegramMessageFactory.CreateUpdate(message: message);

        await _service.ProcessUpdate(update);

        Assert.That(_mockClient.ReceivedCalls(), Is.Empty);
        Assert.That(_service.OnUpdateCalls, Is.EqualTo(new[] { update }));
    }

    [Test]
    public async Task ProcessUpdate_WithOtherMessage_PassesItToOnUpdate()
    {
        var message = TelegramMessageFactory.CreateMessage(TestChatId, TestUserId,
            animation: new Animation { FileId = "gif-file-id" });
        var update = TelegramMessageFactory.CreateUpdate(message: message);

        await _service.ProcessUpdate(update);

        Assert.That(_service.OnUpdateCalls, Is.EqualTo(new[] { update }));
    }

    [Test]
    public async Task ProcessUpdate_WithOtherUpdateType_PassesItToOnUpdate()
    {
        var update = new Update { Id = 1, EditedMessage = TelegramMessageFactory.CreateMessage(TestChatId, TestUserId) };

        await _service.ProcessUpdate(update);

        Assert.That(_service.OnUpdateCalls, Is.EqualTo(new[] { update }));
    }

    [Test]
    public async Task ProcessUpdate_WithMigrateFromChatIdMessage_MovesDataThenCallsHookOnce()
    {
        const long newChatId = -1001234567890;
        RecordDataMoves(TestChatId, newChatId);
        var message = TelegramMessageFactory.CreateMessage(newChatId, TestUserId, chatType: ChatType.Supergroup);
        message.MigrateFromChatId = TestChatId;

        await _service.ProcessUpdate(TelegramMessageFactory.CreateUpdate(message: message));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_service.MigrationEvents,
                Is.EqualTo(new[] { $"move:{TestChatId}:{newChatId}", $"hook:{TestChatId}:{newChatId}" }));
            Assert.That(_service.OnUpdateCalls, Is.Empty);
        }
    }

    [Test]
    public async Task ProcessUpdate_WithMigrateToChatIdMessage_PassesItToOnUpdateWithoutMigrating()
    {
        const long newChatId = -1001234567890;
        var message = TelegramMessageFactory.CreateMessage(TestChatId, TestUserId, chatType: ChatType.Group);
        message.MigrateToChatId = newChatId;
        var update = TelegramMessageFactory.CreateUpdate(message: message);

        await _service.ProcessUpdate(update);

        await _databaseService.DidNotReceiveWithAnyArgs().MigrateChat(default, default);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_service.MigrationEvents, Is.Empty);
            Assert.That(_service.OnUpdateCalls, Is.EqualTo(new[] { update }));
        }
    }

    [Test]
    public async Task ProcessUpdate_WhenChatDataMoveFails_LogsErrorRethrowsAndSkipsHook()
    {
        const long newChatId = -1001234567890;
        var failure = new InvalidOperationException("migration failed");
        _databaseService.MigrateChat(TestChatId, newChatId).Returns<Task>(_ => throw failure);
        var message = TelegramMessageFactory.CreateMessage(newChatId, TestUserId, chatType: ChatType.Supergroup);
        message.MigrateFromChatId = TestChatId;

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ProcessUpdate(TelegramMessageFactory.CreateUpdate(message: message)));

        var errorLogs = _logger.ReceivedCalls()
            .Select(call => call.GetArguments())
            .Where(args => args.Length == 5 && args[0] is LogLevel.Error)
            .ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(thrown, Is.SameAs(failure));
            Assert.That(errorLogs, Has.Count.EqualTo(1));
            Assert.That(errorLogs[0][2]?.ToString(), Does.Contain(TestChatId.ToString()));
            Assert.That(errorLogs[0][2]?.ToString(), Does.Contain(newChatId.ToString()));
            Assert.That(errorLogs[0][3], Is.SameAs(failure));
            Assert.That(_service.OnUpdateCalls, Is.Empty);
            Assert.That(_service.MigrationEvents, Is.Empty);
        }
    }

    [Test]
    public async Task ProcessUpdate_WithOrdinaryMessage_DoesNotMigrateChat()
    {
        var message = TelegramMessageFactory.CreateMessage(TestChatId, TestUserId, text: "hello",
            chatType: ChatType.Group);

        await _service.ProcessUpdate(TelegramMessageFactory.CreateUpdate(message: message));

        await _databaseService.DidNotReceiveWithAnyArgs().MigrateChat(default, default);
    }

    private void RecordDataMoves(long fromChatId, long toChatId)
    {
        _databaseService.MigrateChat(fromChatId, toChatId).Returns(_ =>
        {
            _service.MigrationEvents.Add($"move:{fromChatId}:{toChatId}");
            return Task.CompletedTask;
        });
    }

    private TestUpdateService CreateService(IEnumerable<ICommandHandler> commandHandlers,
        IEnumerable<ICallbackHandler> callbackHandlers)
    {
        return new TestUpdateService(_botService, _botControlService, _logger, _databaseService,
            _localizationService, _markupService, commandHandlers, callbackHandlers);
    }

    private static ICommandHandler CreateCommandHandler(string command)
    {
        var handler = Substitute.For<ICommandHandler>();
        handler.Command.Returns(command);
        return handler;
    }

    private static Message CreateCommandMessage(string text, ChatType chatType)
    {
        return TelegramMessageFactory.CreateMessage(
            TestChatId,
            TestUserId,
            text: text,
            chatType: chatType,
            entities: [new MessageEntity { Type = MessageEntityType.BotCommand, Offset = 0, Length = text.Length }]);
    }

    private class TestUpdateService : UpdateService<TestChatSettings>
    {
        public List<Update> OnUpdateCalls { get; } = [];

        public List<string> MigrationEvents { get; } = [];

        public TestUpdateService(
            IBotService botService,
            IBotControlService botControlService,
            ILogger logger,
            IDatabaseService databaseService,
            ILocalizationService localizationService,
            IMarkupService markupService,
            IEnumerable<ICommandHandler>? commandHandlers = null,
            IEnumerable<ICallbackHandler>? callbackHandlers = null)
            : base(new BotIdentity(TestBotName), botService, botControlService, logger, databaseService,
                localizationService, markupService, commandHandlers, callbackHandlers)
        {
        }

        protected override Task OnUpdate(Update update)
        {
            OnUpdateCalls.Add(update);
            return Task.CompletedTask;
        }

        protected override Task OnChatMigrated(long fromChatId, long toChatId)
        {
            MigrationEvents.Add($"hook:{fromChatId}:{toChatId}");
            return Task.CompletedTask;
        }

        public async Task TestSetLanguage(string callbackId, Message message, User user, string newLanguage)
        {
            await SetLanguage(callbackId, message, user, newLanguage);
        }

        public static bool TestTryParseCallbackData(string? data, out string key, out string parameter)
        {
            return TryParseCallbackData(data, out key, out parameter);
        }

        public async Task TestChangeLanguage(Message message, string language)
        {
            await ChangeLanguage(message, language);
        }

        public async Task TestWelcome(long chatId)
        {
            await Welcome(chatId);
        }

        public async Task TestHandleCommand(string command, Message message, string language)
        {
            await HandleCommand(command, message, language);
        }

        public async Task TestHandleCallback(string callbackKey, string callbackId, Message message, User user,
            string parameter, string language)
        {
            await HandleCallback(callbackKey, callbackId, message, user, parameter, language);
        }
    }

    private class TestChatSettings : Models.ChatSettings
    {
    }
}