using BotFarm.Core.Abstractions;
using BotFarm.Core.Exceptions;
using BotFarm.Core.Services;
using BotFarm.TestKit;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using NSubstitute;
using System.Text;
using System.Text.Json;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class MongoDbBackupServiceTests
{
    private MongoDbBackupService _service;
    private ILogger<MongoDbBackupService> _logger;
    private INotificationService _notificationService;
    private ILocalBackupHelperService _localBackupHelperService;
    private IBotRegistry _botRegistry;
    private IBotControlService _botControlService = null!;
    private IBotService _mockBotService;
    private IMongoDbDatabaseService _mockDatabaseService;
    private string _testTempPath;
    private const string TestBotName = "TestBot";

    [SetUp]
    public void SetUp()
    {
        _logger = Substitute.For<ILogger<MongoDbBackupService>>();
        _notificationService = Substitute.For<INotificationService>();
        _localBackupHelperService = Substitute.For<ILocalBackupHelperService>();
        _mockBotService = Substitute.For<IBotService>();
        _mockBotService.Name.Returns(TestBotName);
        _mockDatabaseService = Substitute.For<IMongoDbDatabaseService>();
        _mockDatabaseService.Name.Returns(TestBotName);

        _botRegistry = Substitute.For<IBotRegistry>();
        _botRegistry.HasService<IBotService>(Arg.Is<string>(n => n.Equals(TestBotName, StringComparison.OrdinalIgnoreCase))).Returns(true);
        _botRegistry.GetService<IBotService>(Arg.Is<string>(n => n.Equals(TestBotName, StringComparison.OrdinalIgnoreCase))).Returns(_mockBotService);
        _botRegistry.GetService<IMongoDbDatabaseService>(Arg.Is<string>(n => n.Equals(TestBotName, StringComparison.OrdinalIgnoreCase))).Returns(_mockDatabaseService);

        _botControlService = Substitute.For<IBotControlService>();
        SetupMaintenance<string>();
        SetupMaintenance<bool>();

        _testTempPath = Path.Combine(Path.GetTempPath(), "BotFarm_Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testTempPath);
        _service = new MongoDbBackupService(
            _botRegistry,
            _botControlService,
            _logger,
            _notificationService,
            _localBackupHelperService);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testTempPath))
        {
            Directory.Delete(_testTempPath, true);
        }
    }

    private void SetupMaintenance<T>()
    {
        _botControlService.RunWithUpdatesPausedAsync(
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task<T>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<T>>>()(CancellationToken.None));
    }

    private void SetupPauseFailure()
    {
        _botControlService.RunWithUpdatesPausedAsync(
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task<string>>>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<string>>(_ => throw new BotUpdatesPauseException(TestBotName));
        _botControlService.RunWithUpdatesPausedAsync(
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task<bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new BotUpdatesPauseException(TestBotName));
    }

    private Task<T> AssertRanWithUpdatesPaused<T>()
    {
        return _botControlService.Received(1).RunWithUpdatesPausedAsync(
            Arg.Is<string>(name => name.Equals(TestBotName, StringComparison.OrdinalIgnoreCase)),
            Arg.Any<Func<CancellationToken, Task<T>>>(),
            Arg.Any<CancellationToken>());
    }

    private void AssertBotWebhookNotTouchedDirectly()
    {
        _ = _mockBotService.DidNotReceiveWithAnyArgs().Pause(default);
        _ = _mockBotService.DidNotReceiveWithAnyArgs().InitializeWebHook(default!, default);
    }

    [Test]
    public async Task BackupDatabase_WithNonExistentBot_ReturnsFailure()
    {
        // Arrange
        var nonExistentBot = "NonExistentBot";

        // Act
        var result = await _service.BackupDatabase(nonExistentBot);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(result.Errors.First().Message, Does.Contain("No databases found"));
        }
    }

    [Test]
    public async Task BackupDatabase_WhenBotPauseFails_ReturnsFailureWithoutCreatingArchive()
    {
        SetupPauseFailure();

        var result = await _service.BackupDatabase(TestBotName);

        Assert.That(result.IsFailed, Is.True);
        Assert.That(result.Errors.First().Message, Does.Contain("could not be paused"));
        await AssertRanWithUpdatesPaused<string>();
        AssertBotWebhookNotTouchedDirectly();
        await _localBackupHelperService.DidNotReceive().CreateArchive(Arg.Any<string>());
        await _mockDatabaseService.DidNotReceive().GetCollectionNames();
        await _notificationService.Received(1).SendErrorNotification(
            Arg.Is<string>(message => message.Contains("could not be paused", StringComparison.Ordinal)),
            TestBotName);
    }

    [Test]
    public async Task BackupDatabase_WithSuccessfulBackup_ReturnsSuccessWithFileName()
    {
        // Arrange
        var fileName = "20240101120000.zip";
        var archivePath = Path.Combine(_testTempPath, fileName);
        _localBackupHelperService.CreateArchive(TestBotName).Returns(archivePath);
        var collections = new List<string> { "collection1", "collection2", "collection3" };
        var tempFilesObservedDuringRead = new List<bool>();
        _mockDatabaseService.GetCollectionNames().Returns(Task.FromResult<IEnumerable<string>>(collections));
        foreach (var collection in collections)
        {
            var tempFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp", $"{collection}.bson");
            _mockDatabaseService.GetCollectionData(collection).Returns(ObserveCollectionRead(collection, () =>
                tempFilesObservedDuringRead.Add(File.Exists(tempFilePath))));
        }

        // Create actual zip file
        using (var fs = File.Create(archivePath))
        using (var zipStream = new ZipOutputStream(fs))
        {
            zipStream.Close();
        }

        // Act
        var result = await _service.BackupDatabase(TestBotName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Successes.First().Metadata["fileName"], Is.EqualTo(fileName));
            Assert.That(tempFilesObservedDuringRead, Has.Count.EqualTo(collections.Count));
            Assert.That(tempFilesObservedDuringRead.All(fileExists => !fileExists), Is.True);
        }

        using (var zipFile = new ZipFile(archivePath))
        {
            Assert.That(zipFile.Count, Is.EqualTo(collections.Count));
            foreach (var collection in collections)
            {
                var entry = zipFile.GetEntry($"{collection}.bson");
                Assert.That(entry, Is.Not.Null);

                using var entryStream = zipFile.GetInputStream(entry!);
                using var documentStream = new MemoryStream();
                entryStream.CopyTo(documentStream);
                documentStream.Position = 0;
                using var bsonReader = new BsonBinaryReader(documentStream);
                var documents = new[]
                {
                    BsonSerializer.Deserialize<BsonDocument>(bsonReader),
                    BsonSerializer.Deserialize<BsonDocument>(bsonReader)
                };
                Assert.That(documents.Select(document => document["_id"].AsInt32), Is.EqualTo(new[] { 1, 2 }));
                Assert.That(documents.All(document => document["name"].AsString == collection), Is.True);
            }
        }

        await _localBackupHelperService.Received(1).CreateArchive(TestBotName);
        await AssertRanWithUpdatesPaused<string>();
        AssertBotWebhookNotTouchedDirectly();
        await _mockDatabaseService.Received(1).GetCollectionNames();
        foreach (var collection in collections)
        {
            _mockDatabaseService.Received(1).GetCollectionData(collection);
        }
        await _localBackupHelperService.Received(1).CleanupBackups(TestBotName, Arg.Any<int>());
    }

    [Test]
    public async Task BackupDatabase_WhenCollectionListingFails_ReturnsFailure()
    {
        var archivePath = Path.Combine(_testTempPath, "failed-backup.zip");
        _localBackupHelperService.CreateArchive(TestBotName).Returns(archivePath);
        _mockDatabaseService.GetCollectionNames()
            .Returns<Task<IEnumerable<string>>>(_ => throw new InvalidOperationException("Collection listing failed."));
        using (var fileStream = File.Create(archivePath))
        using (var zipStream = new ZipOutputStream(fileStream))
        {
            zipStream.Close();
        }

        var result = await _service.BackupDatabase(TestBotName);

        Assert.That(result.IsFailed, Is.True);
        Assert.That(File.Exists(archivePath), Is.False);
        await AssertRanWithUpdatesPaused<string>();
        AssertBotWebhookNotTouchedDirectly();
        await _notificationService.Received(1).SendErrorNotification(
            Arg.Is<string>(message => message.Contains("Collection listing failed", StringComparison.Ordinal)),
            TestBotName);
    }

    [Test]
    public async Task BackupDatabase_WhenCollectionReadFails_ReturnsFailureAndRemovesArchive()
    {
        var archivePath = Path.Combine(_testTempPath, "failed-backup.zip");
        _localBackupHelperService.CreateArchive(TestBotName).Returns(archivePath);
        _mockDatabaseService.GetCollectionNames().Returns(Task.FromResult<IEnumerable<string>>(["collection1"]));
        _mockDatabaseService.GetCollectionData("collection1").Returns(FailAfterDocument());
        using (var fileStream = File.Create(archivePath))
        using (var zipStream = new ZipOutputStream(fileStream))
        {
            zipStream.Close();
        }

        var result = await _service.BackupDatabase(TestBotName);

        Assert.That(result.IsFailed, Is.True);
        Assert.That(File.Exists(archivePath), Is.False);
        await AssertRanWithUpdatesPaused<string>();
        AssertBotWebhookNotTouchedDirectly();
        await _notificationService.Received(1).SendErrorNotification(
            Arg.Is<string>(message => message.Contains("Collection read failed", StringComparison.Ordinal)),
            TestBotName);
    }

    [Test]
    public async Task BackupDatabase_PassesCancellationTokenToPauseAndCollectionReads()
    {
        using var cts = new CancellationTokenSource();
        SetupMaintenanceWithCallerToken();
        var archivePath = Path.Combine(_testTempPath, "token-backup.zip");
        _localBackupHelperService.CreateArchive(TestBotName).Returns(archivePath);
        _mockDatabaseService.GetCollectionNames().Returns(Task.FromResult<IEnumerable<string>>(["collection1"]));
        _mockDatabaseService.GetCollectionData("collection1", cts.Token)
            .Returns(AsyncEnumerableFactory.Create(new[] { new BsonDocument { ["_id"] = 1 } }));

        var result = await _service.BackupDatabase(TestBotName, cts.Token);

        Assert.That(result.IsSuccess, Is.True);
        await _botControlService.Received(1).RunWithUpdatesPausedAsync(
            TestBotName,
            Arg.Any<Func<CancellationToken, Task<string>>>(),
            cts.Token);
        _ = _mockDatabaseService.Received(1).GetCollectionData("collection1", cts.Token);
    }

    [Test]
    public async Task BackupDatabase_WhenCancelledDuringCollectionRead_ThrowsAndRemovesPartialArchiveWithoutAlert()
    {
        using var cts = new CancellationTokenSource();
        SetupMaintenanceWithCallerToken();
        var archivePath = Path.Combine(_testTempPath, "cancelled-backup.zip");
        _localBackupHelperService.CreateArchive(TestBotName).Returns(archivePath);
        _mockDatabaseService.GetCollectionNames().Returns(Task.FromResult<IEnumerable<string>>(["collection1"]));
        _mockDatabaseService.GetCollectionData("collection1", cts.Token).Returns(CancelAfterDocument(cts));

        await Assert.CatchAsync<OperationCanceledException>(() => _service.BackupDatabase(TestBotName, cts.Token));

        Assert.That(File.Exists(archivePath), Is.False);
        await _notificationService.DidNotReceiveWithAnyArgs().SendErrorNotification(default!, default!);
        await _localBackupHelperService.DidNotReceiveWithAnyArgs().CleanupBackups(default!);
    }

    private void SetupMaintenanceWithCallerToken()
    {
        _botControlService.RunWithUpdatesPausedAsync(
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, Task<string>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<string>>>()(call.ArgAt<CancellationToken>(2)));
    }

    private static async IAsyncEnumerable<BsonDocument> CancelAfterDocument(CancellationTokenSource cts)
    {
        yield return new BsonDocument { ["_id"] = 1 };
        await cts.CancelAsync();
        cts.Token.ThrowIfCancellationRequested();
    }

    [Test]
    public async Task BackupDatabase_WithEmptyArchivePath_ReturnsFailure()
    {
        // Arrange
        _localBackupHelperService.CreateArchive(TestBotName).Returns(string.Empty);

        // Act
        var result = await _service.BackupDatabase(TestBotName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(result.Errors.First().Message, Does.Contain("finished with errors"));
        }

        await _notificationService.Received(1).SendErrorNotification(
            Arg.Is<string>(message => message.Contains("Database backup finished with errors", StringComparison.Ordinal)),
            TestBotName);
    }

    [Test]
    public async Task BackupDatabase_CaseInsensitiveBotName_WorksCorrectly()
    {
        // Arrange
        var archivePath = Path.Combine(_testTempPath, "20240101120000.zip");
        _localBackupHelperService.CreateArchive(Arg.Any<string>()).Returns(archivePath);
        _mockDatabaseService.GetCollectionNames().Returns(Task.FromResult<IEnumerable<string>>(["collection1"]));
        _mockDatabaseService.GetCollectionData("collection1").Returns(AsyncEnumerableFactory.Create(new[]
        {
            new BsonDocument { ["_id"] = 1 }
        }));
        using (var fs = File.Create(archivePath))
        using (var zipStream = new ZipOutputStream(fs))
        {
            zipStream.Close();
        }

        // Act
        var result = await _service.BackupDatabase(TestBotName.ToLower()); // lowercase

        // Assert
        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public async Task RestoreBackup_WhenBotPauseFails_ReturnsFailure()
    {
        // Arrange
        var backupName = "backup.zip";
        SetupPauseFailure();
        _localBackupHelperService.GetBackupPath(backupName, TestBotName).Returns("somepath.zip");

        // Act
        var result = await _service.RestoreBackup(backupName, TestBotName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(result.Errors.First().Message, Does.Contain("finished with errors"));
        }
        await AssertRanWithUpdatesPaused<bool>();
        AssertBotWebhookNotTouchedDirectly();
        await _mockDatabaseService.DidNotReceive().DropCollection(Arg.Any<string>());
        await _mockDatabaseService.DidNotReceive().CreateAndPopulateCollection(Arg.Any<string>(), Arg.Any<IEnumerable<BsonDocument>>());
    }

    [Test]
    public async Task RestoreBackup_WithSuccessfulRestore_ReturnsSuccess()
    {
        // Arrange
        var backupName = "backup.zip";
        var backupPath = Path.Combine(_testTempPath, backupName);
        _localBackupHelperService.GetBackupPath(backupName, TestBotName).Returns(backupPath);
        var collections = new Dictionary<string, BsonDocument[]>
        {
            { "collection1", new[] { new BsonDocument { ["_id"] = 1 } } },
            { "collection2", new[] { new BsonDocument { ["_id"] = 2 } } },
            { "collection3", new[] { new BsonDocument { ["_id"] = 3 } } }
        };
        CreateTestBackupFile(backupPath, collections);
        _mockDatabaseService.CreateAndPopulateCollection(Arg.Any<string>(), Arg.Any<IEnumerable<BsonDocument>>()).Returns(true);
        _mockDatabaseService.RenameCollection(Arg.Any<string>(), Arg.Any<string>(), dropTarget: true).Returns(true);

        // Act
        var result = await _service.RestoreBackup(backupName, TestBotName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Successes.First().Message, Does.Contain("finished successfully"));
        }
        await AssertRanWithUpdatesPaused<bool>();
        AssertBotWebhookNotTouchedDirectly();
        await _mockDatabaseService.Received(collections.Count).CreateAndPopulateCollection(
            Arg.Is<string>(name => name.StartsWith("_restore_", StringComparison.Ordinal)),
            Arg.Any<IEnumerable<BsonDocument>>());
        foreach (var collection in collections.Keys)
        {
            await _mockDatabaseService.Received(1).RenameCollection(
                Arg.Is<string>(name => name.StartsWith("_restore_", StringComparison.Ordinal)),
                collection,
                dropTarget: true);
        }
    }

    [Test]
    public async Task RestoreBackup_WithLargeCollection_InsertsDocumentsInBatches()
    {
        // Arrange
        var backupName = "backup.zip";
        var backupPath = Path.Combine(_testTempPath, backupName);
        _localBackupHelperService.GetBackupPath(backupName, TestBotName).Returns(backupPath);
        var documents = Enumerable.Range(1, MongoDbBackupService.RestoreBatchSize + 1)
            .Select(id => new BsonDocument { ["_id"] = id })
            .ToArray();
        CreateTestBackupFile(backupPath, new Dictionary<string, BsonDocument[]>
        {
            { "collection1", documents }
        });
        List<int> batchSizes = [];
        _mockDatabaseService.CreateAndPopulateCollection(
                Arg.Is<string>(name => name.StartsWith("_restore_", StringComparison.Ordinal)),
                Arg.Do<IEnumerable<BsonDocument>>(batch => batchSizes.Add(batch.Count())))
            .Returns(true);
        _mockDatabaseService.RenameCollection(
            Arg.Is<string>(name => name.StartsWith("_restore_", StringComparison.Ordinal)),
            "collection1",
            dropTarget: true).Returns(true);

        // Act
        var result = await _service.RestoreBackup(backupName, TestBotName);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(batchSizes, Is.EqualTo(new[] { MongoDbBackupService.RestoreBatchSize, 1 }));
        await _mockDatabaseService.Received(1).RenameCollection(
            Arg.Is<string>(name => name.StartsWith("_restore_", StringComparison.Ordinal)),
            "collection1",
            dropTarget: true);
    }

    [Test]
    public async Task RestoreBackup_WhenRenameFails_LeavesTargetAndCleansStagingCollection()
    {
        // Arrange
        var backupName = "backup.zip";
        var backupPath = Path.Combine(_testTempPath, backupName);
        _localBackupHelperService.GetBackupPath(backupName, TestBotName).Returns(backupPath);
        CreateTestBackupFile(backupPath, new Dictionary<string, BsonDocument[]>
        {
            { "collection1", new[] { new BsonDocument { ["_id"] = 1 } } }
        });
        _mockDatabaseService.CreateAndPopulateCollection(Arg.Any<string>(), Arg.Any<IEnumerable<BsonDocument>>())
            .Returns(true);
        _mockDatabaseService.RenameCollection(
            Arg.Any<string>(),
            "collection1",
            dropTarget: true).Returns(false);
        _mockDatabaseService.DropCollection(Arg.Any<string>()).Returns(true);

        // Act
        var result = await _service.RestoreBackup(backupName, TestBotName);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        await _mockDatabaseService.Received(1).CreateAndPopulateCollection(
            Arg.Is<string>(name => name.StartsWith("_restore_", StringComparison.Ordinal)),
            Arg.Any<IEnumerable<BsonDocument>>());
        await _mockDatabaseService.Received(1).DropCollection(
            Arg.Is<string>(name => name.StartsWith("_restore_", StringComparison.Ordinal)));
        await _mockDatabaseService.DidNotReceive().DropCollection("collection1");
    }

    [Test]
    public async Task RestoreBackup_WhenStagingInsertFails_LeavesTargetAndCleansStagingCollection()
    {
        // Arrange
        var backupName = "backup.zip";
        var backupPath = Path.Combine(_testTempPath, backupName);
        _localBackupHelperService.GetBackupPath(backupName, TestBotName).Returns(backupPath);
        CreateTestBackupFile(backupPath, new Dictionary<string, BsonDocument[]>
        {
            { "collection1", new[] { new BsonDocument { ["_id"] = 1 } } }
        });
        _mockDatabaseService.CreateAndPopulateCollection(Arg.Any<string>(), Arg.Any<IEnumerable<BsonDocument>>())
            .Returns(false);
        _mockDatabaseService.DropCollection(Arg.Any<string>()).Returns(true);

        // Act
        var result = await _service.RestoreBackup(backupName, TestBotName);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        await _mockDatabaseService.Received(1).DropCollection(
            Arg.Is<string>(name => name.StartsWith("_restore_", StringComparison.Ordinal)));
        await _mockDatabaseService.DidNotReceive().DropCollection("collection1");
        await _mockDatabaseService.DidNotReceive().RenameCollection(
            Arg.Any<string>(),
            "collection1",
            dropTarget: true);
    }

    [Test]
    public async Task RestoreBackup_WithEmptyCollection_ReplacesTarget()
    {
        // Arrange
        var backupName = "backup.zip";
        var backupPath = Path.Combine(_testTempPath, backupName);
        _localBackupHelperService.GetBackupPath(backupName, TestBotName).Returns(backupPath);
        CreateTestBackupFile(backupPath, new Dictionary<string, BsonDocument[]>
        {
            { "collection1", Array.Empty<BsonDocument>() }
        });
        _mockDatabaseService.CreateCollection(Arg.Any<string>()).Returns(true);
        _mockDatabaseService.RenameCollection(Arg.Any<string>(), "collection1", dropTarget: true).Returns(true);

        // Act
        var result = await _service.RestoreBackup(backupName, TestBotName);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        await _mockDatabaseService.Received(1).CreateCollection(
            Arg.Is<string>(name => name.StartsWith("_restore_", StringComparison.Ordinal)));
        await _mockDatabaseService.Received(1).RenameCollection(
            Arg.Is<string>(name => name.StartsWith("_restore_", StringComparison.Ordinal)),
            "collection1",
            dropTarget: true);
        await _mockDatabaseService.DidNotReceive().DropCollection(Arg.Any<string>());
        await _mockDatabaseService.DidNotReceive().CreateAndPopulateCollection(Arg.Any<string>(), Arg.Any<IEnumerable<BsonDocument>>());
    }

    [Test]
    public async Task RestoreBackup_WhenCreatingEmptyStagingCollectionFails_LeavesTargetAndCleansStagingCollection()
    {
        // Arrange
        var backupName = "backup.zip";
        var backupPath = Path.Combine(_testTempPath, backupName);
        _localBackupHelperService.GetBackupPath(backupName, TestBotName).Returns(backupPath);
        CreateTestBackupFile(backupPath, new Dictionary<string, BsonDocument[]>
        {
            { "collection1", Array.Empty<BsonDocument>() }
        });
        _mockDatabaseService.CreateCollection(Arg.Any<string>()).Returns(false);
        _mockDatabaseService.DropCollection(Arg.Any<string>()).Returns(true);

        // Act
        var result = await _service.RestoreBackup(backupName, TestBotName);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        await _mockDatabaseService.Received(1).DropCollection(
            Arg.Is<string>(name => name.StartsWith("_restore_", StringComparison.Ordinal)));
        await _mockDatabaseService.DidNotReceive().RenameCollection(
            Arg.Any<string>(),
            "collection1",
            dropTarget: true);
    }

    [Test]
    public async Task RestoreBackup_WhenExceptionOccurs_SendsNotificationAndResumesBot()
    {
        // Arrange
        var backupName = "backup.zip";
        var invalidPath = "invalid\\path\\backup.zip";
        _localBackupHelperService.GetBackupPath(backupName, TestBotName).Returns(invalidPath);

        // Act
        var result = await _service.RestoreBackup(backupName, TestBotName);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        await _notificationService.Received(1).SendErrorNotification(
            Arg.Is<string>(s => s.Contains("Could not restore backup")),
            TestBotName);
        await AssertRanWithUpdatesPaused<bool>();
        AssertBotWebhookNotTouchedDirectly();
    }

    [Test]
    public async Task RestoreBackup_CaseInsensitiveBotName_WorksCorrectly()
    {
        // Arrange
        var backupName = "backup.zip";
        var backupPath = Path.Combine(_testTempPath, backupName);
        _localBackupHelperService.GetBackupPath(backupName, Arg.Any<string>()).Returns(backupPath);
        CreateTestBackupFile(backupPath, new Dictionary<string, BsonDocument[]>
        {
            { "collection1", new[] { new BsonDocument { ["_id"] = 1 } } }
        });
        _mockDatabaseService.CreateAndPopulateCollection(Arg.Any<string>(), Arg.Any<IEnumerable<BsonDocument>>()).Returns(true);
        _mockDatabaseService.RenameCollection(Arg.Any<string>(), Arg.Any<string>(), dropTarget: true).Returns(true);

        // Act
        var result = await _service.RestoreBackup(backupName, TestBotName.ToLower()); // lowercase

        // Assert
        Assert.That(result.IsSuccess, Is.True);
    }

    private void CreateTestBackupFile(string path, Dictionary<string, BsonDocument[]> collections)
    {
        using var zipFile = ZipFile.Create(path);
        zipFile.BeginUpdate();

        foreach (var kvp in collections)
        {
            var collectionName = kvp.Key;
            var documents = kvp.Value;

            using var memoryStream = new MemoryStream();
            using var binaryWriter = new MongoDB.Bson.IO.BsonBinaryWriter(memoryStream);
            foreach (var document in documents)
            {
                BsonSerializer.Serialize(binaryWriter, document);
            }

            var bytes = memoryStream.ToArray();
            var dataSource = new CustomStaticDataSource(bytes);
            zipFile.Add(dataSource, $"{collectionName}.bson");
        }

        zipFile.CommitUpdate();
        zipFile.Close();
    }

    private static async IAsyncEnumerable<BsonDocument> FailAfterDocument()
    {
        yield return new BsonDocument { ["_id"] = 1 };
        await Task.Yield();
        throw new InvalidOperationException("Collection read failed.");
    }

    private static async IAsyncEnumerable<BsonDocument> ObserveCollectionRead(string collection, Action onRead)
    {
        onRead();
        yield return new BsonDocument { ["_id"] = 1, ["name"] = collection };
        yield return new BsonDocument { ["_id"] = 2, ["name"] = collection };
        await Task.Yield();
    }

    private class CustomStaticDataSource : IStaticDataSource
    {
        private readonly byte[] _data;

        public CustomStaticDataSource(byte[] data)
        {
            _data = data;
        }
        public Stream GetSource()
        {
            return new MemoryStream(_data);
        }
    }
}