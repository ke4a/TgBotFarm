using System.Diagnostics.Metrics;
using BotFarm.Core.Telemetry;
using BotFarm.Extensions;
using BotFarm.Health;
using Microsoft.Extensions.Options;

namespace BotFarm.Telemetry;

/// <summary>
/// Reports the app's working set, GC heap size and memory warning threshold, in bytes, on the meter
/// <see cref="UpdateMetrics.MeterName"/>. They come from the same sources as <see cref="MemoryHealthCheck"/>, so the
/// chart matches the health check.
/// </summary>
/// <remarks>
/// The gauges are read only when the metric reader collects. The threshold is read from the
/// <see cref="HealthCheckExtensions.MemoryCheckName"/> options on every collection, so a changed setting shows from
/// the next collection on. The meter belongs to the <see cref="IMeterFactory"/>, which disposes it.
/// </remarks>
internal sealed class AppMemoryMetrics
{
    /// <summary>
    /// Creates the gauges through <paramref name="meterFactory"/>.
    /// </summary>
    /// <param name="meterFactory">Creates the <see cref="UpdateMetrics.MeterName"/> meter.</param>
    /// <param name="memoryCheckOptions">Holds the threshold of the memory health check.</param>
    public AppMemoryMetrics(IMeterFactory meterFactory, IOptionsMonitor<MemoryCheckOptions> memoryCheckOptions)
    {
        var meter = meterFactory.Create(UpdateMetrics.MeterName);
        meter.CreateObservableGauge(
            "botfarm.memory.working_set",
            () => Environment.WorkingSet,
            "By",
            "Physical memory used by the process.");
        meter.CreateObservableGauge(
            "botfarm.memory.gc_heap",
            () => GC.GetGCMemoryInfo().HeapSizeBytes,
            "By",
            "Size of the managed heap, measured at the last garbage collection.");
        meter.CreateObservableGauge(
            "botfarm.memory.threshold",
            () => memoryCheckOptions.Get(HealthCheckExtensions.MemoryCheckName).Threshold,
            "By",
            "Working set at which the memory health check fails.");
    }
}
