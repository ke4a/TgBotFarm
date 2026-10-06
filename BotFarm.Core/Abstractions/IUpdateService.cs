using Telegram.Bot.Types;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Processes incoming Telegram updates for a single bot.
/// </summary>
public interface IUpdateService : INamedService
{
    /// <summary>
    /// Whether authoritative enabled state has been applied and this bot may process updates.
    /// </summary>
    bool CanProcessUpdates { get; }

    /// <summary>
    /// Handles one incoming <see cref="Update"/>.
    /// </summary>
    Task ProcessUpdate(Update update);
}