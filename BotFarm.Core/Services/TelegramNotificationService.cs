using BotFarm.Core.Models;
using BotFarm.Core.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace BotFarm.Core.Services
{
    public class TelegramNotificationService : INotificationService
    {
        private readonly IEnumerable<BotConfig> _botConfigs;
        private readonly IEnumerable<IBotService> _botServices;

        public TelegramNotificationService(
            IConfiguration config,
            IEnumerable<IBotService> botServices,
            IEnumerable<IOptions<BotConfig>> botConfigs)
        {
            _botServices = botServices;
            _botConfigs = botConfigs.Select(c => c.Value);
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
            var service = _botServices.First(s => s.Name.Equals(botName, StringComparison.InvariantCultureIgnoreCase));
            await service.Client.SendTextMessageAsync(chatId, message, parseMode: ParseMode.Html);
        }

        protected string BuildAlert(string alertText, Message message, LogLevel alertType)
        {
            string header = string.Empty;
            string prefix = string.Empty;
            if (alertType == LogLevel.Error)
            {
                header = "‼ *Exception occurred in Bot Farm*";
                prefix = "🔴 Error:";
            }
            else if (alertType == LogLevel.Warning)
            {
                header = "⚠️ *Alert from Bot Farm*";
                prefix = "🟡 Warning:";
            }

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

        protected async Task DoSend(string botName, string message)
        {
            var service = _botServices.First(s => s.Name.Equals(botName, StringComparison.InvariantCultureIgnoreCase));
            var config = _botConfigs.First(c => c.Name.Equals(botName, StringComparison.InvariantCultureIgnoreCase));

            await service.Client.SendTextMessageAsync(config.AdminChatId, message, parseMode: ParseMode.Markdown);
        }
    }
}
