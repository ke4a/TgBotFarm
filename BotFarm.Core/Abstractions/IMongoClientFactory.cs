using MongoDB.Driver;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Creates <see cref="IMongoClient"/> instances.
/// </summary>
public interface IMongoClientFactory
{
    /// <summary>
    /// Creates a Mongo client for the supplied connection string.
    /// </summary>
    IMongoClient Create(string connectionString);
}
