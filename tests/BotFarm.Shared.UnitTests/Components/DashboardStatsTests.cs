using BotFarm.Core.Abstractions;
using BotFarm.Shared.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Reflection;

namespace BotFarm.Shared.UnitTests.Components;

[TestFixture]
public class DashboardStatsTests
{
    private TestableDashboardStats _component;
    private IEnumerable<IDatabaseService> _databaseServices;
    private IDatabaseService _databaseService;
    private ILogger<DashboardStats> _logger;
    private IJSRuntime _jsRuntime;
    private const string TestBotName = "TestBot";

    private class TestableDashboardStats : DashboardStats
    {
        private readonly FieldInfo _databaseServiceField;
        private readonly FieldInfo _loadingStatsField;
        private readonly FieldInfo _chatsCountField;

        public TestableDashboardStats()
        {
            var type = typeof(DashboardStats);
            _databaseServiceField = type.GetField("_databaseService", BindingFlags.NonPublic | BindingFlags.Instance)!;
            _loadingStatsField = type.GetField("_loadingStats", BindingFlags.NonPublic | BindingFlags.Instance)!;
            _chatsCountField = type.GetField("_chatsCount", BindingFlags.NonPublic | BindingFlags.Instance)!;
        }

        public void SetDependencies(
            IEnumerable<IDatabaseService> databaseServices,
            ILogger<DashboardStats> logger,
            IJSRuntime jsRuntime)
        {
            DatabaseServices = databaseServices;
            Logger = logger;
            JSRuntime = jsRuntime;
        }

        public void SetDatabaseService(IDatabaseService databaseService)
        {
            _databaseServiceField.SetValue(this, databaseService);
        }

        public Task InvokeOnInitializedAsync() => OnInitializedAsync();
        public Task InvokeLoadStatsAsync() => LoadStatsAsync();
        
        public bool IsLoadingStats => (bool)_loadingStatsField.GetValue(this)!;
        public int? ChatsCount => (int?)_chatsCountField.GetValue(this);
    }

    [SetUp]
    public void SetUp()
    {
        _databaseService = Substitute.For<IDatabaseService>();
        _databaseService.Name.Returns(TestBotName);

        _databaseServices = [_databaseService];
        _logger = Substitute.For<ILogger<DashboardStats>>();
        _jsRuntime = Substitute.For<IJSRuntime>();

        _component = new TestableDashboardStats
        {
            BotName = TestBotName,
            Title = "Bot stats",
            CountKey = "ChatsCount",
            CountLabel = "Chats count:"
        };
        _component.SetDependencies(_databaseServices, _logger, _jsRuntime);
    }

    [Test]
    public async Task OnInitializedAsync_LoadsStats()
    {
        // Arrange
        var chatIds = new List<long> { 123, 456, 789 };
        _databaseService.GetAllChatIds().Returns(chatIds);

        // Act
        await _component.InvokeOnInitializedAsync();

        // Assert
        await _databaseService.Received(1).GetAllChatIds();
        Assert.That(_component.ChatsCount, Is.EqualTo(chatIds.Count));
    }

    [Test]
    public async Task LoadStatsAsync_WithMultipleChats_SetsCorrectCount()
    {
        // Arrange
        var chatIds = new List<long> { 123, 456, 789, 101, 202 };
        _databaseService.GetAllChatIds().Returns(chatIds);
        _component.SetDatabaseService(_databaseService);

        // Act
        await _component.InvokeLoadStatsAsync();

        // Assert
        Assert.That(_component.ChatsCount, Is.EqualTo(chatIds.Count));
    }

    [Test]
    public async Task LoadStatsAsync_WithNoChats_SetsCountToZero()
    {
        // Arrange
        _databaseService.GetAllChatIds().Returns([]);
        _component.SetDatabaseService(_databaseService);

        // Act
        await _component.InvokeLoadStatsAsync();

        // Assert
        Assert.That(_component.ChatsCount, Is.Zero);
    }

