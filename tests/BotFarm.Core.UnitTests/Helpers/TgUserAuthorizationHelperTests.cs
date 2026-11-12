using BotFarm.Core.Abstractions;
using BotFarm.Core.Helpers;
using BotFarm.TestKit;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static BotFarm.TestKit.TelegramRequestAssertHelpers;

namespace BotFarm.Core.UnitTests.Helpers;

[TestFixture]
public class TgUserAuthorizationHelperTests
{
    [Test]
    public async Task IsFromAdminOrPrivate_PrivateChatReturnsTrueWithoutAdminLookup()
    {
        const long chatId = 12345;
        const long userId = 67890;
        var message = TelegramMessageFactory.CreateMessage(chatId, userId, chatType: ChatType.Private);
        var botService = Substitute.For<IBotService>();
        var client = TelegramBotClientFactory.CreateSubstitute();
        botService.Client.Returns(client);

        var result = await TgUserAuthorizationHelper.IsFromAdminOrPrivate(
            message, botService, Substitute.For<ILogger>());

        Assert.That(result, Is.True);
        Assert.That(
            client.ReceivedCalls()
                .SelectMany(call => call.GetArguments())
                .OfType<GetChatAdministratorsRequest>(),
            Is.Empty);
    }

    [Test]
    public async Task IsFromAdminOrPrivate_GroupChatUsesChatAdministratorsAndMatchesMessageSender()
    {
        const long chatId = 12345;
        const long userId = 67890;
        var sender = TelegramMessageFactory.CreateUser(userId);
        var message = TelegramMessageFactory.CreateMessage(
            chatId, userId, chatType: ChatType.Supergroup);
        var botService = Substitute.For<IBotService>();
        var client = TelegramBotClientFactory.CreateSubstitute();
        botService.Client.Returns(client);
        client.SendRequest(Arg.Any<GetChatAdministratorsRequest>(), Arg.Any<CancellationToken>())
            .Returns([new ChatMemberOwner { User = sender }]);

        var result = await TgUserAuthorizationHelper.IsFromAdminOrPrivate(
            message, botService, Substitute.For<ILogger>());

        var request = GetSingleRequest(client, "GetChatAdministratorsRequest");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.True);
            Assert.That(GetPropertyValue(request, "ChatId")?.ToString(), Is.EqualTo(chatId.ToString()));
        }
    }

    [Test]
    public async Task IsFromAdminOrPrivate_UsesSuppliedCallbackUserInsteadOfMessageSender()
    {
        const long chatId = 12345;
        const long messageSenderId = 67890;
        const long callbackUserId = 24680;
        var callbackUser = TelegramMessageFactory.CreateUser(callbackUserId);
        var message = TelegramMessageFactory.CreateMessage(
            chatId, messageSenderId, chatType: ChatType.Group);
        var botService = Substitute.For<IBotService>();
        var client = TelegramBotClientFactory.CreateSubstitute();
        botService.Client.Returns(client);
        client.SendRequest(Arg.Any<GetChatAdministratorsRequest>(), Arg.Any<CancellationToken>())
            .Returns([new ChatMemberOwner { User = callbackUser }]);

        var result = await TgUserAuthorizationHelper.IsFromAdminOrPrivate(
            message,
            botService,
            Substitute.For<ILogger>(),
            callbackUser);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsFromAdmin_SenderIsNotAnAdministrator_ReturnsFalse()
    {
        var message = TelegramMessageFactory.CreateMessage(12345, 67890, chatType: ChatType.Group);
        var (botService, client) = CreateBotService();
        client.SendRequest(Arg.Any<GetChatAdministratorsRequest>(), Arg.Any<CancellationToken>())
            .Returns([new ChatMemberAdministrator { User = TelegramMessageFactory.CreateUser(11111) }]);

        var result = await TgUserAuthorizationHelper.IsFromAdmin(message, botService, Substitute.For<ILogger>());

        Assert.That(result, Is.False);
    }

    [TestCase(ChatType.Private)]
    [TestCase(ChatType.Channel)]
    public async Task IsFromAdmin_OutsideGroups_ReturnsFalseWithoutAdminLookup(ChatType chatType)
    {
        var message = TelegramMessageFactory.CreateMessage(12345, 67890, chatType: chatType);
        var (botService, client) = CreateBotService();

        var result = await TgUserAuthorizationHelper.IsFromAdmin(message, botService, Substitute.For<ILogger>());

        Assert.That(result, Is.False);
        Assert.That(client.ReceivedCalls(), Is.Empty);
    }

    [Test]
    public async Task IsFromAdmin_WithoutSender_ReturnsFalseWithoutAdminLookup()
    {
        var message = TelegramMessageFactory.CreateMessage(12345, 67890, chatType: ChatType.Supergroup);
        message.From = null;
        var (botService, client) = CreateBotService();

        var result = await TgUserAuthorizationHelper.IsFromAdmin(message, botService, Substitute.For<ILogger>());

        Assert.That(result, Is.False);
        Assert.That(client.ReceivedCalls(), Is.Empty);
    }

    private static IEnumerable<TestCaseData> LookupFailures()
    {
        yield return new TestCaseData(new ApiRequestException("Bad Request: chat not found", 400)).SetName("TelegramError");
        yield return new TestCaseData(new HttpRequestException("unreachable")).SetName("NetworkError");
        yield return new TestCaseData(new TaskCanceledException("timed out")).SetName("Timeout");
    }

    [TestCaseSource(nameof(LookupFailures))]
    public async Task IsFromAdmin_WhenAdminLookupFails_ReturnsFalseAndLogsWarning(Exception failure)
    {
        var message = TelegramMessageFactory.CreateMessage(12345, 67890, chatType: ChatType.Group);
        var (botService, client) = CreateBotService();
        client.SendRequest(Arg.Any<GetChatAdministratorsRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        var logger = Substitute.For<ILogger>();

        var result = await TgUserAuthorizationHelper.IsFromAdmin(message, botService, logger);

        Assert.That(result, Is.False);
        var warnings = logger.ReceivedCalls()
            .Select(call => call.GetArguments())
            .Where(args => args.Length == 5 && args[0] is LogLevel.Warning && ReferenceEquals(args[3], failure));
        Assert.That(warnings, Has.Exactly(1).Items);
    }

    [Test]
    public async Task IsFromAdmin_MessageSentOnBehalfOfTheGroup_ReturnsTrueWithoutAdminLookup()
    {
        const long chatId = -100123;
        const long anonymousAdminBotId = 1087968824;
        var message = TelegramMessageFactory.CreateMessage(chatId, anonymousAdminBotId, chatType: ChatType.Supergroup);
        message.SenderChat = new Chat { Id = chatId, Type = ChatType.Supergroup };
        var (botService, client) = CreateBotService();

        var result = await TgUserAuthorizationHelper.IsFromAdmin(message, botService, Substitute.For<ILogger>());

        Assert.That(result, Is.True);
        Assert.That(
            client.ReceivedCalls()
                .SelectMany(call => call.GetArguments())
                .OfType<GetChatAdministratorsRequest>(),
            Is.Empty);
    }

    [Test]
    public async Task IsFromAdmin_MessageSentOnBehalfOfAnotherChat_ChecksTheSenderAsUsual()
    {
        const long chatId = -100123;
        const long channelId = -100456;
        const long channelBotId = 136817688;
        var message = TelegramMessageFactory.CreateMessage(chatId, channelBotId, chatType: ChatType.Supergroup);
        message.SenderChat = new Chat { Id = channelId, Type = ChatType.Channel };
        var (botService, client) = CreateBotService();
        client.SendRequest(Arg.Any<GetChatAdministratorsRequest>(), Arg.Any<CancellationToken>())
            .Returns([new ChatMemberOwner { User = TelegramMessageFactory.CreateUser(1) }]);

        var result = await TgUserAuthorizationHelper.IsFromAdmin(message, botService, Substitute.For<ILogger>());

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task IsFromAdmin_WithCallbackUser_IgnoresTheMessageSenderChat()
    {
        const long chatId = -100123;
        var message = TelegramMessageFactory.CreateMessage(chatId, 1087968824, chatType: ChatType.Supergroup);
        message.SenderChat = new Chat { Id = chatId, Type = ChatType.Supergroup };
        var callbackUser = TelegramMessageFactory.CreateUser(24680);
        var (botService, client) = CreateBotService();
        client.SendRequest(Arg.Any<GetChatAdministratorsRequest>(), Arg.Any<CancellationToken>())
            .Returns([new ChatMemberOwner { User = TelegramMessageFactory.CreateUser(1) }]);

        var result = await TgUserAuthorizationHelper.IsFromAdmin(
            message, botService, Substitute.For<ILogger>(), callbackUser);

        Assert.That(result, Is.False);
    }

    private static (IBotService BotService, Telegram.Bot.TelegramBotClient Client) CreateBotService()
    {
        var botService = Substitute.For<IBotService>();
        var client = TelegramBotClientFactory.CreateSubstitute();
        botService.Client.Returns(client);
        return (botService, client);
    }
}