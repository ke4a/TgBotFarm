namespace BotFarm.Core.Abstractions;

/// <summary>
/// Checks with Telegram that a bot token is accepted.
/// </summary>
public interface ITelegramTokenVerifier
{
    /// <summary>
    /// Asks Telegram which bot <paramref name="token"/> belongs to.
    /// </summary>
    Task<TelegramTokenVerification> VerifyAsync(string token, CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of verifying a bot token with Telegram.
/// </summary>
public abstract record TelegramTokenVerification
{
    private TelegramTokenVerification()
    {
    }

    /// <summary>Telegram accepted the token.</summary>
    public sealed record Verified(long TelegramBotId, string Username) : TelegramTokenVerification;

    /// <summary>Telegram did not accept the token.</summary>
    public sealed record RejectedByTelegram : TelegramTokenVerification;

    /// <summary>The token could not be verified.</summary>
    public sealed record TelegramUnreachable : TelegramTokenVerification;
}
