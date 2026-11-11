using System.Globalization;
using BotFarm.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BotFarm.TestKit;

/// <summary>
/// Registers the real admin UI localization in test service collections.
/// </summary>
public static class AdminLocalizationTestExtensions
{
    private static readonly Lazy<IServiceProvider> ShippedLocalization =
        new(() => new ServiceCollection().AddAdminLocalizationForTests().BuildServiceProvider());

    /// <summary>
    /// Returns a localizer over the shipped translation set of <typeparamref name="T"/>'s assembly, for tests that
    /// construct components or call helpers without a service provider. It reads the current UI culture on every
    /// lookup.
    /// </summary>
    public static IStringLocalizer<T> CreateAdminLocalizer<T>() =>
        ShippedLocalization.Value.GetRequiredService<IStringLocalizer<T>>();

    /// <summary>
    /// Adds admin localization over the translation sets in <paramref name="root"/>, or in <c>AdminLanguages</c>
    /// under the test output folder when null. Adds a <see cref="NullLoggerFactory"/> unless logging is already
    /// registered.
    /// </summary>
    public static IServiceCollection AddAdminLocalizationForTests(this IServiceCollection services, string? root = null)
    {
        services.TryAddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.TryAdd(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(Logger<>)));
        return services.AddAdminLocalization(root);
    }
}

/// <summary>
/// Sets <see cref="CultureInfo.CurrentCulture"/> and <see cref="CultureInfo.CurrentUICulture"/> to one culture
/// and restores both when disposed: <c>using var _ = new CultureScope("ru-RU");</c>.
/// </summary>
public sealed class CultureScope : IDisposable
{
    private readonly CultureInfo originalCulture;
    private readonly CultureInfo originalUICulture;

    /// <summary>
    /// Switches the current culture and UI culture to <paramref name="cultureName"/>.
    /// </summary>
    public CultureScope(string cultureName)
    {
        originalCulture = CultureInfo.CurrentCulture;
        originalUICulture = CultureInfo.CurrentUICulture;
        var culture = CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>
    /// Restores the cultures that were current when the scope was created.
    /// </summary>
    public void Dispose()
    {
        CultureInfo.CurrentCulture = originalCulture;
        CultureInfo.CurrentUICulture = originalUICulture;
    }
}
