using FluentResults;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Coordinates backup and restore operations for bot databases.
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Creates a new backup archive for the bot named <paramref name="botName"/>.
    /// </summary>
    Task<Result> BackupDatabase(string botName);

    /// <summary>
    /// Restores the named backup archive into the bot database.
    /// </summary>
    Task<Result> RestoreBackup(string backupName, string botName);
}
