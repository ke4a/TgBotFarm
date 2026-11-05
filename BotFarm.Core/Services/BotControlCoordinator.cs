using System.Collections.Concurrent;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Exceptions;
using BotFarm.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace BotFarm.Core.Services;

/// <summary>
/// Loads persisted bot state and applies operator commands to registered bots.
/// </summary>
public sealed class BotControlCoordinator : IBotControlService
{
    private const int MaximumTransitionAttempts = 3;

    private readonly IOptionsMonitor<BotConfig> _botConfigs;
    private readonly IBotControlStateStore _stateStore;
    private readonly IBotWebhookInitializer _webhookInitializer;
    private readonly INotificationService _notificationService;
    private readonly ILogger<BotControlCoordinator> _logger;
    private readonly IReadOnlyDictionary<string, IBotService> _botServices;
    private readonly ConcurrentDictionary<string, BotRuntimeStatus> _statuses = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _botOperationLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, BotControlState> _unconfirmedCommands = new(StringComparer.Ordinal);

    public BotControlCoordinator(
        IOptionsMonitor<BotConfig> botConfigs,
        IBotControlStateStore stateStore,
        IBotRegistry botRegistry,
        IBotWebhookInitializer webhookInitializer,
        INotificationService notificationService,
        ILogger<BotControlCoordinator> logger)
    {
        _botConfigs = botConfigs;
        _stateStore = stateStore;
        _webhookInitializer = webhookInitializer;
        _notificationService = notificationService;
        _logger = logger;
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
            return await ApplyDesiredStateWithRetriesAsync(bot, updated, cancellationToken);
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
            if (current.Status is not (BotRuntimeState.Unknown or BotRuntimeState.Error))
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
                    return await ApplyDesiredStateWithRetriesAsync(bot, saved, cancellationToken);
                }
                catch (Exception exception) when (IsPersistenceFailure(exception))
                {
                    SetUnknown(botName, unconfirmedCommand.DesiredEnabled, current.AppliedEnabled);
                    LogFailure(botName, exception);
                    return _statuses[botName];
                }
            }

            SetPending(botName, current.DesiredEnabled, current.AppliedEnabled);
            return await LoadAndApplyAsync(bot, cancellationToken);
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

    /// <inheritdoc />
    /// <remarks>
    /// If the bot is running, its updates are paused before <paramref name="action"/> and turned back on
    /// afterwards, retrying on failure. If they still can't be turned back on, the bot goes to
    /// <see cref="BotRuntimeState.Error"/> and an admin alert is sent.
    /// If the bot is turned off, its webhook is left alone and only <paramref name="action"/> runs.
    /// If the bot's on/off state hasn't been confirmed (<see cref="BotRuntimeState.Unknown"/>,
    /// <see cref="BotRuntimeState.Error"/> or <see cref="BotRuntimeState.Pending"/>), updates are paused if
    /// possible and stay off afterwards; use <see cref="RetryAsync"/> to bring the bot back.
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

            if (await TryPauseAsync(bot, cancellationToken))
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
    /// commands until the host stops.
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
                _logger.LogWarning("Pausing updates for bot {BotName} before shutdown.", bot.Name);
                SetPending(bot.Name, current.DesiredEnabled, current.AppliedEnabled);
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

        try
        {
            await _notificationService.SendErrorNotification(
                $"Updates for bot {bot.Name} could not be resumed after maintenance. Use Retry on the dashboard.",
                bot.Name);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "Could not send the resume failure alert for {BotName} ({FailureType}).",
                bot.Name,
                exception.GetType().Name);
        }
    }

    private async Task<BotRuntimeStatus> LoadAndApplyAsync(
        IBotService bot,
        CancellationToken cancellationToken)
    {
        BotControlState desired;
        try
        {
            desired = await GetOrSeedStateAsync(bot.Name, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetUnknown(bot.Name);
            throw;
        }
        catch (Exception exception) when (IsPersistenceFailure(exception))
        {
            SetUnknown(bot.Name);
            LogFailure(bot.Name, exception);
            return _statuses[bot.Name];
        }

        return await ApplyDesiredStateWithRetriesAsync(bot, desired, cancellationToken);
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

        var desiredEnabled = _botConfigs.Get(botName).Enabled;
        var initialState = new BotControlState
        {
            BotName = botName,
            DesiredEnabled = desiredEnabled,
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
                    "Bot state transition for {BotName} failed on attempt {Attempt} ({FailureType}); retrying.",
                    desired.BotName,
                    attempt,
                    exception.GetType().Name);
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

    private void LogFailure(string botName, Exception exception)
    {
        _logger.LogWarning(
            "Bot control operation failed for {BotName} ({FailureType}).",
            botName,
            exception.GetType().Name);
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
            or Telegram.Bot.Exceptions.ApiRequestException;

    private static bool IsPersistenceFailure(Exception exception) =>
        exception is MongoException
            or InvalidOperationException
            or TimeoutException
            or IOException;
}
