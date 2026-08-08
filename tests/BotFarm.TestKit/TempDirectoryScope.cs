namespace BotFarm.TestKit;

public sealed class TempDirectoryScope : IDisposable
{
    public TempDirectoryScope(string? prefix = null)
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"{prefix ?? "BotFarmTests"}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public string GetPath(params string[] segments)
    {
        return segments.Aggregate(RootPath, Path.Combine);
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, true);
        }
    }
}
