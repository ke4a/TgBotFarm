namespace BotFarm.Core.Models;

/// <summary>
/// Usability of a bot's stored settings.
/// </summary>
public enum BotSettingsState
{
    /// <summary>The settings store could not be read.</summary>
    Unknown,

    /// <summary>No settings are stored, or the token or admin chat is missing.</summary>
    Incomplete,

    /// <summary>A stored secret could not be unprotected and must be re-entered.</summary>
    Unreadable,

    /// <summary>The token and admin chat are present and every stored secret is readable.</summary>
    Configured
}

/// <summary>
/// Dashboard read model of a bot's settings. It never contains a token or webhook secret.
/// </summary>
public sealed record BotSettingsView
{
    public string BotName { get; init; } = string.Empty;

    public BotSettingsState State { get; init; }

    /// <summary>Whether a token is stored.</summary>
    public bool HasToken { get; init; }

    /// <summary>Numeric Telegram bot ID of the stored token.</summary>
    public long? TelegramBotId { get; init; }

    /// <summary>
    /// Username of the running bot when it reports the stored bot ID; otherwise <see langword="null"/>.
    /// </summary>
    public string? TelegramUsername { get; init; }

    public long? AdminChatId { get; init; }

    public string? Emoji { get; init; }

    /// <summary>Whether a custom webhook secret is stored instead of the one derived from the token.</summary>
    public bool HasCustomWebhookSecret { get; init; }

    public DateTime? UpdatedAtUtc { get; init; }

    public string? UpdatedBy { get; init; }

    /// <summary>
    /// Names of stored secrets that could not be unprotected (<c>Token</c>, <c>WebhookSecret</c>); empty
    /// unless <see cref="State"/> is <see cref="BotSettingsState.Unreadable"/>.
    /// </summary>
    public IReadOnlyList<string> UnreadableSettings { get; init; } = [];
}
