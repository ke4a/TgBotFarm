using BotFarm.Core.Models;
using FluentResults;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Manages backup archives on local disk.
/// </summary>
public interface ILocalBackupHelperService
{
    /// <summary>
    /// Removes old backup archives beyond the retention limit for a bot.
    /// </summary>
    Task CleanupBackups(string botName, int maxBackupsToKeep = 7);

    /// <summary>
    /// Creates an empty archive file ready to be populated with backup contents.
    /// </summary>
    Task<string> CreateArchive(string botName);

    /// <summary>
    /// Resolves the full path to a stored backup archive.
    /// </summary>
    Task<string> GetBackupPath(string fileName, string botName);

    /// <summary>
    /// Lists stored backup archives for the named bot.
    /// </summary>
    Task<Result<IEnumerable<BackupInfo>>> GetBackupsList(string botName);

    /// <summary>
    /// Deletes a stored backup archive.
    /// </summary>
    Task<Result> RemoveBackup(string fileName, string botName);
}
