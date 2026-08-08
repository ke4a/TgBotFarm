using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.TestKit;
using FluentResults;
using FluentScheduler;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BotFarm.UnitTests;

[TestFixture]
public class ScheduledJobsRegistryTests
{
    private IBackupService _backupService;
    private IHostApplicationLifetime _appLifetime;
    private ILogger<ScheduledJobsRegistry> _logger;

    [SetUp]
    public void SetUp()
    {
        _backupService = Substitute.For<IBackupService>();
        _appLifetime = Substitute.For<IHostApplicationLifetime>();
        _logger = Substitute.For<ILogger<ScheduledJobsRegistry>>();
    }

    [Test]
    public void GetJobs_WithMissingShutdownConfiguration_ReturnsOnlyBackupJob()
    {
        var registry = CreateRegistry([new BotIdentity("BotA")], new ConfigurationBuilder().Build());

        var jobs = registry.GetJobs();

        Assert.That(jobs, Has.Length.EqualTo(1));
    }

    [Test]
    public void GetJobs_WithPositiveShutdownConfiguration_ReturnsBackupAndShutdownJobs()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ScheduledJobs:ShutdownEveryHours"] = "6" })
            .Build();
        var registry = CreateRegistry([new BotIdentity("BotA")], configuration);

        var jobs = registry.GetJobs();

        Assert.That(jobs, Has.Length.EqualTo(2));
    }

    [Test]
    public async Task GetJobs_BackupJobIsScheduledDailyAtFiveAndBacksUpEveryRegisteredBot()
    {
        var identities = new[] { new BotIdentity("BotA"), new BotIdentity("BotB") };
        var registry = CreateRegistry(identities, new ConfigurationBuilder().Build());
        _backupService.BackupDatabase(Arg.Any<string>()).Returns(Task.FromResult(Result.Ok()));
        var job = registry.GetJobs().Single();

        job.Start();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(job.NextRun, Is.Not.Null);
            Assert.That(job.NextRun!.Value.Hour, Is.EqualTo(5));
            Assert.That(job.NextRun!.Value.Minute, Is.EqualTo(0));
        }

        job.StopAndBlock(1000);
        await InvokeJob(job);

        await _backupService.Received(1).BackupDatabase("BotA");
        await _backupService.Received(1).BackupDatabase("BotB");
        _appLifetime.Received(1).StopApplication();
    }

    private ScheduledJobsRegistry CreateRegistry(IEnumerable<BotIdentity> identities, IConfiguration configuration)
    {
        return new ScheduledJobsRegistry(_backupService, identities, _appLifetime, configuration, _logger);
    }

    private static Task InvokeJob(Schedule schedule)
    {
        var internalSchedule = ReflectionTestHelper.GetRequiredInstanceFieldValue<object>(schedule, "Internal");
        var job = ReflectionTestHelper.GetRequiredInstanceFieldValue<Func<CancellationToken, Task>>(internalSchedule, "_job");

        return job(CancellationToken.None);
    }
}
