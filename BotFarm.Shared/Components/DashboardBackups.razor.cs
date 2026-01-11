using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;

namespace BotFarm.Shared.Components;

public partial class DashboardBackups : DashboardComponentBase
{
    [Parameter]
    public string Title { get; set; } = "Backups";

    private readonly List<BackupInfo> _backups = [];
    private bool _loadingBackups;
    private bool _workingBackup;

    [Inject] protected IBackupService BackupService { get; set; } = default!;
    [Inject] protected ILocalBackupHelperService LocalBackupService { get; set; } = default!;
    [Inject] protected ILogger<DashboardBackups> Logger { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await LoadBackupsAsync(true);
    }

    protected async Task CreateBackupAsync()
    {
        if (_workingBackup)
        {
            return;
        }

        _workingBackup = true;
        try
        {
            var result = await BackupService.BackupDatabase(BotName);
            var message = GetResultMessage(result, "Backup created", "Backup failed");
            var fileName = GetResultMetadata(result, "fileName");

            if (result.IsSuccess)
            {
                var toast = string.IsNullOrWhiteSpace(fileName) ? message : $"{message}\n{fileName}";
                Snackbar.Add(toast, Severity.Success);
                await LoadBackupsAsync(true);
            }
            else
            {
                Snackbar.Add(message, Severity.Error);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to create backup");
            Snackbar.Add($"Failed to create backup: {ex.Message}", Severity.Error);
        }
        finally
        {
            _workingBackup = false;
        }
    }

    protected async Task LoadBackupsAsync(bool noToast)
    {
        _loadingBackups = true;
        try
        {
            var result = await LocalBackupService.GetBackupsList(BotName);
            _backups.Clear();

            var backups = result.ValueOrDefault;
            if (backups != null)
            {
                _backups.AddRange(backups.OrderByDescending(b => b.Date));
            }

            if (!noToast)
            {
                var message = GetResultMessage(result, "Backups loaded", "Failed to load backups");
                Snackbar.Add(message, result.IsSuccess ? Severity.Success : Severity.Error);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load backups");
            Snackbar.Add($"Failed to load backups: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loadingBackups = false;
        }
    }

    protected async Task DeleteBackupAsync(string name)
    {
        var dialog = await DialogService.ShowMessageBox(
            "Delete backup",
            $"Are you sure you want to delete backup '{name}'?",
            yesText: "Delete",
            noText: "Cancel",
            options: new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small });

        if (!dialog.HasValue || !dialog.Value)
        {
            return;
        }

        _workingBackup = true;
        try
        {
            var result = await LocalBackupService.RemoveBackup(name, BotName);
            var message = GetResultMessage(result, "Delete backup", "Delete backup failed");
            Snackbar.Add(message, result.IsSuccess ? Severity.Success : Severity.Error);
            await LoadBackupsAsync(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to delete backup");
            Snackbar.Add($"Failed to delete backup: {ex.Message}", Severity.Error);
        }
        finally
        {
            _workingBackup = false;
        }
    }

    protected async Task RestoreBackupAsync(string fileName)
    {
        var dialog = await DialogService.ShowMessageBox(
            "Restore backup",
            $"Are you sure you want to restore backup '{fileName}'?",
            yesText: "Restore",
            noText: "Cancel",
            options: new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small });

        if (!dialog.HasValue || !dialog.Value)
        {
            return;
        }

        _workingBackup = true;
        try
        {
            var result = await BackupService.RestoreBackup(fileName, BotName);
            var message = GetResultMessage(result, "Restore backup", "Restore backup failed");
            Snackbar.Add(message, result.IsSuccess ? Severity.Success : Severity.Error);
            await LoadBackupsAsync(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to restore backup");
            Snackbar.Add($"Failed to restore backup: {ex.Message}", Severity.Error);
        }
        finally
        {
            _workingBackup = false;
        }
    }

    protected async Task DownloadBackup(string fileName)
    {
        var filePath = await LocalBackupService.GetBackupPath(fileName, BotName);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            Snackbar.Add("Backup file not found.", Severity.Error);
            return;
        }

        using var reader = File.OpenRead(filePath);
        using var streamRef = new DotNetStreamReference(stream: reader);

        await JSRuntime.InvokeAsync<object>("downloadFileFromStream", [fileName, streamRef]);
    }
}
