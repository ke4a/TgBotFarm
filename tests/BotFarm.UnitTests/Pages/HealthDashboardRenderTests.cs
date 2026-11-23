using BotFarm.Core.Abstractions;
using BotFarm.Pages;
using BotFarm.TestKit;
using Bunit;
using Bunit.Rendering;
using Microsoft.AspNetCore.Components.Sections;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MudBlazor.Services;
using NSubstitute;

namespace BotFarm.UnitTests.Pages;

/// <summary>
/// Renders the health page and checks how it lists health-check results and links to the memory chart.
/// </summary>
[TestFixture]
public class HealthDashboardRenderTests : AdminUiTestFixture
{
    private const string MemoryPanelUrl = "http://localhost:3000/d/botfarm/botfarm?viewPanel=panel-5";

    private BunitContext _context = default!;
    private HealthCheckService _healthChecks = default!;
    private IMetricsLinks _metricsLinks = default!;

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
        _healthChecks.CheckHealthAsync(Arg.Any<Func<HealthCheckRegistration, bool>?>(), Arg.Any<CancellationToken>())
            .Returns(new HealthReport(new Dictionary<string, HealthReportEntry>(), TimeSpan.Zero));

        _metricsLinks = Substitute.For<IMetricsLinks>();
        _metricsLinks.ForAppMemory().Returns((string?)null);
        _context.Services.AddSingleton(_metricsLinks);
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
                new Dictionary<string, object> { ["WorkingSetBytes"] = 1024L }),
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
            Assert.That(rows[0].TextContent, Does.Contain("MemoryCheck").And.Contain("Healthy").And.Contain("WorkingSetBytes:"));
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

    [Test]
    public void Render_WithDashboardAddress_ShowsTheMemoryMetricsLinkInTheAppBarActions()
    {
        _metricsLinks.ForAppMemory().Returns(MemoryPanelUrl);

        var cut = RenderWithActionsOutlet();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cut.FindAll("a.metrics-link"), Has.Count.EqualTo(1));
            Assert.That(cut.Find("#actions a.metrics-link").GetAttribute("href"), Is.EqualTo(MemoryPanelUrl));
            Assert.That(cut.FindAll(".card-header a.metrics-link"), Is.Empty);
        }
    }

    [Test]
    public void Render_WithoutDashboardAddress_ShowsNoMetricsLink()
    {
        var cut = RenderWithActionsOutlet();

        Assert.That(cut.FindAll("a.metrics-link"), Is.Empty);
    }

    // The page writes its app bar actions through <SectionContent>, which renders only where a SectionOutlet exists,
    // as it does in MainLayout.
    private IRenderedComponent<ContainerFragment> RenderWithActionsOutlet() =>
        _context.Render(builder =>
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "id", "actions");
            builder.OpenComponent<SectionOutlet>(2);
            builder.AddAttribute(3, nameof(SectionOutlet.SectionName), "ActionsSection");
            builder.CloseComponent();
            builder.CloseElement();
            builder.OpenComponent<HealthDashboard>(4);
            builder.CloseComponent();
        });
}
