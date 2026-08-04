using BotFarm.Core.Models;
using MongoDB.Bson;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Extends <see cref="IDatabaseService"/> with low-level MongoDB backup and inspection operations.
/// </summary>
public interface IMongoDbDatabaseService : IDatabaseService
{
    /// <summary>
    /// Lists collection names in the bot database.
    /// </summary>
    IEnumerable<string> GetCollectionNames();

    /// <summary>
    /// Reads all documents from the named collection.
    /// </summary>
    IEnumerable<BsonDocument> GetCollectionData(string collectionName);

    /// <summary>
    /// Deletes the named collection.
    /// </summary>
    Task<bool> DropCollection(string collectionName);

    /// <summary>
    /// Creates the named collection content from raw BSON documents.
    /// </summary>
    Task<bool> CreateAndPopulateCollection(string collectionName, IEnumerable<BsonDocument> data);

    /// <summary>
    /// Counts documents in the named collection.
    /// </summary>
    Task<long> GetCollectionDocumentCount(string collectionName);

    /// <summary>
    /// Retrieves size and collection statistics for the current database.
    /// </summary>
    Task<MongoDatabaseStats?> GetDatabaseStats();
}
