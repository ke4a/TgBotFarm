using BotFarm.TestKit;
using Microsoft.Extensions.Configuration;
using NLog;
using NLog.Extensions.Logging;

namespace BotFarm.UnitTests.Logging;

[TestFixture]
public class NLogConfigTests
{
    [Test]
    public void LogFiles_AreWrittenToLogsFolderInsideAbsoluteDataPath()
    {
        using var scope = new TempDirectoryScope();

        WriteLogEntry(scope.RootPath);

        Assert.That(File.Exists(scope.GetPath("logs", "nlog-all.log")), Is.True);
    }

    [Test]
    public void LogFiles_AreWrittenToLogsFolderInsideRelativeDataPath()
    {
        var relativeDataPath = $"nlog-test-{Guid.NewGuid():N}";
        var expectedRoot = Path.Combine(AppContext.BaseDirectory, relativeDataPath);
        try
        {
            WriteLogEntry(relativeDataPath);

            Assert.That(File.Exists(Path.Combine(expectedRoot, "logs", "nlog-all.log")), Is.True);
        }
        finally
        {
            if (Directory.Exists(expectedRoot))
            {
                Directory.Delete(expectedRoot, true);
            }
        }
    }

    private static void WriteLogEntry(string dataPath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DataPath"] = dataPath })
            .Build();
        var logFactory = new LogFactory();
        try
        {
            logFactory.Setup()
                .SetupExtensions(extensions => extensions.RegisterConfigSettings(configuration))
                .LoadConfigurationFromFile(Path.Combine(AppContext.BaseDirectory, "nlog.config"));

            logFactory.GetLogger("BotFarm.Tests").Info("entry");
        }
        finally
        {
            logFactory.Shutdown();
        }
    }
}
