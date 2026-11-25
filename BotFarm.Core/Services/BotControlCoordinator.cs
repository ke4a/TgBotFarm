using System.Collections.Concurrent;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Exceptions;
using BotFarm.Core.Models;
using BotFarm.Core.Telemetry;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace BotFarm.Core.Services;

/// <summary>
/// Loads each bot's settings and persisted state, applies operator commands to registered bots and saves their
/// settings.
/// </summary>
/// <remarks>
/// A bot whose settings are incomplete or unreadable is reported as <see cref="BotRuntimeState.NotConfigured"/>
/// with its saved desired state and stays gated; a bot whose settings cannot be read stays
/// <see cref="BotRuntimeState.Unknown"/>. Settings are saved under the same per-bot operation lock as commands, so a
/// save never interleaves with an enable, disable, retry or maintenance pause.
/// </remarks>
public sealed class BotControlCoordinator : IBotControlService, IBotSettingsService
{
    private const int MaximumTransitionAttempts = 3;

    private readonly IBotSettingsProvider _settingsProvider;
    private readonly IBotSettingsStore _settingsStore;
    private readonly ITelegramTokenVerifier _tokenVerifier;
    private readonly IBotControlStateStore _stateStore;
    private readonly IBotWebhookInitializer _webhookInitializer;
    private readonly INotificationService _notificationService;
    private readonly ILogger<BotControlCoordinator> _logger;
    private readonly BotEventMetrics _eventMetrics;
    private readonly IReadOnlyDictionary<string, IBotService> _botServices;
    private readonly ConcurrentDictionary<string, BotRuntimeStatus> _statuses = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _botOperationLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, BotControlState> _unconfirmedCommands = new(StringComparer.Ordinal);

    public BotControlCoordinator(
        IBotSettingsProvider settingsProvider,
        IBotSettingsStore settingsStore,
        ITelegramTokenVerifier tokenVerifier,
        IBotControlStateStore stateStore,
        IBotRegistry botRegistry,
        IBotWebhookInitializer webhookInitializer,
        INotificationService notificationService,
        ILogger<BotControlCoordinator> logger,
        BotEventMetrics eventMetrics)
    {
        _settingsProvider = settingsProvider;
        _settingsStore = settingsStore;
        _tokenVerifier = tokenVerifier;
        _stateStore = stateStore;
        _webhookInitializer = webhookInitializer;
        _notificationService = notificationService;
        _logger = logger;
        _eventMetrics = eventMetrics;
        _botServices = botRegistry.GetServicesForAllBots<IBotService>().ToDictionary(bot => bot.Name, StringComparer.Ordinal);

        foreach (var botName in _botServices.Keys)
        {
            _statuses[botName] = UnknownStatus(botName);
            _botOperationLocks[botName] = new SemaphoreSlim(1, 1);
        }
    }

