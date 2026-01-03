using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Shared.Components;
using FluentResults;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Reflection;

namespace BotFarm.Shared.UnitTests.Components;

[TestFixture]
public class DashboardBackupsTests
{
    private TestableDashboardBackups _component;
    private IBackupService _backupService;
    private ILocalBackupHelperService _localBackupService;
    private ILogger<DashboardBackups> _logger;
    private IJSRuntime _jsRuntime;
    private const string TestBotName = "TestBot";

    private class TestableDashboardBackups : DashboardBackups
    {
        private readonly FieldInfo _workingBackupField;
        private readonly FieldInfo _loadingBackupsField;
        private readonly FieldInfo _backupsField;

        public TestableDashboardBackups()
        {
            var type = typeof(DashboardBackups);
            _workingBackupField = type.GetField("_workingBackup", BindingFlags.NonPublic | BindingFlags.Instance)!;
            _loadingBackupsField = type.GetField("_loadingBackups", BindingFlags.NonPublic | BindingFlags.Instance)!;
            _backupsField = type.GetField("_backups", BindingFlags.NonPublic | BindingFlags.Instance)!;
        }

        public void SetDependencies(
            IBackupService backupService,
            ILocalBackupHelperService localBackupService,
            ILogger<DashboardBackups> logger,
            IJSRuntime jsRuntime)
        {
            BackupService = backupService;
            LocalBackupService = localBackupService;
            Logger = logger;
            JSRuntime = jsRuntime;
        }

        public Task InvokeOnInitializedAsync() => OnInitializedAsync();
        public Task InvokeCreateBackupAsync() => CreateBackupAsync();
        public Task InvokeLoadBackupsAsync(bool noToast) => LoadBackupsAsync(noToast);
        public Task InvokeDeleteBackupAsync(string name) => DeleteBackupAsync(name);
        public Task InvokeRestoreBackupAsync(string fileName) => RestoreBackupAsync(fileName);
        public Task InvokeDownloadBackup(string fileName) => DownloadBackup(fileName);
        
        public bool IsWorkingBackup => (bool)_workingBackupField.GetValue(this)!;
        public bool IsLoadingBackups => (bool)_loadingBackupsField.GetValue(this)!;
        public IReadOnlyList<BackupInfo> Backups => (List<BackupInfo>)_backupsField.GetValue(this)!;
    }

    [SetUp]
    public void SetUp()
    {
        _backupService = Substitute.For<IBackupService>();
        _localBackupService = Substitute.For<ILocalBackupHelperService>();
        _logger = Substitute.For<ILogger<DashboardBackups>>();
        _jsRuntime = Substitute.For<IJSRuntime>();

        _component = new TestableDashboardBackups
        {
            BotName = TestBotName,
            Title = "Backups"
        };
        _component.SetDependencies(_backupService, _localBackupService, _logger, _jsRuntime);
    }

