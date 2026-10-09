namespace BotFarm.TestKit;

public static class AsyncEnumerableFactory
{
    public static async IAsyncEnumerable<T> Create<T>(IEnumerable<T> items)
    {
        await Task.CompletedTask;

        foreach (var item in items)
        {
            yield return item;
        }
    }
}
