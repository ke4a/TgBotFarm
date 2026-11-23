using BotFarm.Telemetry;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenTelemetry;

namespace BotFarm.UnitTests.Telemetry;

[TestFixture]
public class ExportFailureLogTests
{
    private const string Headers = "Authorization=Basic c2VjcmV0LXRva2Vu";
    private static readonly Uri Endpoint = new("https://otlp.example.com/otlp/v1/metrics?tenant=42");

    private ILogger _logger = null!;
    private ExportFailureLog _log = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = Substitute.For<ILogger>();
        _log = new ExportFailureLog(Endpoint, _logger);
    }

    [Test]
    public void FirstFailure_LogsOneWarningNamingOnlyTheHost()
    {
        _log.Report(ExportResult.Failure);

        var entry = _logger.LoggedEntries().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Message, Does.Contain("otlp.example.com"));
            Assert.That(entry.Message, Does.Not.Contain("/otlp"));
            Assert.That(entry.Message, Does.Not.Contain("tenant"));
        }
    }

    [Test]
    public void RepeatedFailures_LogNothingMore()
    {
        _log.Report(ExportResult.Failure);
        _log.Report(ExportResult.Failure);
        _log.Report(ExportResult.Failure);

        Assert.That(_logger.LoggedEntries(), Has.Count.EqualTo(1));
    }

    [Test]
    public void FirstSuccessAfterAFailure_LogsOneRecoveryLine()
    {
        _log.Report(ExportResult.Failure);

        _log.Report(ExportResult.Success);
        _log.Report(ExportResult.Success);

        var entries = _logger.LoggedEntries();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[1].Level, Is.EqualTo(LogLevel.Information));
            Assert.That(entries[1].Message, Does.Contain("otlp.example.com"));
        }
    }

    [Test]
    public void SuccessWithoutAnEarlierFailure_LogsNothing()
    {
        _log.Report(ExportResult.Success);

        Assert.That(_logger.LoggedEntries(), Is.Empty);
    }

    [Test]
    public void AnotherOutage_LogsAnotherWarning()
    {
        _log.Report(ExportResult.Failure);
        _log.Report(ExportResult.Success);

        _log.Report(ExportResult.Failure);

        Assert.That(_logger.LoggedEntries().Select(entry => entry.Level),
            Is.EqualTo(new[] { LogLevel.Warning, LogLevel.Information, LogLevel.Warning }));
    }

    [Test]
    public void Logs_NeverContainTheHeaders()
    {
        _log.Report(ExportResult.Failure);
        _log.Report(ExportResult.Success);

        Assert.That(_logger.LoggedEntries().Select(entry => entry.Message), Has.None.Contains(Headers));
    }
}
