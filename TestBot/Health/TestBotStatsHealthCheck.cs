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

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<long> result;

        try
        {
            result = Task.Run(_dbService.GetAllChatIds).Result;
        }
        catch (Exception ex)
        {
            return Task.FromResult(new HealthCheckResult(
                HealthStatus.Unhealthy,
                description: $"Reports {Constants.Name} stats.",
                exception: ex,
                data: null));
        }

        var data = new Dictionary<string, object>()
        {
            { "ChatsCount", result.Count() },
        };

        return Task.FromResult(new HealthCheckResult(
            HealthStatus.Healthy,
            description: $"Reports {Constants.Name} stats.",
            exception: null,
            data: data));
    }
}
