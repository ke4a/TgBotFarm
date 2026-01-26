using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Shared.Components;
using Microsoft.Extensions.Logging;
using MudBlazor;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Reflection;

namespace BotFarm.Shared.UnitTests.Components;

[TestFixture]
public class DashboardStatsTests
{
    private TestableDashboardStats _component = default!;
    private IEnumerable<IMongoDbDatabaseService> _databaseServices = default!;
    private IMongoDbDatabaseService _databaseService = default!;
    private ILogger<DashboardStats> _logger = default!;
    private ISnackbar _snackbar = default!;
    private IDialogService _dialogService = default!;
    private const string TestBotName = "TestBot";

    private class TestableDashboardStats : DashboardStats
    {
        private readonly FieldInfo _databaseServiceField;
        private readonly FieldInfo _loadingStatsField;
        private readonly FieldInfo _chatsCountField;
        private readonly FieldInfo _dbStatsField;
        private readonly FieldInfo _additionalStatsField;
        private Dictionary<string, string>? _additionalStatsResult;

        public TestableDashboardStats()
        {
            var type = typeof(DashboardStats);
            _databaseServiceField = type.GetField("_databaseService", BindingFlags.NonPublic | BindingFlags.Instance)!;
            _loadingStatsField = type.GetField("_loadingStats", BindingFlags.NonPublic | BindingFlags.Instance)!;
            _chatsCountField = type.GetField("_chatsCount", BindingFlags.NonPublic | BindingFlags.Instance)!;
            _dbStatsField = type.GetField("_dbStats", BindingFlags.NonPublic | BindingFlags.Instance)!;
            _additionalStatsField = type.GetField("_additionalStats", BindingFlags.NonPublic | BindingFlags.Instance)!;
        }

        public void SetDependencies(
            IEnumerable<IMongoDbDatabaseService> databaseServices,
            ILogger<DashboardStats> logger,
            ISnackbar snackbar,
            IDialogService dialogService)
        {
            DatabaseServices = databaseServices;
            Logger = logger;
            Snackbar = snackbar;
            DialogService = dialogService;
        }

        public void SetDatabaseService(IMongoDbDatabaseService databaseService)
        {
            _databaseServiceField.SetValue(this, databaseService);
        }

        public void SetAdditionalStatsResult(Dictionary<string, string> additionalStats)
        {
            _additionalStatsResult = additionalStats;
        }

        public Task InvokeOnInitializedAsync() => OnInitializedAsync();
        public Task InvokeLoadStats() => LoadStats();
        
        public bool IsLoadingStats => (bool)_loadingStatsField.GetValue(this)!;
        public int? ChatsCount => (int?)_chatsCountField.GetValue(this);
        public MongoDatabaseStats? DbStats => (MongoDatabaseStats?)_dbStatsField.GetValue(this);
        public Dictionary<string, string> AdditionalStats => (Dictionary<string, string>)(_additionalStatsField.GetValue(this) ?? new Dictionary<string, string>());

        protected override Task<Dictionary<string, string>> LoadAdditionalStats()
        {
            return Task.FromResult(_additionalStatsResult ?? new Dictionary<string, string>());
        }
    }

    [SetUp]
    public void SetUp()
    {
        _databaseService = Substitute.For<IMongoDbDatabaseService>();
        _databaseService.Name.Returns(TestBotName);

        _databaseServices = [_databaseService];
        _logger = Substitute.For<ILogger<DashboardStats>>();
        _snackbar = Substitute.For<ISnackbar>();
        _dialogService = Substitute.For<IDialogService>();

        _component = new TestableDashboardStats
        {
            BotName = TestBotName,
        };
        _component.SetDependencies(_databaseServices, _logger, _snackbar, _dialogService);
    }

    [TearDown]
    public void TearDown()
    {
        _snackbar?.Dispose();
    }

    [Test]
    public async Task OnInitializedAsync_LoadsStats()
    {
        // Arrange
        var chatIds = new List<long> { 123, 456, 789 };
        _databaseService.GetAllChatIds().Returns(chatIds);
        _databaseService.GetDatabaseStats().Returns(new MongoDatabaseStats());

        // Act
        await _component.InvokeOnInitializedAsync();

        // Assert
        await _databaseService.Received(1).GetAllChatIds();
        await _databaseService.Received(1).GetDatabaseStats();
        Assert.That(_component.ChatsCount, Is.EqualTo(chatIds.Count));
    }

