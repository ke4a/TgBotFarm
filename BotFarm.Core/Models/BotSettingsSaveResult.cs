namespace BotFarm.Core.Models;

/// <summary>
/// Outcome of saving a bot's settings.
/// </summary>
public abstract record BotSettingsSaveResult
{
    private BotSettingsSaveResult()
    {
    }

    /// <summary>
    /// The settings were stored.
    /// </summary>
    /// <param name="View">The updated settings view.</param>
    /// <param name="Status">The bot's runtime status after the save.</param>
    /// <param name="VerifiedTelegramUsername">
    /// The username Telegram reported when a new token was verified; otherwise <see langword="null"/>.
    /// </param>
    public sealed record Saved(
        BotSettingsView View,
        BotRuntimeStatus Status,
        string? VerifiedTelegramUsername) : BotSettingsSaveResult;

    /// <summary>
    /// The new token belongs to a different Telegram bot; nothing was stored. Saving again with
    /// <see cref="BotSettingsChange.ConfirmAccountChange"/> confirms the change.
    /// </summary>
    public sealed record NeedsAccountChangeConfirmation(
        long StoredTelegramBotId,
        long NewTelegramBotId) : BotSettingsSaveResult;

    /// <summary>
    /// The change was not stored; one rejection is listed per problem.
    /// </summary>
    public sealed record Rejected(IReadOnlyList<BotSettingsRejection> Rejections) : BotSettingsSaveResult;
}

/// <summary>
/// Reason a settings change was not stored.
/// </summary>
public enum BotSettingsRejection
{
    /// <summary>The token breaks the format rules.</summary>
    InvalidToken,

    /// <summary>The admin chat is missing or zero.</summary>
    InvalidAdminChat,

    /// <summary>The webhook secret breaks the format rules or is combined with the derived secret.</summary>
    InvalidWebhookSecret,

    /// <summary>The emoji is more than one visible character.</summary>
    InvalidEmoji,

    /// <summary>Telegram did not accept the token.</summary>
    RejectedByTelegram,

    /// <summary>The token could not be verified with Telegram.</summary>
    TelegramUnreachable,

    /// <summary>Another registered bot stores the same Telegram bot ID.</summary>
    DuplicateAccount,

    /// <summary>
    /// The change needs a re-apply and the bot is unknown, pending or failed, or another operation
    /// holds the bot.
    /// </summary>
    BotBusy,

    /// <summary>The write could not be confirmed.</summary>
    StoreUnavailable
}
