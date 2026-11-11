using BotFarm.Core.Abstractions;
using BotFarm.Core.Helpers;
using BotFarm.Core.Models;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;

namespace BotFarm.Core.Services;

/// <summary>
/// Sends one message to many chats through a registered Telegram bot, one chat at a time with a pause between
/// sends, and waits as long as Telegram asks when it answers with "too many requests". Runs at most one broadcast
/// per bot at a time; it is registered as a singleton so that this holds across dashboard sessions.
/// </summary>
internal sealed class TelegramBroadcastService : IBroadcastService
{
    /// <summary>
    /// Pause between two sends. Telegram allows about 30 messages per second to different chats; 20 per second
    /// leaves headroom for the bot's regular traffic.
    /// </summary>
    internal static readonly TimeSpan DefaultSendInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// How many times one chat is tried when Telegram keeps answering with "too many requests".
    /// </summary>
    internal const int MaxAttemptsPerChat = 3;

    // Used when a "too many requests" answer does not say how long to wait.
    private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(1);

    private readonly IBotRegistry _botRegistry;
    private readonly ILogger<TelegramBroadcastService> _logger;
    private readonly TimeSpan _sendInterval;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ConcurrentDictionary<string, byte> _runningBots = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates the broadcast service that resolves bots by name and paces sends with <see cref="DefaultSendInterval"/>.
    /// </summary>
    public TelegramBroadcastService(IBotRegistry botRegistry, ILogger<TelegramBroadcastService> logger)
        : this(botRegistry, logger, DefaultSendInterval, Task.Delay)
    {
    }

    /// <summary>
    /// Creates the broadcast service with its own pause between sends and its own way of waiting.
    /// </summary>
    internal TelegramBroadcastService(
        IBotRegistry botRegistry,
        ILogger<TelegramBroadcastService> logger,
        TimeSpan sendInterval,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _botRegistry = botRegistry;
        _logger = logger;
        _sendInterval = sendInterval;
        _delay = delay;
    }

    /// <inheritdoc />
    public async Task<BroadcastResult> Broadcast(
        string botName,
        IReadOnlyCollection<long> chatIds,
        string message,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!_runningBots.TryAdd(botName, 0))
        {
            throw new InvalidOperationException($"A message to all chats of bot '{botName}' is still being sent.");
        }

        try
        {
            return await SendToAll(botName, chatIds, message, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _runningBots.TryRemove(botName, out _);
        }
    }

    private async Task<BroadcastResult> SendToAll(
        string botName,
        IReadOnlyCollection<long> chatIds,
        string message,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var client = _botRegistry.GetService<IBotService>(botName).Client;
        var sent = 0;
        var unreachable = new List<long>();
        var failed = new List<long>();
        var handled = 0;

        foreach (var chatId in chatIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (handled > 0)
            {
                await _delay(_sendInterval, cancellationToken).ConfigureAwait(false);
            }

            switch (await SendToChat(client, botName, chatId, message, cancellationToken).ConfigureAwait(false))
            {
                case Outcome.Sent:
                    sent++;
                    break;
                case Outcome.Unreachable:
                    unreachable.Add(chatId);
                    break;
                default:
                    failed.Add(chatId);
                    break;
            }

            handled++;
            progress?.Report(handled);
        }

        _logger.LogInformation(
            "Broadcast from bot '{BotName}' finished: {Sent} sent, {Unreachable} unreachable, {Failed} failed",
            botName, sent, unreachable.Count, failed.Count);

        return new BroadcastResult(sent, unreachable, failed);
    }

    private async Task<Outcome> SendToChat(
        ITelegramBotClient client, string botName, long chatId, string message, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await client.SendMessage(chatId, message, parseMode: ParseMode.Html, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return Outcome.Sent;
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 429 && attempt < MaxAttemptsPerChat)
            {
                var retryAfter = ex.Parameters?.RetryAfter is { } seconds
                    ? TimeSpan.FromSeconds(seconds)
                    : DefaultRetryAfter;
                _logger.LogWarning(
                    "Telegram rate limit hit while broadcasting from bot '{BotName}'; waiting {RetryAfter} before retrying chat '{ChatId}'",
                    botName, retryAfter, chatId);
                await _delay(retryAfter, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ChatReachability.IsUnreachable(ex))
            {
                _logger.LogInformation(
                    "Chat '{ChatId}' is unreachable for bot '{BotName}': {Reason}", chatId, botName, ex.Message);
                return Outcome.Unreachable;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Failed to broadcast to chat '{ChatId}' from bot '{BotName}'", chatId, botName);
                return Outcome.Failed;
            }
        }
    }

    private enum Outcome
    {
        Sent,
        Unreachable,
        Failed,
    }
}
