using AngleSharp.Dom;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Pages;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using NSubstitute;

namespace BotFarm.UnitTests.Pages;

/// <summary>
/// Renders the Dashboard page and checks what the Bot controls card shows and does for each runtime state.
/// </summary>
[TestFixture]
public class DashboardBotControlRenderTests
{
    private BunitContext _context = default!;
    private IBotControlService _controlService = default!;

    [SetUp]
    public void SetUp()
    {
        _context = new BunitContext();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddMudServices();
        _context.Services.AddLogging();

        var healthChecks = Substitute.For<HealthCheckService>();
        healthChecks.CheckHealthAsync(Arg.Any<Func<HealthCheckRegistration, bool>?>(), Arg.Any<CancellationToken>())
            .Returns(new HealthReport(new Dictionary<string, HealthReportEntry>(), TimeSpan.Zero));
        var dataPaths = Substitute.For<IAppDataPaths>();
        dataPaths.Logs.Returns(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        _controlService = Substitute.For<IBotControlService>();

        _context.Services.AddSingleton(healthChecks);
        _context.Services.AddSingleton(Substitute.For<IHostApplicationLifetime>());
        _context.Services.AddSingleton(Substitute.For<ILogger<Dashboard>>());
        _context.Services.AddSingleton(dataPaths);
        _context.Services.AddSingleton(_controlService);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.DisposeAsync();
    }

    [Test]
    public void AppliedBot_ShowsEnabledSwitchWithoutProblem()
    {
        _controlService.GetStatuses().Returns(
            [new BotRuntimeStatus("TestBot", true, true, BotRuntimeState.Applied, DateTime.UtcNow, null)]);

        var cut = _context.Render<Dashboard>();

        var bot = BotBlock(cut, "TestBot");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Switch(bot).HasAttribute("checked"), Is.True);
            Assert.That(Switch(bot).HasAttribute("disabled"), Is.False);
            Assert.That(bot.QuerySelector(".mud-alert"), Is.Null);
            Assert.That(bot.TextContent, Does.Not.Contain("Applying..."));
        }
    }

    [Test]
    public void PendingBot_ShowsApplyingAndDisablesSwitch()
    {
        _controlService.GetStatuses().Returns(
            [new BotRuntimeStatus("TestBot", true, false, BotRuntimeState.Pending, DateTime.UtcNow, null)]);

        var cut = _context.Render<Dashboard>();

        var bot = BotBlock(cut, "TestBot");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bot.TextContent, Does.Contain("Applying..."));
            Assert.That(Switch(bot).HasAttribute("disabled"), Is.True);
            Assert.That(bot.QuerySelector(".mud-alert"), Is.Null);
        }
    }

    [Test]
    public void ErrorBot_ShowsRetryWhichSubmitsRetry()
    {
        var error = new BotRuntimeStatus("TestBot", true, false, BotRuntimeState.Error, DateTime.UtcNow, null);
        var applied = new BotRuntimeStatus("TestBot", true, true, BotRuntimeState.Applied, DateTime.UtcNow, null);
        var statuses = new List<BotRuntimeStatus> { error };
        _controlService.GetStatuses().Returns(_ => statuses.ToList());
        _controlService.RetryAsync("TestBot", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            statuses[0] = applied;
            return applied;
        });

        var cut = _context.Render<Dashboard>();

        var bot = BotBlock(cut, "TestBot");
        Assert.That(bot.QuerySelector(".mud-alert")!.TextContent, Does.Contain("could not be applied"));
        Assert.That(Switch(bot).HasAttribute("disabled"), Is.True);

        RetryButton(bot).Click();

        cut.WaitForAssertion(() => Assert.That(BotBlock(cut, "TestBot").QuerySelector(".mud-alert"), Is.Null));
        _controlService.Received(1).RetryAsync("TestBot", Arg.Any<CancellationToken>());
        Assert.That(Switch(BotBlock(cut, "TestBot")).HasAttribute("disabled"), Is.False);
    }

    [Test]
    public void UnknownBotWithRequestedState_ShowsUnconfirmedMessageAndRetry()
    {
        _controlService.GetStatuses().Returns(
            [new BotRuntimeStatus("TestBot", true, false, BotRuntimeState.Unknown, DateTime.UtcNow, null)]);

        var cut = _context.Render<Dashboard>();

        var bot = BotBlock(cut, "TestBot");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bot.QuerySelector(".mud-alert")!.TextContent, Does.Contain("requested state is not confirmed"));
            Assert.That(RetryButton(bot), Is.Not.Null);
            Assert.That(Switch(bot).HasAttribute("disabled"), Is.True);
        }
    }

    [Test]
    public void ToggleSwitch_ShowsApplyingWhileRequestRunsThenFinalState()
    {
        var applied = new BotRuntimeStatus("TestBot", true, true, BotRuntimeState.Applied, DateTime.UtcNow, null);
        var statuses = new List<BotRuntimeStatus> { applied };
        _controlService.GetStatuses().Returns(_ => statuses.ToList());
        var response = new TaskCompletionSource<BotRuntimeStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        _controlService.SetEnabledAsync("TestBot", false, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            statuses[0] = applied with { DesiredEnabled = false, Status = BotRuntimeState.Pending };
            return response.Task;
        });

        var cut = _context.Render<Dashboard>();
        Switch(BotBlock(cut, "TestBot")).Change(false);

        cut.WaitForAssertion(() =>
        {
            var bot = BotBlock(cut, "TestBot");
            Assert.That(bot.TextContent, Does.Contain("Applying..."));
            Assert.That(Switch(bot).HasAttribute("disabled"), Is.True);
            Assert.That(Switch(bot).HasAttribute("checked"), Is.False);
        });

        var disabled = applied with { DesiredEnabled = false, AppliedEnabled = false };
        statuses[0] = disabled;
        response.SetResult(disabled);

        cut.WaitForAssertion(() =>
        {
            var bot = BotBlock(cut, "TestBot");
            Assert.That(bot.TextContent, Does.Not.Contain("Applying..."));
            Assert.That(Switch(bot).HasAttribute("disabled"), Is.False);
            Assert.That(Switch(bot).HasAttribute("checked"), Is.False);
        });
        _controlService.Received(1).SetEnabledAsync("TestBot", false, Arg.Any<CancellationToken>());
    }

    [Test]
    public void ToggleSwitch_WhenRequestFails_ShowsRetryableError()
    {
        var applied = new BotRuntimeStatus("TestBot", false, false, BotRuntimeState.Applied, DateTime.UtcNow, null);
        var statuses = new List<BotRuntimeStatus> { applied };
        _controlService.GetStatuses().Returns(_ => statuses.ToList());
        _controlService.SetEnabledAsync("TestBot", true, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            statuses[0] = applied with { DesiredEnabled = true, Status = BotRuntimeState.Unknown };
            return Task.FromException<BotRuntimeStatus>(new InvalidOperationException("provider detail"));
        });

        var cut = _context.Render<Dashboard>();
        Switch(BotBlock(cut, "TestBot")).Change(true);

        cut.WaitForAssertion(() =>
        {
            var alert = BotBlock(cut, "TestBot").QuerySelector(".mud-alert");
            Assert.That(alert, Is.Not.Null);
            Assert.That(alert!.TextContent, Does.Contain("could not be saved"));
            Assert.That(alert.TextContent, Does.Not.Contain("provider detail"));
        });
        Assert.That(RetryButton(BotBlock(cut, "TestBot")), Is.Not.Null);
    }

    [Test]
    public void NoBots_ShowsEmptyMessage()
    {
        _controlService.GetStatuses().Returns([]);

        var cut = _context.Render<Dashboard>();

        Assert.That(cut.Markup, Does.Contain("No registered bots."));
    }

    private static IElement BotBlock(IRenderedComponent<Dashboard> cut, string botName) =>
        cut.FindAll(".py-2")
            .Single(block => block.QuerySelector("p")?.TextContent.Trim() == botName);

    private static IElement Switch(IElement bot) =>
        bot.QuerySelector("input[role=switch]")!;

    private static IElement RetryButton(IElement bot) =>
        bot.QuerySelectorAll(".mud-alert button").Single(button => button.TextContent.Trim() == "Retry");
}
