using MongoDB.Driver;

namespace BotFarm.Core.Extensions;

internal static class MongoCollectionCountExtensions
{
    /// <summary>
    /// Returns either an estimated or exact count for a MongoDB collection.
    /// </summary>
    /// <param name="collection">The collection to count.</param>
    /// <param name="estimated">When true, returns a fast estimate; otherwise, counts documents exactly.</param>
    public static Task<long> GetDocumentCountAsync<TDocument>(
        this IMongoCollection<TDocument> collection,
        bool estimated)
    {
        return estimated
            ? collection.EstimatedDocumentCountAsync()
            : collection.CountDocumentsAsync(Builders<TDocument>.Filter.Empty);
    }
}
