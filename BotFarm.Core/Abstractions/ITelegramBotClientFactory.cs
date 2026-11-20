using Telegram.Bot;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Creates <see cref="TelegramBotClient"/> instances.
/// </summary>
public interface ITelegramBotClientFactory
{
    /// <summary>
    /// Creates a Telegram client authenticated with <paramref name="token"/> whose requests give up after
    /// <paramref name="timeout"/>, or after a default that stays under Telegram's webhook timeout when it is null.
    /// </summary>
    TelegramBotClient Create(string token, TimeSpan? timeout = null);
}