    [Test]
    public async Task OnInitializedAsync_LoadsBackups()
    {
        // Arrange
        var backups = new List<BackupInfo>
        {
            new() { Name = "backup1.zip", Date = DateTime.UtcNow }
        };
        _localBackupService.GetBackupsList(TestBotName).Returns(Result.Ok(backups.AsEnumerable()));

        // Act
        await _component.InvokeOnInitializedAsync();

        // Assert
        await _localBackupService.Received(1).GetBackupsList(TestBotName);
        Assert.That(_component.Backups, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task CreateBackupAsync_WhenSuccess_ShowsSuccessToastAndReloadsBackups()
    {
        // Arrange
        var fileName = "backup_test.zip";
        var success = new Success("Backup created").WithMetadata("fileName", fileName);
        _backupService.BackupDatabase(TestBotName).Returns(Result.Ok().WithSuccess(success));
        _localBackupService.GetBackupsList(TestBotName).Returns(Result.Ok(Enumerable.Empty<BackupInfo>()));

        // Act
        await _component.InvokeCreateBackupAsync();

        // Assert
        await _backupService.Received(1).BackupDatabase(TestBotName);
        await _jsRuntime.Received(1).InvokeVoidAsync(
            "showToast",
            Arg.Is<object?[]>(args => args.Length == 2 && (bool?)args[1] == true));
        await _localBackupService.Received(1).GetBackupsList(TestBotName);
    }

    [Test]
    public async Task CreateBackupAsync_WhenFails_ShowsErrorToast()
    {
        // Arrange
        _backupService.BackupDatabase(TestBotName).Returns(Result.Fail("Backup failed"));

        // Act
        await _component.InvokeCreateBackupAsync();

        // Assert
        await _jsRuntime.Received(1).InvokeVoidAsync(
            "showToast",
            Arg.Is<object?[]>(args => args.Length == 2 && (bool?)args[1] == false));
        await _localBackupService.DidNotReceive().GetBackupsList(Arg.Any<string>());
    }

    [Test]
    public async Task CreateBackupAsync_WhenAlreadyWorking_DoesNothing()
    {
        // Arrange
        var tcs = new TaskCompletionSource<Result>();
        var callCount = 0;
        _backupService.BackupDatabase(TestBotName)
            .Returns(callInfo => 
            {
                callCount++;
                return tcs.Task;
            });

        // Act
        var task1 = _component.InvokeCreateBackupAsync();
        var task2 = _component.InvokeCreateBackupAsync();
        tcs.SetResult(Result.Ok());
        await Task.WhenAll(task1, task2);

        // Assert
        Assert.That(callCount, Is.EqualTo(1));
    }

    [Test]
    public async Task CreateBackupAsync_WhenException_ShowsErrorToastAndResetsWorkingFlag()
    {
        // Arrange
        _backupService.BackupDatabase(TestBotName).Throws(new Exception("Test exception"));

        // Act
        await _component.InvokeCreateBackupAsync();

        // Assert
        await _jsRuntime.Received(1).InvokeVoidAsync(
            "showToast",
            Arg.Is<object?[]>(args => 
                args.Length == 2 && 
                args[0] != null && args[0].ToString()!.Contains("Test exception") &&
                (bool?)args[1] == false));
        Assert.That(_component.IsWorkingBackup, Is.False);
    }

    [Test]
    public async Task LoadBackupsAsync_WithBackups_LoadsAndOrdersByDateDescending()
    {
        // Arrange
        var backups = new List<BackupInfo>
        {
            new() { Name = "backup1.zip", Date = DateTime.UtcNow.AddDays(-2) },
            new() { Name = "backup2.zip", Date = DateTime.UtcNow.AddDays(-1) },
            new() { Name = "backup3.zip", Date = DateTime.UtcNow }
        };
        _localBackupService.GetBackupsList(TestBotName).Returns(Result.Ok(backups.AsEnumerable()));

        // Act
        await _component.InvokeLoadBackupsAsync(true);

        // Assert
        Assert.That(_component.Backups, Has.Count.EqualTo(3));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_component.Backups[0].Name, Is.EqualTo("backup3.zip"));
            Assert.That(_component.Backups[1].Name, Is.EqualTo("backup2.zip"));
            Assert.That(_component.Backups[2].Name, Is.EqualTo("backup1.zip"));
        }
    }

    [Test]
    public async Task LoadBackupsAsync_WithNoToastFalse_ShowsToast()
    {
        // Arrange
        _localBackupService.GetBackupsList(TestBotName).Returns(Result.Ok(Enumerable.Empty<BackupInfo>()));

        // Act
        await _component.InvokeLoadBackupsAsync(false);

        // Assert
        await _jsRuntime.Received(1).InvokeVoidAsync("showToast", Arg.Any<object?[]>());
    }

    [Test]
    public async Task LoadBackupsAsync_WithNoToastTrue_DoesNotShowToast()
    {
        // Arrange
        _localBackupService.GetBackupsList(TestBotName).Returns(Result.Ok(Enumerable.Empty<BackupInfo>()));

        // Act
        await _component.InvokeLoadBackupsAsync(true);

        // Assert
        await _jsRuntime.DidNotReceive().InvokeVoidAsync("showToast", Arg.Any<object?[]>());
    }

    [Test]
    public async Task LoadBackupsAsync_WhenException_ShowsErrorToastAndResetsLoadingFlag()
    {
        // Arrange
        _localBackupService.GetBackupsList(TestBotName).Throws(new Exception("Load failed"));

        // Act
        await _component.InvokeLoadBackupsAsync(false);

        // Assert
        await _jsRuntime.Received(1).InvokeVoidAsync(
            "showToast",
            Arg.Is<object?[]>(args => 
                args.Length == 2 && 
                args[0] != null && args[0].ToString()!.Contains("Load failed") &&
                (bool?)args[1] == false));
        Assert.That(_component.IsLoadingBackups, Is.False);
    }

    [Test]
    public async Task DeleteBackupAsync_WhenUserConfirms_DeletesBackup()
    {
        // Arrange
        var backupName = "backup_test.zip";
        _jsRuntime.InvokeAsync<bool>("confirm", Arg.Any<object?[]>()).Returns(true);
        _localBackupService.RemoveBackup(backupName, TestBotName).Returns(Result.Ok());
        _localBackupService.GetBackupsList(TestBotName).Returns(Result.Ok(Enumerable.Empty<BackupInfo>()));

        // Act
        await _component.InvokeDeleteBackupAsync(backupName);

        // Assert
        await _localBackupService.Received(1).RemoveBackup(backupName, TestBotName);
        await _localBackupService.Received(1).GetBackupsList(TestBotName);
    }

