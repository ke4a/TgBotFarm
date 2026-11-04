using BotFarm.Core.Abstractions;
using BotFarm.HostedServices;
using BotFarm.TestKit;
using FluentResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using static BotFarm.UnitTests.HostedServices.HostedServiceTestHelpers;

namespace BotFarm.UnitTests.HostedServices;

[TestFixture]
public class NightlyBackupServiceTests
{
    private static readonly DateTimeOffset Start = new(2024, 01, 01, 04, 00, 00, TimeSpan.Zero);

    private IBackupService _backupService = null!;
    private INotificationService _notificationService = null!;
    private ILogger<NightlyBackupService> _logger = null!;
    private ManualTimeProvider _timeProvider = null!;

    [SetUp]
    public void SetUp()
    {
        _backupService = Substitute.For<IBackupService>();
        _backupService.BackupDatabase(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Ok());
        _notificationService = Substitute.For<INotificationService>();
        _logger = Substitute.For<ILogger<NightlyBackupService>>();
        _timeProvider = new ManualTimeProvider(Start);
    }

    private NightlyBackupService CreateService(
        IEnumerable<string>? botNames = null,
        string? backupTime = null,
        string? timeZone = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [NightlyBackupService.BackupTimeKey] = backupTime,
                [NightlyBackupService.TimeZoneKey] = timeZone
            })
            .Build();

        return new NightlyBackupService(
            _backupService,
            (botNames ?? ["BotA"]).Select(TestBotModules.Create),
            _notificationService,
            configuration,
            _timeProvider,
            _logger);
    }

    private int BackupCallCount() =>
        _backupService.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IBackupService.BackupDatabase));

    private async Task RunOnceAsync(NightlyBackupService service, int expectedBackupCalls)
    {
        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _timeProvider.ActiveTimerCount == 1, "the first run is scheduled");

        _timeProvider.Advance(TimeSpan.FromHours(1));

        await WaitUntilAsync(() => BackupCallCount() == expectedBackupCalls, "every bot is backed up");
        await WaitUntilAsync(() => _timeProvider.ActiveTimerCount == 1, "the next run is scheduled");
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    private static TimeZoneInfo Berlin => TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [Test]
    public void GetNextRun_BeforeTodaysTime_ReturnsToday()
    {
        var next = NightlyBackupService.GetNextRun(Utc(2024, 1, 10, 3, 0), new TimeOnly(5, 0), TimeZoneInfo.Utc);

        Assert.That(next, Is.EqualTo(Utc(2024, 1, 10, 5, 0)));
    }

    [TestCase(5, 0)]
    [TestCase(6, 30)]
    public void GetNextRun_AtOrAfterTodaysTime_ReturnsTomorrow(int hour, int minute)
    {
        var next = NightlyBackupService.GetNextRun(Utc(2024, 1, 10, hour, minute), new TimeOnly(5, 0), TimeZoneInfo.Utc);

        Assert.That(next, Is.EqualTo(Utc(2024, 1, 11, 5, 0)));
    }

    [Test]
    public void GetNextRun_InOtherTimeZone_UsesThatZonesClock()
    {
        // 03:30 UTC is 04:30 in Berlin in winter (UTC+1), so 05:00 Berlin is 04:00 UTC the same day.
        var next = NightlyBackupService.GetNextRun(Utc(2024, 1, 10, 3, 30), new TimeOnly(5, 0), Berlin);

        Assert.That(next, Is.EqualTo(Utc(2024, 1, 10, 4, 0)));
    }

    [Test]
    public void GetNextRun_InOtherTimeZone_UsesThatZonesDate()
    {
        // 23:30 UTC on Jan 10 is already 00:30 on Jan 11 in Berlin, so 00:15 Berlin comes next on Jan 12.
        var next = NightlyBackupService.GetNextRun(Utc(2024, 1, 10, 23, 30), new TimeOnly(0, 15), Berlin);

        Assert.That(next, Is.EqualTo(Utc(2024, 1, 11, 23, 15)));
    }

    [Test]
    public void GetNextRun_OnDayClocksMoveForward_UsesNewOffset()
    {
        // Berlin switches to UTC+2 at 02:00 local on 2024-03-31.
        var next = NightlyBackupService.GetNextRun(Utc(2024, 3, 30, 12, 0), new TimeOnly(5, 0), Berlin);

        Assert.That(next, Is.EqualTo(Utc(2024, 3, 31, 3, 0)));
    }

    [Test]
    public void GetNextRun_TimeSkippedByClocksMovingForward_RunsRightAfterTheJump()
    {
        // 02:30 does not exist in Berlin on 2024-03-31; it runs at 03:30 local (01:30 UTC).
        var next = NightlyBackupService.GetNextRun(Utc(2024, 3, 30, 12, 0), new TimeOnly(2, 30), Berlin);

        Assert.That(next, Is.EqualTo(Utc(2024, 3, 31, 1, 30)));
    }

    [Test]
    public void GetNextRun_TimeRepeatedByClocksMovingBack_RunsOnlyAtFirstOccurrence()
    {
        // 02:30 happens twice in Berlin on 2024-10-27: at 00:30 UTC (UTC+2) and 01:30 UTC (UTC+1).
        var time = new TimeOnly(2, 30);

        var first = NightlyBackupService.GetNextRun(Utc(2024, 10, 26, 12, 0), time, Berlin);
        var afterFirst = NightlyBackupService.GetNextRun(first, time, Berlin);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.EqualTo(Utc(2024, 10, 27, 0, 30)));
            Assert.That(afterFirst, Is.EqualTo(Utc(2024, 10, 28, 1, 30)));
        }
    }

    [TestCase("25:00")]
    [TestCase("5:00")]
    [TestCase("05:00:00")]
    [TestCase("five")]
    public void Constructor_InvalidBackupTime_ThrowsNamingTheSetting(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreateService(backupTime: value));

        Assert.That(exception!.Message, Does.Contain(NightlyBackupService.BackupTimeKey).And.Contain(value));
    }

    [Test]
    public void Constructor_InvalidTimeZone_ThrowsNamingTheSetting()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CreateService(timeZone: "Mars/Olympus_Mons"));

        Assert.That(exception!.Message, Does.Contain(NightlyBackupService.TimeZoneKey).And.Contain("Mars/Olympus_Mons"));
    }

    [Test]
    public async Task ExecuteAsync_LogsNextRunAtStartupAndAfterEachRun()
    {
        using var service = CreateService();

        await RunOnceAsync(service, expectedBackupCalls: 1);
        await service.StopAsync(CancellationToken.None);

        var scheduleLogs = _logger.LoggedEntries()
            .Where(entry => entry.Level == LogLevel.Information && entry.Message.StartsWith("Next nightly backup"))
            .Select(entry => entry.Message)
            .ToList();
        Assert.That(scheduleLogs, Is.EqualTo(new[]
        {
            "Next nightly backup is scheduled for 2024-01-01 05:00 +00:00 (UTC).",
            "Next nightly backup is scheduled for 2024-01-02 05:00 +00:00 (UTC)."
        }));
    }

    [Test]
    public async Task ExecuteAsync_WhenOneBotThrows_BacksUpTheOthersAndAlertsOnce()
    {
        _backupService.BackupDatabase("BotB", Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("disk full"));
        using var service = CreateService(["BotA", "BotB", "BotC"]);

        await RunOnceAsync(service, expectedBackupCalls: 3);
        await service.StopAsync(CancellationToken.None);

        await _backupService.Received(1).BackupDatabase("BotA", Arg.Any<CancellationToken>());
        await _backupService.Received(1).BackupDatabase("BotC", Arg.Any<CancellationToken>());
        await _notificationService.Received(1).SendErrorNotification(Arg.Any<string>(), Arg.Any<string>());
        await _notificationService.Received(1).SendErrorNotification(
            Arg.Is<string>(text => text.Contains("disk full")), "BotB");
        Assert.That(
            _logger.LoggedEntries().Count(entry => entry.Level == LogLevel.Error && entry.Exception is InvalidOperationException),
            Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteAsync_WhenAlertFails_KeepsBackingUpAndScheduling()
    {
        _backupService.BackupDatabase("BotA", Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));
        _notificationService.SendErrorNotification(Arg.Any<string>(), Arg.Any<string>())
            .ThrowsAsync(new HttpRequestException("Telegram down"));
        using var service = CreateService(["BotA", "BotB"]);

        await RunOnceAsync(service, expectedBackupCalls: 2);

        Assert.That(service.ExecuteTask!.IsCompleted, Is.False);
        await service.StopAsync(CancellationToken.None);
        await _backupService.Received(1).BackupDatabase("BotB", Arg.Any<CancellationToken>());
        Assert.That(
            _logger.LoggedEntries().Any(entry => entry.Level == LogLevel.Warning && entry.Exception is HttpRequestException),
            Is.True);
    }

    [Test]
    public async Task ExecuteAsync_WhenBackupReturnsFailure_LogsWarningWithoutAlert()
    {
        _backupService.BackupDatabase("BotA", Arg.Any<CancellationToken>()).Returns(Result.Fail("pause failed"));
        using var service = CreateService();

        await RunOnceAsync(service, expectedBackupCalls: 1);
        await service.StopAsync(CancellationToken.None);

        await _notificationService.DidNotReceiveWithAnyArgs().SendErrorNotification(default!, default!);
        Assert.That(
            _logger.LoggedEntries().Any(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("pause failed")),
            Is.True);
    }

    [Test]
    public async Task ExecuteAsync_StoppedDuringBackup_CancelsBackupAndEndsWithoutError()
    {
        var backupToken = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        _backupService.BackupDatabase("BotA", Arg.Any<CancellationToken>()).Returns(async call =>
        {
            var token = call.ArgAt<CancellationToken>(1);
            backupToken.TrySetResult(token);
            await Task.Delay(Timeout.Infinite, token);
            return Result.Ok();
        });
        using var service = CreateService(["BotA", "BotB"]);
        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _timeProvider.ActiveTimerCount == 1, "the first run is scheduled");
        _timeProvider.Advance(TimeSpan.FromHours(1));
        var token = await backupToken.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await service.StopAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(service.ExecuteTask!.IsCompletedSuccessfully, Is.True);
        }
        await _backupService.DidNotReceive().BackupDatabase("BotB", Arg.Any<CancellationToken>());
        await _notificationService.DidNotReceiveWithAnyArgs().SendErrorNotification(default!, default!);
        Assert.That(_logger.LoggedEntries().Any(entry => entry.Level >= LogLevel.Warning), Is.False);
    }

    [Test]
    public async Task ExecuteAsync_StoppedWhileWaiting_EndsWithoutBackingUp()
    {
        using var service = CreateService();
        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => _timeProvider.ActiveTimerCount == 1, "the first run is scheduled");

        await service.StopAsync(CancellationToken.None);

        Assert.That(service.ExecuteTask!.IsCompletedSuccessfully, Is.True);
        Assert.That(BackupCallCount(), Is.Zero);
    }
}
