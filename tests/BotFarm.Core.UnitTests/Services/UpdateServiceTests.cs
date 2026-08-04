using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Core.UnitTests.TestHelpers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot;
using Telegram.Bot.Types;
using static BotFarm.Core.UnitTests.TestHelpers.TelegramRequestAssertHelpers;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class UpdateServiceTests
{
    private TestUpdateService _service;
    private IBotService _botService;
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
        _logger = Substitute.For<ILogger>();
        _databaseService = Substitute.For<IDatabaseService>();
        _localizationService = Substitute.For<ILocalizationService>();
        _markupService = Substitute.For<IMarkupService>();
        _mockClient = Substitute.For<TelegramBotClient>("123456789:test", null, CancellationToken.None);

        _botService.Client.Returns(_mockClient);

        _service = new TestUpdateService(
            _botService,
            _logger,
            _databaseService,
            _localizationService,
            _markupService);
    }

    [Test]
    public void ServiceConstruction_WithValidDependencies_CreatesInstance()
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
        var message = CreateTestMessage();
        var user = new User { Id = TestUserId, Username = "testuser" };

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

    private Message CreateTestMessage()
    {
        return new Message
        {
            Chat = new Chat { Id = TestChatId, Title = "Test Chat" },
            From = new User { Id = TestUserId, Username = "testuser" },
            Date = DateTime.UtcNow
        };
    }

    private class TestUpdateService : UpdateService
    {
        public TestUpdateService(
            IBotService botService,
            ILogger logger,
            IDatabaseService databaseService,
            ILocalizationService localizationService,
            IMarkupService markupService)
            : base(new BotIdentity(TestBotName), botService, logger, databaseService, localizationService, markupService)
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

        public async Task TestWelcome(long chatId)
        {
            await Welcome(chatId);
        }
    }

    private class TestChatSettings : Models.ChatSettings
    {
    }
}
