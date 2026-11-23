using BotFarm.Core.Telemetry;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

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
    /// The keys of the bot's built-in and registered commands, as <see cref="Describe"/> reports them: lower case,
    /// without <c>/</c>.
    /// </summary>
    IReadOnlyCollection<string> CommandKeys => [];

    /// <summary>
    /// Handles one incoming <see cref="Update"/>.
    /// </summary>
    Task ProcessUpdate(Update update);

    /// <summary>
    /// Tells how <paramref name="update"/> is counted in the bot's metrics: its kind and, for a command, its key, or
    /// <see cref="Constants.Telemetry.UnknownCommand"/> for a command that is neither built in nor registered. Keys
    /// are lower case, without <c>/</c> and without <c>@botname</c>. Does no I/O, changes no state and never throws.
    /// </summary>
    /// <remarks>
    /// By default, a message that starts with a bot command is a command with the unknown key, a callback query is a
    /// callback query, and anything else is other.
    /// </remarks>
    UpdateDescription Describe(Update update)
    {
        if (update.Type == UpdateType.CallbackQuery)
        {
            return new UpdateDescription(UpdateKind.CallbackQuery, null);
        }

        return update.Message?.Entities is [{ Type: MessageEntityType.BotCommand, Offset: 0 }, ..]
            ? new UpdateDescription(UpdateKind.Command, Constants.Telemetry.UnknownCommand)
            : new UpdateDescription(UpdateKind.Other, null);
    }
}
