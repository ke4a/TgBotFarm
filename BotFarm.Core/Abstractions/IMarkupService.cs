using Telegram.Bot.Types.ReplyMarkups;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Builds bot-specific Telegram reply markup.
/// </summary>
public interface IMarkupService : INamedService
{
    /// <summary>
    /// Creates the inline keyboard used to switch a chat's language.
    /// </summary>
    InlineKeyboardMarkup GenerateChangeLanguageMarkup(string botName);
}
