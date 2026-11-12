using AngleSharp.Dom;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Exceptions;
using BotFarm.Core.Models;
using BotFarm.Shared.Components;
using BotFarm.TestKit;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;
using Telegram.Bot.Types;

namespace BotFarm.Shared.UnitTests.Components;

/// <summary>
/// Renders <see cref="DashboardWebhook"/> and checks what it shows and when updates can be dropped.
/// </summary>
[TestFixture]
public class DashboardWebhookRenderTests : AdminUiTestFixture
{
    private const string TestBotName = "TestBot";

    private BunitContext _context = default!;
    private IBotControlService _controlService = default!;

    [SetUp]
    public void SetUp()
    {
        _context = new BunitContext();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddAdminLocalizationForTests();
        _context.Services.AddMudServices();
        _context.Services.AddLogging();
        _controlService = Substitute.For<IBotControlService>();
        _context.Services.AddSingleton(_controlService);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.DisposeAsync();
    }

    [Test]
    public void ShowsUrlPendingCountAndLastError()
    {
        var errorDate = new DateTime(2026, 11, 10, 8, 30, 0, DateTimeKind.Utc);
        GivenWebhook(new WebhookInfo
        {
            Url = "https://example.com/api/TestBot/update",
            PendingUpdateCount = 12,
            LastErrorMessage = "Connection timed out",
            LastErrorDate = errorDate
        });

        var cut = Render();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cut.Markup, Does.Contain("https://example.com/api/TestBot/update"));
            Assert.That(cut.Markup, Does.Contain("12"));
            Assert.That(cut.Markup, Does.Contain("Connection timed out"));
            Assert.That(cut.Markup, Does.Contain(errorDate.ToLocalTime().ToString("dd MMM yyyy HH:mm:ss")));
            Assert.That(DropButton(cut).HasAttribute("disabled"), Is.False);
        }
    }

    [Test]
    public void CopyUrl_CopiesFullUrl()
    {
        const string url = "https://a-rather-long-tunnel-subdomain.example.com/api/TestBot/update";
        GivenWebhook(new WebhookInfo { Url = url, PendingUpdateCount = 0 });
        var cut = Render();

        cut.Find("button[aria-label='Copy URL']").Click();

        var copied = _context.JSInterop.Invocations
            .Single(i => i.Identifier == "navigator.clipboard.writeText")
            .Arguments.Single();
        Assert.That(copied, Is.EqualTo(url));
    }

    [Test]
    public void WithoutWebhookOrPendingUpdates_ShowsNotSetAndDisablesDrop()
    {
        GivenWebhook(new WebhookInfo { Url = string.Empty, PendingUpdateCount = 0 });

        var cut = Render();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cut.Markup, Does.Contain("Not set"));
            Assert.That(cut.Markup, Does.Contain("None"));
            Assert.That(cut.Markup, Does.Not.Contain("Last error at"));
            Assert.That(cut.FindAll("button[aria-label='Copy URL']"), Is.Empty);
            Assert.That(DropButton(cut).HasAttribute("disabled"), Is.True);
        }
    }

    [Test]
    public void WhenInfoCannotBeLoaded_ShowsUnavailableAndDisablesDrop()
    {
        _controlService.GetWebhookInfoAsync(TestBotName, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<WebhookInfo>(new HttpRequestException("network")));

        var cut = Render();

        Assert.That(cut.Markup, Does.Contain("Webhook info is unavailable."));
        Assert.That(DropButton(cut).HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void Drop_AfterConfirmation_DropsAndReloads()
    {
        var infos = new Queue<WebhookInfo>(
        [
            new WebhookInfo { Url = "https://example.com/hook", PendingUpdateCount = 5 },
            new WebhookInfo { Url = "https://example.com/hook", PendingUpdateCount = 0 }
        ]);
        _controlService.GetWebhookInfoAsync(TestBotName, Arg.Any<CancellationToken>())
            .Returns(_ => infos.Dequeue());
        var provider = _context.Render<MudDialogProvider>();
        var cut = Render();

        DropButton(cut).Click();
        provider.WaitForNUnitAssertion(() => Assert.That(provider.Markup, Does.Contain("Discard 5 pending update(s)")));
        DialogButton(provider, "Drop").Click();

        cut.WaitForNUnitAssertion(() => Assert.That(DropButton(cut).HasAttribute("disabled"), Is.True));
        _controlService.Received(1).DropPendingUpdatesAsync(TestBotName, Arg.Any<CancellationToken>());
    }

    [Test]
    public void Drop_WhenCancelled_DoesNotDrop()
    {
        GivenWebhook(new WebhookInfo { Url = "https://example.com/hook", PendingUpdateCount = 5 });
        var provider = _context.Render<MudDialogProvider>();
        var cut = Render();

        DropButton(cut).Click();
        provider.WaitForNUnitAssertion(() => Assert.That(provider.Markup, Does.Contain("Discard 5 pending update(s)")));
        DialogButton(provider, "Cancel").Click();

        provider.WaitForNUnitAssertion(() => Assert.That(provider.Markup, Does.Not.Contain("Discard 5 pending update(s)")));
        _controlService.DidNotReceive().DropPendingUpdatesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void Drop_WhenBotIsBusy_ShowsInfoAndReloads()
    {
        GivenWebhook(new WebhookInfo { Url = "https://example.com/hook", PendingUpdateCount = 5 });
        _controlService.DropPendingUpdatesAsync(TestBotName, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new BotControlBusyException(TestBotName)));
        var provider = _context.Render<MudDialogProvider>();
        var snackbars = _context.Render<MudSnackbarProvider>();
        var cut = Render();

        DropButton(cut).Click();
        provider.WaitForNUnitAssertion(() => Assert.That(provider.Markup, Does.Contain("Discard 5 pending update(s)")));
        DialogButton(provider, "Drop").Click();

        snackbars.WaitForNUnitAssertion(() => Assert.That(snackbars.Markup, Does.Contain("already being updated")));
        _controlService.Received(2).GetWebhookInfoAsync(TestBotName, Arg.Any<CancellationToken>());
    }

    [Test]
    public void Toggle_TurningBotOff_ReloadsWebhookInfoWhenDone()
    {
        var statuses = new List<BotRuntimeStatus>
        {
            new(TestBotName, true, true, BotRuntimeState.Applied, DateTime.UtcNow, null)
        };
        _controlService.GetStatuses().Returns(_ => statuses.ToList());
        var infos = new Queue<WebhookInfo>(
        [
            new WebhookInfo { Url = "https://example.com/hook", PendingUpdateCount = 0 },
            new WebhookInfo { Url = string.Empty, PendingUpdateCount = 0 }
        ]);
        _controlService.GetWebhookInfoAsync(TestBotName, Arg.Any<CancellationToken>())
            .Returns(_ => infos.Dequeue());
        _controlService.SetEnabledAsync(TestBotName, false, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            statuses[0] = statuses[0] with { DesiredEnabled = false, AppliedEnabled = false };
            return statuses[0];
        });
        var cut = Render();
        Assert.That(cut.Markup, Does.Contain("https://example.com/hook"));

        cut.Find("input[role=switch]").Change(false);

        cut.WaitForNUnitAssertion(() => Assert.That(cut.Markup, Does.Contain("Not set")));
        _controlService.Received(1).SetEnabledAsync(TestBotName, false, Arg.Any<CancellationToken>());
        Assert.That(cut.Find("input[role=switch]").HasAttribute("checked"), Is.False);
    }

    [Test]
    public void WhileBotIsApplying_DisablesDrop()
    {
        _controlService.GetStatuses().Returns(
            [new BotRuntimeStatus(TestBotName, true, false, BotRuntimeState.Pending, DateTime.UtcNow, null)]);
        GivenWebhook(new WebhookInfo { Url = "https://example.com/hook", PendingUpdateCount = 5 });

        var cut = Render();

        Assert.That(DropButton(cut).HasAttribute("disabled"), Is.True);
    }

    private IRenderedComponent<DashboardWebhook> Render() =>
        _context.Render<DashboardWebhook>(parameters => parameters.Add(p => p.BotName, TestBotName));

    private void GivenWebhook(WebhookInfo info) =>
        _controlService.GetWebhookInfoAsync(TestBotName, Arg.Any<CancellationToken>()).Returns(info);

    private static IElement DropButton(IRenderedComponent<DashboardWebhook> cut) =>
        cut.Find("button[aria-label='Drop pending updates']");

    private static IElement DialogButton(IRenderedComponent<MudDialogProvider> provider, string text) =>
        provider.FindAll("button").Single(button => button.TextContent.Trim() == text);
}
