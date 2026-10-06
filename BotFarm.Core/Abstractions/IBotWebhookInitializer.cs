namespace BotFarm.Core.Abstractions;

/// <summary>
/// Applies enable and disable transitions to a registered bot.
/// </summary>
public interface IBotWebhookInitializer
{
    Task EnableAsync(IBotService botService, CancellationToken cancellationToken = default);

    Task<bool> DisableAsync(IBotService botService, CancellationToken cancellationToken = default);
}