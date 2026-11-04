using BotFarm.Core.Models;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace BotFarm.Core.Services;

/// <summary>
/// Checks each bot's <see cref="BotConfig"/> so that missing or malformed settings stop the app at startup
/// with a message naming the setting. Messages never contain the configured token or secret.
/// </summary>
internal sealed partial class BotConfigValidator : IValidateOptions<BotConfig>
{
    /// <summary>
    /// Returns the configuration section that holds the settings of the named bot.
    /// </summary>
    public static string SectionPath(string botName) => $"Bots:{botName}:{nameof(BotConfig)}";

    /// <summary>
    /// Returns whether <paramref name="secret"/> is a webhook secret Telegram accepts:
    /// 1-256 characters from A-Z, a-z, 0-9, '_' and '-'.
    /// </summary>
    public static bool IsValidWebhookSecret(string secret) => WebhookSecretRegex().IsMatch(secret);

    /// <summary>
    /// Returns the error shown when the named bot's webhook secret is not accepted by Telegram.
    /// </summary>
    public static string InvalidWebhookSecretMessage(string botName) =>
        $"{SectionPath(botName)}:{nameof(BotConfig.WebhookSecret)} is invalid: it must be 1-256 characters " +
        "and contain only A-Z, a-z, 0-9, '_' and '-'.";

    public ValidateOptionsResult Validate(string? name, BotConfig options)
    {
        var botName = name ?? Options.DefaultName;
        var path = SectionPath(botName);
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Token))
        {
            failures.Add($"{path}:{nameof(BotConfig.Token)} is required.");
        }
        else if (!TokenRegex().IsMatch(options.Token))
        {
            failures.Add(
                $"{path}:{nameof(BotConfig.Token)} is not a valid Telegram bot token: " +
                "expected the bot ID (digits), a colon, then the secret part.");
        }

        if (!string.IsNullOrWhiteSpace(options.WebhookSecret) && !IsValidWebhookSecret(options.WebhookSecret))
        {
            failures.Add(InvalidWebhookSecretMessage(botName));
        }

        if (options.AdminChatId == 0)
        {
            failures.Add($"{path}:{nameof(BotConfig.AdminChatId)} is required: admin alerts are sent to this chat.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    [GeneratedRegex(@"^\d+:\S+$")]
    private static partial Regex TokenRegex();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,256}$")]
    private static partial Regex WebhookSecretRegex();
}
