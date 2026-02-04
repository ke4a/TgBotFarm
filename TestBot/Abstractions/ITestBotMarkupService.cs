using BotFarm.Core.Abstractions;
using Telegram.Bot.Types.ReplyMarkups;

namespace TestBot.Abstractions;

public interface ITestBotMarkupService : IMarkupService
{
    InlineKeyboardMarkup GenerateClearChatDataMarkup(string language);
}
