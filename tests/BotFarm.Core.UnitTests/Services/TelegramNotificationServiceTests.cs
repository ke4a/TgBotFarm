using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using Microsoft.Extensions.Options;
using NSubstitute;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static BotFarm.TestKit.TelegramRequestAssertHelpers;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class TelegramNotificationServiceTests
{
    private const string TestBotName = "TestBot";
    private const long AdminChatId = 987654321;

    private TelegramNotificationService _service;
    private IBotRegistry _botRegistry;
    private IOptionsMonitor<BotConfig> _optionsMonitor;
    private IBotService _botService;
    private TelegramBotClient _client;

    [SetUp]
    public void SetUp()
    {
        _botRegistry = Substitute.For<IBotRegistry>();
        _optionsMonitor = Substitute.For<IOptionsMonitor<BotConfig>>();
        _botService = Substitute.For<IBotService>();
        _client = Substitute.For<TelegramBotClient>("111111111:AAAAAbAAAAbbAAbbAAAbAbAAbbb_bAAbAb1", null, CancellationToken.None);

        _botService.Client.Returns(_client);
        _botRegistry.GetService<IBotService>(TestBotName).Returns(_botService);
        _optionsMonitor.Get(TestBotName).Returns(new BotConfig
        {
            AdminChatId = AdminChatId,
            Emoji = "🤖",
            Handle = "testbot",
            Token = "123456789:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
        });

        _service = new TelegramNotificationService(_botRegistry, _optionsMonitor);
    }

    [Test]
    public async Task SendMessage_WithChatId_SendsHtmlMessageToProvidedChat()
    {
        const long chatId = 12345;
        const string text = "Hello, world!";

        await _service.SendMessage(chatId, TestBotName, text);

        var request = GetSingleRequest(_client, "SendMessageRequest");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(GetPropertyValue(request, "ChatId")?.ToString(), Is.EqualTo(chatId.ToString()));
            Assert.That(GetPropertyValue(request, "Text"), Is.EqualTo(text));
            Assert.That(GetPropertyValue(request, "ParseMode"), Is.EqualTo(ParseMode.Html));
        }
    }

    [Test]
    public async Task SendErrorNotification_WithMessage_SendsFormattedAlertToAdminChat()
    {
        const string alertText = "Database exploded";
        var message = new Message
        {
            Chat = new Chat { Id = 24680, Title = "Ops Room" },
            From = new User { Id = 13579, FirstName = "Jane", LastName = "Doe" },
            Date = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc)
        };

        await _service.SendErrorNotification(alertText, TestBotName, message);

        var request = GetSingleRequest(_client, "SendMessageRequest");
        var text = GetPropertyValue(request, "Text")?.ToString();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(GetPropertyValue(request, "ChatId")?.ToString(), Is.EqualTo(AdminChatId.ToString()));
            Assert.That(GetPropertyValue(request, "ParseMode"), Is.EqualTo(ParseMode.Markdown));
            Assert.That(text, Does.Contain(alertText));
            Assert.That(text, Does.Contain("Ops Room (24680)"));
            Assert.That(text, Does.Contain("Jane Doe"));
            Assert.That(text, Does.Contain("tg://user?id=13579"));
            Assert.That(text, Does.Contain("Time:"));
        }
    }

    [Test]
    public async Task SendWarningNotification_WithoutMessage_SendsWarningAlertToAdminChat()
    {
        const string alertText = "Queue depth is high";

        await _service.SendWarningNotification(alertText, TestBotName, null);

        var request = GetSingleRequest(_client, "SendMessageRequest");
        var text = GetPropertyValue(request, "Text")?.ToString();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(GetPropertyValue(request, "ChatId")?.ToString(), Is.EqualTo(AdminChatId.ToString()));
            Assert.That(GetPropertyValue(request, "ParseMode"), Is.EqualTo(ParseMode.Markdown));
            Assert.That(text, Does.Contain(alertText));
            Assert.That(text, Does.Contain("Warning"));
            Assert.That(text, Does.Not.Contain("Chat:"));
            Assert.That(text, Does.Not.Contain("User:"));
        }
    }
}
