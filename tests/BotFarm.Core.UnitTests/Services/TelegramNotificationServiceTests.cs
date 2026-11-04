using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using Microsoft.Extensions.Logging;
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
        _client = Substitute.For<TelegramBotClient>("111111111:AAAAAbAAAAbbAAbbAAAbAbAAbbb_bAAbAb1", null,
            CancellationToken.None);

        _botService.Name.Returns(TestBotName);
        _botService.Client.Returns(_client);
        _botRegistry.GetService<IBotService>(TestBotName).Returns(_botService);
        _optionsMonitor.Get(TestBotName).Returns(new BotConfig
        {
            AdminChatId = AdminChatId,
            Emoji = "🤖",
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
    public async Task SendMessage_WhenInboundGateIsClosed_RemainsAvailableForExplicitOutboundActions()
    {
        var state = new BotControlState
        {
            BotName = TestBotName,
            DesiredEnabled = false,
            UpdatedAtUtc = DateTime.UtcNow
        };
        var stateStore = Substitute.For<IBotControlStateStore>();
        stateStore.GetAsync(TestBotName, Arg.Any<CancellationToken>()).Returns(state);
        var webhookInitializer = Substitute.For<IBotWebhookInitializer>();
        webhookInitializer.DisableAsync(_botService, Arg.Any<CancellationToken>()).Returns(true);
        var controlService = new BotControlCoordinator(
            _optionsMonitor,
            stateStore,
            [_botService],
            webhookInitializer,
            Substitute.For<INotificationService>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BotControlCoordinator>.Instance);
        await controlService.InitializeAsync();
        Assert.That(controlService.CanProcessUpdates(TestBotName), Is.False);

        await _service.SendMessage(12345, TestBotName, "outbound admin action");

        var request = GetSingleRequest(_client, "SendMessageRequest");
        Assert.That(GetPropertyValue(request, "Text"), Is.EqualTo("outbound admin action"));
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
            Assert.That(GetPropertyValue(request, "ParseMode"), Is.EqualTo(ParseMode.Html));
            Assert.That(text, Does.Contain(alertText));
            Assert.That(text, Does.Contain("Ops Room (24680)"));
            Assert.That(text, Does.Contain("Jane Doe"));
            Assert.That(text, Does.Contain("tg://user?id=13579"));
            Assert.That(text, Does.Contain("Time:"));
        }
    }

    [Test]
    public async Task SendErrorNotification_WithSenderWithoutLastName_OmitsTrailingSpace()
    {
        var message = new Message
        {
            Chat = new Chat { Id = 24680, Title = "Ops Room" },
            From = new User { Id = 13579, FirstName = "Jane" },
            Date = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc)
        };

        await _service.SendErrorNotification("boom", TestBotName, message);

        var text = GetPropertyValue(GetSingleRequest(_client, "SendMessageRequest"), "Text")?.ToString();
        Assert.That(text, Does.Contain("User: <a href=\"tg://user?id=13579\">Jane</a>"));
    }

    [Test]
    public async Task SendErrorNotification_WithMessageWithoutSender_OmitsUserLine()
    {
        var message = new Message
        {
            Chat = new Chat { Id = 24680, Title = "Ops Room" },
            Date = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc)
        };

        await _service.SendErrorNotification("boom", TestBotName, message);

        var text = GetPropertyValue(GetSingleRequest(_client, "SendMessageRequest"), "Text")?.ToString();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Does.Contain("Ops Room (24680)"));
            Assert.That(text, Does.Not.Contain("User:"));
            Assert.That(text, Does.Contain("Error:"));
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
            Assert.That(GetPropertyValue(request, "ParseMode"), Is.EqualTo(ParseMode.Html));
            Assert.That(text, Does.Contain(alertText));
            Assert.That(text, Does.Contain("Warning"));
            Assert.That(text, Does.Not.Contain("Chat:"));
            Assert.That(text, Does.Not.Contain("User:"));
        }
    }

    [Test]
    public async Task SendErrorNotification_WithHtmlAndMarkdownCharacters_EscapesUserContent()
    {
        var message = new Message
        {
            Chat = new Chat { Id = 24680, Title = "<b>*Ops_Room*</b> & `co`" },
            From = new User { Id = 13579, FirstName = "<Jane>", LastName = "_Doe_" },
            Date = new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc)
        };

        await _service.SendErrorNotification("Failed: <null> & `x`", TestBotName, message);

        var text = GetPropertyValue(GetSingleRequest(_client, "SendMessageRequest"), "Text")?.ToString();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Does.Contain("Chat: &lt;b&gt;*Ops_Room*&lt;/b&gt; &amp; `co` (24680)"));
            Assert.That(text, Does.Contain(">&lt;Jane&gt; _Doe_</a>"));
            Assert.That(text, Does.Contain("<pre>Failed: &lt;null&gt; &amp; `x`</pre>"));
        }
    }

    [Test]
    public void BuildAlert_WithVeryLongText_TruncatesItBelowTelegramsMessageLimit()
    {
        var alert = TelegramNotificationService.BuildAlert(new string('x', 10_000), null, LogLevel.Error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(alert.Length, Is.LessThan(4096));
            Assert.That(alert, Does.Contain("x…</pre>"));
        }
    }
}