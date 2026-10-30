using System.Globalization;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;

namespace BotFarm.HostedServices;

/// <summary>
/// Backs up every bot's database once a day at the time set in <c>ScheduledJobs:BackupTime</c>,
/// read in the time zone set in <c>ScheduledJobs:TimeZone</c>.
/// </summary>
public class NightlyBackupService : BackgroundService
{
    internal const string BackupTimeKey = "ScheduledJobs:BackupTime";
    internal const string TimeZoneKey = "ScheduledJobs:TimeZone";
    private const string DefaultBackupTime = "05:00";
    private const string DefaultTimeZone = "UTC";

    private readonly IBackupService _backupService;
    private readonly IEnumerable<BotIdentity> _identities;
    private readonly INotificationService _notificationService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NightlyBackupService> _logger;
    private readonly TimeOnly _backupTime;
    private readonly TimeZoneInfo _timeZone;

    /// <summary>
    /// Creates the backup service and reads the backup time and time zone from configuration.
    /// </summary>
    /// <exception cref="InvalidOperationException">The backup time or time zone setting is not valid.</exception>
    public NightlyBackupService(
        IBackupService backupService,
        IEnumerable<BotIdentity> identities,
        INotificationService notificationService,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<NightlyBackupService> logger)
    {
        _backupService = backupService;
        _identities = identities;
        _notificationService = notificationService;
        _timeProvider = timeProvider;
        _logger = logger;

        var backupTime = configuration[BackupTimeKey] ?? DefaultBackupTime;
        if (!TimeOnly.TryParseExact(backupTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _backupTime))
        {
            throw new InvalidOperationException(
                $"Setting '{BackupTimeKey}' has invalid value '{backupTime}'. Use 24-hour HH:mm format, for example 05:00.");
        }

        var timeZoneId = configuration[TimeZoneKey] ?? DefaultTimeZone;
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone))
        {
            throw new InvalidOperationException(
                $"Setting '{TimeZoneKey}' has invalid value '{timeZoneId}'. Use an IANA time zone ID, for example Europe/Berlin or UTC.");
        }

        _timeZone = timeZone;
    }

    /// <summary>
    /// Waits for each scheduled time and backs up all bots, until the application stops.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                var now = _timeProvider.GetUtcNow();
                var nextRun = GetNextRun(now, _backupTime, _timeZone);
                _logger.LogInformation(
                    "Next nightly backup is scheduled for {NextRun:yyyy-MM-dd HH:mm zzz} ({TimeZone}).",
                    TimeZoneInfo.ConvertTime(nextRun, _timeZone),
                    _timeZone.Id);

                await Task.Delay(nextRun - now, _timeProvider, stoppingToken);
                await BackupAllBotsAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// Finds the first moment after <paramref name="utcNow"/> when the clock in <paramref name="timeZone"/>
    /// shows <paramref name="time"/>.
    /// </summary>
    /// <remarks>
    /// On the day clocks move forward, a time that does not exist runs right after the jump, shifted
    /// forward by the length of the jump. On the day clocks move back, a time that occurs twice runs only
    /// at its first occurrence.
    /// </remarks>
    internal static DateTimeOffset GetNextRun(DateTimeOffset utcNow, TimeOnly time, TimeZoneInfo timeZone)
    {
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, timeZone).DateTime);
        while (true)
        {
            var local = date.ToDateTime(time, DateTimeKind.Unspecified);
            TimeSpan offset;
            if (timeZone.IsInvalidTime(local))
            {
                // Read the time with the offset in effect before the clocks jumped.
                offset = timeZone.GetUtcOffset(local.AddDays(-1));
            }
            else if (timeZone.IsAmbiguousTime(local))
            {
                // The larger offset belongs to the earlier of the two moments.
                offset = timeZone.GetAmbiguousTimeOffsets(local).Max();
            }
            else
            {
                offset = timeZone.GetUtcOffset(local);
            }

            var candidate = new DateTimeOffset(local, offset).ToUniversalTime();
            if (candidate > utcNow)
            {
                return candidate;
            }

            date = date.AddDays(1);
        }
    }

    private async Task BackupAllBotsAsync(CancellationToken stoppingToken)
    {
        foreach (var bot in _identities)
        {
            stoppingToken.ThrowIfCancellationRequested();
            _logger.LogInformation("Scheduled database backup for bot {BotName}.", bot.Name);
            try
            {
                var result = await _backupService.BackupDatabase(bot.Name, stoppingToken);
                if (result.IsFailed)
                {
                    // The backup service sends its own alerts for the failures it reports.
                    _logger.LogWarning(
                        "Scheduled database backup for bot {BotName} failed: {Errors}",
                        bot.Name,
                        string.Join("; ", result.Errors.Select(error => error.Message)));
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Scheduled database backup for bot {BotName} threw an exception.", bot.Name);
                await SendAlertAsync(bot.Name, exception);
            }
        }
    }

    private async Task SendAlertAsync(string botName, Exception exception)
    {
        try
        {
            await _notificationService.SendErrorNotification(
                $"Scheduled database backup failed: {exception.Message}",
                botName);
        }
        catch (Exception alertException)
        {
            _logger.LogWarning(alertException, "Could not send the backup failure alert for bot {BotName}.", botName);
        }
    }
}
