using System.Text;
using System.Text.RegularExpressions;

namespace BotFarm.Logging;

/// <summary>
/// Severity of a log entry, as written by the NLog file layouts.
/// </summary>
public enum LogEntryLevel
{
    /// <summary>The line has no recognizable level field.</summary>
    Unknown,
    Trace,
    Debug,
    Info,
    Warn,
    Error,
    Fatal
}

/// <summary>
/// Which entries the log viewer shows.
/// </summary>
public enum LogLevelFilter
{
    /// <summary>Every entry, including lines without a level.</summary>
    All,
    /// <summary>Warnings and anything more severe.</summary>
    Warnings,
    /// <summary>Errors and fatal entries.</summary>
    Errors
}

/// <summary>
/// One log entry: a timestamped line plus any continuation lines that follow it, such as an exception stack trace.
/// </summary>
/// <param name="Id">Position of the entry in the file, used as a stable render key.</param>
/// <param name="Level">Severity read from the entry's first line.</param>
/// <param name="Text">Full text of the entry, lines joined with <c>\n</c>.</param>
public sealed record LogEntry(int Id, LogEntryLevel Level, string Text)
{
    /// <summary>
    /// Whether the entry passes <paramref name="filter"/>.
    /// </summary>
    public bool Matches(LogLevelFilter filter) => filter switch
    {
        LogLevelFilter.Warnings => Level >= LogEntryLevel.Warn,
        LogLevelFilter.Errors => Level >= LogEntryLevel.Error,
        _ => true
    };
}

/// <summary>
/// Reads a log file into <see cref="LogEntry"/> items and picks up lines appended to it later.
/// </summary>
/// <remarks>
/// Only complete lines are consumed while tailing, so an entry that is half written is read on a later call.
/// When the file becomes shorter than what was already read (the logger rolled it over), reading starts again
/// from the beginning and <see cref="ReadResult.Reset"/> is reported.
/// </remarks>
public sealed partial class LogFileTail
{
    private readonly string _filePath;
    private readonly List<LogEntry> _entries = [];
    private long _offset;

    /// <summary>
    /// Creates a reader for <paramref name="filePath"/>. Nothing is read until <see cref="ReadAsync"/> is called.
    /// </summary>
    public LogFileTail(string filePath)
    {
        _filePath = filePath;
    }

    /// <summary>
    /// Entries read so far, oldest first.
    /// </summary>
    public IReadOnlyList<LogEntry> Entries => _entries;

    /// <summary>
    /// Outcome of a <see cref="ReadAsync"/> call.
    /// </summary>
    /// <param name="Changed">True when entries were added or the last entry got more lines.</param>
    /// <param name="Reset">True when the file was rolled over and all earlier entries were dropped.</param>
    public readonly record struct ReadResult(bool Changed, bool Reset);

    /// <summary>
    /// Whether <paramref name="fileName"/> is a log file the logger is still writing to, as opposed to an archive.
    /// </summary>
    public static bool IsLiveLogFile(string fileName) =>
        string.Equals(Path.GetExtension(fileName), ".log", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads everything written since the previous call.
    /// </summary>
    /// <param name="includePartialLine">
    /// Also consume a last line that has no line break yet. Use it for files nobody writes to anymore;
    /// while tailing, leave it off so the line is read once it is complete.
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<ReadResult> ReadAsync(bool includePartialLine = false, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            _filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);

        var reset = false;
        if (stream.Length < _offset)
        {
            _offset = 0;
            _entries.Clear();
            reset = true;
        }

        var available = stream.Length - _offset;
        if (available <= 0)
        {
            return new ReadResult(reset, reset);
        }

        var buffer = new byte[available];
        stream.Seek(_offset, SeekOrigin.Begin);
        await stream.ReadExactlyAsync(buffer, cancellationToken);

        // '\n' never occurs inside a multi-byte UTF-8 sequence, so cutting after it never splits a character.
        var consumed = includePartialLine ? buffer.Length : Array.LastIndexOf(buffer, (byte)'\n') + 1;
        if (consumed == 0)
        {
            return new ReadResult(reset, reset);
        }

        var skipBom = _offset == 0 && consumed >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF ? 3 : 0;
        var text = Encoding.UTF8.GetString(buffer, skipBom, consumed - skipBom);
        _offset += consumed;

        var changed = Append(text);
        return new ReadResult(changed || reset, reset);
    }

    private bool Append(string text)
    {
        var lines = text.Split('\n');
        // A trailing line break leaves an empty last element that is not a line of its own.
        var count = text.EndsWith('\n') ? lines.Length - 1 : lines.Length;
        var changed = false;

        for (var i = 0; i < count; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (EntryStart().IsMatch(line) || _entries.Count == 0)
            {
                _entries.Add(new LogEntry(_entries.Count, ParseLevel(line), line));
            }
            else
            {
                var last = _entries[^1];
                _entries[^1] = last with { Text = last.Text + "\n" + line };
            }

            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Reads the level from a <c>date|...|LEVEL|...</c> line. The file layouts put it in the third field
    /// (after the event id), the console layout in the second, so both are checked.
    /// </summary>
    internal static LogEntryLevel ParseLevel(string line)
    {
        if (!EntryStart().IsMatch(line))
        {
            return LogEntryLevel.Unknown;
        }

        var fields = line.Split('|', 4);
        for (var i = 1; i < Math.Min(fields.Length, 3); i++)
        {
            if (Enum.TryParse<LogEntryLevel>(fields[i], ignoreCase: true, out var level)
                && level != LogEntryLevel.Unknown
                && !int.TryParse(fields[i], out _))
            {
                return level;
            }
        }

        return LogEntryLevel.Unknown;
    }

    /// <summary>
    /// Matches the start of a new entry: NLog's <c>${longdate}</c> followed by the field separator.
    /// </summary>
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+\|")]
    private static partial Regex EntryStart();
}
