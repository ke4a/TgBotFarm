using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.TestKit;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot;
using Telegram.Bot.Types;
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

        _databaseService.GetChatLanguage<Models.ChatSettings>(TestChatId).Returns(expectedLanguage);
        _localizationService.GetLocalizedString(TestBotName, "Welcome", expectedLanguage).Returns(welcomeMessage);

        // Act
        await _service.TestWelcome(TestChatId);

        // Assert
        var sendMessageRequest = GetSingleRequest(_mockClient, "SendMessageRequest");

        await _databaseService.Received(1).GetChatLanguage<Models.ChatSettings>(TestChatId);
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

    private class TestUpdateService : UpdateService
    {
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

        public override Task ProcessUpdate(Update update)
        {
            return Task.CompletedTask;
        }

        public async Task TestSetLanguage(string callbackId, Message message, User user, string newLanguage)
        {
            await SetLanguage<TestChatSettings>(callbackId, message, user, newLanguage);
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