    [Test]
    public async Task LoadStatsAsync_WithMultipleChats_SetsCorrectCount()
    {
        // Arrange
        var chatIds = new List<long> { 123, 456, 789, 101, 202 };
        _databaseService.GetAllChatIds().Returns(chatIds);
        _databaseService.GetDatabaseStats().Returns(new MongoDatabaseStats());
        _component.SetDatabaseService(_databaseService);

        // Act
        await _component.InvokeLoadStats();

        // Assert
        Assert.That(_component.ChatsCount, Is.EqualTo(chatIds.Count));
    }

    [Test]
    public async Task LoadStatsAsync_WithNoChats_SetsCountToZero()
    {
        // Arrange
        _databaseService.GetAllChatIds().Returns([]);
        _databaseService.GetDatabaseStats().Returns(new MongoDatabaseStats());
        _component.SetDatabaseService(_databaseService);

        // Act
        await _component.InvokeLoadStats();

        // Assert
        Assert.That(_component.ChatsCount, Is.Zero);
    }

    [Test]
    public async Task LoadStatsAsync_WhenException_ShowsErrorSnackbarAndResetsLoadingFlag()
    {
        // Arrange
        _databaseService.GetAllChatIds().Throws(new Exception("Database error"));
        _component.SetDatabaseService(_databaseService);

        // Act
        await _component.InvokeLoadStats();

        // Assert
        _snackbar.Received(1).Add(
            Arg.Is<string>(msg => msg.Contains("Database error")),
            Severity.Error,
            Arg.Any<Action<SnackbarOptions>>(),
            Arg.Any<string>());
        Assert.That(_component.IsLoadingStats, Is.False);
    }

    [Test]
    public async Task LoadStatsAsync_WhenException_KeepsChatsCountAsNull()
    {
        // Arrange
        _databaseService.GetAllChatIds().Throws(new Exception("Database error"));
        _component.SetDatabaseService(_databaseService);

        // Act
        await _component.InvokeLoadStats();

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
        _databaseService.GetDatabaseStats().Returns(new MongoDatabaseStats());
        _component.SetDatabaseService(_databaseService);

        // Act
        await _component.InvokeLoadStats();

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
        _databaseService.GetDatabaseStats().Returns(new MongoDatabaseStats());
        _component.SetDatabaseService(_databaseService);

        // Act
        await _component.InvokeLoadStats();
        var firstCount = _component.ChatsCount;
        await _component.InvokeLoadStats();
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
        var otherDatabaseService = Substitute.For<IMongoDbDatabaseService>();
        otherDatabaseService.Name.Returns("OtherBot");
        var multipleDatabaseServices = new[] { otherDatabaseService, _databaseService };

        var component = new TestableDashboardStats
        {
            BotName = TestBotName,
        };
        component.SetDependencies(multipleDatabaseServices, _logger, _snackbar, _dialogService);

        _databaseService.GetAllChatIds().Returns([123]);
        _databaseService.GetDatabaseStats().Returns(new MongoDatabaseStats());

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
        };
        component.SetDependencies(_databaseServices, _logger, _snackbar, _dialogService);

        _databaseService.GetAllChatIds().Returns([123]);
        _databaseService.GetDatabaseStats().Returns(new MongoDatabaseStats());

        // Act
        await component.InvokeOnInitializedAsync();

        // Assert
        await _databaseService.Received(1).GetAllChatIds();
        Assert.That(component.ChatsCount, Is.EqualTo(1));
    }

    [Test]
    public async Task LoadStatsAsync_SetsDatabaseStats()
    {
        // Arrange
        var expectedStats = new MongoDatabaseStats { Collections = 3, Ok = 1 };
        _databaseService.GetAllChatIds().Returns([123]);
        _databaseService.GetDatabaseStats().Returns(expectedStats);
        _component.SetDatabaseService(_databaseService);

        // Act
        await _component.InvokeLoadStats();

        // Assert
        Assert.That(_component.DbStats, Is.EqualTo(expectedStats));
    }

    [Test]
    public async Task LoadStatsAsync_SetsAdditionalStats()
    {
        // Arrange
        var expectedAdditionalStats = new Dictionary<string, string> { { "Key1", "Value1" }, { "Key2", "Value2" } };
        _databaseService.GetAllChatIds().Returns([123]);
        _databaseService.GetDatabaseStats().Returns(new MongoDatabaseStats());
        _component.SetDatabaseService(_databaseService);
        _component.SetAdditionalStatsResult(expectedAdditionalStats);

        // Act
        await _component.InvokeLoadStats();

        // Assert
        Assert.That(_component.AdditionalStats, Is.EquivalentTo(expectedAdditionalStats));
    }
}
