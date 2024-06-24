using LiteDB;

namespace BotFarm.Core.Services.Interfaces
{
    public interface IDatabaseService : IHandle
    {
        IEnumerable<string> GetCollectionNames();

        IEnumerable<BsonDocument> GetCollectionData(string collectionName);

        Task<bool> Release();

        Task<bool> Reconnect();
    }
}
