using Telegram.Bot;
using Telegram.Bot.Types;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Defines the Telegram client lifecycle for a single registered bot.
/// </summary>
public interface IBotService : INamedService
{
    /// <summary>
    /// Whether this bot is enabled in configuration and should receive updates.
    /// </summary>
    bool Enabled { get; }

    /// <summary>
    /// Telegram client used for webhook management and outbound bot messages.
    /// </summary>
    TelegramBotClient Client { get; }

    /// <summary>
    /// Points Telegram at this bot's update endpoint.
    /// </summary>
    Task InitializeWebHook(string url);

    /// <summary>
    /// Stops Telegram from delivering new updates for this bot.
    /// </summary>
    Task<bool> Pause();

    /// <summary>
    /// Restores webhook delivery after a prior <see cref="Pause"/>.
    /// </summary>
    Task<bool> Resume();

    /// <summary>
    /// Performs startup work such as verifying the bot identity and preparing local state.
    /// </summary>
    Task Initialize();

    /// <summary>
    /// Bot-scoped working folder used for temporary files.
    /// </summary>
    string TempPath { get; }

    /// <summary>
    /// Telegram account metadata for the authenticated bot user.
    /// </summary>
    User Me { get; }
}
