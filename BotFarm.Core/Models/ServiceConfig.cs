namespace BotFarm.Core.Models;

public class ServiceConfig
{
    public IEnumerable<string> Interfaces { get; set; }

    public string Implementation { get; set; }

    public string Lifetime { get; set; }

    public string? Key { get; set; }
}
