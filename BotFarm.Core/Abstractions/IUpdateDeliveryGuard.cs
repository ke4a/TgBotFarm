namespace BotFarm.Core.Abstractions;

/// <summary>
/// Tracks recently accepted webhook updates, temporary processing failures and recent error alerts for each bot.
/// </summary>
public interface IUpdateDeliveryGuard
{
    /// <summary>
    /// Records <paramref name="updateId"/> as accepted for <paramref name="botName"/>.
    /// Returns <see langword="false"/> when the same update was already accepted for that bot recently.
    /// </summary>
    bool TryAcceptUpdate(string botName, int updateId);

    /// <summary>
    /// Returns <see langword="true"/> and records the time when an error alert may be sent for
    /// <paramref name="botName"/>, or <see langword="false"/> when one was already sent within the last minute.
    /// </summary>
    bool TryAcquireAlertSlot(string botName);

    /// <summary>
    /// Records a temporary processing failure of <paramref name="updateId"/> for <paramref name="botName"/>
    /// and sets <paramref name="attempts"/> to the number of failed attempts so far, including this one.
    /// While that number is below <paramref name="maxAttempts"/>, the update is released so its next delivery
    /// is processed again, and the method returns <see langword="true"/>. Otherwise the update stays accepted,
    /// its failure count is cleared, and the method returns <see langword="false"/>.
    /// </summary>
    bool TryReleaseForRetry(string botName, int updateId, int maxAttempts, out int attempts);

    /// <summary>
    /// Forgets any temporary failures recorded for <paramref name="updateId"/> of <paramref name="botName"/>.
    /// </summary>
    void ClearFailures(string botName, int updateId);
}
