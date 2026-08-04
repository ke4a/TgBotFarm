using MongoDB.Driver;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Creates <see cref="MongoClient"/> instances.
/// </summary>
public interface IMongoClientFactory
{
    MongoClient Create(string connectionString);
}
