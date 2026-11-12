using BotFarm.Core.Abstractions;
using BotFarm.Core.Exceptions;
using FluentResults;
using Microsoft.Extensions.Caching.Hybrid;
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

    /// <summary>
    /// The archive folder that holds each collection's index definitions, next to the collection data files.
    /// </summary>
    internal const string IndexesFolder = "indexes/";

    private readonly IBotRegistry _botRegistry;
    private readonly IBotControlService _botControlService;
    private readonly ILogger<MongoDbBackupService> _logger;
    private readonly INotificationService _notificationService;
    private readonly ILocalBackupHelperService _localBackupHelperService;
    private readonly HybridCache _cache;
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
        HybridCache cache,
        IAppDataPaths dataPaths)
    {
        _botRegistry = botRegistry;
        _botControlService = botControlService;
        _logger = logger;
        _notificationService = notificationService;
        _localBackupHelperService = localBackupHelperService;
        _cache = cache;
        tempPath = dataPaths.Temp;

        Directory.CreateDirectory(tempPath);
    }

    /// <summary>
    /// Pauses incoming webhook deliveries while writing every collection and its index definitions from the bot
    /// database into a new zip archive.
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
                        WriteDocument(zipStream, document);
                    }

                    zipStream.CloseEntry();

                    // Written even when the collection has no indexes, so a restore knows the list is complete.
                    zipStream.PutNextEntry(new ZipEntry(IndexEntryName(name)));
                    foreach (var index in await dbService.GetIndexDefinitions(name))
                    {
                        WriteDocument(zipStream, index);
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

    /// <summary>
    /// The archive entry that holds the index definitions of <paramref name="collectionName"/>.
    /// </summary>
    private static string IndexEntryName(string collectionName) => $"{IndexesFolder}{collectionName}.bson";

    private static void WriteDocument(Stream stream, BsonDocument document)
    {
        // BSON serialization backpatches document length, so each document needs a seekable buffer.
        using var documentStream = new MemoryStream();
        using (var bsonWriter = new BsonBinaryWriter(documentStream))
        {
            BsonSerializer.Serialize(bsonWriter, document);
        }

        documentStream.WriteTo(stream);
    }

    private static void DeletePartialArchive(string archivePath)
    {
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }
    }

    /// <summary>
    /// Pauses the bot, restores each collection and its indexes from the selected archive, clears the bot's cached
    /// chat settings, and re-applies the bot's desired state.
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
                            // Index definitions are read together with their collection's data.
                            if (entry.Name.StartsWith(IndexesFolder, StringComparison.Ordinal))
                            {
                                continue;
                            }

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
                                    // Archives made before index definitions were saved have none, so the
                                    // collection keeps the indexes it has now.
                                    var indexes = await ReadIndexDefinitions(zipFile, collectionName)
                                                  ?? await dbService.GetIndexDefinitions(collectionName);
                                    if (indexes.Count > 0
                                        && !await dbService.CreateIndexes(stagingCollectionName, indexes))
                                    {
                                        throw new InvalidOperationException(
                                            $"Could not create the indexes of collection '{collectionName}'.");
                                    }

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
                finally
                {
                    // Cached chat settings may no longer match the restored data, even after a partial restore.
                    await _cache.RemoveByTagAsync(botName);
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

    /// <summary>
    /// Reads the index definitions the archive holds for <paramref name="collectionName"/>, or returns
    /// <see langword="null"/> when the archive has none for it.
    /// </summary>
    private static async Task<IReadOnlyList<BsonDocument>?> ReadIndexDefinitions(ZipFile zipFile, string collectionName)
    {
        var entry = zipFile.GetEntry(IndexEntryName(collectionName));
        if (entry == null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        using (var entryStream = zipFile.GetInputStream(entry))
        {
            await entryStream.CopyToAsync(buffer);
        }

        buffer.Position = 0;
        using var bsonReader = new BsonBinaryReader(buffer);
        var indexes = new List<BsonDocument>();
        while (buffer.Position < buffer.Length)
        {
            indexes.Add(BsonSerializer.Deserialize<BsonDocument>(bsonReader));
        }

        return indexes;
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