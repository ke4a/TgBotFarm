using MongoDB.Driver;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Creates <see cref="IMongoClient"/> instances.
/// </summary>
public interface IMongoClientFactory
{
    IMongoClient Create(string connectionString);
}
