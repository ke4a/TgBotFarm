using BotFarm.Core.Abstractions;
using BotFarm.Shared.Components;
using BotFarm.TestKit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BotFarm.Shared.UnitTests.Components;

[TestFixture]
public class SendMessageDialogTests
{
    private const string TestBotName = "TestBot";
    private const long TestChatId = 123;

    private TestableSendMessageDialog _component = default!;
    private IJSRuntime _jsRuntime = default!;
    private IJSObjectReference _module = default!;
    private ISnackbar _snackbar = default!;
    private INotificationService _notificationService = default!;
    private IMudDialogInstance _dialog = default!;

    private class TestableSendMessageDialog : SendMessageDialog
    {
        private readonly InstanceFieldAccessor<bool> _sendingField =
            ReflectionTestHelper.CreateInstanceFieldAccessor<SendMessageDialog, bool>("_sending");

        public void SetDependencies(
            IJSRuntime jsRuntime,
            ISnackbar snackbar,
            IDialogService dialogService,
            ILogger<SendMessageDialog> logger,
            INotificationService notificationService,
            IMudDialogInstance dialog)
        {
            JSRuntime = jsRuntime;
            Snackbar = snackbar;
            DialogService = dialogService;
            Logger = logger;
            NotificationService = notificationService;
            MudDialog = dialog;
        }

        public Task InvokeOnAfterRender(bool firstRender) => OnAfterRenderAsync(firstRender);
        public Task InvokeSend() => Send();
        public bool IsSending => _sendingField.Get(this);
    }

    [SetUp]
    public void SetUp()
    {
        _module = Substitute.For<IJSObjectReference>();
        _jsRuntime = Substitute.For<IJSRuntime>();
        _jsRuntime.InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]?>()).Returns(_module);
        _snackbar = Substitute.For<ISnackbar>();
        _notificationService = Substitute.For<INotificationService>();
        _dialog = Substitute.For<IMudDialogInstance>();

        _component = new TestableSendMessageDialog
        {
            BotName = TestBotName,
            ChatId = TestChatId,
            ChatName = "Test Chat"
        };
        _component.SetDependencies(
            _jsRuntime,
            _snackbar,
            Substitute.For<IDialogService>(),
            Substitute.For<ILogger<SendMessageDialog>>(),
            _notificationService,
            _dialog);
    }

    [TearDown]
    public async Task TearDown()
    {
        _snackbar?.Dispose();
        await _component.DisposeAsync();
    }

    private void SetupContent(string? content)
    {
        _module.InvokeAsync<string?>("getContent", Arg.Any<object?[]?>()).Returns(content);
    }

    [Test]
    public async Task OnAfterRender_FirstRender_ImportsModuleAndInitializesEditor()
    {
        // Act
        await RenderFirstTime();

        // Assert
        await _jsRuntime.Received(1).InvokeAsync<IJSObjectReference>(
            "import",
            Arg.Is<object?[]?>(args => args != null && (string)args[0]! == "./_content/BotFarm.Shared/Components/SendMessageDialog.razor.js"));
        await _module.Received(1).InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>(
            "initialize",
            Arg.Is<object?[]?>(args => args != null && args.Length == 2 && args[0] is ElementReference && args[1] is ElementReference));
    }

    [Test]
    public async Task OnAfterRender_LaterRender_DoesNotReinitialize()
    {
        // Act
        await _component.InvokeOnAfterRender(false);

        // Assert
        await _jsRuntime.DidNotReceive().InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]?>());
    }

    [Test]
    public async Task Send_WithContent_SendsHtmlAndClosesDialog()
    {
        // Arrange
        await RenderFirstTime();
        SetupContent("<strong>Hi</strong>\n");

        // Act
        await _component.InvokeSend();

        // Assert
        await _notificationService.Received(1).SendMessage(TestChatId, TestBotName, "<strong>Hi</strong>\n");
        _dialog.Received(1).Close(Arg.Any<DialogResult>());
        _snackbar.Received(1).Add("Message sent", Severity.Success, Arg.Any<Action<SnackbarOptions>>(), Arg.Any<string>());
        Assert.That(_component.IsSending, Is.False);
    }

    [Test]
    public async Task Send_WhenEmpty_ShowsWarningAndKeepsDialogOpen()
    {
        // Arrange
        await RenderFirstTime();
        SetupContent(null);

        // Act
        await _component.InvokeSend();

        // Assert
        await _notificationService.DidNotReceiveWithAnyArgs().SendMessage(default, default!, default!);
        _snackbar.Received(1).Add("Message is empty", Severity.Warning, Arg.Any<Action<SnackbarOptions>>(), Arg.Any<string>());
        _dialog.DidNotReceiveWithAnyArgs().Close(default(DialogResult)!);
        Assert.That(_component.IsSending, Is.False);
    }

    [Test]
    public async Task Send_WhenSendFails_ShowsErrorAndKeepsDialogOpen()
    {
        // Arrange
        await RenderFirstTime();
        SetupContent("Hi\n");
        _notificationService.SendMessage(TestChatId, TestBotName, Arg.Any<string>()).Throws(new Exception("Forbidden"));

        // Act
        await _component.InvokeSend();

        // Assert
        _snackbar.Received(1).Add(
            Arg.Is<string>(msg => msg.Contains("Forbidden")),
            Severity.Error,
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
        _dialog.DidNotReceiveWithAnyArgs().Close(default(DialogResult)!);
        Assert.That(_component.IsSending, Is.False);
    }

    [Test]
    public async Task Send_WhileSendInFlight_IgnoresSecondSend()
    {
        // Arrange
        await RenderFirstTime();
        SetupContent("Hi\n");
        var pending = new TaskCompletionSource();
        _notificationService.SendMessage(TestChatId, TestBotName, Arg.Any<string>()).Returns(pending.Task);

        // Act
        var first = _component.InvokeSend();
        var sendingDuringFirst = _component.IsSending;
        await _component.InvokeSend();
        pending.SetResult();
        await first;

        // Assert
        Assert.That(sendingDuringFirst, Is.True);
        await _notificationService.Received(1).SendMessage(TestChatId, TestBotName, Arg.Any<string>());
        _dialog.Received(1).Close(Arg.Any<DialogResult>());
    }

    [Test]
    public async Task DisposeAsync_DisposesModule()
    {
        // Arrange
        await RenderFirstTime();

        // Act
        await _component.DisposeAsync();

        // Assert
        await _module.Received().DisposeAsync();
    }

    private Task RenderFirstTime() => _component.InvokeOnAfterRender(true);
}
