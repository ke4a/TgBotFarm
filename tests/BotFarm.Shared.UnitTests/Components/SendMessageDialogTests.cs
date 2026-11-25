using BotFarm.Core.Abstractions;
using BotFarm.Core.Exceptions;
using BotFarm.Shared.Components;
using BotFarm.TestKit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot.Exceptions;

namespace BotFarm.Shared.UnitTests.Components;

[TestFixture]
public class SendMessageDialogTests : AdminUiTestFixture
{
    private const string TestBotName = "TestBot";
    private const long TestChatId = 123;
    private const string TelegramExceptionText = "Bad Request: TELEGRAM-EXCEPTION-TEXT";

    private TestableSendMessageDialog _component = default!;
    private IJSRuntime _jsRuntime = default!;
    private IJSObjectReference _module = default!;
    private ISnackbar _snackbar = default!;
    private INotificationService _notificationService = default!;
    private IMudDialogInstance _dialog = default!;
    private ILogger<SendMessageDialog> _logger = default!;

    private class TestableSendMessageDialog : SendMessageDialog
    {
        public TestableSendMessageDialog(string botName, long chatId, string chatName)
        {
            BotName = botName;
            ChatId = chatId;
            ChatName = chatName;
        }

        public void SetSubmitHandler(Func<string, Task<bool>> submitHandler) => SubmitHandler = submitHandler;

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
            L = AdminLocalizationTestExtensions.CreateAdminLocalizer<SendMessageDialog>();
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
        _logger = Substitute.For<ILogger<SendMessageDialog>>();

        _component = new TestableSendMessageDialog(TestBotName, TestChatId, "Test Chat");
        _component.SetDependencies(
            _jsRuntime,
            _snackbar,
            Substitute.For<IDialogService>(),
            _logger,
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
    public async Task Send_WhenTelegramRejectsTheMessage_ShowsTheErrorCodeAndLogsTheException()
    {
        // Arrange
        await RenderFirstTime();
        SetupContent("Hi\n");
        _notificationService.SendMessage(TestChatId, TestBotName, Arg.Any<string>())
            .Throws(new ApiRequestException(TelegramExceptionText, 400));

        // Act
        await _component.InvokeSend();

        // Assert
        _snackbar.Received(1).Add(
            "Telegram rejected the request (code 400).",
            Severity.Error,
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
        AssertTelegramFailureLogged(nameof(ApiRequestException), 400);
        _dialog.DidNotReceiveWithAnyArgs().Close(default(DialogResult)!);
    }

    [Test]
    public async Task Send_WhenTelegramCannotBeReached_ShowsUnreachableAndLogsTheException()
    {
        // Arrange
        await RenderFirstTime();
        SetupContent("Hi\n");
        _notificationService.SendMessage(TestChatId, TestBotName, Arg.Any<string>())
            .Throws(new RequestException(TelegramExceptionText, new HttpRequestException(TelegramExceptionText)));

        // Act
        await _component.InvokeSend();

        // Assert
        _snackbar.Received(1).Add(
            "Telegram could not be reached.",
            Severity.Error,
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
        AssertTelegramFailureLogged(nameof(RequestException), null);
    }

    /// <summary>
    /// Checks that one error names the bot and the error code and carries the exception, and that no snackbar holds
    /// the exception text.
    /// </summary>
    private void AssertTelegramFailureLogged(string failureType, int? errorCode)
    {
        var entries = _logger.LoggedEntries();
        var failure = entries.Where(entry => entry.Level == LogLevel.Error).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure, Has.Count.EqualTo(1));
            Assert.That(failure.Single()["BotName"], Is.EqualTo(TestBotName));
            Assert.That(failure.Single().Exception?.GetType().Name, Is.EqualTo(failureType));
            Assert.That(failure.Single()["ErrorCode"], Is.EqualTo(errorCode));
        }

        _snackbar.DidNotReceive().Add(
            Arg.Is<string>(message => message.Contains(TelegramExceptionText)),
            Arg.Any<Severity>(),
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
    }

    [Test]
    public async Task Send_WhenBotIsNotConfigured_ShowsNotConfiguredAndKeepsDialogOpen()
    {
        // Arrange
        await RenderFirstTime();
        SetupContent("Hi\n");
        _notificationService.SendMessage(TestChatId, TestBotName, Arg.Any<string>())
            .Throws(new BotNotConfiguredException(TestBotName));

        // Act
        await _component.InvokeSend();

        // Assert
        _snackbar.Received(1).Add(
            "This bot is not configured. Enter its settings first.",
            Severity.Warning,
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
        _snackbar.DidNotReceive().Add(
            Arg.Any<string>(),
            Severity.Error,
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
        _dialog.DidNotReceiveWithAnyArgs().Close(default(DialogResult)!);
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
    public async Task Send_WithSubmitHandlerAccepting_ClosesWithMessageWithoutSending()
    {
        // Arrange
        await RenderFirstTime();
        SetupContent("Hi all\n");
        string? submitted = null;
        _component.SetSubmitHandler(message =>
        {
            submitted = message;
            return Task.FromResult(true);
        });

        // Act
        await _component.InvokeSend();

        // Assert
        Assert.That(submitted, Is.EqualTo("Hi all\n"));
        await _notificationService.DidNotReceiveWithAnyArgs().SendMessage(default, default!, default!);
        _dialog.Received(1).Close(Arg.Is<DialogResult>(r => !r.Canceled && (string)r.Data! == "Hi all\n"));
    }

    [Test]
    public async Task Send_WithSubmitHandlerDeclining_KeepsDialogOpen()
    {
        // Arrange
        await RenderFirstTime();
        SetupContent("Hi all\n");
        _component.SetSubmitHandler(_ => Task.FromResult(false));

        // Act
        await _component.InvokeSend();

        // Assert
        await _notificationService.DidNotReceiveWithAnyArgs().SendMessage(default, default!, default!);
        _dialog.DidNotReceiveWithAnyArgs().Close(default(DialogResult)!);
        Assert.That(_component.IsSending, Is.False);
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
