using Telegram.Bot.Types;

namespace BotFarm.Core.Abstractions;

public interface INotificationService
{
    Task SendErrorNotification(string alertText, string handle, Message? message = null);

    Task SendWarningNotification(string alertText, string handle, Message? message = null);

    Task SendMessage(long chatId, string name, string message);
}
