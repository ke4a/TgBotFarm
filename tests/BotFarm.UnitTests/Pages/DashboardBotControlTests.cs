using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Pages;
using BotFarm.TestKit;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BotFarm.UnitTests.Pages;

[TestFixture]
public class DashboardBotControlTests
{
    private TestableDashboard _dashboard = default!;
    private IBotControlService _controlService = default!;

    [SetUp]
    public void SetUp()
    {
        _controlService = Substitute.For<IBotControlService>();
        _dashboard = new TestableDashboard();
        _dashboard.SetDependencies(_controlService, Substitute.For<ILogger<Dashboard>>());
    }

    [Test]
    public void LoadBotStatuses_PreservesUnknownAppliedAndErrorStates()
    {
        _controlService.GetStatuses().Returns(
        [
            new BotRuntimeStatus("UnknownBot", null, null, BotRuntimeState.Unknown, null, null),
            new BotRuntimeStatus("AppliedBot", false, false, BotRuntimeState.Applied, DateTime.UtcNow, null),
            new BotRuntimeStatus("ErrorBot", true, false, BotRuntimeState.Error, DateTime.UtcNow,
                "Transition failed after 3 attempts.")
        ]);

        _dashboard.RefreshBotStatuses();

        Assert.That(
            _dashboard.Statuses.Select(status => status.Status),
            Is.EqualTo(new[] { BotRuntimeState.Unknown, BotRuntimeState.Applied, BotRuntimeState.Error }));
    }

    [Test]
    public async Task LoadLogs_ListsFilesFromConfiguredLogsFolder()
    {
        using var scope = new TempDirectoryScope();
        var logsFolder = scope.GetPath("logs");
        Directory.CreateDirectory(logsFolder);
        await File.WriteAllTextAsync(Path.Combine(logsFolder, "nlog-all.log"), "entry");
        var dataPaths = Substitute.For<IAppDataPaths>();
        dataPaths.Logs.Returns(logsFolder);
        _dashboard.SetDataPaths(dataPaths);

        await _dashboard.ReloadLogs();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_dashboard.LogsDirectory, Is.EqualTo(logsFolder));
            Assert.That(_dashboard.LogFileNames, Is.EqualTo(new[] { "nlog-all.log" }));
        }
    }

    private sealed class TestableDashboard : Dashboard
    {
        public string? LogsDirectory => GetField<string?>("_logsDirectory");
        public IEnumerable<string> LogFileNames =>
            ((System.Collections.IEnumerable)GetField<object>("_logFiles")).Cast<object>()
            .Select(entry => (string)entry.GetType().GetProperty("Name")!.GetValue(entry)!);

        public void SetDataPaths(IAppDataPaths dataPaths) => DataPaths = dataPaths;
        public Task ReloadLogs() => LoadLogs();

        public IReadOnlyList<BotRuntimeStatus> Statuses => GetField<List<BotRuntimeStatus>>("_botStatuses");

        public void SetDependencies(IBotControlService controlService, ILogger<Dashboard> logger)
        {
            BotControlService = controlService;
            Logger = logger;
        }

        public void RefreshBotStatuses() => LoadBotStatuses();

        private T GetField<T>(string name)
        {
            var field = typeof(Dashboard).GetField(
                name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return (T)field!.GetValue(this)!;
        }
    }
}
