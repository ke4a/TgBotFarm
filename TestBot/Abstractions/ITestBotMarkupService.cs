using BotFarm.Core.Abstractions;
using Telegram.Bot.Types.ReplyMarkups;

namespace TestBot.Abstractions;

/// <summary>
/// Builds TestBot-specific inline keyboards.
/// </summary>
public interface ITestBotMarkupService : IMarkupService
{
    /// <summary>
    /// Creates the confirmation markup used before clearing a chat's stored GIF data.
    /// </summary>
    InlineKeyboardMarkup GenerateClearChatDataMarkup(string language);
}
