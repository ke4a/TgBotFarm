using BotFarm.Core.Abstractions;
using FluentResults;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;

namespace BotFarm.Core.Services;

/// <summary>
/// Creates and restores MongoDB backup archives for bot databases.
/// </summary>
public sealed class MongoDbBackupService : IBackupService
{
    internal const int RestoreBatchSize = 500;

    private readonly IBotRegistry _botRegistry;
    private readonly ILogger<MongoDbBackupService> _logger;
    private readonly INotificationService _notificationService;
    private readonly ILocalBackupHelperService _localBackupHelperService;
    private readonly string tempPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp");
    private const string logPrefix = $"[{nameof(MongoDbBackupService)}]";

    /// <summary>
    /// Creates the backup coordinator and ensures the shared temporary folder exists.
    /// </summary>
    public MongoDbBackupService(
        IBotRegistry botRegistry,
        ILogger<MongoDbBackupService> logger,
        INotificationService notificationService,
        ILocalBackupHelperService localBackupHelperService)
    {
        _botRegistry = botRegistry;
        _logger = logger;
        _notificationService = notificationService;
        _localBackupHelperService = localBackupHelperService;

        Directory.CreateDirectory(tempPath);
    }

    /// <summary>
    /// Writes every collection from the named bot database into a new zip archive.
    /// </summary>
    public async Task<Result> BackupDatabase(string botName)
    {
        if (!_botRegistry.HasService<IBotService>(botName))
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
            var filename = archivePath.Contains('\\') ? archivePath.Split('\\').Last() : archivePath.Split('/').Last();
            return Result.Ok()
                         .WithSuccess(new Success(successMessage)
                         .WithMetadata("fileName", filename));
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
            using (var fileStream = File.Create(archivePath))
            using (var zipStream = new ZipOutputStream(fileStream))
            {
                zipStream.IsStreamOwner = true;
                var dbService = _botRegistry.GetService<IMongoDbDatabaseService>(botName);
                _logger.LogInformation($"{logPrefix} Writing backup data to '{archivePath}'.");

                foreach (var name in await dbService!.GetCollectionNames())
                {
                    _logger.LogInformation($"{logPrefix} Backing up collection '{name}'.");
                    var collectionData = dbService.GetCollectionData(name);

                    zipStream.PutNextEntry(new ZipEntry($"{name}.bson"));
                    await foreach (var document in collectionData)
                    {
                        // BSON serialization backpatches document length, so each document needs a seekable buffer.
                        using var documentStream = new MemoryStream();
                        using (var bsonWriter = new BsonBinaryWriter(documentStream))
                        {
                            BsonSerializer.Serialize(bsonWriter, document);
                        }

                        documentStream.WriteTo(zipStream);
                    }

                    zipStream.CloseEntry();
                }

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

    /// <summary>
    /// Pauses the bot, restores each collection from the selected archive, and resumes update delivery.
    /// </summary>
    public async Task<Result> RestoreBackup(string backupName, string botName)
    {
        var botService = _botRegistry.GetService<IBotService>(botName);
        var dbService = _botRegistry.GetService<IMongoDbDatabaseService>(botName);
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
                        var stagingCollectionName = $"_restore_{Guid.NewGuid():N}";
                        var stagingCollectionCreated = false;
                        
                        try
                        {
                            // Extract BSON file to temp location
                            using (var zipStream = zipFile.GetInputStream(entry))
                            using (var fileStream = File.Create(tempFilePath))
                            {
                                await zipStream.CopyToAsync(fileStream);
                            }

                            var hasDocuments = false;
                            foreach (var documents in ReadBsonDocumentBatches(tempFilePath))
                            {
                                stagingCollectionCreated = true;
                                if (!await dbService.CreateAndPopulateCollection(stagingCollectionName, documents))
                                {
                                    throw new InvalidOperationException(
                                        $"Could not populate staging collection '{stagingCollectionName}'.");
                                }

                                hasDocuments = true;
                            }

                            if (!hasDocuments)
                            {
                                stagingCollectionCreated = true;
                                if (!await dbService.CreateCollection(stagingCollectionName))
                                {
                                    throw new InvalidOperationException(
                                        $"Could not create empty staging collection '{stagingCollectionName}'.");
                                }
                            }

                            if (stagingCollectionCreated)
                            {
                                if (!await dbService.RenameCollection(stagingCollectionName, collectionName, dropTarget: true))
                                {
                                    throw new InvalidOperationException(
                                        $"Could not replace collection '{collectionName}' with the staged restore.");
                                }

                                stagingCollectionCreated = false;
                            }
                        }
                        finally
                        {
                            if (stagingCollectionCreated
                                && !await dbService.DropCollection(stagingCollectionName))
                            {
                                _logger.LogError(
                                    $"{logPrefix} Failed to remove temporary restore collection '{stagingCollectionName}'.");
                            }

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

    private static IEnumerable<List<BsonDocument>> ReadBsonDocumentBatches(string filePath)
    {
        using var fileStream = File.OpenRead(filePath);
        using var bsonReader = new BsonBinaryReader(fileStream);
        var batch = new List<BsonDocument>(RestoreBatchSize);
        while (fileStream.Position < fileStream.Length)
        {
            batch.Add(BsonSerializer.Deserialize<BsonDocument>(bsonReader));
            if (batch.Count == RestoreBatchSize)
            {
                yield return batch;
                batch = new List<BsonDocument>(RestoreBatchSize);
            }
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }
}
