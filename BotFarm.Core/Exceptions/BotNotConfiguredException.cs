namespace BotFarm.Core.Exceptions;

/// <summary>
/// Thrown when a Telegram operation is requested for a bot without usable settings.
/// </summary>
public sealed class BotNotConfiguredException : InvalidOperationException
{
    public BotNotConfiguredException(string botName)
        : base($"Bot '{botName}' is not configured.")
    {
        BotName = botName;
    }

    public string BotName { get; }
}
