using BotFarm.Core.Models;
using BotFarm.Core.Services.Interfaces;
using FluentScheduler;

namespace BotFarm
{
    public class ScheduledJobsRegistry : Registry
    {
        public ScheduledJobsRegistry(
            IBackupService backupService,
            IConfiguration config)
        {
            var botConfigs = config.GetSection("Bots")
                                   .GetChildren()
                                   .Select(c => c.GetSection(nameof(BotConfig)).Get<BotConfig>());
            // back up database every night
            foreach (var bot in botConfigs)
            {
                Schedule(async () =>
                    {
                        _ = await backupService.BackupDatabase(bot.Handle);
                    }).ToRunEvery(1).Days().At(05, 00);
            }
        }
    }
}