    [Test]
    public async Task DeleteBackupAsync_WhenUserCancels_DoesNotDeleteBackup()
    {
        // Arrange
        var backupName = "backup_test.zip";
        _jsRuntime.InvokeAsync<bool>("confirm", Arg.Any<object?[]>()).Returns(false);

        // Act
        await _component.InvokeDeleteBackupAsync(backupName);

        // Assert
        await _localBackupService.DidNotReceive().RemoveBackup(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task DeleteBackupAsync_WhenException_ShowsErrorToastAndResetsWorkingFlag()
    {
        // Arrange
        var backupName = "backup_test.zip";
        _jsRuntime.InvokeAsync<bool>("confirm", Arg.Any<object?[]>()).Returns(true);
        _localBackupService.RemoveBackup(backupName, TestBotName).Throws(new Exception("Delete failed"));

        // Act
        await _component.InvokeDeleteBackupAsync(backupName);

        // Assert
        await _jsRuntime.Received(1).InvokeVoidAsync(
            "showToast",
            Arg.Is<object?[]>(args => 
                args.Length == 2 && 
                args[0] != null && args[0].ToString()!.Contains("Delete failed") &&
                (bool?)args[1] == false));
        Assert.That(_component.IsWorkingBackup, Is.False);
    }

    [Test]
    public async Task RestoreBackupAsync_WhenUserConfirms_RestoresBackup()
    {
        // Arrange
        var fileName = "backup_test.zip";
        _jsRuntime.InvokeAsync<bool>("confirm", Arg.Any<object?[]>()).Returns(true);
        _backupService.RestoreBackup(fileName, TestBotName).Returns(Result.Ok());
        _localBackupService.GetBackupsList(TestBotName).Returns(Result.Ok(Enumerable.Empty<BackupInfo>()));

        // Act
        await _component.InvokeRestoreBackupAsync(fileName);

        // Assert
        await _backupService.Received(1).RestoreBackup(fileName, TestBotName);
        await _localBackupService.Received(1).GetBackupsList(TestBotName);
    }

    [Test]
    public async Task RestoreBackupAsync_WhenUserCancels_DoesNotRestoreBackup()
    {
        // Arrange
        var fileName = "backup_test.zip";
        _jsRuntime.InvokeAsync<bool>("confirm", Arg.Any<object?[]>()).Returns(false);

        // Act
        await _component.InvokeRestoreBackupAsync(fileName);

        // Assert
        await _backupService.DidNotReceive().RestoreBackup(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task RestoreBackupAsync_WhenException_ShowsErrorToastAndResetsWorkingFlag()
    {
        // Arrange
        var fileName = "backup_test.zip";
        _jsRuntime.InvokeAsync<bool>("confirm", Arg.Any<object?[]>()).Returns(true);
        _backupService.RestoreBackup(fileName, TestBotName).Throws(new Exception("Restore failed"));

        // Act
        await _component.InvokeRestoreBackupAsync(fileName);

        // Assert
        await _jsRuntime.Received(1).InvokeVoidAsync(
            "showToast",
            Arg.Is<object?[]>(args => 
                args.Length == 2 && 
                args[0] != null && args[0].ToString()!.Contains("Restore failed") &&
                (bool?)args[1] == false));
        Assert.That(_component.IsWorkingBackup, Is.False);
    }

    [Test]
    public async Task DownloadBackup_WithValidFile_DownloadsFile()
    {
        // Arrange
        var fileName = "backup_test.zip";
        var filePath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(filePath, "test content");
            _localBackupService.GetBackupPath(fileName, TestBotName).Returns(filePath);

            // Act
            await _component.InvokeDownloadBackup(fileName);

            // Assert
            await _jsRuntime.Received(1).InvokeAsync<object>(
                "downloadFileFromStream",
                Arg.Is<object?[]>(args => args.Length == 2 && args[0] as string == fileName));
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Test]
    public async Task DownloadBackup_WithMissingFile_ShowsAlert()
    {
        // Arrange
        var fileName = "backup_test.zip";
        _localBackupService.GetBackupPath(fileName, TestBotName).Returns(string.Empty);

        // Act
        await _component.InvokeDownloadBackup(fileName);

        // Assert
        await _jsRuntime.Received(1).InvokeAsync<object>(
            "alert",
            Arg.Is<object?[]>(args => args.Length == 1 && args[0] != null && args[0].ToString()!.Contains("not found")));
    }
}
