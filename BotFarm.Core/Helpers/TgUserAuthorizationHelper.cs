using BotFarm.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace BotFarm.Core.Helpers;

/// <summary>
/// Provides authorization checks for Telegram chats based on chat type and sender administrator status.
/// </summary>
public static class TgUserAuthorizationHelper
{
    /// <summary>
    /// Allows private messages, or checks whether the sender is an administrator in a group chat.
    /// </summary>
    public static Task<bool> IsFromAdminOrPrivate(
        Message message,
        IBotService botService,
        ILogger logger,
        User? sender = null)
    {
        return message.Chat.Type == ChatType.Private
            ? Task.FromResult(true)
            : IsFromAdmin(message, botService, logger, sender);
    }

    /// <summary>
    /// Checks whether the message sender (or the supplied callback user) is an administrator
    /// in the message's group or supergroup.
    /// </summary>
    public static async Task<bool> IsFromAdmin(
        Message message,
        IBotService botService,
        ILogger logger,
        User? sender = null)
    {
        sender ??= message.From;
        if (message.Chat.Type is not (ChatType.Group or ChatType.Supergroup) || sender == null)
        {
            return false;
        }

        try
        {
            var administrators = await botService.Client.SendRequest(new GetChatAdministratorsRequest
            {
                ChatId = message.Chat.Id
            });
            return administrators.Any(administrator => administrator.User.Id == sender.Id);
        }
        catch (ApiRequestException exception)
        {
            logger.LogWarning(exception, "[{BotName}] Could not verify administrator in chat {ChatId}.",
                botService.Name, message.Chat.Id);
            return false;
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "[{BotName}] Could not verify administrator in chat {ChatId}.",
                botService.Name, message.Chat.Id);
            return false;
        }
        catch (TaskCanceledException exception)
        {
            logger.LogWarning(exception, "[{BotName}] Administrator lookup timed out in chat {ChatId}.",
                botService.Name, message.Chat.Id);
            return false;
        }
    }
}