    [Test]
    public async Task LoadStatsAsync_WhenException_ShowsErrorToastAndResetsLoadingFlag()
    {
        // Arrange
        _databaseService.GetAllChatIds().Throws(new Exception("Database error"));
        _component.SetDatabaseService(_databaseService);

        // Act
        await _component.InvokeLoadStatsAsync();

        // Assert
        await _jsRuntime.Received(1).InvokeVoidAsync(
            "showToast",
            Arg.Is<object?[]>(args => 
                args.Length == 2 && 
                args[0] != null && args[0].ToString()!.Contains("Database error") &&
                (bool?)args[1] == false));
        Assert.That(_component.IsLoadingStats, Is.False);
    }

    [Test]
    public async Task LoadStatsAsync_WhenException_KeepsChatsCountAsNull()
    {
        // Arrange
        _databaseService.GetAllChatIds().Throws(new Exception("Database error"));
        _component.SetDatabaseService(_databaseService);

        // Act
        await _component.InvokeLoadStatsAsync();

        // Assert
        Assert.That(_component.ChatsCount, Is.Null);
    }

    [Test]
    public async Task LoadStatsAsync_SetsLoadingFlagDuringExecution()
    {
        // Arrange
        bool? loadingDuringExecution = null;
        _databaseService.GetAllChatIds()
            .Returns(callInfo =>
            {
                loadingDuringExecution = _component.IsLoadingStats;
                return [];
            });
        _component.SetDatabaseService(_databaseService);

        // Act
        await _component.InvokeLoadStatsAsync();

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(loadingDuringExecution, Is.True);
            Assert.That(_component.IsLoadingStats, Is.False);
        }
    }

    [Test]
    public async Task LoadStatsAsync_UpdatesCountOnMultipleCalls()
    {
        // Arrange
        _databaseService.GetAllChatIds()
            .Returns(
                [123, 456],
                [789, 101, 202]
            );
        _component.SetDatabaseService(_databaseService);

        // Act
        await _component.InvokeLoadStatsAsync();
        var firstCount = _component.ChatsCount;
        await _component.InvokeLoadStatsAsync();
        var secondCount = _component.ChatsCount;

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstCount, Is.EqualTo(2));
            Assert.That(secondCount, Is.EqualTo(3));
        }
    }

    [Test]
    public async Task OnInitializedAsync_FindsCorrectDatabaseService_ByName()
    {
        // Arrange
        var otherDatabaseService = Substitute.For<IDatabaseService>();
        otherDatabaseService.Name.Returns("OtherBot");
        var multipleDatabaseServices = new[] { otherDatabaseService, _databaseService };

        var component = new TestableDashboardStats
        {
            BotName = TestBotName,
            Title = "Bot stats",
            CountKey = "ChatsCount",
            CountLabel = "Chats count:"
        };
        component.SetDependencies(multipleDatabaseServices, _logger, _jsRuntime);

        _databaseService.GetAllChatIds().Returns([123]);

        // Act
        await component.InvokeOnInitializedAsync();

        // Assert
        await _databaseService.Received(1).GetAllChatIds();
        await otherDatabaseService.DidNotReceive().GetAllChatIds();
    }

    [Test]
    public async Task OnInitializedAsync_IsCaseInsensitive_WhenFindingDatabaseService()
    {
        // Arrange
        var component = new TestableDashboardStats
        {
            BotName = "testbot", // lowercase
            Title = "Bot stats",
            CountKey = "ChatsCount",
            CountLabel = "Chats count:"
        };
        component.SetDependencies(_databaseServices, _logger, _jsRuntime);

        _databaseService.GetAllChatIds().Returns([123]);

        // Act
        await component.InvokeOnInitializedAsync();

        // Assert
        await _databaseService.Received(1).GetAllChatIds();
        Assert.That(component.ChatsCount, Is.EqualTo(1));
    }
}
