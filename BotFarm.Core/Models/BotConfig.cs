namespace BotFarm.Core.Models;

/// <summary>
/// Per-bot configuration values loaded from application settings.
/// </summary>
public class BotConfig
{
    /// <summary>
    /// The state the bot starts in the first time it runs. Later changes are made from the dashboard.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Optional emoji shown next to the bot's name in the dashboard.
    /// </summary>
    public string? Emoji { get; set; }

    /// <summary>
    /// Telegram bot token from @BotFather, in the form <c>&lt;bot id&gt;:&lt;secret&gt;</c>.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Telegram chat that receives admin alerts for this bot.
    /// </summary>
    public long AdminChatId { get; set; }

    /// <summary>
    /// Optional secret Telegram echoes in the <c>X-Telegram-Bot-Api-Secret-Token</c> webhook header.
    /// Allowed characters are A-Z, a-z, 0-9, '_' and '-' (1-256 chars). When empty, a stable
    /// secret is derived from <see cref="Token"/>.
    /// </summary>
    public string? WebhookSecret { get; set; }
}
