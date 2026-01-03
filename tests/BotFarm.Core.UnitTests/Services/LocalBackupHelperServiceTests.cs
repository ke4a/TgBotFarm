using BotFarm.Core.Abstractions;
using BotFarm.Core.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class LocalBackupHelperServiceTests
{
    private TestableLocalBackupHelperService _service;
    private ILogger<LocalBackupHelperService> _logger;
    private INotificationService _notificationService;
    private string _testBackupsPath;
    private const string TestBotName = "TestBot";

    [SetUp]
    public void SetUp()
    {
        _logger = Substitute.For<ILogger<LocalBackupHelperService>>();
        _notificationService = Substitute.For<INotificationService>();
        _testBackupsPath = Path.Combine(Path.GetTempPath(), "BotFarm_Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testBackupsPath);
        _service = new TestableLocalBackupHelperService(
            _logger,
            _notificationService,
            _testBackupsPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testBackupsPath))
        {
            Directory.Delete(_testBackupsPath, true);
        }
    }

    [Test]
    public async Task CleanupBackups_WithNoBackupDirectory_DoesNotThrowException()
    {
        // Arrange
        var nonExistentBot = "NonExistentBot";

        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await _service.CleanupBackups(nonExistentBot));
    }

    [Test]
    public async Task CleanupBackups_WithFewerBackupsThanMax_DoesNotDeleteAny()
    {
        // Arrange
        var botBackupPath = Path.Combine(_testBackupsPath, TestBotName);
        Directory.CreateDirectory(botBackupPath);
        CreateTestBackupFiles(botBackupPath, 5);
        var initialFileCount = Directory.GetFiles(botBackupPath, "*.zip").Length;

        // Act
        await _service.CleanupBackups(TestBotName, maxBackupsToKeep: 7);

        // Assert
        var finalFileCount = Directory.GetFiles(botBackupPath, "*.zip").Length;
        Assert.That(finalFileCount, Is.EqualTo(initialFileCount));
    }

    [Test]
    public async Task CleanupBackups_KeepsMostRecentBackups()
    {
        // Arrange
        var botBackupPath = Path.Combine(_testBackupsPath, TestBotName);
        Directory.CreateDirectory(botBackupPath);
        var files = CreateTestBackupFiles(botBackupPath, 10);
        var maxToKeep = 3;
        // Get the 3 most recent files
        var expectedFiles = files.OrderByDescending(f => f.CreationTime)
                                 .Take(maxToKeep)
                                 .Select(f => f.Name)
                                 .ToList();

        // Act
        await _service.CleanupBackups(TestBotName, maxBackupsToKeep: maxToKeep);

        // Assert
        var remainingFiles = Directory.GetFiles(botBackupPath, "*.zip")
                                      .Select(Path.GetFileName)
                                      .ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(remainingFiles, Has.Count.EqualTo(maxToKeep));
            foreach (var expectedFile in expectedFiles)
            {
                Assert.That(remainingFiles, Contains.Item(expectedFile));
            }
        }
    }

    [Test]
    public async Task CreateArchive_WithValidBotName_CreatesZipFileAndReturnsPath()
    {
        // Act
        var archivePath = await _service.CreateArchive(TestBotName);
        var botBackupPath = Path.Combine(_testBackupsPath, TestBotName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(botBackupPath), Is.True);
            Assert.That(archivePath, Is.Not.Empty);
            Assert.That(File.Exists(archivePath), Is.True);
            Assert.That(Path.GetExtension(archivePath), Is.EqualTo(".zip"));
            Assert.That(archivePath, Does.Contain(TestBotName));
        }
    }

    [Test]
    public async Task CreateArchive_CreatesValidZipFile()
    {
        // Act
        var archivePath = await _service.CreateArchive(TestBotName);
        var fileName = Path.GetFileNameWithoutExtension(archivePath);

        // Assert
        Assert.That(fileName, Does.Match(@"^\d{14}$")); // yyyyMMddHHmmss format
        // Verify file can be opened as zip
        Assert.DoesNotThrow(() =>
        {
            using var fileStream = File.OpenRead(archivePath);
            using var zipStream = new ICSharpCode.SharpZipLib.Zip.ZipInputStream(fileStream);
        });
    }

    [Test]
    public async Task GetBackupsList_WithNoBackupDirectory_ReturnsEmptyList()
    {
        // Act
        var result = await _service.GetBackupsList("NonExistentBot");

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.Empty);
        }
    }

    [Test]
    public async Task GetBackupsList_WithBackupFiles_ReturnsBackupInfoList()
    {
        // Arrange
        var botBackupPath = Path.Combine(_testBackupsPath, TestBotName);
        Directory.CreateDirectory(botBackupPath);
        CreateTestBackupFiles(botBackupPath, 3);

        // Act
        var result = await _service.GetBackupsList(TestBotName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Count(), Is.EqualTo(3));

            foreach (var backup in result.Value)
            {
                Assert.That(backup.Name, Is.Not.Null);
                Assert.That(backup.Size, Is.GreaterThan(0));
                Assert.That(backup.Date, Is.GreaterThan(DateTime.MinValue));
            }
        }
    }

    [Test]
    public async Task GetBackupsList_ReturnsBackupsOrderedByDateDescending()
    {
        // Arrange
        var botBackupPath = Path.Combine(_testBackupsPath, TestBotName);
        Directory.CreateDirectory(botBackupPath);
        CreateTestBackupFiles(botBackupPath, 5);

        // Act
        var result = await _service.GetBackupsList(TestBotName);

        // Assert
        var dates = result.Value.Select(b => b.Date).ToList();
        var sortedDates = dates.OrderByDescending(d => d).ToList();
        
        Assert.That(dates, Is.EqualTo(sortedDates));
    }

    [Test]
    public async Task GetBackupsList_OnlyReturnsZipFiles()
    {
        // Arrange
        var botBackupPath = Path.Combine(_testBackupsPath, TestBotName);
        Directory.CreateDirectory(botBackupPath);
        CreateTestBackupFiles(botBackupPath, 2);
        File.WriteAllText(Path.Combine(botBackupPath, "notazip.txt"), "test");
        File.WriteAllText(Path.Combine(botBackupPath, "alsonotazip.db"), "test");

        // Act
        var result = await _service.GetBackupsList(TestBotName);

        // Assert
        Assert.That(result.Value.Count(), Is.EqualTo(2));
    }

    [Test]
    public async Task RemoveBackup_WithExistingFile_DeletesFileAndReturnsSuccess()
    {
        // Arrange
        var botBackupPath = Path.Combine(_testBackupsPath, TestBotName);
        Directory.CreateDirectory(botBackupPath);
        var fileName = "backup.zip";
        var filePath = Path.Combine(botBackupPath, fileName);
        File.WriteAllText(filePath, "test backup");

        // Act
        var result = await _service.RemoveBackup(fileName, TestBotName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(File.Exists(filePath), Is.False);
        }
    }

    [Test]
    public async Task RemoveBackup_WithNonExistentFile_ReturnsFailure()
    {
        // Arrange
        var botBackupPath = Path.Combine(_testBackupsPath, TestBotName);
        Directory.CreateDirectory(botBackupPath);
        var fileName = "nonexistent.zip";

        // Act
        var result = await _service.RemoveBackup(fileName, TestBotName);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsFailed, Is.True);
            Assert.That(result.Errors[0].Message, Does.Contain("not found"));
        }
    }

    [Test]
    public async Task GetBackupPath_WithExistingFile_ReturnsFilePath()
    {
        // Arrange
        var botBackupPath = Path.Combine(_testBackupsPath, TestBotName);
        Directory.CreateDirectory(botBackupPath);
        var fileName = "backup.zip";
        var expectedPath = Path.Combine(botBackupPath, fileName);
        File.WriteAllText(expectedPath, "test");

        // Act
        var result = await _service.GetBackupPath(fileName, TestBotName);

        // Assert
        Assert.That(result, Is.EqualTo(expectedPath));
    }

    [Test]
    public async Task GetBackupPath_WithNonExistentFile_ReturnsEmptyString()
    {
        // Arrange
        var fileName = "nonexistent.zip";

        // Act
        var result = await _service.GetBackupPath(fileName, TestBotName);

        // Assert
        Assert.That(result, Is.Empty);
        await _notificationService.Received(1).SendErrorNotification(
            Arg.Is<string>(s => s.Contains($"Backup file '{fileName}' not found")),
            TestBotName);
    }

    private List<FileInfo> CreateTestBackupFiles(string directory, int count)
    {
        var files = new List<FileInfo>();
        
        for (int i = 0; i < count; i++)
        {
            var fileName = $"backup_{i:D3}.zip";
            var filePath = Path.Combine(directory, fileName);
            
            // Create file with some content
            File.WriteAllText(filePath, $"Test backup content {i}");
            
            var fileInfo = new FileInfo(filePath);
            
            // Set different creation times to simulate real backups
            fileInfo.CreationTime = DateTime.Now.AddMinutes(-count + i);
            
            files.Add(fileInfo);
            
            // Small delay to ensure different timestamps
            Thread.Sleep(10);
        }
        
        return files;
    }

    private class TestableLocalBackupHelperService : LocalBackupHelperService
    {
        private readonly string _testBackupsPath;

        public TestableLocalBackupHelperService(
            ILogger<LocalBackupHelperService> logger,
            INotificationService notificationService,
            string testBackupsPath) : base(logger, notificationService)
        {
            _testBackupsPath = testBackupsPath;
            
            // Use reflection to set the backupsPath field
            var field = typeof(LocalBackupHelperService)
                .GetField("backupsPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(this, _testBackupsPath);
        }
    }
}
