using BotFarm.Core.Abstractions;
using BotFarm.Pages;
using BotFarm.TestKit;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;

namespace BotFarm.UnitTests.Pages;

/// <summary>
/// Renders the Dashboard page and checks how the Application card shows memory use against the warning threshold.
/// </summary>
[TestFixture]
public class DashboardMemoryRenderTests : AdminUiTestFixture
{
    private const long Mb = 1024L * 1024L;

    private BunitContext _context = default!;
    private HealthCheckService _healthChecks = default!;

    [SetUp]
    public void SetUp()
    {
        _context = new BunitContext();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddAdminLocalizationForTests();
        _context.Services.AddMudServices();
        _context.Services.AddLogging();

        _healthChecks = Substitute.For<HealthCheckService>();
        var dataPaths = Substitute.For<IAppDataPaths>();
        dataPaths.Logs.Returns(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var controlService = Substitute.For<IBotControlService>();
        controlService.GetStatuses().Returns([]);

        _context.Services.AddSingleton(_healthChecks);
        _context.Services.AddSingleton(Substitute.For<IHostApplicationLifetime>());
        _context.Services.AddSingleton(Substitute.For<ILogger<Dashboard>>());
        _context.Services.AddSingleton(dataPaths);
        _context.Services.AddSingleton(controlService);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.DisposeAsync();
    }

    [TestCase(100, "25%", "mud-progress-linear-color-success")]
    [TestCase(300, "75%", "mud-progress-linear-color-warning")]
    [TestCase(400, "100%", "mud-progress-linear-color-error")]
    public void MemoryCard_ShowsUsageWithGaugeAndShareOfThreshold(long workingSetMb, string percent, string colorClass)
    {
        ReturnMemory(new()
        {
            ["WorkingSetBytes"] = workingSetMb * Mb,
            ["ThresholdBytes"] = 400 * Mb,
            ["GCHeapSizeBytes"] = 50 * Mb,
        });

        var cut = _context.Render<Dashboard>();

        var meter = cut.Find(".stat-meter");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(meter.QuerySelector(".stat-value")!.TextContent, Is.EqualTo($"{workingSetMb} MiB"));
            Assert.That(meter.TextContent, Does.Contain($"{percent} of the 400 MiB warning threshold"));
            Assert.That(meter.QuerySelector(".mud-progress-linear")!.ClassList, Does.Contain(colorClass));
            Assert.That(cut.Markup, Does.Not.Contain("GC heap"));
        }
    }

    [Test]
    public void MemoryCard_WithoutThreshold_ShowsUsageWithoutGauge()
    {
        ReturnMemory(new() { ["WorkingSetBytes"] = 120 * Mb });

        var cut = _context.Render<Dashboard>();

        var meter = cut.Find(".stat-meter");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(meter.QuerySelector(".stat-value")!.TextContent, Is.EqualTo("120 MiB"));
            Assert.That(meter.TextContent, Does.Not.Contain("threshold"));
            Assert.That(meter.QuerySelector(".mud-progress-linear"), Is.Null);
        }
    }

    private void ReturnMemory(Dictionary<string, object> data)
    {
        var entry = new HealthReportEntry(HealthStatus.Healthy, null, TimeSpan.Zero, null, data);
        _healthChecks.CheckHealthAsync(Arg.Any<Func<HealthCheckRegistration, bool>?>(), Arg.Any<CancellationToken>())
            .Returns(new HealthReport(new Dictionary<string, HealthReportEntry> { ["MemoryCheck"] = entry }, TimeSpan.Zero));
    }
}
