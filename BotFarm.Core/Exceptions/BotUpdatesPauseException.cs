namespace BotFarm.Core.Exceptions;

/// <summary>
/// Thrown when a bot's webhook could not be paused before a maintenance operation.
/// </summary>
public sealed class BotUpdatesPauseException : InvalidOperationException
{
    public BotUpdatesPauseException(string botName, Exception? innerException = null)
        : base($"Updates for bot '{botName}' could not be paused.", innerException)
    {
        BotName = botName;
    }

    public string BotName { get; }
}
