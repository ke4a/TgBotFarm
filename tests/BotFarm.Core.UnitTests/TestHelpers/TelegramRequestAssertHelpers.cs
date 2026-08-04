using NSubstitute;
using Telegram.Bot;

namespace BotFarm.Core.UnitTests.TestHelpers;

/// <summary>
/// Shared helpers for asserting on the outgoing Telegram Bot API requests recorded by a
/// substituted <see cref="TelegramBotClient"/>, since the SDK itself does not expose sent
/// requests as first-class objects.
/// </summary>
internal static class TelegramRequestAssertHelpers
{
    public static object GetSingleRequest(TelegramBotClient client, string requestTypeName)
    {
        var requests = client.ReceivedCalls()
            .SelectMany(call => call.GetArguments())
            .Where(arg => arg?.GetType().Name == requestTypeName)
            .ToList();

        Assert.That(requests, Has.Count.EqualTo(1));
        return requests[0]!;
    }

    public static object? GetPropertyValue(object target, string propertyName)
    {
        return target.GetType().GetProperty(propertyName)?.GetValue(target);
    }
}
