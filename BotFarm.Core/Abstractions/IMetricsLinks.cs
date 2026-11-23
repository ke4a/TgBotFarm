namespace BotFarm.Core.Abstractions;

/// <summary>
/// Builds the admin UI's links to the monitoring dashboard that charts the app's metrics.
/// </summary>
public interface IMetricsLinks
{
    /// <summary>
    /// The monitoring dashboard filtered to the bot named <paramref name="botName"/>, or null when no dashboard
    /// address is configured.
    /// </summary>
    string? ForBot(string botName);

    /// <summary>
    /// The monitoring dashboard's app memory panel, or null when no dashboard address is configured.
    /// </summary>
    string? ForAppMemory();
}
