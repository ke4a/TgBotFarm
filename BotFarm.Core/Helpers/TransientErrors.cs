using MongoDB.Driver;
using System.Net.Sockets;
using Telegram.Bot.Exceptions;

namespace BotFarm.Core.Helpers;

/// <summary>
/// Tells temporary failures, such as an unreachable database or network, apart from errors
/// that would happen again on every attempt.
/// </summary>
public static class TransientErrors
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="exception"/> or any exception it wraps
    /// is a temporary connection, timeout or server-side failure that may succeed when tried later.
    /// </summary>
    /// <remarks>
    /// Temporary failures are MongoDB connection and execution timeout errors, timeouts, HTTP request,
    /// socket and I/O errors, and Telegram API errors with code 429 (too many requests) or 500 and above.
    /// A cancellation counts only when it was caused by a timeout.
    /// </remarks>
    public static bool IsTransient(Exception exception)
    {
        var pending = new Stack<Exception>();
        pending.Push(exception);

        while (pending.TryPop(out var current))
        {
            if (IsTransientType(current))
            {
                return true;
            }

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    pending.Push(inner);
                }
            }
            else if (current.InnerException != null)
            {
                pending.Push(current.InnerException);
            }
        }

        return false;
    }

    // A timed-out HTTP call surfaces as a cancellation wrapping a TimeoutException, which the
    // chain walk finds, so cancellations themselves are not listed here.
    private static bool IsTransientType(Exception exception) =>
        exception is MongoConnectionException
            or MongoExecutionTimeoutException
            or TimeoutException
            or HttpRequestException
            or SocketException
            or IOException
        || exception is ApiRequestException { ErrorCode: 429 or >= 500 };
}
