using BotFarm.Core.Services.Interfaces;
using FluentResults;
using ICSharpCode.SharpZipLib.Zip;
using LiteDB;
using Microsoft.Extensions.Logging;
using System.Text;

namespace BotFarm.Core.Services
{
    public class LiteDBBackupService : IBackupService
    {
        private readonly IEnumerable<IDatabaseService> _databaseServices;
        private readonly ILogger<LiteDBBackupService> _logger;
        private readonly IEnumerable<IBotService> _botServices;
        private readonly ICloudService _cloudService;
        private readonly INotificationService _notificationService;
        private readonly string tempPath;

        private const string logPrefix = $"[{nameof(LiteDBBackupService)}]";

        public LiteDBBackupService(
            IEnumerable<IBotService> botServices,
            IEnumerable<IDatabaseService> databaseServices,
            ILogger<LiteDBBackupService> logger,
            INotificationService notificationService,
            ICloudService cloudService)
        {
            _databaseServices = databaseServices;
            _logger = logger;
            _botServices = botServices;
            _notificationService = notificationService;
            _cloudService = cloudService;
            tempPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp");
        }

        public async Task<Result> BackupDatabase(string handle)
        {
            _logger.LogInformation($"{logPrefix} Starting database backup.");
            var archivePath = await CreateBackupArchive(handle);
            if (!string.IsNullOrEmpty(archivePath))
            {
                if (await _cloudService.Upload(archivePath, handle))
                {
                    if (await _cloudService.CleanupRemote(handle))
                    {
                        if (await RemoveLocalArchive(archivePath, handle))
                        {
                            var successMessage = $"{logPrefix} Database backup finished successfully.";
                            _logger.LogInformation(successMessage);
                            return Result.Ok()
                                .WithSuccess(new Success(successMessage)
                                                .WithMetadata("fileName", archivePath.Split('\\').Last()));
                        }
                    }
                }
            }
            var failMessage = $"{logPrefix} Database backup finished with errors.";
            _logger.LogError(failMessage);
            return Result.Fail(failMessage);
        }

        private async Task<string> CreateBackupArchive(string handle)
        {
            var archivePath = Path.Combine(tempPath, $"{DateTime.Now:yyyyMMddHHmmss}.zip");

            try
            {
                Directory.CreateDirectory(tempPath);
                using (FileStream fs = File.Create(archivePath)) //create empty archive
                using (ZipOutputStream zipStream = new(fs))
                {
                    zipStream.IsStreamOwner = true;
                    zipStream.Close();
                }
                _logger.LogInformation($"{logPrefix} Writing backup data to '{archivePath}'.");
                using (ZipFile zipFile = new(archivePath))
                {
                    var dbService = _databaseServices.First(s => s.Handle.Equals(handle, StringComparison.InvariantCultureIgnoreCase));
                    foreach (var name in dbService.GetCollectionNames())
                    {
                        _logger.LogInformation($"{logPrefix} Backing up collection '{name}'.");
                        var json = JsonSerializer.Serialize(new BsonArray(dbService.GetCollectionData(name)));
                        var filePath = Path.Combine(tempPath, $"{name}.json");
                        File.WriteAllText(filePath, json, Encoding.UTF8);
                        zipFile.BeginUpdate();
                        zipFile.Add(filePath, $"{name}.json");
                        zipFile.CommitUpdate();
                        File.Delete(filePath);
                    }

                    zipFile.Close();
                    _logger.LogInformation($"{logPrefix} Ended writing backup data to '{archivePath}'.");

                    return archivePath;
                }
            }
            catch (Exception ex)
            {
                var message = $"{logPrefix} Failed to create backup archive. Error: '{ex.Message}'";
                _logger.LogError(message);
                await _notificationService.SendErrorNotification(message, handle);
                return string.Empty;
            }
        }

        private async Task<bool> RemoveLocalArchive(string archivePath, string handle)
        {
            try
            {
                File.Delete(archivePath);
                _logger.LogInformation($"{logPrefix} Local backup '{archivePath}' removed.");
                return true;
            }
            catch (Exception ex)
            {
                var message = $"{logPrefix} Failed to remove local backup '{archivePath}'. Error: '{ex.Message}'";
                _logger.LogError(message);
                await _notificationService.SendErrorNotification(message, handle);
                return false;
            }
        }

