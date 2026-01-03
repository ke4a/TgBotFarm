using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

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
                await ShowToastAsync(toast, true);
                await LoadBackupsAsync(true);
            }
            else
            {
                await ShowToastAsync(message, false);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to create backup");
            await ShowToastAsync($"Failed to create backup: {ex.Message}", false);
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
                await ShowToastAsync(message, result.IsSuccess);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load backups");
            await ShowToastAsync($"Failed to load backups: {ex.Message}", false);
        }
        finally
        {
            _loadingBackups = false;
        }
    }

    protected async Task DeleteBackupAsync(string name)
    {
        var confirm = await JSRuntime.InvokeAsync<bool>("confirm", [$"Delete backup '{name}'?"]);
        if (!confirm)
        {
            return;
        }

        _workingBackup = true;
        try
        {
            var result = await LocalBackupService.RemoveBackup(name, BotName);
            var message = GetResultMessage(result, "Delete backup", "Delete backup failed");
            await ShowToastAsync(message, result.IsSuccess);
            await LoadBackupsAsync(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to delete backup");
            await ShowToastAsync($"Failed to delete backup: {ex.Message}", false);
        }
        finally
        {
            _workingBackup = false;
        }
    }

    protected async Task RestoreBackupAsync(string fileName)
    {
        var confirm = await JSRuntime.InvokeAsync<bool>("confirm", [$"Restore backup '{fileName}'?"]);
        if (!confirm)
        {
            return;
        }

        _workingBackup = true;
        try
        {
            var result = await BackupService.RestoreBackup(fileName, BotName);
            var message = GetResultMessage(result, "Restore backup", "Restore backup failed");
            await ShowToastAsync(message, result.IsSuccess);
            await LoadBackupsAsync(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to restore backup");
            await ShowToastAsync($"Failed to restore backup: {ex.Message}", false);
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
            await JSRuntime.InvokeAsync<object>("alert", ["Backup file not found."]);
            return;
        }

        using var reader = File.OpenRead(filePath);
        using var streamRef = new DotNetStreamReference(stream: reader);

        await JSRuntime.InvokeAsync<object>("downloadFileFromStream", [fileName, streamRef]);
    }
}
