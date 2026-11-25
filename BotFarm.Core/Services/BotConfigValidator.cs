using BotFarm.Core.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BotFarm.Core.Services;

/// <summary>
/// Rules for each bot setting value: the token, the Telegram bot ID inside it, the admin chat, the webhook secret
/// and the emoji. Messages name the setting and never contain the token or secret.
/// </summary>
internal static partial class BotConfigValidator
{
    /// <summary>
    /// Returns whether <paramref name="token"/> has the form of a Telegram bot token: the bot ID (digits), a colon,
    /// then the secret part without spaces.
    /// </summary>
    public static bool IsValidToken(string? token) => TryGetTelegramBotId(token, out _);

    /// <summary>
    /// Reads the numeric Telegram bot ID, the part of <paramref name="token"/> before the colon.
    /// </summary>
    /// <returns><see langword="false"/> when <paramref name="token"/> is not a valid bot token.</returns>
    public static bool TryGetTelegramBotId(string? token, out long telegramBotId)
    {
        telegramBotId = 0;
        if (string.IsNullOrEmpty(token) || !TokenRegex().IsMatch(token))
        {
            return false;
        }

        return long.TryParse(
            token.AsSpan(0, token.IndexOf(':')),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out telegramBotId);
    }

    /// <summary>
    /// Returns whether <paramref name="adminChatId"/> names a chat: present and non-zero. Group chat IDs are negative.
    /// </summary>
    public static bool IsValidAdminChatId(long? adminChatId) => adminChatId is not null and not 0;

    /// <summary>
    /// Returns whether <paramref name="secret"/> is a webhook secret Telegram accepts:
    /// 1-256 characters from A-Z, a-z, 0-9, '_' and '-'.
    /// </summary>
    public static bool IsValidWebhookSecret(string? secret) =>
        !string.IsNullOrEmpty(secret) && WebhookSecretRegex().IsMatch(secret);

    /// <summary>
    /// Returns whether <paramref name="emoji"/> is absent or has at most
    /// <see cref="BotSettingsChange.MaxEmojiCharacters"/> visible characters and
    /// <see cref="BotSettingsChange.MaxEmojiLength"/> UTF-16 code units. A visible character is a text element, so an
    /// emoji with a skin tone, a flag or a joined sequence counts as one.
    /// </summary>
    public static bool IsValidEmoji(string? emoji) =>
        string.IsNullOrEmpty(emoji)
        || (emoji.Length <= BotSettingsChange.MaxEmojiLength
            && new StringInfo(emoji).LengthInTextElements <= BotSettingsChange.MaxEmojiCharacters);

    /// <summary>
    /// Returns the error shown when the named bot's webhook secret is not accepted by Telegram.
    /// </summary>
    public static string InvalidWebhookSecretMessage(string botName) =>
        $"The webhook secret of bot {botName} is invalid: it must be 1-256 characters " +
        "and contain only A-Z, a-z, 0-9, '_' and '-'.";

    [GeneratedRegex(@"^\d+:\S+$")]
    private static partial Regex TokenRegex();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,256}$")]
    private static partial Regex WebhookSecretRegex();
}
