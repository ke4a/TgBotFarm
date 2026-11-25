using MongoDB.Bson.Serialization.Attributes;

namespace BotFarm.Core.Models;

/// <summary>
/// Stored settings of a registered bot. Secrets are kept only in protected form.
/// </summary>
[BsonIgnoreExtraElements]
public sealed record BotSettings
{
    /// <summary>Exact name of the registered bot identity.</summary>
    [BsonId] public string BotName { get; init; } = string.Empty;

    /// <summary>The bot token protected for this bot, or <see langword="null"/> when none is stored.</summary>
    public string? ProtectedToken { get; init; }

    /// <summary>
    /// Numeric Telegram bot ID taken from the token, unique across bots, or <see langword="null"/>
    /// when no token is stored.
    /// </summary>
    /// <remarks>Omitted from the document when absent so the sparse unique index skips it.</remarks>
    [BsonIgnoreIfNull] public long? TelegramBotId { get; init; }

    /// <summary>Non-zero chat that receives admin alerts, or <see langword="null"/> when none is stored.</summary>
    public long? AdminChatId { get; init; }

    /// <summary>
    /// Custom webhook secret protected for this bot, or <see langword="null"/> when the secret
    /// derived from the token is used.
    /// </summary>
    public string? ProtectedWebhookSecret { get; init; }

    /// <summary>Emoji shown next to the bot's name.</summary>
    public string? Emoji { get; init; }

    /// <summary>Time of the last accepted save.</summary>
    public DateTime UpdatedAtUtc { get; init; }

    /// <summary>User name of the administrator who made the last accepted save.</summary>
    public string UpdatedBy { get; init; } = string.Empty;
}
