using BotFarm.Core.Abstractions;
using MongoDB.Driver;

namespace BotFarm.Core.Services;

/// <inheritdoc cref="IMongoClientFactory"/>
internal sealed class MongoClientFactory : IMongoClientFactory
{
    public MongoClient Create(string connectionString) => new(connectionString);
}
