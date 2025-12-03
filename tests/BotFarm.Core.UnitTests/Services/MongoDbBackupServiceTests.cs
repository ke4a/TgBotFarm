using BotFarm.Core.Abstractions;
using BotFarm.Core.Services;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
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
    private IEnumerable<IBotService> _botServices;
    private IEnumerable<IMongoDbDatabaseService> _databaseServices;
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
        _botServices = new[] { _mockBotService };
        _databaseServices = new[] { _mockDatabaseService };
        _testTempPath = Path.Combine(Path.GetTempPath(), "BotFarm_Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testTempPath);
        _service = new MongoDbBackupService(
            _botServices,
            _databaseServices,
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
    public async Task BackupDatabase_WithSuccessfulBackup_ReturnsSuccessWithFileName()
    {
        // Arrange
        var fileName = "20240101120000.zip";
        var archivePath = Path.Combine(_testTempPath, fileName);
        _localBackupHelperService.CreateArchive(TestBotName).Returns(archivePath);
        var collections = new List<string> { "collection1", "collection2", "collection3" };
        _mockDatabaseService.GetCollectionNames().Returns(collections);
        foreach (var collection in collections)
        {
            _mockDatabaseService.GetCollectionData(collection).Returns(new List<BsonDocument>
            {
                new BsonDocument { ["_id"] = 1, ["name"] = collection }
            });
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
        }
        await _localBackupHelperService.Received(1).CleanupBackups(TestBotName, Arg.Any<int>());
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
    }

    [Test]
    public async Task BackupDatabase_CaseInsensitiveBotName_WorksCorrectly()
    {
        // Arrange
        var archivePath = Path.Combine(_testTempPath, "20240101120000.zip");
        _localBackupHelperService.CreateArchive(Arg.Any<string>()).Returns(archivePath);
        _mockDatabaseService.GetCollectionNames().Returns(new List<string> { "collection1" });
        _mockDatabaseService.GetCollectionData("collection1").Returns(new List<BsonDocument>
        {
            new BsonDocument { ["_id"] = 1 }
        });
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
        _mockBotService.Pause().Returns(false);
        _localBackupHelperService.GetBackupPath(backupName, TestBotName).Returns("somepath.zip");

        // Act
        var result = await _service.RestoreBackup(backupName, TestBotName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(result.Errors.First().Message, Does.Contain("finished with errors"));
        }
    }

    [Test]
    public async Task RestoreBackup_WithSuccessfulRestore_ReturnsSuccess()
    {
        // Arrange
        var backupName = "backup.zip";
        var backupPath = Path.Combine(_testTempPath, backupName);
        _mockBotService.Pause().Returns(true);
        _mockBotService.Resume().Returns(true);
        _localBackupHelperService.GetBackupPath(backupName, TestBotName).Returns(backupPath);
        var collections = new Dictionary<string, BsonDocument[]>
        {
            { "collection1", new[] { new BsonDocument { ["_id"] = 1 } } },
            { "collection2", new[] { new BsonDocument { ["_id"] = 2 } } },
            { "collection3", new[] { new BsonDocument { ["_id"] = 3 } } }
        };
        CreateTestBackupFile(backupPath, collections);
        _mockDatabaseService.DropCollection(Arg.Any<string>()).Returns(true);
        _mockDatabaseService.CreateAndPopulateCollection(Arg.Any<string>(), Arg.Any<IEnumerable<BsonDocument>>()).Returns(true);

        // Act
        var result = await _service.RestoreBackup(backupName, TestBotName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Successes.First().Message, Does.Contain("finished successfully"));
        }
        await _mockBotService.Received(1).Pause();
        await _mockBotService.Received(1).Resume();
        foreach (var collection in collections.Keys)
        {
            await _mockDatabaseService.Received(1).DropCollection(collection);
            await _mockDatabaseService.Received(1).CreateAndPopulateCollection(collection, Arg.Any<IEnumerable<BsonDocument>>());
        }
    }

    [Test]
    public async Task RestoreBackup_WhenDropCollectionFails_SkipsPopulation()
    {
        // Arrange
        var backupName = "backup.zip";
        var backupPath = Path.Combine(_testTempPath, backupName);
        _mockBotService.Pause().Returns(true);
        _mockBotService.Resume().Returns(true);
        _localBackupHelperService.GetBackupPath(backupName, TestBotName).Returns(backupPath);
        CreateTestBackupFile(backupPath, new Dictionary<string, BsonDocument[]>
        {
            { "collection1", new[] { new BsonDocument { ["_id"] = 1 } } }
        });
        _mockDatabaseService.DropCollection(Arg.Any<string>()).Returns(false);

        // Act
        await _service.RestoreBackup(backupName, TestBotName);

        // Assert
        await _mockDatabaseService.DidNotReceive().CreateAndPopulateCollection(Arg.Any<string>(), Arg.Any<IEnumerable<BsonDocument>>());
    }

    [Test]
    public async Task RestoreBackup_WithEmptyCollection_SkipsRestore()
    {
        // Arrange
        var backupName = "backup.zip";
        var backupPath = Path.Combine(_testTempPath, backupName);
        _mockBotService.Pause().Returns(true);
        _mockBotService.Resume().Returns(true);
        _localBackupHelperService.GetBackupPath(backupName, TestBotName).Returns(backupPath);
        CreateTestBackupFile(backupPath, new Dictionary<string, BsonDocument[]>
        {
            { "collection1", Array.Empty<BsonDocument>() }
        });

        // Act
        await _service.RestoreBackup(backupName, TestBotName);

        // Assert
        await _mockDatabaseService.DidNotReceive().DropCollection(Arg.Any<string>());
        await _mockDatabaseService.DidNotReceive().CreateAndPopulateCollection(Arg.Any<string>(), Arg.Any<IEnumerable<BsonDocument>>());
    }

    [Test]
    public async Task RestoreBackup_WhenExceptionOccurs_SendsNotificationAndResumesBot()
    {
        // Arrange
        var backupName = "backup.zip";
        var invalidPath = "invalid\\path\\backup.zip";
        _mockBotService.Pause().Returns(true);
        _mockBotService.Resume().Returns(true);
        _localBackupHelperService.GetBackupPath(backupName, TestBotName).Returns(invalidPath);

        // Act
        var result = await _service.RestoreBackup(backupName, TestBotName);

        // Assert
        Assert.That(result.IsFailed, Is.True);
        await _notificationService.Received(1).SendErrorNotification(
            Arg.Is<string>(s => s.Contains("Could not restore backup")),
            TestBotName);
        await _mockBotService.Received(1).Resume();
    }

    [Test]
    public async Task RestoreBackup_CaseInsensitiveBotName_WorksCorrectly()
    {
        // Arrange
        var backupName = "backup.zip";
        var backupPath = Path.Combine(_testTempPath, backupName);
        _mockBotService.Pause().Returns(true);
        _mockBotService.Resume().Returns(true);
        _localBackupHelperService.GetBackupPath(backupName, Arg.Any<string>()).Returns(backupPath);
        CreateTestBackupFile(backupPath, new Dictionary<string, BsonDocument[]>
        {
            { "collection1", new[] { new BsonDocument { ["_id"] = 1 } } }
        });
        _mockDatabaseService.DropCollection(Arg.Any<string>()).Returns(true);
        _mockDatabaseService.CreateAndPopulateCollection(Arg.Any<string>(), Arg.Any<IEnumerable<BsonDocument>>()).Returns(true);

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
