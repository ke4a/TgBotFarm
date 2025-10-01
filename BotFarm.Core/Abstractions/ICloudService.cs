using BotFarm.Core.Models;
using FluentResults;

namespace BotFarm.Core.Abstractions;

public interface ICloudService
{
    Task<bool> Upload(string path, string botName);

    Task<bool> CleanupRemote(string botName);

    Task<Result<IEnumerable<BackupInfo>>> GetBackupsList(string botName);

    Task<Result> RemoveBackup(string name, string botName);

    Task<string> DownloadBackup(string uri, string botName);
}
