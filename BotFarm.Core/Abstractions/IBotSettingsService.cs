using BotFarm.Core.Models;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Reads and saves the settings of registered bots.
/// </summary>
public interface IBotSettingsService
{
    /// <summary>
    /// The registered bot's settings view. It never contains a token or webhook secret.
    /// </summary>
    BotSettingsView GetView(string botName);

    /// <summary>
    /// Validates and saves <paramref name="change"/> for the registered bot, verifying a new token with
    /// Telegram first.
    /// </summary>
    /// <returns>
    /// <see cref="BotSettingsSaveResult.Saved"/> when stored,
    /// <see cref="BotSettingsSaveResult.NeedsAccountChangeConfirmation"/> when the new token belongs to a
    /// different Telegram bot and the change is not confirmed, or
    /// <see cref="BotSettingsSaveResult.Rejected"/> with one category per problem. Only a
    /// <see cref="BotSettingsSaveResult.Saved"/> result changes the stored settings.
    /// </returns>
    Task<BotSettingsSaveResult> SaveAsync(
        string botName,
        BotSettingsChange change,
        CancellationToken cancellationToken = default);
}
