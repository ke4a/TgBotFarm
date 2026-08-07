using Telegram.Bot.Types;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Handles a single bot command, registered via DI.
/// </summary>
public interface ICommandHandler
{
    /// <summary>
    /// The command text this handler responds to.</c>.
    /// </summary>
    string Command { get; }

    /// <summary>
    /// Handles the command message.
    /// </summary>
    Task Handle(Message message, string language);
}
