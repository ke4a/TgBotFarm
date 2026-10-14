using BotFarm.Core.Abstractions;
using BotFarm.TestKit;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TestBot.Abstractions;
using TestBot.Handlers.Commands;
using static BotFarm.TestKit.TelegramRequestAssertHelpers;

namespace TestBot.UnitTests.Handlers.Commands;

[TestFixture]
public class ClearChatDataCommandHandlerTests
{
    private const long ChatId = 12345;
    private const long UserId = 67890;

    private IBotService _botService = null!;
    private ILocalizationService _localizationService = null!;
    private ITestBotMarkupService _markupService = null!;
    private TelegramBotClient _client = null!;
    private ClearChatDataCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _botService = Substitute.For<IBotService>();
        _localizationService = Substitute.For<ILocalizationService>();
        _markupService = Substitute.For<ITestBotMarkupService>();
        _client = TelegramBotClientFactory.CreateSubstitute();
        _botService.Client.Returns(_client);
        _handler = new ClearChatDataCommandHandler(
            _botService,
            _localizationService,
            _markupService,
            Substitute.For<ILogger<ClearChatDataCommandHandler>>());
    }

    [Test]
    public async Task Handle_PrivateChat_SendsLocalizedConfirmationPrompt()
    {
        var message = TelegramMessageFactory.CreateMessage(ChatId, UserId, messageId: 7);
        _localizationService.GetLocalizedString(Constants.Name, "AreYouSureClear", "en")
            .Returns("Confirm clearing chat data?");

        await _handler.Handle(message, "en");

        var request = GetSingleRequest(_client, "SendMessageRequest");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(GetPropertyValue(request, "ChatId")?.ToString(), Is.EqualTo(ChatId.ToString()));
            Assert.That(GetPropertyValue(request, "Text"), Is.EqualTo("Confirm clearing chat data?"));
        }

        _markupService.Received(1).GenerateClearChatDataMarkup("en");
    }

    [Test]
    public async Task Handle_GroupChatFromNonAdmin_SendsLocalizedDenial()
    {
        var message = TelegramMessageFactory.CreateMessage(
            ChatId, UserId, chatType: ChatType.Supergroup);
        _client.SendRequest(Arg.Any<GetChatAdministratorsRequest>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _localizationService.GetLocalizedString(Constants.Name, "OnlyAdminsClear", "en")
            .Returns("Only admins can clear chat data.");

        await _handler.Handle(message, "en");

        var request = GetSingleRequest(_client, "SendMessageRequest");
        Assert.That(GetPropertyValue(request, "Text"), Is.EqualTo("Only admins can clear chat data."));
        _markupService.DidNotReceive().GenerateClearChatDataMarkup(Arg.Any<string>());
    }
}
