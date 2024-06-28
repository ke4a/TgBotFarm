using BotFarm.Core.Models;
using BotFarm.Core.Services.Interfaces;
using FluentScheduler;
using Microsoft.Extensions.Options;

namespace BotFarm
{
    public class ScheduledJobsRegistry : Registry
    {
        public ScheduledJobsRegistry(
            IBackupService backupService,
            IEnumerable<IOptions<BotConfig>> botConfigs)
        {
            // back up database every night
            foreach (var bot in botConfigs)
            {
                Schedule(async () =>
                    {
                        _ = await backupService.BackupDatabase(bot.Value.Name);
                    }).ToRunEvery(1).Days().At(05, 00);
            }
        }
    }
}
