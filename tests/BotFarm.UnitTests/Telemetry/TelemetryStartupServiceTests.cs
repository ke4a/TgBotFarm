using System.Diagnostics.Metrics;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Telemetry;
using BotFarm.Health;
using BotFarm.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BotFarm.UnitTests.Telemetry;

[TestFixture]
public class TelemetryStartupServiceTests
{
    private const string EnabledBot = "EnabledBot";
    private const string DisabledBot = "DisabledBot";

    private ServiceProvider _provider = null!;
    private MetricCollector<long> _updates = null!;
    private MetricCollector<long> _outcomes = null!;
    private MetricCollector<long> _commands = null!;
    private MetricCollector<long> _events = null!;
    private FakeLogger<TelemetryStartupService> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection().AddMetrics();
        services.AddSingleton<UpdateMetrics>();
        services.AddSingleton<BotEventMetrics>();
        services.AddSingleton(Substitute.For<IOptionsMonitor<MemoryCheckOptions>>());
        services.AddSingleton<AppMemoryMetrics>();
        AddUpdateService(services, EnabledBot, canProcessUpdates: true, ["start", "changelanguage", "getlastgif"]);
        AddUpdateService(services, DisabledBot, canProcessUpdates: false, ["start", "changelanguage"]);
        _provider = services.BuildServiceProvider();

        var meterFactory = _provider.GetRequiredService<IMeterFactory>();
        _updates = new MetricCollector<long>(meterFactory, UpdateMetrics.MeterName, "botfarm.updates");
        _outcomes = new MetricCollector<long>(meterFactory, UpdateMetrics.MeterName, "botfarm.update.outcomes");
        _commands = new MetricCollector<long>(meterFactory, UpdateMetrics.MeterName, "botfarm.commands");
        _events = new MetricCollector<long>(meterFactory, UpdateMetrics.MeterName, "botfarm.bot.events");
        _logger = new FakeLogger<TelemetryStartupService>();
    }

    [TearDown]
    public void TearDown()
    {
        _updates.Dispose();
        _outcomes.Dispose();
        _commands.Dispose();
        _events.Dispose();
        _provider.Dispose();
    }

    [Test]
    public async Task StartAsync_RecordsZerosOnceForEveryRegisteredBotWithItsCommandKeys()
    {
        var service = CreateService(EnabledBot, DisabledBot);

        await service.StartAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(CommandsOf(EnabledBot),
                Is.EquivalentTo(new[] { "start", "changelanguage", "getlastgif", "(unknown)" }));
            Assert.That(CommandsOf(DisabledBot), Is.EquivalentTo(new[] { "start", "changelanguage", "(unknown)" }));
            Assert.That(MeasurementsOf(_updates, EnabledBot), Has.Count.EqualTo(3));
            Assert.That(MeasurementsOf(_updates, DisabledBot), Has.Count.EqualTo(3));
            Assert.That(MeasurementsOf(_outcomes, EnabledBot), Has.Count.EqualTo(5));
            Assert.That(MeasurementsOf(_outcomes, DisabledBot), Has.Count.EqualTo(5));
            Assert.That(MeasurementsOf(_events, EnabledBot), Has.Count.EqualTo(Enum.GetValues<BotEvent>().Length));
            Assert.That(MeasurementsOf(_events, DisabledBot), Has.Count.EqualTo(Enum.GetValues<BotEvent>().Length));
            Assert.That(
                _updates.GetMeasurementSnapshot().Concat(_outcomes.GetMeasurementSnapshot())
                    .Concat(_commands.GetMeasurementSnapshot())
                    .Concat(_events.GetMeasurementSnapshot())
                    .Select(m => m.Value),
                Is.All.Zero);
        }
    }

    [Test]
    public async Task StartAsync_WhenABotsUpdateServiceCannotBeResolved_StillRecordsZerosForTheOthers()
    {
        var service = CreateService("MissingBot", EnabledBot);

        await service.StartAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(CommandsOf(EnabledBot), Has.Count.EqualTo(4));
            Assert.That(MeasurementsOf(_updates, "MissingBot"), Is.Empty);
            Assert.That(_logger.Collector.GetSnapshot().Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Warning }));
        }
    }

    private TelemetryStartupService CreateService(params string[] botNames)
    {
        return new TelemetryStartupService(
            botNames.Select(TestBotModules.Create).ToArray(),
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _provider.GetRequiredService<UpdateMetrics>(),
            _provider.GetRequiredService<BotEventMetrics>(),
            _provider.GetRequiredService<AppMemoryMetrics>(),
            _logger);
    }

    private static void AddUpdateService(
        IServiceCollection services, string botName, bool canProcessUpdates, string[] commandKeys)
    {
        services.AddKeyedScoped(botName, (_, _) =>
        {
            var updateService = Substitute.For<IUpdateService>();
            updateService.Name.Returns(botName);
            updateService.CanProcessUpdates.Returns(canProcessUpdates);
            updateService.CommandKeys.Returns(commandKeys);
            return updateService;
        });
    }

    private List<string?> CommandsOf(string botName)
    {
        return MeasurementsOf(_commands, botName).Select(m => (string?)m.Tags["command"]).ToList();
    }

    private static IReadOnlyList<CollectedMeasurement<long>> MeasurementsOf(MetricCollector<long> collector,
        string botName)
    {
        return collector.GetMeasurementSnapshot().Where(m => Equals(m.Tags["bot"], botName)).ToList();
    }
}
