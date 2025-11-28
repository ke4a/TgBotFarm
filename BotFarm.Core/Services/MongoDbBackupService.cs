using BotFarm.Core.Abstractions;
using FluentResults;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;

namespace BotFarm.Core.Services;

public class MongoDbBackupService : IBackupService
{
    private readonly IEnumerable<IMongoDbDatabaseService> _databaseServices;
    private readonly ILogger<MongoDbBackupService> _logger;
    private readonly IEnumerable<IBotService> _botServices;
    private readonly INotificationService _notificationService;
    private readonly ILocalBackupHelperService _localBackupHelperService;
    private readonly string tempPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp");
    private const string logPrefix = $"[{nameof(MongoDbBackupService)}]";

    public MongoDbBackupService(
        IEnumerable<IBotService> botServices,
        IEnumerable<IMongoDbDatabaseService> databaseServices,
        ILogger<MongoDbBackupService> logger,
        INotificationService notificationService,
        ILocalBackupHelperService localBackupHelperService)
    {
        _databaseServices = databaseServices;
        _logger = logger;
        _botServices = botServices;
        _notificationService = notificationService;
        _localBackupHelperService = localBackupHelperService;

        Directory.CreateDirectory(tempPath);
    }

    public async Task<Result> BackupDatabase(string botName)
    {
        if (!_botServices.Any(b => b.Name.Equals(botName, StringComparison.OrdinalIgnoreCase)))
        {
            var failMessage = $"{logPrefix} No databases found for bot {botName}.";
            _logger.LogWarning(failMessage);

            return Result.Fail(failMessage);
        }

        _logger.LogInformation($"{logPrefix} Starting database backup.");
        var archivePath = await CreateBackupArchive(botName);

        if (!string.IsNullOrEmpty(archivePath))
        {
            await _localBackupHelperService.CleanupBackups(botName);

            var successMessage = $"{logPrefix} Database backup finished successfully.";
            _logger.LogInformation(successMessage);
            return Result.Ok()
                         .WithSuccess(new Success(successMessage)
                         .WithMetadata("fileName", archivePath.Split('\\').Last()));
        }
        else
        {
            var failMessage = $"{logPrefix} Database backup finished with errors.";
            _logger.LogError(failMessage);

            return Result.Fail(failMessage);
        }
    }

    private async Task<string> CreateBackupArchive(string botName)
    {
        var archivePath = await _localBackupHelperService.CreateArchive(botName);
        
        try
        {
            using (var zipFile = new ZipFile(archivePath))
            {
                var dbService = _databaseServices.First(s => s.Name.Equals(botName, StringComparison.OrdinalIgnoreCase));
                _logger.LogInformation($"{logPrefix} Writing backup data to '{archivePath}'.");

                foreach (var name in dbService!.GetCollectionNames())
                {
                    _logger.LogInformation($"{logPrefix} Backing up collection '{name}'.");
                    var collectionData = dbService.GetCollectionData(name);
                    var filePath = Path.Combine(tempPath, $"{name}.bson");
                    
                    using (var fileStream = File.Create(filePath))
                    using (var bsonWriter = new BsonBinaryWriter(fileStream))
                    {
                        foreach (var document in collectionData)
                        {
                            BsonSerializer.Serialize(bsonWriter, document);
                        }
                    }
                    
                    zipFile.BeginUpdate();
                    zipFile.Add(filePath, $"{name}.bson");
                    zipFile.CommitUpdate();
                    File.Delete(filePath);
                }

                zipFile.Close();
                _logger.LogInformation($"{logPrefix} Ended writing backup data to '{archivePath}'.");
            }

            return archivePath;
        }
        catch (Exception ex)
        {
            var message = $"{logPrefix} Failed to create backup. Error: '{ex.Message}'";

            _logger.LogError(message);
            await _notificationService.SendErrorNotification(message, botName);

            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            return string.Empty;
        }
    }

    public async Task<Result> RestoreBackup(string backupName, string botName)
    {
        var botService = _botServices.First(s => s.Name.Equals(botName, StringComparison.OrdinalIgnoreCase));
        var dbService = _databaseServices.First(s => s.Name.Equals(botName, StringComparison.OrdinalIgnoreCase));
        var backupPath = await _localBackupHelperService.GetBackupPath(backupName, botName);

        if (await botService.Pause())
        {
            try
            {
                using (var zipFile = new ZipFile(backupPath))
                {
                    foreach (ZipEntry entry in zipFile)
                    {
                        var collectionName = Path.GetFileNameWithoutExtension(entry.Name);
                        _logger.LogInformation($"{logPrefix} Restoring collection '{collectionName}'.");

                        var tempFilePath = Path.Combine(tempPath, entry.Name);
                        
                        try
                        {
                            // Extract BSON file to temp location
                            using (var zipStream = zipFile.GetInputStream(entry))
                            using (var fileStream = File.Create(tempFilePath))
                            {
                                await zipStream.CopyToAsync(fileStream);
                            }

                            // Read BSON documents
                            var documents = new List<BsonDocument>();
                            using (var fileStream = File.OpenRead(tempFilePath))
                            using (var bsonReader = new BsonBinaryReader(fileStream))
                            {
                                while (fileStream.Position < fileStream.Length)
                                {
                                    var document = BsonSerializer.Deserialize<BsonDocument>(bsonReader);
                                    documents.Add(document);
                                }
                            }

                            if (documents.Any())
                            {
                                var dropped = await dbService.DropCollection(collectionName);
                                if (dropped)
                                {
                                    await dbService.CreateAndPopulateCollection(collectionName, documents);
                                }
                            }
                        }
                        finally
                        {
                            if (File.Exists(tempFilePath))
                            {
                                File.Delete(tempFilePath);
                            }
                        }
                    }
                }

                var successMessage = $"{logPrefix} Database restore finished successfully.";
                _logger.LogInformation(successMessage);

                return Result.Ok().WithSuccess(successMessage);
            }
            catch (Exception ex)
            {
                var message = $"{logPrefix} Could not restore backup. Error: '{ex.Message}'";
                _logger.LogError(message);
                await _notificationService.SendErrorNotification(message, botName);
            }
            finally
            {
                _ = await botService.Resume();
            }
        }

        var failMessage = $"{logPrefix} Database restore finished with errors.";
        _logger.LogError(failMessage);

        return Result.Fail(failMessage);
    }
}
