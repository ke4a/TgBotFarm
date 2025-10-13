using LiteDB;

namespace BotFarm.Core.Abstractions
{
    public interface ILiteDbDatabaseService : IDatabaseService
    {
        IEnumerable<string> GetCollectionNames();

        IEnumerable<BsonDocument> GetCollectionData(string collectionName);
    }
}
