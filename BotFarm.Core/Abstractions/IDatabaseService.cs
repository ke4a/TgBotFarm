using BotFarm.Core.Models;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Represents persistent bot storage.
/// </summary>
public interface IDatabaseService : INamedService
{
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

    /// <summary>
    /// Moves all data this bot holds for <paramref name="fromChatId"/> to <paramref name="toChatId"/>.
    /// Data already stored under <paramref name="toChatId"/> wins conflicts. Safe to call repeatedly for the
    /// same pair.
    /// </summary>
    Task MigrateChat(long fromChatId, long toChatId);

    /// <summary>
    /// Deletes all data this bot holds for <paramref name="chatId"/>, including its chat settings, so the chat
    /// is no longer known to the bot. Safe to call repeatedly for the same chat.
    /// </summary>
    Task DeleteChat(long chatId);
}
