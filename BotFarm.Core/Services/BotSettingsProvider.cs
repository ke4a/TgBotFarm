using System.Collections.Concurrent;
using System.Security.Cryptography;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace BotFarm.Core.Services;

/// <inheritdoc cref="IBotSettingsProvider"/>
/// <remarks>
/// Keeps each bot's stored document and its unprotected secrets in memory. <see cref="BotSettingsOptionsSetup"/>
/// copies them into the bot's named <see cref="BotConfig"/>, and every load or persist fires the bot's
/// <see cref="BotSettingsChangeTokenSource"/> so <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>
/// reads them again. Secrets that cannot be unprotected are left out and named in the view, the other stored fields
/// stay in the view, and each load that finds such secrets logs one error naming them, never their values.
/// </remarks>
internal sealed class BotSettingsProvider : IBotSettingsProvider
{
    private readonly IBotSettingsStore _store;
    private readonly BotSettingsProtector _protector;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BotSettingsProvider> _logger;
    private readonly ConcurrentDictionary<string, LoadedSettings> _settings = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConfigurationReloadToken> _changeTokens = new(StringComparer.Ordinal);

    public BotSettingsProvider(
        IBotSettingsStore store,
        BotSettingsProtector protector,
        TimeProvider timeProvider,
        ILogger<BotSettingsProvider> logger)
    {
        _store = store;
        _protector = protector;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<BotSettingsState> LoadAsync(string botName, CancellationToken cancellationToken = default)
    {
        BotSettings? stored;
        try
        {
            stored = await _store.GetAsync(botName, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException
                                          || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "Settings of bot {BotName} could not be read.", botName);
            Replace(botName, null);
            return BotSettingsState.Unknown;
        }

        var loaded = stored is null ? LoadedSettings.Empty : Unprotect(botName, stored);
        Replace(botName, loaded);
        return loaded.State;
    }

    public BotSettingsState GetState(string botName) =>
        _settings.TryGetValue(botName, out var loaded) ? loaded.State : BotSettingsState.Unknown;

    public BotSettingsView GetView(string botName)
    {
        if (!_settings.TryGetValue(botName, out var loaded))
        {
            return new BotSettingsView
            {
                BotName = botName,
                State = BotSettingsState.Unknown
            };
        }

        var stored = loaded.Stored;
        return new BotSettingsView
        {
            BotName = botName,
            State = loaded.State,
            HasToken = stored?.ProtectedToken is not null,
            TelegramBotId = stored?.TelegramBotId,
            AdminChatId = stored?.AdminChatId,
            Emoji = stored?.Emoji,
            HasCustomWebhookSecret = stored?.ProtectedWebhookSecret is not null,
            UpdatedAtUtc = stored?.UpdatedAtUtc,
            UpdatedBy = stored?.UpdatedBy,
            UnreadableSettings = loaded.UnreadableSettings
        };
    }

    public async Task<BotSettingsState> PersistAsync(
        string botName,
        BotSettingsChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (!_settings.TryGetValue(botName, out var current))
        {
            throw new InvalidOperationException($"The settings of bot '{botName}' are not loaded.");
        }

        var stored = current.Stored;
        var token = current.Token;
        var protectedToken = stored?.ProtectedToken;
        var telegramBotId = stored?.TelegramBotId;
        var unreadable = current.UnreadableSettings.ToList();
        if (!string.IsNullOrEmpty(change.Token))
        {
            if (!BotConfigValidator.TryGetTelegramBotId(change.Token, out var newTelegramBotId))
            {
                throw new ArgumentException("The token is not a valid Telegram bot token.", nameof(change));
            }

            token = change.Token;
            protectedToken = _protector.Protect(botName, change.Token);
            telegramBotId = newTelegramBotId;
            unreadable.Remove(nameof(BotConfig.Token));
        }

        var webhookSecret = current.WebhookSecret;
        var protectedWebhookSecret = stored?.ProtectedWebhookSecret;
        if (!string.IsNullOrEmpty(change.WebhookSecret))
        {
            webhookSecret = change.WebhookSecret;
            protectedWebhookSecret = _protector.Protect(botName, change.WebhookSecret);
            unreadable.Remove(nameof(BotConfig.WebhookSecret));
        }
        else if (change.UseDerivedWebhookSecret)
        {
            webhookSecret = null;
            protectedWebhookSecret = null;
            unreadable.Remove(nameof(BotConfig.WebhookSecret));
        }

        var updated = new BotSettings
        {
            BotName = botName,
            ProtectedToken = protectedToken,
            TelegramBotId = telegramBotId,
            AdminChatId = change.AdminChatId ?? stored?.AdminChatId,
            ProtectedWebhookSecret = protectedWebhookSecret,
            Emoji = string.IsNullOrEmpty(change.Emoji) ? null : change.Emoji,
            UpdatedAtUtc = TruncateToMilliseconds(_timeProvider.GetUtcNow().UtcDateTime),
            UpdatedBy = change.ChangedBy
        };

        await _store.UpsertAsync(updated, cancellationToken);

        var loaded = LoadedSettings.Create(updated, token, webhookSecret, unreadable);
        Replace(botName, loaded);
        return loaded.State;
    }

    /// <summary>
    /// Copies the bot's current settings into <paramref name="options"/>; a bot without usable settings gets an empty
    /// token and a zero admin chat.
    /// </summary>
    internal void Configure(string botName, BotConfig options)
    {
        _settings.TryGetValue(botName, out var loaded);
        options.Token = loaded?.Token ?? string.Empty;
        options.AdminChatId = loaded?.Stored?.AdminChatId ?? 0;
        options.WebhookSecret = loaded?.WebhookSecret;
        options.Emoji = loaded?.Stored?.Emoji;
    }

    /// <summary>
    /// Returns a token that fires the next time the bot's settings are loaded or persisted.
    /// </summary>
    internal IChangeToken GetChangeToken(string botName) =>
        _changeTokens.GetOrAdd(botName, _ => new ConfigurationReloadToken());

    private void Replace(string botName, LoadedSettings? loaded)
    {
        if (loaded is null)
        {
            _settings.TryRemove(botName, out _);
        }
        else
        {
            _settings[botName] = loaded;
        }

        _changeTokens.TryRemove(botName, out var previous);
        previous?.OnReload();
    }

    private LoadedSettings Unprotect(string botName, BotSettings stored)
    {
        var unreadable = new List<string>();
        var token = TryUnprotect(botName, stored.ProtectedToken, nameof(BotConfig.Token), unreadable);
        var webhookSecret = TryUnprotect(
            botName,
            stored.ProtectedWebhookSecret,
            nameof(BotConfig.WebhookSecret),
            unreadable);

        if (unreadable.Count > 0)
        {
            _logger.LogError(
                "Settings of bot {BotName} could not be decrypted and must be entered again: {SettingNames}.",
                botName,
                string.Join(", ", unreadable));
        }

        return LoadedSettings.Create(stored, token, webhookSecret, unreadable);
    }

    private string? TryUnprotect(string botName, string? protectedValue, string settingName, List<string> unreadable)
    {
        if (protectedValue is null)
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(botName, protectedValue);
        }
        catch (CryptographicException)
        {
            // Data protection reports every unreadable value (wrong key ring, other bot's purpose, altered or
            // malformed payload) as a CryptographicException; anything else is a fault, not unreadable settings.
            unreadable.Add(settingName);
            return null;
        }
    }

    private static DateTime TruncateToMilliseconds(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);

    /// <summary>
    /// A bot's stored document with its unprotected secrets.
    /// </summary>
    private sealed record LoadedSettings(
        BotSettingsState State,
        BotSettings? Stored,
        string? Token,
        string? WebhookSecret,
        IReadOnlyList<string> UnreadableSettings)
    {
        public static LoadedSettings Empty { get; } = new(BotSettingsState.Incomplete, null, null, null, []);

        public static LoadedSettings Create(
            BotSettings stored,
            string? token,
            string? webhookSecret,
            IReadOnlyList<string> unreadableSettings)
        {
            var state = unreadableSettings.Count > 0
                ? BotSettingsState.Unreadable
                : token is not null && BotConfigValidator.IsValidAdminChatId(stored.AdminChatId)
                    ? BotSettingsState.Configured
                    : BotSettingsState.Incomplete;

            return new LoadedSettings(state, stored, token, webhookSecret, unreadableSettings);
        }
    }
}
