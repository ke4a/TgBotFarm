using BotFarm.Core.Abstractions;
using BotFarm.Shared.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Reflection;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace BotFarm.Shared.UnitTests.Components;

[TestFixture]
public class DashboardChatsTests
{
    private TestableDashboardChats _component = default!;
    private IEnumerable<IDatabaseService> _databaseServices = default!;
    private IEnumerable<IBotService> _botServices = default!;
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
        private readonly FieldInfo _databaseServiceField;
        private readonly FieldInfo _botServiceField;
        private readonly FieldInfo _loadingChatsField;
        private readonly FieldInfo _chatsField;

        public TestableDashboardChats()
        {
            var type = typeof(DashboardChats);
            _databaseServiceField = type.GetField("_databaseService", BindingFlags.NonPublic | BindingFlags.Instance)!;
            _botServiceField = type.GetField("_botService", BindingFlags.NonPublic | BindingFlags.Instance)!;
            _loadingChatsField = type.GetField("_loadingChats", BindingFlags.NonPublic | BindingFlags.Instance)!;
            _chatsField = type.GetField("_chats", BindingFlags.NonPublic | BindingFlags.Instance)!;
        }

        public void SetDependencies(
            IEnumerable<IDatabaseService> databaseServices,
            IEnumerable<IBotService> botServices,
            ILogger<DashboardChats> logger,
            INotificationService notificationService,
            IJSRuntime jsRuntime,
            ISnackbar snackbar,
            IDialogService dialogService)
        {
            DatabaseServices = databaseServices;
            BotServices = botServices;
            Logger = logger;
            NotificationService = notificationService;
            JSRuntime = jsRuntime;
            Snackbar = snackbar;
            DialogService = dialogService;
        }

        public void SetServices(IDatabaseService databaseService, IBotService botService)
        {
            _databaseServiceField.SetValue(this, databaseService);
            _botServiceField.SetValue(this, botService);
        }

        public Task InvokeLoadChats(bool noToast) => LoadChats(noToast);
        public Task InvokeSendMessage(ChatFullInfo chat) => SendMessage(chat);
        
        public bool IsLoadingChats => (bool)_loadingChatsField.GetValue(this)!;
        public IReadOnlyList<ChatFullInfo> Chats => (List<ChatFullInfo>)_chatsField.GetValue(this)!;
    }

    [SetUp]
    public void SetUp()
    {
        _databaseService = Substitute.For<IDatabaseService>();
        _databaseService.Name.Returns(TestBotName);

        var mockClient = Substitute.For<TelegramBotClient>("123456789:test", null, CancellationToken.None);
        _botService = Substitute.For<IBotService>();
        _botService.Name.Returns(TestBotName);
        _botService.Client.Returns(mockClient);

        _databaseServices = [_databaseService];
        _botServices = [_botService];
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
        _component.SetDependencies(_databaseServices, _botServices, _logger, _notificationService, _jsRuntime, _snackbar, _dialogService);
    }

    [TearDown]
    public void TearDown()
    {
        _snackbar?.Dispose();
    }

    [Test]
    public async Task LoadChatsAsync_WithNoChatIds_DoesNotLoadChats()
    {
        // Arrange
        _databaseService.GetAllChatIds().Returns([]);
        _component.SetServices(_databaseService, _botService);

        // Act
        await _component.InvokeLoadChats(true);

        // Assert
        await _databaseService.Received(1).GetAllChatIds();
        Assert.That(_component.Chats, Is.Empty);
    }

    [Test]
    public async Task LoadChatsAsync_WithNoToastFalse_ShowsSnackbar()
    {
        // Arrange
        _databaseService.GetAllChatIds().Returns([]);
        _component.SetServices(_databaseService, _botService);

        // Act
        await _component.InvokeLoadChats(false);

        // Assert
        _snackbar.Received(1).Add(
            Arg.Any<string>(),
            Severity.Success,
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
    }

    [Test]
    public async Task LoadChatsAsync_WithNoToastTrue_DoesNotShowSnackbar()
    {
        // Arrange
        _databaseService.GetAllChatIds().Returns([]);
        _component.SetServices(_databaseService, _botService);

        // Act
        await _component.InvokeLoadChats(true);

        // Assert
        _snackbar.DidNotReceive().Add(
            Arg.Any<string>(),
            Arg.Any<Severity>(),
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
    }

    [Test]
    public async Task LoadChatsAsync_WhenException_ShowsErrorSnackbarAndResetsLoadingFlag()
    {
        // Arrange
        _databaseService.GetAllChatIds().Throws(new Exception("Database error"));
        _component.SetServices(_databaseService, _botService);

        // Act
        await _component.InvokeLoadChats(false);

        // Assert
        _snackbar.Received(1).Add(
            Arg.Is<string>(msg => msg.Contains("Database error")),
            Severity.Error,
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
        Assert.That(_component.IsLoadingChats, Is.False);
    }

    [Test]
    public async Task LoadChatsAsync_ClearsExistingChats_BeforeLoadingNew()
    {
        // Arrange
        _databaseService.GetAllChatIds().Returns([]);
        _component.SetServices(_databaseService, _botService);
        
        // Manually add a chat to verify it gets cleared
        var chatsField = _component.Chats as List<ChatFullInfo>;
        chatsField!.Add(new ChatFullInfo { Id = 999 });

        // Act
        await _component.InvokeLoadChats(true);

        // Assert
        Assert.That(_component.Chats, Is.Empty);
    }

    [Test]
    public async Task SendMessageAsync_InitializesQuillEditor()
    {
        // Arrange
        var chat = new ChatFullInfo { Id = 123, Title = "Test Chat" };

        // Act
        await _component.InvokeSendMessage(chat);

        // Assert
        await _jsRuntime.Received(1).InvokeVoidAsync("initializeQuillEditor");
    }
}
