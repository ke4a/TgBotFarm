using System.Reflection;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Base;
using Microsoft.AspNetCore.Mvc.ApplicationParts;

namespace BotFarm.Core.Services;

/// <summary>
/// Finds the bot modules (<see cref="IBotModule"/> implementations) declared with <see cref="BotModuleAttribute"/>.
/// </summary>
public static class BotModuleDiscovery
{
    /// <summary>
    /// Creates the bot modules of the projects the host references, ordered by name.
    /// </summary>
    /// <remarks>
    /// The build lists every referenced project with MVC controllers in <see cref="ApplicationPartAttribute"/>s
    /// on the host assembly, so each bot project (which has an update controller) is in that list.
    /// </remarks>
    /// <param name="hostAssembly">The host application's assembly.</param>
    /// <exception cref="InvalidOperationException">A declared module type is invalid, or two modules share a name.</exception>
    public static IReadOnlyList<IBotModule> Discover(Assembly hostAssembly)
    {
        var assemblies = hostAssembly.GetCustomAttributes<ApplicationPartAttribute>()
            .Select(part => Assembly.Load(new AssemblyName(part.AssemblyName)));

        return Discover(assemblies);
    }

    /// <summary>
    /// Creates the bot module declared by each of <paramref name="assemblies"/>, ordered by name.
    /// Assemblies without a <see cref="BotModuleAttribute"/> are skipped.
    /// </summary>
    /// <exception cref="InvalidOperationException">A declared module type is invalid, or two modules share a name.</exception>
    public static IReadOnlyList<IBotModule> Discover(IEnumerable<Assembly> assemblies)
    {
        var modules = assemblies
            .Distinct()
            .Select(assembly => (assembly, attribute: assembly.GetCustomAttribute<BotModuleAttribute>()))
            .Where(pair => pair.attribute != null)
            .Select(pair => CreateModule(pair.attribute!.ModuleType, pair.assembly));

        return OrderByUniqueName(modules);
    }

    /// <summary>
    /// Returns the distinct assemblies holding the pages and components of <paramref name="modules"/>.
    /// </summary>
    public static Assembly[] GetComponentsAssemblies(IEnumerable<IBotModule> modules)
    {
        return modules.Select(module => module.ComponentsAssembly).Distinct().ToArray();
    }

    /// <summary>
    /// Creates an instance of <paramref name="moduleType"/>, the module declared by <paramref name="declaringAssembly"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The type does not implement <see cref="IBotModule"/>, cannot be instantiated or has no public parameterless constructor.
    /// </exception>
    internal static IBotModule CreateModule(Type moduleType, Assembly declaringAssembly)
    {
        string? problem = null;
        if (!typeof(IBotModule).IsAssignableFrom(moduleType))
        {
            problem = $"does not implement {nameof(IBotModule)}";
        }
        else if (moduleType.IsAbstract || moduleType.ContainsGenericParameters)
        {
            problem = "is abstract or an open generic type";
        }
        else if (moduleType.GetConstructor(Type.EmptyTypes) == null)
        {
            problem = "has no public parameterless constructor";
        }

        if (problem != null)
        {
            throw new InvalidOperationException(
                $"The bot module '{moduleType.FullName}' declared by assembly '{declaringAssembly.GetName().Name}' {problem}.");
        }

        return (IBotModule)Activator.CreateInstance(moduleType)!;
    }

    /// <summary>
    /// Orders <paramref name="modules"/> by name and checks that the names are unique, ignoring case.
    /// </summary>
    /// <exception cref="InvalidOperationException">Two modules share a name.</exception>
    internal static IReadOnlyList<IBotModule> OrderByUniqueName(IEnumerable<IBotModule> modules)
    {
        var ordered = modules.OrderBy(module => module.Name, StringComparer.Ordinal).ToList();

        var duplicate = ordered
            .GroupBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate != null)
        {
            var typeNames = string.Join(", ", duplicate.Select(module => module.GetType().FullName));
            throw new InvalidOperationException(
                $"More than one bot module is named '{duplicate.Key}' ({typeNames}). Each bot needs a unique name.");
        }

        return ordered;
    }
}
