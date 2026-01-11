using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MudBlazor;

namespace BotFarm.Pages;

public partial class Dashboard
{
    private bool _loadingStats;
    private bool _shuttingDown;
    private string? _memory;
    private string? _uptime;

    [Inject] private HealthCheckService HealthChecks { get; set; } = default!;
    [Inject] private IHostApplicationLifetime ApplicationLifetime { get; set; } = default!;
    [Inject] private ILogger<Dashboard> Logger { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private IDialogService DialogService { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        await LoadHealthAsync();
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
                _memory = FormatBytes(ToInt64(allocatedValue));
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

    private static string FormatBytes(long bytes, int decimals = 2)
    {
        if (bytes == 0)
        {
            return "0 Bytes";
        }

        const double k = 1024d;
        var dm = decimals < 0 ? 0 : decimals;
        string[] sizes = ["Bytes", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB", "ZiB", "YiB"];

        var i = (int)Math.Floor(Math.Log(bytes) / Math.Log(k));
        return $"{Math.Round(bytes / Math.Pow(k, i), dm)} {sizes[i]}";
    }
}
