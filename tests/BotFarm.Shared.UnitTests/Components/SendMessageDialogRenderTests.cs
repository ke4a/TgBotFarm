using BotFarm.Core.Abstractions;
using BotFarm.Shared.Components;
using BotFarm.TestKit;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using NSubstitute;

namespace BotFarm.Shared.UnitTests.Components;

/// <summary>
/// Renders <see cref="SendMessageDialog"/> through MudBlazor's dialog provider, as the dashboard opens it,
/// and drives it through its buttons.
/// </summary>
[TestFixture]
public class SendMessageDialogRenderTests : AdminUiTestFixture
{
    private const string ModulePath = "./_content/BotFarm.Shared/Components/SendMessageDialog.razor.js";
    private const string TestBotName = "TestBot";
    private const long TestChatId = 123;

    private BunitContext _context = default!;
    private BunitJSModuleInterop _module = default!;
    private INotificationService _notificationService = default!;
    private IRenderedComponent<MudDialogProvider> _provider = default!;

    [SetUp]
    public async Task SetUp()
    {
        _context = new BunitContext();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
        _context.Services.AddAdminLocalizationForTests();
        _context.Services.AddMudServices();
        _notificationService = Substitute.For<INotificationService>();
        _context.Services.AddSingleton(_notificationService);

        _module = _context.JSInterop.SetupModule(ModulePath);
        _module.SetupVoid("initialize", _ => true);

        _provider = _context.Render<MudDialogProvider>();
        var parameters = new DialogParameters<SendMessageDialog>
        {
            { x => x.BotName, TestBotName },
            { x => x.ChatId, TestChatId },
            { x => x.ChatName, "Test Chat" },
        };
        var dialogService = _context.Services.GetRequiredService<IDialogService>();
        await _provider.InvokeAsync(() => dialogService.ShowAsync<SendMessageDialog>(null, parameters));
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.DisposeAsync();
    }

    [Test]
    public void Render_ShowsChatNameAndTelegramToolbar()
    {
        Assert.That(_provider.Find(".mud-dialog-title").TextContent, Does.Contain("Send message to 'Test Chat'"));

        var toolbarButtons = _provider.FindAll(".mud-dialog-content button").Select(button => button.ClassName);
        Assert.That(
            toolbarButtons,
            Is.EquivalentTo(new[] { "ql-bold", "ql-italic", "ql-underline", "ql-strike", "ql-link", "ql-spoiler" }));
    }

    [Test]
    public void FirstRender_InitializesEditorModuleWithEditorAndToolbar()
    {
        _provider.WaitForAssertion(() =>
        {
            var initialize = _module.VerifyInvoke("initialize");
            Assert.That(initialize.Arguments, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void Send_WhileInFlight_DisablesButtonsAndShowsProgress_ThenClosesDialog()
    {
        _module.Setup<string?>("getContent", _ => true).SetResult("<b>Hello</b>");
        var send = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _notificationService.SendMessage(TestChatId, TestBotName, "<b>Hello</b>").Returns(send.Task);

        ClickAction("Send");

        _provider.WaitForAssertion(() =>
        {
            Assert.That(ActionButton("Send").HasAttribute("disabled"), Is.True);
            Assert.That(ActionButton("Cancel").HasAttribute("disabled"), Is.True);
            Assert.That(_provider.FindAll(".mud-dialog-actions .mud-progress-circular"), Has.Count.EqualTo(1));
        });

        send.SetResult();

        _provider.WaitForAssertion(() => Assert.That(_provider.FindAll(".mud-dialog"), Is.Empty));
        _notificationService.Received(1).SendMessage(TestChatId, TestBotName, "<b>Hello</b>");
        Assert.That(Snackbars().Select(s => s.Message), Does.Contain("Message sent"));
    }

    [Test]
    public void Send_WithEmptyMessage_WarnsAndKeepsDialogOpen()
    {
        _module.Setup<string?>("getContent", _ => true).SetResult(null);

        ClickAction("Send");

        _provider.WaitForAssertion(() =>
            Assert.That(Snackbars().Select(s => s.Message), Does.Contain("Message is empty")));
        Assert.That(_provider.FindAll(".mud-dialog"), Has.Count.EqualTo(1));
        Assert.That(ActionButton("Send").HasAttribute("disabled"), Is.False);
        _notificationService.DidNotReceiveWithAnyArgs().SendMessage(default, default!, default!);
    }

    [Test]
    public void Send_WhenSendingFails_ShowsErrorAndKeepsDialogOpen()
    {
        _module.Setup<string?>("getContent", _ => true).SetResult("Hello");
        _notificationService.SendMessage(TestChatId, TestBotName, "Hello")
            .Returns(Task.FromException(new InvalidOperationException("boom")));

        ClickAction("Send");

        _provider.WaitForAssertion(() =>
            Assert.That(Snackbars().Select(s => s.Message), Does.Contain("Failed to send message: boom")));
        Assert.That(_provider.FindAll(".mud-dialog"), Has.Count.EqualTo(1));
        Assert.That(ActionButton("Send").HasAttribute("disabled"), Is.False);
    }

    [Test]
    public void Cancel_ClosesDialogWithoutSending()
    {
        ClickAction("Cancel");

        _provider.WaitForAssertion(() => Assert.That(_provider.FindAll(".mud-dialog"), Is.Empty));
        _notificationService.DidNotReceiveWithAnyArgs().SendMessage(default, default!, default!);
    }

    private AngleSharp.Dom.IElement ActionButton(string text) =>
        _provider.FindAll(".mud-dialog-actions button")
            .Single(button => button.TextContent.Trim().Equals(text, StringComparison.OrdinalIgnoreCase));

    private void ClickAction(string text) => ActionButton(text).Click();

    private IEnumerable<Snackbar> Snackbars() =>
        _context.Services.GetRequiredService<ISnackbar>().ShownSnackbars;
}
