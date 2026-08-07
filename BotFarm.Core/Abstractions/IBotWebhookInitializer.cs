namespace BotFarm.Core.Abstractions;

/// <summary>
/// Performs one-time startup initialization for all registered bots.
/// </summary>
public interface IBotWebhookInitializer
{
    Task InitializeAllAsync(CancellationToken cancellationToken = default);
}
