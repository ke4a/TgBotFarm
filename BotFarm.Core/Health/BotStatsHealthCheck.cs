using BotFarm.Core.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BotFarm.Core.Health;

/// <summary>
/// Reports whether a bot's chat statistics can be read from the database.
/// </summary>
public sealed class BotStatsHealthCheck : IHealthCheck
{
    private readonly IDatabaseService _databaseService;
    private readonly string _botName;

    /// <summary>
    /// Initializes a new instance of the <see cref="BotStatsHealthCheck"/> class.
    /// </summary>
    /// <param name="databaseService">The database service to query.</param>
    /// <param name="botName">The name to include in the health-check description.</param>
    public BotStatsHealthCheck(IDatabaseService databaseService, string botName)
    {
        _databaseService = databaseService;
        _botName = botName;
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        long chatCount;

        try
        {
            chatCount = await _databaseService.GetChatCount();
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(
                HealthStatus.Unhealthy,
                description: $"Reports {_botName} stats.",
                exception: ex,
                data: null);
        }

        var data = new Dictionary<string, object>()
        {
            { "ChatsCount", chatCount },
        };

        return new HealthCheckResult(
            HealthStatus.Healthy,
            description: $"Reports {_botName} stats.",
            exception: null,
            data: data);
    }
}
