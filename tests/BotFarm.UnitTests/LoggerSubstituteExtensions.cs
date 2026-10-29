using Microsoft.Extensions.Logging;
using NSubstitute;

namespace BotFarm.UnitTests;

/// <summary>
/// Reads back what was logged through an NSubstitute <see cref="ILogger"/>, with templates already formatted.
/// </summary>
internal static class LoggerSubstituteExtensions
{
    public static IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> LoggedEntries(this ILogger logger)
    {
        return logger.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(call =>
            {
                var args = call.GetArguments();
                return ((LogLevel)args[0]!, args[2]?.ToString() ?? string.Empty, args[3] as Exception);
            })
            .ToList();
    }
}
