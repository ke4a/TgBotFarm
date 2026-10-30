using BotFarm.Core.Abstractions;
using BotFarm.Core.Extensions;
using BotFarm.Core.Services;
using BotFarm.Core.Services.WebhookUrlResolvers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using NSubstitute;

namespace BotFarm.Core.UnitTests.Extensions;

[TestFixture]
public class CoreServicesDependencyInjectionTests
{
    [Test]
    public void AddCoreServices_RegistersMongoClientAsSingleton()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MongoDb"] = "mongodb://localhost:27017"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddCoreServices(configuration, Substitute.For<IHostEnvironment>());

        using var provider = services.BuildServiceProvider();

        var firstClient = provider.GetRequiredService<IMongoClient>();
        var secondClient = provider.GetRequiredService<IMongoClient>();

        Assert.That(secondClient, Is.SameAs(firstClient));
    }

    [Test]
    public void AddCoreServices_ThrowsWhenMongoConnectionStringIsMissing()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddCoreServices(configuration, Substitute.For<IHostEnvironment>());

        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<IMongoClient>());

        Assert.That(exception!.Message, Is.EqualTo("MongoDB connection string not found in configuration."));
    }

    [Test]
    public void AddCoreServices_RegistersWebhookSecretProviderAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddCoreServices(new ConfigurationBuilder().Build(), Substitute.For<IHostEnvironment>());

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IWebhookSecretProvider>();
        var second = provider.GetRequiredService<IWebhookSecretProvider>();

        Assert.That(second, Is.SameAs(first));
    }

    [Test]
    public void AddCoreServices_InDevelopment_RegistersTunnelResolversBeforeStaticFallback()
    {
        var resolverTypes = GetResolverTypes(Environments.Development);

        Assert.That(resolverTypes, Is.EqualTo(new[]
        {
            typeof(DevTunnelWebhookUrlResolver),
            typeof(LocalTunnelWebhookUrlResolver),
            typeof(NgrokWebhookUrlResolver),
            typeof(StaticWebhookUrlResolver)
        }));
    }

    [Test]
    public void AddCoreServices_InProduction_RegistersOnlyStaticResolver()
    {
        var resolverTypes = GetResolverTypes(Environments.Production);

        Assert.That(resolverTypes, Is.EqualTo(new[] { typeof(StaticWebhookUrlResolver) }));
    }

    [Test]
    public void AddCoreServices_ResolvesBotControlServiceAsTheCoordinatorSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddCoreServices(new ConfigurationBuilder().Build(), Substitute.For<IHostEnvironment>());

        using var provider = services.BuildServiceProvider();

        Assert.That(
            provider.GetRequiredService<IBotControlService>(),
            Is.SameAs(provider.GetRequiredService<BotControlCoordinator>()));
    }

    private static Type[] GetResolverTypes(string environmentName)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        var services = new ServiceCollection();
        services.AddCoreServices(new ConfigurationBuilder().Build(), environment);

        using var provider = services.BuildServiceProvider();

        return provider.GetServices<IWebhookUrlResolver>().Select(resolver => resolver.GetType()).ToArray();
    }

    [Test]
    public void AddCoreServices_RegistersDataPathsFromConfiguration()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "botfarm-data");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = dataPath })
            .Build();
        var services = new ServiceCollection();
        services.AddCoreServices(configuration, Substitute.For<IHostEnvironment>());

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IAppDataPaths>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(provider.GetRequiredService<IAppDataPaths>(), Is.SameAs(first));
            Assert.That(first.Root, Is.EqualTo(dataPath));
        }
    }

    [Test]
    public void AddCoreServices_RegistersUpdateDeliveryGuardAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddCoreServices(new ConfigurationBuilder().Build(), Substitute.For<IHostEnvironment>());

        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IUpdateDeliveryGuard>();
        var second = provider.GetRequiredService<IUpdateDeliveryGuard>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(second, Is.SameAs(first));
            Assert.That(provider.GetRequiredService<TimeProvider>(), Is.SameAs(TimeProvider.System));
        }
    }
}
