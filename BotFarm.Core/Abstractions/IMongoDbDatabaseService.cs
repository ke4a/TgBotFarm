using BotFarm.Core.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace BotFarm.Core.Abstractions;

public interface IMongoDbDatabaseService : IDatabaseService
{
    IEnumerable<string> GetCollectionNames();

    IEnumerable<BsonDocument> GetCollectionData(string collectionName);

    Task<bool> DropCollection(string collectionName);

    Task<bool> CreateAndPopulateCollection(string collectionName, IEnumerable<BsonDocument> data);

    Task<long> GetCollectionDocumentCount(string collectionName);

    Task<MongoDatabaseStats?> GetDatabaseStats();

    Task<TSettings> SaveChatSettings<TSettings>(TSettings settings) where TSettings : ChatSettings;

    Task<TSettings> UpdateChatSettings<TSettings>(long chatId, UpdateDefinition<TSettings> update) where TSettings : ChatSettings;
}
