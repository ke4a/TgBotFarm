using BotFarm.Core.Abstractions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace BotFarm.Core.Telemetry;

/// <inheritdoc cref="IMetricsLinks"/>
/// <remarks>
/// Adds Grafana's URL parameters to <see cref="TelemetryOptions.DashboardUrl"/>: <c>var-bot</c> to select the bot
/// and <c>viewPanel=panel-{id}</c> to open one panel. Query parameters already in the address are kept. The
/// address is read on every call, so a changed setting applies without a restart.
/// </remarks>
internal sealed class MetricsLinks : IMetricsLinks
{
    private const string BotVariable = "var-bot";
    private const string ViewPanelParameter = "viewPanel";

    private readonly IOptionsMonitor<TelemetryOptions> _options;

    public MetricsLinks(IOptionsMonitor<TelemetryOptions> options)
    {
        _options = options;
    }

    public string? ForBot(string botName) => WithParameter(BotVariable, botName);

    public string? ForAppMemory() => WithParameter(ViewPanelParameter, $"panel-{Constants.Telemetry.MemoryPanelId}");

    private string? WithParameter(string name, string value)
    {
        var dashboardUrl = _options.CurrentValue.DashboardUrl;
        return string.IsNullOrWhiteSpace(dashboardUrl)
            ? null
            : QueryHelpers.AddQueryString(dashboardUrl.Trim(), name, value);
    }
}
