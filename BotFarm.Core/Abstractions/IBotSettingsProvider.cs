using BotFarm.Core.Models;
using MongoDB.Driver;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Loads bot settings, keeps the usable values of each bot and stores accepted changes.
/// </summary>
public interface IBotSettingsProvider
{
    /// <summary>
    /// Loads the bot's stored settings and makes them the bot's current settings.
    /// </summary>
    /// <returns>The bot's settings state after the load.</returns>
    Task<BotSettingsState> LoadAsync(string botName, CancellationToken cancellationToken = default);

    /// <summary>
    /// The bot's current settings state; <see cref="BotSettingsState.Unknown"/> before a successful load.
    /// </summary>
    BotSettingsState GetState(string botName);

    /// <summary>
    /// The bot's current settings view without a Telegram username.
    /// </summary>
    BotSettingsView GetView(string botName);

    /// <summary>
    /// Stores <paramref name="change"/> on top of the bot's current settings and makes the result the
    /// bot's current settings. The change must already be validated and verified. A stored secret that
    /// could not be unprotected is kept as stored unless the change replaces it, or, for the webhook
    /// secret, clears it.
    /// </summary>
    /// <returns>The bot's settings state after the change.</returns>
    /// <exception cref="InvalidOperationException">
    /// The bot's settings are not loaded (<see cref="BotSettingsState.Unknown"/>); nothing is written.
    /// </exception>
    /// <exception cref="MongoException">
    /// The write could not be confirmed; the current settings are unchanged.
    /// </exception>
    Task<BotSettingsState> PersistAsync(
        string botName,
        BotSettingsChange change,
        CancellationToken cancellationToken = default);
}
