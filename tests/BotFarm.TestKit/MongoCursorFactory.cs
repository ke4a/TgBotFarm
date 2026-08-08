using MongoDB.Driver;
using NSubstitute;

namespace BotFarm.TestKit;

public static class MongoCursorFactory
{
    public static IAsyncCursor<T> Create<T>(IEnumerable<T> items)
    {
        var materializedItems = items as IReadOnlyCollection<T> ?? items.ToArray();
        var cursor = Substitute.For<IAsyncCursor<T>>();
        cursor.Current.Returns(materializedItems);
        cursor.MoveNext(Arg.Any<CancellationToken>()).Returns(materializedItems.Count > 0, false);
        cursor.MoveNextAsync(Arg.Any<CancellationToken>()).Returns(materializedItems.Count > 0, false);
        return cursor;
    }
}
