using AngleSharp.Dom;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Exceptions;
using BotFarm.Core.Models;
using BotFarm.Shared.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;

namespace BotFarm.Shared.UnitTests.Components;

/// <summary>
/// Renders <see cref="BotEnabledControl"/> inside a parent that reloads the status whenever the control
/// asks it to, as the dashboard and the Webhook card do.
/// </summary>
[TestFixture]
public class BotEnabledControlTests
{
    private const string BotName = "TestBot";

    private BunitContext _context = default!;
    private IBotControlService _controlService = default!;
    private List<BotRuntimeStatus> _statuses = default!;

    [SetUp]
    public void SetUp()
    {
        _context = new BunitContext();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddMudServices();
        _context.Services.AddLogging();
        _controlService = Substitute.For<IBotControlService>();
        _statuses = [];
        _controlService.GetStatuses().Returns(_ => _statuses.ToList());
        _context.Services.AddSingleton(_controlService);
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.DisposeAsync();
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Toggle_SubmitsExplicitTarget(bool enabled)
    {
        _statuses.Add(Status(!enabled, BotRuntimeState.Applied));
        var cut = _context.Render<StatusHost>();

        Switch(cut).Change(enabled);

        _controlService.Received(1).SetEnabledAsync(BotName, enabled, Arg.Any<CancellationToken>());
    }

    [Test]
    public void Toggle_ShowsApplyingWhileRequestRunsThenFinalState()
    {
        _statuses.Add(Status(false, BotRuntimeState.Applied));
        var response = new TaskCompletionSource<BotRuntimeStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        _controlService.SetEnabledAsync(BotName, true, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _statuses[0] = Status(true, BotRuntimeState.Pending, applied: false);
            return response.Task;
        });
        var cut = _context.Render<StatusHost>();

        Switch(cut).Change(true);

        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Applying..."));
            Assert.That(Switch(cut).HasAttribute("disabled"), Is.True);
            Assert.That(Switch(cut).HasAttribute("checked"), Is.True);
        });

        _statuses[0] = Status(true, BotRuntimeState.Applied);
        response.SetResult(_statuses[0]);

        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Not.Contain("Applying..."));
            Assert.That(Switch(cut).HasAttribute("disabled"), Is.False);
            Assert.That(Switch(cut).HasAttribute("checked"), Is.True);
        });
        Assert.That(cut.Instance.ChangeCount, Is.EqualTo(2));
    }

    [Test]
    public void Toggle_WhenSaveFails_ShowsRetryableErrorWithoutProviderDetail()
    {
        _statuses.Add(Status(false, BotRuntimeState.Applied));
        _controlService.SetEnabledAsync(BotName, true, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _statuses[0] = Status(true, BotRuntimeState.Unknown, applied: false);
            return Task.FromException<BotRuntimeStatus>(new InvalidOperationException("secret-bearing provider detail"));
        });
        var cut = _context.Render<StatusHost>();

        Switch(cut).Change(true);

        cut.WaitForAssertion(() =>
        {
            var alert = cut.Find(".mud-alert");
            Assert.That(alert.TextContent, Does.Contain("could not be saved"));
            Assert.That(alert.TextContent, Does.Not.Contain("secret-bearing"));
        });
        Assert.That(Switch(cut).HasAttribute("disabled"), Is.True);
        _controlService.DidNotReceive().RetryAsync(BotName, Arg.Any<CancellationToken>());
    }

    [Test]
    public void Toggle_WhenBotIsBusy_ShowsInfoWithoutError()
    {
        _statuses.Add(Status(false, BotRuntimeState.Applied));
        _controlService.SetEnabledAsync(BotName, true, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<BotRuntimeStatus>(new BotControlBusyException(BotName)));
        var snackbars = _context.Render<MudSnackbarProvider>();
        var cut = _context.Render<StatusHost>();

        Switch(cut).Change(true);

        snackbars.WaitForAssertion(() => Assert.That(snackbars.Markup, Does.Contain("already being updated")));
        Assert.That(cut.FindAll(".mud-alert"), Is.Empty);
    }

    [Test]
    public void Retry_OnErrorState_SubmitsRetryAndClearsProblem()
    {
        _statuses.Add(Status(true, BotRuntimeState.Error, applied: false));
        _controlService.RetryAsync(BotName, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _statuses[0] = Status(true, BotRuntimeState.Applied);
            return _statuses[0];
        });
        var cut = _context.Render<StatusHost>();
        Assert.That(cut.Find(".mud-alert").TextContent, Does.Contain("could not be applied"));

        RetryButton(cut).Click();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".mud-alert"), Is.Empty));
        _controlService.Received(1).RetryAsync(BotName, Arg.Any<CancellationToken>());
        Assert.That(Switch(cut).HasAttribute("disabled"), Is.False);
    }

    [TestCase(BotRuntimeState.Applied, false, null)]
    [TestCase(BotRuntimeState.Pending, true, null)]
    [TestCase(BotRuntimeState.Error, true, "saved state")]
    [TestCase(BotRuntimeState.Unknown, true, "not confirmed")]
    public void State_SetsSwitchAndProblemMessage(BotRuntimeState state, bool switchDisabled, string? problem)
    {
        _statuses.Add(Status(true, state, applied: false));

        var cut = _context.Render<StatusHost>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Switch(cut).HasAttribute("disabled"), Is.EqualTo(switchDisabled));
            if (problem is null)
            {
                Assert.That(cut.FindAll(".mud-alert"), Is.Empty);
            }
            else
            {
                Assert.That(cut.Find(".mud-alert").TextContent, Does.Contain(problem));
            }
        }
    }

    [Test]
    public void UnknownStateWithoutSavedTarget_SaysStateIsUnknown()
    {
        _statuses.Add(new BotRuntimeStatus(BotName, null, null, BotRuntimeState.Unknown, null, null));

        var cut = _context.Render<StatusHost>();

        Assert.That(cut.Find(".mud-alert").TextContent, Does.Contain("Bot state is unknown"));
        Assert.That(RetryButton(cut), Is.Not.Null);
    }

    private static BotRuntimeStatus Status(bool desired, BotRuntimeState state, bool? applied = null) =>
        new(BotName, desired, applied ?? desired, state, DateTime.UtcNow, null);

    private static IElement Switch(IRenderedComponent<StatusHost> cut) =>
        cut.Find("input[role=switch]");

    private static IElement RetryButton(IRenderedComponent<StatusHost> cut) =>
        cut.FindAll(".mud-alert button").Single(button => button.TextContent.Trim() == "Retry");

    /// <summary>Owns the bot's status and reloads it from the control service when the control asks.</summary>
    private sealed class StatusHost : ComponentBase
    {
        private BotRuntimeStatus _status = default!;

        [Inject] private IBotControlService ControlService { get; set; } = default!;

        public int ChangeCount { get; private set; }

        protected override void OnInitialized() => Reload();

        private void Reload() => _status = ControlService.GetStatuses().Single();

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<BotEnabledControl>(0);
            builder.AddComponentParameter(1, nameof(BotEnabledControl.Status), _status);
            builder.AddComponentParameter(2, nameof(BotEnabledControl.OnStatusChanged),
                EventCallback.Factory.Create<bool>(this, () =>
                {
                    ChangeCount++;
                    Reload();
                }));
            builder.CloseComponent();
        }
    }
}
