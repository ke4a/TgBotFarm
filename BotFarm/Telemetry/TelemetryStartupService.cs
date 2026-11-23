using BotFarm.Core.Abstractions;
using BotFarm.Core.Telemetry;

namespace BotFarm.Telemetry;

/// <summary>
/// Starts every registered bot's update, outcome, command and event counts at zero when the app starts, so an idle bot
/// reports zero rather than no data and the first update after startup is counted. It also creates the
/// <see cref="AppMemoryMetrics"/> gauges, so app memory is reported from the start.
/// </summary>
/// <remarks>
/// <para>
/// Registered only when metrics export is on. Every registered bot is covered, enabled or not, with the command
/// keys from its <see cref="IUpdateService.CommandKeys"/>, resolved through the bot's keyed registration in a scope
/// of its own.
/// </para>
/// <para>
/// It is registered after the OpenTelemetry services, whose hosted service starts the meter provider, so the zeros
/// are recorded while the provider listens. A bot whose update service cannot be resolved is logged and skipped.
/// </para>
/// </remarks>
internal sealed class TelemetryStartupService : IHostedService
{
    private readonly IEnumerable<IBotModule> _botModules;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly UpdateMetrics _metrics;
    private readonly BotEventMetrics _eventMetrics;
    private readonly ILogger<TelemetryStartupService> _logger;

    /// <param name="botModules">The registered bots.</param>
    /// <param name="scopeFactory">Creates the scope each bot's update service is resolved in.</param>
    /// <param name="metrics">Records the update, outcome and command zeros.</param>
    /// <param name="eventMetrics">Records the event zeros.</param>
    /// <param name="memoryMetrics">
    /// The memory gauges; taking them creates them with this service, so they exist once the host has started.
    /// </param>
    /// <param name="logger">Receives a warning for each bot whose commands could not be read.</param>
    public TelemetryStartupService(
        IEnumerable<IBotModule> botModules,
        IServiceScopeFactory scopeFactory,
        UpdateMetrics metrics,
        BotEventMetrics eventMetrics,
        AppMemoryMetrics memoryMetrics,
        ILogger<TelemetryStartupService> logger)
    {
        _botModules = botModules;
        _scopeFactory = scopeFactory;
        _metrics = metrics;
        _eventMetrics = eventMetrics;
        _logger = logger;
    }

    /// <summary>
    /// Records the zeros of every registered bot.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var botModule in _botModules)
        {
            _eventMetrics.RecordZeros(botModule.Name);

            IReadOnlyCollection<string> commandKeys;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                commandKeys = scope.ServiceProvider.GetRequiredKeyedService<IUpdateService>(botModule.Name).CommandKeys;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not read the commands of bot {BotName}; its metrics start when it first handles an update.",
                    botModule.Name);
                continue;
            }

            _metrics.RecordZeros(botModule.Name, commandKeys);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
