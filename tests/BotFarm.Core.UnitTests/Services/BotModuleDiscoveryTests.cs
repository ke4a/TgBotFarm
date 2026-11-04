using BotFarm.Core.Abstractions;
using BotFarm.Core.Base;
using BotFarm.Core.Services;
using BotFarm.Core.UnitTests.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: BotModule(typeof(DeclaredTestModule))]

namespace BotFarm.Core.UnitTests.Services;

/// <summary>
/// The bot module this test assembly declares with <see cref="BotModuleAttribute"/>.
/// </summary>
public sealed class DeclaredTestModule : IBotModule
{
    public string Name => "DeclaredTest";

    public void AddServices(IServiceCollection services, IConfiguration configuration) { }

    public void AddHealthChecks(IHealthChecksBuilder healthChecks) { }
}

[TestFixture]
public class BotModuleDiscoveryTests
{
    private static readonly System.Reflection.Assembly TestAssembly = typeof(BotModuleDiscoveryTests).Assembly;

    [Test]
    public void Discover_CreatesTheModuleDeclaredByEachAssemblyOnce()
    {
        var modules = BotModuleDiscovery.Discover(new[] { TestAssembly, TestAssembly });

        Assert.That(modules.Select(module => module.GetType()), Is.EqualTo(new[] { typeof(DeclaredTestModule) }));
    }

    [Test]
    public void Discover_SkipsAssembliesWithoutBotModuleAttribute()
    {
        var modules = BotModuleDiscovery.Discover(new[] { typeof(IBotModule).Assembly, typeof(string).Assembly, TestAssembly });

        Assert.That(modules.Select(module => module.GetType()), Is.EqualTo(new[] { typeof(DeclaredTestModule) }));
    }

    [Test]
    public void Discover_WithAssemblyWithoutModule_ReturnsEmptyList()
    {
        var modules = BotModuleDiscovery.Discover(new[] { typeof(IBotModule).Assembly });

        Assert.That(modules, Is.Empty);
    }

    [Test]
    public void Discover_WithHostAssemblyWithoutApplicationParts_ReturnsEmptyList()
    {
        var modules = BotModuleDiscovery.Discover(TestAssembly);

        Assert.That(modules, Is.Empty);
    }

    [Test]
    public void CreateModule_WithValidType_ReturnsInstance()
    {
        var module = BotModuleDiscovery.CreateModule(typeof(AlphaModule), TestAssembly);

        Assert.That(module, Is.InstanceOf<AlphaModule>());
    }

    [TestCase(typeof(string), "does not implement IBotModule")]
    [TestCase(typeof(IBotModule), "is abstract")]
    [TestCase(typeof(AbstractModule), "is abstract")]
    [TestCase(typeof(GenericModule<>), "open generic")]
    [TestCase(typeof(ModuleWithoutParameterlessConstructor), "has no public parameterless constructor")]
    public void CreateModule_WithInvalidType_ThrowsNamingTypeAndAssembly(Type moduleType, string problem)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            BotModuleDiscovery.CreateModule(moduleType, TestAssembly));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain(moduleType.FullName));
            Assert.That(exception.Message, Does.Contain(TestAssembly.GetName().Name));
            Assert.That(exception.Message, Does.Contain(problem));
        }
    }

    [Test]
    public void OrderByUniqueName_OrdersModulesByName()
    {
        var modules = BotModuleDiscovery.OrderByUniqueName(new IBotModule[] { new BetaModule(), new AlphaModule() });

        Assert.That(modules.Select(module => module.Name), Is.EqualTo(new[] { "Alpha", "Beta" }));
    }

    [Test]
    public void OrderByUniqueName_WithTwoModulesSharingANameIgnoringCase_ThrowsNamingBothModules()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            BotModuleDiscovery.OrderByUniqueName(new IBotModule[] { new AlphaModule(), new OtherAlphaModule() }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("'Alpha'"));
            Assert.That(exception.Message, Does.Contain(typeof(AlphaModule).FullName));
            Assert.That(exception.Message, Does.Contain(typeof(OtherAlphaModule).FullName));
        }
    }

    [Test]
    public void GetComponentsAssemblies_ReturnsEachAssemblyOnce()
    {
        var assemblies = BotModuleDiscovery.GetComponentsAssemblies(new IBotModule[] { new AlphaModule(), new BetaModule() });

        Assert.That(assemblies, Is.EqualTo(new[] { typeof(AlphaModule).Assembly }));
    }

    private abstract class TestModuleBase : IBotModule
    {
        public abstract string Name { get; }

        public void AddServices(IServiceCollection services, IConfiguration configuration) { }

        public void AddHealthChecks(IHealthChecksBuilder healthChecks) { }
    }

    private abstract class AbstractModule : TestModuleBase
    {
        public override string Name => "Abstract";
    }

    private sealed class AlphaModule : TestModuleBase
    {
        public override string Name => "Alpha";
    }

    private sealed class OtherAlphaModule : TestModuleBase
    {
        public override string Name => "alpha";
    }

    private sealed class BetaModule : TestModuleBase
    {
        public override string Name => "Beta";
    }

    private sealed class ModuleWithoutParameterlessConstructor(string name) : TestModuleBase
    {
        public override string Name => name;
    }

    private sealed class GenericModule<T> : TestModuleBase
    {
        public override string Name => typeof(T).Name;
    }
}
