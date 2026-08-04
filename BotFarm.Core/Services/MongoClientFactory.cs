using BotFarm.Core.Abstractions;
using MongoDB.Driver;

namespace BotFarm.Core.Services;

/// <inheritdoc cref="IMongoClientFactory"/>
internal sealed class MongoClientFactory : IMongoClientFactory
{
    public IMongoClient Create(string connectionString) => new MongoClient(connectionString);
}
