using BotFarm.Core.Abstractions;
using BotFarm.Shared.Components;
using BotFarm.TestKit;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Telegram.Bot;
using Telegram.Bot.Requests;
using Telegram.Bot.Types;

namespace BotFarm.Shared.UnitTests.Components;

[TestFixture]
public class DashboardChatsTests
{
    private TestableDashboardChats _component = default!;
    private IBotRegistry _botRegistry = default!;
    private IDatabaseService _databaseService = default!;
    private IBotService _botService = default!;
    private ILogger<DashboardChats> _logger = default!;
    private INotificationService _notificationService = default!;
    private IJSRuntime _jsRuntime = default!;
    private ISnackbar _snackbar = default!;
    private IDialogService _dialogService = default!;
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
            INotificationService notificationService,
            IJSRuntime jsRuntime,
            ISnackbar snackbar,
            IDialogService dialogService)
        {
            BotRegistry = botRegistry;
            Logger = logger;
            NotificationService = notificationService;
            JSRuntime = jsRuntime;
            Snackbar = snackbar;
            DialogService = dialogService;
        }

        public void SetServices(IDatabaseService databaseService, IBotService botService)
        {
            _databaseServiceField.Set(this, databaseService);
            _botServiceField.Set(this, botService);
        }

        public Task InvokeOnInitialized() => OnInitializedAsync();
        public Task InvokeLoadChatIds(bool noToast) => LoadChatIds(noToast);
        public Task InvokeCopyToClipboard(string text, string label) => CopyToClipboard(text, label);
        public Task InvokeSendMessage(long id, string name) => SendMessage(new ChatRow(1, id, name, true));

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
        _notificationService = Substitute.For<INotificationService>();
        _jsRuntime = Substitute.For<IJSRuntime>();
        _snackbar = Substitute.For<ISnackbar>();
        _dialogService = Substitute.For<IDialogService>();

        _component = new TestableDashboardChats
        {
            BotName = TestBotName,
            Title = "Chats"
        };
        _component.SetDependencies(_botRegistry, _logger, _notificationService, _jsRuntime, _snackbar, _dialogService);
    }

    [TearDown]
    public void TearDown()
    {
        _snackbar?.Dispose();
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
        _botService.Client.SendRequest(Arg.Any<GetChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => throw new Exception("Chat not found"),
                _ => new ChatFullInfo { Id = 2, Title = "Second" });
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
    public async Task SendMessage_InitializesQuillEditor()
    {
        // Arrange
        // Act
        await _component.InvokeSendMessage(123, "Test Chat");

        // Assert
        await _jsRuntime.Received(1).InvokeVoidAsync("initializeQuillEditor");
    }
}
