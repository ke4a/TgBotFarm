using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using FluentResults;
using LiteDB;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class LiteDBBackupServiceTests
{
    private LiteDBBackupService _service;
    private IEnumerable<IBotService> _botServices;
    private IEnumerable<IDatabaseService> _databaseServices;
    private ILogger<LiteDBBackupService> _logger;
    private INotificationService _notificationService;
    private ICloudService _cloudService;
    private IBotService _mockBotService;
    private IDatabaseService _mockDatabaseService;
    private string _testBotName;
    private string _tempTestPath;

    [SetUp]
    public void SetUp()
    {
        _testBotName = "TestBot";
        _tempTestPath = Path.Combine(Path.GetTempPath(), "LiteDBBackupServiceTests");
        
        _mockBotService = Substitute.For<IBotService>();
        _mockBotService.Name.Returns(_testBotName);

        _mockDatabaseService = Substitute.For<IDatabaseService>();
        _mockDatabaseService.Name.Returns(_testBotName);

        _botServices = new List<IBotService> { _mockBotService };
        _databaseServices = new List<IDatabaseService> { _mockDatabaseService };
        
        _logger = Substitute.For<ILogger<LiteDBBackupService>>();
        _notificationService = Substitute.For<INotificationService>();
        _cloudService = Substitute.For<ICloudService>();

        _service = new LiteDBBackupService(
            _botServices,
            _databaseServices,
            _logger,
            _notificationService,
            _cloudService);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempTestPath))
        {
            Directory.Delete(_tempTestPath, true);
        }
    }

    [Test]
    public async Task BackupDatabase_SuccessfulBackup_ReturnsOkResult()
    {
        // Arrange
        var collectionNames = new List<string> { "collection1", "collection2" };
        var collectionData = new List<BsonDocument>
        {
            new() { ["_id"] = 1, ["name"] = "test1" },
            new() { ["_id"] = 2, ["name"] = "test2" }
        };

        _mockDatabaseService.GetCollectionNames().Returns(collectionNames);
        _mockDatabaseService.GetCollectionData(Arg.Any<string>()).Returns(collectionData);
        _cloudService.Upload(Arg.Any<string>(), _testBotName).Returns(true);
        _cloudService.CleanupRemote(_testBotName).Returns(true);

        // Act
        var result = await _service.BackupDatabase(_testBotName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Successes.First().Message, Does.Contain("Database backup finished successfully"));
            Assert.That(result.Successes.First().Metadata.ContainsKey("fileName"), Is.True);
        }

        await _cloudService.Received(1).Upload(Arg.Any<string>(), _testBotName);
        await _cloudService.Received(1).CleanupRemote(_testBotName);
    }

    [Test]
    public async Task BackupDatabase_CloudUploadFails_ReturnsFailResult()
    {
        // Arrange
        var collectionNames = new List<string> { "collection1" };
        var collectionData = new List<BsonDocument> { new() { ["_id"] = 1 } };

        _mockDatabaseService.GetCollectionNames().Returns(collectionNames);
        _mockDatabaseService.GetCollectionData(Arg.Any<string>()).Returns(collectionData);
        _cloudService.Upload(Arg.Any<string>(), _testBotName).Returns(false);

        // Act
        var result = await _service.BackupDatabase(_testBotName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(result.Errors.First().Message, Does.Contain("Database backup finished with errors"));
        }

        await _cloudService.Received(1).Upload(Arg.Any<string>(), _testBotName);
        await _cloudService.DidNotReceive().CleanupRemote(_testBotName);
    }

    [Test]
    public async Task BackupDatabase_CloudCleanupFails_ReturnsFailResult()
    {
        // Arrange
        var collectionNames = new List<string> { "collection1" };
        var collectionData = new List<BsonDocument> { new() { ["_id"] = 1 } };

        _mockDatabaseService.GetCollectionNames().Returns(collectionNames);
        _mockDatabaseService.GetCollectionData(Arg.Any<string>()).Returns(collectionData);
        _cloudService.Upload(Arg.Any<string>(), _testBotName).Returns(true);
        _cloudService.CleanupRemote(_testBotName).Returns(false);

        // Act
        var result = await _service.BackupDatabase(_testBotName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(result.Errors.First().Message, Does.Contain("Database backup finished with errors"));
        }

        await _cloudService.Received(1).Upload(Arg.Any<string>(), _testBotName);
        await _cloudService.Received(1).CleanupRemote(_testBotName);
    }

    [Test]
    public async Task BackupDatabase_DatabaseServiceThrowsException_ReturnsFailResult()
    {
        // Arrange
        _mockDatabaseService.GetCollectionNames().Returns(x => throw new Exception("Database error"));

        // Act
        var result = await _service.BackupDatabase(_testBotName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(result.Errors.First().Message, Does.Contain("Database backup finished with errors"));
        }

        await _notificationService.Received(1).SendErrorNotification(
            Arg.Is<string>(s => s.Contains("Failed to create backup archive")), 
            _testBotName);
    }

    [Test]
    public async Task RestoreBackup_SuccessfulRestore_ReturnsOkResult()
    {
        // Arrange
        var backupName = "backup1.zip";
        var backupUri = "https://example.com/backup1.zip";
        var downloadPath = Path.Combine(_tempTestPath, "downloaded.zip");
        
        Directory.CreateDirectory(_tempTestPath);
        CreateTestZipFile(downloadPath);

        var backupsList = new List<BackupInfo>
        {
            new() { Name = backupName, Uri = backupUri }
        };

        _cloudService.GetBackupsList(_testBotName).Returns(Result.Ok<IEnumerable<BackupInfo>>(backupsList));
        _cloudService.DownloadBackup(backupUri, _testBotName).Returns(downloadPath);
        _mockBotService.Pause().Returns(true);
        _mockDatabaseService.Release().Returns(true);
        _mockDatabaseService.Reconnect().Returns(true);
        _mockBotService.Resume().Returns(true);

        // Act
        var result = await _service.RestoreBackup(backupName, _testBotName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Successes.First().Message, Does.Contain("Database restore finished successfully"));
        }

        await _mockBotService.Received(1).Pause();
        await _mockDatabaseService.Received(1).Release();
        await _mockDatabaseService.Received(1).Reconnect();
        await _mockBotService.Received(1).Resume();
    }

    [Test]
    public async Task RestoreBackup_BackupNotFound_ReturnsFailResult()
    {
        // Arrange
        var backupName = "nonexistent.zip";
        var backupsList = new List<BackupInfo>();

        _cloudService.GetBackupsList(_testBotName).Returns(Result.Ok<IEnumerable<BackupInfo>>(backupsList));

        // Act
        var result = await _service.RestoreBackup(backupName, _testBotName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(result.Errors.First().Message, Does.Contain("Database restore finished with errors"));
        }

        await _cloudService.Received(1).GetBackupsList(_testBotName);
        await _cloudService.DidNotReceive().DownloadBackup(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task RestoreBackup_DownloadFails_ReturnsFailResult()
    {
        // Arrange
        var backupName = "backup1.zip";
        var backupUri = "https://example.com/backup1.zip";
        
        var backupsList = new List<BackupInfo>
        {
            new() { Name = backupName, Uri = backupUri }
        };

        _cloudService.GetBackupsList(_testBotName).Returns(Result.Ok<IEnumerable<BackupInfo>>(backupsList));
        _cloudService.DownloadBackup(backupUri, _testBotName).Returns(string.Empty);

        // Act
        var result = await _service.RestoreBackup(backupName, _testBotName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(result.Errors.First().Message, Does.Contain("Database restore finished with errors"));
        }

        await _cloudService.Received(1).DownloadBackup(backupUri, _testBotName);
    }

    [Test]
    public async Task RestoreBackup_BotPauseFails_ReturnsFailResult()
    {
        // Arrange
        var backupName = "backup1.zip";
        var backupUri = "https://example.com/backup1.zip";
        var downloadPath = Path.Combine(_tempTestPath, "downloaded.zip");
        
        Directory.CreateDirectory(_tempTestPath);
        CreateTestZipFile(downloadPath);

        var backupsList = new List<BackupInfo>
        {
            new() { Name = backupName, Uri = backupUri }
        };

        _cloudService.GetBackupsList(_testBotName).Returns(Result.Ok<IEnumerable<BackupInfo>>(backupsList));
        _cloudService.DownloadBackup(backupUri, _testBotName).Returns(downloadPath);
        _mockBotService.Pause().Returns(false);

        // Act
        var result = await _service.RestoreBackup(backupName, _testBotName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(result.Errors.First().Message, Does.Contain("Database restore finished with errors"));
        }

        await _mockBotService.Received(1).Pause();
        await _mockDatabaseService.DidNotReceive().Release();
        await _notificationService.Received(1).SendErrorNotification(
            Arg.Is<string>(s => s.Contains("Could not pause bot updates")), 
            _testBotName);
    }

    [Test]
    public async Task RestoreBackup_DatabaseReleaseFails_ReturnsFailResult()
    {
        // Arrange
        var backupName = "backup1.zip";
        var backupUri = "https://example.com/backup1.zip";
        var downloadPath = Path.Combine(_tempTestPath, "downloaded.zip");
        
        Directory.CreateDirectory(_tempTestPath);
        CreateTestZipFile(downloadPath);

        var backupsList = new List<BackupInfo>
        {
            new() { Name = backupName, Uri = backupUri }
        };

        _cloudService.GetBackupsList(_testBotName).Returns(Result.Ok<IEnumerable<BackupInfo>>(backupsList));
        _cloudService.DownloadBackup(backupUri, _testBotName).Returns(downloadPath);
        _mockBotService.Pause().Returns(true);
        _mockDatabaseService.Release().Returns(false);

        // Act
        var result = await _service.RestoreBackup(backupName, _testBotName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(result.Errors.First().Message, Does.Contain("Database restore finished with errors"));
        }

        await _mockBotService.Received(1).Pause();
        await _mockDatabaseService.Received(1).Release();
    }

    [Test]
    public async Task RestoreBackup_GetBackupsListFails_ReturnsFailResult()
    {
        // Arrange
        var backupName = "backup1.zip";
        _cloudService.GetBackupsList(_testBotName).Returns(Result.Fail<IEnumerable<BackupInfo>>("Cloud service error"));

        // Act
        var result = await _service.RestoreBackup(backupName, _testBotName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(result.Errors.First().Message, Does.Contain("Database restore finished with errors"));
        }

        await _cloudService.Received(1).GetBackupsList(_testBotName);
    }

    private void CreateTestZipFile(string zipPath)
    {
        var testData = new { _id = 1, name = "test" };
        var json = System.Text.Json.JsonSerializer.Serialize(new[] { testData });

        using var fileStream = new FileStream(zipPath, System.IO.FileMode.Create);
        using var zipStream = new ICSharpCode.SharpZipLib.Zip.ZipOutputStream(fileStream);

        var entry = new ICSharpCode.SharpZipLib.Zip.ZipEntry("collection1.json");
        zipStream.PutNextEntry(entry);

        var data = System.Text.Encoding.UTF8.GetBytes(json);
        zipStream.Write(data, 0, data.Length);
        zipStream.CloseEntry();
    }
}
