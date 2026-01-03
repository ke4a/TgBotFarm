using BotFarm.Core.Abstractions;
using BotFarm.Shared.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Reflection;
using Telegram.Bot.Types;

namespace BotFarm.Shared.UnitTests.Components;

[TestFixture]
public class DashboardChatsTests
{
    private TestableDashboardChats _component;
    private IEnumerable<IDatabaseService> _databaseServices;
    private IEnumerable<IBotService> _botServices;
    private IDatabaseService _databaseService;
    private IBotService _botService;
    private ILogger<DashboardChats> _logger;
    private INotificationService _notificationService;
    private IJSRuntime _jsRuntime;
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
            IJSRuntime jsRuntime)
        {
            DatabaseServices = databaseServices;
            BotServices = botServices;
            Logger = logger;
            NotificationService = notificationService;
            JSRuntime = jsRuntime;
        }

        public void SetServices(IDatabaseService databaseService, IBotService botService)
        {
            _databaseServiceField.SetValue(this, databaseService);
            _botServiceField.SetValue(this, botService);
        }

        public Task InvokeLoadChatsAsync(bool noToast) => LoadChatsAsync(noToast);
        public Task InvokeSendMessageAsync(ChatFullInfo chat) => SendMessageAsync(chat);
        
        public bool IsLoadingChats => (bool)_loadingChatsField.GetValue(this)!;
        public IReadOnlyList<ChatFullInfo> Chats => (List<ChatFullInfo>)_chatsField.GetValue(this)!;
    }

    [SetUp]
    public void SetUp()
    {
        _databaseService = Substitute.For<IDatabaseService>();
        _databaseService.Name.Returns(TestBotName);

        _botService = Substitute.For<IBotService>();
        _botService.Name.Returns(TestBotName);

        _databaseServices = [_databaseService];
        _botServices = [_botService];
        _logger = Substitute.For<ILogger<DashboardChats>>();
        _notificationService = Substitute.For<INotificationService>();
        _jsRuntime = Substitute.For<IJSRuntime>();

        _component = new TestableDashboardChats
        {
            BotName = TestBotName,
            Title = "Chats"
        };
        _component.SetDependencies(_databaseServices, _botServices, _logger, _notificationService, _jsRuntime);
    }

    [Test]
    public async Task LoadChatsAsync_WithNoChatIds_DoesNotLoadChats()
    {
        // Arrange
        _databaseService.GetAllChatIds().Returns([]);
        _component.SetServices(_databaseService, _botService);

        // Act
        await _component.InvokeLoadChatsAsync(true);

        // Assert
        await _databaseService.Received(1).GetAllChatIds();
        Assert.That(_component.Chats, Is.Empty);
    }

    [Test]
    public async Task LoadChatsAsync_WithNoToastFalse_ShowsToast()
    {
        // Arrange
        _databaseService.GetAllChatIds().Returns([]);
        _component.SetServices(_databaseService, _botService);

        // Act
        await _component.InvokeLoadChatsAsync(false);

        // Assert
        await _jsRuntime.Received().InvokeVoidAsync(
            Arg.Is<string>(s => s == "showToast"),
            Arg.Any<object?[]>());
    }

    [Test]
    public async Task LoadChatsAsync_WithNoToastTrue_DoesNotShowToast()
    {
        // Arrange
        _databaseService.GetAllChatIds().Returns([]);
        _component.SetServices(_databaseService, _botService);

        // Act
        await _component.InvokeLoadChatsAsync(true);

        // Assert
        await _jsRuntime.DidNotReceive().InvokeVoidAsync(
            Arg.Is<string>(s => s == "showToast"),
            Arg.Any<object?[]>());
    }

    [Test]
    public async Task LoadChatsAsync_WhenException_ShowsErrorToastAndResetsLoadingFlag()
    {
        // Arrange
        _databaseService.GetAllChatIds().Throws(new Exception("Database error"));
        _component.SetServices(_databaseService, _botService);

        // Act
        await _component.InvokeLoadChatsAsync(false);

        // Assert
        await _jsRuntime.Received(1).InvokeVoidAsync(
            "showToast",
            Arg.Is<object?[]>(args => 
                args.Length == 2 && 
                args[0] != null && args[0].ToString()!.Contains("Database error") &&
                (bool?)args[1] == false));
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
        await _component.InvokeLoadChatsAsync(true);

        // Assert
        Assert.That(_component.Chats, Is.Empty);
    }

    [Test]
    public async Task SendMessageAsync_WithValidMessage_SendsMessage()
    {
        // Arrange
        var chat = new ChatFullInfo { Id = 123, Title = "Test Chat" };
        var message = "Hello world";
        _jsRuntime.InvokeAsync<string?>("openQuillModal", Arg.Any<object?[]>()).Returns(message);
        _notificationService.SendMessage(chat.Id, TestBotName, message).Returns(Task.CompletedTask);

        // Act
        await _component.InvokeSendMessageAsync(chat);

        // Assert
        await _notificationService.Received(1).SendMessage(chat.Id, TestBotName, message);
        await _jsRuntime.Received(1).InvokeVoidAsync(
            "showToast",
            Arg.Is<object?[]>(args => 
                args.Length == 2 && 
                args[0] != null && args[0].ToString()!.Equals("Message sent", StringComparison.Ordinal) &&
                (bool?)args[1] == true));
    }

    [Test]
    public async Task SendMessageAsync_WhenUserCancels_DoesNotSendMessage()
    {
        // Arrange
        var chat = new ChatFullInfo { Id = 123, Title = "Test Chat" };
        _jsRuntime.InvokeAsync<string?>("openQuillModal", Arg.Any<object?[]>()).Returns((string?)null);

        // Act
        await _component.InvokeSendMessageAsync(chat);

        // Assert
        await _notificationService.DidNotReceive().SendMessage(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task SendMessageAsync_WhenUserEntersEmptyMessage_DoesNotSendMessage()
    {
        // Arrange
        var chat = new ChatFullInfo { Id = 123, Title = "Test Chat" };
        _jsRuntime.InvokeAsync<string?>("openQuillModal", Arg.Any<object?[]>()).Returns("   ");

        // Act
        await _component.InvokeSendMessageAsync(chat);

        // Assert
        await _notificationService.DidNotReceive().SendMessage(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task SendMessageAsync_WhenException_ShowsErrorToast()
    {
        // Arrange
        var chat = new ChatFullInfo { Id = 123, Title = "Test Chat" };
        var message = "Hello world";
        _jsRuntime.InvokeAsync<string?>("openQuillModal", Arg.Any<object?[]>()).Returns(message);
        _notificationService.SendMessage(chat.Id, TestBotName, message).Throws(new Exception("Send failed"));

        // Act
        await _component.InvokeSendMessageAsync(chat);

        // Assert
        await _jsRuntime.Received(1).InvokeVoidAsync(
            "showToast",
            Arg.Is<object?[]>(args => 
                args.Length == 2 && 
                args[0] != null && args[0].ToString()!.Contains("Send failed") &&
                (bool?)args[1] == false));
    }
}
