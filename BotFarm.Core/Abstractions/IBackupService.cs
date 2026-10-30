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
    /// <param name="botName">The bot whose database is backed up.</param>
    /// <param name="cancellationToken">Stops the backup; a partially written archive is removed.</param>
    /// <exception cref="OperationCanceledException">The backup was cancelled through <paramref name="cancellationToken"/>.</exception>
    Task<Result> BackupDatabase(string botName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores the named backup archive into the bot database.
    /// </summary>
    Task<Result> RestoreBackup(string backupName, string botName);
}
