namespace BotFarm.Core.Abstractions;

public sealed class BotControlBusyException : InvalidOperationException
{
    public BotControlBusyException(string botName)
        : base($"Bot '{botName}' is already changing state or is not available.")
    {
        BotName = botName;
    }

    public string BotName { get; }
}
