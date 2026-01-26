using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Shared.Components;
using FluentResults;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Reflection;

namespace BotFarm.Shared.UnitTests.Components;

[TestFixture]
public class DashboardBackupsTests
{
    private TestableDashboardBackups _component = default!;
    private IBackupService _backupService = default!;
    private ILocalBackupHelperService _localBackupService = default!;
    private ILogger<DashboardBackups> _logger = default!;
    private IJSRuntime _jsRuntime = default!;
    private ISnackbar _snackbar = default!;
    private IDialogService _dialogService = default!;
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
            IJSRuntime jsRuntime,
            ISnackbar snackbar,
            IDialogService dialogService)
        {
            BackupService = backupService;
            LocalBackupService = localBackupService;
            Logger = logger;
            JSRuntime = jsRuntime;
            Snackbar = snackbar;
            DialogService = dialogService;
        }

        public Task InvokeOnInitializedAsync() => OnInitializedAsync();
        public Task InvokeCreateBackup() => CreateBackup();
        public Task InvokeLoadBackups(bool noToast) => LoadBackups(noToast);
        public Task InvokeDeleteBackup(string name) => DeleteBackup(name);
        public Task InvokeRestoreBackup(string fileName) => RestoreBackup(fileName);
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
        _snackbar = Substitute.For<ISnackbar>();
        _dialogService = Substitute.For<IDialogService>();

        _component = new TestableDashboardBackups
        {
            BotName = TestBotName,
            Title = "Backups"
        };
        _component.SetDependencies(_backupService, _localBackupService, _logger, _jsRuntime, _snackbar, _dialogService);
    }

    [TearDown]
    public void TearDown()
    {
        _snackbar?.Dispose();
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
    public async Task CreateBackupAsync_WhenSuccess_ShowsSuccessSnackbarAndReloadsBackups()
    {
        // Arrange
        var fileName = "backup_test.zip";
        var success = new Success("Backup created").WithMetadata("fileName", fileName);
        _backupService.BackupDatabase(TestBotName).Returns(Result.Ok().WithSuccess(success));
        _localBackupService.GetBackupsList(TestBotName).Returns(Result.Ok(Enumerable.Empty<BackupInfo>()));

        // Act
        await _component.InvokeCreateBackup();

        // Assert
        await _backupService.Received(1).BackupDatabase(TestBotName);
        _snackbar.Received(1).Add(
            Arg.Is<string>(msg => msg.Contains("Backup created")),
            Severity.Success,
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
        await _localBackupService.Received(1).GetBackupsList(TestBotName);
    }

    [Test]
    public async Task CreateBackupAsync_WhenFails_ShowsErrorSnackbar()
    {
        // Arrange
        _backupService.BackupDatabase(TestBotName).Returns(Result.Fail("Backup failed"));

        // Act
        await _component.InvokeCreateBackup();

        // Assert
        _snackbar.Received(1).Add(
            Arg.Is<string>(msg => msg.Contains("Backup failed")),
            Severity.Error,
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
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
        var task1 = _component.InvokeCreateBackup();
        var task2 = _component.InvokeCreateBackup();
        tcs.SetResult(Result.Ok());
        await Task.WhenAll(task1, task2);

        // Assert
        Assert.That(callCount, Is.EqualTo(1));
    }

    [Test]
    public async Task CreateBackupAsync_WhenException_ShowsErrorSnackbarAndResetsWorkingFlag()
    {
        // Arrange
        _backupService.BackupDatabase(TestBotName).Throws(new Exception("Test exception"));

        // Act
        await _component.InvokeCreateBackup();

        // Assert
        _snackbar.Received(1).Add(
            Arg.Is<string>(msg => msg.Contains("Test exception")),
            Severity.Error,
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
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
        await _component.InvokeLoadBackups(true);

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
    public async Task LoadBackupsAsync_WithNoToastFalse_ShowsSnackbar()
    {
        // Arrange
        _localBackupService.GetBackupsList(TestBotName).Returns(Result.Ok(Enumerable.Empty<BackupInfo>()));

        // Act
        await _component.InvokeLoadBackups(false);

        // Assert
        _snackbar.Received(1).Add(
            Arg.Any<string>(),
            Arg.Any<Severity>(),
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
    }

    [Test]
    public async Task LoadBackupsAsync_WithNoToastTrue_DoesNotShowSnackbar()
    {
        // Arrange
        _localBackupService.GetBackupsList(TestBotName).Returns(Result.Ok(Enumerable.Empty<BackupInfo>()));

        // Act
        await _component.InvokeLoadBackups(true);

        // Assert
        _snackbar.DidNotReceive().Add(
            Arg.Any<string>(),
            Arg.Any<Severity>(),
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
    }

    [Test]
    public async Task LoadBackupsAsync_WhenException_ShowsErrorSnackbarAndResetsLoadingFlag()
    {
        // Arrange
        _localBackupService.GetBackupsList(TestBotName).Throws(new Exception("Load failed"));

        // Act
        await _component.InvokeLoadBackups(false);

        // Assert
        _snackbar.Received(1).Add(
            Arg.Is<string>(msg => msg.Contains("Load failed")),
            Severity.Error,
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
        Assert.That(_component.IsLoadingBackups, Is.False);
    }

    [Test]
    public async Task DeleteBackupAsync_WhenUserConfirms_DeletesBackup()
    {
        // Arrange
        var backupName = "backup_test.zip";
        _dialogService.ShowMessageBox(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<DialogOptions>()).Returns(true);
        _localBackupService.RemoveBackup(backupName, TestBotName).Returns(Result.Ok());
        _localBackupService.GetBackupsList(TestBotName).Returns(Result.Ok(Enumerable.Empty<BackupInfo>()));

        // Act
        await _component.InvokeDeleteBackup(backupName);

        // Assert
        await _localBackupService.Received(1).RemoveBackup(backupName, TestBotName);
        await _localBackupService.Received(1).GetBackupsList(TestBotName);
    }

    [Test]
    public async Task DeleteBackupAsync_WhenUserCancels_DoesNotDeleteBackup()
    {
        // Arrange
        var backupName = "backup_test.zip";
        _dialogService.ShowMessageBox(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<DialogOptions>()).Returns((bool?)null);

        // Act
        await _component.InvokeDeleteBackup(backupName);

        // Assert
        await _localBackupService.DidNotReceive().RemoveBackup(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task DeleteBackupAsync_WhenException_ShowsErrorSnackbarAndResetsWorkingFlag()
    {
        // Arrange
        var backupName = "backup_test.zip";
        _dialogService.ShowMessageBox(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<DialogOptions>()).Returns(true);
        _localBackupService.RemoveBackup(backupName, TestBotName).Throws(new Exception("Delete failed"));

        // Act
        await _component.InvokeDeleteBackup(backupName);

        // Assert
        _snackbar.Received(1).Add(
            Arg.Is<string>(msg => msg.Contains("Delete failed")),
            Severity.Error,
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
        Assert.That(_component.IsWorkingBackup, Is.False);
    }

    [Test]
    public async Task RestoreBackupAsync_WhenUserConfirms_RestoresBackup()
    {
        // Arrange
        var fileName = "backup_test.zip";
        _dialogService.ShowMessageBox(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<DialogOptions>()).Returns(true);
        _backupService.RestoreBackup(fileName, TestBotName).Returns(Result.Ok());
        _localBackupService.GetBackupsList(TestBotName).Returns(Result.Ok(Enumerable.Empty<BackupInfo>()));

        // Act
        await _component.InvokeRestoreBackup(fileName);

        // Assert
        await _backupService.Received(1).RestoreBackup(fileName, TestBotName);
        await _localBackupService.Received(1).GetBackupsList(TestBotName);
    }

    [Test]
    public async Task RestoreBackupAsync_WhenUserCancels_DoesNotRestoreBackup()
    {
        // Arrange
        var fileName = "backup_test.zip";
        _dialogService.ShowMessageBox(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<DialogOptions>()).Returns((bool?)null);

        // Act
        await _component.InvokeRestoreBackup(fileName);

        // Assert
        await _backupService.DidNotReceive().RestoreBackup(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task RestoreBackupAsync_WhenException_ShowsErrorSnackbarAndResetsWorkingFlag()
    {
        // Arrange
        var fileName = "backup_test.zip";
        _dialogService.ShowMessageBox(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<DialogOptions>()).Returns(true);
        _backupService.RestoreBackup(fileName, TestBotName).Throws(new Exception("Restore failed"));

        // Act
        await _component.InvokeRestoreBackup(fileName);

        // Assert
        _snackbar.Received(1).Add(
            Arg.Is<string>(msg => msg.Contains("Restore failed")),
            Severity.Error,
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
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
    public async Task DownloadBackup_WithMissingFile_ShowsSnackbar()
    {
        // Arrange
        var fileName = "backup_test.zip";
        _localBackupService.GetBackupPath(fileName, TestBotName).Returns(string.Empty);

        // Act
        await _component.InvokeDownloadBackup(fileName);

        // Assert
        _snackbar.Received(1).Add(
            Arg.Is<string>(msg => msg.Contains("not found")),
            Severity.Error,
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
    }
}
