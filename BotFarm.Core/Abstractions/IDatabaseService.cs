namespace BotFarm.Core.Abstractions;

public interface IDatabaseService : INamedService
{
    Task<bool> Disconnect();

    Task<bool> Reconnect();
}
