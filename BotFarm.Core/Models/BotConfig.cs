namespace BotFarm.Core.Models;

/// <summary>
/// Per-bot configuration values loaded from application settings.
/// </summary>
public class BotConfig
{
    public string Emoji { get; set; }

    public string Token { get; set; }

    public string Handle { get; set; }

    public long AdminChatId { get; set; }

    /// <summary>
    /// Optional secret Telegram echoes in the <c>X-Telegram-Bot-Api-Secret-Token</c> webhook header.
    /// Allowed characters are A-Z, a-z, 0-9, '_' and '-' (1-256 chars). When empty, a stable
    /// secret is derived from <see cref="Token"/>.
    /// </summary>
    public string? WebhookSecret { get; set; }
}