        public async Task<Result> RestoreBackup(string name, string handle)
        {
            _logger.LogInformation($"{logPrefix} Getting backup {name}.");
            var backups = await _cloudService.GetBackupsList(handle);
            var backupUri = backups.ValueOrDefault.FirstOrDefault(b => b.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase))?.Uri;
            if (!string.IsNullOrWhiteSpace(backupUri))
            {
                var downloadedBackupPath = await _cloudService.DownloadBackup(backupUri, handle);
                if (!string.IsNullOrWhiteSpace(downloadedBackupPath))
                {
                    _logger.LogInformation($"{logPrefix} Started restoring backup {name}.");
                    if (await RestoreDownloadedBackup(downloadedBackupPath, handle))
                    {
                        if (await RemoveLocalArchive(downloadedBackupPath, handle))
                        {
                            var successMessage = $"{logPrefix} Database restore finished successfully.";
                            _logger.LogInformation(successMessage);
                            return Result.Ok().WithSuccess(successMessage);
                        }
                    }
                }
            }
            var failMessage = $"{logPrefix} Database restore finished with errors.";
            _logger.LogError(failMessage);
            return Result.Fail(failMessage);
        }

        private async Task<bool> RestoreDownloadedBackup(string downloadedBackupPath, string handle)
        {
            var botService = _botServices.First(s => s.Handle.Equals(handle, StringComparison.InvariantCultureIgnoreCase));
            var dbService = _databaseServices.First(s => s.Handle.Equals(handle, StringComparison.InvariantCultureIgnoreCase));
            if (await botService.Pause())
            {
                if (await dbService.Release())
                {
                    using (ZipFile zipFile = new(downloadedBackupPath))
                    using (LiteEngine tempDb = new(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Temp.db")))
                    {
                        foreach (ZipEntry entry in zipFile)
                        {
                            using (StreamReader reader = new(zipFile.GetInputStream(entry)))
                            {
                                var collectionName = Path.GetFileNameWithoutExtension(entry.Name);
                                _logger.LogInformation($"{logPrefix} Restoring collection '{collectionName}' to temporary database.");
                                try
                                {
                                    var json = reader.ReadToEnd();
                                    var bson = JsonSerializer.Deserialize(json).AsArray.Select(d => d.AsDocument);
                                    tempDb.Insert(collectionName, bson);
                                }
                                catch (Exception ex)
                                {
                                    var message = $"{logPrefix} Could not restore collection '{collectionName}'. Error: '{ex.Message}'";
                                    _logger.LogError(message);
                                    await _notificationService.SendErrorNotification(message, handle);
                                    _ = await dbService.Reconnect();
                                    _ = await botService.Resume();
                                    return false;
                                }
                            }
                        }
                    }

                    try
                    {
                        _logger.LogInformation($"{logPrefix} Replacing current database with restored one.");
                        File.Delete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Database.db"));
                        File.Move(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Temp.db"),
                            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Database.db"));
                        return true;
                    }
                    catch (Exception ex)
                    {
                        var message = $"{logPrefix} Could not replace current database. Error: '{ex.Message}'";
                        _logger.LogError(message);
                        await _notificationService.SendErrorNotification(message, handle);
                        return false;
                    }
                    finally
                    {
                        _ = await dbService.Reconnect();
                        _ = await botService.Resume();
                    }
                }
                else
                {
                    var message = $"{logPrefix} Could not release database.";
                    _logger.LogError(message);
                    await _notificationService.SendErrorNotification(message, handle);
                    _ = await dbService.Reconnect();
                }
            }
            else
            {
                var message = $"{logPrefix} Could not pause bot updates.";
                _logger.LogError(message);
                await _notificationService.SendErrorNotification(message, handle);
                _ = await dbService.Reconnect();
            }

            _logger.LogError($"{logPrefix} Could not restore downloaded backup.");
            return false;
        }
    }
}
