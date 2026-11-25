using BotFarm.Core.Models;
using MongoDB.Driver;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Persists bot settings documents.
/// </summary>
public interface IBotSettingsStore
{
    /// <summary>
    /// Reads the bot's settings, or <see langword="null"/> when none are stored.
    /// </summary>
    Task<BotSettings?> GetAsync(string botName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores <paramref name="settings"/>, replacing any settings of the same bot.
    /// </summary>
    /// <exception cref="MongoWriteException">
    /// Another bot's settings have the same <see cref="BotSettings.TelegramBotId"/>; the write error's
    /// category is <see cref="ServerErrorCategory.DuplicateKey"/> and nothing is stored.
    /// </exception>
    Task UpsertAsync(BotSettings settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the bot whose stored token has <paramref name="telegramBotId"/>, or <see langword="null"/>
    /// when no bot has it.
    /// </summary>
    Task<string?> FindBotNameByTelegramBotIdAsync(
        long telegramBotId,
        CancellationToken cancellationToken = default);
}
