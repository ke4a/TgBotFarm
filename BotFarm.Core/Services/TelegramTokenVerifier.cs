using BotFarm.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;

namespace BotFarm.Core.Services;

/// <inheritdoc cref="ITelegramTokenVerifier"/>
/// <remarks>
/// Calls <c>getMe</c> through a temporary client from <see cref="ITelegramBotClientFactory"/>. Telegram answering
/// 401 or 404 means it does not accept the token; any other failure means it could not be verified. Failures are
/// logged as warnings with the exception and, when Telegram answered, its error code.
/// </remarks>
internal sealed class TelegramTokenVerifier : ITelegramTokenVerifier
{
    private readonly ITelegramBotClientFactory _clientFactory;
    private readonly ILogger<TelegramTokenVerifier> _logger;

    public TelegramTokenVerifier(ITelegramBotClientFactory clientFactory, ILogger<TelegramTokenVerifier> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    public async Task<TelegramTokenVerification> VerifyAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var me = await _clientFactory.Create(token).GetMe(cancellationToken);
            return new TelegramTokenVerification.Verified(me.Id, me.Username ?? string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiRequestException exception) when (exception.ErrorCode is 401 or 404)
        {
            LogFailure(exception);
            return new TelegramTokenVerification.RejectedByTelegram();
        }
        catch (Exception exception)
        {
            LogFailure(exception);
            return new TelegramTokenVerification.TelegramUnreachable();
        }
    }

    private void LogFailure(Exception exception)
    {
        _logger.LogWarning(
            exception,
            "A bot token could not be verified with Telegram (error code {ErrorCode}).",
            (exception as ApiRequestException)?.ErrorCode);
    }
}
