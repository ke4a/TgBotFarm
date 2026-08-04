using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using FluentScheduler;

namespace BotFarm;

/// <summary>
/// Defines the recurring background jobs run by the BotFarm host.
/// </summary>
public class ScheduledJobsRegistry
{
    private readonly IBackupService _backupService;
    private readonly IEnumerable<BotRegistration> _registrations;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ScheduledJobsRegistry> _logger;

    /// <summary>
    /// Creates the registry that can build schedules.
    /// </summary>
    public ScheduledJobsRegistry(
        IBackupService backupService,
        IEnumerable<BotRegistration> registrations,
        IHostApplicationLifetime appLifetime,
        IConfiguration configuration,
        ILogger<ScheduledJobsRegistry> logger)
    {
        _backupService = backupService;
        _registrations = registrations;
        _appLifetime = appLifetime;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Builds the schedules for nightly backups and optional periodic shutdowns.
    /// </summary>
    public Schedule[] GetJobs()
    {
        var jobs = new List<Schedule>();

        #region Back up database every night
        var backupSchedule = new Schedule(
            async () =>
            {
                foreach (var bot in _registrations)
                {
                    _logger.LogInformation($"Scheduled database backup for bot '{bot.BotName}'.");
                    _ = await _backupService.BackupDatabase(bot.BotName);
                }
                _appLifetime.StopApplication();
            },
            run => run.Everyday().At(05, 00)
        );

        jobs.Add(backupSchedule);
        #endregion

        #region Shutdown application to clear memory
        var shutdownEveryHours = _configuration.GetValue<int>("ScheduledJobs:ShutdownEveryHours");
        if (shutdownEveryHours > 0)
        {
            var shutdownSchedule = new Schedule(
                async () =>
                {
                    _logger.LogWarning("Scheduled shutdown.");
                    _appLifetime.StopApplication();
                },
                run => run.Every(shutdownEveryHours).Hours()
            );

            jobs.Add(shutdownSchedule);
        }
        #endregion

        return jobs.ToArray();
    }
}
