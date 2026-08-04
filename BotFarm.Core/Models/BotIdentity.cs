namespace BotFarm.Core.Models;

/// <summary>
/// Identifies a single bot instance by name. Passed as a constructor parameter to
/// relevant services so <c>Name</c> is a known value at construction time.
/// </summary>
public sealed record BotIdentity(string Name)
{
    /// <summary>Log line prefix derived from the bot name, e.g. <c>"[TestBot]"</c>.</summary>
    public string LogPrefix => $"[{Name}]";
}
