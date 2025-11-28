using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BotFarm.Core.Abstractions;

public abstract class MongoDbDatabaseService : IMongoDbDatabaseService
{
    protected readonly ILogger<MongoDbDatabaseService> _logger;
    protected readonly IHostApplicationLifetime _appLifetime;
    protected readonly INotificationService _notificationService;

    protected string logPrefix = $"[{nameof(MongoDbDatabaseService)}]";

    protected IMongoDatabase Instance { get; set; }

    protected MongoClient Client { get; }

    public string Name { get; protected set; }

    public string DatabaseName { get; protected set; }

    public MongoDbDatabaseService(
        ILogger<MongoDbDatabaseService> logger,
        IHostApplicationLifetime appLifetime,
        INotificationService notificationService,
        IConfiguration configuration)
    {
        _logger = logger;
        _appLifetime = appLifetime;
        _notificationService = notificationService;
        var connectionString = configuration?.GetConnectionString("MongoDb")
            ?? throw new InvalidOperationException("MongoDB connection string not found in configuration.");
        Client = new MongoClient(connectionString);
    }

    public virtual IEnumerable<string> GetCollectionNames()
    {
        try
        {
            return Instance.ListCollectionNames().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError($"{logPrefix} Error getting collection names: '{ex.Message}'");
            return Enumerable.Empty<string>();
        }
    }

    public virtual IEnumerable<BsonDocument> GetCollectionData(string collectionName)
    {
        try
        {
            var collection = Instance.GetCollection<BsonDocument>(collectionName);
            return collection.Find(Builders<BsonDocument>.Filter.Empty).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError($"{logPrefix} Error getting collection data for '{collectionName}': '{ex.Message}'");
            return Enumerable.Empty<BsonDocument>();
        }
    }

    public virtual async Task<bool> Disconnect()
    {
        Instance = null;
        _logger.LogInformation($"{logPrefix} Disconnected from database {DatabaseName}.");

        return await Task.FromResult(true);
    }

    public virtual async Task<bool> Reconnect()
    {
        try
        {
            Instance = Client.GetDatabase(DatabaseName);
            
            // Test the connection
            await Instance.RunCommandAsync((Command<BsonDocument>)"{ping:1}");
            
            _logger.LogInformation($"{logPrefix} Reconnected to database {DatabaseName}.");

            return true;
        }
        catch (Exception ex)
        {
            var message = $"{logPrefix} Could not reconnect to database. Error: '{ex.Message}'";
            _logger.LogError(message);
            await _notificationService.SendErrorNotification(message, Name);
            _logger.LogWarning("Stopping application...");
            _appLifetime.StopApplication();

            return false;
        }
    }

    public async Task<bool> DropCollection(string collectionName)
    {
        try
        {
            await Instance.DropCollectionAsync(collectionName);

            return true;
        }
        catch (Exception ex)
        {
            var message = $"{logPrefix} Could not drop collection '{collectionName}'. Error: '{ex.Message}'";
            _logger.LogError(message);
            await _notificationService.SendErrorNotification(message, Name);

            return false;
        }
    }

    public async Task<bool> CreateAndPopulateCollection(string collectionName, IEnumerable<BsonDocument> data)
    {
        try
        {
            var collection = Instance.GetCollection<BsonDocument>(collectionName);
            await collection.InsertManyAsync(data);

            return true;
        }
        catch (Exception ex)
        {
            var message = $"{logPrefix} Could not create and populate collection '{collectionName}'. Error: '{ex.Message}'";
            _logger.LogError(message);
            await _notificationService.SendErrorNotification(message, Name);

            return false;
        }
    }
}
