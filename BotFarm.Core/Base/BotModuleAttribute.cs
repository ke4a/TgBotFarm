using BotFarm.Core.Abstractions;

namespace BotFarm.Core.Base;

/// <summary>
/// Declares which class is the bot's module (its <see cref="IBotModule"/> implementation).
/// The host creates one instance of that class at startup.
/// </summary>
/// <example><c>[assembly: BotModule(typeof(TestBotModule))]</c></example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class BotModuleAttribute(Type moduleType) : Attribute
{
    /// <summary>
    /// The class implementing <see cref="IBotModule"/>. It must be concrete and have a public parameterless constructor.
    /// </summary>
    public Type ModuleType { get; } = moduleType;
}
