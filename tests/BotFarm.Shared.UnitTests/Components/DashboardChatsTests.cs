using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Shared.Components;
using BotFarm.TestKit;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;

namespace BotFarm.Shared.UnitTests.Components;

[TestFixture]
public class DashboardChatsTests : AdminUiTestFixture
{
    private TestableDashboardChats _component = default!;
    private IBotRegistry _botRegistry = default!;
    private IDatabaseService _databaseService = default!;
    private IBotService _botService = default!;
    private ILogger<DashboardChats> _logger = default!;
    private IJSRuntime _jsRuntime = default!;
    private ISnackbar _snackbar = default!;
    private IDialogService _dialogService = default!;
    private IBroadcastService _broadcastService = default!;
    private const string TestBotName = "TestBot";

    private class TestableDashboardChats : DashboardChats
    {
        private readonly InstanceFieldAccessor<IDatabaseService> _databaseServiceField;
        private readonly InstanceFieldAccessor<IBotService> _botServiceField;
        private readonly InstanceFieldAccessor<bool> _loadingChatIdsField;

        public TestableDashboardChats()
        {
            _databaseServiceField = ReflectionTestHelper.CreateInstanceFieldAccessor<DashboardChats, IDatabaseService>("_databaseService");
            _botServiceField = ReflectionTestHelper.CreateInstanceFieldAccessor<DashboardChats, IBotService>("_botService");
            _loadingChatIdsField = ReflectionTestHelper.CreateInstanceFieldAccessor<DashboardChats, bool>("_loadingChatIds");
        }

        public void SetDependencies(
            IBotRegistry botRegistry,
            ILogger<DashboardChats> logger,
            IJSRuntime jsRuntime,
            ISnackbar snackbar,
            IDialogService dialogService,
            IBroadcastService broadcastService)
        {
            L = AdminLocalizationTestExtensions.CreateAdminLocalizer<DashboardChats>();
            BotRegistry = botRegistry;
            Logger = logger;
            JSRuntime = jsRuntime;
            Snackbar = snackbar;
            DialogService = dialogService;
            BroadcastService = broadcastService;
        }

        public void SetServices(IDatabaseService databaseService, IBotService botService)
        {
            _databaseServiceField.Set(this, databaseService);
            _botServiceField.Set(this, botService);
        }

        public Task InvokeOnInitialized() => OnInitializedAsync();
        public Task InvokeLoadChatIds(bool noToast) => LoadChatIds(noToast);
        public Task InvokeRefreshChats() => RefreshChats();
        public Task InvokeCopyToClipboard(string text, string label) => CopyToClipboard(text, label, Logger);
        public Task InvokeSendMessage(long id, string name) => SendMessage(new ChatRow(1, id, name, true));
        public Task InvokeDeleteChat(long id, string name) => DeleteChat(new ChatRow(1, id, name, true));
        public Task InvokeCleanUpUnreachableChats() => CleanUpUnreachableChats();
        public Task InvokeBroadcastMessage() => BroadcastMessage();

        protected override Task ReportProgress() => Task.CompletedTask;

        public async Task<(IReadOnlyList<(int Number, long Id, string Name, bool Available)> Rows, int Total)> InvokeLoadPage(
            int page, int pageSize, CancellationToken cancellationToken = default)
        {
            var data = await LoadPage(new TableState { Page = page, PageSize = pageSize }, cancellationToken);
            var rows = data.Items!.Select(r => (r.Number, r.Id, r.Name, r.Available)).ToList();
            return (rows, data.TotalItems);
        }

        public bool IsLoadingChatIds => _loadingChatIdsField.Get(this);
    }

    [SetUp]
    public void SetUp()
    {
        _databaseService = Substitute.For<IDatabaseService>();
        _databaseService.Name.Returns(TestBotName);

        var mockClient = TelegramBotClientFactory.CreateSubstitute();
        _botService = Substitute.For<IBotService>();
        _botService.Name.Returns(TestBotName);
        _botService.Client.Returns(mockClient);

        _botRegistry = Substitute.For<IBotRegistry>();
        _botRegistry.GetService<IDatabaseService>(TestBotName).Returns(_databaseService);
        _botRegistry.GetService<IBotService>(TestBotName).Returns(_botService);
        _logger = Substitute.For<ILogger<DashboardChats>>();
        _jsRuntime = Substitute.For<IJSRuntime>();
        _snackbar = Substitute.For<ISnackbar>();
        _dialogService = Substitute.For<IDialogService>();
        _broadcastService = Substitute.For<IBroadcastService>();

        _component = new TestableDashboardChats
        {
            BotName = TestBotName,
            Title = "Chats"
        };
        _component.SetDependencies(_botRegistry, _logger, _jsRuntime, _snackbar, _dialogService, _broadcastService);
    }

