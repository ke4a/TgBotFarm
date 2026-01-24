using BotFarm.Components;
using BotFarm.Shared.Utilities;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.JSInterop;
using MudBlazor;

namespace BotFarm.Pages;

public partial class Dashboard
{
    private bool _loadingStats;
    private bool _loadingLogs;
    private bool _shuttingDown;
    private string? _memory;
    private string? _uptime;
    private string? _logsDirectory;
    private readonly List<LogFileEntry> _logFiles = [];

    [Inject] private HealthCheckService HealthChecks { get; set; } = default!;
    [Inject] private IHostApplicationLifetime ApplicationLifetime { get; set; } = default!;
    [Inject] private ILogger<Dashboard> Logger { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;
    [Inject] private IJSRuntime JSRuntime { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await Task.WhenAll(LoadHealthAsync(), LoadLogsAsync());
    }

    private async Task LoadHealthAsync()
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
                _memory = FormatUtils.FormatBytes(ToInt64(allocatedValue), 0);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load health data.");
            Snackbar.Add($"Failed to load health data: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loadingStats = false;
        }
    }

    private async Task LoadLogsAsync()
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
                _logFiles.Add(new LogFileEntry(info.Name, info.Length, info.LastWriteTimeUtc));
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to load log files.");
            Snackbar.Add($"Failed to load log files: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loadingLogs = false;
        }
    }

    private static string? GetLogsDirectory()
    {
        var logsFolder = Path.Combine(AppContext.BaseDirectory, "logs");
        if (Directory.Exists(logsFolder))
        {
            return logsFolder;
        }

        return null;
    }

    private async Task ViewLogAsync(LogFileEntry logFile)
    {
        if (string.IsNullOrWhiteSpace(_logsDirectory))
        {
            Snackbar.Add("Logs folder not found.", Severity.Error);
            return;
        }

        var filePath = Path.Combine(_logsDirectory, logFile.Name);
        if (!File.Exists(filePath))
        {
            Snackbar.Add("Log file not found.", Severity.Error);
            await LoadLogsAsync();
            return;
        }

        try
        {
            string content;
            await using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                content = await reader.ReadToEndAsync();
            }

            var parameters = new DialogParameters
            {
                ["Content"] = string.IsNullOrWhiteSpace(content) ? "(File is empty)" : content
            };

            var options = new DialogOptions
            {
                CloseButton = true,
                MaxWidth = MaxWidth.ExtraLarge,
                FullWidth = true
            };

            _ = await DialogService.ShowAsync<LogViewerDialog>($"{logFile.Name}", parameters, options);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to view log file {LogFile}.", logFile.Name);
            Snackbar.Add($"Failed to view log file: {ex.Message}", Severity.Error);
        }
    }

    private async Task DownloadLogAsync(LogFileEntry logFile)
    {
        if (string.IsNullOrWhiteSpace(_logsDirectory))
        {
            Snackbar.Add("Logs folder not found.", Severity.Error);
            return;
        }

        var filePath = Path.Combine(_logsDirectory, logFile.Name);
        if (!File.Exists(filePath))
        {
            Snackbar.Add("Log file not found.", Severity.Error);
            await LoadLogsAsync();
            return;
        }

        await using var reader = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var streamRef = new DotNetStreamReference(stream: reader);

        await JSRuntime.InvokeVoidAsync("downloadFileFromStream", logFile.Name, streamRef);
    }

    private async Task ConfirmShutdown()
    {
        var dialog = await DialogService.ShowMessageBox(
            "Shut down application",
            "Are you sure you want to shut down the application?",
            yesText: "Shut down",
            noText: "Cancel",
            options: new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small });

        if (dialog.HasValue && dialog.Value)
        {
            await ShutdownAsync();
        }
    }

    private async Task ShutdownAsync()
    {
        if (_shuttingDown)
        {
            return;
        }

        _shuttingDown = true;
        try
        {
            Snackbar.Add("Application stopping...", Severity.Warning);
            Logger.LogWarning("Shutdown from Dashboard.");
            ApplicationLifetime.StopApplication();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to shut down application.");
            Snackbar.Add($"Failed to shut down: {ex.Message}", Severity.Error);
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

    private sealed record LogFileEntry(string Name, long Size, DateTime LastModified);
}
