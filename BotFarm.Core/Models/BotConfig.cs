namespace BotFarm.Core.Models;

/// <summary>
/// Per-bot configuration values loaded from application settings.
/// </summary>
public class BotConfig
{
    public bool Enabled { get; set; }

    public string Emoji { get; set; }

    public string Token { get; set; }

    public string Handle { get; set; }

    public long AdminChatId { get; set; }
}
