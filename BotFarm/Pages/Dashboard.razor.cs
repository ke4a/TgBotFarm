using BotFarm.Components;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Shared;
using BotFarm.Shared.Utilities;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using MudBlazor;

namespace BotFarm.Pages;

/// <summary>
/// Code-behind for the operational dashboard page.
/// </summary>
public partial class Dashboard : IAsyncDisposable
{
    private readonly List<BotRuntimeStatus> _botStatuses = [];
    private ElementReference _uptimeElement;
    private IJSObjectReference? _uptimeModule;
    private IJSObjectReference? _uptimeTicker;
    private bool _loadingStats;
    private bool _loadingLogs;
    private bool _loadingBotStatuses;
    private bool _shuttingDown;
    private string? _memory;
    private string? _uptime;
    private string? _logsDirectory;
    private readonly List<LogFileEntry> _logFiles = [];

    [Inject] private HealthCheckService HealthChecks { get; set; } = default!;
    [Inject] private IHostApplicationLifetime ApplicationLifetime { get; set; } = default!;
    [Inject] protected ILogger<Dashboard> Logger { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
    [Inject] protected IBotControlService BotControlService { get; set; } = default!;
    [Inject] protected IAppDataPaths DataPaths { get; set; } = default!;
    [Inject] protected IStringLocalizer<Dashboard> L { get; set; } = default!;
    [Inject] protected IStringLocalizer<SharedResources> Units { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        LoadBotStatuses();
        await Task.WhenAll(LoadHealth(), LoadLogs());
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        // The browser keeps the uptime ticking, so it needs no server renders after this.
        var seconds = (DateTime.UtcNow - Program.StartTime).TotalSeconds;
        _uptimeModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/uptime.js");
        _uptimeTicker = await _uptimeModule.InvokeAsync<IJSObjectReference>("startUptime", _uptimeElement, seconds);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_uptimeTicker is not null)
            {
                await _uptimeTicker.InvokeVoidAsync("dispose");
                await _uptimeTicker.DisposeAsync();
            }

            if (_uptimeModule is not null)
            {
                await _uptimeModule.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone, so the page and its ticker are gone too.
        }
    }

    protected void LoadBotStatuses()
    {
        _loadingBotStatuses = true;
        try
        {
            _botStatuses.Clear();
            _botStatuses.AddRange(BotControlService.GetStatuses());
        }
        finally
        {
            _loadingBotStatuses = false;
        }
    }

    private async Task LoadHealth()
    {
        _loadingStats = true;
        try
        {
            var report = await HealthChecks.CheckHealthAsync(registration => registration.Tags.Contains("BotFarmHealth"));

            if (report.Entries.TryGetValue("AppStats", out var appStats)
                && appStats.Data.TryGetValue("Uptime", out var uptimeValue))
            {
                _uptime = Convert.ToString(uptimeValue);
            }

            if (report.Entries.TryGetValue("MemoryCheck", out var memory)
                && memory.Data.TryGetValue("AllocatedBytes", out var allocatedValue))
            {
                _memory = FormatUtils.FormatBytes(ToInt64(allocatedValue), 0, Units);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load health data.");
            Snackbar.Add(L["Dashboard.Error.Health", ex.Message], Severity.Error);
        }
        finally
        {
            _loadingStats = false;
        }
    }

    protected async Task LoadLogs()
    {
        _loadingLogs = true;
        try
        {
            _logsDirectory = GetLogsDirectory();

            if (string.IsNullOrWhiteSpace(_logsDirectory))
            {
                return;
            }

            var files = Directory.GetFiles(_logsDirectory);
            _logFiles.Clear();
            foreach (var file in files.OrderByDescending(File.GetLastWriteTimeUtc))
            {
                var info = new FileInfo(file);
                _logFiles.Add(new LogFileEntry(_logFiles.Count + 1, info.Name, info.Length, info.LastWriteTimeUtc));
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load log files.");
            Snackbar.Add(L["Dashboard.Error.Logs", ex.Message], Severity.Error);
        }
        finally
        {
            _loadingLogs = false;
        }
    }

    private string? GetLogsDirectory()
    {
        var logsFolder = DataPaths.Logs;
        if (Directory.Exists(logsFolder))
        {
            return logsFolder;
        }

        return null;
    }

    private async Task ViewLog(LogFileEntry logFile)
    {
        if (string.IsNullOrWhiteSpace(_logsDirectory))
        {
            Snackbar.Add(L["Dashboard.Logs.FolderNotFound"], Severity.Error);
            return;
        }

        var filePath = Path.Combine(_logsDirectory, logFile.Name);
        if (!File.Exists(filePath))
        {
            Snackbar.Add(L["Dashboard.Logs.FileNotFound"], Severity.Error);
            await LoadLogs();
            return;
        }

        var parameters = new DialogParameters<LogViewerDialog>
        {
            { x => x.FilePath, filePath }
        };

        var options = new DialogOptions
        {
            CloseButton = true,
            MaxWidth = MaxWidth.ExtraLarge,
            FullWidth = true
        };

        _ = await DialogService.ShowAsync<LogViewerDialog>(logFile.Name, parameters, options);
    }

    private async Task DownloadLog(LogFileEntry logFile)
    {
        if (string.IsNullOrWhiteSpace(_logsDirectory))
        {
            Snackbar.Add(L["Dashboard.Logs.FolderNotFound"], Severity.Error);
            return;
        }

        var filePath = Path.Combine(_logsDirectory, logFile.Name);
        if (!File.Exists(filePath))
        {
            Snackbar.Add(L["Dashboard.Logs.FileNotFound"], Severity.Error);
            await LoadLogs();
            return;
        }

        await using var reader = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var streamRef = new DotNetStreamReference(stream: reader);

        await JSRuntime.InvokeVoidAsync("downloadFileFromStream", logFile.Name, streamRef);
    }

    private async Task ConfirmShutdown()
    {
        var dialog = await DialogService.ShowMessageBoxAsync(
            L["Dashboard.Shutdown.Title"],
            L["Dashboard.Shutdown.Confirm"],
            yesText: L["Dashboard.Shutdown.Yes"],
            noText: L["Common.Cancel"],
            options: new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small });

        if (dialog.HasValue && dialog.Value)
        {
            await Shutdown();
        }
    }

    private async Task Shutdown()
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        try
        {
            Snackbar.Add(L["Dashboard.Shutdown.Stopping"], Severity.Warning);
            Logger.LogWarning("Shutdown from Dashboard.");
            ApplicationLifetime.StopApplication();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to shut down application.");
            Snackbar.Add(L["Dashboard.Shutdown.Error", ex.Message], Severity.Error);
            _shuttingDown = false;
        }
    }

    private static long ToInt64(object? value)
    {
        return value switch
        {
            null => 0,
            long longValue => longValue,
            int intValue => intValue,
            _ => Convert.ToInt64(value)
        };
    }

    private sealed record LogFileEntry(int Number, string Name, long Size, DateTime LastModified);
}
