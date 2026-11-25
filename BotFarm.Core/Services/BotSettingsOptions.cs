using BotFarm.Core.Models;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace BotFarm.Core.Services;

/// <summary>
/// Fills each bot's named <see cref="BotConfig"/> from the settings the <see cref="BotSettingsProvider"/> holds.
/// </summary>
internal sealed class BotSettingsOptionsSetup : IConfigureNamedOptions<BotConfig>
{
    private readonly BotSettingsProvider _provider;

    public BotSettingsOptionsSetup(BotSettingsProvider provider)
    {
        _provider = provider;
    }

    public void Configure(string? name, BotConfig options)
    {
        _provider.Configure(name ?? Options.DefaultName, options);
    }

    public void Configure(BotConfig options)
    {
        Configure(Options.DefaultName, options);
    }
}

/// <summary>
/// Tells <see cref="IOptionsMonitor{TOptions}"/> that one bot's <see cref="BotConfig"/> changed after its settings
/// were loaded or persisted.
/// </summary>
internal sealed class BotSettingsChangeTokenSource : IOptionsChangeTokenSource<BotConfig>
{
    private readonly BotSettingsProvider _provider;

    public BotSettingsChangeTokenSource(string botName, BotSettingsProvider provider)
    {
        Name = botName;
        _provider = provider;
    }

    public string Name { get; }

    public IChangeToken GetChangeToken() => _provider.GetChangeToken(Name);
}
