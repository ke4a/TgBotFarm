using Telegram.Bot.Types;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Sends operational alerts and direct outbound messages through Telegram.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Sends an error alert, optionally enriched with the triggering Telegram message context.
    /// </summary>
    Task SendErrorNotification(string alertText, string handle, Message? message = null);

    /// <summary>
    /// Sends a warning alert, optionally enriched with the triggering Telegram message context.
    /// </summary>
    Task SendWarningNotification(string alertText, string handle, Message? message = null);

    /// <summary>
    /// Sends an arbitrary message to the specified chat using the named bot.
    /// </summary>
    Task SendMessage(long chatId, string name, string message);
}
