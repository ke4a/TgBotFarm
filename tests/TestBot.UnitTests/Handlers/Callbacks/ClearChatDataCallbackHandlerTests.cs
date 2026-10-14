using BotFarm.Core.Abstractions;
using BotFarm.TestKit;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types.Enums;
using TestBot.Abstractions;
using TestBot.Handlers.Callbacks;
using static BotFarm.TestKit.TelegramRequestAssertHelpers;

namespace TestBot.UnitTests.Handlers.Callbacks;

[TestFixture]
public class ClearChatDataCallbackHandlerTests
{
    private const long ChatId = 12345;
    private const long UserId = 67890;

    private IBotService _botService = null!;
    private ILocalizationService _localizationService = null!;
    private ITestBotDatabaseService _databaseService = null!;
    private TelegramBotClient _client = null!;
    private ClearChatDataCallbackHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _botService = Substitute.For<IBotService>();
        _localizationService = Substitute.For<ILocalizationService>();
        _databaseService = Substitute.For<ITestBotDatabaseService>();
        _client = TelegramBotClientFactory.CreateSubstitute();
        _botService.Client.Returns(_client);
        _handler = new ClearChatDataCallbackHandler(
            _botService,
            _localizationService,
            _databaseService,
            Substitute.For<ILogger<ClearChatDataCallbackHandler>>());
    }

    [Test]
    public async Task Handle_YesFromPrivateChat_ClearsDataAndEditsConfirmation()
    {
        var message = TelegramMessageFactory.CreateMessage(ChatId, UserId, messageId: 7);
        var user = TelegramMessageFactory.CreateUser(UserId);
        _localizationService.GetLocalizedString(Constants.Name, "DataCleared", "en").Returns("Data cleared.");

        await _handler.Handle("callback-1", message, user, "yes", "en");

        await _databaseService.Received(1).ClearChatData(ChatId);
        var request = GetSingleRequest(_client, "EditMessageTextRequest");
        Assert.That(GetPropertyValue(request, "Text"), Is.EqualTo("Data cleared."));
        GetSingleRequest(_client, "AnswerCallbackQueryRequest");
    }

    [Test]
    public async Task Handle_NoFromPrivateChat_DeletesConfirmationWithoutClearingData()
    {
        var message = TelegramMessageFactory.CreateMessage(ChatId, UserId, messageId: 7);
        var user = TelegramMessageFactory.CreateUser(UserId);

        await _handler.Handle("callback-1", message, user, "no", "en");

        await _databaseService.DidNotReceive().ClearChatData(Arg.Any<long>());
        GetSingleRequest(_client, "DeleteMessageRequest");
        GetSingleRequest(_client, "AnswerCallbackQueryRequest");
    }

    [Test]
    public async Task Handle_GroupCallbackFromNonAdmin_AlertsWithoutClearingData()
    {
        var message = TelegramMessageFactory.CreateMessage(
            ChatId, UserId, chatType: ChatType.Supergroup);
        var user = TelegramMessageFactory.CreateUser(UserId);
        _client.SendRequest(Arg.Any<GetChatAdministratorsRequest>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _localizationService.GetLocalizedString(Constants.Name, "OnlyAdminsClear", "en")
            .Returns("Only admins can clear chat data.");

        await _handler.Handle("callback-1", message, user, "yes", "en");

        await _databaseService.DidNotReceive().ClearChatData(Arg.Any<long>());
        var request = GetSingleRequest(_client, "AnswerCallbackQueryRequest");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(GetPropertyValue(request, "Text"), Is.EqualTo("Only admins can clear chat data."));
            Assert.That(GetPropertyValue(request, "ShowAlert"), Is.EqualTo(true));
        }
    }
}
