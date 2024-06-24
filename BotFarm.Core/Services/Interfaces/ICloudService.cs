using BotFarm.Core.Models;
using FluentResults;

namespace BotFarm.Core.Services.Interfaces
{
    public interface ICloudService
    {
        Task<bool> Upload(string path, string handle);

        Task<bool> CleanupRemote(string handle);

        Task<Result<IEnumerable<BackupInfo>>> GetBackupsList(string handle);

        Task<Result> RemoveBackup(string name, string handle);

        Task<string> DownloadBackup(string uri, string handle);
    }
}