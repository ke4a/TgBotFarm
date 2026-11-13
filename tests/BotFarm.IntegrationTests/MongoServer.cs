using MongoDB.Driver;
using Testcontainers.MongoDb;

namespace BotFarm.IntegrationTests;

/// <summary>
/// Runs one MongoDB server in a Docker container for every test in this assembly. Tests keep apart by using their
/// own databases, or by dropping the databases they use before they start.
/// </summary>
[SetUpFixture]
public sealed class MongoServer
{
    /// <summary>
    /// The MongoDB image the app runs against in docker-compose.yml.
    /// </summary>
    public const string Image = "mongo:8.3.11";

    private static MongoDbContainer? _container;
    private static MongoClient? _client;

    /// <summary>
    /// The connection string of the running server.
    /// </summary>
    public static string ConnectionString =>
        _container?.GetConnectionString()
        ?? throw new InvalidOperationException("The MongoDB container has not been started.");

    /// <summary>
    /// A client connected to the running server, shared by all tests.
    /// </summary>
    public static IMongoClient Client =>
        _client ?? throw new InvalidOperationException("The MongoDB container has not been started.");

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        _container = new MongoDbBuilder(Image).Build();
        await _container.StartAsync();
        _client = new MongoClient(_container.GetConnectionString());
    }

    [OneTimeTearDown]
    public async Task StopAsync()
    {
        _client?.Dispose();
        _client = null;

        if (_container != null)
        {
            await _container.DisposeAsync();
            _container = null;
        }
    }
}
