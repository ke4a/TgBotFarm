using System.Diagnostics.Metrics;
using BotFarm.Core.Telemetry;
using Microsoft.Extensions.DependencyInjection;

namespace BotFarm.TestKit;

/// <summary>
/// Metric services for tests that need one as a dependency but do not check what it records.
/// </summary>
public static class TestMetrics
{
    /// <summary>
    /// Creates a <see cref="BotEventMetrics"/> on a meter factory of its own, so nothing it records reaches other
    /// tests.
    /// </summary>
    public static BotEventMetrics BotEvents() =>
        new(new ServiceCollection().AddMetrics().BuildServiceProvider().GetRequiredService<IMeterFactory>());
}
