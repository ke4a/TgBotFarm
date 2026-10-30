using BotFarm.Core.Services;
using Microsoft.Extensions.Configuration;

namespace BotFarm.Core.UnitTests.Services;

[TestFixture]
public class AppDataPathsTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Root_WhenDataPathIsEmpty_IsAppFolder(string? dataPath)
    {
        var paths = CreatePaths(dataPath);

        Assert.That(paths.Root, Is.EqualTo(AppContext.BaseDirectory));
    }

    [Test]
    public void Root_WithAbsoluteDataPath_UsesItAsIs()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), "botfarm-data");

        var paths = CreatePaths(dataPath);

        Assert.That(paths.Root, Is.EqualTo(dataPath));
    }

    [Test]
    public void Root_WithRelativeDataPath_ResolvesAgainstAppFolder()
    {
        var paths = CreatePaths(Path.Combine("..", "data"));

        Assert.That(
            paths.Root,
            Is.EqualTo(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "data"))));
    }

    [Test]
    public void Subfolders_AreInsideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "botfarm-data");

        var paths = CreatePaths(root);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(paths.Backups, Is.EqualTo(Path.Combine(root, "backups")));
            Assert.That(paths.Logs, Is.EqualTo(Path.Combine(root, "logs")));
            Assert.That(paths.Temp, Is.EqualTo(Path.Combine(root, "tmp")));
            Assert.That(paths.DataProtectionKeys, Is.EqualTo(Path.Combine(root, "dpkeys")));
        }
    }

    [Test]
    public void Constructor_DoesNotCreateFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), $"botfarm-data-{Guid.NewGuid():N}");

        _ = CreatePaths(root);

        Assert.That(Directory.Exists(root), Is.False);
    }

    private static AppDataPaths CreatePaths(string? dataPath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [AppDataPaths.ConfigurationKey] = dataPath })
            .Build();

        return new AppDataPaths(configuration);
    }
}
