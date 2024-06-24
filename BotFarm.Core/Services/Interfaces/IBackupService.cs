using FluentResults;

namespace BotFarm.Core.Services.Interfaces
{
    public interface IBackupService
    {
        Task<Result> BackupDatabase(string handle);

        Task<Result> RestoreBackup(string name, string handle);
    }
}