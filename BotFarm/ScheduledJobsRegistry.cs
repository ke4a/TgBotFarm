using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using FluentScheduler;
using Microsoft.Extensions.Options;

namespace BotFarm;

public class ScheduledJobsRegistry : Registry
{
    public ScheduledJobsRegistry(
        IBackupService backupService,
        IEnumerable<IOptions<BotConfig>> botConfigs,
        IHostApplicationLifetime appLifetime,
        IConfiguration configuration,
        ILogger<ScheduledJobsRegistry> logger)
    {
        #region Back up database every night
        Schedule(async () =>
        {
            foreach (var bot in botConfigs)
            {
                logger.LogInformation($"Scheduled database backup for bot '{bot.Value.Name}'.");
                _ = await backupService.BackupDatabase(bot.Value.Name);
            }
            appLifetime.StopApplication();
        }).ToRunEvery(1).Days().At(05, 00);
        #endregion

        #region Shutdown application to clear memory
        var shutdownEveryHours = configuration.GetValue<int>("ScheduledJobs:ShutdownEveryHours");
        if (shutdownEveryHours > 0)
        {
            Schedule(() =>
            {
                logger.LogWarning("Scheduled shutdown.");
                appLifetime.StopApplication();
            }).ToRunEvery(shutdownEveryHours).Hours();
        } 
        #endregion
    }
}
