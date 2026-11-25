using System.Globalization;
using BotFarm.Core.Abstractions;
using BotFarm.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace BotFarm.Pages;

/// <summary>
/// Code-behind for the health page, which runs every registered health check on demand, lists the results and links
/// to the monitoring dashboard's app memory panel.
/// </summary>
public partial class HealthDashboard
{
    /// <summary>
    /// The time zone times are shown in, cascaded by <see cref="MainLayout"/>.
    /// </summary>
    [CascadingParameter] private DisplayTimeZone DisplayTimeZone { get; set; } = DisplayTimeZone.ServerLocal;

    private readonly List<HealthEntry> _entries = [];
    private HealthReport? _report;
    private DateTime? _checkedAt;
    private bool _loading;

    [Inject] private HealthCheckService HealthChecks { get; set; } = default!;
    [Inject] private IMetricsLinks Links { get; set; } = default!;
    [Inject] private ILogger<HealthDashboard> Logger { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private IStringLocalizer<HealthDashboard> L { get; set; } = default!;

    protected override Task OnInitializedAsync() => LoadReport();

    private async Task LoadReport()
    {
        _loading = true;
        try
        {
            _report = await HealthChecks.CheckHealthAsync();
            _checkedAt = DateTime.UtcNow;
            _entries.Clear();
            _entries.AddRange(_report.Entries
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new HealthEntry(entry.Key, entry.Value)));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to run health checks.");
            Snackbar.Add(L["Health.Error", ex.Message], Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private string StatusText(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => L["Health.Status.Healthy"],
        HealthStatus.Degraded => L["Health.Status.Degraded"],
        _ => L["Health.Status.Unhealthy"]
    };

    private static Color StatusColor(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => Color.Success,
        HealthStatus.Degraded => Color.Warning,
        _ => Color.Error
    };

    private static string FormatValue(object? value) =>
        Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>
    /// One row of the health table: a check's registered name and its latest result.
    /// </summary>
    private sealed record HealthEntry(string Name, HealthReportEntry Entry);
}
