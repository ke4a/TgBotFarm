namespace BotFarm.Core.Abstractions;

/// <summary>
/// Exposes the bot name associated with a service instance.
/// </summary>
public interface INamedService
{
    /// <summary>
    /// Bot name used to associate this service with keyed registrations and configuration.
    /// </summary>
    string Name { get; }
}
