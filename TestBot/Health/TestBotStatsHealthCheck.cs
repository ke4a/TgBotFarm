using Microsoft.Extensions.Diagnostics.HealthChecks;
using TestBot.Abstractions;

namespace TestBot.Health;

internal class TestBotStatsHealthCheck : IHealthCheck
{
    private readonly ITestBotDatabaseService _dbService;

    public TestBotStatsHealthCheck(ITestBotDatabaseService dbService)
    {
        _dbService = dbService;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        long result;

        try
        {
            result = await _dbService.GetChatCount();
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(
                HealthStatus.Unhealthy,
                description: $"Reports {Constants.Name} stats.",
                exception: ex,
                data: null);
        }

        var data = new Dictionary<string, object>()
        {
            { "ChatsCount", result },
        };

        return new HealthCheckResult(
            HealthStatus.Healthy,
            description: $"Reports {Constants.Name} stats.",
            exception: null,
            data: data);
    }
}
