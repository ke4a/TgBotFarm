using BotFarm.Core.Abstractions;
using BotFarm.Core.Exceptions;
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
    private readonly IBotControlService _botControlService;
    private readonly ILogger<MongoDbBackupService> _logger;
    private readonly INotificationService _notificationService;
    private readonly ILocalBackupHelperService _localBackupHelperService;
    private readonly string tempPath;
    private const string logPrefix = $"[{nameof(MongoDbBackupService)}]";

    /// <summary>
    /// Creates the backup coordinator and ensures the shared temporary folder exists.
    /// </summary>
    public MongoDbBackupService(
        IBotRegistry botRegistry,
        IBotControlService botControlService,
        ILogger<MongoDbBackupService> logger,
        INotificationService notificationService,
        ILocalBackupHelperService localBackupHelperService,
        IAppDataPaths dataPaths)
    {
        _botRegistry = botRegistry;
        _botControlService = botControlService;
        _logger = logger;
        _notificationService = notificationService;
        _localBackupHelperService = localBackupHelperService;
        tempPath = dataPaths.Temp;

        Directory.CreateDirectory(tempPath);
    }

    /// <summary>
    /// Pauses incoming webhook deliveries while writing every collection from the bot database into a new zip archive.
    /// </summary>
    /// <remarks>
    /// Cancelling stops the backup, removes the partial archive and resumes the bot's updates.
    /// </remarks>
    public async Task<Result> BackupDatabase(string botName, CancellationToken cancellationToken = default)
    {
        if (!_botRegistry.HasService<IBotService>(botName))
        {
            var failMessage = $"{logPrefix} No databases found for bot {botName}.";
            _logger.LogWarning("{LogPrefix} No databases found for bot {BotName}.", logPrefix, botName);

            return Result.Fail(failMessage);
        }

        string archivePath;
        try
        {
            archivePath = await _botControlService.RunWithUpdatesPausedAsync(botName, token =>
            {
                _logger.LogInformation("{LogPrefix} Starting database backup.", logPrefix);
                return CreateBackupArchive(botName, token);
            }, cancellationToken);
        }
        catch (BotUpdatesPauseException)
        {
            var failMessage = $"{logPrefix} Database backup could not start because bot updates could not be paused.";
            _logger.LogError(
                "{LogPrefix} Database backup could not start because bot updates could not be paused.",
                logPrefix);
            await _notificationService.SendErrorNotification(failMessage, botName);

            return Result.Fail(failMessage);
        }

        if (string.IsNullOrEmpty(archivePath))
        {
            var failMessage = $"{logPrefix} Database backup finished with errors.";
            _logger.LogError("{LogPrefix} Database backup finished with errors.", logPrefix);
            await _notificationService.SendErrorNotification(failMessage, botName);

            return Result.Fail(failMessage);
        }

        await _localBackupHelperService.CleanupBackups(botName);

        var successMessage = $"{logPrefix} Database backup finished successfully.";
        _logger.LogInformation("{LogPrefix} Database backup finished successfully.", logPrefix);
        var filename = archivePath.Contains('\\') ? archivePath.Split('\\').Last() : archivePath.Split('/').Last();
        return Result.Ok()
                     .WithSuccess(new Success(successMessage)
                     .WithMetadata("fileName", filename));
    }

    private async Task<string> CreateBackupArchive(string botName, CancellationToken cancellationToken)
    {
        var archivePath = await _localBackupHelperService.CreateArchive(botName);
        try
        {
            using (var fileStream = File.Create(archivePath))
            using (var zipStream = new ZipOutputStream(fileStream))
            {
                zipStream.IsStreamOwner = true;
                var dbService = _botRegistry.GetService<IMongoDbDatabaseService>(botName);
                _logger.LogInformation("{LogPrefix} Writing backup data to '{ArchivePath}'.", logPrefix, archivePath);

                foreach (var name in await dbService!.GetCollectionNames())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    _logger.LogInformation("{LogPrefix} Backing up collection '{CollectionName}'.", logPrefix, name);
                    var collectionData = dbService.GetCollectionData(name, cancellationToken);

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

                _logger.LogInformation("{LogPrefix} Ended writing backup data to '{ArchivePath}'.", logPrefix, archivePath);
            }

            return archivePath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("{LogPrefix} Database backup for bot {BotName} was cancelled.", logPrefix, botName);
            DeletePartialArchive(archivePath);

            throw;
        }
        catch (Exception ex)
        {
            var message = $"{logPrefix} Failed to create backup. Error: '{ex.Message}'";

            _logger.LogError(ex, "{LogPrefix} Failed to create backup. Error: '{ErrorMessage}'", logPrefix, ex.Message);
            await _notificationService.SendErrorNotification(message, botName);

            DeletePartialArchive(archivePath);

            return string.Empty;
        }
    }

    private static void DeletePartialArchive(string archivePath)
    {
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }
    }

    /// <summary>
    /// Pauses the bot, restores each collection from the selected archive, and re-applies the bot's desired state.
    /// </summary>
    public async Task<Result> RestoreBackup(string backupName, string botName)
    {
        var dbService = _botRegistry.GetService<IMongoDbDatabaseService>(botName);
        var backupPath = await _localBackupHelperService.GetBackupPath(backupName, botName);

        try
        {
            var restored = await _botControlService.RunWithUpdatesPausedAsync(botName, async _ =>
            {
                try
                {
                    using (var zipFile = new ZipFile(backupPath))
                    {
                        foreach (ZipEntry entry in zipFile)
                        {
                            var collectionName = Path.GetFileNameWithoutExtension(entry.Name);
                            _logger.LogInformation("{LogPrefix} Restoring collection '{CollectionName}'.", logPrefix, collectionName);

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
                                        "{LogPrefix} Failed to remove temporary restore collection '{CollectionName}'.",
                                        logPrefix,
                                        stagingCollectionName);
                                }

                                if (File.Exists(tempFilePath))
                                {
                                    File.Delete(tempFilePath);
                                }
                            }
                        }
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    var message = $"{logPrefix} Could not restore backup. Error: '{ex.Message}'";
                    _logger.LogError(
                        ex,
                        "{LogPrefix} Could not restore backup. Error: '{ErrorMessage}'",
                        logPrefix,
                        ex.Message);
                    await _notificationService.SendErrorNotification(message, botName);

                    return false;
                }
            });

            if (restored)
            {
                var successMessage = $"{logPrefix} Database restore finished successfully.";
                _logger.LogInformation("{LogPrefix} Database restore finished successfully.", logPrefix);

                return Result.Ok().WithSuccess(successMessage);
            }
        }
        catch (BotUpdatesPauseException)
        {
            _logger.LogError(
                "{LogPrefix} Database restore could not start because bot updates could not be paused.",
                logPrefix);
        }

        var failMessage = $"{logPrefix} Database restore finished with errors.";
        _logger.LogError("{LogPrefix} Database restore finished with errors.", logPrefix);

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