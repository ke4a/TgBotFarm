using BotFarm.Logging;
using BotFarm.TestKit;

namespace BotFarm.UnitTests.Logging;

/// <summary>
/// Reads log files written in the NLog file layout and checks how entries are split, leveled and tailed.
/// </summary>
[TestFixture]
public class LogFileTailTests
{
    private const string InfoLine = "2026-11-11 10:00:00.1234|0|INFO|BotFarm.Program|Started";
    private const string WarnLine = "2026-11-11 10:00:01.1234|0|WARN|BotFarm.Program|Slow response";
    private const string ErrorLine = "2026-11-11 10:00:02.1234|17|ERROR|BotFarm.Program|Failed System.Exception: boom";

    private TempDirectoryScope _scope = default!;
    private string _path = default!;

    [SetUp]
    public void SetUp()
    {
        _scope = new TempDirectoryScope();
        _path = _scope.GetPath("nlog-all.log");
    }

    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
    }

    [Test]
    public async Task ReadAsync_SplitsEntriesAndReadsLevels()
    {
        await File.WriteAllTextAsync(_path, $"{InfoLine}\n{WarnLine}\n{ErrorLine}\n");
        var tail = new LogFileTail(_path);

        await tail.ReadAsync();

        Assert.That(tail.Entries.Select(e => e.Level),
            Is.EqualTo(new[] { LogEntryLevel.Info, LogEntryLevel.Warn, LogEntryLevel.Error }));
        Assert.That(tail.Entries[2].Text, Is.EqualTo(ErrorLine));
    }

    [Test]
    public async Task ReadAsync_AttachesStackTraceLinesToTheirEntry()
    {
        await File.WriteAllTextAsync(_path, $"{ErrorLine}\r\n   at Foo.Bar()\r\n   at Foo.Baz()\r\n{InfoLine}\r\n");
        var tail = new LogFileTail(_path);

        await tail.ReadAsync();

        Assert.That(tail.Entries, Has.Count.EqualTo(2));
        Assert.That(tail.Entries[0].Text, Is.EqualTo($"{ErrorLine}\n   at Foo.Bar()\n   at Foo.Baz()"));
        Assert.That(tail.Entries[0].Level, Is.EqualTo(LogEntryLevel.Error));
    }

    [Test]
    public async Task ReadAsync_PicksUpAppendedLinesIncludingLateStackTrace()
    {
        await File.WriteAllTextAsync(_path, $"{InfoLine}\n{ErrorLine}\n");
        var tail = new LogFileTail(_path);
        await tail.ReadAsync();

        await File.AppendAllTextAsync(_path, $"   at Foo.Bar()\n{WarnLine}\n");
        var result = await tail.ReadAsync();

        Assert.That(result, Is.EqualTo(new LogFileTail.ReadResult(Changed: true, Reset: false)));
        Assert.That(tail.Entries, Has.Count.EqualTo(3));
        Assert.That(tail.Entries[1].Text, Is.EqualTo($"{ErrorLine}\n   at Foo.Bar()"));
        Assert.That(tail.Entries[2].Level, Is.EqualTo(LogEntryLevel.Warn));
    }

    [Test]
    public async Task ReadAsync_WithoutNewData_ReportsNoChange()
    {
        await File.WriteAllTextAsync(_path, $"{InfoLine}\n");
        var tail = new LogFileTail(_path);
        await tail.ReadAsync();

        var result = await tail.ReadAsync();

        Assert.That(result.Changed, Is.False);
        Assert.That(tail.Entries, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ReadAsync_HoldsBackUnfinishedLineUntilItIsComplete()
    {
        await File.WriteAllTextAsync(_path, $"{InfoLine}\n2026-11-11 10:00:01.1234|0|WA");
        var tail = new LogFileTail(_path);

        await tail.ReadAsync();
        Assert.That(tail.Entries, Has.Count.EqualTo(1));

        await File.AppendAllTextAsync(_path, "RN|BotFarm.Program|Slow response\n");
        await tail.ReadAsync();

        Assert.That(tail.Entries, Has.Count.EqualTo(2));
        Assert.That(tail.Entries[1].Text, Is.EqualTo(WarnLine));
        Assert.That(tail.Entries[1].Level, Is.EqualTo(LogEntryLevel.Warn));
    }

    [Test]
    public async Task ReadAsync_IncludePartialLine_ReadsLastLineWithoutLineBreak()
    {
        await File.WriteAllTextAsync(_path, $"{InfoLine}\n{WarnLine}");
        var tail = new LogFileTail(_path);

        await tail.ReadAsync(includePartialLine: true);

        Assert.That(tail.Entries, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ReadAsync_WhenFileIsRolledOver_StartsAgainFromTheTop()
    {
        await File.WriteAllTextAsync(_path, $"{InfoLine}\n{WarnLine}\n{ErrorLine}\n");
        var tail = new LogFileTail(_path);
        await tail.ReadAsync();

        await File.WriteAllTextAsync(_path, $"{WarnLine}\n");
        var result = await tail.ReadAsync();

        Assert.That(result, Is.EqualTo(new LogFileTail.ReadResult(Changed: true, Reset: true)));
        Assert.That(tail.Entries.Select(e => e.Text), Is.EqualTo(new[] { WarnLine }));
        Assert.That(tail.Entries[0].Id, Is.Zero);
    }

    [Test]
    public async Task ReadAsync_SkipsUtf8ByteOrderMarkAndKeepsMultiByteText()
    {
        var line = "2026-11-11 10:00:00.1234|0|INFO|BotFarm.Program|Привет";
        await File.WriteAllTextAsync(_path, line + "\n", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var tail = new LogFileTail(_path);

        await tail.ReadAsync();

        Assert.That(tail.Entries.Single().Text, Is.EqualTo(line));
        Assert.That(tail.Entries.Single().Level, Is.EqualTo(LogEntryLevel.Info));
    }

    [TestCase("2026-11-11 10:00:00.1234|0|FATAL|Logger|msg", LogEntryLevel.Fatal)]
    [TestCase("2026-11-11 10:00:00.1234|DEBUG|Logger|msg", LogEntryLevel.Debug)]
    [TestCase("2026-11-11 10:00:00.1234||TRACE|Logger|msg", LogEntryLevel.Trace)]
    [TestCase("2026-11-11 10:00:00.1234|4|Logger|msg", LogEntryLevel.Unknown)]
    [TestCase("   at Foo.Bar()", LogEntryLevel.Unknown)]
    public void ParseLevel_ReadsLevelFromFileAndConsoleLayouts(string line, LogEntryLevel expected)
    {
        Assert.That(LogFileTail.ParseLevel(line), Is.EqualTo(expected));
    }

    [TestCase(LogLevelFilter.All, new[] { LogEntryLevel.Unknown, LogEntryLevel.Debug, LogEntryLevel.Info, LogEntryLevel.Warn, LogEntryLevel.Error, LogEntryLevel.Fatal })]
    [TestCase(LogLevelFilter.Warnings, new[] { LogEntryLevel.Warn, LogEntryLevel.Error, LogEntryLevel.Fatal })]
    [TestCase(LogLevelFilter.Errors, new[] { LogEntryLevel.Error, LogEntryLevel.Fatal })]
    public void Matches_KeepsLevelsAtOrAboveTheFilter(LogLevelFilter filter, LogEntryLevel[] expected)
    {
        var levels = new[] { LogEntryLevel.Unknown, LogEntryLevel.Debug, LogEntryLevel.Info, LogEntryLevel.Warn, LogEntryLevel.Error, LogEntryLevel.Fatal };

        var kept = levels.Where(level => new LogEntry(0, level, string.Empty).Matches(filter));

        Assert.That(kept, Is.EqualTo(expected));
    }

    [TestCase("nlog-all.log", true)]
    [TestCase("NLOG-OWN.LOG", true)]
    [TestCase("log.3.txt", false)]
    public void IsLiveLogFile_OnlyMatchesCurrentLogFiles(string name, bool expected)
    {
        Assert.That(LogFileTail.IsLiveLogFile(name), Is.EqualTo(expected));
    }
}
