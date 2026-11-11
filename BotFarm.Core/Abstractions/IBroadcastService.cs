using BotFarm.Core.Models;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Sends one message to many chats of a bot while staying within Telegram's rate limits.
/// </summary>
public interface IBroadcastService
{
    /// <summary>
    /// Sends <paramref name="message"/> as Telegram HTML to every chat in <paramref name="chatIds"/>, one chat at a
    /// time and paced so the bot stays under Telegram's broadcast limit. A chat that cannot receive the message does
    /// not stop the broadcast: it is reported in the result instead.
    /// </summary>
    /// <param name="botName">The bot that sends the message.</param>
    /// <param name="chatIds">The chats to send the message to.</param>
    /// <param name="message">The message as Telegram HTML.</param>
    /// <param name="progress">Receives the number of chats handled so far after each chat.</param>
    /// <param name="cancellationToken">Stops the broadcast before the next chat.</param>
    /// <exception cref="InvalidOperationException">A broadcast for the same bot is still running.</exception>
    Task<BroadcastResult> Broadcast(
        string botName,
        IReadOnlyCollection<long> chatIds,
        string message,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}
