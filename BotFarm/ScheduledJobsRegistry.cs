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
        ILogger<ScheduledJobsRegistry> logger)
    {
        // back up database every night
        foreach (var bot in botConfigs)
        {
            Schedule(async () =>
            {
                _ = await backupService.BackupDatabase(bot.Value.Name);
            }).ToRunEvery(1).Days().At(05, 00);
        }

        // shutdown every 2 hours to clear memory
        Schedule(() =>
        {
            logger.LogWarning("Scheduled shutdown.");
            appLifetime.StopApplication();
        }).ToRunEvery(2).Hours();
        
    }
}
