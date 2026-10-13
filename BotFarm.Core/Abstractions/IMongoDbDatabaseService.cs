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
    Task<IEnumerable<string>> GetCollectionNames();

    /// <summary>
    /// Reads all documents from the named collection.
    /// </summary>
    IAsyncEnumerable<BsonDocument> GetCollectionData(string collectionName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the named collection.
    /// </summary>
    Task<bool> DropCollection(string collectionName);

    /// <summary>
    /// Creates the named collection content from raw BSON documents.
    /// </summary>
    Task<bool> CreateAndPopulateCollection(string collectionName, IEnumerable<BsonDocument> data);

    /// <summary>
    /// Renames a collection, optionally replacing the destination collection.
    /// </summary>
    Task<bool> RenameCollection(string sourceCollectionName, string targetCollectionName, bool dropTarget);

    /// <summary>
    /// Returns the number of documents in the named collection.
    /// </summary>
    /// <param name="collectionName">The collection to count.</param>
    /// <param name="estimated">When true, returns a fast estimate; otherwise, returns an exact count.</param>
    Task<long> GetCollectionDocumentCount(string collectionName, bool estimated = true);

    /// <summary>
    /// Retrieves size and collection statistics for the current database.
    /// </summary>
    Task<MongoDatabaseStats?> GetDatabaseStats();
}
