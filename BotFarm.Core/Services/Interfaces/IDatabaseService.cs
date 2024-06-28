using LiteDB;

namespace BotFarm.Core.Services.Interfaces
{
    public interface IDatabaseService : IName
    {
        IEnumerable<string> GetCollectionNames();

        IEnumerable<BsonDocument> GetCollectionData(string collectionName);

        Task<bool> Release();

        Task<bool> Reconnect();
    }
}
