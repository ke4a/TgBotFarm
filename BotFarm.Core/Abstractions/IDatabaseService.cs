using BotFarm.Core.Models;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Represents persistent bot storage.
/// </summary>
public interface IDatabaseService : INamedService
{
    /// <summary>
    /// Releases database resources during bot shutdown.
    /// </summary>
    Task<bool> Disconnect();

    /// <summary>
    /// Re-establishes the backing database connection.
    /// </summary>
    Task<bool> Reconnect();

    /// <summary>
    /// Returns every chat currently known to this bot.
    /// </summary>
    Task<IEnumerable<long>> GetAllChatIds();

    /// <summary>
    /// Returns the number of chats currently known to this bot.
    /// </summary>
    /// <param name="estimated">When true, returns a fast estimate; otherwise, returns an exact count.</param>
    Task<long> GetChatCount(bool estimated = true);

    /// <summary>
    /// Resolves the stored language for <paramref name="chatId"/>, creating a default value when needed.
    /// </summary>
    Task<string> GetChatLanguage<TSettings>(long chatId) where TSettings : ChatSettings;

    /// <summary>
    /// Persists a new language for <paramref name="chatId"/>.
    /// </summary>
    Task SetChatLanguage<TSettings>(long chatId, string language) where TSettings : ChatSettings;
}
