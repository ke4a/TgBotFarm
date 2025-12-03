using BotFarm.Core.Models;
using FluentResults;

namespace BotFarm.Core.Abstractions;

public interface ILocalBackupHelperService
{
    Task CleanupBackups(string botName, int maxBackupsToKeep = 7);

    Task<string> CreateArchive(string botName);

    Task<string> GetBackupPath(string fileName, string botName);

    Task<Result<IEnumerable<BackupInfo>>> GetBackupsList(string botName);

    Task<Result> RemoveBackup(string fileName, string botName);
}
