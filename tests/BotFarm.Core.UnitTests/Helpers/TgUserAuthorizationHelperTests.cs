using BotFarm.Core.Abstractions;
using BotFarm.Core.Helpers;
using BotFarm.TestKit;
using Microsoft.Extensions.Logging;
using NSubstitute;
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
}