    public IReadOnlyList<BotRuntimeStatus> GetStatuses()
    {
        return _botServices.Keys
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => _statuses[name])
            .ToArray();
    }

    public bool CanProcessUpdates(string botName)
    {
        return _statuses.TryGetValue(botName, out var status)
               && status is { Status: BotRuntimeState.Applied, AppliedEnabled: true };
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        foreach (var bot in _botServices.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var operationLock = _botOperationLocks[bot.Name];
            await operationLock.WaitAsync(cancellationToken);
            try
            {
                if (_statuses[bot.Name].Status == BotRuntimeState.Applied)
                {
                    continue;
                }

                var current = _statuses[bot.Name];
                SetPending(bot.Name, current.DesiredEnabled, current.AppliedEnabled);
                await LoadAndApplyAsync(bot, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                SetUnknown(bot.Name);
                throw;
            }
            catch (Exception exception)
            {
                SetUnknown(bot.Name);
                LogFailure(bot.Name, exception);
            }
            finally
            {
                operationLock.Release();
            }
        }
    }

    public async Task<BotRuntimeStatus> SetEnabledAsync(
        string botName,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var bot = GetBot(botName);
        var operationLock = _botOperationLocks[botName];
        if (!await operationLock.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            throw new BotControlBusyException(botName);
        }

        try
        {
            var previous = _statuses[botName];
            if (previous.Status == BotRuntimeState.NotConfigured)
            {
                throw new BotNotConfiguredException(botName);
            }

            if (previous.Status != BotRuntimeState.Applied)
            {
                throw new BotControlBusyException(botName);
            }

            SetPending(botName, enabled, previous.AppliedEnabled);

            var commandId = Guid.NewGuid().ToString("N");
            var command = new BotControlState
            {
                BotName = botName,
                DesiredEnabled = enabled,
                LastCommandId = commandId,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _unconfirmedCommands[botName] = command;

            BotControlState updated;
            try
            {
                updated = await PersistDesiredStateAsync(command, cancellationToken);
            }
            catch (Exception exception) when (IsPersistenceFailure(exception))
            {
                SetUnknown(botName, enabled, previous.AppliedEnabled);
                LogFailure(botName, exception);
                throw new InvalidOperationException(
                    "The state change could not be confirmed; the bot remains gated.",
                    exception);
            }

            _unconfirmedCommands.TryRemove(botName, out _);
            var status = await ApplyDesiredStateWithRetriesAsync(bot, updated, cancellationToken);
            RecordOnOffChange(previous.AppliedEnabled, status);

            return status;
        }
        catch (OperationCanceledException)
        {
            SetUnknownFromOperation(botName);
            throw;
        }
        finally
        {
            operationLock.Release();
        }
    }

    public async Task<BotRuntimeStatus> RetryAsync(
        string botName,
        CancellationToken cancellationToken = default)
    {
        var bot = GetBot(botName);
        var operationLock = _botOperationLocks[botName];
        if (!await operationLock.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            throw new BotControlBusyException(botName);
        }

        try
        {
            var current = _statuses[botName];
            if (current.Status is not (BotRuntimeState.Unknown or BotRuntimeState.Error or BotRuntimeState.NotConfigured))
            {
                throw new BotControlBusyException(botName);
            }

            if (_unconfirmedCommands.TryGetValue(botName, out var unconfirmedCommand))
            {
                SetPending(botName, unconfirmedCommand.DesiredEnabled, current.AppliedEnabled);
                try
                {
                    var saved = await PersistDesiredStateAsync(unconfirmedCommand, cancellationToken);
                    _unconfirmedCommands.TryRemove(botName, out _);
                    var applied = await ApplyDesiredStateWithRetriesAsync(bot, saved, cancellationToken);
                    RecordOnOffChange(current.AppliedEnabled, applied);

                    return applied;
                }
                catch (Exception exception) when (IsPersistenceFailure(exception))
                {
                    SetUnknown(botName, unconfirmedCommand.DesiredEnabled, current.AppliedEnabled);
                    LogFailure(botName, exception);
                    return _statuses[botName];
                }
            }

            SetPending(botName, current.DesiredEnabled, current.AppliedEnabled);
            var loaded = await LoadAndApplyAsync(bot, cancellationToken);
            RecordOnOffChange(current.AppliedEnabled, loaded);

            return loaded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetUnknownFromOperation(botName);
            throw;
        }
        finally
        {
            operationLock.Release();
        }
    }

    /// <summary>
    /// Counts an operator's on/off change once it is applied: <see cref="BotEvent.BotEnabled"/> or
    /// <see cref="BotEvent.BotDisabled"/> when <paramref name="status"/> is applied in a different state from
    /// <paramref name="wasEnabled"/>.
    /// </summary>
    private void RecordOnOffChange(bool? wasEnabled, BotRuntimeStatus status)
    {
        if (status.Status != BotRuntimeState.Applied || status.AppliedEnabled is not { } enabled || enabled == wasEnabled)
        {
            return;
        }

        _eventMetrics.Record(status.BotName, enabled ? BotEvent.BotEnabled : BotEvent.BotDisabled);
    }

    public Task<WebhookInfo> GetWebhookInfoAsync(
        string botName,
        CancellationToken cancellationToken = default)
    {
        var bot = GetBot(botName);
        ThrowIfNotConfigured(botName);
        return bot.Client.GetWebhookInfo(cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Holds the bot's operation lock so the webhook is not changed while an enable or disable is applied.
    /// </remarks>
    public async Task DropPendingUpdatesAsync(
        string botName,
        CancellationToken cancellationToken = default)
    {
        var bot = GetBot(botName);
        var operationLock = _botOperationLocks[botName];
        if (!await operationLock.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            throw new BotControlBusyException(botName);
        }

        try
        {
            ThrowIfNotConfigured(botName);
            await bot.DropPendingUpdates(cancellationToken);
            _eventMetrics.Record(botName, BotEvent.PendingUpdatesDropped);
        }
        finally
        {
            operationLock.Release();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="BotSettingsView.TelegramUsername"/> comes from the account the bot reported when it was last
    /// enabled, and only while that account has the stored Telegram bot ID.
    /// </remarks>
    public BotSettingsView GetView(string botName)
    {
        var bot = GetBot(botName);
        var view = _settingsProvider.GetView(botName);
        var me = bot.Me;

        return me is not null && view.TelegramBotId == me.Id && !string.IsNullOrEmpty(me.Username)
            ? view with { TelegramUsername = me.Username }
            : view;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Leading and trailing white space is removed from the submitted token and webhook secret.
    /// Runs under the bot's operation lock and is rejected with <see cref="BotSettingsRejection.BotBusy"/> while
    /// another operation holds it. Settings that could not be read are read again first, so a save never writes over
    /// settings that are not loaded. A token or webhook secret change is rejected as busy while the bot is
    /// <see cref="BotRuntimeState.Unknown"/> or <see cref="BotRuntimeState.Pending"/>, since its on/off state is not
    /// known. A stored secret that could not be unprotected must be replaced in the change: a new token, and a new or
    /// cleared webhook secret.
    /// </para>
    /// <para>
    /// A new token is verified with Telegram and checked against the bot IDs other bots store before anything is
    /// written. A running bot is gated as <see cref="BotRuntimeState.Pending"/> before a new token or webhook secret is
    /// written, and gets its previous status back if the write fails. After the write, a token change on an enabled bot
    /// deletes the webhook with the old client (a failure is ignored, since a revoked token cannot do it), recreates
    /// the client and re-applies the bot; on a disabled bot it only recreates the client. A webhook secret change re-applies an enabled bot. A token or webhook secret
    /// change on a bot in <see cref="BotRuntimeState.Error"/> applies its desired on/off state again with the new
    /// settings, so a bot that failed because of its token can be repaired. A not configured bot whose settings become
    /// usable gets its saved desired state applied. If bringing the bot in line fails, the save is still reported as
    /// stored, with the bot in <see cref="BotRuntimeState.Error"/>.
    /// </para>
    /// <para>
    /// A stored change is logged as information with the names of the changed settings and the administrator, a
    /// rejection as a warning with the names of the submitted settings, the categories and the administrator. Setting
    /// values are never logged.
    /// </para>
    /// </remarks>
    public async Task<BotSettingsSaveResult> SaveAsync(
        string botName,
        BotSettingsChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        // Values copied from BotFather or a password manager often carry a trailing space or line break.
        change = change with { Token = change.Token?.Trim(), WebhookSecret = change.WebhookSecret?.Trim() };
        var bot = GetBot(botName);
        var operationLock = _botOperationLocks[botName];
        if (!await operationLock.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            var busy = Rejected(BotSettingsRejection.BotBusy);
            LogSaveOutcome(botName, change, busy);
            return busy;
        }

        try
        {
            var result = await SaveUnderLockAsync(bot, change, cancellationToken);
            LogSaveOutcome(botName, change, result);
            return result;
        }
        finally
        {
            operationLock.Release();
        }
    }

    private async Task<BotSettingsSaveResult> SaveUnderLockAsync(
        IBotService bot,
        BotSettingsChange change,
        CancellationToken cancellationToken)
    {
        var botName = bot.Name;
        if (_settingsProvider.GetState(botName) == BotSettingsState.Unknown)
        {
            var reloaded = await _settingsProvider.LoadAsync(botName, cancellationToken);
            bot.ReloadClient();
            if (reloaded == BotSettingsState.Unknown)
            {
                return Rejected(BotSettingsRejection.StoreUnavailable);
            }
        }

        var current = _settingsProvider.GetView(botName);
        var rejections = Validate(change, current);
        if (rejections.Count > 0)
        {
            return new BotSettingsSaveResult.Rejected(rejections);
        }

        var tokenChanged = !string.IsNullOrEmpty(change.Token);
        var webhookSecretChanged = !string.IsNullOrEmpty(change.WebhookSecret)
                                   || (change.UseDerivedWebhookSecret && current.HasCustomWebhookSecret);
        var previous = _statuses[botName];
        if ((tokenChanged || webhookSecretChanged)
            && previous.Status is BotRuntimeState.Unknown or BotRuntimeState.Pending)
        {
            return Rejected(BotSettingsRejection.BotBusy);
        }

        string? verifiedUsername = null;
        if (tokenChanged)
        {
            BotConfigValidator.TryGetTelegramBotId(change.Token, out var newTelegramBotId);
            switch (await _tokenVerifier.VerifyAsync(change.Token!, cancellationToken))
            {
                case TelegramTokenVerification.Verified verified:
                    verifiedUsername = verified.Username;
                    break;
                case TelegramTokenVerification.RejectedByTelegram:
                    return Rejected(BotSettingsRejection.RejectedByTelegram);
                default:
                    return Rejected(BotSettingsRejection.TelegramUnreachable);
            }

            string? owner;
            try
            {
                owner = await _settingsStore.FindBotNameByTelegramBotIdAsync(newTelegramBotId, cancellationToken);
            }
            catch (Exception exception) when (IsPersistenceFailure(exception))
            {
                LogFailure(botName, exception);
                return Rejected(BotSettingsRejection.StoreUnavailable);
            }

            if (!string.IsNullOrEmpty(owner) && !string.Equals(owner, botName, StringComparison.Ordinal))
            {
                return Rejected(BotSettingsRejection.DuplicateAccount);
            }

            if (current.TelegramBotId is { } storedTelegramBotId
                && storedTelegramBotId != newTelegramBotId
                && !change.ConfirmAccountChange)
            {
                return new BotSettingsSaveResult.NeedsAccountChangeConfirmation(storedTelegramBotId, newTelegramBotId);
            }
        }

        var changedSettings = GetChangedSettingNames(change, current);

        // The new secret is checked on incoming updates as soon as it is stored, while Telegram keeps sending the old
        // one until the bot is re-applied, so a running bot is gated first and those updates are deferred.
        var gated = (tokenChanged || webhookSecretChanged)
                    && previous is { Status: BotRuntimeState.Applied, AppliedEnabled: true };
        if (gated)
        {
            SetPending(botName, previous.DesiredEnabled, previous.AppliedEnabled);
        }

        BotSettingsState settingsState;
        try
        {
            settingsState = await _settingsProvider.PersistAsync(botName, change, cancellationToken);
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            RestoreUngated(gated, previous);
            return Rejected(BotSettingsRejection.DuplicateAccount);
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            RestoreUngated(gated, previous);
            LogFailure(botName, exception);
            return Rejected(BotSettingsRejection.StoreUnavailable);
        }
        catch
        {
            RestoreUngated(gated, previous);
            throw;
        }

        _logger.LogInformation(
            "Settings of bot {BotName} changed by {Administrator}: {ChangedSettings}.",
            botName,
            change.ChangedBy,
            FormatSettingNames(changedSettings));
        if (changedSettings.Count > 0)
        {
            _eventMetrics.Record(botName, BotEvent.SettingsChanged);
        }

        BotRuntimeStatus status;
        try
        {
            status = await ApplySettingsChangeAsync(
                bot,
                previous,
                settingsState,
                tokenChanged,
                webhookSecretChanged,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetUnknownFromOperation(botName);
            throw;
        }
        catch (Exception exception)
        {
            // The settings are stored, so the save is reported as done and the bot as failed; Retry re-applies it.
            var failed = _statuses[botName];
            status = new BotRuntimeStatus(
                botName,
                failed.DesiredEnabled,
                failed.AppliedEnabled,
                BotRuntimeState.Error,
                DateTime.UtcNow,
                $"Applying the saved settings failed ({exception.GetType().Name}).");
            _statuses[botName] = status;
            LogFailure(botName, exception);
        }

        return new BotSettingsSaveResult.Saved(GetView(botName), status, verifiedUsername);
    }

    /// <summary>
    /// Puts back the status a bot had before it was gated for a settings change that was not stored.
    /// </summary>
    private void RestoreUngated(bool gated, BotRuntimeStatus previous)
    {
        if (gated)
        {
            _statuses[previous.BotName] = previous;
        }
    }

    /// <summary>
    /// Brings the bot in line with settings that were just stored; see <see cref="SaveAsync"/>.
    /// </summary>
    private async Task<BotRuntimeStatus> ApplySettingsChangeAsync(
        IBotService bot,
        BotRuntimeStatus previous,
        BotSettingsState settingsState,
        bool tokenChanged,
        bool webhookSecretChanged,
        CancellationToken cancellationToken)
    {
        var botName = bot.Name;
        if (previous.Status == BotRuntimeState.NotConfigured)
        {
            bot.ReloadClient();
            if (settingsState != BotSettingsState.Configured)
            {
                return _statuses[botName];
            }

            BotControlState desired;
            try
            {
                desired = await GetOrSeedStateAsync(botName, cancellationToken);
            }
            catch (Exception exception) when (IsPersistenceFailure(exception))
            {
                SetUnknown(botName);
                LogFailure(botName, exception);
                return _statuses[botName];
            }

            return await ApplyDesiredStateWithRetriesAsync(bot, desired, cancellationToken);
        }

        if (!tokenChanged && !webhookSecretChanged)
        {
            return _statuses[botName];
        }

        var failed = previous.Status == BotRuntimeState.Error;
        if (!failed && previous.AppliedEnabled != true)
        {
            if (tokenChanged)
            {
                bot.ReloadClient();
            }

            return _statuses[botName];
        }

        if (tokenChanged)
        {
            // Gated while the old account's webhook is removed, so no update arrives between the two clients.
            SetPending(botName, previous.DesiredEnabled, previous.AppliedEnabled);
            _ = await TryPauseAsync(bot, cancellationToken);
            bot.ReloadClient();
        }

        var reapplied = new BotControlState
        {
            BotName = botName,
            DesiredEnabled = !failed || previous.DesiredEnabled == true,
            UpdatedAtUtc = DateTime.UtcNow
        };

        return await ApplyDesiredStateWithRetriesAsync(bot, reapplied, cancellationToken);
    }

    /// <summary>
    /// Checks the submitted values against the format rules and returns one rejection per broken setting. A stored
    /// secret that could not be unprotected must be replaced: a missing new token is rejected as
    /// <see cref="BotSettingsRejection.InvalidToken"/>, and a missing new or cleared webhook secret as
    /// <see cref="BotSettingsRejection.InvalidWebhookSecret"/>.
    /// </summary>
    private static List<BotSettingsRejection> Validate(BotSettingsChange change, BotSettingsView current)
    {
        var rejections = new List<BotSettingsRejection>();
        if (string.IsNullOrEmpty(change.Token)
                ? current.UnreadableSettings.Contains(nameof(BotConfig.Token))
                : !BotConfigValidator.IsValidToken(change.Token))
        {
            rejections.Add(BotSettingsRejection.InvalidToken);
        }

        if (!BotConfigValidator.IsValidAdminChatId(change.AdminChatId ?? current.AdminChatId))
        {
            rejections.Add(BotSettingsRejection.InvalidAdminChat);
        }

        if (string.IsNullOrEmpty(change.WebhookSecret)
                ? !change.UseDerivedWebhookSecret && current.UnreadableSettings.Contains(nameof(BotConfig.WebhookSecret))
                : change.UseDerivedWebhookSecret || !BotConfigValidator.IsValidWebhookSecret(change.WebhookSecret))
        {
            rejections.Add(BotSettingsRejection.InvalidWebhookSecret);
        }

        if (!BotConfigValidator.IsValidEmoji(change.Emoji))
        {
            rejections.Add(BotSettingsRejection.InvalidEmoji);
        }

        return rejections;
    }

    private static BotSettingsSaveResult.Rejected Rejected(BotSettingsRejection rejection) => new([rejection]);

    /// <summary>
    /// Logs a save that was not stored: a rejection as a warning, a pending account change as information. Only
    /// setting names, categories and bot IDs are logged, never a value.
    /// </summary>
    private void LogSaveOutcome(string botName, BotSettingsChange change, BotSettingsSaveResult result)
    {
        switch (result)
        {
            case BotSettingsSaveResult.Rejected rejected:
                _logger.LogWarning(
                    "Settings change of bot {BotName} by {Administrator} was rejected ({Rejections}): {Settings}.",
                    botName,
                    change.ChangedBy,
                    string.Join(", ", rejected.Rejections),
                    FormatSettingNames(GetChangedSettingNames(change, _settingsProvider.GetView(botName))));
                break;
            case BotSettingsSaveResult.NeedsAccountChangeConfirmation confirmation:
                _logger.LogInformation(
                    "Settings change of bot {BotName} by {Administrator} waits for confirmation of the account change from Telegram bot {StoredTelegramBotId} to {NewTelegramBotId}.",
                    botName,
                    change.ChangedBy,
                    confirmation.StoredTelegramBotId,
                    confirmation.NewTelegramBotId);
                break;
        }
    }

    /// <summary>
    /// Names of the settings <paramref name="change"/> sets to a value other than the stored one. A submitted token
    /// always counts as changed, since secrets are not compared.
    /// </summary>
    private static List<string> GetChangedSettingNames(BotSettingsChange change, BotSettingsView current)
    {
        var names = new List<string>();
        if (!string.IsNullOrEmpty(change.Token))
        {
            names.Add(nameof(BotConfig.Token));
        }

        if (change.AdminChatId is { } adminChatId && adminChatId != current.AdminChatId)
        {
            names.Add(nameof(BotConfig.AdminChatId));
        }

        if (!string.IsNullOrEmpty(change.WebhookSecret) || (change.UseDerivedWebhookSecret && current.HasCustomWebhookSecret))
        {
            names.Add(nameof(BotConfig.WebhookSecret));
        }

        var emoji = string.IsNullOrEmpty(change.Emoji) ? null : change.Emoji;
        if (!string.Equals(emoji, current.Emoji, StringComparison.Ordinal))
        {
            names.Add(nameof(BotConfig.Emoji));
        }

        return names;
    }

    private static string FormatSettingNames(IReadOnlyCollection<string> names) =>
        names.Count == 0 ? "none" : string.Join(", ", names);

    /// <summary>
    /// Whether the bot's current settings give it a Telegram client.
    /// </summary>
    private bool HasUsableSettings(string botName) =>
        _settingsProvider.GetState(botName) == BotSettingsState.Configured;

    /// <inheritdoc />
    /// <remarks>
    /// If the bot is running, its updates are paused before <paramref name="action"/> and turned back on
    /// afterwards, retrying on failure. If they still can't be turned back on, the bot goes to
    /// <see cref="BotRuntimeState.Error"/> and an admin alert is sent.
    /// If the bot is turned off, its webhook is left alone and only <paramref name="action"/> runs.
    /// If the bot's on/off state hasn't been confirmed (<see cref="BotRuntimeState.Unknown"/>,
    /// <see cref="BotRuntimeState.Error"/> or <see cref="BotRuntimeState.Pending"/>), updates are paused if
    /// possible and stay off afterwards; use <see cref="RetryAsync"/> to bring the bot back. A bot without usable
    /// settings has no webhook to pause, so only <paramref name="action"/> runs.
    /// </remarks>
    public async Task<T> RunWithUpdatesPausedAsync<T>(
        string botName,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        var bot = GetBot(botName);
        var operationLock = _botOperationLocks[botName];
        await operationLock.WaitAsync(cancellationToken);
        try
        {
            var previous = _statuses[botName];
            if (previous.Status == BotRuntimeState.Applied)
            {
                if (previous.AppliedEnabled != true)
                {
                    return await action(cancellationToken);
                }

                SetPending(botName, previous.DesiredEnabled, previous.AppliedEnabled);
                try
                {
                    if (!await TryPauseAsync(bot, cancellationToken))
                    {
                        throw new BotUpdatesPauseException(botName);
                    }

                    SetPending(botName, previous.DesiredEnabled, false);
                    return await action(cancellationToken);
                }
                finally
                {
                    await ResumeAfterMaintenanceAsync(bot);
                }
            }

            if (HasUsableSettings(botName) && await TryPauseAsync(bot, cancellationToken))
            {
                _statuses[botName] = previous with { AppliedEnabled = false };
            }

            return await action(cancellationToken);
        }
        finally
        {
            operationLock.Release();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Each bot is left <see cref="BotRuntimeState.Pending"/>, so it stays gated and rejects operator
    /// commands until the host stops. A bot without usable settings has no webhook to pause and is only gated.
    /// </remarks>
    public async Task PauseAllForShutdownAsync(CancellationToken cancellationToken = default)
    {
        foreach (var bot in _botServices.Values)
        {
            var operationLock = _botOperationLocks[bot.Name];
            await operationLock.WaitAsync(cancellationToken);
            var current = _statuses[bot.Name];
            try
            {
                SetPending(bot.Name, current.DesiredEnabled, current.AppliedEnabled);
                if (!HasUsableSettings(bot.Name))
                {
                    continue;
                }

                _logger.LogWarning("Pausing updates for bot {BotName} before shutdown.", bot.Name);
                if (await TryPauseAsync(bot, cancellationToken))
                {
                    SetPending(bot.Name, current.DesiredEnabled, false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                SetUnknown(bot.Name, current.DesiredEnabled, current.AppliedEnabled);
                throw;
            }
            finally
            {
                operationLock.Release();
            }
        }
    }

    private async Task<bool> TryPauseAsync(IBotService bot, CancellationToken cancellationToken)
    {
        try
        {
            if (await _webhookInitializer.DisableAsync(bot, cancellationToken))
            {
                return true;
            }

            _logger.LogWarning("Webhook pause for {BotName} was not confirmed.", bot.Name);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsTransitionFailure(exception))
        {
            LogFailure(bot.Name, exception);
        }

        return false;
    }

    private async Task ResumeAfterMaintenanceAsync(IBotService bot)
    {
        var desired = new BotControlState
        {
            BotName = bot.Name,
            DesiredEnabled = true,
            UpdatedAtUtc = DateTime.UtcNow
        };

        // Not cancellable, so a cancelled or failed action never leaves the bot paused.
        var status = await ApplyDesiredStateWithRetriesAsync(bot, desired, CancellationToken.None);
        if (status.Status != BotRuntimeState.Error)
        {
            return;
        }

        await _notificationService.SendErrorNotification(
            $"Updates for bot {bot.Name} could not be resumed after maintenance. Use Retry on the dashboard.",
            bot.Name);
    }

    /// <summary>
    /// Loads the bot's settings, then its desired state (seeded disabled when missing), and applies the desired
    /// state when the settings are usable. Any failure other than cancellation leaves the bot
    /// <see cref="BotRuntimeState.Unknown"/> so it stays gated and can be retried.
    /// </summary>
    private async Task<BotRuntimeStatus> LoadAndApplyAsync(
        IBotService bot,
        CancellationToken cancellationToken)
    {
        BotSettingsState settingsState;
        BotControlState desired;
        try
        {
            settingsState = await _settingsProvider.LoadAsync(bot.Name, cancellationToken);
            bot.ReloadClient();
            if (settingsState == BotSettingsState.Unknown)
            {
                SetUnknown(bot.Name);
                return _statuses[bot.Name];
            }

            desired = await GetOrSeedStateAsync(bot.Name, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetUnknown(bot.Name);
            throw;
        }
        catch (Exception exception)
        {
            SetUnknown(bot.Name);
            LogFailure(bot.Name, exception);
            return _statuses[bot.Name];
        }

        if (settingsState != BotSettingsState.Configured)
        {
            SetNotConfigured(bot.Name, desired.DesiredEnabled);
            return _statuses[bot.Name];
        }

        try
        {
            return await ApplyDesiredStateWithRetriesAsync(bot, desired, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            SetUnknown(bot.Name, desired.DesiredEnabled, null);
            LogFailure(bot.Name, exception);
            return _statuses[bot.Name];
        }
    }

    private async Task<BotControlState> PersistDesiredStateAsync(
        BotControlState command,
        CancellationToken cancellationToken)
    {
        var commandId = command.LastCommandId
                        ?? throw new InvalidOperationException("The state change command ID is missing.");
        try
        {
            return await _stateStore.TrySetDesiredStateAsync(
                       command.BotName,
                       command.DesiredEnabled,
                       commandId,
                       command.UpdatedAtUtc,
                       cancellationToken)
                   ?? throw new InvalidOperationException("The bot's authoritative state is not available.");
        }
        catch (MongoException exception)
        {
            BotControlState? readBack;
            try
            {
                readBack = await _stateStore.GetAsync(command.BotName, cancellationToken);
            }
            catch (Exception readBackException) when (IsPersistenceFailure(readBackException))
            {
                throw new InvalidOperationException(
                    "The state change could not be confirmed.",
                    readBackException);
            }

            if (readBack?.LastCommandId == command.LastCommandId)
            {
                return readBack;
            }

            throw new InvalidOperationException(
                "The state change could not be confirmed.",
                exception);
        }
    }

    private async Task<BotControlState> GetOrSeedStateAsync(
        string botName,
        CancellationToken cancellationToken)
    {
        var savedState = await _stateStore.GetAsync(botName, cancellationToken);
        if (savedState is not null)
        {
            return savedState;
        }

        var initialState = new BotControlState
        {
            BotName = botName,
            DesiredEnabled = false,
            LastCommandId = null,
            UpdatedAtUtc = DateTime.UtcNow
        };

        if (await _stateStore.TryInsertAsync(initialState, cancellationToken))
        {
            return initialState;
        }

        return await _stateStore.GetAsync(botName, cancellationToken)
               ?? throw new InvalidOperationException("The bot's authoritative state is not available.");
    }

    private async Task<BotRuntimeStatus> ApplyDesiredStateWithRetriesAsync(
        IBotService bot,
        BotControlState desired,
        CancellationToken cancellationToken)
    {
        SetPending(desired.BotName, desired.DesiredEnabled, _statuses[desired.BotName].AppliedEnabled);

        for (var attempt = 1; attempt <= MaximumTransitionAttempts; attempt++)
        {
            try
            {
                if (desired.DesiredEnabled)
                {
                    await _webhookInitializer.EnableAsync(bot, cancellationToken);
                }
                else if (!await _webhookInitializer.DisableAsync(bot, cancellationToken))
                {
                    throw new InvalidOperationException("Webhook pause was not confirmed.");
                }

                var applied = new BotRuntimeStatus(
                    desired.BotName,
                    desired.DesiredEnabled,
                    desired.DesiredEnabled,
                    BotRuntimeState.Applied,
                    DateTime.UtcNow,
                    null);
                _statuses[desired.BotName] = applied;
                return applied;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                SetUnknown(desired.BotName);
                throw;
            }
            catch (Exception exception) when (IsTransitionFailure(exception))
            {
                if (attempt == MaximumTransitionAttempts)
                {
                    SetError(desired, exception, attempt);
                    return _statuses[desired.BotName];
                }

                _logger.LogWarning(
                    exception,
                    "Bot state transition for {BotName} failed on attempt {Attempt}; retrying.",
                    desired.BotName,
                    attempt);
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }

        throw new InvalidOperationException("Bot state transition ended without a result.");
    }

    private IBotService GetBot(string botName)
    {
        if (!_botServices.TryGetValue(botName, out var bot))
        {
            throw new ArgumentException("The requested bot is not registered.", nameof(botName));
        }

        return bot;
    }

    private void SetPending(string botName, bool? desiredEnabled, bool? appliedEnabled)
    {
        _statuses[botName] = new BotRuntimeStatus(
            botName,
            desiredEnabled,
            appliedEnabled,
            BotRuntimeState.Pending,
            DateTime.UtcNow,
            null);
    }

    private void SetNotConfigured(string botName, bool desiredEnabled)
    {
        _statuses[botName] = new BotRuntimeStatus(
            botName,
            desiredEnabled,
            false,
            BotRuntimeState.NotConfigured,
            DateTime.UtcNow,
            null);
    }

    private void ThrowIfNotConfigured(string botName)
    {
        if (_statuses[botName].Status == BotRuntimeState.NotConfigured)
        {
            throw new BotNotConfiguredException(botName);
        }
    }

    private void SetError(BotControlState state, Exception exception, int attempts)
    {
        _statuses[state.BotName] = new BotRuntimeStatus(
            state.BotName,
            state.DesiredEnabled,
            _statuses[state.BotName].AppliedEnabled,
            BotRuntimeState.Error,
            DateTime.UtcNow,
            $"Transition failed ({exception.GetType().Name}) after {attempts} attempts.");
        LogFailure(state.BotName, exception);
    }

    private void SetUnknown(string botName)
    {
        _statuses[botName] = UnknownStatus(botName);
    }

    private void SetUnknown(string botName, bool? desiredEnabled, bool? appliedEnabled)
    {
        _statuses[botName] = new BotRuntimeStatus(
            botName,
            desiredEnabled,
            appliedEnabled,
            BotRuntimeState.Unknown,
            DateTime.UtcNow,
            "The requested state has not been confirmed.");
    }

    private void SetUnknownFromOperation(string botName)
    {
        if (_unconfirmedCommands.TryGetValue(botName, out var command))
        {
            SetUnknown(botName, command.DesiredEnabled, _statuses[botName].AppliedEnabled);
            return;
        }

        SetUnknown(botName);
    }

    /// <summary>
    /// Logs a failed operation as a warning with the bot name and the exception, adding the Telegram error code when
    /// Telegram answered.
    /// </summary>
    private void LogFailure(string botName, Exception exception)
    {
        if (exception is Telegram.Bot.Exceptions.ApiRequestException apiException)
        {
            _logger.LogWarning(
                exception,
                "Bot control operation failed for {BotName} (error code {ErrorCode}).",
                botName,
                apiException.ErrorCode);
            return;
        }

        _logger.LogWarning(exception, "Bot control operation failed for {BotName}.", botName);
    }

    private static BotRuntimeStatus UnknownStatus(string botName) =>
        new(botName, null, null, BotRuntimeState.Unknown, null, null);

    private static bool IsTransitionFailure(Exception exception) =>
        exception is MongoException
            or HttpRequestException
            or InvalidOperationException
            or OperationCanceledException
            or TimeoutException
            or IOException
            or Telegram.Bot.Exceptions.RequestException;

    private static bool IsPersistenceFailure(Exception exception) =>
        exception is MongoException
            or InvalidOperationException
            or TimeoutException
            or IOException;
}
