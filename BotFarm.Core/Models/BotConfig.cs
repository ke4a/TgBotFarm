namespace BotFarm.Core.Models;

/// <summary>
/// A bot's current settings, filled from its stored settings. A bot without usable settings has an empty
/// <see cref="Token"/> and a zero <see cref="AdminChatId"/>.
/// </summary>
public class BotConfig
{
    /// <summary>
    /// Optional emoji shown next to the bot's name in the dashboard.
    /// </summary>
    public string? Emoji { get; set; }

    /// <summary>
    /// Telegram bot token from @BotFather, in the form <c>&lt;bot id&gt;:&lt;secret&gt;</c>; empty when none is stored
    /// or it cannot be read.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Telegram chat that receives admin alerts for this bot; zero when none is stored.
    /// </summary>
    public long AdminChatId { get; set; }

    /// <summary>
    /// Optional secret Telegram echoes in the <c>X-Telegram-Bot-Api-Secret-Token</c> webhook header.
    /// Allowed characters are A-Z, a-z, 0-9, '_' and '-' (1-256 chars). When empty, a stable
    /// secret is derived from <see cref="Token"/>.
    /// </summary>
    public string? WebhookSecret { get; set; }
}
