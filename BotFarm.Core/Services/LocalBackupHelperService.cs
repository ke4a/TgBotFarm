using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using FluentResults;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Extensions.Logging;

namespace BotFarm.Core.Services;

public class LocalBackupHelperService : ILocalBackupHelperService
{
    private readonly ILogger<LocalBackupHelperService> _logger;
    private readonly INotificationService _notificationService;
    private readonly string backupsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
    private const string logPrefix = $"[{nameof(LocalBackupHelperService)}]";

    public LocalBackupHelperService(
        ILogger<LocalBackupHelperService> logger,
        INotificationService notificationService)
    {
        _logger = logger;
        _notificationService = notificationService;
    }

    public async Task CleanupBackups(string botName, int maxBackupsToKeep = 7)
    {
        try
        {
            var botBackupPath = Path.Combine(backupsPath, botName);
            
            if (!Directory.Exists(botBackupPath))
            {
                _logger.LogWarning($"{logPrefix} Backup folder not found for bot '{botName}'");
                return;
            }

            var backupFiles = Directory.GetFiles(botBackupPath, "*.zip")
                                       .Select(f => new FileInfo(f))
                                       .OrderByDescending(f => f.CreationTime)
                                       .ToList();

            if (backupFiles.Count > maxBackupsToKeep)
            {
                var filesToDelete = backupFiles.Skip(maxBackupsToKeep);
                foreach (var file in filesToDelete)
                {
                    _logger.LogInformation($"{logPrefix} Deleting old backup file: '{file.Name}'");
                    file.Delete();
                }
            }
        }
        catch (Exception ex)
        {
            var message = $"{logPrefix} Failed to clean up backups. Error: '{ex.Message}'";
            _logger.LogError(message);
            await _notificationService.SendErrorNotification(message, botName);
        }
    }

    public async Task<string> CreateArchive(string botName)
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(backupsPath, botName));
            var archivePath = Path.Combine(backupsPath, botName, $"{DateTime.Now:yyyyMMddHHmmss}.zip");

            using (FileStream fs = File.Create(archivePath)) //create empty archive
            using (ZipOutputStream zipStream = new(fs))
            {
                zipStream.IsStreamOwner = true;
                zipStream.Close();
            }

            return archivePath;
        }
        catch (Exception ex)
        {
            var message = $"{logPrefix} Failed to create backup archive. Error: '{ex.Message}'";
            _logger.LogError(message);
            await _notificationService.SendErrorNotification(message, botName);

            return string.Empty;
        }
    }

    public async Task<Result<IEnumerable<BackupInfo>>> GetBackupsList(string botName)
    {
        try
        {
            var botBackupPath = Path.Combine(backupsPath, botName);
            
            if (!Directory.Exists(botBackupPath))
            {
                _logger.LogWarning($"{logPrefix} Backup folder not found for bot '{botName}'");
                return Result.Ok(Enumerable.Empty<BackupInfo>());
            }

            var backupFiles = Directory.GetFiles(botBackupPath, "*.zip")
                .Select(filePath =>
                {
                    var fileInfo = new FileInfo(filePath);
                    return new BackupInfo
                    {
                        Name = fileInfo.Name,
                        Size = fileInfo.Length,
                        Date = fileInfo.CreationTime
                    };
                })
                .OrderByDescending(b => b.Date)
                .ToList();

            return Result.Ok<IEnumerable<BackupInfo>>(backupFiles);
        }
        catch (Exception ex)
        {
            var message = $"{logPrefix} Failed to get backups list. Error: '{ex.Message}'";
            _logger.LogError(message);
            await _notificationService.SendErrorNotification(message, botName);
            
            return Result.Fail(message);
        }
    }

    public async Task<Result> RemoveBackup(string fileName, string botName)
    {
        try
        {
            var botBackupPath = Path.Combine(backupsPath, botName);
            var filePath = Path.Combine(botBackupPath, fileName);

            if (!File.Exists(filePath))
            {
                var notFoundMessage = $"{logPrefix} Backup file '{fileName}' not found for bot '{botName}'.";
                _logger.LogWarning(notFoundMessage);
                return Result.Fail(notFoundMessage);
            }

            File.Delete(filePath);

            var successMessage = $"{logPrefix} Deleted backup file: '{fileName}' for bot '{botName}'";
            _logger.LogInformation(successMessage);
            
            return Result.Ok().WithSuccess(successMessage);
        }
        catch (Exception ex)
        {
            var message = $"{logPrefix} Failed to remove backup '{fileName}'. Error: '{ex.Message}'";
            _logger.LogError(message);
            await _notificationService.SendErrorNotification(message, botName);
            
            return Result.Fail(message);
        }
    }

    public async Task<string> GetBackupPath(string fileName, string botName)
    {
        var filePath = Path.Combine(backupsPath, botName, fileName);

        if (!File.Exists(filePath))
        {
            var notFoundMessage = $"{logPrefix} Backup file '{fileName}' not found for bot '{botName}'.";
            _logger.LogWarning(notFoundMessage);
            await _notificationService.SendErrorNotification(notFoundMessage, botName);

            return string.Empty;
        }

        return filePath;
    }
}
