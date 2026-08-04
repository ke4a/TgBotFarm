using Telegram.Bot.Types;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Handles a single callback query, registered via DI.
/// </summary>
public interface ICallbackHandler
{
    /// <summary>
    /// The callback data prefix this handler responds to.
    /// </summary>
    string CallbackKey { get; }

    /// <summary>
    /// Handles the callback query.
    /// </summary>
    Task HandleAsync(string callbackId, Message message, User user, string parameter, string language);
}
