using FluentResults;

namespace BotFarm.Core.Abstractions;

public interface IBackupService
{
    Task<Result> BackupDatabase(string botName);

    Task<Result> RestoreBackup(string backupName, string botName);
}
