using BotFarm.Core.Extensions;
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
}
