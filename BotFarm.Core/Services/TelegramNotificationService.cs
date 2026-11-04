using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace BotFarm.Core.Services;

/// <summary>
/// Sends operational alerts and ad-hoc messages through the registered Telegram bots.
/// </summary>
internal sealed class TelegramNotificationService : INotificationService
{
    private readonly IBotRegistry _botRegistry;
    private readonly IOptionsMonitor<BotConfig> _botConfigs;

    /// <summary>
    /// Creates the notification service that resolves bots by name and reads their admin chat settings.
    /// </summary>
    public TelegramNotificationService(
        IBotRegistry botRegistry,
        IOptionsMonitor<BotConfig> options)
    {
        _botRegistry = botRegistry;
        _botConfigs = options;
    }
    
    /// <summary>
    /// Sends an error alert to the configured admin chat for the bot.
    /// </summary>
    public async Task SendErrorNotification(string alertText, string botName, Message? message)
    {
        var alert = BuildAlert(alertText, message, LogLevel.Error);
        await DoSend(botName, alert);
    }

    /// <summary>
    /// Sends a warning alert to the configured admin chat for the bot.
    /// </summary>
    public async Task SendWarningNotification(string alertText, string botName, Message? message)
    {
        var alert = BuildAlert(alertText, message, LogLevel.Warning);
        await DoSend(botName, alert);
    }

    /// <summary>
    /// Sends a regular message to an arbitrary chat using the named bot.
    /// </summary>
    public async Task SendMessage(long chatId, string botName, string message)
    {
        var service = _botRegistry.GetService<IBotService>(botName);
        await service.Client.SendMessage(chatId, message, parseMode: ParseMode.Html);
    }

    /// <summary>
    /// Builds the alert as Telegram HTML. Chat titles, user names and the alert text are escaped, so any
    /// characters they contain are shown as-is.
    /// </summary>
    internal static string BuildAlert(string alertText, Message? message, LogLevel alertType)
    {
        var (header, prefix) = alertType switch
        {
            LogLevel.Error => ("‼ <b>Exception occurred in Bot Farm</b>", "🔴 Error:"),
            LogLevel.Warning => ("⚠️ <b>Alert from Bot Farm</b>", "🟡 Warning:"),
            _ => (string.Empty, string.Empty)
        };

        var alert = new StringBuilder(header);
        alert.AppendLine().AppendLine();

        if (message != null)
        {
            alert.AppendLine($"💬 Chat: {Encode(message.Chat.Title)} ({message.Chat.Id})");
            if (message.From != null)
            {
                var userName = string.IsNullOrWhiteSpace(message.From.LastName)
                    ? message.From.FirstName
                    : $"{message.From.FirstName} {message.From.LastName}";
                alert.AppendLine($"🗣 User: <a href=\"tg://user?id={message.From.Id}\">{Encode(userName)}</a>");
            }
            alert.AppendLine($"🕑 Time: {message.Date} UTC");
        }

        alert.AppendLine(prefix);
        alert.AppendLine($"<pre>{Encode(Truncate(alertText))}</pre>");

        return alert.ToString();
    }

    // Telegram rejects messages longer than 4096 characters; this leaves room for the header and context lines.
    private const int MaxAlertTextLength = 3000;

    private static string Truncate(string text)
    {
        return text.Length <= MaxAlertTextLength ? text : $"{text[..MaxAlertTextLength]}…";
    }

    private static string Encode(string? text)
    {
        return WebUtility.HtmlEncode(text ?? string.Empty);
    }

    private async Task DoSend(string botName, string message)
    {
        var service = _botRegistry.GetService<IBotService>(botName);
        var config = _botConfigs.Get(botName);

        await service.Client.SendMessage(config.AdminChatId, message, parseMode: ParseMode.Html);
    }
}
