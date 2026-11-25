using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BotFarm.TestKit;

/// <summary>
/// One entry written through a substitute <see cref="ILogger"/>.
/// </summary>
/// <param name="Level">The entry's level.</param>
/// <param name="Exception">The exception passed to the logger, if any.</param>
/// <param name="State">The entry's structured values, including the message template.</param>
/// <param name="Message">The formatted message.</param>
public sealed record LoggedEntry(
    LogLevel Level,
    Exception? Exception,
    IReadOnlyList<KeyValuePair<string, object?>> State,
    string Message)
{
    /// <summary>
    /// The structured value named <paramref name="key"/>, or <see langword="null"/> when there is none.
    /// </summary>
    public object? this[string key] => State.FirstOrDefault(pair => pair.Key == key).Value;

    /// <summary>
    /// Whether the formatted message, a structured value or the attached exception contains <paramref name="text"/>.
    /// </summary>
    public bool Contains(string text) =>
        Message.Contains(text, StringComparison.Ordinal)
        || State.Any(pair => pair.Value?.ToString()?.Contains(text, StringComparison.Ordinal) == true)
        || Exception?.ToString().Contains(text, StringComparison.Ordinal) == true;
}

/// <summary>
/// Reads the entries written through a substitute <see cref="ILogger"/>.
/// </summary>
public static class LoggerSubstituteExtensions
{
    /// <summary>
    /// The entries written through <paramref name="logger"/> so far, in order.
    /// </summary>
    public static IReadOnlyList<LoggedEntry> LoggedEntries(this ILogger logger) =>
        logger.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(call =>
            {
                var arguments = call.GetArguments();
                var exception = (Exception?)arguments[3];
                var state = arguments[2] as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
                var message = (arguments[4] as Delegate)?.DynamicInvoke(arguments[2], exception) as string;
                return new LoggedEntry((LogLevel)arguments[0]!, exception, state, message ?? string.Empty);
            })
            .ToList();
}
