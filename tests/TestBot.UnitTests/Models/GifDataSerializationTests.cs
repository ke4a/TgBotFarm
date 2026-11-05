using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using TestBot.Models;

namespace TestBot.UnitTests.Models;

[TestFixture]
public class GifDataSerializationTests
{
    [Test]
    public void GifData_SerializesCompoundIdWithChatIdFirst()
    {
        var gifData = new GifData { Id = new GifDataKey { ChatId = -100123, UserId = 42 }, FileId = "f" };

        var document = gifData.ToBsonDocument();

        var expected = new BsonDocument
        {
            { "_id", new BsonDocument { { "ChatId", -100123L }, { "UserId", 42L } } },
            { "FileId", "f" }
        };
        Assert.That(document, Is.EqualTo(expected));
        Assert.That(document["_id"].AsBsonDocument.GetElement(0).Name, Is.EqualTo("ChatId"));
    }

    [Test]
    public void GifData_DeserializesCompoundId()
    {
        var document = new BsonDocument
        {
            { "_id", new BsonDocument { { "ChatId", -100123L }, { "UserId", 42L } } },
            { "FileId", "f" }
        };

        var gifData = BsonSerializer.Deserialize<GifData>(document);

        Assert.That(gifData.Id.ChatId, Is.EqualTo(-100123));
        Assert.That(gifData.Id.UserId, Is.EqualTo(42));
        Assert.That(gifData.FileId, Is.EqualTo("f"));
    }

    [Test]
    public void GifData_DeserializesRestoredDocument()
    {
        var backedUp = new BsonDocument
        {
            { "_id", new BsonDocument { { "ChatId", -100777L }, { "UserId", 99L } } },
            { "FileId", "restored-file" }
        };
        var restored = BsonSerializer.Deserialize<BsonDocument>(backedUp.ToBson());

        var gifData = BsonSerializer.Deserialize<GifData>(restored);

        Assert.That(gifData.Id.ChatId, Is.EqualTo(-100777));
        Assert.That(gifData.Id.UserId, Is.EqualTo(99));
        Assert.That(gifData.FileId, Is.EqualTo("restored-file"));
    }
}
