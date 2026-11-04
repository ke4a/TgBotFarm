using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.Extensions.Options;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace BotFarm.Core.Services;

/// <inheritdoc cref="IWebhookSecretProvider"/>
internal sealed class WebhookSecretProvider : IWebhookSecretProvider
{
    // Fixed purpose string so the derived secret is bound to webhook authentication only.
    private const string DerivationPurpose = "BotFarm.TelegramWebhookSecret.v1";

    private readonly IOptionsMonitor<BotConfig> _botConfigs;

    /// <summary>
    /// Creates the provider that reads per-bot configuration by bot name.
    /// </summary>
    public WebhookSecretProvider(IOptionsMonitor<BotConfig> botConfigs)
    {
        _botConfigs = botConfigs;
    }

    public string GetSecret(string botName)
    {
        var botConfig = _botConfigs.Get(botName);

        if (!string.IsNullOrWhiteSpace(botConfig?.WebhookSecret))
        {
            if (!BotConfigValidator.IsValidWebhookSecret(botConfig.WebhookSecret))
            {
                throw new InvalidOperationException(BotConfigValidator.InvalidWebhookSecretMessage(botName));
            }

            return botConfig.WebhookSecret;
        }

        if (string.IsNullOrEmpty(botConfig?.Token))
        {
            throw new InvalidOperationException(
                $"Cannot derive a webhook secret for {botName}: bot token is not configured.");
        }

        return DeriveSecret(botConfig.Token);
    }

    /// <summary>
    /// Derives a stable secret as unpadded base64url(HMAC-SHA256(key: token, data: purpose)),
    /// which is 43 characters from Telegram's allowed charset.
    /// </summary>
    internal static string DeriveSecret(string token)
    {
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(token),
            Encoding.UTF8.GetBytes(DerivationPurpose));

        return Base64Url.EncodeToString(hash);
    }
}
