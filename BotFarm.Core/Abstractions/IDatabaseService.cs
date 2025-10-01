using LiteDB;

namespace BotFarm.Core.Abstractions;

public interface IDatabaseService : INamedService
{
    IEnumerable<string> GetCollectionNames();

    IEnumerable<BsonDocument> GetCollectionData(string collectionName);

    Task<bool> Release();

    Task<bool> Reconnect();
}
