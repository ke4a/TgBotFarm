using Telegram.Bot.Types;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Processes incoming Telegram updates for a single bot.
/// </summary>
public interface IUpdateService : INamedService
{
    /// <summary>
    /// Handles one incoming <see cref="Update"/>.
    /// </summary>
    Task ProcessUpdate(Update update);
}
