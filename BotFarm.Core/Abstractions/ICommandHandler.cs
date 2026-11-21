using BotFarm.Core.Models;
using Telegram.Bot.Types;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Handles a single bot command, registered via DI.
/// </summary>
public interface ICommandHandler
{
    /// <summary>
    /// The command text this handler responds to, starting with <c>/</c>, e.g. <c>/getlastgif</c>.
    /// </summary>
    string Command { get; }

    /// <summary>
    /// The key of the command's description in the bot's language files, shown next to the command in the
    /// Telegram command menu. The command is left out of the menu when this is null or the key has no text.
    /// </summary>
    string? DescriptionKey => null;

    /// <summary>
    /// Who sees the command in the Telegram command menu.
    /// </summary>
    CommandVisibility Visibility => CommandVisibility.Everyone;

    /// <summary>
    /// Handles the command message.
    /// </summary>
    Task Handle(Message message, string language);
}
