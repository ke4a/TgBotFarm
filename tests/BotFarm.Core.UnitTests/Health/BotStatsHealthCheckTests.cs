using BotFarm.Core.Abstractions;
using BotFarm.Core.Health;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BotFarm.Core.UnitTests.Health;

[TestFixture]
public class BotStatsHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_WhenChatCountLoads_ReturnsHealthyResultWithChatCount()
    {
        var databaseService = Substitute.For<IDatabaseService>();
        databaseService.GetChatCount().Returns(Task.FromResult(7L));
        var healthCheck = new BotStatsHealthCheck(databaseService, "Bot1");

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
            Assert.That(result.Data["ChatsCount"], Is.EqualTo(7L));
            Assert.That(result.Description, Is.EqualTo("Reports Bot1 stats."));
            Assert.That(result.Exception, Is.Null);
        }
    }

    [Test]
    public async Task CheckHealthAsync_WhenChatCountFails_ReturnsUnhealthyResultWithException()
    {
        var databaseService = Substitute.For<IDatabaseService>();
        var exception = new InvalidOperationException("boom");
        databaseService.GetChatCount().Returns(Task.FromException<long>(exception));
        var healthCheck = new BotStatsHealthCheck(databaseService, "Bot1");

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
            Assert.That(result.Exception, Is.SameAs(exception));
            Assert.That(result.Data, Is.Empty);
        }
    }

    [Test]
    public async Task AddBotStatsHealthCheck_RegistersNamedTaggedCheckResolvingDatabaseService()
    {
        var databaseService = Substitute.For<IDatabaseService>();
        databaseService.GetChatCount().Returns(Task.FromResult(2L));
        var services = new ServiceCollection();
        services.AddSingleton(databaseService);
        services.AddHealthChecks().AddBotStatsHealthCheck<IDatabaseService>("Bot1");
        await using var provider = services.BuildServiceProvider();

        var registration = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Single();
        var instance = registration.Factory(provider);
        var result = await instance.CheckHealthAsync(new HealthCheckContext { Registration = registration });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(registration.Name, Is.EqualTo("Bot1Stats"));
            Assert.That(registration.Tags, Is.EquivalentTo(new[] { "Bot1" }));
            Assert.That(registration.FailureStatus, Is.EqualTo(HealthStatus.Unhealthy));
            Assert.That(result.Data["ChatsCount"], Is.EqualTo(2L));
        }
    }
}
