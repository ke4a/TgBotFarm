using Telegram.Bot.Types.ReplyMarkups;

namespace BotFarm.Core.Abstractions;

public interface IMarkupService : INamedService
{
    InlineKeyboardMarkup GenerateChangeLanguageMarkup(string botName);
}
