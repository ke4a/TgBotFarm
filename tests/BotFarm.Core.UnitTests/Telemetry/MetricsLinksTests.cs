using BotFarm.Core.Telemetry;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BotFarm.Core.UnitTests.Telemetry;

[TestFixture]
public class MetricsLinksTests
{
    private const string DashboardUrl = "http://localhost:3000/d/botfarm/botfarm";

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void NoDashboardUrl_GivesNoLinks(string? dashboardUrl)
    {
        var links = Create(dashboardUrl);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(links.ForBot("TestBot"), Is.Null);
            Assert.That(links.ForAppMemory(), Is.Null);
        }
    }

    [Test]
    public void ForBot_AddsTheBotVariable()
    {
        Assert.That(Create(DashboardUrl).ForBot("TestBot"), Is.EqualTo($"{DashboardUrl}?var-bot=TestBot"));
    }

    [Test]
    public void ForBot_EncodesTheBotName()
    {
        Assert.That(Create(DashboardUrl).ForBot("Test Bot&1"), Is.EqualTo($"{DashboardUrl}?var-bot=Test%20Bot%261"));
    }

    [Test]
    public void ForBot_KeepsAnExistingQueryString()
    {
        Assert.That(Create($"{DashboardUrl}?orgId=1").ForBot("TestBot"),
            Is.EqualTo($"{DashboardUrl}?orgId=1&var-bot=TestBot"));
    }

    [Test]
    public void ForAppMemory_AddsTheMemoryPanel()
    {
        Assert.That(Create(DashboardUrl).ForAppMemory(), Is.EqualTo($"{DashboardUrl}?viewPanel=panel-5"));
    }

    [Test]
    public void ForAppMemory_KeepsAnExistingQueryString()
    {
        Assert.That(Create($"{DashboardUrl}?orgId=1").ForAppMemory(),
            Is.EqualTo($"{DashboardUrl}?orgId=1&viewPanel=panel-5"));
    }

    [Test]
    public void ForBot_FollowsAChangedDashboardUrl()
    {
        var options = new TelemetryOptions { DashboardUrl = DashboardUrl };
        var monitor = Substitute.For<IOptionsMonitor<TelemetryOptions>>();
        monitor.CurrentValue.Returns(_ => options);
        var links = new MetricsLinks(monitor);

        options = new TelemetryOptions();

        Assert.That(links.ForBot("TestBot"), Is.Null);
    }

    private static MetricsLinks Create(string? dashboardUrl)
    {
        var monitor = Substitute.For<IOptionsMonitor<TelemetryOptions>>();
        monitor.CurrentValue.Returns(new TelemetryOptions { DashboardUrl = dashboardUrl });
        return new MetricsLinks(monitor);
    }
}
