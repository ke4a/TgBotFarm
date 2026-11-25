namespace BotFarm.Core.Models;

/// <summary>
/// Settings submitted by an administrator for one bot.
/// </summary>
public sealed record BotSettingsChange
{
    /// <summary>The most visible characters <see cref="Emoji"/> may have.</summary>
    public const int MaxEmojiCharacters = 1;

    /// <summary>The most UTF-16 code units <see cref="Emoji"/> may have; the longest emoji sequence fits.</summary>
    public const int MaxEmojiLength = 16;

    /// <summary>
    /// New token in the form <c>&lt;digits&gt;:&lt;non-space characters&gt;</c>; null or empty keeps the
    /// stored token.
    /// </summary>
    public string? Token { get; init; }

    /// <summary>Non-zero admin chat ID; null keeps the stored admin chat and is invalid when none is stored.</summary>
    public long? AdminChatId { get; init; }

    /// <summary>
    /// New custom webhook secret of 1-256 characters from <c>A-Z a-z 0-9 _ -</c>; null or empty
    /// keeps the stored secret.
    /// </summary>
    public string? WebhookSecret { get; init; }

    /// <summary>
    /// Removes the custom webhook secret so the one derived from the token is used. Cannot be combined
    /// with <see cref="WebhookSecret"/>.
    /// </summary>
    public bool UseDerivedWebhookSecret { get; init; }

    /// <summary>
    /// Emoji shown next to the bot's name, at most <see cref="MaxEmojiCharacters"/> visible characters and
    /// <see cref="MaxEmojiLength"/> UTF-16 code units; null or empty clears it.
    /// </summary>
    public string? Emoji { get; init; }

    /// <summary>Must be true when the new token belongs to a different Telegram bot than the stored one.</summary>
    public bool ConfirmAccountChange { get; init; }

    /// <summary>User name of the authenticated administrator making the change.</summary>
    public string ChangedBy { get; init; } = string.Empty;
}
