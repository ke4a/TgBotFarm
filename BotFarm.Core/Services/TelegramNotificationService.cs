using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace BotFarm.Core.Services;

internal sealed class TelegramNotificationService : INotificationService
{
    private readonly IBotRegistry _botRegistry;
    private readonly IOptionsMonitor<BotConfig> _botConfigs;

    public TelegramNotificationService(
        IBotRegistry botRegistry,
        IOptionsMonitor<BotConfig> options)
    {
        _botRegistry = botRegistry;
        _botConfigs = options;
    }
    
    public async Task SendErrorNotification(string alertText, string botName, Message? message)
    {
        var alert = BuildAlert(alertText, message, LogLevel.Error);
        await DoSend(botName, alert);
    }

    public async Task SendWarningNotification(string alertText, string botName, Message? message)
    {
        var alert = BuildAlert(alertText, message, LogLevel.Warning);
        await DoSend(botName, alert);
    }

    public async Task SendMessage(long chatId, string botName, string message)
    {
        var service = _botRegistry.GetService<IBotService>(botName);
        await service.Client.SendMessage(chatId, message, parseMode: ParseMode.Html);
    }

    private string BuildAlert(string alertText, Message? message, LogLevel alertType)
    {
        var (header, prefix) = alertType switch
        {
            LogLevel.Error => ("‼ *Exception occurred in Bot Farm*", "🔴 Error:"),
            LogLevel.Warning => ("⚠️ *Alert from Bot Farm*", "🟡 Warning:"),
            _ => (string.Empty, string.Empty)
        };

        var alert = new StringBuilder(header);
        alert.AppendLine().AppendLine();

        if (message != null)
        {
            alert.AppendLine($"💬 Chat: {message.Chat.Title} ({message.Chat.Id})");
            if (message.From != null)
            {
                alert.AppendLine($"🗣 User: [{message.From.FirstName}{(string.IsNullOrWhiteSpace(message.From.LastName) ? "" : $" {message.From.LastName}")}](tg://user?id={message.From.Id})");
            }
            alert.AppendLine($"🕑 Time: {message.Date} UTC");
        }

        alert.AppendLine(prefix);
        alert.AppendLine($"```{alertText}```");

        return alert.ToString();
    }

    private async Task DoSend(string botName, string message)
    {
        var service = _botRegistry.GetService<IBotService>(botName);
        var config = _botConfigs.Get(botName);

        await service.Client.SendMessage(config.AdminChatId, message, parseMode: ParseMode.Markdown);
    }
}
