using BotFarm.Core.Abstractions;
using NSubstitute;

namespace BotFarm.UnitTests;

/// <summary>
/// Creates stand-in bot modules for tests that only need a bot's name.
/// </summary>
internal static class TestBotModules
{
    public static IBotModule Create(string name)
    {
        var module = Substitute.For<IBotModule>();
        module.Name.Returns(name);
        return module;
    }
}
