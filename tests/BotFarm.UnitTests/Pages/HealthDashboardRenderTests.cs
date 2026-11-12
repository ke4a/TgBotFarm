using BotFarm.Pages;
using BotFarm.TestKit;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MudBlazor.Services;
using NSubstitute;

namespace BotFarm.UnitTests.Pages;

/// <summary>
/// Renders the health page and checks how it lists health-check results.
/// </summary>
[TestFixture]
public class HealthDashboardRenderTests : AdminUiTestFixture
{
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
        _context.Services.AddSingleton(_healthChecks);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.DisposeAsync();
    }

    [Test]
    public void Render_ListsEveryCheckWithStatusDataAndError()
    {
        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["MemoryCheck"] = new(HealthStatus.Healthy, "Reports memory.", TimeSpan.FromMilliseconds(12), null,
                new Dictionary<string, object> { ["AllocatedBytes"] = 1024L }),
            ["TestBot Stats"] = new(HealthStatus.Unhealthy, "Reports TestBot stats.", TimeSpan.FromMilliseconds(3),
                new InvalidOperationException("database offline"), new Dictionary<string, object>())
        };
        _healthChecks.CheckHealthAsync(Arg.Any<Func<HealthCheckRegistration, bool>?>(), Arg.Any<CancellationToken>())
            .Returns(new HealthReport(entries, TimeSpan.FromMilliseconds(15)));

        var cut = _context.Render<HealthDashboard>();

        var rows = cut.FindAll(".health-table tbody tr");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cut.Find(".health-overall").TextContent, Is.EqualTo("Unhealthy"));
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows[0].TextContent, Does.Contain("MemoryCheck").And.Contain("Healthy").And.Contain("AllocatedBytes:"));
            Assert.That(rows[1].TextContent, Does.Contain("TestBot Stats").And.Contain("Unhealthy").And.Contain("database offline"));
        }
    }

    [Test]
    public void Refresh_RunsChecksAgain()
    {
        _healthChecks.CheckHealthAsync(Arg.Any<Func<HealthCheckRegistration, bool>?>(), Arg.Any<CancellationToken>())
            .Returns(new HealthReport(new Dictionary<string, HealthReportEntry>(), TimeSpan.Zero));

        var cut = _context.Render<HealthDashboard>();
        cut.Find("button[aria-label='Run health checks again']").Click();

        cut.WaitForNUnitAssertion(() => _healthChecks.Received(2)
            .CheckHealthAsync(Arg.Any<Func<HealthCheckRegistration, bool>?>(), Arg.Any<CancellationToken>()));
    }
}
