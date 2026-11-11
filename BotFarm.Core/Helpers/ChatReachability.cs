using Telegram.Bot.Exceptions;

namespace BotFarm.Core.Helpers;

/// <summary>
/// Tells from a failed Telegram request about a chat whether the bot can no longer reach that chat at all,
/// for example because the bot was kicked or the chat was deleted.
/// </summary>
public static class ChatReachability
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="exception"/> is a Telegram API error saying the bot
    /// has lost access to the chat: code 403 (kicked, blocked, not a member, or the user is deactivated), or
    /// code 400 "chat not found". Other errors, including temporary ones and group-to-supergroup migrations,
    /// return <see langword="false"/>.
    /// </summary>
    public static bool IsUnreachable(Exception exception)
    {
        return exception is ApiRequestException apiException
            && (apiException.ErrorCode == 403
                || (apiException.ErrorCode == 400
                    && apiException.Parameters?.MigrateToChatId == null
                    && apiException.Message.Contains("chat not found", StringComparison.OrdinalIgnoreCase)));
    }
}