    [TearDown]
    public void TearDown()
    {
        _snackbar?.Dispose();
        _component.Dispose();
    }

    [Test]
    public async Task OnInitialized_ResolvesServicesFromRegistryByBotName()
    {
        // Arrange
        _databaseService.GetAllChatIds().Returns([]);

        // Act
        await _component.InvokeOnInitialized();

        // Assert
        _botRegistry.Received(1).GetService<IDatabaseService>(TestBotName);
        _botRegistry.Received(1).GetService<IBotService>(TestBotName);
        await _databaseService.Received(1).GetAllChatIds();
    }

    [Test]
    public async Task LoadChatIds_DoesNotFetchChatDetails()
    {
        // Arrange
        _databaseService.GetAllChatIds().Returns([1L, 2L, 3L]);
        _component.SetServices(_databaseService, _botService);

        // Act
        await _component.InvokeLoadChatIds(true);

        // Assert
        await _databaseService.Received(1).GetAllChatIds();
        await _botService.Client.DidNotReceive().SendRequest(Arg.Any<GetChatRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LoadChatIds_WithNoToastFalse_ShowsSnackbar()
    {
        // Arrange
        _databaseService.GetAllChatIds().Returns([]);
        _component.SetServices(_databaseService, _botService);

        // Act
        await _component.InvokeLoadChatIds(false);

        // Assert
        _snackbar.Received(1).Add(
            Arg.Any<string>(),
            Severity.Success,
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
    }

    [Test]
    public async Task LoadChatIds_WithNoToastTrue_DoesNotShowSnackbar()
    {
        // Arrange
        _databaseService.GetAllChatIds().Returns([]);
        _component.SetServices(_databaseService, _botService);

        // Act
        await _component.InvokeLoadChatIds(true);

        // Assert
        _snackbar.DidNotReceive().Add(
            Arg.Any<string>(),
            Arg.Any<Severity>(),
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
    }

    [Test]
    public async Task LoadChatIds_WhenException_ShowsErrorSnackbarAndResetsLoadingFlag()
    {
        // Arrange
        _databaseService.GetAllChatIds().Throws(new Exception("Database error"));
        _component.SetServices(_databaseService, _botService);

        // Act
        await _component.InvokeLoadChatIds(false);
        var (rows, total) = await _component.InvokeLoadPage(0, 10);

        // Assert
        _snackbar.Received(1).Add(
            Arg.Is<string>(msg => msg.Contains("Database error")),
            Severity.Error,
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
        Assert.That(_component.IsLoadingChatIds, Is.False);
        Assert.That(rows, Is.Empty);
        Assert.That(total, Is.Zero);
    }

    [Test]
    public async Task LoadPage_FetchesOnlyChatsOnRequestedPage()
    {
        // Arrange
        _databaseService.GetAllChatIds().Returns(Enumerable.Range(1, 25).Select(i => (long)i));
        _botService.Client.SendRequest(Arg.Any<GetChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => new ChatFullInfo
            {
                Id = call.Arg<GetChatRequest>().ChatId.Identifier!.Value,
                Title = $"Chat {call.Arg<GetChatRequest>().ChatId.Identifier}"
            });
        _component.SetServices(_databaseService, _botService);
        await _component.InvokeLoadChatIds(true);

        // Act
        var (rows, total) = await _component.InvokeLoadPage(1, 10);

        // Assert
        Assert.That(total, Is.EqualTo(25));
        Assert.That(rows.Select(r => r.Id), Is.EqualTo(Enumerable.Range(11, 10).Select(i => (long)i)));
        Assert.That(rows[0].Number, Is.EqualTo(11));
        Assert.That(rows[0].Name, Is.EqualTo("Chat 11"));
        await _botService.Client.Received(10).SendRequest(Arg.Any<GetChatRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LoadPage_WhenChatFetchFails_ShowsUnavailableRow()
    {
        // Arrange
        _databaseService.GetAllChatIds().Returns([1L, 2L]);
        _botService.Client.SendRequest(Arg.Is<GetChatRequest>(r => r.ChatId.Identifier == 1), Arg.Any<CancellationToken>())
            .Throws(new Exception("Chat not found"));
        _botService.Client.SendRequest(Arg.Is<GetChatRequest>(r => r.ChatId.Identifier == 2), Arg.Any<CancellationToken>())
            .Returns(new ChatFullInfo { Id = 2, Title = "Second" });
        _component.SetServices(_databaseService, _botService);
        await _component.InvokeLoadChatIds(true);

        // Act
        var (rows, _) = await _component.InvokeLoadPage(0, 10);

        // Assert
        Assert.That(rows, Has.Count.EqualTo(2));
        Assert.That(rows[0], Is.EqualTo((1, 1L, "⚠️ Unavailable", false)));
        Assert.That(rows[1], Is.EqualTo((2, 2L, "Second", true)));
    }

    [Test]
    public async Task LoadPage_WhenReturningToPage_UsesCachedNames()
    {
        // Arrange
        _databaseService.GetAllChatIds().Returns(Enumerable.Range(1, 20).Select(i => (long)i));
        SetUpChatTitles();
        _component.SetServices(_databaseService, _botService);
        await _component.InvokeLoadChatIds(true);

        // Act
        await _component.InvokeLoadPage(0, 10);
        await _component.InvokeLoadPage(1, 10);
        var (rows, _) = await _component.InvokeLoadPage(0, 10);

        // Assert
        Assert.That(rows.Select(r => r.Name), Is.EqualTo(Enumerable.Range(1, 10).Select(i => $"Chat {i}")));
        Assert.That(rows.All(r => r.Available), Is.True);
        await _botService.Client.Received(20).SendRequest(Arg.Any<GetChatRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LoadPage_WhenChatFetchFailed_RetriesOnNextLoad()
    {
        // Arrange
        _databaseService.GetAllChatIds().Returns([1L]);
        _botService.Client.SendRequest(Arg.Any<GetChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new Exception("Chat not found"),
                _ => new ChatFullInfo { Id = 1, Title = "First" });
        _component.SetServices(_databaseService, _botService);
        await _component.InvokeLoadChatIds(true);

        // Act
        var (firstRows, _) = await _component.InvokeLoadPage(0, 10);
        var (secondRows, _) = await _component.InvokeLoadPage(0, 10);

        // Assert
        Assert.That(firstRows[0], Is.EqualTo((1, 1L, "⚠️ Unavailable", false)));
        Assert.That(secondRows[0], Is.EqualTo((1, 1L, "First", true)));
        await _botService.Client.Received(2).SendRequest(Arg.Any<GetChatRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RefreshChats_ClearsCachedNames()
    {
        // Arrange
        _databaseService.GetAllChatIds().Returns([1L, 2L]);
        SetUpChatTitles();
        _component.SetServices(_databaseService, _botService);
        await _component.InvokeLoadChatIds(true);
        await _component.InvokeLoadPage(0, 10);

        // Act
        await _component.InvokeRefreshChats();
        await _component.InvokeLoadPage(0, 10);

        // Assert
        await _botService.Client.Received(4).SendRequest(Arg.Any<GetChatRequest>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LoadPage_KeepsOrderAndNumberingRegardlessOfCompletionOrder()
    {
        // Arrange
        _databaseService.GetAllChatIds().Returns(Enumerable.Range(1, 16).Select(i => (long)i));
        _botService.Client.SendRequest(Arg.Any<GetChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var id = call.Arg<GetChatRequest>().ChatId.Identifier!.Value;
                await Task.Delay(TimeSpan.FromMilliseconds((16 - id) * 5));
                return new ChatFullInfo { Id = id, Title = $"Chat {id}" };
            });
        _component.SetServices(_databaseService, _botService);
        await _component.InvokeLoadChatIds(true);

        // Act
        var (rows, _) = await _component.InvokeLoadPage(1, 8);

        // Assert
        Assert.That(rows.Select(r => r.Id), Is.EqualTo(Enumerable.Range(9, 8).Select(i => (long)i)));
        Assert.That(rows.Select(r => r.Number), Is.EqualTo(Enumerable.Range(9, 8)));
        Assert.That(rows.Select(r => r.Name), Is.EqualTo(Enumerable.Range(9, 8).Select(i => $"Chat {i}")));
    }

    private void SetUpChatTitles()
    {
        _botService.Client.SendRequest(Arg.Any<GetChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => new ChatFullInfo
            {
                Id = call.Arg<GetChatRequest>().ChatId.Identifier!.Value,
                Title = $"Chat {call.Arg<GetChatRequest>().ChatId.Identifier}"
            });
    }

    [Test]
    public async Task CopyToClipboard_WritesTextAndShowsConfirmation()
    {
        // Act
        await _component.InvokeCopyToClipboard("900000001", "ID");

        // Assert
        await _jsRuntime.Received(1).InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>(
            "navigator.clipboard.writeText", Arg.Is<object?[]?>(args => args != null && (string)args[0]! == "900000001"));
        _snackbar.Received(1).Add(
            "ID copied",
            Severity.Info,
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
    }

    [Test]
    public async Task CopyToClipboard_WhenClipboardFails_ShowsErrorSnackbar()
    {
        // Arrange
        _jsRuntime.InvokeAsync<Microsoft.JSInterop.Infrastructure.IJSVoidResult>(
                "navigator.clipboard.writeText", Arg.Any<object?[]?>())
            .Throws(new JSException("Clipboard blocked"));

        // Act
        await _component.InvokeCopyToClipboard("Test Chat", "Name");

        // Assert
        _snackbar.Received(1).Add(
            "Failed to copy Name",
            Severity.Error,
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
    }

    [Test]
    public async Task SendMessage_OpensSendMessageDialogForChat()
    {
        // Act
        await _component.InvokeSendMessage(123, "Test Chat");

        // Assert
        await _dialogService.Received(1).ShowAsync<SendMessageDialog>(
            Arg.Any<string?>(),
            Arg.Is<DialogParameters>(p =>
                (string)p[nameof(SendMessageDialog.BotName)]! == TestBotName &&
                (long)p[nameof(SendMessageDialog.ChatId)]! == 123 &&
                (string)p[nameof(SendMessageDialog.ChatName)]! == "Test Chat"),
            Arg.Any<DialogOptions?>());
    }

    [Test]
    public async Task DeleteChat_WhenConfirmed_DeletesChatAndReloadsIds()
    {
        // Arrange
        _component.SetServices(_databaseService, _botService);
        _databaseService.GetAllChatIds().Returns([]);
        SetupConfirmation(true);

        // Act
        await _component.InvokeDeleteChat(123, "Test Chat");

        // Assert
        await _databaseService.Received(1).DeleteChat(123);
        await _databaseService.Received(1).GetAllChatIds();
        _snackbar.Received(1).Add("Chat 123 deleted", Severity.Success, Arg.Any<Action<SnackbarOptions>>(), Arg.Any<string>());
    }

    [Test]
    public async Task DeleteChat_WhenCancelled_DeletesNothing()
    {
        // Arrange
        _component.SetServices(_databaseService, _botService);
        SetupConfirmation(null);

        // Act
        await _component.InvokeDeleteChat(123, "Test Chat");

        // Assert
        await _databaseService.DidNotReceive().DeleteChat(Arg.Any<long>());
    }

    [Test]
    public async Task DeleteChat_WhenDeletionFails_ShowsError()
    {
        // Arrange
        _component.SetServices(_databaseService, _botService);
        _databaseService.GetAllChatIds().Returns([]);
        _databaseService.DeleteChat(123).ThrowsAsync(new InvalidOperationException("db down"));
        SetupConfirmation(true);

        // Act
        await _component.InvokeDeleteChat(123, "Test Chat");

        // Assert
        _snackbar.Received(1).Add("Failed to delete chat 123: db down", Severity.Error, Arg.Any<Action<SnackbarOptions>>(), Arg.Any<string>());
    }

    [Test]
    public async Task CleanUpUnreachableChats_WhenConfirmed_DeletesOnlyUnreachableChats()
    {
        // Arrange
        _component.SetServices(_databaseService, _botService);
        _databaseService.GetAllChatIds().Returns([1L, 2L, 3L, 4L]);
        SetupGetChat(new Dictionary<long, Exception>
        {
            [2] = new ApiRequestException("Forbidden: bot was kicked from the group chat", 403),
            [3] = new ApiRequestException("Bad Request: chat not found", 400),
            [4] = new ApiRequestException("Too Many Requests", 429),
        });
        SetupConfirmation(true);

        // Act
        await _component.InvokeCleanUpUnreachableChats();

        // Assert
        await _databaseService.Received(1).DeleteChat(2);
        await _databaseService.Received(1).DeleteChat(3);
        await _databaseService.DidNotReceive().DeleteChat(1);
        await _databaseService.DidNotReceive().DeleteChat(4);
        await _dialogService.Received(1).ShowMessageBoxAsync(
            Arg.Any<string>(),
            Arg.Is<string>(m => m.StartsWith("2 of 4 chat(s) are unreachable") && m.Contains("1 chat(s) could not be checked")),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<DialogOptions>());
        _snackbar.Received(1).Add("Deleted 2 unreachable chat(s)", Severity.Success, Arg.Any<Action<SnackbarOptions>>(), Arg.Any<string>());
    }

    [Test]
    public async Task CleanUpUnreachableChats_WhenCancelled_DeletesNothing()
    {
        // Arrange
        _component.SetServices(_databaseService, _botService);
        _databaseService.GetAllChatIds().Returns([1L]);
        SetupGetChat(new Dictionary<long, Exception> { [1] = new ApiRequestException("Forbidden", 403) });
        SetupConfirmation(false);

        // Act
        await _component.InvokeCleanUpUnreachableChats();

        // Assert
        await _databaseService.DidNotReceive().DeleteChat(Arg.Any<long>());
    }

    [Test]
    public async Task CleanUpUnreachableChats_WhenAllReachable_SkipsConfirmation()
    {
        // Arrange
        _component.SetServices(_databaseService, _botService);
        _databaseService.GetAllChatIds().Returns([1L, 2L]);
        SetupGetChat([]);

        // Act
        await _component.InvokeCleanUpUnreachableChats();

        // Assert
        await _dialogService.DidNotReceiveWithAnyArgs().ShowMessageBoxAsync(default!, default(string)!, default!, default, default, default);
        await _databaseService.DidNotReceive().DeleteChat(Arg.Any<long>());
        _snackbar.Received(1).Add("No unreachable chats found.", Severity.Info, Arg.Any<Action<SnackbarOptions>>(), Arg.Any<string>());
    }

    [Test]
    public async Task BroadcastMessage_WhenComposedAndConfirmed_SendsMessageToAllChats()
    {
        // Arrange
        _component.SetServices(_databaseService, _botService);
        _databaseService.GetAllChatIds().Returns([1L, 2L, 3L]);
        SetupBroadcastDialog(DialogResult.Ok("<b>Hi</b>"));
        _broadcastService.Broadcast(TestBotName, Arg.Any<IReadOnlyCollection<long>>(), "<b>Hi</b>", Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(new BroadcastResult(3, [], []));

        // Act
        await _component.InvokeBroadcastMessage();

        // Assert
        await _broadcastService.Received(1).Broadcast(
            TestBotName,
            Arg.Is<IReadOnlyCollection<long>>(ids => ids.SequenceEqual(new long[] { 1, 2, 3 })),
            "<b>Hi</b>",
            Arg.Any<IProgress<int>?>(),
            Arg.Any<CancellationToken>());
        _snackbar.Received(1).Add("Message sent to 3 of 3 chat(s)", Severity.Success, Arg.Any<Action<SnackbarOptions>>(), Arg.Any<string>());
    }

    [Test]
    public async Task BroadcastMessage_OpensEditorWithConfirmingSubmitHandler()
    {
        // Arrange
        _component.SetServices(_databaseService, _botService);
        _databaseService.GetAllChatIds().Returns([1L, 2L]);
        DialogParameters? shown = null;
        var reference = Substitute.For<IDialogReference>();
        reference.Result.Returns(DialogResult.Cancel());
        _dialogService.ShowAsync<SendMessageDialog>(Arg.Any<string?>(), Arg.Do<DialogParameters>(p => shown = p), Arg.Any<DialogOptions?>())
            .Returns(reference);
        SetupConfirmation(false);

        // Act
        await _component.InvokeBroadcastMessage();
        var handler = (Func<string, Task<bool>>)shown![nameof(SendMessageDialog.SubmitHandler)]!;
        var accepted = await handler("Hi");

        // Assert
        Assert.That(accepted, Is.False);
        await _dialogService.Received(1).ShowMessageBoxAsync(
            "Send message to all chats",
            Arg.Is<string>(m => m.Contains("all 2 chat(s)")),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<DialogOptions>());
    }

    [Test]
    public async Task BroadcastMessage_WhenEditorCancelled_SendsNothing()
    {
        // Arrange
        _component.SetServices(_databaseService, _botService);
        _databaseService.GetAllChatIds().Returns([1L]);
        SetupBroadcastDialog(DialogResult.Cancel());

        // Act
        await _component.InvokeBroadcastMessage();

        // Assert
        await _broadcastService.DidNotReceiveWithAnyArgs().Broadcast(default!, default!, default!, default, default);
    }

    [Test]
    public async Task BroadcastMessage_WhenNoChats_DoesNotOpenEditor()
    {
        // Arrange
        _component.SetServices(_databaseService, _botService);
        _databaseService.GetAllChatIds().Returns([]);

        // Act
        await _component.InvokeBroadcastMessage();

        // Assert
        await _dialogService.DidNotReceiveWithAnyArgs().ShowAsync<SendMessageDialog>(default, default(DialogParameters)!, default);
        _snackbar.Received(1).Add("No chats to send to", Severity.Info, Arg.Any<Action<SnackbarOptions>>(), Arg.Any<string>());
    }

    [Test]
    public async Task BroadcastMessage_WhenSomeChatsFail_ShowsWarningWithCounts()
    {
        // Arrange
        _component.SetServices(_databaseService, _botService);
        _databaseService.GetAllChatIds().Returns([1L, 2L, 3L, 4L]);
        SetupBroadcastDialog(DialogResult.Ok("Hi"));
        _broadcastService.Broadcast(TestBotName, Arg.Any<IReadOnlyCollection<long>>(), "Hi", Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(new BroadcastResult(1, [2L, 3L], [4L]));

        // Act
        await _component.InvokeBroadcastMessage();

        // Assert
        _snackbar.Received(1).Add(
            "Message sent to 1 of 4 chat(s); 2 unreachable (use clean-up to remove them), 1 failed (see logs)",
            Severity.Warning,
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
    }

    [Test]
    public async Task BroadcastMessage_WhenBroadcastThrows_ShowsError()
    {
        // Arrange
        _component.SetServices(_databaseService, _botService);
        _databaseService.GetAllChatIds().Returns([1L]);
        SetupBroadcastDialog(DialogResult.Ok("Hi"));
        _broadcastService.Broadcast(TestBotName, Arg.Any<IReadOnlyCollection<long>>(), "Hi", Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Bot not found"));

        // Act
        await _component.InvokeBroadcastMessage();

        // Assert
        _snackbar.Received(1).Add(
            Arg.Is<string>(m => m.Contains("Bot not found")),
            Severity.Error,
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
    }

    [Test]
    public async Task BroadcastMessage_WhenPageClosedMidway_FinishesWithoutTouchingPage()
    {
        // Arrange
        _component.SetServices(_databaseService, _botService);
        _databaseService.GetAllChatIds().Returns([1L, 2L]);
        SetupBroadcastDialog(DialogResult.Ok("Hi"));
        var pending = new TaskCompletionSource<BroadcastResult>();
        _broadcastService.Broadcast(TestBotName, Arg.Any<IReadOnlyCollection<long>>(), "Hi", Arg.Any<IProgress<int>?>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        // Act
        var broadcast = _component.InvokeBroadcastMessage();
        _component.Dispose();
        pending.SetResult(new BroadcastResult(2, [], []));
        await broadcast;

        // Assert
        await _broadcastService.Received(1).Broadcast(
            TestBotName,
            Arg.Any<IReadOnlyCollection<long>>(),
            "Hi",
            Arg.Any<IProgress<int>?>(),
            Arg.Is<CancellationToken>(t => !t.CanBeCanceled));
        _snackbar.DidNotReceive().Add(
            Arg.Is<string>(m => m.StartsWith("Message sent")),
            Arg.Any<Severity>(),
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
    }

    private void SetupBroadcastDialog(DialogResult result)
    {
        var reference = Substitute.For<IDialogReference>();
        reference.Result.Returns(result);
        _dialogService.ShowAsync<SendMessageDialog>(Arg.Any<string?>(), Arg.Any<DialogParameters>(), Arg.Any<DialogOptions?>())
            .Returns(reference);
    }

    private void SetupConfirmation(bool? answer)
    {
        _dialogService.ShowMessageBoxAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<DialogOptions>()).Returns(answer);
    }

    private void SetupGetChat(Dictionary<long, Exception> failures)
    {
        _botService.Client.SendRequest(Arg.Any<GetChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var id = call.Arg<GetChatRequest>().ChatId.Identifier!.Value;
                return failures.TryGetValue(id, out var failure)
                    ? Task.FromException<ChatFullInfo>(failure)
                    : Task.FromResult(new ChatFullInfo { Id = id, Title = $"Chat {id}" });
            });
    }
}
