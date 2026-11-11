using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;

namespace BotFarm.Shared.Components;

/// <summary>
/// Manages backup creation, restore, deletion, and download for the selected bot.
/// </summary>
public partial class DashboardBackups : DashboardComponentBase
{
    /// <summary>
    /// The card title. Defaults to the localized "Backups".
    /// </summary>
    [Parameter] public string? Title { get; set; }

    private readonly List<BackupRow> _backups = [];
    private bool _loadingBackups;
    private bool _workingBackup;
    private bool _creatingBackup;

    [Inject] protected IBackupService BackupService { get; set; } = default!;
    [Inject] protected ILocalBackupHelperService LocalBackupService { get; set; } = default!;
    [Inject] protected ILogger<DashboardBackups> Logger { get; set; } = default!;
    [Inject] protected IStringLocalizer<DashboardBackups> L { get; set; } = default!;

    /// <inheritdoc />
    protected override IStringLocalizer Localizer => L;

    private string CreateBackupLabel => _creatingBackup ? L["Backups.Create.InProgress"] : L["Backups.Create.Action"];

    protected override async Task OnInitializedAsync()
    {
        await LoadBackups(true);
    }

    protected async Task CreateBackup()
    {
        if (_workingBackup)
        {
            return;
        }

        _workingBackup = true;
        _creatingBackup = true;
        try
        {
            var result = await BackupService.BackupDatabase(BotName);
            var message = GetResultMessage(result, L["Backups.Create.Done"], L["Backups.Create.Failed"]);
            var fileName = GetResultMetadata(result, "fileName");

            if (result.IsSuccess)
            {
                var toast = string.IsNullOrWhiteSpace(fileName) ? message : $"{message}\n{fileName}";
                Snackbar.Add(toast, Severity.Success);
                await LoadBackups(true);
            }
            else
            {
                Snackbar.Add(message, Severity.Error);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to create backup");
            Snackbar.Add(L["Backups.Create.Error", ex.Message], Severity.Error);
        }
        finally
        {
            _workingBackup = false;
            _creatingBackup = false;
        }
    }

    protected async Task LoadBackups(bool noToast)
    {
        _loadingBackups = true;
        try
        {
            var result = await LocalBackupService.GetBackupsList(BotName);
            _backups.Clear();

            var backups = result.ValueOrDefault;
            if (backups != null)
            {
                _backups.AddRange(backups.OrderByDescending(b => b.Date).Select((b, i) => new BackupRow(i + 1, b)));
            }

            if (!noToast)
            {
                var message = GetResultMessage(result, L["Backups.Load.Done"], L["Backups.Load.Failed"]);
                Snackbar.Add(message, result.IsSuccess ? Severity.Success : Severity.Error);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load backups");
            Snackbar.Add(L["Backups.Load.Error", ex.Message], Severity.Error);
        }
        finally
        {
            _loadingBackups = false;
        }
    }

    protected async Task DeleteBackup(string name)
    {
        var dialog = await DialogService.ShowMessageBoxAsync(
            L["Backups.Delete.Title"],
            L["Backups.Delete.Confirm", name],
            yesText: L["Common.Delete"],
            noText: L["Common.Cancel"],
            options: new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small });

        if (!dialog.HasValue || !dialog.Value)
        {
            return;
        }

        _workingBackup = true;
        try
        {
            var result = await LocalBackupService.RemoveBackup(name, BotName);
            var message = GetResultMessage(result, L["Backups.Delete.Done"], L["Backups.Delete.Failed"]);
            Snackbar.Add(message, result.IsSuccess ? Severity.Success : Severity.Error);
            await LoadBackups(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to delete backup");
            Snackbar.Add(L["Backups.Delete.Error", ex.Message], Severity.Error);
        }
        finally
        {
            _workingBackup = false;
        }
    }

    protected async Task RestoreBackup(string fileName)
    {
        var dialog = await DialogService.ShowMessageBoxAsync(
            L["Backups.Restore.Title"],
            L["Backups.Restore.Confirm", fileName],
            yesText: L["Backups.Restore.Action"],
            noText: L["Common.Cancel"],
            options: new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small });

        if (!dialog.HasValue || !dialog.Value)
        {
            return;
        }

        _workingBackup = true;
        try
        {
            var result = await BackupService.RestoreBackup(fileName, BotName);
            var message = GetResultMessage(result, L["Backups.Restore.Done"], L["Backups.Restore.Failed"]);
            Snackbar.Add(message, result.IsSuccess ? Severity.Success : Severity.Error);
            await LoadBackups(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to restore backup");
            Snackbar.Add(L["Backups.Restore.Error", ex.Message], Severity.Error);
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
            Snackbar.Add(L["Backups.NotFound"], Severity.Error);
            return;
        }

        using var reader = File.OpenRead(filePath);
        using var streamRef = new DotNetStreamReference(stream: reader);

        await JSRuntime.InvokeAsync<object>("downloadFileFromStream", [fileName, streamRef]);
    }

    /// <summary>
    /// A backup table row with its 1-based position in the list.
    /// </summary>
    protected sealed record BackupRow(int Number, BackupInfo Backup);
}